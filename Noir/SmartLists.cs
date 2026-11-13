using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
#if !NOIR_HEADLESS
using System.Windows;
using System.Windows.Controls;
#endif
namespace Noir;

public sealed record SmartEpisode(
    string Id,
    int Season,
    int Episode,
    bool Watched,
    bool Started,
    int Watches,
    int Rating10,
    DateTimeOffset LastViewed,
    double Minutes,
    bool Playable,
    DateTimeOffset? AirsAt,
    string Summary = ""
);

public sealed record SmartTitle(
    string Key,
    bool Movie,
    bool Dropped,
    List<SmartEpisode> Episodes,
    IReadOnlyList<string> Directors
);

public sealed record SmartPick(string Key, string Reason, string EpisodeId);

public static class SmartLists
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<
        (string Profile, string Item),
        DateTimeOffset
    > streams = new();

    public static void RememberStream(string profile, string item, bool confirmed, DateTimeOffset? now = null)
    {
        var key = (profile, item);
        if (confirmed)
            streams[key] = now ?? DateTimeOffset.UtcNow;
        else
            streams.TryRemove(key, out _);
        if (streams.Count > 11000)
            foreach (var pair in streams)
                if (pair.Value < (now ?? DateTimeOffset.UtcNow).AddMinutes(-30))
                    streams.TryRemove(pair.Key, out _);
    }

    public static bool StreamReady(string profile, string item, DateTimeOffset now) =>
        streams.TryGetValue((profile, item), out var at) && at <= now && at >= now.AddMinutes(-30);

    public static readonly string[] Ids = ["unfinished", "comfort", "directors", "short-tonight"];

    public static string Name(string id) =>
        id switch
        {
            "unfinished" => "Unfinished business",
            "comfort" => "Comfort rewatches",
            "directors" => "Directors you love",
            "short-tonight" => "Short tonight",
            _ => id,
        };

    public static string Arabic(string id) =>
        id switch
        {
            "unfinished" => "حكايات لم تكتمل",
            "comfort" => "إعادة لأعمال تحبها",
            "directors" => "مخرجون تحبهم",
            "short-tonight" => "مشاهدة قصيرة الليلة",
            _ => id,
        };

    public static string Episode(SmartEpisode e) => $"S{e.Season}E{e.Episode}";

    public static string Ago(DateTimeOffset at, DateTimeOffset now, bool ar = false)
    {
        var days = Math.Max(0, (int)(now - at).TotalDays);
        return days >= 14
            ? (ar ? $"قبل {days / 7} أسابيع" : $"{days / 7} weeks ago")
            : (ar ? $"قبل {days} أيام" : $"{days} days ago");
    }

    public static Dictionary<string, List<SmartPick>> Build(
        IEnumerable<SmartTitle> titles,
        DateTimeOffset now,
        bool arabic = false
    )
    {
        var rows = Ids.ToDictionary(id => id, _ => new List<SmartPick>());
        var loved = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(SmartTitle Title, string[] Directors)>();
        foreach (var title in titles)
        {
            if (title.Episodes.Count == 0)
                continue;
            SmartEpisode? latest = null;
            SmartEpisode? next = null;
            var last = DateTimeOffset.MinValue;
            int rating = 0,
                rewatches = int.MaxValue;
            bool started = false,
                watched = false;
            int regular = 0;
            foreach (var ep in title.Episodes)
            {
                if (
                    (title.Movie || ep.Season > 0)
                    && (ep.Watched || ep.Started)
                    && (
                        latest == null
                        || ep.LastViewed > latest.LastViewed
                        || ep.LastViewed == latest.LastViewed
                            && (ep.Season > latest.Season || ep.Season == latest.Season && ep.Episode > latest.Episode)
                    )
                )
                    latest = ep;
                last = ep.LastViewed > last ? ep.LastViewed : last;
                rating = Math.Max(rating, ep.Rating10);
                started |= ep.Started || ep.Watched;
                watched |= ep.Watched;
                if (title.Movie || ep.Season > 0)
                {
                    regular++;
                    rewatches = Math.Min(rewatches, ep.Watches);
                    if (
                        !ep.Watched
                        && (
                            next == null
                            || ep.Season < next.Season
                            || ep.Season == next.Season && ep.Episode < next.Episode
                        )
                    )
                        next = ep;
                }
            }
            if (!title.Movie && latest != null)
            {
                next = latest.Started ? latest : null;
                if (!latest.Started)
                    foreach (var ep in title.Episodes)
                        if (
                            ep.Season > 0
                            && !ep.Watched
                            && (ep.Season > latest.Season || ep.Season == latest.Season && ep.Episode > latest.Episode)
                            && (
                                next == null
                                || ep.Season < next.Season
                                || ep.Season == next.Season && ep.Episode < next.Episode
                            )
                        )
                            next = ep;
            }
            if (title.Dropped)
                continue;
            var directors = title
                .Directors.Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (directors.Length > 0)
            {
                candidates.Add((title, directors));
                if (rating >= 8)
                    foreach (var director in directors)
                        loved[director] = loved.GetValueOrDefault(director) + 1;
            }
            var available = next != null && (next.Playable || next.AirsAt is { } air && air <= now);
            var aired = next?.AirsAt == null || next.AirsAt <= now;
            if (
                !title.Movie
                && started
                && last > DateTimeOffset.MinValue
                && last <= now.AddDays(-14)
                && available
                && aired
            )
                rows["unfinished"]
                    .Add(
                        new(
                            title.Key,
                            arabic
                                ? $"توقفت {Ago(last, now, true)} · التالية {Episode(next!)}"
                                : $"Stalled {Math.Max(2, (int)(now - last).TotalDays / 7)} weeks · next is {Episode(next!)}",
                            next!.Id
                        )
                    );
            var twice = regular > 0 && rewatches >= 2;
            if (twice || watched && rating >= 9 && last > DateTimeOffset.MinValue && last <= now.AddMonths(-6))
                rows["comfort"]
                    .Add(
                        new(
                            title.Key,
                            twice
                                ? (arabic ? "شاهدته مرتين أو أكثر" : "Watched at least twice")
                                : (
                                    arabic
                                        ? $"تقييمك ★ {rating / 2.0:0.#} · {Ago(last, now, true)}"
                                        : $"Your ★ {rating / 2.0:0.#} · {Ago(last, now)}"
                                ),
                            title.Episodes[0].Id
                        )
                    );
            if (
                next != null
                && next.Playable
                && aired
                && next.Minutes > 0
                && next.Minutes < (title.Movie ? 100 : 30)
                && (!title.Movie || !started)
            )
                rows["short-tonight"]
                    .Add(
                        new(
                            title.Key,
                            arabic
                                ? $"{Math.Ceiling(next.Minutes)} دقيقة · جاهز للمشاهدة"
                                : $"{Math.Ceiling(next.Minutes)} min · ready to play",
                            next.Id
                        )
                    );
        }
        foreach (var (title, directors) in candidates)
        {
            var director = directors.FirstOrDefault(d => loved.GetValueOrDefault(d) >= 2);
            if (director != null)
                rows["directors"]
                    .Add(
                        new(
                            title.Key,
                            arabic
                                ? $"{director} · قيّمت {loved[director]} من أعماله بأربع نجوم أو أكثر"
                                : $"{director} · {loved[director]} titles you rated ★ 4+",
                            title.Episodes[0].Id
                        )
                    );
        }
        return rows;
    }

    public static SmartTitle Read(
        LibraryStore store,
        List<VideoItem> group,
        IReadOnlyList<string>? directors = null,
        Func<VideoItem, bool>? playable = null
    )
    {
        var first = group[0];
        var schedule = store
            .State.EpisodeSchedules.GetValueOrDefault(first.GroupKey)
            ?.DistinctBy(e => (e.Season, e.Episode))
            .ToDictionary(e => (e.Season, e.Episode));
        var episodes = new List<SmartEpisode>(group.Count);
        foreach (var item in group)
        {
            var progress = LibraryStore.ProgressFor(store.State, item);
            var views = store.State.Viewings.GetValueOrDefault(item.Id) ?? [];
            var watched = views.Count > 0 || progress?.Completed == true;
            var at = views
                .Where(v => !v.Backfill)
                .Select(v => v.WatchedAt)
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Max();
            if (progress is { Completed: false, Position: > 0 } && progress.UpdatedAt > at)
                at = progress.UpdatedAt;
            var known = schedule?.GetValueOrDefault((item.Season, item.Episode));
            var minutes =
                item.Duration > 0 ? item.Duration / 60
                : known?.Runtime > 0 ? known.Runtime
                : store.State.ShowRuntimes.GetValueOrDefault(item.GroupKey);
            episodes.Add(
                new(
                    item.Id,
                    item.Season,
                    item.Episode,
                    watched,
                    progress is { Completed: false, Position: > 0 },
                    Math.Max(views.Count, watched ? 1 : 0),
                    store.State.ShowRatings.TryGetValue(item.GroupKey, out var showRating)
                        ? showRating * 2
                        : store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Rating10 ?? 0,
                    at,
                    minutes,
                    (!watched || progress is { Completed: false, Position: > 0 }) && (playable?.Invoke(item) ?? false),
                    known?.AirsAt,
                    item.EpisodeSummary
                )
            );
        }
        return new(
            first.GroupKey,
            first.IsMovie,
            store.State.TitleStatuses.GetValueOrDefault(first.GroupKey) == "Dropped",
            episodes,
            directors ?? []
        );
    }

    public static TitleCredits CachedCredits(string directory, VideoItem item)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId, @"^tt\d{1,12}$"))
            return new();
        _ = CreditsClient.CachedPeople(directory, item.ImdbId, item.IsMovie);
        JsonDocument? Read(string url)
        {
            var path = Path.Combine(
                directory,
                "credits",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".json"
            );
            try
            {
                return File.Exists(path) && new FileInfo(path).Length <= 2_000_000
                    ? JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8))
                    : null;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        using var meta = Read(
            $"https://v3-cinemeta.strem.io/meta/{(item.IsMovie ? "movie" : "series")}/{item.ImdbId}.json"
        );
        var credits = meta == null ? new TitleCredits() : CreditsClient.ParseCinemeta(meta.RootElement);
        if (!item.IsMovie)
        {
            using var show = Read("https://api.tvmaze.com/lookup/shows?imdb=" + item.ImdbId);
            if (show?.RootElement.TryGetProperty("id", out var id) == true && id.TryGetInt32(out var number))
            {
                using var cast = Read($"https://api.tvmaze.com/shows/{number}/cast");
                if (cast != null)
                    foreach (var person in CreditsClient.ParseTvmaze(cast.RootElement).Cast)
                    {
                        var old = credits.Cast.FirstOrDefault(p =>
                            p.Name.Equals(person.Name, StringComparison.OrdinalIgnoreCase)
                        );
                        if (old == null)
                            credits.Cast.Add(person);
                        else if (old.PhotoUrl.Length == 0)
                            old.PhotoUrl = person.PhotoUrl;
                    }
            }
        }
        foreach (var person in credits.Cast)
            if (person.PhotoUrl.Length > 0)
            {
                var path = Path.Combine(
                    directory,
                    "art",
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(person.PhotoUrl))) + ".jpg"
                );
                if (File.Exists(path))
                    person.Photo = path;
            }
        return credits;
    }
}

