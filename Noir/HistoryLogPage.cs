using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Noir;

// "Log what you watched": find any movie or show online and check off what you've seen, with the
// date you choose. Nothing is downloaded; the title joins your history the moment you log it.
public partial class MainWindow
{
    bool logging,
        logLoading;
    DiscoverTitle? logTitle;
    string logQuery = "",
        logFilter = "All",
        logWhen = "Just now";
    DateTime logDay = DateTime.Today;
    int logSeason;
    List<DiscoverTitle>? logResults;
    CancellationTokenSource? logCancel;
    DispatcherTimer? logDebounce;
    CancellationTokenSource? logSearchCancel;
    StackPanel? logSearchHost;
    int logSearchGeneration;

    void CancelLogSearch()
    {
        logSearchGeneration++;
        logDebounce?.Stop();
        logDebounce = null;
        logSearchCancel?.Cancel();
        logSearchCancel?.Dispose();
        logSearchCancel = null;
        logSearchHost = null;
    }

    void OpenLog(DiscoverTitle? title)
    {
        if (page != "History")
        {
            CancelDiscover();
            page = "History";
            detailKey = null;
            SearchBox.Text = "";
        }
        logging = true;
        logTitle = null;
        logSeason = 0;
        logLoading = false;
        logWhen = "Just now";
        logDay = DateTime.Today;
        Render();
        ContentScroll.ScrollToTop();
        if (title != null)
            _ = OpenLogTitle(title);
    }

    void CloseLog()
    {
        logCancel?.Cancel();
        logging = false;
        logTitle = null;
        logLoading = false;
        Render();
        ContentScroll.ScrollToTop();
    }

