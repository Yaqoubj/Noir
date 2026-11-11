using System.IO;
using System.IO.Compression;
using System.Text.Json;
namespace Noir;
// Full backups: history, ratings, notes, lists, library adds, settings and artwork in one .noirbackup
// zip. Restoring merges into what is here (undoable) or replaces it after a safety copy. Account
// secrets (*.dpapi) and the browser profile are never included.
public sealed record BackupPreview(string Path,PlayerState State,DateTimeOffset? Created,int Viewings,int Shows,int Movies,int Ratings,int Notes)
{
    public string Summary=>$"{Viewings:N0} viewings · {Shows:N0} shows · {Movies:N0} movies · {Ratings:N0} ratings · {Notes:N0} notes"+(Created is {} at?" · made "+at.ToLocalTime().ToString("d MMM yyyy"):"");
}
public sealed partial class LibraryStore
{
    public const string BackupExtension=".noirbackup";
    public string AutoBackupFolder=>State.BackupFolder.Length>0&&Directory.Exists(State.BackupFolder)?State.BackupFolder:System.IO.Path.Combine(DataDirectory,"backups");
    public void ExportBackup(string path)
    {
        byte[] state;int viewings;
        lock(gate){Save();state=JsonSerializer.SerializeToUtf8Bytes(State,JsonOptions);viewings=State.Viewings.Values.Sum(v=>v.Count);}
        var full=System.IO.Path.GetFullPath(path);var temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var file=File.Create(temp))using(var archive=new ZipArchive(file,ZipArchiveMode.Create))
            {
                using(var writer=new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                    writer.Write(JsonSerializer.Serialize(new{format=1,app="Noir",version=typeof(LibraryStore).Assembly.GetName().Version?.ToString()??"",created=DateTimeOffset.UtcNow,viewings}));
                using(var entry=archive.CreateEntry("library.json").Open())entry.Write(state);
                var seed=System.IO.Path.Combine(DataDirectory,"metadata.json");if(File.Exists(seed))archive.CreateEntryFromFile(seed,"metadata.json");
                var art=System.IO.Path.Combine(DataDirectory,"art");
                if(Directory.Exists(art))foreach(var image in Directory.EnumerateFiles(art).Where(f=>!f.EndsWith(".tmp")))
                    try{archive.CreateEntryFromFile(image,"art/"+System.IO.Path.GetFileName(image),CompressionLevel.NoCompression);}catch(IOException){}
                using(var readme=new StreamWriter(archive.CreateEntry("RESTORE.txt").Open()))
                    readme.Write("Restore in Noir: History → Import → Restore from backup. Video files, passwords and browser sign-ins are not included.");
            }
            File.Move(temp,full,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static bool IsBackup(string path)
    {
        if(!path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)&&!path.EndsWith(BackupExtension,StringComparison.OrdinalIgnoreCase))return false;
        try{using var zip=ZipFile.OpenRead(path);return zip.GetEntry("library.json")!=null;}catch(Exception e)when(e is IOException or InvalidDataException){return false;}
    }
    public static BackupPreview ReadBackup(string path)
    {
        try
        {
            using var zip=ZipFile.OpenRead(path);
            var entry=zip.GetEntry("library.json")??throw new IOException("This isn't a Noir backup: it has no library.json.");
            PlayerState state;using(var stream=entry.Open())state=JsonSerializer.Deserialize<PlayerState>(stream,JsonOptions)??throw new IOException("The backup's library is empty.");
            DateTimeOffset? created=null;
            if(zip.GetEntry("manifest.json") is {} manifest){using var m=manifest.Open();using var doc=JsonDocument.Parse(m);if(doc.RootElement.TryGetProperty("created",out var c)&&c.TryGetDateTimeOffset(out var at))created=at;}
            else created=entry.LastWriteTime;
            var watched=state.Viewings.Where(p=>p.Value.Count>0).Select(p=>state.Catalog.GetValueOrDefault(p.Key)?.Item).OfType<VideoItem>().ToList();
            return new(path,state,created,state.Viewings.Values.Sum(v=>v.Count),watched.Where(i=>!i.IsMovie).Select(i=>i.GroupKey).Distinct().Count(),watched.Count(i=>i.IsMovie),
                state.PersonalReviews.Values.Count(r=>r.Rating>0),state.PersonalReviews.Values.Count(r=>r.Note.Length>0));
        }
        catch(Exception e)when(e is JsonException or InvalidDataException or NotSupportedException){throw new IOException("This backup could not be read. It may be damaged or incomplete.",e);}
    }
    // Adds whatever the backup has that this library lacks. Existing ratings, notes and statuses win.
    public int MergeBackup(BackupPreview backup)
    {
        var other=backup.State;var id=BeginImport("backup "+(backup.Created?.ToLocalTime().ToString("d MMM yyyy")??System.IO.Path.GetFileName(backup.Path)));int added=0;
        CopyArt(backup.Path);
        string Rebase(string path){var local=System.IO.Path.Combine(DataDirectory,"art",System.IO.Path.GetFileName(path));return File.Exists(local)?local:path;}
        foreach(var entry in other.Catalog.Values){entry.Item.Poster=Rebase(entry.Item.Poster);entry.Item.Backdrop=Rebase(entry.Item.Backdrop);entry.Item.EpisodeImage=Rebase(entry.Item.EpisodeImage);}
        using(Batch())
        {
            foreach(var (itemId,viewings) in other.Viewings)
            {
                if(other.Catalog.GetValueOrDefault(itemId)?.Item is not {} item||viewings.Count==0)continue;
                // Restored history keeps its original source and logging time, so live viewings stay live.
                added+=LogWatched(viewings.OrderBy(v=>v.WatchedAt).Select(v=>new LogEntry(item,v.WatchedAt,v.DateUnknown,0,v.Approximate,v.Source,v.LoggedAt)),"Restored",queueSync:false,exact:true,batch:id);
            }
            lock(gate)
            {
                var record=State.ImportBatches.First(b=>b.Id==id);
                foreach(var (itemId,entry) in other.Catalog)
                {
                    var review=other.PersonalReviews.GetValueOrDefault(itemId);var listed=other.Watchlist.Contains(entry.Item.GroupKey);var added2=other.LibraryAdds.Contains(entry.Item.GroupKey);
                    if(review==null&&!listed&&!added2&&!other.Viewings.ContainsKey(itemId))continue;
                    var mine=Remember(entry.Item).Item;
                    if(review!=null)
                    {
                        var current=State.PersonalReviews.GetValueOrDefault(mine.Id);
                        if(current==null){State.PersonalReviews[mine.Id]=new(){Rating10=review.Rating10,Note=review.Note};if(review.Rating>0)record.RatedItemIds.Add(mine.Id);}
                        else{if(current.Rating==0&&review.Rating>0){current.Rating10=review.Rating10;record.RatedItemIds.Add(mine.Id);}if(current.Note.Length==0)current.Note=review.Note;}
                    }
                    if(listed)State.Watchlist.Add(mine.GroupKey);
                    if(added2)State.LibraryAdds.Add(mine.GroupKey);
                    if(other.TitleStatuses.TryGetValue(entry.Item.GroupKey,out var status))State.TitleStatuses.TryAdd(mine.GroupKey,status);
                    if(other.ShowRatings.TryGetValue(entry.Item.GroupKey,out var rating))State.ShowRatings.TryAdd(mine.GroupKey,rating);
                    if(other.IncludeSpecials.Contains(entry.Item.GroupKey))State.IncludeSpecials.Add(mine.GroupKey);
                    if(other.EpisodeSchedules.TryGetValue(entry.Item.GroupKey,out var schedule))State.EpisodeSchedules.TryAdd(mine.GroupKey,schedule);
                }
                foreach(var (name,members) in other.Collections)
                {
                    if(!State.Collections.TryGetValue(name,out var set))State.Collections[name]=set=[];
                    set.UnionWith(members.Where(g=>State.Catalog.Values.Any(e=>e.Item.GroupKey==g)));
                }
                Save();
            }
        }
        EndImport(id);
        return added;
    }
    void CopyArt(string path)
    {
        try
        {
            var art=System.IO.Path.Combine(DataDirectory,"art");Directory.CreateDirectory(art);
            using var zip=ZipFile.OpenRead(path);
            foreach(var entry in zip.Entries.Where(e=>e.FullName.StartsWith("art/")&&e.Name.Length>0))
            {
                var target=System.IO.Path.Combine(art,System.IO.Path.GetFileName(entry.Name));
                if(!File.Exists(target))entry.ExtractToFile(target);
            }
        }
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException){Log(e);}
    }
    // Replace swaps the files in on the next start, before anything is loaded. The phone pairing
    // (instance id and receipts) is kept so a paired phone simply receives the restored history.
    public void StageReplace(BackupPreview backup)
    {
        SafetyBackup("before-restore");
        var pending=System.IO.Path.Combine(DataDirectory,"restore-pending");
        if(Directory.Exists(pending))Directory.Delete(pending,true);
        Directory.CreateDirectory(pending);
        var state=backup.State;
        lock(gate){state.MobileInstanceId=State.MobileInstanceId;state.MobileReceipts=new(State.MobileReceipts);state.AutoBackup=State.AutoBackup;state.BackupFolder=State.BackupFolder;state.LastBackupAt=State.LastBackupAt;}
        File.WriteAllBytes(System.IO.Path.Combine(pending,"library.json"),JsonSerializer.SerializeToUtf8Bytes(state,JsonOptions));
        using var zip=ZipFile.OpenRead(backup.Path);
        zip.GetEntry("metadata.json")?.ExtractToFile(System.IO.Path.Combine(pending,"metadata.json"));
        // Artwork comes along; paths from another profile are re-pointed at this one's art folder on load.
        var art=System.IO.Path.Combine(pending,"art");Directory.CreateDirectory(art);
        foreach(var entry in zip.Entries.Where(e=>e.FullName.StartsWith("art/")&&e.Name.Length>0))entry.ExtractToFile(System.IO.Path.Combine(art,System.IO.Path.GetFileName(entry.Name)),true);
        File.WriteAllText(System.IO.Path.Combine(pending,"ready"),"");
    }
    // Called first thing in the constructor.
    void ApplyPendingRestore()
    {
        var pending=System.IO.Path.Combine(DataDirectory,"restore-pending");
        if(!File.Exists(System.IO.Path.Combine(pending,"ready")))return;
        try
        {
            foreach(var name in new[]{"library.json","metadata.json"})
            {
                var from=System.IO.Path.Combine(pending,name);var to=System.IO.Path.Combine(DataDirectory,name);
                if(File.Exists(from))File.Move(from,to,true);else if(name=="metadata.json"&&File.Exists(to))File.Delete(to);
            }
            var art=System.IO.Path.Combine(pending,"art");var target=System.IO.Path.Combine(DataDirectory,"art");
            if(Directory.Exists(art)){Directory.CreateDirectory(target);foreach(var file in Directory.EnumerateFiles(art)){var to=System.IO.Path.Combine(target,System.IO.Path.GetFileName(file));if(!File.Exists(to))File.Move(file,to);}}
            Directory.Delete(pending,true);
        }
        catch(Exception e){LoadWarning="A backup restore couldn't finish; your previous library is still in place.";Log(e);}
    }
    public string SafetyBackup(string reason)
    {
        var folder=AutoBackupFolder;Directory.CreateDirectory(folder);
        var path=System.IO.Path.Combine(folder,$"Noir-{reason}-{DateTime.Now:yyyy-MM-dd-HHmmss}{BackupExtension}");
        ExportBackup(path);Prune(folder);return path;
    }
    // Weekly while Noir runs; keeps the newest eight of each kind.
    public bool AutoBackupDue=>State.AutoBackup&&(State.LastBackupAt is not {} at||DateTimeOffset.UtcNow-at>TimeSpan.FromDays(7));
    public void RunAutoBackup()
    {
        var folder=AutoBackupFolder;Directory.CreateDirectory(folder);
        ExportBackup(System.IO.Path.Combine(folder,$"Noir-auto-{DateTime.Now:yyyy-MM-dd}{BackupExtension}"));
        lock(gate){State.LastBackupAt=DateTimeOffset.UtcNow;}Save();Prune(folder);
    }
    static void Prune(string folder)
    {
        foreach(var kind in Directory.EnumerateFiles(folder,"Noir-*"+BackupExtension).GroupBy(f=>System.IO.Path.GetFileName(f).StartsWith("Noir-auto-")?"auto":"safety"))
            foreach(var old in kind.OrderByDescending(File.GetLastWriteTimeUtc).Skip(8))try{File.Delete(old);}catch(IOException){}
    }
}
