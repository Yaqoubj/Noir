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

        Surface(check);
        PersistFails(check);
        Big(check);
        LatinDigits(check);
        PopupDirection(check);
        Golden(check);
    }

    // A save that fails before the send: nothing is sent, the job is left as it was, the site backs off, it is logged.
    static void PersistFails(Action<bool, string> check)
    {
        var state = State(("p1", true));
        var job = state.WatchSync["Trakt:p1"];
        job.Attempts = 2;
        job.Message = "Website unavailable. Noir will retry later.";
        var backoff = new CommunityBackoff();
        var sends = 0;
        var logged = new List<Exception>();
        var unobserved = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> watch = (_, _) => unobserved++;
        TaskScheduler.UnobservedTaskException += watch;
        Exception? escaped = null;
        try
        {
            CommunityQueue
                .Pass(
                    state,
                    backoff,
                    () => t0,
                    j => j.ItemId,
                    _ => true,
                    _ =>
                    {
                        sends++;
                        return Task.FromResult(true);
                    },
                    () => { },
                    _ => Task.CompletedTask,
                    () => Task.FromException(new IOException("The disk is full.")),
                    logged.Add
                )
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception e)
        {
            escaped = e;
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        TaskScheduler.UnobservedTaskException -= watch;
        check(
            escaped == null
                && unobserved == 0
                && sends == 0
                && job.Attempts == 2
                && job.Message == "Website unavailable. Noir will retry later."
                && job.SyncedAt == null
                && backoff.Until("Trakt") > t0
                && logged.Count > 0
                && logged.All(e => e is IOException),
            "A failing save before a send leaves the job as it was, is logged and backs the site off, nothing escapes"
        );
    }

    // F13, F14, S3, S4: the notice, the daily ID lookup, past history as a separate action, the status line.
    static void Surface(Action<bool, string> check)
    {
        var en = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        var ar = System.Globalization.CultureInfo.GetCultureInfo("ar");
        VideoItem Episode(string id) =>
            new()
            {
                Id = id,
                Season = 1,
                Episode = 1,
                ImdbId = "tt9",
            };

        // F13: Serializd fails for a day; the notice appears once, names the counts, and clears on success.
        var state = new PlayerState { SyncTrakt = true, SyncSerializd = true };
        for (int n = 0; n < 4; n++)
            CommunityQueue.Enqueue(state, Episode("s" + n), t0.AddMinutes(n));
        state.WatchSync["Serializd:s0"].SyncedAt = t0.AddDays(-30);
        var backoff = new CommunityBackoff(state.SyncPace);
        Task<bool> Send(WatchSyncJob j) =>
            j.Provider == "Serializd"
                ? throw new SyncSiteException("The website could not load.")
                : Task.FromResult(true);
        var early = true;
        var now = t0;
        for (int h = 0; h <= 23; h++)
        {
            now = t0 + TimeSpan.FromHours(h);
            Pass(state, backoff, now, Send);
            early &= CommunityQueue.Notices(state, backoff, now).Count == 0;
        }
        check(early, "No stopped-site notice during the first day of failures");
        now = t0 + TimeSpan.FromHours(24);
        Pass(state, backoff, now, Send);
        var notices = CommunityQueue.Notices(state, backoff, now);
        check(
            notices.Count == 1
                && notices[0].Provider == "Serializd"
                && notices[0].Waiting == 3
                && notices[0].LastSynced == t0.AddDays(-30)
                && CommunityQueue.NoticeText(notices[0], false, en).StartsWith("Serializd has not synced since ")
                && CommunityQueue.NoticeText(notices[0], false, en).EndsWith(" · 3 waiting")
                && CommunityQueue.NoticeText(notices[0], true, ar).Contains("لم تتم مزامنته منذ"),
            "After a day of failing the notice appears once, for that site only, with last success and waiting count (English and Arabic)"
        );
        var restarted = new CommunityBackoff(state.SyncPace);
        check(
            CommunityQueue.Notices(state, restarted, now).Count == 1,
            "The notice survives a restart (failure start is stored with the pace state)"
        );
        Pass(state, backoff, now + TimeSpan.FromHours(26), _ => Task.FromResult(true));
        check(
            CommunityQueue.Notices(state, backoff, now + TimeSpan.FromHours(26)).Count == 0
                && backoff.FailingSince("Serializd") == null,
            "The notice clears on the next success"
        );
        var off = new PlayerState { SyncTrakt = true };
        var offBackoff = new CommunityBackoff(off.SyncPace);
        offBackoff.SiteFailed("Serializd", t0);
        check(
            CommunityQueue.Notices(off, offBackoff, t0.AddDays(3)).Count == 0,
            "A site switched off shows no stopped-site notice"
        );

        // U84/U85: a card's header is the state only; a stopped site is amber and never says "Sending".
        var sound = new CommunityBackoff(state.SyncPace);
        var okHeader = CommunityQueue.Header(state, sound, "Trakt", t0, false, en);
        check(
            okHeader.Text == "Sending" && okHeader.Dot == "green" && !okHeader.Text.Contains("waiting"),
            "U84 a card whose last attempt worked reads Sending with a green dot and no count"
        );
        var stopped = new CommunityBackoff(state.SyncPace);
        stopped.SiteFailed("Serializd", t0);
        var hdrSoon = CommunityQueue.Header(state, stopped, "Serializd", t0 + TimeSpan.FromHours(3), false, en);
        var hdrLate = CommunityQueue.Header(state, stopped, "Serializd", t0 + TimeSpan.FromDays(2), false, en);
        var hdrLateAr = CommunityQueue.Header(state, stopped, "Serializd", t0 + TimeSpan.FromDays(2), true, ar);
        check(
            hdrSoon.Dot == "amber"
                && hdrSoon.Text == "Retrying"
                && hdrLate.Dot == "amber"
                && hdrLate.Text.StartsWith("Not syncing since ")
                && !hdrLate.Text.Contains("Sending")
                && hdrLateAr.Text.StartsWith("لا تتم المزامنة منذ")
                && !hdrLateAr.Text.Contains("يرسل"),
            "U84 with the stopped notice the header reads Not syncing since <date> (English and Arabic), amber, never Sending"
        );
        var offState = new PlayerState();
        check(
            CommunityQueue.Header(offState, sound, "Trakt", t0, false, en) is { Text: "Off", Dot: "grey" },
            "U84 a switched-off site reads Off with a grey dot"
        );
        var paused = State(("p1", true), ("p2", true));
        paused.WatchSync.Values.ToList().ForEach(j => j.Backlog = true);
        var pausedBackoff = new CommunityBackoff(paused.SyncPace);
        pausedBackoff.SetBacklogPaused("Trakt", true);
        check(
            CommunityQueue.Header(paused, pausedBackoff, "Trakt", t0, false, en).Text == "Paused",
            "U85 when only past history is queued and it is paused the header reads Paused"
        );
        var cardNotice = CommunityQueue.NoticeText(notices[0], false, en, false);
        var bannerNotice = CommunityQueue.NoticeText(notices[0], false, en);
        var counts = CommunityQueue.StatusText(CommunityQueue.Status(state, backoff, "Serializd", now), false, en);
        check(
            !cardNotice.Contains("waiting")
                && bannerNotice.EndsWith(" · 3 waiting")
                && !CommunityQueue.NoticeText(notices[0], true, ar, false).Contains("في الانتظار")
                && counts.Split(" · ").Count(p => p.EndsWith("waiting")) == 1
                && !okHeader.Text.Any(char.IsDigit)
                && !hdrLate.Text.Any(c => c is >= '٠' and <= '٩'),
            "U85 the in-card notice drops the waiting count (the History banner keeps it) and one counts line carries it once"
        );

        // F14: titles waiting for an ID are looked up once a day; a found ID unparks the job; specials are listed.
        var ids = new PlayerState { SyncTrakt = true };
        var keys = new Dictionary<string, string> { ["x1"] = "", ["x2"] = "tt2" };
        CommunityQueue.Enqueue(ids, new VideoItem { Id = "x1", IsMovie = true }, t0);
        CommunityQueue.Enqueue(
            ids,
            new VideoItem
            {
                Id = "sp",
                Season = 0,
                Episode = 3,
            },
            t0.AddMinutes(-1)
        );
        var idBackoff = new CommunityBackoff(ids.SyncPace);
        Task<bool> SendIds(WatchSyncJob j) =>
            j.ItemId == "x1" && keys["x1"].Length == 0 ? throw new SyncTitleException(CommunityQueue.NoIdMessage)
            : j.ItemId == "sp" ? throw new SyncTitleException(CommunityQueue.SpecialMessage)
            : Task.FromResult(true);
        void IdPass(DateTimeOffset at) =>
            CommunityQueue
                .Pass(
                    ids,
                    idBackoff,
                    () => at,
                    j => j.ItemId + keys.GetValueOrDefault(j.ItemId, ""),
                    _ => true,
                    SendIds,
                    () => { },
                    _ => Task.CompletedTask
                )
                .GetAwaiter()
                .GetResult();
        var lookups = 0;
        bool Lookup(DateTimeOffset at) =>
            CommunityQueue
                .RetryIds(ids, idBackoff, at, () =>
                {
                    lookups++;
                    return Task.CompletedTask;
                })
                .GetAwaiter()
                .GetResult();
        IdPass(t0);
        Lookup(t0);
        var first = lookups;
        Lookup(t0.AddHours(23));
        var beforeDay = lookups;
        Lookup(t0.AddHours(25));
        Lookup(t0.AddHours(26));
        var afterDay = lookups;
        Lookup(t0.AddHours(50));
        check(
            first == 0 && beforeDay == 0 && afterDay == 1 && lookups == 2,
            "Titles waiting for a verified ID are looked up again once a day (not in the first day, not twice in one day)"
        );
        var noWaiting = new PlayerState { SyncTrakt = true };
        var noneCalls = 0;
        CommunityQueue
            .RetryIds(noWaiting, new CommunityBackoff(noWaiting.SyncPace), t0.AddDays(9), () =>
            {
                noneCalls++;
                return Task.CompletedTask;
            })
            .GetAwaiter()
            .GetResult();
        check(noneCalls == 0, "No daily lookup runs when no title is waiting for an ID");
        keys["x1"] = "tt1";
        IdPass(t0.AddHours(51));
        check(
            ids.WatchSync["Trakt:x1"].SyncedAt != null && ids.WatchSync["Trakt:sp"].SyncedAt == null,
            "A title whose ID the lookup found is sent on the next pass; a special stays parked"
        );
        check(
            CommunityQueue.NeedsMatch(new VideoItem { ImdbId = "" })
                && !CommunityQueue.NeedsMatch(new VideoItem { ImdbId = "tt0111161" })
                && CommunityQueue.NeedsId(new WatchSyncJob { Message = CommunityQueue.NoIdMessage }),
            "A title without a verified ID is offered Match this title"
        );
        var special = CommunityQueue.Specials(ids, "Trakt");
        check(
            special.Count == 1
                && special[0].ItemId == "sp"
                && ReviewLinks
                    .TraktUrl(
                        new VideoItem
                        {
                            ImdbId = "tt0111161",
                            Season = 0,
                            Episode = 3,
                        }
                    )
                    .StartsWith("https://"),
            "Specials that need a manual mark are listed, each with a page link"
        );

        // S3: switching a site on queues nothing past; the past history is its own action with an estimate.
        var late = new PlayerState();
        for (int n = 0; n < 1240; n++)
            late.Catalog["h" + n] = new CatalogEntry
            {
                Item = new VideoItem
                {
                    Id = "h" + n,
                    IsMovie = n % 2 == 0,
                    ImdbId = "tt1",
                },
                Progress = new Bookmark { Completed = true, UpdatedAt = t0.AddHours(-n - 1) },
            };
        late.SyncTrakt = true;
        late.SyncSerializd = true;
        var lateBackoff = new CommunityBackoff(late.SyncPace);
        Pass(late, lateBackoff, t0, _ => Task.FromResult(true));
        check(late.WatchSync.Count == 0, "Switching a site on queues none of the past history");
        CommunityQueue.Enqueue(late, new VideoItem { Id = "new", IsMovie = true }, t0);
        check(
            late.WatchSync.Keys.Order().SequenceEqual(new[] { "Trakt:new" }),
            "From then on what is watched is queued (a movie goes to Trakt, not Serializd)"
        );
        late.WatchSync.Clear();
        var plan = CommunityQueue.PlanBacklog(late, "Trakt");
        check(
            plan.Titles == 1240
                && CommunityQueue.BacklogText(plan, false, en) == "1,240 titles · about 6 days in the background"
                && CommunityQueue.PlanBacklog(late, "Serializd").Titles == 620
                && CommunityQueue.BacklogText(new("Trakt", 1, 0.005), false, en)
                    == "1 title · less than a day in the background"
                && CommunityQueue.BacklogText(plan, true, ar).Contains("أيام"),
            "Past history says how many titles and how long at the pace before anything is queued (English and Arabic)"
        );
        check(late.WatchSync.Count == 0, "Asking for the estimate queues nothing");

        var past = new PlayerState { SyncTrakt = true, SyncSerializd = true };
        for (int n = 0; n < 6; n++)
            past.Catalog["p" + n] = new CatalogEntry
            {
                Item = new VideoItem { Id = "p" + n, ImdbId = "tt1" },
                Progress = new Bookmark { Completed = true, UpdatedAt = t0.AddDays(-n - 1) },
            };
        var pastBackoff = new CommunityBackoff(past.SyncPace);
        CommunityQueue.Enqueue(past, new VideoItem { Id = "fresh", ImdbId = "tt1" }, t0);
        var trakt = CommunityQueue.QueueBacklog(past, "Trakt");
        check(
            trakt == 6 && past.WatchSync.Keys.All(k => !k.StartsWith("Serializd:p")),
            "Also send my past history queues the chosen site only"
        );
        pastBackoff.SetBacklogPaused("Trakt", true);
        var sentIds = new List<string>();
        Task<bool> Track(WatchSyncJob j)
        {
            sentIds.Add(j.Provider + ":" + j.ItemId);
            return Task.FromResult(true);
        }
        Pass(past, pastBackoff, t0, Track);
        var pausedOk = sentIds.Contains("Trakt:fresh") && !sentIds.Any(i => i.StartsWith("Trakt:p"));
        check(
            pausedOk
                && new CommunityBackoff(past.SyncPace).BacklogPaused("Trakt")
                && CommunityQueue.Status(past, pastBackoff, "Trakt", t0).BacklogPaused,
            "Pausing the past history holds only the backlog (new watches still go) and survives a restart"
        );
        pastBackoff.SetBacklogPaused("Trakt", false);
        sentIds.Clear();
        Pass(past, pastBackoff, t0.AddMinutes(10), Track);
        check(sentIds.Count(i => i.StartsWith("Trakt:p")) == 6, "Resuming sends the past history");

        var cancel = new PlayerState { SyncTrakt = true, SyncSerializd = true };
        for (int n = 0; n < 6; n++)
            cancel.Catalog["c" + n] = new CatalogEntry
            {
                Item = new VideoItem { Id = "c" + n, ImdbId = "tt1" },
                Progress = new Bookmark { Completed = true, UpdatedAt = t0.AddDays(-n - 1) },
            };
        var cancelBackoff = new CommunityBackoff(cancel.SyncPace);
        CommunityQueue.Enqueue(cancel, new VideoItem { Id = "newer", ImdbId = "tt1" }, t0);
        CommunityQueue.QueueBacklog(cancel);
        cancel.WatchSync["Trakt:c0"].SyncedAt = t0;
        cancelBackoff.SetBacklogPaused("Trakt", true);
        var gone = CommunityQueue.CancelBacklog(cancel, cancelBackoff, "Trakt");
        check(
            gone == 5
                && cancel.WatchSync.ContainsKey("Trakt:c0")
                && cancel.WatchSync.ContainsKey("Trakt:newer")
                && cancel.WatchSync.Keys.Count(k => k.StartsWith("Serializd:c")) == 6
                && !cancelBackoff.BacklogPaused("Trakt"),
            "Cancelling one site's past history keeps what was sent, newer jobs and the other site's backlog"
        );

        // S4: one line per site, and the list is the newest 50 with "and N more".
        var big = new PlayerState { SyncTrakt = true };
        for (int n = 0; n < 143; n++)
            CommunityQueue.Enqueue(big, new VideoItem { Id = "b" + n, IsMovie = true }, t0.AddMinutes(n));
        for (int n = 0; n < 6; n++)
            big.WatchSync["Trakt:b" + n].SyncedAt = t0.AddDays(-2);
        var bigBackoff = new CommunityBackoff(big.SyncPace);
        var st = CommunityQueue.Status(big, bigBackoff, "Trakt", t0.AddDays(1));
        var line = CommunityQueue.StatusText(st, false, en);
        var newest = CommunityQueue.Newest(big, "Trakt");
        check(
            st.Synced == 6
                && st.Unsent == 137
                && line.StartsWith("6 synced, last ")
                && line.Contains(" · 137 waiting · up to 200 a day · done about ")
                && st.EstimatedFinish == t0.AddDays(1) + TimeSpan.FromDays(137.0 / 200)
                && CommunityQueue.StatusText(st, true, ar).Contains("137 في الانتظار"),
            "A site's line shows synced, waiting, the pace and the estimated finish (English and Arabic)"
        );
        check(
            newest.Count == 50
                && newest[0].ItemId == "b142"
                && newest.Zip(newest.Skip(1)).All(p => p.First.WatchedAt >= p.Second.WatchedAt)
                && CommunityQueue.UnsentCount(big, "Trakt") - newest.Count == 87,
            "The job list is the newest 50, and the rest is counted as and 87 more"
        );
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

    // U97: Arabic text in the app shows Western digits, as the rest of the app does; the app's own strings hold none.
    static void LatinDigits(Action<bool, string> check)
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Noir"), "*.cs"))
        {
            if (Path.GetFileName(file).Contains("Validation", StringComparison.OrdinalIgnoreCase))
                continue;
            var n = 0;
            foreach (var line in File.ReadLines(file))
            {
                n++;
                if (line.Any(c => c is >= '٠' and <= '٩' or >= '۰' and <= '۹'))
                    offenders.Add(Path.GetFileName(file) + ":" + n);
            }
        }
        check(offenders.Count == 0, "U97 no Arabic-Indic digits in the app's strings " + string.Join(", ", offenders));
    }

    // U90: a popup is its own window, so each one the app builds says which way it reads.
    static void PopupDirection(Action<bool, string> check)
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Noir"), "*.cs"))
        {
            if (Path.GetFileName(file).Contains("Validation", StringComparison.OrdinalIgnoreCase))
                continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (lines[i].Contains("new Popup") && !lines.Skip(i).Take(6).Any(l => l.Contains("FlowDirection")))
                    offenders.Add(Path.GetFileName(file) + ":" + (i + 1));
        }
        check(
            offenders.Count == 0,
            "U90 every Popup the app builds is given the app's flow direction " + string.Join(", ", offenders)
        );
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