    void RenderLogWatched()
    {
        PageTitle.Text = "Log what you watched";
        var back = ActionButton(
            logTitle != null
                ? T("[[back]]  Back to search", "[[back]]  العودة إلى البحث")
                : T("[[back]]  Back to History", "[[back]]  العودة إلى السجل"),
            () =>
            {
                if (logTitle != null)
                {
                    logTitle = null;
                    Render();
                    ContentScroll.ScrollToTop();
                }
                else
                    CloseLog();
            }
        );
        back.Background = Brush("Transparent");
        back.Foreground = Brush("#AAA6A0");
        back.Padding = new(0, 4, 0, 4);
        ContentPanel.Children.Add(back);
        if (logLoading)
        {
            ContentPanel.Children.Add(Heading("Loading episodes…"));
            return;
        }
        if (logTitle != null)
        {
            RenderLogTitle(logTitle);
            return;
        }
        ContentPanel.Children.Add(
            Body(
                "Search for anything you've watched — on another service, at the cinema, years ago. Pick it, choose when, and check it off. Nothing downloads.",
                14
            )
        );
        var box = new TextBox
        {
            Style = (Style)FindResource("SearchPill"),
            Height = 52,
            FontSize = 17,
            Text = logQuery,
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 760,
            Margin = new(0, 8, 0, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(box, "Search movies and shows to log");
        var hint = new TextBlock
        {
            Text = "Movie or show title",
            FontSize = 17,
            Foreground = Brush("#7D7872"),
            Margin = new(44, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = logQuery.Length > 0 ? Visibility.Collapsed : Visibility.Visible,
        };
        var glass = Icon("M10,2 A8,8 0 1 1 9.99,2 M16,16 L22,22", 18, "#AAA6A0");
        glass.Margin = new(16, 0, 0, 0);
        glass.HorizontalAlignment = HorizontalAlignment.Left;
        glass.VerticalAlignment = VerticalAlignment.Center;
        glass.IsHitTestVisible = false;
        var field = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        field.Children.Add(box);
        field.Children.Add(hint);
        field.Children.Add(glass);
        ContentPanel.Children.Add(field);
        ContentPanel.Children.Add(Chips(["All", "Movies", "TV shows"], logFilter, v =>
            {
                logFilter = v;
                logResults = null;
                Render();
            }));
        var results = new StackPanel();
        ContentPanel.Children.Add(results);
        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.CaretIndex = box.Text.Length;
        };
        box.TextChanged += (_, _) =>
        {
            hint.Visibility = box.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            logQuery = box.Text;
            logResults = null;
            CancelLogSearch();
            logSearchHost = results;
            var generation = logSearchGeneration;
            var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
            logDebounce = debounce;
            debounce.Tick += (_, _) =>
            {
                debounce.Stop();
                if (generation == logSearchGeneration)
                    _ = LogSearch(results, generation);
            };
            debounce.Start();
        };
        logSearchHost = results;
        if (logResults != null)
            FillLogResults(results);
        else
            _ = LogSearch(results, logSearchGeneration);
    }

    async Task LogSearch(StackPanel host, int generation)
    {
        bool Current() =>
            generation == logSearchGeneration
            && ReferenceEquals(host, logSearchHost)
            && logging
            && page == "History"
            && logTitle == null
            && !closing;
        if (!Current())
            return;
        logSearchCancel?.Cancel();
        logSearchCancel?.Dispose();
        logSearchCancel = new();
        var token = logSearchCancel.Token;
        var query = logQuery.Trim();
        host.Children.Clear();
        host.Children.Add(Body(query.Length > 0 ? $"Searching for “{query}”…" : "Loading popular titles…", 13));
        try
        {
            using var client = DiscoverClient();
            var found = await client.Search(query, logFilter, token);
            if (token.IsCancellationRequested || !Current())
                return;
            logResults = found.Titles;
            FillLogResults(host);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (token.IsCancellationRequested || !Current())
                return;
            store.Log(e);
            host.Children.Clear();
            host.Children.Add(Heading("Search is unavailable", "Check your connection and try again."));
        }
    }

    void FillLogResults(StackPanel host)
    {
        host.Children.Clear();
        var titles = logResults ?? [];
        logSearchCancel ??= new();
        var token = logSearchCancel.Token;
        if (titles.Count == 0)
        {
            host.Children.Add(Heading("Nothing found", "Try the original title, or fewer words."));
            return;
        }
        host.Children.Add(
            Heading(
                logQuery.Trim().Length > 0 ? $"Results for “{logQuery.Trim()}”" : "Popular right now",
                "Choose a title to log it."
            )
        );
        var wrap = new WrapPanel();
        host.Children.Add(wrap);
        foreach (var title in titles.Take(40))
        {
            var body = new StackPanel { Width = 160, Margin = new(0, 0, 16, 20) };
            var picture = new Grid { Height = 240, ClipToBounds = true };
            picture.Children.Add(Art(title.Poster, title.Name, 240, true));
            var group = TrackedGroup(title);
            if (group != null)
            {
                var watched = title.IsMovie
                    ? (store.EverWatched(group[0]) ? 1 : 0)
                    : group.Count(i => i.Season > 0 && store.EverWatched(i));
                if (watched > 0)
                    picture.Children.Add(
                        new Border
                        {
                            IsHitTestVisible = false,
                            Background = Brush("#E4C084"),
                            CornerRadius = new(6),
                            Padding = new(7, 3, 7, 3),
                            Margin = new(8),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Bottom,
                            Child = Text(
                                title.IsMovie ? "✓ WATCHED" : $"✓ {watched} EPISODES",
                                10,
                                "#16120C",
                                FontWeights.Bold
                            ),
                        }
                    );
            }
            var button = new Button
            {
                Content = picture,
                Padding = new(0),
                Background = Brush("Transparent"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = "Log " + title.Name,
            };
            button.Click += (_, _) => _ = OpenLogTitle(title);
            System.Windows.Automation.AutomationProperties.SetName(button, "Log " + title.Name);
            var edge = new Border
            {
                CornerRadius = new(10),
                ClipToBounds = true,
                Child = button,
                BorderThickness = new(1.5),
                BorderBrush = Brush("#00000000"),
            };
            body.Children.Add(edge);
            var name = Text(title.Name, 14, weight: FontWeights.SemiBold);
            name.Margin = new(0, 9, 0, 2);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.TextWrapping = TextWrapping.NoWrap;
            body.Children.Add(name);
            body.Children.Add(
                Text(
                    (title.IsMovie ? "Movie" : "Series") + (title.Years.Length > 0 ? " · " + title.Years : ""),
                    11,
                    "#9F9A93"
                )
            );
            if (!File.Exists(title.Poster))
                _ = LoadPoster();
            async Task LoadPoster()
            {
                try
                {
                    using var client = DiscoverClient();
                    await client.Artwork(title, token);
                    if (token.IsCancellationRequested || !logging)
                        return;
                    var source = await LoadImage(title.Poster, 384);
                    if (token.IsCancellationRequested || !logging || closing)
                        return;
                    if (source != null)
                    {
                        picture.Children.RemoveAt(0);
                        var image = new Image { Source = source, Stretch = Stretch.UniformToFill };
                        picture.Children.Insert(0, image);
                        image.BeginAnimation(
                            OpacityProperty,
                            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                        );
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception e)
                {
                    store.Log(e);
                }
            }
            wrap.Children.Add(Lift(body, edge));
        }
    }

    async Task OpenLogTitle(DiscoverTitle title)
    {
        logCancel?.Cancel();
        logCancel = new();
        var token = logCancel.Token;
        logLoading = true;
        logging = true;
        Render();
        ContentScroll.ScrollToTop();
        try
        {
            using var client = DiscoverClient();
            var details = await client.Details(title, token);
            if (token.IsCancellationRequested || !logging)
                return;
            logTitle = details;
            logSeason = 0;
            // An old title nobody has logged yet: don't assume it was watched today; ask.
            var group = TrackedGroup(details);
            var aired = details.IsMovie
                ? (
                    details.Year > 0
                        ? new DateTimeOffset(details.Year, 1, 1, 0, 0, 0, TimeSpan.Zero)
                        : (DateTimeOffset?)null
                )
                : details.Episodes.Where(e => e.Season > 0 && e.AirsAt != null).Min(e => e.AirsAt);
            logWhen = "Just now";
            logDay = DateTime.Today;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e)
        {
            store.Log(e);
            Status.Text = "Couldn't load this title's episodes. Check your connection.";
        }
        finally
        {
            // A newer lookup owns the loading state; one cancelled by navigation must still clear it.
            if (logCancel?.Token == token)
                logLoading = false;
            if (!token.IsCancellationRequested)
            {
                Render();
                ContentScroll.ScrollToTop();
            }
        }
    }

    // The library group a Discover title already belongs to, if any.
    List<VideoItem>? TrackedGroup(DiscoverTitle title) =>
        store.FindDiscovered(title) is { } key ? store.EpisodesFor(key) : null;

    void RenderLogTitle(DiscoverTitle title)
    {
        var now = DateTimeOffset.UtcNow;
        var group = TrackedGroup(title);
        bool Seen(int season, int episode) =>
            group?.FirstOrDefault(i => title.IsMovie || i.Season == season && i.Episode == episode) is { } i
            && store.EverWatched(i);
        var hero = new Grid { Margin = new(0, 10, 0, 0) };
        hero.ColumnDefinitions.Add(new() { Width = new(220) });
        hero.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        hero.Children.Add(
            new Border
            {
                CornerRadius = new(12),
                ClipToBounds = true,
                Child = Art(title.Poster, title.Name, 330, true),
                VerticalAlignment = VerticalAlignment.Top,
            }
        );
        var info = new StackPanel { Margin = new(34, 4, 0, 0) };
        Grid.SetColumn(info, 1);
        hero.Children.Add(info);
        info.Children.Add(
            new Border
            {
                Background = Brush("#33E4C084"),
                BorderBrush = Brush("#66E4C084"),
                BorderThickness = new(1),
                CornerRadius = new(4),
                Padding = new(9, 3, 9, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = Text(title.IsMovie ? "MOVIE" : "SERIES", 11, "#F0D3A0", FontWeights.SemiBold),
            }
        );
        var name = Text(title.Name, title.Name.Length > 26 ? 36 : 46, weight: FontWeights.Bold);
        name.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        name.Margin = new(0, 12, 0, 6);
        info.Children.Add(name);
        info.Children.Add(
            Text(
                string.Join(
                    "   ·   ",
                    new[] { title.Years, title.Rating.Length > 0 ? "★ " + title.Rating : "" }
                        .Concat(title.Genres.Take(3))
                        .Where(x => x.Length > 0)
                ),
                14,
                "#C7C2BC"
            )
        );
        if (title.Summary.Length > 0)
        {
            var summary = Text(title.Summary, 14, "#AAA6A0");
            summary.MaxHeight = 62;
            summary.TextTrimming = TextTrimming.WordEllipsis;
            summary.Margin = new(0, 12, 0, 0);
            summary.MaxWidth = 720;
            summary.HorizontalAlignment = HorizontalAlignment.Left;
            info.Children.Add(summary);
        }
        var released = title.Episodes.Where(e => e.Season > 0 && (e.AirsAt == null || e.AirsAt <= now)).ToList();
        var seenCount = title.IsMovie ? 0 : released.Count(e => Seen(e.Season, e.Episode));
        string state;
        if (title.IsMovie)
        {
            var views = group == null ? [] : store.ViewingHistory(group[0]);
            state =
                views.Count == 0 ? "Not in your history yet"
                : views.Count == 1
                    ? $"Watched · {(views[0].DateUnknown ? "date unknown" : views[0].WatchedAt.ToLocalTime().ToString("d MMM yyyy"))}"
                : $"Watched {views.Count} times · last {views[0].WatchedAt.ToLocalTime():d MMM yyyy}";
        }
        else
            state =
                seenCount == 0 ? $"{released.Count} episodes released · none logged yet"
                : seenCount >= released.Count ? $"All {released.Count} released episodes watched"
                : $"{seenCount} of {released.Count} released episodes watched";
        var stateLine = Text(state, 15, "#E4C084", FontWeights.SemiBold);
        stateLine.Margin = new(0, 16, 0, 0);
        info.Children.Add(stateLine);
        // When you watched applies to every check below.
        info.Children.Add(Text("WHEN DID YOU WATCH?", 10.5, "#817E78", FontWeights.SemiBold));
        ((FrameworkElement)info.Children[^1]).Margin = new(0, 20, 0, 0);
        var whenRow = new WrapPanel { Margin = new(0, 8, 0, 0) };
        info.Children.Add(whenRow);
        string[] options = title.IsMovie
            ? ["Just now", "Pick a date", "Don't remember"]
            : ["Just now", "When it aired", "Pick a date", "Don't remember"];
        if (logWhen.Length > 0 && !options.Contains(logWhen))
            logWhen = "Just now";
        if (logWhen.Length == 0)
        {
            var hint = Text(
                (title.Years.Length > 0 ? "Came out " + title.Years.Split('–')[0] + " — " : "")
                    + "pick when you watched it, or Don't remember.",
                12,
                "#9F9A93"
            );
            hint.Margin = new(0, 6, 0, 0);
            info.Children.Add(hint);
        }
        foreach (var option in options)
        {
            var label =
                option == "Pick a date" && logWhen == option
                    ? logDay.ToString("d MMM yyyy") + "  [[chevron-down]]"
                    : option;
            var value = option;
            var pill = ActionButton(label, () => { });
            pill.Style = (Style)FindResource("Pill");
            pill.Padding = new(16, 8, 16, 8);
            pill.Margin = new(0, 0, 8, 8);
            var selected = value == logWhen;
            pill.Background = Brush(selected ? "#E4C084" : "#14FFFFFF");
            pill.Foreground = Brush(selected ? "#16120C" : "#D9D4CC");
            pill.BorderBrush = Brush(selected ? "#E4C084" : "#2EFFFFFF");
            pill.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            pill.Click += (_, _) =>
            {
                if (value == "Pick a date")
                    DateChooser(
                        pill,
                        logDay,
                        (day, unknown) =>
                        {
                            logWhen = unknown ? "Don't remember" : "Pick a date";
                            logDay = day;
                            RenderQuiet();
                        },
                        false
                    );
                else
                {
                    logWhen = value;
                    RenderQuiet();
                }
            };
            whenRow.Children.Add(pill);
        }
        var actions = new WrapPanel { Margin = new(0, 14, 0, 0) };
        info.Children.Add(actions);
        if (title.IsMovie)
        {
            var watched = group != null && store.EverWatched(group[0]);
            actions.Children.Add(
                BigButton(
                    watched ? "[[plus]]   Log a rewatch" : "[[check]]   I watched it",
                    () =>
                        LogTitleItems(
                            title,
                            [(0, 0)],
                            watched ? $"Rewatch of {title.Name} logged" : $"{title.Name} logged",
                            watched
                        ),
                    true
                )
            );
            if (watched)
                actions.Children.Add(
                    BigButton(
                        "Remove from history",
                        () =>
                        {
                            var g = TrackedGroup(title);
                            if (g != null)
                                UncheckAll([g[0]], $"{title.Name} removed from history");
                        },
                        false
                    )
                );
            if (group != null)
            {
                var stars = Stars(group[0], 22);
                stars.Margin = new(6, 0, 0, 0);
                actions.Children.Add(stars);
            }
            ContentPanel.Children.Add(hero);
            return;
        }
        var todo = released.Where(e => !Seen(e.Season, e.Episode)).ToList();
        if (released.Count > 0)
        {
            actions.Children.Add(
                BigButton(
                    T("Watched up to…", "شاهدت حتى…"),
                    () =>
                        UpToPicker(title.Name, released.Select(e => (e.Season, e.Episode)).ToList(), (s, e) =>
                            LogTitleItems(
                                title,
                                released
                                    .Where(x => x.Season < s || x.Season == s && x.Episode <= e)
                                    .Select(x => (x.Season, x.Episode))
                                    .ToList(),
                                T("Your history is up to date", "سجلّك محدث الآن")
                            )
                        ),
                    false
                )
            );
            actions.Children.Add(
                BigButton(T("Pick episodes", "اختر الحلقات"), () => ContentScroll.ScrollToVerticalOffset(480), false)
            );
        }
        if (todo.Count > 0)
            actions.Children.Add(
                BigButton(
                    $"[[check]]   Mark all {released.Count} watched",
                    () =>
                        LogTitleItems(
                            title,
                            todo.Select(e => (e.Season, e.Episode)).ToList(),
                            $"{title.Name}: {todo.Count} episodes logged"
                        ),
                    true
                )
            );
        else if (released.Count > 0)
            actions.Children.Add(
                BigButton(
                    "Unmark all",
                    () =>
                    {
                        var g = TrackedGroup(title);
                        if (g != null)
                            UncheckAll(g.Where(store.EverWatched).ToList(), $"{title.Name} removed from history");
                    },
                    false
                )
            );
        ContentPanel.Children.Add(hero);
        var seasons = title.Episodes.Select(e => e.Season).Where(s => s > 0).Distinct().Order().ToList();
        if (seasons.Count == 0)
        {
            ContentPanel.Children.Add(Body("No episode list is available for this show yet."));
            return;
        }
        if (!seasons.Contains(logSeason))
            logSeason = seasons.FirstOrDefault(
                s =>
                    title.Episodes.Any(e =>
                        e.Season == s && (e.AirsAt == null || e.AirsAt <= now) && !Seen(s, e.Episode)
                    ),
                seasons[0]
            );
        ContentPanel.Children.Add(
            UnderlineTabs(
                seasons
                    .Select(s =>
                    {
                        var done = released.Where(e => e.Season == s).ToList();
                        return "Season "
                            + s
                            + (done.Count > 0 && done.All(e => Seen(e.Season, e.Episode)) ? " [[check]]" : "");
                    })
                    .ToArray(),
                "Season "
                    + logSeason
                    + (
                        released.Where(e => e.Season == logSeason).ToList() is { Count: > 0 } r
                        && r.All(e => Seen(e.Season, e.Episode))
                            ? " [[check]]"
                            : ""
                    ),
                label =>
                {
                    logSeason = int.Parse(label[7..].Split(' ')[0]);
                    RenderQuiet();
                }
            )
        );
        var seasonEpisodes = title.Episodes.Where(e => e.Season == logSeason).OrderBy(e => e.Episode).ToList();
        var seasonReleased = seasonEpisodes.Where(e => e.AirsAt == null || e.AirsAt <= now).ToList();
        var seasonSeen = seasonReleased.Count(e => Seen(e.Season, e.Episode));
        var bar = new DockPanel { Margin = new(0, 12, 0, 12) };
        var seasonAction =
            seasonSeen < seasonReleased.Count
                ? ActionButton(
                    $"[[check]]  Mark season {logSeason} watched",
                    () =>
                        LogTitleItems(
                            title,
                            seasonReleased
                                .Where(e => !Seen(e.Season, e.Episode))
                                .Select(e => (e.Season, e.Episode))
                                .ToList(),
                            $"{title.Name} season {logSeason} logged"
                        ),
                    true
                )
                : ActionButton($"Unmark season {logSeason}", () =>
                {
                    var g = TrackedGroup(title);
                    if (g != null)
                        UncheckAll(
                            g.Where(i => i.Season == logSeason && store.EverWatched(i)).ToList(),
                            $"Season {logSeason} unmarked"
                        );
                });
        seasonAction.IsEnabled = seasonReleased.Count > 0;
        DockPanel.SetDock(seasonAction, Dock.Right);
        bar.Children.Add(seasonAction);
        bar.Children.Add(
            Text(
                $"{seasonSeen} of {seasonReleased.Count} watched"
                    + (
                        seasonEpisodes.Count > seasonReleased.Count
                            ? $" · {seasonEpisodes.Count - seasonReleased.Count} still to air"
                            : ""
                    ),
                14,
                "#AAA6A0"
            )
        );
        ((FrameworkElement)bar.Children[^1]).VerticalAlignment = VerticalAlignment.Center;
        ContentPanel.Children.Add(bar);
        var list = new StackPanel();
        ContentPanel.Children.Add(list);
        foreach (var e in seasonEpisodes)
        {
            var aired = e.AirsAt == null || e.AirsAt <= now;
            var seen = Seen(e.Season, e.Episode);
            var ep = e;
            var row = new Border
            {
                Background = Brush(seen ? "#1A1814" : "#141416"),
                CornerRadius = new(12),
                Padding = new(14, 10, 14, 10),
                Margin = new(0, 0, 0, 8),
                BorderBrush = Brush("#00000000"),
                BorderThickness = new(1),
                Opacity = aired ? 1 : .55,
            };
            var g = new Grid();
            row.Child = g;
            g.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new() { Width = new(64) });
            g.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            if (aired)
                g.Children.Add(
                    CheckCircle(
                        seen,
                        seen ? $"Unmark E{e.Episode}" : $"Mark E{e.Episode} watched",
                        () =>
                        {
                            if (seen)
                            {
                                var gr = TrackedGroup(title);
                                var item = gr?.FirstOrDefault(i => i.Season == ep.Season && i.Episode == ep.Episode);
                                if (item != null)
                                    SmartWatchedClick(item);
                            }
                            else
                                LogTitleEpisode(
                                    title,
                                    ep.Season,
                                    ep.Episode,
                                    $"{title.Name} S{ep.Season:00} E{ep.Episode:00} logged"
                                );
                        },
                        38
                    )
                );
            else
                g.Children.Add(new Border { Width = 38 });
            var number = Text(e.Episode.ToString("00"), 24, seen ? "#8C7A55" : "#4A463F", FontWeights.Bold);
            number.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            number.VerticalAlignment = VerticalAlignment.Center;
            number.Margin = new(16, 0, 0, 0);
            Grid.SetColumn(number, 1);
            g.Children.Add(number);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 12, 0) };
            Grid.SetColumn(text, 2);
            g.Children.Add(text);
            var epName = Text(
                e.Title.Length > 0 ? e.Title : "Episode " + e.Episode,
                15,
                seen ? "#E9E4DC" : "#F4F1EB",
                FontWeights.SemiBold
            );
            epName.TextTrimming = TextTrimming.CharacterEllipsis;
            epName.TextWrapping = TextWrapping.NoWrap;
            text.Children.Add(epName);
            var when = e.AirsAt is { } at
                ? (
                    aired
                        ? "Aired " + at.ToLocalTime().ToString("d MMM yyyy")
                        : "Airs " + at.ToLocalTime().ToString("ddd d MMM yyyy")
                )
                : "Air date unknown";
            if (
                seen
                && group?.FirstOrDefault(i => i.Season == e.Season && i.Episode == e.Episode) is { } logged
                && store.ViewingHistory(logged).FirstOrDefault() is { } v
            )
                when +=
                    "   ·   Watched "
                    + (v.DateUnknown ? "(date unknown)" : v.WatchedAt.ToLocalTime().ToString("d MMM yyyy"));
            text.Children.Add(Text(when, 12, "#8F8A83"));
            if (aired && !seen)
            {
                // TV Time's most-used shortcut when catching up a show watched elsewhere.
                var before = released
                    .Where(x =>
                        (x.Season < e.Season || x.Season == e.Season && x.Episode <= e.Episode)
                        && !Seen(x.Season, x.Episode)
                    )
                    .ToList();
                if (before.Count > 1)
                {
                    var upTo = ActionButton($"[[check]] Up to here ({before.Count})", () =>
                        LogTitleItems(
                            title,
                            before.Select(x => (x.Season, x.Episode)).ToList(),
                            $"{title.Name}: {before.Count} episodes logged up to S{ep.Season:00} E{ep.Episode:00}"
                        )
                    );
                    upTo.Background = Brush("Transparent");
                    upTo.Foreground = Brush("#E4C084");
                    upTo.FontSize = 12.5;
                    upTo.Padding = new(10, 6, 10, 6);
                    upTo.Opacity = 1;
                    upTo.ToolTip = "Mark every earlier unwatched episode, and this one";
                    Grid.SetColumn(upTo, 3);
                    g.Children.Add(upTo);
                }
            }
            row.MouseEnter += (_, _) => row.BorderBrush = Brush("#2EFFFFFF");
            row.MouseLeave += (_, _) => row.BorderBrush = Brush("#00000000");
            list.Children.Add(row);
        }
        ContentPanel.Children.Add(
            Body(
                TrackerOnly
                    ? T("A little log, a lasting memory.", "مشاهدة تسجّلها، وذكرى تبقى.")
                    : "Information: Cinemeta. Logging never downloads anything; use Discover or Get to download.",
                11
            )
        );
    }

    // One episode from the Log page: the chosen "when" applies to it, and earlier unwatched
    // episodes are offered like everywhere else.
    void LogTitleEpisode(DiscoverTitle title, int season, int episode, string message)
    {
        try
        {
            var group = store.AddDiscovered(title, false);
            var item = group.FirstOrDefault(i => i.Season == season && i.Episode == episode);
            if (item == null)
            {
                Status.Text = "That episode is missing from the schedule. Try again later.";
                return;
            }
            if (logWhen.Length == 0)
            {
                BulkCheckOff([item], message);
                return;
            }
            SmartCheckOff(
                item,
                message,
                DatedEntries([item], logWhen == "When it aired" ? "When they aired" : logWhen, logDay)[0]
            );
        }
        catch (Exception e)
        {
            store.Log(e);
            Status.Text = "Couldn't log this title. " + e.Message;
        }
    }

    // Resolves the chosen "when" per item, so "When it aired" gives each episode its own air date.
    void LogTitleItems(DiscoverTitle title, List<(int Season, int Episode)> keys, string message, bool rewatch = false)
    {
        try
        {
            var group = store.AddDiscovered(title, false);
            var now = DateTimeOffset.UtcNow;
            var items = title.IsMovie
                ? [group[0]]
                : keys.Select(k => group.FirstOrDefault(i => i.Season == k.Season && i.Episode == k.Episode))
                    .OfType<VideoItem>()
                    .ToList();
            if (items.Count == 0)
            {
                Status.Text = "Those episodes are missing from the schedule. Try again later.";
                return;
            }
            // No "when" chosen yet: ask, rather than defaulting to today.
            if (logWhen.Length == 0 && !rewatch)
            {
                BulkCheckOff(items, message);
                return;
            }
            var before = store.Snapshot(items);
            var rewards = RewardState();
            var focus = items.OrderBy(i => i.Season).ThenBy(i => i.Episode).Last();
            var seasonWasDone =
                focus.IsMovie || ShowEpisodes(focus).Where(i => i.Season == focus.Season).All(store.EverWatched);
            LogEntry Entry(VideoItem item)
            {
                var aired = title.IsMovie
                    ? null
                    : title.Episodes.FirstOrDefault(e => e.Season == item.Season && e.Episode == item.Episode)?.AirsAt;
                return logWhen switch
                {
                    "When it aired" when aired is { } a && a <= now => new(
                        item,
                        a.ToLocalTime().Date.AddHours(21) < now.LocalDateTime
                            ? new DateTimeOffset(a.ToLocalTime().Date.AddHours(21))
                            : a,
                        Approximate: true
                    ),
                    "When it aired" or "Don't remember" => new(
                        item,
                        aired is { } known && known <= now ? known : now,
                        true
                    ),
                    "Pick a date" => new(
                        item,
                        new DateTimeOffset(
                            logDay.Date.AddHours(logDay.Date == DateTime.Today ? Math.Min(DateTime.Now.Hour, 20) : 20)
                        )
                    ),
                    _ => new(item, now),
                };
            }
            var entries = items.Select(Entry).ToList();
            // A rewatch is a new viewing even on a day that already has one.
            if (rewatch)
                entries = entries.Select(e => e.DateUnknown ? e with { DateUnknown = false, At = now } : e).ToList();
            var added = rewatch ? store.LogRewatch(entries[0], "Logged") : store.LogWatched(entries, "Logged");
            RenderQuiet();
            if (added > 0)
            {
                AfterCheck(focus, message, () =>
                {
                    store.Restore(before);
                    RenderQuiet();
                });
                CelebrateHistory(
                    rewards,
                    !seasonWasDone && ShowEpisodes(focus).Where(i => i.Season == focus.Season).All(store.EverWatched)
                        ? focus.Season
                        : 0
                );
            }
            else
                Status.Text = "Already in your history for that date.";
        }
        catch (Exception e)
        {
            store.Log(e);
            Status.Text = "Couldn't log this title. " + e.Message;
        }
    }
}
