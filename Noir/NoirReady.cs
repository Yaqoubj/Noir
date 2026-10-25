namespace Noir;

public sealed class NoirReadyState
{
    public int Version {get;set;}=1;
    public long BudgetBytes {get;set;}=20*NoirReadyPolicy.GB;
    public long KeepFreeBytes {get;set;}=10*NoirReadyPolicy.GB;
    public bool Paused {get;set;}
    public bool CleanupWatched {get;set;}
    public bool Migrated {get;set;}
    public HashSet<string> PausedJobs {get;set;}=[];
    public HashSet<string> Offered {get;set;}=[];
    public List<ReadyManagedFile> ManagedFiles {get;set;}=[];
    public Dictionary<string,ReadyTitlePlan> Titles {get;set;}=[];
    public List<ReadyWhy> Why {get;set;}=[];
    public void Log(string code,string title="",string detail="",DateTimeOffset? now=null){Why.Add(new(){At=now??DateTimeOffset.UtcNow,Code=code,Title=title,Detail=detail});if(Why.Count>200)Why.RemoveRange(0,Why.Count-200);}
}
public sealed class ReadyWhy {public DateTimeOffset At {get;set;}public string Code {get;set;}="";public string Title {get;set;}="";public string Detail {get;set;}="";}
public sealed class ReadyManagedFile
{
    public string JobId {get;set;}="";
    public string ItemId {get;set;}="";
    public string Group {get;set;}="";
    public string Path {get;set;}="";
    public string Title {get;set;}="";
    public string ReleaseKey {get;set;}="";
    public long ChargedBytes {get;set;}
    public long FileBytes {get;set;}
    public DateTime? LastWriteUtc {get;set;}
    public bool Pinned {get;set;}
    public bool CleanupCancelled {get;set;}
    public DateTimeOffset? RemoveAt {get;set;}
}
public sealed class ReadyTitlePlan
{
    public string Group {get;set;}="";
    public string Title {get;set;}="";
    public string ItemId {get;set;}="";
    public string State {get;set;}="Waiting";
    public string Reason {get;set;}="Checking sources";
    public string Quality {get;set;}="";
    public long Bytes {get;set;}
    public int Attempts {get;set;}
    public DateTimeOffset RecheckAt {get;set;}
    public List<ReadyEpisode> Episodes {get;set;}=[];
}
public sealed record ReadyEpisode(string ItemId,string Group,string Title,int Season,int Episode,bool Watched,bool Partial,bool Local,DateTimeOffset? AirsAt);
public sealed record ReadySource(string Id,string Release,long FileBytes,long ReleaseBytes,bool Pack,bool Confirmed,string Quality,long AlreadyReserved=0);
public sealed record ReadyStorage(long UsedBytes,long ReservedBytes,long FreeBytes,long DiskReservedBytes);
public sealed record ReadyDecision(ReadySource? Source,string Reason,long Charge=0);
public static class NoirReadyPolicy
{
    public const long GB=1073741824;
    public static int MigrateCount(ShowDownloadPreference preference)=>preference.KeepReadyCount is 1 or 2 or 3 or -1?preference.KeepReadyCount:2;
    public static bool Enabled(ShowDownloadPreference p)=>p.KeepReadyEnabled??p.AutoDownload;
    public static List<ReadyEpisode> Next(IEnumerable<ReadyEpisode> input,int count,DateTimeOffset now)
    {
        var all=input.Where(e=>!e.Watched&&e.Season>0&&e.Episode>0&&e.AirsAt is {} air&&air<=now).OrderBy(e=>e.Season).ThenBy(e=>e.Episode).ToList();
        if(count==-1)return all.Count==0?[]:all.Where(e=>e.Season==all[0].Season).ToList();
        return all.Take(count is 1 or 2 or 3?count:2).ToList();
    }
    public static List<IGrouping<string,ReadyEpisode>> OrderShows(IEnumerable<ReadyEpisode> episodes)=>episodes.GroupBy(e=>e.Group).OrderByDescending(g=>g.Any(e=>e.Partial)).ThenBy(g=>g.Key,StringComparer.Ordinal).ToList();
    public static ReadyDecision Choose(IEnumerable<ReadySource> options,NoirReadyState state,ReadyStorage storage,string quality)
    {
        if(state.Paused)return new(null,"Preparation paused");
        if(storage.UsedBytes+storage.ReservedBytes>state.BudgetBytes)return new(null,"Preparation budget reached");
        if(storage.FreeBytes-storage.DiskReservedBytes<Math.Max(10*GB,state.KeepFreeBytes))return new(null,"Keeping free space available");
        var ready=options.Where(o=>o.Confirmed&&o.FileBytes>0&&(!o.Pack||o.ReleaseBytes>0)).OrderByDescending(o=>quality=="Any"||o.Quality==quality).ThenBy(o=>o.Pack?Math.Max(o.FileBytes,o.ReleaseBytes):o.FileBytes).ThenBy(o=>o.Id,StringComparer.Ordinal).ToList();
        if(ready.Count==0)return new(null,"Availability or size unconfirmed; recheck scheduled");
        string reason="Preparation budget reached";
        foreach(var o in ready){var bytes=Math.Max(0,(o.Pack?Math.Max(o.FileBytes,o.ReleaseBytes):o.FileBytes)-Math.Max(0,o.AlreadyReserved));
            if(bytes>Math.Max(0,state.BudgetBytes-storage.UsedBytes-storage.ReservedBytes))continue;
            if(Math.Max(o.FileBytes,bytes)>Math.Max(0,storage.FreeBytes-storage.DiskReservedBytes-Math.Max(10*GB,state.KeepFreeBytes))){reason="Keeping free space available";continue;}
            return new(o,"Confirmed ready source",bytes);
        }
        return new(null,reason);
    }
    public static bool CanRemove(bool cleanup,bool managed,bool watched,bool pinned,bool cancelled,DateTimeOffset? watchedAt,IEnumerable<bool?> otherProfiles,DateTimeOffset now)
        =>cleanup&&managed&&watched&&!pinned&&!cancelled&&watchedAt is {} at&&at<=now.AddDays(-7)&&otherProfiles.All(done=>done==true);
    public static DateTimeOffset Backoff(DateTimeOffset now,int attempts)=>now.AddMinutes(Math.Min(360,15*Math.Pow(2,Math.Clamp(attempts-1,0,5))));
}
