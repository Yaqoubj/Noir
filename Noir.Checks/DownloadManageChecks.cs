using System.Text.Json;
using Noir;

static class DownloadManageChecks
{
    public static async Task Run(string parent, Action<bool, string> check)
    {
        using (var f = new Fixture(parent))
        {
            var m = f.Manager;
            var groups = m.LocalLibrary();
            check(
                groups.Count == 3 && groups.Sum(g => g.Files.Count) == 6 && groups.Sum(g => g.Bytes) == 2100,
                "Local downloads group shows and Movies with exact file counts and sizes"
            );
            check(
                m.LocalLibrary("Watched").Sum(g => g.Files.Count) == 3
                    && m.LocalLibrary("Unwatched").Sum(g => g.Files.Count) == 3
                    && m.LocalLibrary("Downloaded by Noir").Sum(g => g.Files.Count) == 5,
                "Local filters distinguish watched, unwatched and completed Noir receipts"
            );
            check(
                m.LocalLibrary(sort: "Size").First().Bytes == 1000
                    && m.LocalLibrary(sort: "Name")
                        .Select(g => g.Name)
                        .SequenceEqual(new[] { "Alpha", "Beta", "Movies" })
                    && m.LocalLibrary(sort: "Recent").First().Key == "movies",
                "Size, name and recent local-library sorts are stable"
            );
            m.SetDeleteChoice(DownloadDeleteChoice.Permanent);
            m.SaveScopePreferences(
                "alpha",
                new()
                {
                    SkipWatched = true,
                    IncludeSpecials = true,
                    MaxTotalBytes = 123,
                }
            );
            check(
                m.ManagementPreferences.WhenDeleting == DownloadDeleteChoice.Permanent
                    && m.ScopePreferences("alpha").MaxTotalBytes == 123,
                "Delete choice and additional per-show scope options persist separately from credentials"
            );
            var file = m.LocalLibrary().SelectMany(g => g.Files).Single(x => x.Item.Id == "a1");
            m.IsFilePlaying = p => p == file.Path;
            var refused = await m.DeleteLocal([file], DownloadDeleteChoice.Permanent);
            check(
                refused.Files == 0 && refused.Errors.Count == 1 && File.Exists(file.Path),
                "Deleting the currently-playing video is refused before touching sidecars"
            );
            m.IsFilePlaying = null;
            var result = await m.DeleteLocal([file], DownloadDeleteChoice.Permanent);
            check(
                result.Files == 1
                    && FileBundleGone(file.Path)
                    && !f.Store.Archive.Single(i => i.Id == "a1").Available
                    && f.Store.EverWatched(file.Item)
                    && m.Jobs().All(j => j.Target.Id != "a1"),
                "Deleting an episode removes video, language subtitles, NFO, availability and receipts while keeping history"
            );
            var season = m.LocalLibrary().Single(g => g.Key == "alpha").Files.Where(x => x.Item.Season == 1).ToList();
            check(
                (await m.DeleteLocal(season, DownloadDeleteChoice.Permanent)).Files == 1
                    && season.All(x => !File.Exists(x.Path)),
                "Deleting a season removes all its remaining video files"
            );
            var show = m.LocalLibrary().Single(g => g.Key == "alpha").Files;
            check(
                (await m.DeleteLocal(show, DownloadDeleteChoice.Permanent)).Files == 1
                    && !m.LocalLibrary().Any(g => g.Key == "alpha"),
                "Deleting a show removes its remaining seasons without hiding its history"
            );
        }
        using (var f = new Fixture(parent))
        {
            var m = f.Manager;
            bool recycled = false;
            m.ManageDeleter = (path, recycle) =>
            {
                recycled |= recycle;
                File.Delete(path);
            };
            var file = m.LocalLibrary().Single(g => g.Key == "beta").Files[0];
            var deleted = await m.DeleteLocal([file], DownloadDeleteChoice.Recycle);
            check(
                recycled && deleted.Files == 1,
                "Recycle selection reaches the injectable file deleter and updates library availability"
            );
            var alpha = m.LocalLibrary().Single(g => g.Key == "alpha").Files;
            var watched = await m.DeleteLocal(alpha, DownloadDeleteChoice.Permanent, true);
            check(
                watched.Files == 1 && alpha.Where(x => !x.Watched).All(x => File.Exists(x.Path)),
                "Delete watched rechecks history and keeps unwatched episodes"
            );
        }
        using (var f = new Fixture(parent))
        {
            var m = f.Manager;
            var file = m.LocalLibrary().Single(g => g.Key == "alpha").Files.Single(x => x.Item.Id == "a2");
            f.Store.Record(f.Store.Archive.Single(i => i.Id == "a2"), 42.5, 120);
            var move = await m.MoveLocal([file], f.Other);
            var target = DownloadNaming.Destination(f.Other, file.Item, file.Path);
            check(
                move.Files == 1
                    && File.Exists(target)
                    && File.Exists(Path.ChangeExtension(target, ".en.srt"))
                    && !File.Exists(file.Path)
                    && f.Store.Archive.Single(i => i.Id == "a2").Path == target
                    && m.Jobs().Single(j => j.Target.Id == "a2").Destination == target
                    && f.Store.GetProgress(f.Store.Archive.Single(i => i.Id == "a2"))?.Position == 42.5,
                "Move to another library root carries sidecars, progress, catalog paths and job destinations"
            );
            var plan = f.Store.PlanOrganization();
            if (plan.Moves.Count > 0)
            {
                var video = plan.Moves.First(x => x.Video);
                f.Store.Organize(plan);
                check(
                    m.Jobs().Single(j => j.Id == video.ItemId).Destination == video.To,
                    "Organizer apply updates download job destinations through the move hook"
                );
                f.Store.Organize(plan, true);
                check(
                    m.Jobs().Single(j => j.Id == video.ItemId).Destination == video.From,
                    "Organizer undo updates download job destinations through the move hook"
                );
            }
        }
        using (var f = new Fixture(parent))
        { // fresh fixture: the organizer move above correctly stops the seeding torrent on a1
            var m = f.Manager;
            var before = m.LocalLibrary().SelectMany(g => g.Files).Select(x => x.Path).ToList();
            var cleared = m.ClearFinished();
            check(
                cleared == 6
                    && before.All(File.Exists)
                    && m.Jobs().Any(j => j.Id == "seed")
                    && m.Jobs().Any(j => j.Id == "paused"),
                "Clear finished only removes Completed/Cancelled receipts and keeps files plus sharing torrents"
            );
        }
        using (var f = new Fixture(parent))
        {
            var m = f.Manager;
            var suggestions = m.SpaceSuggestions();
            var flat = suggestions.SelectMany(g => g.Items).ToList();
            check(
                suggestions.Any(g => g.Kind == "Finished shows")
                    && suggestions.Any(g => g.Kind == "Watched episodes (older than 7 days)")
                    && flat.Any(x => x.Path.EndsWith("orphan.part"))
                    && flat.Any(x => x.Path.EndsWith("cancel.part"))
                    && flat.Any(x => x.Path.EndsWith("orphan-incoming"))
                    && !flat.Any(x => x.Path.EndsWith("paused.part")),
                "Free-space suggestions include old watched, finished shows, cancelled/orphan parts and orphan incoming folders while protecting resumable jobs"
            );
            check(
                flat.Select(x => x.Key).Distinct().Count() == flat.Count
                    && suggestions.Sum(g => g.Bytes) == flat.Sum(x => x.Bytes)
                    && suggestions.Single(g => g.Kind == "Torrents only sharing back").Bytes == 0,
                "Cleanup suggestions do not double count videos and stopping sharing claims no freed bytes"
            );
            await m.CancelDownload("paused", true);
            check(
                !File.Exists(Path.Combine(f.Parts, "paused.part"))
                    && m.Jobs().Single(j => j.Id == "paused").Status == "Cancelled"
                    && m.Jobs().Single(j => j.Id == "paused").Bytes == 0,
                "Cancel-and-delete stops the job and removes its partial bytes"
            );
            var parts = flat.Where(x => x.Kind == "Leftover partial downloads").ToList();
            var removed = await m.FreeSpace(parts, DownloadDeleteChoice.Permanent);
            check(
                removed.Errors.Count == 0 && parts.All(x => !File.Exists(x.Path) && !Directory.Exists(x.Path)),
                "Cleanup deletes only confirmed leftover parts and orphan incoming data"
            );
        }
        using (var f = new Fixture(parent))
        {
            var m = f.Manager;
            var item = f.Store.Archive.Single(i => i.Id == "a1");
            var next = m.ScopeEpisodes(
                item,
                new()
                {
                    Scope = "Next N episodes",
                    NextCount = 2,
                    SkipWatched = true,
                }
            );
            check(
                next.Select(i => (i.Season, i.Episode)).SequenceEqual(new[] { (2, 3), (2, 4) }),
                "Next N counts after local, queued, watched and future skips across season boundaries"
            );
            var without = m.ScopeEpisodes(item, new() { Scope = "Whole show" });
            var with = m.ScopeEpisodes(item, new() { Scope = "Whole show", IncludeSpecials = true });
            check(
                without.All(i => i.Season > 0) && with.Any(i => i.Season == 0) && with.Count == without.Count + 1,
                "Whole show excludes specials unless explicitly included"
            );
            var plan = await m.PlanRequest(
                item.Id,
                new()
                {
                    Scope = "Specific episodes",
                    SpecificEpisodes = [new(2, 3), new(2, 4)],
                    MaxTotalBytes = 150,
                    Preferences = new()
                    {
                        Resolution = "1080p",
                        Language = "All",
                        MaxEpisodeBytes = 200,
                    },
                }
            );
            check(
                plan.Entries.Count == 1
                    && plan.Episodes.Last().State == "Over your total limit"
                    && plan.Episodes.Last().Chosen != null,
                "Total cap fills in episode order and leaves over-limit alternatives available for explicit review"
            );
            bool blocked = false;
            try
            {
                m.EnqueuePlan(
                    new()
                    {
                        PlanId = plan.Id,
                        RequestId = Guid.NewGuid().ToString(),
                        FileIds = plan.Episodes.Select(e => e.Chosen!.File.Id).ToList(),
                    }
                );
            }
            catch (IOException)
            {
                blocked = true;
            }
            check(blocked, "Enqueue rechecks the plan total instead of silently increasing its limit");
            var request = new DownloadPlanRequest
            {
                Scope = "Specific episodes",
                Seasons = [2],
                FromEpisode = 3,
                NextCount = 1,
                SpecificEpisodes = [new(2, 4)],
                Preferences = new() { Resolution = "720p", Language = "All" },
                MaxTotalBytes = 90,
                SkipWatched = true,
                IncludeSpecials = true,
            };
            var passed = await m.PlanRequest(
                item.Id,
                JsonSerializer.Deserialize<DownloadPlanRequest>(JsonSerializer.Serialize(request))!
            );
            check(
                passed.Episodes.Count == 1
                    && passed.Episodes[0].Season == 2
                    && passed.Episodes[0].Episode == 4
                    && passed.Preferences.Resolution == "720p"
                    && passed.Entries.Count == 0,
                "The phone plan request passes scope, seasons, specific episodes, preferences and total cap to the real planner"
            );
        }
    }

