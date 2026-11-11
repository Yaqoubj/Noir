using System.IO;

namespace Noir;

public sealed class WatchSyncJob
{
    public string Provider { get; set; } = "";
    public string ItemId { get; set; } = "";
    public DateTimeOffset WatchedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
    public DateTimeOffset RetryAt { get; set; }
    public int Attempts { get; set; }
    public string Message { get; set; } = "Waiting to sync";

    // Set when the title itself cannot be synced (no verified ID, special, episode match). The job stays
    // parked until Retry or until the title's identity changes, and never slows the provider down.
    public string BlockedKey { get; set; } = "";

    // Queued by "Also send my past history"; cancelling that backlog removes these, never the newer ones.
    public bool Backlog { get; set; }
}

/// <summary>The website as a whole failed (could not load, signed out, controls unavailable): the provider backs off.</summary>
public sealed class SyncSiteException(string message) : IOException(message);

/// <summary>This title cannot be synced right now: the job waits for Retry or a title change.</summary>
public sealed class SyncTitleException(string message) : IOException(message);

/// <summary>Per-provider backoff and pace counters, stored in the library so a restart does not reset them.</summary>
public sealed class SyncPaceState
{
    public int Failures { get; set; }
    public DateTimeOffset Until { get; set; }

    // When recent marks were sent (page loads that reached the site), newest last, kept for 24 hours.
    public List<DateTimeOffset> Marks { get; set; } = [];

    // F13: when the provider's current run of site failures began; cleared by the next success.
    public DateTimeOffset? FailingSince { get; set; }

    // S3: "Also send my past history" is paused for this provider; its backlog jobs stay queued.
    public bool BacklogPaused { get; set; }

    // F14: the last daily title-ID lookup, kept on the reserved entry CommunityBackoff.IdsKey.
    public DateTimeOffset LookupAt { get; set; }

    // Stremio's two switches, kept on its own entry ("Stremio"): mark watched in the account, and add titles the
    // account lacks. Both off by default. (Stored here so the setting travels with the queue's own state.)
    public bool MarkWatched { get; set; }
    public bool AddTitles { get; set; }
}

