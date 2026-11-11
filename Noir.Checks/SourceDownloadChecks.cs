using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Noir;

static class SourceDownloadChecks
{
    const string Secret = "private-playback-credential";
    static readonly string Hash = new('d', 40);

    static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };

    static async Task<bool> Refused(Func<Task> action)
    {
        try
        {
            await action();
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public static async Task Run(string parent, Action<bool, string> check)
    {
        await Links(parent, check);
        await Seasons(parent, check);
        await RealDebrid(check);
        await Servers(parent, check);
    }

    static DownloadCredentials LinksConfig() =>
        new()
        {
            Service = "AIOStreams links",
            Manifest = "https://aio.fixture.invalid/private-config/manifest.json",
            PreferredResolution = "1080p",
            MaxEpisodeBytes = 10000,
            BrowseLanguage = "English",
        };

    static LibraryStore Store(string parent, string name, bool movie = false)
    {
        var dir = Path.Combine(parent, name);
        var media = Path.Combine(dir, "media");
        Directory.CreateDirectory(media);
        var store = new LibraryStore(Path.Combine(dir, "profile"));
        store.State.Roots = [new() { Path = media, Movies = movie }];
        store.State.Catalog.Clear();
        var item = new VideoItem
        {
            Id = "target",
            CollectionKey = "source-fixture",
            Title = "Story",
            Year = 2024,
            IsMovie = movie,
            Season = 1,
            Episode = 1,
            ImdbId = "tt123",
            Available = false,
        };
        store.State.Catalog[item.Id] = new() { Item = item };
        store.Save();
        return store;
    }

    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            send(request, ct);
    }

    sealed class AioHandler : HttpMessageHandler
    {
        public int Active,
            Peak,
            Count;
        public List<int> Episodes = [];
        readonly object gate = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "aio.fixture.invalid")
                return Json(new { success = true, data = Array.Empty<object>() });
            var match = Regex.Match(Uri.UnescapeDataString(request.RequestUri.AbsolutePath), @"tt123:1:(\d+)");
            var episode = match.Success ? int.Parse(match.Groups[1].Value) : 0;
            lock (gate)
            {
                Count++;
                Episodes.Add(episode);
                Active++;
                Peak = Math.Max(Peak, Active);
            }
            try
            {
                await Task.Delay(15, ct);
                object Stream(string quality, long size, string language) =>
                    new
                    {
                        name = quality,
                        description = $"[NOIR_SOURCE:Easynews]\n[NOIR_AUDIO:{language}]\nhttps://aio.fixture.invalid/{Secret}/watch",
                        url = $"https://cdn.fixture.invalid/{Secret}/{episode}/{quality}",
                        behaviorHints = new
                        {
                            filename = $"Story.S01E{episode:00}.{quality}.mkv",
                            videoSize = size,
                            proxyHeaders = new
                            {
                                request = new Dictionary<string, string>
                                {
                                    { "Authorization", "Bearer " + Secret },
                                    { "X-Source", "Noir" },
                                },
                            },
                        },
                    };
                return Json(
                    new
                    {
                        streams = new[]
                        {
                            Stream("720p", 4000, "English"),
                            Stream("1080p", 8192, "English"),
                            Stream("1080p", 20000, "English"),
                            Stream("1080p", 1000, "Arabic"),
                        },
                    }
                );
            }
            finally
            {
                lock (gate)
                    Active--;
            }
        }
    }

    sealed class NoCloud : IDownloadProvider
    {
        public Task<List<IndexedRelease>> Search(VideoItem item, CancellationToken ct) =>
            throw new Exception("Unexpected cloud search");

        public Task<List<TorboxFile>> Files(string hash, CancellationToken ct) =>
            throw new Exception("Direct files must stay local");

        public Task<TorboxTorrent> Prepare(string hash, long id, CancellationToken ct) =>
            throw new Exception("Direct jobs must skip Prepare");

        public Task<string> Link(long id, long file, CancellationToken ct) =>
            throw new Exception("Direct jobs must skip Link");
    }

    static async Task Links(string parent, Action<bool, string> check)
    {
        var config = LinksConfig();
        using var http = new HttpClient(new AioHandler());
        var provider = new TorboxProvider(() => config, http);
        var store = Store(parent, "direct-sources");
        var item = store.Archive.Single();
        using var manager = new PcDownloads(store, provider, start: false);
        manager.Configure(
            config.Manifest,
            "",
            store.State.Roots[0].Path,
            "",
            subtitleLanguages: [],
            service: "AIOStreams links"
        );
        var releases = await manager.Search(item.Id, false);
        var files = await manager.Files(releases[1].Id);
        check(
            manager.Configured
                && releases.Count == 4
                && releases.All(r => r.Sources.Contains("Easynews (AIOStreams links)"))
                && files.Single().Selectable,
            "AIOStreams URL-only releases work without a debrid key and expose normal selectable files"
        );
        manager.Enqueue(
            new()
            {
                RequestId = Guid.NewGuid().ToString(),
                ReleaseId = releases[1].Id,
                FileIds = [files.Single().Id],
            }
        );
        var job = manager.Jobs().Single();
        manager.Control(job.Id, "pause");
        var protectedSource = DownloadSecurity.Unprotect(job.ProtectedDirectSource);
        var persisted = File.ReadAllText(Path.Combine(store.DataDirectory, "pc-downloads.json"));
        check(
            protectedSource.Url.Contains(Secret)
                && protectedSource.Headers["Authorization"] == "Bearer " + Secret
                && protectedSource.Episode == 1,
            "Direct sources round-trip URL, headers and episode identity through CurrentUser DPAPI"
        );
        var visible =
            JsonSerializer.Serialize(releases)
            + JsonSerializer.Serialize(files)
            + JsonSerializer.Serialize(manager.PublicJobs())
            + persisted;
        check(
            !visible.Contains(Secret)
                && !visible.Contains("https://")
                && !visible.Contains("private-config")
                && !visible.Contains("Authorization"),
            "Direct URLs and credential headers never enter choices, phone jobs or plain-text queue JSON"
        );
        config.Service = "TorBox";
        config.Key = "fixture-key";
        var fallback = await provider.Search(item, CancellationToken.None);
        check(
            fallback.Count == 4 && fallback.All(r => r.Direct?.Count == 1),
            "TorBox search retains AIOStreams links that have no convertible torrent hash"
        );
        var bytes = Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)).ToArray();
        long range = -1;
        bool headers = false;
        using var downloadHttp = new HttpClient(
            new Handler(
                (request, ct) =>
                {
                    range = request.Headers.Range?.Ranges.Single().From ?? 0;
                    headers =
                        request.Headers.GetValues("Authorization").Single() == "Bearer " + Secret
                        && request.Headers.GetValues("X-Source").Single() == "Noir"
                        && request.Headers.GetValues("If-Range").Single() == "\"direct\"";
                    var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new ByteArrayContent(bytes[(int)range..]),
                    };
                    response.Headers.ETag = new EntityTagHeaderValue("\"direct\"");
                    response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                        range,
                        bytes.Length - 1,
                        bytes.Length
                    );
                    return Task.FromResult(response);
                }
            )
        );
        var saved = manager.Jobs();
        saved[0].ETag = "\"direct\"";
        File.WriteAllBytes(Path.Combine(store.DataDirectory, "download-parts", job.Id + ".part"), bytes[..777]);
        File.WriteAllText(Path.Combine(store.DataDirectory, "pc-downloads.json"), JsonSerializer.Serialize(saved));
        using (var restarted = new PcDownloads(store, new NoCloud(), downloadHttp))
        {
            restarted.Control(job.Id, "resume");
            for (int i = 0; i < 200 && restarted.Jobs().Single().Status is not ("Completed" or "Failed"); i++)
                await Task.Delay(25);
            check(
                restarted.Jobs().Single().Status == "Completed"
                    && range == 777
                    && headers
                    && File.ReadAllBytes(job.Destination).SequenceEqual(bytes),
                "Restarted direct jobs skip cloud preparation and resume with Range, If-Range and source headers"
            );
        }
        var second = new PcDownloadJob
        {
            Root = store.State.Roots[0].Path,
            Target = new()
            {
                Id = "second",
                Title = "Story",
                Year = 2024,
                Season = 1,
                Episode = 2,
                CollectionKey = item.GroupKey,
            },
            Size = bytes.Length,
            ETag = "\"direct\"",
        };
        second.Destination = DownloadNaming.Destination(second.Root, second.Target, "video.mkv");
        File.WriteAllBytes(Path.Combine(store.DataDirectory, "download-parts", second.Id + ".part"), bytes[..777]);
        using (
            var ignoresRange = new PcDownloads(
                store,
                new NoCloud(),
                new HttpClient(
                    new Handler(
                        (request, ct) =>
                            Task.FromResult(
                                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                            )
                    )
                ),
                start: false
            )
        )
            await ignoresRange.Transfer(second, "https://cdn.fixture.invalid/file", CancellationToken.None);
        check(
            File.ReadAllBytes(second.Destination).SequenceEqual(bytes),
            "A direct server that ignores Range restarts the partial file from zero"
        );
        check(
            !DownloadSecurity.ValidUrl("http://cdn.fixture.invalid/file")
                && !DownloadSecurity.ValidUrl("https://user:password@cdn.fixture.invalid/file")
                && DownloadSecurity.ValidUrl("http://lan.fixture.invalid:8096/video", "http://lan.fixture.invalid:8096")
                && !DownloadSecurity.ValidUrl("http://other.fixture.invalid/video", "http://lan.fixture.invalid:8096"),
            "Download URL validation permits HTTPS and only the configured media-server HTTP origin, without userinfo"
        );
        var redirected = 0;
        using var redirect = new PcDownloads(
            store,
            new NoCloud(),
            new HttpClient(
                new Handler(
                    (request, ct) =>
                    {
                        redirected++;
                        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                        response.Headers.Location = new Uri("http://unexpected.fixture.invalid/private");
                        return Task.FromResult(response);
                    }
                )
            ),
            start: false
        );
        var invalid = new PcDownloadJob
        {
            Root = second.Root,
            Destination = Path.Combine(second.Root, "redirect.mkv"),
            Size = 8192,
        };
        check(
            await Refused(() => redirect.Transfer(invalid, "https://cdn.fixture.invalid/start", CancellationToken.None))
                && redirected == 1,
            "Direct downloads reject insecure redirects before requesting the new address"
        );
        var forwarded = false;
        var hops = 0;
        using var safeRedirect = new PcDownloads(
            store,
            new NoCloud(),
            new HttpClient(
                new Handler(
                    (request, ct) =>
                    {
                        if (++hops == 1)
                        {
                            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                            response.Headers.Location = new Uri("https://other-cdn.fixture.invalid/file");
                            return Task.FromResult(response);
                        }
                        forwarded = request.Headers.Contains("Authorization");
                        var final = new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new ByteArrayContent(bytes),
                        };
                        final.Headers.ETag = new EntityTagHeaderValue("\"" + Secret + "\"");
                        return Task.FromResult(final);
                    }
                )
            ),
            start: false
        );
        var redirectedJob = new PcDownloadJob
        {
            Root = second.Root,
            Destination = Path.Combine(second.Root, "safe-redirect.mkv"),
            Size = 8192,
            Target = new()
            {
                Id = "redirect",
                Title = "Redirect",
                IsMovie = true,
            },
        };
        await safeRedirect.Transfer(
            redirectedJob,
            "https://cdn.fixture.invalid/start",
            CancellationToken.None,
            protectedSource.Headers
        );
        check(
            hops == 2 && !forwarded && redirectedJob.ETag.Length == 0,
            "Cross-origin redirects drop credential headers and response validators cannot persist a source token"
        );
        using var hostileHttp = new HttpClient(
            new Handler(
                (request, ct) =>
                    Task.FromResult(
                        Json(
                            new
                            {
                                streams = new[]
                                {
                                    new
                                    {
                                        name = "1080p " + Secret,
                                        description = "[NOIR_AUDIO:English] " + Secret,
                                        url = "https://cdn.fixture.invalid/" + Secret,
                                        behaviorHints = new
                                        {
                                            filename = "Story." + Secret + ".mkv",
                                            proxyHeaders = new
                                            {
                                                request = new Dictionary<string, string>
                                                {
                                                    { "Authorization", "Bearer " + Secret },
                                                },
                                            },
                                        },
                                    },
                                },
                            }
                        )
                    )
            )
        );
        var hostile = new TorboxProvider(LinksConfig, hostileHttp);
        var redacted = await hostile.Search(item, CancellationToken.None);
        check(
            !JsonSerializer.Serialize(redacted.Select(r => r.Choice)).Contains(Secret)
                && !redacted.Single().Direct!.Single().Name.Contains(Secret),
            "AIOStreams metadata cannot echo credential-header tokens into public names, details or file choices"
        );
    }

    static async Task Seasons(string parent, Action<bool, string> check)
    {
        var store = Store(parent, "direct-seasons");
        var item = store.Archive.Single();
        var config = LinksConfig();
        var handler = new AioHandler();
        using var http = new HttpClient(handler);
        var provider = new TorboxProvider(() => config, http);
        store.State.EpisodeSchedules[item.GroupKey] = Enumerable
            .Range(1, 8)
            .Select(i => new KnownEpisode
            {
                Season = 1,
                Episode = i,
                AirsAt = i == 8 ? DateTimeOffset.UtcNow.AddDays(1) : DateTimeOffset.UtcNow.AddDays(-1),
            })
            .ToList();
        item.Path = Path.Combine(store.State.Roots[0].Path, "Story.S01E01.mkv");
        File.WriteAllBytes(item.Path, [1]);
        item.Available = true;
        store.Save();
        using var manager = new PcDownloads(store, provider, start: false);
        manager.Configure(
            config.Manifest,
            "",
            store.State.Roots[0].Path,
            "",
            subtitleLanguages: [],
            service: "AIOStreams links"
        );
        var ep2 = store.EpisodesFor(item.GroupKey).Single(i => i.Episode == 2);
        var queued = await manager.Search(ep2.Id, false);
        var queuedFiles = await manager.Files(queued[0].Id);
        manager.Enqueue(
            new()
            {
                RequestId = Guid.NewGuid().ToString(),
                ReleaseId = queued[0].Id,
                FileIds = [queuedFiles[0].Id],
            }
        );
        handler.Episodes.Clear();
        handler.Count = 0;
        var season = await manager.SearchDetailed(item.Id, true);
        var files = await manager.Files(season.Choices.Single().Id);
        var plan = await manager.Plan(item.Id, [season.Choices.Single().Id]);
        check(
            season.Choices.Single().Name == "Best link per episode"
                && files.Select(f => f.Episode).SequenceEqual(new[] { 3, 4, 5, 6, 7 })
                && files.All(f => f.Size == 8192 && f.Name.Contains("1080p"))
                && plan.Entries.Count == 5,
            "Best link per episode honors quality, language and size preferences while skipping local, queued and future episodes"
        );
        check(
            handler.Peak is > 0 and <= 3
                && handler.Count == 5
                && handler.Episodes.Order().SequenceEqual(new[] { 3, 4, 5, 6, 7 }),
            "AIOStreams season lookup uses at most three concurrent requests for only missing released episodes"
        );
        handler.Count = 0;
        var many = Enumerable
            .Range(1, 60)
            .Select(n => new VideoItem
            {
                ImdbId = "tt123",
                Season = 1,
                Episode = n,
            })
            .ToList();
        var capped = await provider.Discover(item, true, many, null, CancellationToken.None);
        check(
            handler.Count == 40 && capped.Releases.Single().Direct!.Count == 40,
            "AIOStreams synthetic seasons cap per-episode lookups at 40"
        );
        var custom = await manager.PlanSeason(
            item.Id,
            new()
            {
                Preferences = new()
                {
                    Resolution = "720p",
                    Language = "English",
                    MaxEpisodeBytes = 5000,
                },
            }
        );
        check(
            custom.Entries.Count == 5
                && custom
                    .Episodes.Where(e => e.Chosen != null)
                    .All(e =>
                        e.Chosen!.Resolution == "720p" && e.Chosen.Language == "English" && e.Chosen.File.Size == 4000
                    ),
            "Shared planner retains AIOStreams alternatives and applies show preferences independently of global synthetic-pack defaults"
        );
    }

    static async Task RealDebrid(Action<bool, string> check)
    {
        var config = new DownloadCredentials
        {
            Service = "Real-Debrid",
            Key = "rd-fixture-token",
            Manifest = "https://aio.fixture.invalid/private/manifest.json",
        };
        var calls = new List<string>();
        var added = false;
        var selected = false;
        var polls = 0;
        object Info() =>
            new
            {
                hash = Hash,
                status = selected
                    ? ++polls > 1
                        ? "downloaded"
                        : "downloading"
                    : "waiting_files_selection",
                files = new[]
                {
                    new
                    {
                        id = 41,
                        path = "/Story.S01E01.mkv",
                        bytes = 8192,
                        selected = selected ? 1 : 0,
                    },
                    new
                    {
                        id = 42,
                        path = "/Story.S01E02.mkv",
                        bytes = 8192,
                        selected = selected ? 1 : 0,
                    },
                },
                links = new[]
                {
                    "https://restricted.fixture.invalid/first",
                    "https://restricted.fixture.invalid/second",
                },
            };
        using var http = new HttpClient(
            new Handler(
                async (request, ct) =>
                {
                    var path = request.RequestUri!.AbsolutePath;
                    calls.Add(path);
                    if (request.RequestUri.Host == "aio.fixture.invalid")
                        return Json(new { streams = new[] { new { name = "Story 1080p", infoHash = Hash } } });
                    if (request.Headers.Authorization?.ToString() != "Bearer rd-fixture-token")
                        throw new Exception("Missing RD Bearer token");
                    if (path.EndsWith("/torrents"))
                        return Json(
                            added
                                ? new object[]
                                {
                                    new
                                    {
                                        id = "RD-STRING-ID",
                                        hash = Hash,
                                        filename = "Story.2024.S01",
                                        status = selected && polls > 1 ? "downloaded" : "downloading",
                                    },
                                }
                                : Array.Empty<object>()
                        );
                    if (path.EndsWith("/addMagnet"))
                    {
                        added = true;
                        var form = await request.Content!.ReadAsStringAsync(ct);
                        if (!Uri.UnescapeDataString(form).Contains("magnet:?xt=urn:btih:" + Hash))
                            throw new Exception("Wrong magnet");
                        return Json(new { id = "RD-STRING-ID" });
                    }
                    if (path.Contains("/info/"))
                        return Json(Info());
                    if (path.Contains("/selectFiles/"))
                    {
                        selected = await request.Content!.ReadAsStringAsync(ct) == "files=all";
                        return new(HttpStatusCode.NoContent);
                    }
                    if (path.EndsWith("/unrestrict/link"))
                    {
                        var form = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync(ct));
                        if (!form.Contains("second"))
                            throw new Exception("Wrong selected-file link");
                        return Json(
                            new
                            {
                                download = "https://cdn.fixture.invalid/rd-file",
                                filesize = 8192,
                                filename = "Story.S01E02.mkv",
                            }
                        );
                    }
                    throw new Exception("Unexpected RD path");
                }
            )
        );
        var provider = new RealDebridProvider(() => config, http);
        var item = new VideoItem
        {
            Title = "Story",
            Year = 2024,
            ImdbId = "tt123",
            Season = 1,
            Episode = 1,
        };
        var discovery = await provider.Discover(item, false, [item], null, CancellationToken.None);
        check(
            discovery.Releases.Single().Choice.CacheStatus == "Unknown"
                && discovery.Releases.Single().Choice.Detail.Contains("may add"),
            "Real-Debrid hash releases have Unknown cache status and explain magnet metadata acquisition"
        );
        var files = await provider.Files(Hash, CancellationToken.None);
        var preparing = await provider.Prepare(Hash, -1, CancellationToken.None);
        var ready = await provider.Prepare(Hash, preparing.Id, CancellationToken.None);
        var link = await provider.Link(ready.Id, 42, CancellationToken.None);
        check(
            files.Count == 2
                && !preparing.Ready
                && ready.Ready
                && selected
                && link == "https://cdn.fixture.invalid/rd-file"
                && calls.Count(p => p.EndsWith("/addMagnet")) == 1,
            "Real-Debrid adds a magnet once, selects files, polls readiness and unrestricts the correct selected file"
        );
        var restarted = new RealDebridProvider(() => config, http);
        var rehydrated = await restarted.Prepare(Hash, 999999, CancellationToken.None);
        check(
            rehydrated.Ready && calls.Count(p => p.EndsWith("/addMagnet")) == 1,
            "Real-Debrid resolves string torrent IDs by hash after restart and ignores stale numeric handles"
        );
        var library = await provider.Discover(item, true, [item], null, CancellationToken.None);
        check(
            library.Releases.Single().Choice.Sources.Contains("Real-Debrid library")
                && library.Releases.Single().Choice.CacheStatus == "Ready",
            "Downloaded Real-Debrid library hashes merge into index results as ready releases"
        );
        using var partialHttp = new HttpClient(
            new Handler(
                (request, ct) =>
                {
                    var path = request.RequestUri!.AbsolutePath;
                    if (path.EndsWith("/torrents"))
                        return Task.FromResult(Json(new[] { new { id = "partial", hash = Hash } }));
                    if (path.Contains("/info/"))
                        return Task.FromResult(
                            Json(
                                new
                                {
                                    hash = Hash,
                                    status = "downloaded",
                                    files = new[]
                                    {
                                        new
                                        {
                                            id = 41,
                                            path = "/Story.S01E01.mkv",
                                            bytes = 8192,
                                            selected = 0,
                                        },
                                        new
                                        {
                                            id = 42,
                                            path = "/Story.S01E02.mkv",
                                            bytes = 8192,
                                            selected = 1,
                                        },
                                    },
                                    links = new[] { "https://restricted.fixture.invalid/selected" },
                                }
                            )
                        );
                    if (path.EndsWith("/unrestrict/link"))
                        return Task.FromResult(Json(new { download = "https://cdn.fixture.invalid/selected" }));
                    throw new Exception("An active torrent must not be reselected");
                }
            )
        );
        var partial = new RealDebridProvider(() => config, partialHttp);
        var partialFiles = await partial.Files(Hash, CancellationToken.None);
        var partialReady = await partial.Prepare(Hash, -1, CancellationToken.None);
        var partialLink = await partial.Link(partialReady.Id, 42, CancellationToken.None);
        check(
            partialFiles.Single().Id == 42 && partialReady.Files.Single().Id == 42 && partialLink.EndsWith("/selected"),
            "Existing Real-Debrid torrents retain their selected files and map link order without reselecting active torrents"
        );
        foreach (var (status, word) in new[] { (401, "token"), (403, "premium"), (503, "unavailable"), (429, "Wait") })
        {
            using var badHttp = new HttpClient(
                new Handler(
                    (request, ct) =>
                        Task.FromResult(
                            new HttpResponseMessage((HttpStatusCode)status)
                            {
                                Content = new StringContent("rd-fixture-token https://private.invalid/"),
                            }
                        )
                )
            );
            var bad = new RealDebridProvider(() => config, badHttp);
            var message = "";
            try
            {
                await bad.Files(Hash, CancellationToken.None);
            }
            catch (IOException e)
            {
                message = e.Message;
            }
            check(
                message.Contains(word, StringComparison.OrdinalIgnoreCase)
                    && !message.Contains(config.Key)
                    && !message.Contains("https://"),
                $"Real-Debrid HTTP {status} maps to a friendly redacted error"
            );
        }
    }

    static async Task Servers(string parent, Action<bool, string> check)
    {
        var authenticated = false;
        var episodeCalls = 0;
        var pinPolls = 0;
        var plexHeaders = true;
        object JellyItem(bool movie) =>
            new
            {
                Id = movie ? "movie" : "series",
                Name = "Story",
                Type = movie ? "Movie" : "Series",
                ProductionYear = 2024,
                ProviderIds = new { Imdb = "tt123" },
                MediaSources = new[]
                {
                    new
                    {
                        Size = 8192,
                        Path = "/media/Story.2024.mkv",
                        MediaStreams = new object[]
                        {
                            new { Type = "Video", Height = 1080 },
                            new { Type = "Audio", Language = "eng" },
                            new { Type = "Subtitle", Language = "ara" },
                        },
                    },
                },
            };
        object PlexItem(string type) =>
            new
            {
                ratingKey = "show-key",
                title = "Story",
                type,
                year = 2024,
                Guid = new[] { new { id = "imdb://tt123" } },
                Media = new[]
                {
                    new
                    {
                        height = 2160,
                        Part = new[]
                        {
                            new
                            {
                                key = "/library/parts/movie/file.mkv",
                                file = "/media/Story.2024.mkv",
                                size = 8192,
                                Stream = new[]
                                {
                                    new { streamType = 2, languageCode = "ara" },
                                    new { streamType = 3, languageCode = "eng" },
                                },
                            },
                        },
                    },
                },
            };
        using var http = new HttpClient(
            new Handler(
                async (request, ct) =>
                {
                    var uri = request.RequestUri!;
                    var path = uri.AbsolutePath;
                    if (uri.Host == "plex.tv")
                    {
                        plexHeaders &=
                            request.Headers.GetValues("X-Plex-Product").Single() == "Noir"
                            && request.Headers.GetValues("X-Plex-Client-Identifier").Single() == "stable-device"
                            && request.Headers.Accept.ToString() == "application/json";
                        if (path.EndsWith("/pins"))
                            return Json(new { id = 1234, code = "ABCD" });
                        if (path.EndsWith("/pins/1234"))
                            return Json(new { authToken = ++pinPolls == 1 ? "" : "plex-fixture-token" });
                        if (path.EndsWith("/resources"))
                            return Json(
                                new object[]
                                {
                                    new
                                    {
                                        name = "Living room",
                                        provides = "server,player",
                                        accessToken = "plex-server-token",
                                        connections = new[]
                                        {
                                            new { uri = "http://plex-lan.fixture.invalid:32400", local = true },
                                            new { uri = "https://plex-remote.fixture.invalid", local = false },
                                            new { uri = "https://plex-local.fixture.invalid", local = true },
                                        },
                                    },
                                    new
                                    {
                                        name = "Remote only",
                                        provides = "server",
                                        accessToken = "remote-token",
                                        connections = new[]
                                        {
                                            new { uri = "https://remote.fixture.invalid", local = false },
                                        },
                                    },
                                    new
                                    {
                                        name = "Player",
                                        provides = "player",
                                        accessToken = "player-token",
                                        connections = Array.Empty<object>(),
                                    },
                                    new
                                    {
                                        name = "Local only",
                                        provides = "server",
                                        accessToken = "local-token",
                                        connections = new[]
                                        {
                                            new { uri = "http://only-local.fixture.invalid:32400", local = true },
                                        },
                                    },
                                }
                            );
                    }
                    if (uri.Host == "jelly.fixture.invalid")
                    {
                        if (path.EndsWith("AuthenticateByName"))
                        {
                            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                            authenticated =
                                body.RootElement.GetProperty("Username").GetString() == "fixture-user"
                                && body.RootElement.GetProperty("Pw").GetString() == "never-store-password"
                                && request
                                    .Headers.GetValues("Authorization")
                                    .Single()
                                    .Contains("DeviceId=\"stable-device\"");
                            return Json(new { AccessToken = "jelly-token", User = new { Id = "user-id" } });
                        }
                        if (request.Headers.GetValues("X-Emby-Token").Single() != "jelly-token")
                            throw new Exception("Missing Jellyfin token");
                        if (path.EndsWith("/Items"))
                            return Json(
                                new
                                {
                                    Items = new object[]
                                    {
                                        JellyItem(true),
                                        JellyItem(false),
                                        new
                                        {
                                            Id = "wrong",
                                            Name = "Story",
                                            Type = "Movie",
                                            ProductionYear = 2024,
                                            ProviderIds = new { Imdb = "tt999" },
                                        },
                                        new
                                        {
                                            Id = "legacy",
                                            Name = "Legacy Story",
                                            Type = "Movie",
                                            ProductionYear = 2024,
                                            MediaSources = new[]
                                            {
                                                new { Size = 8192, Path = "/media/Legacy.Story.mkv" },
                                            },
                                        },
                                    },
                                }
                            );
                        if (path.EndsWith("/Episodes"))
                        {
                            episodeCalls++;
                            return Json(
                                new
                                {
                                    Items = new[]
                                    {
                                        new
                                        {
                                            Id = "ep1",
                                            ParentIndexNumber = 1,
                                            IndexNumber = 1,
                                            MediaSources = new[]
                                            {
                                                new { Size = 8192, Path = "/media/Story.S01E01.mkv" },
                                            },
                                        },
                                        new
                                        {
                                            Id = "ep2",
                                            ParentIndexNumber = 1,
                                            IndexNumber = 2,
                                            MediaSources = new[]
                                            {
                                                new { Size = 8192, Path = "/media/Story.S01E02.mkv" },
                                            },
                                        },
                                        new
                                        {
                                            Id = "other",
                                            ParentIndexNumber = 2,
                                            IndexNumber = 1,
                                            MediaSources = new[]
                                            {
                                                new { Size = 8192, Path = "/media/Story.S02E01.mkv" },
                                            },
                                        },
                                    },
                                }
                            );
                        }
                        return Json(new { Id = "user-id" });
                    }
                    if (uri.Host == "plex-local.fixture.invalid")
                    {
                        if (request.Headers.GetValues("X-Plex-Token").Single() != "plex-server-token")
                            throw new Exception("Missing Plex token");
                        if (path.EndsWith("/all"))
                            return Json(
                                new
                                {
                                    MediaContainer = new { Metadata = new[] { PlexItem("movie"), PlexItem("show") } },
                                }
                            );
                        if (path.EndsWith("/search"))
                            return Json(
                                new
                                {
                                    MediaContainer = new
                                    {
                                        Hub = new[] { new { Metadata = new[] { PlexItem("movie") } } },
                                    },
                                }
                            );
                        if (path.EndsWith("/allLeaves"))
                            return Json(
                                new
                                {
                                    MediaContainer = new
                                    {
                                        Metadata = new[]
                                        {
                                            new
                                            {
                                                parentIndex = 1,
                                                index = 1,
                                                Media = new[]
                                                {
                                                    new
                                                    {
                                                        Part = new[]
                                                        {
                                                            new
                                                            {
                                                                key = "/library/parts/1/file.mkv",
                                                                file = "/media/Story.S01E01.mkv",
                                                                size = 8192,
                                                            },
                                                        },
                                                    },
                                                },
                                            },
                                            new
                                            {
                                                parentIndex = 1,
                                                index = 2,
                                                Media = new[]
                                                {
                                                    new
                                                    {
                                                        Part = new[]
                                                        {
                                                            new
                                                            {
                                                                key = "/library/parts/2/file.mkv",
                                                                file = "/media/Story.S01E02.mkv",
                                                                size = 8192,
                                                            },
                                                        },
                                                    },
                                                },
                                            },
                                            new
                                            {
                                                parentIndex = 2,
                                                index = 1,
                                                Media = new[]
                                                {
                                                    new
                                                    {
                                                        Part = new[]
                                                        {
                                                            new
                                                            {
                                                                key = "/library/parts/3/file.mkv",
                                                                file = "/media/Story.S02E01.mkv",
                                                                size = 8192,
                                                            },
                                                        },
                                                    },
                                                },
                                            },
                                        },
                                    },
                                }
                            );
                        return Json(new { MediaContainer = new { } });
                    }
                    if (uri.Host == "api.torbox.app")
                        return Json(new { success = true, data = Array.Empty<object>() });
                    throw new Exception("Unexpected media server fixture request");
                }
            )
        );
        var api = new MediaServers(http);
        var jelly = await api.Authenticate(
            "http://jelly.fixture.invalid:8096",
            "fixture-user",
            "never-store-password",
            "stable-device",
            "Home",
            "Jellyfin",
            CancellationToken.None
        );
        var movie = new VideoItem
        {
            Title = "Story",
            Year = 2024,
            ImdbId = "tt123",
            IsMovie = true,
        };
        var episode = new VideoItem
        {
            Title = "Story",
            Year = 2024,
            ImdbId = "tt123",
            Season = 1,
            Episode = 2,
        };
        var movies = await api.Search(jelly, movie, false, CancellationToken.None);
        var episodes = await api.Search(jelly, episode, false, CancellationToken.None);
        var season = await api.Search(jelly, episode, true, CancellationToken.None);
        check(
            authenticated
                && jelly.UserId == "user-id"
                && movies.Count == 1
                && movies.Single().Direct!.Single().Url == "http://jelly.fixture.invalid:8096/Items/movie/Download"
                && movies.Single().Direct!.Single().Headers["X-Emby-Token"] == "jelly-token",
            "Jellyfin authenticates with a stable device, matches IMDb and builds token-header download sources"
        );
        check(
            episodes.Single().Direct!.Single().Episode == 2
                && season.Single().Direct!.Select(d => d.Episode).SequenceEqual(new[] { 1, 2 })
                && episodeCalls == 2,
            "Jellyfin single-episode and season matches parse parent/index numbers and exclude other seasons"
        );
        var legacy = await api.Search(
            jelly,
            new()
            {
                Title = "Legacy Story",
                Year = 2024,
                ImdbId = "tt456",
                IsMovie = true,
            },
            false,
            CancellationToken.None
        );
        var remake = await api.Search(
            jelly,
            new()
            {
                Title = "Legacy Story",
                Year = 2023,
                ImdbId = "tt456",
                IsMovie = true,
            },
            false,
            CancellationToken.None
        );
        check(
            legacy.Count == 1 && remake.Count == 0,
            "Jellyfin falls back to exact normalized title and year only when server IMDb identity is missing"
        );
        var pin = await api.BeginPlex("stable-device", CancellationToken.None);
        var empty = await api.PollPlex(pin, CancellationToken.None);
        var token = await api.PollPlex(pin, CancellationToken.None);
        var resources = await api.PlexResources(token, pin.DeviceId, CancellationToken.None);
        var plex = resources.First();
        check(
            plexHeaders
                && empty.Length == 0
                && token == "plex-fixture-token"
                && pin.BrowserUrl.Contains("clientID=stable-device&code=ABCD")
                && resources.Count == 3
                && resources[2].BaseUrl == "http://only-local.fixture.invalid:32400"
                && plex.BaseUrl == "https://plex-local.fixture.invalid"
                && resources[1].BaseUrl == "https://remote.fixture.invalid",
            "Plex PIN polling and resource selection use required headers and prefer local HTTPS then remote HTTPS with a local HTTP fallback"
        );
        var plexMovie = await api.Search(plex, movie, false, CancellationToken.None);
        var plexSeason = await api.Search(plex, episode, true, CancellationToken.None);
        var plexEpisode = await api.Search(plex, episode, false, CancellationToken.None);
        check(
            movies.Single().Direct!.Single().Resolution == "1080p"
                && movies.Single().Direct!.Single().Language == "English"
                && plexMovie.Single().Direct!.Single().Resolution == "2160p"
                && plexMovie.Single().Direct!.Single().Language == "Arabic",
            "Media server options preserve per-file video quality and audio languages without counting subtitle languages"
        );
        check(
            plexMovie.Single().Direct!.Single().Url.EndsWith("/library/parts/movie/file.mkv?download=1")
                && plexSeason.Single().Direct!.Count == 2
                && plexEpisode.Single().Direct!.Single().Episode == 2
                && plexSeason.Single().Direct!.All(d => d.Headers["X-Plex-Token"] == "plex-server-token"),
            "Plex GUID matching and allLeaves parsing produce original movie, episode and season downloads with token headers"
        );
        var hub = await api.Search(
            plex,
            new()
            {
                Title = "Story",
                Year = 2024,
                IsMovie = true,
            },
            false,
            CancellationToken.None
        );
        check(
            hub.Count == 1,
            "Plex title search reads metadata nested in hub results when no IMDb identity is available"
        );
        var store = Store(parent, "media-settings", true);
        using var manager = new PcDownloads(store, new NoCloud(), start: false);
        manager.AddServer(jelly);
        manager.AddServer(plex);
        check(manager.Configured, "Connected media servers enable PC downloads without a debrid account");
        var cipher = File.ReadAllBytes(Path.Combine(store.DataDirectory, "downloads-settings.dpapi"));
        using var reloaded = new PcDownloads(store, new NoCloud(), start: false);
        check(
            reloaded.Config.Servers.Count == 2
                && reloaded.Config.Servers[0].Token == "jelly-token"
                && !System.Text.Encoding.UTF8.GetString(cipher).Contains("jelly-token")
                && !JsonSerializer.Serialize(reloaded.Config).Contains("never-store-password"),
            "Media server tokens persist only in DPAPI settings and authentication passwords are discarded"
        );
        var config = new DownloadCredentials { Servers = [jelly, plex] };
        var aggregate = new DownloadProviders(() => config, http);
        var discovered = await aggregate.Discover(movie, false, [movie], null, CancellationToken.None);
        check(
            discovered.Releases.Count == 2
                && discovered.Sources.Count(s => s.State == "Ready") == 2
                && discovered.Releases.All(r => r.Choice.CacheStatus == "On your server")
                && !JsonSerializer.Serialize(discovered.Releases.Select(r => r.Choice)).Contains("token"),
            "Connected servers remain independent discovery sources when the selected debrid account is unavailable"
        );
        var tvStore = Store(parent, "media-season-rules");
        var first = tvStore.Archive.Single();
        first.Path = Path.Combine(tvStore.State.Roots[0].Path, "Story.S01E01.mkv");
        File.WriteAllBytes(first.Path, [1]);
        first.Available = true;
        tvStore.State.EpisodeSchedules[first.GroupKey] =
        [
            new() { Season = 1, Episode = 1 },
            new()
            {
                Season = 1,
                Episode = 2,
                AirsAt = DateTimeOffset.UtcNow.AddDays(1),
            },
        ];
        tvStore.Save();
        using var tv = new PcDownloads(tvStore, aggregate, start: false);
        tv.AddServer(jelly);
        tv.AddServer(plex);
        var tvReleases = await tv.Search(first.Id, true);
        var jellyRelease = tvReleases.Single(r => r.Sources.Contains("Home (Jellyfin)"));
        var plexRelease = tvReleases.Single(r => r.Sources.Contains("Living room (Plex)"));
        var unavailable = await tv.Files(jellyRelease.Id);
        check(
            unavailable.Single(f => f.Episode == 1).Existing
                && unavailable.Single(f => f.Episode == 2).Future
                && unavailable.All(f => !f.Selectable),
            "Direct media-server seasons apply existing-file and future-episode exclusions"
        );
        tvStore.State.EpisodeSchedules[first.GroupKey].Single(e => e.Episode == 2).AirsAt =
            DateTimeOffset.UtcNow.AddDays(-1);
        var available = await tv.Files(plexRelease.Id);
        tv.Enqueue(
            new()
            {
                RequestId = Guid.NewGuid().ToString(),
                ReleaseId = plexRelease.Id,
                FileIds = [available.Single(f => f.Selectable).Id],
            }
        );
        var queued = await tv.Files(jellyRelease.Id);
        check(
            queued.Single(f => f.Episode == 2).Queued
                && !queued.Single(f => f.Episode == 2).Selectable
                && !File.ReadAllText(Path.Combine(tvStore.DataDirectory, "pc-downloads.json"))
                    .Contains("plex-server-token"),
            "Direct choices across media servers share episode queue exclusions and keep tokens encrypted"
        );
        var direct = movies.Single().Direct!.Single();
        var bytes = new byte[8192];
        using var lan = new PcDownloads(
            store,
            new NoCloud(),
            new HttpClient(
                new Handler(
                    (request, ct) =>
                        Task.FromResult(
                            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                        )
                )
            ),
            start: false
        );
        var target = store.Archive.Single();
        var job = new PcDownloadJob
        {
            Root = store.State.Roots[0].Path,
            Target = target,
            Size = 8192,
        };
        job.Destination = DownloadNaming.Destination(job.Root, target, direct.Name);
        await lan.Transfer(job, direct.Url, CancellationToken.None, direct.Headers, jelly.Id);
        check(
            File.Exists(job.Destination),
            "A configured Jellyfin LAN HTTP source downloads through the same verified transfer pipeline"
        );
        manager.RemoveServer(jelly.Id);
        manager.RemoveServer(plex.Id);
        check(
            !manager.Configured,
            "Removing all media servers restores the unconfigured state when no debrid key is present"
        );
    }
}
