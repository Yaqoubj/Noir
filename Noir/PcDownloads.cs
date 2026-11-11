using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Noir;

public sealed partial class PcDownloads : IDisposable
{
    readonly LibraryStore store;
    readonly IDownloadProvider provider;
    readonly HttpClient transfer;
    readonly object gate=new();
    readonly SemaphoreSlim wake=new(0,1);
    readonly CancellationTokenSource stop=new();
    readonly string credentialsPath,jobsPath,parts;
    readonly SubtitleDownloads subtitles;
    DownloadCredentials credentials=new();
    List<PcDownloadJob> jobs=[];
    readonly Dictionary<string,(IndexedRelease Release,VideoItem Item,bool Season,DateTimeOffset Expires,string Service)> releases=[];
    readonly Dictionary<string,(string Release,DownloadFileChoice Choice,DirectDownloadSource? Direct)> choices=[];
    readonly Dictionary<string,CancellationTokenSource> running=[];
    DateTime progressSaved=DateTime.MinValue;
    public int ActiveCount {get{lock(gate)return jobs.Count(j=>j.Status is "Queued" or "Preparing" or "Downloading" or "Subtitles");}}
    public int JobCount {get{lock(gate)return jobs.Count;}}
    public event Action? Changed;
    public event Action? Completed;
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public PcDownloads(LibraryStore store,IDownloadProvider? provider=null,HttpClient? transfer=null,bool start=true,SubtitleDownloads? subtitles=null,Func<string,long>? freeSpace=null,TorrentEngineOptions? torrentOptions=null)
    {
        this.torrentOptions=torrentOptions??new();this.store=store;this.freeSpace=freeSpace;credentialsPath=Path.Combine(store.DataDirectory,"downloads-settings.dpapi");jobsPath=Path.Combine(store.DataDirectory,"pc-downloads.json");parts=Path.Combine(store.DataDirectory,"download-parts");Directory.CreateDirectory(parts);
        if(File.Exists(credentialsPath))credentials=JsonSerializer.Deserialize<DownloadCredentials>(ProtectedData.Unprotect(File.ReadAllBytes(credentialsPath),null,DataProtectionScope.CurrentUser))??new();
        else SaveCredentials();
        if(File.Exists(jobsPath))jobs=JsonSerializer.Deserialize<List<PcDownloadJob>>(File.ReadAllText(jobsPath),Json)??[];
        foreach(var j in jobs.Where(j=>j.Status=="Downloading"||j.Service=="Torrents"&&j.Status is "Preparing" or "Subtitles")){j.Status="Paused";j.Message="Interrupted · resume to continue";}
        this.provider=provider??new DownloadProviders(()=>Config,torrentRuntime:()=>TorrentRuntime);this.transfer=transfer??DownloadSecurity.Client(Timeout.InfiniteTimeSpan);
        this.subtitles=subtitles??new SubtitleDownloads(store.DataDirectory,source:()=>SubtitleSelection.From(Config));
        Changed+=CaptureTorrentJobs;
        if(start){_=Task.Run(Worker);torrentWorker=Task.Run(TorrentWorker);}
    }
    public DownloadCredentials Config {get {lock(gate)return JsonSerializer.Deserialize<DownloadCredentials>(JsonSerializer.Serialize(credentials))!;}}
    public bool Configured {get{var c=Config;return c.Servers.Count>0||(c.Service is "AIOStreams links" or "Torrents"?c.Manifest.Length>0:c.Key.Length>0);}}
    public void AddServer(MediaServerAccount server)
    {
        server.BaseUrl=MediaServers.BaseUrl(server.BaseUrl);if(server.Token.Length==0)throw new IOException("Connect the media server account first.");
        lock(gate){credentials.Servers.RemoveAll(s=>s.Kind==server.Kind&&s.BaseUrl.Equals(server.BaseUrl,StringComparison.OrdinalIgnoreCase)&&s.UserId==server.UserId);credentials.Servers.Add(server);SaveCredentials();}Changed?.Invoke();
    }
    public void RemoveServer(string id){lock(gate){credentials.Servers.RemoveAll(s=>s.Id==id);SaveCredentials();}Changed?.Invoke();}
    void SaveCredentials()=>Atomic(credentialsPath,ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(credentials),null,DataProtectionScope.CurrentUser));
    public void UseDownloadProfile(string manifest)
    {
        _=TorboxProvider.Manifest(manifest);
        lock(gate){if(credentials.OriginalManifest.Length==0)credentials.OriginalManifest=credentials.Manifest;credentials.Manifest=manifest;
            Atomic(credentialsPath,ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(credentials),null,DataProtectionScope.CurrentUser));}
    }
    public void SaveReleaseFilters(string quality,string language,bool readyOnly,bool sizeLimit)
    {
        lock(gate){credentials.BrowseQuality=quality;credentials.BrowseLanguage=language;credentials.BrowseReadyOnly=readyOnly;credentials.BrowseSizeLimit=sizeLimit;
            Atomic(credentialsPath,ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(credentials),null,DataProtectionScope.CurrentUser));}
    }
    public void Configure(string manifest,string key,string shows,string movies,string? resolution=null,long? maxEpisodeBytes=null,List<string>? subtitleLanguages=null,bool? autoSync=null,string? service=null,string? seeding=null,int? uploadLimitKB=null,int? downloadLimitKB=null,bool? autoSwitch=null,int? maxActiveTorrents=null)
    {
        service??=Config.Service;if(service is not ("TorBox" or "Real-Debrid" or "AIOStreams links" or "Torrents"))throw new IOException("Choose a download service.");
        if(!string.IsNullOrWhiteSpace(manifest))_=TorboxProvider.Manifest(manifest);if(service=="Torrents"&&string.IsNullOrWhiteSpace(manifest))throw new IOException(store.State.Language=="ar"?"أضف رابط تثبيت AIOStreams أو Torrentio.":"Add an AIOStreams or Torrentio installation link.");if(service is not ("AIOStreams links" or "Torrents")&&string.IsNullOrWhiteSpace(key)&&Config.Servers.Count==0)throw new IOException($"Enter your {service} API key.");
        foreach(var root in new[]{shows,movies}.Where(r=>r.Length>0))if(!store.State.Roots.Any(r=>r.Path.Equals(root,StringComparison.OrdinalIgnoreCase)))throw new IOException("Choose a folder already added to the library.");
        var c=Config;if(c.Service=="TorBox")c.TorBoxKey=c.Key;else if(c.Service=="Real-Debrid")c.RealDebridKey=c.Key;
        c.Service=service;c.Manifest=manifest.Trim();c.Key=service is "AIOStreams links" or "Torrents"?"":key.Trim();if(service=="TorBox")c.TorBoxKey=c.Key;else if(service=="Real-Debrid")c.RealDebridKey=c.Key;c.ShowsRoot=shows;c.MoviesRoot=movies;
        if(resolution!=null)c.PreferredResolution=resolution is "1080p" or "720p" or "2160p"?resolution:"Any";
        if(maxEpisodeBytes!=null)c.MaxEpisodeBytes=Math.Max(0,maxEpisodeBytes.Value);
        if(subtitleLanguages!=null)c.SubtitleLanguages=subtitleLanguages.Where(l=>l is "en" or "ar").Distinct().ToList();
        if(autoSync!=null)c.AutoSyncSubtitles=autoSync.Value;
        if(seeding!=null)c.Seeding=seeding is "off" or "open"?seeding:"ratio";
        if(uploadLimitKB!=null)c.UploadLimitKB=Math.Clamp(uploadLimitKB.Value,0,2097151);
        if(downloadLimitKB!=null)c.DownloadLimitKB=Math.Clamp(downloadLimitKB.Value,0,2097151);
        if(autoSwitch!=null)c.AutoSwitch=autoSwitch.Value;
        if(maxActiveTorrents!=null)c.MaxActiveTorrents=Math.Clamp(maxActiveTorrents.Value,1,20);
        Atomic(credentialsPath,ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(c),null,DataProtectionScope.CurrentUser));lock(gate)credentials=c;Changed?.Invoke();Signal();
    }
    public void ConfigureSubtitleSource(string link,bool only)
    {
        ConfigureSubtitleSource(new(link.Trim().Length>0?"addon":"",AddonUrl:link,Only:only));
    }
    public void ConfigureSubtitleSource(SubtitleSelection selection,Dictionary<string,string>? keys=null)
    {
        var s=selection.Validate();lock(gate){
            if(credentials.SubtitleSource is "opensubtitles" or "subdl" or "subsource" or "wyzie")credentials.SubtitleSiteKeys[credentials.SubtitleSource]=credentials.SubtitleApiKey;
            if(keys!=null)foreach(var pair in keys.Where(p=>p.Key is "opensubtitles" or "subdl" or "subsource" or "wyzie"))credentials.SubtitleSiteKeys[pair.Key]=pair.Value;
            if(s.Source is "opensubtitles" or "subdl" or "subsource" or "wyzie")credentials.SubtitleSiteKeys[s.Source]=s.ApiKey;
            credentials.SubtitleSource=s.Source;credentials.SubtitleApiKey=s.ApiKey;credentials.SubtitleUsername=s.Username;credentials.SubtitlePassword=s.Password;credentials.SubtitleAddonUrl=s.AddonUrl;credentials.SubtitleAddonAuth=s.AddonAuth;credentials.SubtitleAddonOnly=s.Only;SaveCredentials();
        }Changed?.Invoke();
    }
    public List<object> PublicJobs() {lock(gate)return jobs.Select(j=>(object)new{id=j.Id,name=j.Name,status=j.Status,message=j.Message,size=j.Size,bytes=j.Bytes,createdAt=j.CreatedAt,subtitleMessage=j.SubtitleMessage,peers=j.Peers,seeds=j.Seeds,health=j.Health,uploaded=j.Uploaded,seedUntil=j.SeedUntil}).ToList();}
    public List<PcDownloadJob> Jobs() {lock(gate){var copy=JsonSerializer.Deserialize<List<PcDownloadJob>>(JsonSerializer.Serialize(jobs))!;for(var i=0;i<copy.Count;i++){copy[i].Speed=jobs[i].Speed;copy[i].EtaSeconds=jobs[i].EtaSeconds;}return copy;}}
    // Free bytes on the drive that holds a download folder; -1 when the drive cannot be read.
    public long FreeBytesFor(string root){try{return FreeSpace(root);}catch(Exception){return -1;}}
