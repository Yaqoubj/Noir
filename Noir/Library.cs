using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Noir;

public sealed class LibraryRoot
{
    public string Path { get; set; } = "";
    public bool Movies { get; set; }
}

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
    public Dictionary<string, ShowDownloadPreference> ShowDownloads { get; set; } = new();
    public Dictionary<string, string> TitleStatuses { get; set; } = new();
    public Dictionary<string, HashSet<string>> Collections { get; set; } = new();
    public Dictionary<string, string> ReviewTabs { get; set; } = new();
    public Dictionary<string, MatchOverride> Matches { get; set; } = new();
    public List<string> PlayQueue { get; set; } = [];
    public string MobileInstanceId { get; set; } = Guid.NewGuid().ToString("N");
    public HashSet<string> MobileReceipts { get; set; } = [];
    public Dictionary<string, long> MobileFieldTimes { get; set; } = new();
    public Dictionary<string, List<ViewingRecord>> Viewings { get; set; } = new();
    public Dictionary<string, PersonalReview> PersonalReviews { get; set; } = new();
    public List<string> Favorites { get; set; } = [];
    public HashSet<string> Likes { get; set; } = [];
    public Dictionary<string, int> SeasonRatings { get; set; } = new();
    public Dictionary<string, string> Reactions { get; set; } = new();
    public Dictionary<string, PlaybackPreference> PlaybackPreferences { get; set; } = new();
    public Dictionary<string, List<KnownEpisode>> EpisodeSchedules { get; set; } = new();
    public bool SyncTrakt { get; set; }
    public bool SyncSerializd { get; set; }
    public Dictionary<string, WatchSyncJob> WatchSync { get; set; } = new();
    public Dictionary<string, string> SerializdSeasons { get; set; } = new();
    public Dictionary<string, CatalogEntry> Catalog { get; set; } = new();
    public Dictionary<string, string> CatalogAliases { get; set; } = new();
    public bool OnlineMetadata { get; set; } = true;
    public bool MarkWatchedAtCredits { get; set; } = true;
    public bool HideSpoilers { get; set; } = true;

    // "en" or "ar"; Arabic also mirrors the library layout right-to-left.
    public string Language { get; set; } = "en";

    // Side rail: "Auto" (icons only below 1100 px), "Labels" or "Icons".
    public string RailMode { get; set; } = "Auto";
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

    // One-time tips already shown ("player-shortcuts", …), so none of them ever repeats.
    public HashSet<string> TipsShown { get; set; } = [];
    public HashSet<string> NewEpisodeAlerts { get; set; } = [];
    public bool EpisodeAlerts { get; set; } = true;

    // TVmaze show state ("Ended", "Running", …) and typical episode minutes, by group.
    public Dictionary<string, string> ShowStates { get; set; } = new();
    public Dictionary<string, int> ShowRuntimes { get; set; } = new();

    // Personal ratings for a whole show (groupKey) or season (groupKey:S2).
    public Dictionary<string, int> ShowRatings { get; set; } = new();

    // "Still watching?" was answered Keep: hide the nudge until this date.
    public Dictionary<string, DateTimeOffset> TrackerSnoozes { get; set; } = new();
    public HashSet<string> IncludeSpecials { get; set; } = [];

    // "" until chosen on first run; "Tracker" hides the file library and opens on History.
    public string AppMode { get; set; } = "";
    public HashSet<string> HiddenTitles { get; set; } = [];

    // Titles added to the library on purpose (Discover's "+ Library", a started download) without a file yet.
    public HashSet<string> LibraryAdds { get; set; } = [];

    // Upper-cased paths of files left on disk when their title's history was forgotten.
    public HashSet<string> RemovedFiles { get; set; } = [];
    public bool StremioAutoImport { get; set; } = true;
    public DateTimeOffset? StremioImportedAt { get; set; }

    // Nuvio: pull its watched list on a timer, and send Noir's watches back.
    public bool SyncNuvio { get; set; } = true;
    public bool NuvioConnected { get; set; }
    public bool NuvioAutoImport { get; set; } = true;
    public DateTimeOffset? NuvioImportedAt { get; set; }

    // Un-checks waiting to reach Nuvio: "imdb:season:episode" keys.
    public HashSet<string> NuvioDeletes { get; set; } = [];

    // Items Noir sent to or received from Nuvio; only these are ever deleted there.
    public HashSet<string> NuvioKnown { get; set; } = [];

    // Recent imports, newest last, so each can be undone.
    public List<ImportBatch> ImportBatches { get; set; } = [];
    public bool AutoBackup { get; set; } = true;
    public string BackupFolder { get; set; } = "";
    public DateTimeOffset? LastBackupAt { get; set; }
}

public sealed class ImportBatch
{
    public string Id { get; set; } = "";
    public string Service { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public int Added { get; set; }

    // Ratings this import created; undo removes only these.
    public List<string> RatedItemIds { get; set; } = [];

    // Undated guesses a dated import replaced; undo puts them back.
    public List<DisplacedViewing> Displaced { get; set; } = [];
}

public sealed class DisplacedViewing
{
    public string ItemId { get; set; } = "";
    public ViewingRecord Viewing { get; set; } = new();
}

public sealed class ShowDownloadPreference
{
    public string Resolution { get; set; } = "Any";
    public string Language { get; set; } = "All";
    public long MaxEpisodeBytes { get; set; }
    public bool AutoDownload { get; set; }
    public int KeepReadyCount { get; set; } = 2;
    public bool? KeepReadyEnabled { get; set; }
    public bool AlsoDownload { get; set; }
}

public sealed class ViewingRecord
{
    public string Id { get; set; } = "";
    public DateTimeOffset WatchedAt { get; set; }
    public string Source { get; set; } = "Playback";

    // Logged from memory or imported without a date; WatchedAt then holds the air date or log time.
    public bool DateUnknown { get; set; }

    // A guessed date: "when it aired", "around 2019", or spread across a range.
    public bool Approximate { get; set; }

    // When Noir recorded it. Entered more than a week after it was watched = logged history.
    public DateTimeOffset? LoggedAt { get; set; }

    // The import that added it, so that import can be undone.
    public string Batch { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Backfill => DateUnknown || LoggedAt is { } at && (at - WatchedAt).TotalDays > 7;
}

public sealed class PersonalReview
{
    int rating,
        rating10;
    public int Rating
    {
        get => rating;
        set
        {
            rating = Math.Clamp(value, 0, 5);
            if ((rating10 + 1) / 2 != rating)
                rating10 = 0;
        }
    }
    public int Rating10
    {
        get => rating10 > 0 ? rating10 : rating * 2;
        set
        {
            rating10 = Math.Clamp(value, 0, 10);
            rating = (rating10 + 1) / 2;
        }
    }
    public string Note { get; set; } = "";
}

public sealed class PlaybackPreference
{
    public string SubtitleTrack { get; set; } = "";
    public string AudioTrack { get; set; } = "";
    public double SubtitleDelay { get; set; }
}

// Some providers title-case every word, apostrophes included ("Let'S", "Don'T"); lower-case the letters of a contraction.
public static class EpisodeText
{
    public static string Tidy(string? value) =>
        string.IsNullOrEmpty(value)
            ? ""
            : Regex.Replace(value, @"(?<=\p{L})(['’])(S|T|M|D|Ll|Re|Ve)(?![\p{L}\p{N}])", m =>
                m.Groups[1].Value + m.Groups[2].Value.ToLowerInvariant()
            );
}

public sealed class KnownEpisode
{
    public string Image { get; set; } = "";
    public int Season { get; set; }
    public int Episode { get; set; }
    public string Title
    {
        get => title;
        set => title = EpisodeText.Tidy(value);
    }
    string title = "";
    public DateTimeOffset? AirsAt { get; set; }
    public int Runtime { get; set; }
}

public sealed class SleepPlan
{
    public DateTimeOffset? Deadline { get; set; }
    public int EpisodesLeft { get; set; }

