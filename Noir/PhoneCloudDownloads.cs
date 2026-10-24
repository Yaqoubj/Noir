using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Noir;

// Descriptors are revocable, peer-bound capabilities. Server credentials stay behind the proxy.
public sealed class PhoneCloudDownloads(LibraryStore store,PcDownloads downloads) : IDisposable
{
    sealed record Selection(string Peer,VideoItem Item,IndexedRelease Release,string Service,List<TorboxFile> Files,DateTimeOffset Until);
    sealed record Content(string Peer,VideoItem Item,DirectDownloadSource? Source,DateTimeOffset Until);
    readonly ConcurrentDictionary<string,Selection> releases=new();
    readonly ConcurrentDictionary<string,Content> contents=new();
    readonly HttpClient http=DownloadSecurity.Client(TimeSpan.FromSeconds(45));
    static string Id()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    VideoItem? Item(string id)=>store.Archive.FirstOrDefault(i=>i.Id==id)??store.Archive.Select(i=>i.GroupKey).Distinct().SelectMany(store.EpisodesFor).FirstOrDefault(i=>i.Id==id);
    DownloadCredentials For(string service){var c=downloads.Config;c.Key=service=="Real-Debrid"?(c.RealDebridKey.Length>0?c.RealDebridKey:c.Service==service?c.Key:""):(c.TorBoxKey.Length>0?c.TorBoxKey:c.Service=="TorBox"?c.Key:"");c.Service=service;return c;}
    IDownloadProvider Provider(string service)=>new DownloadProviders(()=>For(service),http).ForService(service);
    public async Task<object> Search(string id,string peer,CancellationToken ct)
    {
        Prune();var item=Item(id)??throw new IOException("Unknown title.");var config=downloads.Config;var found=await new DownloadProviders(()=>downloads.Config,http).Discover(item,false,[item],null,ct);var result=new List<object>();
        foreach(var release in found.Releases){
            // Each release is inspected and later prepared through the service that found it.
            var service=release.Service.Length>0?release.Service:config.Service;List<TorboxFile> files;
            try{files=release.Direct is {} direct?direct.Select((f,n)=>new TorboxFile(n,f.Name,f.Size)).ToList():await Provider(service).Files(release.Hash,ct);}
            catch(IOException){continue;}
            var token=Id();releases[token]=new(peer,item,release,service,files,DateTimeOffset.UtcNow.AddMinutes(10));
            foreach(var f in files.Where(f=>DownloadNaming.Video(f.Name))){var number=DownloadNaming.Episode(f.Name,item.Season);if(!item.IsMovie&&number.Episode>0&&(number.Season!=item.Season||number.Episode!=item.Episode))continue;if(!item.IsMovie&&number.Episode==0&&files.Count>1)continue;
                var source=release.Direct is {} d&&f.Id>=0&&f.Id<d.Count?d[(int)f.Id]:null;
                result.Add(new{service="pc",itemId=id,releaseId=token,fileId=f.Id.ToString(),name=release.Choice.Name,size=f.Size,resolution=source is {Resolution.Length:>0}?source.Resolution:release.Choice.Resolution,language=source is {} s&&s.Language!="Unknown"?s.Language:release.Choice.Languages});}
        }return new{choices=result};
    }
    public async Task<object> Prepare(PhonePrepare input,string peer,CancellationToken ct)
    {
        Prune();var item=Item(input.ItemId)??throw new IOException("Unknown title.");DirectDownloadSource? source=null;
        if(input.ReleaseId.Length>0){
            if(!releases.TryGetValue(input.ReleaseId,out var r)||r.Peer!=peer||r.Until<=DateTimeOffset.UtcNow||r.Item.Id!=item.Id||!long.TryParse(input.FileId,out var fileId))throw new IOException("Search choices again.");
            if(r.Release.Direct is {} direct){if(fileId<0||fileId>=direct.Count)throw new IOException("Unknown file.");source=direct[(int)fileId];}
            else {var provider=Provider(r.Service);var torrent=await provider.Prepare(r.Release.Hash,-1,ct);if(!torrent.Ready)throw new IOException("Cloud file is preparing. Retry shortly.");
                // Cached file IDs and owned-torrent file IDs can differ. Match verified name/size again, like the PC worker.
                var chosen=r.Files.FirstOrDefault(f=>f.Id==fileId)??throw new IOException("Unknown file.");
                static string Norm(string n)=>n.Replace('\\','/');
                var matches=torrent.Files.Where(f=>Norm(f.Name)==Norm(chosen.Name)&&(chosen.Size<=0||f.Size==chosen.Size)).ToList();
                if(matches.Count==0)matches=torrent.Files.Where(f=>Norm(f.Name).Split('/').Last()==Norm(chosen.Name).Split('/').Last()&&(chosen.Size<=0||f.Size==chosen.Size)).ToList();
                if(matches.Count!=1)throw new IOException("The service's file list changed. Search again.");
                var file=matches[0];source=new(){Url=await provider.Link(torrent.Id,file.Id,ct),Name=file.Name,Size=file.Size};}
        }else if(!File.Exists(item.Path))throw new IOException("No file is available on this PC.");
        var token=Id();var expires=DateTimeOffset.UtcNow.AddMinutes(10);contents[token]=new(peer,item,source,expires);
        return new{url="/v1/phone-downloads/content/"+token,size=source?.Size??new FileInfo(item.Path).Length,fileName=source?.Name??Path.GetFileName(item.Path),itemId=item.Id,expiresAt=expires};
    }
    public async Task Send(string token,string peer,HttpContext ctx)
    {
        if(!contents.TryGetValue(token,out var content)||content.Peer!=peer||content.Until<=DateTimeOffset.UtcNow){ctx.Response.StatusCode=404;return;}
        if(content.Source==null){if(!File.Exists(content.Item.Path)){ctx.Response.StatusCode=404;return;}await Results.File(content.Item.Path,"application/octet-stream",lastModified:File.GetLastWriteTimeUtc(content.Item.Path),entityTag:new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\""+new FileInfo(content.Item.Path).Length+"-"+File.GetLastWriteTimeUtc(content.Item.Path).Ticks+"\""),enableRangeProcessing:true).ExecuteAsync(ctx);return;}
        var source=content.Source;var server=downloads.Config.Servers.FirstOrDefault(s=>s.Id==source.ServerId);var url=source.Url;var headers=source.Headers;
        for(var redirect=0;redirect<6;redirect++){
            if(!DownloadSecurity.ValidUrl(url,server?.BaseUrl))throw new IOException("Invalid source.");DownloadSecurity.ValidateHeaders(headers);
            using var request=new HttpRequestMessage(HttpMethod.Get,url);foreach(var h in headers)request.Headers.TryAddWithoutValidation(h.Key,h.Value);foreach(var name in new[]{"Range","If-Range"})if(ctx.Request.Headers.TryGetValue(name,out var value))request.Headers.TryAddWithoutValidation(name,value.ToString());request.Headers.TryAddWithoutValidation("Accept-Encoding","identity");
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ctx.RequestAborted);
            if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308){var next=response.Headers.Location;if(next==null)throw new IOException("Invalid redirect.");var target=new Uri(new Uri(url),next);if(new Uri(url).Scheme=="https"&&target.Scheme!="https")throw new IOException("Invalid redirect.");if(new Uri(url).Authority!=target.Authority)headers=[];url=target.AbsoluteUri;continue;}
            if((int)response.StatusCode is not (200 or 206 or 416)){ctx.Response.StatusCode=502;return;}
            ctx.Response.StatusCode=(int)response.StatusCode;ctx.Response.ContentType="application/octet-stream";if(response.Content.Headers.ContentLength is {} length)ctx.Response.ContentLength=length;
            foreach(var name in new[]{"ETag","Last-Modified","Content-Range","Accept-Ranges"}){var value=response.Headers.TryGetValues(name,out var values)?string.Join(",",values):response.Content.Headers.TryGetValues(name,out values)?string.Join(",",values):"";if(value.Length>0&&DownloadSecurity.SafeText(value,downloads.Config,DownloadSecurity.SourceSecrets(source))==value)ctx.Response.Headers[name]=value;}
            await response.Content.CopyToAsync(ctx.Response.Body,ctx.RequestAborted);return;
        }throw new IOException("Too many redirects.");
    }
    void Prune(){var now=DateTimeOffset.UtcNow;foreach(var entry in releases.Where(p=>p.Value.Until<=now))releases.TryRemove(entry.Key,out _);foreach(var entry in contents.Where(p=>p.Value.Until<=now))contents.TryRemove(entry.Key,out _);if(releases.Count>1000||contents.Count>1000)throw new IOException("Too many pending requests. Retry shortly.");}
    public void Dispose()=>http.Dispose();
}
public sealed class PhonePrepare {public string ItemId {get;set;}="";public string ReleaseId {get;set;}="";public string FileId {get;set;}="";}
