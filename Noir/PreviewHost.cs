using System.IO;
using System.Text.Json;
#if !NOIR_HEADLESS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
#endif

namespace Noir;

public enum PreviewMode
{
    Trailer,
    Title,
    Off,
}

public static class PreviewSettings
{
    static readonly object gate = new();

    public static PreviewMode Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.False)
                return PreviewMode.Off;
            if (root.ValueKind == JsonValueKind.True)
                return PreviewMode.Trailer;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
                    return PreviewMode.Off;
                if (!root.TryGetProperty("mode", out root))
                    return PreviewMode.Trailer;
            }
            var value = root.ValueKind == JsonValueKind.String ? root.GetString()?.ToLowerInvariant() : "";
            return value switch
            {
                "off" or "none" => PreviewMode.Off,
                "title" or "self" or "title itself" or "the title itself" => PreviewMode.Title,
                _ => PreviewMode.Trailer,
            };
        }
        catch (JsonException)
        {
            return PreviewMode.Trailer;
        }
    }

    public static PreviewMode Load(string directory)
    {
        try
        {
            var path = Path.Combine(directory, "preview-settings.json");
            return File.Exists(path) && new FileInfo(path).Length <= 4096
                ? Parse(File.ReadAllText(path, System.Text.Encoding.UTF8))
                : PreviewMode.Trailer;
        }
        catch (IOException)
        {
            return PreviewMode.Trailer;
        }
        catch (UnauthorizedAccessException)
        {
            return PreviewMode.Trailer;
        }
    }

    public static void Save(string directory, PreviewMode mode)
    {
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "preview-settings.json");
            File.WriteAllText(
                path + ".tmp",
                JsonSerializer.Serialize(new { version = 1, mode = mode.ToString() }),
                System.Text.Encoding.UTF8
            );
            File.Move(path + ".tmp", path, true);
        }
    }
}

public static class PreviewRules
{
    public static double StartSeconds(double duration, double progress, bool movie, bool watched)
    {
        if (!double.IsFinite(duration) || duration <= 0)
            return 0;
        var start = Math.Max(0, Math.Min(duration * .2, duration - 1));
        return !movie && !watched ? Math.Min(start, double.IsFinite(progress) ? Math.Max(0, progress) : 0) : start;
    }

    public static double EndSeconds(double duration, double progress, bool movie, bool watched)
    {
        if (!double.IsFinite(duration) || duration <= 0)
            return 0;
        return !movie && !watched
            ? Math.Max(0, Math.Min(duration, double.IsFinite(progress) ? progress : 0))
            : duration;
    }
}

#if !NOIR_HEADLESS
public static class PreviewHost
{
    static string directory = "",
        key = "";
    static Func<VideoItem, Bookmark?> progress = _ => null;
    static Func<VideoItem, bool> watched = _ => false;
    static Session? active;
    static PreviewMode mode = PreviewMode.Trailer;
    static long configuration;
    static Task nativeCleanup = Task.CompletedTask;
    static readonly object saveGate = new();
    public static PreviewMode Mode => mode;
    public static Func<VideoItem, string?>? ResolvedStream { get; set; }

    public static void Configure(
        string dataDirectory,
        Func<VideoItem, Bookmark?> bookmarks,
        Func<VideoItem, bool> seen,
        string tmdbKey = ""
    )
    {
        progress = bookmarks;
        watched = seen;
        key = tmdbKey;
        if (directory == dataDirectory)
            return;
        Stop();
        directory = dataDirectory;
        mode = PreviewMode.Off;
        var revision = ++configuration;
        _ = Read();
        async Task Read()
        {
            var setting = await Task.Run(() => PreviewSettings.Load(dataDirectory));
            if (revision == configuration)
            {
                mode = setting;
                if (setting == PreviewMode.Off)
                    Stop();
            }
        }
    }