#if !NOIR_HEADLESS
public partial class MainWindow
{
    Dictionary<string, TitleCredits> smartCredits = new();
    string smartCreditsDirectory = "";
    DateTime smartCreditsStamp;
    bool smartCreditsLoading;

    void WarmSmartCredits(List<List<VideoItem>> groups)
    {
        var directory = store.DataDirectory;
        var folder = Path.Combine(directory, "credits");
        var stamp = Directory.Exists(folder) ? Directory.GetLastWriteTimeUtc(folder) : DateTime.MinValue;
        if (smartCreditsLoading || smartCreditsDirectory == directory && smartCreditsStamp == stamp)
            return;
        smartCreditsLoading = true;
        var items = store.Archive.DistinctBy(i => i.GroupKey).ToList();
        _ = Task.Run(() =>
            {
                var cache = new Dictionary<string, TitleCredits>();
                foreach (var item in items)
                {
                    var credits = SmartLists.CachedCredits(directory, item);
                    if (credits.Cast.Count + credits.Directors.Count > 0)
                        cache[item.GroupKey] = credits;
                }
                return cache;
            })
            .ContinueWith(task =>
                Dispatcher.InvokeAsync(() =>
                {
                    if (closing || store.DataDirectory != directory || !task.IsCompletedSuccessfully)
                    {
                        smartCreditsLoading = false;
                        return;
                    }
                    // Redraw only when the credits change a smart row; an unconditional Render rebuilt Home a second time on every startup.
                    var before = SmartRowsSignature(BuildSmartRows(groups));
                    smartCredits = task.Result;
                    smartCreditsDirectory = directory;
                    smartCreditsStamp = stamp;
                    var after = SmartRowsSignature(BuildSmartRows(groups));
                    smartCreditsLoading = false;
                    if (before != after)
                        Render();
                })
            );
    }