public sealed class CommunityBackoff(Dictionary<string, SyncPaceState>? stored = null)
{
    public static readonly TimeSpan[] Steps =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(24),
    ];

    public const int PerHour = 30,
        PerDay = 200;
    public static readonly TimeSpan Spacing = TimeSpan.FromSeconds(4);

    readonly Dictionary<string, SyncPaceState> state = stored ?? [];

    /// <summary>Reserved entry for state that belongs to no website (the daily title-ID lookup).</summary>
    public const string IdsKey = "ids";

    public DateTimeOffset? FailingSince(string provider) =>
        state.TryGetValue(provider, out var s) ? s.FailingSince : null;

    public bool BacklogPaused(string provider) => state.TryGetValue(provider, out var s) && s.BacklogPaused;

    public void SetBacklogPaused(string provider, bool paused) => Of(provider).BacklogPaused = paused;

    public DateTimeOffset IdLookupAt
    {
        get => state.TryGetValue(IdsKey, out var s) ? s.LookupAt : DateTimeOffset.MinValue;
        set => Of(IdsKey).LookupAt = value;
    }

    SyncPaceState Of(string provider) => state.TryGetValue(provider, out var s) ? s : state[provider] = new();

    public int Failures(string provider) => state.TryGetValue(provider, out var s) ? s.Failures : 0;

    public DateTimeOffset Until(string provider) =>
        state.TryGetValue(provider, out var s) ? s.Until : DateTimeOffset.MinValue;

    public bool Ready(string provider, DateTimeOffset now) => now >= Until(provider);

    public DateTimeOffset SiteFailed(string provider, DateTimeOffset now)
    {
        var s = Of(provider);
        s.Failures++;
        s.FailingSince ??= now;
        return s.Until = now + Steps[Math.Min(s.Failures, Steps.Length) - 1];
    }

    public void Succeeded(string provider)
    {
        if (state.TryGetValue(provider, out var s))
        {
            s.Failures = 0;
            s.Until = DateTimeOffset.MinValue;
            s.FailingSince = null;
        }
    }

    /// <summary>Retry clears the failure waits; the pace history stays, a click does not reset the sites' limits.</summary>
    public void Reset()
    {
        foreach (var s in state.Values)
        {
            s.Failures = 0;
            s.Until = DateTimeOffset.MinValue;
        }
    }

    /// <summary>The websites this backoff has state for (the reserved entry is not one).</summary>
    public IEnumerable<string> Providers() => state.Keys.Where(k => k != IdsKey).ToList();

    public int MarksIn(string provider, DateTimeOffset now, TimeSpan window) =>
        state.TryGetValue(provider, out var s) ? s.Marks.Count(m => m > now - window && m <= now) : 0;

    /// <summary>Zero when another mark may go now, otherwise how long until the hourly or daily cap frees a slot.</summary>
    public TimeSpan CapWait(string provider, DateTimeOffset now)
    {
        if (!state.TryGetValue(provider, out var s))
            return TimeSpan.Zero;
        s.Marks.RemoveAll(m => m <= now - TimeSpan.FromDays(1));
        var wait = TimeSpan.Zero;
        var hour = s.Marks.Where(m => m > now - TimeSpan.FromHours(1)).OrderBy(m => m).ToList();
        if (hour.Count >= PerHour)
            wait = hour[hour.Count - PerHour] + TimeSpan.FromHours(1) - now;
        if (s.Marks.Count >= PerDay)
        {
            var day = s.Marks.OrderBy(m => m).ToList();
            var free = day[day.Count - PerDay] + TimeSpan.FromDays(1) - now;
            if (free > wait)
                wait = free;
        }
        return wait;
    }

    public TimeSpan SpacingWait(string provider, DateTimeOffset now)
    {
        if (!state.TryGetValue(provider, out var s) || s.Marks.Count == 0)
            return TimeSpan.Zero;
        var left = Spacing - (now - s.Marks[^1]);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    public void Marked(string provider, DateTimeOffset now) => Of(provider).Marks.Add(now);
}

public static class CommunityQueue
{
    public static string SeasonKey(VideoItem item) => item.GroupKey + ":season:" + item.Season;

    public static bool Enabled(PlayerState state, string provider) =>
        provider switch
        {
            "Trakt" => state.SyncTrakt,
            "Serializd" => state.SyncSerializd,
            "Nuvio" => state.SyncNuvio && state.NuvioConnected,
            Stremio => StremioMarkOn(state),
            _ => false,
        };

    /// <summary>Every provider that has a queue. A provider added later is listed here and nowhere else.</summary>
    public static readonly string[] Providers = ["Trakt", "Serializd", "Nuvio", Stremio];

    /// <summary>The user's Stremio account (the same one Harbor reads), written through StremioLibraryWriter.</summary>
    public const string Stremio = "Stremio";

    // The providers whose confirmed marks travel between the phone and the PC (P4 "synced").
    static readonly string[] Exchanged = ["Trakt", "Serializd", Stremio];

    public static bool StremioMarkOn(PlayerState state) =>
        state.SyncPace.TryGetValue(Stremio, out var s) && s.MarkWatched;

    public static bool StremioAddTitles(PlayerState state) =>
        state.SyncPace.TryGetValue(Stremio, out var s) && s.AddTitles;

    /// <summary>Changes either Stremio switch (null keeps it). Turning marking on queues nothing from the past (S3).</summary>
    public static void SetStremio(PlayerState state, bool? markWatched, bool? addTitles)
    {
        if (!state.SyncPace.TryGetValue(Stremio, out var s))
            state.SyncPace[Stremio] = s = new();
        if (markWatched is { } mark)
            s.MarkWatched = mark;
        if (addTitles is { } add)
            s.AddTitles = add;
    }

    /// <summary>Queues one watch for one provider only (Nuvio's own "Send earlier history" uses this).</summary>
    public static bool Enqueue(PlayerState state, VideoItem item, DateTimeOffset watchedAt, string provider) =>
        Add(state, provider, item, watchedAt);

