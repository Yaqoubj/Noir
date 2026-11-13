using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
#if !NOIR_HEADLESS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
#endif
namespace Noir;

public static class HomeDiscovery
{
    public static string[] Genres(string genres) =>
        genres.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public static string CatalogPath(string type, string genre, bool rated = false) =>
        $"catalog/{type}/{(rated ? "imdbRating" : "top")}/genre={Uri.EscapeDataString(genre)}.json";

    public static string CachePath(string directory, string path) =>
        Path.Combine(
            directory,
            "discover",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))) + ".json"
        );

    public static List<DiscoverTitle> Parse(string json, string type)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("metas", out var metas) && metas.ValueKind == JsonValueKind.Array
            ? metas
                .EnumerateArray()
                .Select(m => DiscoveryClient.Parse(m, type))
                .Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.Id, @"^tt\d+$"))
                .ToList()
            : [];
    }

    static string Identity(string title) =>
        System.Text.RegularExpressions.Regex.Replace(title.ToUpperInvariant(), @"[^\p{L}\p{N}]", "");

    public static bool Eligible(LibraryStore store, List<VideoItem> group) =>
        group.Count > 0
        && !group.Any(store.EverWatched)
        && !group.Any(i =>
            Excluded(store, i.GroupKey)
            || Excluded(store, i.Id)
            || i.ImdbId.Length > 0 && Excluded(store, (i.IsMovie ? "movie:" : "show:") + i.ImdbId)
        );

    static bool Excluded(LibraryStore store, string key) =>
        store.State.TitleStatuses.GetValueOrDefault(key) == "Dropped"
        || store.State.HiddenTitles.Contains(key)
        || store.State.HiddenContinue.Contains(key);

    public static bool Eligible(LibraryStore store, DiscoverTitle title)
    {
        if (Excluded(store, title.Id) || Excluded(store, (title.IsMovie ? "movie:" : "show:") + title.Id))
            return false;
        var matches = store
            .Archive.Where(i =>
                i.IsMovie == title.IsMovie
                && (
                    i.ImdbId == title.Id
                    || Identity(i.Title) == Identity(title.Name)
                        && (i.Year == 0 || title.Year == 0 || i.Year == title.Year)
                )
            )
            .GroupBy(i => i.GroupKey);
        return matches.All(g => Eligible(store, g.ToList()));
    }

    public static List<DiscoverTitle> Picks(
        LibraryStore store,
        IEnumerable<DiscoverTitle> titles,
        IEnumerable<string>? exclude = null,
        int limit = 20
    )
    {
        var seen = (exclude ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return titles.Where(t => Eligible(store, t) && seen.Add(t.Id)).Take(limit).ToList();
    }

    public static string[] TopGenres(LibraryStore store) =>
        store
            .Archive.GroupBy(i => i.GroupKey)
            .Where(g => g.Any(store.EverWatched))
            .SelectMany(g => Genres(g.First().Genres))
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(2)
            .Select(g => g.Key)
            .ToArray();
}

#if !NOIR_HEADLESS
public partial class MainWindow
{
    sealed record HomeCatalog(List<DiscoverTitle> Titles, DateTimeOffset At);

    readonly Dictionary<string, HomeCatalog> homeCatalogs = new();
    readonly HashSet<string> homeCatalogPending = [];
    string homeCatalogDirectory = "";
    static readonly HttpClient homeCatalogHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    bool homePosterTitles = true;

    List<DiscoverTitle> HomeGenre(string type, string genre, bool rated = false)
    {
        var directory = store.DataDirectory;
        if (homeCatalogDirectory != directory)
        {
            homeCatalogDirectory = directory;
            homeCatalogs.Clear();
            homeCatalogPending.Clear();
        }
        var key = HomeDiscovery.CatalogPath(type, genre, rated);
        homeCatalogs.TryGetValue(key, out var cached);
        if (
            store.State.OnlineMetadata
            && (cached == null || cached.At < DateTimeOffset.UtcNow.AddHours(-6))
            && homeCatalogPending.Add(key)
        )
            _ = LoadHomeGenre(directory, key, type, genre, rated);
        return cached?.Titles ?? [];
    }

