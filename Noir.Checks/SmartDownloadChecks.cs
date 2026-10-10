using Noir;
using System.Net;
using System.Text;
using System.Text.Json;

static class SmartDownloadChecks
{
    public static async Task Run(string parent,Action<bool,string> check)
    {
        var folder=Path.Combine(parent,"smart-shows");Directory.CreateDirectory(folder);
        var store=new LibraryStore(Path.Combine(parent,"smart-profile"));store.State.Roots=[new(){Path=folder}];store.State.Catalog.Clear();
        var existing=Path.Combine(folder,"Smart.S01E01.mkv");File.WriteAllBytes(existing,[1,2,3]);
        var item=new VideoItem{Id="smart1",Title="Smart",CollectionKey="smart",Season=1,Episode=1,Year=2020,ImdbId="tt123",Path=existing};store.State.Catalog[item.Id]=new(){Item=item};
        var deleted=new VideoItem{Id="smart2",Title="Smart",CollectionKey="smart",Season=1,Episode=2,Year=2020,ImdbId="tt123",Available=false};store.State.Catalog[deleted.Id]=new(){Item=deleted};store.Mark(deleted,true);
        store.State.EpisodeSchedules[item.GroupKey]=Enumerable.Range(1,5).Select(n=>new KnownEpisode{Season=1,Episode=n,Title="Episode "+n,AirsAt=n==5?DateTimeOffset.UtcNow.AddDays(2):DateTimeOffset.UtcNow.AddDays(-1)}).ToList();store.Save();
        var provider=new SeasonProvider();using var manager=new PcDownloads(store,provider,start:false);manager.Configure("https://index.invalid/manifest.json","fixture",folder,"",subtitleLanguages:[]);
        var results=await manager.SearchDetailed(item.Id,true);var plan=await manager.Plan(item.Id,results.Choices.Select(c=>c.Id).ToList());
        check(plan.ScheduleKnown&&plan.ReleasedEpisodes==4&&plan.ExistingEpisodes==1&&plan.Entries.Select(e=>e.File.Episode).Order().SequenceEqual(new[]{2,3,4}),"Season planner skips local episodes, includes watched-but-deleted episodes and excludes future episodes");
        check(plan.Entries.Select(e=>e.ReleaseId).Distinct().Count()==2&&plan.SelectedBytes==24576&&plan.MissingEpisodes.Count==0,"A season plan combines a partial pack with individual gap coverage and counts selected bytes");
        var request=new DownloadRequest{RequestId=Guid.NewGuid().ToString(),Selections=plan.Entries.GroupBy(e=>e.ReleaseId).Select(g=>new DownloadSelection{ReleaseId=g.Key,FileIds=g.Select(e=>e.File.Id).ToList()}).ToList()};
        var receipt=manager.Enqueue(request);check(receipt.Count==3&&manager.Enqueue(request).SequenceEqual(receipt),"Multi-release season commands are atomic and idempotent");
        var after=await manager.Plan(item.Id,results.Choices.Select(c=>c.Id).ToList());check(after.Entries.Count==0&&after.QueuedEpisodes==3,"A second plan skips already queued episodes");
        var unverified=await manager.Files(results.Choices.Last().Id);check(unverified.Any(f=>f.Name.Contains("E03-E04")&&!f.Selectable)&&unverified.Any(f=>f.Episode==5&&f.Future&&!f.Selectable),"Multi-episode and unreleased files cannot be silently renamed into a single episode");
        var unsure=unverified.Single(f=>f.MappingRequired);
        bool futureRejected=false;try{manager.MapFile(unsure.Id,5);}catch(IOException){futureRejected=true;}
        var confirmed=manager.MapFile(unsure.Id,4);
        bool multiRejected=false;try{manager.MapFile(unverified.Single(f=>f.Name.Contains("E03-E04")).Id,3);}catch(IOException){multiRejected=true;}
        check(futureRejected&&multiRejected&&confirmed.Episode==4&&confirmed.Queued&&!confirmed.Selectable&&!confirmed.MappingRequired,"Manual matching rejects future and multi-episode files and cannot duplicate an already queued episode");
        check(store.EverWatched(deleted),"Planning and queueing a deleted episode preserves its viewing history");
        var subFolder=Path.Combine(parent,"subtitles");Directory.CreateDirectory(subFolder);var video=Path.Combine(subFolder,"Test.S01E02.mkv");File.WriteAllBytes(video,new byte[131072]);
        var target=new VideoItem{Id="subtitle",Title="Test",ImdbId="tt123",Season=1,Episode=2,Path=video};
        var handler=new SubtitleHandler();var engine=new SubtitleDownloads(Path.Combine(parent,"sub-profile"),new HttpClient(handler));
        var candidates=await engine.Search(target,"Test.S01E02.WEB.1080p-GROUP.mkv",CancellationToken.None);
        check(candidates.First().HashMatch&&candidates.All(c=>c.Language is "en" or "ar")&&candidates.Count==3&&handler.HashRequested,"Subtitle lookup uses the video hash and episode identity, ranks release matches and rejects unrelated episodes/languages");
        var message=await engine.Fetch(target,"Test.S01E02.WEB.1080p-GROUP.mkv",["en","ar"],false,CancellationToken.None);
        var english=Path.ChangeExtension(video,"en.srt");var arabic=Path.ChangeExtension(video,"ar.srt");
        check(File.ReadAllText(english).Contains("Hello")&&File.ReadAllText(arabic).Contains("مرحبا")&&message.Contains("English saved")&&message.Contains("Arabic saved"),"English and Arabic subtitles download as UTF-8 sidecars beside the correct video");
        check(MobileSubtitles.Find(target).Count==2,"Both downloaded subtitle languages are available to paired phones");
        var saved=File.ReadAllText(arabic);await engine.Fetch(target,"Different release",["ar"],false,CancellationToken.None);check(File.ReadAllText(arabic)==saved&&handler.FileDownloads==2,"Existing subtitle sidecars are kept and are not downloaded repeatedly");
        check(!SubtitleDownloads.ValidText("<!DOCTYPE html><html>login</html>",".srt")&&SubtitleDownloads.ValidText(saved,".srt"),"Website error responses cannot masquerade as subtitles");
        check(SubtitleDownloads.AcceptTiming(2.5,25d/24)&&!SubtitleDownloads.AcceptTiming(90,1)&&!SubtitleDownloads.AcceptTiming(2,1.3)&&!SubtitleDownloads.AcceptTiming(double.NaN,1),"Automatic timing rejects implausible offsets, speed changes and invalid values");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);check(SubtitleDownloads.Decode(Encoding.GetEncoding(1256).GetBytes("مرحبا"))=="مرحبا","Legacy Arabic subtitle text is normalized without broken characters");
        using var metadata=new MetadataClient(parent,new IdentityHandler());var id=await metadata.DownloadIdentity(new(){Title="Smart",Year=2020});var ambiguous=await metadata.DownloadIdentity(new(){Title="Ambiguous",Year=2020});
        check(id=="tt123"&&ambiguous.Length==0,"Automatic IMDb matching accepts only an exact unique title/year and rejects ambiguous titles");
        await FairQueue(parent,check);
    }
    sealed class SeasonProvider : IDownloadProvider
    {
        public Task<List<IndexedRelease>> Search(VideoItem item,CancellationToken ct)=>Task.FromResult(new List<IndexedRelease>{new(new(){Id="pack",Name="1080p pack",Resolution="1080p",Cached=true},new string('a',40)),new(new(){Id="gap",Name="1080p gap pack",Resolution="1080p",Cached=true},new string('b',40))});
        public Task<List<TorboxFile>> Files(string hash,CancellationToken ct)=>Task.FromResult(hash[0]=='a'?new List<TorboxFile>{new(1,"Smart.S01E01.mkv",8192),new(2,"Smart.S01E02.mkv",8192)}:new List<TorboxFile>{new(3,"Smart.S01E03.mkv",8192),new(4,"Smart.S01E04.mkv",8192),new(5,"Smart.S01E05.mkv",8192),new(6,"Smart.S01E03-E04.mkv",16384),new(7,"Unknown.mkv",8192)});
        public Task<TorboxTorrent> Prepare(string hash,long id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<string> Link(long id,long file,CancellationToken ct)=>throw new NotSupportedException();
    }
    sealed class SubtitleHandler : HttpMessageHandler
    {
        public bool HashRequested;public int FileDownloads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            string body;if(request.RequestUri!.Host=="opensubtitles-v3.strem.io"){
                HashRequested=request.RequestUri.AbsoluteUri.Contains("videoHash=")&&request.RequestUri.AbsoluteUri.Contains("tt123");
                body=JsonSerializer.Serialize(new{subtitles=new[]{new{id="exact",lang="eng",url="https://subtitle.invalid/en",subtitleFileName="Test.en.srt",movieReleaseName="Other",m="h",season=1,episode=2},new{id="similar",lang="eng",url="https://subtitle.invalid/other",subtitleFileName="Test.en.srt",movieReleaseName="Test WEB GROUP",m="i",season=1,episode=2},new{id="arabic",lang="ara",url="https://subtitle.invalid/ar",subtitleFileName="Test.ar.srt",movieReleaseName="Test WEB GROUP",m="i",season=1,episode=2},new{id="wrong",lang="ara",url="https://subtitle.invalid/wrong",subtitleFileName="Test.ar.srt",movieReleaseName="Test",m="i",season=1,episode=1},new{id="de",lang="ger",url="https://subtitle.invalid/de",subtitleFileName="Test.de.srt",movieReleaseName="Test",m="i",season=1,episode=2}}});
            }else{FileDownloads++;body="1\n00:00:01,000 --> 00:00:03,000\n"+(request.RequestUri.AbsolutePath=="/ar"?"مرحبا":"Hello")+"\n";}
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8)});
        }
    }
    sealed class IdentityHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var name=request.RequestUri!.AbsoluteUri.Contains("Ambiguous")?"Ambiguous":"Smart";
            var body=request.RequestUri.Host=="api.tvmaze.com"?"[]":JsonSerializer.Serialize(new{metas=name=="Ambiguous"?new[]{new{id="tt1",name,releaseInfo="2020"},new{id="tt2",name,releaseInfo="2020"}}:new[]{new{id="tt123",name,releaseInfo="2020"}}});
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
        }
    }
    static async Task FairQueue(string parent,Action<bool,string> check)
    {
        var root=Path.Combine(parent,"fair-media");Directory.CreateDirectory(root);var store=new LibraryStore(Path.Combine(parent,"fair-profile"));store.State.Roots=[new(){Path=root}];store.State.Catalog.Clear();
        var item=new VideoItem{Id="fair",CollectionKey="fair",Title="Fair",Season=1,Episode=1,ImdbId="tt123",Available=false};store.State.Catalog[item.Id]=new(){Item=item};store.State.EpisodeSchedules[item.GroupKey]=[new(){Season=1,Episode=1},new(){Season=1,Episode=2}];store.Save();
        using var manager=new PcDownloads(store,new PreparingProvider(),new HttpClient(new ByteHandler()));manager.Configure("https://index.invalid/manifest.json","fixture",root,"",subtitleLanguages:[]);
        var release=await manager.Search(item.Id,true);var selections=new List<DownloadSelection>();foreach(var r in release){var files=await manager.Files(r.Id);selections.Add(new(){ReleaseId=r.Id,FileIds=files.Where(f=>f.Selectable).Select(f=>f.Id).ToList()});}manager.Enqueue(new(){RequestId=Guid.NewGuid().ToString(),Selections=selections});
        for(int n=0;n<100&&!manager.Jobs().Any(j=>j.Status=="Completed");n++)await Task.Delay(50);
        check(manager.Jobs().Any(j=>j.Status=="Completed"&&j.Target.Episode==2)&&manager.Jobs().Any(j=>j.Status=="Preparing"&&j.Target.Episode==1),"A release still preparing on TorBox cannot block a ready episode in the PC queue");
    }
    sealed class PreparingProvider : IDownloadProvider
    {
        public Task<List<IndexedRelease>> Search(VideoItem i,CancellationToken ct)=>Task.FromResult(new List<IndexedRelease>{new(new(){Id="slow",Name="Slow"},new string('a',40)),new(new(){Id="ready",Name="Ready"},new string('b',40))});
        public Task<List<TorboxFile>> Files(string h,CancellationToken ct)=>Task.FromResult(new List<TorboxFile>{new(1,h[0]=='a'?"Fair.S01E01.mkv":"Fair.S01E02.mkv",8192)});
        public Task<TorboxTorrent> Prepare(string h,long id,CancellationToken ct)=>Task.FromResult(new TorboxTorrent(h[0]=='a'?1:2,h,h[0]=='b',[new(1,h[0]=='a'?"Fair.S01E01.mkv":"Fair.S01E02.mkv",8192)]));
        public Task<string> Link(long id,long file,CancellationToken ct)=>Task.FromResult("https://cdn.invalid/file");
    }
    sealed class ByteHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[8192])});
    }
}