    // Serializd is TV only; everything else takes both.
    static bool Accepts(string provider, VideoItem item) => !(provider == "Serializd" && item.IsMovie);

    static bool Add(PlayerState state, string provider, VideoItem item, DateTimeOffset watchedAt)
    {
        if (item.Id.Length == 0 || !Enabled(state, provider) || !Accepts(provider, item))
            return false;
        var key = provider + ":" + item.Id;
        if (state.WatchSync.ContainsKey(key))
            return false;
        state.WatchSync[key] = new()
        {
            Provider = provider,
            ItemId = item.Id,
            WatchedAt = watchedAt,
        };
        return true;
    }

    public static void Enqueue(PlayerState state, VideoItem item, DateTimeOffset watchedAt)
    {
        foreach (var provider in Providers)
            Add(state, provider, item, watchedAt);
    }

    public static void CancelPending(PlayerState state, string itemId)
    {
        foreach (
            var key in state
                .WatchSync.Where(p => p.Value.ItemId == itemId && p.Value.SyncedAt == null)
                .Select(p => p.Key)
                .ToList()
        )
            state.WatchSync.Remove(key);
    }

    /// <summary>The phone already told the site about this viewing: mark the PC's job done without loading a page.</summary>
    public static bool MarkSyncedByPhone(
        PlayerState state,
        string itemId,
        string provider,
        DateTimeOffset syncedAt,
        DateTimeOffset watchedAt
    )
    {
        if (
            !state.WatchSync.TryGetValue(provider + ":" + itemId, out var job)
            || job.SyncedAt != null
            || Math.Abs((job.WatchedAt - watchedAt).TotalHours) >= 6
        )
            return false;
        job.SyncedAt = syncedAt;
        job.Message = "Watched ✓";
        job.BlockedKey = "";
        return true;
    }

    /// <summary>The user pressed Retry: every provider and every parked title gets a fresh try.</summary>
    public static void Retry(PlayerState state, CommunityBackoff backoff)
    {
        backoff.Reset();
        foreach (var job in state.WatchSync.Values.Where(j => j.SyncedAt == null))
        {
            job.RetryAt = DateTimeOffset.MinValue;
            job.BlockedKey = "";
        }
    }

    /// <summary>A viewing that arrived from the phone with synced[provider]: mark the matching PC jobs done.</summary>
    public static void ApplyPhoneSynced(
        PlayerState state,
        string itemId,
        Dictionary<string, string>? synced,
        DateTimeOffset watchedAt
    )
    {
        if (synced == null)
            return;
        foreach (var (provider, text) in synced)
            if (
                Exchanged.Contains(provider)
                && DateTimeOffset.TryParse(
                    text,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal
                        | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var at
                )
            )
                MarkSyncedByPhone(state, itemId, provider, at, watchedAt);
    }

    /// <summary>The PC's confirmed sync times for one viewing, sent to the phone as synced (null when none).</summary>
    public static Dictionary<string, string>? SyncedTimes(
        PlayerState state,
        string itemId,
        ViewingRecord viewing,
        IReadOnlyList<ViewingRecord> all
    )
    {
        Dictionary<string, string>? result = null;
        foreach (var provider in Exchanged)
            if (
                state.WatchSync.GetValueOrDefault(provider + ":" + itemId) is { SyncedAt: { } at } job
                && all.OrderBy(v => Math.Abs((v.WatchedAt - job.WatchedAt).TotalSeconds)).First().Id == viewing.Id
            )
                (result ??= [])[provider] = at.UtcDateTime.ToString(
                    "o",
                    System.Globalization.CultureInfo.InvariantCulture
                );
        return result;
    }

