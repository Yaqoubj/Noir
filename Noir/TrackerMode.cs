using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noir;

// "Just track" mode: Noir as a TV Time-style tracker with no media folders. History becomes the
// home page and the file-library pages step aside; switching back is one choice in Settings.
public partial class MainWindow
{
    bool TrackerOnly => store.State.AppMode == "Tracker";

    // Only a brand-new profile is asked; existing libraries keep working exactly as before.
    bool NeedsWelcome => store.State.AppMode == "" && store.State.Roots.Count == 0 && store.Archive.Count == 0;
    UIElement[]? trackerOriginalNav;
    Button? trackerDiaryNav,
        trackerStatsNav,
        trackerLogNav;

    void TrackerDiscover_Click(object sender, RoutedEventArgs e) => OpenLog(null);

    void OpenHistoryTitle(List<VideoItem> group)
    {
        if (!TrackerOnly)
        {
            OpenDetails(group);
            return;
        }
        if (group.Count == 0)
            return;
        var item = group[0];
        if (item.ImdbId.Length == 0)
        {
            logQuery = item.Title;
            OpenLog(null);
            return;
        }
        page = "History";
        detailKey = null;
        logging = true;
        logLoading = false;
        logSeason = 0;
        logWhen = "Just now";
        logDay = DateTime.Today;
        logTitle = new DiscoverTitle
        {
            Id = item.ImdbId,
            Type = item.IsMovie ? "movie" : "series",
            Name = item.Title,
            Years = item.Year > 0 ? item.Year.ToString() : "",
            Poster = item.Poster,
            Summary = item.Summary,
            Genres = item
                .Genres.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList(),
            Episodes = store.State.EpisodeSchedules.GetValueOrDefault(item.GroupKey)
                ?? group
                    .Where(i => i.Season > 0)
                    .Select(i => new KnownEpisode
                    {
                        Season = i.Season,
                        Episode = i.Episode,
                        Title = i.EpisodeTitle,
                    })
                    .ToList(),
        };
        Render();
        ContentScroll.ScrollToTop();
    }

    const string DiaryGlyph =
            "M5,3 L17,3 A2,2 0 0 1 19,5 L19,21 L7,21 A2,2 0 0 1 5,19 Z M5,17 L19,17 M9,8 L15,8 M9,11 L14,11",
        StatsGlyph = "M4,21 L20,21 M6,17 L6,11 M11,17 L11,6 M16,17 L16,13";

