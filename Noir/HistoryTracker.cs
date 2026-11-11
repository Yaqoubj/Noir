using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace Noir;

// History as a tracker in the spirit of TV Time: it records what you watch anywhere, not only what
// plays in Noir. "Up next" puts one-click check-offs first, Shows and Movies are the tracked
// collection, Diary is the dated record, and Log / Import fill in everything watched elsewhere.
public partial class MainWindow
{
    string trackerTab = "Up next",
        showsFilter = "All",
        showsSort = "Recent",
        moviesFilter = "All",
        moviesSort = "Recent";
    int trackerLimit = 60;
    static readonly string[] TrackerTabs = ["Up next", "Shows", "Movies", "Diary", "Stats"];
    bool quietRender;
    string historyContinued = "";
    HistorySettings? historySettings;
    string historySettingsDirectory = "";
    HistorySettings HistoryPrefs
    {
        get
        {
            if (historySettings == null || historySettingsDirectory != store.DataDirectory)
            {
                historySettingsDirectory = store.DataDirectory;
                historySettings = HistorySettings.Load(store.DataDirectory);
            }
            return historySettings;
        }
    }

    static string T(string english, string arabic, params object[] values) =>
        string.Format(Loc.Culture, Loc.Arabic ? arabic : T(english), values);

    // A1-5: the one left-to-right episode label ("S01 · E02") for Up next, Shows cards and the diary; inside an Arabic line it keeps its own order.
    static string EpisodeTag(VideoItem item) => "\u202A" + item.EpisodeLabel + "\u202C";

    static System.Windows.Documents.Run EpisodeRun(VideoItem item, string color, FontWeight? weight = null)
    {
        var run = new System.Windows.Documents.Run(EpisodeTag(item))
        {
            Foreground = Brush(color),
            FlowDirection = FlowDirection.LeftToRight,
        };
        if (weight is { } w)
            run.FontWeight = w;
        return run;
    }

    static string MinutesText(int minutes) =>
        minutes >= 60
            ? (Loc.Arabic ? $"{minutes / 60} س {minutes % 60:00}" : $"{minutes / 60} h {minutes % 60:00}")
            : (Loc.Arabic ? $"{minutes} د" : $"{minutes} min");

    static string EpisodesLeft(int left) =>
        left == 1
            ? T("Last episode out", "آخر حلقة صدرت")
            : Loc.Count(
                left,
                "episode left",
                "episodes left",
                "حلقة واحدة متبقية",
                "حلقتان متبقيتان",
                "حلقات متبقية",
                "حلقة متبقية"
            );

    HistoryRewardState RewardState() =>
        HistoryRewards.Summary(
            store.Archive.SelectMany(i =>
                store
                    .ViewingHistory(i)
                    .Select(v => new RewardViewing(
                        v.WatchedAt.ToLocalTime().Date,
                        i.IsMovie,
                        v.DateUnknown,
                        v.Approximate
                    ))
            ),
            DateTime.Today
        );

