using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Noir;

public partial class MainWindow
{
    FrameworkElement CreditHeading(string english, string arabic)
    {
        var text = Text(T(english, arabic), 20, weight: FontWeights.SemiBold);
        text.Margin = new(0, 0, 0, 18);
        return text;
    }

    static ScrollViewer CreditRail(UIElement child) =>
        new()
        {
            Content = child,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.HorizontalOnly,
            Padding = new(0, 0, 0, 8),
        };

    public FrameworkElement DetailPeople(VideoItem item)
    {
        var host = new StackPanel { Margin = new(0, 24, 0, 22) };
        if (!System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId, @"^tt\d+$"))
        {
            host.Visibility = Visibility.Collapsed;
            return host;
        }
        var cast = new StackPanel();
        cast.Children.Add(CreditHeading("Cast & crew", "الممثلون وطاقم العمل"));
        var placeholders = new StackPanel { Orientation = Orientation.Horizontal, Height = 126 };
        for (var i = 0; i < 8; i++)
        {
            var p = new StackPanel { Width = 108, Margin = new(0, 0, 12, 0) };
            p.Children.Add(
                new Border
                {
                    Width = 72,
                    Height = 72,
                    CornerRadius = new(36),
                    Background = Brush("#262529"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                }
            );
            p.Children.Add(
                new Border
                {
                    Height = 9,
                    Width = 82,
                    Margin = new(0, 13, 0, 7),
                    Background = Brush("#262529"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new(4),
                }
            );
            p.Children.Add(
                new Border
                {
                    Height = 8,
                    Width = 60,
                    Background = Brush("#202023"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new(4),
                }
            );
            placeholders.Children.Add(p);
        }
        cast.Children.Add(CreditRail(placeholders));
        host.Children.Add(cast);
        var extras = new StackPanel { Visibility = Visibility.Collapsed, Margin = new(0, 25, 0, 0) };
        host.Children.Add(extras);
        CancellationTokenSource? life = null;
        host.Loaded += async (_, _) =>
        {
            life?.Cancel();
            life?.Dispose();
            life = new();
            var ct = life.Token;
#if NOIR_VALIDATE
            DetailValidationHook(item, host);
#endif
            try
            {
                var key = TmdbKey();
                using var credits = new CreditsClient(store.DataDirectory, key);
                using var trailers = new TrailersClient(store.DataDirectory, key);
                var peopleTask = credits.Title(item.ImdbId, item.IsMovie, ct);
                var trailerTask = trailers.Resolve(item.ImdbId, item.IsMovie, ct);
                var people = await peopleTask;
                if (ct.IsCancellationRequested || !host.IsLoaded)
                    return;
                RenderCreditStrip(cast, people);
                var videos = await trailerTask;
                if (ct.IsCancellationRequested || !host.IsLoaded)
                    return;
                RenderExtraRail(extras, videos);
                if (cast.Visibility == Visibility.Collapsed && extras.Visibility == Visibility.Collapsed)
                    host.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                store.Log(error);
                if (!ct.IsCancellationRequested)
                {
                    host.Visibility = Visibility.Collapsed;
                }
            }
        };
        host.Unloaded += (_, _) => life?.Cancel();
        return host;
    }

    void RenderCreditStrip(StackPanel host, TitleCredits credits)
    {
        host.Children.Clear();
        if (credits.Cast.Count == 0 && credits.Directors.Count == 0 && credits.Writers.Count == 0)
        {
            host.Visibility = Visibility.Collapsed;
            return;
        }
        host.Visibility = Visibility.Visible;
        host.Children.Add(CreditHeading("Cast & crew", "الممثلون وطاقم العمل"));
        if (credits.Cast.Count > 0)
        {
            var rail = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var person in credits.Cast.Take(24))
            {
                var panel = new StackPanel { Width = 108 };
                panel.Children.Add(CreditPortrait(person, 72));
                var name = Text(person.Name, 12.5, weight: FontWeights.SemiBold);
                name.Margin = new(0, 11, 0, 3);
                name.MaxHeight = 38;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                panel.Children.Add(name);
                var role = Text(person.Character, 11, "#A29D96");
                role.MaxHeight = 32;
                role.TextTrimming = TextTrimming.CharacterEllipsis;
                panel.Children.Add(role);
                var button = ActionButton("", () => OpenPerson(person));
                button.Content = panel;
                button.ToolTip = person.Name;
                button.Padding = new(0);
                button.Margin = new(0, 0, 14, 0);
                button.Background = Brush("Transparent");
                rail.Children.Add(button);
            }
            host.Children.Add(CreditRail(rail));
        }
        var crew = new WrapPanel { Margin = new(0, 12, 0, 0) };
        void Add(string english, string arabic, List<CreditPerson> people)
        {
            if (people.Count == 0)
                return;
            var label = Text(T(english, arabic), 12, "#918A80");
            label.Margin = new(0, 6, 9, 0);
            crew.Children.Add(label);
            foreach (var person in people.Take(5))
            {
                var b = ActionButton("", () => OpenPerson(person));
                b.Content = person.Name;
                b.Foreground = Brush("#E4C084");
                b.Background = Brush("Transparent");
                b.Padding = new(0, 5, 0, 5);
                b.Margin = new(0, 0, 16, 0);
                crew.Children.Add(b);
            }
        }
        Add("Directed by", "إخراج", credits.Directors);
        Add("Written by", "كتابة", credits.Writers);
        host.Children.Add(crew);
        Motion.EnterOnce(host);
    }

    FrameworkElement CreditPortrait(CreditPerson person, double size)
    {
        var panel = new Grid
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brush("#29262A"),
            Clip = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2),
        };
        var initial = Text(person.Name.Length > 0 ? person.Name[..1] : "", size * .35, "#BFB09A", FontWeights.SemiBold);
        initial.HorizontalAlignment = HorizontalAlignment.Center;
        initial.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(initial);
        var done = false;
        CancellationTokenSource? life = null;
        panel.Loaded += async (_, _) =>
        {
            if (done)
                return;
            life?.Dispose();
            life = new();
            var ct = life.Token;
            try
            {
                using var client = new CreditsClient(store.DataDirectory);
                var path = await client.Photo(person, ct);
                if (ct.IsCancellationRequested || !panel.IsLoaded || !File.Exists(path))
                    return;
                panel.Children.Add(Art(path, person.Name, size, true));
                done = true;
                Motion.FadeIn(panel);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                store.Log(error);
            }
        };
        panel.Unloaded += (_, _) => life?.Cancel();
        return panel;
    }

    void RenderExtraRail(StackPanel host, List<TrailerInfo> videos)
    {
        host.Children.Clear();
        host.Visibility = videos.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (videos.Count == 0)
            return;
        host.Children.Add(CreditHeading("Extras", "مواد إضافية"));
        var rail = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var video in videos.Take(12))
        {
            var info = new StackPanel { Width = 230 };
            var image = CreditRemoteArt(video.Thumbnail, video.Name, 230, 129);
            var picture = new Grid { Width = 230, Height = 129 };
            picture.Children.Add(image);
            var play = Text("▶", 26, "#FFFFFF");
            play.HorizontalAlignment = HorizontalAlignment.Center;
            play.VerticalAlignment = VerticalAlignment.Center;
            picture.Children.Add(play);
            info.Children.Add(picture);
            var name = Text(
                video.Name.Length > 0 ? video.Name : TrailerKind(video.Kind),
                13,
                weight: FontWeights.SemiBold
            );
            name.MaxHeight = 38;
            name.Margin = new(0, 10, 0, 4);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            info.Children.Add(name);
            info.Children.Add(Text(TrailerKind(video.Kind), 11, "#A29D96"));
            var button = ActionButton("", () => OpenTrailer(video));
            button.Content = info;
            button.Background = Brush("Transparent");
            button.Padding = new(0);
            button.Margin = new(0, 0, 16, 0);
            button.ToolTip = name.Text;
            rail.Children.Add(button);
        }
        host.Children.Add(CreditRail(rail));
        Motion.EnterOnce(host);
    }

    static string TrailerKind(string kind) =>
        kind switch
        {
            "Teaser" => T("Teaser", "إعلان تشويقي"),
            "Featurette" => T("Featurette", "كواليس قصيرة"),
            "Behind the Scenes" => T("Behind the scenes", "خلف الكواليس"),
            "Clip" => T("Clip", "مقطع"),
            _ => T("Trailer", "الإعلان"),
        };

    FrameworkElement CreditRemoteArt(string url, string title, double width, double height, string local = "")
    {
        var box = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new(8),
            ClipToBounds = true,
            Background = Brush("#252329"),
            Child = File.Exists(local) ? Art(local, title, height, true) : null,
        };
        if (File.Exists(local) || url.Length == 0)
            return box;
        CancellationTokenSource? life = null;
        var done = false;
        box.Loaded += async (_, _) =>
        {
            if (done)
                return;
            life?.Dispose();
            life = new();
            var ct = life.Token;
            try
            {
                using var art = new MetadataClient(store.DataDirectory);
                var path = await art.EpisodeArt(url, ct);
                if (ct.IsCancellationRequested || !box.IsLoaded || !File.Exists(path))
                    return;
                box.Child = Art(path, title, height, true);
                done = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                store.Log(error);
            }
        };
        box.Unloaded += (_, _) => life?.Cancel();
        return box;
    }

    public Button TrailerAction(VideoItem item)
    {
        var button = HeroButton(T("[[play]]  Trailer", "[[play]]  الإعلان"), () => { }, false);
        button.Visibility = Visibility.Collapsed;
        CancellationTokenSource? life = null;
        button.Loaded += async (_, _) =>
        {
            life?.Dispose();
            life = new();
            var ct = life.Token;
            try
            {
                using var trailers = new TrailersClient(store.DataDirectory, TmdbKey());
                var videos = await trailers.Resolve(item.ImdbId, item.IsMovie, ct);
                if (ct.IsCancellationRequested || !button.IsLoaded || videos.Count == 0)
                    return;
                button.Visibility = Visibility.Visible;
                button.Click += (_, _) => OpenTrailer(videos[0]);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                store.Log(error);
            }
        };
        button.Unloaded += (_, _) => life?.Cancel();
        return button;
    }

    Window? detailTrailerWindow;

    void OpenTrailer(TrailerInfo trailer)
    {
        if (TrailersClient.YouTubeId(trailer.YouTubeId).Length == 0)
            return;
        detailTrailerWindow?.Close();
        PreviewHost.Stop();
        var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
        var stage = new Grid();
        stage.RowDefinitions.Add(new() { Height = GridLength.Auto });
        stage.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var top = new DockPanel();
        var window = new Window
        {
            Owner = this,
            Title = trailer.Name.Length > 0 ? trailer.Name : TrailerKind(trailer.Kind),
            Width = Math.Min(1100, Math.Max(740, ActualWidth - 80)),
            Height = 680,
            MinWidth = 640,
            MinHeight = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0B0B0D"),
            Foreground = Brush("#F4F1EB"),
            Content = stage,
        };
        detailTrailerWindow = window;
        var close = ActionButton(T("Close", "إغلاق"), window.Close);
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);
        var label = Text(window.Title, 16, weight: FontWeights.SemiBold);
        label.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(label);
        stage.Children.Add(
            new Border
            {
                Background = Brush("#141416"),
                BorderBrush = Brush("#1FFFFFFF"),
                BorderThickness = new(0, 0, 0, 1),
                Padding = new(22, 10, 16, 10),
                Child = top,
            }
        );
        Grid.SetRow(web, 1);
        stage.Children.Add(web);
        var canceled = false;
        window.Closed += (_, _) =>
        {
            canceled = true;
            web.Dispose();
            if (detailTrailerWindow == window)
                detailTrailerWindow = null;
        };
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                window.Close();
        };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: Path.Combine(store.DataDirectory, "review-browser")
                );
                if (canceled)
                    return;
                await web.EnsureCoreWebView2Async(environment);
                if (canceled)
                    return;
                web.CoreWebView2.IsMuted = false;
                web.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
                web.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
                web.CoreWebView2.NavigationStarting += (_, e) =>
                {
                    if (
                        !Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
                        || uri.Scheme != "https"
                        || uri.Host is not ("www.youtube-nocookie.com" or "www.youtube.com" or "consent.youtube.com")
                    )
                        e.Cancel = true;
                };
                web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
                web.CoreWebView2.Navigate(
                    "https://www.youtube-nocookie.com/embed/"
                        + trailer.YouTubeId
                        + "?autoplay=1&mute=0&controls=1&playsinline=1&rel=0"
                );
            }
            catch (Exception error)
            {
                if (canceled)
                    return;
                store.Log(error);
                var message = Body(
                    T("The trailer couldn’t open. Please try again.", "تعذّر فتح الإعلان. حاول مجددًا.")
                );
                message.Margin = new(28);
                Grid.SetRow(message, 1);
                stage.Children.Add(message);
            }
        };
        window.Show();
    }

    public Window OpenPerson(CreditPerson person) => OpenPersonWindow(person, null);

    Window OpenPersonWindow(CreditPerson person, List<CreditTitle>? fixture)
    {
        var window = new Window
        {
            Owner = this,
            Title = person.Name,
            Width = Math.Min(1060, Math.Max(740, ActualWidth - 80)),
            Height = Math.Min(850, Math.Max(620, ActualHeight - 60)),
            MinWidth = 650,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#101012"),
            Foreground = Brush("#F4F1EB"),
            FlowDirection = Loc.Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            ShowActivated = fixture == null,
        };
        var root = new DockPanel();
        var toolbar = new DockPanel();
        var bar = new Border
        {
            Background = Brush("#141416"),
            BorderBrush = Brush("#1FFFFFFF"),
            BorderThickness = new(0, 0, 0, 1),
            Padding = new(28, 10, 20, 10),
            Child = toolbar,
        };
        var close = new Button
        {
            Content = "[[close]]",
            Width = 32,
            Height = 32,
            Padding = new(0),
            FontSize = 13,
            Background = Brush("Transparent"),
            Foreground = Brush("#C7C2BC"),
            BorderThickness = new(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = T("Close", "إغلاق") + " · Esc",
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        System.Windows.Automation.AutomationProperties.SetName(close, T("Close", "إغلاق"));
        close.Click += (_, _) => window.Close();
        toolbar.Children.Add(close);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        var panel = new StackPanel { Margin = new(36, 28, 36, 36) };
        root.Children.Add(
            new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            }
        );
        window.Content = root;
        var head = new DockPanel { Margin = new(0, 0, 0, 32) };
        var photo = CreditPortrait(person, 136);
        photo.Margin = new(0, 0, 28, 0);
        head.Children.Add(photo);
        var intro = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        intro.Children.Add(Text(person.Name, 36, weight: FontWeights.Bold));
        head.Children.Add(intro);
        panel.Children.Add(head);
        var results = new StackPanel();
        results.Children.Add(Body(T("Loading filmography…", "جارٍ تحميل الأعمال…"), 13));
        panel.Children.Add(results);
        var life = new CancellationTokenSource();
        window.Closed += (_, _) => life.Cancel();
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                window.Close();
        };
        window.Loaded += async (_, _) =>
        {
            try
            {
                using var client = new CreditsClient(store.DataDirectory, TmdbKey());
                var titles = fixture ?? await client.Person(person, life.Token);
                if (life.IsCancellationRequested)
                    return;
                RenderPersonTitles(results, titles, window);
                Motion.EnterOnce(head);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                store.Log(error);
                if (!life.IsCancellationRequested)
                {
                    results.Children.Clear();
                    results.Children.Add(
                        Body(T("Filmography is unavailable right now.", "قائمة الأعمال غير متاحة حاليًا."))
                    );
                }
            }
        };
        window.Show();
        return window;
    }

    void RenderPersonTitles(StackPanel panel, List<CreditTitle> titles, Window window)
    {
        panel.Children.Clear();
        if (titles.Count == 0)
        {
            panel.Children.Add(
                Body(
                    T(
                        "More credits will appear here as titles are discovered.",
                        "ستظهر المزيد من الأعمال هنا عند اكتشاف العناوين."
                    )
                )
            );
            return;
        }
        var archive = store.Archive.GroupBy(x => x.GroupKey).Select(g => (Item: g.First(), Group: g.ToList())).ToList();
        var byId = archive
            .Where(x => x.Item.ImdbId.Length > 0)
            .GroupBy(x => x.Item.ImdbId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var byName = archive
            .GroupBy(x => x.Item.Title)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        (VideoItem? Item, List<VideoItem>? Group) Match(CreditTitle title)
        {
            if (title.ImdbId.Length > 0 && byId.TryGetValue(title.ImdbId, out var id))
                return (id.Item, id.Group);
            if (
                byName.TryGetValue(title.Name, out var name)
                && (title.Year == 0 || name.Item.Year == title.Year)
                && title.IsMovie == name.Item.IsMovie
            )
                return (name.Item, name.Group);
            return (null, null);
        }
        string Mark(CreditTitle title)
        {
            var match = Match(title);
            return match.Item == null ? ""
                : match.Group!.Any(store.EverWatched) ? T("✓ Watched", "✓ تمت المشاهدة")
                : T("In your library", "في مكتبتك");
        }
        var navigating = false;
        var navigationLife = new CancellationTokenSource();
        window.Closed += (_, _) => navigationLife.Cancel();
        async void Open(CreditTitle title)
        {
            if (navigating)
                return;
            var match = Match(title);
            if (match.Item != null)
            {
                window.Close();
                OpenDetails(match.Group!);
                ContentScroll.ScrollToTop();
                return;
            }
            navigating = true;
            var imdb = title.ImdbId;
            try
            {
                if (imdb.Length == 0 && title.SourceId > 0)
                {
                    window.Title = T("Opening title…", "جارٍ فتح العنوان…");
                    using var credits = new CreditsClient(store.DataDirectory, TmdbKey());
                    imdb = await credits.ResolveImdb(title, navigationLife.Token);
                }
                if (navigationLife.IsCancellationRequested || closing)
                    return;
                window.Close();
                Navigate("Discover");
                if (System.Text.RegularExpressions.Regex.IsMatch(imdb, @"^tt\d+$"))
                    _ = OpenDiscover(
                        new()
                        {
                            Id = imdb,
                            Name = title.Name,
                            Type = title.IsMovie ? "movie" : "series",
                        }
                    );
                else
                {
                    SearchBox.Text = title.Name;
                    _ = SearchDiscover();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                store.Log(error);
                if (!navigationLife.IsCancellationRequested)
                {
                    window.Close();
                    Navigate("Discover");
                    SearchBox.Text = title.Name;
                    _ = SearchDiscover();
                }
            }
            finally
            {
                navigating = false;
            }
        }
        panel.Children.Add(CreditHeading("Known for", "أبرز الأعمال"));
        var rail = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (
            var title in titles.OrderByDescending(t => Match(t).Item != null).ThenByDescending(t => t.Year).Take(10)
        )
        {
            var match = Match(title);
            var info = new StackPanel { Width = 128 };
            info.Children.Add(
                CreditRemoteArt(
                    title.PosterUrl,
                    title.Name,
                    128,
                    192,
                    File.Exists(title.Poster) ? title.Poster : match.Item?.Poster ?? ""
                )
            );
            var name = Text(title.Name, 12.5, weight: FontWeights.SemiBold);
            name.Margin = new(0, 10, 0, 4);
            name.MaxHeight = 38;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            info.Children.Add(name);
            info.Children.Add(Text(Mark(title), 10.5, "#E4C084"));
            var button = ActionButton("", () => Open(title));
            button.Content = info;
            button.ToolTip = title.Name;
            button.Padding = new(0);
            button.Background = Brush("Transparent");
            button.Margin = new(0, 0, 17, 0);
            rail.Children.Add(button);
        }
        panel.Children.Add(CreditRail(rail));
        var heading = CreditHeading("Filmography", "قائمة الأعمال");
        heading.Margin = new(0, 28, 0, 18);
        panel.Children.Add(heading);
        foreach (var year in titles.OrderByDescending(t => t.Year).GroupBy(t => t.Year))
        {
            var label = Text(
                year.Key > 0 ? year.Key.ToString(Loc.Culture) : T("Year unknown", "السنة غير معروفة"),
                15,
                "#D7B77F",
                FontWeights.SemiBold
            );
            label.Margin = new(0, 15, 0, 8);
            panel.Children.Add(label);
            foreach (var title in year)
            {
                var row = new DockPanel { Margin = new(0, 0, 0, 1) };
                var mark = Text(Mark(title), 11, "#E4C084");
                mark.VerticalAlignment = VerticalAlignment.Center;
                mark.Margin = new(18, 0, 0, 0);
                DockPanel.SetDock(mark, Dock.Right);
                row.Children.Add(mark);
                var names = new StackPanel();
                names.Children.Add(Text(title.Name, 14, weight: FontWeights.SemiBold));
                var role = Text(
                    (title.IsMovie ? T("Movie", "فيلم") : T("Series", "مسلسل"))
                        + (title.Role.Length > 0 ? " · " + title.Role : ""),
                    11,
                    "#A29D96"
                );
                role.Margin = new(0, 4, 0, 0);
                names.Children.Add(role);
                row.Children.Add(names);
                var button = ActionButton("", () => Open(title));
                button.Content = row;
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.Background = Brush("#19191C");
                button.Padding = new(15, 12, 15, 12);
                button.Margin = new(0, 0, 0, 5);
                panel.Children.Add(button);
            }
        }
    }
}