    /// <summary>
    /// One pass over the pending jobs. <paramref name="send"/> returns true when the site confirmed the mark,
    /// false to skip the job (no longer current), throws <see cref="SyncTitleException"/> for a per-title failure
    /// and anything else for a site failure, which ends that provider's pass and backs the provider off.
    /// </summary>
    public static async Task Pass(
        PlayerState state,
        CommunityBackoff backoff,
        Func<DateTimeOffset> clock,
        Func<WatchSyncJob, string> titleKey,
        Func<WatchSyncJob, bool> current,
        Func<WatchSyncJob, Task<bool>> send,
        Action save,
        Func<TimeSpan, Task>? delay = null,
        Func<Task>? persisted = null,
        Action<Exception>? log = null
    )
    {
        delay ??= d => Task.Delay(d);
        var stopped = new HashSet<string>();
        var pending = state
            .WatchSync.Values.Where(j => j.Provider != "Nuvio" && j.SyncedAt == null && Enabled(state, j.Provider))
            .OrderByDescending(j => j.WatchedAt)
            .ToList();
        foreach (var job in pending)
        {
            var now = clock();
            if (stopped.Contains(job.Provider) || !backoff.Ready(job.Provider, now) || job.RetryAt > now)
                continue;
            if (job.Backlog && backoff.BacklogPaused(job.Provider))
                continue;
            if (!current(job))
                continue;
            var key = titleKey(job);
            if (job.BlockedKey.Length > 0)
            {
                if (job.BlockedKey == key)
                    continue;
                job.BlockedKey = "";
            }
            if (backoff.CapWait(job.Provider, now) > TimeSpan.Zero)
            {
                job.Message = "Waiting its turn: the site is sent a limited number of marks per hour and day.";
                stopped.Add(job.Provider);
                continue;
            }
            if (backoff.SpacingWait(job.Provider, now) is { } gap && gap > TimeSpan.Zero)
                await delay(gap);
            var (attempts, message) = (job.Attempts, job.Message);
            job.Attempts++;
            job.Message = "Syncing…";
            // S6: one save per job, after its outcome. Before the next send the previous job's write must
            // have landed, so a crash right after a confirmed send resends at most that one job.
            if (persisted != null)
            {
                try
                {
                    await persisted();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Nothing was sent: the job goes back as it was, and the site waits before the next try.
                    (job.Attempts, job.Message) = (attempts, message);
                    try
                    {
                        log?.Invoke(e);
                    }
                    catch (Exception e2) when (e2 is IOException or UnauthorizedAccessException) { }
                    backoff.SiteFailed(job.Provider, clock());
                    stopped.Add(job.Provider);
                    continue;
                }
            }
            try
            {
                // Not sent because the job is no longer current: leave it as it was.
                if (!await send(job))
                {
                    (job.Attempts, job.Message) = (attempts, message);
                    continue;
                }
                backoff.Marked(job.Provider, clock());
                job.SyncedAt = clock();
                job.Message = "Watched ✓";
                backoff.Succeeded(job.Provider);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SyncTitleException e)
            {
                job.Message = e.Message;
                job.BlockedKey = key;
                backoff.Marked(job.Provider, clock());
            }
            catch (Exception e)
            {
                job.Message = e is IOException ? e.Message : "Website unavailable. Noir will retry later.";
                job.RetryAt = backoff.SiteFailed(job.Provider, clock());
                stopped.Add(job.Provider);
            }
            save();
        }
    }

    /// <summary>
    /// Queues every past completed title as backlog (not already queued), for one provider or all. Returns the
    /// unsent backlog size for that selection. Turning a site on never calls this: only the user's explicit
    /// "Also send my past history" does.
    /// </summary>
    public static int QueueBacklog(PlayerState state, string? provider = null)
    {
        foreach (var entry in state.Catalog.Values.Where(e => e.Progress?.Completed == true))
        foreach (var p in Providers.Where(p => provider == null || p == provider))
            if (Add(state, p, entry.Item, entry.Progress!.UpdatedAt))
                state.WatchSync[p + ":" + entry.Item.Id].Backlog = true;
        return BacklogWaiting(state, provider);
    }

    public static int BacklogWaiting(PlayerState state, string? provider = null) =>
        state.WatchSync.Values.Count(j =>
            j.Backlog && j.SyncedAt == null && (provider == null || j.Provider == provider)
        );

