using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;
// Streaming through AIOStreams: the same stream list Stremio shows. Each entry's link already goes
// through the user's debrid or usenet service, so Noir hands the best ready one straight to VLC and
// keeps its own resume, history, intro skip and Up next.
public sealed record StreamOption(string Url,string Name,string Detail,string Resolution,long Size,bool Cached,string Service,string UserAgent,string Referrer);
public static class StreamSources
{
    // The user's own Stremio profile streams best; a Noir download profile (if any) is the fallback.
    public static string Manifest(DownloadCredentials c)=>c.OriginalManifest.Length>0?c.OriginalManifest:c.Manifest;
    public static async Task<List<StreamOption>> Find(DownloadCredentials c,VideoItem item,HttpClient http,CancellationToken ct)
    {
        if(Manifest(c).Length==0)throw new IOException("Add your AIOStreams link in Settings → Downloads to stream.");
        if(!Regex.IsMatch(item.ImdbId,@"^tt\d+$"))throw new IOException("This title needs a verified match before it can stream.");
        using var request=new HttpRequestMessage(HttpMethod.Get,TorboxProvider.StreamUri(Manifest(c),item));
        request.Headers.UserAgent.ParseAdd("AIO-Noir/0.15.1");
        HttpResponseMessage received;
        try{received=await http.SendAsync(request,ct);}catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new IOException("AIOStreams took too long. Try again shortly.");}catch(HttpRequestException){throw new IOException("Couldn't connect to AIOStreams.");}
        using var response=received;if(!response.IsSuccessStatusCode)throw new IOException($"AIOStreams couldn't find streams ({(int)response.StatusCode}).");
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return Parse(doc.RootElement,c);
    }
    public static List<StreamOption> Parse(JsonElement root,DownloadCredentials c)
    {
        var found=new List<StreamOption>();
        if(!root.TryGetProperty("streams",out var streams)||streams.ValueKind!=JsonValueKind.Array)return found;
        foreach(var s in streams.EnumerateArray())
        {
            if(s.ValueKind!=JsonValueKind.Object)continue;
            var url=Text(s,"url");if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http"))continue;
            var data=s.TryGetProperty("streamData",out var d)&&d.ValueKind==JsonValueKind.Object?d:default;
            var type=Text(data,"type");if(type is "error" or "p2p" or "live")continue;
            var hints=s.TryGetProperty("behaviorHints",out var h)&&h.ValueKind==JsonValueKind.Object?h:default;
            // VLC can send a user agent and referrer, nothing else; streams needing other headers are skipped.
            string agent="",referrer="";var unsupported=false;
            if(hints.ValueKind==JsonValueKind.Object&&hints.TryGetProperty("proxyHeaders",out var proxy)&&proxy.ValueKind==JsonValueKind.Object&&proxy.TryGetProperty("request",out var headers)&&headers.ValueKind==JsonValueKind.Object)
                foreach(var header in headers.EnumerateObject()){
                    if(header.Value.ValueKind!=JsonValueKind.String){unsupported=true;continue;}
                    var value=header.Value.GetString()??"";if(value.Contains('\r')||value.Contains('\n')){unsupported=true;continue;}
                    if(header.Name.Equals("User-Agent",StringComparison.OrdinalIgnoreCase))agent=value;else if(header.Name.Equals("Referer",StringComparison.OrdinalIgnoreCase))referrer=value;else unsupported=true;
                }
            if(unsupported)continue;
            var name=Text(hints,"filename");if(name.Length==0)name=Text(s,"name");
            var detail=Regex.Replace(Text(s,"description") is {Length:>0} desc?desc:Text(s,"title"),@"\[NOIR_[A-Z]+:[^\]\r\n]*\]","").Trim();
            long size=hints.ValueKind==JsonValueKind.Object&&hints.TryGetProperty("videoSize",out var bytes)&&bytes.ValueKind==JsonValueKind.Number&&bytes.TryGetInt64(out var n)?n:0;
            if(size==0&&data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("size",out var ds)&&ds.ValueKind==JsonValueKind.Number&&ds.TryGetInt64(out var dn))size=dn;
            var service=data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("service",out var sv)&&sv.ValueKind==JsonValueKind.Object?sv:default;
            var label=Text(s,"name")+" "+detail;
            var cached=service.ValueKind==JsonValueKind.Object&&service.TryGetProperty("cached",out var ca)?ca.ValueKind==JsonValueKind.True:label.Contains('⚡')||Regex.IsMatch(label,@"(?i)\[(?:TB|RD|AD|PM|DL|ED|OC|PKP|TRB)\+\]|\bcached\b")||type is "http" or "usenet"&&!label.Contains('⏳');
            var resolution=TorboxProvider.Resolution(name+" "+label);
            found.Add(new(url,DownloadSecurity.SafeText(Clean(name),c),DownloadSecurity.SafeText(Clean(detail),c),resolution,size,cached,Text(service,"id"),agent,referrer));
        }
        return found;
    }
    // Ready first, then a size that streams comfortably (remuxes need 30+ Mbps), then the preferred
    // quality; ties keep AIOStreams' own order, which reflects the user's sorting rules.
    public static List<StreamOption> Rank(IEnumerable<StreamOption> options,string preferred,bool movie=false)
    {
        int Quality(StreamOption o)=>o.Resolution==preferred?0:preferred=="Any"?0:o.Resolution.Length==0?3:Math.Abs(Order(o.Resolution)-Order(preferred));
        static int Order(string r)=>r switch{"2160p"=>4,"1080p"=>3,"720p"=>2,"480p"=>1,_=>3};
        var comfortable=(movie?25L:6L)*1024*1024*1024;
        bool Heavy(StreamOption o)=>o.Size>comfortable||Regex.IsMatch(o.Name,@"(?i)\bremux\b");
        return options.OrderByDescending(o=>o.Cached).ThenBy(Heavy).ThenBy(Quality).ToList();
    }
    static string Clean(string s)=>Regex.Replace(Regex.Replace(s,@"https?://\S+","[link]"),@"\s+"," ").Trim();
    static string Text(JsonElement e,string name)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
}
