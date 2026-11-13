using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Noir;

// Discover: a cinematic spotlight and rows when browsing, a poster grid for searches, and a
// full-bleed title page with season pills and episode cards.
public partial class MainWindow
{
    bool DiscoverCinematic =>
        page == "Discover"
        && (
            discoverTitle != null
            || SearchBox.Text.Trim().Length == 0
                && discoverResults is { Titles.Count: > 0 }
                && discoverQuery.Length == 0
                && discoverResultsFilter == discoverFilter
        );

    void RenderDiscover()
    {
        cinematic = DiscoverCinematic;
        ApplyPageLayout();
        PageTitle.Text = discoverTitle?.Name ?? (SearchBox.Text.Trim().Length > 0 ? "Results" : "Discover");
        if (discoverTitle != null)
        {
            RenderDiscoverDetail(discoverTitle);
            return;
        }
        HideSearchHeader();
        if (
            discoverResults == null
            || discoverQuery != SearchBox.Text.Trim()
            || discoverResultsFilter != discoverFilter
        )
        {
            RenderDiscoverLoading(SearchBox.Text.Trim());
            _ = SearchDiscover();
            return;
        }
        var token = discoverCancellation?.Token ?? BeginDiscover();
        var revision = discoverRevision;
        if (discoverResults.Titles.Count == 0)
        {
            ContentPanel.Children.Add(DiscoverFilters());
            // A service failure is not an empty result: say what failed, keep "Nothing found" for a search that really found nothing.
            if (discoverMessage.Length > 0)
                ContentPanel.Children.Add(
                    Heading(T("Discover is unavailable right now", "الاستكشاف غير متاح الآن"), discoverMessage)
                );
            else
                ContentPanel.Children.Add(Heading("Nothing found", "Try another title, or check your connection."));
            ContentPanel.Children.Add(ActionButton("Try again", () => _ = SearchDiscover(), true));
            return;
        }
        if (discoverQuery.Length == 0)
        {
            // A rotating spotlight of up to five titles with artwork, with dots to pick one.
            var spotlights = discoverResults.Titles.Where(t => t.BackgroundUrl.Length > 0).Take(5).ToList();
            if (spotlights.Count == 0)
                spotlights.Add(discoverResults.Titles[0]);
            discoverSpot %= spotlights.Count;
            ContentPanel.Children.Add(DiscoverSpotlight(spotlights, token, revision));
            ContentPanel.Children.Add(DiscoverFilters());
            var genres = discoverResults
                .Titles.SelectMany(t => t.Genres)
                .GroupBy(g => g)
                .Where(g => g.Count() >= 3)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(12)
                .ToList();
            if (discoverGenre != "All genres" && !genres.Contains(discoverGenre))
                discoverGenre = "All genres";
            if (genres.Count > 1)
            {
                var chips = Chips(new[] { "All genres" }.Concat(genres).ToArray(), discoverGenre, g =>
                {
                    discoverGenre = g;
                    Render();
                });
                chips.Margin = new(0, 0, 0, 4);
                ContentPanel.Children.Add(chips);
            }
            if (discoverMessage.Length > 0)
                ContentPanel.Children.Add(Body(discoverMessage, 12));
            var rest = discoverResults
                .Titles.Where(t => !spotlights.Contains(t) || discoverGenre != "All genres")
                .Where(t => discoverGenre == "All genres" || t.Genres.Contains(discoverGenre))
                .ToList();
            if (discoverGenre != "All genres")
                DiscoverGrid(
                    rest,
                    token,
                    revision,
                    T(discoverGenre)
                        + " · "
                        + T(
                            discoverFilter == "All" ? "popular now"
                            : discoverFilter == "Movies" ? "popular movies"
                            : "popular shows"
                        )
                );
            else if (discoverFilter == "All")
            {
                RenderTopTen();
                RenderPersonalRails(token, revision);
                ContentPanel.Children.Add(
                    Rail(
                        "Popular shows",
                        "Trending series, including anime.",
                        rest.Where(t => !t.IsMovie).Take(30).Select(t => DiscoverCard(t, token, revision)).ToList(),
                        () =>
                        {
                            discoverFilter = "TV shows";
                            _ = SearchDiscover();
                        }
                    )
                );
                ContentPanel.Children.Add(
                    Rail(
                        "Popular movies",
                        "What everyone is watching right now.",
                        rest.Where(t => t.IsMovie).Take(30).Select(t => DiscoverCard(t, token, revision)).ToList(),
                        () =>
                        {
                            discoverFilter = "Movies";
                            _ = SearchDiscover();
                        }
                    )
                );
                foreach (var genre in genres.Take(3))
                {
                    var g = genre;
                    var titles = rest.Where(t => t.Genres.Contains(g)).Take(30).ToList();
                    if (titles.Count >= 4)
                        ContentPanel.Children.Add(
                            Rail(
                                T(g),
                                string.Empty,
                                titles.Select(t => DiscoverCard(t, token, revision)).ToList(),
                                () =>
                                {
                                    discoverGenre = g;
                                    Render();
                                    ContentScroll.ScrollToTop();
                                }
                            )
                        );
                }
            }
            else
                DiscoverGrid(rest, token, revision, discoverFilter == "Movies" ? "Popular movies" : "Popular shows");
        }
        else
        {
            ContentPanel.Children.Add(DiscoverFilters());
            if (discoverMessage.Length > 0)
                ContentPanel.Children.Add(Body(discoverMessage, 12));
            DiscoverGrid(
                discoverResults.Titles,
                token,
                revision,
                $"{discoverResults.Titles.Count} results for “{discoverQuery}”"
            );
        }
        var credit = Body(
            "Title information and artwork: Cinemeta. Downloads use your AIOStreams and TorBox connection."
                + (discoverResults.Cached ? " Showing cached results." : ""),
            11
        );
        credit.Margin = new(0, 30, 0, 10);
        ContentPanel.Children.Add(credit);
    }