    /// <summary>What "Also send my past history" would queue for one provider, and how long that takes at the pace.</summary>
    public sealed record BacklogPlan(string Provider, int Titles, double Days);

    public static BacklogPlan PlanBacklog(PlayerState state, string provider) =>
        Enabled(state, provider) || provider == "Nuvio" ? PlanBacklogCore(state, provider) : new(provider, 0, 0);

    static BacklogPlan PlanBacklogCore(PlayerState state, string provider)
    {
        var titles = state
            .Catalog.Values.Where(e =>
                e.Progress?.Completed == true
                && e.Item.Id.Length > 0
                && Accepts(provider, e.Item)
                && !state.WatchSync.ContainsKey(provider + ":" + e.Item.Id)
            )
            .Count();
        return new(provider, titles, (double)titles / CommunityBackoff.PerDay);
    }

    /// <summary>"1,240 titles · about 6 days in the background".</summary>
    public static string BacklogText(BacklogPlan plan, bool arabic, System.Globalization.CultureInfo culture)
    {
        var days = Math.Max(1, (int)Math.Round(plan.Days));
        var title = arabic
            ? plan.Titles == 1
                ? "عنوان واحد"
                : plan.Titles.ToString("N0", culture) + " عنوان"
            : plan.Titles.ToString("N0", culture) + (plan.Titles == 1 ? " title" : " titles");
        var time =
            plan.Days < 1
                ? arabic
                    ? "أقل من يوم في الخلفية"
                    : "less than a day in the background"
                : arabic
                    ? (days == 1 ? "نحو يوم واحد في الخلفية" : $"نحو {days} أيام في الخلفية")
                    : $"about {days} {(days == 1 ? "day" : "days")} in the background";
        return title + " · " + time;
    }

    /// <summary>Cancel the past-history send: removes unsent backlog jobs only. Returns how many.</summary>
    public static int CancelBacklog(PlayerState state, CommunityBackoff? backoff = null, string? provider = null)
    {
        var keys = state
            .WatchSync.Where(p =>
                p.Value.Backlog && p.Value.SyncedAt == null && (provider == null || p.Value.Provider == provider)
            )
            .Select(p => p.Key)
            .ToList();
        foreach (var key in keys)
            state.WatchSync.Remove(key);
        if (backoff != null)
            foreach (var name in Providers.Where(p => provider == null || p == provider))
                backoff.SetBacklogPaused(name, false);
        return keys.Count;
    }

    /// <summary>One line's worth of numbers per site (S4); the UI shows the newest 50 jobs plus "and N more".</summary>
    public sealed record SiteStatus(
        string Provider,
        int Synced,
        int Waiting,
        int MarksLastHour,
        int MarksLastDay,
        DateTimeOffset? LastSynced,
        DateTimeOffset? EstimatedFinish,
        int Unsent = 0,
        int Backlog = 0,
        bool BacklogPaused = false
    );

    public static SiteStatus Status(PlayerState state, CommunityBackoff backoff, string provider, DateTimeOffset now)
    {
        var jobs = state.WatchSync.Values.Where(j => j.Provider == provider).ToList();
        var waiting = jobs.Count(j => j.SyncedAt == null && j.BlockedKey.Length == 0);
        var backlog = jobs.Count(j => j.Backlog && j.SyncedAt == null && j.BlockedKey.Length == 0);
        var paused = backoff.BacklogPaused(provider);
        var active = paused ? waiting - backlog : waiting;
        return new(
            provider,
            jobs.Count(j => j.SyncedAt != null),
            waiting,
            backoff.MarksIn(provider, now, TimeSpan.FromHours(1)),
            backoff.MarksIn(provider, now, TimeSpan.FromDays(1)),
            jobs.Where(j => j.SyncedAt != null).Max(j => j.SyncedAt),
            active <= 0 ? null : now + TimeSpan.FromDays((double)active / CommunityBackoff.PerDay),
            jobs.Count(j => j.SyncedAt == null),
            backlog,
            paused
        );
    }

