using Noir;
using System.Net;
using System.Text.Json;
static class UxChecks
{
    public static async Task Run(string parent,Action<bool,string> check)
    {
        using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(new{description="Version [NOIR_HASH:"+new string('a',40)+"]"})))check(TorboxProvider.Hash(doc.RootElement)==new string('a',40),"Explicit download-profile markers resolve wrapped AIOStreams links");
        using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(new{description="Version "+new string('a',40)})))check(TorboxProvider.Hash(doc.RootElement)=="","Arbitrary hash-like text is not guessed as a torrent reference");
        check(TorboxProvider.DiscoveryMessage(84,["Retired addon"]).StartsWith("84 releases found")&&TorboxProvider.DiscoveryMessage(0,["Retired addon"]).Contains("Retired addon"),"Working playback releases are distinguished from a total addon failure");
        var export="{\"uuid\":\"original\",\"presets\":[{\"type\":\"torbox-search\"},{\"type\":\"comet\"}],\"services\":[],\"formatter\":{\"id\":\"gdrive\"}}";
        var copy=AioProfiles.DownloadConfig(export);
        check(copy["uuid"]==null&&copy["presets"]!.AsArray().Count==1&&copy["formatter"]!["id"]!.ToString()=="custom"&&JsonDocument.Parse(export).RootElement.GetProperty("presets").GetArrayLength()==2,"Noir creates an independent metadata profile without mutating the Stremio export");
        using var http=new HttpClient(new ManyReleases());var provider=new TorboxProvider(()=>new(){Manifest="https://fixture.invalid/stremio/profile/manifest.json",Key="fixture"},http);
        var releases=await provider.Search(new(){Title="Test",ImdbId="tt123",Season=1,Episode=1},CancellationToken.None);
        check(releases.Count==3000&&releases.All(r=>r.Choice.SourceEntries==2&&r.Choice.Sources.Count==2),"Hundreds of duplicate indexer entries collapse only by verified torrent identity");
        check(releases.All(r=>!r.Choice.Detail.Contains("NOIR_")&&r.Choice.Warning.Length==0),"Download markers are hidden and duplicate sources do not produce a false conversion warning");
        var groups=ReleasePresentation.Group(releases.Select(r=>r.Choice),"1080p");
        check(groups.Count==2&&groups.Sum(g=>g.Choices.Count)==3000&&groups.Sum(g=>g.SourceEntries)==6000&&groups[0].Quality=="1080p","Quality groups retain every distinct release and original source count");
        var sessionStore=new LibraryStore(Path.Combine(parent,"large-search-sessions"));var sessionItem=new VideoItem{Id="session",Title="Test",ImdbId="tt123",Season=1,Episode=1};sessionStore.State.Catalog[sessionItem.Id]=new(){Item=sessionItem};sessionStore.State.Roots.Add(new(){Path=parent,Movies=false});sessionStore.State.Roots.Add(new(){Path=parent,Movies=true});
        using(var downloads=new PcDownloads(sessionStore,provider,start:false)){
            downloads.Configure("https://fixture.invalid/stremio/profile/manifest.json","fixture",parent,parent);
            var first=await downloads.SearchDetailed(sessionItem.Id,false);var second=await downloads.SearchDetailed(sessionItem.Id,false);
            check(first.Choices.Count==3000&&second.Choices.Count==3000,"Repeated searches retain thousands of choices without triggering the old session limit");
        }
        var options=new[]{new DownloadChoice{Id="unknown",Size=0,Resolution="1080p"},new DownloadChoice{Id="large",Size=500,Resolution="1080p"},new DownloadChoice{Id="ready",Size=80,Resolution="1080p",CacheStatus="Ready"},new DownloadChoice{Id="pack",Size=500,SizeKind="Pack",Resolution="1080p"}};
        check(ReleasePresentation.Matches(options[0],"All","All",100,false)&&!ReleasePresentation.Matches(options[1],"All","All",100,false)&&ReleasePresentation.Matches(options[3],"All","All",100,false),"Size filters retain unknown sizes and never mistake a pack size for an episode size");
        check(ReleasePresentation.Recommend(options,"1080p",100).First().Id=="ready"&&ReleasePresentation.Matches(options[2],"1080p","All",100,true),"Suggested choices prefer known suitable sizes and TorBox readiness");
        var store=new LibraryStore(Path.Combine(parent,"ordered-saves"));var item=new VideoItem{Id="ordered",Title="Ordered",Season=1,Episode=1};
        for(int n=1;n<=80;n++)store.Record(item,n,100,deferSave:true);
        store.Record(item,91,100);await store.FlushPendingAsync();
        check(new LibraryStore(store.DataDirectory).GetProgress(item)?.Position==91,"A synchronous newer save cannot be overwritten by queued playback snapshots");
        var revision=store.GetProgress(item)!.UpdatedAt;store.Record(item,94,100,deferSave:true);
        check(!store.Record(item,40,100,expectedUpdatedAt:revision,deferSave:true),"Deferred playback persistence still rejects stale session progress");
        await store.FlushPendingAsync();check(new LibraryStore(store.DataDirectory).GetProgress(item)?.Position==94,"Flushing queued saves durably preserves the latest exact resume time");
        store.Record(item,96,100,true,deferSave:true);store.Record(item,97,100,true,deferSave:true);await store.FlushPendingAsync();
        var restored=new LibraryStore(store.DataDirectory);check(restored.ViewingHistory(item).Count==1&&restored.GetProgress(item)?.Completed==true,"Credits completion remains idempotent across background saves and restart");
    }
    sealed class ManyReleases:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var streams=Enumerable.Range(1,3000).SelectMany(n=>new[]{"First index","Second index"}.Select(source=>new{
                name="Same title",description="Version 1080p HEVC [NOIR_HASH:"+n.ToString("x40")+"][NOIR_SOURCE:"+source+"][NOIR_AUDIO:English]",
                url="https://fixture.invalid/playback/opaque",behaviorHints=new{filename="Same title "+(n%2==0?"1080p":"2160p")+".mkv",videoSize=1000L}})).ToList();
            var body=request.RequestUri!.Host=="fixture.invalid"?JsonSerializer.Serialize(new{streams}):"{\"success\":true,\"data\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
        }
    }
}
