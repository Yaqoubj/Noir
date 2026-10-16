using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.IO.Compression;

namespace Noir;

public sealed class LibraryRoot { public string Path { get; set; } = ""; public bool Movies { get; set; } }
public sealed class Bookmark
{
    public string ViewingId { get; set; } = "";
    public double Position { get; set; }
    public double Duration { get; set; }
    public bool Completed { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class IntroSegment
{
    public double Start { get; set; }
    public double End { get; set; }
    public bool Valid => Start >= 0 && End > Start && End - Start <= 600;
}
public sealed class PlayerState
{
    public Dictionary<string,string> TitleStatuses {get;set;}=new();
    public Dictionary<string,HashSet<string>> Collections {get;set;}=new();
    public Dictionary<string,string> ReviewTabs {get;set;}=new();
    public Dictionary<string,MatchOverride> Matches {get;set;}=new();
    public List<string> PlayQueue {get;set;}=[];
    public string MobileInstanceId { get; set; } = Guid.NewGuid().ToString("N");
    public HashSet<string> MobileReceipts { get; set; } = [];
    public Dictionary<string,long> MobileFieldTimes { get; set; } = new();
    public Dictionary<string,List<ViewingRecord>> Viewings { get; set; } = new();
    public Dictionary<string,PersonalReview> PersonalReviews { get; set; } = new();
    public Dictionary<string,PlaybackPreference> PlaybackPreferences { get; set; } = new();
    public Dictionary<string,List<KnownEpisode>> EpisodeSchedules { get; set; } = new();
    public bool SyncTrakt { get; set; }
    public bool SyncSerializd { get; set; }
    public Dictionary<string,WatchSyncJob> WatchSync { get; set; } = new();
    public Dictionary<string,string> SerializdSeasons { get; set; } = new();
    public Dictionary<string, CatalogEntry> Catalog { get; set; } = new();
    public Dictionary<string,string> CatalogAliases {get;set;}=new();
    public bool OnlineMetadata { get; set; } = true;
    public bool MarkWatchedAtCredits { get; set; } = true;
    public bool HideSpoilers { get; set; } = true;
    public HashSet<string> Watchlist { get; set; } = [];
    public HashSet<string> HiddenContinue { get; set; } = [];
    public List<LibraryRoot> Roots { get; set; } = [];
    public Dictionary<string, Bookmark> Progress { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, IntroSegment> Intros { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, IntroSegment> Outros { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AutoNext { get; set; } = true;
    public bool AutoSkipIntro { get; set; } = true;
    public bool FillVideo { get; set; } = true;
    public int Volume { get; set; } = 75;
    public string Subtitles { get; set; } = "English";
    public int SubtitleSize { get; set; } = 16;
    public int SubtitleColor { get; set; } = 0xFFFFFF;
    public bool SubtitleBackground { get; set; }
    public bool RunInBackground { get; set; } = true;
    public bool KeepAwakeWhileBusy { get; set; } = true;
    public bool BackgroundHintShown { get; set; }
    public HashSet<string> NewEpisodeAlerts { get; set; } = [];
}
public sealed class ViewingRecord
{
    public string Id { get; set; } = "";
    public DateTimeOffset WatchedAt { get; set; }
    public string Source { get; set; } = "Playback";
}
public sealed class PersonalReview { public int Rating { get; set; } public string Note { get; set; } = ""; }
public sealed class PlaybackPreference
{
    public string SubtitleTrack { get; set; } = "";
    public string AudioTrack { get; set; } = "";
    public double SubtitleDelay { get; set; }
}
public sealed class KnownEpisode
{
    public string Image {get;set;}="";
    public int Season { get; set; }
    public int Episode { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset? AirsAt { get; set; }
}
public sealed class SleepPlan
{
    public DateTimeOffset? Deadline { get; set; }
    public int EpisodesLeft { get; set; }
    public bool TimeExpired(DateTimeOffset now) => Deadline!=null&&now>=Deadline;
    public bool EpisodeEnded() => EpisodesLeft>0&&--EpisodesLeft==0;
}
public sealed class MediaSeed
{
    public string ImdbId { get; set; } = "";
    public string EpisodeImdbId { get; set; } = "";
    public string Title { get; set; } = "";
    public string EpisodeTitle { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Poster { get; set; } = "";
    public string Backdrop { get; set; } = "";
    public int Year { get; set; }
    public double Duration { get; set; }
    public double IntroStart { get; set; }
    public double IntroEnd { get; set; }
    public double OutroStart { get; set; }
    public double OutroEnd { get; set; }
    public double Position { get; set; }
    public bool Completed { get; set; }
    public string UpdatedAt { get; set; } = "";
}
public sealed class VideoItem
{
    public DateTimeOffset? AddedAt {get;set;}
    public string EpisodeImage {get;set;}="";
    public string Id { get; set; } = "";
    public string CollectionKey { get; set; } = "";
    public bool Available { get; set; } = true;
    public string Genres { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string EpisodeSummary { get; set; } = "";
    public string ImdbId { get; set; } = "";
    public string EpisodeImdbId { get; set; } = "";
    public string ReviewTitle { get; set; } = "";
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string EpisodeTitle { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Poster { get; set; } = "";
    public string Backdrop { get; set; } = "";
    public bool IsMovie { get; set; }
    public int Season { get; set; } = 1;
    public int Episode { get; set; }
    public int Year { get; set; }
    public double Duration { get; set; }
    [JsonIgnore] public string GroupKey => CollectionKey.Length > 0 ? CollectionKey : IsMovie ? "movie:" + Path.ToUpperInvariant() : "show:" + Title.ToUpperInvariant() + ":" + Year;
    public string EpisodeLabel => IsMovie ? (Year > 0 ? Year.ToString() : "Movie") : $"S{Season:00} · E{Episode:00}";
    public string Label => IsMovie ? Title : Title + " — " + EpisodeLabel;
}

public sealed class CatalogEntry
{
    public VideoItem Item { get; set; } = new();
    public List<string> Paths { get; set; } = [];
    public string Fingerprint { get; set; } = "";
    public Bookmark? Progress { get; set; }
    public IntroSegment? ImportedIntro { get; set; }
    public IntroSegment? ImportedOutro { get; set; }
    public DateTimeOffset MetadataCheckedAt { get; set; }
}
public sealed class MatchOverride
{
    public string Title {get;set;}="";
    public int Year {get;set;}
    public string ImdbId {get;set;}="";
    public int Season {get;set;}
    public int Episode {get;set;}
}

public sealed partial class LibraryStore
{
    public event Action? WatchCompleted;
    public string DataDirectory { get; }
    public PlayerState State { get; private set; }
    public string LoadWarning { get; private set; } = "";
    readonly string statePath;
    readonly Dictionary<string, MediaSeed> metadata;
    readonly object gate = new();
    readonly object writeGate=new(),saveQueueGate=new();
    long saveRevision,writtenRevision;
    (long Revision,byte[] Data)? queuedSnapshot;
    Task saveWorker=Task.CompletedTask;
    bool writerRunning;
    Exception? saveFailure;
    public event Action<Exception>? PersistenceFailed;
    readonly Dictionary<string,string> activeViewings = new();
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public LibraryStore(string? directory = null)
    {
        DataDirectory = directory ?? ProfileLocation.Prepare();
        Directory.CreateDirectory(DataDirectory);
        statePath = System.IO.Path.Combine(DataDirectory, "library.json");
        metadata = new(StringComparer.OrdinalIgnoreCase);
        var metaPath = System.IO.Path.Combine(DataDirectory, "metadata.json");
        if (File.Exists(metaPath))
        {
            try { foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, MediaSeed>>(File.ReadAllText(metaPath), JsonOptions) ?? []) metadata[pair.Key] = pair.Value; }
            catch (Exception e) { Log(e); }
        }
        var existing = File.Exists(statePath);
        State = Load(statePath) ?? Load(statePath + ".bak") ?? new PlayerState();
        State.Progress = new(State.Progress, StringComparer.OrdinalIgnoreCase);
        State.Intros = new(State.Intros, StringComparer.OrdinalIgnoreCase);
        State.Outros = new(State.Outros, StringComparer.OrdinalIgnoreCase);
        State.Volume = Math.Clamp(State.Volume, 0, 100);
        ReconcileCatalog();
        RecoverOrganization();
        foreach(var entry in State.Catalog.Values)
        {
            SeedViewing(entry);
            string Rebase(string path) {var local=System.IO.Path.Combine(DataDirectory,"art",System.IO.Path.GetFileName(path));return !File.Exists(path)&&File.Exists(local)?local:path;}
            entry.Item.Poster=Rebase(entry.Item.Poster);entry.Item.Backdrop=Rebase(entry.Item.Backdrop);
        }
        if (!existing && State.Roots.Count == 0)
        {
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            foreach (var name in new[] { "Movies", "TV", "Anime" })
            {
                var path = System.IO.Path.Combine(videos, name);
                if (Directory.Exists(path)) State.Roots.Add(new() { Path = path, Movies = name == "Movies" });
            }
            foreach (var pair in metadata)
            {
                var m = pair.Value;
                if (DateTimeOffset.TryParse(m.UpdatedAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var updated))
                    State.Progress[pair.Key] = new() { Position = m.Position, Duration = m.Duration, Completed = m.Completed, UpdatedAt = updated };
            }
            Save();
        }
    }
    PlayerState? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            for(var attempt=0;;attempt++)
            try {return JsonSerializer.Deserialize<PlayerState>(File.ReadAllText(path),JsonOptions);}
            catch(IOException) when(attempt<4) {Thread.Sleep(40*(attempt+1));}
            catch(UnauthorizedAccessException) when(attempt<4) {Thread.Sleep(40*(attempt+1));}
        }
        catch (Exception e) { LoadWarning = "Recovered library settings from the last backup."; Log(e); return null; }
    }
    public void Log(Exception e) => File.AppendAllText(System.IO.Path.Combine(DataDirectory, "errors.log"), $"{DateTimeOffset.Now:o} {e}\n");
    public void Save()
    {
        (long Revision,byte[] Data) snapshot;
        lock(gate)snapshot=(++saveRevision,JsonSerializer.SerializeToUtf8Bytes(State,JsonOptions));
        WriteSnapshot(snapshot);
    }
    public void SaveDeferred()
    {
        (long Revision,byte[] Data) snapshot;
        lock(gate)snapshot=(++saveRevision,JsonSerializer.SerializeToUtf8Bytes(State,JsonOptions));
        lock(saveQueueGate)
        {
            queuedSnapshot=snapshot;
            if(writerRunning)return;
            writerRunning=true;saveWorker=Task.Run(DrainSaves);
        }
    }
    void DrainSaves()
    {
        while(true)
        {
            (long Revision,byte[] Data) snapshot;
            lock(saveQueueGate){if(queuedSnapshot is not {} next){writerRunning=false;return;}snapshot=next;queuedSnapshot=null;}
            try{WriteSnapshot(snapshot);}
            catch(Exception e){saveFailure=e;try{Log(e);}catch{}PersistenceFailed?.Invoke(e);}
        }
    }
    public async Task FlushPendingAsync()
    {
        while(true){Task pending;lock(saveQueueGate)pending=saveWorker;await pending.ConfigureAwait(false);lock(saveQueueGate)if(!writerRunning)break;}
        if(saveFailure is {} error)throw new IOException("Your latest library changes could not be saved.",error);
    }
    void WriteSnapshot((long Revision,byte[] Data) snapshot)
    {
        lock(writeGate)
        {
            if(snapshot.Revision<=writtenRevision)return;
            var temp = statePath + ".tmp";
            using(var file=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
            {
                file.Write(snapshot.Data);
                file.Flush(true);
            }
            // Windows scanners can briefly hold an otherwise writable JSON file. Keep the
            // flushed new file and previous good copy intact while retrying the atomic commit.
            for(int attempt=0;;attempt++)
            {
                try
                {
                    if(File.Exists(statePath)) File.Copy(statePath,statePath+".bak",true);
                    File.Move(temp,statePath,true);writtenRevision=snapshot.Revision;saveFailure=null;break;
                }
                catch(Exception e) when(attempt<5&&(e is IOException||e is UnauthorizedAccessException)) {Thread.Sleep(40*(attempt+1));}
            }
        }
    }
    public void ExportBackup(string path)
    {
        lock(gate)
        {
            Save();
            using var file=File.Create(path);using var archive=new ZipArchive(file,ZipArchiveMode.Create);
            archive.CreateEntryFromFile(statePath,"library.json");
            var seed=System.IO.Path.Combine(DataDirectory,"metadata.json");if(File.Exists(seed)) archive.CreateEntryFromFile(seed,"metadata.json");
            var art=System.IO.Path.Combine(DataDirectory,"art");
            if(Directory.Exists(art)) foreach(var image in Directory.EnumerateFiles(art).Where(f=>!f.EndsWith(".tmp")))
                archive.CreateEntryFromFile(image,"art/"+System.IO.Path.GetFileName(image));
            var entry=archive.CreateEntry("RESTORE.txt");using var writer=new StreamWriter(entry.Open());
            writer.Write("Close Noir. Extract library.json, metadata.json and art into %LOCALAPPDATA%\\Noir to restore this backup. Video files and browser cookies are not included.");
        }
    }
    public static Bookmark? ProgressFor(PlayerState state, VideoItem item) =>
        state.Catalog.GetValueOrDefault(item.Id)?.Progress ?? (item.Path.Length>0?state.Progress.GetValueOrDefault(item.Path):null);
    public Bookmark? GetProgress(VideoItem item) {lock(gate) return ProgressFor(State,item);}
    void SeedViewing(CatalogEntry entry)
    {
        if(entry.Progress is not {Completed:true} b || State.Viewings.ContainsKey(entry.Item.Id)) return;
        State.Viewings[entry.Item.Id]=[new(){Id=b.ViewingId.Length>0?b.ViewingId:Guid.NewGuid().ToString("N"),WatchedAt=b.UpdatedAt,Source="Earlier record"}];
    }
    public List<ViewingRecord> ViewingHistory(VideoItem item) {lock(gate) return State.Viewings.GetValueOrDefault(item.Id)?.OrderByDescending(v=>v.WatchedAt).ToList()??[];}
    public bool EverWatched(VideoItem item) => ViewingHistory(item).Count>0 || GetProgress(item)?.Completed==true;
    public void BeginViewing(VideoItem item,bool restart)
    {
        lock(gate)
        {
            var entry=Remember(item);SeedViewing(entry);
            activeViewings[item.Id]=!restart&&entry.Progress is {Completed:false,ViewingId.Length:>0} b?b.ViewingId:Guid.NewGuid().ToString("N");
        }
    }
    void AddViewing(VideoItem item,string id,DateTimeOffset at,string source)
    {
        if(!State.Viewings.TryGetValue(item.Id,out var list)) State.Viewings[item.Id]=list=[];
        if(!list.Any(v=>v.Id==id)) list.Add(new(){Id=id,WatchedAt=at,Source=source});
    }
    public List<KnownEpisode> MissingEpisodes(string groupKey,DateTimeOffset now)
    {
        lock(gate)
        {
            var available=State.Catalog.Values.Where(e=>e.Item.GroupKey==groupKey&&e.Item.Available).Select(e=>(e.Item.Season,e.Item.Episode)).ToHashSet();
            return (State.EpisodeSchedules.GetValueOrDefault(groupKey)??[]).Where(e=>e.Season>0&&e.AirsAt<=now&&!available.Contains((e.Season,e.Episode))).OrderBy(e=>e.Season).ThenBy(e=>e.Episode).ToList();
        }
    }
    public bool Record(VideoItem item, double position, double duration, bool completed=false,DateTimeOffset? expectedUpdatedAt=null,bool deferSave=false)
    {
        if (!double.IsFinite(position) || position < 0 || !double.IsFinite(duration) || duration < 0) return false;
        bool newlyCompleted;
        lock(gate)
        {
        var entry=Remember(item);
        // A stale paused/closing PC session must not replace progress received from a phone.
        if(expectedUpdatedAt is { } expected&&(entry.Progress?.UpdatedAt??DateTimeOffset.MinValue)!=expected) return false;
        SeedViewing(entry);
        var viewingId=activeViewings.GetValueOrDefault(entry.Item.Id)??(entry.Progress?.ViewingId is {Length:>0} previous?previous:Guid.NewGuid().ToString("N"));
        newlyCompleted=completed&&(entry.Progress?.Completed!=true || activeViewings.ContainsKey(entry.Item.Id)&&!State.Viewings.GetValueOrDefault(entry.Item.Id,[]).Any(v=>v.Id==viewingId));
        entry.Progress = new Bookmark { Position = duration > 0 ? Math.Min(position, duration) : position,
            Duration = duration > 0 ? duration : item.Duration, Completed = completed, UpdatedAt = DateTimeOffset.UtcNow,ViewingId=viewingId };
        if(item.Path.Length>0) State.Progress[item.Path]=entry.Progress;
        if(newlyCompleted) AddViewing(entry.Item,viewingId,entry.Progress.UpdatedAt,"Playback");
        if(duration>0) entry.Item.Duration=duration;
        if(newlyCompleted) CommunityQueue.Enqueue(State,entry.Item,entry.Progress.UpdatedAt);
        if(deferSave)SaveDeferred();else Save();
        }
        if(newlyCompleted) WatchCompleted?.Invoke();
        return true;
    }
    public void Mark(VideoItem item, bool completed)
    {
        lock(gate)
        {
        var old = GetProgress(item);
        var entry=Remember(item);
        SeedViewing(entry);
        var viewingId=old?.Completed==true?old.ViewingId:Guid.NewGuid().ToString("N");
        if(viewingId.Length==0) viewingId=Guid.NewGuid().ToString("N");
        entry.Progress = new() { Position = completed ? old?.Duration ?? item.Duration : 0,
              Duration = old?.Duration ?? item.Duration, Completed = completed, UpdatedAt = DateTimeOffset.UtcNow,ViewingId=viewingId };
        if(item.Path.Length>0) State.Progress[item.Path]=entry.Progress;
        if(completed&&old?.Completed!=true) AddViewing(entry.Item,viewingId,entry.Progress.UpdatedAt,"Marked watched");
        if(completed) CommunityQueue.Enqueue(State,entry.Item,entry.Progress.UpdatedAt);
        if(!completed) CommunityQueue.CancelPending(State,entry.Item.Id);
        Save();
        }
        if(completed) WatchCompleted?.Invoke();
    }
    public IntroSegment? GetIntro(VideoItem item)
    {
        if (State.Intros.TryGetValue(item.Path, out var own)) return own.Valid ? own : null;
        if(State.Catalog.GetValueOrDefault(item.Id) is { } entry)
            foreach(var alias in entry.Paths)
                if(State.Intros.TryGetValue(alias,out var saved) && saved.Valid) return saved;
        if (State.Intros.TryGetValue(item.GroupKey + ":season:" + item.Season, out var series)) return series.Valid ? series : null;
        if(State.Catalog.GetValueOrDefault(item.Id)?.ImportedIntro is {Valid:true} imported) return imported;
        if (metadata.TryGetValue(item.Path, out var m) && m.IntroEnd > m.IntroStart)
            return new() { Start = m.IntroStart, End = m.IntroEnd };
        return null;
    }
    public static VideoItem? Next(VideoItem current, IEnumerable<VideoItem> items, PlayerState state)
    {
        if (current.IsMovie) return null;
        return items.Where(x => x.GroupKey == current.GroupKey &&
            (current.Season == 0 ? x.Season == 0 : x.Season > 0) &&
            (x.Season > current.Season || (x.Season == current.Season && x.Episode > current.Episode)) &&
            x.Available && !((ProgressFor(state,x)?.Completed) ?? false))
            .OrderBy(x => x.Season).ThenBy(x => x.Episode).FirstOrDefault();
    }
    public IntroSegment? GetOutro(VideoItem item)
    {
        if(State.Outros.TryGetValue(item.Id,out var own)) return own.Valid?own:null;
        if(State.Outros.TryGetValue(item.GroupKey+":season:"+item.Season,out var season)) return season.Valid?season:null;
        return State.Catalog.GetValueOrDefault(item.Id)?.ImportedOutro;
    }
    public double CompletionStart(VideoItem item,double duration)
    {
        if(!State.MarkWatchedAtCredits || duration<=0) return duration;
        if(GetOutro(item) is {Valid:true} outro && outro.Start>=duration*.5 && outro.Start<duration) return outro.Start;
        // A conservative fallback for unlabelled videos; a saved credits marker takes precedence.
        return duration<300?duration:duration-Math.Min(duration*.05,item.IsMovie?180:90);
    }
    public void RememberChapters(VideoItem item,IntroSegment? intro,IntroSegment? outro)
    {
        lock(gate)
        {
            var entry=Remember(item);bool changed=false;
            if(intro is {Valid:true} && entry.ImportedIntro==null) {entry.ImportedIntro=intro;changed=true;}
            if(outro is {Valid:true} && entry.ImportedOutro==null) {entry.ImportedOutro=outro;changed=true;}
            if(changed) Save();
        }
    }
    public VideoItem? ContinueFor(IEnumerable<VideoItem> group)
    {
        var list = group.ToList();
        var last = list.Where(x => GetProgress(x) is { } b && (b.Position > 0 || b.Completed))
            .OrderByDescending(x => new[]{GetProgress(x)!.UpdatedAt,ViewingHistory(x).FirstOrDefault()?.WatchedAt??DateTimeOffset.MinValue}.Max()).FirstOrDefault();
        if (last == null) return null;
        var bookmark=GetProgress(last)!;
        var watchedAt=ViewingHistory(last).FirstOrDefault()?.WatchedAt;
        return bookmark.Completed || watchedAt>bookmark.UpdatedAt ? Next(last, list, State) : last;
    }
    public VideoItem DefaultFor(IEnumerable<VideoItem> group)
    {
        var list = group.OrderBy(x => x.Season == 0 ? int.MaxValue : x.Season).ThenBy(x => x.Episode).ToList();
        return ContinueFor(list) ?? list.FirstOrDefault(x => GetProgress(x)?.Completed != true) ?? list[0];
    }
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".webm", ".ts", ".m2ts", ".wmv", ".mpg", ".mpeg" };
    static readonly Regex EpisodePattern = new(@"(?i)(?<![a-z0-9])(?:S(?<s>\d{1,2})[ ._-]*E(?<e>\d{1,3})|(?<s>\d{1,2})x(?<e>\d{1,3}))(?!\d)", RegexOptions.Compiled);
    public List<VideoItem> Scan(out List<string> warnings)
    {
        lock(gate)
        {
        foreach(var entry in State.Catalog.Values) entry.Item.Available=false;
        var items = new Dictionary<string, VideoItem>(StringComparer.OrdinalIgnoreCase);
        warnings = [];
        foreach (var root in State.Roots.ToList())
        {
            if (!Directory.Exists(root.Path)) { warnings.Add("Folder unavailable: " + root.Path); continue; }
            try
            {
                foreach (var file in Directory.EnumerateFiles(root.Path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                {
                    if (!Extensions.Contains(System.IO.Path.GetExtension(file))) continue;
                    var rel = System.IO.Path.GetRelativePath(root.Path, file);
                    var parts = rel.Split(System.IO.Path.DirectorySeparatorChar);
                    if (parts.Any(p => Regex.IsMatch(p, @"(?i)^(extras?|samples?|trailers?|screens|NC)$"))) continue;
                    var name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (Regex.IsMatch(name, @"(?i)\b(sample|trailer|NCOP|NCED)\b")) continue;
                    metadata.TryGetValue(file, out var seed);
                    var match = EpisodePattern.Match(name);
                    int season = match.Success ? int.Parse(match.Groups["s"].Value) : 1;
                    if(!match.Success)
                    {
                        var seasonFolder=parts.Take(parts.Length-1).Select(p=>Regex.Match(p,@"(?i)^(?:season[ ._-]*|s)(\d{1,2})$")).LastOrDefault(m=>m.Success);
                        if(seasonFolder!=null) season=int.Parse(seasonFolder.Groups[1].Value);
                        else if(parts.Take(parts.Length-1).Any(p=>p.Equals("Specials",StringComparison.OrdinalIgnoreCase))) season=0;
                    }
                    int episode = match.Success ? int.Parse(match.Groups["e"].Value) : 0;
                    string rawTitle;
                    if (root.Movies) rawTitle = name;
                    else if (parts.Length > 1 && !Regex.IsMatch(parts[0], @"(?i)^(season[ ._-]*\d+|specials)$")) rawTitle = parts[0];
                    else rawTitle = match.Success ? name[..match.Index] : name;
                    if (!root.Movies && episode == 0)
                    {
                        var anime = Regex.Match(name, @"(?:^|\s-\s|\s)(\d{1,3})(?:\s|\[|$)");
                        if (anime.Success)
                        {
                            episode = int.Parse(anime.Groups[1].Value);
                            if(parts.Length==1 || Regex.IsMatch(parts[0],@"(?i)^(season[ ._-]*\d+|specials)$")) rawTitle=name[..anime.Index];
                        }
                    }
                    if (!root.Movies && episode == 0) continue;
                    var yearMatch = Regex.Match(rawTitle, @"\b(19\d{2}|20\d{2})\b");
                    var rawName=seed is { Title.Length: > 0 } ? seed.Title : rawTitle;
                    var title = root.Movies?CleanTitle(rawName):CleanShowTitle(rawName);
                    if (title.Length == 0) title = name;
                    var folder = System.IO.Path.GetDirectoryName(file)!;
                    var poster = seed?.Poster ?? "";
                    if (!File.Exists(poster)) poster = new[] { System.IO.Path.Combine(folder, "poster.jpg"), System.IO.Path.Combine(folder, "folder.jpg") }.FirstOrDefault(File.Exists) ?? "";
                    var parsed = new VideoItem { Path = file, Title = title, IsMovie = root.Movies, Season = season, Episode = episode,
                        Year = seed is { Year: > 0 } ? seed.Year : yearMatch.Success ? int.Parse(yearMatch.Value) : 0,
                        EpisodeTitle = seed?.EpisodeTitle ?? "", Summary = seed?.Summary ?? "", Poster = poster,
                        Backdrop = seed?.Backdrop ?? "", Duration = seed?.Duration ?? 0 };
                    items[file]=Remember(parsed).Item;
                }
            }
            catch (Exception e) { warnings.Add("Could not scan: " + root.Path); Log(e); }
        }
        // Recover old path bookmarks even if a media folder is already missing during migration.
        foreach(var path in State.Progress.Keys.ToList())
        {
            if(State.Catalog.Values.Any(x=>x.Paths.Contains(path,StringComparer.OrdinalIgnoreCase))) continue;
            metadata.TryGetValue(path,out var seed);
            var name=System.IO.Path.GetFileNameWithoutExtension(path);
            var match=EpisodePattern.Match(name);
            var movie=!match.Success;
            var title=seed is {Title.Length:>0}?seed.Title:CleanTitle(match.Success?name[..match.Index]:name);
            Remember(new VideoItem {Path=path,Title=title,IsMovie=movie,Available=false,
                Season=match.Success?int.Parse(match.Groups["s"].Value):1,Episode=match.Success?int.Parse(match.Groups["e"].Value):0,
                Year=seed?.Year??0,Summary=seed?.Summary??"",EpisodeTitle=seed?.EpisodeTitle??"",Poster=seed?.Poster??"",Backdrop=seed?.Backdrop??"",Duration=seed?.Duration??0});
        }
        Save();
        return items.Values.OrderBy(x => x.Title).ThenBy(x => x.Season).ThenBy(x => x.Episode).ToList();
        }
    }

    public List<VideoItem> Archive { get { lock(gate) return State.Catalog.Values.Select(x=>x.Item).ToList(); } }
    public List<VideoItem> EpisodesFor(string groupKey)
    {
        lock(gate)
        {
            var saved=State.Catalog.Values.Where(e=>e.Item.GroupKey==groupKey).Select(e=>e.Item).ToList();
            if(saved.Count==0||saved[0].IsMovie) return saved;
            var representative=saved[0];
            foreach(var episode in State.EpisodeSchedules.GetValueOrDefault(groupKey)??[])
            {
                if(episode.Season<=0||episode.Episode<=0||saved.Any(i=>i.Season==episode.Season&&i.Episode==episode.Episode)) continue;
                var id="episode-"+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{groupKey}:{episode.Season}:{episode.Episode}")));
                saved.Add(new(){Id=id,CollectionKey=groupKey,Title=representative.Title,Year=representative.Year,Season=episode.Season,Episode=episode.Episode,EpisodeTitle=episode.Title,Available=false,
                    EpisodeImage=episode.Image,Poster=representative.Poster,Backdrop=representative.Backdrop,Summary=representative.Summary,Genres=representative.Genres,ImdbId=representative.ImdbId,ReviewTitle=representative.ReviewTitle,SourceName=representative.SourceName,SourceUrl=representative.SourceUrl});
            }
            return saved.OrderBy(i=>i.Season==0?int.MaxValue:i.Season).ThenBy(i=>i.Episode).ToList();
        }
    }
    public void ApplyDetails(string groupKey,OnlineDetails? details,bool replaceArtwork=false)
    {
        lock(gate)
        {
            if(details is {Schedule.Count:>0}) State.EpisodeSchedules[groupKey]=details.Schedule;
            foreach(var entry in State.Catalog.Values.Where(x=>x.Item.GroupKey==groupKey))
            {
                entry.MetadataCheckedAt=DateTimeOffset.UtcNow;
                if(details==null) continue;
                var item=entry.Item;
                if(details.Title.Length>0&&!State.Matches.ContainsKey(item.Id)) item.Title=details.Title;
                if(details.Summary.Length>0) item.Summary=details.Summary;
                if((replaceArtwork||!File.Exists(item.Poster)) && File.Exists(details.Poster)) item.Poster=details.Poster;
                if((replaceArtwork||!File.Exists(item.Backdrop)) && File.Exists(details.Backdrop)) item.Backdrop=details.Backdrop;
                if(item.Year==0) item.Year=details.Year;
                item.Genres=details.Genres;item.SourceName=details.SourceName;item.SourceUrl=details.SourceUrl;
                if(details.ImdbId.Length>0&&!State.Matches.ContainsKey(item.Id)) item.ImdbId=details.ImdbId;
                if(details.Title.Length>0&&!State.Matches.ContainsKey(item.Id)) item.ReviewTitle=details.Title;
                if(details.Episodes.TryGetValue($"{item.Season}:{item.Episode}",out var episode))
                {item.EpisodeTitle=episode.Title;item.EpisodeSummary=episode.Summary;item.EpisodeImage=State.EpisodeSchedules.GetValueOrDefault(groupKey)?.FirstOrDefault(e=>e.Season==item.Season&&e.Episode==item.Episode)?.Image??item.EpisodeImage;}
            }
            Save();
        }
    }
    static string MatchTitle(string title) => Regex.Replace(title.ToUpperInvariant(), @"[^\p{L}\p{N}]", "");
    static string Fingerprint(string path)
    {
        try
        {
            using var file=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            if(file.Length==0) return "";
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(BitConverter.GetBytes(file.Length));
            var block=new byte[65536];var count=file.Read(block);hash.AppendData(block,0,count);
            file.Position=Math.Max(count,file.Length-block.Length);count=file.Read(block);hash.AppendData(block,0,count);
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch { return ""; }
    }
    CatalogEntry Remember(VideoItem item)
    {
        if(item.Id.Length>0 && State.Catalog.TryGetValue(item.Id,out var known)) return known;
        var fingerprint=Fingerprint(item.Path);
        bool SameTitle(CatalogEntry e) => e.Item.IsMovie==item.IsMovie && e.Item.Season==item.Season && e.Item.Episode==item.Episode &&
            MatchTitle(e.Item.Title)==MatchTitle(item.Title) && (e.Item.Year==0 || item.Year==0 || e.Item.Year==item.Year);
        var entry=State.Catalog.Values.FirstOrDefault(e=>e.Paths.Contains(item.Path,StringComparer.OrdinalIgnoreCase) && (State.Matches.ContainsKey(e.Item.Id)||SameTitle(e) || fingerprint.Length>0 && fingerprint==e.Fingerprint)) ??
            (fingerprint.Length>0?State.Catalog.Values.FirstOrDefault(e=>e.Fingerprint==fingerprint && e.Item.IsMovie==item.IsMovie):null);
        var matches=State.Catalog.Values.Where(SameTitle).ToList();
        entry ??= matches.Count==1?matches[0]:null;
        var old=entry?.Item;
        var previousGroupKey=item.GroupKey;
        var id=old?.Id??(item.Id.Length>0?item.Id:Guid.NewGuid().ToString("N"));
        var collection=old?.CollectionKey;
        if(string.IsNullOrEmpty(collection))
        {
            var existingShows=item.IsMovie?[]:State.Catalog.Values.Where(e=>!e.Item.IsMovie&&MatchTitle(CleanShowTitle(e.Item.Title))==MatchTitle(CleanShowTitle(item.Title))&&
                (e.Item.Year==0||item.Year==0||e.Item.Year==item.Year)).Select(e=>e.Item.GroupKey).Distinct().ToList();
            collection=existingShows.Count==1?existingShows[0]:item.IsMovie?"movie:"+id:"show:"+MatchTitle(CleanShowTitle(item.Title))+":"+item.Year;
        }
        metadata.TryGetValue(item.Path,out var seed);
        var correction=State.Matches.GetValueOrDefault(id);
        item=new VideoItem {Id=id,CollectionKey=collection,AddedAt=old?.AddedAt??(old==null&&item.Available&&File.Exists(item.Path)?DateTimeOffset.UtcNow:null),EpisodeImage=old?.EpisodeImage??item.EpisodeImage,Path=item.Path,Title=correction?.Title??old?.Title??item.Title,IsMovie=item.IsMovie,Season=correction?.Season??item.Season,Episode=correction?.Episode??item.Episode,
            Available=item.Available,Year=correction?.Year??(old?.Year>0?old.Year:item.Year),Duration=old?.Duration>0?old.Duration:item.Duration,
            EpisodeTitle=old?.EpisodeTitle.Length>0?old.EpisodeTitle:item.EpisodeTitle,Summary=old?.Summary.Length>0?old.Summary:item.Summary,
            Poster=CacheArtwork(File.Exists(old?.Poster)?old!.Poster:item.Poster),Backdrop=CacheArtwork(File.Exists(old?.Backdrop)?old!.Backdrop:item.Backdrop),
            Genres=old?.Genres??item.Genres,SourceName=old?.SourceName??item.SourceName,SourceUrl=old?.SourceUrl??item.SourceUrl,EpisodeSummary=old?.EpisodeSummary??item.EpisodeSummary,
            ImdbId=old?.ImdbId is {Length:>0}?old.ImdbId:seed?.ImdbId??item.ImdbId,EpisodeImdbId=old?.EpisodeImdbId is {Length:>0}?old.EpisodeImdbId:seed?.EpisodeImdbId??item.EpisodeImdbId,ReviewTitle=old?.ReviewTitle??item.ReviewTitle};
        entry ??= new();entry.Item=item;
        if(seed!=null && seed.IntroEnd>seed.IntroStart)
            entry.ImportedIntro ??= new(){Start=seed.IntroStart,End=seed.IntroEnd};
        if(seed!=null && seed.OutroEnd>seed.OutroStart) entry.ImportedOutro ??= new(){Start=seed.OutroStart,End=seed.OutroEnd};
        if(item.Path.Length>0&&!entry.Paths.Contains(item.Path,StringComparer.OrdinalIgnoreCase)) entry.Paths.Add(item.Path);
        if(fingerprint.Length>0) entry.Fingerprint=fingerprint;
        var legacy=entry.Paths.Select(p=>State.Progress.GetValueOrDefault(p)).Where(p=>p!=null).OrderByDescending(p=>p!.UpdatedAt).FirstOrDefault();
        if(legacy!=null && (entry.Progress==null || legacy.UpdatedAt>entry.Progress.UpdatedAt)) entry.Progress=legacy;
        State.Catalog[id]=entry;
        var oldIntroKey=previousGroupKey+":season:"+item.Season;
        var newIntroKey=item.GroupKey+":season:"+item.Season;
        if(State.Intros.TryGetValue(oldIntroKey,out var seasonIntro) && !State.Intros.ContainsKey(newIntroKey)) State.Intros[newIntroKey]=seasonIntro;
        return entry;
    }
    public string CacheArtwork(string path)
    {
        if(!File.Exists(path)) return "";
        try
        {
            var cache=System.IO.Path.Combine(DataDirectory,"art");Directory.CreateDirectory(cache);
            var full=System.IO.Path.GetFullPath(path);
            if(full.StartsWith(cache+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) return full;
            var bytes=File.ReadAllBytes(path);
            var target=System.IO.Path.Combine(cache,Convert.ToHexString(SHA256.HashData(bytes))+System.IO.Path.GetExtension(path));
            if(!File.Exists(target)) File.WriteAllBytes(target,bytes);
            return target;
        }
        catch(Exception e) {Log(e);return path;}
    }
    public static string CleanShowTitle(string value)=>CleanTitle(Regex.Replace(value,@"(?i)[ ._-]+(?:S\d{1,2}(?:[ ._-]|$)|Season[ ._-]+\d+\b).*$",""));
    public static string CleanTitle(string value)
    {
        value = Regex.Replace(value, @"\[[^\]]*\]", "");
        value = Regex.Replace(value, @"(?i)(?:4K|2160p|1080p|720p|480p|WEB[- .]?DL|WEBRip|BluRay|BDRip|x26[45]|HEVC|H264|H265|HDR|DV)(?:\b|$).*", "");
        value = Regex.Replace(value, @"[.( _-]*(?:19\d{2}|20\d{2})[) ]*.*$", "");
        value = value.Replace('.', ' ').Replace('_', ' ').Trim(' ', '-', '(', ')');
        return Regex.Replace(value, @"\s+", " ");
    }
    public static string Time(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
}

