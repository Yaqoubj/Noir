using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Noir;

public sealed class TitleExtra
{
    public string Rating { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string Logo { get; set; } = "";
    public DateTimeOffset CheckedAt { get; set; }
    public DateTimeOffset RetryAfter { get; set; }
}

// Home in the style of the big streaming hubs: title-logo heroes with the IMDb score, services you
// have connected, and this week's Top 10 with large outlined numbers.
public partial class MainWindow
{
    static readonly HttpClient extrasHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    Dictionary<string, TitleExtra>? titleExtras;
    readonly HashSet<string> extrasPending = [];
    DispatcherTimer? extrasRender;
    string ExtrasPath => System.IO.Path.Combine(store.DataDirectory, "title-extras.json");

    // Cinemeta's logo, IMDb rating and runtime for a library title, cached for two weeks.
    TitleExtra? ExtraFor(VideoItem item)
    {
        var id = item.ImdbId;
        if (!Regex.IsMatch(id, @"^tt\d+$"))
            return null;
        if (titleExtras == null)
        {
            try
            {
                titleExtras = File.Exists(ExtrasPath)
                    ? JsonSerializer.Deserialize<Dictionary<string, TitleExtra>>(File.ReadAllText(ExtrasPath)) ?? new()
                    : new();
            }
            catch
            {
                titleExtras = new();
            }
        }
        var known = titleExtras.GetValueOrDefault(id);
        if (
            (
                known == null
                || known.CheckedAt < DateTimeOffset.UtcNow.AddDays(-14)
                || known.Rating.Length == 0 && known.Runtime.Length == 0 && known.Logo.Length == 0
            )
            && (known == null || known.RetryAfter <= DateTimeOffset.UtcNow)
            && store.State.OnlineMetadata
            && !closing
            && extrasPending.Add(id)
        )
            _ = FetchExtra(id, item.IsMovie ? "movie" : "series");
        return known;
    }

    async Task FetchExtra(string id, string type)
    {
        var extra = new TitleExtra { CheckedAt = DateTimeOffset.UtcNow };
        try
        {
            using var doc = JsonDocument.Parse(
                await extrasHttp.GetStringAsync($"https://v3-cinemeta.strem.io/meta/{type}/{id}.json")
            );
            if (doc.RootElement.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
            {
                string S(string name) =>
                    meta.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                        ? v.GetString() ?? ""
                        : "";
                extra.Rating = S("imdbRating");
                extra.Runtime = S("runtime");
                if (S("logo") is { Length: > 0 } url && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = await extrasHttp.GetByteArrayAsync(url);
                    var target = System.IO.Path.Combine(store.DataDirectory, "art", "logo-" + id + ".png");
                    // Only keep files that decode as images; a missing logo falls back to the title text.
                    await Task.Run(async () =>
                    {
                        await imageDecodeSlots.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            using var probe = new MemoryStream(bytes);
                            var frame = BitmapDecoder
                                .Create(probe, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
                                .Frames[0];
                            frame.Freeze();
                            if (frame.PixelWidth < 20)
                                throw new IOException("Logo too small");
                        }
                        finally
                        {
                            imageDecodeSlots.Release();
                        }
                    });
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                    var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        await File.WriteAllBytesAsync(temp, bytes);
                        File.Move(temp, target, true);
                    }
                    finally
                    {
                        if (File.Exists(temp))
                            File.Delete(temp);
                    }
                    extra.Logo = target;
                }
            }
            else
                throw new JsonException("Missing title metadata");
            if (extra.Rating.Length == 0 && extra.Runtime.Length == 0 && extra.Logo.Length == 0)
                throw new JsonException("Empty title metadata");
        }
        catch (Exception e)
            when (e
                    is HttpRequestException
                        or TaskCanceledException
                        or JsonException
                        or IOException
                        or NotSupportedException
                        or FileFormatException
            )
        {
            extra = titleExtras!.GetValueOrDefault(id) ?? new();
            extra.RetryAfter = DateTimeOffset.UtcNow.AddHours(1);
        }
        extrasPending.Remove(id);
        if (closing)
            return;
        titleExtras![id] = extra;
        // Several titles finish together; save and redraw Home once they settle, keeping the scroll position.
        if (extrasRender == null)
        {
            extrasRender = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            extrasRender.Tick += (_, _) =>
            {
                extrasRender.Stop();
                SaveExtras();
            };
        }
        extrasRender.Stop();
        extrasRender.Start();
        if (page == "Home" && detailKey == null)
            QueueMetadataRender();
    }

