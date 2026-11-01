using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public sealed record SubtitleCandidate(string Id,string Language,string Name,string Release,string Url,bool HashMatch,int Score);
public sealed record SubtitleAsset(string Language,string Name,string Url);
public sealed class SubtitleDownloads
{
    readonly HttpClient http;
    readonly string backupDirectory;
    readonly Func<(string Url,bool Only)> source;
    readonly SemaphoreSlim work=new(1,1);
    public static string ToolRoot=Path.Combine(AppContext.BaseDirectory,"Tools","subtitle-sync");
    public SubtitleDownloads(string profile,HttpClient? http=null,Func<(string Url,bool Only)>? source=null){this.source=source??(()=> ("",true));this.http=http??new HttpClient{Timeout=TimeSpan.FromSeconds(35)};backupDirectory=Path.Combine(profile,"subtitle-originals");Directory.CreateDirectory(backupDirectory);}
    public static string Language(string value)=>value.Trim().ToLowerInvariant() switch {"eng" or "en" or "english"=>"en","ara" or "ar" or "arabic"=>"ar",_=>""};
    public static string SidecarLanguage(string name)
    {
        var m=Regex.Match(name,@"(?i)(?:^|[./\\ _-])(eng|english|en|ara|arabic|ar)(?:[./\\ _-]|$)");return Language(m.Groups[1].Value);
    }
    public static bool TextSubtitle(string name)=>Path.GetExtension(name).ToLowerInvariant() is ".srt" or ".ass" or ".ssa" or ".vtt";
    public static async Task<string> VideoHash(string path,CancellationToken ct)
    {
        await using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite,65536,true);if(f.Length<131072)return "";
        ulong hash=(ulong)f.Length;var bytes=new byte[65536];
        foreach(var offset in new[]{0L,f.Length-65536}){f.Position=offset;await f.ReadExactlyAsync(bytes,ct);for(int i=0;i<bytes.Length;i+=8)hash=unchecked(hash+BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(i,8)));}
        return hash.ToString("x16");
    }
    public static int ReleaseScore(string subtitle,string release)
    {
        static HashSet<string> Words(string s)=>Regex.Matches(s.ToLowerInvariant(),@"[a-z0-9]{2,}").Select(m=>m.Value).Where(s=>s is not ("mkv" or "mp4" or "srt" or "en" or "ar" or "eng" or "ara")).ToHashSet();
        var a=Words(subtitle);var b=Words(release);return b.Count==0?0:(int)(100d*a.Intersect(b).Count()/b.Count);
    }
    // A stable release key survives expiring stream links and keeps different encodes apart.
    public static VideoItem StreamTarget(string profile,VideoItem item,string release)
    {
        var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(item.Id+":"+item.ImdbId+":"+item.Season+":"+item.Episode+":"+release)));
        return new VideoItem{Id=item.Id,Path=Path.Combine(profile,"stream-subtitles",key,"video.mkv")};
    }
    public Task<List<SubtitleCandidate>> Search(VideoItem item,string release,CancellationToken ct,bool hashLocal=true)=>Search(item,release,ct,hashLocal,source());
    async Task<List<SubtitleCandidate>> Search(VideoItem item,string release,CancellationToken ct,bool hashLocal,(string Url,bool Only) settings)
    {
        var imdb=item.ImdbId;if(!Regex.IsMatch(imdb,@"^tt\d+$"))return [];
        var hash=hashLocal&&File.Exists(item.Path)?await VideoHash(item.Path,ct):"";
        var id=item.IsMovie?imdb:$"{imdb}:{item.Season}:{item.Episode}";
        var extra="filename="+Uri.EscapeDataString(Path.GetFileName(release));
        if(hash.Length>0)extra+="&videoHash="+hash+"&videoSize="+new FileInfo(item.Path).Length;
        var result=new List<SubtitleCandidate>();var seen=new HashSet<string>(StringComparer.Ordinal);var succeeded=false;var sources=SubtitleAddon.Sources(settings.Url,settings.Only);
        foreach(var baseUrl in sources){
        try{
        var url=baseUrl+"/subtitles/"+(item.IsMovie?"movie/":"series/")+Uri.EscapeDataString(id)+"/"+extra+".json";
        using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
        var bytes=await Limited(response.Content,4*1024*1024,ct);using var doc=JsonDocument.Parse(bytes);succeeded=true;
        if(doc.RootElement.ValueKind!=JsonValueKind.Object||!doc.RootElement.TryGetProperty("subtitles",out var all)||all.ValueKind!=JsonValueKind.Array)continue;
        foreach(var s in all.EnumerateArray().Take(500)){
            if(s.ValueKind!=JsonValueKind.Object)continue;
            string Text(string k)=>s.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
            var language=Language(Text("lang"));if(language.Length==0)continue;
            if(!item.IsMovie&&((s.TryGetProperty("season",out var season)&&season.ValueKind==JsonValueKind.Number&&season.TryGetInt32(out var sn)&&sn!=item.Season)||(s.TryGetProperty("episode",out var ep)&&ep.ValueKind==JsonValueKind.Number&&ep.TryGetInt32(out var en)&&en!=item.Episode)))continue;
            var link=Text("url");if(!ValidUrl(link)||!seen.Add(link))continue;var name=Text("subtitleFileName");var rel=Text("movieReleaseName");
            var exact=Text("m")=="h";result.Add(new(Text("id"),language,name,rel,link,exact,(exact?1000:0)+ReleaseScore(rel+" "+name,release)));
        }
        }catch(Exception e)when(e is HttpRequestException or JsonException or IOException||e is OperationCanceledException&&!ct.IsCancellationRequested){if(sources.Count==1)throw new IOException("Subtitle lookup unavailable.");}
        }
        if(!succeeded)throw new IOException("Subtitle lookup unavailable.");
        return result.OrderByDescending(s=>s.Score).Take(500).ToList();
    }
    public async Task<string> Fetch(VideoItem item,string release,List<string> languages,bool autoSync,CancellationToken ct,List<SubtitleAsset>? pack=null,bool skipEmbedded=false,VideoItem? streamTarget=null)
    {
        await work.WaitAsync(ct);
        try{
            if(streamTarget==null&&!File.Exists(item.Path))throw new IOException("Open a stream or save the video before downloading subtitles.");
            var target=streamTarget??item;
            if(streamTarget!=null)Directory.CreateDirectory(Path.GetDirectoryName(target.Path)!);
            var settings=source();var requested=languages.Select(Language).Where(l=>l.Length>0).Distinct().ToList();var messages=new List<string>();
            var embedded=streamTarget==null&&skipEmbedded&&requested.Count>0?await EmbeddedSubtitles.Probe(item.Path,ct):[];
            List<SubtitleCandidate>? found=null;bool searched=false;
            foreach(var lang in requested){
                ct.ThrowIfCancellationRequested();var label=lang=="ar"?"Arabic":"English";
                var existing=MobileSubtitles.Find(target,requireVideo:streamTarget==null).FirstOrDefault(s=>SidecarLanguage(s.Path)==lang);
                if(existing.Path!=null){messages.Add(label+" already saved");continue;}
                if(embedded.Contains(lang)){messages.Add(label+" built into the video"+(autoSync?" · "+await SyncEmbedded(item.Path,lang,ct):""));continue;}
                var bundled=settings.Url.Length>0&&settings.Only?null:pack?.FirstOrDefault(s=>s.Language==lang);string link,ext;bool exact;
                if(bundled!=null){link=bundled.Url;ext=Path.GetExtension(bundled.Name).ToLowerInvariant();exact=true;}
                else{
                    try{if(!searched){searched=true;found=await Search(item,release,ct,streamTarget==null,settings);}if(found==null){messages.Add(label+" lookup unavailable");continue;}}catch(Exception e)when(e is HttpRequestException or JsonException or IOException||e is OperationCanceledException&&!ct.IsCancellationRequested){messages.Add(label+" lookup unavailable");continue;}
                    var best=found.FirstOrDefault(s=>s.Language==lang);if(best==null){messages.Add(label+" not available");continue;}link=best.Url;ext=Path.GetExtension(best.Name).ToLowerInvariant();if(!TextSubtitle(best.Name))ext=".srt";exact=best.HashMatch;
                }
                try{
                    var data=await Download(link,ct);var text=Decode(data);if(!ValidText(text,ext))throw new IOException("Invalid subtitle response");
                    var destination=Path.Combine(Path.GetDirectoryName(target.Path)!,Path.GetFileNameWithoutExtension(target.Path)+"."+lang+ext);
                    var temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
                    await File.WriteAllTextAsync(temp,text,new UTF8Encoding(false),ct);
                    try{File.Move(temp,destination,false);}finally{if(File.Exists(temp))File.Delete(temp);}
                    var timing=autoSync&&!exact&&streamTarget==null?await Sync(item.Path,destination,ct):exact?"release matched":"timing unchanged · adjust in Subtitles → Timing if needed";
                    messages.Add(label+" saved · "+timing);
                }catch(Exception e)when(e is IOException or HttpRequestException or InvalidDataException||e is OperationCanceledException&&!ct.IsCancellationRequested){messages.Add(label+" download unavailable");}
            }
            return string.Join(" · ",messages);
        }finally{work.Release();}
    }
    public async Task<string> Sync(string video,string subtitle,CancellationToken ct)
    {
        if(!File.Exists(video)||!File.Exists(subtitle))throw new IOException("Save the video and subtitles first.");
        var backup=Path.Combine(backupDirectory,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(subtitle)))+Path.GetExtension(subtitle).ToLowerInvariant());
        var (message,offset,_)=await Align(video,subtitle,ct,backup);
        return offset==null?message:$"auto-synced ({offset:+0.00;-0.00;0.00}s)";
    }
    // Subtitles built into the video can be off too; check a copy and keep it beside the video only when it moved.
    public async Task<string> SyncEmbedded(string video,string language,CancellationToken ct)
    {
        if(!File.Exists(Path.Combine(ToolRoot,"NoirSubtitleSync.exe"))||!File.Exists(Path.Combine(ToolRoot,"ffmpeg","ffmpeg.exe")))return "automatic timing tool unavailable";
        var track=(await EmbeddedSubtitles.ProbeTextTracks(video,ct)).FirstOrDefault(t=>t.Language==language);
        if(track.Extension==null)return "picture subtitles · timing can't be checked";
        var folder=Path.Combine(Path.GetTempPath(),"noir-embedded-"+Guid.NewGuid().ToString("N"));
        try{
            Directory.CreateDirectory(folder);var extracted=Path.Combine(folder,"track"+track.Extension);
            if(!await EmbeddedSubtitles.Extract(video,track.Index,extracted,ct))return "timing check unavailable";
            var (message,offset,scale)=await Align(video,extracted,ct,null,audio:true);
            if(offset==null)return message=="timing uncertain · original kept"?"timing uncertain · built-in track kept":message;
            if(Math.Abs(offset.Value)<.25&&Math.Abs(scale-1)<.001)return "timing matches the video";
            File.Move(extracted,SyncedCopy(video,language,track.Extension),true);
            return $"auto-synced copy saved ({offset:+0.00;-0.00;0.00}s)";
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException){return "timing check unavailable";}
        finally{try{Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    public static string SyncedCopy(string video,string language,string extension)=>Path.Combine(Path.GetDirectoryName(video)!,Path.GetFileNameWithoutExtension(video)+".synced."+language+extension);
    // The language stays last so VLC labels the track [English] / [Arabic].
    public static bool IsSyncedCopy(string name)=>Regex.IsMatch(name,@"(?i)\.synced\.(en|ar)(\.(srt|ass|ssa|vtt))?$");
    // Offset is null when the subtitle was left unchanged; on success the subtitle file is replaced in place.
    async Task<(string Message,double? Offset,double Scale)> Align(string video,string subtitle,CancellationToken ct,string? backup,bool audio=false)
    {
        var tool=Path.Combine(ToolRoot,"NoirSubtitleSync.exe");
        if(!File.Exists(tool))return ("automatic timing tool unavailable",null,1);
        var ext=Path.GetExtension(subtitle).ToLowerInvariant();if(ext is not (".srt" or ".ass" or ".ssa" or ".vtt"))return ("timing unchanged",null,1);
        var output=Path.Combine(Path.GetDirectoryName(subtitle)!,Path.GetFileNameWithoutExtension(subtitle)+"."+Guid.NewGuid().ToString("N")+ext);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(4));
        var start=new ProcessStartInfo(tool){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(tool)!};
        foreach(var a in new[]{video,subtitle,output})start.ArgumentList.Add(a);if(audio)start.ArgumentList.Add("--audio");
        using var process=new Process{StartInfo=start};
        try{
            process.Start();try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch(System.ComponentModel.Win32Exception){}var stdout=process.StandardOutput.ReadToEndAsync(timeout.Token);var stderr=process.StandardError.ReadToEndAsync(timeout.Token);
            try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
            var json=await stdout;await stderr;
            if(process.ExitCode!=0||!File.Exists(output))return ("timing needs manual adjustment",null,1);
            using var result=JsonDocument.Parse(json.Trim());
            if(!result.RootElement.TryGetProperty("accepted",out var ok)||ok.ValueKind!=JsonValueKind.True)return ("timing uncertain · original kept",null,1);
            var offset=result.RootElement.GetProperty("offset").GetDouble();var scale=result.RootElement.GetProperty("scale").GetDouble();
            if(!AcceptTiming(offset,scale)||!ValidText(await File.ReadAllTextAsync(output,ct),ext))return ("timing uncertain · original kept",null,1);
            if(backup!=null&&!File.Exists(backup))File.Copy(subtitle,backup,false);File.Move(output,subtitle,true);
            return ("",offset,scale);
        }catch(OperationCanceledException)when(!ct.IsCancellationRequested){return ("timing check timed out · original kept",null,1);}
        catch(Exception e)when(e is IOException or JsonException or System.ComponentModel.Win32Exception){return ("timing check unavailable · original kept",null,1);}
        finally{if(File.Exists(output))File.Delete(output);}
    }
    public static bool AcceptTiming(double offset,double scale)=>double.IsFinite(offset)&&double.IsFinite(scale)&&Math.Abs(offset)<=30&&Math.Abs(scale-1)<=.05;
    public static bool ValidText(string text,string ext)=>text.Length is >10 and <4*1024*1024&&!text.TrimStart().StartsWith("<!DOCTYPE",StringComparison.OrdinalIgnoreCase)&&(ext is ".ass" or ".ssa"?text.Contains("[Events]",StringComparison.OrdinalIgnoreCase):Regex.IsMatch(text,@"\d{1,2}:\d{2}(?::\d{2})?[,.]\d{3}\s+-->\s+\d{1,2}:\d{2}"));
    static bool ValidUrl(string link)=>Uri.TryCreate(link,UriKind.Absolute,out var u)&&u.Scheme=="https"&&u.UserInfo.Length==0&&!u.IsLoopback;
    async Task<byte[]> Download(string link,CancellationToken ct)
    {
        if(!ValidUrl(link))throw new IOException("Invalid subtitle link");using var r=await http.GetAsync(link,HttpCompletionOption.ResponseHeadersRead,ct);r.EnsureSuccessStatusCode();var bytes=await Limited(r.Content,4*1024*1024,ct);
        if(bytes.Length>2&&bytes[0]==0x1f&&bytes[1]==0x8b){using var gzip=new GZipStream(new MemoryStream(bytes),CompressionMode.Decompress);using var data=new MemoryStream();var buffer=new byte[8192];int n;while((n=await gzip.ReadAsync(buffer,ct))>0){if(data.Length+n>4*1024*1024)throw new IOException("Subtitle too large");data.Write(buffer,0,n);}bytes=data.ToArray();}
        return bytes;
    }
    public static string Decode(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if(bytes.Length>2&&bytes[0]==0xff&&bytes[1]==0xfe)return Encoding.Unicode.GetString(bytes,2,bytes.Length-2);
        if(bytes.Length>2&&bytes[0]==0xfe&&bytes[1]==0xff)return Encoding.BigEndianUnicode.GetString(bytes,2,bytes.Length-2);
        try{return new UTF8Encoding(false,true).GetString(bytes).TrimStart('\ufeff');}catch(DecoderFallbackException){return Encoding.GetEncoding(1256).GetString(bytes);}
    }
    static async Task<byte[]> Limited(HttpContent content,int max,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(35));ct=timeout.Token;
        if(content.Headers.ContentLength>max)throw new IOException("Subtitle response too large");await using var input=await content.ReadAsStreamAsync(ct);using var output=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await input.ReadAsync(buffer,ct))>0){if(output.Length+count>max)throw new IOException("Subtitle response too large");output.Write(buffer,0,count);}return output.ToArray();
    }
}