    static string SmartRowsSignature(Dictionary<string, List<SmartPick>> rows) =>
        string.Join(
            ";",
            rows.OrderBy(r => r.Key)
                .Select(r =>
                    r.Key + ":" + string.Join(",", r.Value.Select(p => p.Key + "/" + p.Reason + "/" + p.EpisodeId))
                )
        );

    Dictionary<string, List<SmartPick>> BuildSmartRows(List<List<VideoItem>> groups)
    {
        WarmSmartCredits(groups);
        var now = DateTimeOffset.UtcNow;
        var stream = CanStream;
        return SmartLists.Build(
            groups
                .Where(g => g.Count > 0)
                .Select(g =>
                    SmartLists.Read(
                        store,
                        g,
                        smartCredits.GetValueOrDefault(g[0].GroupKey)?.Directors.Select(p => p.Name).ToList(),
                        i =>
                            stream && SmartLists.StreamReady(store.DataDirectory, i.Id, now) && store.Aired(i, now)
                            || CanPlay(i)
                    )
                ),
            now,
            Loc.Arabic
        );
    }

    string SmartName(string id) => T(SmartLists.Name(id), SmartLists.Arabic(id));

    void RegisterSmartRows(List<List<VideoItem>> groups)
    {
        var rows = BuildSmartRows(groups);
        var index = groups.ToDictionary(g => g[0].GroupKey);
        foreach (var id in SmartLists.Ids)
        {
            homeRowNames[id] = SmartName(id);
            HomeRow(id, () =>
            {
                if (rows[id].Count < HomeLayout.SmartRowMin)
                    return;
                var cards = new List<FrameworkElement>();
                foreach (var pick in rows[id].Take(20))
                {
                    if (!index.TryGetValue(pick.Key, out var group))
                        continue;
                    var card = new StackPanel
                    {
                        Width = Density.PosterWidth,
                        Margin = new(0, 0, Density.CardGap, 12),
                    };
                    var poster = PosterCard(group);
                    poster.Margin = new(0);
                    card.Children.Add(poster);
                    var reason = Text(pick.Reason, 11, "#C9B18A");
                    reason.TextWrapping = TextWrapping.NoWrap;
                    reason.TextTrimming = TextTrimming.CharacterEllipsis;
                    reason.ToolTip = pick.Reason;
                    reason.Margin = new(0, 6, 0, 0);
                    card.Children.Add(reason);
                    cards.Add(card);
                }
                var rail = Rail(SmartName(id), "", cards);
                ContentPanel.Children.Add(rail);
#if NOIR_VALIDATE
                CaptureW3Element(rail, "row-" + id);
#endif
            });
        }
    }
}
#endif
