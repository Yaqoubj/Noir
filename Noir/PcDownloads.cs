using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Noir;

public sealed class PcDownloads : IDisposable
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
    readonly Dictionary<string,(IndexedRelease Release,VideoItem Item,bool Season,DateTimeOffset Expires)> releases=[];
    readonly Dictionary<string,(string Release,DownloadFileChoice Choice)> choices=[];
    readonly Dictionary<string,CancellationTokenSource> running=[];
    public event Action? Changed;
    public event Action? Completed;
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public PcDownloads(LibraryStore store,IDownloadProvider? provider=null,HttpClient? transfer=null,bool start=true,SubtitleDownloads? subtitles=null)
    {
        this.store=store;credentialsPath=Path.Combine(store.DataDirectory,"downloads-settings.dpapi");jobsPath=Path.Combine(store.DataDirectory,"pc-downloads.json");parts=Path.Combine(store.DataDirectory,"download-parts");Directory.CreateDirectory(parts);
        if(File.Exists(credentialsPath))credentials=JsonSerializer.Deserialize<DownloadCredentials>(ProtectedData.Unprotect(File.ReadAllBytes(credentialsPath),null,DataProtectionScope.CurrentUser))??new();
        if(File.Exists(jobsPath))jobs=JsonSerializer.Deserialize<List<PcDownloadJob>>(File.ReadAllText(jobsPath),Json)??[];
        foreach(var j in jobs.Where(j=>j.Status=="Downloading")){j.Status="Paused";j.Message="Interrupted · resume to continue";}
        this.provider=provider??new TorboxProvider(()=>Config);this.transfer=transfer??new HttpClient {Timeout=Timeout.InfiniteTimeSpan};
        this.subtitles=subtitles??new SubtitleDownloads(store.DataDirectory);
        if(start)_=Task.Run(Worker);
    }
    public DownloadCredentials Config {get {lock(gate)return JsonSerializer.Deserialize<DownloadCredentials>(JsonSerializer.Serialize(credentials))!;}}
    public bool Configured=>Config.Key.Length>0;
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
    public void Configure(string manifest,string key,string shows,string movies,string? resolution=null,long? maxEpisodeBytes=null,List<string>? subtitleLanguages=null,bool? autoSync=null)
    {
        if(!string.IsNullOrWhiteSpace(manifest))_=TorboxProvider.Manifest(manifest);if(string.IsNullOrWhiteSpace(key))throw new IOException("Enter your TorBox API key.");
        foreach(var root in new[]{shows,movies}.Where(r=>r.Length>0))if(!store.State.Roots.Any(r=>r.Path.Equals(root,StringComparison.OrdinalIgnoreCase)))throw new IOException("Choose a folder already added to the library.");
        var c=Config;c.Manifest=manifest.Trim();c.Key=key.Trim();c.ShowsRoot=shows;c.MoviesRoot=movies;
        if(resolution!=null)c.PreferredResolution=resolution is "1080p" or "720p" or "2160p"?resolution:"Any";
        if(maxEpisodeBytes!=null)c.MaxEpisodeBytes=Math.Max(0,maxEpisodeBytes.Value);
        if(subtitleLanguages!=null)c.SubtitleLanguages=subtitleLanguages.Where(l=>l is "en" or "ar").Distinct().ToList();
        if(autoSync!=null)c.AutoSyncSubtitles=autoSync.Value;
        Atomic(credentialsPath,ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(c),null,DataProtectionScope.CurrentUser));lock(gate)credentials=c;Changed?.Invoke();Signal();
    }
    public List<object> PublicJobs() {lock(gate)return jobs.Select(j=>(object)new{id=j.Id,name=j.Name,status=j.Status,message=j.Message,size=j.Size,bytes=j.Bytes,createdAt=j.CreatedAt,subtitleMessage=j.SubtitleMessage}).ToList();}
    public List<PcDownloadJob> Jobs() {lock(gate)return JsonSerializer.Deserialize<List<PcDownloadJob>>(JsonSerializer.Serialize(jobs))!;}
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
    {
        var item=Item(itemId);
        if(!Configured)throw new IOException("Set up TorBox in PC Settings first. AIOStreams is optional for finding new releases.");
        if(Config.Manifest.Length>0&&!System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId,@"^tt\d+$")){
            progress?.Report(new(){Source="AIOStreams",Message="Checking title identity"});
            try{using var metadata=new MetadataClient(store.DataDirectory);var id=await metadata.DownloadIdentity(item,ct);if(id.Length>0){store.SetDownloadIdentity(item.GroupKey,id);item.ImdbId=id;}}
            catch(Exception e)when(e is HttpRequestException or JsonException||e is OperationCanceledException&&!ct.IsCancellationRequested){}
        }
        var missing=Released(item).Where(i=>!OnPc(i)&&!IsQueued(i)).ToList();if(missing.Count==0)missing=[item];
        var probes=season&&!item.IsMovie?new[]{missing[0],missing[missing.Count/2],missing[^1]}.DistinctBy(i=>i.Id).ToList():new List<VideoItem>{item};
        var result=await provider.Discover(item,season&&!item.IsMovie,probes,progress,ct);var found=result.Releases;
        var c=Config;
        foreach(var r in found){if(r.Choice.Resolution.Length==0)r.Choice.Resolution=TorboxProvider.Resolution(r.Choice.Name+" "+r.Choice.Detail);r.Choice.Recommendation=r.Choice.Resolution==c.PreferredResolution?"Matches preferred quality":r.Choice.Cached?"Ready on TorBox":"";}
        found=found.OrderByDescending(r=>c.PreferredResolution=="Any"||r.Choice.Resolution==c.PreferredResolution).ThenByDescending(r=>r.Choice.Cached).ThenBy(r=>r.Choice.Size>0?r.Choice.Size:long.MaxValue).ToList();
        lock(gate)
        {
            foreach(var id in releases.Where(r=>r.Value.Expires<DateTimeOffset.UtcNow).Select(r=>r.Key).ToList()){releases.Remove(id);foreach(var f in choices.Where(c=>c.Value.Release==id).Select(c=>c.Key).ToList())choices.Remove(f);}
            // Bound old search sessions while retaining every result of the current search.
            var limit=Math.Max(20000,found.Count);
            foreach(var old in releases.OrderBy(r=>r.Value.Expires).Select(r=>r.Key).ToList()){
                if(releases.Count+found.Count<=limit)break;releases.Remove(old);foreach(var f in choices.Where(c=>c.Value.Release==old).Select(c=>c.Key).ToList())choices.Remove(f);
            }
            foreach(var r in found)releases[r.Choice.Id]=(r,item,season&&!item.IsMovie,DateTimeOffset.UtcNow.AddMinutes(20));
        }
        var publicChoices=found.Select(r=>r.Choice).ToList();
        return new(){Choices=publicChoices,Sources=result.Sources,Groups=ReleasePresentation.Group(publicChoices,c.PreferredResolution)};
    }
    public async Task<List<DownloadFileChoice>> Files(string releaseId,CancellationToken ct=default)
    {
        (IndexedRelease Release,VideoItem Item,bool Season,DateTimeOffset Expires) r;
        lock(gate)if(!releases.TryGetValue(releaseId,out r)||r.Expires<DateTimeOffset.UtcNow)throw new IOException("These choices expired. Search again.");
        var all=await provider.Files(r.Release.Hash,ct);var videos=all.Where(f=>DownloadNaming.Video(f.Name)&&!System.Text.RegularExpressions.Regex.IsMatch(f.Name,@"(?i)(?:^|[/\\ ._-])sample(?:[/\\ ._-]|$)")).ToList();
        var result=new List<DownloadFileChoice>();
        foreach(var f in videos)
        {
            var number=DownloadNaming.Episode(f.Name,r.Item.Season);if(number.Episode==0&&videos.Count==1&&!r.Season)number=(r.Item.Season,r.Item.Episode);
            var multi=System.Text.RegularExpressions.Regex.IsMatch(f.Name,@"(?i)s\d{1,2}[ ._-]*e\d{1,3}(?:[ ._-]*e\d|[ ._-]*-[ ._-]*(?:e)?\d)");
            var selected=r.Item.IsMovie||number.Episode>0&&number.Season==r.Item.Season&&(r.Season||number.Episode==r.Item.Episode);
            var existing=store.Archive.Any(i=>i.GroupKey==r.Item.GroupKey&&(i.IsMovie||i.Season==number.Season&&i.Episode==number.Episode)&&i.Available&&File.Exists(i.Path));
            var target=new VideoItem{CollectionKey=r.Item.GroupKey,Season=number.Season,Episode=number.Episode,IsMovie=r.Item.IsMovie};
            var queued=IsQueued(target);var future=store.State.EpisodeSchedules.GetValueOrDefault(r.Item.GroupKey)?.Any(e=>e.Season==number.Season&&e.Episode==number.Episode&&e.AirsAt>DateTimeOffset.UtcNow)??false;
            var reason=existing?"Already on PC":queued?"Already queued":future?"Not released yet":multi?"Multi-episode file · needs manual organization":number.Episode==0&&!r.Item.IsMovie?"Numbering unverified · match an episode":!selected?"Different episode":"";
            var choice=new DownloadFileChoice{Id=Guid.NewGuid().ToString("N"),Name=f.Name,Size=f.Size,Season=number.Season,Episode=number.Episode,Selectable=selected&&!existing&&!queued&&!future&&!multi,Existing=existing,Queued=queued,Future=future,Reason=reason,MappingRequired=number.Episode==0&&!r.Item.IsMovie&&!multi};
            lock(gate)choices[choice.Id]=(releaseId,choice);result.Add(choice);
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
    public async Task<DownloadPlan> Plan(string itemId,List<string> releaseIds,CancellationToken ct=default)
    {
        var item=Item(itemId);var all=Released(item);var wanted=all.Where(i=>!OnPc(i)&&!IsQueued(i)).Select(i=>i.Episode).ToHashSet();
        var plan=new DownloadPlan{ReleasedEpisodes=all.Count,ExistingEpisodes=all.Count(OnPc),QueuedEpisodes=all.Count(IsQueued),ScheduleKnown=store.State.EpisodeSchedules.GetValueOrDefault(item.GroupKey)?.Any(e=>e.Season==item.Season)??false};
        var options=new List<(DownloadChoice Release,List<DownloadFileChoice> Files)>();var errors=0;var c=Config;
        foreach(var id in releaseIds.Distinct().Take(8)){
            ct.ThrowIfCancellationRequested();DownloadChoice choice;
            lock(gate){if(!releases.TryGetValue(id,out var release)||release.Item.GroupKey!=item.GroupKey||release.Item.Season!=item.Season)continue;choice=release.Release.Choice;}
            if(c.PreferredResolution!="Any"&&choice.Resolution.Length>0&&choice.Resolution!=c.PreferredResolution)continue;
            try{var files=await Files(id,ct);options.Add((choice,files.Where(f=>f.Selectable&&wanted.Contains(f.Episode)&&(c.MaxEpisodeBytes<=0||f.Size>0&&f.Size<=c.MaxEpisodeBytes)).ToList()));}catch(IOException){errors++;}
        }
        while(wanted.Count>0){
            var best=options.OrderByDescending(o=>o.Files.Select(f=>f.Episode).Distinct().Count(wanted.Contains)).ThenByDescending(o=>o.Release.Cached).ThenBy(o=>o.Files.Sum(f=>f.Size)).FirstOrDefault();
            if(best.Files==null||!best.Files.Any(f=>wanted.Contains(f.Episode)))break;
            foreach(var f in best.Files.GroupBy(f=>f.Episode).Select(g=>g.First()).Where(f=>wanted.Contains(f.Episode)).ToList()){
                plan.Entries.Add(new(){ReleaseId=best.Release.Id,Release=best.Release.Name,File=f});wanted.Remove(f.Episode);
            }
            options.Remove(best);
        }
        plan.MissingEpisodes=wanted.Order().ToList();plan.SelectedBytes=plan.Entries.Sum(e=>e.File.Size);plan.HasUnknownSizes=plan.Entries.Any(e=>e.File.Size<=0);
        plan.Message=$"{plan.Entries.Count} episodes selected · {plan.ExistingEpisodes} on PC · {plan.QueuedEpisodes} queued"+(wanted.Count>0?$" · {wanted.Count} gaps":"")+(errors>0?$" · {errors} file lists unavailable":"")+(!plan.ScheduleKnown?" · Full episode schedule unavailable; coverage covers known episodes only":"");
        return plan;
    }
    public List<string> Enqueue(DownloadRequest request)
    {
        if(!Guid.TryParse(request.RequestId,out _))throw new IOException("Invalid request ID.");
        lock(gate)
        {
            var prior=jobs.Where(j=>j.RequestId==request.RequestId).Select(j=>j.Id).ToList();if(prior.Count>0)return prior;
            var selections=request.Selections.Count>0?request.Selections:new List<DownloadSelection>{new(){ReleaseId=request.ReleaseId,FileIds=request.FileIds}};
            if(selections.Sum(s=>s.FileIds.Count) is <1 or >200)throw new IOException("Select between 1 and 200 episode files.");
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
                if(OnPc(item)||File.Exists(path)||pending.Any(j=>j.Destination==path)||jobs.Any(j=>j.Destination.Equals(path,StringComparison.OrdinalIgnoreCase)&&j.Status is not ("Cancelled" or "Failed" or "Completed")))throw new IOException("An episode is already saved or queued. Choose one release per episode.");
                pending.Add(new(){RequestId=request.RequestId,Name=item.Label,Target=item,Hash=r.Release.Hash,RemoteName=c.Name,Root=root,Destination=path,Size=c.Size,SubtitleLanguages=settings.SubtitleLanguages.ToList(),AutoSyncSubtitles=settings.AutoSyncSubtitles});
            }
            }
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
            if(action=="pause"||action=="cancel"){j.Status=action=="pause"?"Paused":"Cancelled";running.GetValueOrDefault(id)?.Cancel();}
            else if(action=="resume"){if(j.Status is "Downloading" or "Subtitles")return;j.Status="Queued";j.Message="";j.NextAttempt=null;}
            else throw new IOException("Unknown download action.");Save();Signal();Changed?.Invoke();
        }
    }
    void Signal(){try{wake.Release();}catch(SemaphoreFullException){}catch(ObjectDisposedException){}}
    async Task Worker()
    {
        while(!stop.IsCancellationRequested)
        {
            PcDownloadJob? job;lock(gate)job=jobs.FirstOrDefault(j=>(j.Status is "Queued" or "Preparing" or "Subtitles")&&(j.NextAttempt==null||j.NextAttempt<=DateTimeOffset.UtcNow));
            if(job==null){try{await wake.WaitAsync(TimeSpan.FromSeconds(2),stop.Token);}catch(OperationCanceledException){break;}continue;}
            using var ct=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);lock(gate)running[job.Id]=ct;
            try
            {
                if(SavedVideo(job)){await FinishSubtitles(job,[],ct.Token);continue;}
                var cloud=await provider.Prepare(job.Hash,job.TorrentId,ct.Token);
                lock(gate){job.TorrentId=cloud.Id;foreach(var other in jobs.Where(j=>j.Hash==job.Hash))other.TorrentId=cloud.Id;Save();}
                if(!cloud.Ready){lock(gate)job.NextAttempt=DateTimeOffset.UtcNow.AddSeconds(20);Update(job,"Preparing","TorBox is fetching the release");continue;}
                // Cached file IDs and owned-torrent file IDs can differ. Match verified name/size again.
                var matches=cloud.Files.Where(f=>f.Name.Replace('\\','/')==job.RemoteName.Replace('\\','/')&&(job.Size<=0||f.Size==job.Size)).ToList();
                if(matches.Count==0)matches=cloud.Files.Where(f=>f.Name.Replace('\\','/').Split('/').Last()==job.RemoteName.Replace('\\','/').Split('/').Last()&&(job.Size<=0||f.Size==job.Size)).ToList();
                if(matches.Count!=1)throw new IOException("TorBox's file list changed. Search and select this episode again.");
                job.FileId=matches[0].Id;job.Size=matches[0].Size;var link=await provider.Link(cloud.Id,job.FileId,ct.Token);
                ct.Token.ThrowIfCancellationRequested();Update(job,"Downloading","");await Transfer(job,link,ct.Token);
                Update(job,"Subtitles","Video saved · checking release subtitles");
                var assets=new List<SubtitleAsset>();
                foreach(var f in cloud.Files.Where(f=>SubtitleDownloads.TextSubtitle(f.Name)&&f.Size<4*1024*1024)){
                    var language=SubtitleDownloads.SidecarLanguage(f.Name);var number=DownloadNaming.Episode(f.Name,job.Target.Season);
                    if(language.Length==0||!job.SubtitleLanguages.Contains(language)||(!job.Target.IsMovie&&(number.Season!=job.Target.Season||number.Episode!=job.Target.Episode)))continue;
                    if(job.Target.IsMovie&&Path.GetFileNameWithoutExtension(f.Name).Split('.').FirstOrDefault()!=Path.GetFileNameWithoutExtension(job.RemoteName).Split('.').FirstOrDefault())continue;
                    try{assets.Add(new(language,f.Name,await provider.Link(cloud.Id,f.Id,ct.Token)));}catch(IOException){}
                }
                await FinishSubtitles(job,assets,ct.Token);
            }
            catch(OperationCanceledException){lock(gate){if(job.Status is "Downloading" or "Preparing"||stop.IsCancellationRequested&&job.Status=="Queued")job.Status="Paused";Save();}}
            catch(Exception){Update(job,"Failed","Download stopped. Check connection, TorBox availability and disk space, then Resume. No completed file was overwritten.");}
            finally {lock(gate)running.Remove(job.Id);Changed?.Invoke();}
        }
    }
    bool SavedVideo(PcDownloadJob job)=>job.Size>0&&File.Exists(job.Destination)&&new FileInfo(job.Destination).Length==job.Size&&store.Archive.Any(i=>i.Id==job.Target.Id&&i.Available&&i.Path.Equals(job.Destination,StringComparison.OrdinalIgnoreCase));
    async Task FinishSubtitles(PcDownloadJob job,List<SubtitleAsset> assets,CancellationToken ct)
    {
        job.Target.Path=job.Destination;job.Target.Available=true;
        if(job.SubtitleLanguages.Count>0){
            Update(job,"Subtitles","Video saved · finding English / Arabic subtitles");
            try{job.SubtitleMessage=await subtitles.Fetch(job.Target,job.RemoteName,job.SubtitleLanguages,job.AutoSyncSubtitles,ct,assets);}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){if(stop.IsCancellationRequested)throw;job.SubtitleMessage="Video saved · subtitles interrupted; retry from the subtitle menu";}
            catch(Exception){job.SubtitleMessage="Video saved · subtitles unavailable; retry from the subtitle menu";}
        }
        lock(gate){job.Status="Completed";job.Message="Saved to the PC library";Save();}Completed?.Invoke();
    }
    public async Task Transfer(PcDownloadJob job,string link,CancellationToken ct)
    {
        DownloadNaming.Validate(job.Root,job.Destination);if(File.Exists(job.Destination))throw new IOException("The episode already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(job.Destination)!);
        var part=Path.Combine(parts,job.Id+".part");long start=File.Exists(part)?new FileInfo(part).Length:0;
        var drive=new DriveInfo(Path.GetPathRoot(job.Destination)!);if(job.Size>0&&drive.AvailableFreeSpace<Math.Max(0,job.Size-start)+64*1024*1024)throw new IOException("Not enough free space.");
        using var request=new HttpRequestMessage(HttpMethod.Get,link);
        if(start>0){request.Headers.Range=new RangeHeaderValue(start,null);if(job.ETag.Length>0)request.Headers.TryAddWithoutValidation("If-Range",job.ETag);else start=0;}
        if(start==0)request.Headers.Range=null;
        using var response=await transfer.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        response.EnsureSuccessStatusCode();var range=response.Content.Headers.ContentRange;
        if(response.StatusCode==HttpStatusCode.PartialContent)
        {
            if(range?.From!=start||!range.HasLength||range.Length<=0||range.To<range.From||job.Size>0&&range.Length!=job.Size)throw new IOException("Invalid download range.");
            if(job.ETag.Length>0&&response.Headers.ETag?.ToString()!=job.ETag)throw new IOException("Download identity changed.");
        }
        else if(response.StatusCode==HttpStatusCode.OK)start=0;else throw new IOException("Invalid download response.");
        long expected=response.StatusCode==HttpStatusCode.PartialContent?range!.Length!.Value:response.Content.Headers.ContentLength??job.Size;
        if(expected<=0||job.Size>0&&expected!=job.Size)throw new IOException("The download size could not be verified.");
        job.Size=expected;job.ETag=response.Headers.ETag?.ToString()??"";job.Bytes=start;
        await using(var output=new FileStream(part,start>0?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.Read,65536,true))
        await using(var input=await response.Content.ReadAsStreamAsync(ct))
        {
            var buf=new byte[131072];var saved=DateTime.UtcNow;int count;
            while((count=await input.ReadAsync(buf,ct))>0)
            {
                if(job.Bytes+count>expected)throw new IOException("Download exceeded its expected size.");
                await output.WriteAsync(buf.AsMemory(0,count),ct);job.Bytes+=count;
                if(DateTime.UtcNow-saved>TimeSpan.FromSeconds(1)){lock(gate)Save();Changed?.Invoke();saved=DateTime.UtcNow;}
            }
            await output.FlushAsync(ct);output.Flush(true);
        }
        ct.ThrowIfCancellationRequested();if(new FileInfo(part).Length!=expected)throw new IOException("The download is incomplete.");
        DownloadNaming.Validate(job.Root,job.Destination);File.Move(part,job.Destination,false);store.RegisterDownload(job.Target,job.Destination);
    }
    void Update(PcDownloadJob j,string status,string message){lock(gate){if(j.Status is "Paused" or "Cancelled")return;j.Status=status;j.Message=message;Save();}Changed?.Invoke();}
    void Save()=>Atomic(jobsPath,JsonSerializer.SerializeToUtf8Bytes(jobs,Json));
    static void Atomic(string path,byte[] bytes){var temp=path+".tmp";using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){f.Write(bytes);f.Flush(true);}for(int attempt=0;;attempt++){try{File.Move(temp,path,true);break;}catch(Exception e)when(attempt<5&&(e is IOException or UnauthorizedAccessException)){Thread.Sleep(40*(attempt+1));}}}
    public void Dispose(){stop.Cancel();Signal();}
}
public sealed partial class LibraryStore
{
    public void SetDownloadIdentity(string group,string imdb)
    {
        lock(gate){foreach(var e in State.Catalog.Values.Where(e=>e.Item.GroupKey==group&&e.Item.ImdbId.Length==0))e.Item.ImdbId=imdb;Save();}
    }
    public void RegisterDownload(VideoItem item,string path)
    {
        lock(gate)
        {
            if(item.Id.Length==0)item.Id=Guid.NewGuid().ToString("N");
            if(!State.Catalog.TryGetValue(item.Id,out var entry)){item.Path=path;item.Available=true;entry=new(){Item=item,Fingerprint=Fingerprint(path)};State.Catalog[item.Id]=entry;}
            entry.Item.Path=path;entry.Item.Available=true;if(!entry.Paths.Contains(path,StringComparer.OrdinalIgnoreCase))entry.Paths.Add(path);
            Scan(out _);Save();
        }
    }
}
