#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Noir;

public partial class MainWindow
{
    async Task ValidatePolish()
    {
        var records = new List<string>();
        var language = store.State.Language;
        var original = Density.Mode;
        void Check(bool condition, string label)
        {
            records.Add((condition ? "PASS: " : "FAIL: ") + label);
        }
        async Task Settle()
        {
            UpdateLayout();
            await Task.Delay(650);
            UpdateLayout();
        }
        Border HeroCard() => ViewElements(ContentPanel).OfType<Border>().First(b => Equals(b.Tag, "DensityHeroCard"));
        void Shot(string name)
        {
            ContentScroll.ScrollToTop();
            UpdateLayout();
            CaptureLibrary(Path.Combine(store.DataDirectory, name + ".png"));
            try
            {
                var origin = LibraryRoot.PointToScreen(new Point());
                var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice
                    ?? System.Windows.Media.Matrix.Identity;
                var size = transform.Transform(new Point(LibraryRoot.ActualWidth, LibraryRoot.ActualHeight));
                var bounds = new System.Drawing.Rectangle((int)origin.X, (int)origin.Y, (int)size.X, (int)size.Y);
                if (!System.Windows.Forms.SystemInformation.VirtualScreen.Contains(bounds))
                    return;
                Activate();
                using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
                using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
                bitmap.Save(
                    Path.Combine(store.DataDirectory, name + "-screen.png"),
                    System.Drawing.Imaging.ImageFormat.Png
                );
            }
            catch (Exception e)
            {
                records.Add("PASS: Render capture available; screen capture unavailable (" + e.GetType().Name + ")");
            }
        }
        try
        {
            WindowState = WindowState.Normal;
            SetLanguage("en");
            store.State.OnlineMetadata = false;
            store.State.AppMode = "Library";
            store.State.RailMode = "Labels";
            railChoice = null;
            SearchBox.Text = "";
            // The missing device setting must select Compact, independently of profile settings.
            var defaults = Path.Combine(store.DataDirectory, "polish-default-device");
            Directory.CreateDirectory(defaults);
            Density.Load(defaults);
            Check(Density.Compact, "A device without a saved density starts Compact");
            Density.Load(store.DataDirectory);
            SetDensity(UiDensity.Compact);
            var poster = Path.Combine(store.DataDirectory, "polish-poster.png");
            var backdrop = Path.Combine(store.DataDirectory, "polish-backdrop.png");
            File.WriteAllBytes(poster, HomeFixtureArt("The Night Archive", 1, false));
            File.WriteAllBytes(backdrop, HomeFixtureArt("The Night Archive", 1, true));
            var show = store.AddDiscovered(
                new()
                {
                    Id = "tt820001",
                    Name = "The Night Archive",
                    Type = "series",
                    Poster = poster,
                    Background = backdrop,
                    Genres = ["Drama"],
                    Summary =
                        "A hidden archive brings two strangers together in a city full of untold stories. Every letter opens a door to another life.",
                    Episodes =
                    [
                        new()
                        {
                            Season = 1,
                            Episode = 1,
                            Title = "The letter",
                            AirsAt = DateTimeOffset.UtcNow.AddDays(-10),
                        },
                        new()
                        {
                            Season = 1,
                            Episode = 2,
                            Title = "The crossing",
                            AirsAt = DateTimeOffset.UtcNow.AddDays(-3),
                        },
                    ],
                },
                true
            );
            store.State.LibraryAdds.Add(show[0].GroupKey);
            store.Record(show[0], 840, 2700);
            new HomeLayout().Save(store.DataDirectory);
            titleExtras = new()
            {
                ["tt820001"] = new()
                {
                    CheckedAt = DateTimeOffset.UtcNow,
                    Rating = "8.6",
                    Runtime = "45 min",
                },
            };
            foreach (var width in new[] { 1320, 1440 })
            {
                Width = width;
                Height = 900;
                Navigate("Home");
                await Settle();
                var hero = HeroCard();
                Check(
                    Math.Abs(RailColumn.ActualWidth - Density.Sidebar) < 1 && RailColumn.ActualWidth <= 216,
                    $"Compact sidebar matches token at {width}x900"
                );
                Check(
                    Math.Abs(TopBar.ActualHeight - Density.TopBar) < 1 && TopBar.ActualHeight <= 62,
                    $"Compact top bar matches token at {width}x900"
                );
                Check(
                    hero.ActualHeight <= ContentScroll.ViewportHeight * .60 && hero.CornerRadius.TopLeft >= 15,
                    $"Home hero is contained, rounded and at most 60% of viewport at {width}"
                );
                Check(
                    hero.Margin.Left == 0
                        && hero.Margin.Top >= TopBar.ActualHeight
                        && Math.Abs(ContentPanel.Margin.Left - Density.PageSide(width)) < 1,
                    $"Home hero uses page gutters and clears top bar at {width}"
                );
                var continues = ViewElements(ContentPanel)
                    .OfType<FrameworkElement>()
                    .Where(e => Equals(e.Tag, "HomeWideContinue"))
                    .ToList();
                Check(
                    continues.Count > 0 && continues.All(e => Math.Abs(e.ActualWidth - Density.ContinueWidth) < 1),
                    $"Continue widths match density tokens at {width}"
                );
                var probe = PosterCard(show);
                ContentPanel.Children.Add(probe);
                await Settle();
                Check(
                    Math.Abs(probe.ActualWidth - Density.PosterWidth) < 1,
                    $"Poster width matches density token at {width}"
                );
                ContentPanel.Children.Remove(probe);
                var heading = Heading("Density probe");
                Check(
                    ((TextBlock)heading.Children[0]).FontSize == Density.SectionTitle && Density.SectionTitle <= 19,
                    $"Section heading is compact at {width}"
                );
                Check(
                    ContentScroll.ScrollableWidth < 1
                        && ViewElements(ContentPanel)
                            .OfType<ScrollViewer>()
                            .Where(s => s.HorizontalScrollBarVisibility == ScrollBarVisibility.Hidden)
                            .All(s => s.ActualWidth <= ContentPanel.ActualWidth + 13),
                    $"Home rows stay within the page viewport at {width}"
                );
                Check(
                    ViewElements(hero)
                        .OfType<Button>()
                        .Where(b => b.ActualWidth > 0)
                        .All(b =>
                        {
                            var bounds = b.TransformToAncestor(hero).TransformBounds(new Rect(b.RenderSize));
                            return bounds.Left >= 0 && bounds.Right <= hero.ActualWidth + 1;
                        }),
                    $"Hero actions have no horizontal overflow at {width}"
                );
            }
            Shot("home-compact-en");
            settingsSection = "Appearance & language";
            Navigate("Settings");
            await Settle();
            Shot("settings-size-en");
            var comfortable = ViewElements(ContentPanel)
                .OfType<RadioButton>()
                .Single(b => Equals(b.Content, "Comfortable"));
            comfortable.IsChecked = true;
            await Settle();
            Check(
                Density.Mode == UiDensity.Comfortable
                    && Density.ButtonFont == 14
                    && Density.ButtonPadding == new Thickness(17, 11, 17, 11),
                "Settings applies Comfortable immediately with original button geometry"
            );
            Density.Load(store.DataDirectory);
            Check(Density.Mode == UiDensity.Comfortable, "Device density survives a reload without profile state");
            Navigate("Home");
            await Settle();
            var full = (FrameworkElement)ContentPanel.Children[0];
            Check(
                Math.Abs(RailColumn.ActualWidth - 236) < 1 && Math.Abs(TopBar.ActualHeight - 78) < 1,
                "Comfortable restores sidebar 236 and top bar 78 at runtime"
            );
            Check(
                !ViewElements(ContentPanel).OfType<Border>().Any(b => Equals(b.Tag, "DensityHeroCard"))
                    && full.Margin.Left == -sidePad
                    && full.Margin.Right == -sidePad
                    && full.Height >= 620,
                "Comfortable restores full-bleed Home and original carousel minimum"
            );
            Shot("home-comfortable-en");
            settingsSection = "Appearance & language";
            Navigate("Settings");
            await Settle();
            ViewElements(ContentPanel).OfType<RadioButton>().Single(b => Equals(b.Content, "Compact")).IsChecked = true;
            await Settle();
            Check(
                Density.Compact && Math.Abs(TopBar.ActualHeight - 60) < 1,
                "Settings switches back to Compact without restart"
            );
            SetLanguage("ar");
            Navigate("Home");
            await Settle();
            var arabicHero = HeroCard();
            var rail = SideRail
                .TransformToAncestor((FrameworkElement)LibraryRoot.Parent)
                .TransformBounds(new Rect(SideRail.RenderSize));
            Check(
                LibraryRoot.FlowDirection == FlowDirection.RightToLeft
                    && arabicHero.FlowDirection == FlowDirection.RightToLeft,
                "Arabic mirrors the root and contained hero"
            );
            Check(
                rail.Right >= LibraryRoot.ActualWidth - 1 && rail.Left > LibraryRoot.ActualWidth / 2,
                "Arabic puts the side rail on the right"
            );
            Shot("home-compact-ar");
            settingsSection = "Appearance & language";
            Navigate("Settings");
            await Settle();
            Check(
                ViewElements(ContentPanel)
                    .OfType<RadioButton>()
                    .Count(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith(T("Size"))) == 2,
                "Arabic Size choices have localized automation names"
            );
            Shot("settings-size-ar");
            Navigate("Home");
            SetLanguage("en");
            OpenDetails(show);
            await Settle();
            Check(
                Math.Abs(((FrameworkElement)HeroCard().Child).ActualHeight - HeroHeight(true)) < 1,
                "Compact detail hero matches the 52% / 360 minimum token"
            );
            var primary = ViewElements(ContentPanel)
                .OfType<Button>()
                .Where(b =>
                    IconSet.Src(b.Content)
                        is "[[play]] Play"
                            or "[[download]] Download"
                            or "[[play]] Resume"
                            or "[[play]] Stream"
                )
                .ToList();
            Check(
                primary.Count > 0 && primary.All(b => b.ActualWidth >= 90),
                "Primary episode actions keep at least 90 px width"
            );
            Shot("detail-compact-en");
            Navigate("Shows");
            SaveLibraryView("Poster", "M");
            Render();
            await Settle();
            Shot("library-compact-en");
            trackerTab = "Up next";
            Navigate("History");
            await Settle();
            Shot("history-compact-en");
            // Sheets: one chrome for every drawer and dialog, in both languages.
            void SheetShot(string name)
            {
                Shot(name);
                var root = (FrameworkElement)LibraryRoot.Parent;
                root.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)root.ActualWidth,
                    (int)root.ActualHeight,
                    96,
                    96,
                    System.Windows.Media.PixelFormats.Pbgra32
                );
                bitmap.Render(root);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(store.DataDirectory, name + ".png"));
                encoder.Save(output);
            }
            async Task SheetCheck(string tag, Action open, string titleStart, bool side, string? shot)
            {
                Activate();
                var opener = SearchBox;
                opener.Focus();
                open();
                await Settle();
                var sheet = FindSheet(titleStart);
                Check(sheet != null && sheet.IsOpen, $"{tag}: opens as a NoirSheet");
                if (sheet == null)
                    return;
                Check(sheet.Panel.CornerRadius.TopLeft >= 14, $"{tag}: panel radius is at least 14");
                var last = sheet.FooterButtons.Children.OfType<Button>().LastOrDefault();
                Check(
                    last != null && ReferenceEquals(last.Style, FindResource("Primary")),
                    $"{tag}: the primary action is the last footer button"
                );
                if (side)
                {
                    var bounds = sheet
                        .Panel.TransformToAncestor(sheet.Overlay)
                        .TransformBounds(new Rect(sheet.Panel.RenderSize));
                    var middle = (bounds.Left + bounds.Right) / 2;
                    if (Loc.Arabic)
                        Check(
                            middle < sheet.Overlay.ActualWidth / 2
                                && sheet.Panel.FlowDirection == FlowDirection.RightToLeft,
                            $"{tag}: Arabic side sheet sits on the left half and reads right to left"
                        );
                    else
                        Check(
                            middle > sheet.Overlay.ActualWidth / 2
                                && sheet.Panel.FlowDirection == FlowDirection.LeftToRight,
                            $"{tag}: side sheet sits on the right half"
                        );
                }
                if (shot != null)
                    SheetShot(shot);
                sheet.Overlay.RaiseEvent(
                    new System.Windows.Input.KeyEventArgs(
                        System.Windows.Input.Keyboard.PrimaryDevice,
                        PresentationSource.FromVisual(this)!,
                        Environment.TickCount,
                        System.Windows.Input.Key.Escape
                    )
                    {
                        RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent,
                    }
                );
                await Task.Delay(300);
                Check(!sheet.IsOpen && !OpenSheets.Contains(sheet), $"{tag}: Esc closes it");
                Check(
                    ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, opener),
                    $"{tag}: focus returns to the opener"
                );
            }
            SetLanguage("en");
            Navigate("Home");
            await Settle();
            Width = 1320;
            Height = 900;
            await Settle();
            await SheetCheck(
                "Edit Home (en)",
                () => CreateHomeLayoutEditor(HomeLayout.Defaults),
                "Edit Home",
                true,
                "sheet-edit-home-en"
            );
            await SheetCheck("Collections (en)", () => CollectionDialog(null), "Collections", true, null);
            await SheetCheck("Remove title (en)", () => RemoveTitleDialog(show[0]), "Remove", false, "sheet-remove-en");
            await SheetCheck(
                "Shortcuts (en)",
                () => ToggleShortcutSheet(false),
                "Shortcuts & tips",
                false,
                "sheet-shortcuts-en"
            );
            SetLanguage("ar");
            Navigate("Home");
            await Settle();
            await SheetCheck(
                "Edit Home (ar)",
                () => CreateHomeLayoutEditor(HomeLayout.Defaults),
                "تعديل الرئيسية",
                true,
                "sheet-edit-home-ar"
            );
            await SheetCheck("Collections (ar)", () => CollectionDialog(null), "Collections", true, null);
            await SheetCheck("Remove title (ar)", () => RemoveTitleDialog(show[0]), "Remove", false, null);
            await SheetCheck("Shortcuts (ar)", () => ToggleShortcutSheet(false), "الاختصارات والنصائح", false, null);
            SetLanguage("en");
        }
        catch (Exception e)
        {
            records.Add("FAIL: " + e);
        }
        finally
        {
            SetLanguage(language);
            SetDensity(original);
        }
        File.WriteAllText(
            Path.Combine(store.DataDirectory, "polish-checks.json"),
            JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true })
        );
    }
}
#endif
