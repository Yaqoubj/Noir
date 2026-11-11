using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Noir;

public sealed record TasteFriend(string Code, DateTimeOffset SavedAt);

public partial class MainWindow
{
    readonly HashSet<int> tasteHidden = [];
    readonly Dictionary<int, DiscoverTitle> tasteMetadata = [];
    readonly HashSet<int> tasteMissing = [];
    string? tasteSelected;
    string tasteName = "";
    int tasteRevision;
    bool tasteCaptureMode = false;
    string TasteFriendsPath => Path.Combine(store.DataDirectory, "taste-friends.json");

    List<TasteFriend> TasteFriends()
    {
        try
        {
            var f = new FileInfo(TasteFriendsPath);
            return f.Exists && f.Length <= 262144
                ? (JsonSerializer.Deserialize<List<TasteFriend>>(File.ReadAllText(f.FullName, Encoding.UTF8)) ?? [])
                    .Where(f => f != null && TasteCode.TryDecode(f.Code, out _))
                    .Take(128)
                    .ToList()
                : [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    bool SaveTasteFriends(List<TasteFriend> friends)
    {
        try
        {
            File.WriteAllText(TasteFriendsPath + ".tmp", JsonSerializer.Serialize(friends), Encoding.UTF8);
            File.Move(TasteFriendsPath + ".tmp", TasteFriendsPath, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status.Text = T(
                "Couldn’t save friend cards. Check your profile storage.",
                "تعذر حفظ بطاقات الأصدقاء. تحقق من مساحة الملف الشخصي."
            );
            return false;
        }
    }

    Dictionary<int, VideoItem> TasteLibrary()
    {
        var result = new Dictionary<int, VideoItem>();
        foreach (var item in store.Archive)
        {
            var id = TasteCode.Id(item.ImdbId);
            if (id == 0)
                continue;
            var rating = store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Rating10 ?? 0;
            if (
                !result.TryGetValue(id, out var old)
                || rating > (store.State.PersonalReviews.GetValueOrDefault(old.Id)?.Rating10 ?? 0)
                || rating == (store.State.PersonalReviews.GetValueOrDefault(old.Id)?.Rating10 ?? 0)
                    && string.CompareOrdinal(item.Id, old.Id) < 0
            )
                result[id] = item;
        }
        return result;
    }

    TasteCard MyTaste(Dictionary<int, VideoItem> library, bool hide = true)
    {
        var byGroup = library.GroupBy(p => p.Value.GroupKey).ToDictionary(g => g.Key, g => g.First().Key);
        var full = TasteCode.Create(
            tasteName,
            store.State.Favorites.Where(byGroup.ContainsKey).Select(k => byGroup[k]),
            library.Select(p => new TasteRating(
                p.Key,
                store.State.PersonalReviews.GetValueOrDefault(p.Value.Id)?.Rating10 ?? 0
            ))
        );
        return hide ? TasteCode.Create(full.Name, full.Favorites, full.Ratings, tasteHidden) : full;
    }

    public void OpenTaste(string? code = null)
    {
        if (tasteName.Length == 0)
            tasteName = ProfileName;
        Navigate("Taste");
        if (code != null)
        {
            if (!TasteCode.TryDecode(code, out var card))
            {
                RenderTaste();
                Status.Text = T(
                    "This taste link is invalid or uses a newer version.",
                    "رابط الذوق غير صالح أو يستخدم إصدارًا أحدث."
                );
                return;
            }
            tasteSelected = TasteCode.Encode(card);
            var friends = TasteFriends();
            friends.RemoveAll(f => f.Code == tasteSelected);
            friends.Insert(0, new(tasteSelected, DateTimeOffset.UtcNow));
            SaveTasteFriends(friends.Take(128).ToList());
        }
        RenderTaste();
    }

    Border TastePanel(UIElement child, string background = "#171719") =>
        new()
        {
            Child = child,
            Background = Brush(background),
            BorderBrush = Brush("#302B25"),
            BorderThickness = new(1),
            CornerRadius = new(20),
            Padding = new(24),
            Margin = new(0, 0, 0, 18),
        };

    void RenderTaste()
    {
        var revision = ++tasteRevision;
        cinematic = false;
        ApplyPageLayout();
        SearchHost.Visibility = Visibility.Collapsed;
        PageEyebrow.Text = T("A LITTLE SHARED CINEMA", "سينما تجمعكما");
        PageTitle.Text = T("Compare taste", "قارن الذوق");
        ContentPanel.Children.Clear();
        var library = TasteLibrary();
        var mine = MyTaste(library, false);
        var candidates = mine.Favorites.Concat(mine.Ratings.Select(r => r.Id)).Distinct().ToArray();
        var allRatings = library.Count(p =>
            (store.State.PersonalReviews.GetValueOrDefault(p.Value.Id)?.Rating10 ?? 0) > 0
        );
        ContentPanel.Children.Add(
            Body(
                T(
                    "A link between friends. No account, no upload, just the stories you choose.",
                    "رابط بين الأصدقاء. بلا حساب أو رفع بيانات، فقط الأعمال التي تختارها."
                )
            )
        );
        var hero = new Grid();
        hero.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        hero.ColumnDefinitions.Add(new() { Width = new(300) });
        var left = new StackPanel { Margin = new(0, 0, 30, 0) };
        hero.Children.Add(left);
        left.Children.Add(Text(T("MY TASTE CARD", "بطاقة ذوقي"), 11, "#E4C084", FontWeights.SemiBold));
        left.Children.Add(Text(T("Your cinema, in a card.", "سينماك في بطاقة."), 30, weight: FontWeights.SemiBold));
        left.Children.Add(
            Body(
                T(
                    "Preview everything below. Untick any title to leave it out of both favorites and ratings.",
                    "راجع كل ما أدناه. أزل تحديد أي عمل لإخفائه من المفضلة والتقييمات."
                )
            )
        );
        var name = new TextBox
        {
            Text = mine.Name,
            MaxLength = 128,
            Background = Brush("#242426"),
            Foreground = Brush("#F4F1EB"),
            Padding = new(12, 9, 12, 9),
            FontSize = 16,
            Margin = new(0, 0, 0, 10),
        };
        System.Windows.Automation.AutomationProperties.SetName(
            name,
            T("Display name included in your link", "اسم العرض المضمّن في الرابط")
        );
        left.Children.Add(Text(T("Display name included in your link", "اسم العرض المضمّن في الرابط"), 11, "#ADA69B"));
        left.Children.Add(name);
        var count = Text("", 12, "#C7B696");
        left.Children.Add(count);
        left.Children.Add(
            Text(
                T(
                    "Only IMDb IDs, ratings and this name are included. No notes, dates or watch history.",
                    "يتضمن الرابط معرّفات IMDb والتقييمات وهذا الاسم فقط. بلا ملاحظات أو تواريخ أو سجل مشاهدة."
                ),
                11,
                "#9D978D"
            )
        );
        if (allRatings > 60)
            left.Children.Add(
                Body(
                    T(
                        "The first 60 rated IMDb titles are selected by ID. Hidden titles are never replaced automatically.",
                        "يُختار أول 60 عملاً مقيّمًا حسب معرّف IMDb. لا تُستبدل الأعمال المخفية تلقائيًا."
                    ),
                    11
                )
            );
        var checks = new StackPanel { Margin = new(0, 12, 0, 0) };
        left.Children.Add(
            new ScrollViewer
            {
                Content = checks,
                MaxHeight = 212,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            }
        );
        var right = new StackPanel();
        Grid.SetColumn(right, 1);
        hero.Children.Add(right);
        var qrHost = new Border
        {
            Height = 246,
            Width = 246,
            CornerRadius = new(12),
            Background = Brush("#23211E"),
            Padding = new(12),
            Child = Text(
                T("Your QR appears after you review and create your card.", "يظهر رمز QR بعد مراجعة البطاقة وإنشائها."),
                15,
                "#BEB09A"
            ),
        };
        right.Children.Add(qrHost);
        var copy = ActionButton(T("Copy taste link", "نسخ رابط الذوق"), () => { }, true);
        copy.IsEnabled = false;
        copy.Margin = new(0, 12, 0, 0);
        right.Children.Add(copy);
        var note = Text(
            T("Nothing is shared until you copy the link.", "لا تتم مشاركة شيء حتى تنسخ الرابط."),
            11,
            "#A59B8C"
        );
        note.Margin = new(0, 9, 0, 0);
        right.Children.Add(note);
        string? link = null;
        void Invalidate()
        {
            link = null;
            copy.IsEnabled = false;
            qrHost.Padding = new(12);
            qrHost.Background = Brush("#23211E");
            qrHost.Child = Text(
                T("Review your changes, then create a fresh card.", "راجع تغييراتك ثم أنشئ بطاقة جديدة."),
                15,
                "#BEB09A"
            );
            var chosen = MyTaste(library);
            count.Text = T(
                "{0} favorites · {1} ratings included",
                "{0} أعمال مفضلة · {1} تقييمًا مضمّنًا",
                chosen.Favorites.Length,
                chosen.Ratings.Length
            );
        }
        foreach (var id in candidates)
        {
            var rating = mine.Ratings.FirstOrDefault(r => r.Id == id)?.Rating10 ?? 0;
            var title = library[id];
            var box = new CheckBox
            {
                IsChecked = !tasteHidden.Contains(id),
                Content =
                    title.Title
                    + (mine.Favorites.Contains(id) ? "  [[heart-filled]]" : "")
                    + (rating > 0 ? T("  ·  {0}/10", "  ·  {0}/10", rating) : ""),
                Foreground = Brush("#E4DED3"),
                Padding = new(4, 6, 4, 6),
                FontSize = 13,
            };
            box.ToolTip = T("Include this title", "تضمين هذا العمل");
            box.Checked += (_, _) =>
            {
                tasteHidden.Remove(id);
                Invalidate();
            };
            box.Unchecked += (_, _) =>
            {
                tasteHidden.Add(id);
                Invalidate();
            };
            checks.Children.Add(box);
        }
        name.TextChanged += (_, _) =>
        {
            tasteName = TasteCode.Name(name.Text);
            Invalidate();
        };
        count.Text =
            Loc.Count(
                MyTaste(library).Favorites.Length,
                "favorite",
                "favorites",
                "عمل مفضل واحد",
                "عملان مفضلان",
                "أعمال مفضلة",
                "عملًا مفضلًا"
            )
            + " · "
            + Loc.Count(
                MyTaste(library).Ratings.Length,
                "rating included",
                "ratings included",
                "تقييم واحد مضمّن",
                "تقييمان مضمّنان",
                "تقييمات مضمّنة",
                "تقييمًا مضمّنًا"
            );
        var create = ActionButton(
            T("Create reviewed card", "إنشاء البطاقة بعد المراجعة"),
            () =>
            {
                tasteName = TasteCode.Name(name.Text);
                name.Text = tasteName;
                var card = MyTaste(library);
                link = TasteCode.Link(card);
                using var data = QRCodeGenerator.GenerateQrCode(link, QRCodeGenerator.ECCLevel.M);
                using var qr = new PngByteQRCode(data);
                using var bytes = new MemoryStream(qr.GetGraphic(8));
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = bytes;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                var image = new Image { Source = bitmap, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                qrHost.Padding = new(0);
                qrHost.Background = Brush("#FFFFFF");
                qrHost.Child = image;
                copy.IsEnabled = true;
                Motion.Pop(qrHost);
            },
            true
        );
        create.Margin = new(0, 14, 0, 0);
        left.Children.Add(create);
        copy.Click += (_, _) =>
        {
            if (link == null)
                return;
            try
            {
                Clipboard.SetText(link);
                note.Text = T(
                    "Link copied. Only your reviewed selection is included.",
                    "نُسخ الرابط. يتضمن اختياراتك التي راجعتها فقط."
                );
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                note.Text = T("Clipboard unavailable. Please retry.", "الحافظة غير متاحة. حاول مجددًا.");
            }
        };
        ContentPanel.Children.Add(TastePanel(hero));
        var open = new DockPanel();
        var openButton = ActionButton(T("Open taste link", "فتح رابط ذوق"), () => { });
        DockPanel.SetDock(openButton, Dock.Right);
        open.Children.Add(openButton);
        var input = new TextBox
        {
            MaxLength = 600,
            Background = Brush("#242426"),
            Foreground = Brush("#F4F1EB"),
            Padding = new(12),
            FontSize = 14,
            Margin = new(0, 0, 12, 0),
            ToolTip = T("Paste a noir://taste link", "ألصق رابط noir://taste"),
        };
        System.Windows.Automation.AutomationProperties.SetName(
            input,
            T("Paste a noir://taste link", "ألصق رابط noir://taste")
        );
        open.Children.Add(input);
        openButton.Click += (_, _) => OpenTaste(input.Text);
        ContentPanel.Children.Add(TastePanel(open));
        var friends = TasteFriends();
        var heading = new DockPanel();
        heading.Children.Add(Text(T("Your circle", "دائرتك"), 24, weight: FontWeights.SemiBold));
        ContentPanel.Children.Add(heading);
        ContentPanel.Children.Add(
            Body(
                T(
                    "Friend cards are saved only in this profile. Delete them whenever you like.",
                    "تُحفظ بطاقات الأصدقاء في هذا الملف فقط. احذفها متى شئت."
                ),
                12
            )
        );
        var friendRow = new WrapPanel();
        ContentPanel.Children.Add(friendRow);
        foreach (var friend in friends)
        {
            if (!TasteCode.TryDecode(friend.Code, out var card))
                continue;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var select = ActionButton(
                card.Name.Length > 0 ? card.Name : T("Cinema friend", "صديق السينما"),
                () =>
                {
                    tasteSelected = friend.Code;
                    RenderTaste();
                },
                tasteSelected == friend.Code
            );
            row.Children.Add(select);
            var delete = ActionButton("×", () =>
            {
                var kept = TasteFriends();
                kept.RemoveAll(f => f.Code == friend.Code);
                if (SaveTasteFriends(kept))
                {
                    if (tasteSelected == friend.Code)
                        tasteSelected = null;
                    RenderTaste();
                }
            });
            delete.ToolTip = T("Delete friend card", "حذف بطاقة الصديق");
            System.Windows.Automation.AutomationProperties.SetName(delete, T("Delete friend card", "حذف بطاقة الصديق"));
            row.Children.Add(delete);
            row.Margin = new(0, 0, 10, 10);
            friendRow.Children.Add(row);
        }
        if (friends.Count == 0)
            ContentPanel.Children.Add(
                Body(
                    T(
                        "Paste a friend’s link to find your next shared favorite.",
                        "ألصق رابط صديق لاكتشاف عمل مفضل يجمعكما."
                    )
                )
            );
        if (tasteSelected != null && TasteCode.TryDecode(tasteSelected, out var theirs))
        {
            var comparison = new StackPanel();
            ContentPanel.Children.Add(TastePanel(comparison, "#1C1915"));
            RenderTasteComparison(comparison, MyTaste(library, false), theirs, library, revision);
            _ = LoadTasteMetadata(theirs, comparison, MyTaste(library, false), library, revision);
        }
        if (!tasteCaptureMode)
            Motion.FadeIn(hero);
    }

    Dictionary<int, string[]> TasteGenres(Dictionary<int, VideoItem> library)
    {
        var g = library.ToDictionary(
            p => p.Key,
            p =>
                p.Value.Genres.Split(
                    new[] { '·', ',' },
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                )
        );
        foreach (var p in tasteMetadata)
            g[p.Key] = p.Value.Genres.ToArray();
        return g;
    }

    FrameworkElement TastePoster(string path, string title)
    {
        if (File.Exists(path))
            return Art(path, title, 150, true);
        var glyph = Text("✦", 38, "#75634A");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        return new Border
        {
            Height = 150,
            CornerRadius = new(10),
            BorderBrush = Brush("#393024"),
            BorderThickness = new(1),
            Background = new LinearGradientBrush(Brush("#302A22").Color, Brush("#151516").Color, 45),
            Child = glyph,
        };
    }

    void RenderTasteComparison(
        StackPanel body,
        TasteCard mine,
        TasteCard theirs,
        Dictionary<int, VideoItem> library,
        int revision
    )
    {
        if (revision != tasteRevision || page != "Taste")
            return;
        body.Children.Clear();
        var match = TasteCode.Compare(mine, theirs, TasteGenres(library));
        body.Children.Add(
            Text(theirs.Name.Length > 0 ? theirs.Name : T("Cinema friend", "صديق السينما"), 18, "#CDBB9A")
        );
        body.Children.Add(
            Text(
                match.Score.HasValue
                    ? T("{0}% in tune", "{0}% من التوافق", match.Score)
                    : T("A new connection", "بداية تعارف"),
                40,
                "#E4C084",
                FontWeights.SemiBold
            )
        );
        body.Children.Add(
            Body(
                match.Overlap < 5
                    ? T(
                        "Only {0} shared ratings · an early estimate from ratings, favorites and known genres.",
                        "{0} تقييمات مشتركة فقط · تقدير أولي من التقييمات والمفضلة والأنواع المعروفة.",
                        match.Overlap
                    )
                : match.Correlation
                    ? T(
                        "Based on rating correlation across {0} shared titles.",
                        "بناءً على ارتباط التقييمات عبر {0} أعمال مشتركة.",
                        match.Overlap
                    )
                : T(
                    "{0} shared ratings · compared by rating distance.",
                    "{0} تقييمات مشتركة · قورنت بفروق التقييم.",
                    match.Overlap
                )
            )
        );
        if (!match.Score.HasValue)
            body.Children.Add(
                Body(T("Not enough shared evidence for a score yet.", "لا توجد أدلة مشتركة كافية لحساب التوافق بعد."))
            );
        void Section(string label, int[] ids, bool add)
        {
            body.Children.Add(Text(label, 18, weight: FontWeights.SemiBold));
            var rows = new WrapPanel { Margin = new(0, 10, 0, 20) };
            body.Children.Add(rows);
            foreach (var id in ids)
            {
                var panel = new StackPanel { Width = 150, Margin = new(0, 0, 16, 12) };
                var local = library.GetValueOrDefault(id);
                var meta = tasteMetadata.GetValueOrDefault(id);
                var title = local?.Title ?? meta?.Name ?? TasteCode.Imdb(id);
                panel.Children.Add(TastePoster(local?.Poster ?? meta?.Poster ?? "", title));
                panel.Children.Add(Text(title, 13, weight: FontWeights.SemiBold));
                if (add)
                {
                    var button = ActionButton(
                        local != null && store.State.Watchlist.Contains(local.GroupKey)
                            ? T("[[check]] My List", "[[check]] قائمتي")
                            : T("[[plus]] My List", "[[plus]] قائمتي"),
                        () => { }
                    );
                    button.IsEnabled = local != null || meta != null;
                    button.Click += async (_, _) =>
                    {
                        button.IsEnabled = false;
                        try
                        {
                            if (local != null)
                            {
                                store.State.Watchlist.Add(local.GroupKey);
                                store.Save();
                            }
                            else if (meta != null)
                                await Task.Run(() => store.AddDiscovered(meta, true));
                            button.Content = T("[[check]] My List", "[[check]] قائمتي");
                            Motion.Pop(button);
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        {
                            button.IsEnabled = true;
                            Status.Text = T("Couldn’t add this title. Please retry.", "تعذر إضافة العمل. حاول مجددًا.");
                        }
                    };
                    panel.Children.Add(button);
                }
                else
                {
                    var a = mine.Ratings.FirstOrDefault(r => r.Id == id)?.Rating10;
                    var b = theirs.Ratings.FirstOrDefault(r => r.Id == id)?.Rating10;
                    panel.Children.Add(
                        Text(
                            T("You {0} · Them {1}", "أنت {0} · الصديق {1}", a?.ToString() ?? "♥", b?.ToString() ?? "♥"),
                            11,
                            "#C7B696"
                        )
                    );
                }
                if (local == null && meta == null)
                    panel.Children.Add(
                        Text(
                            tasteMissing.Contains(id)
                                ? T("Title unavailable · retry", "العمل غير متاح · حاول مجددًا")
                                : T("Finding title…", "جارٍ البحث عن العمل…"),
                            11,
                            "#A59B8C"
                        )
                    );
                rows.Children.Add(panel);
            }
            if (ids.Length == 0)
                rows.Children.Add(
                    Body(
                        T("More shared stories will fill this space.", "ستملأ الأعمال المشتركة القادمة هذه المساحة."),
                        12
                    )
                );
        }
        Section(T("You both loved", "أحببتماه معًا"), match.BothLoved, false);
        var seen = store.Archive.Where(i => store.EverWatched(i)).Select(i => TasteCode.Id(i.ImdbId)).ToHashSet();
        Section(
            T("They loved, you haven’t seen", "أحبه الصديق ولم تشاهده"),
            match.UnseenLoved.Where(id => !seen.Contains(id)).ToArray(),
            true
        );
        Section(T("Big disagreements", "اختلافات كبيرة"), match.Disagreements, false);
        var retry = ActionButton(T("Refresh title details", "تحديث تفاصيل الأعمال"), () =>
        {
            tasteMissing.Clear();
            _ = LoadTasteMetadata(theirs, body, mine, library, revision);
        });
        body.Children.Add(retry);
        body.Children.Add(
            Body(
                T(
                    "Names and posters come from Cinemeta. Your name, ratings and taste link are never uploaded.",
                    "الأسماء والملصقات من Cinemeta. لا يُرفع اسمك أو تقييماتك أو رابط ذوقك أبدًا."
                ),
                11
            )
        );
    }

    async Task LoadTasteMetadata(
        TasteCard theirs,
        StackPanel body,
        TasteCard mine,
        Dictionary<int, VideoItem> library,
        int revision
    )
    {
        var ids = theirs
            .Favorites.Concat(theirs.Ratings.Select(r => r.Id))
            .Distinct()
            .Where(id => !library.ContainsKey(id) && !tasteMetadata.ContainsKey(id) && !tasteMissing.Contains(id))
            .ToArray();
        if (ids.Length == 0)
            return;
        using var slots = new SemaphoreSlim(4);
        var loaded = await Task.Run(async () =>
        {
            using var client = new DiscoveryClient(store.DataDirectory);
            return await Task.WhenAll(
                ids.Select(async id =>
                {
                    await slots.WaitAsync();
                    try
                    {
                        foreach (var type in new[] { "movie", "series" })
                            try
                            {
                                var title = await client.Details(
                                    new() { Id = TasteCode.Imdb(id), Type = type },
                                    CancellationToken.None
                                );
                                if (title.Name.Length > 0)
                                    return (id, title);
                            }
                            catch (Exception e)
                                when (e
                                        is System.Net.Http.HttpRequestException
                                            or IOException
                                            or TaskCanceledException
                                            or JsonException
                                ) { }
                        return (id, title: (DiscoverTitle?)null);
                    }
                    finally
                    {
                        slots.Release();
                    }
                })
            );
        });
        foreach (var (id, title) in loaded)
            if (title != null)
                tasteMetadata[id] = title;
            else
                tasteMissing.Add(id);
        if (!closing)
            RenderTasteComparison(body, mine, theirs, library, revision);
    }
#if NOIR_VALIDATE
    // Manager ValidatePlayback hook: if(Environment.GetCommandLineArgs().Contains("--taste-only")){await ValidateTastePage();return;}
    public static void RenderTasteValidation(string directory)
    {
        Profiles.RootOverride = Path.GetFullPath(directory);
        var window = new MainWindow(directory, validate: true);
        window.tasteCaptureMode = true;
        window.tasteName = "Leila";
        window.SetLanguage("en");
        if (window.store.Archive.Count == 0)
        {
            var titles = new[]
            {
                ("tt0133093", "The Matrix", 9),
                ("tt0816692", "Interstellar", 10),
                ("tt1375666", "Inception", 9),
                ("tt0111161", "The Shawshank Redemption", 10),
                ("tt0109830", "Forrest Gump", 7),
                ("tt0108052", "Schindler’s List", 9),
                ("tt0468569", "The Dark Knight", 8),
                ("tt0110912", "Pulp Fiction", 6),
            };
            foreach (var (id, title, rating) in titles)
            {
                var item = window.store.AddDiscovered(
                    new()
                    {
                        Id = id,
                        Name = title,
                        Type = "movie",
                        Genres = ["Drama"],
                    },
                    false
                )[0];
                window.store.SetReview10(item, rating, "");
            }
            window.store.SetFavorites(window.store.Archive.Take(4).Select(i => i.GroupKey));
        }
        window.OpenTaste();
        var surface = window.Content;
        window.Content = null;
        var host = new Window
        {
            Content = surface,
            Title = "Noir · Taste validation",
            Width = 1320,
            Height = 1000,
            Background = Brush("#101012"),
            ShowActivated = false,
        };
        var frame = new System.Windows.Threading.DispatcherFrame();
        host.Closed += (_, _) => frame.Continue = false;
        host.Show();
        host.Dispatcher.BeginInvoke(
            new Action(async () =>
            {
                try
                {
                    await Task.Delay(900);
                    window.RenderTaste();
                    host.UpdateLayout();
                    window.CaptureLibrary(Path.Combine(directory, "taste-preview.png"));
                    var create = Descendants(window.ContentPanel)
                        .OfType<Button>()
                        .First(b =>
                            b.Content is string s && s == T("Create reviewed card", "إنشاء البطاقة بعد المراجعة")
                        );
                    create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    host.UpdateLayout();
                    window.CaptureLibrary(Path.Combine(directory, "taste-card.png"));
                    var box = Descendants(window.ContentPanel).OfType<CheckBox>().First();
                    box.IsChecked = false;
                    if (
                        Descendants(window.ContentPanel)
                            .OfType<Button>()
                            .First(b => b.Content is string s && s == T("Copy taste link", "نسخ رابط الذوق"))
                            .IsEnabled
                    )
                        throw new Exception("Changed selection must invalidate sharing");
                    var theirs = TasteCode.Create(
                        "Omar",
                        [133093, 816692],
                        [new(133093, 9), new(816692, 10), new(1375666, 3), new(111161, 10), new(109830, 7)]
                    );
                    window.OpenTaste(TasteCode.Link(theirs));
                    await Task.Delay(350);
                    window.RenderTaste();
                    host.UpdateLayout();
                    var comparison = (FrameworkElement)
                        window.ContentPanel.Children[window.ContentPanel.Children.Count - 1];
                    window.ContentScroll.ScrollToVerticalOffset(
                        comparison.TransformToAncestor(window.PageBody).Transform(new Point()).Y - 100
                    );
                    host.UpdateLayout();
                    window.CaptureLibrary(Path.Combine(directory, "taste-comparison.png"));
                    window.ContentScroll.ScrollToBottom();
                    host.UpdateLayout();
                    window.CaptureLibrary(Path.Combine(directory, "taste-comparison-bottom.png"));
                    window.tasteSelected = null;
                    window.SetLanguage("ar");
                    await Task.Delay(500);
                    window.RenderTaste();
                    window.ContentScroll.ScrollToTop();
                    host.UpdateLayout();
                    window.CaptureLibrary(Path.Combine(directory, "taste-ar.png"));
                    File.WriteAllText(
                        Path.Combine(directory, "taste-checks.json"),
                        "[\"PASS: preview, reviewed QR, privacy invalidation, comparison and Arabic rendered\"]",
                        Encoding.UTF8
                    );
                }
                catch (Exception e)
                {
                    File.WriteAllText(Path.Combine(directory, "taste-checks.json"), "FAIL: " + e, Encoding.UTF8);
                }
                finally
                {
                    host.Close();
                }
            })
        );
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    public async Task ValidateTastePage()
    {
        await Task.Delay(1500);
        WindowState = WindowState.Normal;
        Width = 1320;
        Height = 1000;
        OpenTaste();
        UpdateLayout();
        await Task.Delay(700);
        CaptureLibrary(Path.Combine(store.DataDirectory, "taste-preview.png"));
        var create = Descendants(ContentPanel)
            .OfType<Button>()
            .First(b => b.Content is string s && s == T("Create reviewed card", "إنشاء البطاقة بعد المراجعة"));
        create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(350);
        UpdateLayout();
        CaptureLibrary(Path.Combine(store.DataDirectory, "taste-card.png"));
        SetLanguage("ar");
        RenderTaste();
        await Task.Delay(400);
        UpdateLayout();
        CaptureLibrary(Path.Combine(store.DataDirectory, "taste-ar.png"));
        File.WriteAllText(
            Path.Combine(store.DataDirectory, "taste-checks.json"),
            "[\"PASS: preview, reviewed QR and Arabic page rendered\"]",
            Encoding.UTF8
        );
        Close();
    }
#endif
}
