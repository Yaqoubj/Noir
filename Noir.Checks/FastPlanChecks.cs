using System.Net;
using System.Text;
using Noir;

// Season planning with AIOStreams: one search per episode, packs recognized across episodes, and
// no torrent file-list calls (AIOStreams already names each episode's file inside every release).
static class FastPlanChecks
{
    sealed class Fake : HttpMessageHandler
    {
        public int Streams,
            FileLists,
            Concurrent,
            Peak;
        public bool Direct,
            Library;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "server.invalid")
                return Reply(
                    path.Contains("/Shows/")
                        ? """{"Items":[{"Id":"e6","ParentIndexNumber":1,"IndexNumber":6,"MediaSources":[{"Path":"Show.S01E06.1080p.mkv","Size":2000}]}]}"""
                        : """{"Items":[{"Id":"show","Type":"Series","Name":"Show","ProviderIds":{"Imdb":"tt1234567"}}]}"""
                );
            if (path.Contains("/stream/series/"))
            {
                Interlocked.Increment(ref Streams);
                var now = Interlocked.Increment(ref Concurrent);
                lock (this)
                    Peak = Math.Max(Peak, now);
                await Task.Delay(40, ct);
                Interlocked.Decrement(ref Concurrent);
                var e = int.Parse(
                    System
                        .Text.RegularExpressions.Regex.Match(Uri.UnescapeDataString(path), @":(\d+)(?:\.json)?$")
                        .Groups[1]
                        .Value
                );
                if (Library && e == 6)
                    return Reply("""{"streams":[]}""");
                if (Direct)
                    return Reply(
                        e == 6
                            ? """{"streams":[]}"""
                            : """{"streams":[{"name":"1080p","url":"https://example.invalid/video"}]}"""
                    );
                var pack = new string('a', 40);
                var single = new string('b', 39) + e;
                var json =
                    $$$"""{"streams":[{"name":"Pack","description":"[NOIR_HASH:{{{pack}}}] [NOIR_AUDIO:English]","url":"https://example.invalid/p","behaviorHints":{"filename":"Show.S01E0{{{e}}}.1080p.WEB.mkv","videoSize":{{{1000 + e}}}}},{"name":"Single","description":"[NOIR_HASH:{{{single}}}] [NOIR_AUDIO:English]","url":"https://example.invalid/s","behaviorHints":{"filename":"Show.S01E0{{{e}}}.720p.HDTV.mkv","videoSize":{{{500 + e}}}}}]}""";
                return Reply(json);
            }
            if (request.RequestUri.Query.Contains("list_files=true") || path.Contains("torrentinfo"))
                Interlocked.Increment(ref FileLists);
            if (Library && path.EndsWith("/mylist"))
                return Reply(
                    System.Text.Json.JsonSerializer.Serialize(
                        new
                        {
                            success = true,
                            data = new[]
                            {
                                new
                                {
                                    id = 1,
                                    hash = new string('a', 40),
                                    name = "Show S01",
                                    download_present = true,
                                    files = Enumerable
                                        .Range(1, 6)
                                        .Select(e => new
                                        {
                                            id = e,
                                            name = $"Show.S01E0{e}.1080p.WEB.mkv",
                                            size = 1000 + e,
                                        }),
                                },
                            },
                        }
                    )
                );
            return Reply("""{"success":true,"data":[]}""");
        }

        static HttpResponseMessage Reply(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    public static async Task Run(string root, Action<bool, string> check)
    {
        var store = new LibraryStore(Path.Combine(root, "fastplan-" + Guid.NewGuid().ToString("N")));
        var shows = Path.Combine(store.DataDirectory, "shows");
        Directory.CreateDirectory(shows);
        store.State.Roots = [new() { Path = shows, Movies = false }];
        var item = new VideoItem
        {
            Id = "fast-1",
            CollectionKey = "show:FAST:2020",
            Title = "Show",
            Season = 1,
            Episode = 1,
            ImdbId = "tt1234567",
        };
        store.State.Catalog[item.Id] = new() { Item = item };
        store.State.EpisodeSchedules[item.GroupKey] = Enumerable
            .Range(1, 6)
            .Select(i => new KnownEpisode
            {
                Season = 1,
                Episode = i,
                AirsAt = DateTimeOffset.UtcNow.AddDays(-30 + i),
            })
            .ToList();
        store.Save();
        var fake = new Fake();
        using var http = new HttpClient(fake);
        PcDownloads? manager = null;
        var provider = new TorboxProvider(() => manager!.Config, http);
        manager = new PcDownloads(store, provider, start: false, freeSpace: _ => 100L * 1024 * 1024 * 1024);
        manager.Configure(
            "https://aio.example.invalid/abc/manifest.json",
            "torbox-key",
            shows,
            shows,
            service: "TorBox"
        );
        var steps = new List<string>();
        var plan = await manager.PlanSeason(
            item.Id,
            new()
            {
                Scope = "Several seasons / whole show",
                Seasons = [1],
                Preferences = new() { Resolution = "1080p", Language = "All" },
            },
            new Progress<DownloadSourceStatus>(p =>
            {
                lock (steps)
                    steps.Add(p.Message);
            })
        );
        check(fake.Streams == 6, "A season plan asks AIOStreams once per episode — no extra season probes");
        check(fake.FileLists == 0, "Planning never asks the debrid service for torrent file lists");
        check(fake.Peak > 1 && fake.Peak <= 6, "Episode searches run several at a time, at most six");
        check(
            plan.Entries.Count == 6 && plan.Episodes.All(e => e.Reason.StartsWith("Season pack covers 6")),
            "The same torrent found for every episode is recognized as one season pack"
        );
        check(
            plan.Episodes.All(e => e.Alternatives.Count == 2 && e.Alternatives.Any(a => a.Resolution == "720p")),
            "Each episode keeps the single-episode release as an alternative"
        );
        check(
            plan.Episodes.All(e =>
                e.Chosen!.File.Name.Contains($"S01E0{e.Episode}") && e.Chosen.File.Size == 1000 + e.Episode
            ),
            "Each choice carries the episode's own file name and size for the download to match"
        );
        // Progress<T> reports arrive later on the thread pool; wait (bounded) instead of racing them.
        bool Searched()
        {
            lock (steps)
                return steps.Any(s => s.Contains("of 6 episodes searched"));
        }
        for (int i = 0; i < 100 && !Searched(); i++)
            await Task.Delay(20);
        check(Searched(), "Planning reports how many episodes have been searched");
        var ids = manager.EnqueuePlan(
            new()
            {
                RequestId = Guid.NewGuid().ToString(),
                PlanId = plan.Id,
                FileIds = plan.Entries.Select(e => e.File.Id).ToList(),
            }
        );
        check(
            ids.Count == 6 && manager.Jobs().All(j => j.Hash == new string('a', 40) && j.RemoteName.Contains("1080p")),
            "Confirming the plan queues each episode from the pack by name and size"
        );
        var directStore = new LibraryStore(Path.Combine(root, "directplan-" + Guid.NewGuid().ToString("N")));
        directStore.State.Roots = [new() { Path = shows, Movies = false }];
        directStore.State.Catalog[item.Id] = new() { Item = item };
        directStore.State.EpisodeSchedules[item.GroupKey] = store.State.EpisodeSchedules[item.GroupKey];
        directStore.Save();
        var directFake = new Fake { Direct = true };
        using var directHttp = new HttpClient(directFake);
        PcDownloads? directManager = null;
        var router = new DownloadProviders(() => directManager!.Config, directHttp);
        directManager = new PcDownloads(directStore, router, start: false, freeSpace: _ => 100L * 1024 * 1024 * 1024);
        directManager.Configure(
            "https://aio.example.invalid/abc/manifest.json",
            "",
            shows,
            shows,
            service: "AIOStreams links"
        );
        directManager.AddServer(
            new()
            {
                Id = "server",
                Kind = "Jellyfin",
                BaseUrl = "https://server.invalid",
                UserId = "user",
                Token = "fixture",
            }
        );
        var directPlan = await directManager.PlanSeason(
            item.Id,
            new()
            {
                Scope = "Several seasons / whole show",
                Seasons = [1],
                Preferences = new() { Resolution = "1080p", Language = "All" },
            }
        );
        check(
            directFake.Streams == 6
                && directPlan.Entries.Count == 6
                && directPlan.Episodes.Single(e => e.Episode == 6).Chosen!.CacheState == "On your server",
            "Fast AIOStreams planning fills missing episodes from connected servers without repeating manifest searches"
        );
        check(
            directPlan.Episodes.First().Chosen!.File.Name == "S01E01.mkv",
            "Direct streams without filename metadata use the normalized video filename in season plans"
        );
        var directIds = directManager.EnqueuePlan(
            new()
            {
                RequestId = Guid.NewGuid().ToString(),
                PlanId = directPlan.Id,
                FileIds = directPlan.Entries.Select(e => e.File.Id).ToList(),
            }
        );
        check(
            directIds.Count == 6 && directManager.Jobs().Any(j => j.RemoteName == "S01E01.mkv"),
            "Confirming normalized direct stream filenames queues video downloads successfully"
        );
        var libraryStore = new LibraryStore(Path.Combine(root, "libraryplan-" + Guid.NewGuid().ToString("N")));
        libraryStore.State.Roots = [new() { Path = shows, Movies = false }];
        libraryStore.State.Catalog[item.Id] = new() { Item = item };
        libraryStore.State.EpisodeSchedules[item.GroupKey] = store.State.EpisodeSchedules[item.GroupKey];
        libraryStore.Save();
        var libraryFake = new Fake { Library = true };
        using var libraryHttp = new HttpClient(libraryFake);
        PcDownloads? libraryManager = null;
        var libraryRouter = new DownloadProviders(() => libraryManager!.Config, libraryHttp);
        libraryManager = new PcDownloads(libraryStore, libraryRouter, start: false, freeSpace: _ =>
            100L * 1024 * 1024 * 1024
        );
        libraryManager.Configure(
            "https://aio.example.invalid/abc/manifest.json",
            "fixture-key",
            shows,
            shows,
            service: "TorBox"
        );
        var libraryPlan = await libraryManager.PlanSeason(
            item.Id,
            new()
            {
                Scope = "Several seasons / whole show",
                Seasons = [1],
                Preferences = new() { Resolution = "1080p", Language = "All" },
            }
        );
        check(
            libraryFake.Streams == 6
                && libraryFake.FileLists == 0
                && libraryPlan.Entries.Count == 6
                && libraryPlan.Episodes.Single(e => e.Episode == 6).Chosen!.Source.Contains("TorBox library"),
            "Fast TorBox planning keeps library-only episodes even when the manifest returned the same pack for other episodes"
        );
    }
}