    public static void SetMode(PreviewMode value)
    {
        var revision = ++configuration;
        mode = value;
        Stop();
        var path = directory;
        if (path.Length > 0)
            _ = Task.Run(() =>
            {
                lock (saveGate)
                {
                    if (revision != configuration)
                        return;
                    try
                    {
                        PreviewSettings.Save(path, value);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            });
    }

    public static void Stop()
    {
        active?.Dispose();
        active = null;
    }

    public static void Attach(Border media, VideoItem item, int dwellMs)
    {
        if (media.Tag is Session)
            return;
        var session = new Session(media, item, Math.Clamp(dwellMs, 500, 10000));
        media.Tag = session;
    }

    public static void SetOverlay(Border media, FrameworkElement overlay)
    {
        if (media.Tag is Session session)
            session.Overlay = overlay;
    }

    sealed class Session : IDisposable
    {
        readonly Border media;
        readonly VideoItem item;
        readonly DispatcherTimer dwell,
            limit;
        CancellationTokenSource? cancellation;
        Grid? layer,
            backing;
        Panel? overlayParent;
        int overlayIndex;
        public FrameworkElement? Overlay;
        UIElement? original;
        WebView2CompositionControl? web;
        VideoView? video;
        LibVLC? engine;
        LibVLCSharp.Shared.MediaPlayer? player;
        Window? window;
        ScrollViewer? scroll;
        bool running,
            attached;
        long until,
            generation;

        public Session(Border host, VideoItem title, int wait)
        {
            media = host;
            item = title;
            dwell = new() { Interval = TimeSpan.FromMilliseconds(wait) };
            limit = new() { Interval = TimeSpan.FromMilliseconds(120) };
            dwell.Tick += async (_, _) =>
            {
                dwell.Stop();
                await Start();
            };
            limit.Tick += (_, _) =>
            {
                if (
                    !Motion.Enabled
                    || !media.IsVisible
                    || window?.IsActive != true
                    || Environment.TickCount64 >= until
                    || player != null && End() > 0 && player.Time / 1000d >= End()
                )
                    Dispose();
            };
            media.MouseEnter += (_, _) =>
            {
                if (Motion.Enabled && mode != PreviewMode.Off && window?.IsActive == true)
                    dwell.Start();
            };
            media.MouseLeave += (_, _) => Dispose();
            media.Loaded += (_, _) => Bind();
            media.Unloaded += (_, _) =>
            {
                Dispose();
                Unbind();
            };
            if (media.IsLoaded)
                Bind();
        }

        double End()
        {
            var b = progress(item);
            return PreviewRules.EndSeconds(
                b?.Duration > 0 ? b.Duration : item.Duration,
                b?.Position ?? 0,
                item.IsMovie,
                watched(item)
            );
        }

        void Bind()
        {
            if (attached)
                return;
            attached = true;
            window = Window.GetWindow(media);
            if (window != null)
                window.Deactivated += Deactivated;
            for (DependencyObject? parent = media; parent != null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollViewer found)
                {
                    scroll = found;
                    scroll.ScrollChanged += Scrolled;
                    break;
                }
        }

        void Unbind()
        {
            attached = false;
            if (window != null)
                window.Deactivated -= Deactivated;
            if (scroll != null)
                scroll.ScrollChanged -= Scrolled;
            window = null;
            scroll = null;
        }

        void Deactivated(object? s, EventArgs e) => Dispose();

        void Scrolled(object s, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.VerticalChange) + Math.Abs(e.HorizontalChange) > 0)
                Dispose();
        }

        async Task Start()
        {
            if (
                running
                || !media.IsLoaded
                || !media.IsMouseOver
                || !Motion.Enabled
                || mode == PreviewMode.Off
                || directory.Length == 0
                || window?.IsActive != true
            )
                return;
            Stop();
            active = this;
            running = true;
            cancellation = new();
            var token = cancellation.Token;
            var revision = ++generation;
            var selected = mode;
            var folder = directory;
            var tmdb = key;
            try
            {
                string id = "";
                if (selected == PreviewMode.Trailer && !string.IsNullOrWhiteSpace(item.ImdbId))
                {
                    using var trailers = new TrailersClient(folder, tmdb);
                    id = (await trailers.Resolve(item.ImdbId, item.IsMovie, token)).FirstOrDefault()?.YouTubeId ?? "";
                }
                if (token.IsCancellationRequested || !media.IsLoaded || !Motion.Enabled)
                    return;
                var local = File.Exists(item.Path);
                var resolved = local ? null : ResolvedStream?.Invoke(item);
                var stream =
                    Uri.TryCreate(resolved, UriKind.Absolute, out var streamUri) && streamUri.Scheme == "https"
                        ? streamUri
                        : null;
                if (
                    id.Length == 0
                    && (
                        !local && stream == null
                        || !item.IsMovie && !watched(item) && (progress(item)?.Position ?? 0) < 1
                    )
                )
                {
                    Dispose();
                    return;
                }
                original = media.Child;
                layer = new Grid { ClipToBounds = true };
                if (original is Grid art)
                {
                    backing = art;
                    art.Children.Insert(Math.Min(1, art.Children.Count), layer);
                }
                else
                {
                    media.Child = null;
                    if (original != null)
                        layer.Children.Add(original);
                    media.Child = layer;
                }
                if (id.Length > 0)
                {
                    var environment = await CoreWebView2Environment.CreateAsync(
                        userDataFolder: Path.Combine(folder, "review-browser")
                    );
                    if (token.IsCancellationRequested)
                        return;
                    var browser = new WebView2CompositionControl
                    {
                        IsHitTestVisible = false,
                        DefaultBackgroundColor = System.Drawing.Color.Black,
                    };
                    var cover = new Border
                    {
                        Opacity = 0,
                        Child = browser,
                        IsHitTestVisible = false,
                    };
                    web = browser;
                    layer.Children.Add(cover);
                    await browser.EnsureCoreWebView2Async(environment);
                    if (token.IsCancellationRequested)
                        return;
                    web.CoreWebView2.IsMuted = true;
                    web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    web.CoreWebView2.Settings.IsScriptEnabled = true;
                    web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
                    web.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
                    web.CoreWebView2.NavigationStarting += (_, e) =>
                    {
                        if (!e.Uri.StartsWith("https://www.youtube-nocookie.com/embed/", StringComparison.Ordinal))
                            e.Cancel = true;
                    };
                    web.CoreWebView2.NavigationCompleted += (_, e) =>
                    {
                        if (revision != generation)
                            return;
                        if (!e.IsSuccess)
                            Dispose();
                        else if (web != null)
                            Motion.FadeIn(cover);
                    };
                    web.CoreWebView2.Navigate(
                        "https://www.youtube-nocookie.com/embed/"
                            + id
                            + "?autoplay=1&mute=1&controls=0&playsinline=1&loop=1&playlist="
                            + id
                            + "&modestbranding=1&rel=0"
                    );
                }
                else
                {
                    await nativeCleanup;
                    if (token.IsCancellationRequested)
                        return;
                    var b = progress(item);
                    var start = PreviewRules.StartSeconds(
                        b?.Duration > 0 ? b.Duration : item.Duration,
                        b?.Position ?? 0,
                        item.IsMovie,
                        watched(item)
                    );
                    var created = await Task.Run(() =>
                        PlayerWarmup.CreateEngine([
                            "--quiet",
                            "--no-audio",
                            "--no-sub-autodetect-file",
                            "--no-video-title-show",
                        ])
                    );
                    if (token.IsCancellationRequested)
                    {
                        await Task.Run(created.Dispose);
                        return;
                    }
                    engine = created;
                    player = new LibVLCSharp.Shared.MediaPlayer(engine)
                    {
                        Mute = true,
                        Volume = 0,
                        EnableKeyInput = false,
                        EnableMouseInput = false,
                    };
                    video = new VideoView { MediaPlayer = player };
                    layer.Children.Add(video);
                    if (Overlay?.Parent is Panel owner)
                    {
                        overlayParent = owner;
                        overlayIndex = owner.Children.IndexOf(Overlay);
                        owner.Children.Remove(Overlay);
                        video.Content = Overlay;
                    }
                    using var source = new Media(engine, local ? new Uri(Path.GetFullPath(item.Path)) : stream!);
                    source.AddOption(":no-audio");
                    source.AddOption(
                        ":start-time=" + start.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    );
                    if (!item.IsMovie && !watched(item))
                        source.AddOption(
                            ":stop-time="
                                + (b?.Position ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        );
                    player.Play(source);
                    Motion.FadeIn(video);
                }
                until = Environment.TickCount64 + 25000;
                limit.Start();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                if (revision == generation)
                    Dispose();
            }
            finally
            {
                if (revision == generation && token.IsCancellationRequested)
                    Dispose();
            }
        }

        public void Dispose()
        {
            ++generation;
            dwell.Stop();
            limit.Stop();
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
            running = false;
            if (active == this)
                active = null;
            if (web != null)
            {
                web.Dispose();
                web = null;
            }
            if (video != null)
            {
                video.Content = null;
                if (Overlay != null && overlayParent != null)
                    overlayParent.Children.Insert(Math.Min(overlayIndex, overlayParent.Children.Count), Overlay);
                overlayParent = null;
                video.MediaPlayer = null;
                layer?.Children.Remove(video);
                video.Dispose();
                video = null;
            }
            var old = player;
            player = null;
            var vlc = engine;
            engine = null;
            if (old != null || vlc != null)
                nativeCleanup = Task.Run(() =>
                {
                    try
                    {
                        old?.Stop();
                        old?.Dispose();
                        vlc?.Dispose();
                    }
                    catch { }
                });
            if (layer != null)
            {
                if (backing != null)
                {
                    backing.Children.Remove(layer);
                    backing = null;
                }
                else
                {
                    if (original != null)
                        layer.Children.Remove(original);
                    media.Child = original;
                }
                layer = null;
                original = null;
            }
        }
    }
}

public partial class MainWindow
{
    public FrameworkElement PreviewSettingsPanel()
    {
        PreviewHost.Configure(store.DataDirectory, store.GetProgress, store.EverWatched, TmdbKey());
        var panel = new StackPanel { Margin = new(0, 16, 0, 12) };
        panel.Children.Add(
            Text(
                T("Previews when you rest on a title", "المعاينات عند التوقف على عنوان"),
                15,
                weight: FontWeights.SemiBold
            )
        );
        panel.Children.Add(
            Body(
                T(
                    "Silent previews stop when you scroll. Reduced motion turns previews off.",
                    "تتوقف المعاينات الصامتة عند التمرير. تقليل الحركة يعطل المعاينات."
                ),
                12
            )
        );
        var row = new WrapPanel();
        var current = PreviewSettings.Load(store.DataDirectory);
        foreach (
            var entry in new[]
            {
                (PreviewMode.Trailer, T("Trailer", "إعلان")),
                (PreviewMode.Title, T("The title itself", "العنوان نفسه")),
                (PreviewMode.Off, T("Off", "إيقاف")),
            }
        )
        {
            var option = new RadioButton
            {
                Content = entry.Item2,
                IsChecked = current == entry.Item1,
                GroupName = "noir-preview-mode",
                Margin = new(0, 10, 22, 0),
            };
            option.Checked += (_, _) => PreviewHost.SetMode(entry.Item1);
            row.Children.Add(option);
        }
        panel.Children.Add(row);
        return panel;
    }
}
#endif