    public bool TimeExpired(DateTimeOffset now) => Deadline != null && now >= Deadline;

    public bool EpisodeEnded() => EpisodesLeft > 0 && --EpisodesLeft == 0;
}

public sealed class MediaSeed
{
    public string ImdbId { get; set; } = "";
    public string EpisodeImdbId { get; set; } = "";
    public string Title { get; set; } = "";
    public string EpisodeTitle
    {
        get => episodeTitle;
        set => episodeTitle = EpisodeText.Tidy(value);
    }
    string episodeTitle = "";
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
    public DateTimeOffset? AddedAt { get; set; }
    public string EpisodeImage { get; set; } = "";
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
    public string EpisodeTitle
    {
        get => episodeTitle;
        set => episodeTitle = EpisodeText.Tidy(value);
    }
    string episodeTitle = "";
    public string Summary { get; set; } = "";
    public string Poster { get; set; } = "";
    public string Backdrop { get; set; } = "";
    public bool IsMovie { get; set; }
    public int Season { get; set; } = 1;
    public int Episode { get; set; }
    public int Year { get; set; }
    public double Duration { get; set; }

    [JsonIgnore]
    public string GroupKey =>
        CollectionKey.Length > 0 ? CollectionKey
        : IsMovie ? "movie:" + Path.ToUpperInvariant()
        : "show:" + Title.ToUpperInvariant() + ":" + Year;
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
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public string ImdbId { get; set; } = "";
    public int Season { get; set; }
    public int Episode { get; set; }
}

public sealed partial class LibraryStore
{
    public event Action? WatchCompleted;
    public string DataDirectory { get; }
    public PlayerState State { get; private set; }
    public string LoadWarning { get; private set; } = "";
    readonly string statePath;
    readonly Dictionary<string, MediaSeed> metadata;
    readonly object gate = new(),
        scanGate = new();
    readonly object writeGate = new(),
        saveQueueGate = new();
    long saveRevision,
        writtenRevision;
    (long Revision, PlayerState State)? queuedSnapshot;
    Task saveWorker = Task.CompletedTask;
    bool writerRunning,
        savesBlocked,
        recoveredBackup,
        loadFailed;
    Exception? saveFailure;
    public event Action<Exception>? PersistenceFailed;
    readonly Dictionary<string, string> activeViewings = new();
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public LibraryStore(string? directory = null)
    {
        TraceStartup("Store preparation started");
        DataDirectory = directory ?? ProfileLocation.Prepare();
        Directory.CreateDirectory(DataDirectory);
        statePath = System.IO.Path.Combine(DataDirectory, "library.json");
        TraceStartup("Store preparation ended");
        TraceStartup("Pending restore started");
        ApplyPendingRestore();
        TraceStartup("Pending restore ended");
        metadata = new(StringComparer.OrdinalIgnoreCase);
        var metaPath = System.IO.Path.Combine(DataDirectory, "metadata.json");
        TraceStartup("Metadata read started");
        if (File.Exists(metaPath))
        {
            try
            {
                var json = File.ReadAllText(metaPath);
                TraceStartup("Metadata read ended");
                TraceStartup("Metadata parse started");
                foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, MediaSeed>>(json, JsonOptions) ?? [])
                    metadata[pair.Key] = pair.Value;
            }
            catch (Exception e)
            {
                Log(e);
            }
        }
        TraceStartup("Metadata load ended");
        var existing = File.Exists(statePath);
        var primary = Load(statePath);
        var backup = primary == null ? Load(statePath + ".bak") : null;
        State = primary ?? backup ?? new PlayerState();
        TraceStartup("State load ended");
        TraceStartup("State recovery started");
        if (primary == null && (existing || File.Exists(statePath + ".bak") || loadFailed))
        {
            recoveredBackup = backup != null;
            savesBlocked = backup == null;
            LoadWarning = recoveredBackup
                ? "Recovered library settings from the last backup. · تم استرداد المكتبة من آخر نسخة احتياطية."
                : "Library and backup could not be read. Saving is disabled for this session; both files are preserved. · تعذرت قراءة المكتبة والنسخة الاحتياطية. تم تعطيل الحفظ لهذه الجلسة مع الحفاظ على الملفين.";
            var suffix = ".unreadable-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
            foreach (var path in new[] { statePath, statePath + ".bak" }.Where(File.Exists))
                try
                {
                    File.Copy(path, path + suffix, false);
                }
                catch (Exception e)
                {
                    try
                    {
                        Log(e);
                    }
                    catch { }
                }
        }
        TraceStartup("State recovery ended");
        TraceStartup("State normalization started");
        State.Progress = new(State.Progress, StringComparer.OrdinalIgnoreCase);
        State.Intros = new(State.Intros, StringComparer.OrdinalIgnoreCase);
        State.Outros = new(State.Outros, StringComparer.OrdinalIgnoreCase);
        State.Volume = Math.Clamp(State.Volume, 0, 100);
        TraceStartup("State normalization ended");
        TraceStartup("Catalog reconciliation started");
        ReconcileCatalog();
        TraceStartup("Catalog reconciliation ended");
        TraceStartup("Organization recovery started");
        RecoverOrganization();
        TraceStartup("Organization recovery ended");
        TraceStartup("Viewing migration started");
        // Older records have no LoggedAt: anything logged by hand counts as entered now, playback as live.
        foreach (var list in State.Viewings.Values)
        foreach (var v in list)
            v.LoggedAt ??= v.Source is "Playback" or "Android playback" or "Earlier record"
                ? v.WatchedAt
                : DateTimeOffset.UtcNow;
        TraceStartup("Viewing migration ended");
        TraceStartup("Viewing seed and artwork checks started");
        var rebased = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long artworkTicks = 0,
            seedTicks = 0;
        foreach (var entry in State.Catalog.Values)
        {
            var started = TraceStartupTimestamp();
            SeedViewing(entry);
            var seeded = TraceStartupTimestamp();
            seedTicks += seeded - started;
            // Episodes share artwork paths, so each distinct path is checked once.
            string Rebase(string path)
            {
                if (rebased.TryGetValue(path, out var known))
                    return known;
                var local = System.IO.Path.Combine(DataDirectory, "art", System.IO.Path.GetFileName(path));
                return rebased[path] = !File.Exists(path) && File.Exists(local) ? local : path;
            }
            entry.Item.Poster = Rebase(entry.Item.Poster);
            entry.Item.Backdrop = Rebase(entry.Item.Backdrop);
            artworkTicks += TraceStartupTimestamp() - seeded;
        }
        TraceStartupDuration("Viewing seed", seedTicks);
        TraceStartupDuration("Artwork path checks", artworkTicks);
        TraceStartup("Viewing seed and artwork checks ended");
        TraceStartup("Default library migration started");
        if (!existing && State.Roots.Count == 0)
        {
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            foreach (var name in new[] { "Movies", "TV", "Anime" })
            {
                var path = System.IO.Path.Combine(videos, name);
                if (Directory.Exists(path))
                    State.Roots.Add(new() { Path = path, Movies = name == "Movies" });
            }
            foreach (var pair in metadata)
            {
                var m = pair.Value;
                if (
                    DateTimeOffset.TryParse(
                        m.UpdatedAt,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal,
                        out var updated
                    )
                )
                    State.Progress[pair.Key] = new()
                    {
                        Position = m.Position,
                        Duration = m.Duration,
                        Completed = m.Completed,
                        UpdatedAt = updated,
                    };
            }
            Save();
        }
        TraceStartup("Default library migration ended");
        TraceStartup("Mobile baseline capture started");
        CaptureMobileChanges();
        TraceStartup("Mobile baseline capture ended");
#if !NOIR_HEADLESS
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            try
            {
                var marker = System.IO.Path.Combine(DataDirectory, "cache-maintenance.utc");
                if (File.Exists(marker) && File.GetLastWriteTimeUtc(marker) > DateTime.UtcNow.AddDays(-1))
                    return;
                MaintainCaches(DateTimeOffset.UtcNow);
                File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("O"));
            }
            catch (Exception e)
            {
                try
                {
                    Log(e);
                }
                catch { }
            }
        });
