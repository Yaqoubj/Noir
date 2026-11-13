using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noir;

public partial class MainWindow
{
    static string ReadyText(string value) =>
        value switch
        {
            "Preparation paused" => T("Keep ready paused", "إبقاء الحلقات جاهزة متوقف مؤقتًا"),
            "Preparation budget reached" => T("Keep ready budget reached", "تم بلوغ ميزانية إبقاء الحلقات جاهزة"),
            "Keeping free space available" => T(value, "الحفاظ على المساحة الحرة المطلوبة"),
            "Availability or size unconfirmed; recheck scheduled" => T(
                value,
                "لم يتأكد التوفر أو الحجم؛ تمت جدولة إعادة التحقق"
            ),
            "Waiting for an aired unwatched episode" => T(value, "بانتظار حلقة عُرضت ولم تُشاهد"),
            "Playable offline" => T(value, "جاهز للمشاهدة دون اتصال"),
            "Preparing on this PC" => T(value, "جارٍ التحضير على هذا الكمبيوتر"),
            "Download paused" => T(value, "التنزيل متوقف مؤقتًا"),
            "Connect a download source in Settings" => T(value, "اربط مصدر تنزيل في الإعدادات"),
            "Source check failed; recheck scheduled" => T(value, "تعذر فحص المصدر؛ تمت جدولة إعادة التحقق"),
            "Ready to download" => T(value, "جاهز للتنزيل"),
            "Checking sources" => T(value, "جارٍ فحص المصادر"),
            "Preparation check" => T("Keep ready check", "فحص إبقاء الحلقات جاهزة"),
            "Queued confirmed ready file" => T(value, "تمت جدولة ملف مؤكد الجاهزية"),
            "Migrated keep ready preferences" => T(value, "تم نقل تفضيلات إبقاء الحلقات جاهزة"),
            "Watched file removal scheduled" => T(value, "تمت جدولة إزالة ملف تمت مشاهدته"),
            "Removed watched managed file" => T(value, "تمت إزالة ملف مُدار تمت مشاهدته"),
            "File changed; removal cancelled" => T(value, "تغير الملف؛ أُلغيت الإزالة"),
            "Preparation could not finish" => T("Keep ready could not finish", "تعذر إكمال إبقاء الحلقات جاهزة"),
            "Preferences changed" => T(value, "تغيرت التفضيلات"),
            "Pause preparation" => T("Pause keep ready", "إيقاف إبقاء الحلقات جاهزة مؤقتًا"),
            "Resume preparation" => T("Resume keep ready", "استئناف إبقاء الحلقات جاهزة"),
            "Keep file" => T("Keep this file", "الاحتفاظ بهذا الملف"),
            "Cancel removal" => T(value, "إلغاء الإزالة"),
            "Pin file" => T(value, "تثبيت الملف"),
            "Unpin file" => T(value, "إلغاء تثبيت الملف"),
            "Startup" => T(value, "بدء التشغيل"),
            "Episode finished" => T(value, "انتهت الحلقة"),
            "New episode" => T(value, "حلقة جديدة"),
            "Six-hour refresh" => T(value, "تحديث كل ست ساعات"),
            "Retry" => T(value, "إعادة المحاولة"),
            // A1–A8 rules: which rule removed, scheduled or downloaded something (Activity log, U73).
            "Moved removal settings to the new rules" => T(value, "نُقلت إعدادات الإزالة إلى القواعد الجديدة"),
            "Removal date changed" => T(value, "تغيّر موعد الإزالة"),
            "Removed a watched Noir download" => T(value, "أُزيل ملف شوهد نزّله Noir"),
            "Sent a watched Keep ready file to the Recycle Bin" => T(
                value,
                "نُقل ملف شوهد من إبقاء الحلقات جاهزة إلى سلة المحذوفات"
            ),
            "Sent a watched Noir download to the Recycle Bin" => T(value, "نُقل ملف شوهد نزّله Noir إلى سلة المحذوفات"),
            "Removal failed; the file stays" => T(value, "تعذرت الإزالة؛ يبقى الملف"),
            "Waiting for download hours" => T(value, "بانتظار ساعات التنزيل"),
            "Right after watching" => T(value, "فور المشاهدة"),
            "1 day after watching" => T(value, "بعد يوم من المشاهدة"),
            "7 days after watching" => T(value, "بعد 7 أيام من المشاهدة"),
            "30 days after watching" => T(value, "بعد 30 يومًا من المشاهدة"),
            "Never" => T(value, "أبدًا"),
            "Downloaded by Noir" => T(value, "نُزّل بواسطة Noir"),
            "Keep ready file" => T(value, "ملف من إبقاء الحلقات جاهزة"),
            "Switched on for this show" => T(value, "مفعّل لهذا المسلسل"),
            "Also download automatically" => T(value, "تنزيل تلقائي أيضًا"),
            "A show you are watching" => T(value, "مسلسل تتابعه"),
            "On your watchlist" => T(value, "في قائمتك"),
            _ => T(value),
        };

    // Activity entries whose detail names the rule that acted (A1–A8).
    static readonly HashSet<string> ReadyRuleCodes =
    [
        "Watched file removal scheduled",
        "Removal date changed",
        "Removed watched managed file",
        "Removed a watched Noir download",
        "Sent a watched Keep ready file to the Recycle Bin",
        "Sent a watched Noir download to the Recycle Bin",
        "Removal failed; the file stays",
        "Queued confirmed ready file",
    ];

