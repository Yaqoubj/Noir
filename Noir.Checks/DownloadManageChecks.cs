using System.Text.Json;
using Noir;

static class DownloadManageChecks
{
    public static async Task Run(string parent, Action<bool, string> check)
    {
        DownloadSpaceItem Item(string kind, long bytes, bool listOnly = false) =>
            new(Guid.NewGuid().ToString("N"), kind, "x", bytes, "x", ListOnly: listOnly);
        check(
            DownloadNaming.Size(2048) == "2.0 KB"
                && DownloadNaming.Size(104857) == "102.4 KB"
                && DownloadNaming.Size(104858) == "0.1 MB"
                && DownloadNaming.Size(570L * 1048576) == "570.0 MB"
                && !DownloadNaming.Size(300).Contains("0.0 MB"),
            "U92 files under 0.1 MB show KB, not 0.0 MB"
        );
        var folders = new[] { Item(PcDownloads.KindEmptyFolders, 0), Item(PcDownloads.KindEmptyFolders, 0) };
        check(
            FreeSpaceText.GroupFigure(PcDownloads.KindEmptyFolders, folders, false) == "2 folders"
                && FreeSpaceText.GroupFigure(PcDownloads.KindEmptyFolders, folders.Take(1), false) == "1 folder"
                && FreeSpaceText.GroupFigure(PcDownloads.KindEmptyFolders, folders, true) == "مجلدان"
                && !FreeSpaceText.GroupFigure(PcDownloads.KindEmptyFolders, folders, false).Contains("unknown"),
            "U92 empty folders show a count, not Size unknown"
        );
        // U93: the group figures are what the ticked files free, and the button is their sum.
        long mb = 1048576;
        var dupes = new[]
        {
            Item(PcDownloads.KindDuplicates, 300 * mb),
            Item(PcDownloads.KindDuplicates, 900 * mb),
            Item(PcDownloads.KindDuplicates, 270 * mb),
        };
        var ticked = dupes.Where(d => d.Bytes != 900 * mb).ToList();
        var loose = new[] { Item(PcDownloads.KindLoose, 500), Item(PcDownloads.KindLoose, 40 * mb, true) };
        var tickedLoose = loose.Where(l => !l.ListOnly).ToList();
        var sum = ticked.Sum(i => i.Bytes) + tickedLoose.Sum(i => i.Bytes);
        check(
            FreeSpaceText.GroupFigure(PcDownloads.KindDuplicates, ticked, false) == "570.0 MB"
                && FreeSpaceText.GroupFigure(PcDownloads.KindDuplicates, dupes, false) != "570.0 MB"
                && FreeSpaceText.GroupFigure(PcDownloads.KindLoose, tickedLoose, false) == "0.5 KB"
                && FreeSpaceText.Button(DownloadDeleteChoice.Recycle, sum, false)
                    == "Move " + DownloadNaming.Size(sum) + " to Recycle Bin"
                && FreeSpaceText.GroupFigure(PcDownloads.KindDuplicates, [], false) == "0 KB",
            "U93 group sizes are what their ticked files free (only the ticked duplicate copies, the program file never) and the button is the sum"
        );
        check(
            FreeSpaceText.Button(DownloadDeleteChoice.Ask, 570 * mb, false) == "Remove 570.0 MB…"
                && FreeSpaceText.Button(DownloadDeleteChoice.Recycle, 570 * mb, false) == "Move 570.0 MB to Recycle Bin"
                && FreeSpaceText.Button(DownloadDeleteChoice.Permanent, 570 * mb, false) == "Delete 570.0 MB"
                && FreeSpaceText.Button(DownloadDeleteChoice.Ask, 570 * mb, true).EndsWith("…"),
            "U94 the Free up space button names what will really happen for each delete option (Ask says Remove and an ellipsis)"
        );
        check(
            FreeSpaceText.MiddleTrim("Night Signal", 34) == "Night Signal"
                && FreeSpaceText.MiddleTrim(new string('a', 20) + new string('b', 30) + new string('c', 20), 34)
                    is var cut
                && cut.Length == 34
                && cut.StartsWith("aaaa")
                && cut.EndsWith("cccc")
                && cut.Contains('…'),
            "U95 long library-relative paths are trimmed in the middle, start and end both kept"
        );
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
        await Sharpen(parent, check);
    }

