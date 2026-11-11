using System.Reflection;
using System.Text.Json;
using Noir;

static class NoirReadyChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        const long gb = NoirReadyPolicy.GB;
        check(
            NoirReadyPolicy.SizeText(60 * gb) == "60 GB"
                && NoirReadyPolicy.SizeText(10 * gb) == "10 GB"
                && NoirReadyPolicy.SizeText(gb + gb / 2) == "1.5 GB"
                && NoirReadyPolicy.SizeText(0) == "0 GB",
            "U98 Keep ready budget and keep-free read 60 GB / 10 GB with decimals only when not whole"
        );
        var now = DateTimeOffset.UtcNow;
        var state = new NoirReadyState();
        ReadySource Single(string id, long size, bool confirmed = true) =>
            new(id, id, size, size, false, confirmed, "1080p");
        var budget = new ReadyStorage(6 * gb, 3 * gb, 30 * gb, 0);
        check(
            NoirReadyPolicy.Choose([Single("fits", 11 * gb)], state, budget, "1080p").Source != null,
            "Ready budget includes completed and reserved bytes at its exact boundary"
        );
        check(
            NoirReadyPolicy.Choose([Single("too-big", 12 * gb)], state, budget, "1080p").Reason
                == "Preparation budget reached",
            "Ready pauses with a human reason above budget"
        );
        check(
            NoirReadyPolicy.Choose([Single("disk", 2 * gb)], state, new(0, 0, 11 * gb, 0), "1080p").Reason
                == "Keeping free space available",
            "Ready always preserves ten GB of free space"
        );
        state.KeepFreeBytes = 0;
        check(
            NoirReadyPolicy.Choose([Single("reserve", 2 * gb)], state, new(0, 0, 12 * gb, gb), "1080p").Source == null,
            "Queued transfers reserve disk and cannot lower the ten GB floor"
        );
        state.Paused = true;
        check(
            NoirReadyPolicy.Choose([Single("pause", gb)], state, new(0, 0, 100 * gb, 0), "1080p").Reason
                == "Preparation paused",
            "Global pause prevents preparation"
        );
        state.Paused = false;
        check(
            NoirReadyPolicy
                .Choose(
                    [Single("unknown", gb, false), Single("unknown-size", 0)],
                    state,
                    new(0, 0, 100 * gb, 0),
                    "1080p"
                )
                .Source == null,
            "Unknown availability and sizes never auto-queue"
        );
        var pack = new ReadySource("pack", "pack", gb, 24 * gb, true, true, "1080p");
        check(
            NoirReadyPolicy.Choose([pack, Single("single", 2 * gb)], state, new(0, 0, 100 * gb, 0), "1080p").Source?.Id
                == "single",
            "Ready prefers a single when the real pack size breaks the budget"
        );
        check(
            NoirReadyPolicy.Choose([pack], state, new(0, 0, 100 * gb, 0), "1080p").Source == null,
            "A pack is charged at its full size rather than one episode's size"
        );
        var sharedPack = new ReadySource("second", "shared-pack", gb, 12 * gb, true, true, "1080p", 12 * gb);
        var shared = NoirReadyPolicy.Choose([sharedPack], state, new(0, 12 * gb, 100 * gb, 0), "1080p");
        check(shared.Source != null && shared.Charge == 0, "Episodes sharing a pack reuse its existing reservation");
        check(
            NoirReadyPolicy.Choose([sharedPack], state, new(0, 12 * gb, 11 * gb, gb), "1080p").Source == null,
            "Shared pack reservations still reserve physical disk for every new file"
        );
        ReadyEpisode Episode(
            string group,
            int n,
            bool watched = false,
            bool partial = false,
            DateTimeOffset? airs = null
        ) => new(group + n, group, group, 1, n, watched, partial, false, airs ?? now.AddDays(-1));
        var episodes = new[]
        {
            Episode("show", 1, true),
            Episode("show", 2),
            Episode("show", 3),
            Episode("show", 4, airs: now.AddDays(1)),
            new ReadyEpisode("unknown", "show", "show", 2, 1, false, false, false, null),
        };
        check(
            NoirReadyPolicy.Next(episodes, 2, now).Select(e => e.Episode).SequenceEqual(new[] { 2, 3 }),
            "Keep ready chooses the next two aired unwatched episodes only"
        );
        check(
            NoirReadyPolicy.Next(episodes, 1, now).Count == 1
                && NoirReadyPolicy.Next(episodes, 3, now).Count == 2
                && NoirReadyPolicy.Next(episodes, -1, now).Count == 2,
            "Ready supports one, two, three and whole-season counts"
        );
        check(
            NoirReadyPolicy.OrderShows([Episode("fresh", 1), Episode("started", 2, partial: true)]).First().Key
                == "started",
            "Partially watched shows prepare first"
        );
        var old = now.AddDays(-8);
        check(
            !NoirReadyPolicy.CanRemove(true, true, false, false, false, old, [], now),
            "Ready never evicts an unwatched episode"
        );
        check(
            !NoirReadyPolicy.CanRemove(true, false, true, false, false, old, [], now),
            "Scanned and manual files cannot enter Ready cleanup"
        );
        check(
            !NoirReadyPolicy.CanRemove(false, true, true, false, false, old, [], now)
                && !NoirReadyPolicy.CanRemove(true, true, true, true, false, old, [], now),
            "Cleanup requires consent and preserves pins"
        );
        check(
            !NoirReadyPolicy.CanRemove(true, true, true, false, true, old, [], now)
                && !NoirReadyPolicy.CanRemove(true, true, true, false, false, now.AddDays(-6), [], now),
            "Cancel removal and the seven-day window are respected"
        );
        check(
            !NoirReadyPolicy.CanRemove(true, true, true, false, false, old, [true, false], now)
                && !NoirReadyPolicy.CanRemove(true, true, true, false, false, old, [null], now),
            "Another profile's unfinished or unknown progress prevents removal"
        );
        check(
            NoirReadyPolicy.CanRemove(true, true, true, false, false, old, [true, true], now),
            "Only watched managed files finished by every profile become eligible"
        );
        check(
            NoirReadyPolicy.Backoff(now, 1) == now.AddMinutes(15) && NoirReadyPolicy.Backoff(now, 9) == now.AddHours(6),
            "Unknown sources use bounded exponential recheck backoff"
        );
        for (int n = 0; n < 220; n++)
            state.Log("check", n.ToString());
        check(state.Why.Count == 200 && state.Why[0].Title == "20", "Ready why-log retains the last 200 actions");

        var folder = Path.Combine(root, "noir-ready-" + Guid.NewGuid().ToString("N"));
        var store = new LibraryStore(folder);
        var media = Path.Combine(folder, "media");
        Directory.CreateDirectory(media);
        store.State.Roots = [new() { Path = media }];
        var item = new VideoItem
        {
            Id = "ready-check",
            CollectionKey = "ready-show",
            Title = "Ready Fixture",
            Season = 1,
            Episode = 1,
            Available = false,
            ImdbId = "tt123",
        };
        store.State.Catalog[item.Id] = new() { Item = item };
        store.State.EpisodeSchedules[item.GroupKey] =
        [
            new()
            {
                Season = 1,
                Episode = 1,
                AirsAt = now.AddDays(-1),
            },
            new()
            {
                Season = 1,
                Episode = 2,
                AirsAt = now.AddDays(-1),
            },
            new()
            {
                Season = 1,
                Episode = 3,
                AirsAt = now.AddDays(1),
            },
        ];
        store.SetShowDownloadPreference(item.GroupKey, new() { AutoDownload = true });
        var provider = new ReadyProvider();
        using var downloads = new PcDownloads(store, provider, start: false, freeSpace: _ => 100 * gb);
        downloads.Configure("", "fixture", media, "");
        downloads.InitializeReady();
        // A5: a migrated count of 2 is "same as default", and the default is 2.
        check(
            downloads.SeasonPreferences(item.Id) is { KeepReadyEnabled: true } migrated
                && NoirReadyPolicy.Count(migrated, downloads.ReadyState) == 2,
            "Existing AutoDownload migrates to Keep ready two"
        );
        await downloads.PrepareReady("Startup");
        check(
            downloads.Jobs().Count == 2 && downloads.ReadyState.ManagedFiles.Count == 2,
            "Ready queues two confirmed aired files and records ownership"
        );
        check(
            provider.UnknownFileCalls == 0,
            "Automatic planner filters unknown sources before reading file lists or adding magnets"
        );
        check(
            File.Exists(Path.Combine(folder, "noir-ready.json"))
                && JsonSerializer
                    .Deserialize<NoirReadyState>(File.ReadAllText(Path.Combine(folder, "noir-ready.json")))!
                    .ManagedFiles.Count == 2,
            "Ready intentions and ownership persist in the profile directory"
        );
        await downloads.ChangeReady(s => s.Paused = true);
        check(downloads.Jobs().All(j => j.Status == "Paused"), "Global pause stops queued managed files");
        await downloads.ChangeReady(s => s.Paused = false);
        check(downloads.Jobs().All(j => j.Status == "Queued"), "Resume restarts only files paused by preparation");
        downloads.SaveSeasonPreferences(item.Id, new() { KeepReadyEnabled = false });
        store.State.Watchlist.Add(item.GroupKey);
        foreach (var job in downloads.Jobs())
            downloads.Control(job.Id, "cancel");
        var before = downloads.Jobs().Count;
        await downloads.PrepareReady("Preferences changed");
        check(
            downloads.Jobs().Count == before && downloads.PreparedTitle(item.GroupKey)?.State == "Ready",
            "My List prepares ready metadata without downloading"
        );
        await downloads.ChangeReady(s => s.BudgetBytes = gb);
        await downloads.PrepareReady("Preferences changed");
        check(
            downloads.Jobs().Count == before,
            "Budget pressure never evicts unwatched files or creates replacement jobs"
        );
        File.WriteAllBytes(Path.Combine(folder, "download-parts", "manual-orphan.part"), new byte[4096]);
        await downloads.CheckReadyStorage();
        check(
            downloads.ReadyStorage.UsedBytes >= 4096,
            "Storage accounting includes real partial files, including interrupted manual transfers"
        );
        foreach (var episode in store.EpisodesFor(item.GroupKey).Where(e => e.Episode is 1 or 2))
            store.Mark(episode, true);
        store.State.EpisodeSchedules[item.GroupKey].Single(e => e.Episode == 3).AirsAt = now.AddDays(-1);
        await downloads.PrepareReady("New episode");
        check(
            downloads.PreparedTitle(item.GroupKey)?.Episodes.SingleOrDefault()?.Episode == 3,
            "A newly aired next episode invalidates a cached plan before its backoff expires"
        );
        store.State.Catalog[item.Id].Progress!.Completed = false;
        store.State.Catalog[item.Id].Progress!.Position = 0;
        check(
            !store.ReadyCleanupInputs([item.Id])[item.Id].Watched,
            "An unfinished rewatch at position zero prevents cleanup despite older watched history"
        );
        await Rules(root, check);
    }

    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    // A1–A8 (sharpen round): fake clocks through the `now` parameters and ReadyClock; temp folders only.
    static async Task Rules(string root, Action<bool, string> check)
    {
        const long gb = NoirReadyPolicy.GB;
        var now = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

        // Pure rules.
        var old = new NoirReadyState { CleanupWatched = true };
        var oldOff = new NoirReadyState { CleanupWatched = false };
        check(
            NoirReadyPolicy.RemoveHours(old) == 168 && NoirReadyPolicy.RemoveHours(oldOff) == -1,
            "A1: state saved before the rules keeps its meaning (remove watched on = 7 days, off = never)"
        );
        PcDownloads.MigrateReadyRules(old);
        PcDownloads.MigrateReadyRules(oldOff);
        check(
            old is { RulesVersion: 1, RemoveAfterHours: 168, RemoveScope: "Keep ready", RecycleRemoved: false }
                && oldOff.RemoveAfterHours == -1
                && old is { ReadyCount: 2, AutoShows: "Switched", WindowStart: null, KeepLastWatched: 0 }
                && new NoirReadyState().RemoveAfterHours == -1,
            "A1–A8 migration: on -> After 7 days, off -> Never; Keep ready only, delete, 2 ahead, switched shows, any time"
        );
        var watchedAt = now.AddDays(-2);
        check(
            NoirReadyPolicy.Schedule(null, 0, 168, watchedAt, now) == watchedAt.AddDays(7)
                && NoirReadyPolicy.Schedule(null, 0, 168, now.AddDays(-9), now) == now.AddDays(1),
            "A1: first scheduling is today's (watched + 7 days; a date already past becomes now + 1 day)"
        );
        var scheduled = watchedAt.AddDays(7);
        check(
            NoirReadyPolicy.Schedule(scheduled, 168, 1, watchedAt, now) == now.AddDays(1)
                && NoirReadyPolicy.Schedule(scheduled, 168, 24, watchedAt, now) == now.AddDays(1)
                && NoirReadyPolicy.Schedule(now.AddHours(5), 168, 1, watchedAt, now) == now.AddHours(5)
                && NoirReadyPolicy.Schedule(now.AddDays(3), 720, 168, now.AddDays(-5), now) == now.AddDays(2),
            "A1: a rule change never schedules a removal sooner than now + 1 day"
        );
        check(
            NoirReadyPolicy.Schedule(scheduled, 168, 720, watchedAt, now) == watchedAt.AddDays(30)
                && NoirReadyPolicy.Schedule(scheduled, 0, 1, watchedAt, now) == scheduled,
            "A1: a later date applies at once; a date from before the rules is never recomputed"
        );
        var justWatched = now.AddMinutes(-10);
        check(
            NoirReadyPolicy.Schedule(null, 0, 1, justWatched, now) == justWatched.AddHours(1)
                && !NoirReadyPolicy.CanRemove(true, true, true, false, false, justWatched, [], now, 1)
                && NoirReadyPolicy.CanRemove(
                    true,
                    true,
                    true,
                    false,
                    false,
                    justWatched,
                    [],
                    justWatched.AddMinutes(61),
                    1
                )
                && !NoirReadyPolicy.CanRemove(true, true, true, false, false, now.AddDays(-99), [], now, -1),
            "A1: Right after watching waits one hour; Never never removes"
        );
        var defaults = new NoirReadyState { RulesVersion = 1, ReadyCount = 5 };
        ReadyEpisode Aired(int n) => new("e" + n, "g", "g", 1, n, false, false, false, now.AddDays(-1));
        check(
            NoirReadyPolicy.Count(new(), defaults) == 5
                && NoirReadyPolicy.Count(new() { KeepReadyCount = 3 }, defaults) == 3
                && NoirReadyPolicy.Count(new() { KeepReadyCount = 4 }, new()) == 2
                && NoirReadyPolicy.Next(Enumerable.Range(1, 8).Select(Aired), 5, now).Count == 5
                && NoirReadyPolicy.MigrateCount(new()) == 2,
            "A5: 1, 2, 3, 5 or the rest of the season; a show without its own count follows the default (2)"
        );
        var switched = new NoirReadyState();
        var watchingScope = new NoirReadyState { AutoShows = "Watching" };
        var listScope = new NoirReadyState { AutoShows = "Watchlist" };
        check(
            NoirReadyPolicy.AutomaticReason(new(), switched, true, true) == null
                && NoirReadyPolicy.AutomaticReason(new() { AutoDownload = true }, switched, false, false)
                    == "Switched on for this show"
                && NoirReadyPolicy.AutomaticReason(new(), watchingScope, true, false) == "A show you are watching"
                && NoirReadyPolicy.AutomaticReason(new(), watchingScope, false, true) == null
                && NoirReadyPolicy.AutomaticReason(new(), listScope, false, true) == "On your watchlist"
                && NoirReadyPolicy.AutomaticReason(new() { KeepReadyEnabled = false }, listScope, true, true) == null,
            "A6+A7: only switched-on shows by default; watching / watchlist widen it; a show's own switch wins"
        );
        check(
            NoirReadyPolicy.InWindow(22 * 60, 6 * 60, TimeSpan.FromHours(23.5))
                && NoirReadyPolicy.InWindow(22 * 60, 6 * 60, TimeSpan.FromMinutes(5 * 60 + 59))
                && !NoirReadyPolicy.InWindow(22 * 60, 6 * 60, TimeSpan.FromHours(6))
                && !NoirReadyPolicy.InWindow(22 * 60, 6 * 60, TimeSpan.FromHours(12))
                && NoirReadyPolicy.InWindow(1 * 60, 7 * 60, TimeSpan.FromHours(3))
                && !NoirReadyPolicy.InWindow(1 * 60, 7 * 60, TimeSpan.FromHours(8))
                && NoirReadyPolicy.InWindow(null, null, TimeSpan.FromHours(12)),
            "A8: a download window may cross midnight; no window = any time"
        );

        // A1 migration on disk: old state -> the same removals at the same times.
        var pre = now.AddDays(2);
        using (
            var f = new RulesFixture(
                root,
                new NoirReadyState
                {
                    Migrated = true,
                    CleanupWatched = true,
                    ManagedFiles =
                    [
                        new()
                        {
                            JobId = "job-rules-show2",
                            ItemId = "rules-show2",
                            Group = "rules-show",
                            Path = "",
                            RemoveAt = pre,
                        },
                    ],
                }
            )
        )
        {
            var first = f.Episode(1, now.AddDays(-3), managed: false);
            var second = f.Episode(2, now.AddDays(-10), managed: false);
            await f.Manager.ChangeReady(s =>
            {
                s.ManagedFiles[0].Path = second.Destination;
                s.ManagedFiles.Add(
                    new()
                    {
                        JobId = first.Id,
                        ItemId = first.Target.Id,
                        Group = "rules-show",
                        Path = first.Destination,
                        Title = "one",
                    }
                );
            });
            f.Reconcile(now);
            var migrated = f.Manager.ReadyState;
            check(
                migrated is { RulesVersion: 1, RemoveAfterHours: 168 }
                    && f.Rec(first.Id).RemoveAt == now.AddDays(4)
                    && f.Rec(second.Id) is { RuleHours: 168 } kept
                    && kept.RemoveAt == pre,
                "A1 migration: watched + 7 days as before, and a removal already scheduled keeps its date"
            );
            f.Reconcile(now.AddDays(2).AddMinutes(-1));
            f.Reconcile(now.AddDays(4).AddMinutes(-1));
            var beforeDue = File.Exists(first.Destination);
            f.Reconcile(now.AddDays(4).AddMinutes(1));
            check(
                beforeDue
                    && !File.Exists(first.Destination)
                    && !File.Exists(second.Destination)
                    && f.Removed.All(r => !r.Recycle),
                "A1 migration: files are removed at the same times as before, permanently (A4 default)"
            );
        }
        using (var f = new RulesFixture(root, new NoirReadyState { Migrated = true, CleanupWatched = false }))
        {
            var job = f.Episode(1, now.AddDays(-30), managed: true);
            f.Reconcile(now);
            f.Reconcile(now.AddDays(60));
            check(
                f.Manager.ReadyState.RemoveAfterHours == -1
                    && f.Rec(job.Id).RemoveAt == null
                    && File.Exists(job.Destination),
                "A1 migration: remove watched off becomes Never and nothing is scheduled"
            );
        }

        // A1 Right after watching.
        using (var f = new RulesFixture(root))
        {
            await f.Manager.ChangeReady(s => s.RemoveAfterHours = 1);
            var w = now.AddMinutes(-10);
            var job = f.Episode(1, w, managed: true);
            f.Reconcile(now);
            var at = f.Rec(job.Id).RemoveAt;
            f.Reconcile(w.AddMinutes(30));
            var stayed = File.Exists(job.Destination);
            f.Reconcile(w.AddMinutes(61));
            check(
                at == w.AddHours(1) && stayed && !File.Exists(job.Destination),
                "A1: Right after watching removes an hour after watching, not before"
            );
        }

        // A1 rule change: earlier never sooner than now + 1 day; later at once.
        using (var f = new RulesFixture(root))
        {
            await f.Manager.ChangeReady(s => s.RemoveAfterHours = 168);
            var a = f.Episode(1, now.AddDays(-2), managed: true);
            var b = f.Episode(2, now.AddHours(-12), managed: true);
            f.Reconcile(now);
            var first = (f.Rec(a.Id).RemoveAt, f.Rec(b.Id).RemoveAt);
            await f.Manager.ChangeReady(s => s.RemoveAfterHours = 1);
            f.Reconcile(now);
            var earlier = (f.Rec(a.Id).RemoveAt, f.Rec(b.Id).RemoveAt);
            f.Reconcile(now.AddHours(23));
            check(
                first == (now.AddDays(5), now.AddDays(6.5))
                    && earlier == (now.AddDays(1), now.AddDays(1))
                    && File.Exists(a.Destination)
                    && File.Exists(b.Destination)
                    && f.Manager.ReadyViewState.Why.Any(w => w.Code == "Removal date changed"),
                "A1: changing to an earlier rule moves removals to now + 1 day at the soonest"
            );
            await f.Manager.ChangeReady(s => s.RemoveAfterHours = 720);
            f.Reconcile(now.AddHours(23));
            check(
                f.Rec(a.Id).RemoveAt == now.AddDays(28) && f.Rec(b.Id).RemoveAt == now.AddHours(-12).AddDays(30),
                "A1: changing to a later rule moves the removal date at once"
            );
        }

        // A2 scope, pins and user-added files.
        using (var f = new RulesFixture(root))
        {
            await f.Manager.ChangeReady(s => s.RemoveAfterHours = 24);
            var managed = f.Episode(1, now.AddDays(-5), managed: true);
            var pinned = f.Episode(2, now.AddDays(-5), managed: true);
            var manual = f.Episode(3, now.AddDays(-5), managed: false);
            var manualPinned = f.Episode(4, now.AddDays(-5), managed: false);
            var movie = f.Episode(1, now.AddDays(-5), managed: false, movie: true, group: "rules-movie");
            var userFile = Path.Combine(f.Media, "rules-show-own.mkv");
            File.WriteAllBytes(userFile, [9]);
            f.Store.State.Catalog["own"] = new()
            {
                Item = new()
                {
                    Id = "own",
                    CollectionKey = "rules-show",
                    Season = 1,
                    Episode = 5,
                    Path = userFile,
                    Available = true,
                },
                Progress = new() { Completed = true, UpdatedAt = now.AddDays(-5) },
            };
            await f.Manager.ChangeReady(s => s.ManagedFiles.Single(x => x.JobId == pinned.Id).Pinned = true);
            f.Reconcile(now);
            check(
                f.Rec(managed.Id).RemoveAt == now.AddDays(1)
                    && f.Rec(manual.Id).RemoveAt == null
                    && f.Manager.ReadyState.NoirFiles.Count == 3,
                "A2: Keep ready files only (default) leaves manual downloads alone; Noir records them at download time"
            );
            await f.Manager.ChangeReady(s =>
            {
                s.RemoveScope = "Noir";
                s.NoirFiles.Single(x => x.JobId == manualPinned.Id).Pinned = true;
            });
            f.Reconcile(now);
            check(
                f.Rec(manual.Id).RemoveAt == now.AddDays(1)
                    && f.Rec(movie.Id).RemoveAt == null
                    && f.Rec(manualPinned.Id).RemoveAt == null,
                "A2: Everything Noir downloaded covers manual episodes; movies stay off by default; pins respected"
            );
            f.Reconcile(now.AddDays(2));
            check(
                !File.Exists(managed.Destination)
                    && !File.Exists(manual.Destination)
                    && File.Exists(movie.Destination)
                    && File.Exists(pinned.Destination)
                    && File.Exists(manualPinned.Destination)
                    && File.Exists(userFile)
                    && f.Removed.All(r => r.Path != userFile),
                "A2: pinned and user-added files are never removed (Keep ready and Noir downloads)"
            );
            await f.Manager.ChangeReady(s => s.RemoveMovies = true);
            f.Manager.ClearFinished();
            f.Reconcile(now.AddDays(2));
            var movieAt = f.Rec(movie.Id).RemoveAt;
            f.Reconcile(now.AddDays(3).AddMinutes(1));
            check(
                movieAt == now.AddDays(3) && !File.Exists(movie.Destination) && File.Exists(manualPinned.Destination),
                "A2: Movies too includes Noir's movies, and a manual download is still judged after Clear finished"
            );
            check(
                f.Manager.ReadyViewState.Why.Any(w =>
                    w.Code == "Removed a watched Noir download"
                    && w.Detail == "1 day after watching · Downloaded by Noir"
                ),
                "Activity log names the rule and the scope behind each removal"
            );
        }

        // A2: a manual download moved in Manage keeps its record and is still judged at its new path.
        using (var f = new RulesFixture(root))
        {
            await f.Manager.ChangeReady(s =>
            {
                s.RemoveAfterHours = 24;
                s.RemoveScope = "Noir";
            });
            var manual = f.Episode(1, now.AddDays(-5), managed: false);
            f.Reconcile(now);
            var moved = Path.Combine(f.Media, "Moved", "rules-show-1.mkv");
            Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
            File.Move(manual.Destination, moved);
            File.SetLastWriteTimeUtc(moved, f.Rec(manual.Id).LastWriteUtc!.Value);
            f.Manager.UpdateMovedFiles([(manual.Destination, moved)]);
            f.Reconcile(now);
            var record = f.Rec(manual.Id);
            f.Reconcile(now.AddDays(2));
            check(
                record.Path == moved && record.RemoveAt == now.AddDays(1) && !File.Exists(moved),
                "A2: a moved manual download keeps its record and its removal follows the new path"
            );
        }

        // A3 keep the last N watched, with per-show overrides.
        using (var f = new RulesFixture(root))
        {
            await f.Manager.ChangeReady(s =>
            {
                s.RemoveAfterHours = 24;
                s.KeepLastWatched = 1;
            });
            var older = f.Episode(1, now.AddDays(-3), managed: true);
            var newer = f.Episode(2, now.AddDays(-2), managed: true);
            f.Reconcile(now);
            var kept = (f.Rec(older.Id).RemoveAt, f.Rec(newer.Id).RemoveAt);
            f.Store.SetShowDownloadPreference("rules-show", new() { KeepLastWatched = 0 });
            f.Reconcile(now);
            var none = f.Rec(newer.Id).RemoveAt;
            f.Store.SetShowDownloadPreference("rules-show", new() { RemoveAfterHours = -1 });
            f.Reconcile(now);
            check(
                kept == (now.AddDays(1), null)
                    && none == now.AddDays(1)
                    && f.Rec(older.Id).RemoveAt == null
                    && f.Rec(newer.Id).RemoveAt == null,
                "A3: the last N watched episodes stay; a show's own choice overrides the default (A1 and A3)"
            );
        }

        // A4 Recycle Bin through the shared remover.
        using (var f = new RulesFixture(root))
        {
            await f.Manager.ChangeReady(s =>
            {
                s.RemoveAfterHours = 24;
                s.RecycleRemoved = true;
            });
            var job = f.Episode(1, now.AddDays(-5), managed: true);
            f.Reconcile(now);
            f.Reconcile(now.AddDays(2));
            check(
                f.Removed.SequenceEqual([(job.Destination, true)])
                    && File.Exists(job.Destination + ".recycled")
                    && f.Manager.ReadyViewState.Why.Any(w =>
                        w.Code == "Sent a watched Keep ready file to the Recycle Bin"
                    ),
                "A4: Recycle Bin, when chosen, sends the removed file there through RemoveDisk"
            );
        }

        // A8 window, A5 default count and in-flight / manual jobs.
        {
            var (store, downloads, item) = ReadyShow(root);
            using var _ = downloads;
            store.SetShowDownloadPreference(item.GroupKey, new() { KeepReadyEnabled = true });
            var day = DateTime.Today;
            DateTimeOffset Local(double hours)
            {
                var t = day.AddHours(hours);
                return new(t, TimeZoneInfo.Local.GetUtcOffset(t));
            }
            var jobs = (List<PcDownloadJob>)typeof(PcDownloads).GetField("jobs", Hidden)!.GetValue(downloads)!;
            jobs.Add(
                new()
                {
                    Id = "running",
                    Status = "Downloading",
                    Target = new() { Id = "other", CollectionKey = "other-show" },
                }
            );
            jobs.Add(
                new()
                {
                    Id = "manual",
                    Status = "Queued",
                    Target = new() { Id = "other2", CollectionKey = "other-show" },
                }
            );
            await downloads.ChangeReady(s =>
            {
                s.WindowStart = 22 * 60;
                s.WindowEnd = 6 * 60;
                s.ManagedFiles.Add(new() { JobId = "running", ItemId = "other" });
            });
            downloads.ReadyClock = () => Local(12);
            await downloads.PrepareReady("Startup");
            var waiting = downloads.PreparedTitle(item.GroupKey);
            check(
                downloads.Jobs().Count == 2
                    && waiting?.Reason == "Waiting for download hours"
                    && waiting.RecheckAt == Local(22),
                "A8: outside the window no new automatic download starts; it rechecks when the window opens"
            );
            check(
                downloads.Jobs().Single(j => j.Id == "running").Status == "Downloading"
                    && downloads.Jobs().Single(j => j.Id == "manual").Status == "Queued",
                "A8: the window never stops an in-flight download or a manual one"
            );
            await downloads.ChangeReady(s => s.ReadyCount = 1);
            downloads.ReadyClock = () => Local(23.5);
            await downloads.PrepareReady("Preferences changed");
            check(
                downloads.Jobs().Count == 3 && downloads.ReadyState.ManagedFiles.Count == 2,
                "A8 + A5: inside a window that crosses midnight it downloads, and the default count (1 here) applies"
            );
        }

        // A6 Shows I am watching.
        {
            var (store, downloads, item) = ReadyShow(root);
            using var _ = downloads;
            store.Mark(item, true);
            await downloads.PrepareReady("Startup");
            var off = downloads.Jobs().Count;
            await downloads.ChangeReady(s => s.AutoShows = "Watching");
            var watching = store.WatchingShow(item.GroupKey, DateTimeOffset.UtcNow);
            await downloads.PrepareReady("Preferences changed");
            var queued = downloads.Jobs().Count;
            var why = downloads.ReadyViewState.Why.LastOrDefault(w => w.Code == "Queued confirmed ready file");
            downloads.SaveSeasonPreferences(item.Id, new() { KeepReadyEnabled = false });
            check(
                off == 0
                    && watching
                    && queued == 1
                    && why?.Detail.StartsWith("A show you are watching") == true
                    && downloads.AutomaticRule(item.Id) == null,
                "A6: Shows I am watching downloads a started show without its switch; switching it off wins"
            );
        }
    }

    static (LibraryStore Store, PcDownloads Downloads, VideoItem Item) ReadyShow(string root)
    {
        var aired = DateTimeOffset.UtcNow.AddDays(-2);
        var folder = Path.Combine(root, "noir-ready-show-" + Guid.NewGuid().ToString("N"));
        var store = new LibraryStore(folder);
        var media = Path.Combine(folder, "media");
        Directory.CreateDirectory(media);
        store.State.Roots = [new() { Path = media }];
        var item = new VideoItem
        {
            Id = "ready-rules",
            CollectionKey = "ready-rules-show",
            Title = "Ready Fixture",
            Season = 1,
            Episode = 1,
            Available = false,
            ImdbId = "tt124",
        };
        store.State.Catalog[item.Id] = new() { Item = item };
        store.State.EpisodeSchedules[item.GroupKey] = Enumerable
            .Range(1, 3)
            .Select(n => new KnownEpisode
            {
                Season = 1,
                Episode = n,
                AirsAt = aired,
            })
            .ToList();
        var downloads = new PcDownloads(store, new ReadyProvider(), start: false, freeSpace: _ =>
            100 * NoirReadyPolicy.GB
        );
        downloads.Configure("", "fixture", media, "");
        downloads.InitializeReady();
        return (store, downloads, item);
    }

    sealed class RulesFixture : IDisposable
    {
        public readonly LibraryStore Store;
        public readonly PcDownloads Manager;
        public readonly string Media;
        public readonly List<(string Path, bool Recycle)> Removed = [];

        public RulesFixture(string root, NoirReadyState? saved = null)
        {
            var folder = Path.Combine(root, "noir-rules-" + Guid.NewGuid().ToString("N"));
            Store = new LibraryStore(folder);
            Media = Path.Combine(folder, "media");
            Directory.CreateDirectory(Media);
            Store.State.Roots = [new() { Path = Media }];
            File.WriteAllText(
                Path.Combine(folder, "profiles.json"),
                JsonSerializer.Serialize(new ProfileList { Profiles = [new() { Id = "default" }] })
            );
            if (saved != null)
                File.WriteAllText(Path.Combine(folder, "noir-ready.json"), JsonSerializer.Serialize(saved));
            Manager = new PcDownloads(Store, start: false);
            Manager.ManageDeleter = (path, recycle) =>
            {
                Removed.Add((path, recycle));
                if (recycle)
                    File.Move(path, path + ".recycled");
                else
                    File.Delete(path);
            };
        }

        public PcDownloadJob Episode(
            int n,
            DateTimeOffset? watched,
            bool managed,
            bool movie = false,
            string group = "rules-show"
        )
        {
            var path = Path.Combine(Media, $"{group}-{n}.mkv");
            File.WriteAllBytes(path, [1, 2, 3]);
            var item = new VideoItem
            {
                Id = group + n,
                CollectionKey = group,
                Title = "Rules " + n,
                Season = movie ? 0 : 1,
                Episode = movie ? 0 : n,
                IsMovie = movie,
                Path = path,
                Available = true,
            };
            Store.State.Catalog[item.Id] = new()
            {
                Item = item,
                Progress = watched is { } w ? new() { Completed = true, UpdatedAt = w } : null,
            };
            if (watched is { } at)
                Store.State.Viewings[item.Id] = [new() { Id = "v" + item.Id, WatchedAt = at }];
            var job = new PcDownloadJob
            {
                Id = "job-" + item.Id,
                Name = item.Title,
                Status = "Completed",
                Root = Media,
                Destination = path,
                Target = item,
            };
            ((List<PcDownloadJob>)typeof(PcDownloads).GetField("jobs", Hidden)!.GetValue(Manager)!).Add(job);
            if (managed)
                Manager
                    .ChangeReady(s =>
                        s.ManagedFiles.Add(
                            new()
                            {
                                JobId = job.Id,
                                ItemId = item.Id,
                                Group = item.GroupKey,
                                Path = path,
                                Title = item.Title,
                            }
                        )
                    )
                    .Wait();
            return job;
        }

        public void Reconcile(DateTimeOffset now) =>
            typeof(PcDownloads).GetMethod("ReconcileReadyFiles", Hidden)!.Invoke(Manager, [now]);

        public ReadyManagedFile Rec(string jobId)
        {
            var state = Manager.ReadyState;
            return state.ManagedFiles.Concat(state.NoirFiles).Single(f => f.JobId == jobId);
        }

        public void Dispose() => Manager.Dispose();
    }

    sealed class ReadyProvider : IDownloadProvider
    {
        public int UnknownFileCalls;

        public Task<List<IndexedRelease>> Search(VideoItem item, CancellationToken ct) =>
            Task.FromResult(
                new List<IndexedRelease>
                {
                    new(
                        new()
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = "ready",
                            Cached = true,
                            Resolution = "1080p",
                            CacheStatus = "Ready",
                            Size = 2 * NoirReadyPolicy.GB,
                            SizeKind = "Pack",
                        },
                        new string('a', 40)
                    ),
                    new(
                        new()
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = "unknown",
                            Size = NoirReadyPolicy.GB,
                        },
                        new string('b', 40)
                    ),
                }
            );

        public Task<List<TorboxFile>> Files(string hash, CancellationToken ct)
        {
            if (hash[0] == 'b')
                UnknownFileCalls++;
            return Task.FromResult(
                new List<TorboxFile>
                {
                    new(1, "Ready.Fixture.S01E01.mkv", NoirReadyPolicy.GB),
                    new(2, "Ready.Fixture.S01E02.mkv", NoirReadyPolicy.GB),
                }
            );
        }

        public Task<TorboxTorrent> Prepare(string hash, long id, CancellationToken ct) =>
            throw new Exception("Preparation worker must not start in checks");

        public Task<string> Link(long id, long file, CancellationToken ct) =>
            throw new Exception("Offline checks never request links");
    }
}
