#if NOIR_VALIDATE
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Drawing = System.Drawing;

namespace Noir;

static class ReadmeMockBootstrap
{
    internal static string? DirectoryPrepared;
    internal static string? Error;

    // Must run before LibraryStore's constructor and initial ScanLibrary. A missing library.json
    // normally enables discovery of the owner's Videos folders, even with --data-dir.
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Prepare()
    {
        var args = Environment.GetCommandLineArgs();
        if (!args.Contains("--validate") || !args.Contains("--readme-shots") || !args.Contains("--readme-mock"))
            return;
        var index = Array.IndexOf(args, "--data-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Error = "--readme-mock requires an explicit --data-dir.";
            return;
        }
        try
        {
            var directory = Path.GetFullPath(args[index + 1]);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Error =
                    "--readme-mock requires a fresh empty directory. Use --readme-shots alone to recapture a seeded mock.";
                return;
            }
            Directory.CreateDirectory(directory);
            // Serialize the app's own state model; no independent seed storage format.
            File.WriteAllText(
                Path.Combine(directory, "library.json"),
                System.Text.Json.JsonSerializer.Serialize(
                    new PlayerState
                    {
                        OnlineMetadata = true,
                        Language = "en",
                        AppMode = "Library",
                        RunInBackground = false,
                        AutoBackup = false,
                        BackupFolder = Path.Combine(directory, "backups"),
                    }
                )
            );
            DirectoryPrepared = directory;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Error = error.Message;
        }
    }
}

public partial class MainWindow
{
    // Kept in this validation-only type so production activation is unaffected.
    static class ReadmeDesktop
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct PixelPoint
        {
            public int X,
                Y;
        }

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        internal static extern bool AttachThreadInput(uint first, uint second, bool attach);