    // O2 (switches shared with new downloads), O4 (leftovers inside the library only) and O5 (duplicates).
    static async Task Sharpen(string parent, Action<bool, string> check)
    {
        using (var f = new Fixture(parent))
        {
            var m = f.Manager;
            f.Store.State.Catalog["a2"].Item.EpisodeTitle = "Second";
            m.SetNaming(new() { EpisodeTitle = true, MovieFolder = true });
            m.SetAutoOrganize(true);
            var reopened = m.ManagementPreferences;
            check(
                reopened.Naming.EpisodeTitle && reopened.Naming.MovieFolder && reopened.AutoOrganize,
                "O2/O3 the naming switches and automatic organizing persist in the download settings"
            );
            var file = m.LocalLibrary().Single(g => g.Key == "alpha").Files.Single(x => x.Item.Id == "a2");
            var move = await m.MoveLocal([file], f.Other);
            check(
                move.Files == 1 && f.Store.Archive.Single(i => i.Id == "a2").Path.EndsWith("Alpha S01E02 - Second.mkv"),
                "O2 moving a download uses the same naming switches as the organizer"
            );
        }
        using (var f = new Fixture(parent))
        {
            var m = f.Manager;
            var outside = Path.Combine(Path.GetDirectoryName(f.Root)!, "outside");
            Directory.CreateDirectory(Path.Combine(outside, "Empty"));
            File.WriteAllBytes(Path.Combine(outside, "Big.Movie.mkv"), new byte[300]);
            File.WriteAllBytes(Path.Combine(outside, "big.movie.sample.mkv"), new byte[10]);
            File.WriteAllText(Path.Combine(outside, "RARBG.txt"), "ad");
            Directory.CreateDirectory(Path.Combine(f.Root, "Ghost Show", "Season 01"));
            Directory.CreateDirectory(Path.Combine(f.Root, "Only Thumbs"));
            File.WriteAllText(Path.Combine(f.Root, "Only Thumbs", "Thumbs.db"), "x");
            var pack = Path.Combine(f.Root, "Pack");
            Directory.CreateDirectory(pack);
            File.WriteAllBytes(Path.Combine(pack, "Movie.Name.2020.mkv"), new byte[300]);
            File.WriteAllText(Path.Combine(pack, "Movie.Name.2020.nfo"), "matches the video");
            File.WriteAllBytes(Path.Combine(pack, "movie.name.sample.mkv"), new byte[40]);
            File.WriteAllText(Path.Combine(pack, "RARBG.txt"), "advert");
            File.WriteAllText(Path.Combine(pack, "info.nfo"), "no video");
            File.WriteAllText(Path.Combine(pack, "tvshow.nfo"), "kodi");
            File.WriteAllText(Path.Combine(pack, "setup.exe"), "program");
            var lone = Path.Combine(f.Root, "Lone");
            Directory.CreateDirectory(lone);
            File.WriteAllBytes(Path.Combine(lone, "sample.mkv"), new byte[20]);
            var flat = m.SpaceSuggestions().SelectMany(g => g.Items).ToList();
            string Rel(DownloadSpaceItem i) => Path.GetRelativePath(f.Root, i.Path);
            check(
                flat.Where(i => i.Kind == PcDownloads.KindEmptyFolders)
                    .Select(Rel)
                    .OrderBy(x => x)
                    .SequenceEqual(new[] { "Ghost Show", "Only Thumbs" }),
                "O4 empty folders are listed outermost only, and a folder holding only Thumbs.db counts as empty"
            );
            check(
                flat.Where(i => i.Kind == PcDownloads.KindSamples)
                    .Select(Rel)
                    .SequenceEqual(new[] { Path.Combine("Pack", "movie.name.sample.mkv") })
                    && flat.Where(i => i.Kind == PcDownloads.KindLoose)
                        .Select(Rel)
                        .OrderBy(x => x)
                        .SequenceEqual(
                            new[]
                            {
                                Path.Combine("Pack", "info.nfo"),
                                Path.Combine("Pack", "RARBG.txt"),
                                Path.Combine("Pack", "setup.exe"),
                            }
                        )
                    && !flat.Single(i => i.Path.EndsWith("setup.exe")).Preselect
                    && flat.Single(i => i.Path.EndsWith("setup.exe")).ListOnly
                    && flat.Where(i => i.Kind == PcDownloads.KindLoose && !i.ListOnly)
                        .All(i => !i.Path.EndsWith(".exe"))
                    && flat.Single(i => i.Path.EndsWith("RARBG.txt")).Preselect,
                "O4 samples next to a bigger video and loose .nfo/.txt/.url/.exe that match no video are listed; a lone sample, a matching .nfo and tvshow.nfo are not; a program is never ticked"
            );
            check(
                flat.All(i => !i.Path.StartsWith(outside, StringComparison.OrdinalIgnoreCase))
                    && !flat.Any(i => i.Path.Contains(".noir-incoming") && i.Kind != "Leftover partial downloads"),
                "O4 nothing outside the library folders (or inside Noir's own incoming folder) is ever listed"
            );
            bool recycled = false;
            m.ManageDeleter = (path, recycle) =>
            {
                recycled |= recycle;
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                else
                    File.Delete(path);
            };
            var leftovers = flat.Where(i =>
                    i.Kind is PcDownloads.KindEmptyFolders or PcDownloads.KindSamples or PcDownloads.KindLoose
                )
                .ToList();
            // A forged item outside the library, and an empty folder that gained a file after the review, are skipped.
            File.WriteAllText(Path.Combine(f.Root, "Ghost Show", "Season 01", "late.txt"), "arrived after the review");
            var forged = new DownloadSpaceItem(
                Path.Combine(outside, "RARBG.txt"),
                PcDownloads.KindLoose,
                "x",
                2,
                Path.Combine(outside, "RARBG.txt")
            );
            var freed = await m.FreeSpace(leftovers.Append(forged), DownloadDeleteChoice.Recycle);
            check(
                recycled
                    && !File.Exists(Path.Combine(pack, "RARBG.txt"))
                    && File.Exists(Path.Combine(pack, "setup.exe"))
                    && freed.Errors.Any(e => e.Contains("Program file"))
                    && !File.Exists(Path.Combine(pack, "movie.name.sample.mkv"))
                    && !Directory.Exists(Path.Combine(f.Root, "Only Thumbs"))
                    && File.Exists(Path.Combine(pack, "Movie.Name.2020.mkv"))
                    && File.Exists(Path.Combine(pack, "Movie.Name.2020.nfo"))
                    && File.Exists(Path.Combine(pack, "tvshow.nfo"))
                    && File.Exists(Path.Combine(lone, "sample.mkv")),
                "O4 cleanup uses the chosen delete method (Recycle Bin here), leaves videos, matching files and lone samples, and refuses a program file even when handed it directly"
            );
            check(
                File.Exists(Path.Combine(outside, "RARBG.txt"))
                    && File.Exists(Path.Combine(outside, "big.movie.sample.mkv"))
                    && Directory.Exists(Path.Combine(outside, "Empty"))
                    && File.Exists(Path.Combine(f.Root, "Ghost Show", "Season 01", "late.txt")),
                "O4 never touches anything outside the library folders, nor a folder that is no longer empty"
            );
            m.ManageDeleter = null;
            Directory.CreateDirectory(Path.Combine(f.Root, "Real Empty", "Season 01"));
            var real = m.SpaceSuggestions().SelectMany(g => g.Items).Single(i => i.Path.EndsWith("Real Empty"));
            Directory.CreateDirectory(Path.Combine(f.Root, "Real Empty", "Season 02"));
            await m.FreeSpace([real], DownloadDeleteChoice.Permanent);
            check(
                !Directory.Exists(Path.Combine(f.Root, "Real Empty")),
                "O4 empty folders are removed with the plain delete, nested ones first"
            );
            // A top folder that is a junction (made without admin rights) is refused, and nothing behind it is touched.
            var target = Path.Combine(Path.GetDirectoryName(f.Root)!, "junction-target");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "Thumbs.db"), "x");
            var linked = Path.Combine(f.Root, "Linked Empty");
            using (
                var mk = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linked}\" \"{target}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                    }
                )!
            )
                mk.WaitForExit();
            var isJunction =
                Directory.Exists(linked) && (File.GetAttributes(linked) & FileAttributes.ReparsePoint) != 0;
            var forgedFolder = new DownloadSpaceItem(linked, PcDownloads.KindEmptyFolders, "x", 0, linked);
            await m.FreeSpace([forgedFolder], DownloadDeleteChoice.Permanent);
            // The scan never lists a junction, so FreeSpace skips it; the delete itself must refuse it too, for a
            // folder swapped for a link between the rescan and the delete (reached directly here).
            string? guard = null;
            try
            {
                typeof(PcDownloads)
                    .GetMethod(
                        "RemoveLeftover",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                    )!
                    .Invoke(m, [forgedFolder, false]);
            }
            catch (System.Reflection.TargetInvocationException e) when (e.InnerException is IOException io)
            {
                guard = io.Message;
            }
            check(
                isJunction
                    && Directory.Exists(linked)
                    && File.Exists(Path.Combine(target, "Thumbs.db"))
                    && guard != null
                    && guard.Contains("redirected"),
                "O4 an empty-folder item whose top folder is a junction is never deleted, nor anything behind it"
            );
            if (Directory.Exists(linked))
                Directory.Delete(linked);
        }
        using (var f = new Fixture(parent))
        {
            var m = f.Manager;
            var movies = Path.Combine(Path.GetDirectoryName(f.Root)!, "movies");
            var a2 = f.Store.State.Catalog["a2"];
            var smallPath = a2.Item.Path;
            var bigPath = Path.Combine(f.Root, "extra", "Alpha.S01E02.1080p.BluRay.mkv");
            Directory.CreateDirectory(Path.GetDirectoryName(bigPath)!);
            File.WriteAllBytes(bigPath, new byte[350]);
            a2.Paths.Add(bigPath);
            f.Store.Record(f.Store.Archive.Single(i => i.Id == "a2"), 42.5, 120);
            f.Store.State.Intros[smallPath] = new() { Start = 5, End = 65 };
            f.Store.State.Outros[smallPath] = new() { Start = 100, End = 120 };
            var m1 = f.Store.State.Catalog["m1"];
            var movieBig = Path.Combine(movies, "m1-remux.mkv");
            File.WriteAllBytes(movieBig, new byte[900]);
            var movieSmall = m1.Item.Path;
            m1.Paths.Add(movieBig);
            var items = m.SpaceSuggestions()
                .SelectMany(g => g.Items)
                .Where(i => i.Kind == PcDownloads.KindDuplicates)
                .ToList();
            check(
                items.Count == 4
                    && items.Single(i => i.Path == smallPath).Preselect
                    && !items.Single(i => i.Path == bigPath).Preselect
                    && items.Single(i => i.Path == bigPath).Label.Contains("1080p")
                    && items.Single(i => i.Path == bigPath).Label.Contains("bluray", StringComparison.OrdinalIgnoreCase)
                    && items.Single(i => i.Path == movieBig).KeepPath == movieBig
                    && items.Single(i => i.Path == movieSmall).Preselect
                    && !items.Any(i => i.Label.Contains(f.Root, StringComparison.OrdinalIgnoreCase))
                    && !items.Any(i => i.Label.Contains(movies, StringComparison.OrdinalIgnoreCase)),
                "O5 duplicates are the same movie or episode on 2 paths, show quality from the name and size, and pre-select the smaller copy"
            );
            // Even with every copy ticked, one stays.
            var result = await m.FreeSpace(items, DownloadDeleteChoice.Permanent);
            var kept = f.Store.Archive.Single(i => i.Id == "a2");
            check(
                result.Errors.Count == 0
                    && !File.Exists(smallPath)
                    && File.Exists(bigPath)
                    && File.Exists(movieBig)
                    && !File.Exists(movieSmall)
                    && kept.Path == bigPath
                    && kept.Available,
                "O5 confirming deletes the smaller copies, never every copy, and the catalog points at the kept file"
            );
            check(
                f.Store.State.Progress.TryGetValue(bigPath, out var moved)
                    && moved.Position == 42.5
                    && f.Store.State.Intros.TryGetValue(bigPath, out var intro)
                    && intro.End == 65
                    && f.Store.State.Outros.TryGetValue(bigPath, out var outro)
                    && outro.Start == 100,
                "O5 progress and intro/outro marks are copied to the kept path"
            );
            check(
                !m.SpaceSuggestions().SelectMany(g => g.Items).Any(i => i.Kind == PcDownloads.KindDuplicates),
                "O5 after cleaning there is nothing left to list as a duplicate"
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
