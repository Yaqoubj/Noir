using System.IO;
using System.Text.Json;

namespace Noir;

public enum DownloadDeleteChoice { Ask, Recycle, Permanent }
public sealed class DownloadScopePreference
{
    public bool OnlyMissing {get;set;}=true;
    public bool SkipWatched {get;set;}
    public bool IncludeSpecials {get;set;}
    public long MaxTotalBytes {get;set;}
}
public sealed class DownloadManagePreference
{
    public DownloadDeleteChoice WhenDeleting {get;set;}
    public Dictionary<string,DownloadScopePreference> Shows {get;set;}=[];
}
public sealed record DownloadLocalFile(VideoItem Item,string Path,long Bytes,DateTimeOffset Modified,bool Watched,bool Noir,DateTimeOffset? WatchedAt);
public sealed record DownloadLocalGroup(string Key,string Name,List<DownloadLocalFile> Files)
{
    public long Bytes=>Files.Sum(f=>f.Bytes);
    public int Watched=>Files.Count(f=>f.Watched);
    public DateTimeOffset Recent=>Files.Max(f=>f.Modified);
}
public sealed class DownloadManageResult
{
    public int Files {get;set;}
    public long Bytes {get;set;}
    public List<string> Errors {get;}=[];
}
public sealed record DownloadSpaceItem(string Key,string Kind,string Label,long Bytes,string Path="",DownloadLocalFile? File=null,string JobId="");
public sealed record DownloadSpaceGroup(string Kind,List<DownloadSpaceItem> Items)
{
    public long Bytes=>Items.Sum(i=>i.Bytes);
}

