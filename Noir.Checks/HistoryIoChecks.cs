using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Noir;

// Undo imports, sources, full backups, lossless Noir JSON and the Nuvio protocol (against a fake server).
static class HistoryIoChecks
{
    static readonly DateTimeOffset At = new(2024, 5, 1, 20, 0, 0, TimeSpan.Zero);

    static LibraryStore Store(string dir)
    {
        var s = new LibraryStore(dir);
        s.State.Roots = [];
        return s;
    }

    public static async Task Run(string root, Action<bool, string> check)
    {
        var dir = Path.Combine(root, "historyio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var store = Store(Path.Combine(dir, "main"));
        var movie = new VideoItem
        {
            Title = "Heat",
            IsMovie = true,
            Year = 1995,
            ImdbId = "tt0113277",
            Available = false,
        };
        var episode = new VideoItem
        {
            Title = "Show",
            Season = 1,
            Episode = 2,
            ImdbId = "tt0000500",
            Available = false,
        };
        store.LogWatched([new(movie, At)], "Playback");
        var batch = store.BeginImport("Fixture");
        var added = store.LogWatched(
            [new(episode, At.AddDays(1), false, 4), new(movie, At.AddDays(30))],
            "Imported from Fixture",
            false,
            true,
            batch
        );
        store.EndImport(batch);
        movie = store.Archive.Single(i => i.Title == "Heat");
        episode = store.Archive.Single(i => i.Title == "Show");
        check(
            added == 2
                && store.State.ImportBatches.Single().Added == 2
                && store.State.ImportBatches[0].RatedItemIds.SequenceEqual([episode.Id]),
            "History import batches count viewings and the ratings they created"
        );
        check(
            store.Sources().Any(s => s.Source == "Imported from Fixture" && s.Viewings == 2 && s.Titles == 2)
                && store.Sources().Any(s => s.Source == "Playback" && s.Viewings == 1),
            "History sources group viewings and titles by where they came from"
        );
        var empty = store.BeginImport("Nothing");
        store.EndImport(empty);
        check(store.State.ImportBatches.Count == 1, "History imports that add nothing leave no undo entry");
        var undone = store.UndoImport(batch);
        check(
            undone == 2
                && store.ViewingHistory(movie).Count == 1
                && store.ViewingHistory(movie)[0].Source == "Playback"
                && !store.EverWatched(episode)
                && !store.State.PersonalReviews.ContainsKey(episode.Id)
                && store.State.ImportBatches.Count == 0,
            "History Undo import removes only that import's viewings and ratings and keeps Noir's own"
        );
        store.LogWatched([new(episode, At.AddDays(1))], "Imported from Fixture", false, true);
        var removed = store.RemoveSource("Imported from Fixture");
        check(
            removed.Count == 1 && !store.EverWatched(episode) && store.EverWatched(movie),
            "History Remove source clears that source and returns the items to unwatched"
        );
        store.Restore(removed);
        check(
            store.EverWatched(episode) && store.ViewingHistory(episode)[0].Source == "Imported from Fixture",
            "History Remove source can be undone"
        );
        check(
            store.MobileSnapshot().Items.Single(i => i.Id == episode.Id).Viewings.Count == 1,
            "History imported viewings are in the phone snapshot"
        );

        // Lossless Noir JSON: source, logged time, note and show rating survive a round trip.
        store.State.PersonalReviews[movie.Id] = new() { Rating = 5, Note = "Diner scene" };
        store.RateShow(episode.GroupKey, 4);
        store.Save();
        var json = Path.Combine(dir, "noir.json");
        HistoryExport.Write(store, json, "noir-json");
        var read = HistoryFormats.Read(json);
        var heat = read.Rows.Where(r => r.Title == "Heat").ToList();
        check(
            heat.Any(r => r.Source == "Playback" && r.LoggedAt != null && r.Note == "Diner scene")
                && read.Rows.Any(r => r.Title == "Show" && r.ShowRating == 4),
            "History Noir JSON keeps source, logged time, notes and show ratings"
        );
        var csv = Path.Combine(dir, "noir.csv");
        HistoryExport.Write(store, csv, "noir-csv");
        check(
            File.ReadLines(csv).First().EndsWith(",approximate")
                && HistoryFormats.Read(csv).Rows.Any(r => r.Source == "Playback"),
            "History Noir CSV carries approximate and the original source"
        );

        // Backups: merge into an empty profile, twice, then replace.
        var art = Path.Combine(store.DataDirectory, "art");
        Directory.CreateDirectory(art);
        foreach (var name in new[] { "poster.jpg", "backdrop.jpg", "episode.jpg" })
            File.WriteAllBytes(Path.Combine(art, name), [1, 2, 3]);
        var image = store.Archive.First();
        image.Poster = Path.Combine(art, "poster.jpg");
        image.Backdrop = Path.Combine(art, "backdrop.jpg");
        image.EpisodeImage = Path.Combine(art, "episode.jpg");
        store.Save();
        var file = Path.Combine(dir, "all" + LibraryStore.BackupExtension);
        store.ExportBackup(file);
        check(
            LibraryStore.IsBackup(file) && new ZipArchive(File.OpenRead(file)).GetEntry("manifest.json") != null,
            "History backup writes a manifest and the library"
        );
        var preview = LibraryStore.ReadBackup(file);
        var remote = preview.State.Catalog[image.Id].Item;
        remote.Poster = @"Z:\other-computer\art\poster.jpg";
        remote.Backdrop = @"Z:\other-computer\art\backdrop.jpg";
        remote.EpisodeImage = @"Z:\other-computer\art\episode.jpg";
        check(
            preview.Viewings == store.State.Viewings.Values.Sum(v => v.Count)
                && preview.Movies == 1
                && preview.Shows == 1
                && preview.Notes == 1
                && preview.Created != null,
            "History backup preview counts viewings, titles, ratings and notes"
        );
        var other = Store(Path.Combine(dir, "other"));
        var merged = other.MergeBackup(preview);
        var heat2 = other.Archive.Single(i => i.Title == "Heat");
        var restored = other.Archive.Single(i => i.Id == image.Id);
        check(
            new[] { restored.Poster, restored.Backdrop, restored.EpisodeImage }.All(p =>
                p.StartsWith(Path.Combine(other.DataDirectory, "art")) && File.Exists(p)
            ),
            "History merge rebases artwork from another computer before remembering restored items"
        );
        check(
            merged == preview.Viewings
                && other.ViewingHistory(heat2).Single().Source == "Playback"
                && other.ViewingHistory(heat2)[0].LoggedAt == store.ViewingHistory(movie)[0].LoggedAt
                && other.State.PersonalReviews[heat2.Id].Note == "Diner scene"
                && other.State.ShowRatings.Count == 1,
            "History merge restores viewings with their source and logging time, notes and show ratings"
        );
        check(other.MergeBackup(preview) == 0, "History merging the same backup twice adds nothing");
        other.State.PersonalReviews[heat2.Id] = new() { Rating = 2, Note = "Mine" };
        other.Save();
        other.MergeBackup(preview);
        check(
            other.State.PersonalReviews[heat2.Id] is { Rating: 2, Note: "Mine" },
            "History merge never overwrites existing ratings or notes"
        );
        var instance = other.State.MobileInstanceId;
        other.StageReplace(preview);
        var reopened = new LibraryStore(other.DataDirectory);
        check(
            reopened.State.MobileInstanceId == instance
                && reopened.State.PersonalReviews.Values.Any(r => r.Note == "Diner scene")
                && !Directory.Exists(Path.Combine(other.DataDirectory, "restore-pending"))
                && Directory.EnumerateFiles(reopened.AutoBackupFolder, "Noir-before-restore-*").Any(),
            "History replace restores on next start, keeps phone pairing and saves a safety backup first"
        );
        var old = Path.Combine(dir, "Noir-backup-old.zip");
        using (var zip = ZipFile.Open(old, ZipArchiveMode.Create))
            zip.CreateEntryFromFile(Path.Combine(store.DataDirectory, "library.json"), "library.json");
        check(
            LibraryStore.IsBackup(old) && LibraryStore.ReadBackup(old).Viewings == preview.Viewings,
            "History restore accepts older backup zips without a manifest"
        );
        bool damaged = false;
        File.WriteAllText(Path.Combine(dir, "bad.noirbackup"), "nope");
        try
        {
            LibraryStore.ReadBackup(Path.Combine(dir, "bad.noirbackup"));
        }
        catch (IOException)
        {
            damaged = true;
        }
        check(damaged, "History restore reports a damaged backup instead of crashing");

        // Nuvio protocol against a fake server.
        var fake = new FakeNuvio();
        using var http = new HttpClient(fake);
        NuvioSession? saved = null;
        var client = new NuvioClient(http, null, s => saved = s);
        var link = await client.StartLink("Noir test", CancellationToken.None);
        check(
            link.Code == "ABC-123"
                && fake.Bodies["start_device_login_session"]
                    .Contains("\"p_redirect_base_url\":\"https://nuvio.tv/link\"")
                && fake.LastApiKey == NuvioConfig.Key,
            "Nuvio link starts with the public key and nuvio.tv/link"
        );
        check(
            await client.PollLink(link, CancellationToken.None) == "pending"
                && await client.PollLink(link, CancellationToken.None) == "approved",
            "Nuvio link polling reports pending then approved"
        );
        await client.Exchange(link, CancellationToken.None);
        check(saved is { AccessToken: "a1", RefreshToken: "r1" }, "Nuvio link exchange stores the session");
        saved!.Profile = 2;
        fake.ExpireNext = true;
        var profiles = await client.Profiles(CancellationToken.None);
        check(
            profiles.Select(p => p.Index).SequenceEqual([1, 2])
                && fake.Refreshes == 1
                && saved.AccessToken == "a2"
                && fake.LastAuth == "Bearer a2",
            "Nuvio refreshes an expired sign-in once and retries"
        );
        var watched = await client.PullWatched(2, CancellationToken.None);
        check(
            watched.Count == 501
                && watched[0].WatchedAt
                    == new DateTimeOffset(2024, 1, 31, 23, 59, 59, TimeSpan.Zero).ToUnixTimeMilliseconds()
                && watched[1] is { Season: 1, Episode: 3 },
            "Nuvio pulls every page and normalizes compact timestamps"
        );
        var rows = watched.Take(2).Select(NuvioClient.Row).ToList();
        check(
            rows[0] is { IsMovie: true, ImdbId: "tt0113277", Service: "Nuvio" }
                && rows[1] is { IsMovie: false, Season: 1, Episode: 3 },
            "Nuvio items become import rows with IMDb IDs"
        );
        await client.PushWatched(2, [new("tt0113277", "movie", "Heat", null, null, 123)], CancellationToken.None);
        check(
            fake.Bodies["sync_push_watched_items"].Contains("\"p_profile_id\":2")
                && fake.Bodies["sync_push_watched_items"].Contains("\"watched_at\":123"),
            "Nuvio push sends the profile and watch time"
        );
        var events = await client.PullDelta(2, 10, CancellationToken.None);
        check(
            events.Count == 2 && events[1].Deleted && !events[0].Deleted,
            "Nuvio delta reads additions and deletions"
        );

        var nuvio = Store(Path.Combine(dir, "nuvio"));
        var shared = new VideoItem
        {
            Title = "Heat",
            IsMovie = true,
            ImdbId = "tt0113277",
            Available = false,
        };
        nuvio.LogWatched([new(shared, At)], "Imported from Nuvio", false, true);
        nuvio.LogWatched([new(shared, At.AddDays(9))], "Playback");
        var gone = nuvio.RemoveNuvioViewings([new("tt0113277", "movie", "Heat", null, null, 0)]);
        check(
            gone == 1 && nuvio.ViewingHistory(nuvio.Archive.Single()).Single().Source == "Playback",
            "Nuvio deletions remove only viewings that came from Nuvio"
        );
        var heat3 = nuvio.Archive.Single();
        nuvio.MarkNuvioKnown([heat3.Id]);
        nuvio.State.WatchSync["Nuvio:" + heat3.Id] = new()
        {
            Provider = "Nuvio",
            ItemId = heat3.Id,
            SyncedAt = DateTimeOffset.UtcNow,
        };
        nuvio.Unwatch([heat3]);
        check(
            nuvio.State.NuvioDeletes.SequenceEqual(["tt0113277||"]),
            "Nuvio un-checking a known item queues a removal there"
        );
        check(
            !nuvio.State.WatchSync.ContainsKey("Nuvio:" + heat3.Id),
            "Nuvio un-checking drops the synced receipt so checking again can send a new watch"
        );
        nuvio.State.NuvioConnected = true;
        nuvio.LogWatched([new(heat3, At.AddDays(20))], "Checked off");
        check(nuvio.State.WatchSync.ContainsKey("Nuvio:" + heat3.Id), "Nuvio queues Noir's own watches to send");
        nuvio.State.WatchSync.Clear();
        nuvio.LogWatched([new(heat3, At.AddDays(40))], "Imported from Nuvio", false, true);
        check(nuvio.State.WatchSync.Count == 0, "Nuvio pulled watches are never sent back");
        nuvio.MarkNuvioKnown([heat3.Id]);
        nuvio.State.NuvioDeletes.Clear();
        var offline = nuvio.Unwatch([heat3]);
        nuvio.Restore(offline);
        check(
            nuvio.State.NuvioDeletes.Count == 0 && nuvio.State.NuvioKnown.Contains(heat3.Id),
            "Nuvio undoing an un-check cancels the removal waiting to be sent"
        );

        // Review fixes: undo restores guesses a dated import replaced, and keeps notes on undone ratings.
        var guess = Store(Path.Combine(dir, "guess"));
        var film = new VideoItem
        {
            Title = "Alien",
            IsMovie = true,
            ImdbId = "tt0078748",
            Available = false,
        };
        guess.LogWatched([new(film, At, true)], "Logged");
        film = guess.Archive.Single();
        guess.State.PersonalReviews[film.Id] = new() { Note = "Mine" };
        var b2 = guess.BeginImport("Fixture");
        guess.LogWatched([new(film, At.AddDays(3), false, 5)], "Imported from Fixture", false, true, b2);
        guess.EndImport(b2);
        check(
            guess.ViewingHistory(film).Single().Source == "Imported from Fixture",
            "History a dated import replaces an undated guess"
        );
        guess.State.ImportBatches.Single().RatedItemIds.Add(film.Id);
        guess.State.PersonalReviews[film.Id].Rating = 5;
        guess.UndoImport(b2);
        check(
            guess.ViewingHistory(film).Single() is { Source: "Logged", DateUnknown: true } && guess.EverWatched(film),
            "History undoing an import brings back the guess it replaced"
        );
        check(
            guess.State.PersonalReviews[film.Id] is { Rating: 0, Note: "Mine" },
            "History undoing an imported rating keeps an existing note"
        );
    }

    sealed class FakeNuvio : HttpMessageHandler
    {
        public readonly Dictionary<string, string> Bodies = [];
        public string LastApiKey = "",
            LastAuth = "";
        public bool ExpireNext;
        public int Refreshes;
        int polls;
        string token = "a1";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            var name = path.Split('/')[^1];
            Bodies[name] = body;
            LastApiKey = request.Headers.GetValues("apikey").First();
            LastAuth = request.Headers.GetValues("Authorization").First();
            HttpResponseMessage Json(object o, HttpStatusCode code = HttpStatusCode.OK) =>
                new(code)
                {
                    Content = new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json"),
                };
            if (path.StartsWith("/auth/v1/token"))
            {
                Refreshes++;
                token = "a2";
                return Json(
                    new
                    {
                        access_token = "a2",
                        refresh_token = "r2",
                        expires_in = 3600,
                    }
                );
            }
            if (name == "tv-logins-exchange")
                return Json(
                    new
                    {
                        access_token = "a1",
                        refresh_token = "r1",
                        expires_in = 3600,
                    }
                );
            if (name == "start_device_login_session")
                return Json(
                    new[]
                    {
                        new
                        {
                            device_code = "dev",
                            user_code = "abc123",
                            verification_uri_complete = "https://nuvio.tv/link?code=abc123",
                            poll_interval_seconds = 2,
                        },
                    }
                );
            if (name == "poll_tv_login_session")
                return Json(new[] { new { status = ++polls == 1 ? "pending" : "approved" } });
            if (ExpireNext)
            {
                ExpireNext = false;
                return Json(new { message = "JWT expired" }, HttpStatusCode.Unauthorized);
            }
            if (LastAuth != "Bearer " + token)
                return Json(new { }, HttpStatusCode.Unauthorized);
            return name switch
            {
                "sync_pull_profiles" => Json(
                    new[] { new { profile_index = 2, name = "Kid" }, new { profile_index = 1, name = "Me" } }
                ),
                "sync_pull_watched_items" => Json(
                    body.Contains("\"p_page\":1")
                        ? Enumerable
                            .Range(0, 500)
                            .Select(i =>
                                (object)(
                                    i == 0
                                        ? new
                                        {
                                            content_id = "tt0113277",
                                            content_type = "movie",
                                            title = "Heat",
                                            season = (int?)null,
                                            episode = (int?)null,
                                            watched_at = 20240131235959L,
                                        }
                                        : new
                                        {
                                            content_id = "tt0000500",
                                            content_type = "series",
                                            title = "Show",
                                            season = (int?)1,
                                            episode = (int?)3,
                                            watched_at = 1714600000000L,
                                        }
                                )
                            )
                            .ToArray()
                        : new object[]
                        {
                            new
                            {
                                content_id = "tt1",
                                content_type = "movie",
                                title = "Last",
                                watched_at = 1L,
                            },
                        }
                ),
                "sync_pull_watched_items_delta" => Json(
                    new object[]
                    {
                        new
                        {
                            event_id = 11,
                            operation = "upsert",
                            content_id = "tt0113277",
                            content_type = "movie",
                            title = "Heat",
                            watched_at = 1714600000000L,
                        },
                        new
                        {
                            event_id = 12,
                            operation = "delete",
                            content_id = "tt0000500",
                            content_type = "series",
                            season = 1,
                            episode = 3,
                        },
                    }
                ),
                _ => Json(new object[0]),
            };
        }
    }
}
