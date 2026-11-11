using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace Noir;

public partial class MainWindow : Window
{
    readonly LibraryStore store;
    List<VideoItem> items = [];
    string page = "Home";
    string? detailKey;
    int? detailSeason;
    string historyType = "All",
        librarySort = "A–Z";
    bool ready,
        scanning,
        closing;
    bool metadataRefreshing;
    bool metadataRefreshRequested;
    bool pageEditInProgress;
    DispatcherTimer? metadataRender;
    readonly CancellationTokenSource metadataCancellation = new();
    readonly bool validationMode;
    internal bool StartHidden { get; set; }
    readonly Dictionary<string, BitmapImage> images = new(StringComparer.OrdinalIgnoreCase);
    long imageBytes,
        imageTick;
    readonly Dictionary<string, long> imageUse = new(StringComparer.OrdinalIgnoreCase);
    string renderedContext = "";

    public MainWindow(
        string? dataDirectory = null,
        string? playPath = null,
        bool validate = false,
        string? initialPage = null
    )
    {
        StartupTrace.Mark("MainWindow constructor");
        StartupTrace.WatchDispatcher(Dispatcher);
        Density.Load(
            validate ? dataDirectory!
            : dataDirectory != null && !Profiles.IsProfileFolder(dataDirectory) ? dataDirectory
            : Profiles.Root
        );
        StartupTrace.Mark("XAML initialization started");
        InitializeComponent();
        StartupTrace.Mark("XAML initialization ended");
        StartupTrace.Mark("Shell setup started");
        ContentPanel.AddHandler(
            TextBox.TextChangedEvent,
            new TextChangedEventHandler(
                (_, e) =>
                {
                    if (e.OriginalSource is TextBox box && !box.IsReadOnly)
                        pageEditInProgress = true;
                }
            )
        );
        ApplyNavigationIcons();
        NavLinks.LayoutUpdated += (_, _) => SlideNavIndicator();
        StartupTrace.Mark("UI built");
        store = new LibraryStore(dataDirectory);
        StartupTrace.Mark("Library loaded");
        StartupTrace.Mark("ApplyLanguage started");
        ApplyLanguage();
        StartupTrace.Mark("ApplyLanguage ended");
        StartupTrace.Mark("Preview configuration started");
        PreviewHost.Configure(store.DataDirectory, store.GetProgress, store.EverWatched, TmdbKey());
        StartupTrace.Mark("Preview configuration ended");
        StartupTrace.Mark("Constructor tail started");
        var people = new Dictionary<string, IReadOnlyList<string>>();
        PeopleFor = key =>
        {
            if (people.TryGetValue(key, out var known))
                return known;
            var item = store.Archive.FirstOrDefault(i => i.GroupKey == key && i.ImdbId.Length > 0);
            return people[key] =
                item == null ? [] : CreditsClient.CachedPeople(store.DataDirectory, item.ImdbId, item.IsMovie);
        };
        SourceInitialized += (_, _) =>
        {
            StartupTrace.Mark("SourceInitialized started");
            WindowAppearance.Apply(this);
            StartupTrace.Mark("Window appearance applied");
            WindowAppearance.Restore(this, store.DataDirectory);
            StartupTrace.Mark("Window restore ended");
        };
        store.PersistenceFailed += _ =>
            Dispatcher.BeginInvoke(() =>
            {
                if (!closing)
                {
                    SavedLabel.Text = "Couldn't save progress";
                    SavedLabel.Visibility = Visibility.Visible;
                    Status.Text = "Couldn't save the latest progress. Check your library storage.";
                }
            });
        validationMode = validate;
        if (!validate)
            StartupTrace.Mark("Early background services started");
        if (!validate)
            watchSync = new CommunitySync(store, Dispatcher, () =>
            {
                if (!closing && page == "Settings")
                    Status.Text = "Community sync updated. Reopen the Community section to see details.";
            });
        if (!validate)
            StartStremioChecks();
        if (!validate)
            StartNuvio();
        if (!validate)
            StartupTrace.Mark("Early background services scheduled");
        ready = true;
        Loaded += async (_, _) =>
        {
            foreach (Window overlay in OwnedWindows)
                if (overlay.Title == "LibVLCSharp.WPF")
                    overlay.Hide();
            StartupTrace.Mark("Window loaded");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            if (closing)
                return;
            StartupTrace.Mark("Initial page preparation started");
            ApplyAppMode();
            items = store.Archive.Where(i => i.Available).ToList();
            UpdateLibraryCount();
            StartupTrace.Mark("Initial page preparation ended");
            StartupTrace.Mark("Initial page build started");
            if (
                initialPage
                is "Home"
                    or "History"
                    or "Movies"
                    or "Shows"
                    or "My List"
                    or "Settings"
                    or "Discover"
                    or "Downloads"
            )
                Navigate(initialPage);
            else
                Render();
            StartupTrace.Mark("Saved catalog rendered");
            StartupTrace.PageReady(this);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            if (closing)
                return;
            StartupTrace.Mark("Saved catalog presented");
            if (!validate)
            {
                StartupTrace.Mark("Downloads start");
                StartDownloads();
                StartupTrace.Mark("Downloads ready");
                if (downloads != null)
                {
                    if (page == "Downloads")
                        RenderQuiet();
                    else
                        QueueMetadataRender();
                }
                _ = StartPhone();
                StartupTrace.Mark("Background services start");
                StartBackgroundServices();
                StartupTrace.Mark("Background services scheduled");
                if (StartHidden && store.State.RunInBackground)
                    HideToTray(true);
            }
            if (!validate)
                watchSync?.Wake();
            if (!validate && playPath == null)
                _ = Dispatcher.InvokeAsync(
                    async () =>
                    {
                        await Task.Delay(1500);
                        if (ShouldWarmPlayer())
                            WarmPlayer();
                    },
                    DispatcherPriority.ApplicationIdle
                );
            Task? play = null;
            if (playPath != null)
            {
                var item = await Task.Run(() => ResolveStartupPath(playPath));
                if (!closing && item != null)
                    play = PlayVideo(item);
            }
            var scan = ScanLibrary();
#if NOIR_VALIDATE
            if (validate && Environment.GetCommandLineArgs().Contains("--startup-audit-only"))
            {
                await scan;
                await ValidateStartupAudit();
                Close();
                return;
            }
            if (validate)
            {
                await scan;
                if (Environment.GetCommandLineArgs().Contains("--home-layout-only"))
                {
                    await ValidateHomeLayout();
                    Close();
                    return;
                }
                await ValidatePlayback();
                return;
            }
#endif
            if (play != null)
                await play;
            async Task StartPhone()
            {
                StartupTrace.Mark("Phone sync start");
                await StartMobileSync();
                StartupTrace.Mark("Phone sync ready");
            }
        };
        if (StartupTrace.Enabled)
        {
            EventHandler? layout = null;
            layout = (_, _) =>
            {
                LayoutUpdated -= layout;
                StartupTrace.Mark("First window layout completed");
            };
            LayoutUpdated += layout;
            EventHandler? rendered = null;
            rendered = (_, _) =>
            {
                ContentRendered -= rendered;
                StartupTrace.Mark("First window content presented");
            };
            ContentRendered += rendered;
        }
        System.Windows.Automation.AutomationProperties.SetName(SearchBox, "Search movies and shows");
    }