    static string ReadyDetail(string detail) => string.Join(" · ", detail.Split(" · ").Select(ReadyText));

    static (string Label, int Value)[] ReadyRemoveOptions() =>
        [
            (T("Never", "أبدًا"), -1),
            (T("Right after watching", "فور المشاهدة"), 1),
            (T("After 1 day", "بعد يوم"), 24),
            (T("After 7 days", "بعد 7 أيام"), 168),
            (T("After 30 days", "بعد 30 يومًا"), 720),
        ];

    static (string Label, int Value)[] ReadyCountOptions() =>
        [
            (T("Next episode", "الحلقة التالية"), 1),
            (T("Next 2", "الحلقتان التاليتان"), 2),
            (T("Next 3", "الحلقات الثلاث التالية"), 3),
            (T("Next 5", "الحلقات الخمس التالية"), 5),
            (T("Rest of the season", "بقية الموسم"), -1),
        ];

    static (string Label, int Value)[] ReadyKeepLastOptions() =>
        [
            (T("None", "لا شيء"), 0),
            (T("Last 1", "آخر حلقة"), 1),
            (T("Last 2", "آخر حلقتين"), 2),
            (T("Last 3", "آخر 3 حلقات"), 3),
        ];

    // Per show: "Same as default" first, naming what the default is today.
    static (string Label, int? Value)[] ReadyWithDefault((string Label, int Value)[] options, int current) =>
        new[]
        {
            (
                T(
                    "Same as default · {0}",
                    "مثل الافتراضي · {0}",
                    options.FirstOrDefault(o => o.Value == current).Label ?? options[0].Label
                ),
                (int?)null
            ),
        }
            .Concat(options.Select(o => (o.Label, (int?)o.Value)))
            .ToArray();

    // U47 pick-one row for the Keep ready rules: a caption, gold pills, an optional hint. Picking restyles the row.
    FrameworkElement ReadyChoice<TV>(
        string caption,
        (string Label, TV Value)[] options,
        TV selected,
        Action<TV> pick,
        string hint = ""
    )
    {
        var column = new StackPanel { Margin = new(0, 12, 0, 2) };
        column.Children.Add(Text(caption, 12, "#E4C084"));
        var row = new WrapPanel { Margin = new(0, 6, 0, 0) };
        var buttons = new List<(Button Button, TV Value)>();
        foreach (var (label, value) in options)
        {
            var button = PillOption(
                label,
                () =>
                {
                    foreach (var (other, v) in buttons)
                        StylePillOption(other, EqualityComparer<TV>.Default.Equals(v, value));
                    pick(value);
                },
                EqualityComparer<TV>.Default.Equals(value, selected)
            );
            System.Windows.Automation.AutomationProperties.SetName(button, caption + " · " + label);
            buttons.Add((button, value));
            row.Children.Add(button);
        }
        column.Children.Add(row);
        if (hint.Length > 0)
            column.Children.Add(Body(hint, 12));
        return column;
    }

    // Arabic: each Latin value (a size, a quality) is its own left-to-right run, so "60 GB" keeps its order.
    static void LtrRuns(TextBlock block, params string[] values)
    {
        if (!Loc.Arabic)
            return;
        var text = block.Text;
        block.Inlines.Clear();
        var at = 0;
        foreach (var value in values)
        {
            var i = value.Length == 0 ? -1 : text.IndexOf(value, at, StringComparison.Ordinal);
            if (i < 0)
                continue;
            if (i > at)
                block.Inlines.Add(new System.Windows.Documents.Run(text[at..i]));
            block.Inlines.Add(new System.Windows.Documents.Run(value) { FlowDirection = FlowDirection.LeftToRight });
            at = i + value.Length;
        }
        if (at < text.Length)
            block.Inlines.Add(new System.Windows.Documents.Run(text[at..]));
    }

    static string ReadySize(long bytes) => NoirReadyPolicy.SizeText(bytes);

    Border ReadyCard(UIElement child) =>
        new()
        {
            Background = Brush("#19191C"),
            BorderBrush = Brush("#343029"),
            BorderThickness = new(1),
            CornerRadius = new(14),
            Padding = new(20),
            Margin = new(0, 8, 0, 12),
            Child = child,
        };

    public string? ReadyTitleInfo(string group)
    {
        var title = downloads?.PreparedTitle(group);
        if (title == null)
            return null;
        return downloads!.ReadyViewState.Paused
            ? ReadyText("Preparation paused")
            : ReadyText(title.Reason) + (title.Bytes > 0 ? " · " + ReadySize(title.Bytes) : "");
    }

