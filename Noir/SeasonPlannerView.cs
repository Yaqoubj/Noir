using System.Windows;
using System.Windows.Controls;

namespace Noir;

public partial class MainWindow
{
    // U49: a scope card keeps its gold outline when chosen; keyboard focus is a white outline, so focus never reads as "selected".
    static readonly ControlTemplate ScopeCardTemplate = (ControlTemplate)
        System.Windows.Markup.XamlReader.Parse(
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"Button\"><Grid><Border x:Name=\"Chrome\" Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"1\" CornerRadius=\"10\" Padding=\"{TemplateBinding Padding}\"><ContentPresenter HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" VerticalAlignment=\"Center\"/></Border><Border x:Name=\"Sheen\" Background=\"White\" CornerRadius=\"10\" Opacity=\"0\" IsHitTestVisible=\"False\"/></Grid><ControlTemplate.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"Sheen\" Property=\"Opacity\" Value=\"0.07\"/></Trigger><Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter TargetName=\"Chrome\" Property=\"BorderBrush\" Value=\"#F4F1EB\"/></Trigger></ControlTemplate.Triggers></ControlTemplate>"
        );

    void ShowSeasonPlanner(VideoItem item, bool seasonContext = false)
    {
        if (downloads == null)
            return;
        var manager = downloads;
        var window = OpenSheet(T("Download", "تنزيل"), item.Title, SheetKind.Side, 900);
        // The footer carries the running summary on the left and the actions at the end.
        var summary = Text(
            T("Choose what to download, then find releases.", "اختر ما تريد تنزيله ثم ابحث عن الإصدارات."),
            13,
            "#C7C2BC"
        );
        var feedback = Body("", 12);
        feedback.Margin = new(0, 2, 0, 0);
        var footerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 16, 0) };
        footerText.Children.Add(summary);
        footerText.Children.Add(feedback);
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        window.Footer.Child = null;
        footerGrid.Children.Add(footerText);
        Grid.SetColumn(window.FooterButtons, 1);
        footerGrid.Children.Add(window.FooterButtons);
        window.Footer.Child = footerGrid;
        DownloadPlan? plan = null;
        var picks = new Dictionary<PlannedEpisode, EpisodeDownloadOption?>();
        var excluded = new HashSet<PlannedEpisode>();
        var confirmId = Guid.NewGuid().ToString();
        bool reviewing = false;
        var refresh = window.Action(T("Find releases", "البحث عن الإصدارات"), () => { }, true);
        var download = window.Action(
            T("Download episodes"),
            () =>
            {
                if (plan == null)
                    return;
                try
                {
                    var ids = manager.EnqueuePlan(
                        new()
                        {
                            PlanId = plan.Id,
                            RequestId = confirmId,
                            FileIds = picks
                                .Where(p => p.Value != null && !excluded.Contains(p.Key))
                                .Select(p => p.Value!.File.Id)
                                .ToList(),
                        }
                    );
                    Status.Text = T(
                        "Queued {0} episodes for this PC.",
                        "أُضيفت {0} إلى قائمة التنزيل على هذا الكمبيوتر.",
                        ids.Count
                    );
                    window.Close(true);
                    ShowDownloads();
                }
                catch (Exception e)
                {
                    feedback.Text =
                        e is System.IO.IOException
                            ? e.Message
                            : T(
                                "Couldn't queue this plan. Refresh sources and try again.",
                                "تعذّرت إضافة هذه الخطة. حدّث المصادر وحاول مجددًا."
                            );
                }
            },
            true
        );
        download.IsEnabled = false;
        var content = window.Body;
        var controls = new StackPanel();
        content.Children.Add(controls);
        var progressText = Body("", 12);
        content.Children.Add(progressText);
        var rows = new StackPanel();
        content.Children.Add(rows);
        var sources = new StackPanel();
        content.Children.Add(
            new Expander
            {
                Header = T("Where the files come from", "مصادر الملفات"),
                Content = sources,
                Margin = new(0, 12, 0, 4),
            }
        );
        var remembered = manager.ScopePreferences(item.GroupKey);
        var request = new DownloadPlanRequest
        {
            Scope = seasonContext ? "This season" : "This episode",
            OnlyMissing = remembered.OnlyMissing,
            SkipWatched = remembered.SkipWatched,
            IncludeSpecials = remembered.IncludeSpecials,
            MaxTotalBytes = remembered.MaxTotalBytes,
        };
        var preference = manager.SeasonPreferences(item.Id);
        var episodes = store.DownloadEpisodesFor(item.GroupKey, true);
        var seasons = episodes.Where(e => e.Season > 0).Select(e => e.Season).Distinct().Order().ToList();
        var chosenSeasons = seasons.ToHashSet();
        var specific = new HashSet<SeasonEpisode>();
        string from = "1",
            next = "3";
        bool optionsOpen = false;
        var lifetime = new CancellationTokenSource();
        CancellationTokenSource? search = null;
        int revision = 0;
        refresh.Click += (_, _) =>
        {
            reviewing = true;
            refresh.Content = T("Refresh plan", "تحديث الخطة");
            refresh.ClearValue(FrameworkElement.StyleProperty);
            window.SetTitle(
                request.Scope == "This episode" ? T("Download", "تنزيل") : T("Download season", "تنزيل الموسم")
            );
            download.Visibility = Visibility.Visible;
            _ = Build();
        };
        window.Closed += _ =>
        {
            lifetime.Cancel();
            search?.Cancel();
            lifetime.Dispose();
        };
        void SavePreference()
        {
            try
            {
                manager.SaveSeasonPreferences(item.Id, preference);
            }
            catch (System.IO.IOException e)
            {
                feedback.Text = e.Message;
            }
        }
        void Invalidate(bool cancelSearch = true)
        {
            if (cancelSearch)
            {
                revision++;
                search?.Cancel();
                refresh.IsEnabled = true;
            }
            plan = null;
            download.IsEnabled = false;
            summary.Text = T(
                "Your choices changed. Find releases again to see them.",
                "تغيّرت اختياراتك. ابحث عن الإصدارات مجددًا لرؤيتها."
            );
            rows.Children.Clear();
        }
        void OptionsChanged()
        {
            manager.SaveScopePreferences(
                item.GroupKey,
                new()
                {
                    OnlyMissing = request.OnlyMissing,
                    SkipWatched = request.SkipWatched,
                    IncludeSpecials = request.IncludeSpecials,
                    MaxTotalBytes = request.MaxTotalBytes,
                }
            );
            Invalidate();
            RenderControls();
            if (reviewing)
                _ = Build();
        }
        FrameworkElement CompactChips(string[] options, string selected, Action<string> choose)
        {
            var chips = (WrapPanel)Chips(options, selected, choose);
            chips.Margin = new(0, 5, 0, 6);
            foreach (var button in chips.Children.OfType<Button>())
            {
                button.FontSize = 12;
                button.Padding = new(11, 6, 11, 6);
                button.Margin = new(0, 0, 5, 6);
                button.Background = Brush((string)button.Content == selected ? "#E4C084" : "#242427");
            }
            return chips;
        }
        void RenderControls()
        {
            controls.Children.Clear();
            var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new(0, 2, 0, 12) };
            void Choice(string key, string title, string hint)
            {
                var labels = new StackPanel();
                labels.Children.Add(
                    Text(title, 16, request.Scope == key ? "#E4C084" : "#F4F1EB", FontWeights.SemiBold)
                );
                labels.Children.Add(Body(hint, 12));
                var button = ActionButton(title, () =>
                {
                    request.Scope = key;
                    OptionsChanged();
                });
                button.Content = labels;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.MinHeight = 84;
                button.Margin = new(0, 0, 8, 8);
                button.Padding = new(16, 12, 16, 12);
                button.Background = Brush(request.Scope == key ? "#30291E" : "#1A1A1D");
                button.Template = ScopeCardTemplate;
                button.BorderThickness = new(1);
                button.BorderBrush = Brush(request.Scope == key ? "#E4C084" : "#26FFFFFF");
                System.Windows.Automation.AutomationProperties.SetName(button, title + " · " + hint);
                button.Tag = "DownloadScope:" + key;
                cards.Children.Add(button);
            }
            if (item.Episode > 0)
                Choice("This episode", T("This episode", "هذه الحلقة"), $"S{item.Season:00} · E{item.Episode:00}");
            Choice("This season", T("This season", "هذا الموسم"), T("Season {0}", "الموسم {0}", item.Season));
            var aired = episodes
                .Where(e =>
                    e.Season > 0
                    && !(
                        store
                            .State.EpisodeSchedules.GetValueOrDefault(item.GroupKey)
                            ?.Any(x =>
                                x.Season == e.Season && x.Episode == e.Episode && x.AirsAt > DateTimeOffset.UtcNow
                            )
                        ?? false
                    )
                )
                .ToList();
            Choice(
                "Whole show",
                T("Whole show", "المسلسل كاملًا"),
                T("{0} episodes", "{0} حلقات", aired.Count)
                    + (seasons.Count > 0 ? $" · S{seasons.Min()}–S{seasons.Max()}" : "")
            );
            Choice(
                "Next N episodes",
                T("Next {0} episodes", "الحلقات {0} القادمة", next),
                T("After your furthest watched episode", "بعد آخر حلقة شاهدتها")
            );
            controls.Children.Add(cards);
            // Keyboard focus sits on the chosen card, so the white focus outline is never on a different card than the gold one.
            if (cards.Children.OfType<Button>().FirstOrDefault(b => Equals(b.Tag, "DownloadScope:" + request.Scope)) is { } chosenCard)
                Dispatcher.BeginInvoke(
                    () =>
                    {
                        if (chosenCard.IsVisible && System.Windows.Input.Keyboard.FocusedElement is not TextBox)
                            chosenCard.Focus();
                    },
                    System.Windows.Threading.DispatcherPriority.Input
                );
            if (request.Scope == "Next N episodes")
            {
                var stepper = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 8) };
                var stepLabel = Body(T("Next episodes", "الحلقات القادمة"), 13);
                stepLabel.VerticalAlignment = VerticalAlignment.Center;
                stepLabel.Margin = new(0, 0, 12, 0);
                stepper.Children.Add(stepLabel);
                var minus = ActionButton("[[minus]]", () =>
                {
                    next = Math.Max(1, int.Parse(next) - 1).ToString();
                    request.Scope = "Next N episodes";
                    OptionsChanged();
                });
                var plus = ActionButton("[[plus]]", () =>
                {
                    next = Math.Min(25, int.Parse(next) + 1).ToString();
                    request.Scope = "Next N episodes";
                    OptionsChanged();
                });
                minus.IsEnabled = int.Parse(next) > 1;
                plus.IsEnabled = int.Parse(next) < 25;
                System.Windows.Automation.AutomationProperties.SetName(
                    minus,
                    T("Fewer next episodes", "تقليل الحلقات القادمة")
                );
                System.Windows.Automation.AutomationProperties.SetName(
                    plus,
                    T("More next episodes", "زيادة الحلقات القادمة")
                );
                stepper.Children.Add(minus);
                stepper.Children.Add(
                    new TextBlock
                    {
                        Text = next,
                        FlowDirection = FlowDirection.LeftToRight,
                        Margin = new(12, 8, 12, 0),
                        Foreground = Brush("#F4F1EB"),
                    }
                );
                stepper.Children.Add(plus);
                controls.Children.Add(stepper);
            }
            var options = new StackPanel();
            void Toggle(string label, bool value, Action<bool> apply)
            {
                var check = new CheckBox
                {
                    Content = label,
                    IsChecked = value,
                    Margin = new(0, 8, 0, 5),
                };
                System.Windows.Automation.AutomationProperties.SetName(check, label);
                check.Checked += (_, _) =>
                {
                    apply(true);
                    OptionsChanged();
                };
                check.Unchecked += (_, _) =>
                {
                    apply(false);
                    OptionsChanged();
                };
                options.Children.Add(check);
            }
            Toggle(T("Skip watched", "تخطي ما شاهدته"), request.SkipWatched, v => request.SkipWatched = v);
            options.Children.Add(Body(T("Quality", "الجودة"), 12));
            options.Children.Add(CompactChips(["Any", "720p", "1080p", "2160p"], preference.Resolution, v =>
                {
                    preference.Resolution = v;
                    SavePreference();
                    OptionsChanged();
                }));
            var languages = new Dictionary<string, string>
            {
                { T("All", "الكل"), "All" },
                { T("English", "الإنجليزية"), "English" },
                { T("Arabic", "العربية"), "Arabic" },
                { T("Japanese", "اليابانية"), "Japanese" },
                { T("Korean", "الكورية"), "Korean" },
                { T("Hindi", "الهندية"), "Hindi" },
                { T("Spanish", "الإسبانية"), "Spanish" },
                { T("French", "الفرنسية"), "French" },
            };
            options.Children.Add(Body(T("Audio", "الصوت"), 12));
            options.Children.Add(
                CompactChips(
                    languages.Keys.ToArray(),
                    languages.FirstOrDefault(x => x.Value == preference.Language).Key ?? T("All", "الكل"),
                    v =>
                    {
                        preference.Language = languages[v];
                        SavePreference();
                        OptionsChanged();
                    }
                )
            );
            var sizes = new Dictionary<string, long>
            {
                { T("Off", "إيقاف"), 0 },
                { "500 MB", 500L * 1024 * 1024 },
                { "1 GB", 1L << 30 },
                { "2 GB", 2L << 30 },
                { "4 GB", 4L << 30 },
            };
            var sizeLabel = sizes.FirstOrDefault(p => p.Value == preference.MaxEpisodeBytes).Key
                ?? DownloadNaming.Size(preference.MaxEpisodeBytes);
            sizes[sizeLabel] = preference.MaxEpisodeBytes;
            options.Children.Add(Body(T("Max per episode", "الحد لكل حلقة"), 12));
            options.Children.Add(CompactChips(sizes.Keys.ToArray(), sizeLabel, v =>
                {
                    preference.MaxEpisodeBytes = sizes[v];
                    SavePreference();
                    OptionsChanged();
                }));
            var caps = new Dictionary<string, long>
            {
                { T("Off", "إيقاف"), 0 },
                { "5 GB", 5L << 30 },
                { "10 GB", 10L << 30 },
                { "20 GB", 20L << 30 },
                { "50 GB", 50L << 30 },
                { "100 GB", 100L << 30 },
            };
            var capLabel = caps.FirstOrDefault(x => x.Value == request.MaxTotalBytes).Key
                ?? DownloadNaming.Size(request.MaxTotalBytes);
            caps[capLabel] = request.MaxTotalBytes;
            options.Children.Add(Body(T("Max total for this download", "الحد الإجمالي لهذا التنزيل"), 12));
            options.Children.Add(CompactChips(caps.Keys.ToArray(), capLabel, v =>
                {
                    request.MaxTotalBytes = caps[v];
                    OptionsChanged();
                }));
            var custom = new TextBox
            {
                Text =
                    request.MaxTotalBytes > 0
                        ? (request.MaxTotalBytes / 1073741824d).ToString(
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                        : "",
                Width = 110,
                HorizontalAlignment = HorizontalAlignment.Left,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new(0, 4, 0, 8),
            };
            System.Windows.Automation.AutomationProperties.SetName(
                custom,
                T("Custom total limit in GB", "حد إجمالي مخصص بالغيغابايت")
            );
            options.Children.Add(custom);
            options.Children.Add(ActionButton(T("Apply custom GB", "تطبيق الحد المخصص GB"), () =>
                {
                    if (
                        double.TryParse(
                            custom.Text,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var gb
                        )
                        && double.IsFinite(gb)
                        && gb >= 0
                        && gb <= 1000000
                    )
                    {
                        request.MaxTotalBytes = (long)(gb * 1073741824);
                        OptionsChanged();
                    }
                    else
                        feedback.Text = T("Enter a valid size in GB.", "أدخل حجمًا صحيحًا بالغيغابايت.");
                }));
            Toggle(T("Include specials", "تضمين الحلقات الخاصة"), request.IncludeSpecials, v =>
                request.IncludeSpecials = v
            );
            options.Children.Add(Body(T("More ways to choose", "طرق اختيار أخرى"), 12));
            foreach (
                var pair in new[]
                {
                    ("Missing episodes", T("Missing episodes", "الحلقات الناقصة")),
                    ("Unwatched only", T("Unwatched only", "غير المشاهد فقط")),
                    ("From episode N", T("From episode N", "من الحلقة N")),
                    ("Specific episodes", T("Specific episodes", "حلقات محددة")),
                    ("Several seasons / whole show", T("Several seasons / whole show", "عدة مواسم أو المسلسل كاملًا")),
                }
            )
            {
                var key = pair.Item1;
                options.Children.Add(ActionButton(pair.Item2, () =>
                    {
                        request.Scope = key;
                        OptionsChanged();
                    }));
            }
            if (request.Scope == "From episode N")
            {
                var number = new TextBox
                {
                    Text = from,
                    Width = 90,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                System.Windows.Automation.AutomationProperties.SetName(number, T("Starting episode", "حلقة البداية"));
                number.TextChanged += (_, _) =>
                {
                    from = number.Text;
                    Invalidate();
                };
                options.Children.Add(number);
            }
            if (request.Scope == "Several seasons / whole show")
                foreach (var season in seasons)
                {
                    var check = new CheckBox
                    {
                        Content = T("Season {0}", "الموسم {0}", season),
                        IsChecked = chosenSeasons.Contains(season),
                        Margin = new(0, 6, 0, 6),
                    };
                    check.Checked += (_, _) =>
                    {
                        chosenSeasons.Add(season);
                        Invalidate();
                    };
                    check.Unchecked += (_, _) =>
                    {
                        chosenSeasons.Remove(season);
                        Invalidate();
                    };
                    options.Children.Add(check);
                }
            if (request.Scope == "Specific episodes")
            {
                var list = new WrapPanel();
                foreach (var episode in episodes.Where(e => e.Episode > 0 && (e.Season > 0 || request.IncludeSpecials)))
                {
                    var key = new SeasonEpisode(episode.Season, episode.Episode);
                    var check = new CheckBox
                    {
                        Content = $"S{episode.Season:00} E{episode.Episode:00}",
                        IsChecked = specific.Contains(key),
                        Margin = new(0, 6, 16, 6),
                    };
                    check.Checked += (_, _) =>
                    {
                        specific.Add(key);
                        Invalidate();
                    };
                    check.Unchecked += (_, _) =>
                    {
                        specific.Remove(key);
                        Invalidate();
                    };
                    list.Children.Add(check);
                }
                options.Children.Add(list);
            }
            var line =
                T("Quality and limits", "الجودة والحدود")
                + ": "
                + preference.Resolution
                + " · "
                + (
                    preference.MaxEpisodeBytes > 0
                        ? T("up to {0} each", "حتى {0} لكل حلقة", DownloadNaming.Size(preference.MaxEpisodeBytes))
                        : T("any size", "أي حجم")
                )
                + " · "
                + (
                    request.MaxTotalBytes > 0
                        ? T("{0} total", "إجمالي {0}", DownloadNaming.Size(request.MaxTotalBytes))
                        : T("no total limit", "بلا حد إجمالي")
                );
            optionsOpen |= request.Scope is "Specific episodes" or "From episode N" or "Several seasons / whole show";
            var more = new Expander
            {
                Header = line,
                Content = options,
                IsExpanded = optionsOpen,
                Margin = new(0, 4, 0, 12),
                Tag = "DownloadMoreOptions",
            };
            System.Windows.Automation.AutomationProperties.SetName(more, T("More options", "المزيد من الخيارات"));
            more.Expanded += (_, _) => optionsOpen = true;
            more.Collapsed += (_, _) => optionsOpen = false;
            controls.Children.Add(more);
            // A6+A7: the per-show switch shows what happens now (own switch or the global "Which shows" choice);
            // ticking or unticking makes it this show's own choice, "Same as default" gives it back.
            var quietAuto = false;
            var rule = manager.AutomaticRule(item.Id);
            var auto = new CheckBox
            {
                Content = T("Keep this show ready", "إبقاء هذا المسلسل جاهزًا"),
                IsChecked = preference.AutoDownload || rule != null,
                Margin = new(0, 12, 0, 4),
            };
            var sameAsDefault = ActionButton(T("Same as default", "مثل الافتراضي"), () =>
            {
                preference.AutoDownload = false;
                preference.KeepReadyEnabled = null;
                SavePreference();
                var effective = manager.AutomaticRule(item.Id) != null;
                if (auto.IsChecked != effective)
                {
                    quietAuto = true;
                    auto.IsChecked = effective;
                    quietAuto = false;
                }
            });
            sameAsDefault.Visibility = preference.KeepReadyEnabled == null ? Visibility.Collapsed : Visibility.Visible;
            var readyNote = T(
                "Noir downloads the next episodes ahead, so they are ready when you are.",
                "ينزّل Noir الحلقات القادمة مسبقًا لتكون جاهزة عند حاجتك."
            );
            var autoHint = Body(
                preference.KeepReadyEnabled == null && rule != null
                    ? T("On by default: {0}", "مفعّل افتراضيًا: {0}", ReadyText(rule))
                    : readyNote,
                12
            );
            auto.Checked += (_, _) =>
            {
                if (quietAuto)
                    return;
                preference.AutoDownload = true;
                preference.KeepReadyEnabled = true;
                SavePreference();
                sameAsDefault.Visibility = Visibility.Visible;
                autoHint.Text = readyNote;
                ShowKeepReady(item);
            };
            auto.Unchecked += (_, _) =>
            {
                if (quietAuto)
                    return;
                preference.AutoDownload = false;
                preference.KeepReadyEnabled = false;
                SavePreference();
                sameAsDefault.Visibility = Visibility.Visible;
                autoHint.Text = readyNote;
            };
            controls.Children.Add(auto);
            controls.Children.Add(autoHint);
            controls.Children.Add(sameAsDefault);
            var readyOptions = ActionButton(
                "[[sliders]]  " + T("Keep ready options", "خيارات إبقاء الحلقات جاهزة"),
                () => ShowKeepReady(item)
            );
            readyOptions.ToolTip = T(
                "How far ahead to download and when watched files are removed, for this show.",
                "كم حلقة تُنزَّل مسبقًا ومتى تُزال الملفات المشاهدة، لهذا المسلسل."
            );
            controls.Children.Add(readyOptions);
        }
        void UpdateSummary()
        {
            if (plan == null)
                return;
            var selected = picks
                .Where(p => p.Value != null && !excluded.Contains(p.Key))
                .Select(p => p.Value!)
                .ToList();
            var bytes = selected.Sum(o => o.File.Size);
            var missing = plan.Episodes.Count(e => e.State == "Not found");
            var unknown = selected.Count(o => o.File.Size <= 0);
            var fits =
                bytes + 64L * 1024 * 1024 <= plan.FreeBytes
                && (
                    request.MaxTotalBytes == 0
                    || selected.Sum(o => o.File.Size > 0 ? o.File.Size : preference.MaxEpisodeBytes)
                        <= request.MaxTotalBytes
                );
            string Episodes(int n) => Loc.Count(n, "episode", "episodes", "حلقة واحدة", "حلقتان", "حلقات", "حلقة");
            SetMarked(
                summary,
                Episodes(selected.Count)
                    + " · "
                    + Ltr
                    + DownloadNaming.Size(bytes)
                    + End
                    + (
                        unknown > 0
                            ? " + " + T("{0} unknown sizes", "{0} بحجم غير معروف", unknown)
                            : ""
                    )
                    + " · "
                    + T("{0} free", "{0} متاحة", Ltr + DownloadNaming.Size(plan.FreeBytes) + End)
                    + " · "
                    + T("{0} not found", "{0} غير موجودة", missing)
            );
            progressText.Text = T(
                "{0} on PC · {1} queued · {2} not aired",
                "{0} على الكمبيوتر · {1} في القائمة · {2} لم تُعرض بعد",
                plan.ExistingEpisodes,
                plan.QueuedEpisodes,
                plan.Episodes.Count(e => e.State == "Unaired")
            );
            SetMarked(
                feedback,
                (
                    !fits
                        ? T(
                            "Not enough space. Exclude episodes or choose smaller files. ",
                            "المساحة لا تكفي. استبعد حلقات أو اختر ملفات أصغر. "
                        )
                        : ""
                )
                    + T("Save to: ", "الحفظ في: ")
                    + Ltr
                    + plan.Folder
                    + End
                    + (
                        plan.ScheduleKnown
                            ? ""
                            : " · "
                                + T(
                                    "Schedule unavailable; known episodes only",
                                    "الجدول غير متاح؛ الحلقات المعروفة فقط"
                                )
                    )
            );
            download.Content = T("Download", "تنزيل") + " " + Episodes(selected.Count);
            download.IsEnabled = selected.Count > 0 && fits;
        }
        void RenderRows()
        {
            rows.Children.Clear();
            if (plan == null)
                return;
            foreach (var season in plan.Episodes.GroupBy(e => e.Season))
            {
                rows.Children.Add(Heading(T("Season {0}", "الموسم {0}", season.Key)));
                foreach (var episode in season)
                {
                    var chosen = picks.GetValueOrDefault(episode);
                    var card = new StackPanel { Margin = new(14, 10, 14, 10) };
                    var header = new DockPanel();
                    var change = ActionButton("Change", () => { });
                    DockPanel.SetDock(change, Dock.Right);
                    header.Children.Add(change);
                    var check = new CheckBox
                    {
                        Content = Text(
                            $"E{episode.Episode:00}  {(episode.Title.Length > 0 ? episode.Title : T("Episode {0}", "الحلقة {0}", episode.Episode))}",
                            15,
                            weight: FontWeights.SemiBold
                        ),
                        IsChecked = chosen != null && !excluded.Contains(episode),
                        IsEnabled = chosen != null,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Margin = new(0),
                    };
                    check.Checked += (_, _) =>
                    {
                        excluded.Remove(episode);
                        UpdateSummary();
                    };
                    check.Unchecked += (_, _) =>
                    {
                        excluded.Add(episode);
                        UpdateSummary();
                    };
                    header.Children.Add(check);
                    card.Children.Add(header);
                    var state =
                        episode.State == "Over your total limit"
                            ? T("Over your total limit", "يتجاوز الحد الإجمالي")
                            : chosen?.CacheState ?? episode.State;
                    card.Children.Add(
                        new Border
                        {
                            Background = Brush(state is "Ready" or "On your server" ? "#30291E" : "#252529"),
                            CornerRadius = new(5),
                            Padding = new(8, 3, 8, 3),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Margin = new(24, 8, 0, 6),
                            Child = Text(state, 11, state is "Ready" or "On your server" ? "#E4C084" : "#AAA6A0"),
                        }
                    );
                    if (chosen != null)
                    {
                        card.Children.Add(
                            Body(
                                $"{chosen.Source} · {chosen.Resolution} · {chosen.Language} · {DownloadNaming.Size(chosen.File.Size)}",
                                12
                            )
                        );
                        var release = Body(chosen.Release, 12);
                        release.TextTrimming = TextTrimming.CharacterEllipsis;
                        release.MaxHeight = 38;
                        release.ToolTip = chosen.Release;
                        card.Children.Add(release);
                    }
                    card.Children.Add(
                        Body(
                            episode.Chosen?.File.Id == chosen?.File.Id
                                ? episode.Reason
                                : T("Your choice", "اختيارك") + " · " + (chosen?.CacheState ?? episode.State),
                            12
                        )
                    );
                    change.IsEnabled = episode.Alternatives.Count > 0;
                    change.ToolTip = Loc.Count(
                        episode.Alternatives.Count,
                        "available option",
                        "available options",
                        "خيار واحد متاح",
                        "خياران متاحان",
                        "خيارات متاحة",
                        "خيارًا متاحًا"
                    );
                    change.Click += (_, _) =>
                    {
                        var menu = new ContextMenu();
                        foreach (var option in episode.Alternatives)
                        {
                            var label = new StackPanel { MaxWidth = 620 };
                            label.Children.Add(
                                Text(
                                    $"{option.Source} · {option.Resolution} · {option.Language} · {DownloadNaming.Size(option.File.Size)} · {option.CacheState}",
                                    12
                                )
                            );
                            label.Children.Add(Body(option.Release, 11));
                            var entry = new MenuItem
                            {
                                Header = label,
                                IsCheckable = true,
                                IsChecked = picks.GetValueOrDefault(episode)?.File.Id == option.File.Id,
                            };
                            entry.Click += (_, _) =>
                            {
                                picks[episode] = option;
                                excluded.Remove(episode);
                                RenderRows();
                                UpdateSummary();
                            };
                            menu.Items.Add(entry);
                        }
                        change.ContextMenu = menu;
                        menu.PlacementTarget = change;
                        menu.IsOpen = true;
                    };
                    rows.Children.Add(
                        new Border
                        {
                            Background = Brush("#18181B"),
                            BorderBrush = Brush("#2B2B2F"),
                            BorderThickness = new(1),
                            CornerRadius = new(12),
                            Child = card,
                            Margin = new(0, 0, 0, 10),
                        }
                    );
                }
            }
            if (plan.Episodes.Count == 0)
                rows.Children.Add(Body("No episodes in this scope. Choose episodes or another season above."));
        }
        async Task Build()
        {
            search?.Cancel();
            search = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var active = search;
            var version = ++revision;
            Invalidate(false);
            feedback.Text = "";
            progressText.Text = T("Looking for releases…", "جارٍ البحث عن الإصدارات…");
            refresh.IsEnabled = false;
            sources.Children.Clear();
            try
            {
                request.Preferences = preference;
                if (request.Scope == "From episode N")
                {
                    if (!int.TryParse(from, out var n))
                        throw new System.IO.IOException("Enter a starting episode number.");
                    request.FromEpisode = n;
                }
                if (request.Scope == "Next N episodes")
                {
                    if (!int.TryParse(next, out var n))
                        throw new System.IO.IOException("Enter how many episodes (1–25).");
                    request.NextCount = n;
                }
                if (request.Scope == "Several seasons / whole show" && chosenSeasons.Count == 0)
                    throw new System.IO.IOException("Choose at least one season.");
                request.Seasons = request.Scope == "Several seasons / whole show" ? chosenSeasons.Order().ToList() : [];
                request.SpecificEpisodes = specific.OrderBy(e => e.Season).ThenBy(e => e.Episode).ToList();
                var progress = new Progress<DownloadSourceStatus>(s =>
                {
                    if (version == revision && !active.IsCancellationRequested)
                        // What it is doing in words; the stage names ("Planner · Checking") were internal.
                        progressText.Text = (s.Source is "Planner" or "" ? "" : s.Source + " · ") + s.Message;
                });
                var result = await manager.PlanSeason(item.Id, request, progress, active.Token);
                if (active.IsCancellationRequested || version != revision)
                    return;
                plan = result;
                confirmId = Guid.NewGuid().ToString();
                picks.Clear();
                excluded.Clear();
                foreach (var episode in plan.Episodes)
                {
                    picks[episode] = episode.Chosen;
                    if (episode.State == "Over your total limit")
                        excluded.Add(episode);
                }
                foreach (var status in plan.Sources.DistinctBy(s => (s.Source, s.State, s.Message)))
                    sources.Children.Add(Body(status.Source + " · " + status.State + " · " + status.Message, 12));
                progressText.Text = plan.Message;
                RenderRows();
                UpdateSummary();
            }
            catch (OperationCanceledException) when (active.IsCancellationRequested) { }
            catch (Exception e)
            {
                if (version == revision)
                {
                    progressText.Text = T("Couldn't find releases. Try again.", "تعذّر العثور على إصدارات. حاول مجددًا.");
                    feedback.Text =
                        e is System.IO.IOException ? e.Message : "Check source availability and try Refresh plan.";
                }
            }
            finally
            {
                if (ReferenceEquals(search, active))
                    search = null;
                active.Dispose();
                if (version == revision)
                    refresh.IsEnabled = true;
            }
        }
        download.Visibility = Visibility.Collapsed;
        RenderControls();
    }
}