    void SaveExtras()
    {
        try
        {
            var temp = ExtrasPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(titleExtras));
            File.Move(temp, ExtrasPath, true);
        }
        catch (Exception e)
        {
            store.Log(e);
        }
    }

    bool IsKeyboardFocusWithinSearch => SearchBox.IsKeyboardFocusWithin;

    FrameworkElement? TitleLogo(VideoItem item, double maxHeight, double maxWidth)
    {
        if (ExtraFor(item) is not { Logo.Length: > 0 } extra)
            return null;
        var host = new Grid
        {
            MaxHeight = maxHeight,
            MaxWidth = maxWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var fallback = Text(item.Title, Density.HeroTitle(item.Title.Length > 24), weight: FontWeights.Bold);
        fallback.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        host.Children.Add(fallback);
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            MaxHeight = maxHeight,
            MaxWidth = maxWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = item.Title,
        };
        host.Children.Add(image);
        _ = Load();
        return host;
        async Task Load()
        {
            await SetArtwork(image, extra.Logo, 900);
            if (image.Source != null)
                fallback.Visibility = Visibility.Collapsed;
        }
    }

    // "Year 2015 · [IMDb] 8.5 · Runtime 25 min · Drama": labels muted, values bright, as one wrapping line.
    TextBlock HeroFacts(VideoItem item, List<VideoItem> group, int rating)
    {
        var line = new TextBlock
        {
            FontSize = Density.Pick(13, 14),
            Foreground = Brush("#F4F1EB"),
            TextWrapping = TextWrapping.Wrap,
        };
        void Gap()
        {
            if (line.Inlines.Count > 0)
                line.Inlines.Add(new Run("     "));
        }
        void Fact(string label, string value)
        {
            Gap();
            if (label.Length > 0)
                line.Inlines.Add(new Run(T(label) + (Loc.Arabic ? ": " : "  ")) { Foreground = Brush("#9F9A93") });
            line.Inlines.Add(new Run(value) { FontWeight = FontWeights.SemiBold });
        }
        if (rating > 0)
        {
            line.Inlines.Add(new Run(new string('★', rating)) { Foreground = Brush("#E4C084") });
        }
        if (item.Year > 0)
            Fact("Year", item.Year.ToString(CultureInfo.InvariantCulture));
        var extra = ExtraFor(item);
        if (extra is { Rating.Length: > 0 })
        {
            Gap();
            var badge = new Border
            {
                Background = Brush("#F5C518"),
                CornerRadius = new(3),
                Padding = new(5, 0, 5, 1),
                Margin = new(0, 0, 6, -3),
                FlowDirection = FlowDirection.LeftToRight,
                Child = new TextBlock
                {
                    Text = "IMDb",
                    FontSize = 11,
                    FontWeight = FontWeights.Black,
                    Foreground = Brush("#000000"),
                },
            };
            line.Inlines.Add(new InlineUIContainer(badge) { BaselineAlignment = BaselineAlignment.Center });
            line.Inlines.Add(new Run(extra.Rating) { FontWeight = FontWeights.SemiBold });
        }
        if (item.IsMovie)
        {
            if (item.Duration > 0)
                Fact("Runtime", Loc.Runtime(item.Duration));
            else if (extra is { Runtime.Length: > 0 })
                Fact("Runtime", LocalRuntime(extra.Runtime));
        }
        else
        {
            var seasons = group.Where(i => i.Season > 0).Select(i => i.Season).Distinct().Count();
            if (seasons > 0)
                Fact("", Loc.Count(seasons, "season", "seasons", "موسم واحد", "موسمان", "مواسم", "موسمًا"));
            Fact(
                "",
                Loc.Count(
                    group.Count(i => i.Season > 0),
                    "episode",
                    "episodes",
                    "حلقة واحدة",
                    "حلقتان",
                    "حلقات",
                    "حلقة"
                )
            );
            if (extra is { Runtime.Length: > 0 })
                Fact("Runtime", LocalRuntime(extra.Runtime));
        }
        var genres = item
            .Genres.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Take(3)
            .Select(T)
            .ToList();
        if (genres.Count > 0)
        {
            Gap();
            line.Inlines.Add(new Run(string.Join(Loc.Arabic ? "، " : " · ", genres)) { Foreground = Brush("#C9C4BD") });
        }
        return line;
    }

