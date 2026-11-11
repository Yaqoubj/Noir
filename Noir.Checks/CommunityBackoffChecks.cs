using System.Text;
using System.Text.Json;
using Noir;

static class CommunityBackoffChecks
{
    static readonly DateTimeOffset t0 = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    static PlayerState State(params (string Id, bool Movie)[] items)
    {
        var state = new PlayerState { SyncTrakt = true, SyncSerializd = true };
        foreach (var (id, movie) in items)
            CommunityQueue.Enqueue(state, new VideoItem { Id = id, IsMovie = movie }, t0);
        return state;
    }

    static void Pass(
        PlayerState state,
        CommunityBackoff backoff,
        DateTimeOffset now,
        Func<WatchSyncJob, Task<bool>> send,
        Func<WatchSyncJob, string>? key = null
    ) =>
        CommunityQueue
            .Pass(
                state,
                backoff,
                () => now,
                key ?? (j => j.ItemId),
                _ => true,
                send,
                () => { },
                _ => Task.CompletedTask
            )
            .GetAwaiter()
            .GetResult();

    public static void Run(string root, Action<bool, string> check)
    {
        // Site failure: provider-level backoff steps, one page load per pass, reset on success.
        var state = State(("m1", true), ("m2", true), ("m3", true));
        var backoff = new CommunityBackoff();
        var calls = 0;
        Task<bool> Down(WatchSyncJob _)
        {
            calls++;
            throw new SyncSiteException("The website could not load.");
        }
        var now = t0;
        var stepsOk = true;
        var stoppedOk = true;
        foreach (var minutes in new[] { 5.0, 30, 120, 720, 1440, 1440 })
        {
            calls = 0;
            Pass(state, backoff, now, Down);
            stoppedOk &= calls == 1;
            stepsOk &= backoff.Until("Trakt") == now + TimeSpan.FromMinutes(minutes);
            calls = 0;
            Pass(state, backoff, now + TimeSpan.FromMinutes(minutes - 1), Down);
            stepsOk &= calls == 0;
            now += TimeSpan.FromMinutes(minutes);
        }
        check(
            stoppedOk,
            "A site failure stops the provider's pass after the first job (1 page load, not one per pending title)"
        );
        check(stepsOk, "Provider backoff waits 5 min, 30 min, 2 h, 12 h, then 24 h and stays at 24 h");
        calls = 0;
        Pass(state, backoff, now, j =>
        {
            calls++;
            return Task.FromResult(true);
        });
        check(
            calls == 3 && backoff.Failures("Trakt") == 0 && state.WatchSync.Values.All(j => j.SyncedAt != null),
            "A success resets the provider's backoff and the pass continues through the rest"
        );

        var retryState = State(("m1", true), ("m2", true));
        var retryBackoff = new CommunityBackoff();
        Pass(retryState, retryBackoff, t0, Down);
        var backedOff = retryBackoff.Failures("Trakt") == 1;
        calls = 0;
        Pass(retryState, retryBackoff, t0.AddSeconds(1), Down);
        var waited = calls == 0;
        CommunityQueue.Retry(retryState, retryBackoff);
        calls = 0;
        Pass(retryState, retryBackoff, t0.AddSeconds(2), Down);
        check(backedOff && waited && calls == 1, "Retry resets the provider backoff so the very next pass tries again");

        // A failing Trakt does not stop Serializd.
        var both = State(("ep1", false));
        var seen = new List<string>();
        Pass(both, new(), t0, j =>
        {
            seen.Add(j.Provider);
            if (j.Provider == "Trakt")
                throw new SyncSiteException("down");
            return Task.FromResult(true);
        });
        check(
            seen.Count == 2 && both.WatchSync["Serializd:ep1"].SyncedAt != null,
            "One provider backing off does not stop the other provider's jobs"
        );

        // Per-title failure: parked for the title, no provider backoff, next job still runs.
        var title = State(("bad", true), ("good", true));
        title.WatchSync["Trakt:bad"].WatchedAt = t0.AddMinutes(-1);
        var titleBackoff = new CommunityBackoff();
        var order = new List<string>();
        Task<bool> TitleSend(WatchSyncJob j)
        {
            order.Add(j.ItemId);
            if (j.ItemId == "bad")
                throw new SyncTitleException("Waiting for a verified title ID.");
            return Task.FromResult(true);
        }
        Pass(title, titleBackoff, t0, TitleSend);
        check(
            order.SequenceEqual(["good", "bad"])
                && title.WatchSync["Trakt:good"].SyncedAt != null
                && titleBackoff.Failures("Trakt") == 0
                && titleBackoff.Ready("Trakt", t0),
            "A per-title failure does not block the next job and does not back off the provider"
        );
        order.Clear();
        Pass(title, titleBackoff, t0.AddDays(3), TitleSend);
        check(order.Count == 0, "A parked title is not retried on later passes however much time passes");
        Pass(title, titleBackoff, t0.AddDays(3), TitleSend, j => j.ItemId + "|fixed");
        check(order.SequenceEqual(["bad"]), "A parked title is tried again once its identity changes");
        order.Clear();
        CommunityQueue.Retry(title, titleBackoff);
        Pass(title, titleBackoff, t0.AddDays(3), TitleSend, j => j.ItemId + "|fixed");
        check(order.SequenceEqual(["bad"]), "Retry tries a parked title again");

        // Phone already told the site: the PC job is done without a page load; the PC's own times go back.
        var s = new LibraryStore(Path.Combine(root, "pcsync-" + Guid.NewGuid().ToString("N")));
        s.State.Roots = [];
        s.State.SyncTrakt = true;
        s.State.SyncSerializd = true;
        var ep = new VideoItem
        {
            Title = "Synced Show",
            Path = "missing.mkv",
            Season = 1,
            Episode = 1,
            Duration = 1800,
        };
        s.Record(ep, 10, 1800);
        ep = s.Archive.Single();
        var viewing = Guid.NewGuid().ToString();
        var syncedAt = DateTimeOffset.UtcNow.AddSeconds(-30);
        s.MergeMobile([
            new MobileOperation
            {
                Id = Guid.NewGuid().ToString(),
                ItemId = ep.Id,
                Kind = "mark",
                ViewingId = viewing,
                At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Position = 1700,
                Duration = 1800,
                Completed = true,
                Synced = new() { ["Trakt"] = syncedAt.ToString("o") },
            },
        ]);
        var trakt = s.State.WatchSync.GetValueOrDefault("Trakt:" + ep.Id);
        var serializd = s.State.WatchSync.GetValueOrDefault("Serializd:" + ep.Id);
        check(
            trakt?.SyncedAt is { } got
                && Math.Abs((got - syncedAt).TotalSeconds) < 1
                && serializd != null
                && serializd.SyncedAt == null,
            "A phone viewing carrying synced.Trakt marks the PC's Trakt job done and leaves Serializd pending"
        );
        var providers = new List<string>();
        Pass(s.State, new(), DateTimeOffset.UtcNow, j =>
        {
            providers.Add(j.Provider);
            return Task.FromResult(true);
        });
        check(
            providers.SequenceEqual(["Serializd"]),
            "The PC loads no page for a viewing the phone already synced (only the other provider runs)"
        );
        s.MergeMobile([
            new MobileOperation
            {
                Id = Guid.NewGuid().ToString(),
                ItemId = ep.Id,
                Kind = "synced",
                ViewingId = viewing,
                At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Synced = new() { ["Serializd"] = syncedAt.ToString("o") },
            },
        ]);
        var later = State(("ep2", false));
        later.WatchSync["Serializd:ep2"].WatchedAt = syncedAt;
        CommunityQueue.ApplyPhoneSynced(later, "ep2", new() { ["Serializd"] = syncedAt.ToString("o") }, syncedAt);
        check(
            later.WatchSync["Serializd:ep2"].SyncedAt != null && later.WatchSync["Trakt:ep2"].SyncedAt == null,
            "A synced time marks only the named provider's PC job"
        );
        var sent = s.MobileSnapshot().Items.Single(x => x.Id == ep.Id).Viewings.Single(v => v.Id == viewing);
        var wire = JsonSerializer.Serialize(
            sent,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );
        check(
            sent.Synced != null
                && sent.Synced.ContainsKey("Trakt")
                && sent.Synced.ContainsKey("Serializd")
                && wire.Contains("\"synced\":{"),
            "Viewings sent to the phone include the PC's synced times"
        );
        var fresh = new LibraryStore(Path.Combine(root, "pcsync-" + Guid.NewGuid().ToString("N")));
        check(
            fresh.MobileSnapshot().Items.All(x => x.Viewings.All(v => v.Synced == null)),
            "A viewing nobody synced carries no synced times"
        );

        Big(check);
        Golden(check);
    }