        [DllImport("user32.dll")]
        internal static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPoint(PixelPoint point);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
    }

    async Task SeedReadmeMock(Action<string> note)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--data-dir");
        if (ReadmeMockBootstrap.Error != null)
            throw new InvalidOperationException(ReadmeMockBootstrap.Error);
        if (
            !validationMode
            || index < 0
            || index + 1 >= args.Length
            || ReadmeMockBootstrap.DirectoryPrepared != Path.GetFullPath(store.DataDirectory)
            || !Path.GetFullPath(args[index + 1])
                .Equals(Path.GetFullPath(store.DataDirectory), StringComparison.OrdinalIgnoreCase)
            || store.Archive.Count > 0
            || store.State.Roots.Count > 0
            || store.State.Viewings.Count > 0
            || store.State.Progress.Count > 0
            || store.State.Watchlist.Count > 0
        )
            throw new InvalidOperationException(
                "--readme-mock requires --validate and an explicitly supplied empty data directory; existing libraries are never cleared."
            );
        var profiles = Profiles.Load(store.DataDirectory);
        profiles.Profiles.First(p => p.Id == "default").Name = "Sam";
        profiles.AskOnStart = false;
        Profiles.Save(profiles, store.DataDirectory);
        profileList = null;
        SetLanguage("en");
        HistoryPrefs.WeeklyGoal = 7;
        HistoryPrefs.Save(store.DataDirectory);
        note(
            $"Mock seeding started: profile={ProfileName}; empty catalog={store.Archive.Count}; scan roots={store.State.Roots.Count}; source=live Cinemeta; target=14 series + 12 movies."
        );
        using var client = DiscoverClient();
        using var tokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var token = tokenSource.Token;
        var catalogs = await Task.WhenAll(
            client.Top("series", 0, token),
            client.Top("movie", 0, token),
            client.Genre("series", "Family", token),
            client.Genre("movie", "Animation", token)
        );
        var candidates = catalogs.SelectMany(c => c).DistinctBy(t => (t.Type, t.Id)).ToDictionary(t => (t.Type, t.Id));
        note(
            $"Mock Cinemeta catalogs: series Top={catalogs[0].Count}; movie Top={catalogs[1].Count}; family series={catalogs[2].Count}; animation movies={catalogs[3].Count}."
        );
        // Curated identities only: every name, schedule and piece of artwork is resolved online.
        // First six shows are ongoing, next four finished, last four saved for later.
        string[] series =
        [
            "tt4574334",
            "tt8111088",
            "tt12324366",
            "tt11126994",
            "tt0436992",
            "tt13623580",
            "tt0417299",
            "tt1865718",
            "tt3718778",
            "tt5491994",
            "tt7678620",
            "tt1305826",
            "tt0458290",
            "tt5421602",
        ];
        string[] movies =
        [
            "tt0245429",
            "tt0892769",
            "tt0317705",
            "tt0382932",
            "tt0910970",
            "tt2380307",
            "tt2096673",
            "tt4468740",
            "tt4633694",
            "tt0816692",
            "tt1049413",
            "tt29623480",
        ];
        var seeded = new List<(DiscoverTitle Title, List<VideoItem> Items, int Order)>();
        foreach (var type in new[] { "series", "movie" })
        {
            var ids = type == "series" ? series : movies;
            for (var n = 0; n < ids.Length; n++)
            {
                var identity = candidates.GetValueOrDefault((type, ids[n]))
                    ?? new DiscoverTitle { Id = ids[n], Type = type };
                var title = await client.Details(identity, token);
                if (title.Name.Length == 0 || !File.Exists(title.Poster) || !File.Exists(title.Background))
                    throw new IOException(
                        $"Mock title {ids[n]} lacks its Cinemeta name, poster or backdrop; retry with a working artwork connection."
                    );
                if (!title.IsMovie && !title.Episodes.Any(e => e.AirsAt is { } at && at <= DateTimeOffset.UtcNow))
                    throw new IOException("No dated, released episodes for " + title.Name);
                var group = store.AddDiscovered(title, n >= 10);
                store.SetInLibrary(group[0].GroupKey, true);
                // Use the same title-extras loader as the normal library hero, including logo validation.
                ExtraFor(group[0]);
                seeded.Add((title, group, n));
                note(
                    $"Mock metadata {seeded.Count}/26: {title.Name} ({title.Id}); poster=True; backdrop=True; episodes={title.Episodes.Count}."
                );
            }
        }
        var extraDeadline = DateTime.UtcNow.AddSeconds(60);
        while (extrasPending.Count > 0 && DateTime.UtcNow < extraDeadline)
            await Task.Delay(200);
        extrasRender?.Stop();
        SaveExtras();
        var now = DateTimeOffset.UtcNow;
        var viewings = 0;
        var partials = 0;
        using (store.Batch())
            foreach (var entry in seeded)
            {
                var (title, group, n) = entry;
                var key = group[0].GroupKey;
                var minutes = System.Text.RegularExpressions.Regex.Match(
                    titleExtras?.GetValueOrDefault(title.Id)?.Runtime ?? "",
                    @"\d+"
                );
                var runtime =
                    int.TryParse(minutes.Value, out var mins) && mins > 0 ? mins
                    : title.IsMovie ? 105
                    : 30;
                if (!title.IsMovie)
                    store.State.ShowRuntimes[key] = runtime;
                var released = group
                    .Where(i =>
                        title.IsMovie
                        || title.Episodes.Any(e =>
                            e.Season == i.Season && e.Episode == i.Episode && e.AirsAt is { } at && at <= now
                        )
                    )
                    .ToList();
                var count =
                    title.IsMovie ? (n < 10 ? 1 : 0)
                    : n < 6 ? Math.Min(released.Count - 1, n % 2 == 0 ? released.Count(i => i.Season == 1) : 3 + n)
                    : n < 10 ? released.Count
                    : 0;
                count = Math.Max(0, count);
                for (var e = 0; e < count; e++)
                {
                    var item = released[e];
                    item.Duration = runtime * 60;
                    var at =
                        title.IsMovie ? now.AddDays(-12 - n * 11)
                        : n < 6 ? now.AddDays(-(count - 1 - e) * 1.5 - n * .6).AddHours(-2)
                        : now.AddDays(-50 - (count - 1 - e) * 2 - (n - 6) * 9);
                    var aired = title
                        .Episodes.FirstOrDefault(ep => ep.Season == item.Season && ep.Episode == item.Episode)
                        ?.AirsAt;
                    if (aired is { } release && at < release)
                        at = release; // Never log an episode before release.
                    viewings += store.LogWatched([new(item, at)], "Logged", queueSync: false);
                    if (!title.IsMovie && e == count - 1 && n < 4)
                        store.SetReaction(item.Id, new[] { "loved", "wow", "funny", "loved" }[n]);
                }
                if (!title.IsMovie && n < 6)
                {
                    store.RateShow(key, n % 3 == 0 ? 5 : 4);
                    if (n < 3 && count < released.Count)
                    {
                        var next = released[count];
                        store.Record(next, (9 + n * 6) * 60, runtime * 60);
                        store.GetProgress(next)!.UpdatedAt = now.AddHours(-3 - n * 19);
                        partials++;
                    }
                }
                if (!title.IsMovie && n is >= 6 and < 10)
                {
                    store.State.ShowStates[key] = "Ended";
                    store.RateShow(key, 5);
                }
                if (title.IsMovie && n < 10)
                    store.SetReview10(
                        group[0],
                        new[] { 9, 8, 9, 10, 9, 9, 8, 9, 10, 9 }[n],
                        n switch
                        {
                            0 => "Beautiful world, even better on a rewatch.",
                            3 => "A comfort watch. The kitchen scenes are still my favourite.",
                            8 => "Loved the animation and the soundtrack.",
                            _ => "",
                        }
                    );
            }
        var liked = seeded
            .Where(e => !e.Title.IsMovie && e.Order is 0 or 1 or 6 || e.Title.IsMovie && e.Order is 3 or 5 or 8)
            .ToList();
        foreach (var entry in liked)
            store.SetLike(entry.Items[0].GroupKey, true);
        store.SetFavorites(
            new[] { series[6], movies[3], movies[8], series[1] }.Select(id =>
                seeded.First(e => e.Title.Id == id).Items[0].GroupKey
            )
        );
        store.Save();
        // Warm the normal episode-art cache, retaining local image paths in the existing schedule model.
        using var artwork = new MetadataClient(store.DataDirectory);
        var stills = seeded
            .Where(e => !e.Title.IsMovie && e.Order < 10)
            .SelectMany(e =>
                e.Items.Where(store.EverWatched).TakeLast(1).Concat(e.Items.Where(i => !store.EverWatched(i)).Take(1))
            )
            .Where(i => i.EpisodeImage.Length > 0)
            .ToList();
        var stillCount = 0;
        foreach (var item in stills)
        {
            try
            {
                var path = await artwork.EpisodeArt(item.EpisodeImage, token);
                if (await LoadImage(path, 900) != null)
                {
                    item.EpisodeImage = path;
                    if (store.State.Catalog.TryGetValue(item.Id, out var saved))
                        saved.Item.EpisodeImage = path;
                    var episode = store
                        .State.EpisodeSchedules.GetValueOrDefault(item.GroupKey)
                        ?.FirstOrDefault(e => e.Season == item.Season && e.Episode == item.Episode);
                    if (episode != null)
                        episode.Image = path;
                    stillCount++;
                }
            }
            catch (Exception error)
                when (error is System.Net.Http.HttpRequestException or IOException or TaskCanceledException)
            {
                note("WARNING: episode still " + item.Title + ": " + error.Message);
            }
        }
        var logos = seeded.Count(e => File.Exists(titleExtras?.GetValueOrDefault(e.Title.Id)?.Logo));
        store.Save();
        note("Seeded titles: " + string.Join(" / ", seeded.Select(e => e.Title.Name + " [" + e.Title.Id + "]")));
        note(
            $"Mock complete: profile={ProfileName}; series=14 (6 in progress, 4 finished, 4 not started); movies=12 (10 watched, 2 not started); viewings={viewings}; partial bookmarks={partials}; likes={store.State.Likes.Count}; Top four={store.State.Favorites.Count}; My List={store.State.Watchlist.Count}; posters=26/26; backdrops=26/26; logos={logos}/26; decoded episode stills={stillCount}/{stills.Count}; extras pending={extrasPending.Count}."
        );
        if (logos < 26)
            note("WARNING: some Cinemeta logos unavailable; those heroes use the app's normal title-text fallback.");
    }

    // This mode deliberately uses desktop pixels: RenderTargetBitmap mirrors some RTL runs.
    async Task CaptureReadmeShots()
    {
        var directory = Path.Combine(store.DataDirectory, "readme-shots");
        Directory.CreateDirectory(directory);
        var log = new List<string>
        {
            "README screen captures · " + DateTimeOffset.Now.ToString("O"),
            "Profile: " + store.DataDirectory,
            "Target: 1440 x 900 physical client pixels; Graphics.CopyFromScreen; no bitmap resampling.",
        };
        var oldAfterWatching = HistoryPrefs.AfterWatching;
        var oldQuiet = HistoryPrefs.JustWatchedQuiet;
        var wasTopmost = Topmost;
        var mock = Environment.GetCommandLineArgs().Contains("--readme-mock");
        var mockSeeded = false;
        void Note(string message)
        {
            log.Add(message);
            File.WriteAllLines(Path.Combine(directory, "shots.txt"), log);
        }
        async Task Settle(int milliseconds = 500)
        {
            UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(milliseconds);
            UpdateLayout();
        }
        Rect ScreenBounds(FrameworkElement element)
        {
            // PointToScreen includes device scale AND RTL transforms. Normalize the corners.
            var a = element.PointToScreen(new Point());
            var b = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
            return new Rect(
                new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
                new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y))
            );
        }
        bool Visible(FrameworkElement element, bool belowBar = true)
        {
            if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
                return false;
            try
            {
                var bounds = ScreenBounds(element);
                var viewport = ScreenBounds(ContentScroll);
                if (belowBar)
                    viewport.Y += TopBar.ActualHeight * VisualTreeHelper.GetDpi(this).DpiScaleY;
                viewport.Intersect(ScreenBounds(LibraryRoot));
                bounds.Intersect(viewport);
                // Account for the horizontal rail's clipped cards, too.
                for (
                    DependencyObject? parent = VisualTreeHelper.GetParent(element);
                    parent != null && parent != LibraryRoot;
                    parent = VisualTreeHelper.GetParent(parent)
                )
                    if (parent is ScrollViewer scroll)
                        bounds.Intersect(ScreenBounds(scroll));
                return !bounds.IsEmpty && bounds.Width > 2 && bounds.Height > 2;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
        List<FrameworkElement> Rails() =>
            ContentPanel
                .Children.OfType<FrameworkElement>()
                .Where(section =>
                    Descendants(section)
                        .OfType<ScrollViewer>()
                        .Any(s => s.Content is StackPanel { Orientation: Orientation.Horizontal })
                )
                .ToList();
        List<Image> RailImages(bool visible) =>
            Rails().SelectMany(r => Descendants(r).OfType<Image>()).Where(i => !visible || Visible(i)).ToList();
        List<FrameworkElement> RailCards(bool visible) =>
            Rails()
                .SelectMany(r => Descendants(r).OfType<ScrollViewer>())
                .Where(s => s.Content is StackPanel { Orientation: Orientation.Horizontal })
                .SelectMany(s => ((StackPanel)s.Content).Children.OfType<FrameworkElement>())
                .Where(c => !Equals(c.Tag, "HomeSeeAllTile") && (!visible || Visible(c)))
                .ToList();
        TextBlock? SectionHeader(FrameworkElement section)
        {
            var texts = Descendants(section).OfType<TextBlock>().ToList();
            // Rail's DockPanel adds its See all / arrow buttons before its actual heading.
            return texts.FirstOrDefault(t => Math.Abs(t.FontSize - 21) < .1 && t.FontWeight == FontWeights.SemiBold)
                ?? texts.FirstOrDefault();
        }
        async Task Artwork()
        {
            var watch = Stopwatch.StartNew();
            string previous = "";
            var stable = 0;
            // Missing posters are placeholder Grids, not Images; count cards as well as Sources.
            while (watch.Elapsed < TimeSpan.FromSeconds(25))
            {
                await Settle(250);
                var cards = RailCards(true);
                var ready = cards.Count(c => Descendants(c).OfType<Image>().Any(i => i.Source != null));
                var visible = Descendants(LibraryRoot).OfType<Image>().Where(i => Visible(i, false)).ToList();
                var state =
                    $"{ready}/{cards.Count}/{visible.Count(i => i.Source != null)}/{visible.Count}/{extrasPending.Count}/{imageLoads.Count}";
                stable = state == previous ? stable + 1 : 0;
                previous = state;
                if (
                    watch.Elapsed >= TimeSpan.FromSeconds(5)
                    && ready == cards.Count
                    && visible.All(i => i.Source != null)
                    && extrasPending.Count == 0
                    && imageLoads.Count == 0
                    && stable >= 4
                )
                    break;
            }
            await Settle(350); // Finish image fade-ins after the last decode.
        }
        IntPtr WindowHandle() => new System.Windows.Interop.WindowInteropHelper(this).Handle;
        bool Foreground() => ReadmeDesktop.GetForegroundWindow() == WindowHandle();
        bool Uncovered()
        {
            var bounds = ScreenBounds(LibraryRoot);
            var handle = WindowHandle();
            // Sample a grid including the edges, hero, rails and corner card area.
            foreach (var fx in new[] { .01, .15, .35, .5, .65, .85, .99 })
            foreach (var fy in new[] { .01, .15, .35, .5, .65, .85, .99 })
            {
                var hit = ReadmeDesktop.WindowFromPoint(
                    new() { X = (int)(bounds.Left + bounds.Width * fx), Y = (int)(bounds.Top + bounds.Height * fy) }
                );
                if (hit != handle && ReadmeDesktop.GetAncestor(hit, 2) != handle)
                    return false;
            }
            return true;
        }
        async Task ActivateCaptureWindow()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                ShowActivated = true;
                WindowState = WindowState.Normal;
                Topmost = true;
                Show();
                Activate();
                var foreground = ReadmeDesktop.GetForegroundWindow();
                var thread = ReadmeDesktop.GetCurrentThreadId();
                var other = ReadmeDesktop.GetWindowThreadProcessId(foreground, out _);
                var attached = false;
                try
                {
                    if (other != 0 && other != thread)
                        attached = ReadmeDesktop.AttachThreadInput(thread, other, true);
                    // Windows permits foreground activation after an ALT press. Don't release a user's held ALT.
                    if ((ReadmeDesktop.GetAsyncKeyState(0x12) & 0x8000) == 0)
                    {
                        ReadmeDesktop.keybd_event(0x12, 0, 0, UIntPtr.Zero);
                        ReadmeDesktop.keybd_event(0x12, 0, 2, UIntPtr.Zero);
                    }
                    ReadmeDesktop.SetForegroundWindow(WindowHandle());
                    Activate();
                }
                finally
                {
                    if (attached)
                        ReadmeDesktop.AttachThreadInput(thread, other, false);
                }
                await Settle(200 + attempt * 100);
                if (Foreground())
                    return;
            }
        }
        TextBlock? Header(string title) =>
            Rails().Select(SectionHeader).FirstOrDefault(t => t?.Text == title)
            ?? Descendants(ContentPanel).OfType<TextBlock>().FirstOrDefault(t => t.Text == title);
        async Task AtHeader(string title)
        {
            await Settle(100);
            var header = Header(title);
            if (header == null)
            {
                Note("WARNING: missing requested header: " + title);
                ContentScroll.ScrollToTop();
            }
            else
                ContentScroll.ScrollToVerticalOffset(
                    Math.Max(
                        0,
                        header.TransformToAncestor(PageBody).Transform(new Point()).Y - TopBar.ActualHeight - 16
                    )
                );
            await Settle();
        }
        async Task PrepareScreen()
        {
            ShowActivated = true;
            WindowState = WindowState.Normal;
            Topmost = true;
            Show();
            Activate();
            var display = System.Windows.Forms.Screen.FromHandle(
                new System.Windows.Interop.WindowInteropHelper(this).Handle
            );
            // Start at the requested size, then compensate for DPI and non-client chrome.
            var dpi = VisualTreeHelper.GetDpi(this);
            Width = 1440 / dpi.DpiScaleX;
            Height = 900 / dpi.DpiScaleY;
            await Settle();
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var bounds = ScreenBounds(LibraryRoot);
                dpi = VisualTreeHelper.GetDpi(this);
                if (Math.Abs(bounds.Width - 1440) < .5 && Math.Abs(bounds.Height - 900) < .5)
                    break;
                Width += (1440 - bounds.Width) / dpi.DpiScaleX;
                Height += (900 - bounds.Height) / dpi.DpiScaleY;
                await Settle(250);
            }
            dpi = VisualTreeHelper.GetDpi(this);
            Left =
                (display.WorkingArea.Left + Math.Max(0, (display.WorkingArea.Width - ActualWidth * dpi.DpiScaleX) / 2))
                / dpi.DpiScaleX;
            Top =
                (display.WorkingArea.Top + Math.Max(0, (display.WorkingArea.Height - ActualHeight * dpi.DpiScaleY) / 2))
                / dpi.DpiScaleY;
            await Settle(700);
            Note(
                $"Desktop: {display.Bounds}; DPI {dpi.DpiScaleX:0.##} x {dpi.DpiScaleY:0.##}; client {ScreenBounds(LibraryRoot)}; active={IsActive}."
            );
        }
        async Task Shot(string name, string requested)
        {
            try
            {
                CloseHoverCard();
                await Artwork();
                await ActivateCaptureWindow();
                var images = RailImages(false);
                var visibleImages = RailImages(true);
                var cards = RailCards(true);
                var headers = ContentPanel
                    .Children.OfType<FrameworkElement>()
                    .SelectMany(section =>
                    {
                        var texts = Descendants(section).OfType<TextBlock>().ToList();
                        return new[] { SectionHeader(section) }
                            .OfType<TextBlock>()
                            .Concat(texts.Where(t => t.Text == T("Top four", "أفضل أربعة")));
                    })
                    .Concat(new[] { PageTitle })
                    .Where(t => Visible(t))
                    .Select(t => t.Text)
                    .Distinct();
                var visibleRows = Rails().Where(r => Visible(r)).Select(r => SectionHeader(r)?.Text ?? "(untitled)");
                // Write diagnostics before touching the desktop DC, which a sandbox may deny.
                Note(
                    $"{name}.png | requested: {requested} | visible headers: {string.Join(" / ", headers)} | visible rows (may be partial): {string.Join(" / ", visibleRows)} | rail Images Source: {images.Count(i => i.Source != null)}/{images.Count}; visible {visibleImages.Count(i => i.Source != null)}/{visibleImages.Count}; visible cards with artwork {cards.Count(c => Descendants(c).OfType<Image>().Any(i => i.Source != null))}/{cards.Count} | offset={ContentScroll.VerticalOffset:0}; hero={homeHeroTitle}; corner card={justWatchedCard != null}"
                );
                if (cards.Count > 0 && cards.Any(c => !Descendants(c).OfType<Image>().Any(i => i.Source != null)))
                    Note("WARNING: " + name + " has visible cards without loaded artwork.");
                var allVisible = Descendants(LibraryRoot).OfType<Image>().Where(i => Visible(i, false)).ToList();
                Note(
                    $"{name} artwork: all visible Images (hero/logo/stills included)={allVisible.Count(i => i.Source != null)}/{allVisible.Count}; image loads pending={imageLoads.Count}; extras pending={extrasPending.Count}; foreground={Foreground()}; active={IsActive}; uncovered samples={Uncovered()}."
                );
                if (allVisible.Any(i => i.Source == null))
                    Note("WARNING: " + name + " has visible images still missing after the artwork wait.");
                var bounds = ScreenBounds(LibraryRoot);
                var x = (int)Math.Round(bounds.Left);
                var y = (int)Math.Round(bounds.Top);
                var width = (int)Math.Round(bounds.Width);
                var height = (int)Math.Round(bounds.Height);
                if (width != 1440 || height != 900)
                    throw new InvalidOperationException($"Client is {width} x {height}, expected 1440 x 900.");
                var display = System.Windows.Forms.Screen.FromHandle(
                    new System.Windows.Interop.WindowInteropHelper(this).Handle
                );
                if (!display.Bounds.Contains(new Drawing.Rectangle(x, y, width, height)))
                    throw new InvalidOperationException(
                        "Client is partly off screen; a desktop of at least 1440 x 900 plus window chrome is required."
                    );
                if (!Foreground())
                {
                    if (!Uncovered())
                        throw new InvalidOperationException(
                            "Window could not be activated and client-area samples are covered; refusing an obscured capture."
                        );
                    Note(
                        name
                            + ": foreground unavailable, but all 49 WindowFromPoint samples belong to Noir; capturing unobscured client."
                    );
                }
                using (var bitmap = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Drawing.Graphics.FromImage(bitmap))
                        graphics.CopyFromScreen(
                            x,
                            y,
                            0,
                            0,
                            new Drawing.Size(width, height),
                            Drawing.CopyPixelOperation.SourceCopy
                        );
                    bitmap.Save(Path.Combine(directory, name + ".png"), Drawing.Imaging.ImageFormat.Png);
                }
                Note("SAVED: " + name + ".png (1440 x 900 screen pixels)");
            }
            catch (Exception error)
            {
                Note("FAILED: " + name + ".png | " + error);
            }
        }
        async Task Catalogs()
        {
            var watch = Stopwatch.StartNew();
            // Rendering can schedule further genre catalogs after earlier requests complete.
            while (watch.Elapsed < TimeSpan.FromSeconds(45))
            {
                await Task.Delay(350);
                if (homeCatalogPending.Count == 0 && topTen != null && !topTenLoading)
                {
                    Render();
                    await Settle(600);
                    if (homeCatalogPending.Count == 0)
                        break;
                }
            }
            Render();
            await Settle(1000);
            Note(
                $"Catalogs after {watch.Elapsed.TotalSeconds:0.0}s: pending={homeCatalogPending.Count} ({string.Join(", ", homeCatalogPending)}); Top 10={topTen?.Count.ToString() ?? "null"}; genres={homeCatalogs.Count}; titles={homeCatalogs.Values.Sum(c => c.Titles.Count)}."
            );
            if (homeCatalogPending.Count > 0 || topTen is not { Count: > 0 })
                Note("WARNING: catalog wait timed out or Top 10 is empty; inspect the shots before publishing.");
            await Artwork();
        }
        try
        {
            // All settings and cache writes are confined to the explicitly supplied copy.
            store.State.OnlineMetadata = true;
            store.State.AppMode = "Library";
            store.State.RunInBackground = false;
            store.State.AutoBackup = false;
            store.State.BackupFolder = Path.Combine(store.DataDirectory, "backups");
            if (mock)
            {
                await SeedReadmeMock(Note);
                mockSeeded = true;
            }
            var layout = HomeLayout.Load(store.DataDirectory);
            layout.Rows = new HomeLayout().Resolve(layout.Rows.Select(r => r.Id));
            foreach (var row in layout.Rows)
                row.Visible = true;
            layout.ContinueStyle = "Wide";
            layout.ShowPosterTitles = true;
            layout.Save(store.DataDirectory);
            // An empty favourites shelf also suppresses its discovery row. Showcase real watched
            // titles in the disposable profile without fabricating viewings or metadata.
            if (store.State.Favorites.Count == 0)
            {
                var favourites = store
                    .Archive.GroupBy(i => i.GroupKey)
                    .Where(g => g.Any(store.EverWatched))
                    .OrderByDescending(g => g.Max(HistoryDate))
                    .Take(4)
                    .Select(g => g.Key)
                    .ToList();
                store.SetFavorites(favourites);
                Note(
                    "Showcase-only Top four populated from "
                        + favourites.Count
                        + " recently watched real titles in the data-dir copy."
                );
            }
            if (store.State.Likes.Count == 0)
                foreach (var key in store.State.Favorites)
                    store.SetLike(key, true);
            HistoryPrefs.AfterWatching = "Always";
            HistoryPrefs.JustWatchedQuiet = false;
            validateJustWatched = true;
            SetLanguage("en");
            Navigate("Home");
            await PrepareScreen();
            await Catalogs();
            foreach (var language in new[] { "en", "ar" })
            {
                DismissJustWatched(false);
                SetLanguage(language);
                Navigate("Home");
                await Settle(900);
                if (homeCatalogPending.Count > 0 || topTen == null)
                    await Catalogs();
                ContentScroll.ScrollToTop();
                await Shot(language + "-home-hero", "Home hero");
                await AtHeader(HomeRowName("continue"));
                await Shot(language + "-home-rows-1", "Continue watching / Top 10");
                await AtHeader(HomeRowName("because"));
                await Shot(language + "-home-rows-2", "Both Because you watched rows");
                await AtHeader(HomeRowName("picked"));
                await Shot(language + "-home-rows-3", "Picked for you / More like your favourites / Popular in genre");

                // Fire the actual Cinemeta rail card focus handler, including its dwell timer.
                var picked = Header(HomeRowName("picked"));
                FrameworkElement? rail = picked;
                while (rail != null && rail.Parent != ContentPanel)
                    rail = rail.Parent as FrameworkElement;
                var card =
                    rail == null
                        ? null
                        : Descendants(rail)
                            .OfType<FrameworkElement>()
                            .FirstOrDefault(c => homeFocusCards.TryGetValue(c, out _));
                if (card == null)
                {
                    var because = Header(HomeRowName("because"));
                    rail = because;
                    while (rail != null && rail.Parent != ContentPanel)
                        rail = rail.Parent as FrameworkElement;
                    card =
                        rail == null
                            ? null
                            : Descendants(rail)
                                .OfType<FrameworkElement>()
                                .FirstOrDefault(c =>
                                    homeFocusCards.TryGetValue(c, out _)
                                    && Descendants(c)
                                        .OfType<Button>()
                                        .Any(b =>
                                            System
                                                .Windows.Automation.AutomationProperties.GetName(b)
                                                .StartsWith(
                                                    T("Explore {0}", "استكشف {0}", ""),
                                                    StringComparison.Ordinal
                                                )
                                        )
                                );
                }
                ContentScroll.ScrollToTop();
                await Settle();
                if (card != null)
                {
                    card.RaiseEvent(
                        new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, null, card)
                        {
                            RoutedEvent = Keyboard.GotKeyboardFocusEvent,
                        }
                    );
                    await Settle(900);
                    Note(
                        language
                            + " hover uses real Cinemeta card focus handler; hero="
                            + homeHeroTitle
                            + ". Discovery rails sit below the fixed-height hero and cannot share the top viewport."
                    );
                }
                else
                    Note("WARNING: " + language + " Home has no Cinemeta card to focus.");
                await Shot(language + "-home-hover", "Hero follows a focused Cinemeta card");

                DismissJustWatched(false);
                trackerTab = "Up next";
                Navigate("History");
                await Settle(900);
                ContentScroll.ScrollToTop();
                await Shot(language + "-history-top", "Up next");
                await AtHeader(T("Recently watched", "شاهدت مؤخراً"));
                await Shot(language + "-history-recent", "Recently watched with inline Just watched / Favourites");

                var episode = RecentHistoryViewings().Select(v => v.Item).FirstOrDefault(i => !i.IsMovie)
                    ?? store
                        .Archive.Where(i => !i.IsMovie && store.EverWatched(i))
                        .OrderByDescending(HistoryDate)
                        .FirstOrDefault();
                Navigate("Home");
                ContentScroll.ScrollToTop();
                await Artwork();
                if (episode != null)
                {
                    OfferJustWatched(episode);
                    justWatchedTimer?.Stop();
                    await Settle();
                }
                else
                    Note("WARNING: no recently watched episode is available for the corner card.");
                await Shot(language + "-just-watched", "Just watched corner card over Home");
                DismissJustWatched(false);
            }
        }
        catch (Exception error)
        {
            Note("FAILED: README capture mode | " + error);
        }
        finally
        {
            if (mock && !mockSeeded && ReadmeMockBootstrap.DirectoryPrepared != null)
            {
                var seeded = store.Archive.DistinctBy(i => i.GroupKey).ToList();
                Note(
                    "Seeded titles (INCOMPLETE): "
                        + (
                            seeded.Count == 0
                                ? "none"
                                : string.Join(" / ", seeded.Select(i => i.Title + " [" + i.ImdbId + "]"))
                        )
                );
                Note(
                    $"Incomplete mock image-load counts: posters={seeded.Count(i => File.Exists(i.Poster))}/{seeded.Count}; backdrops={seeded.Count(i => File.Exists(i.Backdrop))}/{seeded.Count}; logos={seeded.Count(i => File.Exists(titleExtras?.GetValueOrDefault(i.ImdbId)?.Logo))}/{seeded.Count}; decoded images={images.Count}; image loads pending={imageLoads.Count}; extras pending={extrasPending.Count}. Captures skipped because seeding did not complete."
                );
            }
            DismissJustWatched(false);
            validateJustWatched = false;
            CloseHoverCard();
            HistoryPrefs.AfterWatching = oldAfterWatching;
            HistoryPrefs.JustWatchedQuiet = oldQuiet;
            try
            {
                SetLanguage("en");
                store.Save();
            }
            catch (Exception error)
            {
                Note("FAILED: restore English | " + error);
            }
            Topmost = wasTopmost;
            Note(
                "Finished; language="
                    + store.State.Language
                    + "; PNGs="
                    + Directory.EnumerateFiles(directory, "*.png").Count()
                    + ". Check FAILED/WARNING entries and visually inspect RTL before publishing."
            );
        }
    }
}
#endif