    static string LocalRuntime(string cinemeta) =>
        Loc.Arabic ? Regex.Replace(cinemeta, @"(\d+)\s*min", m => m.Groups[1].Value + " د") : cinemeta;

    Button HeroButton(string text, Action action, bool primary)
    {
        var b = new Button
        {
            Content = T(text),
            Style = (Style)FindResource(primary ? "HeroPill" : "HeroLink"),
            Margin = new(0, 0, 10, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(b, T(text));
        Motion.ButtonFeedback(b);
        b.Click += (_, _) => action();
        return b;
    }

    // Large outlined numerals behind each poster; digits never mirror in Arabic.
    static FrameworkElement Numeral(int n)
    {
        var text = new FormattedText(
            n.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontStyles.Normal,
                FontWeights.Black,
                FontStretches.Condensed
            ),
            200,
            Brushes.White,
            1
        );
        return new System.Windows.Shapes.Path
        {
            Data = text.BuildGeometry(new Point()),
            Stroke = Brush("#73FFFFFF"),
            StrokeThickness = 2.4,
            Fill = Brush("#0B0B0D"),
            Stretch = Stretch.Uniform,
            Height = Density.Pick(145, 168),
            FlowDirection = FlowDirection.LeftToRight,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(0, 0, 0, 30),
        };
    }

    List<DiscoverTitle>? topTen;
    DateTimeOffset topTenAt;
    bool topTenLoading;

    void RenderTopTen()
    {
        if (!store.State.OnlineMetadata)
            return;
        if (topTen == null || topTenAt < DateTimeOffset.UtcNow.AddHours(-6))
        {
            if (!topTenLoading)
                _ = LoadTopTen();
            if (topTen == null)
                return;
        }
        var cards = topTen.Select((title, index) => TopTenCard(title, index + 1)).ToList();
        if (cards.Count > 0)
            ContentPanel.Children.Add(
                Rail(
                    "Top 10 this week",
                    "Most popular on Cinemeta right now.",
                    cards,
                    page == "Discover" ? null : () => Navigate("Discover")
                )
            );
    }

    async Task LoadTopTen()
    {
        topTenLoading = true;
        try
        {
            using var client = DiscoverClient();
            var lists = await Task.WhenAll(client.Top("movie", 0, default), client.Top("series", 0, default));
            var mixed = new List<DiscoverTitle>();
            for (int i = 0; i < 10 && mixed.Count < 10; i++)
                foreach (var list in lists)
                    if (i < list.Count && mixed.Count < 10)
                        mixed.Add(list[i]);
            await Task.WhenAll(mixed.Select(t => client.Artwork(t, default)));
            topTen = mixed;
            topTenAt = DateTimeOffset.UtcNow;
            if (
                !closing
                && current == null
                && (page == "Home" && detailKey == null || page == "Discover" && discoverTitle == null)
                && SearchBox.Text.Length == 0
            )
                QueueMetadataRender();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or IOException)
        { }
        finally
        {
            topTenLoading = false;
        }
    }

    FrameworkElement TopTenCard(DiscoverTitle title, int rank)
    {
        var card = new Grid
        {
            Width = rank == 10 ? Density.Pick(232, 262) : Density.Pick(188, 212),
            Height = Density.Pick(224, 250),
            Margin = new(0, 0, 10, 0),
            Background = Brush("Transparent"),
        };
        card.Children.Add(Numeral(rank));
        var column = new StackPanel
        {
            Width = Density.Pick(116, 130),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        FrameworkElement picture = Art(title.Poster, title.Name, Density.Pick(174, 195), true);
        var framed = new Grid { Height = Density.Pick(174, 195) };
        framed.Children.Add(picture);
        framed.Children.Add(
            new Border
            {
                Background = Brush("#E4C084"),
                CornerRadius = new(5),
                Padding = new(5, 3, 5, 3),
                Margin = new(5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = Text(StatusText("#" + rank + " this week"), 9, "#16120C", FontWeights.SemiBold),
            }
        );
        var open = new Button
        {
            Content = framed,
            Padding = new(0),
            Background = Brush("#151517"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = title.Name + (title.Rating.Length > 0 ? " · IMDb " + title.Rating : ""),
        };
        System.Windows.Automation.AutomationProperties.SetName(open, $"Top 10 number {rank}: {title.Name}");
        open.Click += (_, _) => _ = OpenDiscover(title);
        var edge = new Border
        {
            CornerRadius = new(9),
            ClipToBounds = true,
            Child = open,
            BorderThickness = new(1.5),
            BorderBrush = Brush("#00000000"),
        };
        column.Children.Add(edge);
        var name = Text(title.Name, 12, "#C7C2BC");
        name.TextAlignment = TextAlignment.Center;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextWrapping = TextWrapping.NoWrap;
        name.Margin = new(0, 9, 0, 0);
        column.Children.Add(name);
        card.Children.Add(column);
        Lift(column, edge);
        return card;
    }

    // Wordmark tiles for what Noir is connected to; each opens the place you manage it.
    void RenderServices()
    {
        var config = downloads?.Config;
        var tiles = new List<(string Name, string Color, Action Open)>();
        void Downloads()
        {
            settingsSection = "Connections";
            Navigate("Settings");
        }
        void Imports() => Downloads();
        if (config != null)
        {
            if (config.TorBoxKey.Length > 0 || config.Service == "TorBox" && config.Key.Length > 0)
                tiles.Add(("TorBox", "#05C28F", Downloads));
            if (config.RealDebridKey.Length > 0 || config.Service == "Real-Debrid" && config.Key.Length > 0)
                tiles.Add(("Real-Debrid", "#78BB4B", Downloads));
            if (config.Manifest.Length > 0)
                tiles.Add(("AIOStreams", "#7AA7FF", Downloads));
            foreach (var kind in config.Servers.Select(s => s.Kind).Distinct())
                tiles.Add(
                    (
                        kind,
                        kind switch
                        {
                            "Plex" => "#E5A00D",
                            "Jellyfin" => "#AA5CC3",
                            "Emby" => "#52B54B",
                            _ => "#D9D4CC",
                        },
                        Downloads
                    )
                );
        }
        if (store.State.StremioImportedAt != null)
            tiles.Add(("Stremio", "#9B6BD3", Imports));
        if (store.State.NuvioConnected)
            tiles.Add(("Nuvio", "#F4F1EB", Imports));
        if (store.State.SyncTrakt)
            tiles.Add(("trakt", "#ED2224", Downloads));
        if (tiles.Count == 0)
            return;
        var cards = tiles
            .Select(t =>
            {
                var tile = new Button
                {
                    Width = Density.Pick(172, 196),
                    Height = Density.Pick(66, 76),
                    Margin = new(0, 0, 14, 0),
                    Padding = new(0),
                    Background = Brush("#16FFFFFF"),
                    ToolTip = t.Name,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                };
                tile.Content = new TextBlock
                {
                    Text = t.Name,
                    FontSize = Density.Pick(18, 20),
                    FontWeight = FontWeights.Black,
                    Foreground = Brush(t.Color),
                    FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                    FlowDirection = FlowDirection.LeftToRight,
                };
                tile.Template = TileTemplate();
                System.Windows.Automation.AutomationProperties.SetName(tile, t.Name);
                var open = t.Open;
                tile.Click += (_, _) => open();
                return (FrameworkElement)Lift(tile);
            })
            .ToList();
        ContentPanel.Children.Add(Rail("Your services", "Connected to Noir. Click one to manage it.", cards));
    }

    static ControlTemplate TileTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brush("#1AFFFFFF"));
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
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#66FFFFFF"), "Chrome"));
        template.Triggers.Add(hover);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#E4C084"), "Chrome"));
        template.Triggers.Add(focus);
        return template;
    }
}
