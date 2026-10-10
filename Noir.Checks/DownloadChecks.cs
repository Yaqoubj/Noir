using Noir;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

static class DownloadChecks
{
    public static async Task Run(string parent,Action<bool,string> check)
    {
        var dir=Path.Combine(parent,"download-tests");var media=Path.Combine(dir,"shows");Directory.CreateDirectory(media);
        var store=new LibraryStore(Path.Combine(dir,"profile"));store.State.Roots=[new(){Path=media,Movies=false}];store.State.Catalog.Clear();
        var item=new VideoItem{Id="download-target",CollectionKey="show:test",Title="Test",Year=2026,Season=2,Episode=1,ImdbId="tt123",Available=false};store.State.Catalog[item.Id]=new(){Item=item};store.Save();
        check(DownloadNaming.Destination(media,item,"../../unsafe.mkv").EndsWith(Path.Combine("Test (2026)","Season 02","Test (2026) S02E01.mkv")),"Downloads ignore remote paths and use organized episode filenames");
        check(DownloadNaming.Episode("Test.S02E03.1080p.mkv",1)==(2,3)&&DownloadNaming.Episode("Test.2x04.mkv",1)==(2,4),"Download selection recognizes season/episode numbers");
        check(DownloadNaming.Episode("Test.1080p.mkv",2).Episode==0,"Ambiguous pack filenames are not guessed as episode numbers");
        var unsafePath=Path.Combine(media,"..","outside.mkv");bool refused=false;try{DownloadNaming.Validate(media,unsafePath);}catch(IOException){refused=true;}check(refused,"Downloads reject destinations outside library roots");
        using(var json=JsonDocument.Parse("{\"infoHash\":\""+new string('a',40)+"\"}"))check(TorboxProvider.Hash(json.RootElement)==new string('a',40),"AIOStreams raw torrent hashes can be resolved exclusively through TorBox");
        using(var json=JsonDocument.Parse("{\"behaviorHints\":{\"bingeGroup\":\"torbox|"+new string('b',40)+"\"}}"))check(TorboxProvider.Hash(json.RootElement)==new string('b',40),"TorBox addon hash hints work without exposing download URLs");
        using(var json=JsonDocument.Parse(JsonSerializer.Serialize(new {streamData=new{torrent=new{infoHash=new string('c',40)}}})))check(TorboxProvider.Hash(json.RootElement)==new string('c',40),"AIOStreams parsed metadata provides hashes for debrid results");
        using(var json=JsonDocument.Parse(JsonSerializer.Serialize(new {url="https://provider.invalid/play/config/"+new string('d',40)+"/3/Test.mkv"})))check(TorboxProvider.Hash(json.RootElement)==new string('d',40),"Wrapped Comet and StremThru file links resolve torrent identities");
        using(var json=JsonDocument.Parse(JsonSerializer.Serialize(new {url="https://provider.invalid/config/"+new string('e',40)+"/watch"})))check(TorboxProvider.Hash(json.RootElement)=="","Unrelated credential-like URL segments are not mistaken for torrent hashes");
        var apiConfig=new DownloadCredentials{Manifest="https://index.fixture.invalid/stremio/private/manifest.json",Key="private-fixture-key"};
        var accountHandler=new ProviderHandler();var accountProvider=new TorboxProvider(()=>apiConfig,new HttpClient(accountHandler));
        var accountPacks=await accountProvider.SearchSeason(item,CancellationToken.None);
        check(accountPacks.Count==1&&accountPacks[0].Choice.Cached&&accountHandler.IndexRequests==1,"Season search always checks AIOStreams even when the TorBox account contains a pack");
        var detailed=await accountProvider.Discover(item,true,[item],null,CancellationToken.None);
        check(detailed.Sources.Any(s=>s.Source=="AIOStreams"&&s.State=="Failed")&&detailed.Releases.Count==1,"TorBox results cannot hide a failed AIOStreams source");
        var selectedUri=TorboxProvider.StreamUri("https://index.fixture.invalid/stremio/private/manifest.json?variant=movies",item);
        check(selectedUri.Query=="?variant=movies"&&selectedUri.AbsolutePath.Contains("stream/series/"),"AIO request construction preserves configuration selectors");
        var accountFiles=await accountProvider.Files(accountPacks[0].Hash,CancellationToken.None);var accountTorrent=await accountProvider.Prepare(accountPacks[0].Hash,-1,CancellationToken.None);
        check(accountFiles.Count==2&&accountTorrent.Id==77&&accountHandler.Creates==0,"Existing TorBox torrents reuse their account file list and torrent ID without creating another download");
        var accountEpisodes=await accountProvider.Search(item,CancellationToken.None);
        check(accountEpisodes.Count==1&&accountHandler.MetadataRequested,"An unavailable AIO index still permits an existing TorBox episode and requests documented parsed metadata");
        var timeoutProvider=new TorboxProvider(()=>apiConfig,new HttpClient(new ProviderHandler{IndexTimeout=true}));var timed=await timeoutProvider.Discover(item,true,[item],null,CancellationToken.None);
        check(timed.Releases.Count==1&&timed.Sources.Single(s=>s.Source=="AIOStreams").State=="Failed"&&timed.Sources.Single(s=>s.Source=="AIOStreams").Message.Contains("timed out"),"A timed-out AIO source stays visible without discarding working TorBox account choices");
        var metadataHandler=new ProviderHandler{IncludeAccount=false,IndexError=false};var metadataProvider=new TorboxProvider(()=>apiConfig,new HttpClient(metadataHandler));
        check((await metadataProvider.Search(item,CancellationToken.None)).Count==1,"Debrid-only AIO results remain selectable from parsed metadata even with no direct infoHash");
        var errorProvider=new TorboxProvider(()=>apiConfig,new HttpClient(new ProviderHandler{IncludeAccount=false}));string actualError="";try{await errorProvider.Search(item,CancellationToken.None);}catch(IOException e){actualError=e.Message;}
        check(actualError.Contains("TorBox Search has been removed")&&!actualError.Contains("No torrent hashes")&&!actualError.Contains(apiConfig.Key)&&!actualError.Contains("https://"),"Noir reports the actual AIO error while redacting links and credentials");
        using(var account=JsonDocument.Parse(ProviderHandler.Torrent("Contest",2026)))check(!TorboxProvider.AccountMatch(item,account.RootElement,true),"TorBox account matching respects title word boundaries");
        using(var account=JsonDocument.Parse(ProviderHandler.Torrent("Test",2025)))check(!TorboxProvider.AccountMatch(item,account.RootElement,true),"TorBox account matching distinguishes dated remakes");
        using(var account=JsonDocument.Parse(ProviderHandler.Torrent("Test",2026)))check(!TorboxProvider.AccountMatch(new VideoItem{Title="Test",Season=3,Episode=1},account.RootElement,true),"Season choices never offer another season's episode files");
        using(var torboxOnly=new PcDownloads(store,accountProvider,start:false)){torboxOnly.Configure("",apiConfig.Key,media,"");check(torboxOnly.Configured&&(await torboxOnly.Search(item.Id,true)).Count==1,"The PC season command works with TorBox alone and dispatches the season provider");}
        using var manager=new PcDownloads(store,new FakeProvider(),start:false);manager.Configure("https://example.invalid/private/manifest.json","fixture-not-real",media,"",subtitleLanguages:[]);
        var encrypted=File.ReadAllBytes(Path.Combine(store.DataDirectory,"downloads-settings.dpapi"));check(!System.Text.Encoding.UTF8.GetString(encrypted).Contains("fixture-not-real"),"TorBox credentials are encrypted separately from library state");
        store.State.EpisodeSchedules[item.GroupKey]=[new(){Season=2,Episode=2,Title="Missing episode"}];store.Save();var virtualEpisode=store.EpisodesFor(item.GroupKey).Single(i=>i.Episode==2);var virtualReleases=await manager.Search(virtualEpisode.Id,false);var virtualFiles=await manager.Files(virtualReleases[0].Id);check(virtualFiles.Count(f=>f.Selectable)==1&&virtualFiles.Single(f=>f.Selectable).Episode==2,"Known episodes that were never on disk can find a release from their season page");
        var choices=await manager.Search(item.Id,true);var files=await manager.Files(choices[0].Id);
        check(files.Count(f=>f.Selectable)==2&&files.Any(f=>f.Season==3&&!f.Selectable),"Season downloads offer only verified episodes from the chosen season and exclude samples");
        var planned=await manager.Plan(item.Id,choices.Select(c=>c.Id).ToList());
        check(planned.Entries.Count==2&&planned.SelectedBytes==16384&&planned.MissingEpisodes.Count==0,"Season plans verify episode coverage and selected bytes before acquisition");
        var request=new DownloadRequest{RequestId=Guid.NewGuid().ToString(),ReleaseId=choices[0].Id,FileIds=files.Where(f=>f.Selectable).Select(f=>f.Id).ToList()};
        var first=manager.Enqueue(request);check(manager.Enqueue(request).SequenceEqual(first)&&manager.Jobs().Count==2,"Retried phone commands cannot duplicate a season download");
        manager.Control(first[0],"pause");check(manager.Jobs()[0].Status=="Paused","PC download pauses persist");
        using(var reloaded=new PcDownloads(store,new FakeProvider(),start:false))check(reloaded.Jobs().Count==2&&reloaded.Config.Key=="fixture-not-real","PC queue and encrypted setup survive restart");
        var visible=JsonSerializer.Serialize(manager.PublicJobs());check(!visible.Contains(media,StringComparison.OrdinalIgnoreCase)&&!visible.Contains("fixture-not-real")&&!visible.Contains("example.invalid"),"Phone queue contains no Windows paths, manifest URLs or TorBox credentials");
        var bytes=Enumerable.Range(0,8192).Select(n=>(byte)(n%251)).ToArray();var handler=new TransferHandler(bytes);using var http=new HttpClient(handler);using var downloader=new PcDownloads(store,new FakeProvider(),http,start:false);
        var job=manager.Jobs()[0];var part=Path.Combine(store.DataDirectory,"download-parts",job.Id+".part");File.WriteAllBytes(part,bytes[..777]);job.Size=bytes.Length;job.ETag="\"fixture\"";
        await downloader.Transfer(job,"https://cdn.example.invalid/file",CancellationToken.None);
        check(handler.Start==777&&File.ReadAllBytes(job.Destination).SequenceEqual(bytes),"PC downloads resume an exact partial file using ETag and validated ranges");
        check(!File.Exists(part)&&store.Archive.Any(i=>i.Id==item.Id&&i.Available&&i.Path==job.Destination),"Completed downloads atomically enter the local catalog without losing viewing identity");
        refused=false;try{await downloader.Transfer(job,"https://cdn.example.invalid/file",CancellationToken.None);}catch(IOException){refused=true;}check(refused&&File.ReadAllBytes(job.Destination).SequenceEqual(bytes),"Existing completed episode files are never overwritten");
        File.WriteAllText(Path.ChangeExtension(job.Destination,".en.srt"),"1\n00:00:01,000 --> 00:00:03,000\nHello\n");File.WriteAllText(Path.Combine(Path.GetDirectoryName(job.Destination)!,"Unrelated.srt"),"other");
        var subtitles=MobileSubtitles.Find(store.Archive.First(i=>i.Id==item.Id));check(subtitles.Count==1&&subtitles[0].Info.Name=="en"&&!JsonSerializer.Serialize(subtitles.Select(s=>s.Info)).Contains(media,StringComparison.OrdinalIgnoreCase),"Phone subtitle projection contains matching sidecars and opaque IDs without private paths");
        var badJob=manager.Jobs()[1];badJob.Size=8192;var badPart=Path.Combine(store.DataDirectory,"download-parts",badJob.Id+".part");File.WriteAllBytes(badPart,bytes[..100]);badJob.ETag="\"fixture\"";
        using var bad=new PcDownloads(store,new FakeProvider(),new HttpClient(new TransferHandler(bytes){WrongRange=true}),start:false);refused=false;try{await bad.Transfer(badJob,"https://cdn.example.invalid/file",CancellationToken.None);}catch(IOException){refused=true;}
        check(refused&&!File.Exists(badJob.Destination)&&new FileInfo(badPart).Length==100,"Mismatched CDN ranges cannot corrupt or publish a partial episode");
        var workerMedia=Path.Combine(dir,"worker-media");Directory.CreateDirectory(workerMedia);var workerStore=new LibraryStore(Path.Combine(dir,"worker-profile"));workerStore.State.Roots=[new(){Path=workerMedia}];workerStore.State.Catalog.Clear();workerStore.State.Catalog[item.Id]=new(){Item=new(){Id=item.Id,Title="Worker",Season=2,Episode=1,CollectionKey="worker",ImdbId="tt123",Available=false}};workerStore.Save();
        var workerProvider=new ReadyProvider();using var worker=new PcDownloads(workerStore,workerProvider,new HttpClient(new TransferHandler(bytes)));worker.Configure("https://example.invalid/private/manifest.json","fixture-not-real",workerMedia,"",subtitleLanguages:[]);var release=await worker.Search(item.Id,true);var workerFiles=await worker.Files(release[0].Id);worker.Enqueue(new(){RequestId=Guid.NewGuid().ToString(),ReleaseId=release[0].Id,FileIds=workerFiles.Where(f=>f.Selectable).Select(f=>f.Id).ToList()});
        for(int n=0;n<200&&worker.Jobs().Any(j=>j.Status!="Completed"&&j.Status!="Failed");n++)await Task.Delay(50);
        check(worker.Jobs().All(j=>j.Status=="Completed")&&workerProvider.LinkedIds.SequenceEqual(new long[]{101,102})&&workerStore.Archive.Count(i=>i.Available)==2,"Queue worker rematches owned TorBox file IDs and completes a season into the library");
        await SmartDownloadChecks.Run(dir,check);
        var savedJobs=worker.Jobs();var interrupted=savedJobs[0];interrupted.Status="Paused";interrupted.SubtitleLanguages=[];
        var recoveryDir=Path.Combine(dir,"subtitle-stage-recovery");Directory.CreateDirectory(recoveryDir);File.Copy(Path.Combine(workerStore.DataDirectory,"downloads-settings.dpapi"),Path.Combine(recoveryDir,"downloads-settings.dpapi"),true);
        File.WriteAllText(Path.Combine(recoveryDir,"pc-downloads.json"),JsonSerializer.Serialize(savedJobs));
        File.Copy(Path.Combine(workerStore.DataDirectory,"library.json"),Path.Combine(recoveryDir,"library.json"),true);var recoveryStore=new LibraryStore(recoveryDir);
        using var recovery=new PcDownloads(recoveryStore,new FakeProvider());recovery.Control(interrupted.Id,"resume");
        for(int n=0;n<100&&recovery.Jobs().First().Status!="Completed";n++)await Task.Delay(50);
        check(recovery.Jobs().First().Status=="Completed"&&File.Exists(interrupted.Destination),"Resuming after video delivery finishes the subtitle stage without downloading or overwriting the saved video");
        using var workerReload=new PcDownloads(workerStore,workerProvider,start:false);check(workerReload.Jobs().All(j=>j.Status=="Completed"),"Completed queue receipts survive restart");
    }
    sealed class FakeProvider : IDownloadProvider
    {
        public Task<List<IndexedRelease>> Search(VideoItem i,CancellationToken ct)=>Task.FromResult(new List<IndexedRelease>{new(new(){Id=Guid.NewGuid().ToString("N"),Name="Season pack 1080p"},new string('a',40))});
        public Task<List<TorboxFile>> Files(string hash,CancellationToken ct)=>Task.FromResult(new List<TorboxFile>{new(1,"Test.S02E01.mkv",8192),new(2,"Test.S02E02.mkv",8192),new(3,"Test.S03E01.mkv",8192),new(4,"sample.mkv",400)});
        public Task<TorboxTorrent> Prepare(string h,long id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<string> Link(long id,long file,CancellationToken ct)=>throw new NotSupportedException();
    }
    sealed class ProviderHandler : HttpMessageHandler
    {
        public bool IncludeAccount=true,IndexError=true,IndexTimeout=false,MetadataRequested=false;public int IndexRequests,Creates;
        public static string Torrent(string title,int year)=>JsonSerializer.Serialize(new{id=77,hash=new string('a',40),name=$"{title}.{year}.S02.1080p",download_present=true,files=new[]{new{id=101,name="Test.S02E01.mkv",size=8192},new{id=102,name="Test.S02E02.mkv",size=8192}}});
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            string body;
            if(request.RequestUri!.Host=="index.fixture.invalid"){
                if(IndexTimeout)throw new TaskCanceledException("Fixture timeout");
                IndexRequests++;MetadataRequested=request.Headers.UserAgent.ToString().StartsWith("AIO");
                body=IndexError?JsonSerializer.Serialize(new{streams=new[]{new{name="Error",description="TorBox Search has been removed private-fixture-key https://index.fixture.invalid/private",streamData=new{type="error",error=new{description="TorBox Search has been removed private-fixture-key https://index.fixture.invalid/private"}}}}}):JsonSerializer.Serialize(new{streams=new[]{new{name="1080p",description="Episode release",url="https://provider.invalid/wrapped",streamData=new{torrent=new{infoHash=new string('b',40)}}}}});
            }else{
                if(request.RequestUri.AbsolutePath.EndsWith("createtorrent"))Creates++;
                body="{\"success\":true,\"data\":"+(request.RequestUri.AbsolutePath.EndsWith("mylist")?(IncludeAccount?"["+Torrent("Test",2026)+"]":"[]"):"[]")+"}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
        }
    }
    sealed class TransferHandler(byte[] bytes) : HttpMessageHandler
    {
        public long Start;public bool WrongRange;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Start=request.Headers.Range?.Ranges.First().From??0;var response=new HttpResponseMessage(Start>0?HttpStatusCode.PartialContent:HttpStatusCode.OK){Content=new ByteArrayContent(bytes[(int)Start..])};response.Headers.ETag=new EntityTagHeaderValue("\"fixture\"");if(Start>0)response.Content.Headers.ContentRange=new ContentRangeHeaderValue(WrongRange?Start+1:Start,bytes.Length-1,bytes.Length);return Task.FromResult(response);
        }
    }
    sealed class ReadyProvider : IDownloadProvider
    {
        readonly FakeProvider index=new();public List<long> LinkedIds=[];
        public Task<List<IndexedRelease>> Search(VideoItem i,CancellationToken ct)=>index.Search(i,ct);
        public Task<List<TorboxFile>> Files(string hash,CancellationToken ct)=>index.Files(hash,ct);
        public Task<TorboxTorrent> Prepare(string h,long id,CancellationToken ct)=>Task.FromResult(new TorboxTorrent(55,h,true,[new(101,"pack/Test.S02E01.mkv",8192),new(102,"pack/Test.S02E02.mkv",8192)]));
        public Task<string> Link(long id,long file,CancellationToken ct){LinkedIds.Add(file);return Task.FromResult("https://cdn.example.invalid/file");}
    }
}
