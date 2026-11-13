using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Noir;

// Home's "right now" strip, directly under the hero: one quiet tile per thing Noir is doing or has waiting for you.
// Noir is a downloader, an organizer, a streamer and a library, and before this strip Home only showed the library.
// A tile with nothing to say is not drawn (design rule 6), each tile opens the place where you act on it, and
// "Edit Home" sits at its end so the page's own settings are at the top, not under every row.
public partial class MainWindow
{
    int? tidyCount; // videos the organizer would move or rename; null until worked out (and again after a scan)
    bool tidyCounting;
    Action? tidyShow; // redraws the tidy tile of the strip that is on screen now

    sealed record StatusTile(Button Root, TextBlock Head, TextBlock Detail, System.Windows.Shapes.Path? Icon);

    static ControlTemplate StatusTileTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brush("#1FFFFFFF"));
        border.SetValue(Border.BackgroundProperty, Brush("#161618"));
        border.SetValue(Border.PaddingProperty, new Thickness(12, 9, 14, 9));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#59E4C084"), "Chrome"));
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#1C1C1F"), "Chrome"));
        template.Triggers.Add(hover);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#E4C084"), "Chrome"));
        template.Triggers.Add(focus);
        return template;
    }

    StatusTile NewStatusTile(string icon, string id, Action open)
    {
        var mark = IconSet.Draw(icon, 17) as System.Windows.Shapes.Path;
        if (mark != null)
            mark.Stroke = Brush("#E4C084");
        var badge = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new(9),
            Background = Brush("#1FE4C084"),
            Margin = new(0, 0, 11, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = mark,
        };
        var head = Text("", 13.5, weight: FontWeights.SemiBold);
        head.TextWrapping = TextWrapping.NoWrap;
        head.TextTrimming = TextTrimming.CharacterEllipsis;
        var detail = Text("", 11.5, "#9F9A93");
        detail.TextWrapping = TextWrapping.NoWrap;
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        detail.Margin = new(0, 2, 0, 0);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(head);
        words.Children.Add(detail);
        var row = new DockPanel();
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        row.Children.Add(words);
        var tile = new Button
        {
            Content = row,
            Template = StatusTileTemplate(),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new(0, 0, 10, 0),
            Tag = "HomeStatus:" + id,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        Motion.ButtonFeedback(tile);
        tile.Click += (_, _) => open();
        return new(tile, head, detail, mark);
    }

    // Marks around a left-to-right piece ("13 GB", "S01 · E02", "4.2 MB/s") inside a line that may be Arabic.
    const string Ltr = "\u202A",
        End = "\u202C";

    static string Unmarked(string text) => text.Replace(Ltr, "").Replace(End, "");

    // WPF does not reorder on the marks alone: in Arabic each marked piece becomes its own left-to-right run, so a
    // number keeps its unit and an episode tag keeps its order. English gets the plain text.
    internal static void SetMarked(TextBlock block, string text)
    {
        var pieces = text.Split('\u202A', '\u202C');
        if (!Loc.Arabic || pieces.Length < 3)
        {
            block.Text = string.Concat(pieces);
            return;
        }
        block.Inlines.Clear();
        for (var i = 0; i < pieces.Length; i++)
            if (pieces[i].Length > 0)
                block.Inlines.Add(
                    new System.Windows.Documents.Run(pieces[i])
                    {
                        FlowDirection = i % 2 == 1 ? FlowDirection.LeftToRight : FlowDirection.RightToLeft,
                    }
                );
    }

    static void Say(StatusTile tile, string head, string detail, bool warn = false)
    {
        SetMarked(tile.Head, head);
        SetMarked(tile.Detail, detail);
        tile.Detail.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        tile.Detail.Foreground = Brush(warn ? "#E9B872" : "#9F9A93");
        tile.Root.ToolTip = Unmarked(detail.Length > 0 ? head + " · " + detail : head);
        System.Windows.Automation.AutomationProperties.SetName(tile.Root, (string)tile.Root.ToolTip);
    }

    /// <summary>The strip for this render of Home, or null when there is nothing to show (a new, empty profile).</summary>
    FrameworkElement? HomeStatusStrip(List<List<VideoItem>> groups, IEnumerable<string> rowIds)
    {
        var tiles = new List<StatusTile>();

        // 1. Downloader: what is coming down right now, or what stopped and needs you.
        StatusTile? downloading = null;
        Action? refreshDownloads = null;
        if (downloads != null)
        {
            var manager = downloads;
            downloading = NewStatusTile("download", "downloads", () => Navigate("Downloads"));
            var downloadsChecked = false;
            refreshDownloads = () =>
            {
                // Jobs() copies the whole list: skip it on a tick when nothing is active and the tile is already hidden.
                if (manager.ActiveCount == 0 && downloadsChecked && downloading.Root.Visibility != Visibility.Visible)
                    return;
                downloadsChecked = true;
                var jobs = manager.JobCount > 0 ? manager.Jobs() : [];
                var now = jobs.Where(j => DownloadGroup(j.Status) == "now").ToList();
                var waiting = jobs.Count(j => j.Status == "Queued");
                var attention = jobs.Count(j => DownloadGroup(j.Status) == "attention");
                if (now.Count == 0 && waiting == 0 && attention == 0)
                {
                    downloading.Root.Visibility = Visibility.Collapsed;
                    return;
                }
                downloading.Root.Visibility = Visibility.Visible;
                if (now.Count == 0 && waiting == 0)
                {
                    Say(
                        downloading,
                        Loc.Count(
                            attention,
                            "download needs attention",
                            "downloads need attention",
                            "تنزيل واحد يحتاج اهتمامًا",
                            "تنزيلان يحتاجان اهتمامًا",
                            "تنزيلات تحتاج اهتمامًا",
                            "تنزيلًا يحتاج اهتمامًا"
                        ),
                        T("Open Downloads to retry or remove", "افتح التنزيلات لإعادة المحاولة أو الإزالة"),
                        true
                    );
                    return;
                }
                var first = now.FirstOrDefault();
                var single = now.Count == 1 && first != null;
                var head =
                    single ? first!.Target.Title
                    : now.Count > 1 ? T("{0} downloading", "{0} قيد التنزيل", now.Count)
                    : T("{0} waiting to download", "{0} بانتظار التنزيل", waiting);
                // Most useful first: the row is short and its end is trimmed.
                var parts = new List<string>();
                if (single)
                    parts.Add(
                        T("Downloading", "جارٍ التنزيل")
                            + (
                                first!.Target.IsMovie
                                    ? ""
                                    : " "
                                        + Ltr
                                        + "S"
                                        + first.Target.Season.ToString("00")
                                        + " · E"
                                        + first.Target.Episode.ToString("00")
                                        + End
                            )
                    );
                long size = now.Sum(j => Math.Max(0, j.Size)),
                    bytes = now.Sum(j => Math.Max(0, j.Bytes));
                if (size > 0)
                    parts.Add(Ltr + Math.Clamp((int)(bytes * 100 / size), 0, 100) + "%" + End);
                var speed = now.Sum(j => j.Speed);
                if (DownloadEta.Left(DownloadEta.Seconds(size, bytes, speed), Loc.Arabic) is { Length: > 0 } left)
                    parts.Add(left);
                if (attention > 0)
                    parts.Add(
                        attention == 1
                            ? T("1 needs attention", "1 يحتاج اهتمامًا")
                            : T("{0} need attention", "{0} تحتاج اهتمامًا", attention)
                    );
                if (now.Count > 0 && waiting > 0)
                    parts.Add(T("{0} waiting", "{0} بالانتظار", waiting));
                if (DownloadEta.Rate(speed) is { Length: > 0 } rate)
                    parts.Add(Ltr + rate + End);
                Say(downloading, head, string.Join(" · ", parts), attention > 0);
            };
            refreshDownloads();
            tiles.Add(downloading);
        }

        // 2. A website that stopped taking your watches (the same notice History shows).
        if (watchSync != null)
        {
            var notice = CommunityQueue
                .Notices(store.State, watchSync.Backoff, watchSync.Clock())
                .FirstOrDefault();
            if (notice != null)
            {
                var tile = NewStatusTile("sync", "sync", () =>
                {
                    settingsSection = "Connections";
                    Navigate("Settings");
                });
                if (tile.Icon != null)
                    tile.Icon.Stroke = Brush("#E9B872");
                var since = notice.LastSynced is { } at
                    ? T("since {0}", "منذ {0}", at.ToLocalTime().ToString("d MMM", Loc.Culture)) + " · "
                    : "";
                Say(
                    tile,
                    T("{0} is not syncing", "{0} لا تتم مزامنته", notice.Provider),
                    since + T("{0} waiting", "{0} في الانتظار", notice.Waiting),
                    true
                );
                tiles.Add(tile);
            }
        }

        // 3. Tracker: the next episode that airs for a show on Home.
        var known = groups.ToDictionary(g => g[0].GroupKey, g => g[0].Title);
        var soon = store
            .State.EpisodeSchedules.Where(g => known.ContainsKey(g.Key))
            .SelectMany(g =>
                g.Value.Where(e =>
                        e.Season > 0
                        && e.AirsAt > DateTimeOffset.UtcNow
                        && e.AirsAt < DateTimeOffset.UtcNow.AddDays(7)
                    )
                    .Select(e => (Title: known[g.Key], Episode: e, At: e.AirsAt!.Value.ToLocalTime()))
            )
            .OrderBy(x => x.At)
            .ToList();
        if (soon.Count > 0)
        {
            var next = soon[0];
            var today = soon.Count(x => x.At.Date == DateTime.Today);
            var tile = NewStatusTile("calendar", "upcoming", () => Navigate("Upcoming"));
            var day =
                next.At.Date == DateTime.Today ? T("tonight", "الليلة")
                : next.At.Date == DateTime.Today.AddDays(1) ? T("tomorrow", "غدًا")
                : next.At.ToString("dddd", Loc.Culture);
            Say(
                tile,
                today > 1
                    ? Loc.Count(
                        today,
                        "new episode tonight",
                        "new episodes tonight",
                        "حلقة جديدة الليلة",
                        "حلقتان جديدتان الليلة",
                        "حلقات جديدة الليلة",
                        "حلقة جديدة الليلة"
                    )
                    : T("{0} airs {1}", "{0} يُعرض {1}", next.Title, day),
                Ltr
                    + "S"
                    + next.Episode.Season.ToString("00")
                    + " · E"
                    + next.Episode.Episode.ToString("00")
                    + " · "
                    + next.At.ToString("HH:mm", Loc.Culture)
                    + End
                    + (soon.Count > 1 ? " · " + T("{0} more this week", "{0} أخرى هذا الأسبوع", soon.Count - 1) : "")
            );
            tiles.Add(tile);
        }

        // 4. Organizer: files that are not yet named and filed the Noir way (worked out off the UI thread, once).
        StatusTile? tidy = null;
        Func<Task>? countTidy = null;
        var manage = downloads?.ManagementPreferences;
        if (downloads != null && manage is { AutoOrganize: false })
        {
            tidy = NewStatusTile("folder", "tidy", OpenOrganizer);
            void ShowTidy()
            {
                tidy.Root.Visibility = tidyCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (tidyCount is > 0 and var n)
                    Say(
                        tidy,
                        Loc.Count(n, "file to tidy", "files to tidy", "ملف واحد للترتيب", "ملفان للترتيب", "ملفات للترتيب", "ملفًا للترتيب"),
                        T("Rename and sort into folders", "إعادة تسمية وفرز في مجلدات")
                    );
            }
            ShowTidy();
            tidyShow = ShowTidy;
            tiles.Add(tidy);
            if (tidyCount == null && !tidyCounting && !scanning)
            {
                var naming = manage.Naming;
                var owner = store;
                countTidy = async () =>
                {
                    if (tidyCount != null || tidyCounting)
                        return;
                    tidyCounting = true;
                    try
                    {
                        // After the page has drawn: planning reads every library path, which Home must not wait for.
                        await Task.Delay(validationMode ? 50 : 1500);
                        var moves = await Task.Run(() => owner.PlanOrganization(naming).Moves.Count(m => m.Video));
                        if (closing || owner != store)
                            return;
                        tidyCount = moves;
                        // Home may have been redrawn while this ran: update the strip that is showing now.
                        tidyShow?.Invoke();
                    }
                    catch (Exception e)
                    {
                        store.Log(e);
                    }
                    finally
                    {
                        tidyCounting = false;
                    }
                };
            }
        }

        // 5. Library on disk: how much room is left where your videos live, and the way to Free up space.
        var onPc = store.Archive.Count(i => i.Available);
        if (downloads != null && onPc > 0)
        {
            long free = -1;
            foreach (var root in store.State.Roots.Select(r => r.Path).Where(p => p.Length > 0))
                try
                {
                    var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
                    if (drive.IsReady && (free < 0 || drive.AvailableFreeSpace < free))
                        free = drive.AvailableFreeSpace;
                }
                catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { }
            if (free >= 0)
            {
                const long GB = 1024L * 1024 * 1024;
                var low = free < 10 * GB;
                var tile = NewStatusTile("drive", "storage", () =>
                {
                    downloadsTab = "On this PC";
                    Navigate("Downloads");
                    if (low)
                        ShowDownloadFreeSpace();
                });
                if (low && tile.Icon != null)
                    tile.Icon.Stroke = Brush("#E9B872");
                Say(
                    tile,
                    T(
                        "{0} free",
                        "{0} متاحة",
                        Ltr + (free >= GB ? free / GB + " GB" : DownloadNaming.Size(free)) + End
                    ),
                    low
                        ? T("Running low · free up space", "المساحة قليلة · حرّر مساحة")
                        : Loc.Count(onPc, "video on this PC", "videos on this PC", "فيديو واحد على هذا الكمبيوتر", "فيديوان على هذا الكمبيوتر", "فيديوهات على هذا الكمبيوتر", "فيديو على هذا الكمبيوتر"),
                    low
                );
                tiles.Add(tile);
            }
        }

        if (tiles.Count == 0)
            return null;

        // Tiles share the row equally up to a comfortable width; the rest of the row stays empty rather than stretching them.
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var strip = new DockPanel
        {
            Margin = new(0, Density.Pick(14, 20), 0, Density.Pick(2, 6)),
            Tag = "HomeStatusStrip",
        };
        void Layout()
        {
            var visible = tiles.Where(t => t.Root.Visibility == Visibility.Visible).ToList();
            var room =
                (
                    strip.ActualWidth > 0 ? strip.ActualWidth
                    : ContentScroll.ViewportWidth > 0 ? ContentScroll.ViewportWidth - 2 * sidePad
                    : 1200
                ) - 150; // "Edit Home" at the end of the row
            var max = Math.Clamp((int)(room / 225), 1, 5);
            var take = visible.Take(max).ToList();
            var width = take.Count == 0 ? 0 : Math.Clamp(Math.Floor(room / take.Count) - 10, 150, 340);
            if (row.Children.Count != take.Count || !take.Select(t => t.Root).SequenceEqual(row.Children.OfType<Button>()))
            {
                row.Children.Clear();
                foreach (var tile in take)
                    row.Children.Add(tile.Root);
            }
            foreach (var tile in take)
                tile.Root.Width = width;
        }
        Layout();
        // A tile that appears or goes while Home is open (a download starts or ends, the tidy count arrives).
        var visibility = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            VisibilityProperty,
            typeof(Button)
        );
        void Changed(object? s, EventArgs e) => Dispatcher.BeginInvoke(Layout, DispatcherPriority.Background);
        var changing = new[] { downloading, tidy }.OfType<StatusTile>().ToList();

        var edit = ActionButton("[[sliders]]  " + T("Edit Home", "تعديل الرئيسية"), () => HomeLayoutEditor(rowIds));
        edit.Background = Brush("Transparent");
        edit.Foreground = Brush("#C7C2BC");
        edit.VerticalAlignment = VerticalAlignment.Center;
        edit.ToolTip = T("Show, hide and reorder the rows on Home.", "أظهر الصفوف أو أخفِها أو غيّر ترتيبها في الرئيسية.");
        System.Windows.Automation.AutomationProperties.SetName(edit, "Edit Home");
        DockPanel.SetDock(edit, Dock.Right);
        strip.Children.Add(edit);
        strip.Children.Add(row);
        strip.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
                Layout();
        };
        // Watched only while the strip is on screen, so a strip that was replaced leaves nothing behind.
        strip.Loaded += (_, _) =>
        {
            foreach (var tile in changing)
            {
                visibility.RemoveValueChanged(tile.Root, Changed);
                visibility.AddValueChanged(tile.Root, Changed);
            }
            Layout();
            _ = countTidy?.Invoke();
        };
        strip.Unloaded += (_, _) =>
        {
            foreach (var tile in changing)
                visibility.RemoveValueChanged(tile.Root, Changed);
        };

        // Downloads move while you look at Home: refresh that one tile every two seconds while the strip is on screen.
        if (refreshDownloads != null)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => refreshDownloads();
            strip.Loaded += (_, _) =>
            {
                if (!validationMode)
                    timer.Start();
            };
            strip.Unloaded += (_, _) => timer.Stop();
        }
        return strip;
    }
}
