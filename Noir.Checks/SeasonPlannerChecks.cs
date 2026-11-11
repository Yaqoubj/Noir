using Noir;

static class SeasonPlannerChecks
{
    public static async Task Run(string parent, Action<bool, string> check)
    {
        using var fixture = new Fixture(parent);
        var m = fixture.Manager;
        var item = fixture.Item;
        var defaults = m.SeasonPreferences(item.Id);
        check(
            defaults.Resolution == "1080p" && defaults.Language == "English" && !defaults.AutoDownload,
            "Planner starts with global quality/language/size and auto-download off"
        );
        var plan = await m.PlanSeason(item.Id, new());
        check(
            plan.Episodes.Count == 10
                && plan.Entries.All(e => e.File.Episode is not (1 or 10))
                && plan.Episodes.Single(e => e.Episode == 1).State == "On PC"
                && plan.Episodes.Single(e => e.Episode == 10).State == "Unaired",
            "Missing scope explains local and unaired exclusions and includes schedule placeholders"
        );
        check(
            plan.Entries.Select(e => e.File.Episode).Order().SequenceEqual(new[] { 2, 3, 4, 5, 6, 7, 8 })
                && plan.MissingEpisodes.SequenceEqual(new[] { 9 }),
            "Planner combines a verified partial pack with a single link and flags uncovered episodes"
        );
        var third = plan.Episodes.Single(e => e.Episode == 3);
        var seventh = plan.Episodes.Single(e => e.Episode == 7);
        check(
            third.Chosen?.Release == "1080p season pack"
                && third.Reason == "Season pack covers 6 of 8"
                && seventh.Reason == "Pack missing E07 · single link"
                && fixture.Provider.SingleProbes.Contains((1, 7)),
            "Pack coverage is explained and every wanted episode is searched for single-link gaps"
        );
        check(
            third.Alternatives.Any(o => o.Release == "1080p single E03")
                && third.Alternatives.Any(o => o.Resolution == "720p")
                && third.Chosen.Source == "TorBox library"
                && third.Chosen.CacheState == "Ready"
                && third.Chosen.Language == "English",
            "Each planned episode exposes source, release, resolution, audio, cache state, size and alternatives"
        );
        fixture.Provider.Reverse = true;
        var again = await m.PlanSeason(item.Id, new());
        check(
            again.Episodes.Select(e => e.Chosen?.Release).SequenceEqual(plan.Episodes.Select(e => e.Chosen?.Release)),
            "Plan choices are deterministic when providers return a different order and fresh IDs"
        );
        var unwatched = await m.PlanSeason(item.Id, new() { Scope = "Unwatched only" });
        check(
            unwatched.Episodes.All(e => e.Episode is not (2 or 4)) && plan.Entries.Any(e => e.File.Episode == 2),
            "Unwatched scope excludes previously watched episodes; missing scope restores deleted watched episodes"
        );
        var from = await m.PlanSeason(item.Id, new() { Scope = "From episode N", FromEpisode = 8 });
        var next = await m.PlanSeason(item.Id, new() { Scope = "Next N episodes", NextCount = 2 });
        check(
            from.Episodes.Select(e => e.Episode).SequenceEqual(new[] { 8, 9, 10 })
                && next.Episodes.Select(e => (e.Season, e.Episode)).SequenceEqual(new[] { (1, 5), (1, 6) }),
            "From episode and next N scopes start at the requested episode and after the furthest watched"
        );
        var across = await m.PlanSeason(item.Id, new() { Scope = "Next N episodes", NextCount = 25 });
        check(
            across.Episodes.Any(e => e.Season == 2) && across.Entries.All(e => e.File.Episode != 10),
            "Next N can cross a season boundary while unaired episodes remain excluded"
        );
        bool invalid = false;
        try
        {
            await m.PlanSeason(item.Id, new() { Scope = "Next N episodes", NextCount = 26 });
        }
        catch (IOException)
        {
            invalid = true;
        }
        check(invalid, "Next N refuses counts outside 1–25");
        var explicitPlan = await m.PlanSeason(
            item.Id,
            new() { Scope = "Specific episodes", SpecificEpisodes = [new(1, 3), new(2, 2)] }
        );
        var whole = await m.PlanSeason(item.Id, new() { Scope = "Several seasons / whole show" });
        var secondSeason = await m.PlanSeason(item.Id, new() { Scope = "Several seasons / whole show", Seasons = [2] });
        check(
            explicitPlan.Episodes.Count == 2
                && explicitPlan.Entries.Count == 2
                && whole.Episodes.Count == 13
                && secondSeason.Episodes.All(e => e.Season == 2)
                && secondSeason.Episodes.Count == 3,
            "Explicit episodes, selected seasons and whole-show scope combine independently planned seasons"
        );
        var preference = new ShowDownloadPreference
        {
            Resolution = "720p",
            Language = "English",
            MaxEpisodeBytes = 150,
            AutoDownload = true,
        };
        m.SaveSeasonPreferences(item.Id, preference);
        preference.Language = "Arabic";
        using (
            var reopened = new PcDownloads(
                new LibraryStore(fixture.Store.DataDirectory),
                fixture.Provider,
                start: false
            )
        )
        {
            var remembered = reopened.SeasonPreferences(item.Id);
            check(
                remembered.Resolution == "720p"
                    && remembered.Language == "English"
                    && remembered.MaxEpisodeBytes == 150
                    && remembered.AutoDownload,
                "Show preferences persist after reopening and cannot be mutated through the caller's object"
            );
        }
        var small = await m.PlanSeason(item.Id, new());
        check(
            small.Entries.Count > 0
                && small
                    .Episodes.Where(e => e.Chosen != null)
                    .All(e =>
                        (e.Chosen!.Resolution == "720p" || e.Reason.Contains("preferred quality unavailable"))
                        && e.Alternatives.All(o => o.File.Size <= 150)
                    ),
            "Per-show quality and per-episode size limits apply to pack files and alternatives, with explicit quality fallback"
        );
        m.SaveSeasonPreferences(item.Id, new() { Resolution = "1080p", Language = "Arabic" });
        var arabic = await m.PlanSeason(item.Id, new());
        check(
            arabic.Entries.Count == 0 && arabic.MissingEpisodes.Count == 8,
            "Audio preferences reject unconfirmed and other-language options"
        );
        m.SaveSeasonPreferences(item.Id, new() { Resolution = "1080p", Language = "English" });
        var swap = third.Alternatives.Single(o => o.Release == "1080p single E03");
        bool forged = false;
        try
        {
            m.EnqueuePlan(
                new()
                {
                    PlanId = plan.Id,
                    RequestId = Guid.NewGuid().ToString(),
                    FileIds = ["not-in-this-plan"],
                }
            );
        }
        catch (IOException)
        {
            forged = true;
        }
        bool duplicate = false;
        try
        {
            m.EnqueuePlan(
                new()
                {
                    PlanId = plan.Id,
                    RequestId = Guid.NewGuid().ToString(),
                    FileIds = [swap.File.Id, third.Chosen!.File.Id],
                }
            );
        }
        catch (IOException)
        {
            duplicate = true;
        }
        check(
            forged && duplicate && m.Jobs().Count == 0,
            "Confirmation rejects forged alternatives and duplicate episode options atomically"
        );
        fixture.Free = 1;
        bool full = false;
        try
        {
            m.EnqueuePlan(
                new()
                {
                    PlanId = plan.Id,
                    RequestId = Guid.NewGuid().ToString(),
                    FileIds = [swap.File.Id],
                }
            );
        }
        catch (IOException e)
        {
            full = e.Message.Contains("free space");
        }
        var noSpace = await m.PlanSeason(
            item.Id,
            new() { Scope = "Specific episodes", SpecificEpisodes = [new(1, 3)] }
        );
        check(
            full && !noSpace.Fits && noSpace.FreeBytes == 1 && m.Jobs().Count == 0,
            "Plan reports the chosen folder's free space and enqueue rechecks capacity after disk changes"
        );
        fixture.Free = long.MaxValue / 4;
        var command = new SeasonDownloadConfirmation
        {
            PlanId = plan.Id,
            RequestId = Guid.NewGuid().ToString(),
            FileIds = [swap.File.Id, seventh.Chosen!.File.Id],
        };
        var receipt = m.EnqueuePlan(command);
        check(
            receipt.Count == 2
                && m.EnqueuePlan(command).SequenceEqual(receipt)
                && m.Jobs().Any(j => j.Target.Episode == 3 && j.Hash == "single-3")
                && m.Jobs().All(j => j.Target.Episode is 3 or 7),
            "One confirmation swaps an episode, excludes unchecked episodes and enqueues atomically and idempotently"
        );
        var queued = await m.PlanSeason(item.Id, new());
        check(
            queued.QueuedEpisodes == 2 && queued.Entries.All(e => e.File.Episode is not (3 or 7)),
            "Replanning skips episodes already queued through another plan"
        );
        var changing = await m.PlanSeason(item.Id, new() { Scope = "Unwatched only" });
        var fifth = changing.Episodes.Single(e => e.Episode == 5);
        fixture.Store.Mark(fixture.Store.EpisodesFor(item.GroupKey).Single(e => e.Season == 1 && e.Episode == 5), true);
        bool watched = false;
        try
        {
            m.EnqueuePlan(
                new()
                {
                    PlanId = changing.Id,
                    RequestId = Guid.NewGuid().ToString(),
                    FileIds = [fifth.Chosen!.File.Id],
                }
            );
        }
        catch (IOException)
        {
            watched = true;
        }
        check(watched, "Unwatched confirmation rechecks viewing history changed after planning");
        using var automatic = new Fixture(parent);
        var auto = automatic.Manager;
        var autoEpisodes = automatic.Store.EpisodesFor(automatic.Item.GroupKey);
        var readyItem = autoEpisodes.Single(e => e.Season == 1 && e.Episode == 7);
        var fetching = autoEpisodes.Single(e => e.Season == 2 && e.Episode == 1);
        var server = autoEpisodes.Single(e => e.Season == 2 && e.Episode == 2);
        var unknown = autoEpisodes.Single(e => e.Season == 2 && e.Episode == 3);
        check(
            !await auto.TryAutoDownload(readyItem.Id) && automatic.Provider.SingleProbes.Count == 0,
            "Auto-download is opt-in and disabled shows perform no discovery"
        );
        auto.SaveSeasonPreferences(
            automatic.Item.Id,
            new()
            {
                Resolution = "1080p",
                Language = "English",
                AutoDownload = true,
            }
        );
        check(
            !await auto.TryAutoDownload(fetching.Id)
                && !await auto.TryAutoDownload(unknown.Id)
                && !await auto.TryAutoDownload(autoEpisodes.Single(e => e.Season == 1 && e.Episode == 10).Id),
            "Auto-download leaves fetching, unknown-size and unaired episodes for the normal alert"
        );
        check(
            await auto.TryAutoDownload(readyItem.Id)
                && await auto.TryAutoDownload(server.Id)
                && !await auto.TryAutoDownload(readyItem.Id)
                && auto.Jobs().Count == 2,
            "Auto-download queues ready and on-server options exactly once"
        );
        automatic.Free = 1;
        check(
            !await auto.TryAutoDownload(autoEpisodes.Single(e => e.Season == 1 && e.Episode == 6).Id),
            "Automatic downloads fall back to an alert when capacity is insufficient"
        );
        fixture.Provider.FailFiles = true;
        var partial = await m.PlanSeason(item.Id, new());
        check(
            partial.Sources.Any(s => s.State == "Partial") && partial.MissingEpisodes.Count > 0,
            "Unavailable file lists are visible as partial sources with uncovered episodes"
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool cancelled = false;
        try
        {
            await m.PlanSeason(item.Id, new(), ct: cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        check(cancelled, "Cancelled discovery cannot produce an enqueueable partial plan");
        using var large = new Fixture(parent);
        large.Provider.Large = true;
        large.Store.State.EpisodeSchedules[large.Item.GroupKey] = Enumerable
            .Range(1, 215)
            .Select(n => new KnownEpisode
            {
                Season = 1,
                Episode = n,
                AirsAt = DateTimeOffset.UtcNow.AddDays(-1),
            })
            .ToList();
        var largeReleases = await large.Manager.SearchDetailed(large.Item.Id, true);
        var largePlan = await large.Manager.PlanSeason(
            large.Item.Id,
            new()
            {
                Scope = "Several seasons / whole show",
                ReleaseIds = largeReleases.Choices.Select(c => c.Id).ToList(),
            }
        );
        var largeIds = largePlan.Entries.Select(e => e.File.Id).ToList();
        check(
            largeIds.Count == 214
                && large
                    .Manager.EnqueuePlan(
                        new()
                        {
                            PlanId = largePlan.Id,
                            RequestId = Guid.NewGuid().ToString(),
                            FileIds = largeIds,
                        }
                    )
                    .Count == 214,
            "A whole-show plan over 200 episodes confirms in one atomic action while the legacy enqueue limit stays compatible"
        );
    }

    sealed class Fixture : IDisposable
    {
        public LibraryStore Store;
        public PcDownloads Manager;
        public FixtureProvider Provider = new();
        public VideoItem Item;
        public long Free = long.MaxValue / 4;

        public Fixture(string parent)
        {
            var dir = Path.Combine(parent, "planner-" + Guid.NewGuid().ToString("N"));
            var media = Path.Combine(dir, "media");
            Directory.CreateDirectory(media);
            Store = new(Path.Combine(dir, "profile"));
            Store.State.Roots = [new() { Path = media }];
            Store.State.Catalog.Clear();
            var path = Path.Combine(media, "Planner.S01E01.mkv");
            File.WriteAllBytes(path, [1]);
            Item = new()
            {
                Id = "planner",
                CollectionKey = "planner",
                Title = "Planner",
                ImdbId = "tt123",
                Season = 1,
                Episode = 1,
                Path = path,
                Available = true,
            };
            Store.State.Catalog[Item.Id] = new() { Item = Item };
            Store.State.EpisodeSchedules[Item.GroupKey] = Enumerable
                .Range(1, 10)
                .Select(n => new KnownEpisode
                {
                    Season = 1,
                    Episode = n,
                    Title = "Episode " + n,
                    AirsAt = DateTimeOffset.UtcNow.AddDays(n == 10 ? 1 : -1),
                })
                .Concat(
                    Enumerable
                        .Range(1, 3)
                        .Select(n => new KnownEpisode
                        {
                            Season = 2,
                            Episode = n,
                            AirsAt = DateTimeOffset.UtcNow.AddDays(-1),
                        })
                )
                .ToList();
            foreach (var episode in Store.EpisodesFor(Item.GroupKey).Where(e => e.Season == 1 && e.Episode is 2 or 4))
                Store.Mark(episode, true);
            Store.Save();
            Manager = new(Store, Provider, start: false, freeSpace: _ => Free);
            Manager.Configure("https://fixture.invalid/manifest.json", "fixture", media, "", subtitleLanguages: []);
            Manager.SaveReleaseFilters("All", "English", false, false);
        }

        public void Dispose() => Manager.Dispose();
    }

    sealed class FixtureProvider : IDownloadProvider
    {
        public bool Reverse,
            FailFiles,
            Large;
        public HashSet<(int, int)> SingleProbes = [];

        static IndexedRelease Release(
            string hash,
            string name,
            string resolution = "1080p",
            bool ready = true,
            string cache = "Ready"
        ) =>
            new(
                new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = name,
                    Resolution = resolution,
                    Languages = "English",
                    Cached = ready,
                    CacheStatus = cache,
                    Sources = [cache == "On your server" ? "Jellyfin" : "TorBox library"],
                },
                hash
            );

        public Task<List<IndexedRelease>> SearchSeason(VideoItem item, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var list =
                item.Season == 1
                    ? new List<IndexedRelease>
                    {
                        Release("pack", "1080p season pack"),
                        Release("low", "720p season pack", "720p"),
                    }
                    : new List<IndexedRelease>();
            if (Reverse)
                list.Reverse();
            return Task.FromResult(list);
        }

        public Task<List<IndexedRelease>> Search(VideoItem item, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SingleProbes.Add((item.Season, item.Episode));
            List<IndexedRelease> list = [];
            if (item.Season == 1 && item.Episode is 3 or 7)
                list.Add(Release("single-" + item.Episode, $"1080p single E{item.Episode:00}"));
            if (item.Season == 2)
                list.Add(
                    Release(
                        "s2-" + item.Episode,
                        "Season 2 episode " + item.Episode,
                        ready: item.Episode != 1,
                        cache: item.Episode == 1 ? "Not cached"
                            : item.Episode == 2 ? "On your server"
                            : "Ready"
                    )
                );
            return Task.FromResult(list);
        }

        public Task<List<TorboxFile>> Files(string hash, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (FailFiles)
                throw new IOException("Fixture unavailable");
            if (hash is "pack" or "low")
                return Task.FromResult(
                    (Large ? Enumerable.Range(1, 215) : new[] { 1, 2, 3, 4, 5, 6, 8, 10 })
                        .Select(n => new TorboxFile(n, $"Planner.S01E{n:00}.mkv", hash == "low" ? 100 : 200))
                        .ToList()
                );
            var n = int.Parse(hash.Split('-')[1]);
            return Task.FromResult(
                new List<TorboxFile>
                {
                    new(n, $"Planner.S{(hash.StartsWith("s2") ? 2 : 1):00}E{n:00}.mkv", hash == "s2-3" ? 0 : 100),
                }
            );
        }

        public Task<TorboxTorrent> Prepare(string h, long id, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string> Link(long id, long file, CancellationToken ct) => throw new NotSupportedException();
    }
}
