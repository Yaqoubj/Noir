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
            _ => T(value),
        };

    static string ReadySize(long bytes) => bytes == 0 ? "0 GB" : DownloadNaming.Size(bytes);

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

    public FrameworkElement? ReadyShelf()
    {
        if (downloads == null)
            return null;
        var state = downloads.ReadyViewState;
        var titles = state.Titles.Values.Where(t => t.Episodes.Count > 0).Take(12).ToList();
        if (titles.Count == 0)
            return null;
        var panel = new StackPanel { Margin = new(0, 18, 0, 10) };
        panel.Children.Add(
            Heading(
                T("Keep ready", "إبقاء الحلقات جاهزة"),
                T("Your next watch is already here", "مشاهدتك القادمة جاهزة هنا")
            )
        );
        var rail = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(
            new ScrollViewer
            {
                Content = rail,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            }
        );
        foreach (var title in titles)
        {
            var column = new StackPanel { Width = 250 };
            column.Children.Add(Text(title.Title, 17, "#F4F1EB", FontWeights.SemiBold));
            column.Children.Add(Body(state.Paused ? ReadyText("Preparation paused") : ReadyText(title.Reason), 12));
            var first = title.Episodes.First();
            column.Children.Add(
                Text(
                    first.Season == 0 ? T("Movie", "فيلم") : $"S{first.Season:00} · E{first.Episode:00}",
                    12,
                    "#E4C084"
                )
            );
            if (first.Local)
            {
                var item = store.EpisodesFor(title.Group).FirstOrDefault(e => e.Id == first.ItemId);
                if (item != null)
                    column.Children.Add(
                        ActionButton(T("Play offline", "شاهد دون اتصال"), () => _ = PlayVideo(item), true)
                    );
            }
            else
                column.Children.Add(ActionButton(T("Keep ready", "إبقاء الحلقات جاهزة"), ShowDownloads));
            var card = ReadyCard(column);
            card.Margin = new(0, 0, 14, 6);
            rail.Children.Add(card);
        }
        Motion.EnterOnce(panel);
        return panel;
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
        var counts = new[]
        {
            T("1 episode", "حلقة واحدة"),
            T("2 episodes", "حلقتان"),
            T("3 episodes", "٣ حلقات"),
            T("Whole season", "الموسم كاملًا"),
        };
        int[] values = [1, 2, 3, -1];
        var count = new ComboBox
        {
            ItemsSource = counts,
            SelectedIndex = Math.Max(0, Array.IndexOf(values, preference.KeepReadyCount)),
            Margin = new(0, 10, 0, 12),
            Padding = Density.InputPadding,
        };
        panel.Children.Add(count);
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
            var selected = values[Math.Max(0, count.SelectedIndex)];
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
            }
            catch (Exception)
            {
                if (rev == revision && window.IsOpen)
                    rows.Children.Add(Body(ReadyText("Source check failed; recheck scheduled"), 12));
            }
        }
        var cleanup = new CheckBox
        {
            Content = T("Remove watched after 7 days", "إزالة ما تمت مشاهدته بعد ٧ أيام"),
            IsChecked = manager.ReadyViewState.CleanupWatched,
            Margin = new(0, 16, 0, 8),
        };
        panel.Children.Add(cleanup);
        panel.Children.Add(
            Body(
                T(
                    "Only files Keep ready downloaded. Pinned files stay. Removal waits until every profile has finished; unknown progress keeps the file.",
                    "فقط الملفات التي جلبتها ميزة إبقاء الحلقات جاهزة. تبقى الملفات المثبتة. تنتظر الإزالة حتى تنتهي جميع الملفات الشخصية من المشاهدة؛ وعند غياب معلومات التقدم يبقى الملف."
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
                    preference.KeepReadyCount = values[Math.Max(0, count.SelectedIndex)];
                    await Task.Run(() => manager.SaveSeasonPreferences(item.Id, preference));
                    var removeWatched = cleanup.IsChecked == true;
                    await manager.ChangeReady(s =>
                    {
                        s.CleanupWatched = removeWatched;
                        s.Log("Preferences changed", item.Title);
                    });
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
        count.SelectionChanged += (_, _) => keepReadyPreviewTask = Preview();
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
        var cleanup = new CheckBox
        {
            Content = T("Remove watched after 7 days", "إزالة ما تمت مشاهدته بعد ٧ أيام"),
            IsChecked = state.CleanupWatched,
            Margin = new(0, 8, 0, 8),
        };
        panel.Children.Add(cleanup);
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
        string last = budget.Text + "|" + free.Text + "|" + cleanup.IsChecked;
        async Task Save()
        {
            if (budget.Text + "|" + free.Text + "|" + cleanup.IsChecked == last)
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
            var removeWatched = cleanup.IsChecked == true;
            await manager.ChangeReady(s =>
            {
                s.BudgetBytes = (long)(b * NoirReadyPolicy.GB);
                s.KeepFreeBytes = (long)(f * NoirReadyPolicy.GB);
                s.CleanupWatched = removeWatched;
                s.Log("Preferences changed");
            });
            status.Text = "✓ " + T("Saved", "تم الحفظ");
            _ = manager.PrepareReady("Preferences changed");
            last = budget.Text + "|" + free.Text + "|" + cleanup.IsChecked;
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
        cleanup.Click += async (_, _) => await Save();
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
        panel.Children.Add(
            Body(
                T(
                    "At least {0} stays free. Unwatched episodes stay on your PC.",
                    "تبقى مساحة {0} حرة على الأقل. تبقى الحلقات غير المشاهدة على الكمبيوتر.",
                    ReadySize(state.KeepFreeBytes)
                ),
                12
            )
        );
        var titleRows = new StackPanel();
        panel.Children.Add(titleRows);
        void DrawTitles()
        {
            titleRows.Children.Clear();
            foreach (var title in state.Titles.Values.Take(40))
            {
                var row = new StackPanel();
                row.Children.Add(Text(title.Title, 15, "#F4F1EB", FontWeights.SemiBold));
                row.Children.Add(Body(state.Paused ? ReadyText("Preparation paused") : ReadyText(title.Reason), 12));
                if (title.Bytes > 0)
                    row.Children.Add(Text(title.Quality + " · " + ReadySize(title.Bytes), 12, "#E4C084"));
                titleRows.Children.Add(ReadyCard(row));
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
            foreach (var file in state.ManagedFiles.TakeLast(60))
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
                            var current = s.ManagedFiles.FirstOrDefault(f => f.JobId == file.JobId);
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
                            + (entry.Title.Length > 0 ? " · " + entry.Title : ""),
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
            usage.Text = T(
                "{0} used + {1} reserved · {2} budget",
                "{0} مستخدمة + {1} محجوزة · الميزانية {2}",
                ReadySize(storage.UsedBytes),
                ReadySize(storage.ReservedBytes),
                ReadySize(state.BudgetBytes)
            );
            storageBar.Maximum = Math.Max(1, state.BudgetBytes);
            storageBar.Value = Math.Min(state.BudgetBytes, storage.UsedBytes + storage.ReservedBytes);
            var stamp =
                state.Paused
                + string.Join("|", state.Titles.Values.Select(t => t.Group + t.Reason + t.Bytes))
                + string.Join("|", state.ManagedFiles.Select(f => f.JobId + f.Pinned + f.RemoveAt + f.CleanupCancelled))
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
