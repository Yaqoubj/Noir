using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public sealed class RealDebridProvider(Func<DownloadCredentials> config,HttpClient? client=null) : IDownloadProvider
{
    readonly HttpClient http=client??DownloadSecurity.Client(TimeSpan.FromSeconds(45));
    readonly SemaphoreSlim metadata=new(1);
    readonly Dictionary<string,string> ids=new(StringComparer.OrdinalIgnoreCase);
    readonly object accountGate=new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<long,string> handles=new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string,bool> downloaded=new(StringComparer.OrdinalIgnoreCase);
    string accountKey="";
    long nextId;
    long Handle(string id){foreach(var pair in handles)if(pair.Value==id)return pair.Key;var n=Interlocked.Increment(ref nextId);handles[n]=id;return n;}
    public Task<List<IndexedRelease>> Search(VideoItem item,CancellationToken ct)=>SearchCore(item,false,ct);
    public Task<List<IndexedRelease>> SearchSeason(VideoItem item,CancellationToken ct)=>SearchCore(item,true,ct);
    async Task<List<IndexedRelease>> SearchCore(VideoItem item,bool season,CancellationToken ct)
    {
        var r=await Discover(item,season,[item],null,ct);if(r.Releases.Count==0)throw new IOException(string.Join(" · ",r.Sources.Select(s=>s.Source+": "+s.Message)));return r.Releases;
    }
    public async Task<ProviderSearchResult> Discover(VideoItem item,bool season,List<VideoItem> probes,IProgress<DownloadSourceStatus>? progress,CancellationToken ct)
    {
        async Task<ProviderSearchResult> Run(string name,Func<Task<List<IndexedRelease>>> fetch)
        {
            var status=new DownloadSourceStatus{Source=name};progress?.Report(status);List<IndexedRelease> found=[];
            try{found=await fetch();status.State=found.Count>0?"Ready":"Empty";status.Count=found.Count;status.Message=found.Count>0?$"{found.Count} matching releases":"No matching releases";}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
            catch(Exception e){status.State="Failed";status.Message=e is IOException?DownloadSecurity.SafeText(e.Message,config()):"Couldn't read this source. Retry shortly.";}
            progress?.Report(status);return new(){Releases=found,Sources=[status]};
        }
        async Task<List<IndexedRelease>> Index()
        {
            if(config().Manifest.Length==0)return [];
            var index=new TorboxProvider(()=>{var c=config();c.Service="Real-Debrid";return c;},http);var found=new List<IndexedRelease>();var errors=0;
            foreach(var probe in probes.DistinctBy(p=>(p.Season,p.Episode)).Take(season?3:1))try{found.AddRange(await index.IndexSearch(probe,ct));}catch(IOException){errors++;}
            if(found.Count==0&&errors>0)throw new IOException("AIOStreams couldn't find releases. Check its installation URL and services.");
            return found;
        }
        var tasks=new[]{Run("AIOStreams",Index),Run("Real-Debrid library",()=>Library(item,season,ct))};var result=await Task.WhenAll(tasks);
        var merged=new List<IndexedRelease>();
        foreach(var group in result.SelectMany(r=>r.Releases).GroupBy(r=>r.Direct!=null?r.Choice.Id:r.Hash,StringComparer.OrdinalIgnoreCase)){
            var r=group.First();r.Choice.Sources=group.SelectMany(x=>x.Choice.Sources).Distinct().ToList();r.Choice.SourceEntries=group.Sum(x=>x.Choice.SourceEntries);
            var library=group.FirstOrDefault(x=>x.Choice.Sources.Contains("Real-Debrid library"));if(library!=null){r.Choice.Cached=library.Choice.Cached;r.Choice.CacheStatus=library.Choice.CacheStatus;}
            if(r.Direct==null&&downloaded.ContainsKey(r.Hash)){r.Choice.Cached=true;r.Choice.CacheStatus="Ready";}
            if(r.Direct==null)r.Choice.Detail+=" · Reading files may add this magnet to your Real-Debrid account.";merged.Add(r);
        }
        return new(){Releases=merged,Sources=result.SelectMany(r=>r.Sources).ToList()};
    }
    string Account()
    {
        var key=config().Key;lock(accountGate){if(accountKey!=key){ids.Clear();handles.Clear();downloaded.Clear();accountKey=key;}return key;}
    }
    Task<List<JsonElement>> Listing(CancellationToken ct)
    {
        var key=Account();return PlanningRequests.Read(this,"torrents|"+key,async()=>{
            var all=await Torrents(ct);lock(accountGate){if(Account()==key){ids.Clear();downloaded.Clear();foreach(var t in all){var hash=Text(t,"hash");var id=Text(t,"id");if(hash.Length>0&&id.Length>0)ids.TryAdd(hash,id);if(Text(t,"status")=="downloaded")downloaded[hash]=true;}}}return all;
        });
    }
    async Task<List<JsonElement>> Torrents(CancellationToken ct)
    {
        var result=new List<JsonElement>();
        for(int page=1;;page++){var data=await Api("torrents?limit=100&page="+page,null,ct);if(data.ValueKind!=JsonValueKind.Array)throw new IOException("Real-Debrid returned an incomplete torrent list.");var batch=data.EnumerateArray().Select(x=>x.Clone()).ToList();result.AddRange(batch);if(batch.Count<100)break;ct.ThrowIfCancellationRequested();}
        return result;
    }
    async Task<List<IndexedRelease>> Library(VideoItem item,bool season,CancellationToken ct)
    {
        var all=await Listing(ct);using var slots=new SemaphoreSlim(3);
        var tasks=all.Where(t=>Regex.IsMatch(Text(t,"hash"),@"\A[a-fA-F0-9]{40}\z")&&Candidate(item,Text(t,"filename"))).Select(async t=>{
            await slots.WaitAsync(ct);try{
                var id=Text(t,"id");var info=await PlanningRequests.Read(this,"info|"+Account()+"|"+id,()=>Api("torrents/info/"+Uri.EscapeDataString(id),null,ct));var files=ReadFiles(info);
                using var normalized=JsonDocument.Parse(JsonSerializer.Serialize(new{name=Text(t,"filename"),files=files.Select(f=>new{id=f.Id,name=f.Name,size=f.Size})}));if(!TorboxProvider.AccountMatch(item,normalized.RootElement,season))return null;
                var hash=Text(t,"hash").ToLowerInvariant();
                var ready=Text(info,"status")=="downloaded";var name=DownloadSecurity.SafeText(Text(t,"filename"),config());return new IndexedRelease(new(){Id=Guid.NewGuid().ToString("N"),Name=name,Size=files.Where(f=>DownloadNaming.Video(f.Name)).Sum(f=>f.Size),SizeKind="Pack",Cached=ready,CacheStatus=ready?"Ready":"Unknown",Sources=["Real-Debrid library"],Resolution=TorboxProvider.Resolution(name),ReferenceSeason=item.Season,ReferenceEpisode=item.Episode},hash);
            }finally{slots.Release();}
        }).ToList();return (await Task.WhenAll(tasks)).Where(r=>r!=null).Select(r=>r!).ToList();
    }
    static bool Candidate(VideoItem item,string name)
    {
        static string Words(string s)=>Regex.Replace(s.ToLowerInvariant(),@"[^\p{L}\p{Nd}]+"," ").Trim();
        return new[]{item.Title,item.ReviewTitle}.Select(Words).Any(t=>t.Length>0&&(" "+Words(name)+" ").Contains(" "+t+" ",StringComparison.Ordinal));
    }
    async Task<(string Id,JsonElement Info)> Resolve(string hash,CancellationToken ct)
    {
        if(!Regex.IsMatch(hash,@"\A[a-fA-F0-9]{40}\z"))throw new IOException("This release has no valid torrent identity.");
        await metadata.WaitAsync(ct);try{
            var key=Account();string? id;lock(accountGate)ids.TryGetValue(hash,out id);
            if(id==null){
                var existing=(await Listing(ct)).FirstOrDefault(t=>Text(t,"hash").Equals(hash,StringComparison.OrdinalIgnoreCase));id=Text(existing,"id");
                if(id.Length==0){var added=await Api("torrents/addMagnet",new FormUrlEncodedContent(new Dictionary<string,string>{{"magnet","magnet:?xt=urn:btih:"+hash}}),ct);id=Text(added,"id");}
                if(id.Length==0)throw new IOException("Real-Debrid didn't return a torrent ID. Check your account limits.");lock(accountGate)if(Account()==key)ids[hash]=id;
            }
            var info=await PlanningRequests.Read(this,"info|"+key+"|"+id,()=>Api("torrents/info/"+Uri.EscapeDataString(id),null,ct));if(!Text(info,"hash").Equals(hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Real-Debrid returned a different release.");return(id,info);
        }finally{metadata.Release();}
    }
    public async Task<List<TorboxFile>> Files(string hash,CancellationToken ct)
    {
        var (_,info)=await Resolve(hash,ct);var files=ReadFiles(info);if(files.Count==0)throw new IOException("Real-Debrid is reading the magnet metadata. Retry shortly.");return files;
    }
    public async Task<TorboxTorrent> Prepare(string hash,long torrentId,CancellationToken ct)
    {
        // Persisted numeric IDs belong to the previous process; hash resolution also handles account changes.
        var (id,info)=await Resolve(hash,ct);var status=Text(info,"status");
        if(status is "error" or "magnet_error" or "virus" or "dead")throw new IOException("Real-Debrid couldn't fetch this release. Choose another release.");
        if(status=="waiting_files_selection"){
            await Api("torrents/selectFiles/"+Uri.EscapeDataString(id),new FormUrlEncodedContent(new Dictionary<string,string>{{"files","all"}}),ct);info=await Api("torrents/info/"+Uri.EscapeDataString(id),null,ct);
        }
        return new(Handle(id),hash,Text(info,"status")=="downloaded",ReadFiles(info));
    }
    public async Task<string> Link(long torrentId,long fileId,CancellationToken ct)
    {
        Account();if(!handles.TryGetValue(torrentId,out var id))throw new IOException("Refresh this Real-Debrid release before retrying.");
        var info=await Api("torrents/info/"+Uri.EscapeDataString(id),null,ct);if(Text(info,"status")!="downloaded")throw new IOException("Real-Debrid is still fetching this release. Resume shortly.");
        var selected=info.GetProperty("files").EnumerateArray().Where(Selected).ToList();var index=selected.FindIndex(f=>Number(f,"id")==fileId);
        if(index<0||!info.TryGetProperty("links",out var links)||index>=links.GetArrayLength())throw new IOException("Real-Debrid's selected files changed. Search again.");
        var result=await Api("unrestrict/link",new FormUrlEncodedContent(new Dictionary<string,string>{{"link",links[index].GetString()??""}}),ct);var link=Text(result,"download");if(!DownloadSecurity.ValidUrl(link))throw new IOException("Real-Debrid returned an invalid download link.");return link;
    }
    static bool Selected(JsonElement f)=>f.TryGetProperty("selected",out var s)&&(s.ValueKind==JsonValueKind.True||s.ValueKind==JsonValueKind.Number&&s.TryGetInt32(out var n)&&n==1);
    List<TorboxFile> ReadFiles(JsonElement info)
    {
        if(!info.TryGetProperty("files",out var files)||files.ValueKind!=JsonValueKind.Array)return [];
        var active=Text(info,"status") is "downloaded" or "downloading" or "queued" or "compressing" or "uploading";
        return files.EnumerateArray().Where(f=>!active||Selected(f)).Select(f=>new TorboxFile(Number(f,"id"),DownloadSecurity.SafeText(Text(f,"path").TrimStart('/'),config()),Math.Max(0,Number(f,"bytes")))).Where(f=>f.Id>=0).ToList();
    }
    async Task<JsonElement> Api(string path,HttpContent? content,CancellationToken ct)
    {
        try{
            var key=Account();if(key.Length==0)throw new IOException("Add your Real-Debrid token in PC Settings first.");
            using var request=new HttpRequestMessage(content==null?HttpMethod.Get:HttpMethod.Post,"https://api.real-debrid.com/rest/1.0/"+path){Content=content};request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
            using var response=await http.SendAsync(request,ct);
            if(!response.IsSuccessStatusCode)throw new IOException(response.StatusCode switch{HttpStatusCode.Unauthorized=>"Real-Debrid rejected this token. Update it in PC Settings.",HttpStatusCode.Forbidden=>"Real-Debrid requires permission and an active premium account for this request.",HttpStatusCode.TooManyRequests=>"Real-Debrid is busy. Wait a minute before retrying.",HttpStatusCode.ServiceUnavailable=>"Real-Debrid is temporarily unavailable. Retry shortly.",_=>$"Real-Debrid couldn't complete this request ({(int)response.StatusCode}). Check your account limits."});
            if(response.StatusCode==HttpStatusCode.NoContent)return default;
            using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));if(doc.RootElement.ValueKind==JsonValueKind.Object&&doc.RootElement.TryGetProperty("error",out _))throw new IOException("Real-Debrid couldn't complete this request. Check your token, premium account and download limits.");return doc.RootElement.Clone();
        }catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new IOException("Real-Debrid request timed out. Retry shortly.");}catch(HttpRequestException){throw new IOException("Couldn't connect to Real-Debrid. Retry shortly.");}catch(JsonException){throw new IOException("Couldn't read Real-Debrid's response. Retry shortly.");}
    }
    static string Text(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    static long Number(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.TryGetInt64(out var n)?n:-1;
}