    async Task LoadHomeGenre(string directory, string key, string type, string genre, bool rated)
    {
        List<DiscoverTitle> titles = [];
        bool success = false;
        try
        {
            if (!rated)
            {
                using var client = DiscoverClient();
                titles = await client.Genre(type, genre, default);
            }
            else
            {
                var path = HomeDiscovery.CachePath(directory, key);
                string json;
                if (File.Exists(path) && File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddHours(-12))
                    json = await File.ReadAllTextAsync(path);
                else
                {
                    try
                    {
                        json = await homeCatalogHttp.GetStringAsync("https://v3-cinemeta.strem.io/" + key);
                        _ = HomeDiscovery.Parse(json, type);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            await File.WriteAllTextAsync(temp, json);
                            File.Move(temp, path, true);
                        }
                        finally
                        {
                            if (File.Exists(temp))
                                File.Delete(temp);
                        }
                    }
                    catch when (File.Exists(path))
                    {
                        json = await File.ReadAllTextAsync(path);
                    }
                }
                titles = HomeDiscovery.Parse(json, type);
            }
            success = true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            store.Log(e);
        }
        if (closing || directory != store.DataDirectory || directory != homeCatalogDirectory)
            return;
        homeCatalogPending.Remove(key);
        var before = homeCatalogs.GetValueOrDefault(key);
        if (!success && before != null)
            titles = before.Titles;
        string Signature(IEnumerable<DiscoverTitle> list) =>
            string.Join(
                "|",
                HomeDiscovery.Picks(store, list).Select(t => t.Id + ":" + t.Name + ":" + t.Rating + ":" + t.PosterUrl)
            );
        var changed = Signature(before?.Titles ?? []) != Signature(titles);
        homeCatalogs[key] = new(
            titles,
            success ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow.AddHours(-6).AddMinutes(10)
        );
        if (changed && store.State.OnlineMetadata && page == "Home" && detailKey == null)
            QueueMetadataRender();
    }

    IEnumerable<DiscoverTitle> HomeGenrePool(IEnumerable<string> genres, bool movie, bool rated = false)
    {
        var lists = new List<DiscoverTitle>();
        foreach (var type in movie ? new[] { "movie", "series" } : new[] { "series", "movie" })
        foreach (var genre in genres.Take(3))
            lists.AddRange(HomeGenre(type, genre, rated));
        return lists;
    }

    FrameworkElement HomeDiscoverCard(DiscoverTitle title, string reason = "", bool wide = false)
    {
        FrameworkElement card;
        if (wide)
        {
            var panel = new StackPanel { Width = Density.DiscoveryWide, Margin = new(0, 0, 18, 8) };
            var picture = new Grid { Height = Density.DiscoveryWideHeight, ClipToBounds = true };
            picture.Children.Add(
                Art(
                    File.Exists(title.Background) ? title.Background : title.Poster,
                    title.Name,
                    Density.DiscoveryWideHeight
                )
            );
            picture.Children.Add(
                new Border
                {
                    Background = new LinearGradientBrush(Colors.Transparent, Color.FromArgb(240, 11, 11, 13), 90),
                    IsHitTestVisible = false,
                }
            );
            var name = Text(title.Name, 18, weight: FontWeights.SemiBold);
            name.Margin = new(14);
            name.VerticalAlignment = VerticalAlignment.Bottom;
            name.IsHitTestVisible = false;
            picture.Children.Add(name);
            var button = ActionButton("", () => _ = OpenDiscover(title));
            button.Content = picture;
            button.Padding = new(0);
            System.Windows.Automation.AutomationProperties.SetName(button, T("Explore {0}", "استكشف {0}", title.Name));
            var edge = new Border
            {
                CornerRadius = new(12),
                ClipToBounds = true,
                Child = button,
            };
            panel.Children.Add(edge);
            card = Lift(panel, edge);
            _ = LoadWide();
            async Task LoadWide()
            {
                try
                {
                    using var client = DiscoverClient();
                    await client.Artwork(title, default, true);
                    var source = await LoadImage(File.Exists(title.Background) ? title.Background : title.Poster, 640);
                    if (!closing && source != null)
                    {
                        picture.Children.RemoveAt(0);
                        picture.Children.Insert(0, new Image { Source = source, Stretch = Stretch.UniformToFill });
                    }
                }
                catch (Exception e)
                {
                    store.Log(e);
                }
            }
        }
        else
        {
            card = DiscoverCard(title, default, discoverRevision, true);
            if (!homePosterTitles && card is StackPanel body)
                foreach (var text in body.Children.OfType<TextBlock>())
                    text.Visibility = Visibility.Collapsed;
            if (card is StackPanel details)
            {
                var meta = details.Children.OfType<TextBlock>().LastOrDefault();
                if (meta != null)
                    meta.Text =
                        T(title.IsMovie ? "Movie" : "Series", title.IsMovie ? "فيلم" : "مسلسل")
                        + (title.Years.Length > 0 ? " · " + title.Years : "");
            }
            var button = ViewElements(card).OfType<Button>().FirstOrDefault();
            if (button != null)
                System.Windows.Automation.AutomationProperties.SetName(
                    button,
                    T("Explore {0}", "استكشف {0}", title.Name)
                );
        }
        if (reason.Length > 0 && card is Panel host)
        {
            var label = Text(reason, 11, "#C9B18A");
            label.Margin = new(0, 8, 0, 0);
            label.MaxWidth = wide ? Density.DiscoveryWide : Density.PosterWidth;
            label.TextWrapping = TextWrapping.NoWrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.ToolTip = reason;
            host.Children.Add(label);
        }
        return FollowHomeHero(card, () => HomeHeroItem(title));
    }

    VideoItem HomeHeroItem(DiscoverTitle title) =>
        new()
        {
            Id = title.Id,
            CollectionKey = title.Type + ":" + title.Id,
            ImdbId = title.Id,
            Title = title.Name,
            IsMovie = title.IsMovie,
            Year = title.Year,
            Poster = title.Poster,
            Backdrop = title.Background,
            Summary = title.Summary,
            Genres = string.Join(" · ", title.Genres),
            Available = false,
        };

    // shown: titles already on another Because row this render, so the two rows never repeat each other.
    List<FrameworkElement> HomeBecauseCards(List<VideoItem> seed, List<List<VideoItem>> library, HashSet<string> shown)
    {
        var tastes = HomeDiscovery.Genres(seed[0].Genres);
        var local = library
            .Where(g =>
                g[0].GroupKey != seed[0].GroupKey
                && !shown.Contains(g[0].GroupKey)
                && HomeDiscovery.Eligible(store, g)
                && HomeDiscovery.Genres(g[0].Genres).Intersect(tastes, StringComparer.OrdinalIgnoreCase).Any()
            )
            .Take(20)
            .ToList();
        var cards = local.Select(PosterCard).ToList();
        shown.UnionWith(local.Select(g => g[0].GroupKey));
        shown.UnionWith(local.Select(g => g[0].ImdbId).Where(id => id.Length > 0));
        if (store.State.OnlineMetadata)
        {
            var exclusions = local.Select(g => g[0].ImdbId).Append(seed[0].ImdbId).Concat(shown);
            var picks = HomeDiscovery
                .Picks(store, HomeGenrePool(tastes, seed[0].IsMovie), exclusions, 20 - cards.Count)
                .ToList();
            shown.UnionWith(picks.Select(t => t.Id));
            cards.AddRange(picks.Select(t => HomeDiscoverCard(t)));
        }
        return cards;
    }

    void RegisterHomeDiscovery(List<List<VideoItem>> groups)
    {
        homePosterTitles = HomeLayout.Load(store.DataDirectory).ShowPosterTitles;
        var library = store.LibraryGroups();
        var local = groups.Where(g => library.Contains(g[0].GroupKey)).ToList();
        var history = store
            .Archive.GroupBy(i => i.GroupKey)
            .Select(g => g.ToList())
            .Where(g => g.Any(store.EverWatched))
            .OrderByDescending(g => g.Max(HistoryDate))
            .DistinctBy(g => g[0].ImdbId.Length > 0 ? g[0].ImdbId : g[0].GroupKey)
            .ToList();
        foreach (var (id, index) in new[] { ("because", 0), ("because-2", 1) })
        {
            var seed = history.ElementAtOrDefault(index);
            homeRowNames[id] =
                seed == null ? HomeRowName(id) : T("Because you watched {0}", "لأنك شاهدت {0}", seed[0].Title);
            HomeRow(id, () =>
            {
                if (seed == null)
                    return;
                var cards = HomeBecauseCards(seed, local, homeShown);
                if (cards.Count > 0)
                    ContentPanel.Children.Add(Rail(homeRowNames[id], "", cards, () => Navigate("Discover")));
            });
        }
        var loved = store
            .Archive.GroupBy(i => i.GroupKey)
            .Select(g => g.ToList())
            .Where(g =>
                store.State.Favorites.Contains(g[0].GroupKey)
                || g.Any(i => store.State.Likes.Contains(i.GroupKey) || store.State.Likes.Contains(i.Id))
            )
            .OrderBy(g =>
                store.State.Favorites.IndexOf(g[0].GroupKey) is var index && index >= 0 ? index : int.MaxValue
            )
            .ToList();
        var rated = store
            .Archive.GroupBy(i => i.GroupKey)
            .Select(g => g.ToList())
            .Where(g =>
                store.State.ShowRatings.GetValueOrDefault(g[0].GroupKey) >= 4
                || g.Any(i => (store.State.PersonalReviews.GetValueOrDefault(i.Id)?.Rating10 ?? 0) >= 8)
            )
            .ToList();
        var genres = HomeDiscovery.TopGenres(store);
        foreach (var id in new[] { "picked", "fav-like" })
        {
            homeRowNames[id] = HomeRowName(id);
            HomeRow(id, () =>
            {
                if (!store.State.OnlineMetadata)
                    return;
                var seeds = (id == "fav-like" ? loved : loved.Concat(rated))
                    .DistinctBy(g => g[0].GroupKey)
                    .Take(4)
                    .ToList();
                if (id == "fav-like" && seeds.Count == 0)
                    return;
                var channels = new List<List<(DiscoverTitle Title, string Reason)>>();
                foreach (var seed in seeds)
                {
                    var reason = T(
                        loved.Contains(seed) ? "Because you liked {0}" : "Because you rated {0} highly",
                        loved.Contains(seed) ? "لأنك أحببت {0}" : "لأنك قيّمت {0} بتقييم عالٍ",
                        seed[0].Title
                    );
                    channels.Add(
                        HomeGenrePool(HomeDiscovery.Genres(seed[0].Genres).Take(2), seed[0].IsMovie)
                            .Select(t => (t, reason))
                            .ToList()
                    );
                }
                if (id == "picked")
                    foreach (var genre in genres)
                        channels.Add(
                            HomeGenrePool([genre], false)
                                .Select(t => (t, T("Popular in {0}", "الرائج في {0}", T(genre))))
                                .ToList()
                        );
                var choices = Enumerable
                    .Range(0, channels.Select(c => c.Count).DefaultIfEmpty().Max())
                    .SelectMany(i => channels.Where(c => i < c.Count).Select(c => c[i]));
                var seen = seeds.Select(g => g[0].ImdbId).ToHashSet();
                var picks = FreshFirst(
                    choices.Where(p => HomeDiscovery.Eligible(store, p.Title) && seen.Add(p.Title.Id)).Take(80),
                    p => p.Title.Id
                );
                if (picks.Count > 0)
                    ContentPanel.Children.Add(
                        Rail(
                            homeRowNames[id],
                            "",
                            picks
                                .Select((p, i) => HomeDiscoverCard(p.Title, p.Reason, id == "picked" && i == 0))
                                .ToList(),
                            () => Navigate("Discover")
                        )
                    );
            });
        }
        foreach (var (id, top) in new[] { ("genre-popular", false), ("genre-top", true) })
        {
            var genre = genres.FirstOrDefault();
            homeRowNames[id] =
                genre == null
                    ? HomeRowName(id)
                    : T(
                        top ? "Top rated in {0}" : "Popular in {0}",
                        top ? "الأعلى تقييمًا في {0}" : "الرائج في {0}",
                        T(genre)
                    );
            HomeRow(id, () =>
            {
                if (!store.State.OnlineMetadata || genre == null)
                    return;
                var pool = HomeGenrePool([genre], false, top);
                if (top)
                    pool = pool.OrderByDescending(t =>
                        double.TryParse(t.Rating, NumberStyles.Float, CultureInfo.InvariantCulture, out var score)
                            ? score
                            : 0
                    );
                var picks = FreshFirst(HomeDiscovery.Picks(store, pool, null, 80), t => t.Id);
                if (picks.Count > 0)
                    ContentPanel.Children.Add(
                        Rail(homeRowNames[id], "", picks.Select(t => HomeDiscoverCard(t)).ToList(), () =>
                            Navigate("Discover")
                        )
                    );
            });
        }
    }
}
#endif
