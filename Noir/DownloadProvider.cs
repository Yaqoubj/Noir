using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public sealed class DownloadCredentials
{
    public string Manifest {get;set;}="";
    public string Key {get;set;}="";
    public string ShowsRoot {get;set;}="";
    public string MoviesRoot {get;set;}="";
    public string PreferredResolution {get;set;}="1080p";
    public long MaxEpisodeBytes {get;set;}
    public List<string> SubtitleLanguages {get;set;}=["en","ar"];
    public bool AutoSyncSubtitles {get;set;}=true;
    public string BrowseQuality {get;set;}="All";
    public string BrowseLanguage {get;set;}="All";
    public bool BrowseReadyOnly {get;set;}
    public bool BrowseSizeLimit {get;set;}
    public string OriginalManifest {get;set;}="";
}
public sealed record IndexedRelease(DownloadChoice Choice,string Hash);
public sealed record TorboxFile(long Id,string Name,long Size);
public sealed record TorboxTorrent(long Id,string Hash,bool Ready,List<TorboxFile> Files);
public interface IDownloadProvider
{
    Task<List<IndexedRelease>> Search(VideoItem item,CancellationToken ct);
    Task<List<IndexedRelease>> SearchSeason(VideoItem item,CancellationToken ct)=>Search(item,ct);
    async Task<ProviderSearchResult> Discover(VideoItem item,bool season,List<VideoItem> probes,IProgress<DownloadSourceStatus>? progress,CancellationToken ct)
    {
        var found=season?await SearchSeason(item,ct):await Search(item,ct);
        return new(){Releases=found,Sources=[new(){Source="Provider",State=found.Count>0?"Ready":"Empty",Count=found.Count}]};
    }
    Task<List<TorboxFile>> Files(string hash,CancellationToken ct);
    Task<TorboxTorrent> Prepare(string hash,long torrentId,CancellationToken ct);
    Task<string> Link(long torrentId,long fileId,CancellationToken ct);
}
public sealed class TorboxProvider(Func<DownloadCredentials> config,HttpClient? client=null) : IDownloadProvider
{
    readonly HttpClient http=client??new HttpClient {Timeout=TimeSpan.FromSeconds(45)};
    readonly System.Collections.Concurrent.ConcurrentDictionary<string,TorboxTorrent> owned=new(StringComparer.OrdinalIgnoreCase);
    public static Uri Manifest(string text)
    {
        text=text.Trim();if(text.StartsWith("stremio://",StringComparison.OrdinalIgnoreCase))text="https://"+text[10..];
        if(!Uri.TryCreate(text,UriKind.Absolute,out var u)||u.Scheme!="https"||u.UserInfo.Length>0||!u.AbsolutePath.EndsWith("/manifest.json"))throw new IOException("Paste the full HTTPS AIOStreams installation URL ending in /manifest.json.");
        return u;
    }
    public static string Hash(JsonElement stream)
    {
        var candidates=new List<string>{Text(stream,"infoHash"),Text(stream,"magnet"),Text(stream,"url")};
        var marker=Regex.Match(Text(stream,"description")+" "+Text(stream,"title"),@"\[NOIR_HASH:([a-fA-F0-9]{40})\]");
        if(marker.Success)candidates.Add(marker.Groups[1].Value);
        if(stream.ValueKind!=JsonValueKind.Object)return "";
        if(stream.TryGetProperty("streamData",out var data)&&data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("torrent",out var torrent))candidates.Add(Text(torrent,"infoHash"));
        if(stream.TryGetProperty("torrent",out var nested))candidates.Add(Text(nested,"infoHash"));
        if(stream.TryGetProperty("behaviorHints",out var hints))candidates.Add(Text(hints,"bingeGroup"));
        foreach(var value in candidates)
        {
            if(Regex.IsMatch(value,@"\A[a-fA-F0-9]{40}\z"))return value.ToLowerInvariant();
            var m=Regex.Match(value,@"(?i)(?:btih:|torbox\||torrentio\||infohash[=:])([a-f0-9]{40})(?![a-f0-9])");
            if(m.Success)return m.Groups[1].Value.ToLowerInvariant();
        }
        // Comet/StremThru links can identify a torrent immediately before its numeric file index.
        if(Uri.TryCreate(Text(stream,"url"),UriKind.Absolute,out var releaseUri)){
            var match=Regex.Match(releaseUri.AbsolutePath,@"(?i)/([a-f0-9]{40})/(?:-?\d+)(?:/|$)");
            if(match.Success)return match.Groups[1].Value.ToLowerInvariant();
        }
        // TorBox's Stremio endpoint encodes the torrent hash in a base64 JSON path segment.
        if(Uri.TryCreate(Text(stream,"url"),UriKind.Absolute,out var uri)&&(uri.Host.Equals("torbox.app",StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith(".torbox.app",StringComparison.OrdinalIgnoreCase)))
        foreach(var part in uri.AbsolutePath.Split('/'))try
        {
            var encoded=Uri.UnescapeDataString(part).Replace('-','+').Replace('_','/');encoded=encoded.PadRight((encoded.Length+3)/4*4,'=');
            using var payload=JsonDocument.Parse(Convert.FromBase64String(encoded));
            if(payload.RootElement.ValueKind==JsonValueKind.Array&&payload.RootElement.GetArrayLength()>0){var hash=payload.RootElement[0].GetString()??"";if(Regex.IsMatch(hash,@"\A[a-fA-F0-9]{40}\z"))return hash.ToLowerInvariant();}
        }catch(Exception e) when(e is FormatException or JsonException or InvalidOperationException){}
        return "";
    }
    public static Uri StreamUri(string manifestUrl,VideoItem item)
    {
        var manifest=Manifest(manifestUrl);var id=item.IsMovie?item.ImdbId:$"{item.ImdbId}:{item.Season}:{item.Episode}";
        return new UriBuilder(manifest){Path=manifest.AbsolutePath[..^"manifest.json".Length]+"stream/"+(item.IsMovie?"movie/":"series/")+Uri.EscapeDataString(id)+".json"}.Uri;
    }
    public async Task<List<IndexedRelease>> Search(VideoItem item,CancellationToken ct)=>Require(await Discover(item,false,[item],null,ct));
    public async Task<List<IndexedRelease>> SearchSeason(VideoItem item,CancellationToken ct)=>Require(await Discover(item,true,[item],null,ct));
    static List<IndexedRelease> Require(ProviderSearchResult result)
    {
        if(result.Releases.Count>0)return result.Releases;
        throw new IOException(string.Join(" · ",result.Sources.Select(s=>s.Source+": "+(s.Message.Length>0?s.Message:s.State))));
    }
    public async Task<ProviderSearchResult> Discover(VideoItem item,bool season,List<VideoItem> probes,IProgress<DownloadSourceStatus>? progress,CancellationToken ct)
    {
        string indexWarning="";
        async Task<(List<IndexedRelease> Releases,DownloadSourceStatus Status)> Run(string source,Func<Task<List<IndexedRelease>>> fetch)
        {
            progress?.Report(new(){Source=source});
            List<IndexedRelease> found=[];var status=new DownloadSourceStatus{Source=source};
            try{found=await fetch();status.State=found.Count>0?"Ready":"Empty";status.Count=found.Count;status.Message=found.Count==0?"No matching releases":$"{found.Count} matching releases";var warnings=found.Select(r=>r.Choice.Warning).Where(w=>w.Length>0).Distinct().ToList();if(warnings.Count>0){status.State="Partial";status.Message+=" · "+string.Join(" · ",warnings.Take(2));}}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
            catch(OperationCanceledException){status.State="Failed";status.Message="Source timed out. Retry.";}
            catch(Exception e)when(e is IOException or JsonException or HttpRequestException){status.State="Failed";status.Message=e is IOException?SafeText(e.Message):"Couldn't read this source's response. Retry.";}
            progress?.Report(status);return(found,status);
        }
        async Task<List<IndexedRelease>> Index()
        {
            var found=new List<IndexedRelease>();var errors=new List<string>();
            foreach(var probe in probes.DistinctBy(p=>(p.Season,p.Episode)).Take(season?3:1)){
                try{found.AddRange(await IndexSearch(probe,ct));}catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}catch(Exception e)when(e is IOException or HttpRequestException or JsonException or OperationCanceledException){errors.Add(e is IOException?e.Message:e is OperationCanceledException?"Source timed out. Retry.":"Couldn't read this source's response. Retry.");if(found.Count==0&&errors.Count==1&&e.Message.Contains("has been removed",StringComparison.OrdinalIgnoreCase))throw;}
            }
            if(errors.Count>0)indexWarning=string.Join(" · ",errors.Distinct().Take(2));
            if(found.Count==0&&errors.Count>0)throw new IOException(errors[0]);return found;
        }
        // Both requests run even when the account already contains a matching pack.
        var libraryTask=Run("TorBox library",()=>AccountReleases(item,season,ct));
        var indexTask=config().Manifest.Length>0?Run("AIOStreams",Index):Task.FromResult((Releases:new List<IndexedRelease>(),Status:new DownloadSourceStatus{Source="AIOStreams",State="Not configured",Message="Add a manifest link in PC Settings to find new releases"}));
        await Task.WhenAll(indexTask,libraryTask);var indexed=await indexTask;var library=await libraryTask;
        if(indexWarning.Length>0&&indexed.Status.State=="Ready"){indexed.Status.State="Partial";indexed.Status.Message=indexWarning;progress?.Report(indexed.Status);}
        var merged=new List<IndexedRelease>();
        foreach(var group in indexed.Releases.Concat(library.Releases).GroupBy(r=>r.Hash,StringComparer.OrdinalIgnoreCase)){
            var release=group.First();release.Choice.Sources=group.SelectMany(r=>r.Choice.Sources).Distinct().ToList();release.Choice.SourceEntries=group.Sum(r=>Math.Max(1,r.Choice.SourceEntries));
            var ownedRelease=group.FirstOrDefault(r=>r.Choice.Sources.Contains("TorBox library"));
            if(ownedRelease!=null){release.Choice.Cached=ownedRelease.Choice.Cached;release.Choice.CacheStatus=ownedRelease.Choice.CacheStatus;release.Choice.Size=ownedRelease.Choice.Size;release.Choice.SizeKind="Pack";}
            merged.Add(release);
        }
        return new(){Releases=merged,Sources=[indexed.Status,library.Status]};
    }
    public static bool AccountMatch(VideoItem item,JsonElement torrent,bool season)
    {
        static string Words(string s)=>Regex.Replace(s.ToLowerInvariant(),@"[^\p{L}\p{Nd}]+"," ").Trim();
        var title=Words(item.Title);var name=Words(Text(torrent,"name"));
        if(title.Length==0||!new[]{title,Words(item.ReviewTitle)}.Where(t=>t.Length>0).Any(t=>(" "+name+" ").Contains(" "+t+" ",StringComparison.Ordinal)))return false;
        var years=Regex.Matches(Text(torrent,"name"),@"(?<!\d)(?:19|20)\d{2}(?!\d)").Select(m=>int.Parse(m.Value)).ToList();
        if(item.Year>0&&years.Count>0&&!years.Contains(item.Year))return false;
        var videos=ReadFiles(torrent).Where(f=>DownloadNaming.Video(f.Name)&&!Regex.IsMatch(f.Name,@"(?i)(?:^|[/\\ ._-])sample(?:[/\\ ._-]|$)")).ToList();
        if(item.IsMovie){
            if(videos.Count!=1||DownloadNaming.Episode(videos[0].Name,0).Episode>0)return false;
            return true;
        }
        var numbers=videos.Select(f=>DownloadNaming.Episode(f.Name,item.Season)).ToList();
        return season?numbers.Where(n=>n.Season==item.Season&&n.Episode>0).Distinct().Count()>1:numbers.Contains((item.Season,item.Episode));
    }
    async Task<List<IndexedRelease>> AccountReleases(VideoItem item,bool season,CancellationToken ct)
    {
        var data=await Api("torrents/mylist",null,ct);var result=new List<IndexedRelease>();
        if(data.ValueKind!=JsonValueKind.Array)return result;
        foreach(var t in data.EnumerateArray()){
            var hash=Text(t,"hash");var id=Number(t,"id");if(!Regex.IsMatch(hash,@"\A[a-fA-F0-9]{40}\z")||id<0||!AccountMatch(item,t,season))continue;
            hash=hash.ToLowerInvariant();var files=ReadFiles(t);var ready=t.TryGetProperty("download_present",out var present)&&present.ValueKind==JsonValueKind.True;
            owned[hash]=new(id,hash,ready,files);
            var name=SafeText(Text(t,"name"));var size=files.Where(f=>DownloadNaming.Video(f.Name)).Sum(f=>f.Size);
            result.Add(new(new(){Id=Guid.NewGuid().ToString("N"),Name=name,Detail=season?$"Season {item.Season} candidate · episode coverage will be verified":"Choose this episode from its file list",Cached=ready,CacheStatus=ready?"Ready":"Not cached",Size=size,SizeKind="Pack",Sources=["TorBox library"],Resolution=Resolution(name),ReferenceSeason=item.Season,ReferenceEpisode=item.Episode},hash));
        }
        return result;
    }
    string SafeText(string value){var key=config().Key;if(key.Length>0)value=value.Replace(key,"[private]");value=Regex.Replace(value,@"https?://\S+","[link]");return value[..Math.Min(1500,value.Length)];}
    async Task<List<IndexedRelease>> IndexSearch(VideoItem item,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(config().Manifest))throw new IOException("Add an AIOStreams installation URL to find new releases. No matching files were found in your TorBox library.");
        if(!Regex.IsMatch(item.ImdbId,@"^tt\d+$"))throw new IOException("Correct this title's IMDb match before finding releases.");
        var url=StreamUri(config().Manifest,item);
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        // AIOStreams documents this client prefix for returning parsed stream metadata.
        request.Headers.UserAgent.ParseAdd("AIO-Noir/0.11.0");
        HttpResponseMessage received;
        try{received=await http.SendAsync(request,ct);}catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new IOException("AIOStreams search timed out. Try again shortly.");}catch(HttpRequestException){throw new IOException("Couldn't connect to AIOStreams.");}
        using var response=received;if(!response.IsSuccessStatusCode)throw new IOException($"AIOStreams couldn't find releases ({(int)response.StatusCode}). Check its installation URL.");
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var found=new List<IndexedRelease>();
        if(!doc.RootElement.TryGetProperty("streams",out var streams)||streams.ValueKind!=JsonValueKind.Array)return found;
        var errors=new List<string>();var playable=0;var converted=0;
        foreach(var stream in streams.EnumerateArray())
        {
            if(stream.ValueKind!=JsonValueKind.Object)continue;
            if(stream.TryGetProperty("streamData",out var metadata)&&Text(metadata,"type")=="error"){
                if(metadata.TryGetProperty("error",out var error))errors.Add(SafeText(Text(error,"description")));continue;
            }
            if(stream.TryGetProperty("externalUrl",out _)&&!stream.TryGetProperty("url",out _)&&!stream.TryGetProperty("infoHash",out _))continue;
            playable++;
            var hash=Hash(stream);if(hash.Length==0)continue;converted++;
            var sourceMatch=Regex.Match(Text(stream,"description"),@"\[NOIR_SOURCE:([^\]\r\n]{1,100})\]");
            var source=sourceMatch.Success?SafeText(sourceMatch.Groups[1].Value):"AIOStreams";
            if(found.FirstOrDefault(x=>x.Hash==hash) is {} duplicate){duplicate.Choice.SourceEntries++;if(!duplicate.Choice.Sources.Contains(source))duplicate.Choice.Sources.Add(source);continue;}
            var name=Text(stream,"name");var detail=Text(stream,"description");if(detail.Length==0)detail=Text(stream,"title");
            // URLs and any provider account configuration never enter the phone response.
            name=Regex.Replace(name,@"https?://\S+","[link]");detail=Regex.Replace(detail,@"https?://\S+","[link]");
            long size=0;if(stream.TryGetProperty("behaviorHints",out var hints)&&hints.TryGetProperty("videoSize",out var bytes)&&bytes.ValueKind==JsonValueKind.Number)bytes.TryGetInt64(out size);
            if(stream.TryGetProperty("behaviorHints",out var fileHints)&&Text(fileHints,"filename") is {Length:>0} filename)name=filename;
            var audio=Regex.Match(detail,@"\[NOIR_AUDIO:([^\]\r\n]{0,200})\]");
            name=SafeText(name);detail=SafeText(Regex.Replace(detail,@"\[NOIR_(?:HASH|SOURCE|AUDIO):[^\]\r\n]*\]","").Trim());
            found.Add(new(new(){Id=Guid.NewGuid().ToString("N"),Name=name.Length>0?name[..Math.Min(256,name.Length)]:"Release",Detail=detail,Languages=audio.Success&&audio.Groups[1].Value.Trim().Length>0?SafeText(audio.Groups[1].Value):"Unknown",Size=size,SizeKind=item.IsMovie?"Movie":"Episode",Sources=[source],Resolution=Resolution(name+" "+detail),ReferenceSeason=item.Season,ReferenceEpisode=item.Episode},hash));
        }
        if(found.Count==0)throw new IOException(DiscoveryMessage(playable,errors));
        if(errors.Count>0||playable>converted){found[0].Choice.Warning=(errors.Count>0?SafeText(errors[0])+" · ":"")+(playable>converted?"Some links could not be converted to TorBox torrent downloads":"");}
        try
        {
            var hashes=string.Join("&",found.Take(100).Select(x=>"hash="+x.Hash));
            var cached=await Api("torrents/checkcached?format=list&list_files=false&"+hashes,null,ct);
            if(cached.ValueKind==JsonValueKind.Array){var set=cached.EnumerateArray().Select(x=>Text(x,"hash")).ToHashSet(StringComparer.OrdinalIgnoreCase);foreach(var x in found.Take(100)){x.Choice.Cached=set.Contains(x.Hash);x.Choice.CacheStatus=x.Choice.Cached?"Ready":"Not cached";}}
        }catch(Exception e)when(e is IOException or HttpRequestException||e is OperationCanceledException&&!ct.IsCancellationRequested){} // Releases remain browsable if the cache lookup fails.
        return found;
    }
    public static string Resolution(string name)=>Regex.Match(name,@"(?i)(?<!\d)(2160p|1080p|720p|480p|4k)(?!\d)").Value.ToLowerInvariant().Replace("4k","2160p");
    public static string DiscoveryMessage(int playable,IEnumerable<string> errors)
    {
        var notes=string.Join(" · ",errors.Where(e=>e.Length>0).Distinct().Take(2));
        return playable>0?$"{playable} releases found, but AIOStreams supplied playback links without usable download references. Connect a Noir download profile in Settings."+(notes.Length>0?" · Source note: "+notes:"")
            :notes.Length>0?"AIOStreams: "+notes:"AIOStreams returned no releases. Check its indexers and filters.";
    }
    public async Task<List<TorboxFile>> Files(string hash,CancellationToken ct)
    {
        if(owned.TryGetValue(hash,out var saved)&&saved.Files.Count>0)return saved.Files;
        var cached=await Api("torrents/checkcached?hash="+hash+"&format=list&list_files=true",null,ct);
        if(cached.ValueKind==JsonValueKind.Array&&cached.GetArrayLength()>0){var files=ReadFiles(cached[0]);if(files.Count>0)return files;}
        var info=await Api("torrents/torrentinfo?hash="+hash+"&timeout=20&use_cache_lookup=true",null,ct);
        var result=ReadFiles(info);if(result.Count==0)throw new IOException("TorBox has no file list for this release yet. Try another release or retry after its metadata is available.");return result;
    }
    public async Task<TorboxTorrent> Prepare(string hash,long torrentId,CancellationToken ct)
    {
        if(torrentId<0&&owned.TryGetValue(hash,out var saved))torrentId=saved.Id;
        if(torrentId<0)
        {
            using var body=new MultipartFormDataContent();body.Add(new StringContent("magnet:?xt=urn:btih:"+hash),"magnet");body.Add(new StringContent("0"),"seed");body.Add(new StringContent("false"),"allow_zip");
            var created=await Api("torrents/createtorrent",body,ct);
            torrentId=Number(created,"torrent_id");if(torrentId<0)torrentId=Number(created,"id");
            if(torrentId<0)throw new IOException("TorBox didn't return a torrent ID. Check your account's download limits.");
        }
        var data=await Api("torrents/mylist?id="+torrentId+"&bypass_cache=true",null,ct);
        if(data.ValueKind==JsonValueKind.Array)data=data.GetArrayLength()>0?data[0]:default;
        if(data.ValueKind!=JsonValueKind.Object)throw new IOException("TorBox is preparing this release. Resume shortly.");
        var actual=Text(data,"hash");if(actual.Length>0&&!actual.Equals(hash,StringComparison.OrdinalIgnoreCase))throw new IOException("TorBox returned a different release.");
        var ready=data.TryGetProperty("download_present",out var present)&&present.ValueKind==JsonValueKind.True;
        return new(torrentId,hash,ready,ReadFiles(data));
    }
    public async Task<string> Link(long torrentId,long fileId,CancellationToken ct)
    {
        var data=await Api("torrents/requestdl?token="+Uri.EscapeDataString(config().Key)+"&torrent_id="+torrentId+"&file_id="+fileId+"&redirect=false",null,ct);
        var url=data.ValueKind==JsonValueKind.String?data.GetString()??"":Text(data,"url");
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length>0||uri.IsLoopback)throw new IOException("TorBox returned an invalid download link.");return url;
    }
    async Task<JsonElement> Api(string path,HttpContent? content,CancellationToken ct)
    {
        try {
        var key=config().Key;if(key.Length==0)throw new IOException("Add your TorBox API key in PC Settings first.");
        using var request=new HttpRequestMessage(content==null?HttpMethod.Get:HttpMethod.Post,"https://api.torbox.app/v1/api/"+path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);request.Content=content;
        using var response=await http.SendAsync(request,ct);
        if(!response.IsSuccessStatusCode)throw new IOException(response.StatusCode==HttpStatusCode.TooManyRequests?"TorBox is busy. Wait a minute before retrying.":$"TorBox request failed ({(int)response.StatusCode}). Check your key and account limits.");
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));var root=doc.RootElement;
        if(root.TryGetProperty("success",out var success)&&success.ValueKind==JsonValueKind.False)throw new IOException("TorBox could not complete this request. Check the release and account limits in your TorBox dashboard.");
        if(!root.TryGetProperty("data",out var data))throw new IOException("TorBox returned an incomplete response.");return data.Clone();
        }catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new IOException("TorBox request timed out. Try again shortly.");}
        catch(HttpRequestException){throw new IOException("Couldn't connect to TorBox. Check your connection and retry.");}
        catch(JsonException){throw new IOException("Couldn't read TorBox's response. Retry shortly.");}
    }
    public static List<TorboxFile> ReadFiles(JsonElement data)
    {
        var result=new List<TorboxFile>();if(data.ValueKind!=JsonValueKind.Object||!data.TryGetProperty("files",out var files)||files.ValueKind!=JsonValueKind.Array)return result;
        foreach(var f in files.EnumerateArray()){var name=Text(f,"short_name");if(name.Length==0)name=Text(f,"shortName");if(name.Length==0)name=Text(f,"name");var id=Number(f,"id");if(id>=0)result.Add(new(id,name,Math.Max(0,Number(f,"size"))));}return result;
    }
    static string Text(JsonElement element,string name)=>element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    static long Number(JsonElement element,string name)=>element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt64(out var n)?n:-1;
}
