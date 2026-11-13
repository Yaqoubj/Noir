#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Noir;

public partial class MainWindow
{
    // --history-tracker-only: renders every History tab and the log flow, exercises log/undo/unwatch,
    // and saves screenshots beside the profile. Uses live Cinemeta unless --offline-fixture is passed.
    async Task ValidateHistoryTracker()
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
        void Shot(string name) => CaptureLibrary(Path.Combine(store.DataDirectory, "tracker-" + name + ".png"));
        try
        {
            if (Environment.GetCommandLineArgs().Contains("--offline-fixture"))
                discoveryFactory = () => new DiscoveryClient(store.DataDirectory, new DiscoveryFixtureHandler());
            WindowState = WindowState.Normal;
            Width = 1320;
            Height = 900;
            foreach (var tab in TrackerTabs.Concat(["Upcoming", "Import"]))
            {
                trackerTab = tab;
                Navigate("History");
                await Settle();
                Check(ContentPanel.Children.Count > 2, "History " + tab + " renders");
                Shot(tab.Replace(" & ", "-").Replace(" ", "-").ToLowerInvariant());
            }
            var before = store.State.Viewings.Values.Sum(v => v.Count);
            OpenLog(null);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (logResults == null && DateTime.UtcNow < deadline)
                await Task.Delay(100);
            await Settle(1500);
            Check(logResults?.Count > 0, "Log search shows titles to pick");
            Shot("log-search");
            var show = logResults!.First(t => !t.IsMovie);
            await OpenLogTitle(show);
            await Settle(1200);
            Check(logTitle != null && logTitle.Episodes.Count > 0, "Log title loads its episodes");
            Shot("log-title");
            var aired = logTitle!
                .Episodes.Where(e => e.Season > 0 && (e.AirsAt == null || e.AirsAt <= DateTimeOffset.UtcNow))
                .OrderBy(e => e.Season)
                .ThenBy(e => e.Episode)
                .Take(3)
                .ToList();
            logWhen = "When it aired";
            LogTitleItems(logTitle, aired.Select(e => (e.Season, e.Episode)).ToList(), "fixture");
            await Settle();
            var group = TrackedGroup(logTitle)!;
            Check(
                aired.All(e => group.Any(i => i.Season == e.Season && i.Episode == e.Episode && store.EverWatched(i))),
                "Logging marks the chosen episodes without any file"
            );
            Check(
                store.State.Viewings.Values.Sum(v => v.Count) == before + aired.Count,
                "Each logged episode adds one viewing"
            );
            LogTitleItems(logTitle, aired.Select(e => (e.Season, e.Episode)).ToList(), "again");
            Check(
                store.State.Viewings.Values.Sum(v => v.Count) == before + aired.Count,
                "Logging the same episodes again adds nothing"
            );
            Shot("log-title-checked");
            var first = group.First(i => i.Season == aired[0].Season && i.Episode == aired[0].Episode);
            var undo = store.Unwatch([first]);
            Check(!store.EverWatched(first), "Unchecking forgets the viewing");
            store.Restore(undo);
            Check(store.EverWatched(first), "Undo restores it");
            CloseLog();
            trackerTab = "Up next";
            Navigate("History");
            await Settle();
            Check(
                Descendants(ContentPanel)
                    .OfType<Button>()
                    .Any(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Mark ")),
                "Up next offers one-click check-offs"
            );
            Shot("up-next-after-log");
            var continued = TrackedShows().First(t => t.Status == "Watching" && t.Next != null);
            var recentItem = continued.Group.First(store.EverWatched);
            var recentBefore = store.Snapshot([recentItem]);
            store.LogRewatch(new(recentItem, DateTimeOffset.UtcNow), "Validation");
            trackerTab = "Up next";
            Navigate("History");
            await Settle();
            FrameworkElement Section(string tag) =>
                ContentPanel.Children.OfType<FrameworkElement>().First(e => e.Tag as string == tag);
            void PressUndo() =>
                Descendants(undoBar!)
                    .OfType<Button>()
                    .First(b => b.Content as string == "Undo")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var watch = Section("HistoryWatchNext");
            var recent = Section("HistoryRecentlyWatched");
            // U134: the totals, weekly goal and sync line sit under the History title, not below the page.
            var stats = Descendants(ContentPanel)
                .OfType<FrameworkElement>()
                .First(e => e.Tag as string == "HistoryStatsStrip");
            Check(
                Descendants(ContentPanel.Children[0]).Contains(stats)
                    && ContentPanel.Children.IndexOf(watch) > 0
                    && Descendants(ContentPanel).OfType<Button>().Any(b => IconSet.Plain(IconSet.Src(b.Content)) == "Export"),
                "U134/U136 the viewing totals and weekly goal sit in the History header, with Export beside Import"
            );
            Check(
                ContentPanel.Children.IndexOf(recent) == ContentPanel.Children.IndexOf(watch) + 1,
                "Recently watched is immediately after the Up next grid"
            );
            Check(
                Descendants(recent).OfType<StackPanel>().Any(p => p.Tag as string == recentItem.Id),
                "Recently watched contains the most recent viewing"
            );
            var latestCard = Descendants(recent).OfType<StackPanel>().First(p => p.Tag as string == recentItem.Id);
            Check(
                Descendants(latestCard)
                    .OfType<Button>()
                    .Any(b =>
                        System.Windows.Automation.AutomationProperties.GetName(b) is "Add a note…" or "Edit note"
                    ),
                "The newest viewing includes the inline Just watched tools"
            );
            var targetKey = continued.Group[0].GroupKey;
            var row = Descendants(watch)
                .OfType<Border>()
                .First(p => p.Tag as string == "HistoryTrackerRow:" + targetKey);
            var more = Descendants(row)
                .OfType<Button>()
                .First(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("More options for"));
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(
                !more.ContextMenu!.Items.OfType<MenuItem>()
                    .Any(m => m.Header as string == "Stop showing (I dropped this)"),
                "An Up next card's menu no longer repeats Stop showing (Viewing status holds Dropped)"
            );
            more.ContextMenu.IsOpen = false;
            StopShowing(continued.Group[0]);
            await Settle();
            Check(
                TrackedShows().Any(t => t.Group[0].GroupKey == targetKey && t.Status == "Dropped")
                    && !Descendants(ContentPanel)
                        .OfType<Border>()
                        .Any(p => p.Tag as string == "HistoryTrackerRow:" + targetKey),
                "Stop showing moves the show to Dropped and removes its Up next card"
            );
            PressUndo();
            await Settle();
            Check(
                TrackedShows().Any(t => t.Group[0].GroupKey == targetKey && t.Status == "Watching")
                    && Descendants(ContentPanel)
                        .OfType<Border>()
                        .Any(p => p.Tag as string == "HistoryTrackerRow:" + targetKey),
                "Undo restores the show in Up next"
            );
            StopShowing(recentItem);
            trackerTab = "Shows";
            showsFilter = "Dropped";
            Navigate("History");
            await Settle();
            var bringBack = Descendants(TrackerPoster(TrackedShows().First(t => t.Group[0].GroupKey == targetKey)))
                .OfType<Button>()
                .First(b => b.Content as string == "Bring back");
            bringBack.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle();
            Check(
                store.State.TitleStatuses.GetValueOrDefault(targetKey, "Automatic") == "Automatic",
                "Dropped shows offer Bring back with Automatic status"
            );
            PressUndo();
            store.SetStatus(recentItem, "Automatic");
            showsFilter = "All";
            trackerTab = "Up next";
            Navigate("History");
            await Settle();
            var likedBefore = store.State.Likes.ToHashSet();
            foreach (var key in continued.Group.Select(i => i.Id).Append(targetKey).Distinct())
                if (store.State.Likes.Contains(key))
                    store.SetLike(key, false);
            var favouriteMenu = ItemMenu(recentItem);
            favouriteMenu
                .Items.OfType<MenuItem>()
                .First(m => IconSet.Src(m.Header) == "[[heart]] Favourite")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Settle();
            Check(
                store.State.Likes.Contains(targetKey)
                    && Descendants(Section("HistoryFavourites"))
                        .OfType<StackPanel>()
                        .Any(p => p.Tag as string == targetKey),
                "Favourite adds to Likes and appears on the Favourites shelf"
            );
            PressUndo();
            Check(!store.State.Likes.Contains(targetKey), "Undo removes the new Favourite");
            var detailActions = DetailActions(recentItem, continued.Group, ActionButton("Back", () => { }));
            var tile = Descendants(detailActions)
                .OfType<Button>()
                .First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Favourite");
            tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(store.State.Likes.Contains(targetKey), "The title-page heart tile adds a Favourite");
            detailActions = DetailActions(recentItem, continued.Group, ActionButton("Back", () => { }));
            Descendants(detailActions)
                .OfType<Button>()
                .First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Remove from favourites")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!store.State.Likes.Contains(targetKey), "The title-page heart tile removes a Favourite");
            var diaryMenu = ItemMenu(recentItem);
            Check(
                diaryMenu.Items.OfType<MenuItem>().Any(m => IconSet.Src(m.Header) == "[[heart]] Favourite")
                    && Descendants(DiaryRow(recentItem, store.ViewingHistory(recentItem).First()))
                        .OfType<Button>()
                        .Any(b =>
                            System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("More options for")
                        ),
                "Diary entries expose Favourite in a visible menu"
            );
            foreach (var key in store.State.Likes.Except(likedBefore).ToList())
                store.SetLike(key, false);
            foreach (var key in likedBefore.Except(store.State.Likes))
                store.SetLike(key, true);
            var topFour = store.State.Favorites.ToList();
            store.SetLike(targetKey, true);
            store.SetFavorites(new[] { targetKey }.Concat(topFour.Where(k => k != targetKey)).Take(4));
            trackerTab = "Up next";
            Navigate("History");
            await Settle();
            Check(
                Descendants(Section("HistoryFavourites")).OfType<StackPanel>().First(p => p.Tag is string).Tag as string
                    == targetKey
                    && Descendants(Section("HistoryFavourites")).OfType<TextBlock>().Any(t => t.Text == "TOP 4"),
                "Favourites shows Top four first with its chip"
            );
            undoTimer?.Stop();
            if (undoBar?.Parent is Panel undoHost)
                undoHost.Children.Remove(undoBar);
            undoBar = null;
            async Task ShowSection(string tag)
            {
                var section = Section(tag);
                ContentScroll.ScrollToVerticalOffset(
                    Math.Max(0, section.TransformToAncestor(PageBody).Transform(new Point()).Y - 100)
                );
                await Settle();
            }
            ContentScroll.ScrollToTop();
            Shot("redesign-en");
            await ShowSection("HistoryRecentlyWatched");
            Shot("recently-watched-en");
            await ShowSection("HistoryFavourites");
            Shot("favourites-stats-en");
            SetLanguage("ar");
            trackerTab = "Up next";
            Navigate("History");
            await Settle();
            ContentScroll.ScrollToTop();
            Check(
                LibraryRoot.FlowDirection == FlowDirection.RightToLeft
                    && Descendants(ContentPanel).OfType<TextBlock>().Any(t => t.Text == "شاهدت مؤخراً"),
                "Arabic History renders with mirrored layout and translated Recently watched"
            );
            var arabicUpNext = Descendants(Section("HistoryWatchNext"))
                .OfType<TextBlock>()
                .Select(t =>
                    t.Inlines.Count > 0
                        ? string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text))
                        : t.Text
                )
                .ToList(); /* the episode line is built from runs */
            Check(
                LibraryRoot.FlowDirection == FlowDirection.RightToLeft
                    && arabicUpNext.Any(t => t.Contains("متبقي") || t.Contains("آخر حلقة صدرت"))
                    && !arabicUpNext.Any(t =>
                        t.Contains("episodes left") || t.Contains("Last episode out") || t.Contains("On your PC")
                    )
                    && arabicUpNext.Any(t => t.StartsWith("\u202AS")),
                "Arabic Up next shows translated status text and a left-to-right episode label under a right-to-left root"
                    + (
                        LibraryRoot.FlowDirection == FlowDirection.RightToLeft
                        && arabicUpNext.Any(t => t.Contains("متبقي") || t.Contains("آخر حلقة صدرت"))
                        && arabicUpNext.Any(t => t.StartsWith("\u202AS"))
                            ? ""
                            : " | seen: " + string.Join(" ¦ ", arabicUpNext.Take(30))
                    )
            );
            Shot("redesign-ar");
            await ShowSection("HistoryRecentlyWatched");
            Shot("recently-watched-ar");
            await ShowSection("HistoryFavourites");
            Shot("favourites-stats-ar");
            SetLanguage("en");
            store.SetFavorites(topFour);
            store.SetLike(targetKey, likedBefore.Contains(targetKey));
            store.Restore(recentBefore);
            trackerTab = "Up next";
            Navigate("History");
            await Settle();
            var next = NextToTrack(store.EpisodesFor(group[0].GroupKey), DateTimeOffset.UtcNow);
            if (next != null)
            {
                CheckOff([next], "next");
                Check(store.EverWatched(next), "Checking off Up next logs the episode");
                await Settle();
                Shot("after-check-off");
            }
            trackerTab = "Diary";
            Navigate("History");
            await Settle();
            Shot("diary-after-log");
            trackerTab = "Shows";
            Navigate("History");
            await Settle();
            Shot("shows-after-log");
            foreach (var w in new[] { 780, 940 })
            {
                Width = w;
                Height = 700;
                trackerTab = "Up next";
                Navigate("History");
                await Settle();
                Shot("up-next-" + w);
            }
            if (Environment.GetEnvironmentVariable("NOIR_IMPORT_SAMPLE") is { Length: > 0 } sample)
            {
                Width = 1320;
                Height = 900;
                trackerTab = "Import";
                Navigate("History");
                var count = store.State.Viewings.Values.Sum(v => v.Count);
                await ImportFile(sample);
                await Settle(800);
                Check(importPlan is { Titles.Count: > 0 }, "A Netflix export resolves into a reviewable plan");
                Shot("import-review");
                File.WriteAllText(
                    Path.Combine(store.DataDirectory, "tracker-import-plan.txt"),
                    string.Join(
                        "\n",
                        importPlan!
                            .Titles.Select(t =>
                                $"{t.Title.Name} [{t.Title.Id}] {string.Join(",", t.Views.Select(v => $"S{v.Season}E{v.Episode}@{v.At:yyyy-MM-dd}"))}"
                            )
                            .Concat(
                                importPlan.Unmatched.Select(u =>
                                    $"UNMATCHED {u.Row.Title} | {u.Row.EpisodeTitle} | {u.Reason}"
                                )
                            )
                    )
                );
                var planned = importPlan.Titles.Sum(t => t.Views.Count);
                ApplyImport(importPlan);
                await Settle();
                Check(
                    store.State.Viewings.Values.Sum(v => v.Count) == count + planned,
                    "Applying the plan adds every matched viewing"
                );
                Shot("import-done");
                await ImportFile(sample);
                var again = importPlan;
                if (again != null)
                    ApplyImport(again);
                Check(
                    store.State.Viewings.Values.Sum(v => v.Count) == count + planned,
                    "Re-importing the same export adds nothing"
                );
                trackerTab = "Up next";
                Navigate("History");
                await Settle();
                Shot("up-next-after-import");
                trackerTab = "Diary";
                Navigate("History");
                await Settle();
                Shot("diary-after-import");
            }
        }
        catch (Exception error)
        {
            results.Add("FAIL: " + error);
        }
        File.WriteAllText(
            Path.Combine(store.DataDirectory, "tracker-checks.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })
        );
    }
}
#endif