    static string Date(DateTimeOffset at, System.Globalization.CultureInfo culture) =>
        at.ToLocalTime().ToString("d MMM", culture);

    /// <summary>S4/F13: "6 synced, last 5 Oct · 137 waiting · up to 200 a day · done about 18 Nov".</summary>
    public static string StatusText(SiteStatus s, bool arabic, System.Globalization.CultureInfo culture)
    {
        var parts = new List<string>();
        var synced = s.Synced.ToString("N0", culture);
        parts.Add(
            arabic
                ? $"{synced} تمت مزامنتها" + (s.LastSynced is { } l ? $"، آخرها {Date(l, culture)}" : "")
                : $"{synced} synced" + (s.LastSynced is { } last ? $", last {Date(last, culture)}" : "")
        );
        var waiting = s.Unsent.ToString("N0", culture);
        parts.Add(arabic ? $"{waiting} في الانتظار" : $"{waiting} waiting");
        if (s.Unsent > 0)
        {
            parts.Add(arabic ? $"حتى {CommunityBackoff.PerDay} يوميًا" : $"up to {CommunityBackoff.PerDay} a day");
            if (s.EstimatedFinish is { } done)
                parts.Add(arabic ? $"ينتهي نحو {Date(done, culture)}" : $"done about {Date(done, culture)}");
        }
        if (s.BacklogPaused && s.Backlog > 0)
            parts.Add(arabic ? "السجل السابق متوقف مؤقتًا" : "past history paused");
        return string.Join(" · ", parts);
    }

    /// <summary>F13: a provider that has failed for a day, shown once (one line per provider).</summary>
    public sealed record SiteNotice(
        string Provider,
        DateTimeOffset FailingSince,
        DateTimeOffset? LastSynced,
        int Waiting
    );

    public static readonly TimeSpan NoticeAfter = TimeSpan.FromDays(1);

    public static List<SiteNotice> Notices(PlayerState state, CommunityBackoff backoff, DateTimeOffset now) =>
        backoff
            .Providers()
            .Where(p => Enabled(state, p) && backoff.FailingSince(p) is { } since && now - since >= NoticeAfter)
            .Select(p =>
            {
                var jobs = state.WatchSync.Values.Where(j => j.Provider == p).ToList();
                return new SiteNotice(
                    p,
                    backoff.FailingSince(p)!.Value,
                    jobs.Max(j => j.SyncedAt),
                    jobs.Count(j => j.SyncedAt == null)
                );
            })
            .OrderBy(n => n.Provider)
            .ToList();

    /// <summary>
    /// "Serializd has not synced since 5 Oct · 137 waiting" (the Sign in button is the UI's). The History banner keeps
    /// the count; the card has its own counts line, so it asks for <paramref name="withWaiting"/> false (U85).
    /// </summary>
    public static string NoticeText(
        SiteNotice n,
        bool arabic,
        System.Globalization.CultureInfo culture,
        bool withWaiting = true
    )
    {
        var waiting = n.Waiting.ToString("N0", culture);
        var tail = withWaiting ? (arabic ? $" · {waiting} في الانتظار" : $" · {waiting} waiting") : "";
        return n.LastSynced is { } at
            ? arabic
                ? $"{n.Provider} لم تتم مزامنته منذ {Date(at, culture)}{tail}"
                : $"{n.Provider} has not synced since {Date(at, culture)}{tail}"
            : arabic
                ? $"{n.Provider} لم تتم مزامنته بعد{tail}"
                : $"{n.Provider} has not synced yet{tail}";
    }

    /// <summary>U84/U85: a card's one state line and the colour of its dot ("green", "amber" or "grey").</summary>
    public sealed record SiteHeader(string Text, string Dot);