public sealed partial class PcDownloads
{
    readonly SemaphoreSlim manageRun=new(1,1);
    readonly object manageGate=new();
    bool movesTracked;
    public Func<string,bool>? IsFilePlaying {get;set;}
    // Tests can select Recycle without depending on the desktop Recycle Bin.
    public Action<string,bool>? ManageDeleter {get;set;}
    string ManagePath=>Path.Combine(store.DataDirectory,"download-manage.json");
    public void TrackLibraryMoves(){lock(manageGate){if(movesTracked)return;movesTracked=true;store.DownloadFileMoved+=UpdateMovedFile;}}
    public DownloadManagePreference ManagementPreferences
    {
        get{lock(manageGate){try{return File.Exists(ManagePath)?JsonSerializer.Deserialize<DownloadManagePreference>(File.ReadAllText(ManagePath))??new():new();}catch(JsonException){return new();}}}
    }
    public void SetDeleteChoice(DownloadDeleteChoice choice){lock(manageGate){var p=ManagementPreferences;p.WhenDeleting=choice;Atomic(ManagePath,JsonSerializer.SerializeToUtf8Bytes(p,Json));}}
    public DownloadScopePreference ScopePreferences(string group)=>ManagementPreferences.Shows.GetValueOrDefault(group)??new();
    public void SaveScopePreferences(string group,DownloadScopePreference scope){lock(manageGate){var p=ManagementPreferences;p.Shows[group]=Copy(scope);Atomic(ManagePath,JsonSerializer.SerializeToUtf8Bytes(p,Json));}}
    public async Task<DownloadPlan> PlanRequest(string id,DownloadPlanRequest request,CancellationToken ct=default)
    {
        // Old clients sent only release IDs to the release-combining planner.
        if(request.ReleaseIds.Count>0&&request.Scope=="Missing episodes"&&request.Seasons.Count==0&&request.SpecificEpisodes.Count==0&&request.FromEpisode==1&&request.NextCount==3&&request.Preferences==null&&request.MaxTotalBytes==0&&!request.SkipWatched&&!request.IncludeSpecials)return await Plan(id,request.ReleaseIds,ct);
        return await PlanSeason(id,request,ct:ct);
    }
    public List<DownloadLocalGroup> LocalLibrary(string filter="All",string sort="Recent")
    {
        TrackLibraryMoves();var receipts=Jobs().Where(j=>j.Status=="Completed").Select(j=>j.Destination).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files=new List<DownloadLocalFile>();
        foreach(var (item,path,watched,at) in store.DownloadFileSnapshot()){
            try{var info=new FileInfo(path);if(!info.Exists)continue;var noir=receipts.Contains(path);if(filter=="Watched"&&!watched||filter=="Unwatched"&&watched||filter=="Downloaded by Noir"&&!noir)continue;files.Add(new(item,path,info.Length,info.LastWriteTimeUtc,watched,noir,at));}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        IEnumerable<DownloadLocalGroup> groups=files.GroupBy(f=>f.Item.IsMovie?"movies":f.Item.GroupKey).Select(g=>new DownloadLocalGroup(g.Key,g.Key=="movies"?"Movies":g.First().Item.Title,g.OrderBy(f=>f.Item.Season).ThenBy(f=>f.Item.Episode).ThenBy(f=>f.Item.Title).ToList()));
        return (sort=="Name"?groups.OrderBy(g=>g.Name,StringComparer.CurrentCultureIgnoreCase):sort=="Size"?groups.OrderByDescending(g=>g.Bytes):groups.OrderByDescending(g=>g.Recent)).ToList();
    }
    public static List<string> FileBundle(string video)
    {
        var folder=Path.GetDirectoryName(video)!;var stem=Path.GetFileNameWithoutExtension(video);
        if(!Directory.Exists(folder))return [];
        return new[]{video}.Concat(Directory.EnumerateFiles(folder).Where(f=>{
            var name=Path.GetFileNameWithoutExtension(f);return (SubtitleDownloads.TextSubtitle(f)||Path.GetExtension(f).Equals(".nfo",StringComparison.OrdinalIgnoreCase))&&(name.Equals(stem,StringComparison.OrdinalIgnoreCase)||name.StartsWith(stem+".",StringComparison.OrdinalIgnoreCase));
        })).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    void ValidateLibraryPath(string path,bool movies)
    {
        var root=store.State.Roots.Where(r=>r.Movies==movies&&Inside(path,r.Path)).OrderByDescending(r=>r.Path.Length).FirstOrDefault()??throw new IOException(Message("This file is outside your library folders.","هذا الملف خارج مجلدات المكتبة."));DownloadNaming.Validate(root.Path,path);
        if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException(Message("Choose a file without redirected links.","اختر ملفًا دون روابط إعادة توجيه."));
    }
    static bool Inside(string path,string root)=>Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    string Message(string en,string ar)=>store.State.Language=="ar"?ar:en;
    void RefusePlaying(string path){if(IsFilePlaying?.Invoke(path)==true)throw new IOException(Message("Close this video before changing its files.","أغلق هذا الفيديو قبل تغيير ملفاته."));}
    async Task StopForFile(string path,bool deleteData,CancellationToken ct)
    {
        List<PcDownloadJob> matching;lock(gate)matching=jobs.Where(j=>j.Destination.Equals(path,StringComparison.OrdinalIgnoreCase)).ToList();
        foreach(var job in matching){if(job.Service=="Torrents")TorrentStop(job,deleteData);else if(job.Status is not ("Completed" or "Cancelled" or "Failed"))Control(job.Id,"pause");}
        for(var n=0;n<100;n++){bool busy;lock(gate)busy=matching.Any(j=>running.ContainsKey(j.Id));if(!busy)return;await Task.Delay(50,ct);}
        throw new IOException(Message("This download is still stopping. Try again shortly.","لا يزال التنزيل يتوقف. حاول بعد قليل."));
    }
    void RemoveDisk(string path,bool recycle)
    {
        if(ManageDeleter!=null){ManageDeleter(path,recycle);return;}
        if(Directory.Exists(path)){
            if(recycle)Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else Directory.Delete(path,true);
        }else if(File.Exists(path)){
            if(recycle)Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else File.Delete(path);
        }
    }
    void ReleaseOwnership(IEnumerable<string> paths)
    {
        InitializeReady();var removed=paths.ToHashSet(StringComparer.OrdinalIgnoreCase);lock(readyGate){ready!.ManagedFiles.RemoveAll(f=>removed.Contains(f.Path));SaveReady();}
    }
    public async Task<DownloadManageResult> DeleteLocal(IEnumerable<DownloadLocalFile> selection,DownloadDeleteChoice choice,bool watchedOnly=false,CancellationToken ct=default)
    {
        if(choice==DownloadDeleteChoice.Ask)throw new IOException(Message("Choose Recycle Bin or permanent deletion.","اختر سلة المحذوفات أو الحذف النهائي."));
        await manageRun.WaitAsync(ct);var result=new DownloadManageResult();var removed=new List<string>();
        try{
            foreach(var file in selection.DistinctBy(f=>f.Path,StringComparer.OrdinalIgnoreCase)){
                ct.ThrowIfCancellationRequested();try{
                    ValidateLibraryPath(file.Path,file.Item.IsMovie);RefusePlaying(file.Path);if(watchedOnly&&!store.ReadyWatched(file.Item.Id))continue;
                    var bundle=FileBundle(file.Path);await StopForFile(file.Path,true,ct);RefusePlaying(file.Path);
                    // Recycle must preserve the completed torrent data until the selected deleter moves it.
                    foreach(var sidecar in bundle.Where(p=>!p.Equals(file.Path,StringComparison.OrdinalIgnoreCase)))RemoveDisk(sidecar,choice==DownloadDeleteChoice.Recycle);
                    RemoveDisk(file.Path,choice==DownloadDeleteChoice.Recycle);
                    if(!File.Exists(file.Path)){removed.Add(file.Path);result.Files++;result.Bytes+=file.Bytes;}
                }catch(Exception e)when(e is IOException or UnauthorizedAccessException or OperationCanceledException&&!ct.IsCancellationRequested){result.Errors.Add(file.Item.Label+" · "+e.Message);}
            }
            if(removed.Count>0){var set=removed.ToHashSet(StringComparer.OrdinalIgnoreCase);lock(gate){jobs.RemoveAll(j=>set.Contains(j.Destination));Save();}ReleaseOwnership(removed);store.DownloadFilesRemoved(removed);}
            Changed?.Invoke();return result;
        }finally{manageRun.Release();}
    }
    public void UpdateMovedFile(string from,string to)
    {
        List<PcDownloadJob> changed;lock(gate){changed=jobs.Where(j=>j.Destination.Equals(from,StringComparison.OrdinalIgnoreCase)).ToList();foreach(var job in changed){job.Destination=to;job.Target.Path=to;job.Root=store.State.Roots.Where(r=>r.Movies==job.Target.IsMovie&&Inside(to,r.Path)).OrderByDescending(r=>r.Path.Length).FirstOrDefault()?.Path??job.Root;}if(changed.Count>0)Save();}
        foreach(var job in changed)TorrentFileMoved(job,to);
        InitializeReady();lock(readyGate){foreach(var file in ready!.ManagedFiles.Where(f=>f.Path.Equals(from,StringComparison.OrdinalIgnoreCase)))file.Path=to;SaveReady();}Changed?.Invoke();
    }
    public async Task<DownloadManageResult> MoveLocal(IEnumerable<DownloadLocalFile> selection,string root,IProgress<(long Done,long Total)>? progress=null,CancellationToken ct=default)
    {
        TrackLibraryMoves();await manageRun.WaitAsync(ct);var result=new DownloadManageResult();
        try{
            var files=selection.DistinctBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).ToList();var total=files.Sum(f=>f.Bytes);long done=0;
            if(files.Any(f=>!store.State.Roots.Any(r=>r.Movies==f.Item.IsMovie&&r.Path.Equals(root,StringComparison.OrdinalIgnoreCase))))throw new IOException(Message("Add this folder to the matching library first.","أضف المجلد إلى المكتبة المناسبة أولًا."));
            foreach(var file in files){try{
                ct.ThrowIfCancellationRequested();ValidateLibraryPath(file.Path,file.Item.IsMovie);RefusePlaying(file.Path);var to=DownloadNaming.Destination(root,file.Item,file.Path);if(to.Equals(file.Path,StringComparison.OrdinalIgnoreCase))continue;
                var bundle=FileBundle(file.Path);var stem=Path.Combine(Path.GetDirectoryName(to)!,Path.GetFileNameWithoutExtension(to));var oldStem=Path.GetFileNameWithoutExtension(file.Path);
                var moves=bundle.Select(from=>(From:from,To:from==file.Path?to:stem+Path.GetFileName(from)[oldStem.Length..])).ToList();
                if(moves.Any(m=>File.Exists(m.To)||Directory.Exists(m.To)))throw new IOException(Message("A file already exists at the destination.","يوجد ملف مسبقًا في الوجهة."));
                var cross=!Path.GetPathRoot(file.Path)!.Equals(Path.GetPathRoot(to),StringComparison.OrdinalIgnoreCase);if(cross&&FreeBytesFor(root)<bundle.Sum(p=>new FileInfo(p).Length)+DiskReserve)throw new IOException(Message("Not enough free space in that folder.","لا توجد مساحة كافية في ذلك المجلد."));
                await StopForFile(file.Path,false,ct);Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                var applied=new List<(string From,string To)>();
                try{
                    foreach(var m in moves){
                        if(cross){var temp=m.To+".noir-moving";try{await using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true))await using(var input=new FileStream(m.From,FileMode.Open,FileAccess.Read,FileShare.Read,131072,true)){var buffer=new byte[131072];int read;long copied=0;while((read=await input.ReadAsync(buffer,ct))>0){await output.WriteAsync(buffer.AsMemory(0,read),ct);copied+=read;progress?.Report((done+copied,total));}await output.FlushAsync(ct);}File.Move(temp,m.To);File.Delete(m.From);}finally{if(File.Exists(temp))File.Delete(temp);}}
                        else File.Move(m.From,m.To);applied.Add(m);
                    }
                }catch{foreach(var m in applied.AsEnumerable().Reverse())try{File.Move(m.To,m.From);}catch(IOException){}throw;}
                store.DownloadFileRelocated(file.Item.Id,file.Path,to);done+=file.Bytes;progress?.Report((done,total));result.Files++;result.Bytes+=file.Bytes;
            }catch(Exception e)when(e is IOException or UnauthorizedAccessException){result.Errors.Add(file.Item.Label+" · "+e.Message);}}
            store.Save();Changed?.Invoke();return result;
        }finally{manageRun.Release();}
    }
    static bool Live(PcDownloadJob j)=>j.Status is not ("Completed" or "Cancelled" or "Failed");
    static bool Sharing(PcDownloadJob j)=>j.Service=="Torrents"&&(j.SeedUntil!=null||j.Status is "Seeding" or "Sharing back"||j.Message.Contains("Sharing back",StringComparison.OrdinalIgnoreCase));
    public int ClearFinished()
    {
        int count;lock(gate){count=jobs.RemoveAll(j=>j.Status is "Completed" or "Cancelled"&&!Sharing(j));if(count>0)Save();}Changed?.Invoke();return count;
    }
    public async Task CancelDownload(string id,bool deletePartial,CancellationToken ct=default)
    {
        PcDownloadJob? job;lock(gate)job=jobs.FirstOrDefault(j=>j.Id==id);if(job==null)return;
        Control(id,deletePartial?"cancel":"pause");if(job.Service=="Torrents")TorrentStop(job,deletePartial);
        for(int n=0;n<100;n++){bool busy;lock(gate)busy=running.ContainsKey(id);if(!busy)break;if(n==99)throw new IOException(Message("This download is still stopping. Try again shortly.","لا يزال التنزيل يتوقف. حاول بعد قليل."));await Task.Delay(50,ct);}
        if(deletePartial){var path=Path.Combine(parts,id+".part");if(File.Exists(path))File.Delete(path);lock(gate){job.Bytes=0;job.ETag="";Save();}ReleaseOwnership([job.Destination]);}Changed?.Invoke();
    }
    public List<DownloadSpaceGroup> SpaceSuggestions(DateTimeOffset? at=null)
    {
        var now=at??DateTimeOffset.UtcNow;var local=LocalLibrary().SelectMany(g=>g.Files).ToList();var all=Jobs();var items=new List<DownloadSpaceItem>();var claimed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var group in local.Where(f=>!f.Item.IsMovie).GroupBy(f=>f.Item.GroupKey)){
            if(store.EpisodesFor(group.Key).Where(e=>e.Season>0&&!Future(e)).All(store.EverWatched)&&group.All(f=>f.Watched))foreach(var file in group){items.Add(new(file.Path,"Finished shows",file.Item.Label,file.Bytes,file.Path,file));claimed.Add(file.Path);}
        }
        foreach(var file in local.Where(f=>!f.Item.IsMovie&&f.Watched&&f.WatchedAt<=now.AddDays(-7)&&!claimed.Contains(f.Path)))items.Add(new(file.Path,"Watched episodes (older than 7 days)",file.Item.Label,file.Bytes,file.Path,file));
        foreach(var path in Directory.EnumerateFiles(parts,"*.part")){
            if(all.Any(j=>Path.GetFileName(path).Equals(j.Id+".part",StringComparison.OrdinalIgnoreCase)&&Live(j)))continue;
            items.Add(new(path,"Leftover partial downloads",Path.GetFileName(path),new FileInfo(path).Length,path));
        }
        foreach(var root in store.State.Roots.Select(r=>r.Path).Distinct(StringComparer.OrdinalIgnoreCase)){
            var incoming=Path.Combine(root,".noir-incoming");if(!Directory.Exists(incoming)||(File.GetAttributes(incoming)&FileAttributes.ReparsePoint)!=0)continue;
            foreach(var path in Directory.EnumerateDirectories(incoming)){
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0||all.Any(j=>j.Service=="Torrents"&&Live(j)&&j.Hash.Equals(Path.GetFileName(path),StringComparison.OrdinalIgnoreCase)))continue;
                try{var entries=Directory.EnumerateFileSystemEntries(path,"*",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint}).ToList();items.Add(new(path,"Leftover partial downloads",Path.GetFileName(path),entries.Where(File.Exists).Sum(p=>new FileInfo(p).Length),path));}catch(IOException){}
            }
        }
        foreach(var job in all.Where(Sharing))items.Add(new(job.Id,"Torrents only sharing back",job.Name,0,JobId:job.Id));
        return items.GroupBy(i=>i.Kind).Select(g=>new DownloadSpaceGroup(g.Key,g.ToList())).ToList();
    }
    public async Task<DownloadManageResult> FreeSpace(IEnumerable<DownloadSpaceItem> selection,DownloadDeleteChoice choice,CancellationToken ct=default)
    {
        var chosen=selection.DistinctBy(i=>i.Key).ToList();var result=await DeleteLocal(chosen.Where(i=>i.File!=null).Select(i=>i.File!),choice,ct:ct);
        foreach(var item in chosen.Where(i=>i.File==null))try{
            if(item.JobId.Length>0){PcDownloadJob? job;lock(gate)job=jobs.FirstOrDefault(j=>j.Id==item.JobId);if(job!=null&&Sharing(job))TorrentStop(job,false);continue;}
            // Refresh the orphan list, so a download started since opening the sheet is protected.
            if(!SpaceSuggestions().SelectMany(g=>g.Items).Any(i=>i.File==null&&i.Path.Equals(item.Path,StringComparison.OrdinalIgnoreCase)))continue;
            RemoveDisk(item.Path,choice==DownloadDeleteChoice.Recycle);result.Files++;result.Bytes+=item.Bytes;
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException){result.Errors.Add(item.Label+" · "+e.Message);}
        Changed?.Invoke();return result;
    }
}