    void ApplyAppMode()
    {
        var library = TrackerOnly ? Visibility.Collapsed : Visibility.Visible;
        foreach (var button in new[] { HomeNav, ShowNav, MovieNav, DownloadsNav, ScanNav })
            button.Visibility = library;
        UpcomingNav.Visibility = WatchlistNav.Visibility = library;
        trackerOriginalNav ??= NavLinks.Children.Cast<UIElement>().ToArray();
        Button Tab(string label, string glyph, string tab)
        {
            var button = new Button { Style = (Style)FindResource("SideNav") };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Icon(glyph, 19));
            row.Children.Add(Text(label, 14));
            ((FrameworkElement)row.Children[1]).Margin = new(14, 0, 0, 0);
            button.Content = row;
            button.ToolTip = label;
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) =>
            {
                trackerTab = tab;
                logging = false;
                Navigate("History");
            };
            return button;
        }
        trackerDiaryNav ??= Tab(T("Diary", "اليوميات"), DiaryGlyph, "Diary");
        trackerStatsNav ??= Tab(T("Stats", "الإحصاءات"), StatsGlyph, "Stats");
        foreach (
            var pair in new[] { (trackerDiaryNav, T("Diary", "اليوميات")), (trackerStatsNav, T("Stats", "الإحصاءات")) }
        )
            if (pair.Item1.Content is StackPanel row && row.Children[1] is TextBlock text)
            {
                text.Text = pair.Item2;
                text.Visibility = railCompact ? Visibility.Collapsed : Visibility.Visible;
                pair.Item1.HorizontalContentAlignment = railCompact
                    ? HorizontalAlignment.Center
                    : HorizontalAlignment.Left;
                pair.Item1.ToolTip = pair.Item2;
            }
        if (trackerLogNav == null)
        {
            trackerLogNav = ActionButton(T("[[plus]]  Log", "[[plus]]  سجّل"), () => OpenLog(null), true);
            trackerLogNav.Margin = new(12, 8, 12, 12);
            trackerLogNav.Padding = new(8, 12, 8, 12);
            DockPanel.SetDock(trackerLogNav, Dock.Top);
            if (NavScroll.Parent is DockPanel dock)
                dock.Children.Insert(Math.Max(0, dock.Children.IndexOf(NavScroll)), trackerLogNav);
        }
        trackerLogNav.Visibility = Visibility.Collapsed;
        DiscoverNav.Click -= TrackerDiscover_Click;
        DiscoverNav.Click -= Discover_Click;
        DiscoverNav.Click += TrackerOnly ? TrackerDiscover_Click : Discover_Click;
        if (TrackerOnly)
        {
            foreach (var button in new[] { HistoryNav, DiscoverNav, trackerDiaryNav, trackerStatsNav })
                NavLinks.Children.Remove(button);
            NavLinks.Children.Insert(0, HistoryNav);
            NavLinks.Children.Insert(1, trackerDiaryNav);
            NavLinks.Children.Insert(2, DiscoverNav);
            NavLinks.Children.Insert(3, trackerStatsNav);
            if (navItems.TryGetValue(HistoryNav, out var item))
                item.Text.Text = T("Next", "التالي");
            trackerDiaryNav.Tag = page == "History" && trackerTab == "Diary" ? "Selected" : null;
            trackerStatsNav.Tag = page == "History" && trackerTab == "Stats" ? "Selected" : null;
            if (page == "History" && (logging || trackerTab is "Diary" or "Stats"))
                HistoryNav.Tag = null;
            if (logging)
                DiscoverNav.Tag = "Selected";
        }
        else
        {
            NavLinks.Children.Remove(trackerDiaryNav);
            NavLinks.Children.Remove(trackerStatsNav);
            foreach (var child in trackerOriginalNav)
            {
                NavLinks.Children.Remove(child);
                NavLinks.Children.Add(child);
            }
            if (navItems.TryGetValue(HistoryNav, out var item))
                item.Text.Text = T("History");
        }
    }

    void ChooseAppMode(string mode)
    {
        store.State.AppMode = mode;
        store.Save();
        ApplyAppMode();
        if (mode == "Tracker")
        {
            historyType = "All";
            Navigate("History");
        }
        else
        {
            settingsSection = "Library & folders";
            Navigate("Settings");
        }
    }

    void RenderWelcome()
    {
        PageTitle.Text = "Welcome to Noir";
        PageEyebrow.Text = "GETTING STARTED";
        ContentPanel.Children.Add(Body("Choose how you'll use it. You can switch any time in Settings → Library.", 15));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new(-8, 10, -8, 0) };
        grid.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
                grid.Columns = e.NewSize.Width < 760 ? 1 : 2;
        };
        FrameworkElement Card(
            string eyebrow,
            string title,
            string text,
            string[] points,
            string action,
            Action choose,
            bool primary,
            (string Label, Action Act)? extra = null
        )
        {
            var card = new Border
            {
                Background = primary
                    ? new LinearGradientBrush(Brush("#2A241C").Color, Brush("#141416").Color, 25)
                    : Brush("#141416"),
                CornerRadius = new(18),
                Padding = new(30, 28, 30, 28),
                Margin = new(8),
                BorderBrush = Brush(primary ? "#55E4C084" : "#26FFFFFF"),
                BorderThickness = new(1),
            };
            var stack = new StackPanel();
            card.Child = stack;
            stack.Children.Add(Text(eyebrow, 11, "#E4C084", FontWeights.Bold));
            var heading = Text(title, 28, weight: FontWeights.Bold);
            heading.Margin = new(0, 8, 0, 6);
            stack.Children.Add(heading);
            stack.Children.Add(Body(text, 14));
            foreach (var p in points)
            {
                var line = Text("✓   " + p, 13.5, "#D9D4CC");
                line.Margin = new(0, 4, 0, 4);
                stack.Children.Add(line);
            }
            var row = new WrapPanel { Margin = new(0, 18, 0, 0) };
            row.Children.Add(BigButton(action, choose, primary));
            if (extra is { } e)
            {
                var more = ActionButton(e.Label, e.Act);
                more.Background = Brush("Transparent");
                more.Foreground = Brush("#E4C084");
                more.Padding = new(14, 10, 14, 10);
                more.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(more);
            }
            stack.Children.Add(row);
            return card;
        }
        grid.Children.Add(
            Card(
                T("TRACKER", "متتبع المشاهدة"),
                T("Just track what I watch", "تتبّع ما أشاهده فقط"),
                T(
                    "Keep every show and movie in one place, wherever you watch.",
                    "احتفظ بكل مسلسل وفيلم في مكان واحد، أينما شاهدته."
                ),
                [
                    "Check off episodes in one click",
                    "See what's next and when new episodes air",
                    "Alerts when a show you follow has a new episode",
                    "Stats, diary and ratings · syncs with your phone",
                ],
                "[[check]]   Start tracking",
                () => ChooseAppMode("Tracker"),
                true,
                ("[[swap]]  Import from TV Time, Trakt, Netflix…", () =>
                {
                    trackerTab = "Import";
                    ChooseAppMode("Tracker");
                })
            )
        );
        grid.Children.Add(
            Card(
                T("LIBRARY & TRACKER", "المكتبة والمتتبع"),
                T("Play & track", "شغّل وتتبّع"),
                "Point Noir at your movie and show folders. It plays them, remembers where you stopped and tracks everything automatically.",
                [
                    "Resume anywhere, skip intros, subtitles",
                    "Stream to your phone over home Wi-Fi",
                    "Download what's missing",
                    "Everything in the tracker, too",
                ],
                "[[play]]   Add my folders",
                () => ChooseAppMode("Library"),
                false
            )
        );
        ContentPanel.Children.Add(grid);
    }

    void RenderModeSettings()
    {
        ApplyAppMode();
        ContentPanel.Children.Add(
            Heading(
                "How you use Noir",
                "Tracker only hides the file library and opens on History. Your history, lists and folders are kept either way."
            )
        );
        ContentPanel.Children.Add(
            Chips(["Tracker only", "Library & tracker"], TrackerOnly ? "Tracker only" : "Library & tracker", label =>
            {
                store.State.AppMode = label == "Tracker only" ? "Tracker" : "Library";
                store.Save();
                ApplyAppMode();
                Render();
            })
        );
    }
}