    /// <summary>
    /// The header says the state only (counts live in <see cref="StatusText"/>). Green means the last attempt worked;
    /// a stopped website is amber with "Not syncing since ..." and never "Sending".
    /// </summary>
    public static SiteHeader Header(
        PlayerState state,
        CommunityBackoff backoff,
        string provider,
        DateTimeOffset now,
        bool arabic,
        System.Globalization.CultureInfo culture
    )
    {
        if (!Enabled(state, provider))
            return new(arabic ? "متوقف" : "Off", "grey");
        if (Notices(state, backoff, now).FirstOrDefault(n => n.Provider == provider) is { } n)
        {
            var since = Date(n.LastSynced ?? n.FailingSince, culture);
            return new(arabic ? $"لا تتم المزامنة منذ {since}" : $"Not syncing since {since}", "amber");
        }
        if (backoff.FailingSince(provider) != null)
            return new(arabic ? "تُعاد المحاولة" : "Retrying", "amber");
        var s = Status(state, backoff, provider, now);
        if (s.BacklogPaused && s.Backlog > 0 && s.Waiting - s.Backlog <= 0)
            return new(arabic ? "متوقف مؤقتًا" : "Paused", "green");
        return new(arabic ? "يرسل" : "Sending", "green");
    }

    /// <summary>The newest unsent jobs for the UI list (default 50); the rest is "and N more".</summary>
    public static List<WatchSyncJob> Newest(PlayerState state, string provider, int count = 50) =>
        state
            .WatchSync.Values.Where(j => j.Provider == provider && j.SyncedAt == null)
            .OrderByDescending(j => j.WatchedAt)
            .Take(count)
            .ToList();

    public static int UnsentCount(PlayerState state, string provider) =>
        state.WatchSync.Values.Count(j => j.Provider == provider && j.SyncedAt == null);

    // The parked per-title reasons the UI offers fixes for (the text is stored on the job).
    public const string NoIdMessage = "Waiting for a verified title ID. Refresh information in Settings.";
    public const string SpecialMessage =
        "Specials need a confirmed Trakt match. Mark the correct special on its website; your Noir history remains saved.";

    public static bool NeedsId(WatchSyncJob job) =>
        job.SyncedAt == null && job.Message.StartsWith("Waiting for a verified title ID");

    /// <summary>F14: the list offers "Match this title" for a title with no verified IMDb ID.</summary>
    public static bool NeedsMatch(VideoItem item) =>
        !System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId, @"^tt\d+$");

    public static bool IsSpecial(WatchSyncJob job) => job.SyncedAt == null && job.Message.StartsWith("Specials need");

    /// <summary>F14: the specials that need a manual mark, newest first.</summary>
    public static List<WatchSyncJob> Specials(PlayerState state, string provider) =>
        state
            .WatchSync.Values.Where(j => j.Provider == provider && IsSpecial(j))
            .OrderByDescending(j => j.WatchedAt)
            .ToList();

    /// <summary>
    /// F14: once a day, while titles wait for a verified ID, ask <paramref name="lookup"/> to look them up again
    /// (Cinemeta). The first call only starts the day. A found ID changes the title's key, which unparks the job.
    /// </summary>
    public static async Task<bool> RetryIds(
        PlayerState state,
        CommunityBackoff backoff,
        DateTimeOffset now,
        Func<Task> lookup
    )
    {
        if (!state.WatchSync.Values.Any(NeedsId))
            return false;
        if (backoff.IdLookupAt == DateTimeOffset.MinValue)
        {
            backoff.IdLookupAt = now;
            return false;
        }
        if (now - backoff.IdLookupAt < TimeSpan.FromDays(1))
            return false;
        backoff.IdLookupAt = now;
        await lookup();
        return true;
    }

    public static string SerializdEpisode(PlayerState state, VideoItem item)
    {
        if (item.IsMovie || item.Episode < 1)
            return "";
        var season = state.SerializdSeasons.GetValueOrDefault(SeasonKey(item), "");
        return System.Text.RegularExpressions.Regex.IsMatch(
            season,
            @"^https://www\.serializd\.com/show/[^/?#]+/season/\d+/" + item.Season + "$"
        )
            ? season + "/episode/" + item.Episode
            : "";
    }
}
