using System.Text.Json;
using Noir;

public static class HomeRedesignChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var expected = new[]
        {
            "continue",
            "top10",
            "because",
            "because-2",
            "picked",
            "fav-like",
            "genre-popular",
            "genre-top",
            "unfinished",
            "comfort",
            "directors",
            "short-tonight",
            "list",
            "ready",
        };
        var fresh = new HomeLayout().Resolve([]);
        check(
            fresh.Take(expected.Length).Select(r => r.Id).SequenceEqual(expected),
            "Home redesign defaults put discovery first and Keep ready immediately after My List"
        );
        check(
            new HomeLayout().ContinueStyle == "Wide" && new HomeLayout().ShowPosterTitles,
            "Home defaults to Wide Continue cards and visible poster titles"
        );
        var directory = Path.Combine(root, "home-redesign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var oldRows = new List<HomeRowSetting>
        {
            new() { Id = "continue", Visible = false },
            new()
            {
                Id = "ready",
                Visible = false,
                Pinned = true,
            },
            new() { Id = "services" },
            new() { Id = "because", Visible = false },
            new()
            {
                Id = "genre:Drama",
                Visible = false,
                Pinned = true,
            },
            new() { Id = "list" },
            new() { Id = "top10" },
        };
        File.WriteAllText(
            Path.Combine(directory, "home-layout.json"),
            JsonSerializer.Serialize(new { Rows = oldRows })
        );
        var savedPath = Path.Combine(directory, "home-layout.json");
        File.SetAttributes(savedPath, File.GetAttributes(savedPath) | FileAttributes.ReadOnly);
        try
        {
            var readOnly = HomeLayout.Load(directory).Resolve(["genre:Drama"]);
            check(
                !readOnly.Single(r => r.Id == "because").Visible && !readOnly.Single(r => r.Id == "ready").Visible,
                "A read-only legacy Home layout keeps user choices when migration cannot be saved"
            );
        }
        finally
        {
            File.SetAttributes(savedPath, File.GetAttributes(savedPath) & ~FileAttributes.ReadOnly);
        }
        var migrated = HomeLayout.Load(directory);
        var rows = migrated.Resolve(["genre:Drama"]);
        var ids = rows.Select(r => r.Id).ToList();
        check(
            migrated.Version == HomeLayout.CurrentVersion
                && ids.IndexOf("ready") == ids.IndexOf("list") + 1
                && ids.Skip(ids.IndexOf("because") + 1).Take(5).SequenceEqual(HomeLayout.DiscoveryRows),
            "Old Home layouts migrate Keep ready and insert discovery rows after Because"
        );
        check(
            !rows.Single(r => r.Id == "because").Visible
                && !rows.Single(r => r.Id == "ready").Visible
                && rows.Single(r => r.Id == "ready").Pinned
                && !rows.Single(r => r.Id == "genre:Drama").Visible
                && rows.Single(r => r.Id == "genre:Drama").Pinned,
            "Home migration preserves hidden rows and pinned choices"
        );
        check(
            rows[0].Id == "continue" && rows[0].Visible && rows.Count(r => r.Id == "continue") == 1,
            "Home migration locks Continue watching first and visible"
        );
        check(
            ids.Where(id => oldRows.Any(r => r.Id == id) && id != "ready")
                .SequenceEqual(oldRows.Where(r => r.Id != "ready").Select(r => r.Id)),
            "Home migration preserves the relative order of all other saved rows"
        );
        migrated.Rows = rows;
        var ready = rows.Single(r => r.Id == "ready");
        rows.Remove(ready);
        rows.Insert(1, ready);
        migrated.ContinueStyle = "Poster";
        migrated.ShowPosterTitles = false;
        migrated.Save(directory);
        var reopened = HomeLayout.Load(directory);
        check(
            reopened.Resolve(["genre:Drama"])[1].Id == "ready"
                && reopened.ContinueStyle == "Poster"
                && !reopened.ShowPosterTitles,
            "Home migration runs once and later user order and card preferences persist"
        );
        var store = new LibraryStore(Path.Combine(directory, "profile"));
        var source = store.AddDiscovered(
            new()
            {
                Id = "tt700001",
                Name = "Watched seed",
                Type = "series",
                Genres = ["Thriller", "Drama"],
            },
            false
        )[0];
        store.Record(source, 100, 100, true);
        var blocked = new[]
        {
            ("tt700002", "Dropped"),
            ("tt700003", "Hidden"),
            ("tt700004", "Hidden Continue"),
            ("tt700005", "Watched"),
        };
        foreach (var (id, status) in blocked)
        {
            var item = store.AddDiscovered(
                new()
                {
                    Id = id,
                    Name = status,
                    Genres = ["Thriller"],
                },
                false
            )[0];
            if (status == "Dropped")
                store.State.TitleStatuses[item.GroupKey] = "Dropped";
            else if (status == "Hidden")
                store.State.HiddenTitles.Add(item.GroupKey);
            else if (status == "Hidden Continue")
                store.State.HiddenContinue.Add(item.GroupKey);
            else
                store.Record(item, 100, 100, true);
        }
        var pool = Enumerable
            .Range(1, 24)
            .Select(i => new DiscoverTitle
            {
                Id = "tt" + (700000 + i),
                Name = "Catalog " + i,
                Genres = ["Thriller"],
            })
            .ToList();
        var selected = HomeDiscovery.Picks(store, pool.Concat(pool), ["tt700006"]);
        check(
            selected.Count >= 12
                && selected.Select(t => t.Id).Distinct().Count() == selected.Count
                && selected.All(t => !blocked.Select(b => b.Item1).Append("tt700006").Contains(t.Id)),
            "Home recommendations fill from catalogs while skipping watched, dropped, hidden and duplicate titles"
        );
        check(
            HomeDiscovery.TopGenres(store).Contains("Thriller"),
            "Home discovery derives genres from watched history"
        );
        check(
            HomeDiscovery.CatalogPath("movie", "Sci-Fi", true) == "catalog/movie/imdbRating/genre=Sci-Fi.json",
            "Top rated Home discovery uses Cinemeta's genre-filtered rating catalog"
        );
    }
}