    // U33: a search shows its own heading ("2 results for ..."), so the page header ("Find your next watch / Results") is not drawn.
    void HideSearchHeader()
    {
        if (SearchBox.Text.Trim().Length == 0)
            return;
        LibraryHeader.Visibility = Visibility.Collapsed;
        ContentPanel.Margin = new Thickness(
            sidePad,
            TopBar.Height + Density.Pick(22, 30),
            sidePad,
            Density.Pick(48, 64)
        );
    }

    int discoverSpot;
    string discoverGenre = "All genres";
    System.Windows.Threading.DispatcherTimer? spotTimer;

    FrameworkElement DiscoverSpotlight(List<DiscoverTitle> spotlights, CancellationToken token, int revision)
    {
        var host = new Grid();
        var slot = new ContentControl
        {
            Content = DiscoverHero(spotlights[discoverSpot], token, revision, false),
            Focusable = false,
        };
        host.Children.Add(slot);
        if (spotlights.Count < 2)
            return host;
        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new(0, 0, 0, 30),
        };
        host.Children.Add(dots);
        void Show(int index)
        {
            discoverSpot = index;
            slot.Content = DiscoverHero(spotlights[index], token, revision, false);
            Motion.FadeIn(slot);
            for (int i = 0; i < dots.Children.Count; i++)
            {
                var d = (Button)dots.Children[i];
                d.Width = i == index ? 26 : 9;
                d.Background = Brush(i == index ? "#E4C084" : "#66FFFFFF");
            }
        }
        for (int i = 0; i < spotlights.Count; i++)
        {
            var index = i;
            var dot = new Button
            {
                Width = i == discoverSpot ? 26 : 9,
                Height = 9,
                Margin = new(4, 0, 4, 0),
                Padding = new(0),
                Background = Brush(i == discoverSpot ? "#E4C084" : "#66FFFFFF"),
                BorderThickness = new(0),
                Template = DotTemplate(),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = spotlights[i].Name,
            };
            System.Windows.Automation.AutomationProperties.SetName(
                dot,
                "Spotlight " + (i + 1) + ": " + spotlights[i].Name
            );
            dot.Click += (_, _) =>
            {
                spotTimer?.Stop();
                Show(index);
            };
            dots.Children.Add(dot);
        }
        // Advances every 9 seconds while Discover's browse view is showing and the pointer isn't on it.
        spotTimer?.Stop();
        spotTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
        spotTimer.Tick += (_, _) =>
        {
            if (page != "Discover" || discoverTitle != null || revision != discoverRevision || !host.IsLoaded)
            {
                spotTimer?.Stop();
                return;
            }
            if (
                !host.IsMouseOver
                && !host.IsKeyboardFocusWithin
                && IsActive
                && current == null
                && CarouselVisible(host)
                && !validationMode
            )
                Show((discoverSpot + 1) % spotlights.Count);
        };
        if (Motion.Enabled && !validationMode)
            spotTimer.Start();
        host.Unloaded += (_, _) => spotTimer?.Stop();
        return host;
    }

    static ControlTemplate DotTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4.5));
        border.SetBinding(
            Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent,
            }
        );
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    FrameworkElement DiscoverFilters()
    {
        var row = Chips(["All", "Movies", "TV shows"], discoverFilter, label =>
        {
            discoverFilter = label;
            _ = SearchDiscover();
        });
        ((FrameworkElement)row).Margin = new(0, cinematic ? 8 : 4, 0, 0);
        return row;
    }

    void DiscoverGrid(List<DiscoverTitle> titles, CancellationToken token, int revision, string heading)
    {
        ContentPanel.Children.Add(Heading(heading));
        var panel = new WrapPanel();
        ContentPanel.Children.Add(panel);
        var rendered = 0;
        var more = ActionButton("", () => { });
        more.Style = (Style)FindResource("Pill");
        more.Padding = new(20, 10, 20, 10);
        more.HorizontalAlignment = HorizontalAlignment.Center;
        more.Margin = new(0, 16, 0, 0);
        void Batch()
        {
            foreach (var t in titles.Skip(rendered).Take(30))
                panel.Children.Add(DiscoverCard(t, token, revision));
            rendered = Math.Min(rendered + 30, titles.Count);
            more.Content = $"Show more · {titles.Count - rendered} remaining";
            more.Visibility = rendered < titles.Count ? Visibility.Visible : Visibility.Collapsed;
        }
        Batch();
        StaggerCards(panel);
        more.Click += (_, _) => Batch();
        ContentPanel.Children.Add(more);
    }

    // Placeholder cards with a soft pulse while results load, instead of a line of text.
    void RenderDiscoverLoading(string query)
    {
        cinematic = false;
        ApplyPageLayout();
        HideSearchHeader();
        ContentPanel.Children.Add(DiscoverFilters());
        ContentPanel.Children.Add(
            Heading(query.Length > 0 ? $"Searching for “{query}”…" : "Finding popular movies & shows…")
        );
        var row = new WrapPanel();
        var sheen = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, .25) };
        sheen.GradientStops.Add(new(Color.FromRgb(25, 25, 28), 0));
        sheen.GradientStops.Add(new(Color.FromRgb(43, 41, 39), .5));
        sheen.GradientStops.Add(new(Color.FromRgb(25, 25, 28), 1));
        for (int i = 0; i < 12; i++)
        {
            var card = new StackPanel { Width = Density.PosterWidth, Margin = new(0, 0, Density.CardGap, 22) };
            card.Children.Add(
                new Border
                {
                    Height = Density.PosterHeight,
                    CornerRadius = new(10),
                    Background = sheen,
                }
            );
            card.Children.Add(
                new Border
                {
                    Height = 12,
                    Width = 120,
                    CornerRadius = new(6),
                    Background = Brush("#1C1C1F"),
                    Margin = new(0, 10, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                }
            );
            row.Children.Add(card);
        }
        Motion.Shimmer(row, sheen);
        ContentPanel.Children.Add(row);
    }

    FrameworkElement DiscoverCard(DiscoverTitle title, CancellationToken token, int revision, bool anywhere = false)
    {
        var body = new StackPanel { Width = Density.PosterWidth, Margin = new(0, 0, Density.CardGap, 8) };
        var picture = new Grid { Height = Density.PosterHeight, ClipToBounds = true };
        picture.Children.Add(Art(title.Poster, title.Name, Density.PosterHeight, true));
        var status = StatusLabels.For(
            title.Episodes.Select(e => new StatusEpisode(e.Season, e.Episode, e.AirsAt)),
            DateTimeOffset.UtcNow,
            topTen?.FindIndex(t => t.Id == title.Id) + 1 ?? 0
        );
        if (status.Length > 0)
            picture.Children.Add(
                new Border
                {
                    Background = Brush("#E4C084"),
                    CornerRadius = new(5),
                    Padding = new(7, 3, 7, 3),
                    Margin = new(8),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    IsHitTestVisible = false,
                    Child = Text(StatusText(status), 10, "#16120C", FontWeights.SemiBold),
                }
            );
        if (title.Rating.Length > 0)
            picture.Children.Add(
                new Border
                {
                    IsHitTestVisible = false,
                    Background = Brush("#E6101012"),
                    CornerRadius = new(6),
                    Padding = new(7, 3, 7, 3),
                    Margin = new(8),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = Text("★ " + title.Rating, 11, "#E4C084", FontWeights.SemiBold),
                }
            );
        var existing = store.Archive.Where(i => i.ImdbId == title.Id && i.IsMovie == title.IsMovie).ToList();
        if (existing.Count > 0)
            picture.Children.Add(
                new Border
                {
                    IsHitTestVisible = false,
                    Background = Brush("#E4C084"),
                    CornerRadius = new(6),
                    Padding = new(7, 3, 7, 3),
                    Margin = new(8),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Child = Text(
                        T(
                            existing.Any(CanPlay) ? "ON YOUR PC"
                            : store.State.Watchlist.Contains(existing[0].GroupKey) ? "IN MY LIST"
                            : "IN LIBRARY"
                        ),
                        10,
                        "#16120C",
                        FontWeights.Bold
                    ),
                }
            );
        var button = new Button
        {
            Content = picture,
            Padding = new(0),
            Background = Brush("Transparent"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = T("Explore {0}", "استكشف {0}", title.Name),
        };
        button.Click += (_, _) => _ = OpenDiscover(title);
        // Stream, Download and My List appear over the poster on hover or keyboard focus (U143).
        var quick = DiscoverQuickActions(title, existing);
        var frame = new Grid();
        frame.Children.Add(button);
        frame.Children.Add(quick);
        var edge = new Border
        {
            CornerRadius = new(10),
            ClipToBounds = true,
            Child = frame,
            BorderThickness = new(1.5),
            BorderBrush = Brush("#00000000"),
        };
        body.Children.Add(edge);
        RevealOnFocus(body, quick);
        var name = Text(title.Name, 14, weight: FontWeights.SemiBold);
        name.Margin = new(0, 9, 0, 2);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextWrapping = TextWrapping.NoWrap;
        body.Children.Add(name);
        body.Children.Add(
            Text(
                T(title.IsMovie ? "Movie" : "Series") + (title.Years.Length > 0 ? " · " + title.Years : ""),
                11,
                "#9F9A93"
            )
        );
        if (!File.Exists(title.Poster))
            _ = Load();
        async Task Load()
        {
            try
            {
                using var client = DiscoverClient();
                await client.Artwork(title, token);
                if (
                    token.IsCancellationRequested
                    || !anywhere && (revision != discoverRevision || page != "Discover")
                    || closing
                )
                    return;
                var source = await LoadImage(title.Poster, 384);
                if (
                    token.IsCancellationRequested
                    || !anywhere && (revision != discoverRevision || page != "Discover")
                    || closing
                )
                    return;
                if (source != null)
                {
                    picture.Children.RemoveAt(0);
                    var image = new Image { Source = source, Stretch = Stretch.UniformToFill };
                    picture.Children.Insert(0, image);
                    if (image.IsLoaded)
                        Motion.FadeIn(image);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception e)
            {
                store.Log(e);
            }
        }
        return Lift(body, edge);
    }

    // What you can do with a title you have just met, without opening it first.
    FrameworkElement DiscoverQuickActions(DiscoverTitle title, List<VideoItem> existing)
    {
        var overlay = new Grid
        {
            Opacity = 0,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new LinearGradientBrush(Colors.Transparent, Color.FromArgb(240, 8, 8, 10), 90),
            Tag = "DiscoverQuickActions",
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(10, 34, 10, 12) };
        overlay.Children.Add(row);
        void Small(string icon, string tip, Action act, bool primary = false)
        {
            var b = ActionButton(icon, act, primary);
            b.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(b, tip + " " + title.Name);
            b.Padding = new(primary ? 13 : 12, 7, primary ? 13 : 12, 7);
            b.Margin = new(0, 0, 7, 0);
            row.Children.Add(b);
        }
        var local = existing.FirstOrDefault(CanPlay);
        if (local != null)
            Small("[[play]]", T("Play"), () => _ = PlayVideo(local), true);
        else if (!TrackerOnly)
        {
            if (StreamOffered)
                Small("[[play]]", T("Stream now", "بث الآن"), () => DiscoverQuick(title, false), true);
            if (downloads != null)
                Small("[[download]]", T("Download", "تنزيل"), () => DiscoverQuick(title, true));
        }
        var listed = existing.Count > 0 && store.State.Watchlist.Contains(existing[0].GroupKey);
        Small(
            listed ? "[[check]]" : "[[plus]]",
            T(listed ? "In My List · click to remove" : "Add to My List"),
            () => ToggleDiscoveredWatchlist(title)
        );
        return overlay;
    }

    // Stream or download straight from a card or the spotlight. A show from a catalog row has no episode list yet,
    // so it is fetched first; the next episode to watch is the one that streams or downloads.
    async void DiscoverQuick(DiscoverTitle title, bool download)
    {
        try
        {
            var full = title;
            if (!title.IsMovie && title.Episodes.Count == 0)
            {
                Status.Text = T("Getting the episode list…", "جارٍ جلب قائمة الحلقات…");
                using var client = DiscoverClient();
                full = await client.Details(title, default);
                if (closing)
                    return;
                Status.Text = "";
            }
            var existing = store.Archive.FirstOrDefault(i => i.ImdbId == full.Id && i.IsMovie == full.IsMovie);
            var next = full.IsMovie ? null : NextToStream(full, existing);
            if (!full.IsMovie && next == null)
            {
                Status.Text = T("No released episode to play yet.", "لا توجد حلقة صادرة للتشغيل بعد.");
                return;
            }
            if (download)
                DiscoverDownload(full, next?.Season ?? 0, next?.Episode ?? 0, false);
            else
                DiscoverStream(full, next?.Season ?? 0, next?.Episode ?? 0);
        }
        catch (Exception e)
            when (e
                    is System.Net.Http.HttpRequestException
                        or TaskCanceledException
                        or IOException
                        or System.Text.Json.JsonException
            )
        {
            store.Log(e);
            Status.Text = T(
                "Couldn't load this title. Check your connection and try again.",
                "تعذّر تحميل هذا العنوان. تحقق من الاتصال وحاول مجددًا."
            );
        }
    }

    // Full-bleed artwork for a Discover title; the background is fetched if it isn't cached yet.
    FrameworkElement DiscoverHero(
        DiscoverTitle title,
        CancellationToken token,
        int revision,
        bool detail,
        Button? back = null
    )
    {
        var grid = new Grid
        {
            Height = HeroHeight(detail),
            ClipToBounds = true,
            Background = Brush("#151517"),
        };
        var artwork = new Grid { IsHitTestVisible = false };
        grid.Children.Add(artwork);
        void Paint()
        {
            artwork.Children.Clear();
            HeroArtwork(artwork, File.Exists(title.Background) ? title.Background : title.Poster, title.Id);
        }
        Paint();
        if (!File.Exists(title.Background))
            _ = Fetch();
        async Task Fetch()
        {
            try
            {
                using var client = DiscoverClient();
                await client.Artwork(title, token, true);
                if (!token.IsCancellationRequested && revision == discoverRevision && !closing)
                    Paint();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception e)
            {
                store.Log(e);
            }
        }
        var panel = new StackPanel
        {
            Margin = new(
                Density.Compact ? 24 : sidePad,
                0,
                Density.Compact ? 24 : sidePad,
                Density.Compact ? 38
                    : detail ? 42
                    : 56
            ),
            MaxWidth = Density.Pick(620, 700),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        panel.Children.Add(
            new Border
            {
                Background = Brush("#33E4C084"),
                BorderBrush = Brush("#66E4C084"),
                BorderThickness = new(1),
                CornerRadius = new(4),
                Padding = new(9, 3, 9, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = Text(
                    T(
                        detail
                            ? (title.IsMovie ? "MOVIE" : "SERIES")
                            : "SPOTLIGHT · " + (title.IsMovie ? "MOVIE" : "SERIES")
                    ),
                    11,
                    "#F0D3A0",
                    FontWeights.SemiBold
                ),
            }
        );
        var name = Text(title.Name, Density.HeroTitle(title.Name.Length > 24), weight: FontWeights.Bold);
        name.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        name.LineHeight = Density.HeroLine(title.Name.Length > 24);
        name.Margin = new(0, 14, 0, 10);
        panel.Children.Add(name);
        var facts = new TextBlock
        {
            FontSize = 14,
            Foreground = Brush("#D9D4CC"),
            TextWrapping = TextWrapping.Wrap,
        };
        if (title.Rating.Length > 0)
            facts.Inlines.Add(
                new System.Windows.Documents.Run("★ " + title.Rating + "  IMDb      ") { Foreground = Brush("#E4C084") }
            );
        facts.Inlines.Add(
            new System.Windows.Documents.Run(
                string.Join("   ·   ", new[] { title.Years }.Concat(title.Genres.Take(3)).Where(x => x.Length > 0))
            )
        );
        panel.Children.Add(facts);
        if (title.Summary.Length > 0)
        {
            var summary = Text(title.Summary, Density.HeroSummary, "#C9C4BD");
            summary.MaxHeight = Density.HeroSummaryMax;
            summary.LineHeight = Density.HeroSummaryLine;
            summary.TextTrimming = TextTrimming.WordEllipsis;
            summary.Margin = new(0, 12, 0, 0);
            summary.MaxWidth = 520;
            summary.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(summary);
        }
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new(0, Density.Pick(16, 26), 0, 0),
        };
        var existing = store.Archive.FirstOrDefault(i => i.ImdbId == title.Id && i.IsMovie == title.IsMovie);
        FrameworkElement? detailRow = null;
        if (detail)
        {
            detailRow = DiscoverDetailRow(title, existing);
            detailRow.Margin = new(0, Density.Pick(16, 26), 0, 0);
            if (back != null)
                PlaceHeroBack(grid, back);
        }
        else
        {
            // The spotlight offers what Noir does with a title: stream it, download it, save it. "Explore" is
            // "More info" and no longer the gold action; "Add to library" stays in the title page's More menu (U144).
            var group = existing != null ? store.EpisodesFor(existing.GroupKey) : [];
            var local = group.Any(CanPlay) ? ChoiceFor(group) : null;
            var acted = false;
            if (local != null)
            {
                actions.Children.Add(HeroGold("[[play]]   Play", () => _ = PlayVideo(local)));
                acted = true;
            }
            else if (!TrackerOnly && StreamOffered)
            {
                actions.Children.Add(HeroGold("[[play]]   Stream", () => DiscoverQuick(title, false)));
                actions.Children.Add(HeroButton("[[download]]  Download", () => DiscoverQuick(title, true), false));
                acted = true;
            }
            var listed = existing != null && store.State.Watchlist.Contains(existing.GroupKey);
            var listBtn = HeroButton(
                listed ? "[[check]]  In My List" : "[[plus]]  Add to My List",
                () => ToggleDiscoveredWatchlist(title),
                false
            );
            listBtn.ToolTip = T(listed ? "In My List · click to remove" : "Add to My List");
            actions.Children.Add(listBtn);
            var info = acted
                ? HeroButton("[[info]]   More info", () => _ = OpenDiscover(title), false)
                : BigButton("[[info]]   More info", () => _ = OpenDiscover(title), true);
            info.ToolTip = T("More about {0}", "المزيد عن {0}", title.Name);
            if (acted)
                actions.Children.Add(info);
            else
                actions.Children.Insert(0, info);
        }
        panel.Children.Add(detailRow ?? actions);
        grid.Children.Add(panel);
        FitHero(grid, panel, detail);
        return Density.Compact ? ContainedHero(grid, detail ? 6 : 0) : Bleed(grid, detail ? 6 : 0);
    }

    // U27: the Discover title page's one action row (inside the hero): watch action in gold, then the rest; library actions sit in More.
    FrameworkElement DiscoverDetailRow(DiscoverTitle title, VideoItem? existing)
    {
        var row = new FoldRow();
        Button Tile(string glyph, string label, Action action, string? tip = null, bool primary = false)
        {
            var b = DetailTile(glyph, label, action, tip, primary);
            row.Children.Add(b);
            return b;
        }
        var playable = existing != null && CanPlay(existing);
        if (title.IsMovie && !playable && StreamOffered)
        {
            Tile("[[play]]", T("Stream"), () => DiscoverStream(title, 0, 0), null, true);
            Tile("[[download]]", T("Download"), () => DiscoverDownload(title, 0, 0, false));
        }
        else if (title.IsMovie)
            Tile(
                playable ? "[[play]]" : "[[download]]",
                playable ? "Play" : "Find movie downloads",
                () =>
                {
                    if (playable)
                        _ = PlayVideo(existing!);
                    else
                        DiscoverDownload(title, 0, 0, false);
                },
                null,
                true
            );
        else if (NextToStream(title, existing) is { } next)
            Tile(
                "[[play]]",
                $"{T("Stream")} S{next.Season} E{next.Episode}",
                () => DiscoverStream(title, next.Season, next.Episode),
                null,
                true
            );
        var listed = existing != null && store.State.Watchlist.Contains(existing.GroupKey);
        Tile(
            listed ? "[[check]]" : "[[plus]]",
            listed ? T("In My List", "في قائمتي") : T("Add to My List", "أضف إلى قائمتي"),
            () => ToggleDiscoveredWatchlist(title),
            T(
                listed ? "In My List · click to remove" : "Add to My List",
                listed ? "في قائمتي · انقر للإزالة" : "أضف إلى قائمتي"
            )
        );
        if (existing != null)
        {
            Tile(HistoryLiked(existing) ? "[[heart-filled]]" : "[[heart]]", HistoryFavouriteLabel(existing), () =>
                ToggleHistoryFavourite(existing)
            );
            var mine = store.State.PersonalReviews.GetValueOrDefault(existing.Id)?.Rating ?? 0;
            Tile(
                "[[star-filled]]",
                mine > 0 ? new string('★', mine) : T("Rate", "قيّم"),
                () => OpenDiary(existing),
                T("Your rating and note", "تقييمك وملاحظتك")
            );
        }
        row.Children.Add(
            TrailerTile(
                existing
                    ?? new VideoItem
                    {
                        ImdbId = title.Id,
                        IsMovie = title.IsMovie,
                        Title = title.Name,
                    }
            )
        );
        var more = Tile("[[more]]", "More", () => { });
        row.More = more;
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu { FlowDirection = ContentPanel.FlowDirection };
            void Add(string header, Action run)
            {
                var m = new MenuItem { Header = header };
                m.Click += (_, _) => run();
                menu.Items.Add(m);
            }
            foreach (var hidden in row.Folded())
            {
                var tile = hidden;
                Add(tile.Tag as string ?? "", () => tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            }
            if (existing != null)
                Add(T("Open in library", "افتح في المكتبة"), () =>
                {
                    var group = store.EpisodesFor(existing.GroupKey);
                    Navigate(existing.IsMovie ? "Movies" : "Shows");
                    OpenDetails(group);
                });
            var inLibrary = existing != null && store.InLibrary(existing.GroupKey);
            Add(
                inLibrary
                    ? T("[[check]]  In your library", "[[check]]  في مكتبتك")
                    : T("[[plus]]  Add to library", "[[plus]]  أضف إلى المكتبة"),
                () =>
                {
                    var key = existing?.GroupKey ?? store.AddDiscovered(title, false)[0].GroupKey;
                    store.SetInLibrary(key, !inLibrary);
                    Render();
                    Status.Text = inLibrary ? "Removed from your library. History stays." : "Added to your library.";
                }
            );
            if (title.IsMovie)
            {
                var seen = existing != null && store.EverWatched(existing);
                Add(
                    seen ? T("[[check]]  Watched", "[[check]]  شوهد") : T("[[plus]]  I've seen it", "[[plus]]  شاهدته"),
                    () =>
                    {
                        if (seen)
                            OpenLog(title);
                        else
                            LogTitleItems(title, [(0, 0)], title.Name + " logged as watched");
                    }
                );
            }
            else
                Add(T("[[check]]  Track episodes you've seen", "[[check]]  تتبّع الحلقات التي شاهدتها"), () =>
                    OpenLog(title)
                );
            menu.PlacementTarget = more;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        return row;
    }

    void ToggleDiscoveredWatchlist(DiscoverTitle title)
    {
        var group = TrackedGroup(title);
        if (group is { Count: > 0 })
            ToggleWatchlist(group[0]);
        else
        {
            store.AddDiscovered(title, true);
            Render();
        }
    }

    // The show's next aired episode you haven't watched (the first one again when you've seen them all).
    KnownEpisode? NextToStream(DiscoverTitle title, VideoItem? existing)
    {
        var released = title
            .Episodes.Where(e =>
                e.Season > 0 && e.Episode > 0 && (e.AirsAt == null || e.AirsAt <= DateTimeOffset.UtcNow)
            )
            .OrderBy(e => e.Season)
            .ThenBy(e => e.Episode)
            .ToList();
        var seen =
            existing == null
                ? []
                : store
                    .EpisodesFor(existing.GroupKey)
                    .Where(store.EverWatched)
                    .Select(i => (i.Season, i.Episode))
                    .ToHashSet();
        // Details still loading: the pilot is the natural place to start.
        return released.FirstOrDefault(e => !seen.Contains((e.Season, e.Episode)))
            ?? released.FirstOrDefault()
            ?? (title.Episodes.Count == 0 ? new KnownEpisode { Season = 1, Episode = 1 } : null);
    }

    void DiscoverStream(DiscoverTitle title, int season, int episode)
    {
        if (!StreamSetUp())
            return;
        try
        {
            var group = store.AddDiscovered(title, false);
            var item = title.IsMovie ? group[0] : group.FirstOrDefault(i => i.Season == season && i.Episode == episode);
            if (item == null)
            {
                Status.Text = "This episode is missing from the title schedule. Refresh details.";
                return;
            }
            StreamItem(item);
        }
        catch (Exception e)
        {
            store.Log(e);
            Status.Text = "Couldn't prepare this title for streaming.";
        }
    }

    void DiscoverDownload(DiscoverTitle title, int season, int episode, bool whole)
    {
        try
        {
            var group = store.AddDiscovered(title, false);
            var item = title.IsMovie ? group[0] : group.FirstOrDefault(i => i.Season == season && i.Episode == episode);
            if (item == null)
            {
                Status.Text = "This episode is missing from the title schedule. Refresh details.";
                return;
            }
            FindDownloads(item, whole);
        }
        catch (Exception e)
        {
            store.Log(e);
            Status.Text = "Couldn't prepare this title for downloading.";
        }
    }

    void RenderDiscoverDetail(DiscoverTitle title)
    {
        var token = discoverCancellation?.Token ?? BeginDiscover();
        var revision = discoverRevision;
        var back = ActionButton("[[back]]  Back to Discover", BackToDiscoverResults);
        back.Background = Brush("Transparent");
        back.Foreground = Brush("#AAA6A0");
        back.Padding = new(0, 4, 0, 4);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        ContentPanel.Children.Add(DiscoverHero(title, token, revision, true, back));
        ContentPanel.Children.Add(WatchOptions(title.Id, title.IsMovie, title.Name));
        if (title.IsMovie)
        {
            ContentPanel.Children.Add(
                Body(
                    "Or download it: review the releases, quality and size before anything downloads. The movie joins your library when it finishes.",
                    13
                )
            );
            ContentPanel.Children.Add(MoreLikeThis(title.Id, true, title.Genres));
            return;
        }
        var seasons = title.Episodes.Select(e => e.Season).Distinct().Order().ToList();
        if (seasons.Count == 0)
        {
            ContentPanel.Children.Add(Body("No episode schedule is available for this title yet."));
            return;
        }
        if (!seasons.Contains(discoverSeason))
            discoverSeason = seasons[0];
        var existing = store.Archive.FirstOrDefault(i => i.ImdbId == title.Id && i.IsMovie == title.IsMovie);
        ContentPanel.Children.Add(
            UnderlineTabs(seasons.Select(s => "Season " + s).ToArray(), "Season " + discoverSeason, label =>
            {
                discoverSeason = int.Parse(label[7..]);
                Render();
            })
        );
        var released = title
            .Episodes.Where(e => e.Season == discoverSeason && (e.AirsAt == null || e.AirsAt <= DateTimeOffset.UtcNow))
            .ToList();
        var tools = new DockPanel { Margin = new(0, 10, 0, 16) };
        var seasonButton = ActionButton(T("Download season", "تنزيل الموسم"), () =>
            DiscoverDownload(title, discoverSeason, released[0].Episode, true)
        );
        seasonButton.IsEnabled = released.Count > 0;
        seasonButton.Padding = new(20, 11, 22, 11);
        DockPanel.SetDock(seasonButton, Dock.Right);
        tools.Children.Add(seasonButton);
        tools.Children.Add(
            Body(
                T(
                    "{0} of {1} episodes released. Choose one, or download the whole season; episodes you already have are skipped.",
                    "صدرت {0} من {1} حلقات. اختر حلقة أو نزّل الموسم كاملًا؛ الحلقات الموجودة لديك تُتخطّى.",
                    released.Count,
                    title.Episodes.Count(e => e.Season == discoverSeason)
                ),
                13
            )
        );
        ContentPanel.Children.Add(tools);
        var grid = new WrapPanel();
        ContentPanel.Children.Add(grid);
        foreach (var episode in title.Episodes.Where(e => e.Season == discoverSeason))
        {
            var available = episode.AirsAt == null || episode.AirsAt <= DateTimeOffset.UtcNow;
            var local =
                existing == null
                    ? null
                    : store
                        .EpisodesFor(existing.GroupKey)
                        .FirstOrDefault(i => i.Season == episode.Season && i.Episode == episode.Episode);
            var card = new Border
            {
                Width = 300,
                Margin = new(0, 0, 16, 16),
                CornerRadius = new(12),
                Background = Brush("#161618"),
                BorderBrush = Brush("#24FFFFFF"),
                BorderThickness = new(1),
                Padding = new(16, 14, 16, 14),
            };
            var stack = new StackPanel();
            card.Child = stack;
            var top = new DockPanel();
            if (available)
            {
                var seen = local != null && store.EverWatched(local);
                var ep = episode;
                var check = CheckCircle(
                    seen,
                    seen ? "Unmark this episode" : "I've seen this episode",
                    () =>
                    {
                        if (seen)
                            UncheckAll([local!], $"S{ep.Season:00} E{ep.Episode:00} unmarked");
                        else
                            LogTitleItems(
                                title,
                                [(ep.Season, ep.Episode)],
                                $"{title.Name} S{ep.Season:00} E{ep.Episode:00} logged"
                            );
                    },
                    30
                );
                check.Margin = new(10, 0, 0, 0);
                check.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(check, Dock.Right);
                top.Children.Add(check);
            }
            var number = Text(episode.Episode.ToString("00"), 30, "#3A362F", FontWeights.Bold);
            number.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            DockPanel.SetDock(number, Dock.Left);
            top.Children.Add(number);
            var state = Text(
                !available ? "Airs " + episode.AirsAt?.ToLocalTime().ToString("d MMM yyyy")
                    : local != null && CanPlay(local) ? "On your PC"
                    : local != null && store.EverWatched(local) ? "Watched"
                    : "Released",
                11,
                available ? "#E4C084" : "#9F9A93",
                FontWeights.SemiBold
            );
            state.HorizontalAlignment = HorizontalAlignment.Right;
            state.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(state);
            stack.Children.Add(top);
            var reveal = !store.State.HideSpoilers || local != null && store.EverWatched(local);
            var name = Text(
                reveal && episode.Title.Length > 0 ? episode.Title : T("Episode {0}", "الحلقة {0}", episode.Episode),
                14.5,
                "#F4F1EB",
                FontWeights.SemiBold
            );
            name.Height = 40;
            name.TextTrimming = TextTrimming.WordEllipsis;
            name.Margin = new(0, 4, 0, 10);
            stack.Children.Add(name);
            var playable = local != null && CanPlay(local);
            var streams = !playable && StreamOffered;
            var act = ActionButton(
                playable ? "[[play]] Play"
                    : streams ? "[[play]] Stream"
                    : "[[download]] Download",
                () =>
                {
                    if (playable)
                        _ = PlayVideo(local!);
                    else if (streams)
                        DiscoverStream(title, episode.Season, episode.Episode);
                    else
                        DiscoverDownload(title, episode.Season, episode.Episode, false);
                },
                playable || available
            );
            act.IsEnabled = available;
            act.HorizontalAlignment = HorizontalAlignment.Left;
            act.Padding = new(14, 7, 14, 7);
            if (streams)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(act);
                var get = ActionButton("[[download]]", () =>
                    DiscoverDownload(title, episode.Season, episode.Episode, false)
                );
                get.IsEnabled = available;
                get.ToolTip = T("Download to this PC");
                get.Padding = new(12, 7, 12, 7);
                get.Margin = new(8, 0, 0, 0);
                row.Children.Add(get);
                stack.Children.Add(row);
            }
            else
                stack.Children.Add(act);
            grid.Children.Add(Lift(card));
        }
        ContentPanel.Children.Add(MoreLikeThis(title.Id, false, title.Genres));
        ContentPanel.Children.Add(
            Body("Information: Cinemeta · Artwork and schedules are cached for later browsing.", 11)
        );
    }
}
