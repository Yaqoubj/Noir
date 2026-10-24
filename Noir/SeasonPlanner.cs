using System.IO;
using System.Text.Json;

namespace Noir;

public sealed record SeasonEpisode(int Season,int Episode);
public sealed class EpisodeDownloadOption
{
    public string ReleaseId {get;set;}="";
    public string Release {get;set;}="";
    public string Source {get;set;}="";
    public string Resolution {get;set;}="";
    public string Language {get;set;}="Unknown";
    public string CacheState {get;set;}="Needs fetching";
    public DownloadFileChoice File {get;set;}=new();
}
public sealed class PlannedEpisode
{
    public string ItemId {get;set;}="";
    public int Season {get;set;}
    public int Episode {get;set;}
    public string Title {get;set;}="";
    public string State {get;set;}="Not found";
    public string Reason {get;set;}="No source covers this episode at these preferences";
    public EpisodeDownloadOption? Chosen {get;set;}
    public List<EpisodeDownloadOption> Alternatives {get;set;}=[];
}
public sealed class SeasonDownloadConfirmation
{
    public string PlanId {get;set;}="";
    public string RequestId {get;set;}="";
    // Explicit list: omit an episode to exclude it; choose any alternative's file ID to swap it.
    public List<string> FileIds {get;set;}=[];
}