public sealed partial class LibraryStore
{
    public event Action<string,string>? DownloadFileMoved;
    public List<VideoItem> DownloadEpisodesFor(string group,bool specials)
    {
        lock(gate){var episodes=EpisodesFor(group);var representative=episodes.FirstOrDefault();if(!specials||representative==null)return episodes;bool changed=false;foreach(var special in (State.EpisodeSchedules.GetValueOrDefault(group)??[]).Where(e=>e.Season==0&&e.Episode>0&&!episodes.Any(i=>i.Season==0&&i.Episode==e.Episode))){var item=JsonSerializer.Deserialize<VideoItem>(JsonSerializer.Serialize(representative))!;item.Id="special-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(group+":"+special.Episode)));item.Path="";item.Available=false;item.Season=0;item.Episode=special.Episode;item.EpisodeTitle=special.Title;State.Catalog[item.Id]=new(){Item=item};episodes.Add(item);changed=true;}if(changed)Save();return episodes;}
    }
    public List<(VideoItem Item,string Path,bool Watched,DateTimeOffset? At)> DownloadFileSnapshot()
    {
        lock(gate)return State.Catalog.Values.Where(e=>e.Item.Available).SelectMany(e=>e.Paths.Append(e.Item.Path).Where(p=>p.Length>0&&DownloadNaming.Video(p)&&State.Roots.Any(r=>r.Movies==e.Item.IsMovie&&Path.GetFullPath(p).StartsWith(Path.GetFullPath(r.Path).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).Select(p=>(JsonSerializer.Deserialize<VideoItem>(JsonSerializer.Serialize(e.Item))!,p,EverWatched(e.Item),ViewingHistory(e.Item).FirstOrDefault()?.WatchedAt??(GetProgress(e.Item)?.Completed==true?GetProgress(e.Item)!.UpdatedAt:(DateTimeOffset?)null)))).DistinctBy(e=>e.p,StringComparer.OrdinalIgnoreCase).ToList();
    }
    public void DownloadFilesRemoved(IEnumerable<string> paths)
    {
        var gone=paths.ToHashSet(StringComparer.OrdinalIgnoreCase);lock(gate){foreach(var entry in State.Catalog.Values.Where(e=>gone.Contains(e.Item.Path)||e.Paths.Any(gone.Contains))){entry.Paths.RemoveAll(gone.Contains);if(gone.Contains(entry.Item.Path)){entry.Item.Available=false;var remaining=entry.Paths.FirstOrDefault(File.Exists);if(remaining!=null){entry.Item.Path=remaining;entry.Item.Available=true;}}}Save();}
    }
    public void DownloadFileRelocated(string id,string from,string to){lock(gate){Relocate(id,from,to);if(State.Catalog.TryGetValue(id,out var entry))entry.Paths.RemoveAll(p=>p.Equals(from,StringComparison.OrdinalIgnoreCase));}DownloadFileMoved?.Invoke(from,to);}
}
