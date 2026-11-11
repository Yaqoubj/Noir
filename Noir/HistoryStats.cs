using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Noir;

// History → Stats: what you have watched, when, and how far along you are. Every number comes
// from the saved viewing diary and bookmarks, so it includes titles whose files are gone.
public partial class MainWindow
{
    sealed record Watched(
        VideoItem Item,
        DateTimeOffset At,
        double Seconds,
        string Source,
        bool DateUnknown = false,
        bool Approximate = false
    )
    {
        public bool Precise => !DateUnknown && !Approximate;
    }

    double RuntimeOf(VideoItem item, IReadOnlyDictionary<string, double> typical)
    {
        var bookmark = store.GetProgress(item);
        if (bookmark?.Duration > 60)
            return bookmark.Duration;
        if (item.Duration > 60)
            return item.Duration;
        return typical.GetValueOrDefault(item.GroupKey, item.IsMovie ? 6600 : 2580);
    }

    List<Watched> AllViewings()
    {
        var archive = store.Archive;
        // Episodes without a known length use their show's typical length.
        var typical = archive
            .GroupBy(i => i.GroupKey)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var known = g.Select(i =>
                            store.GetProgress(i)?.Duration > 60 ? store.GetProgress(i)!.Duration : i.Duration
                        )
                        .Where(d => d > 60)
                        .ToList();
                    // Otherwise the show's real runtime from TVmaze, before the generic guess.
                    return known.Count > 0 ? known.Average() : store.RuntimeMinutes(g.First()) * 60.0;
                }
            );
        return archive
            .SelectMany(i =>
                store
                    .ViewingHistory(i)
                    .Select(v => new Watched(
                        i,
                        v.WatchedAt,
                        RuntimeOf(i, typical),
                        v.Source,
                        v.DateUnknown,
                        v.Approximate
                    ))
            )
            .OrderByDescending(w => w.At)
            .ToList();
    }

    static string Duration(double seconds, bool compact = false)
    {
        var span = TimeSpan.FromSeconds(seconds);
        if (compact)
            return span.TotalHours >= 1 ? $"{span.TotalHours:0.#} h" : $"{span.TotalMinutes:0} min";
        var parts = new List<string>();
        if (span.Days > 0)
            parts.Add(span.Days + (span.Days == 1 ? " day" : " days"));
        if (span.Hours > 0)
            parts.Add(span.Hours + (span.Hours == 1 ? " hour" : " hours"));
        if (span.Days == 0 && span.Minutes > 0)
            parts.Add(span.Minutes + " min");
        return parts.Count > 0 ? string.Join(" ", parts) : "0 min";
    }

    static (int Current, int Longest) Streaks(IEnumerable<DateTime> days)
    {
        return HistoryRewards.Streaks(days.Select(d => new RewardViewing(d)), DateTime.Today);
    }

    int historyReviewYear = DateTime.Today.Year;

    void RenderYearReview(List<Watched> views)
    {
        var years = views
            .Where(v => !v.DateUnknown)
            .Select(v => v.At.LocalDateTime.Year)
            .Append(DateTime.Today.Year)
            .Distinct()
            .OrderDescending()
            .ToArray();
        var selected = views.Where(v => !v.DateUnknown && v.At.LocalDateTime.Year == historyReviewYear).ToList();
        var card = new Border
        {
            CornerRadius = new(16),
            Background = Brush("#191715"),
            BorderBrush = Brush("#4DE4C084"),
            BorderThickness = new(1),
            Padding = new(22),
            Margin = new(0, 16, 0, 12),
        };
        var body = new StackPanel();
        card.Child = body;
        var head = new DockPanel();
        var year = ActionButton(historyReviewYear + "  [[chevron-down]]", () => { });
        year.Background = Brush("#22E4C084");
        year.Foreground = Brush("#E4C084");
        year.Padding = new(12, 5, 12, 5);
        DockPanel.SetDock(year, Dock.Right);
        head.Children.Add(year);
        head.Children.Add(Text(T("Year in review", "حصاد السنة"), 22, "#F4F1EB", FontWeights.SemiBold));
        body.Children.Add(head);
        year.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            foreach (var y in years)
            {
                var value = y;
                var item = new MenuItem
                {
                    Header = y.ToString(),
                    IsCheckable = true,
                    IsChecked = y == historyReviewYear,
                };
                item.Click += (_, _) =>
                {
                    historyReviewYear = value;
                    RenderQuiet();
                };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = year;
            menu.IsOpen = true;
        };
        var busiest = selected
            .GroupBy(v => v.At.LocalDateTime.Month)
            .OrderByDescending(g => g.Sum(v => v.Seconds))
            .FirstOrDefault();
        var longest = HistoryRewards
            .Streaks(
                selected.Select(v => new RewardViewing(
                    v.At.LocalDateTime.Date,
                    v.Item.IsMovie,
                    v.DateUnknown,
                    v.Approximate
                )),
                new DateTime(historyReviewYear, 12, 31)
            )
            .Longest;
        var metrics = new WrapPanel { Margin = new(0, 12, 0, 16) };
        foreach (
            var pair in new[]
            {
                (T("Hours watched", "ساعات المشاهدة"), $"{selected.Sum(v => v.Seconds) / 3600:0.#}"),
                (
                    T("Busiest month", "أكثر الشهور مشاهدة"),
                    busiest == null
                        ? "—"
                        : new DateTime(historyReviewYear, busiest.Key, 1).ToString("MMMM", Loc.Culture)
                ),
                (T("Longest streak", "أطول سلسلة مشاهدة"), T("{0} days", "{0} يوم", longest)),
            }
        )
        {
            var metric = new StackPanel { MinWidth = 145, Margin = new(0, 0, 26, 8) };
            metric.Children.Add(Text(pair.Item2, 23, "#E4C084", FontWeights.SemiBold));
            metric.Children.Add(Text(pair.Item1, 11.5, "#B6AA96"));
            metrics.Children.Add(metric);
        }
        body.Children.Add(metrics);
        var tops = new StackPanel();
        foreach (var movie in new[] { false, true })
        {
            var top = selected
                .Where(v => v.Item.IsMovie == movie)
                .GroupBy(v => v.Item.GroupKey)
                .OrderByDescending(g => g.Sum(v => v.Seconds))
                .Take(2)
                .ToList();
            var topRow = new WrapPanel();
            if (top.Count > 0)
            {
                var topLabel = Text(
                    movie ? T("Top movies", "من أبرز الأفلام") : T("Top shows", "من أبرز المسلسلات"),
                    11,
                    "#B6AA96"
                );
                topLabel.Margin = new(0, 4, 0, 6);
                tops.Children.Add(topLabel);
                tops.Children.Add(topRow);
            }
            foreach (var group in top)
            {
                var item = group.First().Item;
                var row = new Grid { Width = 200, Margin = new(0, 0, 18, 8) };
                row.ColumnDefinitions.Add(new() { Width = new(52) });
                row.ColumnDefinitions.Add(new());
                row.Children.Add(
                    new Border
                    {
                        CornerRadius = new(6),
                        ClipToBounds = true,
                        Child = HistoryArt(item, 75),
                    }
                );
                var info = new StackPanel { Margin = new(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(info, 1);
                var title = Text(item.Title, 13.5, weight: FontWeights.SemiBold);
                title.TextWrapping = TextWrapping.NoWrap;
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                info.Children.Add(title);
                info.Children.Add(Text(Duration(group.Sum(v => v.Seconds), true), 11, "#E4C084"));
                row.Children.Add(info);
                var open = new Button
                {
                    Content = row,
                    Padding = new(0),
                    Background = Brush("Transparent"),
                    ToolTip = item.Title,
                };
                open.Click += (_, _) => OpenHistoryTitle(store.EpisodesFor(item.GroupKey));
                topRow.Children.Add(open);
            }
        }
        if (selected.Count == 0)
            body.Children.Add(
                Text(
                    T(
                        "A fresh chapter. Log a story to start your year.",
                        "بداية جديدة. سجّل مشاهدة لتبدأ حكاية السنة."
                    ),
                    13,
                    "#C7BBA7"
                )
            );
        else
            body.Children.Add(tops);
        ContentPanel.Children.Add(card);
        ContentPanel.Children.Add(Heading(T("On this day", "في مثل هذا اليوم")));
        var memories = views
            .Where(v =>
                v.Precise
                && v.At.LocalDateTime.Year < DateTime.Today.Year
                && v.At.LocalDateTime.Month == DateTime.Today.Month
                && v.At.LocalDateTime.Day == DateTime.Today.Day
            )
            .Take(6)
            .ToList();
        if (memories.Count == 0)
            ContentPanel.Children.Add(
                Body(
                    T(
                        "Today's memories are still ahead. Your diary will keep them here.",
                        "ذكريات هذا اليوم قادمة. ستحتفظ بها يومياتك هنا."
                    ),
                    13
                )
            );
        else
            foreach (var memory in memories)
            {
                var row = new DockPanel { Margin = new(0, 0, 0, 8) };
                var poster = new Border
                {
                    Width = 34,
                    Height = 50,
                    CornerRadius = new(5),
                    ClipToBounds = true,
                    Child = HistoryArt(memory.Item, 50),
                    Margin = new(0, 0, 12, 0),
                };
                DockPanel.SetDock(poster, Dock.Left);
                row.Children.Add(poster);
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(Text(memory.Item.Title, 14, weight: FontWeights.SemiBold));
                info.Children.Add(
                    Text(
                        memory.At.LocalDateTime.Year
                            + "  ·  "
                            + (memory.Item.IsMovie ? T("Movie") : Short(memory.Item)),
                        11.5,
                        "#C0AC89"
                    )
                );
                row.Children.Add(info);
                ContentPanel.Children.Add(
                    new Border
                    {
                        Background = Brush("#151517"),
                        CornerRadius = new(10),
                        Padding = new(12, 8, 12, 8),
                        Margin = new(0, 0, 0, 6),
                        Child = row,
                    }
                );
            }
    }

    void RenderHistoryStats()
    {
        var views = AllViewings();
        var story = ActionButton(
            T("Your {0} story [[chevron]]", "حكايتك في {0} [[chevron]]", historyReviewYear),
            () => OpenYearStory(historyReviewYear),
            true
        );
        story.Margin = new(0, 0, 10, 0);
        var taste = ActionButton(T("Compare taste with a friend [[chevron]]", "قارن ذوقك مع صديق [[chevron]]"), () =>
            OpenTaste()
        );
        taste.Margin = new(0, 0, 0, 0);
        var storyRow = new WrapPanel { Margin = new(0, 14, 0, 0) };
        storyRow.Children.Add(story);
        storyRow.Children.Add(taste);
        ContentPanel.Children.Add(storyRow);
        if (views.Count == 0)
        {
            ContentPanel.Children.Add(
                Heading(
                    T("Your stats start with your first story", "إحصاءاتك تبدأ بأول حكاية"),
                    T(
                        "Log a favourite, or bring your history from Netflix, Trakt and more.",
                        "سجّل عملاً تحبه أو استورد سجلك من نتفليكس وتراكت وغيرها."
                    )
                )
            );
            HistoryEmptyActions();
            return;
        }
        RenderYearReview(views);
        RenderProfileStats(views);
        var episodes = views.Where(v => !v.Item.IsMovie).ToList();
        var movies = views.Where(v => v.Item.IsMovie).ToList();
        var total = views.Sum(v => v.Seconds);
        // Logged-without-a-date viewings count toward totals but not toward when-you-watch charts.
        var dated = views.Where(v => !v.DateUnknown).ToList();
        var exact = dated.Where(v => v.Precise).ToList();
        var streak = Streaks(exact.Select(v => v.At.ToLocalTime().DateTime));
        // Headline
        var headline = new Border
        {
            Background = new LinearGradientBrush(Brush("#2A241C").Color, Brush("#141416").Color, 20),
            CornerRadius = new(14),
            Padding = new(26, 22, 26, 22),
            Margin = new(0, 10, 0, 14),
        };
        var head = new StackPanel();
        head.Children.Add(Text("TIME WATCHED", 10, "#E4C084", FontWeights.SemiBold));
        var big = Text(Duration(total), 36, weight: FontWeights.Bold);
        big.Margin = new(0, 6, 0, 4);
        head.Children.Add(big);
        var monthStart = new DateTimeOffset(
            DateTime.Today.Year,
            DateTime.Today.Month,
            1,
            0,
            0,
            0,
            DateTimeOffset.Now.Offset
        );
        var thisMonth = dated.Where(v => v.At >= monthStart).Sum(v => v.Seconds);
        var lastMonth = dated.Where(v => v.At >= monthStart.AddMonths(-1) && v.At < monthStart).Sum(v => v.Seconds);
        var trend =
            lastMonth > 0
                ? $" · {(thisMonth >= lastMonth ? "▲" : "▼")} {Math.Abs(thisMonth - lastMonth) / lastMonth:P0} vs last month"
                : "";
        head.Children.Add(
            Text(
                $"{Duration(thisMonth, true)} this month{trend}  ·  {views.Count} viewings"
                    + (dated.Count > 0 ? $" since {dated[^1].At.ToLocalTime():d MMM yyyy}" : "")
                    + "",
                13,
                "#C7C2BC"
            )
        );
        var share = ActionButton("Share my stats", () => ShareStats(views, total, streak.Longest));
        share.Margin = new(0, 16, 0, 0);
        head.Children.Add(share);
        headline.Child = head;
        ContentPanel.Children.Add(headline);
        var cards = new WrapPanel();
        cards.Children.Add(StatCard("episodes watched", episodes.Count.ToString()));
        cards.Children.Add(StatCard("shows", episodes.Select(v => v.Item.GroupKey).Distinct().Count().ToString()));
        cards.Children.Add(StatCard("movies", movies.Count.ToString()));
        cards.Children.Add(
            StatCard(
                streak.Current > 0 ? "day streak · best " + streak.Longest : "best streak (days)",
                (streak.Current > 0 ? streak.Current : streak.Longest).ToString()
            )
        );
        var phone = views.Count(v => v.Source.Contains("Android"));
        if (phone > 0)
            cards.Children.Add(StatCard("watched on your phone", $"{phone * 100 / views.Count}%"));
        var binge = episodes
            .Where(v => v.Precise)
            .GroupBy(v => (v.Item.GroupKey, v.At.ToLocalTime().Date))
            .Select(g => (g.First().Item.Title, g.Key.Date, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .FirstOrDefault();
        if (binge.Count > 1)
            cards.Children.Add(StatCard($"biggest binge · {binge.Title}, {binge.Date:d MMM}", $"{binge.Count} eps"));
        ContentPanel.Children.Add(cards);

        // Last 12 weeks
        ContentPanel.Children.Add(Heading("Your last 12 weeks", "Hours watched each week."));
        var weekStart = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
        var weeks = Enumerable
            .Range(0, 12)
            .Select(i => weekStart.AddDays(-7 * (11 - i)))
            .Select(start =>
                (
                    Start: start,
                    Hours: dated
                        .Where(v =>
                            v.At.ToLocalTime().DateTime >= start && v.At.ToLocalTime().DateTime < start.AddDays(7)
                        )
                        .Sum(v => v.Seconds) / 3600
                )
            )
            .ToList();
        ContentPanel.Children.Add(
            BarChart(
                weeks.Select(w => (w.Start.ToString("d MMM"), w.Hours, w.Hours > 0 ? $"{w.Hours:0.#} h" : "")).ToList(),
                140,
                highlightLast: true
            )
        );

        // When you watch
        var rhythm = new Grid { Margin = new(0, 8, 0, 0) };
        rhythm.ColumnDefinitions.Add(new());
        rhythm.ColumnDefinitions.Add(new() { Width = new(28) });
        rhythm.ColumnDefinitions.Add(new());
        var days = Enumerable
            .Range(0, 7)
            .Select(d => (DayOfWeek)((d + 1) % 7))
            .Select(d => (Label: d.ToString()[..3], Value: (double)exact.Count(v => v.At.ToLocalTime().DayOfWeek == d)))
            .ToList();
        var dayPanel = new StackPanel();
        dayPanel.Children.Add(Text("Favourite days", 15, weight: FontWeights.SemiBold));
        dayPanel.Children.Add(
            BarChart(days.Select(d => (d.Label, d.Value, d.Value > 0 ? d.Value.ToString("0") : "")).ToList(), 96)
        );
        rhythm.Children.Add(dayPanel);
        string Part(int hour) =>
            hour is >= 5 and < 12 ? "Morning"
            : hour is >= 12 and < 17 ? "Afternoon"
            : hour is >= 17 and < 22 ? "Evening"
            : "Late night";
        var parts = new[] { "Morning", "Afternoon", "Evening", "Late night" }
            .Select(p => (Label: p, Count: exact.Count(v => Part(v.At.ToLocalTime().Hour) == p)))
            .ToList();
        var timePanel = new StackPanel();
        Grid.SetColumn(timePanel, 2);
        var favourite = parts.MaxBy(p => p.Count);
        timePanel.Children.Add(
            Text(
                favourite.Label switch
                {
                    "Late night" => "You're a night owl 🦉",
                    "Morning" => "An early-morning watcher",
                    "Afternoon" => "Afternoon is your time",
                    _ => "Evenings are for watching",
                },
                15,
                weight: FontWeights.SemiBold
            )
        );
        foreach (var p in parts)
            timePanel.Children.Add(
                MeterRow(
                    p.Label,
                    p.Count / (double)Math.Max(1, exact.Count),
                    $"{p.Count * 100 / Math.Max(1, exact.Count)}%"
                )
            );
        rhythm.Children.Add(timePanel);
        ContentPanel.Children.Add(Heading("When you watch"));
        ContentPanel.Children.Add(rhythm);

        // Top shows with completion
        var shows = episodes
            .GroupBy(v => v.Item.GroupKey)
            .Select(g => (Key: g.Key, Seconds: g.Sum(v => v.Seconds), Count: g.Count()))
            .OrderByDescending(s => s.Seconds)
            .Take(6)
            .ToList();
        if (shows.Count > 0)
        {
            ContentPanel.Children.Add(
                Heading("Your top shows", "By time watched, with how much of each show you've seen.")
            );
            var row = new WrapPanel();
            foreach (var s in shows)
            {
                var group = store.EpisodesFor(s.Key);
                if (group.Count == 0)
                    continue;
                var first = group[0];
                var released = Math.Max(
                    group.Count(i =>
                        i.Season > 0
                        && (
                            store
                                .State.EpisodeSchedules.GetValueOrDefault(s.Key)
                                ?.FirstOrDefault(e => e.Season == i.Season && e.Episode == i.Episode)
                                ?.AirsAt
                                is not { } at
                            || at <= DateTimeOffset.UtcNow
                        )
                    ),
                    1
                );
                var seen = group.Count(i => i.Season > 0 && store.EverWatched(i));
                var card = new StackPanel { Width = 170, Margin = new(0, 0, 18, 18) };
                var poster = ActionButton("", () => OpenHistoryTitle(group));
                poster.Content = Art(first.Poster, first.Title, 240, true);
                poster.Padding = new(0);
                poster.HorizontalAlignment = HorizontalAlignment.Stretch;
                card.Children.Add(
                    new Border
                    {
                        CornerRadius = new(10),
                        ClipToBounds = true,
                        Child = poster,
                    }
                );
                var name = Text(first.Title, 14, weight: FontWeights.SemiBold);
                name.Margin = new(0, 8, 0, 2);
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.TextWrapping = TextWrapping.NoWrap;
                card.Children.Add(name);
                card.Children.Add(
                    Text(
                        $"{Duration(s.Seconds, true)} · {s.Count} episode{(s.Count == 1 ? "" : "s")} watched",
                        11,
                        "#AAA6A0"
                    )
                );
                card.Children.Add(
                    new ProgressBar
                    {
                        Value = Math.Min(seen, released),
                        Maximum = released,
                        Height = 4,
                        Margin = new(0, 8, 0, 4),
                        Foreground = Brush("#E4C084"),
                        Background = Brush("#353238"),
                        BorderThickness = new(0),
                    }
                );
                var left = released - seen;
                card.Children.Add(
                    Text(
                        seen >= released
                            ? "Completed · caught up"
                            : $"{(seen > 0 && seen * 100 / released == 0 ? "<1" : (seen * 100 / released).ToString())}% · {left} episode{(left == 1 ? "" : "s")} to go",
                        11,
                        "#C7C2BC"
                    )
                );
                row.Children.Add(card);
            }
            ContentPanel.Children.Add(row);
        }

        // Genres
        var genres = views
            .SelectMany(v =>
                v.Item.Genres.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(g => (Genre: g, v.Seconds))
            )
            .GroupBy(g => g.Genre)
            .Select(g => (Genre: g.Key, Seconds: g.Sum(x => x.Seconds)))
            .OrderByDescending(g => g.Seconds)
            .Take(6)
            .ToList();
        if (genres.Count > 0)
        {
            ContentPanel.Children.Add(Heading("Your genres"));
            var max = genres[0].Seconds;
            foreach (var g in genres)
                ContentPanel.Children.Add(MeterRow(g.Genre, g.Seconds / max, Duration(g.Seconds, true)));
        }

        // Catch up list
        var behind = store
            .Archive.GroupBy(i => i.GroupKey)
            .Where(g => !g.First().IsMovie && store.TitleStatus(g) == "Watching")
            .Select(g => store.EpisodesFor(g.Key))
            .Select(group =>
                (
                    Group: group,
                    Left: group.Count(i =>
                        i.Season > 0
                        && !store.EverWatched(i)
                        && (
                            store
                                .State.EpisodeSchedules.GetValueOrDefault(group[0].GroupKey)
                                ?.FirstOrDefault(e => e.Season == i.Season && e.Episode == i.Episode)
                                ?.AirsAt
                                is not { } at
                            || at <= DateTimeOffset.UtcNow
                        )
                    )
                )
            )
            .Where(x => x.Left > 0)
            .OrderBy(x => x.Left)
            .Take(8)
            .ToList();
        if (behind.Count > 0)
        {
            ContentPanel.Children.Add(Heading("Catch up", "Shows you're watching, closest to caught up first."));
            foreach (var b in behind)
            {
                var avg = b
                    .Group.Select(i =>
                        store.GetProgress(i)?.Duration > 60 ? store.GetProgress(i)!.Duration : i.Duration
                    )
                    .Where(d => d > 60)
                    .DefaultIfEmpty(2580)
                    .Average();
                var line = ActionButton(
                    $"{b.Group[0].Title}  ·  {b.Left} episode{(b.Left == 1 ? "" : "s")} left  ·  about {Duration(avg * b.Left, true)}",
                    () => OpenHistoryTitle(b.Group)
                );
                line.HorizontalAlignment = HorizontalAlignment.Stretch;
                line.HorizontalContentAlignment = HorizontalAlignment.Left;
                line.Margin = new(0, 0, 0, 8);
                ContentPanel.Children.Add(line);
            }
        }

        // Milestones
        ContentPanel.Children.Add(Heading("Milestones"));
        var badges = new WrapPanel();
        void Ladder(string unit, int value, int[] steps)
        {
            foreach (var step in steps.Where(s => s <= value))
                badges.Children.Add(Milestone($"{step} {unit}", true, ""));
            if (steps.FirstOrDefault(s => s > value) is var next and > 0)
                badges.Children.Add(Milestone($"{next} {unit}", false, $"{next - value} to go"));
        }
        Ladder("episodes", episodes.Count, [10, 50, 100, 250, 500, 1000, 2500]);
        Ladder("movies", movies.Count, [5, 10, 25, 50, 100, 250]);
        Ladder("hours", (int)(total / 3600), [24, 100, 250, 500, 1000]);
        if (binge.Count >= 5)
            badges.Children.Add(Milestone("Binge watcher", true, $"{binge.Count} episodes in a day"));
        if (streak.Longest >= 7)
            badges.Children.Add(Milestone("Week-long streak", true, $"{streak.Longest} days in a row"));
        if (parts.First(p => p.Label == "Late night").Count * 3 >= views.Count && views.Count >= 6)
            badges.Children.Add(Milestone("Night owl", true, "A third of your viewing after 10 pm"));
        var finished = store
            .Archive.GroupBy(i => i.GroupKey)
            .Count(g => !g.First().IsMovie && store.CaughtUp(g.Key, DateTimeOffset.UtcNow));
        if (finished > 0)
            badges.Children.Add(Milestone($"{finished} show{(finished == 1 ? "" : "s")} caught up", true, ""));
        ContentPanel.Children.Add(badges);
    }

    FrameworkElement BarChart(
        List<(string Label, double Value, string Caption)> bars,
        double height,
        bool highlightLast = false
    )
    {
        var grid = new Grid { Height = height + 44, Margin = new(0, 4, 0, 8) };
        var max = Math.Max(bars.Max(b => b.Value), 0.0001);
        for (int i = 0; i < bars.Count; i++)
        {
            grid.ColumnDefinitions.Add(new());
            var b = bars[i];
            var column = new DockPanel { Margin = new(4, 0, 4, 0) };
            Grid.SetColumn(column, i);
            var label = Text(b.Label, 10, "#8F8A83");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.TextWrapping = TextWrapping.NoWrap;
            DockPanel.SetDock(label, Dock.Bottom);
            column.Children.Add(label);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            var caption = Text(b.Caption, 10, "#C7C2BC");
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.TextWrapping = TextWrapping.NoWrap;
            stack.Children.Add(caption);
            stack.Children.Add(
                new Border
                {
                    Height = Math.Max(b.Value > 0 ? 4 : 2, height * b.Value / max),
                    CornerRadius = new(5, 5, 2, 2),
                    Margin = new(0, 4, 0, 6),
                    MaxWidth = 46,
                    Background = Brush(
                        b.Value <= 0 ? "#2A2A2E"
                        : highlightLast && i == bars.Count - 1 ? "#E4C084"
                        : "#8C7550"
                    ),
                    ToolTip = b.Label + " · " + b.Caption,
                }
            );
            column.Children.Add(stack);
            grid.Children.Add(column);
        }
        return grid;
    }

    FrameworkElement MeterRow(string label, double fraction, string value)
    {
        var row = new Grid { Margin = new(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new() { Width = new(130) });
        row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new() { Width = new(80) });
        row.Children.Add(Text(label, 13, "#C7C2BC"));
        var track = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
        track.ColumnDefinitions.Add(new() { Width = new(Math.Max(fraction, 0.001), GridUnitType.Star) });
        track.ColumnDefinitions.Add(new() { Width = new(Math.Max(1 - fraction, 0.001), GridUnitType.Star) });
        track.Children.Add(new Border { Background = Brush("#E4C084"), CornerRadius = new(4) });
        var rest = new Border { Background = Brush("#2A2A2E"), CornerRadius = new(4) };
        Grid.SetColumn(rest, 1);
        track.Children.Add(rest);
        Grid.SetColumn(track, 1);
        row.Children.Add(track);
        var number = Text(value, 12, "#AAA6A0");
        number.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(number, 2);
        row.Children.Add(number);
        // Bars stop at 520 px (130 label + 510 bar + 80 value = 720 in all): the row sits in a capped first column.
        var capped = new Grid();
        capped.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), MaxWidth = 720 });
        capped.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        capped.Children.Add(row);
        return capped;
    }

    FrameworkElement Milestone(string title, bool unlocked, string detail)
    {
        var p = new StackPanel { Margin = new(16, 12, 16, 12) };
        p.Children.Add(
            Text((unlocked ? "★  " : "☆  ") + title, 14, unlocked ? "#E4C084" : "#8F8A83", FontWeights.SemiBold)
        );
        if (detail.Length > 0)
            p.Children.Add(Text(detail, 11, "#AAA6A0"));
        return new Border
        {
            Margin = new(0, 0, 12, 12),
            CornerRadius = new(12),
            Background = Brush(unlocked ? "#221E18" : "#151517"),
            BorderBrush = Brush(unlocked ? "#5E4E36" : "#29292D"),
            BorderThickness = new(1),
            Child = p,
            Opacity = unlocked ? 1 : .75,
        };
    }

    // Renders a poster-style summary card and saves it to Pictures\Noir (also copied to the clipboard).
    void ShareStats(List<Watched> views, double total, int longestStreak)
    {
        try
        {
            var card = new Grid
            {
                Width = 1080,
                Height = 1350,
                Background = new LinearGradientBrush(Brush("#2B241B").Color, Brush("#0B0B0D").Color, 70),
            };
            var panel = new StackPanel { Margin = new(90, 90, 90, 80) };
            card.Children.Add(panel);
            panel.Children.Add(Text("NOIR", 44, "#F4F1EB", FontWeights.Bold));
            panel.Children.Add(
                Text(
                    "MY WATCH STATS · " + DateTime.Today.ToString("MMMM yyyy").ToUpperInvariant(),
                    18,
                    "#E4C084",
                    FontWeights.SemiBold
                )
            );
            var time = Text(Duration(total), 76, weight: FontWeights.Bold);
            time.Margin = new(0, 70, 0, 6);
            panel.Children.Add(time);
            panel.Children.Add(Text("of movies and shows", 26, "#C7C2BC"));
            var numbers = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = 3,
                Margin = new(0, 60, 0, 60),
            };
            foreach (
                var (value, name) in new[]
                {
                    (views.Count(v => !v.Item.IsMovie).ToString(), "episodes"),
                    (views.Count(v => v.Item.IsMovie).ToString(), "movies"),
                    (longestStreak.ToString(), "day best streak"),
                }
            )
            {
                var n = new StackPanel();
                n.Children.Add(Text(value, 58, "#E4C084", FontWeights.SemiBold));
                n.Children.Add(Text(name, 22, "#AAA6A0"));
                numbers.Children.Add(n);
            }
            panel.Children.Add(numbers);
            panel.Children.Add(Text("MOST WATCHED", 18, "#E4C084", FontWeights.SemiBold));
            var posters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 20, 0, 0) };
            foreach (var g in views.GroupBy(v => v.Item.GroupKey).OrderByDescending(g => g.Sum(v => v.Seconds)).Take(4))
            {
                var item = g.First().Item;
                var poster = new StackPanel { Width = 204, Margin = new(0, 0, 24, 0) };
                poster.Children.Add(
                    new Border
                    {
                        CornerRadius = new(12),
                        ClipToBounds = true,
                        Child = Art(item.Poster, item.Title, 300, true),
                    }
                );
                var t = Text(item.Title, 20, weight: FontWeights.SemiBold);
                t.TextTrimming = TextTrimming.CharacterEllipsis;
                t.TextWrapping = TextWrapping.NoWrap;
                t.Margin = new(0, 10, 0, 0);
                poster.Children.Add(t);
                posters.Children.Add(poster);
            }
            panel.Children.Add(posters);
            card.Measure(new Size(1080, 1350));
            card.Arrange(new Rect(0, 0, 1080, 1350));
            card.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1080, 1350, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(card);
            bitmap.Freeze();
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Noir");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"Noir stats {DateTime.Now:yyyy-MM-dd HHmm}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(file))
                encoder.Save(stream);
            try
            {
                Clipboard.SetImage(bitmap);
            }
            catch { }
            Status.Text = "Saved to Pictures\\Noir and copied. Paste it anywhere to share.";
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{file}\"")
                {
                    UseShellExecute = true,
                }
            );
        }
        catch (Exception e)
        {
            store.Log(e);
            Status.Text = "Couldn't create the stats image.";
        }
    }
}