    // Home's Keep ready row: the same artwork cards and arrows as every other row (it used to be text boxes with a
    // scrollbar). A card plays the episode when it is on this PC and opens Downloads when it is still on its way.
    public FrameworkElement? ReadyShelf()
    {
        if (downloads == null)
            return null;
        var state = downloads.ReadyViewState;
        // Home shows what Keep ready has on this PC or is bringing in now. A title it could download but has not been
        // asked to ("Ready to download") is a preview, and stays in Downloads where it can be switched on.
        var titles = state
            .Titles.Values.Where(t => t.Episodes.Count > 0 && (t.Episodes[0].Local || t.State is "Offline" or "Preparing"))
            .OrderByDescending(t => t.Episodes[0].Local)
            .Take(12)
            .ToList();
        if (titles.Count == 0)
            return null;
        var cards = new List<FrameworkElement>();
        var anyLocal = false;
        foreach (var title in titles)
        {
            var first = title.Episodes.First();
            var group = store.EpisodesFor(title.Group);
            var item = group.FirstOrDefault(e => e.Id == first.ItemId) ?? group.FirstOrDefault();
            if (item == null)
                continue;
            var local = first.Local && CanPlay(item);
            anyLocal |= local;
            var panel = new StackPanel
            {
                Width = Density.ContinueWidth,
                Margin = new(0, 0, Density.CardGap, 12),
                Tag = "HomeReadyCard",
            };
            var picture = new Grid { Height = Density.ContinueHeight, ClipToBounds = true };
            var open = ActionButton("", () =>
            {
                if (local)
                    _ = PlayVideo(item);
                else
                    ShowDownloads();
            });
            open.Content = Art(
                File.Exists(item.Backdrop) ? item.Backdrop : item.Poster,
                item.Title,
                Density.ContinueHeight,
                false,
                false
            );
            open.Padding = new(0);
            open.HorizontalAlignment = HorizontalAlignment.Stretch;
            var what = local
                ? T("Play {0} offline", "شاهد {0} دون اتصال", item.Label)
                : T("{0} · open Downloads", "{0} · افتح التنزيلات", item.Label);
            open.ToolTip = what;
            System.Windows.Automation.AutomationProperties.SetName(open, what);
            picture.Children.Add(open);
            picture.Children.Add(
                new Border
                {
                    IsHitTestVisible = false,
                    Background = new LinearGradientBrush(Colors.Transparent, Color.FromArgb(235, 11, 11, 13), 90),
                }
            );
            if (local)
                picture.Children.Add(
                    new Border
                    {
                        IsHitTestVisible = false,
                        Background = Brush("#E6101012"),
                        BorderBrush = Brush("#66E4C084"),
                        BorderThickness = new(1),
                        CornerRadius = new(6),
                        Padding = new(8, 3, 8, 3),
                        Margin = new(10),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        Child = Text(T("On this PC", "على هذا الكمبيوتر"), 10.5, "#E4C084", FontWeights.SemiBold),
                    }
                );
            var caption = new StackPanel
            {
                Margin = new(14, 0, 14, 12),
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
            };
            caption.Children.Add(
                HomeTitleMark(item, Density.Scaled(44), Math.Min(240, Density.ContinueWidth - 28), 18)
            );
            var line = Text("", 11.5, "#D9D4CC");
            SetMarked(
                line,
                (
                    first.Season == 0
                        ? ""
                        : Ltr + "S" + first.Season.ToString("00") + " · E" + first.Episode.ToString("00") + End + " · "
                ) + (state.Paused ? ReadyText("Preparation paused") : ReadyText(title.Reason))
            );
            line.TextWrapping = TextWrapping.NoWrap;
            line.TextTrimming = TextTrimming.CharacterEllipsis;
            line.Margin = new(0, 4, 0, 0);
            caption.Children.Add(line);
            picture.Children.Add(caption);
            var edge = new Border
            {
                CornerRadius = new(10),
                ClipToBounds = true,
                Child = picture,
                BorderThickness = new(1.5),
                BorderBrush = Brush("#00000000"),
            };
            panel.Children.Add(edge);
            cards.Add(FollowHomeHero(Lift(panel, edge), () => item));
        }
        if (cards.Count == 0)
            return null;
        return Rail(
            T("Keep ready", "إبقاء الحلقات جاهزة"),
            anyLocal
                ? T("Downloaded ahead and waiting for you.", "نُزّلت مسبقًا وتنتظرك.")
                : T("On their way, so they are ready when you are.", "في الطريق، لتكون جاهزة عند حاجتك."),
            cards,
            ShowDownloads,
            T("Manage", "إدارة")
        );
    }

    public async void OfferKeepReady(VideoItem finished)
    {
        if (downloads == null || finished.IsMovie)
            return;
        var manager = downloads;
        await manager.ChangeReady(s => { });
        if (
            manager.ReadyOfferShown(finished.GroupKey)
            || NoirReadyPolicy.Enabled(manager.SeasonPreferences(finished.Id))
        )
            return;
        await manager.ChangeReady(s => s.Offered.Add(finished.GroupKey));
        if (!closing)
            ShowKeepReady(finished);
    }

    public async Task SetAlsoDownload(VideoItem item, bool enabled)
    {
        if (downloads == null)
            return;
        var manager = downloads;
        await Task.Run(() =>
        {
            var preference = manager.SeasonPreferences(item.Id);
            preference.AlsoDownload = enabled;
            manager.SaveSeasonPreferences(item.Id, preference);
        });
        await manager.PrepareReady("Preferences changed");
    }

    public FrameworkElement AlsoDownloadToggle(VideoItem item)
    {
        var toggle = new CheckBox
        {
            Content = T("Also download automatically", "تنزيل تلقائي أيضًا"),
            IsChecked = downloads?.SeasonPreferences(item.Id).AlsoDownload == true,
            Margin = new(0, 10, 0, 10),
        };
        toggle.Click += async (_, _) => await SetAlsoDownload(item, toggle.IsChecked == true);
        return toggle;
    }

