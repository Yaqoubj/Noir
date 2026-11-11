#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace Noir;

public partial class MainWindow
{
    // --tracker-mode-only: the welcome choice, tracker-only navigation and the Upcoming calendar
    // (with a seeded aired and announced episode), screenshotted beside the profile copy.
    async Task ValidateTrackerMode()
    {
        var results = new List<string>();
        void Check(bool ok, string label)
        {
            if (!ok)
                throw new Exception(label);
            results.Add("PASS: " + label);
        }
        async Task Settle(int ms = 500)
        {
            UpdateLayout();
            await Task.Delay(ms);
            UpdateLayout();
        }
        void Shot(string name) => CaptureLibrary(Path.Combine(store.DataDirectory, "mode-" + name + ".png"));
        try
        {
            if (Environment.GetCommandLineArgs().Contains("--offline-fixture"))
                discoveryFactory = () => new DiscoveryClient(store.DataDirectory, new TrackerHistoryFixtureHandler());
            WindowState = WindowState.Normal;
            Width = 1320;
            Height = 900;
            var show = TrackedShows()
                .Where(t =>
                    t.Watched > 0 && store.State.EpisodeSchedules.GetValueOrDefault(t.Group[0].GroupKey)?.Count > 0
                )
                .OrderByDescending(t => t.Last)
                .FirstOrDefault();
            Check(show != null, "The profile has a tracked show with a schedule");
            var key = show!.Group[0].GroupKey;
            var schedule = store.State.EpisodeSchedules[key];
            var last = schedule.Max(e => e.Season);
            var top = schedule.Where(e => e.Season == last).Max(e => e.Episode);
            schedule.Add(
                new()
                {
                    Season = last,
                    Episode = top + 1,
                    Title = "Validation aired",
                    AirsAt = DateTimeOffset.UtcNow.AddDays(-1),
                }
            );
            schedule.Add(
                new()
                {
                    Season = last,
                    Episode = top + 2,
                    Title = "Validation upcoming",
                    AirsAt = DateTimeOffset.UtcNow.AddDays(3),
                }
            );
            foreach (var e in schedule.Where(e => e.Season == last && e.Episode <= top))
                if (
                    store.EpisodesFor(key).FirstOrDefault(i => i.Season == e.Season && i.Episode == e.Episode) is { } i
                    && !store.EverWatched(i)
                )
                    store.LogWatched([new(i, DateTimeOffset.UtcNow.AddDays(-2))], "Validation");
            trackerTab = "Upcoming";
            Navigate("History");
            await Settle(900);
            var texts = Descendants(ContentPanel).OfType<TextBlock>().Select(t => t.Text).ToList();
            Check(texts.Contains("Out now"), "Upcoming shows the newly aired episode under Out now");
            Check(
                texts.Any(t => t.StartsWith(DateTime.Today.ToString("MMMM yyyy"))),
                "Upcoming shows this month's calendar"
            );
            Check(texts.Contains("Coming up"), "Upcoming lists announced episodes");
            Shot("upcoming");
            ContentScroll.ScrollToVerticalOffset(700);
            await Settle();
            Shot("upcoming-scrolled");
            var aired = store.EpisodesFor(key).First(i => i.Season == last && i.Episode == top + 1);
            CheckOff([aired], "validation");
            Check(store.EverWatched(aired), "Checking off from Out now logs the episode");
            await Settle();
            upcomingDay = DateTime.Today.AddDays(3);
            RenderQuiet();
            await Settle();
            Check(
                Descendants(ContentPanel)
                    .OfType<TextBlock>()
                    .Any(t => t.Text.Contains("Validation upcoming") || t.Text.Contains($"E{top + 2:00}")),
                "Picking a calendar day lists that day's episodes"
            );
            Shot("upcoming-day");
            upcomingDay = null;
            upcomingScope = "Everything";
            RenderQuiet();
            await Settle();
            Shot("upcoming-everything");
            upcomingScope = "My shows";
            Width = 900;
            Height = 760;
            trackerTab = "Upcoming";
            Navigate("History");
            await Settle();
            Shot("upcoming-900");
            Width = 1320;
            Height = 900;
            page = "Home";
            detailKey = null;
            ContentPanel.Children.Clear();
            RenderWelcome();
            await Settle();
            Shot("welcome");
            Check(
                Descendants(ContentPanel).OfType<Button>().Count() >= 3,
                "Welcome offers tracker and library choices"
            );
            ChooseAppMode("Tracker");
            await Settle();
            Check(
                page == "History"
                    && HomeNav.Visibility == Visibility.Collapsed
                    && MovieNav.Visibility == Visibility.Collapsed,
                "Tracker only opens History and hides the file library"
            );
            Check(
                new LibraryStore(store.DataDirectory).State.AppMode == "Tracker",
                "The tracker choice persists after reopening"
            );
            Check(
                NavLinks
                    .Children.OfType<Button>()
                    .Where(b => b.Visibility == Visibility.Visible)
                    .Take(4)
                    .SequenceEqual(new[] { HistoryNav, trackerDiaryNav!, DiscoverNav, trackerStatsNav! }),
                "Tracker navigation starts with Next, Diary, Discover and Stats"
            );
            Check(
                trackerLogNav?.Visibility != Visibility.Visible
                    && DownloadsNav.Visibility == Visibility.Collapsed
                    && ScanNav.Visibility == Visibility.Collapsed,
                "Tracker drops the rail Log button (the page header has Log) and hides playback setup actions"
            );
            var goal = HistoryPrefs.WeeklyGoal;
            HistoryPrefs.WeeklyGoal = 7;
            HistoryPrefs.Save(store.DataDirectory);
            Check(HistorySettings.Load(store.DataDirectory).WeeklyGoal == 7, "The weekly goal persists per profile");
            HistoryPrefs.WeeklyGoal = goal;
            HistoryPrefs.Save(store.DataDirectory);
            trackerTab = "Diary";
            Navigate("History");
            await Settle();
            Check(
                Descendants(ContentPanel).OfType<System.Windows.Controls.Image>().Any(),
                "Visual diary renders cached posters"
            );
            Shot("visual-diary");
            var diaryItem = store.Archive.First(i => store.ViewingHistory(i).Count > 0);
            var diaryBefore = store.Snapshot([diaryItem]);
            store.LogRewatch(new(diaryItem, DateTimeOffset.UtcNow), "Validation");
            var diaryViews = store.ViewingHistory(diaryItem);
            var removedView = diaryViews.Last();
            var diaryRow = DiaryRow(diaryItem, removedView);
            var diaryMore = Descendants(diaryRow)
                .OfType<Button>()
                .First(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("More options for"));
            diaryMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            diaryMore
                .ContextMenu!.Items.OfType<MenuItem>()
                .First(m => IconSet.Src(m.Header) == "Remove this viewing")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            diaryMore.ContextMenu.IsOpen = false;
            Check(
                store.ViewingHistory(diaryItem).Count == diaryViews.Count - 1
                    && store.ViewingHistory(diaryItem).All(v => v.Id != removedView.Id),
                "Diary delete removes only that viewing"
            );
            Descendants(undoBar!)
                .OfType<Button>()
                .First(b => b.Content as string == "Undo")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(store.ViewingHistory(diaryItem).Count == diaryViews.Count, "Diary Undo restores the deleted viewing");
            store.Restore(diaryBefore);
            trackerTab = "Stats";
            Navigate("History");
            await Settle();
            Check(
                Descendants(ContentPanel).OfType<TextBlock>().Any(t => t.Text == "Year in review"),
                "Stats includes a year in review"
            );
            Shot("year-review");
            SetLanguage("ar");
            await Settle();
            Check(
                PageTitle.Text == "السجل"
                    && SearchPlaceholder.Text == "ابحث في سجلك"
                    && Descendants(ContentPanel).OfType<TextBlock>().Any(t => t.Text == "حصاد السنة"),
                "New History labels have Arabic translations"
            );
            Shot("history-ar");
            SetLanguage("en");
            DiscoverNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle();
            Check(logging, "Tracker Discover opens the quick logging search");
            CloseLog();
            Navigate("Home");
            Check(page == "History", "Home leads to History in tracker mode");
            Navigate("Shows");
            Check(page == "History" && trackerTab == "Shows", "Shows opens the tracker's Shows tab");
            trackerTab = "Up next";
            Navigate("History");
            await Settle();
            Shot("tracker-only");
            var order = ContentPanel.Children.OfType<FrameworkElement>().ToList();
            var watchIndex = order.FindIndex(e => e.Tag as string == "HistoryWatchNext");
            var recentIndex = order.FindIndex(e => e.Tag as string == "HistoryRecentlyWatched");
            var statsIndex = order.FindIndex(e => e.Tag as string == "HistoryStatsStrip");
            Check(
                watchIndex >= 0 && recentIndex == watchIndex + 1 && statsIndex > recentIndex,
                "Tracker-only puts Recently watched directly after Up next and stats below both"
            );
            // U4: the tracker rail already holds Next, Diary, Discover and Stats, so the pill row offers only the other destinations.
            Check(
                !Descendants(ContentPanel)
                    .OfType<Button>()
                    .Any(b => b.Content as string is "Up next" or "Diary" or "Stats" or "Import"),
                "Tracker-only pills never repeat a rail destination"
            );
            trackerTab = "Shows";
            Render();
            await Settle();
            var selectedPill = Descendants(ContentPanel).OfType<Button>().First(b => b.Content as string == "Shows");
            Check(
                selectedPill.Background.ToString() == "#FFE4C084",
                "Tracker-only highlights the selected pill in gold"
            );
            trackerTab = "Up next";
            Render();
            await Settle();

            var continuation = TrackedShows().First(t => t.Status == "Watching" && t.Next != null);
            var continuationItem = continuation.Next!;
            var countBefore = store.ViewingHistory(continuationItem).Count;
            var continuationCard = TrackerRow(continuation, false);
            Descendants(continuationCard)
                .OfType<Button>()
                .Single(b =>
                    System.Windows.Automation.AutomationProperties.GetName(b)
                    == "Mark " + continuationItem.EpisodeLabel + " watched"
                )
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle(600);
            Check(
                smartPopup == null
                    && store.ViewingHistory(continuationItem).Count == countBefore + 1
                    && store.ViewingHistory(continuationItem)[0].WatchedAt.LocalDateTime.Date == DateTime.Today,
                "One-tap continuation logs exactly the next episode today without a catch-up prompt"
            );
            Shot("one-tap-reward");
            Descendants(undoBar!)
                .OfType<Button>()
                .First(b => b.Content as string == "Undo")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(
                store.ViewingHistory(continuationItem).Count == countBefore,
                "Continuation Undo restores the previous episode state"
            );
            settingsSection = "Profile";
            Navigate("Settings");
            await Settle();
            Shot("settings-mode");
            ChooseAppMode("Library");
            Check(HomeNav.Visibility == Visibility.Visible, "Switching back restores the library pages");
            // Smart check-offs, driven through the real popups.
            Button Find(string text) =>
                Descendants(((Border)smartPopup!.Child).Child)
                    .OfType<Button>()
                    .First(b => IconSet.Src(b.Content).StartsWith(text));
            void Press(string text)
            {
                Find(text).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            var all = store
                .EpisodesFor(key)
                .Where(i => i.Season > 0 && store.Aired(i, DateTimeOffset.UtcNow))
                .OrderBy(i => i.Season)
                .ThenBy(i => i.Episode)
                .ToList();
            var target = all[Math.Min(5, all.Count - 1)];
            store.Unwatch(all.Where(i => LibraryStore.Before(i, target) || i == target));
            trackerTab = "Up next";
            Navigate("History");
            await Settle();
            SmartCheckOff(target, "validation");
            Check(smartPopup != null, "Checking off a later episode asks about the earlier ones");
            Press("Yes, mark all");
            await Settle(200);
            Check(smartPopup != null, "Choosing all asks when the earlier ones were watched");
            Press("Don't remember");
            await Settle();
            var earlier = all.Where(i => LibraryStore.Before(i, target)).ToList();
            Check(
                earlier.All(store.EverWatched) && store.EverWatched(target),
                "The earlier episodes and the tapped one are all checked off"
            );
            Check(
                earlier.All(i => store.ViewingHistory(i).All(v => v.DateUnknown))
                    && earlier.All(i => store.LastActivity(i) == DateTimeOffset.MinValue),
                "Earlier ones are undated and never count as recent"
            );
            Check(!store.ViewingHistory(target)[0].DateUnknown, "The tapped episode is dated now");
            var middle = earlier[earlier.Count / 2];
            store.Unwatch([middle, earlier[earlier.Count / 2 + 1]]);
            Navigate("History");
            await Settle();
            Check(
                Descendants(ContentPanel).OfType<Button>().Any(b => (b.Content as string ?? "").StartsWith("Skipped ")),
                "Up next offers to fill a skipped episode"
            );
            Shot("smart-gap");
            var views = store.ViewingHistory(target).Count;
            SmartWatchedClick(target);
            Press("[[sync]]  Log a rewatch");
            await Settle();
            Check(store.ViewingHistory(target).Count == views + 1, "An already-watched check offers a rewatch");
            SmartWatchedClick(earlier[0]);
            Press("Remove from history");
            await Settle(200);
            Check(smartPopup != null, "Removing an early episode asks about the later ones");
            Press("Just ");
            await Settle();
            Check(
                !store.EverWatched(earlier[0]) && store.EverWatched(target),
                "Just this one leaves later episodes checked"
            );
            MarkWholeShow(target);
            Check(smartPopup != null, "Mark whole show asks when");
            Press("When they aired");
            await Settle();
            Check(ShowEpisodes(target).All(store.EverWatched), "Mark whole show checks off every released episode");
            Check(
                undoBar != null && Descendants(undoBar).OfType<TextBlock>().Any(t => t.Text.Contains("rating")),
                "Finishing the show offers a rating"
            );
            Shot("smart-finished");
            // Adding history: quick add, a pasted list, grouped review, approximate dates, no "recent" intrusion.
            trackerTab = "Import";
            Navigate("History");
            var wait = DateTime.UtcNow.AddSeconds(25);
            while (addTitles == null && DateTime.UtcNow < wait)
                await Task.Delay(200);
            await Settle(1200);
            Check(addTitles?.Count > 0, "Quick add shows popular titles to tap");
            Shot("add-quick");
            var popular = addTitles!.First(t => !t.IsMovie && TrackedGroup(t) == null);
            picks[popular.Id] = new(popular, 0, 0);
            pasteText = "Inception (2010)\nThe Office S2\nArrival";
            await ReviewPicks();
            Check(importPlan is { Titles.Count: 1 }, "Quick-add picks become a reviewable plan");
            importPlan = null;
            var before = store.Archive.Count;
            await BuildPlan("Pasted list", HistoryList.Parse(pasteText));
            await Settle(800);
            File.WriteAllText(
                Path.Combine(store.DataDirectory, "mode-paste.txt"),
                importPlan == null
                    ? "no plan " + Status.Text
                    : string.Join(
                        " | ",
                        importPlan
                            .Titles.Select(t => $"OK {t.Title.Name} ({t.Title.Year}) {t.Views.Count}")
                            .Concat(importPlan.Unmatched.Select(u => $"NO {u.Row.Title}: {u.Reason}"))
                    )
            );
            Check(
                importPlan is { } pasted && pasted.Titles.Any(t => t.Title.Name.Contains("Inception")),
                "A pasted list matches a movie by title and year"
            );
            var officeChoice = importChoices.FirstOrDefault(c => c.Key.Title == "The Office");
            Check(
                officeChoice.Value is { Count: > 1 },
                "An ambiguous title (“The Office”) offers its versions as choices"
            );
            Shot("add-review-choices");
            await ChooseImportTitle(officeChoice.Key, officeChoice.Value.First(t => t.Year == 2005));
            await Settle(500);
            Check(
                importPlan!.Titles.Any(t => t.Title.Name.Contains("Office") && t.Views.All(v => v.Season <= 2)),
                "Picking a version adds it, up to the season written in the list"
            );
            Check(!ImportReady(importPlan), "Undated entries need a “when” before adding");
            Shot("add-review");
            importWhen = "Over a period:2015-2017";
            ApplyImport(importPlan);
            await Settle(500);
            var office = store.Archive.Where(i => i.Title.Contains("Office") && !i.IsMovie).ToList();
            Check(
                office.Count > 0
                    && office
                        .SelectMany(store.ViewingHistory)
                        .All(v => v.Approximate && v.WatchedAt.Year is >= 2015 and <= 2017),
                "“Over a period” spreads the viewings across those years, marked approximate"
            );
            trackerTab = "Up next";
            Navigate("History");
            await Settle();
            Check(
                !Descendants(ContentPanel).OfType<TextBlock>().Any(t => t.Text.Contains("The Office")),
                "Back-filled shows stay out of Up next and Recently watched"
            );
            Shot("add-after");
            Navigate("Home");
            await Settle();
            File.WriteAllText(
                Path.Combine(store.DataDirectory, "mode-home.txt"),
                string.Join(
                    " | ",
                    Descendants(ContentPanel).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Length > 0)
                )
            );
            Check(
                !Descendants(ContentPanel).OfType<TextBlock>().Any(t => t.Text == "Inception"),
                "Back-filled movies don't appear on Home"
            );
        }
        catch (Exception error)
        {
            results.Add("FAIL: " + error);
        }
        File.WriteAllText(
            Path.Combine(store.DataDirectory, "mode-checks.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })
        );
    }

    sealed class TrackerHistoryFixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            var series = path.Contains("/series/");
            object Title(string id, string name, int year) =>
                new
                {
                    id,
                    name,
                    releaseInfo = year.ToString(),
                    description = "History validation fixture",
                    genres = new[] { "Drama" },
                    videos = series
                        ? Enumerable
                            .Range(1, 6)
                            .Select(n => new
                            {
                                season = (n - 1) / 3 + 1,
                                episode = (n - 1) % 3 + 1,
                                name = "Episode " + n,
                                released = year + "-01-01",
                            })
                            .ToArray()
                        : [],
                };
            object[] choices =
                series && path.Contains("The Office")
                    ? [Title("tt0386676", "The Office", 2005), Title("tt0290978", "The Office", 2001)]
                : series ? [Title("tt88888000", "History fixture show", 2019)]
                : path.Contains("Arrival") ? [Title("tt2543164", "Arrival", 2016)]
                : path.Contains("Inception") || path.Contains("tt1375666") ? [Title("tt1375666", "Inception", 2010)]
                : [Title("tt88888001", "History fixture movie", 2010)];
            var meta =
                path.Contains("tt0386676") ? Title("tt0386676", "The Office", 2005)
                : path.Contains("tt0290978") ? Title("tt0290978", "The Office", 2001)
                : path.Contains("tt2543164") ? Title("tt2543164", "Arrival", 2016)
                : choices[0];
            var json = path.Contains("/meta/")
                ? JsonSerializer.Serialize(new { meta })
                : JsonSerializer.Serialize(new { metas = choices });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
#endif
