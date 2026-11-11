using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Noir;

static class HistoryImportChecks
{
    static readonly DateTimeOffset At = new(2024, 10, 3, 18, 30, 0, TimeSpan.Zero);

    public static async Task Run(string root, Action<bool, string> check)
    {
        var dir = Path.Combine(root, "history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        (string Service, List<ImportRow> Rows) Read(string name, string text)
        {
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, text, Encoding.UTF8);
            return HistoryFormats.Read(path);
        }
        string Zip(string name, params (string Name, string Text)[] entries)
        {
            var path = Path.Combine(dir, name);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open(), Encoding.UTF8);
                writer.Write(entry.Text);
            }
            return path;
        }
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var netflix = Read(
                "ViewingActivity.csv",
                "Title,Date\r\n\"World: Beyond: Season 2: Hello, \"\"World\"\"\",03/10/2024\r\nPlain Movie,13/10/2024\r\nShow: Limited Series: Finale,2024-10-03\r\nShow: Part 2: Ep,03/10/2024\r\nShow: Book One: Ep,03/10/2024\r\nMovie: Subtitle,03/10/2024\r\n"
            );
            check(
                netflix.Service == "Netflix"
                    && netflix.Rows[0]
                        is { Title: "World: Beyond", IsMovie: false, Season: 2, EpisodeTitle: "Hello, \"World\"" }
                    && netflix.Rows[0].WatchedAt?.Month == 10
                    && netflix.Rows[0].WatchedAt?.Day == 3,
                "History Netflix detects day-first dates across the whole file and preserves colon titles and CSV quotes"
            );
            check(
                netflix.Rows[1].IsMovie == true
                    && netflix.Rows[2].Season == 1
                    && netflix.Rows[3].Season == 2
                    && netflix.Rows[4].Season == 1
                    && netflix.Rows[5] is { IsMovie: null, Title: "Movie: Subtitle", EpisodeTitle: "Subtitle" },
                "History Netflix recognizes limited series, parts and word-numbered books while retaining ambiguous colon titles"
            );
            var american = Read("NetflixViewingHistory.csv", "Title,Date\nMovie,10/3/24\nMovie,10/23/24\n");
            check(
                american.Rows[0].WatchedAt == new DateTimeOffset(2024, 10, 3, 0, 0, 0, TimeSpan.Zero),
                "History Netflix detects month-first dates and two-digit years"
            );
            var sequel = Read(
                "NetflixViewingHistory.csv",
                "Title,Date\n\"Stranger Things: Stranger Things 2: Chapter One: MADMAX\",10/3/24\n"
            );
            check(
                sequel.Rows[0]
                    is { Title: "Stranger Things", IsMovie: false, Season: 2, EpisodeTitle: "Chapter One: MADMAX" },
                "History Netflix reads seasons named after the show"
            );
            var markers = Read(
                "markers.csv",
                "Title,Date\nShow: Series 3: Ep,2024-10-03\nShow: Volume 4: Ep,2024-10-03\nShow: Vol. 5: Ep,2024-10-03\nShow: Chapter 6: Ep,2024-10-03\nShow: Collection 7: Ep,2024-10-03\nShow: Miniseries: Ep,2024-10-03\n"
            );
            check(
                markers.Rows.Select(r => r.Season).SequenceEqual([3, 4, 5, 6, 7, 1]),
                "History Netflix accepts every supported season marker"
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
        }
        var letterboxd = HistoryFormats.Read(
            Zip(
                "letterboxd.zip",
                (
                    "export/diary.csv",
                    "Date,Name,Year,Letterboxd URI,Rating,Watched Date\n2024-10-04,Arrival,2016,https://letterboxd.test/arrival,4.5,2024-10-03\n2024-10-05,Arrival,2016,https://letterboxd.test/arrival,,2024-10-04\n"
                ),
                (
                    "watched.csv",
                    "Date,Name,Year,Letterboxd URI\n2024-10-04,Arrival,2016,https://letterboxd.test/arrival\n2024-10-04,Other,2020,https://letterboxd.test/other\n"
                ),
                (
                    "ratings.csv",
                    "Date,Name,Year,Letterboxd URI,Rating\n2024-10-05,Arrival,2016,https://letterboxd.test/arrival,4.5\n2024-10-05,Other,2020,https://letterboxd.test/other,0.5\n"
                ),
                ("profile.csv", "Username,Email\nprivate,private@example.test\n")
            )
        );
        check(
            letterboxd.Service == "Letterboxd"
                && letterboxd.Rows.Count == 3
                && letterboxd.Rows.Count(r => r.Title == "Arrival") == 2
                && letterboxd.Rows.Where(r => r.Title == "Arrival").All(r => r.Rating == 5)
                && letterboxd.Rows.Single(r => r.Title == "Other") is { WatchedAt: null, Rating: 1, IsMovie: true },
            "History Letterboxd ZIP prefers diary rewatches, merges ratings and adds watched-only movies without a date"
        );
        check(
            Read("ratings.csv", "Date,Name,Year,Letterboxd URI,Rating\n2024-10-03,Arrival,2016,url,3.5\n")
                .Rows.Single()
                .Rating == 4,
            "History Letterboxd standalone ratings round half stars away from zero"
        );
        var imdb = Read(
            "imdb.csv",
            "Const,Your Rating,Date Rated,Title,Title Type,Year\n tt0000001,1,2024-10-03,Movie,movie,2024\ntt0000002,9,2024-10-03,Episode,tvEpisode,2023\ntt0000003,6,2024-10-03,Show,tvMiniSeries,2020\n"
        );
        check(
            imdb.Service == "IMDb"
                && imdb.Rows.Select(r => r.Rating).SequenceEqual([1, 5, 3])
                && imdb.Rows[1] is { ImdbId: "tt0000002", IsMovie: false, Season: 0, Episode: 0 }
                && imdb.Rows[2].IsMovie == false,
            "History IMDb ratings normalize scores and preserve episode IMDb identities with unknown numbers"
        );
        var trakt = Read(
            "history.json",
            """
            [{"watched_at":"2024-10-03T18:30:00Z","type":"movie","movie":{"title":"Arrival","year":2016,"ids":{"imdb":"tt2543164"}}},
             {"watched_at":"2024-10-04T18:30:00Z","type":"episode","show":{"title":"The Café & Co","year":2020,"ids":{"imdb":"tt1000001","tvdb":42}},"episode":{"season":1,"number":2,"title":"The Return"}}]
            """
        );
        check(
            trakt.Service == "Trakt"
                && trakt.Rows.Count == 2
                && trakt.Rows[0].WatchedAt == At
                && trakt.Rows[1] is { Season: 1, Episode: 2, TvdbId: "42", EpisodeTitle: "The Return" },
            "History Trakt history arrays preserve movie dates and episode identities"
        );
        var watchedMovies = Read(
            "watched-movies.json",
            """[{"last_watched_at":"2024-10-03T18:30:00Z","plays":3,"movie":{"title":"Arrival","year":2016,"ids":{"imdb":"tt2543164"}}}]"""
        );
        var watchedShows = Read(
            "watched-shows.json",
            """[{"last_watched_at":"2024-10-03T18:30:00Z","plays":1,"show":{"title":"Show","ids":{"tvdb":42}},"seasons":[{"number":2,"episodes":[{"number":3,"plays":2,"last_watched_at":"2024-10-04T00:00:00Z"}]}]}]"""
        );
        check(
            watchedMovies.Rows.Count == 1
                && watchedShows.Rows.Single() is { Season: 2, Episode: 3 }
                && watchedShows.Rows[0].WatchedAt?.Day == 4,
            "History Trakt watched snapshots use the last known viewing rather than inventing plays"
        );
        var body = Read(
            "sync.json",
            """{"movies":[{"title":"Arrival","year":2016,"ids":{"imdb":"tt2543164"},"watched_at":"2024-10-03T18:30:00Z"}],"shows":[{"title":"Show","ids":{"imdb":"tt1000001"},"seasons":[{"number":1,"episodes":[{"number":1},{"number":2,"watched_at":"2024-10-03T18:30:00Z"}]}]}]}"""
        );
        check(
            body.Rows.Count == 3 && body.Rows[1].WatchedAt == null && body.Rows[2].WatchedAt == At,
            "History Trakt sync bodies support undated episodes and explicit timestamps"
        );
        var simkl = Read(
            "simkl.json",
            """
            {"movies":[{"movie":{"title":"Done","ids":{"imdb":"tt0000001"}},"status":"completed","last_watched_at":"2024-10-03T18:30:00Z"},{"movie":{"title":"Planned"},"status":"plantowatch"}],
             "shows":[{"show":{"title":"Show","ids":{"tvdb":42}},"status":"watching","seasons":[{"number":1,"episodes":[{"number":2,"watched_at":"2024-10-03T18:30:00Z"}]}]}],
             "anime":[{"show":{"title":"Anime"},"status":"watching","seasons":[{"number":1,"episodes":[{"number":1}]}]}]}
            """
        );
        check(
            simkl.Service == "Simkl"
                && simkl.Rows.Count == 3
                && !simkl.Rows.Any(r => r.Title == "Planned")
                && simkl.Rows[2].IsMovie == false,
            "History Simkl imports completed movies and watched show/anime episodes only"
        );
        var simklWithoutStatus = Read(
            "backup.json",
            """{"shows":[{"show":{"title":"Show"},"seasons":[{"number":1,"episodes":[{"number":2}]}]}]}"""
        );
        check(
            simklWithoutStatus.Service == "Simkl"
                && simklWithoutStatus.Rows.Single() is { Season: 1, Episode: 2, WatchedAt: null },
            "History Simkl recognizes wrapped shows with watched episodes even without status or anime fields"
        );
        var tvtime = HistoryFormats.Read(
            Zip(
                "tvtime.zip",
                (
                    "episodes.csv",
                    "tv_show_name,episode_season_number,episode_number,created_at,tv_show_id\nShow,2,3,2024-10-03T18:30:00Z,42\n"
                ),
                (
                    "duplicate.csv",
                    "series_name,season_number,number,updated_at,series_id\nShow,2,3,2024-10-03T18:30:00Z,42\n"
                ),
                ("movies.csv", "movie_name,watched_at\nMovie,2024-10-03T18:30:00Z\n"),
                ("account.csv", "email,address\nprivate,private\n"),
                ("unrelated.json", "{incomplete unrelated data")
            )
        );
        check(
            tvtime.Service == "TV Time"
                && tvtime.Rows.Count == 2
                && tvtime.Rows[0] is { Season: 2, Episode: 3, TvdbId: "42" }
                && tvtime.Rows[1].IsMovie == true,
            "History TV Time ZIP scans alias schemas and deduplicates repeated episode exports"
        );
        var generic = Read(
            "generic.csv",
            "type,title,year,imdb,season,episode,episode_title,watched date,rating\r\nmovie,\"A, \"\"quoted\"\"\r\nTitle\",2020,tt0000001,0,0,,2024-10-03,4\r\nepisode,Show,2020,tt1000001,1,2,The Return,,0\r\n"
        );
        check(
            generic.Service == "CSV"
                && generic.Rows[0].Title == "A, \"quoted\"\r\nTitle"
                && generic.Rows[0].IsMovie == true
                && generic.Rows[1].WatchedAt == null,
            "History generic CSV handles BOM, embedded commas, quotes and newlines with header aliases"
        );
        bool friendly = false;
        try
        {
            Read("unknown.csv", "email,password\nprivate,private\n");
        }
        catch (IOException e)
        {
            friendly = e.Message.Contains("recognizable");
        }
        check(friendly, "History unrecognized exports report a friendly IOException");
        bool malformed = false;
        try
        {
            Read("broken.csv", "title,year\n\"unfinished,2020\n");
        }
        catch (IOException e)
        {
            malformed = e.InnerException is FormatException;
        }
        check(malformed, "History malformed standalone CSV reports a friendly IOException");
        check(Read("empty.json", "[]").Rows.Count == 0, "History empty Noir JSON exports can be imported");
        var duplicate = Read(
            "duplicate.json",
            """[{"type":"movie","title":"Movie","imdb_id":"tt123","date_unknown":false,"watched_at":"2024-10-03T00:00:00Z"},{"type":"movie","title":"Movie","imdb_id":"tt123","date_unknown":false,"watched_at":"2024-10-03T00:00:00Z"}]"""
        );
        check(
            duplicate.Rows.Count == 2,
            "History Noir parsing retains one row per viewing even when viewing fields are identical"
        );

        var ids = new List<string> { "tt1000001:1:1", "tt1000001:1:2", "tt1000001:2:1", "tt1000001:2:2" };
        var bits = Bitfield(ids[1], 4, [0b00010100]); // offset=4-1-1=2: logical episodes 0 and 2 are set.
        check(
            StremioLibrary.DecodeWatched(bits, ids).SequenceEqual([ids[0], ids[2]]),
            "History Stremio bitfield decodes colon anchors, little-endian bits and a non-zero offset"
        );
        check(
            StremioLibrary.DecodeWatched(Bitfield(ids[2], 1, [0b00000010]), ids).SequenceEqual([ids[3]]),
            "History Stremio bitfield handles a negative offset without shifting the video order"
        );
        var handler = new FixtureHandler
        {
            Stremio = JsonSerializer.Serialize(
                new
                {
                    result = new object[]
                    {
                        new
                        {
                            _id = "tt2543164",
                            name = "Arrival",
                            type = "movie",
                            removed = true,
                            state = new { timesWatched = 1, lastWatched = At.ToUnixTimeMilliseconds() },
                        },
                        new
                        {
                            _id = "tt0000003",
                            name = "Never watched",
                            type = "movie",
                            removed = true,
                            state = new { timesWatched = 0 },
                        },
                        new
                        {
                            _id = "tt0000004",
                            name = "Temporary",
                            type = "movie",
                            temp = true,
                            state = new { flaggedWatched = 1 },
                        },
                        new
                        {
                            _id = "tt1000001",
                            name = "The Café & Co",
                            type = "series",
                            state = new
                            {
                                watched = bits,
                                video_id = ids[2],
                                lastWatched = At.ToUnixTimeMilliseconds(),
                            },
                        },
                        new
                        {
                            _id = "tt1000002",
                            name = "Broken",
                            type = "series",
                            state = new
                            {
                                watched = "invalid",
                                video_id = "tt1000002:1:3",
                                timesWatched = 1,
                                lastWatched = At.ToUnixTimeMilliseconds(),
                            },
                        },
                        new
                        {
                            _id = "tt1000003",
                            name = "Unknown anchor",
                            type = "series",
                            state = new
                            {
                                watched = Bitfield("tt1000003:9:9", 2, [1]),
                                video_id = "tt1000003:1:1",
                                lastWatched = At.ToUnixTimeMilliseconds(),
                            },
                        },
                        new
                        {
                            _id = "tt1000004",
                            name = "Removed resume",
                            type = "series",
                            removed = true,
                            state = new { video_id = "tt1000004:1:1" },
                        },
                    },
                }
            ),
        };
        using var http = new HttpClient(handler);
        using var discovery = new DiscoveryClient(Path.Combine(dir, "metadata"), handler);
        var fetched = await StremioLibrary.Fetch(
            "fixture-secret",
            http,
            (_, _) => Task.FromResult(ids),
            CancellationToken.None
        );
        check(
            handler.StremioBody
                && fetched.Count == 4
                && fetched.Any(r => r.ImdbId == "tt1000002" && r.Episode == 3)
                && !fetched.Any(r => r.ImdbId == "tt1000003")
                && fetched.Count(r => r.IsMovie == true) == 1
                && fetched.Single(r => r.ImdbId == "tt1000001" && r.Episode == 1 && r.Season == 1).WatchedAt == null
                && fetched.Single(r => r.ImdbId == "tt1000001" && r.Season == 2).WatchedAt == At,
            "History Stremio fetch posts the library request, skips unwatched removals/temp items, falls back to the current episode only for counted watches, and dates only the current episode"
        );
        check(
            fetched.Any(r => r.ImdbId == "tt1000002" && r.Episode == 3) && !fetched.Any(r => r.ImdbId == "tt1000003"),
            "History Stremio falls back to the current episode for damaged bitfields only when Stremio counted a watch"
        );
        var videoOrder = await discovery.VideoIds("tt1000001", CancellationToken.None);
        var reads = handler.MetaReads;
        check(
            videoOrder.SequenceEqual([ids[2], ids[0], ids[1], ids[3]])
                && (await discovery.VideoIds("tt1000001", CancellationToken.None)).SequenceEqual(videoOrder)
                && handler.MetaReads == reads,
            "History Cinemeta video IDs retain API order and reuse the metadata cache"
        );

        var store = new LibraryStore(Path.Combine(dir, "store"));
        store.State.Roots = [];
        store.State.SyncTrakt = true;
        var importer = new HistoryImporter(store, discovery, http);
        ImportRow Row(
            string title,
            bool? movie = false,
            string imdbId = "",
            string tvdbId = "",
            int season = 0,
            int episode = 0,
            string episodeTitle = "",
            int year = 0,
            DateTimeOffset? at = null,
            int rating = 0
        ) => new("Fixture", title, year, movie, imdbId, tvdbId, season, episode, episodeTitle, at, rating);
        var progress = new CaptureProgress();
        var plan = await importer.Resolve(
            "Fixture",
            [
                Row("Not the metadata title", true, "tt2543164", at: At, rating: 4),
                Row("TVDB show", tvdbId: "42", season: 1, episode: 1),
                Row("TVDB show", tvdbId: "42", season: 1, episode: 2, at: At),
                Row("Cafe and Co", year: 2021, episodeTitle: "The Return", season: 1, at: At),
                Row("The Cafe & Co", year: 2020, episodeTitle: "Episode 1", season: 2),
                Row("Arrival", true, year: 2016, at: At.AddDays(1)),
                Row("Movie: Subtitle", null, episodeTitle: "Subtitle", at: At),
                Row("World: Beyond", episodeTitle: "Finale", season: 2, at: At),
                Row("Missing"),
                Row("Bad metadata", true, "tt9999999"),
                Row("The Cafe & Co", year: 2020, episodeTitle: "Does not exist"),
            ],
            progress,
            CancellationToken.None
        );
        check(
            plan.Titles.Sum(t => t.Views.Count) == 8
                && plan.Unmatched.Count == 3
                && plan.Unmatched.All(u => u.Reason.Length > 0)
                && handler.TvdbReads == 1,
            "History resolver uses IMDb, cached TVDB and normalized title/year search and retains unmatched reasons"
        );
        check(
            plan.Titles.Any(t => t.Title.Id == "tt2543164" && t.Title.Year == 2016)
                && plan.Titles.Any(t => t.Title.Id == "tt1000001" && t.Views.Any(v => v.Season == 2 && v.Episode == 1))
                && plan.Titles.Any(t => t.Title.Id == "tt1000006" && t.Views.Single().Episode == 1),
            "History resolver selects the right remake and matches Netflix episode titles and numbered chapters"
        );
        check(
            plan.Titles.Any(t => t.Title.Id == "tt1000005" && !t.Title.IsMovie)
                && handler.Queries.Contains("movie:Movie: Subtitle")
                && handler.Queries.Contains("series:Movie"),
            "History resolver tries ambiguous colon titles as movies before searching the first segment as a show"
        );
        check(
            progress.Values.Last().Done == progress.Values.Last().Total
                && progress.Values.Select(v => v.Done).SequenceEqual(Enumerable.Range(1, progress.Values.Count)),
            "History resolver reports monotonic title progress"
        );
        var failed = await importer.Resolve(
            "Nuvio",
            [Row("Unavailable", true, "tt8888888")],
            null,
            CancellationToken.None
        );
        var missing = await importer.Resolve("Nuvio", [Row("No such title")], null, CancellationToken.None);
        var searchFailed = await importer.Resolve("Nuvio", [Row("Unavailable search")], null, CancellationToken.None);
        check(
            failed.LookupFailed
                && failed.Titles.Count == 0
                && searchFailed.LookupFailed
                && !missing.LookupFailed
                && missing.Unmatched.Count == 1,
            "History resolution explicitly flags transient metadata and search failures while ordinary unmatched titles allow cursor advancement"
        );
        var added = importer.Apply(plan);
        var again = importer.Apply(plan);
        check(
            added > 0
                && again == 0
                && store.State.WatchSync.Count == 0
                && store.State.Viewings.Values.SelectMany(v => v).All(v => v.Source == "Imported from Fixture"),
            "History Apply is idempotent and imports without queueing service sync"
        );
        var showItems = store.Archive.Where(i => i.ImdbId == "tt1000001").ToList();
        var unknown = showItems.Single(i => i.Season == 2 && i.Episode == 1);
        check(
            store.ViewingHistory(unknown).Single() is { DateUnknown: true }
                && store.ViewingHistory(unknown)[0].WatchedAt == store.AirDate(unknown)
                && !unknown.Available,
            "History Apply logs unavailable schedule episodes using their air date when the watch date is unknown"
        );
        var movie = store.Archive.Single(i => i.ImdbId == "tt2543164");
        check(
            store.State.PersonalReviews[movie.Id].Rating == 4 && store.ViewingHistory(movie).Count == 2,
            "History Apply retains ratings and dated movie rewatches"
        );
        var dated = new ImportPlan(
            "Fixture",
            [new(plan.Titles.First(t => t.Title.Id == "tt1000001").Title, [], [(2, 1, At, 3)])],
            []
        );
        check(
            importer.Apply(dated) == 1
                && store.ViewingHistory(unknown).Count == 1
                && !store.ViewingHistory(unknown)[0].DateUnknown
                && importer.Apply(dated) == 0,
            "History dated imports replace earlier undated guesses exactly once"
        );

        var concurrent = await importer.Resolve(
            "Fixture",
            Enumerable.Range(10, 10).Select(n => Row("Concurrent", true, "tt20000" + n)).ToList(),
            null,
            CancellationToken.None
        );
        check(
            concurrent.Titles.Count == 10 && handler.MaxActive is > 1 and <= 4,
            "History resolver limits concurrent title lookups to four"
        );
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            bool cancelled = false;
            try
            {
                await importer.Resolve("Fixture", [Row("Cancelled")], null, cancel.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            check(cancelled, "History resolver propagates cancellation");
        }

        var manual = new VideoItem
        {
            Title = "Manual",
            IsMovie = true,
            ImdbId = "tt0000010",
            Year = 2020,
            Available = false,
        };
        int events = 0;
        store.HistoryChanged += () => events++;
        check(
            store.LogWatched([new(manual, At, true, 5)], "Manual") == 1,
            "History LogWatched accepts an undated log and personal rating"
        );
        manual = store.Archive.Single(i => i.Title == "Manual");
        check(
            store.LogWatched([new(manual, At.AddYears(1), true)], "Manual") == 0
                && store.LogWatched([new(manual, At)], "Manual") == 1
                && store.ViewingHistory(manual).Count == 1
                && !store.ViewingHistory(manual)[0].DateUnknown,
            "History LogWatched skips repeated undated logs and replaces guesses with dates"
        );
        check(
            store.LogWatched([new(manual, At.AddHours(1)), new(manual, At.AddDays(2))], "Manual") == 1
                && store.ViewingHistory(manual).Count == 2,
            "History LogWatched deduplicates nearby dates while retaining rewatches"
        );
        var undo = store.Unwatch([manual]);
        check(
            undo.Count == 1
                && store.ViewingHistory(manual).Count == 0
                && !store.EverWatched(manual)
                && !store.State.WatchSync.Values.Any(j => j.ItemId == manual.Id),
            "History Unwatch clears history and completed progress and cancels queued sync"
        );
        store.Restore(undo);
        check(
            store.ViewingHistory(manual).Count == 2
                && store.GetProgress(manual)?.Completed == true
                && store.State.PersonalReviews[manual.Id].Rating == 5,
            "History Restore restores all viewings and progress without losing the rating"
        );
        var edited = store.ViewingHistory(manual)[0];
        var originalAt = edited.WatchedAt;
        var dateUndo = store.Snapshot([manual]);
        store.SetViewingDate(manual, edited.Id, At.AddYears(-3), true);
        store.Restore(dateUndo);
        check(
            store.ViewingHistory(manual).Single(v => v.Id == edited.Id) is { DateUnknown: false } restored
                && restored.WatchedAt == originalAt,
            "History Undo after a date edit restores the original date"
        );
        var binge = new VideoItem
        {
            Title = "Rewatched import",
            IsMovie = true,
            Available = false,
        };
        var bingeStore = new LibraryStore(Path.Combine(dir, "binge"));
        bingeStore.State.Roots = [];
        check(
            bingeStore.LogWatched(
                [new(binge, At), new(binge, At.AddHours(3))],
                "Imported from Noir",
                false,
                exact: true
            ) == 2
                && bingeStore.LogWatched(
                    [new(binge, At), new(binge, At.AddHours(3))],
                    "Imported from Noir",
                    false,
                    exact: true
                ) == 0,
            "History imports keep same-day rewatches and stay idempotent"
        );
        var viewing = store.ViewingHistory(manual)[0];
        store.SetViewingDate(manual, viewing.Id, At.AddDays(5), true);
        check(
            store.ViewingHistory(manual).Single(v => v.Id == viewing.Id) is { DateUnknown: true } && events >= 6,
            "History SetViewingDate updates unknown dates and publishes history changes"
        );
        foreach (var v in store.ViewingHistory(manual))
            store.RemoveViewing(manual, v.Id);
        check(
            !store.EverWatched(manual) && store.ViewingHistory(manual).Count == 0,
            "History RemoveViewing clears completion when the last viewing is removed"
        );
        store.LogWatched([new(manual, At.AddDays(5), true)], "Manual");
        var noId = new VideoItem
        {
            Title = "No IMDb",
            IsMovie = true,
            Available = false,
        };
        store.LogWatched([new(noId, At)], "Manual", false);
        await Exports(dir, store, discovery, http, check);
    }

    static async Task Exports(
        string dir,
        LibraryStore store,
        DiscoveryClient discovery,
        HttpClient http,
        Action<bool, string> check
    )
    {
        var count = store.Archive.Sum(i => store.ViewingHistory(i).Count);
        var source = store
            .Archive.SelectMany(i =>
                store
                    .ViewingHistory(i)
                    .Select(v =>
                        (
                            i.Title,
                            i.Year,
                            i.IsMovie,
                            i.ImdbId,
                            Season: i.IsMovie ? 0 : i.Season,
                            Episode: i.IsMovie ? 0 : i.Episode,
                            i.EpisodeTitle,
                            At: v.DateUnknown ? (DateTimeOffset?)null : v.WatchedAt,
                            Rating: store.State.PersonalReviews.GetValueOrDefault(i.Id)?.Rating ?? 0
                        )
                    )
            )
            .ToHashSet();
        foreach (var format in new[] { "noir-csv", "noir-json", "trakt", "letterboxd" })
        {
            var path = Path.Combine(
                dir,
                format + (format.EndsWith("csv") || format == "letterboxd" ? ".csv" : ".json")
            );
            File.WriteAllText(path, "old content");
            var written = HistoryExport.Write(store, path, format, out var skipped);
            var imported = HistoryFormats.Read(path);
            var expected = store
                .Archive.Where(i => format != "letterboxd" || i.IsMovie)
                .Where(i => format != "trakt" || i.ImdbId.StartsWith("tt"))
                .Sum(i => store.ViewingHistory(i).Count);
            check(
                written == expected
                    && skipped == count - written
                    && imported.Rows.Count == expected
                    && !Directory.GetFiles(dir, "*.tmp").Any(),
                "History "
                    + format
                    + " exports one row per viewing, counts skipped records and atomically replaces existing files"
            );
            if (format.StartsWith("noir"))
            {
                var actual = imported
                    .Rows.Select(r =>
                        (
                            r.Title,
                            r.Year,
                            IsMovie: r.IsMovie == true,
                            r.ImdbId,
                            r.Season,
                            r.Episode,
                            r.EpisodeTitle,
                            At: r.WatchedAt,
                            r.Rating
                        )
                    )
                    .ToHashSet();
                check(
                    written == count && actual.SetEquals(source),
                    "History " + format + " round-trip preserves title identities, dates, unknown dates and ratings"
                );
            }
            if (format == "trakt")
            {
                check(
                    written == count - 1
                        && imported.Rows.Any(r => r.WatchedAt == null)
                        && imported.Rows.Where(r => r.IsMovie == false).All(r => r.Season > 0 && r.Episode > 0),
                    "History Trakt export skips missing IMDb identities and preserves undated entries and episode numbers"
                );
                var target = new LibraryStore(Path.Combine(dir, "roundtrip-trakt"));
                target.State.Roots = [];
                var resolver = new HistoryImporter(target, discovery, http);
                var plan = await resolver.Resolve(imported.Service, imported.Rows, null, CancellationToken.None);
                resolver.Apply(plan);
                check(
                    plan.Unmatched.Count == 0
                        && target.Archive.Sum(i => target.ViewingHistory(i).Count) == written
                        && resolver.Apply(plan) == 0,
                    "History Trakt export resolves and applies offline with rewatches and idempotency"
                );
            }
            if (format == "noir-csv")
            {
                var target = new LibraryStore(Path.Combine(dir, "roundtrip-noir"));
                target.State.Roots = [];
                var resolver = new HistoryImporter(target, discovery, http);
                var plan = await resolver.Resolve(
                    imported.Service,
                    imported.Rows.Where(r => r.ImdbId.Length > 0).ToList(),
                    null,
                    CancellationToken.None
                );
                resolver.Apply(plan);
                check(
                    plan.Unmatched.Count == 0
                        && target.Archive.Sum(i => target.ViewingHistory(i).Count) == written - 1
                        && resolver.Apply(plan) == 0,
                    "History Noir CSV export resolves and applies offline with rewatches and idempotency"
                );
            }
            if (format == "letterboxd")
            {
                var csv = HistoryData.Csv(File.ReadAllText(path));
                var rewatchIndex = csv[0].IndexOf("Rewatch");
                check(
                    imported.Rows.All(r => r.IsMovie == true)
                        && imported.Rows.Any(r => r.Title == "Manual" && r.WatchedAt == null)
                        && csv.Skip(1).Any(r => r[rewatchIndex] == "true"),
                    "History Letterboxd export includes only movies with unknown dates and rewatch flags"
                );
                var eastOfUtc = TimeZoneInfo.CreateCustomTimeZone("plus9", TimeSpan.FromHours(9), "plus9", "plus9");
                var lateUtc = new DateTimeOffset(2026, 3, 4, 20, 30, 0, TimeSpan.Zero);
                check(
                    HistoryExport.LetterboxdDate(lateUtc, eastOfUtc) == "2026-03-05"
                        && HistoryExport.LetterboxdDate(lateUtc, TimeZoneInfo.Utc) == "2026-03-04"
                        && HistoryExport.LetterboxdRating(7) == "3.5"
                        && HistoryExport.LetterboxdRating(10) == "5.0"
                        && HistoryExport.LetterboxdRating(1) == "0.5"
                        && HistoryExport.LetterboxdRating(0) == "",
                    "History Letterboxd export writes half stars and the local watched date"
                );
                var written10 = csv.Skip(1).Where(r => r[csv[0].IndexOf("Rating")].Length > 0).ToList();
                check(
                    written10.All(r => r[csv[0].IndexOf("Rating")].Contains('.')),
                    "History Letterboxd export rating cells use one decimal"
                );
            }
        }
    }

    static string Bitfield(string anchor, int length, byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zip = new ZLibStream(output, CompressionLevel.SmallestSize, true))
            zip.Write(bytes);
        return anchor + ":" + length + ":" + Convert.ToBase64String(output.ToArray());
    }

    sealed class CaptureProgress : IProgress<(int Done, int Total)>
    {
        public List<(int Done, int Total)> Values = [];

        public void Report((int Done, int Total) value) => Values.Add(value);
    }

    sealed class FixtureHandler : HttpMessageHandler
    {
        public string Stremio = "";
        public bool StremioBody;
        public int TvdbReads,
            MetaReads,
            MaxActive;
        int active;
        readonly object gate = new();
        public HashSet<string> Queries = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            string json;
            if (uri.Host == "api.strem.io")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var b = body.RootElement;
                StremioBody =
                    request.Method == HttpMethod.Post
                    && uri.AbsolutePath == "/api/datastoreGet"
                    && b.GetProperty("authKey").GetString() == "fixture-secret"
                    && b.GetProperty("collection").GetString() == "libraryItem"
                    && b.GetProperty("all").GetBoolean()
                    && b.GetProperty("ids").GetArrayLength() == 0;
                json = Stremio;
            }
            else if (uri.Host == "api.tvmaze.com")
            {
                TvdbReads++;
                if (uri.Query != "?thetvdb=42")
                    throw new Exception("Unexpected TVDB ID");
                json = """{"externals":{"imdb":"tt1000001"}}""";
            }
            else if (uri.Host == "v3-cinemeta.strem.io" && uri.AbsolutePath.StartsWith("/catalog/"))
            {
                if (uri.AbsolutePath.Contains("Unavailable%20search"))
                    return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("retry") };
                var parts = uri.AbsolutePath.Split('/');
                var type = parts[2];
                var query = Uri.UnescapeDataString(parts[^1][7..^5]);
                lock (gate)
                    Queries.Add(type + ":" + query);
                var found = query switch
                {
                    "Arrival" when type == "movie" => new[]
                    {
                        new
                        {
                            id = "tt0000008",
                            name = "Arrival",
                            releaseInfo = "1996",
                        },
                        new
                        {
                            id = "tt2543164",
                            name = "Arrival",
                            releaseInfo = "2016",
                        },
                    },
                    "Cafe and Co" or "The Cafe & Co" when type == "series" =>
                    [
                        new
                        {
                            id = "tt1000001",
                            name = "The Café & Co",
                            releaseInfo = "2020",
                        },
                    ],
                    "Movie" when type == "series" =>
                    [
                        new
                        {
                            id = "tt1000005",
                            name = "Movie",
                            releaseInfo = "2020",
                        },
                    ],
                    "World: Beyond" when type == "series" =>
                    [
                        new
                        {
                            id = "tt1000006",
                            name = "World: Beyond",
                            releaseInfo = "2020",
                        },
                    ],
                    _ => System.Array.Empty<object>(),
                };
                json = JsonSerializer.Serialize(new { metas = found });
            }
            else if (uri.Host == "v3-cinemeta.strem.io" && uri.AbsolutePath.StartsWith("/meta/"))
            {
                Interlocked.Increment(ref MetaReads);
                var path = uri.AbsolutePath;
                var id = Path.GetFileNameWithoutExtension(path);
                if (id == "tt9999999")
                    return new(HttpStatusCode.NotFound) { Content = new StringContent("missing") };
                if (id == "tt8888888")
                    return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("retry") };
                if (id.StartsWith("tt20000"))
                {
                    var n = Interlocked.Increment(ref active);
                    lock (gate)
                        MaxActive = Math.Max(MaxActive, n);
                    try
                    {
                        await Task.Delay(25, ct);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                    }
                }
                var name = id switch
                {
                    "tt2543164" => "Arrival",
                    "tt1000001" => "The Café & Co",
                    "tt1000005" => "Movie",
                    "tt1000006" => "World: Beyond",
                    "tt0000010" => "Manual",
                    _ => "Concurrent",
                };
                var videos = id switch
                {
                    "tt1000001" => new[]
                    {
                        new
                        {
                            id = "tt1000001:2:1",
                            season = 2,
                            episode = 1,
                            name = "Chapter One",
                            released = "2023-02-01T00:00:00Z",
                        },
                        new
                        {
                            id = "tt1000001:1:1",
                            season = 1,
                            episode = 1,
                            name = "Pilot",
                            released = "2020-01-01T00:00:00Z",
                        },
                        new
                        {
                            id = "tt1000001:1:2",
                            season = 1,
                            episode = 2,
                            name = "The Return",
                            released = "2020-01-08T00:00:00Z",
                        },
                        new
                        {
                            id = "tt1000001:2:2",
                            season = 2,
                            episode = 2,
                            name = "End",
                            released = "2023-02-08T00:00:00Z",
                        },
                    },
                    "tt1000005" =>
                    [
                        new
                        {
                            id = "tt1000005:1:1",
                            season = 1,
                            episode = 1,
                            name = "Subtitle",
                            released = "2020-01-01T00:00:00Z",
                        },
                    ],
                    "tt1000006" =>
                    [
                        new
                        {
                            id = "tt1000006:2:1",
                            season = 2,
                            episode = 1,
                            name = "Finale",
                            released = "2020-01-01T00:00:00Z",
                        },
                    ],
                    _ => [],
                };
                json = JsonSerializer.Serialize(
                    new
                    {
                        meta = new
                        {
                            id,
                            name,
                            releaseInfo = id == "tt2543164" ? "2016" : "2020",
                            videos,
                        },
                    }
                );
            }
            else
                throw new Exception("Unexpected history fixture request: " + uri);
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