    void Library_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ContentPanel == null || store == null)
            return;
        // Generous margins on wide windows; the rail folds to icons and the search field narrows on small ones.
        var pad = Density.PageSide(ActualWidth);
        sidePad = pad;
        ApplyRailWidth();
        ApplyPageLayout();
        var content = ActualWidth - RailColumn.Width.Value;
        SearchHost.Width = Math.Clamp(content - 260, 200, 440);
        TopBarContent.Margin = new Thickness(Math.Max(16, pad - 16), 0, Math.Max(16, pad - 20), 0);
        if (PlayerSurfaceBuilt)
            ApplyVolumeWidth(ActualWidth < 1000); // U80
    }

    VideoItem? ResolveStartupPath(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
        if (!File.Exists(path))
            return null;
        var known = store.Archive.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        if (known != null)
            return known;
        var name = Path.GetFileNameWithoutExtension(path);
        var match = System.Text.RegularExpressions.Regex.Match(
            name,
            @"(?i)(?<![a-z0-9])(?:S(?<s>\d{1,2})[ ._-]*E(?<e>\d{1,3})|(?<s>\d{1,2})x(?<e>\d{1,3}))(?!\d)"
        );
        var item = new VideoItem
        {
            Path = path,
            Id = Guid.NewGuid().ToString("N"),
            Title = LibraryStore.CleanTitle(match.Success ? name[..match.Index] : name) is { Length: > 0 } title
                ? title
                : name,
            IsMovie = !match.Success,
            Season = match.Success ? int.Parse(match.Groups["s"].Value) : 1,
            Episode = match.Success ? int.Parse(match.Groups["e"].Value) : 0,
        };
        store.BeginViewing(item, false);
        return store.Archive.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))
            ?? item;
    }

    void UpdateLibraryCount()
    {
        var catalog = store.Archive;
        LibraryCount.Text =
            $"{catalog.Count(x => x.IsMovie)} movies · {catalog.Where(x => !x.IsMovie).Select(x => x.GroupKey).Distinct().Count()} shows";
    }

    string[] CatalogStamp() =>
        store
            .Archive.Select(i =>
                string.Join(
                    "\0",
                    i.Id,
                    i.GroupKey,
                    i.Path,
                    i.Available,
                    i.Title,
                    i.Year,
                    i.Season,
                    i.Episode,
                    i.Poster,
                    i.Backdrop,
                    i.EpisodeImage
                )
            )
            .OrderBy(i => i, StringComparer.Ordinal)
            .ToArray();

    async Task ScanLibrary(bool refreshMetadata = true)
    {
        if (scanning)
            return;
        scanning = true;
        StartupTrace.Mark("Scan preparation started");
        var before = CatalogStamp();
        StartupTrace.Mark("Scan started");
        Status.Text = "Looking for movies and episodes…";
        try
        {
            var result = await Task.Run(() =>
            {
                StartupTrace.Mark("Scan worker started");
                var found = store.Scan(out var warnings);
                StartupTrace.Mark("Scan worker ended");
                return (found, warnings);
            });
            if (closing)
                return;
            items = result.found;
            UpdateLibraryCount();
            warningToast = result.warnings.Count > 0;
            Status.Text =
                result.warnings.Count > 0 ? string.Join("  ·  ", result.warnings) : $"{items.Count} videos ready";
            warningToast = false;
            if (!string.IsNullOrEmpty(store.LoadWarning))
            {
                warningToast = true;
                Status.Text = store.LoadWarning;
                warningToast = false;
            }
            if (
                !before.SequenceEqual(CatalogStamp())
                && current == null
                && !pageEditInProgress
                && page is not ("Settings" or "Downloads")
                && !logging
            )
            {
                RenderQuiet();
                StartupTrace.Mark("Changed scan rendered");
            }
            if (!validationMode)
                WatchLibraryFolders();
            if (!validationMode && refreshMetadata)
                _ = RefreshMetadata();
            StartupTrace.Mark("Scan applied");
        }
        catch (Exception e)
        {
            store.Log(e);
            Status.Text = "Could not scan your library: " + e.Message;
        }
        finally
        {
            scanning = false;
            StartupTrace.Mark("Scan ended");
            StartupTrace.Flush();
        }
    }

    void Discover_Click(object sender, RoutedEventArgs e) => Navigate("Discover");

    void Downloads_Click(object sender, RoutedEventArgs e) => Navigate("Downloads");

    void Home_Click(object sender, RoutedEventArgs e) => Navigate("Home");

    void Movies_Click(object sender, RoutedEventArgs e) => Navigate("Movies");

    void Shows_Click(object sender, RoutedEventArgs e) => Navigate("Shows");

    void History_Click(object sender, RoutedEventArgs e)
    {
        historyType = "All";
        trackerTab = "Up next";
        Navigate("History");
    }

    void Watchlist_Click(object sender, RoutedEventArgs e) => Navigate("My List");

    void Settings_Click(object sender, RoutedEventArgs e) => Navigate("Settings");

    async void Scan_Click(object sender, RoutedEventArgs e) => await ScanLibrary();

    DispatcherTimer? searchDebounce;

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (!ready)
            return;
        searchDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        searchDebounce.Stop();
        searchDebounce.Tick -= CommitSearch;
        searchDebounce.Tick += CommitSearch;
        searchDebounce.Start();
    }

    void CommitSearch(object? sender, EventArgs e)
    {
        searchDebounce?.Stop();
        if (page == "Discover")
        {
            discoverTitle = null;
            _ = SearchDiscover();
            return;
        }
        detailKey = null;
        detailOrigin = null;
        if (page == "Settings" || page == "Downloads" && SearchBox.Text.Trim().Length > 0)
            page = "Home";
        Render();
        ContentScroll.ScrollToTop();
    }

    void Navigate(string target)
    {
        if (TrackerOnly && target is "Home" or "Shows" or "Movies")
        {
            if (target != "Home")
                trackerTab = target;
            target = "History";
        }
        CancelStreamSearch();
        CancelLogSearch();
        CloseHoverCard();
        CloseDiscoverPanel();
        CancelDiscover();
        SmoothScrolling.Stop(ContentScroll);
        page = target;
        detailOrigin = null;
        detailKey = null;
        detailSeason = null;
        discoverTitle = null;
        logging = false;
        logTitle = null;
        logCancel?.Cancel();
        SearchBox.Text = "";
        searchDebounce?.Stop();
        Render();
        ContentScroll.ScrollToTop();
    }

    void Render()
    {
        if (StartupTrace.Enabled)
            StartupTrace.Mark("Page build started: " + page);
        CancelLogSearch();
        metadataRender?.Stop();
        pageEditInProgress = false;
        if (hiddenInTray)
        {
            renderWhenShown = true;
            return;
        }
        if (TrackerOnly && page == "Home")
            page = "History";
        var context = page + "|" + detailKey + "|" + SearchBox.Text;
        var offset = context == renderedContext ? ContentScroll.VerticalOffset : 0;
        animateCards = offset == 0 && !quietRender;
        if (animateCards)
            cardEntranceCount = 0;
        renderedContext = context;
        var focused = Keyboard.FocusedElement as FrameworkElement;
        var focusName = focused != null ? System.Windows.Automation.AutomationProperties.GetName(focused) : "";
        try
        {
            CloseHoverCard();
            downloadPageUpdate = null;
            SmoothScrolling.Stop(ContentScroll);
            ContentPanel.Children.Clear();
            SearchHost.Visibility = Visibility.Visible;
            SearchPlaceholder.Text = T(page == "Discover" ? "Search new movies & shows" : "Search your library");
            SearchBox.ToolTip = T(page == "Discover" ? "Find new movies and shows online" : "Search your library");
            PageEyebrow.Text = T(
                page switch
                {
                    "Discover" => "FIND YOUR NEXT WATCH",
                    "Downloads" => "SAVED TO YOUR PC",
                    "Settings" => "PREFERENCES",
                    "History" => "",
                    "My List" => "SAVED FOR LATER",
                    _ => SearchBox.Text.Length > 0 ? "SEARCH" : "YOUR LIBRARY",
                }
            );
            // Home and title pages open on a full-bleed hero beneath the floating top bar.
            var welcome = page == "Home" && detailKey == null && SearchBox.Text.Length == 0 && NeedsWelcome;
            cinematic =
                (page == "Home" && SearchBox.Text.Length == 0 || detailKey != null)
                && page is not ("Settings" or "Discover" or "Downloads")
                && !welcome;
            ApplyPageLayout();
            UpdateNavSelection();
            if (offset == 0 && !quietRender)
                PageEntrance();
            StartupTrace.Mark("Page chrome ended");
            if (welcome)
            {
                StartupTrace.Mark("Welcome section started");
                RenderWelcome();
                return;
            }
            if (page == "Taste")
            {
                RenderTaste();
                return;
            }
            if (page == "Downloads")
            {
                RenderDownloads();
                return;
            }
            if (page == "Discover")
            {
                RenderDiscover();
                return;
            }
            if (page == "Settings")
            {
                RenderSettings();
                return;
            }
            if (detailKey != null)
            {
                var group = store.EpisodesFor(detailKey);
                if (group.Count > 0)
                {
                    RenderDetail(group);
                    return;
                }
                detailKey = null;
            }
            if (page == "History")
            {
                RenderTrackerHistory();
                return;
            }
            PageTitle.Text = T(page == "Home" ? "Your evening, queued." : page);
            var query = SearchBox.Text.Trim();
            // Library pages list library titles; Home also keeps shows you're actively watching elsewhere.
            StartupTrace.Mark("Page grouping started");
            var library = store.LibraryGroups();
            var active =
                page == "Home"
                    ? store
                        .Archive.Where(x => HistoryDate(x) > DateTimeOffset.UtcNow.AddDays(-120))
                        .Select(x => x.GroupKey)
                        .ToHashSet()
                    : [];
            var groups = store
                .Archive.Where(x =>
                    !store.State.HiddenTitles.Contains(x.GroupKey)
                    && (
                        page == "My List"
                        || library.Contains(x.GroupKey)
                        || active.Contains(x.GroupKey)
                        || page == "Home" && store.State.Watchlist.Contains(x.GroupKey)
                    )
                )
                .Where(x =>
                    (page != "Movies" || x.IsMovie)
                    && (page != "Shows" || !x.IsMovie)
                    && (
                        page != "My List"
                        || collectionFilter == "My List" && store.State.Watchlist.Contains(x.GroupKey)
                        || collectionFilter != "My List"
                            && store.State.Collections.GetValueOrDefault(collectionFilter)?.Contains(x.GroupKey) == true
                    )
                )
                .GroupBy(x => x.GroupKey)
                .Select(x => store.EpisodesFor(x.Key))
                .Where(g =>
                    query.Length == 0
                    || g.Any(x =>
                        x.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || x.EpisodeTitle.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || x.Genres.Contains(query, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .ToList();
            StartupTrace.Mark("Page grouping ended");
            if (page != "Home")
            {
                StartupTrace.Mark("Library section started");
                RenderLibraryPage(groups);
                return;
            }
            groups = groups.OrderBy(g => g[0].Title).ToList();
            if (query.Length == 0 && page == "Home" && groups.Count > 0)
            {
                // Titles stay here after their last local episode: the next one can be downloaded,
                // or is shown with its air date, so finishing something never means hunting in History.
                StartupTrace.Mark("Home hero planning started");
                var recent = DateTimeOffset.UtcNow.AddDays(-120);
                var continuations = groups
                    .Where(g =>
                        store.TitleStatus(g) != "Dropped" && !store.State.HiddenContinue.Contains(g[0].GroupKey)
                    )
                    .Select(g => store.UpNextFor(g))
                    .OfType<UpNext>()
                    .Where(x => x.Kind is UpNextKind.Resume or UpNextKind.Ready || x.Activity > recent)
                    .OrderByDescending(x => x.Activity)
                    .ToList();
                var latest = groups
                    .Where(g => g.Max(HistoryDate) > DateTimeOffset.MinValue)
                    .OrderByDescending(g => g.Max(HistoryDate))
                    .FirstOrDefault();
                var justFinished =
                    latest != null
                    && store.UpNextFor(latest) == null
                    && latest.Max(HistoryDate) > DateTimeOffset.UtcNow.AddDays(-3)
                    && (continuations.Count == 0 || latest.Max(HistoryDate) > continuations[0].Activity);
                var heroNext = continuations.FirstOrDefault(x => x.Kind != UpNextKind.Upcoming);
                var hero = justFinished
                    ? latest!.OrderByDescending(HistoryDate).First()
                    : heroNext?.Item ?? ChoiceFor(groups.FirstOrDefault(g => g.Any(CanPlay)) ?? groups[0]);
                var slides = new List<(VideoItem Item, List<VideoItem> Group, bool Continuing, bool Finished)>
                {
                    (
                        hero,
                        groups.First(g => g[0].GroupKey == hero.GroupKey),
                        heroNext != null && !justFinished,
                        justFinished
                    ),
                };
                foreach (
                    var more in continuations
                        .Where(x =>
                            x.Kind != UpNextKind.Upcoming
                            && x.Item.GroupKey != hero.GroupKey
                            && File.Exists(x.Item.Backdrop)
                        )
                        .Take(3)
                )
                    slides.Add((more.Item, groups.First(g => g[0].GroupKey == more.Item.GroupKey), true, false));
                foreach (
                    var pick in groups
                        .Where(g =>
                            slides.All(s => s.Item.GroupKey != g[0].GroupKey)
                            && g.Any(CanPlay)
                            && !g.Any(store.EverWatched)
                            && File.Exists(g[0].Backdrop)
                        )
                        .OrderByDescending(g => store.State.Watchlist.Contains(g[0].GroupKey))
                        .ThenByDescending(g => g.Max(i => i.AddedAt))
                        .Take(Math.Max(0, 5 - slides.Count))
                )
                    slides.Add((ChoiceFor(pick), pick, false, false));
                StartupTrace.Mark("Home hero planning ended");
                StartupTrace.Mark("Home hero build started");
                ContentPanel.Children.Add(HeroCarousel(slides));
                StartupTrace.Mark("Home hero build ended");
                StartupTrace.Mark("Home continue section started");
                if (continuations.Count > 0)
                {
                    // One shelf, most recent first: resumes, next episodes and downloads sit together;
                    // shows waiting for their next air date follow at the end.
                    var shelf = continuations
                        .Where(x => x.Kind != UpNextKind.Upcoming)
                        .Concat(continuations.Where(x => x.Kind == UpNextKind.Upcoming))
                        .ToList();
                    ContentPanel.Children.Add(
                        ContinueShelf(
                            shelf,
                            "Continue watching",
                            shelf.Any(x => x.Kind == UpNextKind.NotOnPc)
                                ? "Resume, play the next episode, or get it on this PC in one click."
                                : "Resume where you stopped or play the next episode."
                        )
                    );
                }
                else
                    ContentPanel.Children.Add(
                        Heading("Continue watching", "Start a movie or episode. It will appear here when you return.")
                    );
            }
            StartupTrace.Mark("Home continue section ended");
            if (page == "Home" && query.Length == 0)
            {
                StartupTrace.Mark("Home rows started");
                if (groups.Count == 0)
                    ContentPanel.Children.Add(
                        Heading("Continue watching", "Start a movie or episode. It will appear here when you return.")
                    );
                RenderHomeRows(groups);
                StartupTrace.Mark("Home rows ended");
                return;
            }
            if (groups.Count == 0)
            {
                if (page == "My List")
                {
                    ContentPanel.Children.Add(
                        Heading("Your next watch", "Use + My List on any title to keep it here.")
                    );
                    return;
                }
                ContentPanel.Children.Add(
                    Heading(
                        query.Length > 0 ? "No matches" : "Your cinema starts here",
                        query.Length > 0 ? "Try another title." : "Add your movie and show folders in Settings."
                    )
                );
                if (query.Length == 0)
                    ContentPanel.Children.Add(ActionButton("Add folders", () => Navigate("Settings"), true));
                return;
            }
            if (page == "Home")
            {
                RailCatalog(groups, "Results");
                return;
            }
        }
        finally
        {
            if (StartupTrace.Enabled)
                StartupTrace.Mark("Page sections ended: " + page);
            ContentScroll.ScrollToVerticalOffset(offset);
            if (focusName.Length > 0)
                ViewElements(ContentPanel)
                    .FirstOrDefault(e => System.Windows.Automation.AutomationProperties.GetName(e) == focusName)
                    ?.Focus();
            if (StartupTrace.Enabled)
                StartupTrace.Mark("Page build ended: " + page);
        }
    }

    // Home sorts several rails by last watch; work it out once per render.
    readonly Dictionary<string, DateTimeOffset> lastWatched = new();

    DateTimeOffset LastWatched(List<VideoItem> g) =>
        lastWatched.TryGetValue(g[0].GroupKey, out var at) ? at : g.Max(HistoryDate);

    string detailTab = "Episodes";

    sealed record DetailOrigin(
        string Page,
        string Query,
        string LibraryFilter,
        string Genre,
        string Sort,
        string ListType,
        string Collection,
        string HistoryType,
        string TrackerTab,
        string ShowsFilter,
        string ShowsSort,
        string MoviesFilter,
        string MoviesSort,
        double Offset
    );

    DetailOrigin? detailOrigin;

    void BackFromDetails(string fallback)
    {
        if (detailOrigin is not { } origin)
        {
            Navigate(fallback);
            return;
        }
        CancelStreamSearch();
        CancelLogSearch();
        CloseHoverCard();
        CloseDiscoverPanel();
        CancelDiscover();
        SmoothScrolling.Stop(ContentScroll);
        page = origin.Page;
        detailKey = null;
        detailSeason = null;
        detailOrigin = null;
        SearchBox.Text = origin.Query;
        searchDebounce?.Stop();
        libraryFilter = origin.LibraryFilter;
        genreFilter = origin.Genre;
        librarySort = origin.Sort;
        listType = origin.ListType;
        collectionFilter = origin.Collection;
        historyType = origin.HistoryType;
        trackerTab = origin.TrackerTab;
        showsFilter = origin.ShowsFilter;
        showsSort = origin.ShowsSort;
        moviesFilter = origin.MoviesFilter;
        moviesSort = origin.MoviesSort;
        Render();
        ContentScroll.UpdateLayout();
        ContentScroll.ScrollToVerticalOffset(origin.Offset);
    }

    void OpenDetails(List<VideoItem> group)
    {
        if (group.Count == 0)
            return;
        if (detailKey == null)
            detailOrigin = new(
                page,
                SearchBox.Text,
                libraryFilter,
                genreFilter,
                librarySort,
                listType,
                collectionFilter,
                historyType,
                trackerTab,
                showsFilter,
                showsSort,
                moviesFilter,
                moviesSort,
                ContentScroll.VerticalOffset
            );
        searchDebounce?.Stop();
        detailTab = group[0].IsMovie ? "About" : "Episodes";
        detailKey = group[0].GroupKey;
        detailSeason = null;
        episodeFilter = "All episodes";
        Render();
        ContentScroll.ScrollToTop();
    }

    async Task RefreshMetadata(bool force = false)
    {
        if (closing)
            return;
        if (!store.State.OnlineMetadata)
        {
            if (force)
                Status.Text = "Enable online artwork and information in Settings first.";
            return;
        }
        if (metadataRefreshing)
        {
            if (force)
            {
                metadataRefreshRequested = true;
                Status.Text = "Refresh queued · waiting for the current information check…";
            }
            return;
        }
        metadataRefreshing = true;
        int updated = 0,
            unmatched = 0,
            failed = 0,
            checkedTitles = 0;
        try
        {
            var groups = store.Archive.GroupBy(x => x.GroupKey).Select(g => g.ToList()).ToList();
            using var client = new MetadataClient(store.DataDirectory, refreshArtwork: force);
            foreach (var group in groups)
            {
                if (closing || !store.State.OnlineMetadata)
                    break;
                if (
                    !force
                    && group.All(i => store.State.Catalog[i.Id].MetadataCheckedAt > DateTimeOffset.UtcNow.AddDays(-1))
                    && (
                        group.All(i => i.ReviewTitle.Length == 0)
                        || group[0].IsMovie
                        || store.State.EpisodeSchedules.ContainsKey(group[0].GroupKey)
                            && store.State.ShowStates.ContainsKey(group[0].GroupKey)
                    )
                )
                    continue;
                try
                {
                    if (force)
                        Status.Text =
                            $"Refreshing {++checkedTitles}/{groups.Count}: {LibraryStore.CleanShowTitle(group[0].Title)}…";
                    var details = await client.Fetch(group[0], metadataCancellation.Token);
                    if (closing)
                        break;
                    if (details != null)
                        foreach (
                            var episode in details.Schedule.Where(e =>
                                group.Any(i => i.Available && i.Season == e.Season && i.Episode == e.Episode)
                                && e.Image.Length > 0
                            )
                        )
                        {
                            try
                            {
                                episode.Image = await client.EpisodeArt(episode.Image, metadataCancellation.Token);
                            }
                            catch (Exception e)
                            {
                                store.Log(e);
                            }
                        }
                    store.ApplyDetails(group[0].GroupKey, details, force);
                    if (details != null)
                    {
                        updated++;
                        if (force)
                        {
                            images.Clear();
                            imageUse.Clear();
                            imageBytes = 0;
                        }
                        QueueMetadataRender();
                    }
                    else
                        unmatched++;
                }
                catch (OperationCanceledException) when (metadataCancellation.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    store.Log(e);
                    failed++;
                }
                await Task.Delay(600, metadataCancellation.Token);
            }
            if (!closing && force)
                Status.Text =
                    $"Refresh finished · {updated} titles updated"
                    + (unmatched > 0 ? $" · {unmatched} unmatched" : "")
                    + (failed > 0 ? $" · {failed} could not connect" : "");
            if (!closing && failed == 0 && !validationMode)
                _ = RefreshEpisodeLinks();
        }
        catch (OperationCanceledException) { }
        finally
        {
            metadataRefreshing = false;
            if (metadataRefreshRequested && !closing)
            {
                metadataRefreshRequested = false;
                _ = RefreshMetadata(true);
            }
        }
    }

    void QueueMetadataRender()
    {
        if (closing)
            return;
        if (metadataRender == null)
        {
            metadataRender = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            metadataRender.Tick += (_, _) =>
            {
                metadataRender.Stop();
                if (CanRenderMetadata)
                    RenderQuiet();
            };
        }
        if (!metadataRender.IsEnabled)
            metadataRender.Start();
    }

    bool MetadataFocusSafe =>
        Keyboard.FocusedElement is not DependencyObject focus
        || focus is not System.Windows.Controls.Primitives.TextBoxBase
            && PresentationSource.FromDependencyObject(focus) == PresentationSource.FromVisual(this);
    bool CanRenderMetadata =>
        !closing
        && current == null
        && !hiddenInTray
        && IsActive
        && MetadataFocusSafe
        && !pageEditInProgress
        && !SearchBox.IsKeyboardFocusWithin
        && discoverPanel == null
        && smartPopup == null
        && hoverCard == null
        && !logging
        && (page is "Home" or "Movies" or "Shows" or "My List" or "Discover" or "History")
        && !(page == "History" && trackerTab == "Import");

    internal static SolidColorBrush Brush(string value) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;

    internal static TextBlock Text(
        string value,
        double size = double.NaN,
        string color = "#F4F1EB",
        FontWeight? weight = null
    ) =>
        new()
        {
            Text = value,
            FontSize = double.IsNaN(size) ? Density.Text : size,
            Foreground = Brush(color),
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
        };

    internal static TextBlock Body(string value, double size = double.NaN)
    {
        var t = Text(T(value), double.IsNaN(size) ? Density.Body : size, "#AAA6A0");
        t.Margin = Density.BodyMargin;
        return Bidi(t);
    }

    static StackPanel Heading(string title, string subtitle = "")
    {
        var p = new StackPanel { Margin = Density.SectionMargin };
        p.Children.Add(Text(T(title), Density.SectionTitle, weight: FontWeights.SemiBold));
        if (subtitle.Length > 0)
        {
            var t = Body(subtitle, Density.SectionSubtitle);
            t.Margin = new(0, 4, 0, 0);
            p.Children.Add(t);
        }
        return p;
    }

    Button ActionButton(string label, Action action, bool primary = false)
    {
        var b = new Button { Content = T(label), HorizontalAlignment = HorizontalAlignment.Left };
        if (primary)
            b.Style = (Style)FindResource("Primary");
        System.Windows.Automation.AutomationProperties.SetName(b, T(label));
        Motion.ButtonFeedback(b);
        b.Click += (_, _) => action();
        return b;
    }

    readonly Dictionary<string, Task<BitmapImage?>> imageLoads = new(StringComparer.OrdinalIgnoreCase);
    static readonly object imageDecodeGate = new();
    static readonly SemaphoreSlim imageDecodeSlots = new(4);
    static readonly Dictionary<string, Task<BitmapImage?>> imageDecodes = new(StringComparer.OrdinalIgnoreCase);

    BitmapImage? ImageSource(string path, int width = 1200)
    {
        if (CachedImage(path, width) is { } cached)
            return cached;
        _ = LoadAndRefresh();
        return null;
        async Task LoadAndRefresh()
        {
            if (await LoadImage(path, width) != null && !closing)
                QueueMetadataRender();
        }
    }

    async Task<BitmapImage?> LoadImage(string path, int width = 1200)
    {
        if (path.Length == 0)
            return null;
        if (CachedImage(path, width) is { } cached)
            return cached;
        var key = path + "|" + width;
        if (imageLoads.TryGetValue(key, out var pending))
            return await pending;
        var load = DecodeShared(path, width);
        imageLoads[key] = load;
        try
        {
            return Remember(path, width, await load);
        }
        finally
        {
            if (imageLoads.GetValueOrDefault(key) == load)
                imageLoads.Remove(key);
        }
    }

    static Task<BitmapImage?> DecodeShared(string path, int width)
    {
        var key = path + "|" + width;
        lock (imageDecodeGate)
        {
            if (imageDecodes.TryGetValue(key, out var pending))
                return pending;
            var task = Task.Run(async () =>
            {
                await imageDecodeSlots.WaitAsync().ConfigureAwait(false);
                try
                {
                    return DecodeImageCore(path, width);
                }
                finally
                {
                    imageDecodeSlots.Release();
                }
            });
            imageDecodes[key] = task;
            _ = task.ContinueWith(
                _ =>
                {
                    lock (imageDecodeGate)
                    {
                        if (imageDecodes.GetValueOrDefault(key) == task)
                            imageDecodes.Remove(key);
                    }
                },
                TaskScheduler.Default
            );
            return task;
        }
    }

    async Task SetArtwork(Image image, string path, int width)
    {
        var request = new object();
        image.Tag = request;
        var source = await LoadImage(path, width);
        if (closing || !ReferenceEquals(image.Tag, request) || source == null)
            return;
        image.Source = source;
        if (image.IsLoaded)
            Motion.FadeIn(image);
    }

    // Least-recently-used cache of decoded artwork, capped by count and decoded size.
    BitmapImage? CachedImage(string path, int width)
    {
        var key = path + "|" + width;
        if (!images.TryGetValue(key, out var cached))
            return null;
        imageUse[key] = ++imageTick;
        return cached;
    }

    BitmapImage? Remember(string path, int width, BitmapImage? image)
    {
        if (image == null)
            return null;
        var key = path + "|" + width;
        if (images.TryGetValue(key, out var known))
            return known;
        var bytes = (long)image.PixelWidth * image.PixelHeight * 4;
        while (images.Count > 0 && (images.Count >= 400 || imageBytes + bytes > 96L * 1024 * 1024))
        {
            var oldest = images.Keys.MinBy(k => imageUse.GetValueOrDefault(k))!;
            var old = images[oldest];
            imageBytes -= (long)old.PixelWidth * old.PixelHeight * 4;
            images.Remove(oldest);
            imageUse.Remove(oldest);
        }
        images[key] = image;
        imageUse[key] = ++imageTick;
        imageBytes += bytes;
        return image;
    }

    static BitmapImage? DecodeImage(string path, int width) => DecodeShared(path, width).GetAwaiter().GetResult();

    static BitmapImage? DecodeImageCore(string path, int width)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(Path.GetFullPath(path));
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = width;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    // Uncached artwork decodes off the UI thread and fades in, so long pages render at once.
    FrameworkElement Art(string path, string title, double height, bool portrait = false)
    {
        var width = portrait ? 384 : 640;
        if (File.Exists(path))
        {
            if (CachedImage(path, width) is { } cached)
                return new Image
                {
                    Source = cached,
                    Stretch = Stretch.UniformToFill,
                    Height = height,
                    ClipToBounds = true,
                };
            var image = new Image
            {
                Stretch = Stretch.UniformToFill,
                Height = height,
                ClipToBounds = true,
            };
            _ = SetArtwork(image, path, width);
            return image;
        }
        var grid = new Grid
        {
            Height = height,
            Background = new LinearGradientBrush(Brush("#36312B").Color, Brush("#131315").Color, 35),
        };
        var text = Text(title, portrait ? 25 : 21, weight: FontWeights.SemiBold);
        text.Margin = new(20);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        return grid;
    }

    bool closeReady;

    async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (closeReady)
            return;
        e.Cancel = true;
        if (closing)
            return;
        if (!quitting && tray != null && store.State.RunInBackground)
        {
            HideToTray();
            return;
        }
        closing = true;
        WindowAppearance.Remember(
            store.DataDirectory,
            fullscreen ? oldWindowBounds : RestoreBounds,
            fullscreen ? oldWindowState == WindowState.Maximized : WindowState == WindowState.Maximized
        );
        StopWatchingFolders();
        metadataRender?.Stop();
        extrasRender?.Stop();
        CancelStreamSearch();
        CancelLogSearch();
        metadataCancellation.Cancel();
        CancelDiscover();
        SmoothScrolling.Stop(ContentScroll);
        downloadPageUpdate = null;
        HidePreview();
        if (previews != null)
            await previews.DisposeAsync();
        SavePlayback();
        DisposeBackgroundServices();
        watchSync?.Dispose();
        nuvioSync?.Dispose();
        downloads?.Dispose();
        if (mobileSync != null)
            _ = mobileSync.DisposeAsync();
        tick?.Stop();
        nextTimer?.Stop();
        hideTimer?.Stop();
        sleepTimer?.Stop();
        hintTimer?.Stop();
        searchDebounce?.Stop();
        if (player != null)
        {
            DetachEvents();
            Video.MediaPlayer = null;
            var native = player;
            player = null;
            await nativeTransition.WaitAsync();
            try
            {
                await Task.Run(() =>
                {
                    native.Stop();
                    native.Dispose();
                    media?.Dispose();
                    vlc?.Dispose();
                });
            }
            finally
            {
                nativeTransition.Release();
            }
        }
        if (warmEngine is { } warm)
        {
            warmEngine = null;
            try
            {
                (await warm).Dispose();
            }
            catch { }
        }
        if (PlayerSurfaceBuilt)
            store.State.Volume = (int)VolumeBar.Value;
        try
        {
            await Task.Run(store.Save);
            await store.FlushPendingAsync();
        }
        catch (Exception error)
        {
            try
            {
                store.Log(error);
            }
            catch { }
        }
        if (PlayerSurfaceBuilt)
            Video.Dispose();
        closeReady = true;
        Close();
    }
}