    public void ShowKeepReady(VideoItem item)
    {
        if (downloads == null)
            return;
        var manager = downloads;
        var preference = manager.SeasonPreferences(item.Id);
        var window = OpenSheet(
            T("Keep ready", "إبقاء الحلقات جاهزة"),
            item.Title + " · " + T("Your next watch is already here", "مشاهدتك القادمة جاهزة هنا"),
            SheetKind.Side,
            560
        );
        var panel = window.Body;
        panel.Children.Add(
            Body(
                T(
                    "Keep the next unwatched, aired episodes ready. Started shows prepare first.",
                    "أبقِ الحلقات القادمة المعروضة وغير المشاهدة جاهزة. الأولوية للمسلسلات التي بدأت مشاهدتها."
                ),
                13
            )
        );
        // A5 / A1 / A3 per show: "Same as default" first; the defaults live in the Keep ready settings.
        var defaults = manager.ReadyViewState;
        int? countChoice = preference.KeepReadyCount is 1 or 2 or 3 or 5 or -1 ? preference.KeepReadyCount : null;
        int? removeChoice = preference.RemoveAfterHours is -1 or 1 or 24 or 168 or 720
            ? preference.RemoveAfterHours
            : null;
        Action? rerunPreview = null;
        int? keepChoice = preference.KeepLastWatched is >= 0 and <= 3 ? preference.KeepLastWatched : null;
        panel.Children.Add(
            ReadyChoice(
                T("How far ahead", "إلى أي مدى مسبقًا"),
                ReadyWithDefault(ReadyCountOptions(), NoirReadyPolicy.Count(new(), defaults)),
                countChoice,
                v =>
                {
                    countChoice = v;
                    rerunPreview?.Invoke();
                }
            )
        );
        panel.Children.Add(new Border { Height = 10 });
        var rows = new StackPanel();
        var card = ReadyCard(rows);
        card.Padding = new(16);
        card.Margin = new(0, 0, 0, 10);
        panel.Children.Add(card);
        var impact = Body("", 12);
        panel.Children.Add(impact);
        int revision = 0;
        async Task Preview()
        {
            var rev = ++revision;
            rows.Children.Clear();
            rows.Children.Add(Text(T("UP NEXT", "الحلقات القادمة"), 11, "#E4C084"));
            rows.Children.Add(Body(T("Checking sources…", "جارٍ فحص المصادر…"), 13));
            var selected = countChoice ?? NoirReadyPolicy.Count(new(), defaults);
            var preview = await Task.Run(() =>
            {
                var inputs = store.ReadyPreview(item.GroupKey);
                return NoirReadyPolicy.Next(inputs, selected, DateTimeOffset.UtcNow);
            });
            if (rev != revision || !window.IsOpen)
                return;
            rows.Children.Clear();
            rows.Children.Add(Text(T("UP NEXT", "الحلقات القادمة"), 11, "#E4C084"));
            foreach (var episode in preview)
            {
                rows.Children.Add(
                    Body(
                        $"S{episode.Season:00} E{episode.Episode:00} · "
                            + (episode.Local ? ReadyText("Playable offline") : T("Will prepare", "سيتم التحضير")),
                        15
                    )
                );
            }
            if (preview.Count == 0)
                rows.Children.Add(Body(ReadyText("Waiting for an aired unwatched episode"), 13));
            impact.Text = T(
                "Quality: {0} · Budget: {1} · Keep free: {2}",
                "الجودة: {0} · الميزانية: {1} · المساحة الحرة: {2}",
                preference.Resolution,
                ReadySize(manager.ReadyViewState.BudgetBytes),
                ReadySize(manager.ReadyViewState.KeepFreeBytes)
            );
            LtrRuns(
                impact,
                preference.Resolution,
                ReadySize(manager.ReadyViewState.BudgetBytes),
                ReadySize(manager.ReadyViewState.KeepFreeBytes)
            );
            try
            {
                var missing = preview.Where(e => !e.Local).ToList();
                if (missing.Count == 0)
                    return;
                var plan = await Task.Run(() =>
                    manager.PlanSeason(
                        item.Id,
                        new()
                        {
                            Scope = "Specific episodes",
                            SpecificEpisodes = missing.Select(e => new SeasonEpisode(e.Season, e.Episode)).ToList(),
                        },
                        readyOnly: true
                    )
                );
                if (rev != revision || !window.IsOpen)
                    return;
                var choices = await Task.Run(() => manager.PreviewReadyChoices(plan));
                if (rev != revision || !window.IsOpen)
                    return;
                long bytes = 0;
                rows.Children.Clear();
                rows.Children.Add(Text(T("UP NEXT", "الحلقات القادمة"), 11, "#E4C084"));
                foreach (var (e, decision) in choices)
                {
                    if (decision.Source is { } chosen)
                    {
                        bytes += decision.Charge;
                        rows.Children.Add(
                            Body(
                                $"S{e.Season:00} E{e.Episode:00} · {chosen.Quality} · {ReadySize(chosen.FileBytes)}",
                                15
                            )
                        );
                    }
                    else
                        rows.Children.Add(Body($"S{e.Season:00} E{e.Episode:00} · " + ReadyText(decision.Reason), 12));
                }
                impact.Text += Environment.NewLine + T("Budget impact: {0}", "أثر الميزانية: {0}", ReadySize(bytes));
                LtrRuns(
                    impact,
                    preference.Resolution,
                    ReadySize(manager.ReadyViewState.BudgetBytes),
                    ReadySize(manager.ReadyViewState.KeepFreeBytes),
                    ReadySize(bytes)
                );
            }
            catch (Exception)
            {
                if (rev == revision && window.IsOpen)
                    rows.Children.Add(Body(ReadyText("Source check failed; recheck scheduled"), 12));
            }
        }
        panel.Children.Add(
            ReadyChoice(
                T("Remove watched files", "إزالة الملفات المشاهدة"),
                ReadyWithDefault(ReadyRemoveOptions(), NoirReadyPolicy.RemoveHours(defaults)),
                removeChoice,
                v => removeChoice = v
            )
        );
        panel.Children.Add(
            ReadyChoice(
                T("Keep the last watched", "الإبقاء على آخر ما شوهد"),
                ReadyWithDefault(ReadyKeepLastOptions(), NoirReadyPolicy.KeepLast(defaults)),
                keepChoice,
                v => keepChoice = v
            )
        );
        panel.Children.Add(
            Body(
                T(
                    "Pinned files and files you added yourself stay. Removal waits until every profile has finished; unknown progress keeps the file.",
                    "تبقى الملفات المثبتة والملفات التي أضفتها بنفسك. تنتظر الإزالة حتى تنتهي جميع الملفات الشخصية من المشاهدة؛ وعند غياب معلومات التقدم يبقى الملف."
                ),
                12
            )
        );
        panel.Children.Add(
            Body(
                T(
                    "Storage is shared across your prepared shows. If it fills up, Keep ready pauses and your unwatched episodes stay.",
                    "المساحة مشتركة بين المسلسلات المُحضّرة. عند امتلائها يتوقف إبقاء الحلقات جاهزة مؤقتًا وتبقى الحلقات غير المشاهدة."
                ),
                12
            )
        );
        var feedback = Body("", 12);
        panel.Children.Add(feedback);
        window.Action(T("Not now", "ليس الآن"), () => window.Close(false));
        window.Action(
            T("Keep ready on this PC", "إبقاء الحلقات جاهزة على هذا الكمبيوتر"),
            async () =>
            {
                try
                {
                    preference.KeepReadyEnabled = true;
                    preference.AutoDownload = true;
                    preference.KeepReadyCount = countChoice;
                    preference.RemoveAfterHours = removeChoice;
                    preference.KeepLastWatched = keepChoice;
                    await Task.Run(() => manager.SaveSeasonPreferences(item.Id, preference));
                    await manager.ChangeReady(s => s.Log("Preferences changed", item.Title));
                    window.Close(true);
                    _ = manager.PrepareReady("Preferences changed");
                }
                catch (Exception)
                {
                    feedback.Text = T("Couldn't save Keep ready settings.", "تعذر حفظ إعدادات إبقاء الحلقات جاهزة.");
                }
            },
            true
        );
        rerunPreview = () => keepReadyPreviewTask = Preview();
        window.Closed += _ => revision++;
        keepReadyPreviewTask = Preview();
    }

