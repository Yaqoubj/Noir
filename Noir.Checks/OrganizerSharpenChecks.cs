using System.Text.Json;
using Noir;

// O1 (groups, list of runs, old journal), O2 (naming switches), O3 (new files only, never in use).
static class OrganizerSharpenChecks
{
    static LibraryStore Library(string dir, string name, params string[] files)
    {
        var tv = Path.Combine(dir, name, "TV");
        Directory.CreateDirectory(tv);
        foreach (var file in files)
            File.WriteAllText(Path.Combine(tv, file), "data of " + file);
        var store = new LibraryStore(Path.Combine(dir, name, "profile"));
        store.State.Roots = [new() { Path = tv }];
        store.State.OnlineMetadata = false;
        store.Scan(out _);
        foreach (var group in store.Archive.Select(i => i.GroupKey).Distinct().ToList())
        {
            var first = store.Archive.First(i => i.GroupKey == group);
            store.ApplyDetails(
                group,
                new()
                {
                    Title = first.Title,
                    Year = 2020,
                    SourceUrl = "https://example.org/verified",
                }
            );
        }
        return store;
    }

    public static void Run(string dir, Action<bool, string> check)
    {
        // O1: unticked shows stay where they are; the plan is grouped per show.
        var store = Library(
            dir,
            "o1",
            "Alpha.S01E01.mkv",
            "Alpha.S01E02.mkv",
            "Beta.S01E01.mkv",
            "Beta.S01E02.mkv",
            "Gamma.S01E01.mkv"
        );
        var tv = store.State.Roots[0].Path;
        var plan = store.PlanOrganization();
        var groups = plan.Groups();
        check(
            groups.Count == 3 && groups.Single(g => g.Name.StartsWith("Alpha")).Videos == 2,
            "O1 the organizer plan is grouped per show with its video count"
        );
        var beta = groups.Single(g => g.Name.StartsWith("Beta")).Key;
        var gamma = groups.Single(g => g.Name.StartsWith("Gamma")).Key;
        var movedFirst = store.Organize(plan.Without(new HashSet<string> { beta, gamma }));
        check(
            movedFirst == 2
                && File.Exists(Path.Combine(tv, "Beta.S01E01.mkv"))
                && File.Exists(Path.Combine(tv, "Gamma.S01E01.mkv"))
                && !File.Exists(Path.Combine(tv, "Alpha.S01E01.mkv")),
            "O1 unticked shows are left as they are and only the ticked show is organized"
        );
        // Two runs: the second can start with the first still in the journal, and Undo takes back only the last.
        var second = store.PlanOrganization();
        check(
            second.Groups().Count == 2 && second.Groups().All(g => !g.Name.StartsWith("Alpha")),
            "O1 a second plan lists only what is still unorganized"
        );
        var movedSecond = store.Organize(second.Without(new HashSet<string> { gamma }));
        check(
            movedSecond == 2 && store.OrganizationRuns() == 2,
            "O1 organizing can be repeated: the journal keeps both runs"
        );
        var undone = store.Organize(store.LastOrganization()!, true);
        check(
            undone == 2
                && File.Exists(Path.Combine(tv, "Beta.S01E01.mkv"))
                && !File.Exists(Path.Combine(tv, "Alpha.S01E01.mkv"))
                && Directory.Exists(Path.Combine(tv, "Alpha (2020)"))
                && store.OrganizationRuns() == 1,
            "O1 two runs then Undo undoes only the last run"
        );
        var reopened = new LibraryStore(store.DataDirectory);
        check(
            reopened.OrganizationRuns() == 1
                && reopened.Archive.Where(i => i.Title == "Alpha").All(i => i.Path.Contains("Alpha (2020)"))
                && reopened.Organize(reopened.LastOrganization()!, true) == 2
                && reopened.OrganizationRuns() == 0
                && File.Exists(Path.Combine(tv, "Alpha.S01E01.mkv")),
            "O1 the journal survives a restart and a second Undo takes back the earlier run"
        );

        // An organization.json from before O1 (one plan, no runs) still loads and can be undone; a new run is not blocked.
        var legacy = Library(dir, "legacy", "Delta.S01E01.mkv", "Delta.S01E02.mkv");
        var ltv = legacy.State.Roots[0].Path;
        var lplan = legacy.PlanOrganization();
        legacy.Organize(lplan);
        File.WriteAllText(
            legacy.OrganizerJournal,
            JsonSerializer.Serialize(
                new
                {
                    Moves = lplan.Moves,
                    Skipped = new string[0],
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            )
        );
        var old = new LibraryStore(legacy.DataDirectory);
        check(
            old.LastOrganization() is { } last
                && last.Moves.Count > 0
                && last.Moves.All(m => m.Done)
                && old.OrganizationRuns() == 1,
            "O1 an old single-plan organization.json still loads as one run"
        );
        File.WriteAllText(Path.Combine(ltv, "Delta.S01E03.mkv"), "third");
        old.Scan(out _);
        old.ApplyDetails(
            old.Archive.First().GroupKey,
            new()
            {
                Title = "Delta",
                Year = 2020,
                SourceUrl = "https://example.org/verified",
            }
        );
        var again = old.PlanOrganization();
        var ran = again.Moves.Count > 0 && old.Organize(again) == 1;
        check(
            ran && old.OrganizationRuns() == 2 && old.Organize(old.LastOrganization()!, true) == 1,
            "O1 a new run is not blocked by an old journal, and Undo takes back only that run"
        );

        // O2: one naming function for downloads and the organizer.
        var named = Library(dir, "o2", "Epic.S01E01.mkv", "Epic.S01E02.mkv");
        var ntv = named.State.Roots[0].Path;
        var mtv = Path.Combine(Path.GetDirectoryName(ntv)!, "Movies");
        Directory.CreateDirectory(mtv);
        File.WriteAllText(Path.Combine(mtv, "Film.2019.mkv"), "movie data");
        named.State.Roots.Add(new() { Path = mtv, Movies = true });
        named.Scan(out _);
        var filmGroup = named.Archive.First(i => i.IsMovie).GroupKey;
        named.ApplyDetails(
            filmGroup,
            new()
            {
                Title = "Film",
                Year = 2019,
                SourceUrl = "https://example.org/verified",
            }
        );
        named.State.Catalog.Values.First(e => !e.Item.IsMovie && e.Item.Episode == 1).Item.EpisodeTitle =
            "The Pilot: Part 1";
        var plain = named.PlanOrganization();
        var titled = named.PlanOrganization(new() { EpisodeTitle = true });
        var one = named.Archive.First(i => !i.IsMovie && i.Episode == 1);
        check(
            plain.Moves.Any(m => m.To.EndsWith("Epic (2020) S01E01.mkv"))
                && titled.Moves.Any(m => m.To.EndsWith("Epic (2020) S01E01 - The Pilot Part 1.mkv"))
                && titled.Moves.Any(m => m.To.EndsWith("Epic (2020) S01E02.mkv")),
            "O2 the episode title is added only when the switch is on, and an episode without a title keeps the plain name"
        );
        check(
            titled.Moves.Single(m => m.Video && m.ItemId == one.Id).To
                == DownloadNaming.Destination(ntv, one, "x.mkv", new() { EpisodeTitle = true })
                && plain.Moves.Single(m => m.Video && m.ItemId == one.Id).To
                    == DownloadNaming.Destination(ntv, one, "x.mkv"),
            "O2 new downloads and the organizer share one naming function"
        );
        var movie = named.Archive.First(i => i.IsMovie);
        var movieOff = named.PlanOrganization(new() { MovieFolder = false });
        var movieOn = named.PlanOrganization();
        check(
            movieOn
                .Moves.Single(m => m.ItemId == movie.Id && m.Video)
                .To.Contains(Path.DirectorySeparatorChar + "Film (2019)" + Path.DirectorySeparatorChar)
                && movieOff.Moves.Single(m => m.ItemId == movie.Id && m.Video).To
                    == Path.Combine(mtv, "Film (2019).mkv"),
            "O2 a movie keeps its own folder unless the switch is off"
        );
        // Turning the switch on shows renames in the preview, and renames nothing by itself.
        named.Organize(plain);
        var before = Directory.EnumerateFiles(ntv, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();
        var preview = named.PlanOrganization(new() { EpisodeTitle = true });
        var after = Directory.EnumerateFiles(ntv, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();
        check(
            named.PlanOrganization().Moves.Count == 0
                && preview.Moves.Count(m => m.Video) == 1
                && Path.GetDirectoryName(preview.Moves[0].From) == Path.GetDirectoryName(preview.Moves[0].To)
                && before.SequenceEqual(after),
            "O2 turning the episode-title switch on shows a rename in the preview and renames nothing by itself"
        );
        var renamed = named.Organize(preview);
        check(
            renamed == 1
                && named.Archive.First(i => i.Id == one.Id).Path.EndsWith("Epic (2020) S01E01 - The Pilot Part 1.mkv")
                && named.Organize(named.LastOrganization()!, true) == 1
                && named.Archive.First(i => i.Id == one.Id).Path.EndsWith("Epic (2020) S01E01.mkv"),
            "O2 Undo restores the old names"
        );

        // O3: only new files, never a file in use or being downloaded.
        var auto = Library(dir, "o3", "Zed.S01E01.mkv", "Zed.S01E02.mkv", "Zed.S01E03.mkv", "Zed.S01E04.mkv");
        var paths = auto.Archive.OrderBy(i => i.Episode).Select(i => i.Path).ToList();
        var fresh = new HashSet<string>(paths.Skip(1), StringComparer.OrdinalIgnoreCase);
        int organized;
        using (new FileStream(paths[2], FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            organized = auto.OrganizeNewFiles(fresh, new(), p => p == paths[3]);
        check(
            organized == 1
                && File.Exists(paths[0])
                && !File.Exists(paths[1])
                && File.Exists(paths[2])
                && File.Exists(paths[3])
                && auto.LastOrganization()!.Auto,
            "O3 automatic organizing takes only new files and skips ones in use, downloading or playing"
        );
        var rest = auto.OrganizeNewFiles(
            new HashSet<string>(auto.Archive.Select(i => i.Path), StringComparer.OrdinalIgnoreCase),
            new()
        );
        check(
            rest == 3 && auto.OrganizationRuns() == 2 && auto.Organize(auto.LastOrganization()!, true) == 3,
            "O3 automatic runs are recorded for Undo as runs of their own"
        );
    }
}
