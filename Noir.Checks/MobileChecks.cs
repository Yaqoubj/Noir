using System.Text.Json;
using Noir;

static class MobileChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var s = new LibraryStore(Path.Combine(root, "mobile-" + Guid.NewGuid().ToString("N")));
        s.State.Roots = [];
        var i = new VideoItem
        {
            Title = "Phone test",
            Path = "missing.mkv",
            Season = 1,
            Episode = 1,
            Duration = 1800,
        };
        s.Record(i, 93.125, 1800);
        i = s.Archive.Single();
        s.State.EpisodeSchedules[i.GroupKey] =
        [
            new() { Season = 1, Episode = 1 },
            new() { Season = 1, Episode = 2 },
            new() { Season = 1, Episode = 3 },
        ];
        var json = JsonSerializer.Serialize(s.MobileSnapshot());
        check(
            !json.Contains("missing.mkv") && !json.Contains("Roots") && !json.Contains("WatchSync"),
            "Mobile projection never exposes PC paths or account data"
        );
        check(
            s.MobileSnapshot().Items.Count == 3,
            "Phone receives full known episode counts including unavailable episodes"
        );
        {
            var (trakt, serializd) = (s.State.SyncTrakt, s.State.SyncSerializd);
            s.State.SyncTrakt = true;
            s.State.SyncSerializd = false;
            var camel = JsonSerializer.Serialize(
                s.MobileSnapshot(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
            );
            check(
                camel.Contains("\"syncTrakt\":true")
                    && camel.Contains("\"syncSerializd\":false")
                    && camel.Contains("\"reviewTitle\":"),
                "Phone receives the PC's watch-sync switches and each title's review title"
            );
            (s.State.SyncTrakt, s.State.SyncSerializd) = (trakt, serializd);
        }
        var at = DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeMilliseconds();
        var pcRevision = s.GetProgress(i)!.UpdatedAt;
        var op = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = i.Id,
            Kind = "progress",
            ViewingId = Guid.NewGuid().ToString(),
            At = at,
            Position = 321.987,
            Duration = 1800,
        };
        check(
            s.MergeMobile([op]).Count == 1 && s.GetProgress(i)!.Position == 321.987,
            "Android exact fractional resume time merges into Windows"
        );
        check(
            !s.Record(i, 93.125, 1800, false, pcRevision) && s.GetProgress(i)!.Position == 321.987,
            "Stale PC close cannot overwrite newer phone progress"
        );
        check(
            new LibraryStore(s.DataDirectory).GetProgress(i)!.Position == 321.987,
            "Exact synced phone position survives PC close and reopen"
        );
        s.MergeMobile([op]);
        check(s.GetProgress(i)!.Position == 321.987, "Repeated operation is acknowledged without changing progress");
        var old = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = i.Id,
            Kind = "progress",
            ViewingId = Guid.NewGuid().ToString(),
            At = at - 5000,
            Position = 10,
            Duration = 1800,
            Completed = true,
        };
        s.MergeMobile([old]);
        check(
            s.GetProgress(i)!.Position == 321.987 && s.ViewingHistory(i).Count == 1,
            "Older offline watch joins diary without overwriting newer resume"
        );
        var reopened = new LibraryStore(s.DataDirectory);
        reopened.MergeMobile([old]);
        check(
            reopened.ViewingHistory(i).Count == 1,
            "Watch receipt survives restart and prevents duplicate diary dates"
        );
        var virtualItem = reopened.MobileSnapshot().Items.First(x => x.Episode == 3);
        var mark = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = virtualItem.Id,
            Kind = "mark",
            ViewingId = Guid.NewGuid().ToString(),
            At = at + 1000,
            Completed = true,
        };
        reopened.MergeMobile([mark]);
        check(
            reopened.Archive.Any(x => x.Id == virtualItem.Id)
                && reopened.ViewingHistory(reopened.Archive.Single(x => x.Id == virtualItem.Id)).Count == 1,
            "Android can mark unavailable known episode watched permanently"
        );
        var discovered = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = "discover-series-tt987654321",
            Kind = "progress",
            ViewingId = Guid.NewGuid().ToString(),
            At = at,
            Position = 127.25,
            Duration = 1200,
            Title = "Phone-only discovery",
            Imdb = "tt987654321",
            Season = 2,
            Episode = 3,
        };
        check(
            reopened.MergeMobile([discovered]).Count == 1
                && reopened.Archive.Any(x => x.Id == discovered.ItemId && reopened.GetProgress(x)!.Position == 127.25),
            "Phone-only Discover downloads import stable episode identity and exact offline progress"
        );
        var imported = reopened.Archive.Single(x => x.Id == discovered.ItemId);
        reopened.MergeMobile([discovered]);
        check(
            reopened.Archive.Count(x => x.ImdbId == discovered.Imdb) == 1,
            "Retried phone Discover progress does not duplicate catalog entries"
        );
        var invalid = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = i.Id,
            Kind = "progress",
            ViewingId = Guid.NewGuid().ToString(),
            At = at,
            Position = double.NaN,
        };
        check(
            reopened.MergeMobile([invalid]).Count == 0,
            "Invalid mobile progress is rejected without modifying the library"
        );
        var review = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = i.Id,
            Kind = "review",
            At = at + 2000,
            Rating = 4,
            Note = "Phone note",
        };
        reopened.MergeMobile([review]);
        check(
            reopened.State.PersonalReviews[i.Id].Rating == 4
                && reopened.State.PersonalReviews[i.Id].Note == "Phone note",
            "Phone ratings and notes sync to PC"
        );
        var stale = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = i.Id,
            Kind = "review",
            At = at,
            Rating = 1,
        };
        reopened.MergeMobile([stale]);
        check(
            reopened.State.PersonalReviews[i.Id].Rating == 4,
            "Stale offline review cannot replace a more recent review"
        );
        var list = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = i.Id,
            Kind = "list",
            At = at + 2000,
            Value = true,
        };
        reopened.MergeMobile([list]);
        check(reopened.State.Watchlist.Contains(i.GroupKey), "Android My List syncs by show group");
        var unknown = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = "../../private",
            Kind = "mark",
            At = at,
            ViewingId = Guid.NewGuid().ToString(),
        };
        check(reopened.MergeMobile([unknown]).Count == 0, "Unknown remote IDs cannot create arbitrary catalog entries");
        // Phone tracker: dated and undated check-offs, follows of phone-only shows, and un-checking.
        var dated = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = "discover-series-tt555000111",
            Kind = "mark",
            ViewingId = Guid.NewGuid().ToString(),
            At = at,
            Completed = true,
            WatchedAt = DateTimeOffset.Parse("2019-03-02T20:00:00Z").ToUnixTimeMilliseconds(),
            Title = "Logged on phone",
            Imdb = "tt555000111",
            Season = 1,
            Episode = 1,
        };
        reopened.MergeMobile([dated]);
        var loggedEpisode = reopened.Archive.Single(x => x.ImdbId == "tt555000111");
        check(
            reopened.ViewingHistory(loggedEpisode).Single().WatchedAt.Year == 2019,
            "A phone check-off keeps the date the user chose"
        );
        var guessed = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = PlaceholderId("show:tt555000111", 1, 2),
            Kind = "mark",
            ViewingId = Guid.NewGuid().ToString(),
            At = at,
            Completed = true,
            DateUnknown = true,
            Title = "Logged on phone",
            Imdb = "tt555000111",
            Season = 1,
            Episode = 2,
        };
        reopened.MergeMobile([guessed]);
        var second = reopened.Archive.Single(x => x.ImdbId == "tt555000111" && x.Episode == 2);
        check(
            reopened.ViewingHistory(second).Single().WatchedAt == DateTimeOffset.FromUnixTimeMilliseconds(at),
            "An undated phone check-off is filed at the time it was made, never 1970"
        );
        check(
            reopened.ViewingHistory(second).Single().DateUnknown,
            "\"Don't remember\" from the phone stays undated on the PC"
        );
        var future = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = loggedEpisode.Id,
            Kind = "mark",
            ViewingId = Guid.NewGuid().ToString(),
            At = at,
            Completed = true,
            WatchedAt = at + 86400000,
        };
        reopened.MergeMobile([future]);
        check(
            reopened.ViewingHistory(loggedEpisode).Count == 2
                && reopened
                    .ViewingHistory(loggedEpisode)
                    .All(v => v.WatchedAt <= DateTimeOffset.FromUnixTimeMilliseconds(at)),
            "A watch date after the operation time is ignored"
        );
        var unview = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = second.Id,
            Kind = "unview",
            At = at + 3000,
            ViewingId = "",
            Imdb = "tt555000111",
            Season = 1,
            Episode = 2,
        };
        reopened.MarkNuvioKnown([second.Id]);
        reopened.State.WatchSync["Nuvio:" + second.Id] = new()
        {
            Provider = "Nuvio",
            ItemId = second.Id,
            SyncedAt = DateTimeOffset.UtcNow,
        };
        check(
            reopened.MergeMobile([unview]).Count == 1 && !reopened.EverWatched(second),
            "Un-checking on the phone forgets the viewing on the PC"
        );
        check(
            reopened.State.NuvioDeletes.Contains("tt555000111|1|2")
                && !reopened.State.NuvioKnown.Contains(second.Id)
                && !reopened.State.WatchSync.ContainsKey("Nuvio:" + second.Id),
            "Removing the final phone viewing queues a Nuvio deletion and clears its watch receipt"
        );
        var stray = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = "discover-series-tt555000999",
            Kind = "unview",
            At = at,
            Imdb = "tt555000999",
            Season = 1,
            Episode = 1,
        };
        check(
            reopened.MergeMobile([stray]).Count == 1 && !reopened.Archive.Any(x => x.ImdbId == "tt555000999"),
            "Un-checking a title the PC never saw is acknowledged without adding it"
        );
        var follow = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = "discover-series-tt555000222",
            Kind = "list",
            At = at,
            Value = true,
            Title = "Followed on phone",
            Imdb = "tt555000222",
            Season = 1,
            Episode = 1,
        };
        check(
            reopened.MergeMobile([follow]).Count == 1
                && reopened.Archive.FirstOrDefault(x => x.ImdbId == "tt555000222") is { } followed
                && reopened.State.Watchlist.Contains(followed.GroupKey),
            "Following a phone-only show adds it to My List on the PC"
        );
        // Forgetting a title whose files stay on disk keeps it out of later scans.
        var media = Path.Combine(root, "forget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(media);
        File.WriteAllBytes(Path.Combine(media, "Forgotten Film (2001).mkv"), [1, 2, 3]);
        var library = new LibraryStore(Path.Combine(root, "forget-store-" + Guid.NewGuid().ToString("N")));
        library.State.Roots = [new() { Path = media, Movies = true }];
        library.Scan(out _);
        var film = library.Archive.Single(x => x.Title.Contains("Forgotten"));
        library.ForgetTitle(film.GroupKey);
        library.Scan(out _);
        check(
            !library.Archive.Any(x => x.Title.Contains("Forgotten"))
                && !new LibraryStore(library.DataDirectory).Scan(out _).Any(x => x.Title.Contains("Forgotten")),
            "A forgotten title's files left on disk don't come back on the next scan"
        );
        File.Delete(Path.Combine(media, "Forgotten Film (2001).mkv"));
        library.Scan(out _);
        File.WriteAllBytes(Path.Combine(media, "Forgotten Film (2001).mkv"), [4, 5, 6, 7]);
        check(
            library.Scan(out _).Any(x => x.Title.Contains("Forgotten")),
            "A new file at the same path is picked up again"
        );
        var final = new LibraryStore(s.DataDirectory);
        check(
            final.MobileSnapshot().Items.Count(x => x.Group == i.GroupKey) == 3 && final.ViewingHistory(i).Count == 1,
            "Deleted files retain phone-synced history and complete counts across restarts"
        );
        CommunityBackoffChecks.Run(root, check);
    }

    static string PlaceholderId(string group, int season, int episode) =>
        "episode-"
        + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{group}:{season}:{episode}")
            )
        );
}