    public FrameworkElement ReadySettingsPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(
            Heading(
                T("Keep ready", "إبقاء الحلقات جاهزة"),
                T("A little room for your next watch", "مساحة لمشاهدتك القادمة")
            )
        );
        if (downloads == null)
            return panel;
        var manager = downloads;
        var state = manager.ReadyViewState;
        var fields = new WrapPanel();
        panel.Children.Add(fields);
        TextBox Number(string title, long bytes)
        {
            var column = new StackPanel { Margin = new(0, 0, 18, 8) };
            column.Children.Add(Body(title, 12));
            var box = new TextBox
            {
                Text = (bytes / (double)NoirReadyPolicy.GB).ToString(
                    "0.##",
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                Width = 130,
                Padding = new(10),
                Margin = new(0, 6, 0, 0),
            };
            column.Children.Add(box);
            fields.Children.Add(column);
            return box;
        }
        var budget = Number(T("Keep ready budget · GB", "ميزانية إبقاء الحلقات جاهزة · GB"), state.BudgetBytes);
        var free = Number(T("Keep free · at least 10 GB", "المساحة الحرة · 10 GB على الأقل"), state.KeepFreeBytes);
        // A1–A8 global defaults; each applies on click (P7). A show's own sheet can override A1, A3, A5 and the switch.
        async Task Rule(Action<NoirReadyState> change)
        {
            await manager.ChangeReady(s =>
            {
                change(s);
                s.Log("Preferences changed");
            });
            _ = manager.PrepareReady("Preferences changed");
        }
        panel.Children.Add(
            ReadyChoice(
                T("Which shows download automatically", "المسلسلات التي تُنزّل تلقائيًا"),
                [
                    (T("Only shows I switch on", "المسلسلات التي أفعّلها فقط"), "Switched"),
                    (T("Shows I am watching", "المسلسلات التي أتابعها"), "Watching"),
                    (T("Also my watchlist", "وقائمتي أيضًا"), "Watchlist"),
                ],
                state.AutoShows,
                v => _ = Rule(s => s.AutoShows = v),
                T(
                    "Shows I am watching are the started shows Noir alerts you about when a new episode airs: watched in the last six months or caught up. A show's own switch always wins.",
                    "المسلسلات التي أتابعها هي التي بدأتها ويُنبّهك Noir عند عرض حلقة جديدة منها: شاهدتها خلال الأشهر الستة الأخيرة أو أنت مواكب لها. مفتاح المسلسل نفسه له الأولوية دائمًا."
                )
            )
        );
        panel.Children.Add(
            ReadyChoice(
                T("How far ahead", "إلى أي مدى مسبقًا"),
                ReadyCountOptions(),
                NoirReadyPolicy.Count(new(), state),
                v => _ = Rule(s => s.ReadyCount = v)
            )
        );
        var hours = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToArray();
        var window = new WrapPanel
        {
            Margin = new(0, 4, 0, 0),
            Visibility = state.WindowStart != null ? Visibility.Visible : Visibility.Collapsed,
        };
        var from = new ComboBox
        {
            ItemsSource = hours,
            SelectedIndex = (state.WindowStart ?? 0) / 60,
            Width = 96,
            Padding = Density.InputPadding,
        };
        var to = new ComboBox
        {
            ItemsSource = hours,
            SelectedIndex = (state.WindowEnd ?? 360) / 60,
            Width = 96,
            Padding = Density.InputPadding,
        };
        window.Children.Add(from);
        window.Children.Add(Body(" " + T("to", "إلى") + " ", 13));
        window.Children.Add(to);
        System.Windows.Automation.AutomationProperties.SetName(from, T("From", "من"));
        System.Windows.Automation.AutomationProperties.SetName(to, T("To", "إلى"));
        void SaveWindow(bool on)
        {
            var (a, b) = (from.SelectedIndex * 60, to.SelectedIndex * 60);
            _ = Rule(s =>
            {
                s.WindowStart = on ? a : null;
                s.WindowEnd = on ? b : null;
            });
        }
        panel.Children.Add(
            ReadyChoice(
                T("When automatic downloads may start", "متى يمكن أن تبدأ التنزيلات التلقائية"),
                [(T("Any time", "في أي وقت"), false), (T("Only between", "فقط بين"), true)],
                state.WindowStart != null,
                on =>
                {
                    window.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                    SaveWindow(on);
                },
                T(
                    "Only new automatic downloads wait for these hours. Downloads already running and the ones you start yourself are never stopped.",
                    "تنتظر هذه الساعات التنزيلات التلقائية الجديدة فقط. لا تتوقف أبدًا التنزيلات الجارية ولا التي تبدؤها بنفسك."
                )
            )
        );
        panel.Children.Add(window);
        from.SelectionChanged += (_, _) => SaveWindow(true);
        to.SelectionChanged += (_, _) => SaveWindow(true);
        panel.Children.Add(
            ReadyChoice(
                T("Remove watched files", "إزالة الملفات المشاهدة"),
                ReadyRemoveOptions(),
                NoirReadyPolicy.RemoveHours(state),
                v => _ = Rule(s => s.RemoveAfterHours = v),
                T(
                    "Right after watching waits an hour so a mistaken mark can be undone. Changing this never removes a file sooner than a day from now. Removal waits until every profile has finished.",
                    "«فور المشاهدة» ينتظر ساعة لتتمكن من التراجع عن علامة خاطئة. تغيير هذا الخيار لا يزيل أي ملف قبل مرور يوم من الآن. تنتظر الإزالة حتى تنتهي جميع الملفات الشخصية من المشاهدة."
                )
            )
        );
        var movies = new CheckBox
        {
            Content = T("Movies too", "الأفلام أيضًا"),
            IsChecked = state.RemoveMovies,
            Margin = new(0, 2, 0, 4),
            IsEnabled = state.RemoveScope == "Noir",
        };
        panel.Children.Add(
            ReadyChoice(
                T("What removal covers", "ما تشمله الإزالة"),
                [
                    (T("Keep ready files only", "ملفات إبقاء الحلقات جاهزة فقط"), "Keep ready"),
                    (T("Everything Noir downloaded", "كل ما نزّله Noir"), "Noir"),
                ],
                state.RemoveScope,
                v =>
                {
                    movies.IsEnabled = v == "Noir";
                    _ = Rule(s => s.RemoveScope = v);
                },
                T(
                    "Files you added yourself are never removed, and neither are pinned files. Movies Noir downloaded stay unless you include them. Everything Noir downloaded includes downloads you started yourself in Noir, old ones too.",
                    "لا تُزال أبدًا الملفات التي أضفتها بنفسك ولا الملفات المثبتة. تبقى الأفلام التي نزّلها Noir ما لم تُضمّنها. تشمل «كل ما نزّله Noir» التنزيلات التي بدأتها بنفسك في Noir، والقديمة منها أيضًا."
                )
            )
        );
        panel.Children.Add(movies);
        movies.Click += (_, _) =>
        {
            var on = movies.IsChecked == true;
            _ = Rule(s => s.RemoveMovies = on);
        };
        panel.Children.Add(
            ReadyChoice(
                T("Keep the last watched", "الإبقاء على آخر ما شوهد"),
                ReadyKeepLastOptions(),
                NoirReadyPolicy.KeepLast(state),
                v => _ = Rule(s => s.KeepLastWatched = v),
                T(
                    "Keeps the most recently watched episodes of each show for a rewatch.",
                    "يُبقي أحدث الحلقات المشاهدة من كل مسلسل لإعادة المشاهدة."
                )
            )
        );
        panel.Children.Add(
            ReadyChoice(
                T("Removed files go", "وجهة الملفات المُزالة"),
                [(T("Delete permanently", "حذف نهائي"), false), (T("Recycle Bin", "سلة المحذوفات"), true)],
                state.RecycleRemoved,
                v => _ = Rule(s => s.RecycleRemoved = v),
                T(
                    "The Recycle Bin frees space only when the bin is emptied.",
                    "سلة المحذوفات لا تُفرغ المساحة إلا عند إفراغها."
                )
            )
        );
        var pause = new CheckBox
        {
            Content = ReadyText("Pause preparation"),
            IsChecked = state.Paused,
            Margin = new(0, 8, 0, 8),
        };
        panel.Children.Add(pause);
        pause.Click += async (_, _) =>
        {
            var paused = pause.IsChecked == true;
            await manager.ChangeReady(s =>
            {
                s.Paused = paused;
                s.Log(s.Paused ? "Pause preparation" : "Resume preparation");
            });
            if (!manager.ReadyViewState.Paused)
                _ = manager.PrepareReady("Preferences changed");
        };
        // Settings apply as you change them (P7): numbers on Enter or when leaving the field, the checkbox on click.
        var status = Body("", 12);
        panel.Children.Add(status);
        string last = budget.Text + "|" + free.Text;
        async Task Save()
        {
            if (budget.Text + "|" + free.Text == last)
                return;
            if (
                !double.TryParse(
                    budget.Text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var b
                )
                || !double.IsFinite(b)
                || b < 1
                || b > 10000
                || !double.TryParse(
                    free.Text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var f
                )
                || !double.IsFinite(f)
                || f < 10
                || f > 10000
            )
            {
                status.Text = T(
                    "Use a budget of 1–10000 GB and keep at least 10 GB free.",
                    "استخدم ميزانية من 1 إلى 10000 GB واترك 10 GB حرة على الأقل."
                );
                return;
            }
            await manager.ChangeReady(s =>
            {
                s.BudgetBytes = (long)(b * NoirReadyPolicy.GB);
                s.KeepFreeBytes = (long)(f * NoirReadyPolicy.GB);
                s.Log("Preferences changed");
            });
            status.Text = "✓ " + T("Saved", "تم الحفظ");
            _ = manager.PrepareReady("Preferences changed");
            last = budget.Text + "|" + free.Text;
        }
        foreach (var box in new[] { budget, free })
        {
            box.LostFocus += async (_, _) => await Save();
            box.KeyDown += async (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                    await Save();
            };
        }
        return ReadyCard(panel);
    }