#endif
    }

    LibraryStore(LibraryStore source, PlayerState snapshot)
    {
        DataDirectory = source.DataDirectory;
        statePath = source.statePath;
        State = snapshot;
        metadata = source.metadata;
        ProfileTitle = source.ProfileTitle;
        savesBlocked = true;
    }

    static void TraceStartup(string step)
    {
#if !NOIR_HEADLESS
        StartupTrace.Mark(step);
#endif
    }

    static long TraceStartupTimestamp()
    {
#if !NOIR_HEADLESS
        if (StartupTrace.Enabled)
            return System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        return 0;
    }

    static void TraceStartupDuration(string phase, long ticks)
    {
#if !NOIR_HEADLESS
        if (StartupTrace.Enabled)
            StartupTrace.Mark(
                phase
                    + " total: "
                    + (ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString(
                        "F3",
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                    + " ms"
            );
#endif
    }

    PlayerState? Load(string path)
    {
        try
        {
            for (var attempt = 0; ; attempt++)
                try
                {
                    TraceStartup("State read started");
                    var json = File.ReadAllText(path);
                    TraceStartup("State read ended");
                    TraceStartup("State parse started");
                    var state = JsonSerializer.Deserialize<PlayerState>(json, JsonOptions)
                        ?? throw new JsonException("Library state is null.");
                    TraceStartup("State parse ended");
                    return state;
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
                catch (DirectoryNotFoundException)
                {
                    return null;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(40 * (attempt + 1));
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    Thread.Sleep(40 * (attempt + 1));
                }
        }
        catch (Exception e)
        {
            loadFailed = true;
            try
            {
                Log(e);
            }
            catch { }
            return null;
        }
    }

    // Links are dropped: stream, debrid and AIOStreams URLs carry account tokens.
    public void Log(Exception e) =>
        File.AppendAllText(
            System.IO.Path.Combine(DataDirectory, "errors.log"),
            $"{DateTimeOffset.Now:o} {Regex.Replace(e.ToString(), @"(?i)\b[a-z][a-z0-9+.-]*://\S+", "[link]")}\n"
        );

    public void Save()
    {
        (long Revision, PlayerState State) snapshot;
        lock (gate)
        {
            if (savesBlocked)
                return;
            if (batchDepth > 0)
            {
                batchDirty = true;
                return;
            }
            TrackMobileChanges();
            snapshot = (++saveRevision, LibrarySnapshot.Copy(State));
        }
        WriteSnapshot((snapshot.Revision, JsonSerializer.SerializeToUtf8Bytes(snapshot.State, JsonOptions)));
    }

    public void SaveDeferred() => SaveDeferredCore(true);

    void SaveDeferredCore(bool trackMobile)
    {
        (long Revision, PlayerState State) snapshot;
        lock (gate)
        {
            if (savesBlocked)
                return;
            if (batchDepth > 0)
            {
                batchDirty = true;
                return;
            }
            if (trackMobile)
                TrackMobileChanges();
            snapshot = (++saveRevision, LibrarySnapshot.Copy(State));
        }
        lock (saveQueueGate)
        {
            if (queuedSnapshot == null || queuedSnapshot.Value.Revision < snapshot.Revision)
                queuedSnapshot = snapshot;
            if (writerRunning)
                return;
            writerRunning = true;
            saveWorker = Task.Run(DrainSaves);
        }
    }

    void DrainSaves()
    {
        while (true)
        {
            (long Revision, PlayerState State) snapshot;
            lock (saveQueueGate)
            {
                if (queuedSnapshot is not { } next)
                {
                    writerRunning = false;
                    return;
                }
                snapshot = next;
                queuedSnapshot = null;
            }
            try
            {
                WriteSnapshot((snapshot.Revision, JsonSerializer.SerializeToUtf8Bytes(snapshot.State, JsonOptions)));
            }
            catch (Exception e)
            {
                saveFailure = e;
                try
                {
                    Log(e);
                }
                catch { }
                PersistenceFailed?.Invoke(e);
            }
        }
    }

    public async Task FlushPendingAsync()
    {
        while (true)
        {
            Task pending;
            lock (saveQueueGate)
                pending = saveWorker;
            await pending.ConfigureAwait(false);
            lock (saveQueueGate)
                if (!writerRunning)
                    break;
        }
        if (saveFailure is { } error)
            throw new IOException("Your latest library changes could not be saved.", error);
    }

    void WriteSnapshot((long Revision, byte[] Data) snapshot)
    {
        lock (writeGate)
        {
            if (savesBlocked || snapshot.Revision <= writtenRevision)
                return;
            var temp = statePath + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(snapshot.Data);
                file.Flush(true);
            }
            // Windows scanners can briefly hold an otherwise writable JSON file. Keep the
            // flushed new file and previous good copy intact while retrying the atomic commit.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(statePath) && !recoveredBackup)
                        File.Copy(statePath, statePath + ".bak", true);
                    File.Move(temp, statePath, true);
                    writtenRevision = snapshot.Revision;
                    saveFailure = null;
                    recoveredBackup = false;
                    break;
                }
                catch (Exception e) when (attempt < 5 && (e is IOException || e is UnauthorizedAccessException))
                {
                    Thread.Sleep(40 * (attempt + 1));
                }
            }
        }
    }

    public static Bookmark? ProgressFor(PlayerState state, VideoItem item) =>
        state.Catalog.GetValueOrDefault(item.Id)?.Progress
        ?? (item.Path.Length > 0 ? state.Progress.GetValueOrDefault(item.Path) : null);

    public Bookmark? GetProgress(VideoItem item)
    {
        lock (gate)
            return ProgressFor(State, item);
    }

    void SeedViewing(CatalogEntry entry)
    {
        if (entry.Progress is not { Completed: true } b || State.Viewings.ContainsKey(entry.Item.Id))
            return;
        State.Viewings[entry.Item.Id] =
        [
            new()
            {
                Id = b.ViewingId.Length > 0 ? b.ViewingId : Guid.NewGuid().ToString("N"),
                WatchedAt = b.UpdatedAt,
                Source = "Earlier record",
                LoggedAt = b.UpdatedAt,
            },
        ];
        RememberMobileViewing(entry.Item.Id, State.Viewings[entry.Item.Id][0]);
    }

    public List<ViewingRecord> ViewingHistory(VideoItem item)
    {
        lock (gate)
            return State.Viewings.GetValueOrDefault(item.Id)?.OrderByDescending(v => v.WatchedAt).ToList() ?? [];
    }

    public bool EverWatched(VideoItem item) => ViewingHistory(item).Count > 0 || GetProgress(item)?.Completed == true;

    public void BeginViewing(VideoItem item, bool restart)
    {
        lock (gate)
        {
            var entry = Remember(item);
            SeedViewing(entry);
            activeViewings[item.Id] =
                !restart && entry.Progress is { Completed: false, ViewingId.Length: > 0 } b
                    ? b.ViewingId
                    : Guid.NewGuid().ToString("N");
        }
    }

    void AddViewing(VideoItem item, string id, DateTimeOffset at, string source)
    {
        if (!State.Viewings.TryGetValue(item.Id, out var list))
            State.Viewings[item.Id] = list = [];
        if (!list.Any(v => v.Id == id))
        {
            var viewing = new ViewingRecord
            {
                Id = id,
                WatchedAt = at,
                Source = source,
                LoggedAt = DateTimeOffset.UtcNow,
            };
            list.Add(viewing);
            RememberMobileViewing(item.Id, viewing);
        }
    }

    public List<KnownEpisode> MissingEpisodes(string groupKey, DateTimeOffset now)
    {
        lock (gate)
        {
            var available = EntriesFor(groupKey)
                .Where(e => e.Item.Available)
                .Select(e => (e.Item.Season, e.Item.Episode))
                .ToHashSet();
            return (State.EpisodeSchedules.GetValueOrDefault(groupKey) ?? [])
                .Where(e => e.Season > 0 && e.AirsAt <= now && !available.Contains((e.Season, e.Episode)))
                .OrderBy(e => e.Season)
                .ThenBy(e => e.Episode)
                .ToList();
        }
    }

    public bool Record(
        VideoItem item,
        double position,
        double duration,
        bool completed = false,
        DateTimeOffset? expectedUpdatedAt = null,
        bool deferSave = false
    )
    {
        if (!double.IsFinite(position) || position < 0 || !double.IsFinite(duration) || duration < 0)
            return false;
        bool newlyCompleted;
        lock (gate)
        {
            var entry = Remember(item);
            // A stale paused/closing PC session must not replace progress received from a phone.
            if (expectedUpdatedAt is { } expected && (entry.Progress?.UpdatedAt ?? DateTimeOffset.MinValue) != expected)
                return false;
            SeedViewing(entry);
            var viewingId = activeViewings.GetValueOrDefault(entry.Item.Id)
                ?? (entry.Progress?.ViewingId is { Length: > 0 } previous ? previous : Guid.NewGuid().ToString("N"));
            newlyCompleted =
                completed
                && (
                    entry.Progress?.Completed != true
                    || activeViewings.ContainsKey(entry.Item.Id)
                        && !State.Viewings.GetValueOrDefault(entry.Item.Id, []).Any(v => v.Id == viewingId)
                );
            entry.Progress = new Bookmark
            {
                Position = duration > 0 ? Math.Min(position, duration) : position,
                Duration = duration > 0 ? duration : item.Duration,
                Completed = completed,
                UpdatedAt = DateTimeOffset.UtcNow,
                ViewingId = viewingId,
            };
            if (item.Path.Length > 0)
                State.Progress[item.Path] = entry.Progress;
            if (newlyCompleted)
                AddViewing(entry.Item, viewingId, entry.Progress.UpdatedAt, "Playback");
            if (duration > 0)
                entry.Item.Duration = duration;
            if (newlyCompleted)
                CommunityQueue.Enqueue(State, entry.Item, entry.Progress.UpdatedAt);
            if (deferSave)
                SaveDeferredCore(false);
            else
                Save();
        }
        if (newlyCompleted)
            WatchCompleted?.Invoke();
        return true;
    }

    public void Mark(VideoItem item, bool completed)
    {
        lock (gate)
        {
            var old = GetProgress(item);
            var entry = Remember(item);
            SeedViewing(entry);
            var viewingId = old?.Completed == true ? old.ViewingId : Guid.NewGuid().ToString("N");
            if (viewingId.Length == 0)
                viewingId = Guid.NewGuid().ToString("N");
            entry.Progress = new()
            {
                Position = completed ? old?.Duration ?? item.Duration : 0,
                Duration = old?.Duration ?? item.Duration,
                Completed = completed,
                UpdatedAt = DateTimeOffset.UtcNow,
                ViewingId = viewingId,
            };
            if (item.Path.Length > 0)
                State.Progress[item.Path] = entry.Progress;
            if (completed && old?.Completed != true)
                AddViewing(entry.Item, viewingId, entry.Progress.UpdatedAt, "Marked watched");
            if (completed)
                CommunityQueue.Enqueue(State, entry.Item, entry.Progress.UpdatedAt);
            if (!completed)
                CommunityQueue.CancelPending(State, entry.Item.Id);
            Save();
        }
        if (completed)
            WatchCompleted?.Invoke();
    }

    public IntroSegment? GetIntro(VideoItem item)
    {
        if (State.Intros.TryGetValue(item.Path, out var own))
            return own.Valid ? own : null;
        if (State.Catalog.GetValueOrDefault(item.Id) is { } entry)
            foreach (var alias in entry.Paths)
                if (State.Intros.TryGetValue(alias, out var saved) && saved.Valid)
                    return saved;
        if (State.Intros.TryGetValue(item.GroupKey + ":season:" + item.Season, out var series))
            return series.Valid ? series : null;
        if (State.Catalog.GetValueOrDefault(item.Id)?.ImportedIntro is { Valid: true } imported)
            return imported;
        if (metadata.TryGetValue(item.Path, out var m) && m.IntroEnd > m.IntroStart)
            return new() { Start = m.IntroStart, End = m.IntroEnd };
        return null;
    }

    public static VideoItem? Next(VideoItem current, IEnumerable<VideoItem> items, PlayerState state)
    {
        if (current.IsMovie)
            return null;
        return items
            .Where(x =>
                x.GroupKey == current.GroupKey
                && (current.Season == 0 ? x.Season == 0 : x.Season > 0)
                && (x.Season > current.Season || (x.Season == current.Season && x.Episode > current.Episode))
                && x.Available
                && !((ProgressFor(state, x)?.Completed) ?? false)
            )
            .OrderBy(x => x.Season)
            .ThenBy(x => x.Episode)
            .FirstOrDefault();
    }

    public IntroSegment? GetOutro(VideoItem item)
    {
        if (State.Outros.TryGetValue(item.Id, out var own))
            return own.Valid ? own : null;
        if (State.Outros.TryGetValue(item.GroupKey + ":season:" + item.Season, out var season))
            return season.Valid ? season : null;
        return State.Catalog.GetValueOrDefault(item.Id)?.ImportedOutro;
    }

    public double CompletionStart(VideoItem item, double duration)
    {
        if (!State.MarkWatchedAtCredits || duration <= 0)
            return duration;
        if (GetOutro(item) is { Valid: true } outro && outro.Start >= duration * .5 && outro.Start < duration)
            return outro.Start;
        // A conservative fallback for unlabelled videos; a saved credits marker takes precedence.
        return duration < 300 ? duration : duration - Math.Min(duration * .05, item.IsMovie ? 180 : 90);
    }

    public void RememberChapters(VideoItem item, IntroSegment? intro, IntroSegment? outro)
    {
        lock (gate)
        {
            var entry = Remember(item);
            bool changed = false;
            if (intro is { Valid: true } && entry.ImportedIntro == null)
            {
                entry.ImportedIntro = intro;
                changed = true;
            }
            if (outro is { Valid: true } && entry.ImportedOutro == null)
            {
                entry.ImportedOutro = outro;
                changed = true;
            }
            if (changed)
                Save();
        }
    }

    public VideoItem? ContinueFor(IEnumerable<VideoItem> group)
    {
        var list = group.ToList();
        var last = list.Where(x => GetProgress(x) is { } b && (b.Position > 0 || b.Completed))
            .OrderByDescending(x =>
                new[]
                {
                    GetProgress(x)!.UpdatedAt,
                    ViewingHistory(x).FirstOrDefault()?.WatchedAt ?? DateTimeOffset.MinValue,
                }.Max()
            )
            .FirstOrDefault();
        if (last == null)
            return null;
        var bookmark = GetProgress(last)!;
        var watchedAt = ViewingHistory(last).FirstOrDefault()?.WatchedAt;
        return bookmark.Completed || watchedAt > bookmark.UpdatedAt ? Next(last, list, State) : last;
    }

    public VideoItem DefaultFor(IEnumerable<VideoItem> group)
    {
        var list = group.OrderBy(x => x.Season == 0 ? int.MaxValue : x.Season).ThenBy(x => x.Episode).ToList();
        return ContinueFor(list) ?? list.FirstOrDefault(x => GetProgress(x)?.Completed != true) ?? list[0];
    }

    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv",
        ".mp4",
        ".avi",
        ".mov",
        ".m4v",
        ".webm",
        ".ts",
        ".m2ts",
        ".wmv",
        ".mpg",
        ".mpeg",
    };
    static readonly Regex EpisodePattern = new(
        @"(?i)(?<![a-z0-9])(?:S(?<s>\d{1,2})[ ._-]*E(?<e>\d{1,3})|(?<s>\d{1,2})x(?<e>\d{1,3}))(?!\d)",
        RegexOptions.Compiled
    );

    public List<VideoItem> Scan(out List<string> warnings)
    {
        lock (scanGate)
            return ScanCore(out warnings);
    }

    List<VideoItem> ScanCore(out List<string> warnings)
    {
        List<LibraryRoot> roots;
        HashSet<string> removed,
            knownPaths;
        List<string> legacyPaths,
            artPaths,
            catalogIds;
        lock (gate)
        {
            catalogIds = State.Catalog.Keys.ToList();
            roots = State.Roots.Select(LibrarySnapshot.Copy).ToList();
            removed = new(State.RemovedFiles);
            legacyPaths = State.Progress.Keys.ToList();
            knownPaths = State.Catalog.Values.SelectMany(e => e.Paths).ToHashSet(StringComparer.OrdinalIgnoreCase);
            artPaths = State
                .Catalog.Values.SelectMany(e => new[] { e.Item.Poster, e.Item.Backdrop })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        var missingRemoved = removed.Where(f => !File.Exists(f)).ToList();
        var scanned = new List<(string Root, VideoItem Item, string Fingerprint)>();
        var legacy = new List<VideoItem>();
        warnings = [];
        foreach (var root in roots)
        {
            if (!Directory.Exists(root.Path))
            {
                warnings.Add("Folder unavailable: " + root.Path);
                continue;
            }
            try
            {
                foreach (
                    var file in Directory.EnumerateFiles(
                        root.Path,
                        "*",
                        new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }
                    )
                )
                {
                    if (
                        !Extensions.Contains(System.IO.Path.GetExtension(file))
                        || removed.Contains(file.ToUpperInvariant())
                    )
                        continue;
                    var rel = System.IO.Path.GetRelativePath(root.Path, file);
                    var parts = rel.Split(System.IO.Path.DirectorySeparatorChar);
                    if (parts.Any(p => Regex.IsMatch(p, @"(?i)^(extras?|samples?|trailers?|screens|NC)$")))
                        continue;
                    var name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (Regex.IsMatch(name, @"(?i)\b(sample|trailer|NCOP|NCED)\b"))
                        continue;
                    metadata.TryGetValue(file, out var seed);
                    var match = EpisodePattern.Match(name);
                    int season = match.Success ? int.Parse(match.Groups["s"].Value) : 1;
                    if (!match.Success)
                    {
                        var seasonFolder = parts
                            .Take(parts.Length - 1)
                            .Select(p => Regex.Match(p, @"(?i)^(?:season[ ._-]*|s)(\d{1,2})$"))
                            .LastOrDefault(m => m.Success);
                        if (seasonFolder != null)
                            season = int.Parse(seasonFolder.Groups[1].Value);
                        else if (
                            parts
                                .Take(parts.Length - 1)
                                .Any(p => p.Equals("Specials", StringComparison.OrdinalIgnoreCase))
                        )
                            season = 0;
                    }
                    int episode = match.Success ? int.Parse(match.Groups["e"].Value) : 0;
                    string rawTitle;
                    if (root.Movies)
                        rawTitle = name;
                    else if (parts.Length > 1 && !Regex.IsMatch(parts[0], @"(?i)^(season[ ._-]*\d+|specials)$"))
                        rawTitle = parts[0];
                    else
                        rawTitle = match.Success ? name[..match.Index] : name;
                    if (!root.Movies && episode == 0)
                    {
                        var anime = Regex.Match(name, @"(?:^|\s-\s|\s)(\d{1,3})(?:\s|\[|$)");
                        if (anime.Success)
                        {
                            episode = int.Parse(anime.Groups[1].Value);
                            if (parts.Length == 1 || Regex.IsMatch(parts[0], @"(?i)^(season[ ._-]*\d+|specials)$"))
                                rawTitle = name[..anime.Index];
                        }
                    }
                    if (!root.Movies && episode == 0)
                        continue;
                    var yearMatch = Regex.Match(rawTitle, @"\b(19\d{2}|20\d{2})\b");
                    var rawName = seed is { Title.Length: > 0 } ? seed.Title : rawTitle;
                    var title = root.Movies ? CleanTitle(rawName) : CleanShowTitle(rawName);
                    if (title.Length == 0)
                        title = name;
                    var folder = System.IO.Path.GetDirectoryName(file)!;
                    var poster = seed?.Poster ?? "";
                    if (!File.Exists(poster))
                        poster = new[]
                        {
                            System.IO.Path.Combine(folder, "poster.jpg"),
                            System.IO.Path.Combine(folder, "folder.jpg"),
                        }.FirstOrDefault(File.Exists)
                            ?? "";
                    var parsed = new VideoItem
                    {
                        Path = file,
                        Title = title,
                        IsMovie = root.Movies,
                        Season = season,
                        Episode = episode,
                        Year =
                            seed is { Year: > 0 } ? seed.Year
                            : yearMatch.Success ? int.Parse(yearMatch.Value)
                            : 0,
                        EpisodeTitle = seed?.EpisodeTitle ?? "",
                        Summary = seed?.Summary ?? "",
                        Poster = poster,
                        Backdrop = seed?.Backdrop ?? "",
                        Duration = seed?.Duration ?? 0,
                    };
                    scanned.Add((root.Path, parsed, Fingerprint(file)));
                }
            }
            catch (Exception e)
            {
                warnings.Add("Could not scan: " + root.Path);
                Log(e);
            }
        }
        // Recover old path bookmarks even if a media folder is already missing during migration.
        foreach (var path in legacyPaths)
        {
            if (knownPaths.Contains(path))
                continue;
            metadata.TryGetValue(path, out var seed);
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var match = EpisodePattern.Match(name);
            var movie = !match.Success;
            var title = seed is { Title.Length: > 0 }
                ? seed.Title
                : CleanTitle(match.Success ? name[..match.Index] : name);
            legacy.Add(
                new VideoItem
                {
                    Path = path,
                    Title = title,
                    IsMovie = movie,
                    Available = false,
                    Season = match.Success ? int.Parse(match.Groups["s"].Value) : 1,
                    Episode = match.Success ? int.Parse(match.Groups["e"].Value) : 0,
                    Year = seed?.Year ?? 0,
                    Summary = seed?.Summary ?? "",
                    EpisodeTitle = seed?.EpisodeTitle ?? "",
                    Poster = seed?.Poster ?? "",
                    Backdrop = seed?.Backdrop ?? "",
                    Duration = seed?.Duration ?? 0,
                }
            );
        }
        var art = artPaths
            .Concat(scanned.SelectMany(f => new[] { f.Item.Poster, f.Item.Backdrop }))
            .Concat(legacy.SelectMany(i => new[] { i.Poster, i.Backdrop }))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p, p => CacheArtwork(p), StringComparer.OrdinalIgnoreCase);
        var items = new Dictionary<string, VideoItem>(StringComparer.OrdinalIgnoreCase);
        lock (gate)
        {
            var scanPaths = State
                .Catalog.Values.SelectMany(e => e.Paths.Select(p => (Path: p, Entry: e)))
                .GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Entry).ToList(), StringComparer.OrdinalIgnoreCase);
            foreach (var id in catalogIds)
                if (State.Catalog.TryGetValue(id, out var entry))
                    entry.Item.Available = false;
            foreach (var path in missingRemoved)
                State.RemovedFiles.Remove(path);
            foreach (var found in scanned)
            {
                if (
                    !State.Roots.Any(r => r.Path == found.Root)
                    || State.RemovedFiles.Contains(found.Item.Path.ToUpperInvariant())
                )
                    continue;
                items[found.Item.Path] = Remember(found.Item, found.Fingerprint, art, scanPaths).Item;
            }
            foreach (var item in legacy)
                if (
                    State.Progress.ContainsKey(item.Path)
                    && !State.RemovedFiles.Contains(item.Path.ToUpperInvariant())
                    && !scanPaths.ContainsKey(item.Path)
                )
                    Remember(item, "", art, scanPaths);
        }
        Save();
        return items.Values.OrderBy(x => x.Title).ThenBy(x => x.Season).ThenBy(x => x.Episode).ToList();
    }

    public List<VideoItem> Archive
    {
        get
        {
            lock (gate)
                return State.Catalog.Values.Select(x => x.Item).ToList();
        }
    }

    public List<VideoItem> EpisodesFor(string groupKey)
    {
        lock (gate)
        {
            var saved = EntriesFor(groupKey).Select(e => e.Item).ToList();
            if (saved.Count == 0 || saved[0].IsMovie)
                return saved;
            var representative = saved[0];
            foreach (var episode in State.EpisodeSchedules.GetValueOrDefault(groupKey) ?? [])
            {
                if (
                    episode.Season <= 0
                    || episode.Episode <= 0
                    || saved.Any(i => i.Season == episode.Season && i.Episode == episode.Episode)
                )
                    continue;
                var id =
                    "episode-"
                    + Convert.ToHexString(
                        SHA256.HashData(
                            System.Text.Encoding.UTF8.GetBytes($"{groupKey}:{episode.Season}:{episode.Episode}")
                        )
                    );
                saved.Add(
                    new()
                    {
                        Id = id,
                        CollectionKey = groupKey,
                        Title = representative.Title,
                        Year = representative.Year,
                        Season = episode.Season,
                        Episode = episode.Episode,
                        EpisodeTitle = episode.Title,
                        Available = false,
                        EpisodeImage = episode.Image,
                        Poster = representative.Poster,
                        Backdrop = representative.Backdrop,
                        Summary = representative.Summary,
                        Genres = representative.Genres,
                        ImdbId = representative.ImdbId,
                        ReviewTitle = representative.ReviewTitle,
                        SourceName = representative.SourceName,
                        SourceUrl = representative.SourceUrl,
                    }
                );
            }
            return saved.OrderBy(i => i.Season == 0 ? int.MaxValue : i.Season).ThenBy(i => i.Episode).ToList();
        }
    }

    public void ApplyDetails(string groupKey, OnlineDetails? details, bool replaceArtwork = false)
    {
        lock (gate)
        {
            if (details is { Schedule.Count: > 0 })
                State.EpisodeSchedules[groupKey] = details.Schedule;
            if (details is { Status.Length: > 0 })
                State.ShowStates[groupKey] = details.Status;
            if (details is { Runtime: > 0 })
                State.ShowRuntimes[groupKey] = details.Runtime;
            foreach (var entry in EntriesFor(groupKey))
            {
                entry.MetadataCheckedAt = DateTimeOffset.UtcNow;
                if (details == null)
                    continue;
                var item = entry.Item;
                if (details.Title.Length > 0 && !State.Matches.ContainsKey(item.Id))
                    item.Title = details.Title;
                if (details.Summary.Length > 0)
                    item.Summary = details.Summary;
                if ((replaceArtwork || !File.Exists(item.Poster)) && File.Exists(details.Poster))
                    item.Poster = details.Poster;
                if ((replaceArtwork || !File.Exists(item.Backdrop)) && File.Exists(details.Backdrop))
                    item.Backdrop = details.Backdrop;
                if (item.Year == 0)
                    item.Year = details.Year;
                item.Genres = details.Genres;
                item.SourceName = details.SourceName;
                item.SourceUrl = details.SourceUrl;
                if (details.ImdbId.Length > 0 && !State.Matches.ContainsKey(item.Id))
                    item.ImdbId = details.ImdbId;
                if (details.Title.Length > 0 && !State.Matches.ContainsKey(item.Id))
                    item.ReviewTitle = details.Title;
                if (details.Episodes.TryGetValue($"{item.Season}:{item.Episode}", out var episode))
                {
                    item.EpisodeTitle = episode.Title;
                    item.EpisodeSummary = episode.Summary;
                    item.EpisodeImage = State
                        .EpisodeSchedules.GetValueOrDefault(groupKey)
                        ?.FirstOrDefault(e => e.Season == item.Season && e.Episode == item.Episode)
                        ?.Image
                        ?? item.EpisodeImage;
                }
            }
            CatalogChanged();
            Save();
        }
    }

    public bool MetadataRecentlyChecked(string groupKey)
    {
        lock (gate)
            return EntriesFor(groupKey) is { Count: > 0 } entries
                && entries.All(e => e.MetadataCheckedAt > DateTimeOffset.UtcNow.AddHours(-6));
    }

    static string MatchTitle(string title) => Regex.Replace(title.ToUpperInvariant(), @"[^\p{L}\p{N}]", "");

    static string Fingerprint(string path)
    {
        try
        {
            using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length == 0)
                return "";
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(BitConverter.GetBytes(file.Length));
            var block = new byte[65536];
            var count = file.Read(block);
            hash.AppendData(block, 0, count);
            file.Position = Math.Max(count, file.Length - block.Length);
            count = file.Read(block);
            hash.AppendData(block, 0, count);
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch
        {
            return "";
        }
    }

    CatalogEntry Remember(
        VideoItem item,
        string? scannedFingerprint = null,
        IReadOnlyDictionary<string, string>? scannedArt = null,
        Dictionary<string, List<CatalogEntry>>? scanPaths = null
    )
    {
        if (item.Id.Length > 0 && State.Catalog.TryGetValue(item.Id, out var known))
            return known;
        var fingerprint = scannedFingerprint ?? Fingerprint(item.Path);
        bool ArtExists(string? path) =>
            path != null
            && (scannedArt == null ? File.Exists(path) : scannedArt.GetValueOrDefault(path, path).Length > 0);
        string Artwork(string path) =>
            scannedArt == null ? CacheArtwork(path) : scannedArt.GetValueOrDefault(path, path);
        bool fileExists = scannedFingerprint != null ? item.Available : File.Exists(item.Path);
        bool SameTitle(CatalogEntry e) =>
            e.Item.IsMovie == item.IsMovie
            && e.Item.Season == item.Season
            && e.Item.Episode == item.Episode
            && MatchTitle(e.Item.Title) == MatchTitle(item.Title)
            && (e.Item.Year == 0 || item.Year == 0 || e.Item.Year == item.Year);
        IEnumerable<CatalogEntry> pathCandidates =
            scanPaths == null ? State.Catalog.Values : scanPaths.GetValueOrDefault(item.Path) ?? [];
        var entry = pathCandidates.FirstOrDefault(e =>
            e.Paths.Contains(item.Path, StringComparer.OrdinalIgnoreCase)
            && (
                State.Matches.ContainsKey(e.Item.Id)
                || SameTitle(e)
                || fingerprint.Length > 0 && fingerprint == e.Fingerprint
            )
        )
            ?? (
                fingerprint.Length > 0
                    ? State.Catalog.Values.FirstOrDefault(e =>
                        e.Fingerprint == fingerprint && e.Item.IsMovie == item.IsMovie
                    )
                    : null
            );
        if (entry == null)
        {
            var matches = State.Catalog.Values.Where(SameTitle).ToList();
            entry = matches.Count == 1 ? matches[0] : null;
        }
        var old = entry?.Item;
        var previousGroupKey = item.GroupKey;
        var id = old?.Id ?? (item.Id.Length > 0 ? item.Id : Guid.NewGuid().ToString("N"));
        var collection = old?.CollectionKey;
        // Schedule placeholders and logged titles already know their show; keep them in it.
        if (
            string.IsNullOrEmpty(collection)
            && item.CollectionKey.Length > 0
            && EntriesFor(item.CollectionKey).Count > 0
        )
            collection = item.CollectionKey;
        if (string.IsNullOrEmpty(collection))
        {
            var existingShows = item.IsMovie
                ? []
                : State
                    .Catalog.Values.Where(e =>
                        !e.Item.IsMovie
                        && MatchTitle(CleanShowTitle(e.Item.Title)) == MatchTitle(CleanShowTitle(item.Title))
                        && (e.Item.Year == 0 || item.Year == 0 || e.Item.Year == item.Year)
                    )
                    .Select(e => e.Item.GroupKey)
                    .Distinct()
                    .ToList();
            collection =
                existingShows.Count == 1 ? existingShows[0]
                : item.IsMovie ? "movie:" + id
                : "show:" + MatchTitle(CleanShowTitle(item.Title)) + ":" + item.Year;
        }
        metadata.TryGetValue(item.Path, out var seed);
        var correction = State.Matches.GetValueOrDefault(id);
        item = new VideoItem
        {
            Id = id,
            CollectionKey = collection,
            AddedAt = old?.AddedAt ?? (old == null && item.Available && fileExists ? DateTimeOffset.UtcNow : null),
            EpisodeImage = old?.EpisodeImage ?? item.EpisodeImage,
            Path = item.Path,
            Title = correction?.Title ?? old?.Title ?? item.Title,
            IsMovie = item.IsMovie,
            Season = correction?.Season ?? item.Season,
            Episode = correction?.Episode ?? item.Episode,
            Available = item.Available,
            Year = correction?.Year ?? (old?.Year > 0 ? old.Year : item.Year),
            Duration = old?.Duration > 0 ? old.Duration : item.Duration,
            EpisodeTitle = old?.EpisodeTitle.Length > 0 ? old.EpisodeTitle : item.EpisodeTitle,
            Summary = old?.Summary.Length > 0 ? old.Summary : item.Summary,
            Poster = Artwork(ArtExists(old?.Poster) ? old!.Poster : item.Poster),
            Backdrop = Artwork(ArtExists(old?.Backdrop) ? old!.Backdrop : item.Backdrop),
            Genres = old?.Genres ?? item.Genres,
            SourceName = old?.SourceName ?? item.SourceName,
            SourceUrl = old?.SourceUrl ?? item.SourceUrl,
            EpisodeSummary = old?.EpisodeSummary ?? item.EpisodeSummary,
            ImdbId = old?.ImdbId is { Length: > 0 } ? old.ImdbId : seed?.ImdbId ?? item.ImdbId,
            EpisodeImdbId = old?.EpisodeImdbId is { Length: > 0 }
                ? old.EpisodeImdbId
                : seed?.EpisodeImdbId ?? item.EpisodeImdbId,
            ReviewTitle = old?.ReviewTitle ?? item.ReviewTitle,
        };
        entry ??= new();
        entry.Item = item;
        // A new file for a removed title brings it back to the library.
        if (old == null && item.Available && fileExists)
            State.HiddenTitles.Remove(item.GroupKey);
        if (seed != null && seed.IntroEnd > seed.IntroStart)
            entry.ImportedIntro ??= new() { Start = seed.IntroStart, End = seed.IntroEnd };
        if (seed != null && seed.OutroEnd > seed.OutroStart)
            entry.ImportedOutro ??= new() { Start = seed.OutroStart, End = seed.OutroEnd };
        if (item.Path.Length > 0 && !entry.Paths.Contains(item.Path, StringComparer.OrdinalIgnoreCase))
            entry.Paths.Add(item.Path);
        if (fingerprint.Length > 0)
            entry.Fingerprint = fingerprint;
        var legacy = entry
            .Paths.Select(p => State.Progress.GetValueOrDefault(p))
            .Where(p => p != null)
            .OrderByDescending(p => p!.UpdatedAt)
            .FirstOrDefault();
        if (legacy != null && (entry.Progress == null || legacy.UpdatedAt > entry.Progress.UpdatedAt))
            entry.Progress = legacy;
        State.Catalog[id] = entry;
        CatalogChanged();
        if (scanPaths != null)
            foreach (var path in entry.Paths)
            {
                if (!scanPaths.TryGetValue(path, out var entries))
                    scanPaths[path] = entries = [];
                if (!entries.Contains(entry))
                    entries.Add(entry);
            }
        var oldIntroKey = previousGroupKey + ":season:" + item.Season;
        var newIntroKey = item.GroupKey + ":season:" + item.Season;
        if (State.Intros.TryGetValue(oldIntroKey, out var seasonIntro) && !State.Intros.ContainsKey(newIntroKey))
            State.Intros[newIntroKey] = seasonIntro;
        return entry;
    }

    public string CacheArtwork(string path)
    {
        if (!File.Exists(path))
            return "";
        try
        {
            var cache = System.IO.Path.Combine(DataDirectory, "art");
            Directory.CreateDirectory(cache);
            var full = System.IO.Path.GetFullPath(path);
            if (full.StartsWith(cache + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return full;
            var bytes = File.ReadAllBytes(path);
            var target = System.IO.Path.Combine(
                cache,
                Convert.ToHexString(SHA256.HashData(bytes)) + System.IO.Path.GetExtension(path)
            );
            if (!File.Exists(target))
                File.WriteAllBytes(target, bytes);
            return target;
        }
        catch (Exception e)
        {
            Log(e);
            return path;
        }
    }

    static IEnumerable<string> ArtworkReferences(PlayerState state) =>
        state
            .Catalog.Values.SelectMany(e => new[] { e.Item.Poster, e.Item.Backdrop, e.Item.EpisodeImage })
            .Concat(state.EpisodeSchedules.Values.SelectMany(e => e.Select(x => x.Image)));

    internal void MaintainCaches(
        DateTimeOffset now,
        long discoveryBudget = 64L * 1024 * 1024,
        long artworkBudget = 512L * 1024 * 1024
    )
    {
        DiskCacheMaintenance.Trim(
            System.IO.Path.Combine(DataDirectory, "discover"),
            now,
            TimeSpan.FromDays(30),
            discoveryBudget
        );
        var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long revision;
        void Protect(IEnumerable<string> paths)
        {
            foreach (var path in paths)
                if (path.Length > 0)
                    protectedNames.Add(System.IO.Path.GetFileName(path));
        }
        lock (gate)
        {
            if (savesBlocked)
                return;
            Protect(ArtworkReferences(State));
            revision = saveRevision;
        }
        Protect(metadata.Values.SelectMany(m => new[] { m.Poster, m.Backdrop }));
        var files = new Dictionary<string, (long Bytes, DateTime Written)>();
        (long Bytes, DateTime Written) Stamp(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.Length, info.LastWriteTimeUtc) : (-1, default);
        }
        var root = MobileProfiles.Root(DataDirectory);
        var registry = System.IO.Path.Combine(root, "profiles.json");
        try
        {
            if (File.Exists(registry))
            {
                files[registry] = Stamp(registry);
                var profiles = JsonSerializer.Deserialize<ProfileList>(File.ReadAllText(registry), JsonOptions);
                if (profiles == null || profiles.Profiles.Count == 0)
                    return;
                foreach (var profile in profiles.Profiles)
                {
                    if (!Profiles.ValidId(profile.Id))
                        return;
                    var folder = profile.Id == "default" ? root : System.IO.Path.Combine(root, "profiles", profile.Id);
                    if (
                        System
                            .IO.Path.GetFullPath(folder)
                            .Equals(System.IO.Path.GetFullPath(DataDirectory), StringComparison.OrdinalIgnoreCase)
                    )
                        continue;
                    var path = System.IO.Path.Combine(folder, "library.json");
                    files[path] = Stamp(path);
                    var other = JsonSerializer.Deserialize<PlayerState>(File.ReadAllText(path), JsonOptions);
                    if (other == null || Stamp(path) != files[path])
                        return;
                    Protect(ArtworkReferences(other));
                }
            }
            else
                files[registry] = Stamp(registry);
            DiskCacheMaintenance.Trim(
                System.IO.Path.Combine(DataDirectory, "art"),
                now,
                TimeSpan.FromDays(90),
                artworkBudget,
                path =>
                {
                    if (files.Any(p => Stamp(p.Key) != p.Value))
                        return true;
                    lock (gate)
                    {
                        if (revision != saveRevision)
                        {
                            Protect(ArtworkReferences(State));
                            revision = saveRevision;
                        }
                        return protectedNames.Contains(System.IO.Path.GetFileName(path));
                    }
                },
                gate
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            try
            {
                Log(e);
            }
            catch { }
        }
    }

    public static string CleanShowTitle(string value) =>
        CleanTitle(Regex.Replace(value, @"(?i)[ ._-]+(?:S\d{1,2}(?:[ ._-]|$)|Season[ ._-]+\d+\b).*$", ""));

    public static string CleanTitle(string value)
    {
        value = Regex.Replace(value, @"\[[^\]]*\]", "");
        value = Regex.Replace(
            value,
            @"(?i)(?:4K|2160p|1080p|720p|480p|WEB[- .]?DL|WEBRip|BluRay|BDRip|x26[45]|HEVC|H264|H265|HDR|DV)(?:\b|$).*",
            ""
        );
        value = Regex.Replace(value, @"[.( _-]*(?:19\d{2}|20\d{2})[) ]*.*$", "");
        value = value.Replace('.', ' ').Replace('_', ' ').Trim(' ', '-', '(', ')');
        return Regex.Replace(value, @"\s+", " ");
    }

    public static string Time(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
}