#if NOIR_VALIDATE
    internal void Mutate(string id,Action<PcDownloadJob> edit){lock(gate){edit(jobs.First(j=>j.Id==id));}Changed?.Invoke();}
    internal void Inject(PcDownloadJob job){lock(gate){jobs.Add(job);}Changed?.Invoke();}
#endif
    public async Task<List<DownloadChoice>> Search(string itemId,bool season,CancellationToken ct=default)
    {
        var result=await SearchDetailed(itemId,season,null,ct);
        if(result.Choices.Count==0)throw new IOException(string.Join(" · ",result.Sources.Select(s=>s.Source+": "+s.Message)));
        return result.Choices;
    }
    VideoItem Item(string itemId)=>store.Archive.FirstOrDefault(i=>i.Id==itemId)??store.Archive.Select(i=>i.GroupKey).Distinct().SelectMany(store.EpisodesFor).FirstOrDefault(i=>i.Id==itemId)??throw new IOException("Refresh the library to find this title.");
    List<VideoItem> Released(VideoItem item)=>store.EpisodesFor(item.GroupKey).Where(i=>i.IsMovie||i.Season==item.Season&&i.Episode>0&&!(store.State.EpisodeSchedules.GetValueOrDefault(item.GroupKey)?.Any(e=>e.Season==i.Season&&e.Episode==i.Episode&&e.AirsAt>DateTimeOffset.UtcNow)??false)).DistinctBy(i=>(i.Season,i.Episode)).ToList();
    bool OnPc(VideoItem item)=>store.Archive.Any(i=>i.GroupKey==item.GroupKey&&(i.IsMovie||i.Season==item.Season&&i.Episode==item.Episode)&&i.Available&&File.Exists(i.Path));
    bool IsQueued(VideoItem item){lock(gate)return jobs.Any(j=>j.Target.GroupKey==item.GroupKey&&(item.IsMovie||j.Target.Season==item.Season&&j.Target.Episode==item.Episode)&&j.Status is not ("Completed" or "Cancelled" or "Failed"));}
    public async Task<DownloadSearchResult> SearchDetailed(string itemId,bool season,IProgress<DownloadSourceStatus>? progress=null,CancellationToken ct=default)
        =>await SearchDetailedCore(itemId,season,progress,ct);
    async Task<DownloadSearchResult> SearchDetailedCore(string itemId,bool season,IProgress<DownloadSourceStatus>? progress,CancellationToken ct,string plannerSession="",bool indexSearched=false)
    {
        var item=Item(itemId);
        if(!Configured)throw new IOException("Choose a download service or connect a media server in PC Settings first.");
        if(Config.Manifest.Length>0&&!System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId,@"^tt\d+$")){
            progress?.Report(new(){Source="AIOStreams",Message="Checking title identity"});
            try{using var metadata=new MetadataClient(store.DataDirectory);var id=await metadata.DownloadIdentity(item,ct);if(id.Length>0){store.SetDownloadIdentity(item.GroupKey,id);item.ImdbId=id;}}
            catch(Exception e)when(e is HttpRequestException or JsonException||e is OperationCanceledException&&!ct.IsCancellationRequested){}
        }
        var missing=Released(item).Where(i=>!OnPc(i)&&!IsQueued(i)).ToList();if(missing.Count==0)missing=[item];
        var probes=season&&!item.IsMovie?Config.Service=="AIOStreams links"?Released(item).Where(i=>!OnPc(i)&&!IsQueued(i)).Take(40).ToList():new[]{missing[0],missing[missing.Count/2],missing[^1]}.DistinctBy(i=>i.Id).ToList():new List<VideoItem>{item};
        var result=indexSearched&&provider is DownloadProviders router?await router.Discover(item,season&&!item.IsMovie,probes,progress,ct,true):await provider.Discover(item,season&&!item.IsMovie,probes,progress,ct);var found=result.Releases;
        var c=Config;
        foreach(var r in found){if(r.Choice.Resolution.Length==0)r.Choice.Resolution=TorboxProvider.Resolution(r.Choice.Name+" "+r.Choice.Detail);r.Choice.Recommendation=r.Choice.Resolution==c.PreferredResolution?"Matches preferred quality":r.Choice.Cached?"Ready to download":"";}
        found=found.OrderByDescending(r=>c.PreferredResolution=="Any"||r.Choice.Resolution==c.PreferredResolution).ThenByDescending(r=>r.Choice.Cached).ThenBy(r=>r.Choice.Size>0?r.Choice.Size:long.MaxValue).ToList();
        lock(gate)
        {
            foreach(var id in releases.Where(r=>r.Value.Expires<DateTimeOffset.UtcNow).Select(r=>r.Key).ToList()){releases.Remove(id);foreach(var f in choices.Where(c=>c.Value.Release==id).Select(c=>c.Key).ToList())choices.Remove(f);}
            // Bound old search sessions while retaining every result of the current search.
            var limit=Math.Max(20000,found.Count);
            var retained=seasonPlans.Values.Where(p=>p.Expires>DateTimeOffset.UtcNow).SelectMany(p=>p.Plan.Episodes).SelectMany(e=>e.Alternatives).Select(o=>o.ReleaseId).Concat(plannerSearches.Values.SelectMany(ids=>ids)).ToHashSet();
            foreach(var old in releases.OrderBy(r=>r.Value.Expires).Select(r=>r.Key).ToList()){
                if(releases.Count+found.Count<=limit)break;if(retained.Contains(old))continue;releases.Remove(old);foreach(var f in choices.Where(c=>c.Value.Release==old).Select(c=>c.Key).ToList())choices.Remove(f);
            }
            foreach(var r in found)releases[r.Choice.Id]=(r,item,season&&!item.IsMovie,DateTimeOffset.UtcNow.AddMinutes(20),r.Service.Length>0?r.Service:c.Service);
            if(plannerSearches.TryGetValue(plannerSession,out var pinned))pinned.UnionWith(found.Select(r=>r.Choice.Id));
        }
        var publicChoices=found.Select(r=>r.Choice).ToList();
        return new(){Choices=publicChoices,Sources=result.Sources,Groups=ReleasePresentation.Group(publicChoices,c.PreferredResolution)};
    }
    public async Task<List<DownloadFileChoice>> Files(string releaseId,CancellationToken ct=default)
    {
        (IndexedRelease Release,VideoItem Item,bool Season,DateTimeOffset Expires,string Service) r;
        lock(gate)if(!releases.TryGetValue(releaseId,out r)||r.Expires<DateTimeOffset.UtcNow)throw new IOException("These choices expired. Search again.");
        var active=provider is DownloadProviders router?router.ForService(r.Service):provider;
        var all=r.Release.Direct is {} direct?direct.Select((d,i)=>new TorboxFile(i,d.Name,d.Size)).ToList():await active.Files(r.Release.Hash,ct);var videos=all.Where(f=>DownloadNaming.Video(f.Name)&&!System.Text.RegularExpressions.Regex.IsMatch(f.Name,@"(?i)(?:^|[/\\ ._-])sample(?:[/\\ ._-]|$)")).ToList();
        var result=new List<DownloadFileChoice>();
        foreach(var f in videos)
        {
            var source=r.Release.Direct?.ElementAt((int)f.Id);var number=source!=null?(source.Season,source.Episode):DownloadNaming.Episode(f.Name,r.Item.Season);if(number.Episode==0&&videos.Count==1&&!r.Season)number=(r.Item.Season,r.Item.Episode);
            var multi=System.Text.RegularExpressions.Regex.IsMatch(f.Name,@"(?i)s\d{1,2}[ ._-]*e\d{1,3}(?:[ ._-]*e\d|[ ._-]*-[ ._-]*(?:e)?\d)");
            var selected=r.Item.IsMovie||number.Episode>0&&number.Season==r.Item.Season&&(r.Season||number.Episode==r.Item.Episode);
            var existing=store.Archive.Any(i=>i.GroupKey==r.Item.GroupKey&&(i.IsMovie||i.Season==number.Season&&i.Episode==number.Episode)&&i.Available&&File.Exists(i.Path));
            var target=new VideoItem{CollectionKey=r.Item.GroupKey,Season=number.Season,Episode=number.Episode,IsMovie=r.Item.IsMovie};
            var queued=IsQueued(target);var future=store.State.EpisodeSchedules.GetValueOrDefault(r.Item.GroupKey)?.Any(e=>e.Season==number.Season&&e.Episode==number.Episode&&e.AirsAt>DateTimeOffset.UtcNow)??false;
            var reason=existing?"Already on PC":queued?"Already queued":future?"Not released yet":multi?"Multi-episode file · needs manual organization":number.Episode==0&&!r.Item.IsMovie?"Numbering unverified · match an episode":!selected?"Different episode":"";
            var choice=new DownloadFileChoice{Id=Guid.NewGuid().ToString("N"),Name=f.Name,Size=f.Size,Season=number.Season,Episode=number.Episode,Selectable=selected&&!existing&&!queued&&!future&&!multi,Existing=existing,Queued=queued,Future=future,Reason=reason,MappingRequired=number.Episode==0&&!r.Item.IsMovie&&!multi};
            lock(gate)choices[choice.Id]=(releaseId,choice,source);result.Add(choice);
        }
        return result;
    }
    public DownloadFileChoice MapFile(string fileId,int episode)
    {
        lock(gate){
            if(!choices.TryGetValue(fileId,out var found)||!found.Choice.MappingRequired||!releases.TryGetValue(found.Release,out var r)||r.Expires<DateTimeOffset.UtcNow)throw new IOException("This file doesn't need an episode match, or the choice expired.");
            var target=Released(r.Item).FirstOrDefault(i=>i.Episode==episode)??throw new IOException("Choose a known released episode from this season.");
            if(!r.Season&&target.Episode!=r.Item.Episode)throw new IOException("Choose the episode you searched for.");
            var f=found.Choice;f.Season=r.Item.Season;f.Episode=episode;f.Existing=OnPc(target);f.Queued=IsQueued(target);f.Selectable=!f.Existing&&!f.Queued;f.MappingRequired=false;f.Reason=f.Existing?"Already on PC":f.Queued?"Already queued":"Episode match confirmed";return f;
        }
    }
    // Existing phone route keeps its signature and response fields; richer fields are additive.
    public Task<DownloadPlan> Plan(string itemId,List<string> releaseIds,CancellationToken ct=default)
        =>PlanSeason(itemId,new DownloadPlanRequest{ReleaseIds=releaseIds},ct:ct);
    public List<string> Enqueue(DownloadRequest request)
        =>EnqueueCore(request,200);
    List<string> EnqueueCore(DownloadRequest request,int? maximumFiles)
    {
        if(!Guid.TryParse(request.RequestId,out _))throw new IOException("Invalid request ID.");
        lock(gate)
        {
            var prior=jobs.Where(j=>j.RequestId==request.RequestId).Select(j=>j.Id).ToList();if(prior.Count>0)return prior;
            var selections=request.Selections.Count>0?request.Selections:new List<DownloadSelection>{new(){ReleaseId=request.ReleaseId,FileIds=request.FileIds}};
            var count=selections.Sum(s=>s.FileIds.Count);
            if(count<1||maximumFiles is {} maximum&&count>maximum)throw new IOException(maximumFiles==null?"Select at least one episode file.":"Select between 1 and 200 episode files.");
            var pending=new List<PcDownloadJob>();var settings=Config;
            foreach(var selection in selections)
            {
            if(!releases.TryGetValue(selection.ReleaseId,out var r)||r.Expires<DateTimeOffset.UtcNow)throw new IOException("These choices expired. Search again.");
            var root=Root(r.Item);
            foreach(var id in selection.FileIds.Distinct())
            {
                if(!choices.TryGetValue(id,out var f)||f.Release!=selection.ReleaseId||!f.Choice.Selectable)throw new IOException("Choose a verified episode file that isn't already on the PC.");
                var c=f.Choice;
                var item=store.EpisodesFor(r.Item.GroupKey).FirstOrDefault(i=>i.IsMovie||i.Season==c.Season&&i.Episode==c.Episode);
                if(item==null){item=JsonSerializer.Deserialize<VideoItem>(JsonSerializer.Serialize(r.Item))!;item.Id=Guid.NewGuid().ToString("N");item.Season=c.Season;item.Episode=c.Episode;item.EpisodeTitle="";item.EpisodeSummary="";item.EpisodeImage="";item.EpisodeImdbId="";item.Path="";item.Available=false;}
                var path=DownloadNaming.Destination(root,item,c.Name);
                if(OnPc(item)||IsQueued(item)||File.Exists(path)||pending.Any(j=>j.Target.GroupKey==item.GroupKey&&j.Target.Season==item.Season&&j.Target.Episode==item.Episode)||jobs.Any(j=>j.Destination.Equals(path,StringComparison.OrdinalIgnoreCase)&&j.Status is not ("Cancelled" or "Failed" or "Completed")))throw new IOException("An episode is already saved or queued. Choose one release per episode.");
                if(!item.IsMovie&&Future(item))throw new IOException("This episode has not aired yet.");
                pending.Add(new(){RequestId=request.RequestId,Name=item.Label,Target=item,Hash=r.Release.Hash,Service=r.Service,ProtectedDirectSource=f.Direct==null?"":DownloadSecurity.Protect(f.Direct),RemoteName=c.Name,Root=root,Destination=path,Size=c.Size,SubtitleLanguages=settings.SubtitleLanguages.ToList(),AutoSyncSubtitles=settings.AutoSyncSubtitles});
            }
            }
            CheckCapacity(pending);
            // Starting a download puts its title in the library.
            foreach(var key in pending.Select(j=>j.Target.GroupKey).Distinct())store.SetInLibrary(key,true);
            jobs.AddRange(pending);Save();Signal();Changed?.Invoke();return pending.Select(j=>j.Id).ToList();
        }
    }
    string Root(VideoItem item)
    {
        var c=Config;var root=item.IsMovie?c.MoviesRoot:c.ShowsRoot;
        if(root.Length==0)root=store.State.Roots.Where(r=>r.Movies==item.IsMovie).OrderByDescending(r=>item.Path.StartsWith(r.Path,StringComparison.OrdinalIgnoreCase)).FirstOrDefault()?.Path??"";
        if(root.Length==0||!Directory.Exists(root))throw new IOException("Choose an existing download folder in PC Settings.");return root;
    }
    public void Control(string id,string action)
    {
        lock(gate)
        {
            var j=jobs.FirstOrDefault(j=>j.Id==id)??throw new IOException("Download not found.");if(j.Status=="Completed")return;
            if(action=="pause"||action=="cancel"){j.Status=action=="pause"?"Paused":"Cancelled";j.Speed=0;j.EtaSeconds=-1;running.GetValueOrDefault(id)?.Cancel();TorrentStop(j,false);}
            else if(action=="resume"){if(j.Status is "Downloading" or "Subtitles")return;j.Status="Queued";j.Message="";j.NextAttempt=null;}
            else throw new IOException("Unknown download action.");Save();Signal();Changed?.Invoke();
        }
    }
    void Signal(){try{wake.Release();}catch(SemaphoreFullException){}catch(ObjectDisposedException){}}
    async Task Worker()
    {
        while(!stop.IsCancellationRequested)
        {
            PcDownloadJob? job;lock(gate)job=jobs.FirstOrDefault(j=>j.Service!="Torrents"&&(j.Status is "Queued" or "Preparing" or "Subtitles")&&(j.NextAttempt==null||j.NextAttempt<=DateTimeOffset.UtcNow));
            if(job==null){TimeSpan delay;lock(gate){var next=jobs.Where(j=>j.Service!="Torrents"&&(j.Status is "Queued" or "Preparing" or "Subtitles")).Select(j=>j.NextAttempt).OfType<DateTimeOffset>().Order().FirstOrDefault();delay=next==default?Timeout.InfiniteTimeSpan:TimeSpan.FromMilliseconds(Math.Clamp((next-DateTimeOffset.UtcNow).TotalMilliseconds,0,int.MaxValue));}try{await wake.WaitAsync(delay,stop.Token);}catch(OperationCanceledException){break;}continue;}
            using var ct=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);lock(gate)running[job.Id]=ct;
            try
            {
                if(SavedVideo(job)){await FinishSubtitles(job,[],ct.Token);continue;}
                if(job.ProtectedDirectSource.Length>0){var source=DownloadSecurity.Unprotect(job.ProtectedDirectSource);Update(job,"Downloading","");await Transfer(job,source.Url,ct.Token,source.Headers,source.ServerId);await FinishSubtitles(job,[],ct.Token);continue;}
                var active=provider is DownloadProviders router?router.ForService(job.Service):provider;
                var cloud=await active.Prepare(job.Hash,job.TorrentId,ct.Token);
                lock(gate){job.TorrentId=cloud.Id;foreach(var other in jobs.Where(j=>j.Hash==job.Hash&&j.Service==job.Service))other.TorrentId=cloud.Id;Save();}
                if(!cloud.Ready){lock(gate)job.NextAttempt=DateTimeOffset.UtcNow.AddSeconds(20);Update(job,"Preparing",job.Service+" is fetching the release");continue;}
                // Cached file IDs and owned-torrent file IDs can differ. Match verified name/size again.
                var matches=cloud.Files.Where(f=>f.Name.Replace('\\','/')==job.RemoteName.Replace('\\','/')&&(job.Size<=0||f.Size==job.Size)).ToList();
                if(matches.Count==0)matches=cloud.Files.Where(f=>f.Name.Replace('\\','/').Split('/').Last()==job.RemoteName.Replace('\\','/').Split('/').Last()&&(job.Size<=0||f.Size==job.Size)).ToList();
                if(matches.Count==0)matches=cloud.Files.Where(f=>f.Name.Replace('\\','/').Split('/').Last().Equals(job.RemoteName.Replace('\\','/').Split('/').Last(),StringComparison.OrdinalIgnoreCase)).ToList();
                if(matches.Count!=1)throw new IOException("The service's file list changed. Search and select this episode again.");
                job.FileId=matches[0].Id;job.Size=matches[0].Size;var link=await active.Link(cloud.Id,job.FileId,ct.Token);
                ct.Token.ThrowIfCancellationRequested();Update(job,"Downloading","");await Transfer(job,link,ct.Token);
                Update(job,"Subtitles","Video saved · checking release subtitles");
                var assets=new List<SubtitleAsset>();
                foreach(var f in cloud.Files.Where(f=>SubtitleDownloads.TextSubtitle(f.Name)&&f.Size<4*1024*1024)){
                    var language=SubtitleDownloads.SidecarLanguage(f.Name);var number=DownloadNaming.Episode(f.Name,job.Target.Season);
                    if(language.Length==0||!job.SubtitleLanguages.Contains(language)||(!job.Target.IsMovie&&(number.Season!=job.Target.Season||number.Episode!=job.Target.Episode)))continue;
                    if(job.Target.IsMovie&&Path.GetFileNameWithoutExtension(f.Name).Split('.').FirstOrDefault()!=Path.GetFileNameWithoutExtension(job.RemoteName).Split('.').FirstOrDefault())continue;
                    try{assets.Add(new(language,f.Name,await active.Link(cloud.Id,f.Id,ct.Token)));}catch(IOException){}
                }
                await FinishSubtitles(job,assets,ct.Token);
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){lock(gate){if(job.Status is "Downloading" or "Preparing"||stop.IsCancellationRequested&&job.Status=="Queued")job.Status="Paused";Save();}}
            catch(Exception e){var secrets=new List<string>();if(job.ProtectedDirectSource.Length>0)try{secrets.AddRange(DownloadSecurity.SourceSecrets(DownloadSecurity.Unprotect(job.ProtectedDirectSource)));}catch{}var message=DownloadSecurity.SafeText(e.Message,Config,secrets).Trim();if(e is not (IOException or HttpRequestException or TimeoutException or OperationCanceledException))try{store.Log(new Exception(DownloadSecurity.SafeText(e.ToString(),Config,secrets)));}catch{}Update(job,"Failed",message.Length>0?message:store.State.Language=="ar"?"توقف التنزيل. تحقق من الاتصال والمصدر ومساحة القرص ثم استأنف.":"Download stopped. Check connection, source availability and disk space, then Resume.");}
            finally {lock(gate){running.Remove(job.Id);job.Speed=0;job.EtaSeconds=-1;}Changed?.Invoke();}
        }
    }
    bool SavedVideo(PcDownloadJob job)=>job.Size>0&&File.Exists(job.Destination)&&new FileInfo(job.Destination).Length==job.Size&&store.Archive.Any(i=>i.Id==job.Target.Id&&i.Available&&i.Path.Equals(job.Destination,StringComparison.OrdinalIgnoreCase));
    async Task FinishSubtitles(PcDownloadJob job,List<SubtitleAsset> assets,CancellationToken ct)
    {
        job.Target.Path=job.Destination;job.Target.Available=true;
        if(job.SubtitleLanguages.Count>0){
            Update(job,"Subtitles","Video saved · finding English / Arabic subtitles");
            try{job.SubtitleMessage=await subtitles.Fetch(job.Target,job.RemoteName,job.SubtitleLanguages,job.AutoSyncSubtitles,ct,assets,skipEmbedded:true);}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){if(stop.IsCancellationRequested)throw;job.SubtitleMessage="Video saved · subtitles interrupted; retry from the subtitle menu";}
            catch(Exception){job.SubtitleMessage="Video saved · subtitles unavailable; retry from the subtitle menu";}
        }
        lock(gate){job.Status="Completed";job.Message="Saved to the PC library";Save();}Completed?.Invoke();
    }
    public async Task Transfer(PcDownloadJob job,string link,CancellationToken ct,IReadOnlyDictionary<string,string>? headers=null,string serverId="")
    {
        for(int attempt=0;;attempt++)try{await TransferOnce(job,link,ct,headers,serverId);return;}
        catch(Exception e)when(attempt<2&&!ct.IsCancellationRequested&&(e is TimeoutException or TransferReadException||e is HttpRequestException h&&(h.StatusCode==null||h.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests||(int)h.StatusCode>=500))){await Task.Delay(TimeSpan.FromSeconds(2*(attempt+1)),ct);}
    }
    sealed class TransferReadException(string message,Exception? inner=null):IOException(message,inner);
    async Task TransferOnce(PcDownloadJob job,string link,CancellationToken ct,IReadOnlyDictionary<string,string>? headers,string serverId)
    {
        var server=Config.Servers.FirstOrDefault(s=>s.Id==serverId);if(!DownloadSecurity.ValidUrl(link,server?.BaseUrl))throw new IOException("The source returned an invalid download link.");if(headers!=null)DownloadSecurity.ValidateHeaders(headers);
        DownloadNaming.Validate(job.Root,job.Destination);if(File.Exists(job.Destination))throw new IOException("The episode already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(job.Destination)!);
        var part=Path.Combine(parts,job.Id+".part");long start=File.Exists(part)?new FileInfo(part).Length:0;
        var drive=new DriveInfo(Path.GetPathRoot(job.Destination)!);if(job.Size>0&&drive.AvailableFreeSpace<Math.Max(0,job.Size-start)+64*1024*1024)throw new IOException("Not enough free space.");
        if(start>0&&job.ETag.Length==0)start=0;
        using var response=await Request(link,start,job.ETag,headers,server?.BaseUrl,ct);
        response.EnsureSuccessStatusCode();var range=response.Content.Headers.ContentRange;
        if(response.StatusCode==HttpStatusCode.PartialContent)
        {
            if(range?.From!=start||!range.HasLength||range.Length<=0||range.To<range.From||job.Size>0&&range.Length!=job.Size)throw new IOException("Invalid download range.");
            if(job.ETag.Length>0&&response.Headers.ETag?.ToString()!=job.ETag)throw new IOException("Download identity changed.");
        }
        else if(response.StatusCode==HttpStatusCode.OK)start=0;else throw new IOException("Invalid download response.");
        long expected=response.StatusCode==HttpStatusCode.PartialContent?range!.Length!.Value:response.Content.Headers.ContentLength??job.Size;
        if(expected<=0||job.Size>0&&expected!=job.Size)throw new IOException("The download size could not be verified.");
        if(FreeSpace(job.Root)<Math.Max(0,expected-start)+64L*1024*1024)throw new IOException("Not enough free space.");
        job.Size=expected;var etag=response.Headers.ETag?.ToString()??"";job.ETag=DownloadSecurity.SafeText(etag,Config,DownloadSecurity.SourceSecrets(new(){Url=link,Headers=headers?.ToDictionary(h=>h.Key,h=>h.Value)??[]}))==etag?etag:"";job.Bytes=start;
        await using(var output=new FileStream(part,start>0?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.Read,65536,true))
        await using(var input=await response.Content.ReadAsStreamAsync(ct))
        {
            var buf=new byte[131072];var saved=DateTime.UtcNow;var meter=new SpeedMeter();var sampled=saved;meter.Add(saved,job.Bytes);
            using var idle=CancellationTokenSource.CreateLinkedTokenSource(ct);
            while(true)
            {
                int count;idle.CancelAfter(TimeSpan.FromSeconds(60));
                try{count=await input.ReadAsync(buf,idle.Token);}
                catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new TimeoutException(store.State.Language=="ar"?"لم تصل بيانات التنزيل لمدة 60 ثانية. أعد المحاولة.":"No download data arrived for 60 seconds. Retry the download.");}
                catch(IOException e){throw new TransferReadException(e.Message,e);}
                finally{idle.CancelAfter(Timeout.InfiniteTimeSpan);}
                if(count==0)break;
                if(job.Bytes+count>expected)throw new IOException("Download exceeded its expected size.");
                await output.WriteAsync(buf.AsMemory(0,count),ct);job.Bytes+=count;
                var now=DateTime.UtcNow;if(now-sampled>=TimeSpan.FromMilliseconds(250)){sampled=now;job.Speed=meter.Add(now,job.Bytes);job.EtaSeconds=DownloadEta.Seconds(job.Size,job.Bytes,job.Speed);}
                if(DateTime.UtcNow-saved>TimeSpan.FromSeconds(1)){lock(gate)if(DateTime.UtcNow-progressSaved>=TimeSpan.FromSeconds(10))Save();Changed?.Invoke();saved=DateTime.UtcNow;}
            }
            await output.FlushAsync(ct);output.Flush(true);job.Speed=0;job.EtaSeconds=-1;
        }
        ct.ThrowIfCancellationRequested();if(new FileInfo(part).Length!=expected)throw new TransferReadException(store.State.Language=="ar"?"التنزيل غير مكتمل.":"The download is incomplete.");
        DownloadNaming.Validate(job.Root,job.Destination);File.Move(part,job.Destination,false);store.RegisterDownload(job.Target,job.Destination);
    }
    async Task<HttpResponseMessage> Request(string link,long start,string etag,IReadOnlyDictionary<string,string>? headers,string? serverBase,CancellationToken ct)
    {
        var origin=new Uri(link);
        for(var hop=0;hop<6;hop++){
            if(!DownloadSecurity.ValidUrl(link,serverBase))throw new IOException("The source returned an invalid download redirect.");
            using var request=new HttpRequestMessage(HttpMethod.Get,link);var uri=new Uri(link);
            if(headers!=null&&uri.Scheme==origin.Scheme&&uri.Authority==origin.Authority)foreach(var h in headers)request.Headers.TryAddWithoutValidation(h.Key,h.Value);
            if(start>0){request.Headers.Range=new RangeHeaderValue(start,null);request.Headers.TryAddWithoutValidation("If-Range",etag);}
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(60));HttpResponseMessage response;
            try{response=await transfer.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);}catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new TimeoutException(store.State.Language=="ar"?"انتهت مهلة اتصال التنزيل. أعد المحاولة.":"Download connection timed out. Retry the download.");}
            if((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))return response;
            var location=response.Headers.Location;response.Dispose();if(location==null)throw new IOException("The download redirect was incomplete.");link=new Uri(uri,location).AbsoluteUri;
        }
        throw new IOException("The download returned too many redirects.");
    }
    void Update(PcDownloadJob j,string status,string message){lock(gate){if(j.Status is "Paused" or "Cancelled")return;j.Status=status;j.Message=message;j.Speed=0;j.EtaSeconds=-1;Save();}Changed?.Invoke();}
    void Save(){Atomic(jobsPath,JsonSerializer.SerializeToUtf8Bytes(jobs,Json));progressSaved=DateTime.UtcNow;}
    static void Atomic(string path,byte[] bytes){var temp=path+".tmp";using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){f.Write(bytes);f.Flush(true);}for(int attempt=0;;attempt++){try{File.Move(temp,path,true);break;}catch(Exception e)when(attempt<5&&(e is IOException or UnauthorizedAccessException)){Thread.Sleep(40*(attempt+1));}}}
    public void Dispose(){stop.Cancel();Signal();DisposeTorrents();}
}
public sealed partial class LibraryStore
{
    public void SetDownloadIdentity(string group,string imdb)
    {
        lock(gate){foreach(var e in EntriesFor(group).Where(e=>e.Item.ImdbId.Length==0))e.Item.ImdbId=imdb;Save();}
    }
    internal Action? RegisterDownloadLocked {get;set;}
    public void RegisterDownload(VideoItem item,string path)
    {
        lock(gate)
        {
            if(item.Id.Length==0)item.Id=Guid.NewGuid().ToString("N");
            if(!State.Catalog.TryGetValue(item.Id,out var entry)){item.Path=path;item.Available=true;entry=new(){Item=item,Fingerprint=Fingerprint(path)};State.Catalog[item.Id]=entry;}
            entry.Item.Path=path;entry.Item.Available=true;if(!entry.Paths.Contains(path,StringComparer.OrdinalIgnoreCase))entry.Paths.Add(path);
            RegisterDownloadLocked?.Invoke();
        }
        Scan(out _);Save();
    }
}
