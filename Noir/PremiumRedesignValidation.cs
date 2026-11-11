#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Noir;

public partial class MainWindow
{
    async Task ValidatePremiumRedesign()
    {
        var results = new List<string>();
        void Check(bool ok, string label)
        {
            if (!ok)
                throw new Exception(label);
            results.Add("PASS: " + label);
        }
        async Task Settle()
        {
            UpdateLayout();
            await Task.Delay(450);
            UpdateLayout();
        }
        try
        {
            var group = store.Archive.Where(i => !i.IsMovie).GroupBy(i => i.GroupKey).First().ToList();
            WindowState = WindowState.Normal;
            foreach (var size in new[] { (780, 520), (940, 650), (1320, 900) })
            {
                Width = size.Item1;
                Height = size.Item2;
                Navigate("Home");
                await Settle();
                Check(NavScroll.ScrollableWidth < 1, $"All navigation links fit at {size}");
                foreach (
                    var button in new[]
                    {
                        HomeNav,
                        ShowNav,
                        MovieNav,
                        WatchlistNav,
                        HistoryNav,
                        DiscoverNav,
                        DownloadsNav,
                        SettingsNav,
                    }
                )
                {
                    var rect = button.TransformToAncestor(LibraryRoot).TransformBounds(new Rect(button.RenderSize));
                    Check(
                        button.IsVisible
                            && rect.Left >= 0
                            && rect.Right <= SideRail.ActualWidth + 1
                            && rect.Top >= 0
                            && (size.Item2 < 650 || rect.Bottom <= LibraryRoot.ActualHeight + 1),
                        $"{button.Name} stays visible in the side rail at {size}"
                    );
                }
                var heroAction = Descendants(ContentPanel)
                    .OfType<Button>()
                    .FirstOrDefault(b =>
                        IconSet.Src(b.Content) is "[[play]]   Resume" or "[[play]]   Play" or "[[info]]   More info"
                    );
                Check(
                    heroAction != null
                        && heroAction.TransformToAncestor(LibraryRoot).Transform(new Point()).Y > TopBar.ActualHeight,
                    $"Hero actions clear the top navigation at {size}"
                );
                CaptureLibrary(Path.Combine(store.DataDirectory, $"premium-home-{size.Item1}.png"));
                OpenDetails(group);
                await Settle();
                ContentScroll.ScrollToVerticalOffset(500);
                await Settle();
                Check(
                    Descendants(ContentPanel).OfType<Button>().Any(b => b.Content as string == "Episodes"),
                    "Title page exposes its episode tab"
                );
                Check(Descendants(ContentPanel).OfType<ComboBox>().Count() == 0, "Season controls use visible pills");
                CaptureLibrary(Path.Combine(store.DataDirectory, $"premium-detail-{size.Item1}.png"));
            }
            Navigate("Home");
            await Settle();
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            {
                RoutedEvent = Mouse.PreviewMouseWheelEvent,
                Source = ContentPanel,
            };
            ContentPanel.RaiseEvent(wheel);
            await Task.Delay(450);
            Check(wheel.Handled && ContentScroll.VerticalOffset > 20, "Scrolling moves down smoothly");
            var down = ContentScroll.VerticalOffset;
            ContentPanel.RaiseEvent(
                new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
                {
                    RoutedEvent = Mouse.PreviewMouseWheelEvent,
                    Source = ContentPanel,
                }
            );
            await Task.Delay(450);
            Check(ContentScroll.VerticalOffset < down, "Scrolling reverses without jumping");
            foreach (var view in new[] { "Stats", "Collection", "Diary" })
            {
                trackerTab = view == "Collection" ? "Shows" : view;
                historyMonthTracker = "All time";
                Navigate("History");
                await Settle();
                CaptureLibrary(
                    Path.Combine(store.DataDirectory, "premium-history-" + view.ToLowerInvariant() + ".png")
                );
                Check(ContentPanel.Children.Count > 3, "History " + view + " renders saved library data");
            }
            settingsSection = "Playback";
            Navigate("Settings");
            await Settle();
            Check(Descendants(ContentPanel).OfType<CheckBox>().Any(), "Playback preferences remain available");
            CaptureLibrary(Path.Combine(store.DataDirectory, "premium-settings.png"));
            // Deterministic discovery data exercises the new hero, season tabs and future-episode guard.
            discoveryFactory = () => new DiscoveryClient(store.DataDirectory, new DiscoveryFixtureHandler());
            Navigate("Discover");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (discoverResults == null && DateTime.UtcNow < deadline)
                await Task.Delay(80);
            Check(discoverResults?.Titles.Count > 0, "Discover loads its spotlight and popular rails");
            await Settle();
            CaptureLibrary(Path.Combine(store.DataDirectory, "premium-discover.png"));
            var count = store.State.Viewings.Count;
            var show = discoverResults!.Titles.First(t => !t.IsMovie);
            await OpenDiscover(show);
            await Settle();
            Check(discoverPanel != null, "Discover opens the preview side panel first");
            CloseDiscoverPanel();
            await OpenDiscoverFull(show);
            await Settle();
            Check(
                Descendants(ContentPanel).OfType<Button>().Any(b => b.Content as string == "Find season downloads"),
                "Discover keeps whole-season download action"
            );
            discoverSeason = 2;
            Render();
            await Settle();
            CaptureLibrary(Path.Combine(store.DataDirectory, "premium-discover-season2.png"));
            File.WriteAllText(
                Path.Combine(store.DataDirectory, "season2-buttons.txt"),
                string.Join(
                    "\n",
                    Descendants(ContentPanel)
                        .OfType<Button>()
                        .Select(b => (b.Content as string ?? b.Content?.GetType().Name) + " enabled=" + b.IsEnabled)
                )
            );
            Check(
                Descendants(ContentPanel)
                    .OfType<Button>()
                    .Any(b => IconSet.Src(b.Content) == "[[download]] Download" && !b.IsEnabled),
                "Future episodes cannot be downloaded"
            );
            Check(store.State.Viewings.Count == count, "Browsing never creates watched history");
            CaptureLibrary(Path.Combine(store.DataDirectory, "premium-discover-detail.png"));
            // Sharpen W3 (A1-4): Hide spoilers also hides unwatched episode names on Discover's episode cards.
            var hide = store.State.HideSpoilers;
            store.State.HideSpoilers = true;
            discoverSeason = 1;
            Render();
            await Settle();
            var names = Descendants(ContentPanel).OfType<TextBlock>().Select(t => t.Text).ToList();
            Check(
                !names.Contains("One") && !names.Contains("Two") && names.Contains(T("Episode {0}", "الحلقة {0}", 1)),
                "Discover episode cards hide unwatched episode names when Hide spoilers is on"
            );
            store.State.HideSpoilers = false;
            Render();
            await Settle();
            Check(
                Descendants(ContentPanel).OfType<TextBlock>().Any(t => t.Text == "One"),
                "Discover episode cards show episode names when Hide spoilers is off"
            );
            store.State.HideSpoilers = hide;
            // A1-1: Full page from Discover keeps the search and the scroll position; Back returns to both.
            SearchBox.Text = "";
            Navigate("Discover");
            for (int i = 0; i < 40 && discoverResults == null; i++)
                await Task.Delay(100);
            await Settle();
            var target = Math.Min(ContentScroll.ScrollableHeight, 320);
            Check(target > 40, "Discover browse page is tall enough to test its scroll position");
            ContentScroll.ScrollToVerticalOffset(target);
            await Settle();
            await OpenDiscover(show);
            await Settle();
            var full =
                discoverPanel == null
                    ? null
                    : Descendants(discoverPanel)
                        .OfType<Button>()
                        .FirstOrDefault(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Full page");
            Check(full != null, "Discover side panel offers Full page");
            full!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            for (int i = 0; i < 40 && discoverTitle == null; i++)
                await Task.Delay(100);
            await Settle();
            Check(page == "Discover" && discoverTitle != null, "Full page opens the title inside Discover");
            var backButton = Descendants(ContentPanel)
                .OfType<Button>()
                .FirstOrDefault(b => IconSet.Src(b.Content).Contains("Back to Discover"));
            Check(backButton != null, "Title page has Back to Discover");
            backButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Settle();
            await Settle();
            Check(
                discoverTitle == null && Math.Abs(ContentScroll.VerticalOffset - target) < 24,
                $"Back from a full page returns to the same Discover scroll position ({ContentScroll.VerticalOffset:0} vs {target:0})"
            );
            // A1-2: the History date picker takes keyboard focus, keeps Tab inside, closes on Esc and gives focus back.
            var anchor = backButton.IsLoaded
                ? backButton
                : Descendants(ContentPanel).OfType<Button>().First(b => b.IsVisible && b.Focusable);
            anchor.Focus();
            await Settle();
            DateChooser(anchor, DateTime.Today, (_, _) => { });
            await Settle();
            var focused = Keyboard.FocusedElement as Button;
            Check(
                focused != null
                    && focused.Content as string == DateTime.Today.Day.ToString()
                    && !Descendants(ContentPanel).Contains(focused),
                "The date picker opens with focus on the selected day"
            );
            DependencyObject? up = focused;
            while (up != null && up is not StackPanel { Margin.Left: 14 })
                up = System.Windows.Media.VisualTreeHelper.GetParent(up) ?? LogicalTreeHelper.GetParent(up);
            Check(
                up != null && KeyboardNavigation.GetTabNavigation(up) == KeyboardNavigationMode.Cycle,
                "Tab stays inside the date picker"
            );
            focused!.RaiseEvent(
                new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(focused)!, 0, Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                }
            );
            await Settle();
            Check(
                ReferenceEquals(Keyboard.FocusedElement, anchor),
                "Esc closes the date picker and returns focus to the button that opened it"
            );
            // A1-3: a service failure says so instead of "Nothing found".
            discoveryFactory = () =>
                new DiscoveryClient(store.DataDirectory, new DiscoveryFixtureHandler(offline: true));
            SearchBox.Text = "sharpen offline probe " + Guid.NewGuid().ToString("N")[..6];
            searchDebounce?.Stop();
            await SearchDiscover();
            await Settle();
            var headings = Descendants(ContentPanel).OfType<TextBlock>().Select(t => t.Text).ToList();
            Check(
                headings.Contains(T("Discover is unavailable right now", "الاستكشاف غير متاح الآن"))
                    && !headings.Contains("Nothing found")
                    && headings.Any(h => h.Contains("temporarily unavailable")),
                "A Discover service failure shows its message, not Nothing found"
            );
            discoveryFactory = () => new DiscoveryClient(store.DataDirectory, new DiscoveryFixtureHandler());
            SearchBox.Text = "";
        }
        catch (Exception error)
        {
            results.Add("FAIL: " + error);
        }
        finally
        {
            CancelDiscover();
        }
        File.WriteAllText(
            Path.Combine(store.DataDirectory, "premium-redesign-checks.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })
        );
    }
}
#endif
