using Noir;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;

static class V2AuditChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static async Task Run(string root,Action<bool,string> check)
    {
        var dir=Path.Combine(root,"v2");Directory.CreateDirectory(dir);var store=new LibraryStore(Path.Combine(dir,"snapshot"));store.State.Roots.Clear();
        var item=new VideoItem{Id="v2",Title="Snapshot",CollectionKey="show:v2",Season=1,Episode=1,Year=2020,ImdbId="tt123",Available=false};store.State.Catalog[item.Id]=new(){Item=item};
        store.State.Viewings[item.Id]=[new(){Id="first",WatchedAt=DateTimeOffset.UtcNow.AddDays(-10)}];store.State.PersonalReviews[item.Id]=new(){Rating10=7,Note="original"};store.State.Collections["List"]=[item.GroupKey];store.State.Progress["CaSe"]=new(){Position=12};
        var copy=LibrarySnapshot.Copy(store.State);check(JsonSerializer.Serialize(copy)==JsonSerializer.Serialize(store.State),"V2: cheap state snapshot preserves the complete serialized library");
        copy.Viewings[item.Id][0].Source="changed";copy.PersonalReviews[item.Id].Note="changed";copy.Catalog[item.Id].Item.Title="changed";copy.Collections["List"].Clear();
        check(store.State.Viewings[item.Id][0].Source!="changed"&&store.State.PersonalReviews[item.Id].Note=="original"&&item.Title=="Snapshot"&&store.State.Collections["List"].Count==1&&copy.Progress.ContainsKey("case"),"V2: state snapshot isolates mutable records and preserves dictionary comparers");
        var writeGate=typeof(LibraryStore).GetField("writeGate",Private)!.GetValue(store)!;using var held=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var blocker=Task.Run(()=>{lock(writeGate){held.Set();release.Wait();}});held.Wait();store.State.Volume=21;store.SaveDeferred();store.State.Volume=22;release.Set();await blocker;await store.FlushPendingAsync();
        var saved=JsonSerializer.Deserialize<PlayerState>(File.ReadAllText(Path.Combine(store.DataDirectory,"library.json")))!;check(saved.Volume==21,"V2: queued state is immutable even when the live library changes before the writer runs");
        store.State.Volume=23;store.SaveDeferred();store.State.Volume=24;store.Save();await store.FlushPendingAsync();check(JsonSerializer.Deserialize<PlayerState>(File.ReadAllText(Path.Combine(store.DataDirectory,"library.json")))!.Volume==24,"V2: synchronous durable saves cannot be overwritten by an older deferred save");
        using var projected=new ManualResetEventSlim();using var projectRelease=new ManualResetEventSlim();store.ProfileTitle=()=>{projected.Set();projectRelease.Wait();return "fixture";};
        var mobile=Task.Run(store.MobileSnapshot);projected.Wait();var query=Task.Run(()=>store.GetProgress(item));var queryCompleted=await Task.WhenAny(query,Task.Delay(2000))==query;item.Title="Live edit";projectRelease.Set();var snapshot=await mobile;store.ProfileTitle=null;
        check(queryCompleted&&snapshot.Items.Single().Title=="Snapshot","V2: phone projection uses isolated state and releases the live library lock before building its response");
        store.State.Viewings[item.Id][0].WatchedAt=DateTimeOffset.UtcNow.AddDays(-11);store.Save();check(store.State.MobileFieldTimes.ContainsKey("viewingDate:"+item.Id+":first"),"V2: explicit history edits still receive mobile conflict clocks");store.State.Viewings[item.Id].Clear();store.Save();check(store.State.MobileFieldTimes.ContainsKey("viewingDeleted:"+item.Id+":first"),"V2: removed viewings still receive mobile tombstones");
        var media=Path.Combine(dir,"scan");Directory.CreateDirectory(media);var mediaFile=Path.Combine(media,"Scan.2024.mkv");File.WriteAllBytes(mediaFile,[1,2,3]);store.State.Roots=[new(){Path=media,Movies=true}];var scanned=store.Scan(out var warnings);
        check(warnings.Count==0&&scanned.Any(i=>i.Path==mediaFile&&i.Available)&&store.Scan(out _).Single().Id==scanned.Single().Id,"V2: scanning prepared filesystem results keeps catalog identities and availability");
        await Planning(check);Cleanup(dir,check);Caches(dir,check);await Measure(dir,check);
    }
    static async Task Planning(Action<bool,string> check)
    {
        var handler=new PlanningHandler();using var http=new HttpClient(handler);var key="account-one";var rd=new RealDebridProvider(()=>new(){Key=key},http);var servers=new MediaServers(http);var item=new VideoItem{Title="Fixture",Year=2020,ImdbId="tt123",Season=1,Episode=1};
        using(PlanningRequests.Begin()){
            await Task.WhenAll(Enumerable.Range(1,4).Select(e=>rd.Discover(new(){Title=item.Title,Year=2020,ImdbId=item.ImdbId,Season=1,Episode=e},false,[item],null,CancellationToken.None)));
            await rd.Files(PlanningHandler.Hash,CancellationToken.None);await rd.Files(PlanningHandler.MissingHash,CancellationToken.None);await rd.Files(PlanningHandler.MissingHash,CancellationToken.None);
            check(handler.Lists==1&&handler.Adds==1&&handler.Infos==2,"V2: one Real-Debrid listing seeds hash IDs and coalesces existing/missing release lookups across a plan");
            foreach(var kind in new[]{"Plex","Jellyfin"}){var server=new MediaServerAccount{Kind=kind,BaseUrl="https://fixture.invalid",Token="token",UserId="u"};await Task.WhenAll(Enumerable.Range(1,4).Select(e=>servers.Search(server,new(){Title=item.Title,Year=2020,ImdbId=item.ImdbId,Season=1,Episode=e},false,CancellationToken.None)));}
            check(handler.PlexEpisodes==1&&handler.JellyfinEpisodes==1,"V2: concurrent episode searches share one Plex/Jellyfin episode listing per plan");
            key="account-two";await rd.Discover(item,false,[item],null,CancellationToken.None);await rd.Files(PlanningHandler.Hash,CancellationToken.None);check(handler.Lists==2&&handler.Infos==3,"V2: a changed Real-Debrid account invalidates account IDs and operation cache entries");
        }
        await rd.Discover(item,false,[item],null,CancellationToken.None);check(handler.Lists==3,"V2: a later planning operation sees a fresh account listing");
    }
    sealed class PlanningHandler:HttpMessageHandler
    {
        public static string Hash=new('a',40),MissingHash=new('b',40);public int Lists,Infos,Adds,PlexEpisodes,JellyfinEpisodes;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            await Task.Delay(5,ct);var path=request.RequestUri!.AbsolutePath;string json;
            if(path.EndsWith("/torrents")){Interlocked.Increment(ref Lists);json=JsonSerializer.Serialize(new[]{new{id="existing",hash=Hash,filename="Fixture.S01",status="downloaded"}});}
            else if(path.Contains("/torrents/info/")){Interlocked.Increment(ref Infos);json=JsonSerializer.Serialize(new{hash=path.EndsWith("existing")?Hash:MissingHash,status="downloaded",files=Enumerable.Range(1,4).Select(e=>new{id=e,path=$"Fixture.S01E{e:00}.mkv",bytes=100,selected=1})});}
            else if(path.EndsWith("/addMagnet")){Interlocked.Increment(ref Adds);json="{\"id\":\"added\"}";}
            else if(path.EndsWith("/allLeaves")){Interlocked.Increment(ref PlexEpisodes);json=JsonSerializer.Serialize(new{MediaContainer=new{Metadata=Enumerable.Range(1,4).Select(e=>new{parentIndex=1,index=e,Media=new[]{new{height=1080,Part=new[]{new{key="/parts/"+e,file=$"Fixture.S01E{e:00}.mkv",size=100}}}}})}});}
            else if(path.EndsWith("/Episodes")){Interlocked.Increment(ref JellyfinEpisodes);json=JsonSerializer.Serialize(new{Items=Enumerable.Range(1,4).Select(e=>new{Id="ep"+e,ParentIndexNumber=1,IndexNumber=e,Path=$"Fixture.S01E{e:00}.mkv",MediaSources=new[]{new{Size=100}}})});}
            else if(path=="/library/all")json="{\"MediaContainer\":{\"Metadata\":[{\"type\":\"show\",\"title\":\"Fixture\",\"year\":2020,\"ratingKey\":\"show\",\"Guid\":[{\"id\":\"imdb://tt123\"}]}]}}";
            else json="{\"Items\":[{\"Type\":\"Series\",\"Name\":\"Fixture\",\"ProductionYear\":2020,\"Id\":\"show\",\"ProviderIds\":{\"Imdb\":\"tt123\"}}]}";
            return new(HttpStatusCode.OK){Content=new StringContent(json)};
        }
    }
    static void Cleanup(string dir,Action<bool,string> check)
    {
        var root=Path.Combine(dir,"cleanup");var store=new LibraryStore(root);var media=Path.Combine(root,"media");Directory.CreateDirectory(media);store.State.Roots=[new(){Path=media,Movies=true}];var path=Path.Combine(media,"Managed.mkv");File.WriteAllBytes(path,[1,2,3]);
        var item=new VideoItem{Id="managed",Title="Managed",CollectionKey="movie:managed",IsMovie=true,Path=path};var watched=DateTimeOffset.UtcNow.AddDays(-12);store.State.Catalog[item.Id]=new(){Item=item,Progress=new(){Completed=true,UpdatedAt=watched}};store.State.Viewings[item.Id]=[new(){Id="v",WatchedAt=watched}];store.Save();
        var other=Path.Combine(root,"profiles",new string('c',32));Directory.CreateDirectory(other);var otherPath=Path.Combine(other,"library.json");File.WriteAllText(otherPath,JsonSerializer.Serialize(store.State));File.WriteAllText(Path.Combine(root,"profiles.json"),JsonSerializer.Serialize(new ProfileList{Profiles=[new(){Id="default"},new(){Id=new string('c',32)}]}));
        using var manager=new PcDownloads(store,start:false);var job=new PcDownloadJob{Id="job",Status="Completed",Root=media,Destination=path,Target=item};((List<PcDownloadJob>)typeof(PcDownloads).GetField("jobs",Private)!.GetValue(manager)!).Add(job);
        var managed=new ReadyManagedFile{JobId=job.Id,ItemId=item.Id,Path=path};var ready=new NoirReadyState{CleanupWatched=false,Migrated=true,ManagedFiles=[managed]};typeof(PcDownloads).GetField("ready",Private)!.SetValue(manager,ready);var reconcile=typeof(PcDownloads).GetMethod("ReconcileReadyFiles",Private)!;
        reconcile.Invoke(manager,[DateTimeOffset.UtcNow]);var readyPath=Path.Combine(root,"noir-ready.json");var firstWrite=File.GetLastWriteTimeUtc(readyPath);reconcile.Invoke(manager,[DateTimeOffset.UtcNow]);check(managed.RemoveAt==null&&File.Exists(path)&&File.GetLastWriteTimeUtc(readyPath)==firstWrite,"V2: cleanup disabled preserves media and unchanged reconciliation does not rewrite Ready state");
        ready.CleanupWatched=true;reconcile.Invoke(manager,[DateTimeOffset.UtcNow]);check(managed.RemoveAt>DateTimeOffset.UtcNow,"V2: one profile summary supports scheduling watched managed-file cleanup");managed.RemoveAt=DateTimeOffset.UtcNow.AddDays(-1);managed.Pinned=true;reconcile.Invoke(manager,[DateTimeOffset.UtcNow]);check(File.Exists(path)&&managed.Pinned,"V2: pinned managed files preserve media during cleanup");
        managed.Pinned=false;managed.RemoveAt=DateTimeOffset.UtcNow.AddDays(-1);store.State.Catalog[item.Id].Progress!.Completed=false;store.Save();reconcile.Invoke(manager,[DateTimeOffset.UtcNow]);check(File.Exists(path)&&managed.RemoveAt==null,"V2: fresh unwatched state prevents managed-file deletion");
        store.State.Catalog[item.Id].Progress!.Completed=true;store.Save();managed.RemoveAt=DateTimeOffset.UtcNow.AddDays(-1);File.AppendAllText(path,"user changed");reconcile.Invoke(manager,[DateTimeOffset.UtcNow]);check(File.Exists(path)&&managed.CleanupCancelled,"V2: a changed file identity cancels cleanup");
        managed.CleanupCancelled=false;managed.FileBytes=new FileInfo(path).Length;managed.LastWriteUtc=File.GetLastWriteTimeUtc(path);managed.RemoveAt=DateTimeOffset.UtcNow.AddDays(-1);reconcile.Invoke(manager,[DateTimeOffset.UtcNow]);check(!File.Exists(path),"V2: unchanged watched managed media is removed only when every profile allows it");
    }
    static void Caches(string dir,Action<bool,string> check)
    {
        var root=Path.Combine(dir,"cache");var store=new LibraryStore(root);var art=Path.Combine(root,"art");var discover=Path.Combine(root,"discover");Directory.CreateDirectory(art);Directory.CreateDirectory(discover);var now=DateTimeOffset.UtcNow;
        string FileAt(string folder,char hash,int days){var path=Path.Combine(folder,new string(hash,64)+(folder==art?".jpg":".json"));File.WriteAllBytes(path,new byte[100]);File.SetLastWriteTimeUtc(path,now.AddDays(-days).UtcDateTime);return path;}
        var poster=FileAt(art,'a',120);var schedule=FileAt(art,'b',120);var profile=FileAt(art,'c',120);var orphan=FileAt(art,'d',120);var pressure=FileAt(art,'e',0);var age=FileAt(discover,'a',40);var old=FileAt(discover,'b',5);var recent=FileAt(discover,'c',2);
        store.State.Catalog["art"]=new(){Item=new(){Id="art",Poster=poster}};store.State.EpisodeSchedules["show"]=[new(){Image=schedule}];store.Save();var other=Path.Combine(root,"profiles",new string('d',32));Directory.CreateDirectory(other);File.WriteAllText(Path.Combine(other,"library.json"),JsonSerializer.Serialize(new PlayerState{Catalog=new(){["other"]=new(){Item=new(){Id="other",Backdrop=profile}}}}));File.WriteAllText(Path.Combine(root,"profiles.json"),JsonSerializer.Serialize(new ProfileList{Profiles=[new(){Id="default"},new(){Id=new string('d',32)}]}));
        store.MaintainCaches(now,100,100);check(File.Exists(poster)&&File.Exists(schedule)&&File.Exists(profile)&&!File.Exists(orphan)&&!File.Exists(pressure),"V2: artwork age/byte eviction preserves catalog, schedule and other-profile references");check(!File.Exists(age)&&!File.Exists(old)&&File.Exists(recent),"V2: discovery cache obeys both maximum age and total-byte budget");
        var unsafeArt=FileAt(art,'f',120);File.WriteAllText(Path.Combine(other,"library.json"),"{bad");store.MaintainCaches(now,100,100);check(File.Exists(unsafeArt),"V2: unreadable profile state conservatively blocks artwork deletion");
    }
    static async Task Measure(string dir,Action<bool,string> check)
    {
        var store=new LibraryStore(Path.Combine(dir,"large"));store.State.Roots.Clear();var now=DateTimeOffset.UtcNow;
        for(int i=0;i<1000;i++){var id="large-"+i;store.State.Catalog[id]=new(){Item=new(){Id=id,Title="Synthetic "+i,CollectionKey="show:"+i,Available=false,Season=1,Episode=1}};store.State.Viewings[id]=Enumerable.Range(0,10).Select(v=>new ViewingRecord{Id=id+"-"+v,WatchedAt=now.AddDays(-v),LoggedAt=now,Source="Synthetic"}).ToList();}
        var options=new JsonSerializerOptions{WriteIndented=true,PropertyNameCaseInsensitive=true};var item=store.State.Catalog.First().Value.Item;store.Save();LibrarySnapshot.Copy(store.State);JsonSerializer.SerializeToUtf8Bytes(store.State,options);
        double Median(List<double> values)=>values.Order().ElementAt(values.Count/2);double Time(Action action){var timer=Stopwatch.StartNew();action();return timer.Elapsed.TotalMilliseconds;}
        var oldSerialize=new List<double>();var newSerialize=new List<double>();var capture=new List<double>();var deferred=new List<double>();int bytes=0;
        for(int run=0;run<15;run++){oldSerialize.Add(Time(()=>bytes=JsonSerializer.SerializeToUtf8Bytes(store.State,options).Length));PlayerState snapshot=null!;capture.Add(Time(()=>snapshot=LibrarySnapshot.Copy(store.State)));newSerialize.Add(Time(()=>JsonSerializer.SerializeToUtf8Bytes(snapshot,options)));deferred.Add(Time(()=>store.Record(item,run+1,100,deferSave:true)));await store.FlushPendingAsync();}
        var metrics=new{Viewings=10000,Catalog=1000,Bytes=bytes,Samples=15,BeforeCallerSerializationMs=Median(oldSerialize),AfterCallerSerializationMs=0,AfterWorkerSerializationMs=Median(newSerialize),SnapshotCaptureMs=Median(capture),DeferredPlaybackCallerMs=Median(deferred)};var json=JsonSerializer.Serialize(metrics);Console.WriteLine("V2 SAVE TIMINGS "+json);File.WriteAllText(Path.Combine(dir,"save-timings.json"),json);check(bytes>1000000&&new LibraryStore(store.DataDirectory).State.Viewings.Values.Sum(v=>v.Count)==10000,"V2: 10,000-viewing deferred-save benchmark retains all history after reload");
    }
}
