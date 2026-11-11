using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Noir;

// Full-bleed artwork heroes: Home rotates through a few titles; title pages show one.
public partial class MainWindow
{
    double HeroHeight(bool detail) =>
        Math.Max(
            detail ? Density.DetailHeroMin : Density.HomeHeroMin,
            (ContentScroll.ViewportHeight > 0 ? ContentScroll.ViewportHeight : ActualHeight - 40)
                * (detail ? Density.DetailHeroFactor : Density.HomeHeroFactor)
        );

    FrameworkElement ContainedHero(FrameworkElement hero, double bottom = 0)
    {
        var card = new Border
        {
            Tag = "DensityHeroCard",
            CornerRadius = new(16),
            Background = Brush("#151517"),
            BorderBrush = Brush("#26FFFFFF"),
            BorderThickness = new(1),
            Margin = new(0, TopBar.Height + 8, 0, bottom),
            Child = hero,
        };
        card.SizeChanged += (_, _) =>
            card.Clip = new RectangleGeometry(new Rect(0, 0, card.ActualWidth, card.ActualHeight), 16, 16);
        return card;
    }

    // Reserve the top navigation area even on short screens and with wrapped, long titles.
    void FitHero(Grid hero, FrameworkElement content, bool detail)
    {
        void Fit()
        {
            var width = hero.ActualWidth > 0 ? hero.ActualWidth : Math.Max(780, ActualWidth);
            content.MaxWidth = Math.Min(
                detail ? Density.Pick(620, 860) : Density.Pick(620, 700),
                Math.Max(200, width - 2 * (Density.Compact ? 24 : sidePad))
            );
            content.Measure(new Size(width, double.PositiveInfinity));
            hero.Height = Density.Compact
                ? HeroHeight(detail)
                : Math.Max(HeroHeight(detail), content.DesiredSize.Height + TopBar.Height + 24);
        }
        hero.Loaded += (_, _) => Fit();
        hero.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
                Fit();
        };
    }

    Button RoundAction(string glyph, string tip, Action action, bool on = false)
    {
        var b = new Button
        {
            Width = Density.HeroRound,
            Height = Density.HeroRound,
            Padding = new(0),
            ToolTip = T(tip),
            Background = Brush(on ? "#E4C084" : "#40FFFFFF"),
            Foreground = Brush(on ? "#16120C" : "#F4F1EB"),
            Margin = new(10, 0, 0, 0),
        };
        b.FontSize = Density.Pick(16, 19);
        b.Content = IconSet.Has(glyph)
            ? IconSet.Content(glyph)
            : new TextBlock
            {
                Text = glyph,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        Motion.ButtonFeedback(b);
        b.Template = RoundTemplate();
        System.Windows.Automation.AutomationProperties.SetName(b, T(tip));
        b.Click += (_, _) => action();
        return b;
    }

    static ControlTemplate RoundTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(24));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1.2));
        border.SetValue(Border.BorderBrushProperty, Brush("#55FFFFFF"));
        border.SetBinding(
            Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent,
            }
        );
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#F4F1EB"), "Chrome"));
        template.Triggers.Add(hover);
        return template;
    }

    Button BigButton(string text, Action action, bool primary)
    {
        var b = ActionButton(text, action, primary);
        b.FontSize = Density.HeroButtonFont;
        b.Padding = Density.HeroButtonPadding;
        b.FontWeight = FontWeights.SemiBold;
        if (!primary)
        {
            b.Background = Brush("#40FFFFFF");
            b.Foreground = Brush("#F4F1EB");
        }
        b.Margin = new(0, 0, 12, 0);
        return b;
    }

    // U27/U28: the same gold primary button on every hero (Home and Discover used to differ).
    Button HeroGold(string text, Action action) => BigButton(text, action, true);

    // One tile of a title page's action row (gold when primary); the label is also its name in More when the row folds it away.
    Button DetailTile(string glyph, string label, Action action, string? tip = null, bool primary = false)
    {
        var plain = IconSet.Plain(T(label));
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = IconSet.Draw(glyph.Replace("[[", "").Replace("]]", "").Trim(), 16);
        icon.Margin = new(0, 0, 9, 0);
        if (!primary && icon is System.Windows.Shapes.Path ip)
        {
            ip.Stroke = Brush("#E4C084");
            if (System.Windows.Data.BindingOperations.IsDataBound(ip, System.Windows.Shapes.Shape.FillProperty))
                ip.Fill = Brush("#E4C084");
        }
        stack.Children.Add(icon);
        stack.Children.Add(
            Text(plain, 13.5, primary ? "#16120C" : "#E9E4DC", primary ? FontWeights.SemiBold : FontWeights.Normal)
        );
        var b = new Button
        {
            Content = stack,
            Background = Brush(primary ? "#E4C084" : "#1A1A1D"),
            Padding = new(16, 10, 18, 10),
            Margin = new(0, 0, 10, 0),
            ToolTip = tip ?? plain,
            Tag = plain,
        };
        b.Click += (_, _) => action();
        System.Windows.Automation.AutomationProperties.SetName(b, plain);
        return b;
    }

    // The Trailer button of a title page: hidden until a trailer is found, drawn like the other tiles.
    Button TrailerTile(VideoItem item)
    {
        var label = T("Trailer", "الإعلان");
        var skin = DetailTile("[[play]]", label, () => { });
        var b = TrailerAction(item);
        b.Style = null;
        b.Content = skin.Content;
        b.Background = skin.Background;
        b.Padding = skin.Padding;
        b.Margin = skin.Margin;
        b.ToolTip = label;
        b.Tag = label;
        System.Windows.Automation.AutomationProperties.SetName(b, label);
        return b;
    }

    // Back link at the top left of a title page hero.
    void PlaceHeroBack(Grid overlay, Button back)
    {
        back.Foreground = Brush("#D9D4CC");
        back.Margin = new(Density.Compact ? 24 : sidePad, TopBar.Height + Density.Pick(10, 14), 0, 0);
        back.VerticalAlignment = VerticalAlignment.Top;
        back.HorizontalAlignment = HorizontalAlignment.Left;
        Panel.SetZIndex(back, 6);
        overlay.Children.Add(back);
    }

    FrameworkElement CinematicHero(
        VideoItem item,
        List<VideoItem> group,
        bool continuing,
        bool justFinished = false,
        FrameworkElement? detailRow = null,
        Button? detailBack = null
    )
    {
        var detail = detailKey != null;
        var height = HeroHeight(detail);
        var grid = new Grid
        {
            Height = height,
            ClipToBounds = true,
            Background = Brush("#151517"),
        };
        var media = new Border { ClipToBounds = true };
        var artwork = new Grid();
        media.Child = artwork;
        HeroArtwork(artwork, File.Exists(item.Backdrop) ? item.Backdrop : item.Poster, item.GroupKey);
        grid.Children.Add(media);
        var overlay = new Grid();
        while (artwork.Children.Count > 1)
        {
            var shade = artwork.Children[1];
            artwork.Children.RemoveAt(1);
            overlay.Children.Add(shade);
        }
        grid.Children.Add(overlay);
        PreviewHost.Configure(store.DataDirectory, store.GetProgress, store.EverWatched, TmdbKey());
        PreviewHost.Attach(media, item, detail ? 1800 : 1500);
        PreviewHost.SetOverlay(media, overlay);
        var panel = new StackPanel
        {
            Margin = new(
                Density.Compact ? 24 : sidePad,
                0,
                Density.Compact ? 24 : sidePad,
                Density.Compact ? (detail ? 24 : 38)
                    : detail ? 42
                    : 70
            ),
            MaxWidth = Density.Pick(620, 700),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var pro = store.GetProgress(item);
        var playable = CanPlay(item);
        var eyebrow =
            justFinished ? (item.IsMovie ? "YOU JUST WATCHED" : "YOU FINISHED THIS SHOW")
            : continuing && !playable ? "UP NEXT · NOT ON THIS PC"
            : continuing && pro is { Completed: false, Position: >= 1 } ? "CONTINUE WATCHING"
            : continuing ? "UP NEXT"
            : detail ? (store.TitleStatus(group)).ToUpperInvariant()
            : "TONIGHT'S PICK";
        var tag = new Border
        {
            Background = Brush("#33E4C084"),
            BorderBrush = Brush("#66E4C084"),
            BorderThickness = new(1),
            CornerRadius = new(4),
            Padding = new(9, 3, 9, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = Text(T(eyebrow), 11, "#F0D3A0", FontWeights.SemiBold),
        };
        panel.Children.Add(tag);
        // The title's own logo artwork when Cinemeta has one; otherwise the name in display type.
        if (TitleLogo(item, Density.HeroLogoHeight(detail), Density.HeroLogoWidth) is { } logo)
        {
            logo.Margin = new(0, 18, 0, 18);
            panel.Children.Add(logo);
        }
        else
        {
            var title = Text(item.Title, Density.HeroTitle(item.Title.Length > 24), weight: FontWeights.Bold);
            title.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            title.LineHeight = Density.HeroLine(item.Title.Length > 24);
            title.Margin = new(0, 14, 0, 10);
            panel.Children.Add(title);
        }
        // One quiet line of facts instead of a row of badges.
        var rating = store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Rating ?? 0;
        panel.Children.Add(HeroFacts(item, group, rating));
        var showEpisode = !item.IsMovie && (continuing || detail);
        if (showEpisode)
        {
            var ep = Bidi(
                Text(
                    item.EpisodeLabel
                        + (
                            item.EpisodeTitle.Length > 0
                            && (!store.State.HideSpoilers || store.EverWatched(item) || pro is { Position: > 0 })
                                ? "  ·  " + item.EpisodeTitle
                                : ""
                        ),
                    15,
                    "#F4F1EB",
                    FontWeights.SemiBold
                )
            );
            ep.Margin = new(0, 14, 0, 0);
            panel.Children.Add(ep);
        }
        var summaryText = item.Summary.Length > 0 ? item.Summary : "";
        if (summaryText.Length > 0)
        {
            var summary = Bidi(Text(summaryText, Density.HeroSummary, "#C9C4BD"));
            summary.MaxHeight = Density.HeroSummaryMax;
            summary.LineHeight = Density.HeroSummaryLine;
            summary.TextTrimming = TextTrimming.WordEllipsis;
            summary.Margin = new(0, 12, 0, 0);
            summary.MaxWidth = 520;
            summary.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(summary);
        }
        if (pro is { Completed: false, Position: >= 1, Duration: > 0 })
        {
            var progress = new DockPanel
            {
                Margin = new(0, 18, 0, 0),
                MaxWidth = 420,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var left = Text(
                Loc.MinutesLeft(Math.Max(1, (int)Math.Ceiling((pro.Duration - pro.Position) / 60))),
                12,
                "#D9D4CC"
            );
            left.Margin = new(14, 0, 0, 0);
            DockPanel.SetDock(left, Dock.Right);
            progress.Children.Add(left);
            progress.Children.Add(
                new ProgressBar
                {
                    Value = pro.Position,
                    Maximum = pro.Duration,
                    Height = 4,
                    Width = 300,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = Brush("#55FFFFFF"),
                }
            );
            panel.Children.Add(progress);
        }
        if (!detail && continuing && CatchUpCard(group, true) is { } catchUp)
            panel.Children.Add(catchUp);
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new(0, Density.Pick(16, 26), 0, 0),
        };
        if (detail && detailRow != null)
        {
            detailRow.Margin = new(0, Density.Pick(16, 26), 0, 0);
            if (detailBack != null)
                PlaceHeroBack(overlay, detailBack);
        }
        else
        {
            if (justFinished)
            {
                actions.Children.Add(
                    HeroGold(rating > 0 ? "[[star-filled]]  Your rating" : "[[star-filled]]  Rate it", () =>
                        OpenDiary(item)
                    )
                );
                if (item.IsMovie && playable)
                    actions.Children.Add(HeroButton("[[sync]]  Watch again", () => _ = PlayVideo(item, true), false));
            }
            else if (
                !playable
                && StreamOffered
                && ActiveDownload(item) == null
                && System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId, @"^tt\d+$")
            )
            {
                // Not on this PC: stream it now, or download it to keep.
                actions.Children.Add(
                    HeroGold(
                        T(
                            pro is { Completed: false, Position: >= 1 }
                                ? "[[play]]   Resume stream"
                                : "[[play]]   Stream"
                        )
                            + (
                                continuing && !item.IsMovie && !(pro is { Completed: false, Position: >= 1 })
                                    ? " " + EpisodeTag(item)
                                    : ""
                            ),
                        () => StreamItem(item)
                    )
                );
                actions.Children.Add(HeroButton("[[download]]  Download", () => FindDownloads(item), false));
            }
            else
                actions.Children.Add(
                    HeroGold(
                        !playable ? (ActiveDownload(item) != null ? "Downloading…" : "[[download]]  Get it on this PC")
                            : pro is { Completed: false, Position: >= 1 } ? "[[play]]   Resume"
                            : continuing && !item.IsMovie ? T("[[play]]   Play") + " " + EpisodeTag(item)
                            : "[[play]]   Play",
                        () =>
                        {
                            if (playable)
                                _ = PlayVideo(item);
                            else if (ActiveDownload(item) != null)
                                Navigate("Downloads");
                            else
                                FindDownloads(item);
                        }
                    )
                );
            var listed = store.State.Watchlist.Contains(item.GroupKey);
            var list = HeroButton(
                listed ? "[[check]]  In My List" : "[[plus]]  Add to My List",
                () => ToggleWatchlist(item),
                false
            );
            list.ToolTip = T(listed ? "In My List · click to remove" : "Add to My List");
            actions.Children.Add(list);
            if (!detail)
                actions.Children.Add(HeroButton("[[info]]   More info", () => OpenDetails(group), false));
            else if (playable && pro is { Completed: false, Position: >= 1 })
                actions.Children.Add(HeroButton("[[sync]]   Start over", () => _ = PlayVideo(item, true), false));
            if (!justFinished)
                actions.Children.Add(
                    RoundAction(rating > 0 ? "[[star-filled]]" : "[[star]]", "Rate & note", () => OpenDiary(item))
                );
            if (detail)
                actions.Children.Add(TrailerAction(item));
        }
        foreach (FrameworkElement action in actions.Children)
            action.Margin = new(action.Margin.Left, action.Margin.Top, action.Margin.Right, 8);
        panel.Children.Add(detail && detailRow != null ? detailRow : actions);
        overlay.Children.Add(panel);
        FitHero(grid, panel, detail);
        panel.SizeChanged += (_, e) =>
        {
            if (e.HeightChanged)
                FitHeroContent();
        };
        void FitHeroContent()
        {
            panel.Measure(new Size(Math.Max(300, grid.ActualWidth - 2 * sidePad), double.PositiveInfinity));
            grid.Height = Density.Compact
                ? HeroHeight(detail)
                : Math.Max(HeroHeight(detail), panel.DesiredSize.Height + TopBar.Height + 24);
        }
        return Density.Compact ? (detail ? ContainedHero(grid, 6) : grid) : Bleed(grid, detail ? 6 : 0);
    }

    readonly Dictionary<string, Task<BitmapImage?>> heroPreloads = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Task<Color>> heroTints = new(StringComparer.OrdinalIgnoreCase);

    Task<BitmapImage?> PreloadHero(string path)
    {
        if (CachedImage(path, 1920) is { } cached)
            return Task.FromResult<BitmapImage?>(cached);
        if (heroPreloads.TryGetValue(path, out var pending))
            return pending;
        if (heroPreloads.Count >= 3)
            heroPreloads.Remove(heroPreloads.Keys.First());
        return heroPreloads[path] = Task.Run(() => DecodeImage(path, 1920));
    }

    Task<Color> HeroTint(string key, string path)
    {
        if (heroTints.TryGetValue(key, out var known))
            return known;
        if (heroTints.Count >= 128)
            heroTints.Remove(heroTints.Keys.First());
        return heroTints[key] = Task.Run(() =>
        {
            try
            {
                var small = DecodeImage(path, 32);
                if (small == null)
                    return Color.FromRgb(91, 66, 39);
                var bitmap = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                bitmap.Freeze();
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                var buckets = new long[64];
                var red = new long[64];
                var green = new long[64];
                var blue = new long[64];
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    int b = pixels[i],
                        g = pixels[i + 1],
                        r = pixels[i + 2];
                    var high = Math.Max(r, Math.Max(g, b));
                    var low = Math.Min(r, Math.Min(g, b));
                    if (high < 30 || low > 230)
                        continue;
                    int bin = (r / 64) * 16 + (g / 64) * 4 + b / 64;
                    var weight = 1 + high - low;
                    buckets[bin] += weight;
                    red[bin] += r * weight;
                    green[bin] += g * weight;
                    blue[bin] += b * weight;
                }
                var best = Array.IndexOf(buckets, buckets.Max());
                var count = buckets[best];
                return count == 0
                    ? Color.FromRgb(91, 66, 39)
                    : Color.FromRgb((byte)(red[best] / count), (byte)(green[best] / count), (byte)(blue[best] / count));
            }
            catch
            {
                return Color.FromRgb(91, 66, 39);
            }
        });
    }

    void HeroArtwork(Grid hero, string path, string key)
    {
        var fallback = new RadialGradientBrush(Color.FromRgb(62, 47, 35), Color.FromRgb(11, 11, 13))
        {
            Center = new(.8, .35),
            GradientOrigin = new(.8, .35),
            RadiusX = .9,
            RadiusY = 1,
        };
        hero.Background = fallback;
        // Stretch inside an explicit viewport: a top-aligned Image otherwise keeps its natural height.
        var viewport = new Grid { ClipToBounds = true, IsHitTestVisible = false };
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        viewport.Children.Add(image);
        hero.Children.Add(viewport);
        Motion.Drift(image);
        var side = new LinearGradientBrush { StartPoint = new(0, .5), EndPoint = new(1, .5) };
        side.GradientStops.Add(new(Color.FromArgb(255, 11, 11, 13), 0));
        side.GradientStops.Add(new(Color.FromArgb(245, 11, 11, 13), .32));
        side.GradientStops.Add(new(Color.FromArgb(210, 11, 11, 13), .55));
        side.GradientStops.Add(new(Color.FromArgb(35, 11, 11, 13), .75));
        side.GradientStops.Add(new(Color.FromArgb(8, 11, 11, 13), .90));
        hero.SizeChanged += (_, _) =>
        {
            var width = hero.ActualWidth;
            if (width <= 0)
                return;
            var edge = (sidePad + Math.Min(520, width - 2 * sidePad)) / width;
            side.GradientStops[1].Offset = Math.Clamp(edge - .23, .32, .62);
            side.GradientStops[2].Offset = Math.Clamp(edge + .02, .55, .82);
            side.GradientStops[3].Offset = Math.Min(.96, side.GradientStops[2].Offset + .20);
            side.GradientStops[4].Offset = Math.Min(1, side.GradientStops[3].Offset + .15);
        };
        var floor = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
        floor.GradientStops.Add(new(Color.FromArgb(38, 11, 11, 13), 0));
        floor.GradientStops.Add(new(Color.FromArgb(0, 11, 11, 13), .35));
        floor.GradientStops.Add(new(Color.FromArgb(20, 11, 11, 13), .63));
        floor.GradientStops.Add(new(Color.FromArgb(255, 11, 11, 13), 1));
        hero.Children.Add(new Border { Background = side, IsHitTestVisible = false });
        var glow = new Border { Opacity = .18, IsHitTestVisible = false };
        hero.Children.Add(glow);
        hero.Children.Add(new Border { Background = floor, IsHitTestVisible = false });
        void Tint(Color color)
        {
            glow.Background = new RadialGradientBrush(
                Color.FromArgb(150, color.R, color.G, color.B),
                Colors.Transparent
            )
            {
                Center = new(.68, .38),
                GradientOrigin = new(.68, .38),
                RadiusX = .7,
                RadiusY = .85,
            };
            if (hero.IsLoaded && cinematic)
                PageAmbient.Background = new LinearGradientBrush(
                    Color.FromArgb(30, color.R, color.G, color.B),
                    Colors.Transparent,
                    90
                );
        }
        var requested = false;
        var painted = false;
        var tintKey = key + "|" + path;
        async void Paint()
        {
            if (requested)
                return;
            requested = true;
            var source = await PreloadHero(path);
            heroPreloads.Remove(path);
            if (closing)
                return;
            if (source != null)
            {
                image.Source = Remember(path, 1920, source);
                painted = true;
                if (hero.IsLoaded)
                    Motion.FadeIn(image);
            }
            var tint = await HeroTint(tintKey, path);
            if (!closing)
                Tint(tint);
        }
        hero.Loaded += (_, _) =>
        {
            if (painted)
                Motion.FadeIn(image);
            Paint();
            if (heroTints.TryGetValue(tintKey, out var tint) && tint.IsCompletedSuccessfully)
                Tint(tint.Result);
        };
        Paint();
    }

    bool CarouselVisible(FrameworkElement host)
    {
        if (!host.IsLoaded || !host.IsVisible)
            return false;
        try
        {
            var bounds = host.TransformToAncestor(ContentScroll).TransformBounds(new Rect(host.RenderSize));
            return bounds.Bottom > 0 && bounds.Top < ContentScroll.ViewportHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    Action<VideoItem?>? homeHeroFocus;
    string homeHeroTitle = "";
    FrameworkElement? homeHeroCard;
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object> homeFocusCards = new();

    FrameworkElement FollowHomeHero(FrameworkElement card, Func<VideoItem> item)
    {
        if (page != "Home" || detailKey != null || homeFocusCards.TryGetValue(card, out _))
            return card;
        homeFocusCards.Add(card, new());
        var dwell = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        var leave = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        void Enter()
        {
            if (page != "Home" || detailKey != null)
                return;
            homeHeroCard = card;
            leave.Stop();
            dwell.Stop();
            dwell.Start();
        }
        void Leave()
        {
            if (card.IsKeyboardFocusWithin)
                return;
            if (homeHeroCard == card)
                homeHeroCard = null;
            dwell.Stop();
            leave.Stop();
            leave.Start();
        }
        dwell.Tick += (_, _) =>
        {
            dwell.Stop();
            if (page == "Home" && detailKey == null)
                homeHeroFocus?.Invoke(item());
        };
        leave.Tick += (_, _) =>
        {
            leave.Stop();
            if (page == "Home" && detailKey == null && homeHeroCard == null)
                homeHeroFocus?.Invoke(null);
        };
        card.MouseEnter += (_, _) => Enter();
        card.MouseLeave += (_, _) => Leave();
        card.GotKeyboardFocus += (_, _) => Enter();
        card.LostKeyboardFocus += (_, _) =>
        {
            if (!card.IsKeyboardFocusWithin)
                Leave();
        };
        card.Unloaded += (_, _) =>
        {
            dwell.Stop();
            leave.Stop();
        };
        return card;
    }

    void WireHomeRailCard(FrameworkElement card)
    {
        if (ViewElements(card).Any(e => homeFocusCards.TryGetValue(e, out _)))
            return;
        var names = ViewElements(card)
            .OfType<Button>()
            .Select(b => System.Windows.Automation.AutomationProperties.GetName(b))
            .ToList();
        var discover = topTen?.FirstOrDefault(t =>
            names.Any(n =>
                n.StartsWith("Top 10 number ", StringComparison.Ordinal)
                && n.EndsWith(": " + t.Name, StringComparison.Ordinal)
            )
        );
        if (discover != null)
        {
            FollowHomeHero(card, () => HomeHeroItem(discover));
            return;
        }
        // S6: was a scan of the whole archive per card (with two strings built per item); now one
        // lookup per page build. Same answer: the first archive item whose name matches.
        var lookup = HomeCardNames();
        var first = names.Select(n => lookup.GetValueOrDefault(n, int.MaxValue)).DefaultIfEmpty(int.MaxValue).Min();
        if (first != int.MaxValue)
        {
            var item = homeArchive![first];
            FollowHomeHero(card, () => item);
        }
    }

    // One archive copy per page build for Home's card wiring (Render clears it).
    List<VideoItem>? homeArchive;
    Dictionary<string, int>? homeCardNames,
        homeTitles;

    List<VideoItem> HomeArchive() => homeArchive ??= store.Archive;

    Dictionary<string, int> HomeCardNames()
    {
        if (homeCardNames != null)
            return homeCardNames;
        var archive = HomeArchive();
        homeCardNames = new();
        for (var n = 0; n < archive.Count; n++)
        {
            homeCardNames.TryAdd(archive[n].Title + " details", n);
            homeCardNames.TryAdd("Continue " + archive[n].Label, n);
        }
        return homeCardNames;
    }

    VideoItem? HomeItemTitled(string title)
    {
        var archive = HomeArchive();
        if (homeTitles == null)
        {
            homeTitles = new();
            for (var n = 0; n < archive.Count; n++)
                homeTitles.TryAdd(archive[n].Title, n);
        }
        return homeTitles.TryGetValue(title, out var at) ? archive[at] : null;
    }

    FrameworkElement HomeTitleMark(VideoItem item, double maxHeight, double maxWidth, double fontSize)
    {
        var host = new Grid
        {
            MaxHeight = maxHeight,
            MaxWidth = maxWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        fontSize =
            item.Title.Length > 24 ? Math.Min(fontSize, 40)
            : item.Title.Length > 12 ? Math.Min(fontSize, 50)
            : fontSize;
        host.MaxHeight = Math.Max(maxHeight, fontSize * 2.6);
        var text = Text(item.Title, fontSize, weight: FontWeights.Bold);
        text.TextWrapping = TextWrapping.Wrap;
        text.LineHeight = fontSize * 1.12;
        text.TextTrimming = TextTrimming.WordEllipsis;
        host.Children.Add(text);
        var logo = titleExtras?.GetValueOrDefault(item.ImdbId)?.Logo;
        if (File.Exists(logo))
        {
            var image = new Image
            {
                MaxHeight = maxHeight,
                MaxWidth = maxWidth,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                FlowDirection = FlowDirection.LeftToRight,
            };
            host.Children.Add(image);
            _ = Paint();
            async Task Paint()
            {
                await SetArtwork(image, logo!, maxWidth > 300 ? 900 : 384);
                if (image.Source != null)
                    text.Visibility = Visibility.Collapsed;
            }
        }
        return host;
    }

    FrameworkElement HomeFocusHero(VideoItem item, double height)
    {
        var grid = new Grid
        {
            Height = height,
            ClipToBounds = true,
            Background = Brush("#0B0B0D"),
        };
        HeroArtwork(grid, File.Exists(item.Backdrop) ? item.Backdrop : item.Poster, item.GroupKey);
        var panel = new StackPanel
        {
            Margin = new(
                Density.Compact ? 24 : sidePad,
                Density.Pick(20, 100),
                Density.Compact ? 24 : sidePad,
                Density.Pick(38, 80)
            ),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 560,
        };
        panel.Children.Add(Text(T("YOUR NEXT DISCOVERY", "اكتشافك القادم"), 11, "#E4C084", FontWeights.SemiBold));
        var mark = HomeTitleMark(
            item,
            Density.Pick(72, 120),
            Density.Pick(320, 420),
            item.Title.Length > 28 ? Density.Pick(40, 42) : Density.Pick(48, 60)
        );
        mark.Margin = new(0, 14, 0, 12);
        panel.Children.Add(mark);
        var extra = titleExtras?.GetValueOrDefault(item.ImdbId);
        var facts = new[]
        {
            item.Year > 0 ? item.Year.ToString() : "",
            extra?.Rating.Length > 0 ? "★ " + extra.Rating : "",
            item.Duration > 0 ? Loc.Runtime(item.Duration) : extra?.Runtime ?? "",
            string.Join(" · ", HomeDiscovery.Genres(item.Genres).Take(3).Select(T)),
        };
        panel.Children.Add(Text(string.Join(" · ", facts.Where(f => f.Length > 0)), 14, "#C7C2BC"));
        var summary = Text(item.Summary, Density.HeroSummary, "#C7C2BC");
        summary.LineHeight = Density.Pick(20, 23);
        summary.MaxHeight = Density.Pick(60, 92);
        summary.TextTrimming = TextTrimming.WordEllipsis;
        summary.Margin = new(0, 14, 0, 0);
        panel.Children.Add(summary);
        var info = ActionButton(T("More info", "المزيد من المعلومات"), () =>
        {
            var group = store.EpisodesFor(item.GroupKey);
            if (group.Count > 0)
                OpenDetails(group);
            else
                _ = OpenDiscover(
                    new()
                    {
                        Id = item.ImdbId,
                        Name = item.Title,
                        Type = item.IsMovie ? "movie" : "series",
                        Poster = item.Poster,
                        Background = item.Backdrop,
                        Summary = item.Summary,
                        Genres = HomeDiscovery.Genres(item.Genres).ToList(),
                    }
                );
        });
        info.HorizontalAlignment = HorizontalAlignment.Left;
        info.Margin = new(0, 24, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(
            info,
            T("More info about {0}", "المزيد عن {0}", item.Title)
        );
        panel.Children.Add(info);
        grid.Children.Add(panel);
        return grid;
    }

    // Home stays at a fixed height while a rail supplies the spotlight.
    FrameworkElement HeroCarousel(List<(VideoItem Item, List<VideoItem> Group, bool Continuing, bool Finished)> slides)
    {
        homePosterTitles = HomeLayout.Load(store.DataDirectory).ShowPosterTitles;
        var host = new Grid { Height = Math.Max(Density.CarouselMin, HeroHeight(false)), ClipToBounds = true };
        var index = 0;
        FrameworkElement? shown = null;
        bool following = false;
        DateTimeOffset focusAt = DateTimeOffset.MinValue;
        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new(0, 0, 0, 26),
        };
        void Paint(FrameworkElement slide, bool animate)
        {
            slide.Margin = new(0);
            var old = shown;
            shown = slide;
            host.Children.Insert(0, slide);
            if (old != null && animate && Motion.Enabled)
            {
                Panel.SetZIndex(old, 1);
                Panel.SetZIndex(slide, 0);
                Motion.FadeIn(slide);
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(450));
                fade.Completed += (_, _) =>
                {
                    old.BeginAnimation(OpacityProperty, null);
                    host.Children.Remove(old);
                };
                old.Unloaded += (_, _) => old.BeginAnimation(OpacityProperty, null);
                old.BeginAnimation(OpacityProperty, fade);
            }
            else if (old != null)
                host.Children.Remove(old);
        }
        void Show(int next, bool animate)
        {
            index = (next + slides.Count) % slides.Count;
            var s = slides[index];
            homeHeroTitle = s.Item.Title;
            Paint(CinematicHero(s.Item, s.Group, s.Continuing, s.Finished), animate);
            var upcoming = slides[(index + 1) % slides.Count].Item;
            _ = PreloadHero(File.Exists(upcoming.Backdrop) ? upcoming.Backdrop : upcoming.Poster);
            for (int i = 0; i < dots.Children.Count; i++)
            {
                var d = (Button)dots.Children[i];
                d.Width = i == index ? 30 : 16;
                d.Background = Brush(i == index ? "#F4F1EB" : "#4DFFFFFF");
            }
        }
        void FocusTitle(VideoItem? title)
        {
            if (title == null)
            {
                if (following && DateTimeOffset.UtcNow - focusAt >= TimeSpan.FromSeconds(2.8))
                {
                    following = false;
                    Show(index, true);
                }
                return;
            }
            focusAt = DateTimeOffset.UtcNow;
            following = true;
            if (homeHeroTitle == title.Title)
                return;
            homeHeroTitle = title.Title;
            Paint(HomeFocusHero(title, host.Height), true);
        }
        homeHeroFocus = FocusTitle;
        for (int i = 0; i < slides.Count; i++)
        {
            var target = i;
            var dot = ActionButton("", () =>
            {
                following = false;
                Show(target, true);
            });
            dot.Height = 6;
            dot.Width = 16;
            dot.Padding = new(0);
            dot.Margin = new(3, 0, 3, 0);
            dot.ToolTip = slides[i].Item.Title;
            System.Windows.Automation.AutomationProperties.SetName(dot, T("Show {0}", "عرض {0}", slides[i].Item.Title));
            dots.Children.Add(dot);
        }
        Panel.SetZIndex(dots, 5);
        host.Children.Add(dots);
        Show(0, false);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        timer.Tick += (_, _) =>
        {
            if (
                !following
                && slides.Count > 1
                && !host.IsMouseOver
                && !host.IsKeyboardFocusWithin
                && IsActive
                && current == null
                && CarouselVisible(host)
            )
                Show(index + 1, true);
        };
        host.Loaded += (_, _) =>
        {
            if (Motion.Enabled && !validationMode)
                timer.Start();
        };
        host.Unloaded += (_, _) =>
        {
            timer.Stop();
            if (homeHeroFocus == FocusTitle)
            {
                homeHeroFocus = null;
                homeHeroCard = null;
            }
        };
        return Density.Compact ? ContainedHero(host) : Bleed(host);
    }
}

// U27: a single-line row for a title page's actions. What does not fit is hidden (not wrapped) and offered in the More menu, which always stays on the row.
public sealed class FoldRow : Panel
{
    public FrameworkElement? More;
    readonly HashSet<UIElement> folded = new();

    public IEnumerable<FrameworkElement> Folded() =>
        InternalChildren.OfType<FrameworkElement>().Where(c => folded.Contains(c) && c != More);

    protected override Size MeasureOverride(Size available)
    {
        var items = InternalChildren
            .OfType<UIElement>()
            .Where(c => c != More && c.Visibility != Visibility.Collapsed)
            .ToList();
        foreach (var c in items)
            c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        More?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var moreWidth = More?.DesiredSize.Width ?? 0;
        var max = double.IsInfinity(available.Width) ? double.MaxValue : available.Width;
        folded.Clear();
        var used = moreWidth;
        var first = true;
        var full = false;
        foreach (var c in items)
        {
            if (full || !first && used + c.DesiredSize.Width > max)
            {
                full = true;
                folded.Add(c);
            }
            else
            {
                used += c.DesiredSize.Width;
            }
            first = false;
        }
        var height = Math.Max(
            items.Where(c => !folded.Contains(c)).Select(c => c.DesiredSize.Height).DefaultIfEmpty().Max(),
            More?.DesiredSize.Height ?? 0
        );
        return new Size(Math.Min(used, max), height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0;
        foreach (var c in InternalChildren.OfType<UIElement>())
        {
            if (c == More || c.Visibility == Visibility.Collapsed)
                continue;
            if (folded.Contains(c))
            {
                if (c.Visibility != Visibility.Hidden)
                    c.Visibility = Visibility.Hidden;
                c.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            if (c.Visibility == Visibility.Hidden)
                c.Visibility = Visibility.Visible;
            c.Arrange(new Rect(x, 0, c.DesiredSize.Width, final.Height));
            x += c.DesiredSize.Width;
        }
        More?.Arrange(new Rect(x, 0, More.DesiredSize.Width, final.Height));
        return final;
    }
}
