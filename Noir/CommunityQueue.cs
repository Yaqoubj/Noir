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

    SyncPaceState Of(string provider) => state.TryGetValue(provider, out var s) ? s : state[provider] = new();

    public int Failures(string provider) => state.TryGetValue(provider, out var s) ? s.Failures : 0;

    public DateTimeOffset Until(string provider) =>
        state.TryGetValue(provider, out var s) ? s.Until : DateTimeOffset.MinValue;

    public bool Ready(string provider, DateTimeOffset now) => now >= Until(provider);

    public DateTimeOffset SiteFailed(string provider, DateTimeOffset now)
    {
        var s = Of(provider);
        s.Failures++;
        return s.Until = now + Steps[Math.Min(s.Failures, Steps.Length) - 1];
    }

    public void Succeeded(string provider)
    {
        if (state.TryGetValue(provider, out var s))
        {
            s.Failures = 0;
            s.Until = DateTimeOffset.MinValue;
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
            _ => false,
        };

    public static void Enqueue(PlayerState state, VideoItem item, DateTimeOffset watchedAt)
    {
        if (item.Id.Length == 0)
            return;
        foreach (var provider in new[] { "Trakt", "Serializd", "Nuvio" })
        {
            if (!Enabled(state, provider) || (provider == "Serializd" && item.IsMovie))
                continue;
            var key = provider + ":" + item.Id;
            if (!state.WatchSync.ContainsKey(key))
                state.WatchSync[key] = new()
                {
                    Provider = provider,
                    ItemId = item.Id,
                    WatchedAt = watchedAt,
                };
        }
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
                provider is "Trakt" or "Serializd"
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
        foreach (var provider in new[] { "Trakt", "Serializd" })
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
        Func<TimeSpan, Task>? delay = null
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
            save();
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

    /// <summary>Queues every past completed title as backlog (not already queued). Returns the unsent backlog size.</summary>
    public static int QueueBacklog(PlayerState state)
    {
        foreach (var entry in state.Catalog.Values.Where(e => e.Progress?.Completed == true))
        {
            var before = state.WatchSync.Count;
            Enqueue(state, entry.Item, entry.Progress!.UpdatedAt);
            if (state.WatchSync.Count > before)
                foreach (var provider in new[] { "Trakt", "Serializd", "Nuvio" })
                    if (
                        state.WatchSync.GetValueOrDefault(provider + ":" + entry.Item.Id) is
                        { SyncedAt: null, Attempts: 0 } job
                    )
                        job.Backlog = true;
        }
        return state.WatchSync.Values.Count(j => j.Backlog && j.SyncedAt == null);
    }

    /// <summary>Cancel the past-history send: removes unsent backlog jobs only. Returns how many.</summary>
    public static int CancelBacklog(PlayerState state)
    {
        var keys = state.WatchSync.Where(p => p.Value.Backlog && p.Value.SyncedAt == null).Select(p => p.Key).ToList();
        foreach (var key in keys)
            state.WatchSync.Remove(key);
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
        DateTimeOffset? EstimatedFinish
    );

    public static SiteStatus Status(PlayerState state, CommunityBackoff backoff, string provider, DateTimeOffset now)
    {
        var jobs = state.WatchSync.Values.Where(j => j.Provider == provider).ToList();
        var waiting = jobs.Count(j => j.SyncedAt == null && j.BlockedKey.Length == 0);
        return new(
            provider,
            jobs.Count(j => j.SyncedAt != null),
            waiting,
            backoff.MarksIn(provider, now, TimeSpan.FromHours(1)),
            backoff.MarksIn(provider, now, TimeSpan.FromDays(1)),
            jobs.Where(j => j.SyncedAt != null).Max(j => j.SyncedAt),
            waiting == 0 ? null : now + TimeSpan.FromDays((double)waiting / CommunityBackoff.PerDay)
        );
    }

    public static List<WatchSyncJob> Newest(PlayerState state, string provider, int count = 50) =>
        state
            .WatchSync.Values.Where(j => j.Provider == provider && j.SyncedAt == null)
            .OrderByDescending(j => j.WatchedAt)
            .Take(count)
            .ToList();

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
