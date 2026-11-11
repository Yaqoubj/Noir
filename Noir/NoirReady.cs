namespace Noir;

public sealed class NoirReadyState
{
    public int Version { get; set; } = 1;
    public long BudgetBytes { get; set; } = 20 * NoirReadyPolicy.GB;
    public long KeepFreeBytes { get; set; } = 10 * NoirReadyPolicy.GB;
    public bool Paused { get; set; }

    // Before the A rules (RulesVersion 0) this one switch meant "remove watched Keep ready files 7 days after watching".
    // It is kept in step with RemoveAfterHours for older readers; the rules below decide.
    public bool CleanupWatched { get; set; }
    public bool Migrated { get; set; }

    // A1–A8 global defaults (sharpen round). RulesVersion 0 = saved before them; InitializeReady migrates once.
    public int RulesVersion { get; set; }

    // A1: -1 never, 1 right after watching (waits an hour), 24 / 168 / 720 = 1, 7, 30 days.
    public int RemoveAfterHours { get; set; } = -1;

    // A2: "Keep ready" (only what Keep ready downloaded) or "Noir" (everything Noir downloaded).
    public string RemoveScope { get; set; } = "Keep ready";
    public bool RemoveMovies { get; set; }

    // A3: the last N watched episodes of a show stay.
    public int KeepLastWatched { get; set; }

    // A4: false = delete permanently (today), true = Recycle Bin.
    public bool RecycleRemoved { get; set; }

    // A5: 1, 2, 3, 5 or -1 (rest of the season).
    public int ReadyCount { get; set; } = 2;

    // A6+A7: "Switched" (only shows I switch on), "Watching" (shows I am watching), "Watchlist" (watching + My List).
    public string AutoShows { get; set; } = "Switched";

    // A8: local minutes after midnight; null = any time. The window may cross midnight.
    public int? WindowStart { get; set; }
    public int? WindowEnd { get; set; }
    public HashSet<string> PausedJobs { get; set; } = [];
    public HashSet<string> Offered { get; set; } = [];
    public List<ReadyManagedFile> ManagedFiles { get; set; } = [];

    // A2: files Noir downloaded outside Keep ready (manual jobs), recorded when the download completes.
    public List<ReadyManagedFile> NoirFiles { get; set; } = [];
    public Dictionary<string, ReadyTitlePlan> Titles { get; set; } = [];
    public List<ReadyWhy> Why { get; set; } = [];

    public void Log(string code, string title = "", string detail = "", DateTimeOffset? now = null)
    {
        Why.Add(
            new()
            {
                At = now ?? DateTimeOffset.UtcNow,
                Code = code,
                Title = title,
                Detail = detail,
            }
        );
        if (Why.Count > 200)
            Why.RemoveRange(0, Why.Count - 200);
    }
}