    void HistoryPop(FrameworkElement element)
    {
        if (!SystemParameters.ClientAreaAnimation)
            return;
        var transform = new ScaleTransform(1, 1);
        element.RenderTransform = transform;
        element.RenderTransformOrigin = new(.5, .5);
        var motion = new DoubleAnimation(.96, 1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, motion);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, motion);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(.3, 1, TimeSpan.FromMilliseconds(240)));
        element.Unloaded += (_, _) =>
        {
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            element.BeginAnimation(OpacityProperty, null);
        };
    }

    void CelebrateHistory(HistoryRewardState before, int season = 0)
    {
        var earned = HistoryRewards.Earned(before, RewardState(), GoalPaused ? 0 : HistoryPrefs.WeeklyGoal, season);
        if (earned.Count == 0)
            return;
        var message = string.Join(
            "  ·  ",
            earned
                .GroupBy(r => r.Kind)
                .Select(g => g.Last())
                .Take(4)
                .Select(r =>
                    r.Kind switch
                    {
                        "Week" => T("Weekly goal reached", "حققت هدف الأسبوع"),
                        "Episode" => T("{0} episodes!", "{0} حلقة!", r.Value),
                        "Movie" => T("{0} movies!", "{0} فيلم!", r.Value),
                        "Season" => T("Season {0} complete", "أكملت الموسم {0}", r.Value),
                        _ => T("Day {0} streak", "{0} أيام متتالية", r.Value),
                    }
                )
        );
        if (undoBar?.Child is Panel row)
        {
            var badge = new Border
            {
                Background = Brush("#33E4C084"),
                CornerRadius = new(10),
                Padding = new(10, 6, 10, 6),
                Margin = new(10, 0, 4, 0),
                MaxWidth = 360,
                Child = Text("★  " + message, 12, "#E4C084", FontWeights.SemiBold),
            };
            row.Children.Add(badge);
            HistoryPop(badge);
        }
    }

    void HistoryEmptyActions()
    {
        var row = new WrapPanel { Margin = new(0, 12, 0, 8) };
        row.Children.Add(BigButton(T("[[plus]]  Log", "[[plus]]  سجّل"), () => OpenLog(null), true));
        row.Children.Add(
            BigButton(
                T("Import", "استيراد"),
                () =>
                {
                    trackerTab = "Import";
                    Render();
                },
                false
            )
        );
        ContentPanel.Children.Add(row);
    }

    void RenderViewingLife(List<Watched> views)
    {
        var reward = HistoryRewards.Summary(
            views.Select(v => new RewardViewing(v.At.ToLocalTime().Date, v.Item.IsMovie, v.DateUnknown, v.Approximate)),
            DateTime.Today
        );
        var card = new Border
        {
            Tag = "HistoryStatsStrip",
            CornerRadius = new(12),
            Background = Brush("#141416"),
            Padding = new(18, 12, 18, 12),
            Margin = new(0, 16, 0, 12),
        };
        var grid = new DockPanel();
        card.Child = grid;
        var paused = GoalPaused;
        var target = paused ? 0 : HistoryPrefs.WeeklyGoal;
        var goal = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(goal, Dock.Right);
        grid.Children.Add(goal);
        var goalButton = ActionButton("", () => { });
        goalButton.Content = Ring(
            target > 0 ? reward.Week / (double)target : 0,
            38,
            target > 0 ? $"{reward.Week}/{target}" : "–"
        );
        goalButton.Padding = new(0);
        goalButton.Background = Brush("Transparent");
        goalButton.ToolTip = T("Edit weekly goal", "تعديل هدف الأسبوع");
        System.Windows.Automation.AutomationProperties.SetName(goalButton, goalButton.ToolTip.ToString());
        goal.Children.Add(goalButton);
        var label = Text(
            paused ? T("Goal paused", "الهدف متوقف مؤقتاً") : T("Weekly goal", "هدف الأسبوع"),
            11,
            "#E4C084"
        );
        label.Margin = new(8, 0, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        goal.Children.Add(label);
        var summary = Text(
            T(
                "{0:N0} viewings  ·  {1:0.#} hours  ·  {2} day streak",
                "{0:N0} مشاهدة  ·  {1:0.#} ساعة  ·  {2} أيام متتالية",
                views.Count,
                views.Sum(v => v.Seconds) / 3600,
                reward.Streak
            ),
            13,
            "#C7C2BC"
        );
        summary.VerticalAlignment = VerticalAlignment.Center;
        summary.Margin = new(0, 0, 12, 0);
        grid.Children.Add(summary);
        goalButton.Click += (_, _) =>
        {
            void SaveGoal(int value)
            {
                HistoryPrefs.WeeklyGoal = value;
                try
                {
                    HistoryPrefs.Save(store.DataDirectory);
                }
                catch (System.IO.IOException e)
                {
                    store.Log(e);
                }
                RenderQuiet();
            }
            var menu = new ContextMenu();
            var pause = new MenuItem
            {
                Header = paused ? T("Resume goal", "استئناف الهدف") : T("Pause goal", "إيقاف الهدف مؤقتاً"),
            };
            pause.Click += (_, _) => PauseGoal(!paused);
            menu.Items.Add(pause);
            menu.Items.Add(new Separator());
            foreach (var count in new[] { 0, 1, 3, 5, 7, 10, 14 })
            {
                var value = count;
                var option = new MenuItem
                {
                    Header = count == 0 ? T("Turn goal off", "إيقاف الهدف") : T("{0} a week", "{0} في الأسبوع", count),
                    IsCheckable = true,
                    IsChecked = count == target,
                };
                option.Click += (_, _) => SaveGoal(value);
                menu.Items.Add(option);
            }
            menu.Items.Add(new Separator());
            var custom = new StackPanel { Orientation = Orientation.Horizontal };
            custom.Children.Add(Text(T("Custom goal", "هدف مخصص"), 12, "#C7C2BC"));
            var number = new TextBox
            {
                Text = Math.Max(1, target).ToString(),
                Width = 48,
                Margin = new(10, 0, 8, 0),
                Padding = new(5, 3, 5, 3),
                Background = Brush("#252529"),
                Foreground = Brush("#F4F1EB"),
            };
            System.Windows.Automation.AutomationProperties.SetName(
                number,
                T("Titles or episodes per week", "عدد الأفلام أو الحلقات أسبوعياً")
            );
            custom.Children.Add(number);
            var save = ActionButton(
                T("Save", "حفظ"),
                () =>
                {
                    if (int.TryParse(number.Text, out var value) && value is >= 1 and <= 100)
                    {
                        menu.IsOpen = false;
                        SaveGoal(value);
                    }
                },
                true
            );
            save.Padding = new(10, 4, 10, 4);
            custom.Children.Add(save);
            number.TextChanged += (_, _) =>
                save.IsEnabled = int.TryParse(number.Text, out var n) && n is >= 1 and <= 100;
            menu.Items.Add(new MenuItem { Header = custom, StaysOpenOnClick = true });
            menu.PlacementTarget = goalButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        grid.SizeChanged += (_, e) =>
            label.Visibility = e.NewSize.Width < 640 ? Visibility.Collapsed : Visibility.Visible;
        ContentPanel.Children.Add(card);
    }

    // Re-renders after a check-off without the page entrance motion, so the list doesn't jump.
    void RenderQuiet()
    {
        quietRender = true;
        try
        {
            Render();
        }
        finally
        {
            quietRender = false;
        }
    }

    void RenderTrackerHistory()
    {
        ApplyAppMode();
        SearchPlaceholder.Text = T("Search your history", "ابحث في سجلك");
        SearchBox.ToolTip = SearchPlaceholder.Text;
        PageEyebrow.Text = "";
        if (logging)
        {
            RenderLogWatched();
            return;
        }
        var subPage = trackerTab is "Upcoming" or "Import";
        PageTitle.Text = subPage ? T(trackerTab) : T("History", "السجل");
        var views = AllViewings();
        LibraryHeader.Visibility = Visibility.Collapsed;
        if (subPage)
        {
            var back = ActionButton(T("[[back]]  Back to History", "[[back]]  العودة إلى السجل"), () =>
            {
                trackerTab = TrackerOnly ? "Shows" : "Up next";
                trackerLimit = 60;
                Render();
                ContentScroll.ScrollToTop();
            });
            back.Background = Brush("Transparent");
            back.Foreground = Brush("#AAA6A0");
            back.Padding = new(0, 4, 0, 4);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Margin = new(0, TopBar.Height + Density.Pick(22, 30), 0, 0);
            ContentPanel.Children.Add(back);
            var subHeading = Text(PageTitle.Text, Density.Pick(24, 28), "#F4F1EB", FontWeights.SemiBold);
            subHeading.Margin = new(0, 6, 0, 0);
            ContentPanel.Children.Add(subHeading);
            if (trackerTab == "Upcoming")
                RenderUpcoming();
            else
                RenderAddHistory();
            return;
        }
        var header = new DockPanel
        {
            Margin = new(0, TopBar.Height + Density.Pick(22, 30), 0, 0),
            LastChildFill = true,
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        var log = ActionButton(T("[[plus]]  Log", "[[plus]]  سجّل"), () => OpenLog(null), true);
        log.Padding = Density.Compact ? Density.ButtonPadding : new(20, 11, 22, 11);
        log.ToolTip = T("Log movies and episodes watched anywhere", "سجّل الأفلام والحلقات التي شاهدتها في أي مكان");
        System.Windows.Automation.AutomationProperties.SetName(log, log.Content.ToString());
        actions.Children.Add(log);
        var import = ActionButton(T("[[swap]]  Import", "[[swap]]  استيراد"), () =>
        {
            trackerTab = "Import";
            Render();
        });
        import.Padding = Density.Compact ? Density.ButtonPadding : new(18, 11, 20, 11);
        import.Margin = new(10, 0, 0, 0);
        import.ToolTip = T("Import history from other apps", "استورد سجلّك من التطبيقات الأخرى");
        System.Windows.Automation.AutomationProperties.SetName(import, import.Content.ToString());
        actions.Children.Add(import);
        var extras = ActionButton("[[more]]", () => { });
        extras.Padding = new(14, 6, 14, 12);
        extras.FontSize = 17;
        extras.Margin = new(10, 0, 0, 0);
        extras.ToolTip = T(
            "More: year story, compare taste, calendar, backup",
            "المزيد: حكاية العام ومقارنة الذوق والتقويم والنسخ الاحتياطي"
        );
        System.Windows.Automation.AutomationProperties.SetName(
            extras,
            T("More history tools", "المزيد من أدوات السجل")
        );
        extras.Click += (_, _) => ShowHistoryMore(extras);
        actions.Children.Add(extras);
        var caption = new StackPanel();
        var heading = Text(PageTitle.Text, Density.Pick(24, 28), "#F4F1EB", FontWeights.SemiBold);
        heading.TextWrapping = TextWrapping.NoWrap;
        heading.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.Children.Add(heading);
        header.Children.Add(caption);
        header.SizeChanged += (_, e) => DockPanel.SetDock(actions, e.NewSize.Width < 640 ? Dock.Bottom : Dock.Right);
        ContentPanel.Children.Add(header);
        if (SyncNoticeLine() is { } syncNotice)
            ContentPanel.Children.Add(syncNotice);
        ContentPanel.Children.Add(HistoryPillTabs());
        switch (trackerTab)
        {
            case "Upcoming":
                RenderUpcoming();
                break;
            case "Shows":
                RenderTrackedShows();
                break;
            case "Movies":
                RenderTrackedMovies();
                break;
            case "Diary":
                RenderTrackerDiary();
                break;
            case "Stats":
                RenderHistoryStats();
                break;
            case "Import":
                RenderAddHistory();
                break;
            default:
                RenderUpNextTracker();
                ContentPanel.Children.Add(HistoryProfileHeader());
                break;
        }
        RenderViewingLife(views);
    }

    bool TrackerFits(VideoItem item)
    {
        var q = SearchBox.Text.Trim();
        return q.Length == 0
            || item.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || item.EpisodeTitle.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    // The episode to check off next: the first released, unwatched one after the furthest you've
    // watched; if you're past the end but skipped some, the earliest gap.
    VideoItem? NextToTrack(List<VideoItem> group, DateTimeOffset now)
    {
        var regular = group
            .Where(i => store.Counts(i) && store.Aired(i, now))
            .OrderBy(i => i.Season)
            .ThenBy(i => i.Episode)
            .ToList();
        var furthest = regular.LastOrDefault(store.EverWatched);
        var after = furthest == null ? regular : regular.SkipWhile(i => i != furthest).Skip(1).ToList();
        return after.FirstOrDefault(i => !store.EverWatched(i)) ?? regular.FirstOrDefault(i => !store.EverWatched(i));
    }

    sealed record Tracked(
        List<VideoItem> Group,
        VideoItem? Next,
        int Watched,
        int Released,
        DateTimeOffset Last,
        string Status
    );

    List<Tracked> TrackedShows()
    {
        var now = DateTimeOffset.UtcNow;
        return store
            .Archive.Where(i => !i.IsMovie)
            .Select(i => i.GroupKey)
            .Distinct()
            .Select(store.EpisodesFor)
            .Where(g => g.Count > 0)
            .Select(g =>
            {
                var released = g.Where(i => store.Counts(i) && store.Aired(i, now)).ToList();
                var watched = released.Count(store.EverWatched);
                var last = g.Select(HistoryDate).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
                var next = NextToTrack(g, now);
                var chosen = store.State.TitleStatuses.GetValueOrDefault(g[0].GroupKey, "");
                var status =
                    chosen == "Dropped" ? "Dropped"
                    : watched == 0 && !g.Any(i => store.GetProgress(i) is { Position: > 0 }) ? "Not started"
                    : next == null
                        ? (
                            store.State.EpisodeSchedules.GetValueOrDefault(g[0].GroupKey)?.Any(e => e.AirsAt > now)
                            == true
                                ? "Caught up"
                            : store.ShowEnded(g[0].GroupKey) && watched >= released.Count || chosen == "Finished"
                                ? "Finished"
                            : "Caught up"
                        )
                    : "Watching";
                // Only logged history and episodes left: you stopped somewhere in the past.
                if (status == "Watching" && last == DateTimeOffset.MinValue && chosen != "Watching")
                    status = "Unfinished";
                return new Tracked(g, next, watched, released.Count, last, status);
            })
            .ToList();
    }

    void RenderUpNextTracker()
    {
        var shows = TrackedShows().Where(t => t.Group.Any(TrackerFits)).ToList();
        var now = DateTimeOffset.UtcNow;
        var going = shows.Where(t => t.Status == "Watching" && t.Next != null).OrderByDescending(t => t.Last).ToList();
        // A new season that has started for a show you'd finished jumps to the top.
        var premieres = going
            .Where(t =>
                t.Next!.Episode == 1
                && t.Next.Season > 1
                && store.AirDate(t.Next) is { } a
                && a > now.AddDays(-60)
                && t.Group.Where(i => store.Counts(i) && i.Season < t.Next.Season).All(store.EverWatched)
            )
            .ToList();
        bool Snoozed(Tracked t) => store.State.TrackerSnoozes.GetValueOrDefault(t.Group[0].GroupKey) > now;
        var active = going.Except(premieres).Where(t => t.Last > now.AddDays(-60) || Snoozed(t)).ToList();
        // Shows that only have logged history aren't something you're watching: they live in Shows, not here.
        var dormant = going.Except(premieres).Except(active).ToList();
        var historyOnly = shows.Count(t => t.Status == "Unfinished");
        var fresh = shows
            .Where(t =>
                t.Status == "Not started"
                && t.Next != null
                && (
                    store.State.Watchlist.Contains(t.Group[0].GroupKey)
                    || store.State.TitleStatuses.ContainsKey(t.Group[0].GroupKey)
                    || t.Group.Any(CanPlay)
                )
            )
            .OrderByDescending(t => t.Group.Max(i => i.AddedAt) ?? DateTimeOffset.MinValue)
            .ToList();
        if (shows.Count == 0 && RecentHistoryViewings().Count == 0)
        {
            var empty = new Border
            {
                Background = new LinearGradientBrush(Brush("#2A241C").Color, Brush("#141416").Color, 20),
                CornerRadius = new(16),
                Padding = new(30, 28, 30, 28),
                Margin = new(0, 18, 0, 10),
            };
            var stack = new StackPanel();
            empty.Child = stack;
            stack.Children.Add(
                Text(T("Track everything you watch", "تتبّع كل ما تشاهده"), 26, weight: FontWeights.Bold)
            );
            stack.Children.Add(
                Body(
                    T(
                        "Log shows and movies watched anywhere, or import your history. Your next episode appears here.",
                        "سجّل الأعمال التي شاهدتها في أي مكان أو استورد سجلّك. ستظهر حلقتك التالية هنا."
                    ),
                    14
                )
            );
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 8, 0, 0) };
            row.Children.Add(
                BigButton(T("[[plus]]   Log what you watched", "[[plus]]   سجّل ما شاهدته"), () => OpenLog(null), true)
            );
            row.Children.Add(
                BigButton(
                    T("[[swap]]   Import", "[[swap]]   استيراد"),
                    () =>
                    {
                        trackerTab = "Import";
                        Render();
                    },
                    false
                )
            );
            stack.Children.Add(row);
            ContentPanel.Children.Add(empty);
        }
        var watchNext = premieres.Count + active.Count;
        if (watchNext > 0)
        {
            ContentPanel.Children.Add(
                Body(
                    T(
                        "{0} shows in progress · check off an episode when you finish it.",
                        "{0} أعمال تتابعها · سجّل الحلقة عند الانتهاء منها.",
                        watchNext
                    ),
                    13
                )
            );
            var watchGrid = TrackerGrid(
                premieres
                    .Select(t => TrackerRow(t, false, T("SEASON {0} IS OUT", "الموسم {0} متاح", t.Next!.Season)))
                    .Concat(active.Take(trackerLimit).Select(t => TrackerRow(t, false)))
            );
            watchGrid.Tag = "HistoryWatchNext";
            ContentPanel.Children.Add(watchGrid);
        }
        RenderRecentlyWatched();
        var soon = shows
            .Where(t => t.Status == "Caught up")
            .Select(t =>
                (
                    t.Group,
                    Next: store
                        .State.EpisodeSchedules.GetValueOrDefault(t.Group[0].GroupKey)
                        ?.Where(e => e.Season > 0 && e.AirsAt > now)
                        .OrderBy(e => e.AirsAt)
                        .FirstOrDefault()
                )
            )
            .Where(x => x.Next != null)
            .OrderBy(x => x.Next!.AirsAt)
            .Take(12)
            .ToList();
        if (soon.Count > 0)
            ContentPanel.Children.Add(
                Rail(
                    T("Caught up · airing soon", "أكملت المتاح · قريباً"),
                    T(
                        "New episodes of shows you've finished so far.",
                        "حلقات جديدة من الأعمال التي أكملت حلقاتها المتاحة."
                    ),
                    soon.Select(x => CalendarCard(x.Group, x.Next!)).ToList()
                )
            );
        if (fresh.Count > 0)
            ContentPanel.Children.Add(
                Rail(
                    T("Not started yet", "لم تبدأ بعد"),
                    T("A new story is waiting.", "حكاية جديدة بانتظارك."),
                    fresh.Take(20).Select(t => TrackerPoster(t)).ToList()
                )
            );
        if (historyOnly > 0 && SearchBox.Text.Trim().Length == 0)
        {
            var note = ActionButton(
                T(
                    "{0} unfinished shows from your history are in Shows  [[chevron]]",
                    "{0} أعمال غير مكتملة من سجلك في قسم المسلسلات  [[chevron-left]]",
                    historyOnly
                ),
                () =>
                {
                    trackerTab = "Shows";
                    showsFilter = "Unfinished";
                    Render();
                }
            );
            note.Background = Brush("Transparent");
            note.Foreground = Brush("#9F9A93");
            note.FontSize = 12.5;
            note.Padding = new(0, 14, 0, 0);
            ContentPanel.Children.Add(note);
        }
        if (dormant.Count > 0)
        {
            ContentPanel.Children.Add(
                Heading(
                    T("Still watching these?", "هل ما زلت تتابع هذه الأعمال؟"),
                    T(
                        "Nothing checked off for two months. Keep watching, or stop showing.",
                        "لم تسجّل مشاهدة منذ شهرين. واصل المتابعة أو أوقف إظهارها."
                    )
                )
            );
            ContentPanel.Children.Add(TrackerGrid(dormant.Take(trackerLimit).Select(t => TrackerRow(t, true))));
        }
        RenderHistoryFavourites();
    }

    // Rows flow into as many columns as fit, so wide windows don't waste half the screen.
    FrameworkElement TrackerGrid(IEnumerable<FrameworkElement> rows)
    {
        var grid = new UniformGrid { Columns = 2, Margin = new(-6, 0, -6, 8) };
        foreach (var r in rows)
        {
            r.Margin = new(6, 0, 6, 12);
            grid.Children.Add(r);
        }
        grid.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
                grid.Columns = Math.Max(1, Math.Min(3, (int)(e.NewSize.Width / Density.Pick(480, 540))));
        };
        return grid;
    }

    FrameworkElement TrackerRow(Tracked t, bool dormant, string badge = "")
    {
        var show = t.Group[0];
        var next = t.Next!;
        var left = t.Released - t.Watched;
        var card = new Border
        {
            Tag = "HistoryTrackerRow:" + show.GroupKey,
            Background = Brush("#161618"),
            CornerRadius = new(14),
            BorderBrush = Brush("#00000000"),
            BorderThickness = new(1.5),
            Padding = new(10, 8, 10, 8),
            Opacity = dormant ? .82 : 1,
        };
        var g = new Grid();
        g.ColumnDefinitions.Add(new() { Width = new(Density.Pick(60, 68)) });
        g.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        card.Child = g;
        var poster = new Button
        {
            Content = Art(show.Poster, show.Title, Density.Pick(90, 102), true),
            Padding = new(0),
            Height = Density.Pick(90, 102),
            Background = Brush("Transparent"),
            ToolTip = T("Open {0}", "افتح {0}", show.Title),
        };
        poster.Click += (_, _) => OpenHistoryTitle(t.Group);
        System.Windows.Automation.AutomationProperties.SetName(poster, poster.ToolTip.ToString());
        g.Children.Add(
            new Border
            {
                CornerRadius = new(8),
                ClipToBounds = true,
                Child = poster,
                VerticalAlignment = VerticalAlignment.Center,
            }
        );
        var info = new StackPanel { Margin = new(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(info, 1);
        g.Children.Add(info);
        var remaining = t
            .Group.Where(i => store.Counts(i) && store.Aired(i, DateTimeOffset.UtcNow) && !store.EverWatched(i))
            .ToList();
        if (badge.Length > 0 || left is > 0 and < 3)
            info.Children.Add(
                new Border
                {
                    Background = Brush(badge.Length > 0 ? "#E4C084" : "#33E4C084"),
                    CornerRadius = new(5),
                    Padding = new(7, 2, 7, 2),
                    Margin = new(0, 0, 0, 5),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = Text(
                        badge.Length > 0 ? badge : T("ALMOST DONE", "أوشكت على الإكمال"),
                        9.5,
                        badge.Length > 0 ? "#16120C" : "#E4C084",
                        FontWeights.Bold
                    ),
                }
            );
        var title = Text(show.Title, 17, weight: FontWeights.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.TextWrapping = TextWrapping.NoWrap;
        info.Children.Add(title);
        var spoiler = store.State.HideSpoilers && !store.EverWatched(next);
        var episode = new TextBlock
        {
            FontSize = 13.5,
            Margin = new(0, 5, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        episode.Inlines.Add(EpisodeRun(next, "#E4C084", FontWeights.SemiBold));
        if (next.EpisodeTitle.Length > 0 && !spoiler)
            episode.Inlines.Add(
                new System.Windows.Documents.Run("   " + next.EpisodeTitle) { Foreground = Brush("#D9D4CC") }
            );
        info.Children.Add(episode);
        var minutes = remaining.Sum(store.RuntimeMinutes);
        var meta = Text(
            EpisodesLeft(left)
                + (minutes > 0 ? " · ≈ " + MinutesText(minutes) : "")
                + (t.Last > DateTimeOffset.MinValue ? "  ·  " + Ago(t.Last) : "")
                + (!TrackerOnly && CanPlay(next) ? "  ·  " + T("On your PC", "على جهازك") : ""),
            12,
            "#9F9A93"
        );
        Bidi(meta);
        meta.Margin = new(0, 3, 0, 7);
        info.Children.Add(meta);
        var bar = new ProgressBar
        {
            Value = t.Watched,
            Maximum = Math.Max(1, t.Released),
            Height = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("#E4C084"),
            Background = Brush("#2E2C2F"),
            BorderThickness = new(0),
        };
        var completion = new DockPanel { LastChildFill = true };
        var progress = Text(T("{0} / {1}", "{0} / {1}", t.Watched, t.Released), 11, "#B9AB94");
        progress.Margin = new(10, 0, 0, 0);
        progress.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(progress, Dock.Right);
        completion.Children.Add(progress);
        completion.Children.Add(bar);
        info.Children.Add(completion);
        // Episodes skipped before the furthest one watched: offer to fill them in instead of ignoring them.
        var gaps = store.Gaps(t.Group).Where(i => i.Id != next.Id).ToList();
        if (gaps.Count > 0)
        {
            var fill = ActionButton($"Skipped {gaps.Count} earlier · Mark them", () =>
                BulkCheckOff(gaps, $"{show.Title}: {gaps.Count} skipped episode{(gaps.Count == 1 ? "" : "s")} marked")
            );
            fill.Background = Brush("Transparent");
            fill.Foreground = Brush("#E4C084");
            fill.FontSize = 12;
            fill.Padding = new(0, 6, 0, 0);
            fill.ToolTip = Range(gaps);
            info.Children.Add(fill);
        }
        if (dormant)
        {
            var nudge = new WrapPanel { Margin = new(0, 8, 0, 0) };
            var keep = ActionButton(T("Keep", "متابعة"), () =>
            {
                store.State.TrackerSnoozes[show.GroupKey] = DateTimeOffset.UtcNow.AddDays(60);
                store.Save();
                RenderQuiet();
            });
            keep.Padding = new(12, 5, 12, 5);
            keep.Margin = new(0, 0, 8, 0);
            nudge.Children.Add(keep);
            var stop = ActionButton(T("Stop showing", "إيقاف الإظهار"), () => StopShowing(show));
            stop.Padding = new(12, 5, 12, 5);
            stop.Background = Brush("Transparent");
            nudge.Children.Add(stop);
            info.Children.Add(nudge);
        }
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tools, 2);
        g.Children.Add(tools);
        if (!TrackerOnly && CanPlay(next))
        {
            var play = SmallRound("[[play]]", T("Play", "تشغيل") + " " + EpisodeTag(next), () => _ = PlayVideo(next));
            play.Background = Brush("#E4C084");
            play.Foreground = Brush("#16120C");
            tools.Children.Add(play);
        }
        else if (!TrackerOnly && StreamOffered)
        {
            var stream = SmallRound("[[play]]", T("Stream", "بث") + " " + EpisodeTag(next), () => StreamItem(next));
            tools.Children.Add(stream);
        }
        tools.Children.Add(
            CheckCircle(
                false,
                T("Mark {0} watched", "تسجيل مشاهدة {0}", EpisodeTag(next)),
                () =>
                {
                    historyContinued = show.GroupKey;
                    LogBatch(
                        [new(next, DateTimeOffset.UtcNow)],
                        T("{0} {1} watched", "تم تسجيل مشاهدة {0} {1}", show.Title, EpisodeTag(next)),
                        next
                    );
                },
                40
            )
        );
        if (historyContinued == show.GroupKey)
        {
            historyContinued = "";
            card.Loaded += (_, _) => HistoryPop(card);
        }
        LazyMenu(card, next);
        tools.Children.Add(MoreButton(next));
        var stopQuick = ActionButton("[[close]]", () => StopShowing(show));
        stopQuick.ToolTip = T("Stop showing", "إيقاف الإظهار");
        System.Windows.Automation.AutomationProperties.SetName(stopQuick, stopQuick.ToolTip.ToString());
        stopQuick.Padding = new(8, 5, 8, 5);
        stopQuick.Opacity = 0;
        tools.Children.Add(stopQuick);
        void StopVisibility() => stopQuick.Opacity = card.IsMouseOver || card.IsKeyboardFocusWithin ? 1 : 0;
        card.MouseEnter += (_, _) => StopVisibility();
        card.MouseLeave += (_, _) => StopVisibility();
        card.IsKeyboardFocusWithinChanged += (_, _) => StopVisibility();
        card.MouseEnter += (_, _) => card.BorderBrush = Brush("#40E4C084");
        card.MouseLeave += (_, _) => card.BorderBrush = Brush("#00000000");
        return card;
    }

    Button SmallRound(string glyph, string tip, Action act)
    {
        var b = RoundAction(glyph, tip, act);
        b.Width = b.Height = 40;
        b.Margin = new(0, 0, 10, 0);
        b.Background = Brush("#26FFFFFF");
        b.FontSize = 14;
        return b;
    }

    // The tracker's signature control: an outlined circle that fills gold with a tick.
    Button CheckCircle(bool on, string tip, Action act, double size = 40)
    {
        var ring = new Ellipse
        {
            Width = size,
            Height = size,
            StrokeThickness = 2,
            Stroke = Brush(on ? "#E4C084" : "#6B665E"),
            Fill = Brush(on ? "#E4C084" : "#00000000"),
        };
        var tick = new Path
        {
            Data = Geometry.Parse("M0,5 L4,9 L12,0"),
            Stroke = Brush(on ? "#16120C" : "#9F9A93"),
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = size * .4,
            Height = size * .32,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var face = new Grid
        {
            Width = size,
            Height = size,
            FlowDirection = FlowDirection.LeftToRight,
        }; /* A1-8: the tick does not mirror */
        face.Children.Add(ring);
        face.Children.Add(tick);
        var b = new Button
        {
            Content = face,
            Padding = new(0),
            Background = Brush("Transparent"),
            ToolTip = tip,
            Width = size,
            Height = size,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, tip.Replace("\u202A", "").Replace("\u202C", "")); // accessible names carry no direction marks
        b.MouseEnter += (_, _) =>
        {
            if (!on)
            {
                ring.Stroke = Brush("#E4C084");
                tick.Stroke = Brush("#E4C084");
            }
        };
        b.MouseLeave += (_, _) =>
        {
            if (!on)
            {
                ring.Stroke = Brush("#6B665E");
                tick.Stroke = Brush("#9F9A93");
            }
        };
        b.Click += (_, _) =>
        {
            if (!b.IsEnabled)
                return;
            b.IsEnabled = false;
            if (!on)
            {
                ring.Fill = Brush("#E4C084");
                ring.Stroke = Brush("#E4C084");
                tick.Stroke = Brush("#16120C");
                HistoryPop(face);
                if (SystemParameters.ClientAreaAnimation)
                {
                    var reveal = new ScaleTransform(1, 1);
                    tick.RenderTransform = reveal;
                    tick.RenderTransformOrigin = new(0, .5);
                    reveal.BeginAnimation(
                        ScaleTransform.ScaleXProperty,
                        new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                    );
                    tick.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                    tick.Unloaded += (_, _) =>
                    {
                        tick.BeginAnimation(OpacityProperty, null);
                        reveal.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    };
                }
            }
            if (!SystemParameters.ClientAreaAnimation)
            {
                act();
                b.IsEnabled = true;
                return;
            }
            // Let the fill animation show before the page re-renders.
            var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(on ? 1 : 260) };
            later.Tick += (_, _) =>
            {
                later.Stop();
                act();
                b.IsEnabled = true;
            };
            b.Unloaded += (_, _) => later.Stop();
            later.Start();
        };
        return b;
    }

    void CheckOff(
        List<VideoItem> items,
        string message,
        DateTimeOffset? at = null,
        bool dateUnknown = false,
        string source = "Checked off"
    )
    {
        var rewards = RewardState();
        var before = store.Snapshot(items);
        var added = store.LogWatched(
            items.Select(i => new LogEntry(i, at ?? DateTimeOffset.UtcNow, dateUnknown)),
            source
        );
        RenderQuiet();
        if (added > 0)
        {
            AfterCheck(items[^1], message, () =>
            {
                store.Restore(before);
                RenderQuiet();
            });
            CelebrateHistory(rewards);
        }
    }

    void UncheckAll(List<VideoItem> items, string message)
    {
        var undo = store.Unwatch(items);
        RenderQuiet();
        if (undo.Count > 0)
            Undoable(message, () =>
            {
                store.Restore(undo);
                RenderQuiet();
            });
    }

    Border? undoBar;
    DispatcherTimer? undoTimer;

    // Like the status notice, but clickable: every tracker change can be taken back for a few seconds.
    void Undoable(string message, Action undo, FrameworkElement? extra = null)
    {
        if (StatusToast.Parent is not Panel host)
            return;
        if (undoBar != null)
            host.Children.Remove(undoBar);
        undoTimer?.Stop();
        var row = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = Math.Max(260, ActualWidth - 100) };
        row.Children.Add(
            new TextBlock
            {
                Text = "✓  " + message,
                Foreground = Brush("#E9E4DC"),
                FontSize = 13.5,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 520,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }
        );
        var bar = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new(24, 0, 24, StatusToast.Opacity > .05 ? 80 : 26),
            Padding = new(20, 6, 8, 6),
            CornerRadius = new(22),
            Background = Brush("#F51C1C1F"),
            BorderBrush = Brush("#55E4C084"),
            BorderThickness = new(1),
            Child = row,
            Opacity = 0,
        };
        void Close()
        {
            undoTimer?.Stop();
            if (!SystemParameters.ClientAreaAnimation)
            {
                host.Children.Remove(bar);
            }
            else
            {
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
                fade.Completed += (_, _) => host.Children.Remove(bar);
                bar.BeginAnimation(OpacityProperty, fade);
            }
            if (undoBar == bar)
                undoBar = null;
        }
        var button = ActionButton(T("Undo", "تراجع"), () =>
        {
            Close();
            undo();
        });
        button.Background = Brush("Transparent");
        button.Foreground = Brush("#E4C084");
        button.FontWeight = FontWeights.SemiBold;
        button.Padding = new(14, 7, 14, 7);
        button.Margin = new(14, 0, 0, 0);
        row.Children.Add(button);
        if (extra != null)
            row.Children.Add(extra);
        host.Children.Add(bar);
        undoBar = bar;
        if (SystemParameters.ClientAreaAnimation)
            bar.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        else
            bar.Opacity = 1;
        bar.Unloaded += (_, _) => bar.BeginAnimation(OpacityProperty, null);
        undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(extra != null ? 14 : 7) };
        undoTimer.Tick += (_, _) => Close();
        undoTimer.Start();
    }

    // A progress ring drawn as an arc over the poster's corner.
    static FrameworkElement Ring(double fraction, double size, string label)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(
            new Ellipse
            {
                Fill = Brush("#E6101012"),
                Stroke = Brush("#3A3832"),
                StrokeThickness = 3.5,
            }
        );
        fraction = Math.Clamp(fraction, 0, 1);
        var r = (size - 3.5) / 2;
        var c = size / 2;
        if (fraction >= .999)
            grid.Children.Add(new Ellipse { Stroke = Brush("#E4C084"), StrokeThickness = 3.5 });
        else if (fraction > 0)
        {
            var angle = fraction * 2 * Math.PI;
            var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
            var figure = new PathFigure { StartPoint = new(c, c - r) };
            figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > .5, SweepDirection.Clockwise, true));
            grid.Children.Add(
                new Path
                {
                    Data = new PathGeometry([figure]),
                    Stroke = Brush("#E4C084"),
                    StrokeThickness = 3.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                }
            );
        }
        grid.Children.Add(
            new TextBlock
            {
                Text = label,
                FontSize = size * .27,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#F4F1EB"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        );
        return grid;
    }

    FrameworkElement TrackerPoster(Tracked t)
    {
        var show = t.Group[0];
        var panel = new StackPanel { Width = Density.PosterWidth, Margin = new(0, 0, Density.CardGap, 8) };
        var picture = new Grid { Height = Density.PosterHeight, ClipToBounds = true };
        var open = new Button
        {
            Content = Art(show.Poster, show.Title, Density.PosterHeight, true),
            Padding = new(0),
            Background = Brush("Transparent"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = show.Title + " · Episodes",
        };
        open.Click += (_, _) => OpenHistoryTitle(t.Group);
        picture.Children.Add(open);
        var shade = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
        shade.GradientStops.Add(new(Color.FromArgb(0, 0, 0, 0), .6));
        shade.GradientStops.Add(new(Color.FromArgb(200, 8, 8, 10), 1));
        picture.Children.Add(new Border { Background = shade, IsHitTestVisible = false });
        var chip = t.Status switch
        {
            "Caught up" => T("CAUGHT UP", "أكملت المتاح"),
            "Finished" => T("FINISHED", "مكتمل"),
            "Dropped" => T("DROPPED", "متوقف"),
            "Not started" => T("NOT STARTED", "لم تبدأ"),
            "Unfinished" => T("UNFINISHED", "غير مكتمل"),
            _ => "",
        };
        if (chip.Length > 0)
            picture.Children.Add(
                new Border
                {
                    IsHitTestVisible = false,
                    Background = Brush(t.Status is "Caught up" or "Finished" ? "#E4C084" : "#E6101012"),
                    CornerRadius = new(6),
                    Padding = new(7, 3, 7, 3),
                    Margin = new(10),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = Text(
                        chip,
                        9.5,
                        t.Status is "Caught up" or "Finished" ? "#16120C" : "#E4C084",
                        FontWeights.Bold
                    ),
                }
            );
        var more = MoreButton(show);
        more.HorizontalAlignment = HorizontalAlignment.Right;
        more.VerticalAlignment = VerticalAlignment.Top;
        more.Margin = new(8);
        picture.Children.Add(more);
        var edge = new Border
        {
            CornerRadius = new(10),
            ClipToBounds = true,
            Child = picture,
            BorderThickness = new(1.5),
            BorderBrush = Brush("#00000000"),
        };
        panel.Children.Add(edge);
        var name = Text(show.Title, 14, weight: FontWeights.SemiBold);
        name.Margin = new(0, 9, 0, 2);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextWrapping = TextWrapping.NoWrap;
        panel.Children.Add(name);
        var sub =
            t.Next != null && t.Status != "Dropped"
                ? T("Next", "التالي") + "  " + EpisodeTag(t.Next)
                : T("{0} of {1} episodes", "{0} من {1} حلقة", t.Watched, t.Released);
        if (ShowStars(show.GroupKey) is { Length: > 0 } rated)
            sub += "   " + rated;
        panel.Children.Add(Text(sub, 11, "#9F9A93"));
        if (t.Next != null && t.Status is "Watching" or "Not started" or "Unfinished")
        {
            var quick = CheckCircle(
                false,
                T("Mark {0} watched", "تسجيل مشاهدة {0}", EpisodeTag(t.Next)),
                () =>
                    SmartCheckOff(
                        t.Next,
                        T("{0} {1} watched", "تم تسجيل مشاهدة {0} {1}", show.Title, EpisodeTag(t.Next))
                    ),
                34
            );
            quick.Margin = new(0, -30, 0, 0);
            quick.HorizontalAlignment = HorizontalAlignment.Right;
            panel.Children.Add(quick);
        }
        if (t.Status == "Dropped")
        {
            var back = ActionButton(T("Bring back", "إعادة الإظهار"), () => BringBack(show));
            back.Margin = new(0, 8, 0, 0);
            panel.Children.Add(back);
        }
        LazyMenu(panel, show);
        return Lift(panel, edge);
    }

    void RenderTrackedShows()
    {
        var all = TrackedShows()
            .Where(t =>
                t.Watched > 0 || t.Status != "Not started" || store.State.Watchlist.Contains(t.Group[0].GroupKey)
            )
            .Where(t => t.Group.Any(TrackerFits))
            .ToList();
        string[] filters = ["All", "Watching", "Unfinished", "Caught up", "Not started", "Finished", "Dropped"];
        var counts = filters.ToDictionary(f => f, f => f == "All" ? all.Count : all.Count(t => t.Status == f));
        var bar = new DockPanel { Margin = new(0, 6, 0, 0) };
        var sort = Chips(["Recent", "A–Z", "Most left", "Rating"], showsSort, v =>
        {
            showsSort = v;
            Render();
        });
        DockPanel.SetDock(sort, Dock.Right);
        bar.Children.Add(sort);
        bar.Children.Add(
            Chips(filters.Where(f => f == "All" || counts[f] > 0).Select(f => f).ToArray(), showsFilter, v =>
            {
                showsFilter = v;
                trackerLimit = 60;
                Render();
            })
        );
        ContentPanel.Children.Add(bar);
        var shown = all.Where(t => showsFilter == "All" || t.Status == showsFilter).ToList();
        shown = (
            showsSort == "A–Z" ? shown.OrderBy(t => t.Group[0].Title)
            : showsSort == "Most left" ? shown.OrderByDescending(t => t.Released - t.Watched)
            : showsSort == "Rating"
                ? shown
                    .OrderByDescending(t => store.State.ShowRatings.GetValueOrDefault(t.Group[0].GroupKey))
                    .ThenByDescending(t => t.Last)
            : shown.OrderByDescending(t => t.Last)
        ).ToList();
        if (shown.Count == 0)
        {
            ContentPanel.Children.Add(
                Heading(
                    all.Count == 0 ? "No shows tracked yet" : "Nothing here",
                    all.Count == 0
                        ? "Log a show you've watched and every episode you check off appears here."
                        : "Try another filter."
                )
            );
            if (all.Count == 0)
                ContentPanel.Children.Add(ActionButton("[[plus]]  Log a show", () => OpenLog(null), true));
            return;
        }
        ContentPanel.Children.Add(
            Body(
                $"{shown.Count} show{(shown.Count == 1 ? "" : "s")} · {shown.Sum(t => t.Watched):N0} episodes watched · {shown.Sum(t => Math.Max(0, t.Released - t.Watched)):N0} to go",
                13
            )
        );
        var wrap = new WrapPanel { Margin = new(0, 8, 0, 0) };
        foreach (var t in shown.Take(trackerLimit))
            wrap.Children.Add(TrackerPoster(t));
        ContentPanel.Children.Add(wrap);
        if (shown.Count > trackerLimit)
            ContentPanel.Children.Add(MoreButton(shown.Count - trackerLimit));
    }

    Button MoreButton(int remaining)
    {
        var more = ActionButton($"Show more · {remaining} remaining", () =>
        {
            trackerLimit += 60;
            Render();
        });
        more.Style = (Style)FindResource("Pill");
        more.Padding = new(20, 10, 20, 10);
        more.HorizontalAlignment = HorizontalAlignment.Center;
        more.Margin = new(0, 16, 0, 0);
        return more;
    }

    void RenderTrackedMovies()
    {
        var movies = store
            .Archive.Where(i => i.IsMovie && TrackerFits(i))
            .Select(i => (Item: i, Views: store.ViewingHistory(i)))
            .Where(m => m.Views.Count > 0 || store.GetProgress(m.Item)?.Completed == true)
            .ToList();
        int Rating(VideoItem i) => store.State.PersonalReviews.GetValueOrDefault(i.Id)?.Rating ?? 0;
        var bar = new DockPanel { Margin = new(0, 6, 0, 0) };
        var sort = Chips(["Recent", "A–Z", "Rating"], moviesSort, v =>
        {
            moviesSort = v;
            Render();
        });
        DockPanel.SetDock(sort, Dock.Right);
        bar.Children.Add(sort);
        bar.Children.Add(Chips(["All", "Rated", "Rewatched", "Not rated"], moviesFilter, v =>
            {
                moviesFilter = v;
                trackerLimit = 60;
                Render();
            }));
        ContentPanel.Children.Add(bar);
        var shown = movies
            .Where(m =>
                moviesFilter switch
                {
                    "Rated" => Rating(m.Item) > 0,
                    "Not rated" => Rating(m.Item) == 0,
                    "Rewatched" => m.Views.Count > 1,
                    _ => true,
                }
            )
            .ToList();
        shown = (
            moviesSort == "A–Z" ? shown.OrderBy(m => m.Item.Title)
            : moviesSort == "Rating"
                ? shown.OrderByDescending(m => Rating(m.Item)).ThenByDescending(m => HistoryDate(m.Item))
            : shown.OrderByDescending(m => HistoryDate(m.Item))
        ).ToList();
        if (shown.Count == 0)
        {
            ContentPanel.Children.Add(
                Heading(
                    movies.Count == 0 ? "No movies logged yet" : "Nothing here",
                    movies.Count == 0
                        ? "Movies you finish in Noir, log by hand or import appear here."
                        : "Try another filter."
                )
            );
            if (movies.Count == 0)
                ContentPanel.Children.Add(ActionButton("[[plus]]  Log a movie", () => OpenLog(null), true));
            return;
        }
        ContentPanel.Children.Add(
            Body(
                $"{shown.Count} movie{(shown.Count == 1 ? "" : "s")} · {shown.Sum(m => m.Views.Count)} viewing{(shown.Sum(m => m.Views.Count) == 1 ? "" : "s")} · {shown.Count(m => Rating(m.Item) > 0)} rated",
                13
            )
        );
        var wrap = new WrapPanel { Margin = new(0, 8, 0, 0) };
        foreach (var (item, list) in shown.Take(trackerLimit))
        {
            var panel = new StackPanel { Width = Density.PosterWidth, Margin = new(0, 0, Density.CardGap, 22) };
            var picture = new Grid { Height = Density.PosterHeight, ClipToBounds = true };
            var open = new Button
            {
                Content = Art(item.Poster, item.Title, Density.PosterHeight, true),
                Padding = new(0),
                Background = Brush("Transparent"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = item.Title,
            };
            open.Click += (_, _) => OpenHistoryTitle(store.EpisodesFor(item.GroupKey));
            picture.Children.Add(open);
            if (list.Count > 1)
                picture.Children.Add(
                    new Border
                    {
                        IsHitTestVisible = false,
                        Background = Brush("#E4C084"),
                        CornerRadius = new(6),
                        Padding = new(7, 3, 7, 3),
                        Margin = new(10),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Child = Text("×" + list.Count, 11, "#16120C", FontWeights.Bold),
                    }
                );
            if (!TrackerOnly)
                picture.Children.Add(CardMenu(item));
            var edge = new Border
            {
                CornerRadius = new(10),
                ClipToBounds = true,
                Child = picture,
                BorderThickness = new(1.5),
                BorderBrush = Brush("#00000000"),
            };
            panel.Children.Add(edge);
            var name = Text(item.Title, 14, weight: FontWeights.SemiBold);
            name.Margin = new(0, 9, 0, 2);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.TextWrapping = TextWrapping.NoWrap;
            panel.Children.Add(name);
            var last = list.FirstOrDefault();
            var when =
                last == null ? "Watched"
                : last.DateUnknown ? "Date unknown"
                : last.WatchedAt.ToLocalTime().ToString("d MMM yyyy");
            var row = new DockPanel();
            var stars = Stars(item, 13);
            DockPanel.SetDock(stars, Dock.Right);
            row.Children.Add(stars);
            row.Children.Add(Text(when, 11, "#9F9A93"));
            panel.Children.Add(row);
            if (!TrackerOnly)
                LazyMenu(panel, item);
            wrap.Children.Add(Lift(panel, edge));
        }
        ContentPanel.Children.Add(wrap);
        if (shown.Count > trackerLimit)
            ContentPanel.Children.Add(MoreButton(shown.Count - trackerLimit));
    }

    // Five clickable stars; the rating is private and syncs to the phone like the diary's.
    // One text line of clickable stars: long lists render hundreds of these, and five buttons each made layout slow.
    FrameworkElement Stars(VideoItem item, double size = 16) => HalfRatePicker(item, size);

    string historyMonthTracker = "All time";
    bool hideLoggedHistory;
    readonly HashSet<string> expandedUndated = [];
    int undatedGroupLimit = 40;
    readonly Dictionary<string, int> undatedViewingLimits = [];

    Button UndatedMore(int remaining, Action more)
    {
        var button = ActionButton(T("Show more ({0})", "عرض المزيد ({0})", remaining), more);
        button.Style = (Style)FindResource("Pill");
        button.Padding = new(20, 10, 20, 10);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.Margin = new(0, 16, 0, 0);
        return button;
    }

    void RenderTrackerDiary()
    {
        var records = store
            .Archive.Where(TrackerFits)
            .SelectMany(i => store.ViewingHistory(i).Select(v => (Item: i, View: v)))
            .Where(r =>
                historyType switch
                {
                    "Movies" => r.Item.IsMovie,
                    "Shows" => !r.Item.IsMovie,
                    _ => true,
                }
            )
            .Where(r => !hideLoggedHistory || !r.View.Backfill)
            .ToList();
        var dated = records.Where(r => !r.View.DateUnknown).OrderByDescending(r => r.View.WatchedAt).ToList();
        var undated = records
            .Where(r => r.View.DateUnknown)
            .OrderBy(r => r.Item.Title)
            .ThenBy(r => r.Item.Season)
            .ThenBy(r => r.Item.Episode)
            .ToList();
        var months = dated.Select(r => r.View.WatchedAt.ToLocalTime().ToString("yyyy-MM")).Distinct().ToList();
        if (historyMonthTracker != "All time" && !months.Contains(historyMonthTracker))
            historyMonthTracker = "All time";
        var bar = new DockPanel { Margin = new(0, 6, 0, 0) };
        var month = ActionButton(
            (
                historyMonthTracker == "All time"
                    ? "All time"
                    : DateTime.Parse(historyMonthTracker + "-01").ToString("MMMM yyyy")
            ) + "  [[chevron-down]]",
            () => { }
        );
        month.Style = (Style)FindResource("Pill");
        month.Padding = new(16, 8, 16, 8);
        month.Margin = new(0, 14, 0, 8);
        DockPanel.SetDock(month, Dock.Right);
        bar.Children.Add(month);
        var menu = new ContextMenu();
        foreach (var value in new[] { "All time" }.Concat(months))
        {
            var v = value;
            var option = new MenuItem
            {
                Header = v == "All time" ? v : DateTime.Parse(v + "-01").ToString("MMMM yyyy"),
            };
            option.Click += (_, _) =>
            {
                historyMonthTracker = v;
                trackerLimit = 60;
                Render();
            };
            menu.Items.Add(option);
        }
        month.Click += (_, _) =>
        {
            menu.PlacementTarget = month;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        var hide = new CheckBox
        {
            Content = T("Hide logged history", "إخفاء السجل المدوَّن"),
            IsChecked = hideLoggedHistory,
            Foreground = Brush("#C7C2BC"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new(0, 14, 16, 8),
            ToolTip = T(
                "Only what you recorded as you watched it, without back-filled or imported entries",
                "فقط ما سجّلته أثناء المشاهدة، دون الإدخالات المضافة لاحقًا أو المستوردة"
            ),
        };
        hide.Click += (_, _) =>
        {
            hideLoggedHistory = hide.IsChecked == true;
            trackerLimit = 60;
            Render();
        };
        DockPanel.SetDock(hide, Dock.Right);
        bar.Children.Add(hide);
        bar.Children.Add(Chips(["All", "Movies", "Shows"], historyType, v =>
            {
                historyType = v;
                trackerLimit = 60;
                Render();
            }));
        ContentPanel.Children.Add(bar);
        var inMonth = dated
            .Where(r =>
                historyMonthTracker == "All time"
                || r.View.WatchedAt.ToLocalTime().ToString("yyyy-MM") == historyMonthTracker
            )
            .ToList();
        if (inMonth.Count + undated.Count == 0)
        {
            ContentPanel.Children.Add(
                Heading(
                    T("Your story starts here", "حكايتك تبدأ هنا"),
                    T(
                        "Log a favourite, or bring your history from Netflix, Trakt and more.",
                        "سجّل عملاً تحبه أو استورد سجلك من نتفليكس وتراكت وغيرها."
                    )
                )
            );
            HistoryEmptyActions();
            return;
        }
        var shown = 0;
        foreach (var day in inMonth.GroupBy(r => r.View.WatchedAt.ToLocalTime().Date))
        {
            if (shown >= trackerLimit)
                break;
            var label =
                day.Key == DateTime.Today ? "Today"
                : day.Key == DateTime.Today.AddDays(-1) ? "Yesterday"
                : day.Key.ToString(day.Key.Year == DateTime.Today.Year ? "dddd, d MMMM" : "dddd, d MMMM yyyy");
            var heading = new DockPanel { Margin = new(0, 26, 0, 10) };
            var count = Text(T("{0} watched", "{0} مشاهدة", day.Count()), 12, "#B5A68E");
            count.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(count, Dock.Right);
            heading.Children.Add(count);
            heading.Children.Add(
                Text(
                    label == "Today" ? T("Today", "اليوم")
                        : label == "Yesterday" ? T("Yesterday", "أمس")
                        : label,
                    19,
                    weight: FontWeights.SemiBold
                )
            );
            ContentPanel.Children.Add(heading);
            foreach (var r in day.Take(Math.Max(0, trackerLimit - shown)))
            {
                ContentPanel.Children.Add(DiaryRow(r.Item, r.View));
                shown++;
            }
        }
        if (inMonth.Count > shown)
        {
            ContentPanel.Children.Add(MoreButton(inMonth.Count - shown));
            return;
        }
        if (undated.Count > 0 && historyMonthTracker == "All time")
        {
            ContentPanel.Children.Add(
                Heading("Date unknown", $"{undated.Count} logged without a date. Set one any time with ✎.")
            );
            var undatedGroups = undated.GroupBy(r => r.Item.GroupKey).ToList();
            foreach (var group in undatedGroups.Take(undatedGroupLimit))
            {
                var first = group.First().Item;
                var key = first.GroupKey;
                var views = group.ToList();
                if (views.Count == 1)
                {
                    ContentPanel.Children.Add(DiaryRow(first, views[0].View));
                    continue;
                }
                var row = new Border
                {
                    Background = Brush("#141416"),
                    CornerRadius = new(12),
                    Padding = new(14, 10, 14, 10),
                    Margin = new(0, 0, 0, 8),
                };
                var dock = new DockPanel();
                row.Child = dock;
                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                DockPanel.SetDock(buttons, Dock.Right);
                dock.Children.Add(buttons);
                Button Small(string label, Action<Button> act)
                {
                    var b = ActionButton(label, () => { });
                    b.Padding = new(12, 6, 12, 6);
                    b.Margin = new(8, 0, 0, 0);
                    b.Click += (_, _) => act(b);
                    buttons.Children.Add(b);
                    return b;
                }
                // One date for the whole group covers "I binged season 1 in March 2019"; single viewings are editable when expanded.
                Small("[[pencil]]  Set date for all", b => DateChooser(
                        b,
                        DateTime.Today,
                        (day, unknown) =>
                        {
                            if (unknown)
                                return;
                            var undo = store.Snapshot(views.Select(r => r.Item).DistinctBy(i => i.Id));
                            foreach (var r in views)
                                store.SetViewingDate(
                                    r.Item,
                                    r.View.Id,
                                    new DateTimeOffset(day.Date.AddHours(20)),
                                    false
                                );
                            RenderQuiet();
                            Undoable(
                                $"{first.Title}: {views.Count} viewing{(views.Count == 1 ? "" : "s")} dated {day:d MMM yyyy}",
                                () =>
                                {
                                    store.Restore(undo);
                                    RenderQuiet();
                                }
                            );
                        },
                        false
                    ));
                Small(expandedUndated.Contains(key) ? "Hide" : "Show " + views.Count, _ =>
                {
                    if (!expandedUndated.Remove(key))
                        expandedUndated.Add(key);
                    RenderQuiet();
                });
                Small("Open", _ => OpenHistoryTitle(store.EpisodesFor(key)));
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(Text(first.Title, 15, weight: FontWeights.SemiBold));
                text.Children.Add(
                    Text(
                        first.IsMovie
                            ? "Movie"
                            : $"{group.Count()} episode{(group.Count() == 1 ? "" : "s")} · "
                                + string.Join(
                                    ", ",
                                    group.GroupBy(r => r.Item.Season).Select(s => $"Season {s.Key} ({s.Count()})")
                                ),
                        12,
                        "#9F9A93"
                    )
                );
                dock.Children.Add(text);
                ContentPanel.Children.Add(row);
                if (expandedUndated.Contains(key))
                {
                    var limit = undatedViewingLimits.GetValueOrDefault(key, 200);
                    foreach (var r in views.Take(limit))
                        ContentPanel.Children.Add(DiaryRow(r.Item, r.View));
                    if (views.Count > limit)
                        ContentPanel.Children.Add(UndatedMore(views.Count - limit, () =>
                            {
                                undatedViewingLimits[key] = limit + 200;
                                RenderQuiet();
                            }));
                }
            }
            if (undatedGroups.Count > undatedGroupLimit)
                ContentPanel.Children.Add(UndatedMore(undatedGroups.Count - undatedGroupLimit, () =>
                    {
                        undatedGroupLimit += 40;
                        RenderQuiet();
                    }));
        }
    }

    static string SourceLabel(string source) =>
        source.StartsWith("Imported from ")
            ? source[14..]
            : source switch
            {
                "Playback" => T("Played in Noir", "شُغّل في Noir"),
                "Android playback" => T("Phone", "الهاتف"),
                "Marked on Android" => T("Phone", "الهاتف"),
                "Earlier record" => "Noir",
                _ => source,
            };

    FrameworkElement HistoryArt(VideoItem item, double height, bool still = false)
    {
        var path = still && System.IO.File.Exists(item.EpisodeImage) ? item.EpisodeImage : item.Poster;
        if (System.IO.File.Exists(path))
            return Art(path, item.Title, height, !still);
        var colors = new[] { "#51432E", "#344B4A", "#4B3650", "#344456", "#503A36" };
        var hash = item.Title.Aggregate(0, (n, c) => (n * 31 + c) & 0x7fffffff);
        var initial = Text(
            item.Title.Trim().Length > 0
                ? System.Globalization.StringInfo.GetNextTextElement(item.Title.Trim()).ToUpperInvariant()
                : "N",
            Math.Min(30, height * .45),
            "#E4C084",
            FontWeights.Bold
        );
        initial.HorizontalAlignment = HorizontalAlignment.Center;
        initial.VerticalAlignment = VerticalAlignment.Center;
        var tile = new Grid
        {
            Height = height,
            Background = new LinearGradientBrush(Brush(colors[hash % colors.Length]).Color, Brush("#171719").Color, 45),
        };
        tile.Children.Add(initial);
        return tile;
    }

    FrameworkElement DiaryRow(VideoItem item, ViewingRecord view)
    {
        var card = new Border
        {
            Background = Brush("#141416"),
            CornerRadius = new(12),
            Padding = new(10),
            Margin = new(0, 0, 0, 8),
            BorderBrush = Brush("#00000000"),
            BorderThickness = new(1),
        };
        var g = new Grid();
        card.Child = g;
        g.ColumnDefinitions.Add(new() { Width = new(58) });
        g.ColumnDefinitions.Add(new() { Width = new(Density.Pick(66, 78)) });
        g.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var time = Text(view.DateUnknown ? "—" : view.WatchedAt.ToLocalTime().ToString("HH:mm"), 13, "#817E78");
        time.VerticalAlignment = VerticalAlignment.Center;
        time.Margin = new(6, 0, 0, 0);
        g.Children.Add(time);
        var poster = new Border
        {
            CornerRadius = new(8),
            ClipToBounds = true,
            Child = HistoryArt(item, Density.Pick(93, 110)),
            Height = Density.Pick(93, 110),
            Width = Density.Pick(62, 74),
        };
        Grid.SetColumn(poster, 1);
        g.Children.Add(poster);
        var info = new StackPanel { Margin = new(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(info, 2);
        g.Children.Add(info);
        var title = ActionButton("", () => OpenHistoryTitle(store.EpisodesFor(item.GroupKey)));
        title.Content = Text(item.Title, 15.5, weight: FontWeights.SemiBold);
        title.Background = Brush("Transparent");
        title.Padding = new(0);
        info.Children.Add(title);
        var line = new TextBlock
        {
            FontSize = 12.5,
            Margin = new(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        line.Inlines.Add(
            item.IsMovie
                ? new System.Windows.Documents.Run(item.Year > 0 ? item.Year.ToString() : T("Movie"))
                {
                    Foreground = Brush("#E4C084"),
                }
                : EpisodeRun(item, "#E4C084")
        );
        if (!item.IsMovie && item.EpisodeTitle.Length > 0)
            line.Inlines.Add(
                new System.Windows.Documents.Run("   " + item.EpisodeTitle) { Foreground = Brush("#C7C2BC") }
            );
        info.Children.Add(line);
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 6, 0, 0) };
        meta.Children.Add(
            new Border
            {
                Background = Brush("#1FFFFFFF"),
                CornerRadius = new(5),
                Padding = new(7, 2, 7, 2),
                Child = Text(SourceLabel(view.Source), 10.5, "#C7C2BC"),
            }
        );
        if (view.Approximate)
            meta.Children.Add(
                new Border
                {
                    Background = Brush("#14FFFFFF"),
                    CornerRadius = new(5),
                    Padding = new(7, 2, 7, 2),
                    Margin = new(6, 0, 0, 0),
                    Child = Text(T("Approx. date", "تاريخ تقريبي"), 10.5, "#9F9A93"),
                }
            );
        var rewatch = store.ViewingHistory(item).Count(v => v.WatchedAt < view.WatchedAt);
        if (rewatch > 0)
            meta.Children.Add(
                new Border
                {
                    Background = Brush("#33E4C084"),
                    CornerRadius = new(5),
                    Padding = new(7, 2, 7, 2),
                    Margin = new(6, 0, 0, 0),
                    Child = Text(T("↻ Rewatch", "↻ إعادة مشاهدة"), 10.5, "#E4C084"),
                }
            );
        info.Children.Add(meta);
        if (store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Note is { Length: > 0 } note)
        {
            var snippet = Text(note.ReplaceLineEndings(" "), 12, "#B5ADA1");
            snippet.TextTrimming = TextTrimming.CharacterEllipsis;
            snippet.TextWrapping = TextWrapping.NoWrap;
            snippet.Margin = new(0, 6, 0, 0);
            snippet.MaxHeight = 20;
            info.Children.Add(snippet);
        }
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tools, 3);
        g.Children.Add(tools);
        var rate = Stars(item, 15);
        var hasRating = (store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Rating10 ?? 0) > 0;
        rate.Opacity = hasRating ? 1 : .4;
        tools.Children.Add(rate);
        var liked = HistoryLiked(item);
        var heart = ActionButton(liked ? "[[heart-filled]]" : "[[heart]]", () => ToggleHistoryFavourite(item));
        heart.Foreground = Brush(liked ? "#E4C084" : "#81796B");
        heart.Background = Brush("Transparent");
        heart.Padding = new(8, 5, 8, 5);
        heart.FontSize = 19;
        heart.ToolTip = HistoryFavouriteLabel(item);
        System.Windows.Automation.AutomationProperties.SetName(heart, heart.ToolTip.ToString());
        tools.Children.Add(heart);
        var more = ActionButton("[[more]]", () => { });
        more.Padding = new(11, 2, 11, 8);
        more.FontSize = 17;
        more.Margin = new(8, 0, 0, 0);
        more.VerticalAlignment = VerticalAlignment.Center;
        more.Background = Brush("#1FFFFFFF");
        more.ToolTip = T(
            "More: change the date, remove, download, match… (or right-click)",
            "المزيد: تغيير التاريخ والحذف والتنزيل والمطابقة… (أو النقر بالزر الأيمن)"
        );
        System.Windows.Automation.AutomationProperties.SetName(
            more,
            T("More options for", "خيارات أخرى لـ") + " " + item.Label
        );
        tools.Children.Add(more);
        g.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
            {
                var narrow = e.NewSize.Width < 680;
                tools.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
                g.ColumnDefinitions[0].Width = new(narrow ? 0 : 58);
                time.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            }
        };
        void ChangeDate() =>
            DateChooser(
                more,
                view.DateUnknown ? DateTime.Today : view.WatchedAt.ToLocalTime().DateTime,
                (day, unknown) =>
                {
                    var at = unknown
                        ? view.WatchedAt
                        : new DateTimeOffset(
                            day.Date.Add(
                                view.DateUnknown ? new TimeSpan(20, 0, 0) : view.WatchedAt.ToLocalTime().TimeOfDay
                            )
                        );
                    var undo = store.Snapshot([item]);
                    store.SetViewingDate(item, view.Id, at, unknown);
                    RenderQuiet();
                    Undoable("Date changed", () =>
                    {
                        store.Restore(undo);
                        RenderQuiet();
                    });
                }
            );
        void RemoveViewing()
        {
            var undo = store.Snapshot([item]);
            store.RemoveViewing(item, view.Id);
            RenderQuiet();
            Undoable($"Removed {item.Title} from {view.WatchedAt.ToLocalTime():d MMM}", () =>
            {
                store.Restore(undo);
                RenderQuiet();
            });
        }
        more.Click += (_, e) =>
        {
            e.Handled = true;
            var menu = ItemMenu(item);
            var remove = new MenuItem { Header = T("Remove this viewing", "حذف هذه المشاهدة") };
            remove.Click += (_, _) => RemoveViewing();
            var edit = new MenuItem { Header = T("Change the date", "تغيير التاريخ") };
            edit.Click += (_, _) => ChangeDate();
            menu.Items.Insert(0, new Separator());
            menu.Items.Insert(0, remove);
            menu.Items.Insert(0, edit);
            more.ContextMenu = menu;
            menu.PlacementTarget = more;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        card.MouseEnter += (_, _) =>
        {
            card.BorderBrush = Brush("#2EFFFFFF");
            Motion.Opacity(rate, 1);
            rate.ToolTip = T(
                "Click the left or right half of a star to rate",
                "انقر النصف الأيسر أو الأيمن للنجمة للتقييم"
            );
        };
        card.MouseLeave += (_, _) =>
        {
            card.BorderBrush = Brush("#00000000");
            Motion.Opacity(rate, hasRating ? 1 : .4);
        };
        if (!TrackerOnly)
            LazyMenu(card, item);
        return card;
    }

    // A small dark month calendar in a popup; future days are disabled.
    void DateChooser(
        FrameworkElement? anchor,
        DateTime initial,
        Action<DateTime, bool> chosen,
        bool allowUnknown = true
    )
    {
        var popup = new Popup
        {
            FlowDirection = LibraryRoot.FlowDirection,
            PlacementTarget = anchor,
            Placement = anchor == null ? PlacementMode.MousePoint : PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
        };
        var shown = new DateTime(initial.Year, initial.Month, 1);
        var body = new StackPanel { Margin = new(14) };
        // Keyboard like the other History popups: focus starts on the selected day, Tab stays inside, Esc closes, focus goes back to the button that opened it.
        var invoker = anchor ?? Keyboard.FocusedElement as FrameworkElement;
        var picked = false;
        string? refocus = null;
        Button? start = null;
        KeyboardNavigation.SetTabNavigation(body, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(body, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(body, KeyboardNavigationMode.Cycle);
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                popup.IsOpen = false;
            }
        };
        popup.Opened += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                if (popup.IsOpen && start != null)
                    Keyboard.Focus(start);
            });
        popup.Closed += (_, _) =>
        {
            if (!picked && invoker != null && invoker.IsLoaded)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                {
                    if (!closing)
                        invoker.Focus();
                });
        };
        popup.Child = new Border
        {
            Background = Brush("#F7141416"),
            BorderBrush = Brush("#2EFFFFFF"),
            BorderThickness = new(1),
            CornerRadius = new(14),
            Child = body,
            Width = 300,
        };
        void Draw()
        {
            body.Children.Clear();
            var head = new DockPanel { Margin = new(0, 0, 0, 8) };
            Button Nav(string g, int months)
            {
                var b = new Button
                {
                    Content = g,
                    Padding = new(10, 4, 10, 4),
                    Background = Brush("#1FFFFFFF"),
                };
                System.Windows.Automation.AutomationProperties.SetName(
                    b,
                    months == -1 ? "Previous month"
                        : months == 1 ? "Next month"
                        : "Previous year"
                );
                b.Click += (_, _) =>
                {
                    shown = shown.AddMonths(months);
                    refocus = g;
                    Draw();
                };
                return b;
            }
            var back = Nav("[[chevron-left]]", -1);
            DockPanel.SetDock(back, Dock.Left);
            head.Children.Add(back);
            var fwd = Nav("[[chevron]]", 1);
            fwd.IsEnabled = shown.AddMonths(1) <= DateTime.Today;
            DockPanel.SetDock(fwd, Dock.Right);
            head.Children.Add(fwd);
            var yearBack = Nav("«", -12);
            DockPanel.SetDock(yearBack, Dock.Left);
            yearBack.Margin = new(6, 0, 0, 0);
            head.Children.Add(yearBack);
            var title = Text(shown.ToString("MMMM yyyy"), 14, weight: FontWeights.SemiBold);
            title.TextAlignment = TextAlignment.Center;
            title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);
            body.Children.Add(head);
            var grid = new UniformGrid { Columns = 7 };
            foreach (var d in new[] { "M", "T", "W", "T", "F", "S", "S" })
                grid.Children.Add(
                    new TextBlock
                    {
                        Text = d,
                        FontSize = 11,
                        Foreground = Brush("#817E78"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new(0, 0, 0, 4),
                    }
                );
            for (int i = 0; i < ((int)shown.DayOfWeek + 6) % 7; i++)
                grid.Children.Add(new Border());
            for (var day = shown; day.Month == shown.Month; day = day.AddDays(1))
            {
                var value = day;
                var selected = value == initial.Date;
                var b = new Button
                {
                    Content = value.Day.ToString(),
                    Padding = new(0, 6, 0, 6),
                    Margin = new(2),
                    FontSize = 12.5,
                    Background = Brush(
                        selected ? "#E4C084"
                        : value == DateTime.Today ? "#33E4C084"
                        : "Transparent"
                    ),
                    Foreground = Brush(selected ? "#16120C" : "#E9E4DC"),
                    IsEnabled = value <= DateTime.Today,
                };
                b.Click += (_, _) =>
                {
                    picked = true;
                    popup.IsOpen = false;
                    chosen(value, false);
                };
                grid.Children.Add(b);
                if (selected && b.IsEnabled || start == null && value == DateTime.Today)
                    start = b;
            }
            body.Children.Add(grid);
            var quick = new WrapPanel { Margin = new(0, 8, 0, 0) };
            void Quick(string label, Action act)
            {
                var b = new Button
                {
                    Content = label,
                    Padding = new(10, 5, 10, 5),
                    Margin = new(0, 0, 6, 6),
                    FontSize = 12,
                    Background = Brush("#1FFFFFFF"),
                };
                b.Click += (_, _) =>
                {
                    picked = true;
                    popup.IsOpen = false;
                    act();
                };
                quick.Children.Add(b);
            }
            Quick("Today", () => chosen(DateTime.Today, false));
            Quick("Yesterday", () => chosen(DateTime.Today.AddDays(-1), false));
            if (allowUnknown)
                Quick("Don't remember", () => chosen(DateTime.Today, true));
            body.Children.Add(quick);
            start ??= grid.Children.OfType<Button>().LastOrDefault(b => b.IsEnabled)
                ?? quick.Children.OfType<Button>().FirstOrDefault();
            if (refocus != null)
            {
                var again = head.Children.OfType<Button>().FirstOrDefault(b => IconSet.Src(b.Content) == refocus);
                refocus = null;
                if (again != null)
                {
                    if (!again.IsEnabled)
                        again = back;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                        Keyboard.Focus(again)
                    );
                }
            }
        }
        Draw();
        popup.IsOpen = true;
    }
}