public sealed partial class PcDownloads
{
    readonly Dictionary<string,(DownloadPlan Plan,DateTimeOffset Expires,string Group,bool Unwatched)> seasonPlans=[];
    readonly Dictionary<string,HashSet<string>> plannerSearches=[];
    // Injectable disk reading keeps checks offline and makes capacity changes testable.
    readonly Func<string,long>? freeSpace;
    const long DiskReserve=64L*1024*1024;
    static T Copy<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    bool Future(VideoItem item)=>store.State.EpisodeSchedules.GetValueOrDefault(item.GroupKey)?.Any(e=>e.Season==item.Season&&e.Episode==item.Episode&&e.AirsAt>DateTimeOffset.UtcNow)??false;
    long FreeSpace(string root)=>freeSpace?.Invoke(root)??new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace;
    long Reserved(string root)=>jobs.Where(j=>string.Equals(Path.GetPathRoot(j.Root),Path.GetPathRoot(root),StringComparison.OrdinalIgnoreCase)&&j.Status is not ("Completed" or "Cancelled" or "Failed")).Sum(j=>Math.Max(0,j.Size-Math.Max(0,j.Bytes)));
    void CheckCapacity(List<PcDownloadJob> pending)
    {
        foreach(var disk in pending.GroupBy(j=>Path.GetPathRoot(j.Root),StringComparer.OrdinalIgnoreCase))
            if(disk.Sum(j=>Math.Max(0,j.Size))+Reserved(disk.First().Root)+DiskReserve>FreeSpace(disk.First().Root))throw new IOException("Not enough free space in the library folder. Exclude episodes or choose smaller files.");
    }
    public ShowDownloadPreference SeasonPreferences(string itemId)
    {
        var group=Item(itemId).GroupKey;
        if(store.GetShowDownloadPreference(group) is {} preference)return preference;
        var c=Config;return new(){Resolution=c.PreferredResolution,Language=c.BrowseLanguage,MaxEpisodeBytes=c.MaxEpisodeBytes};
    }
    public void SaveSeasonPreferences(string itemId,ShowDownloadPreference preference)
    {
        if(preference.Resolution is not ("Any" or "720p" or "1080p" or "2160p")||preference.MaxEpisodeBytes<0)throw new IOException("Choose a valid quality and episode size.");
        var group=Item(itemId).GroupKey;
        store.SetShowDownloadPreference(group,Copy(preference));Changed?.Invoke();
    }
    List<VideoItem> ScopeEpisodes(VideoItem item,DownloadPlanRequest request)
    {
        if(item.IsMovie)throw new IOException("Choose a show for a season plan.");
        var all=store.EpisodesFor(item.GroupKey).Where(i=>i.Season>0&&i.Episode>0).DistinctBy(i=>(i.Season,i.Episode)).OrderBy(i=>i.Season).ThenBy(i=>i.Episode).ToList();
        var seasons=request.Seasons.Count>0?request.Seasons.ToHashSet():request.Scope is "Several seasons / whole show" or "Next N episodes"?all.Select(i=>i.Season).ToHashSet():new HashSet<int>{item.Season};
        var selected=all.Where(i=>seasons.Contains(i.Season));
        switch(request.Scope){
            case "Missing episodes":case "Several seasons / whole show":break;
            case "Unwatched only":selected=selected.Where(i=>!store.EverWatched(i));break;
            case "From episode N":
                if(request.FromEpisode<1)throw new IOException("Enter an episode number of 1 or more.");
                selected=selected.Where(i=>i.Season>item.Season||i.Episode>=request.FromEpisode);break;
            case "Next N episodes":
                if(request.NextCount is <1 or >25)throw new IOException("Choose between 1 and 25 next episodes.");
                var last=all.LastOrDefault(store.EverWatched);
                selected=selected.Where(i=>last==null||i.Season>last.Season||i.Season==last.Season&&i.Episode>last.Episode).Take(request.NextCount);break;
            case "Specific episodes":
                var explicitEpisodes=request.SpecificEpisodes.ToHashSet();
                selected=all.Where(i=>explicitEpisodes.Contains(new(i.Season,i.Episode)));break;
            default:throw new IOException("Choose a season scope.");
        }
        return selected.ToList();
    }
    public async Task<DownloadPlan> PlanSeason(string itemId,DownloadPlanRequest request,IProgress<DownloadSourceStatus>? progress=null,CancellationToken ct=default)
    {
        var searchId=Guid.NewGuid().ToString("N");lock(gate)plannerSearches[searchId]=[];
        try{
        request=Copy(request);
        var item=Item(itemId);var scoped=ScopeEpisodes(item,request);var preference=request.Preferences??SeasonPreferences(itemId);
        SaveSeasonPreferences(itemId,preference);
        var plan=new DownloadPlan{Id=Guid.NewGuid().ToString("N"),Preferences=Copy(preference),Folder=Root(item),ScheduleKnown=store.State.EpisodeSchedules.GetValueOrDefault(item.GroupKey)?.Count>0};
        var wanted=new List<VideoItem>();
        foreach(var episode in scoped){
            var state=OnPc(episode)?"On PC":IsQueued(episode)?"Queued":Future(episode)?"Unaired":"Not found";
            plan.Episodes.Add(new(){ItemId=episode.Id,Season=episode.Season,Episode=episode.Episode,Title=episode.EpisodeTitle,State=state,Reason=state=="Not found"?"No source covers this episode at these preferences":state=="Unaired"?"Not aired yet":state=="On PC"?"Already in your library":"Already in your download queue"});
            if(state=="Not found")wanted.Add(episode);
        }
        plan.ExistingEpisodes=plan.Episodes.Count(e=>e.State=="On PC");plan.QueuedEpisodes=plan.Episodes.Count(e=>e.State=="Queued");plan.ReleasedEpisodes=plan.Episodes.Count(e=>e.State!="Unaired");
        var releaseIds=request.ReleaseIds.Distinct().ToList();
        // File lists are fetched once per release, a few at a time; a failure only drops that release.
        var fileLists=new System.Collections.Concurrent.ConcurrentDictionary<string,Lazy<Task<List<DownloadFileChoice>?>>>();var fileSlots=new SemaphoreSlim(6);var failed=new System.Collections.Concurrent.ConcurrentBag<string>();
        Task<List<DownloadFileChoice>?> FilesOnce(string id)=>fileLists.GetOrAdd(id,key=>new(async()=>
        {
            await fileSlots.WaitAsync(ct);
            try{return await Files(key,ct);}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
            catch(Exception e)when(e is IOException or System.Net.Http.HttpRequestException or JsonException or OperationCanceledException){failed.Add(key);return null;}
            finally{fileSlots.Release();}
        })).Value;
        var aio=releaseIds.Count==0&&wanted.Count>0?EpisodeSearcher(item):null;
        if(aio!=null){
            await EpisodeFirst(aio,wanted,progress,searchId,releaseIds,plan,(id,files)=>fileLists[id]=new(Task.FromResult<List<DownloadFileChoice>?>(files)),ct);
            // Media servers, Real-Debrid and the TorBox account library aren't all in AIOStreams' results; ask them per
            // episode (the TorBox account listing is shared across those requests).
            if(provider is DownloadProviders){
                using var slots=new SemaphoreSlim(4);
                var connected=await Task.WhenAll(wanted.Select(async episode=>{await slots.WaitAsync(ct);try{return await SearchDetailedCore(episode.Id,false,progress,ct,searchId,true);}finally{slots.Release();}}));
                foreach(var result in connected){releaseIds.AddRange(result.Choices.Select(c=>c.Id));plan.Sources.AddRange(result.Sources);}
            }
        }
        else if(releaseIds.Count==0&&wanted.Count>0){
            // Each season: the season-pack search, then every wanted episode for single links (they are the
            // per-episode alternatives), a few searches at a time instead of one by one.
            var searchSlots=new SemaphoreSlim(4);var found=new System.Collections.Concurrent.ConcurrentBag<DownloadSearchResult>();
            foreach(var season in wanted.GroupBy(i=>i.Season)){
                ct.ThrowIfCancellationRequested();
                progress?.Report(new(){Source="Planner",State="Searching",Message=$"Season {season.Key}: looking for season packs"});
                var pack=await SearchDetailedCore(season.First().Id,true,progress,ct,searchId);releaseIds.AddRange(pack.Choices.Select(c=>c.Id));plan.Sources.AddRange(pack.Sources);
                var missing=season.ToList();
                var done=0;
                await Task.WhenAll(missing.Select(async episode=>
                {
                    await searchSlots.WaitAsync(ct);
                    try{found.Add(await SearchDetailedCore(episode.Id,false,progress,ct,searchId));}
                    finally{searchSlots.Release();progress?.Report(new(){Source="Planner",State="Searching",Message=$"Season {season.Key}: single episodes {Interlocked.Increment(ref done)} of {missing.Count}"});}
                }));
            }
            foreach(var singles in found){releaseIds.AddRange(singles.Choices.Select(c=>c.Id));plan.Sources.AddRange(singles.Sources);}
        }
        if(aio==null){progress?.Report(new(){Source="Planner",State="Checking",Message=$"Reading file lists for {releaseIds.Distinct().Count()} releases"});await Task.WhenAll(releaseIds.Distinct().Select(FilesOnce));}
        var options=new List<EpisodeDownloadOption>();var seen=new HashSet<string>();
        foreach(var id in releaseIds.Distinct()){
            ct.ThrowIfCancellationRequested();
            (IndexedRelease Release,VideoItem Item,bool Season,DateTimeOffset Expires,string Service) indexed;
            lock(gate)if(!releases.TryGetValue(id,out indexed)||indexed.Expires<DateTimeOffset.UtcNow||indexed.Item.GroupKey!=item.GroupKey)continue;
            var release=indexed.Release;ReleasePresentation.Enrich(release.Choice);
            // Random public IDs must never affect planning order or cause duplicate file-list calls.
            var identity=indexed.Service+"|"+indexed.Item.Season+"|"+(release.Direct==null?release.Hash:string.Join(";",release.Direct.Select(d=>d.ServerId+"|"+d.Name+"|"+d.Season+"|"+d.Episode+"|"+d.Size+"|"+d.Url)));
            if(aio!=null&&!fileLists.ContainsKey(id))identity+="|episode:"+indexed.Item.Episode;
            if(!seen.Add(identity))continue;
            var files=await FilesOnce(id);
            if(files==null){plan.Sources.Add(new(){Source=string.Join(" / ",release.Choice.Sources),State="Partial",Message="A release file list is unavailable"});continue;}
            foreach(var f in files.Where(f=>f.Selectable&&wanted.Any(w=>w.Season==f.Season&&w.Episode==f.Episode))){
                DirectDownloadSource? direct;lock(gate)direct=choices.GetValueOrDefault(f.Id).Direct;
                var resolution=direct?.Resolution??"";if(resolution.Length==0)resolution=TorboxProvider.Resolution(f.Name);if(resolution.Length==0)resolution=release.Choice.Resolution;
                var language=direct?.Language is {Length:>0} audio&&audio!="Unknown"?audio:release.Choice.Languages;
                if(preference.MaxEpisodeBytes>0&&(f.Size<=0||f.Size>preference.MaxEpisodeBytes))continue;
                if(preference.Language is not ("All" or "Any" or "")&&!language.Contains(preference.Language,StringComparison.OrdinalIgnoreCase))continue;
                options.Add(new(){ReleaseId=id,Release=release.Choice.Name,Source=release.Choice.Sources.Count>0?string.Join(" / ",release.Choice.Sources):indexed.Service,Resolution=resolution.Length>0?resolution:"Unknown",Language=language,CacheState=release.Choice.CacheStatus=="On your server"?"On your server":release.Choice.Cached||release.Choice.CacheStatus=="Ready"?"Ready":"Needs fetching",File=f});
            }
        }
        foreach(var episode in plan.Episodes.Where(e=>e.State=="Not found")){
            var available=options.Where(o=>o.File.Season==episode.Season&&o.File.Episode==episode.Episode).ToList();
            // Prefer exact quality whenever available; retain other qualities as explicit alternatives.
            episode.Alternatives=SortOptions(available,preference).ToList();
        }
        foreach(var season in plan.Episodes.Where(e=>e.State=="Not found").GroupBy(e=>e.Season)){
            var remaining=season.ToList();var total=remaining.Count;bool usedPack=false;
            while(remaining.Count>0){
                var candidates=remaining.SelectMany(e=>e.Alternatives.Where(o=>preference.Resolution=="Any"||!e.Alternatives.Any(a=>a.Resolution==preference.Resolution)||o.Resolution==preference.Resolution)).ToList();
                // Greedy set cover: most wanted episodes, then readiness, known/smaller bytes,
                // then stable source/release/file names. Only selected episode files are queued.
                var best=candidates.GroupBy(o=>o.ReleaseId).Select(g=>new{Options=g.ToList(),Count=g.Select(o=>o.File.Episode).Distinct().Count(),Ready=g.Count(o=>o.CacheState!="Needs fetching")})
                    .OrderByDescending(g=>g.Count*2>=remaining.Count?g.Count:1).ThenByDescending(g=>g.Ready>0).ThenByDescending(g=>g.Count).ThenBy(g=>g.Options.Any(o=>o.File.Size<=0)).ThenBy(g=>g.Options.Sum(o=>o.File.Size))
                    .ThenBy(g=>g.Options[0].Source,StringComparer.Ordinal).ThenBy(g=>g.Options[0].Release,StringComparer.Ordinal).ThenBy(g=>g.Options[0].File.Name,StringComparer.Ordinal).FirstOrDefault();
                if(best==null)break;
                var pack=best.Count>1;usedPack|=pack;
                foreach(var episode in remaining.ToList()){
                    var chosen=SortOptions(best.Options.Where(o=>o.File.Episode==episode.Episode).ToList(),preference).FirstOrDefault();if(chosen==null)continue;
                    episode.Chosen=chosen;episode.State=chosen.CacheState;
                    episode.Reason=pack?$"Season pack covers {best.Count} of {total}":usedPack?$"Pack missing E{episode.Episode:00} · single link":"Best available single link";
                    if(preference.Resolution!="Any"&&chosen.Resolution!=preference.Resolution)episode.Reason+=" · preferred quality unavailable";
                    remaining.Remove(episode);
                }
            }
        }
        plan.Entries=plan.Episodes.Where(e=>e.Chosen!=null).Select(e=>new DownloadPlanEntry{ReleaseId=e.Chosen!.ReleaseId,Release=e.Chosen.Release,File=e.Chosen.File}).ToList();
        plan.MissingEpisodes=plan.Episodes.Where(e=>e.State=="Not found").Select(e=>e.Episode).ToList();plan.SelectedBytes=plan.Entries.Sum(e=>e.File.Size);plan.HasUnknownSizes=plan.Entries.Any(e=>e.File.Size<=0);
        lock(gate){plan.FreeBytes=Math.Max(0,FreeSpace(plan.Folder)-Reserved(plan.Folder));plan.Fits=plan.SelectedBytes+DiskReserve<=plan.FreeBytes;}
        plan.Message=$"{plan.Entries.Count} episodes · {DownloadNaming.Size(plan.SelectedBytes)} · {plan.MissingEpisodes.Count} not found"+(!plan.ScheduleKnown?" · Schedule unavailable; known episodes only":"")+(plan.HasUnknownSizes?" · Some sizes unknown":"");
        lock(gate){foreach(var key in seasonPlans.Where(p=>p.Value.Expires<DateTimeOffset.UtcNow).Select(p=>p.Key).ToList())seasonPlans.Remove(key);seasonPlans[plan.Id]=(Copy(plan),DateTimeOffset.UtcNow.AddMinutes(20),item.GroupKey,request.Scope=="Unwatched only");}
        return plan;
        }finally{lock(gate)plannerSearches.Remove(searchId);}
    }
    // AIOStreams answers each episode with that episode's own file in every release, singles and
    // season packs alike (file name and size included). So planning needs one search per episode,
    // run in parallel, and no season probes or torrent file lists; the download worker re-finds the
    // file by name and size when it starts.
    internal string ReleaseHash(string releaseId){lock(gate)return releases.TryGetValue(releaseId,out var r)?r.Release.Hash:"";}
    TorboxProvider? EpisodeSearcher(VideoItem item)
    {
        var c=Config;if(c.Manifest.Length==0||!System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId,@"^tt\d+$"))return null;
        return (provider is DownloadProviders router?router.ForService(c.Service):provider) as TorboxProvider;
    }
    async Task EpisodeFirst(TorboxProvider aio,List<VideoItem> wanted,IProgress<DownloadSourceStatus>? progress,string searchId,List<string> releaseIds,DownloadPlan plan,Action<string,List<DownloadFileChoice>> files,CancellationToken ct)
    {
        var service=Config.Service;var results=new System.Collections.Concurrent.ConcurrentDictionary<string,List<IndexedRelease>>();var done=0;var failures=0;
        using var slots=new SemaphoreSlim(6);
        progress?.Report(new(){Source="AIOStreams",State="Searching",Message=$"Searching {wanted.Count} episode{(wanted.Count==1?"":"s")}…"});
        await Task.WhenAll(wanted.Select(async episode=>
        {
            await slots.WaitAsync(ct);
            try{results[episode.Id]=await aio.IndexSearch(episode,ct);}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
            catch(Exception e)when(e is IOException or System.Net.Http.HttpRequestException or JsonException or OperationCanceledException){Interlocked.Increment(ref failures);}
            finally{slots.Release();progress?.Report(new(){Source="AIOStreams",State="Searching",Message=$"{Interlocked.Increment(ref done)} of {wanted.Count} episodes searched"});}
        }));
        var groups=new Dictionary<string,(string Id,IndexedRelease Release,List<DownloadFileChoice> Files)>();
        lock(gate)
        {
            foreach(var episode in wanted.OrderBy(i=>i.Season).ThenBy(i=>i.Episode))
            {
                if(!results.TryGetValue(episode.Id,out var found))continue;
                foreach(var release in found)
                {
                    // One release per torrent: the same pack found for several episodes is one choice.
                    var key=release.Direct==null?"hash:"+release.Hash:"link:"+release.Choice.Id;
                    if(!groups.TryGetValue(key,out var group))
                    {
                        group=(release.Choice.Id,release,[]);groups[key]=group;
                        releases[release.Choice.Id]=(release,episode,false,DateTimeOffset.UtcNow.AddMinutes(20),service);
                        if(plannerSearches.TryGetValue(searchId,out var pinned))pinned.Add(release.Choice.Id);
                    }
                    var name=release.Direct?.FirstOrDefault()?.Name??release.Choice.Name;
                    var multi=System.Text.RegularExpressions.Regex.IsMatch(name,@"(?i)s\d{1,2}[ ._-]*e\d{1,3}(?:[ ._-]*e\d|[ ._-]*-[ ._-]*(?:e)?\d)");
                    var choice=new DownloadFileChoice{Id=Guid.NewGuid().ToString("N"),Name=name,Size=release.Choice.Size,Season=episode.Season,Episode=episode.Episode,Selectable=!multi,Reason=multi?"Multi-episode file · needs manual organization":""};
                    choices[choice.Id]=(group.Id,choice,release.Direct?.FirstOrDefault());group.Files.Add(choice);
                    if(release.Choice.Cached)group.Release.Choice.Cached=true;
                }
            }
            foreach(var group in groups.Values.Where(g=>g.Files.Select(f=>(f.Season,f.Episode)).Distinct().Count()>1))
            {
                // A pack: name it by its season rather than by the first episode's file.
                var choice=group.Release.Choice;choice.Name=System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(choice.Name,@"(?i)(s\d{1,2})[ ._-]*e(?:p)?\d{1,3}","$1"),@"(?i)\.(mkv|mp4|avi|m4v|ts|webm)$","");choice.SizeKind="Pack";choice.Size=group.Files.Sum(f=>f.Size);
            }
        }
        foreach(var group in groups.Values){releaseIds.Add(group.Id);files(group.Id,group.Files);}
        plan.Sources.Add(new(){Source="AIOStreams",State=failures==0?groups.Count>0?"Ready":"Empty":"Partial",Count=groups.Count,
            Message=$"{wanted.Count-failures} of {wanted.Count} episodes searched · {groups.Count} release{(groups.Count==1?"":"s")}"+(failures>0?$" · {failures} search{(failures==1?"":"es")} failed; Refresh plan to retry":"")});
    }
    static IOrderedEnumerable<EpisodeDownloadOption> SortOptions(List<EpisodeDownloadOption> options,ShowDownloadPreference preference)=>options.OrderByDescending(o=>preference.Resolution=="Any"||o.Resolution==preference.Resolution).ThenByDescending(o=>o.CacheState!="Needs fetching").ThenBy(o=>o.File.Size<=0).ThenBy(o=>o.File.Size).ThenBy(o=>o.Source,StringComparer.Ordinal).ThenBy(o=>o.Release,StringComparer.Ordinal).ThenBy(o=>o.File.Name,StringComparer.Ordinal);
    public List<string> EnqueuePlan(SeasonDownloadConfirmation confirmation)
    {
        lock(gate){
            if(!Guid.TryParse(confirmation.RequestId,out _))throw new IOException("Invalid request ID.");
            var prior=jobs.Where(j=>j.RequestId==confirmation.RequestId).Select(j=>j.Id).ToList();if(prior.Count>0)return prior;
            if(!seasonPlans.TryGetValue(confirmation.PlanId,out var session)||session.Expires<DateTimeOffset.UtcNow)throw new IOException("This plan expired. Build it again.");
            var representative=store.EpisodesFor(session.Group).FirstOrDefault()??throw new IOException("Refresh this show's library entry.");
            if(!Root(representative).Equals(session.Plan.Folder,StringComparison.OrdinalIgnoreCase))throw new IOException("The download folder changed. Build this plan again.");
            var selected=new List<EpisodeDownloadOption>();
            foreach(var id in confirmation.FileIds.Distinct()){
                var episode=session.Plan.Episodes.FirstOrDefault(e=>e.Alternatives.Any(o=>o.File.Id==id))??throw new IOException("Choose an alternative from this plan.");
                if(selected.Any(o=>o.File.Season==episode.Season&&o.File.Episode==episode.Episode))throw new IOException("Choose one option per episode.");
                var item=store.EpisodesFor(session.Group).FirstOrDefault(i=>i.Season==episode.Season&&i.Episode==episode.Episode)??throw new IOException("Refresh this show's episode schedule.");
                if(session.Unwatched&&store.EverWatched(item))throw new IOException("An episode is now watched. Build the unwatched plan again.");
                selected.Add(episode.Alternatives.First(o=>o.File.Id==id));
            }
            return EnqueueCore(new(){RequestId=confirmation.RequestId,Selections=selected.GroupBy(o=>o.ReleaseId).Select(g=>new DownloadSelection{ReleaseId=g.Key,FileIds=g.Select(o=>o.File.Id).ToList()}).ToList()},null);
        }
    }
    public async Task<bool> TryAutoDownload(string itemId,CancellationToken ct=default)
    {
        var item=Item(itemId);
        if(!SeasonPreferences(itemId).AutoDownload||item.IsMovie||Future(item)||store.EverWatched(item)||OnPc(item)||IsQueued(item))return false;
        var plan=await PlanSeason(itemId,new(){Scope="Specific episodes",SpecificEpisodes=[new(item.Season,item.Episode)]},ct:ct);
        var ready=plan.Episodes.SingleOrDefault()?.Alternatives.FirstOrDefault(o=>o.CacheState is "Ready" or "On your server"&&o.File.Size>0);
        if(ready==null)return false;
        try{return EnqueuePlan(new(){PlanId=plan.Id,RequestId=Guid.NewGuid().ToString(),FileIds=[ready.File.Id]}).Count>0;}catch(IOException){return false;}
    }
}
public sealed partial class LibraryStore
{
    public ShowDownloadPreference? GetShowDownloadPreference(string group){lock(gate)return State.ShowDownloads.TryGetValue(group,out var preference)?JsonSerializer.Deserialize<ShowDownloadPreference>(JsonSerializer.Serialize(preference)):null;}
    public void SetShowDownloadPreference(string group,ShowDownloadPreference preference){lock(gate){State.ShowDownloads[group]=preference;Save();}}
}
