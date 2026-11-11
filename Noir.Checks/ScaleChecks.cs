using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Noir;

// S5 (big libraries): a deterministic synthetic library built from the app's own types, never from user data,
// and in-process timings on a copy of it. Kept as a tool so later rounds can rerun the same numbers:
//   Noir.Checks --generate-scale <empty dir>     builds the profile (library.json, pc-downloads.json, noir-ready.json, media/)
//   Noir.Checks --scale-measure <dir> [<report>] times store, export/import, downloads, organizer, Ready and sync on a temp copy
static class ScaleChecks
{
    public static readonly DateTimeOffset Base = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);
    public const int Shows = 1000,
        Movies = 2000,
        ViewingCount = 40000,
        RatingCount = 5000,
        FileCount = 2000,
        SyncJobCount = 4000,
        ImportRows = 20000;

    static readonly string[] Adjectives =
    [
        "Silent",
        "Golden",
        "Broken",
        "Hidden",
        "Northern",
        "Electric",
        "Hollow",
        "Crimson",
        "Quiet",
        "Wild",
        "Paper",
        "Iron",
        "Velvet",
        "Burning",
        "Distant",
        "Frozen",
        "Lucky",
        "Midnight",
        "Open",
        "Restless",
        "Salt",
        "Secret",
        "Shadow",
        "Silver",
        "Stone",
        "Summer",
        "Tender",
        "Twin",
        "Winter",
        "Young",
        "Amber",
        "Bitter",
        "Bright",
        "Copper",
        "Deep",
        "Empty",
        "Fallen",
        "Glass",
        "Lost",
        "Long",
    ];
    static readonly string[] Nouns =
    [
        "Harbor",
        "Garden",
        "Signal",
        "Kingdom",
        "Station",
        "River",
        "Letters",
        "Engine",
        "Orchard",
        "Frontier",
        "Mirror",
        "Bridge",
        "Circle",
        "Empire",
        "Island",
        "Lantern",
        "Machine",
        "Valley",
        "Witness",
        "Tower",
        "Highway",
        "Hours",
        "Country",
        "Shore",
        "Planet",
        "Season",
        "Ledger",
        "Market",
        "Heights",
        "Avenue",
        "Desert",
        "Motel",
        "Archive",
        "Canyon",
        "Choir",
        "District",
        "Forest",
        "Hotel",
        "Meadow",
        "Parade",
    ];
    static readonly string[] Genres = ["Drama", "Comedy", "Crime", "Sci-Fi", "Animation", "Thriller", "Documentary"];

    public static string Name(int i) =>
        (i >= 1600 ? "The " : "")
        + Adjectives[i % Adjectives.Length]
        + " "
        + Nouns[i / Adjectives.Length % Nouns.Length];

    public static string Imdb(int i) => "tt" + (5100000 + i);

    public static void Generate(string dir, int divide = 1)
    {
        // divide > 1 builds the same mix at 1/divide size (a phone-sized profile); 1 is the S5 library.
        int D(int n) => Math.Max(1, n / divide);
        int shows = D(Shows),
            movieCount = D(Movies),
            viewingTarget = D(ViewingCount);
        dir = Path.GetFullPath(dir);
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
            throw new IOException("Choose an empty folder for the scale profile: " + dir);
        var clock = Stopwatch.StartNew();
        var random = new Random(2026);
        string Id()
        {
            var b = new byte[16];
            random.NextBytes(b);
            return new Guid(b).ToString("N");
        }
        var store = new LibraryStore(dir);
        var s = store.State;
        s.MobileInstanceId = Id();
        s.AppMode = "Library";
        s.OnlineMetadata = false;
        s.StremioAutoImport = false;
        s.NuvioAutoImport = false;
        s.SyncNuvio = false;
        s.AutoBackup = false;
        s.EpisodeAlerts = false;
        s.SyncTrakt = false;
        s.SyncSerializd = false;
        var tvRoot = Path.Combine(dir, "media", "TV");
        var movieRoot = Path.Combine(dir, "media", "Movies");
        s.Roots = [new() { Path = tvRoot, Movies = false }, new() { Path = movieRoot, Movies = true }];
        var showEpisodes = new List<List<CatalogEntry>>();
        var movies = new List<CatalogEntry>();
        CatalogEntry Add(VideoItem item)
        {
            var entry = new CatalogEntry { Item = item, MetadataCheckedAt = Base };
            s.Catalog[item.Id] = entry;
            s.LibraryAdds.Add(item.GroupKey);
            return entry;
        }
        for (var t = 0; t < shows; t++)
        {
            var imdb = Imdb(t);
            var group = "show:" + imdb;
            var year = 1995 + t % 30;
            var seasons = 1 + random.Next(6);
            var list = new List<CatalogEntry>();
            var schedule = new List<KnownEpisode>();
            var start = new DateTimeOffset(year, 1 + t % 12, 1 + t % 27, 21, 0, 0, TimeSpan.Zero);
            for (var season = 1; season <= seasons; season++)
            {
                var count = 8 + random.Next(7);
                for (var e = 1; e <= count; e++)
                {
                    var airs = start.AddDays((season - 1) * 365 + (e - 1) * 7);
                    if (airs > Base.AddDays(-14))
                        airs = Base.AddDays(-14 - (count - e));
                    schedule.Add(
                        new()
                        {
                            Season = season,
                            Episode = e,
                            Title = $"Chapter {e}",
                            AirsAt = airs,
                            Runtime = 45,
                        }
                    );
                    list.Add(
                        Add(
                            new()
                            {
                                Id = $"scale-s{t}-{season}-{e}",
                                CollectionKey = group,
                                Title = Name(t),
                                Year = year,
                                Season = season,
                                Episode = e,
                                EpisodeTitle = $"Chapter {e}",
                                ImdbId = imdb,
                                Genres = Genres[t % Genres.Length],
                                Duration = 2700,
                                Available = false,
                                Summary = "A synthetic show for the S5 scale check.",
                                SourceName = "Cinemeta",
                                SourceUrl = "https://www.imdb.com/title/" + imdb + "/",
                                ReviewTitle = Name(t),
                                AddedAt = Base.AddDays(-random.Next(2500)),
                            }
                        )
                    );
                }
            }
            var running = t % 10 == 0;
            if (running)
                for (var e = 1; e <= 2; e++)
                    schedule.Add(
                        new()
                        {
                            Season = seasons + 1,
                            Episode = e,
                            Title = $"Chapter {e}",
                            AirsAt = Base.AddDays(3 + 7 * e),
                            Runtime = 45,
                        }
                    );
            s.EpisodeSchedules[group] = schedule;
            s.ShowStates[group] = running ? "Running" : "Ended";
            s.ShowRuntimes[group] = 45;
            showEpisodes.Add(list);
        }
        for (var t = Shows; t < Shows + movieCount; t++)
        {
            var imdb = Imdb(t);
            movies.Add(
                Add(
                    new()
                    {
                        Id = $"scale-m{t}",
                        CollectionKey = "movie:" + imdb,
                        Title = Name(t),
                        Year = 1970 + t % 56,
                        IsMovie = true,
                        Episode = 0,
                        ImdbId = imdb,
                        Genres = Genres[t % Genres.Length],
                        Duration = 6600,
                        Available = false,
                        Summary = "A synthetic movie for the S5 scale check.",
                        SourceName = "Cinemeta",
                        SourceUrl = "https://www.imdb.com/title/" + imdb + "/",
                        ReviewTitle = Name(t),
                        AddedAt = Base.AddDays(-random.Next(2500)),
                    }
                )
            );
        }
        // Viewings: 2,100 for movies (1,800 watched, 300 of them twice), the rest a watched prefix of each show plus rewatches.
        var viewings = 0;
        void View(CatalogEntry e, DateTimeOffset at)
        {
            if (at > Base.AddHours(-1))
                at = Base.AddHours(-1 - random.Next(48));
            var id = Id();
            if (!s.Viewings.TryGetValue(e.Item.Id, out var list))
                s.Viewings[e.Item.Id] = list = [];
            var playback = random.Next(10) < 7;
            list.Add(
                new()
                {
                    Id = id,
                    WatchedAt = at,
                    Source = playback ? "Playback" : "Imported from Trakt",
                    LoggedAt = playback ? at : at.AddDays(random.Next(30, 900)),
                }
            );
            if (e.Progress == null || e.Progress.UpdatedAt < at)
                e.Progress = new()
                {
                    Position = e.Item.Duration,
                    Duration = e.Item.Duration,
                    Completed = true,
                    UpdatedAt = at,
                    ViewingId = id,
                };
            viewings++;
        }
        for (var m = 0; m < D(1800); m++)
        {
            var at = Base.AddDays(-random.Next(1, 3600)).AddMinutes(random.Next(1440));
            View(movies[m], at);
            if (m < D(300))
                View(movies[m], at.AddDays(200 + random.Next(600)));
        }
        var watched = new List<CatalogEntry>();
        var episodeBudget = viewingTarget - viewings;
        foreach (var list in showEpisodes)
        {
            var share = 0.55 + random.NextDouble() * 0.45;
            var upTo = (int)Math.Round(list.Count * share);
            var at = Base.AddDays(-random.Next(30, 3300));
            for (var n = 0; n < upTo && watched.Count < episodeBudget; n++)
            {
                at = at.AddHours(6 + random.Next(80));
                View(list[n], at);
                watched.Add(list[n]);
            }
            // Started shows: the next episode is half watched so Continue/Up next have work to do.
            if (upTo < list.Count && random.Next(5) == 0)
                list[upTo].Progress = new()
                {
                    Position = 900,
                    Duration = 2700,
                    UpdatedAt = Base.AddDays(-random.Next(1, 60)),
                    ViewingId = Id(),
                };
        }
        for (var k = 0; viewings < viewingTarget; k += 7)
        {
            var e = watched[k % watched.Count];
            View(e, s.Viewings[e.Item.Id][0].WatchedAt.AddDays(300 + random.Next(900)));
        }
        // Ratings: 2,500 personal reviews (1,500 movies, 1,000 episodes), 1,500 season ratings, 1,000 show ratings.
        foreach (var e in movies.Take(D(1500)))
            s.PersonalReviews[e.Item.Id] = new() { Rating10 = 1 + random.Next(10) };
        foreach (var e in watched.Where((_, n) => n % 29 == 0).Take(D(1000)))
            s.PersonalReviews[e.Item.Id] = new() { Rating10 = 1 + random.Next(10) };
        var seasonKeys = showEpisodes
            .SelectMany(l => l.Select(e => e.Item.GroupKey + ":" + e.Item.Season).Distinct())
            .Take(D(1500))
            .ToList();
        foreach (var key in seasonKeys)
            s.SeasonRatings[key] = 1 + random.Next(10);
        foreach (var list in showEpisodes.Take(D(1000)))
            s.ShowRatings[list[0].Item.GroupKey] = 1 + random.Next(10);
        foreach (var e in movies.Skip(D(1800)).Take(D(200)))
            s.Watchlist.Add(e.Item.GroupKey);
        s.Favorites = showEpisodes.Take(D(50)).Select(l => l[0].Item.GroupKey).ToList();
        // 2,000 files in the download list: tiny dummy videos (1 KiB, no real video bytes); half are tidy, half need organizing.
        var jobs = new List<PcDownloadJob>();
        var bytes = Enumerable.Range(0, 1024).Select(n => (byte)(n % 251)).ToArray();
        var fileEntries = showEpisodes
            .Where((_, n) => n % 6 == 1)
            .SelectMany(l => l)
            .Take(D(FileCount) - D(300))
            .Concat(movies.Skip(D(1000)).Take(D(300)))
            .ToList();
        for (var n = 0; n < fileEntries.Count; n++)
        {
            var e = fileEntries[n];
            var i = e.Item;
            var title = $"{i.Title} ({i.Year})";
            var dotted = i.Title.Replace(' ', '.');
            var root = i.IsMovie ? movieRoot : tvRoot;
            var path =
                n % 2 == 0
                    ? i.IsMovie
                        ? Path.Combine(root, title, title + ".mkv")
                        : Path.Combine(
                            root,
                            title,
                            $"Season {i.Season:00}",
                            $"{title} S{i.Season:00}E{i.Episode:00}.mkv"
                        )
                    : i.IsMovie
                        ? Path.Combine(root, "incoming", $"{dotted}.{i.Year}.1080p.WEB.mkv")
                        : Path.Combine(root, "incoming", $"{dotted}.S{i.Season:00}E{i.Episode:00}.1080p.WEB.mkv");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            i.Available = true;
            i.Path = path;
            e.Paths = [path];
            var hash = new byte[20];
            random.NextBytes(hash);
            jobs.Add(
                new()
                {
                    Id = Id(),
                    RequestId = Id(),
                    Name = Path.GetFileName(path),
                    Hash = Convert.ToHexString(hash).ToLowerInvariant(),
                    Service = "TorBox",
                    Root = root,
                    Destination = path,
                    Size = bytes.Length,
                    Bytes = bytes.Length,
                    Status = "Completed",
                    Message = "Saved to the PC library",
                    CreatedAt = Base.AddDays(-random.Next(1, 700)),
                    Target = JsonSerializer.Deserialize<VideoItem>(JsonSerializer.Serialize(i))!,
                }
            );
        }
        // Keep ready on 300 shows; the 500 oldest episode downloads are Ready-managed files with watched cleanup on.
        foreach (var list in showEpisodes.Where((_, n) => n % 6 == 1 || n % 5 == 2).Take(D(300)))
            s.ShowDownloads[list[0].Item.GroupKey] = new() { KeepReadyEnabled = true, KeepReadyCount = 2 };
        var ready = new NoirReadyState { Migrated = true, CleanupWatched = true };
        foreach (var job in jobs.Where(j => !j.Target.IsMovie).Take(D(500)))
            ready.ManagedFiles.Add(
                new()
                {
                    JobId = job.Id,
                    ItemId = job.Target.Id,
                    Group = job.Target.GroupKey,
                    Path = job.Destination,
                    Title = job.Target.Label,
                    ChargedBytes = job.Size,
                }
            );
        // 4,000 pending Trakt jobs for the newest viewings (the switch stays off, so nothing is ever sent).
        foreach (
            var (itemId, at) in s
                .Viewings.Select(p => (p.Key, p.Value.Max(v => v.WatchedAt)))
                .OrderByDescending(p => p.Item2)
                .Take(D(SyncJobCount))
        )
            s.WatchSync["Trakt:" + itemId] = new()
            {
                Provider = "Trakt",
                ItemId = itemId,
                WatchedAt = at,
            };
        store.Save();
        File.WriteAllBytes(
            Path.Combine(dir, "pc-downloads.json"),
            JsonSerializer.SerializeToUtf8Bytes(jobs, new JsonSerializerOptions { WriteIndented = true })
        );
        File.WriteAllBytes(Path.Combine(dir, "noir-ready.json"), JsonSerializer.SerializeToUtf8Bytes(ready));
        // One scan settles the files against their catalog entries, as the app's first start would.
        var scanned = store.Scan(out var warnings);
        store.Save();
        File.WriteAllText(
            Path.Combine(dir, "scale-profile.txt"),
            "Synthetic S5 scale profile (Noir.Checks --generate-scale). Generated data only; no user data.\n"
        );
        Console.WriteLine(
            $"SCALE PROFILE: {shows + movieCount} titles ({shows} shows, {movieCount} movies), {s.Catalog.Count} catalog items, "
                + $"{s.Viewings.Values.Sum(v => v.Count)} viewings, "
                + $"{s.PersonalReviews.Count + s.SeasonRatings.Count + s.ShowRatings.Count} ratings, {jobs.Count} download files, "
                + $"{s.WatchSync.Count} sync jobs, scan found {scanned.Count} files ({warnings.Count} warnings), "
                + $"library.json {new FileInfo(Path.Combine(dir, "library.json")).Length / 1024} KiB, {clock.ElapsedMilliseconds} ms"
        );
    }

    // S6 fixes (scale report items 2-6): compact library.json with a renamed .bak, check-offs written off
    // the caller's thread, one awaited save per sync job, indexed matching for imports and merges (same
    // answers as the catalog scans) and no duplicate viewings when a backup is merged twice.
    public static async Task Run(string root, Action<bool, string> check)
    {
        var dir = Path.Combine(root, "s6-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        SaveFormat(Path.Combine(dir, "save"), check);
        await CheckOff(Path.Combine(dir, "mark"), check);
        await SyncSaves(Path.Combine(dir, "sync"), check);
        MergeTwice(Path.Combine(dir, "merge-unit"), check);
        for (var run = 0; run < 3; run++)
        {
            var (ok, line) = Stress(Path.Combine(dir, "stress-" + run));
            check(ok, "S6 stress: " + line);
        }
        await Equivalence(Path.Combine(dir, "equal"), check);
    }

    // 500 check-offs from one ("UI") thread while other threads queue sync-job saves, playback ticks,
    // direct saves and a reopen of the same folder; then every mark is on disk, the file loads cleanly and
    // no temp file is left.
    public static (bool Ok, string Line) Stress(string dir)
    {
        Directory.CreateDirectory(dir);
        var store = new LibraryStore(dir);
        store.State.Roots = [];
        var marks = new List<VideoItem>();
        var ticks = new List<VideoItem>();
        for (var n = 0; n < 550; n++)
        {
            var item = new VideoItem
            {
                Id = "stress-" + n,
                Title = "Stress Show",
                Season = 1 + n / 100,
                Episode = 1 + n % 100,
                CollectionKey = "show:stress",
                Duration = 1500,
            };
            store.State.Catalog[item.Id] = new() { Item = item };
            (n < 500 ? marks : ticks).Add(item);
        }
        store.Save();
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var done = 0;
        void Background(Action step)
        {
            try
            {
                for (var n = 0; Volatile.Read(ref done) == 0; n++)
                {
                    step();
                    Thread.Sleep(n % 3);
                }
            }
            catch (Exception e)
            {
                errors.Enqueue(e);
            }
        }
        var tick = 0;
        var reopened = 0;
        var others = new[]
        {
            new Thread(() => Background(store.SaveDeferred)),
            new Thread(() =>
                Background(() =>
                {
                    var item = ticks[tick++ % ticks.Count];
                    store.Record(item, 60 + tick % 600, 1500, false, null, true);
                })
            ),
            new Thread(() =>
                Background(() =>
                {
                    store.Save();
                    Thread.Sleep(5);
                })
            ),
            new Thread(() =>
                Background(() =>
                {
                    // A reopen reads the file back: it must find a whole, loadable library.
                    if (new LibraryStore(dir).LoadWarning.Length > 0)
                        throw new IOException("a reopen found an unreadable library");
                    reopened++;
                    Thread.Sleep(20);
                })
            ),
        };
        var clock = Stopwatch.StartNew();
        foreach (var t in others)
            t.Start();
        try
        {
            foreach (var item in marks)
                store.Mark(item, true);
        }
        catch (Exception e)
        {
            errors.Enqueue(e);
        }
        Volatile.Write(ref done, 1);
        foreach (var t in others)
            t.Join();
        store.FlushPending();
        var elapsed = clock.ElapsedMilliseconds;
        var reload = new LibraryStore(dir);
        var missing = marks.Count(m => reload.GetProgress(m)?.Completed != true || reload.ViewingHistory(m).Count != 1);
        var temps = Directory.EnumerateFiles(dir, "*.tmp").Count();
        var parses = reload.LoadWarning.Length == 0;
        var ok = errors.IsEmpty && missing == 0 && temps == 0 && parses;
        return (
            ok,
            $"500 check-offs with concurrent sync saves, playback ticks, direct saves and {reopened} reopens: "
                + $"{500 - missing}/500 on disk after reload, {(parses ? "file loads cleanly" : "LOAD WARNING")}, "
                + $"{temps} temp files, {errors.Count} errors{(errors.TryPeek(out var first) ? " (" + first.Message + ")" : "")}, {elapsed} ms"
        );
    }

    static void SaveFormat(string dir, Action<bool, string> check)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "library.json");
        var store = new LibraryStore(dir);
        store.State.Roots = [];
        store.State.Volume = 41;
        store.Save();
        var first = File.ReadAllBytes(path);
        store.State.Volume = 42;
        store.Save();
        var text = File.ReadAllText(path);
        check(
            !text.Contains('\n') && text.Contains("\"Volume\":42"),
            "S6 save: library.json is written without indentation"
        );
        check(
            File.ReadAllBytes(path + ".bak").SequenceEqual(first) && !File.Exists(path + ".tmp"),
            "S6 save: the previous library.json becomes the .bak (rotated by rename), no temp file left"
        );
        // A write that stopped between the two renames: library.json is gone, the .bak is the last good file.
        File.Move(path, path + ".tmp", true);
        var reopened = new LibraryStore(dir);
        check(
            reopened.State.Volume == 41 && reopened.LoadWarning.Length > 0,
            "S6 save: interrupted between the renames, the next start loads the previous good file"
        );
        // Old indented files still load.
        var old = Path.Combine(dir, "indented");
        Directory.CreateDirectory(old);
        var state = new PlayerState { Volume = 37, Roots = [] };
        state.Catalog["indented-1"] = new()
        {
            Item = new()
            {
                Id = "indented-1",
                Title = "Indented Movie",
                IsMovie = true,
                CollectionKey = "movie:indented-1",
            },
        };
        File.WriteAllText(
            Path.Combine(old, "library.json"),
            JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true })
        );
        var loaded = new LibraryStore(old);
        check(
            loaded.State.Volume == 37 && loaded.State.Catalog.ContainsKey("indented-1"),
            "S6 save: an older indented library.json still loads"
        );
    }

    static async Task CheckOff(string dir, Action<bool, string> check)
    {
        Directory.CreateDirectory(dir);
        var store = new LibraryStore(dir);
        store.State.Roots = [];
        var item = new VideoItem
        {
            Id = "s6-mark",
            Title = "Check Off Show",
            Season = 1,
            Episode = 1,
            CollectionKey = "show:checkoff",
            Duration = 1500,
        };
        store.LogWatched([new LogEntry(item, DateTimeOffset.UtcNow.AddDays(-1))], "Setup", queueSync: false);
        await store.FlushPendingAsync();
        var episode = store.Archive.First(i => i.Id == "s6-mark");
        store.Mark(episode, false);
        await store.FlushPendingAsync();
        // While Mark runs on this thread, a write on this same thread is a synchronous one.
        var caller = Environment.CurrentManagedThreadId;
        var inMark = false;
        int writes = 0,
            duringMark = 0;
        store.WritingSnapshot = () =>
        {
            Interlocked.Increment(ref writes);
            if (Volatile.Read(ref inMark) && Environment.CurrentManagedThreadId == caller)
                Interlocked.Increment(ref duringMark);
        };
        Volatile.Write(ref inMark, true);
        store.Mark(episode, true);
        Volatile.Write(ref inMark, false);
        await store.FlushPendingAsync();
        store.WritingSnapshot = null;
        check(
            writes > 0 && duringMark == 0,
            "S6 check-off: Mark writes library.json on the save worker, not on the caller's (UI) thread"
        );
        check(
            new LibraryStore(dir).GetProgress(episode)?.Completed == true,
            "S6 check-off: the deferred write still persists the check-off"
        );
        // Bounded delay: with nothing else happening, an un-check is on disk within 2 s (no flush asked).
        store.Mark(episode, false);
        var landed = Stopwatch.StartNew();
        var onDisk = false;
        while (!onDisk && landed.ElapsedMilliseconds < 2000)
        {
            try
            {
                var disk = JsonSerializer.Deserialize<PlayerState>(
                    File.ReadAllText(Path.Combine(dir, "library.json")),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                )!;
                onDisk = disk.Catalog[episode.Id].Progress?.Completed == false;
            }
            catch (IOException) { }
            if (!onDisk)
                Thread.Sleep(20);
        }
        check(
            onDisk,
            $"S6 check-off: a check-off reaches the disk by itself within 2 s ({landed.ElapsedMilliseconds} ms)"
        );
        await store.FlushPendingAsync();
    }

    static async Task SyncSaves(string dir, Action<bool, string> check)
    {
        Directory.CreateDirectory(dir);
        var store = new LibraryStore(dir);
        store.State.Roots = [];
        store.State.SyncTrakt = true;
        var start = DateTimeOffset.UtcNow.AddHours(-3);
        for (var n = 0; n < 4; n++)
        {
            var item = new VideoItem
            {
                Id = "s6-sync-" + n,
                Title = "Sync Movie " + n,
                IsMovie = true,
                ImdbId = "tt99000" + n,
                CollectionKey = "movie:s6-sync-" + n,
            };
            store.State.Catalog[item.Id] = new() { Item = item };
            CommunityQueue.Enqueue(store.State, item, start.AddMinutes(n));
        }
        store.Save();
        var now = DateTimeOffset.UtcNow;
        int saves = 0;
        var sent = new List<string>();
        var onDiskBeforeEachSend = true;
        await CommunityQueue.Pass(
            store.State,
            new CommunityBackoff(store.State.SyncPace),
            () => now = now.AddSeconds(1),
            j => j.ItemId,
            _ => true,
            job =>
            {
                // Every job confirmed earlier is already on disk: a crash now resends only this one.
                var disk = JsonSerializer.Deserialize<PlayerState>(
                    File.ReadAllText(Path.Combine(dir, "library.json")),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                )!;
                onDiskBeforeEachSend &= sent.All(id => disk.WatchSync["Trakt:" + id].SyncedAt != null);
                sent.Add(job.ItemId);
                return Task.FromResult(true);
            },
            () =>
            {
                saves++;
                store.SaveDeferred();
            },
            d =>
            {
                now += d;
                return Task.CompletedTask;
            },
            async () => await store.FlushPendingAsync()
        );
        await store.FlushPendingAsync();
        check(
            sent.Count == 4 && saves == 4,
            $"S6 sync queue: one save per sent job (4 sent, {saves} saves; was two full saves per job)"
        );
        check(onDiskBeforeEachSend, "S6 sync queue: each confirmed send is on disk before the next send starts");
        check(
            new LibraryStore(dir).State.WatchSync.Values.All(j => j.SyncedAt != null),
            "S6 sync queue: the deferred saves persist every job's state"
        );
    }

    // A viewing dated before the episode aired (older data, other imports), backed up and merged back twice.
    static void MergeTwice(string dir, Action<bool, string> check)
    {
        Directory.CreateDirectory(dir);
        var store = new LibraryStore(dir);
        store.State.Roots = [];
        var item = new VideoItem
        {
            Id = "s6-early",
            Title = "Early Show",
            Season = 1,
            Episode = 2,
            CollectionKey = "show:early",
            ImdbId = "tt9911000",
        };
        var watched = new DateTimeOffset(2025, 3, 1, 21, 0, 0, TimeSpan.Zero);
        store.State.Catalog[item.Id] = new() { Item = item };
        store.State.EpisodeSchedules[item.GroupKey] =
        [
            new()
            {
                Season = 1,
                Episode = 2,
                AirsAt = watched.AddDays(10),
            },
        ];
        store.State.Viewings[item.Id] =
        [
            new()
            {
                Id = "early-1",
                WatchedAt = watched,
                Source = "Imported from Trakt",
                LoggedAt = watched,
            },
        ];
        store.Save();
        var backup = Path.Combine(dir, "early.noirbackup");
        store.ExportBackup(backup);
        var first = store.MergeBackup(LibraryStore.ReadBackup(backup));
        var second = store.MergeBackup(LibraryStore.ReadBackup(backup));
        var views = store.ViewingHistory(item);
        check(
            first == 0 && second == 0 && views.Count == 1 && views[0].WatchedAt == watched,
            $"S6 merge: a viewing dated before its air date, merged back twice, adds 0 and keeps its date (added {first}, {second}; {views.Count} viewing)"
        );
    }

    // The generated library at 1/30: merge-restore and import give the same result with the match index
    // as with the original catalog scans, and merging the library's own backup twice adds nothing.
    static async Task Equivalence(string dir, Action<bool, string> check)
    {
        var profile = Path.Combine(dir, "profile");
        Generate(profile, 30);
        var source = new LibraryStore(profile);
        var backup = Path.Combine(dir, "scale.noirbackup");
        source.ExportBackup(backup);
        var twice = new[]
        {
            source.MergeBackup(LibraryStore.ReadBackup(backup)),
            source.MergeBackup(LibraryStore.ReadBackup(backup)),
        };
        check(
            twice[0] == 0 && twice[1] == 0,
            $"S6 merge: the generated library's own backup merged twice adds 0 (added {twice[0]}, {twice[1]})"
        );
        string Fingerprint(LibraryStore s) =>
            string.Join(
                "\n",
                s.State.Catalog.Values.Select(e =>
                        $"{e.Item.GroupKey}|{e.Item.Title}|{e.Item.Season}|{e.Item.Episode}|{e.Item.IsMovie}|{s.State.Viewings.GetValueOrDefault(e.Item.Id)?.Count ?? 0}"
                    )
                    .Order(StringComparer.Ordinal)
            );
        var results = new List<(string Catalog, int Added, double Ms)>();
        foreach (var off in new[] { true, false })
        {
            LibraryStore.MatchIndexOff = off;
            try
            {
                var empty = new LibraryStore(Path.Combine(dir, off ? "scan" : "index"));
                empty.State.Roots = [];
                var clock = Stopwatch.StartNew();
                var added = empty.MergeBackup(LibraryStore.ReadBackup(backup));
                // Then an import of 40 shows x 50 episodes into that library.
                var csv = new StringBuilder(
                    "type,title,year,imdb_id,season,episode,episode_title,watched_at,date_unknown,source,rating,approximate\r\n"
                );
                for (var r = 0; r < 2000; r++)
                {
                    var show = r / 50;
                    csv.Append(
                        $"episode,Import Show {show},{2000 + show % 25},tt{6100000 + show},{1 + r % 50 / 10},{1 + r % 10},Part {1 + r % 10},{Base.AddDays(-2000 + r):O},false,Trakt,0,false\r\n"
                    );
                }
                var file = Path.Combine(dir, (off ? "scan" : "index") + ".csv");
                File.WriteAllText(file, csv.ToString());
                var read = HistoryFormats.Read(file);
                using var discovery = new DiscoveryClient(
                    Path.Combine(dir, (off ? "scan" : "index") + "-discovery"),
                    new MetaFixture()
                );
                var importer = new HistoryImporter(empty, discovery, new HttpClient(new MetaFixture()));
                var plan = await importer.Resolve(read.Service, read.Rows, null, CancellationToken.None);
                added += importer.Apply(plan);
                results.Add((Fingerprint(empty), added, clock.Elapsed.TotalMilliseconds));
            }
            finally
            {
                LibraryStore.MatchIndexOff = false;
            }
        }
        check(
            results[0].Catalog == results[1].Catalog && results[0].Added == results[1].Added && results[0].Added > 0,
            $"S6 matching: merge-restore + import into an empty profile give the same catalog and viewings with the index as with the catalog scans ({results[1].Added} added; scans {results[0].Ms:0} ms, index {results[1].Ms:0} ms)"
        );
    }

    public static void MergeProbe(string source)
    {
        var work = Path.Combine(Path.GetTempPath(), "noir-scale-merge-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path.GetFullPath(source)))
                File.Copy(f, Path.Combine(work, Path.GetFileName(f)));
            var store = new LibraryStore(work);
            var backup = Path.Combine(work, "probe.noirbackup");
            store.ExportBackup(backup);
            var preview = LibraryStore.ReadBackup(backup);
            for (var run = 0; run < 2; run++)
            {
                var c = Stopwatch.StartNew();
                var added = store.MergeBackup(preview);
                Console.WriteLine($"merge {run + 1}: {c.ElapsedMilliseconds} ms, {added} added");
            }
        }
        finally
        {
            Directory.Delete(work, true);
        }
    }

    public static async Task Measure(string source, string? reportPath)
    {
        source = Path.GetFullPath(source);
        if (!File.Exists(Path.Combine(source, "scale-profile.txt")))
            throw new IOException("Not a generated scale profile: " + source);
        var work = Path.Combine(Path.GetTempPath(), "noir-scale-" + Guid.NewGuid().ToString("N")[..8]);
        var lines = new List<string> { "step\tms\tnote" };
        void Note(string step, double ms, string note = "")
        {
            lines.Add($"{step}\t{ms:0}\t{note}");
            Console.WriteLine($"{step, -48} {ms, 8:0} ms  {note}");
        }
        string Copy(string name)
        {
            var target = Path.Combine(work, name);
            Directory.CreateDirectory(target);
            foreach (var f in Directory.EnumerateFiles(source))
                File.Copy(f, Path.Combine(target, Path.GetFileName(f)));
            return target;
        }
        static double Ms(Action a)
        {
            var c = Stopwatch.StartNew();
            a();
            return c.Elapsed.TotalMilliseconds;
        }
        static async Task<double> MsAsync(Func<Task> a)
        {
            var c = Stopwatch.StartNew();
            await a();
            return c.Elapsed.TotalMilliseconds;
        }
        static string Kb(string path) => $"{new FileInfo(path).Length / 1024:N0} KiB";
        try
        {
            var dir = Copy("main");
            LibraryStore store = null!;
            Note(
                "Load library (LibraryStore ctor)",
                Ms(() => store = new LibraryStore(dir)),
                "library.json " + Kb(Path.Combine(dir, "library.json"))
            );
            var count = 0;
            Note("Archive (first)", Ms(() => count = store.Archive.Count), count + " items");
            Note("Archive (again)", Ms(() => count = store.Archive.Count));
            Note("LibraryGroups", Ms(() => count = store.LibraryGroups().Count), count + " groups");
            Note("Scan 2 roots (startup scan)", Ms(() => count = store.Scan(out _).Count), count + " files");
            var saves = Enumerable.Range(0, 5).Select(_ => Ms(store.Save)).OrderBy(x => x).ToList();
            Note(
                "Save (median of 5)",
                saves[2],
                $"min {saves[0]:0} max {saves[4]:0}; library.json " + Kb(Path.Combine(dir, "library.json"))
            );
            // S6: library.json is written compact now (same options as LibraryStore's state writes).
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            PlayerState copy = null!;
            Note("  of which: snapshot copy (caller thread)", Ms(() => copy = LibrarySnapshot.Copy(store.State)));
            byte[] data = [];
            Note(
                "  of which: serialize",
                Ms(() => data = JsonSerializer.SerializeToUtf8Bytes(copy, options)),
                $"{data.Length / 1024:N0} KiB"
            );
            Note("SaveDeferred caller-thread cost", Ms(store.SaveDeferred));
            await store.FlushPendingAsync();
            var episode = store.Archive.First(i => !i.IsMovie && !store.EverWatched(i) && i.Season > 0);
            Note(
                "Playback progress tick (Record, deferred)",
                Ms(() => store.Record(episode, 120, 2700, false, null, true))
            );
            await store.FlushPendingAsync();
            Note("Check-off (Mark, caller thread)", Ms(() => store.Mark(episode, true)));
            Note("Un-check (Mark false, caller thread)", Ms(() => store.Mark(episode, false)));
            Note("  check-off writes landing (background)", await MsAsync(store.FlushPendingAsync));
            MobileSnapshot snapshot = null!;
            Note("Phone snapshot build", Ms(() => snapshot = store.MobileSnapshot()));
            string json = "";
            Note(
                "Phone snapshot serialize",
                Ms(() => json = JsonSerializer.Serialize(snapshot)),
                $"{Encoding.UTF8.GetByteCount(json) / 1024:N0} KiB raw"
            );
            using (var gz = new MemoryStream())
            {
                var c = Stopwatch.StartNew();
                using (var z = new GZipStream(gz, CompressionLevel.Fastest, true))
                    z.Write(Encoding.UTF8.GetBytes(json));
                Note(
                    "Phone snapshot gzip (fastest)",
                    c.Elapsed.TotalMilliseconds,
                    $"{gz.Length / 1024:N0} KiB on the wire"
                );
            }
            Note("MobileRevision", Ms(() => store.MobileRevision()));

            var backup = Path.Combine(work, "scale.noirbackup");
            Note("Backup export", Ms(() => store.ExportBackup(backup)), Kb(backup));
            BackupPreview preview = null!;
            Note(
                "Backup read (restore preview)",
                Ms(() => preview = LibraryStore.ReadBackup(backup)),
                preview.Viewings + " viewings"
            );
            var merged = 0;
            var other = new LibraryStore(Copy("merge"));
            Note(
                "Merge-restore into the same library (copy)",
                Ms(() => merged = other.MergeBackup(preview)),
                merged + " added (expected 0)"
            );
            Note(
                "Merge-restore the same backup again",
                Ms(() => merged = other.MergeBackup(preview)),
                merged + " added (expected 0)"
            );
            // S5 timed samples (the merge was quadratic); S6 also runs the whole backup into an empty profile.
            foreach (var n in new[] { 250, 1000, int.MaxValue })
            {
                var sample = LibraryStore.ReadBackup(backup);
                var keep = sample.State.Viewings.Keys.Take(n).ToHashSet();
                foreach (var key in sample.State.Viewings.Keys.Where(k => !keep.Contains(k)).ToList())
                    sample.State.Viewings.Remove(key);
                foreach (var key in sample.State.Catalog.Keys.Where(k => !keep.Contains(k)).ToList())
                    sample.State.Catalog.Remove(key);
                var empty = new LibraryStore(Path.Combine(work, "empty-" + Math.Min(n, 99999)));
                empty.State.Roots = [];
                var ms = Ms(() => merged = empty.MergeBackup(sample));
                Note(
                    n == int.MaxValue
                        ? $"Merge-restore the full backup into an empty profile"
                        : $"Merge-restore into an empty profile, {n} items",
                    ms,
                    $"{merged} added, {empty.State.Catalog.Count} items"
                );
            }
            foreach (var format in new[] { "noir-csv", "noir-json", "trakt" })
            {
                var file = Path.Combine(work, "export-" + format + (format == "noir-json" ? ".json" : ".csv"));
                var rows = 0;
                Note(
                    "Export " + format,
                    Ms(() => rows = HistoryExport.Write(store, file, format)),
                    $"{rows} rows, {Kb(file)}"
                );
            }

            // 20,000 new rows (400 shows x 50 episodes) in Noir's CSV, resolved against an offline Cinemeta fixture.
            var csv = Path.Combine(work, "import-20000.csv");
            var text = new StringBuilder(
                "type,title,year,imdb_id,season,episode,episode_title,watched_at,date_unknown,source,rating,approximate\r\n"
            );
            for (var r = 0; r < ImportRows; r++)
            {
                var show = r / 50;
                var (season, ep) = (1 + r % 50 / 10, 1 + r % 10);
                text.Append(
                    $"episode,Import Show {show},{2000 + show % 25},tt{6100000 + show},{season},{ep},Part {ep},{Base.AddDays(-ImportRows + r):O},false,Trakt,0,false\r\n"
                );
            }
            File.WriteAllText(csv, text.ToString());
            (string Service, List<ImportRow> Rows) read = default;
            Note(
                "Import read 20,000 rows",
                Ms(() => read = HistoryFormats.Read(csv)),
                $"{read.Rows.Count} rows ({read.Service}), {Kb(csv)}"
            );
            using var discovery = new DiscoveryClient(Path.Combine(work, "discovery"), new MetaFixture());
            var importer = new HistoryImporter(store, discovery, new HttpClient(new MetaFixture()));
            ImportPlan plan = null!;
            Note(
                "Import resolve (offline fixture)",
                await MsAsync(async () =>
                    plan = await importer.Resolve(read.Service, read.Rows, null, CancellationToken.None)
                ),
                $"{plan.Titles.Count} titles, {plan.Unmatched.Count} unmatched"
            );
            var added = 0;
            // Apply grows with the catalog per new item (see report): apply 4 titles, then 16 more, and project 400.
            var applied = 0;
            // S6: then the remaining 380 titles (19,000 rows) in one apply.
            foreach (var k in new[] { 4, 16, 380 })
            {
                var part = new ImportPlan(plan.Service, plan.Titles.Skip(applied).Take(k).ToList(), []);
                applied += k;
                var ms = Ms(() => added = importer.Apply(part));
                Note(
                    $"Import apply {k} titles ({k * 50} rows) into the big library",
                    ms,
                    $"{added} added; {ms / (k * 50):0.0} ms/row"
                );
            }

            // Downloads: the 2,000-file list and its scans, with a provider that never reaches the network.
            PcDownloads downloads = null!;
            Note(
                "Downloads load (2,000 jobs)",
                Ms(() => downloads = new PcDownloads(store, new EmptyProvider(), start: false)),
                Kb(Path.Combine(dir, "pc-downloads.json"))
            );
            List<DownloadLocalGroup> local = null!;
            Note(
                "Downloads LocalLibrary",
                Ms(() => local = downloads.LocalLibrary()),
                $"{local.Count} groups, {local.Sum(g => g.Files.Count)} files"
            );
            Note("Downloads file snapshot", Ms(() => count = store.DownloadFileSnapshot().Count), count + " files");
            List<DownloadSpaceGroup> space = null!;
            Note(
                "Free up space suggestions",
                Ms(() => space = downloads.SpaceSuggestions()),
                string.Join(", ", space.Select(g => $"{g.Kind}: {g.Items.Count}"))
            );
            var jobsFile = Path.Combine(work, "pc-downloads-save.json");
            Note(
                "Download list save (serialize + write, as PcDownloads.Save)",
                Ms(() =>
                    File.WriteAllBytes(
                        jobsFile,
                        JsonSerializer.SerializeToUtf8Bytes(
                            downloads.Jobs(),
                            new JsonSerializerOptions { WriteIndented = true }
                        )
                    )
                ),
                Kb(jobsFile)
            );
            OrganizationPlan organize = null!;
            Note(
                "Organizer preview (PlanOrganization)",
                Ms(() => organize = store.PlanOrganization()),
                $"{organize.Moves.Count(m => m.Video)} videos to move, {organize.Skipped.Count} skipped"
            );
            downloads.InitializeReady();
            Note(
                "Ready inputs (ReadyInputs)",
                Ms(() => count = store.ReadyInputs(DateTimeOffset.UtcNow).Count),
                count + " episodes in 300 Keep-ready shows"
            );
            Note(
                "Ready cleanup inputs (500 managed files)",
                Ms(() =>
                    count = store.ReadyCleanupInputs(downloads.ReadyState.ManagedFiles.Select(f => f.ItemId)).Count
                )
            );
            Note(
                "Ready pass (PrepareReady Startup, no sources)",
                await MsAsync(() => downloads.PrepareReady("Startup")),
                downloads.ReadyState.ManagedFiles.Count(f => f.RemoveAt != null) + " files scheduled for removal"
            );
            var newcomer = new VideoItem
            {
                Id = "scale-new-download",
                Title = "Fresh Download",
                Year = 2026,
                IsMovie = true,
                ImdbId = "tt9900001",
            };
            var newPath = Path.Combine(work, "Fresh Download (2026).mkv");
            File.WriteAllBytes(newPath, new byte[1024]);
            Note(
                "Finished download registers (RegisterDownload: scan + save)",
                Ms(() => store.RegisterDownload(newcomer, newPath))
            );
            downloads.Dispose();

            // Sync queue: one pass with Trakt on, an instant fake site and a fake clock (the S2 cap stops it at 30).
            store.State.SyncTrakt = true;
            var at = DateTimeOffset.UtcNow;
            int fullSaves = 0,
                sent = 0;
            double callerMs = 0;
            var pass = await MsAsync(() =>
                CommunityQueue.Pass(
                    store.State,
                    new CommunityBackoff(store.State.SyncPace),
                    () => at = at.AddSeconds(1),
                    _ => "",
                    _ => true,
                    _ =>
                    {
                        sent++;
                        return Task.FromResult(true);
                    },
                    () =>
                    {
                        fullSaves++;
                        var c = Stopwatch.StartNew();
                        store.SaveDeferred();
                        callerMs += c.Elapsed.TotalMilliseconds;
                    },
                    _ => Task.CompletedTask,
                    async () => await store.FlushPendingAsync()
                )
            );
            // As CommunitySync runs it (S6): one deferred save per job, awaited before the next send.
            Note(
                "Sync pass (4,000 pending, capped)",
                pass,
                $"{sent} sent, {fullSaves} saves; caller thread {callerMs:0} ms in all ({callerMs / Math.Max(1, fullSaves):0} ms each)"
            );
            Note(
                "Sync status line",
                Ms(() =>
                    CommunityQueue.Status(
                        store.State,
                        new CommunityBackoff(store.State.SyncPace),
                        "Trakt",
                        DateTimeOffset.UtcNow
                    )
                )
            );
            Note("Sync newest 50", Ms(() => CommunityQueue.Newest(store.State, "Trakt")));
            store.State.SyncTrakt = false;
            store.Save();
            Console.WriteLine("SCALE MEASURE DONE");
        }
        finally
        {
            if (reportPath != null)
                File.WriteAllLines(reportPath, lines);
            try
            {
                Directory.Delete(work, true);
            }
            catch (Exception e)
            {
                Console.WriteLine("Note: could not remove " + work + ": " + e.Message);
            }
        }
    }

    sealed class EmptyProvider : IDownloadProvider
    {
        public Task<List<IndexedRelease>> Search(VideoItem item, CancellationToken ct) =>
            Task.FromResult(new List<IndexedRelease>());

        public Task<List<TorboxFile>> Files(string hash, CancellationToken ct) => throw new IOException("offline");

        public Task<TorboxTorrent> Prepare(string hash, long torrentId, CancellationToken ct) =>
            throw new IOException("offline");

        public Task<string> Link(long torrentId, long fileId, CancellationToken ct) => throw new IOException("offline");
    }

    // Cinemeta for the import: every tt61xxxxx series has 5 seasons of 10 episodes. Anything else is refused.
    sealed class MetaFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.Host != "v3-cinemeta.strem.io" || !uri.AbsolutePath.StartsWith("/meta/series/tt61"))
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") }
                );
            var id = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            var show = int.Parse(id[2..]) - 6100000;
            var videos = Enumerable
                .Range(0, 50)
                .Select(n => new
                {
                    id = $"{id}:{1 + n / 10}:{1 + n % 10}",
                    season = 1 + n / 10,
                    episode = 1 + n % 10,
                    name = $"Part {1 + n % 10}",
                    released = new DateTimeOffset(2000 + show % 25, 1, 1, 0, 0, 0, TimeSpan.Zero)
                        .AddDays(n * 7)
                        .ToString("O"),
                });
            var json = JsonSerializer.Serialize(
                new
                {
                    meta = new
                    {
                        id,
                        name = "Import Show " + show,
                        releaseInfo = (2000 + show % 25).ToString(),
                        videos,
                    },
                }
            );
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
