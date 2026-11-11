using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Noir;

// The side rail: every page with an icon and label, a profile card (the language switch lives in its menu).
// Narrow windows (or the toggle) fold it to icons only; Arabic puts it on the right.
public partial class MainWindow
{
    bool railCompact;
    bool? railChoice;
    const string HomeGlyph = "M3,11 L12,3 L21,11 M5,9.5 L5,21 L10,21 L10,15 L14,15 L14,21 L19,21 L19,9.5",
        DiscoverGlyph = "M12,2 A10,10 0 1 1 11.99,2 M15.5,8.5 L13.5,13.5 L8.5,15.5 L10.5,10.5 Z",
        ShowsGlyph = "M2,7 L22,7 L22,20 L2,20 Z M8,2 L12,6 L16,2",
        MoviesGlyph =
            "M3,3 L21,3 L21,21 L3,21 Z M7,3 L7,21 M17,3 L17,21 M3,8 L7,8 M3,12 L7,12 M3,16 L7,16 M17,8 L21,8 M17,12 L21,12 M17,16 L21,16",
        ListGlyph = "M6,3 L18,3 L18,21 L12,16.5 L6,21 Z",
        HistoryGlyph = "M3.5,12 A8.5,8.5 0 1 0 6,6 M3,2.5 L3,7.5 L8,7.5 M12,7 L12,12 L15.5,14",
        CalendarGlyph = "M3,5 L21,5 L21,21 L3,21 Z M3,10 L21,10 M8,2 L8,7 M16,2 L16,7",
        MenuGlyph = "M3,6 L21,6 M3,12 L21,12 M3,18 L21,18";
    readonly Dictionary<Button, (string Label, TextBlock Text, Border Icon)> navItems = new();
    (Button Button, string Label, string Glyph)[] NavPages =>
        [
            (HomeNav, "Home", HomeGlyph),
            (DiscoverNav, "Discover", DiscoverGlyph),
            (ShowNav, "Shows", ShowsGlyph),
            (MovieNav, "Movies", MoviesGlyph),
            (WatchlistNav, "My List", ListGlyph),
            (HistoryNav, "History", HistoryGlyph),
            (UpcomingNav, "Upcoming", CalendarGlyph),
            (DownloadsNav, "Downloads", DownloadGlyph),
            (SettingsNav, "Settings", SettingsGlyph),
        ];

    void BuildSideRail()
    {
        navItems.Clear();
        foreach (var (button, label, glyph) in NavPages)
        {
            var icon = new Border
            {
                Width = 26,
                Height = 24,
                Child = Icon(glyph, 19),
            };
            var text = new TextBlock
            {
                Text = T(label),
                FontSize = Density.NavFont,
                Margin = new(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(icon);
            row.Children.Add(text);
            button.Content = row;
            navItems[button] = (label, text, icon);
            System.Windows.Automation.AutomationProperties.SetName(button, T(label));
        }
        RailToggle.Content = Icon(MenuGlyph, 18);
        ScanNav.Content = Icon(ScanGlyph, 18);
        BuildRailFooter();
        ApplyRailWidth();
        UpdateNavSelection();
    }

    void SetNavIcon(Button button, FrameworkElement icon)
    {
        if (navItems.TryGetValue(button, out var item))
            item.Icon.Child = icon;
    }

    void UpdateNavSelection()
    {
        var current =
            page == "History" && trackerTab == "Upcoming"
                ? UpcomingNav
                : page switch
                {
                    "Home" => HomeNav,
                    "Shows" => ShowNav,
                    "Movies" => MovieNav,
                    "My List" => WatchlistNav,
                    "History" => HistoryNav,
                    "Discover" => DiscoverNav,
                    "Downloads" => DownloadsNav,
                    "Settings" => SettingsNav,
                    _ => null,
                };
        foreach (var (button, item) in navItems)
        {
            var on = button == current;
            button.Tag = on ? "Selected" : null;
            item.Text.Foreground = Brush(on ? "#F4F1EB" : "#B8B2AA");
            item.Text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            foreach (var path in ViewElements(item.Icon).OfType<Path>())
                path.Stroke = Brush(on ? "#F4F1EB" : "#B8B2AA");
        }
    }

    void Upcoming_Click(object sender, RoutedEventArgs e)
    {
        historyType = "All";
        trackerTab = "Upcoming";
        Navigate("History");
    }

    void RailToggle_Click(object sender, RoutedEventArgs e)
    {
        railChoice = !railCompact;
        ApplyRailWidth();
    }

    // Icons only below 1100 px unless the person chose otherwise with the toggle or in Settings.
    void ApplyRailWidth()
    {
        railCompact = railChoice
            ?? store.State.RailMode switch
            {
                "Labels" => false,
                "Icons" => true,
                _ => ActualWidth < 1100,
            };
        RailColumn.Width = new GridLength(railCompact ? 76 : Density.Sidebar);
        RailLogo.Text = railCompact ? "N" : "N O I R";
        foreach (var (button, item) in navItems)
        {
            item.Text.Visibility = railCompact ? Visibility.Collapsed : Visibility.Visible;
            button.HorizontalContentAlignment = railCompact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            button.ToolTip = railCompact ? T(item.Label) : null;
        }
        NavDivider.Margin = new(railCompact ? 8 : 12, 12, railCompact ? 8 : 12, 12);
        BuildRailFooter();
    }

    void BuildRailFooter()
    {
        RailFooter.Children.Clear();
        // Profile: who this library belongs to and how much it holds; opens your stats.
        var name = ProfileName;
        var avatar = Profiles.Avatar(DisplayProfile, 38);
        var titles = store.Archive.Select(i => i.GroupKey).Distinct().Count();
        var card = new Button
        {
            Background = Brush("#0FFFFFFF"),
            Padding = new(railCompact ? 0 : 10, 8, 10, 8),
            HorizontalContentAlignment = railCompact ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            ToolTip = name + " · " + T("Switch profile"),
        };
        if (railCompact)
            card.Content = avatar;
        else
        {
            var row = new DockPanel();
            row.Children.Add(avatar);
            DockPanel.SetDock(avatar, Dock.Left);
            var lines = new StackPanel { Margin = new(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var who = Text(name, 13, weight: FontWeights.SemiBold);
            who.TextTrimming = TextTrimming.CharacterEllipsis;
            who.TextWrapping = TextWrapping.NoWrap;
            lines.Children.Add(who);
            var what = Text((TrackerOnly ? T("Tracker") : T("Your library")) + " · " + titles, 11, "#9F9A93");
            what.TextWrapping = TextWrapping.NoWrap;
            what.TextTrimming = TextTrimming.CharacterEllipsis;
            lines.Children.Add(what);
            row.Children.Add(lines);
            card.Content = row;
        }
        System.Windows.Automation.AutomationProperties.SetName(card, "Profile " + name);
        card.Click += (_, _) => ShowProfileSwitcher();
        RailFooter.Children.Add(card);
    }
}