    static bool FileBundleGone(string video) =>
        !File.Exists(video)
        && !File.Exists(Path.ChangeExtension(video, ".en.srt"))
        && !File.Exists(Path.ChangeExtension(video, ".nfo"));

    sealed class Fixture : IDisposable
    {
        public LibraryStore Store;
        public PcDownloads Manager;
        public string Root,
            Other,
            Parts;

        public Fixture(string parent)
        {
            var dir = Path.Combine(parent, "manage-" + Guid.NewGuid().ToString("N"));
            Root = Path.Combine(dir, "shows");
            Other = Path.Combine(dir, "other-shows");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Other);
            Store = new(Path.Combine(dir, "profile"));
            Store.State.Roots =
            [
                new() { Path = Root },
                new() { Path = Other },
                new() { Path = Path.Combine(dir, "movies"), Movies = true },
            ];
            Store.State.Catalog.Clear();
            Store.State.OnlineMetadata = false;
            var items = new List<VideoItem>();
            void Add(
                string id,
                string title,
                string group,
                int season,
                int ep,
                int size,
                bool watched,
                bool movie = false
            )
            {
                var root = movie ? Path.Combine(dir, "movies") : Root;
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, id + ".mkv");
                File.WriteAllBytes(path, new byte[size]);
                File.WriteAllText(Path.ChangeExtension(path, ".en.srt"), "subtitle");
                File.WriteAllText(Path.ChangeExtension(path, ".nfo"), "metadata");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-10 + items.Count));
                var item = new VideoItem
                {
                    Id = id,
                    Title = title,
                    CollectionKey = group,
                    Season = season,
                    Episode = ep,
                    Path = path,
                    Available = true,
                    IsMovie = movie,
                    SourceUrl = "https://fixture.invalid/title",
                    ImdbId = "tt123",
                };
                Store.State.Catalog[id] = new() { Item = item, Paths = [path] };
                items.Add(item);
                if (watched)
                {
                    Store.Mark(item, true);
                    foreach (var v in Store.State.Viewings[id])
                        v.WatchedAt = DateTimeOffset.UtcNow.AddDays(-9);
                }
            }
            Add("a1", "Alpha", "alpha", 1, 1, 100, true);
            Add("a2", "Alpha", "alpha", 1, 2, 200, false);
            Add("a3", "Alpha", "alpha", 2, 1, 300, false);
            Add("b1", "Beta", "beta", 1, 1, 400, true);
            Add("b2", "Beta", "beta", 1, 2, 600, true);
            Add("m1", "Movie", "movie", 0, 0, 500, false, true);
            Store.State.EpisodeSchedules["alpha"] =
            [
                new() { Season = 0, Episode = 1 },
                new() { Season = 1, Episode = 1 },
                new() { Season = 1, Episode = 2 },
                new() { Season = 2, Episode = 1 },
                new() { Season = 2, Episode = 2 },
                new() { Season = 2, Episode = 3 },
                new() { Season = 2, Episode = 4 },
                new()
                {
                    Season = 2,
                    Episode = 5,
                    AirsAt = DateTimeOffset.UtcNow.AddDays(1),
                },
            ];
            Store.Save();
            var jobs = items
                .Take(5)
                .Select(i => new PcDownloadJob
                {
                    Id = i.Id,
                    Target = i,
                    Root = Root,
                    Destination = i.Path,
                    Status = "Completed",
                    Size = new FileInfo(i.Path).Length,
                })
                .ToList();
            jobs.Add(new() { Id = "cancel", Status = "Cancelled" });
            jobs.Add(
                new()
                {
                    Id = "paused",
                    Status = "Paused",
                    Bytes = 30,
                    Target = new()
                    {
                        Id = "a-next",
                        CollectionKey = "alpha",
                        Title = "Alpha",
                        Season = 2,
                        Episode = 2,
                    },
                    Root = Root,
                    Destination = Path.Combine(Root, "next.mkv"),
                }
            );
            jobs.Add(
                new()
                {
                    Id = "seed",
                    Status = "Completed",
                    Service = "Torrents",
                    SeedUntil = DateTimeOffset.UtcNow.AddHours(1),
                    Target = items[0],
                    Destination = items[0].Path,
                }
            );
            File.WriteAllText(Path.Combine(Store.DataDirectory, "pc-downloads.json"), JsonSerializer.Serialize(jobs));
            Manager = new(Store, new ManageProvider(), start: false, freeSpace: _ => long.MaxValue / 4);
            Manager.Configure(
                "https://fixture.invalid/manifest.json",
                "fixture",
                Root,
                Path.Combine(dir, "movies"),
                subtitleLanguages: []
            );
            Parts = Path.Combine(Store.DataDirectory, "download-parts");
            foreach (var id in new[] { "orphan", "cancel", "paused" })
                File.WriteAllBytes(Path.Combine(Parts, id + ".part"), new byte[30]);
            var incoming = Path.Combine(Root, ".noir-incoming", "orphan-incoming");
            Directory.CreateDirectory(incoming);
            File.WriteAllBytes(Path.Combine(incoming, "video.part"), new byte[40]);
        }

        public void Dispose() => Manager.Dispose();
    }

    sealed class ManageProvider : IDownloadProvider
    {
        public Task<List<IndexedRelease>> Search(VideoItem i, CancellationToken ct) =>
            Task.FromResult(
                new List<IndexedRelease>
                {
                    new(
                        new()
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = "Fixture 1080p",
                            Resolution = "1080p",
                            Cached = true,
                            Languages = "English",
                            CacheStatus = "Ready",
                        },
                        "season-" + i.Season
                    ),
                }
            );

        public Task<List<TorboxFile>> Files(string hash, CancellationToken ct) =>
            Task.FromResult(
                Enumerable
                    .Range(1, 5)
                    .Select(e => new TorboxFile(e, $"Alpha.S{int.Parse(hash.Split('-')[1]):00}E{e:00}.mkv", 100))
                    .ToList()
            );

        public Task<TorboxTorrent> Prepare(string h, long id, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string> Link(long id, long f, CancellationToken ct) => throw new NotSupportedException();
    }
}
