using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Noir;

static class SubtitleAddonChecks
{
    sealed class Provider : HttpMessageHandler
    {
        public List<Uri> Requests=[];public string Manifest="{\"name\":\"SubDL\",\"resources\":[\"subtitles\"]}";public bool FailCustom,Large;public string? Results;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var uri=request.RequestUri!;Requests.Add(uri);var custom=uri.Host=="addon.example";
            if(custom&&FailCustom)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var content=uri.AbsolutePath.EndsWith("/manifest.json")?Manifest:uri.AbsolutePath.Contains("/subtitles/")?Results??(Large?JsonSerializer.Serialize(new{subtitles=Enumerable.Range(0,501).Select(i=>new{id=i.ToString(),lang="english",url="https://subs.example/"+(custom?"custom":"default")+i})}):JsonSerializer.Serialize(new{subtitles=new object[]{
                new{id=custom?"custom-duplicate":"default-duplicate",lang=custom?"English":"eng",url="https://subs.example/shared.srt"},
                new{id=custom?"custom":"default",lang="EN",url="https://subs.example/"+(custom?"custom":"default")+".srt"},
                new{id="ar",lang="Arabic",url="https://subs.example/ar.srt"},
                new{id="wrong-episode",lang="en",url="https://subs.example/wrong.srt",season=9,episode=9},
                new{id="unsafe",lang="en",url="http://subs.example/unsafe.srt"}}})):"1\n00:00:01,000 --> 00:00:04,000\nHello custom source";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(content)});
        }
    }
    public static async Task Run(string root,Action<bool,string> check)
    {
        const string link="https://addon.example/private-token";
        foreach(var input in new[]{link+"/manifest.json",link,link+"/", "stremio://addon.example/private-token/manifest.json","https://ADDON.example:443/private-token/manifest.json"})check(SubtitleAddon.Normalize(input)==link,"Subtitle addon normalizes "+(input.StartsWith("stremio")?"Stremio scheme":"HTTPS base or manifest"));
        check(SubtitleAddon.Normalize(" ")=="","Empty subtitle addon keeps the default source");
        foreach(var input in new[]{"http://addon.example/manifest.json","https://addon.example:8443/manifest.json","https://user:secret@addon.example/manifest.json","https://addon.example/manifest.json?token=secret","https://addon.example/#fragment","https://localhost/manifest.json"}){
            bool rejected=false;try{SubtitleAddon.Normalize(input);}catch(IOException){rejected=true;}check(rejected,"Subtitle addon rejects insecure, ambiguous or credentialed links");
        }
        using var provider=new Provider();using var http=new HttpClient(provider);var profile=Path.Combine(root,"subtitle-addon-"+Guid.NewGuid().ToString("N"));
        var settings=(Url:link,Only:true);var engine=new SubtitleDownloads(profile,http,()=>settings);var item=new VideoItem{Id="addon",ImdbId="tt123",Season=1,Episode=2,Path=Path.Combine(profile,"video.mkv")};
        var results=await engine.Search(item,"release.mkv",CancellationToken.None,false);
        check(provider.Requests.Count==1&&provider.Requests[0].Host=="addon.example"&&provider.Requests[0].AbsolutePath.StartsWith("/private-token/subtitles/series/tt123%3A1%3A2/",StringComparison.OrdinalIgnoreCase),"Custom only searches just the configured subtitle addon and preserves its config path");
        check(results.Count==3&&results[0].Id=="custom-duplicate"&&results.Any(s=>s.Language=="ar"),"Subtitle addons accept full language names and missing optional fields, reject unsafe and mismatched results");
        settings.Only=false;provider.Requests.Clear();results=await engine.Search(item,"release.mkv",CancellationToken.None,false);
        check(provider.Requests.Select(u=>u.Host).SequenceEqual(new[]{"addon.example"}),"Mixed subtitle mode uses the custom addon first without unnecessary fallback when both languages exist");
        check(results.Count==3&&results.Select(s=>s.Url).Distinct().Count()==3&&results[0].Id=="custom-duplicate", "Mixed subtitle mode deduplicates URLs and retains custom priority");
        provider.FailCustom=true;provider.Requests.Clear();results=await engine.Search(item,"release.mkv",CancellationToken.None,false);check(results.Count==3&&provider.Requests.Count==2,"Mixed subtitle mode still returns default results when the custom addon fails");
        settings.Only=true;provider.Requests.Clear();bool failed=false;try{await engine.Search(item,"release.mkv",CancellationToken.None,false);}catch(IOException){failed=true;}check(failed&&provider.Requests.Count==1,"Exclusive subtitle mode never falls back when its addon fails");provider.FailCustom=false;
        settings.Url="";provider.Requests.Clear();await engine.Search(item,"release.mkv",CancellationToken.None,false);check(provider.Requests.Count==1&&provider.Requests[0].Host=="opensubtitles-v3.strem.io","No custom link retains the default OpenSubtitles search");
        settings=(link,true);File.WriteAllBytes(item.Path,new byte[131072]);provider.Requests.Clear();await engine.Fetch(item,"release.mkv",["en"],false,CancellationToken.None,[new("en","pack.en.srt","https://pack.example/pack.en.srt")]);
        check(provider.Requests.All(u=>u.Host!="pack.example"&&u.Host!="opensubtitles-v3.strem.io")&&File.Exists(Path.Combine(profile,"video.en.srt")),"Exclusive subtitle fetch ignores bundled pack subtitles and downloads from the addon");
        check(await SubtitleAddon.Check(link,CancellationToken.None,http)=="SubDL"&&provider.Requests[^1].AbsolutePath=="/private-token/manifest.json","Subtitle manifest validation accepts string resources and returns addon name");
        provider.Manifest="{\"name\":\"SubSource\",\"resources\":[{\"name\":\"subtitles\",\"types\":[\"series\"]}]}";
        check(await SubtitleAddon.Check(link,CancellationToken.None,http)=="SubSource","Subtitle manifest validation accepts object resources");
        foreach(var manifest in new[]{"{\"resources\":[\"stream\"]}","{\"resources\":[{\"name\":\"stream\"}]}","{}","[]","not json",new string('x',256*1024+1)}){
            provider.Manifest=manifest;bool rejected=false;try{await SubtitleAddon.Check(link,CancellationToken.None,http);}catch(IOException){rejected=true;}check(rejected,"Subtitle manifest validation rejects missing subtitle resources, invalid or oversized responses");
        }
        provider.Results=JsonSerializer.Serialize(new{subtitles=Enumerable.Range(0,501).Select(i=>new{id=i.ToString(),lang="english",url="https://subs.example/"+i})});
        check((await engine.Search(item,"release",CancellationToken.None,false)).Count==500,"Subtitle addons retain the 500-entry response cap");
        provider.Results=null;provider.Large=true;settings.Only=false;var large=await engine.Search(item,"release",CancellationToken.None,false);
        check(large.Count==500&&large.All(s=>s.Url.Contains("/custom")),"Merged subtitle results retain the 500-entry cap and custom priority on ties");
        var store=new LibraryStore(Path.Combine(profile,"settings"));
        using(var downloads=new PcDownloads(store,start:false)){downloads.ConfigureSubtitleSource(link+"/manifest.json",false);check(downloads.Config.SubtitleAddonUrl==link&&!downloads.Config.SubtitleAddonOnly,"PC subtitle source saves independently of provider credentials");}
        using(var downloads=new PcDownloads(store,start:false)){check(downloads.Config.SubtitleAddonUrl==link&&!downloads.Config.SubtitleAddonOnly,"PC encrypted subtitle settings survive reopening");downloads.ConfigureSubtitleSource("",true);check(downloads.Config.SubtitleAddonUrl.Length==0,"PC custom subtitle source can be cleared");}
        check(new DownloadCredentials().SubtitleAddonOnly&&new PhoneDownloadSettingsPayload().SubtitleAddonOnly,"New and older settings default subtitle exclusive mode to on");
    }
}