    FrameworkElement PreparationCentre()
    {
        var panel = new StackPanel();
        if (downloads == null)
            return panel;
        var manager = downloads;
        var state = manager.ReadyViewState;
        var storage = manager.ReadyStorage;
        var tools = new WrapPanel();
        panel.Children.Add(tools);
        tools.Children.Add(
            ActionButton(ReadyText(state.Paused ? "Resume preparation" : "Pause preparation"), async () =>
            {
                await manager.ChangeReady(s =>
                {
                    s.Paused = !s.Paused;
                    s.Log(s.Paused ? "Pause preparation" : "Resume preparation");
                });
                if (!manager.ReadyViewState.Paused)
                    _ = manager.PrepareReady("Preferences changed");
                RefreshReadyCentre();
            })
        );
        tools.Children.Add(ActionButton(T("Check now", "تحقق الآن"), async () =>
            {
                await manager.PrepareReady("Preferences changed");
                RefreshReadyCentre();
            }));
        // The rules (which shows, how far ahead, when to remove) live in Settings; this is the way there.
        tools.Children.Add(
            ActionButton("[[sliders]]  " + T("Automatic rules", "القواعد التلقائية"), () =>
            {
                settingsSection = "Downloads";
                Navigate("Settings");
            })
        );
        foreach (var tool in tools.Children.OfType<Button>())
            tool.Margin = new(0, 0, 8, 8);
        var usage = Body("", 12);
        panel.Children.Add(usage);
        var storageBar = new ProgressBar
        {
            Height = 5,
            Foreground = Brush("#E4C084"),
            Background = Brush("#29282A"),
            Margin = new(0, 6, 0, 8),
        };
        panel.Children.Add(storageBar);
        var floor = Body("", 12);
        SetMarked(
            floor,
            T(
                "At least {0} stays free. Unwatched episodes stay on your PC.",
                "تبقى مساحة {0} حرة على الأقل. تبقى الحلقات غير المشاهدة على الكمبيوتر.",
                Ltr + ReadySize(state.KeepFreeBytes) + End
            )
        );
        panel.Children.Add(floor);
        var titleRows = new StackPanel();
        panel.Children.Add(titleRows);
        void DrawTitles()
        {
            titleRows.Children.Clear();
            foreach (var title in state.Titles.Values.Take(40))
            {
                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                words.Children.Add(Bidi(Text(title.Title, 14, "#F4F1EB", FontWeights.SemiBold)));
                var reason = Body(state.Paused ? ReadyText("Preparation paused") : ReadyText(title.Reason), 12);
                reason.Margin = new(0, 2, 0, 0);
                words.Children.Add(reason);
                if (title.Bytes > 0)
                    words.Children.Add(
                        Bidi(
                            Text(
                                (title.Quality.Length > 0 ? title.Quality + " · " : "") + ReadySize(title.Bytes),
                                12,
                                "#E4C084"
                            )
                        )
                    );
                // The same compact row as "On this PC": poster thumbnail, name, state.
                var poster = store.EpisodesFor(title.Group).Select(i => i.Poster).FirstOrDefault(File.Exists);
                var thumb = new Border
                {
                    Width = 40,
                    Height = 60,
                    CornerRadius = new(6),
                    Background = Brush("#1C1C1F"),
                    Margin = new(0, 0, 14, 0),
                    Clip = new RectangleGeometry(new Rect(0, 0, 40, 60), 6, 6),
                };
                if (poster != null)
                    thumb.Child = Art(poster, title.Title, 60, true, false);
                var row = new DockPanel();
                DockPanel.SetDock(thumb, Dock.Left);
                row.Children.Add(thumb);
                row.Children.Add(words);
                var card = ReadyCard(row);
                card.Padding = new(12, 10, 16, 10);
                card.Margin = new(0, 4, 0, 4);
                card.CornerRadius = new(10);
                titleRows.Children.Add(card);
            }
            if (state.Titles.Count == 0)
                titleRows.Children.Add(
                    Body(
                        T(
                            "Choose Keep ready in the download sheet. My List titles prepare automatically; downloading is your choice.",
                            "اختر إبقاء الحلقات جاهزة في نافذة التنزيل. تُحضّر عناوين قائمتي تلقائيًا؛ والتنزيل باختيارك."
                        ),
                        13
                    )
                );
        }
        var files = new StackPanel();
        void DrawFiles()
        {
            files.Children.Clear();
            foreach (var file in state.ManagedFiles.TakeLast(60).Concat(state.NoirFiles))
            {
                var row = new StackPanel();
                row.Children.Add(Body(file.Title, 13));
                row.Children.Add(
                    Body(
                        file.RemoveAt is { } at
                                ? T(
                                    "Scheduled removal · {0}",
                                    "الإزالة المجدولة · {0}",
                                    at.ToLocalTime().ToString("g", Loc.Culture)
                                )
                                    + (
                                        file.RuleHours > 0
                                            ? " · " + ReadyText(NoirReadyPolicy.RuleLabel(file.RuleHours))
                                            : ""
                                    )
                            : file.Pinned ? T("Pinned · stays on this PC", "مثبّت · يبقى على هذا الكمبيوتر")
                            : T("Kept ready", "محفوظ جاهزًا"),
                        12
                    )
                );
                var actions = new WrapPanel();
                row.Children.Add(actions);
                void Change(string code, string label, Action<ReadyManagedFile> edit)
                {
                    var button = ActionButton(label, async () =>
                    {
                        await manager.ChangeReady(s =>
                        {
                            var current = s.ManagedFiles.Concat(s.NoirFiles).FirstOrDefault(f => f.JobId == file.JobId);
                            if (current != null)
                            {
                                edit(current);
                                s.Log(code, file.Title);
                            }
                        });
                        RefreshReadyCentre();
                    });
                    button.Margin = new(0, 0, 8, 0);
                    actions.Children.Add(button);
                }
                // U51: one button per state. A file scheduled for removal offers "Keep this file" (pins it and cancels the removal); otherwise Pin or Unpin.
                if (file.RemoveAt != null)
                    Change("Keep file", ReadyText("Keep file"), f =>
                    {
                        f.Pinned = true;
                        f.RemoveAt = null;
                    });
                else if (file.Pinned)
                    Change("Unpin file", T("Unpin", "إلغاء التثبيت"), f =>
                    {
                        f.Pinned = false;
                    });
                else
                    Change("Pin file", T("Pin", "تثبيت"), f =>
                    {
                        f.Pinned = true;
                        f.RemoveAt = null;
                    });
                files.Children.Add(ReadyCard(row));
            }
        }
        panel.Children.Add(
            new Expander
            {
                Header = T("Kept files · pins & scheduled removals", "الملفات المحفوظة · التثبيت والإزالة المجدولة"),
                Content = files,
                Margin = new(0, 10, 0, 8),
            }
        );
        var log = new StackPanel();
        void DrawLog()
        {
            log.Children.Clear();
            foreach (var entry in state.Why.TakeLast(200).Reverse())
                log.Children.Add(
                    Body(
                        entry.At.ToLocalTime().ToString("g", Loc.Culture)
                            + " · "
                            + ReadyText(entry.Code)
                            + (entry.Title.Length > 0 ? " · " + entry.Title : "")
                            + (
                                entry.Detail.Length > 0 && ReadyRuleCodes.Contains(entry.Code)
                                    ? " · " + ReadyDetail(entry.Detail)
                                    : ""
                            ),
                        12
                    )
                );
        }
        panel.Children.Add(
            new Expander
            {
                Header = T("Activity", "النشاط"),
                Content = new ScrollViewer
                {
                    Content = log,
                    MaxHeight = 320,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
                Margin = new(0, 10, 0, 10),
            }
        );
        string drawn = "";
        readyCentreUpdate = () =>
        {
            state = manager.ReadyViewState;
            storage = manager.ReadyStorage;
            SetMarked(
                usage,
                T(
                    "{0} used + {1} reserved · {2} budget",
                    "{0} مستخدمة + {1} محجوزة · الميزانية {2}",
                    Ltr + ReadySize(storage.UsedBytes) + End,
                    Ltr + ReadySize(storage.ReservedBytes) + End,
                    Ltr + ReadySize(state.BudgetBytes) + End
                )
            );
            storageBar.Maximum = Math.Max(1, state.BudgetBytes);
            storageBar.Value = Math.Min(state.BudgetBytes, storage.UsedBytes + storage.ReservedBytes);
            var stamp =
                state.Paused
                + string.Join("|", state.Titles.Values.Select(t => t.Group + t.Reason + t.Bytes))
                + string.Join(
                    "|",
                    state
                        .ManagedFiles.Concat(state.NoirFiles)
                        .Select(f => f.JobId + f.Pinned + f.RemoveAt + f.CleanupCancelled)
                )
                + state.Why.LastOrDefault()?.At.ToString("O");
            if (stamp != drawn)
            {
                drawn = stamp;
                DrawTitles();
                DrawFiles();
                DrawLog();
            }
        };
        readyCentreUpdate();
        Motion.EnterOnce(panel);
        return ReadyCard(panel);
    }

    Border? readyCentreHost;
    Task? keepReadyPreviewTask;
    Action? readyCentreUpdate;

    void RefreshReadyCentre()
    {
        if (readyCentreHost != null)
            readyCentreHost.Child = PreparationCentre();
    }
}