    static void Big(Action<bool, string> check)
    {
        // 5,000 queued jobs on a fake clock that only moves when the code waits or the test steps it.
        var state = new PlayerState { SyncTrakt = true };
        for (int n = 0; n < 5000; n++)
        {
            CommunityQueue.Enqueue(state, new VideoItem { Id = "t" + n, IsMovie = true }, t0.AddMinutes(-n));
            state.WatchSync["Trakt:t" + n].WatchedAt = t0.AddMinutes(-n);
        }
        var now = t0;
        var backoff = new CommunityBackoff(state.SyncPace);
        var sent = new List<(string Id, DateTimeOffset At)>();
        Task<bool> Ok(WatchSyncJob j)
        {
            sent.Add((j.ItemId, now));
            return Task.FromResult(true);
        }
        Task Wait(TimeSpan d)
        {
            now += d;
            return Task.CompletedTask;
        }
        Task Step() => CommunityQueue.Pass(state, backoff, () => now, j => j.ItemId, _ => true, Ok, () => { }, Wait);
        Step().GetAwaiter().GetResult();
        check(
            sent.Count == 30 && sent[0].Id == "t0" && sent[1].Id == "t1" && sent[29].Id == "t29",
            "Newest first: the title watched most recently is sent first, then back through time"
        );
        check(
            sent.Zip(sent.Skip(1)).All(p => p.Second.At - p.First.At >= CommunityBackoff.Spacing),
            "Marks to one site are spaced a few seconds apart"
        );
        var hourOk = true;
        var dayOk = true;
        for (int minute = 0; minute < 3 * 24 * 60; minute += 5)
        {
            now = t0 + TimeSpan.FromMinutes(minute);
            Step().GetAwaiter().GetResult();
            if (minute % 60 == 0)
            {
                foreach (var (_, at) in sent)
                {
                    hourOk &=
                        sent.Count(x => x.At >= at && x.At < at + TimeSpan.FromHours(1)) <= CommunityBackoff.PerHour;
                    dayOk &= sent.Count(x => x.At >= at && x.At < at + TimeSpan.FromDays(1)) <= CommunityBackoff.PerDay;
                }
            }
        }
        check(hourOk, "No more than 30 marks per provider in any hour on 5,000 queued jobs");
        check(
            dayOk && sent.Count >= 3 * 190,
            "No more than 200 marks per provider in any day, and the cap is actually used"
        );
        check(
            state.WatchSync.Count == 5000 && state.WatchSync.Values.Count(j => j.SyncedAt != null) == sent.Count,
            "Nothing is dropped: capped jobs stay queued"
        );
        check(
            sent.Select(x => int.Parse(x.Id[1..]))
                .SequenceEqual(sent.Select(x => int.Parse(x.Id[1..])).OrderBy(i => i)),
            "Capped passes keep sending newest first"
        );

        // Restart keeps counters: reload the state through JSON.
        var copy = JsonSerializer.Deserialize<PlayerState>(JsonSerializer.Serialize(state))!;
        var reopened = new CommunityBackoff(copy.SyncPace);
        var at0 = now;
        check(
            reopened.MarksIn("Trakt", at0, TimeSpan.FromDays(1)) == backoff.MarksIn("Trakt", at0, TimeSpan.FromDays(1))
                && reopened.CapWait("Trakt", at0) == backoff.CapWait("Trakt", at0),
            "A restart keeps the pace counters"
        );
        var old = JsonSerializer.Deserialize<PlayerState>("{\"SyncTrakt\":true}")!;
        check(old.SyncPace.Count == 0, "Old library state without pace counters loads");

        // A site failure still stops the pass, and the backoff survives a restart.
        var down = new PlayerState { SyncTrakt = true };
        for (int n = 0; n < 50; n++)
            CommunityQueue.Enqueue(down, new VideoItem { Id = "d" + n, IsMovie = true }, t0.AddMinutes(-n));
        var downBackoff = new CommunityBackoff(down.SyncPace);
        var loads = 0;
        CommunityQueue
            .Pass(
                down,
                downBackoff,
                () => t0,
                j => j.ItemId,
                _ => true,
                j =>
                {
                    loads++;
                    throw new SyncSiteException("down");
                },
                () => { },
                _ => Task.CompletedTask
            )
            .GetAwaiter()
            .GetResult();
        var restarted = new CommunityBackoff(
            JsonSerializer.Deserialize<PlayerState>(JsonSerializer.Serialize(down))!.SyncPace
        );
        check(
            loads == 1
                && restarted.Failures("Trakt") == 1
                && !restarted.Ready("Trakt", t0.AddMinutes(4))
                && restarted.Ready("Trakt", t0.AddMinutes(5)),
            "A site failure still stops the pass, and the backoff survives a restart"
        );

        // Past history: backlog is its own set; cancelling removes unsent backlog jobs only.
        var past = new PlayerState { SyncTrakt = true };
        for (int n = 0; n < 5; n++)
            past.Catalog["c" + n] = new CatalogEntry
            {
                Item = new VideoItem { Id = "c" + n, IsMovie = true },
                Progress = new Bookmark { Completed = true, UpdatedAt = t0.AddDays(-n - 1) },
            };
        CommunityQueue.Enqueue(past, new VideoItem { Id = "fresh", IsMovie = true }, t0);
        var queued = CommunityQueue.QueueBacklog(past);
        past.WatchSync["Trakt:c0"].SyncedAt = t0;
        var status = CommunityQueue.Status(past, new CommunityBackoff(past.SyncPace), "Trakt", t0);
        var removed = CommunityQueue.CancelBacklog(past);
        check(
            queued == 5
                && status.Waiting == 5
                && status.Synced == 1
                && removed == 4
                && past.WatchSync.ContainsKey("Trakt:fresh")
                && past.WatchSync.ContainsKey("Trakt:c0")
                && past.WatchSync.Count == 2,
            "Cancelling past history removes unsent backlog jobs only; newer and already-sent ones stay"
        );
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Noir.Android")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string BuildGolden()
    {
        var items = new[]
        {
            ("movie", "tt1160419", true, 0, 0, "Dune", ""),
            ("episode", "tt0903747", false, 2, 3, "Breaking Bad", ""),
            ("quotes", "tt0472954", false, 10, 7, "It's \"Always\" Sunny in Philadelphia", "It's Always Sunny"),
            ("arabic", "tt1234567", true, 0, 0, "الرحلة الأخيرة", "الرحلة الأخيرة (2020)"),
        };
        var itemLines = new List<string>();
        var scriptLines = new List<string>();
        foreach (var (name, imdb, movie, season, episode, title, review) in items)
        {
            itemLines.Add(
                JsonSerializer.Serialize(
                    new
                    {
                        name,
                        imdb,
                        movie,
                        season,
                        episode,
                        title,
                        reviewTitle = review,
                    },
                    Compact
                )
            );
            var item = new VideoItem
            {
                ImdbId = imdb,
                IsMovie = movie,
                Season = season,
                Episode = episode,
                Title = title,
                ReviewTitle = review,
            };
            void Add(string kind, bool click, string script) =>
                scriptLines.Add(
                    JsonSerializer.Serialize(
                        new
                        {
                            item = name,
                            kind,
                            click,
                            script,
                        },
                        Compact
                    )
                );
            foreach (var click in new[] { false, true })
            {
                Add("trakt", click, CommunityScripts.TraktControl(item, click));
                if (!movie)
                    Add("serializd", click, CommunityScripts.SerializdControl(click));
            }
            Add("traktDate", false, CommunityScripts.TraktDateControl);
        }
        return "{\"items\":[\n"
            + string.Join(",\n", itemLines)
            + "\n],\"scripts\":[\n"
            + string.Join(",\n", scriptLines)
            + "\n]}\n";
    }

    static void Golden(Action<bool, string> check)
    {
        var path = Path.Combine(
            RepoRoot(),
            "Noir.Android",
            "app",
            "src",
            "androidTest",
            "assets",
            "watch-controls.json"
        );
        var built = new UTF8Encoding(false).GetBytes(BuildGolden());
        if (Environment.GetEnvironmentVariable("NOIR_WRITE_GOLDEN") == "1")
            File.WriteAllBytes(path, built);
        check(
            File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(built),
            "watch-controls.json is byte-equal to the scripts CommunityPages generates (NOIR_WRITE_GOLDEN=1 regenerates)"
        );
    }
}
