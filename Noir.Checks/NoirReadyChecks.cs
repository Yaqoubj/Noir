using Noir;
using System.Text.Json;

static class NoirReadyChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        const long gb=NoirReadyPolicy.GB;var now=DateTimeOffset.UtcNow;var state=new NoirReadyState();
        ReadySource Single(string id,long size,bool confirmed=true)=>new(id,id,size,size,false,confirmed,"1080p");
        var budget=new ReadyStorage(6*gb,3*gb,30*gb,0);
        check(NoirReadyPolicy.Choose([Single("fits",11*gb)],state,budget,"1080p").Source!=null,"Ready budget includes completed and reserved bytes at its exact boundary");
        check(NoirReadyPolicy.Choose([Single("too-big",12*gb)],state,budget,"1080p").Reason=="Preparation budget reached","Ready pauses with a human reason above budget");
        check(NoirReadyPolicy.Choose([Single("disk",2*gb)],state,new(0,0,11*gb,0),"1080p").Reason=="Keeping free space available","Ready always preserves ten GB of free space");
        state.KeepFreeBytes=0;check(NoirReadyPolicy.Choose([Single("reserve",2*gb)],state,new(0,0,12*gb,gb),"1080p").Source==null,"Queued transfers reserve disk and cannot lower the ten GB floor");
        state.Paused=true;check(NoirReadyPolicy.Choose([Single("pause",gb)],state,new(0,0,100*gb,0),"1080p").Reason=="Preparation paused","Global pause prevents preparation");state.Paused=false;
        check(NoirReadyPolicy.Choose([Single("unknown",gb,false),Single("unknown-size",0)],state,new(0,0,100*gb,0),"1080p").Source==null,"Unknown availability and sizes never auto-queue");
        var pack=new ReadySource("pack","pack",gb,24*gb,true,true,"1080p");
        check(NoirReadyPolicy.Choose([pack,Single("single",2*gb)],state,new(0,0,100*gb,0),"1080p").Source?.Id=="single","Ready prefers a single when the real pack size breaks the budget");
        check(NoirReadyPolicy.Choose([pack],state,new(0,0,100*gb,0),"1080p").Source==null,"A pack is charged at its full size rather than one episode's size");
        var sharedPack=new ReadySource("second","shared-pack",gb,12*gb,true,true,"1080p",12*gb);var shared=NoirReadyPolicy.Choose([sharedPack],state,new(0,12*gb,100*gb,0),"1080p");check(shared.Source!=null&&shared.Charge==0,"Episodes sharing a pack reuse its existing reservation");
        check(NoirReadyPolicy.Choose([sharedPack],state,new(0,12*gb,11*gb,gb),"1080p").Source==null,"Shared pack reservations still reserve physical disk for every new file");
        ReadyEpisode Episode(string group,int n,bool watched=false,bool partial=false,DateTimeOffset? airs=null)=>new(group+n,group,group,1,n,watched,partial,false,airs??now.AddDays(-1));
        var episodes=new[]{Episode("show",1,true),Episode("show",2),Episode("show",3),Episode("show",4,airs:now.AddDays(1)),new ReadyEpisode("unknown","show","show",2,1,false,false,false,null)};
        check(NoirReadyPolicy.Next(episodes,2,now).Select(e=>e.Episode).SequenceEqual(new[]{2,3}),"Keep ready chooses the next two aired unwatched episodes only");
        check(NoirReadyPolicy.Next(episodes,1,now).Count==1&&NoirReadyPolicy.Next(episodes,3,now).Count==2&&NoirReadyPolicy.Next(episodes,-1,now).Count==2,"Ready supports one, two, three and whole-season counts");
        check(NoirReadyPolicy.OrderShows([Episode("fresh",1),Episode("started",2,partial:true)]).First().Key=="started","Partially watched shows prepare first");
        var old=now.AddDays(-8);
        check(!NoirReadyPolicy.CanRemove(true,true,false,false,false,old,[],now),"Ready never evicts an unwatched episode");
        check(!NoirReadyPolicy.CanRemove(true,false,true,false,false,old,[],now),"Scanned and manual files cannot enter Ready cleanup");
        check(!NoirReadyPolicy.CanRemove(false,true,true,false,false,old,[],now)&&!NoirReadyPolicy.CanRemove(true,true,true,true,false,old,[],now),"Cleanup requires consent and preserves pins");
        check(!NoirReadyPolicy.CanRemove(true,true,true,false,true,old,[],now)&&!NoirReadyPolicy.CanRemove(true,true,true,false,false,now.AddDays(-6),[],now),"Cancel removal and the seven-day window are respected");
        check(!NoirReadyPolicy.CanRemove(true,true,true,false,false,old,[true,false],now)&&!NoirReadyPolicy.CanRemove(true,true,true,false,false,old,[null],now),"Another profile's unfinished or unknown progress prevents removal");
        check(NoirReadyPolicy.CanRemove(true,true,true,false,false,old,[true,true],now),"Only watched managed files finished by every profile become eligible");
        check(NoirReadyPolicy.Backoff(now,1)==now.AddMinutes(15)&&NoirReadyPolicy.Backoff(now,9)==now.AddHours(6),"Unknown sources use bounded exponential recheck backoff");
        for(int n=0;n<220;n++)state.Log("check",n.ToString());check(state.Why.Count==200&&state.Why[0].Title=="20","Ready why-log retains the last 200 actions");

        var folder=Path.Combine(root,"noir-ready-"+Guid.NewGuid().ToString("N"));var store=new LibraryStore(folder);var media=Path.Combine(folder,"media");Directory.CreateDirectory(media);store.State.Roots=[new(){Path=media}];
        var item=new VideoItem{Id="ready-check",CollectionKey="ready-show",Title="Ready Fixture",Season=1,Episode=1,Available=false,ImdbId="tt123"};store.State.Catalog[item.Id]=new(){Item=item};
        store.State.EpisodeSchedules[item.GroupKey]=[new(){Season=1,Episode=1,AirsAt=now.AddDays(-1)},new(){Season=1,Episode=2,AirsAt=now.AddDays(-1)},new(){Season=1,Episode=3,AirsAt=now.AddDays(1)}];
        store.SetShowDownloadPreference(item.GroupKey,new(){AutoDownload=true});var provider=new ReadyProvider();using var downloads=new PcDownloads(store,provider,start:false,freeSpace:_=>100*gb);downloads.Configure("","fixture",media,"");downloads.InitializeReady();
        check(downloads.SeasonPreferences(item.Id) is {KeepReadyEnabled:true,KeepReadyCount:2},"Existing AutoDownload migrates to Keep ready two");
        await downloads.PrepareReady("Startup");check(downloads.Jobs().Count==2&&downloads.ReadyState.ManagedFiles.Count==2,"Ready queues two confirmed aired files and records ownership");
        check(provider.UnknownFileCalls==0,"Automatic planner filters unknown sources before reading file lists or adding magnets");
        check(File.Exists(Path.Combine(folder,"noir-ready.json"))&&JsonSerializer.Deserialize<NoirReadyState>(File.ReadAllText(Path.Combine(folder,"noir-ready.json")))!.ManagedFiles.Count==2,"Ready intentions and ownership persist in the profile directory");
        await downloads.ChangeReady(s=>s.Paused=true);check(downloads.Jobs().All(j=>j.Status=="Paused"),"Global pause stops queued managed files");await downloads.ChangeReady(s=>s.Paused=false);check(downloads.Jobs().All(j=>j.Status=="Queued"),"Resume restarts only files paused by preparation");
        downloads.SaveSeasonPreferences(item.Id,new(){KeepReadyEnabled=false});store.State.Watchlist.Add(item.GroupKey);foreach(var job in downloads.Jobs())downloads.Control(job.Id,"cancel");
        var before=downloads.Jobs().Count;await downloads.PrepareReady("Preferences changed");check(downloads.Jobs().Count==before&&downloads.PreparedTitle(item.GroupKey)?.State=="Ready","My List prepares ready metadata without downloading");
        await downloads.ChangeReady(s=>s.BudgetBytes=gb);await downloads.PrepareReady("Preferences changed");check(downloads.Jobs().Count==before,"Budget pressure never evicts unwatched files or creates replacement jobs");
        File.WriteAllBytes(Path.Combine(folder,"download-parts","manual-orphan.part"),new byte[4096]);await downloads.CheckReadyStorage();check(downloads.ReadyStorage.UsedBytes>=4096,"Storage accounting includes real partial files, including interrupted manual transfers");
        foreach(var episode in store.EpisodesFor(item.GroupKey).Where(e=>e.Episode is 1 or 2))store.Mark(episode,true);store.State.EpisodeSchedules[item.GroupKey].Single(e=>e.Episode==3).AirsAt=now.AddDays(-1);await downloads.PrepareReady("New episode");check(downloads.PreparedTitle(item.GroupKey)?.Episodes.SingleOrDefault()?.Episode==3,"A newly aired next episode invalidates a cached plan before its backoff expires");
        store.State.Catalog[item.Id].Progress!.Completed=false;store.State.Catalog[item.Id].Progress!.Position=0;check(!store.ReadyCleanupInputs([item.Id])[item.Id].Watched,"An unfinished rewatch at position zero prevents cleanup despite older watched history");
    }
    sealed class ReadyProvider:IDownloadProvider
    {
        public int UnknownFileCalls;
        public Task<List<IndexedRelease>> Search(VideoItem item,CancellationToken ct)=>Task.FromResult(new List<IndexedRelease>{new(new(){Id=Guid.NewGuid().ToString("N"),Name="ready",Cached=true,Resolution="1080p",CacheStatus="Ready",Size=2*NoirReadyPolicy.GB,SizeKind="Pack"},new string('a',40)),new(new(){Id=Guid.NewGuid().ToString("N"),Name="unknown",Size=NoirReadyPolicy.GB},new string('b',40))});
        public Task<List<TorboxFile>> Files(string hash,CancellationToken ct){if(hash[0]=='b')UnknownFileCalls++;return Task.FromResult(new List<TorboxFile>{new(1,"Ready.Fixture.S01E01.mkv",NoirReadyPolicy.GB),new(2,"Ready.Fixture.S01E02.mkv",NoirReadyPolicy.GB)});}
        public Task<TorboxTorrent> Prepare(string hash,long id,CancellationToken ct)=>throw new Exception("Preparation worker must not start in checks");
        public Task<string> Link(long id,long file,CancellationToken ct)=>throw new Exception("Offline checks never request links");
    }
}