public sealed class ReadyWhy
{
    public DateTimeOffset At { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class ReadyManagedFile
{
    public string JobId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string Group { get; set; } = "";
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string ReleaseKey { get; set; } = "";
    public long ChargedBytes { get; set; }
    public long FileBytes { get; set; }
    public DateTime? LastWriteUtc { get; set; }
    public bool Pinned { get; set; }
    public bool CleanupCancelled { get; set; }
    public DateTimeOffset? RemoveAt { get; set; }

    // The removal delay (hours) RemoveAt was worked out with; 0 = unknown (saved before the A rules), never recomputed.
    public int RuleHours { get; set; }

    // Manual downloads only (NoirFiles): enough of the job to judge the file after "Clear finished" removed the job.
    public string Root { get; set; } = "";
    public bool Movie { get; set; }
    public int Season { get; set; }
    public int Episode { get; set; }
    public string ImdbId { get; set; } = "";
}

public sealed class ReadyTitlePlan
{
    public string Group { get; set; } = "";
    public string Title { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string State { get; set; } = "Waiting";
    public string Reason { get; set; } = "Checking sources";
    public string Quality { get; set; } = "";
    public long Bytes { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset RecheckAt { get; set; }
    public List<ReadyEpisode> Episodes { get; set; } = [];
}

public sealed record ReadyEpisode(
    string ItemId,
    string Group,
    string Title,
    int Season,
    int Episode,
    bool Watched,
    bool Partial,
    bool Local,
    DateTimeOffset? AirsAt
);

public sealed record ReadySource(
    string Id,
    string Release,
    long FileBytes,
    long ReleaseBytes,
    bool Pack,
    bool Confirmed,
    string Quality,
    long AlreadyReserved = 0
);

public sealed record ReadyStorage(long UsedBytes, long ReservedBytes, long FreeBytes, long DiskReservedBytes);

public sealed record ReadyDecision(ReadySource? Source, string Reason, long Charge = 0);

public static class NoirReadyPolicy
{
    public const long GB = 1073741824;

    /// <summary>U98: sizes in the Keep ready sheet read "60 GB", with decimals only when the figure is not whole.</summary>
    public static string SizeText(long bytes) =>
        bytes == 0 ? "0 GB"
        : bytes >= GB ? (bytes / (double)GB).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " GB"
        : DownloadNaming.Size(bytes);

    static bool ValidCount(int? n) => n is 1 or 2 or 3 or 5 or -1;

    public static int MigrateCount(ShowDownloadPreference preference) =>
        ValidCount(preference.KeepReadyCount) ? preference.KeepReadyCount!.Value : 2;

    // A5: the show's own count, else the global default; anything unknown stays today's 2.
    public static int Count(ShowDownloadPreference p, NoirReadyState s) =>
        ValidCount(p.KeepReadyCount) ? p.KeepReadyCount!.Value
        : ValidCount(s.ReadyCount) ? s.ReadyCount
        : 2;

    // The show's own switch: true = on, false = off, null = "Same as default" (AutoDownload is the pre-Keep-ready switch).
    public static bool Enabled(ShowDownloadPreference p) => p.KeepReadyEnabled ?? p.AutoDownload;

    // A6+A7: why a show downloads automatically, or null. The per-show switch always wins over the global choice.
    public static string? AutomaticReason(ShowDownloadPreference p, NoirReadyState s, bool watching, bool watchlist)
    {
        if (p.KeepReadyEnabled == true || p.KeepReadyEnabled == null && p.AutoDownload)
            return "Switched on for this show";
        if (p.AlsoDownload)
            return "Also download automatically";
        if (p.KeepReadyEnabled == false)
            return null;
        if (s.AutoShows is "Watching" or "Watchlist" && watching)
            return "A show you are watching";
        if (s.AutoShows == "Watchlist" && watchlist)
            return "On your watchlist";
        return null;
    }

    static bool ValidHours(int? h) => h is -1 or 1 or 24 or 168 or 720;

    // A1: the removal delay in hours for a show (-1 = never). Before the A rules, CleanupWatched meant 7 days.
    public static int RemoveHours(NoirReadyState s, ShowDownloadPreference? p = null) =>
        ValidHours(p?.RemoveAfterHours) ? p!.RemoveAfterHours!.Value
        : s.RulesVersion < 1 ? (s.CleanupWatched ? 168 : -1)
        : ValidHours(s.RemoveAfterHours) ? s.RemoveAfterHours
        : -1;

    public static int KeepLast(NoirReadyState s, ShowDownloadPreference? p = null) =>
        p?.KeepLastWatched is >= 0 and <= 3 ? p.KeepLastWatched.Value : Math.Clamp(s.KeepLastWatched, 0, 3);

    public static string RuleLabel(int hours) =>
        hours switch
        {
            1 => "Right after watching",
            24 => "1 day after watching",
            168 => "7 days after watching",
            720 => "30 days after watching",
            _ => "Never",
        };

    // A1: when a watched file is removed. First scheduling keeps today's rule (a date already past becomes now + 1 day).
    // After a rule change a later date applies at once; an earlier one is never sooner than now + 1 day.
    public static DateTimeOffset Schedule(
        DateTimeOffset? current,
        int currentHours,
        int hours,
        DateTimeOffset watchedAt,
        DateTimeOffset now
    )
    {
        var due = watchedAt.AddHours(hours);
        if (current is not { } at)
            return due > now ? due : now.AddDays(1);
        if (currentHours == 0 || currentHours == hours || due == at)
            return at;
        if (due > at)
            return due;
        var floor = now.AddDays(1);
        return due > floor ? due
            : at < floor ? at
            : floor;
    }

    // A8: whether a new automatic download may start at this local time of day.
    public static bool InWindow(int? start, int? end, TimeSpan local)
    {
        if (start is not int a || end is not int b || a == b)
            return true;
        var t = (int)local.TotalMinutes;
        return a < b ? t >= a && t < b : t >= a || t < b;
    }

    public static DateTimeOffset NextWindowStart(int start, DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        var at = new DateTimeOffset(local.Date.AddMinutes(start), local.Offset);
        return at > local ? at : at.AddDays(1);
    }

    public static List<ReadyEpisode> Next(IEnumerable<ReadyEpisode> input, int count, DateTimeOffset now)
    {
        var all = input
            .Where(e => !e.Watched && e.Season > 0 && e.Episode > 0 && e.AirsAt is { } air && air <= now)
            .OrderBy(e => e.Season)
            .ThenBy(e => e.Episode)
            .ToList();
        if (count == -1)
            return all.Count == 0 ? [] : all.Where(e => e.Season == all[0].Season).ToList();
        return all.Take(count is 1 or 2 or 3 or 5 ? count : 2).ToList();
    }

    public static List<IGrouping<string, ReadyEpisode>> OrderShows(IEnumerable<ReadyEpisode> episodes) =>
        episodes
            .GroupBy(e => e.Group)
            .OrderByDescending(g => g.Any(e => e.Partial))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

    public static ReadyDecision Choose(
        IEnumerable<ReadySource> options,
        NoirReadyState state,
        ReadyStorage storage,
        string quality
    )
    {
        if (state.Paused)
            return new(null, "Preparation paused");
        if (storage.UsedBytes + storage.ReservedBytes > state.BudgetBytes)
            return new(null, "Preparation budget reached");
        if (storage.FreeBytes - storage.DiskReservedBytes < Math.Max(10 * GB, state.KeepFreeBytes))
            return new(null, "Keeping free space available");
        var ready = options
            .Where(o => o.Confirmed && o.FileBytes > 0 && (!o.Pack || o.ReleaseBytes > 0))
            .OrderByDescending(o => quality == "Any" || o.Quality == quality)
            .ThenBy(o => o.Pack ? Math.Max(o.FileBytes, o.ReleaseBytes) : o.FileBytes)
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .ToList();
        if (ready.Count == 0)
            return new(null, "Availability or size unconfirmed; recheck scheduled");
        string reason = "Preparation budget reached";
        foreach (var o in ready)
        {
            var bytes = Math.Max(
                0,
                (o.Pack ? Math.Max(o.FileBytes, o.ReleaseBytes) : o.FileBytes) - Math.Max(0, o.AlreadyReserved)
            );
            if (bytes > Math.Max(0, state.BudgetBytes - storage.UsedBytes - storage.ReservedBytes))
                continue;
            if (
                Math.Max(o.FileBytes, bytes)
                > Math.Max(0, storage.FreeBytes - storage.DiskReservedBytes - Math.Max(10 * GB, state.KeepFreeBytes))
            )
            {
                reason = "Keeping free space available";
                continue;
            }
            return new(o, "Confirmed ready source", bytes);
        }
        return new(null, reason);
    }

    public static bool CanRemove(
        bool cleanup,
        bool managed,
        bool watched,
        bool pinned,
        bool cancelled,
        DateTimeOffset? watchedAt,
        IEnumerable<bool?> otherProfiles,
        DateTimeOffset now,
        int hours = 168
    ) =>
        cleanup
        && managed
        && watched
        && !pinned
        && !cancelled
        && hours > 0
        && watchedAt is { } at
        && at <= now.AddHours(-hours)
        && otherProfiles.All(done => done == true);

    public static DateTimeOffset Backoff(DateTimeOffset now, int attempts) =>
        now.AddMinutes(Math.Min(360, 15 * Math.Pow(2, Math.Clamp(attempts - 1, 0, 5))));
}
