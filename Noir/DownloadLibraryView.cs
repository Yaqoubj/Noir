using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Noir;

public partial class MainWindow
{
    string? downloadsTab;
    string downloadLibraryFilter = "All",
        downloadLibrarySort = "Recent";
    readonly HashSet<string> downloadLibraryExpanded = [];

    string Videos(int n) => n == 1 ? T("1 video", "فيديو واحد") : T("{0} videos", "{0} فيديوهات", n);

    static void DownloadName(DependencyObject element, string name) => AutomationProperties.SetName(element, name);

    Button ManageButton(string label, Action action, bool primary = false)
    {
        var b = ActionButton(label, action, primary);
        DownloadName(b, label);
        return b;
    }

    void PrepareDownloadManagement()
    {
        if (downloads == null)
            return;
        downloads.TrackLibraryMoves();
        downloads.IsFilePlaying = path =>
            Dispatcher.CheckAccess()
                ? current?.Path.Equals(path, StringComparison.OrdinalIgnoreCase) == true
                : Dispatcher.Invoke(() => current?.Path.Equals(path, StringComparison.OrdinalIgnoreCase) == true);
    }

    static ControlTemplate DownloadTabTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetValue(Border.PaddingProperty, new Thickness(12, 10, 14, 10));
        foreach (
            var (target, source) in new[]
            {
                (Border.BackgroundProperty, "Background"),
                (Border.BorderBrushProperty, "BorderBrush"),
                (Border.BorderThicknessProperty, "BorderThickness"),
            }
        )
            border.SetBinding(
                target,
                new System.Windows.Data.Binding(source)
                {
                    RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent,
                }
            );
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    // The three tabs of Downloads, drawn as cards: an icon, the tab's name and its live line (U130).
    void BuildDownloadTabs(StackPanel page, StackPanel queue, StackPanel library, StackPanel ready)
    {
        PrepareDownloadManagement();
        downloadsTab ??= downloads!.ActiveCount > 0 ? "Queue" : "On this PC";
        if (downloadsTab is not ("Queue" or "On this PC" or "Keep ready"))
            downloadsTab = "Queue";
        var tabs = new System.Windows.Controls.Primitives.UniformGrid
        {
            Rows = 1,
            MaxWidth = 1080,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(-5, 4, 0, Density.Pick(14, 18)),
        };
        bool loaded = false;
        var cards = new Dictionary<string, (Button Card, System.Windows.Shapes.Path? Icon, TextBlock Name)>();
        void Card(string key, string icon, string name, TextBlock note)
        {
            var mark = IconSet.Draw(icon, 17) as System.Windows.Shapes.Path;
            var badge = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new(9),
                Background = Brush("#14FFFFFF"),
                Margin = new(0, 0, 11, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = mark,
            };
            var title = Text(name, 14, weight: FontWeights.SemiBold);
            title.TextWrapping = TextWrapping.NoWrap;
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(title);
            if (note.Parent is Panel old)
                old.Children.Remove(note);
            words.Children.Add(note);
            var row = new DockPanel();
            DockPanel.SetDock(badge, Dock.Left);
            row.Children.Add(badge);
            row.Children.Add(words);
            var card = new Button
            {
                Content = row,
                Template = DownloadTabTemplate(),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new(5, 0, 5, 0),
                MinWidth = 200,
                Tag = "DownloadsTab:" + key,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            DownloadName(card, name);
            Motion.ButtonFeedback(card);
            card.Click += (_, _) => Select(key);
            cards[key] = (card, mark, title);
            tabs.Children.Add(card);
        }
        Card("Queue", "download", T("Queue", "قائمة التنزيل"), downloadsQueueNote!);
        Card("On this PC", "drive", T("On this PC", "على هذا الكمبيوتر"), downloadsPcNote!);
        Card("Keep ready", "check", T("Keep ready", "إبقاء الحلقات جاهزة"), downloadsReadyNote!);
        page.Children.Insert(0, tabs);
        void Select(string tab)
        {
            downloadsTab = tab;
            queue.Visibility = tab == "Queue" ? Visibility.Visible : Visibility.Collapsed;
            library.Visibility = tab == "On this PC" ? Visibility.Visible : Visibility.Collapsed;
            ready.Visibility = tab == "Keep ready" ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (key, c) in cards)
            {
                var on = key == tab;
                c.Card.Background = Brush(on ? "#1F1B14" : "#141416");
                c.Card.BorderBrush = Brush(on ? "#E4C084" : "#1FFFFFFF");
                c.Card.BorderThickness = new(on ? 1.4 : 1);
                c.Name.Foreground = Brush(on ? "#F0D3A0" : "#F4F1EB");
                if (c.Icon != null)
                    c.Icon.Stroke = Brush(on ? "#E4C084" : "#C7C2BC");
            }
            if (tab == "On this PC" && !loaded)
            {
                loaded = true;
                _ = BuildDownloadLibrary(library);
            }
        }
        Select(downloadsTab);
    }

    async Task BuildDownloadLibrary(StackPanel host)
    {
        host.Children.Clear();
        var bar = new DockPanel { Margin = new(0, 0, 0, 12) };
        var manageTools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(manageTools, Dock.Right);
        var organize = ManageButton("[[folder]]  " + T("Organize files", "تنظيم الملفات"), OpenOrganizer);
        DownloadName(organize, T("Organize files", "تنظيم الملفات"));
        organize.ToolTip = T(
            "Rename and sort your videos into show and season folders, with a preview and Undo.",
            "أعد تسمية الفيديوهات وافرزها في مجلدات المسلسلات والمواسم، مع معاينة وتراجع."
        );
        organize.Margin = new(8, 0, 0, 0);
        var freeUp = ManageButton("[[drive]]  " + T("Free up space", "تحرير مساحة"), ShowDownloadFreeSpace);
        DownloadName(freeUp, T("Free up space", "تحرير مساحة"));
        freeUp.ToolTip = T(
            "Find watched episodes, leftovers, samples and duplicate copies you can remove.",
            "اعثر على حلقات مشاهدة وملفات متبقية وعينات ونسخ مكررة يمكن إزالتها."
        );
        freeUp.Margin = new(8, 0, 0, 0);
        manageTools.Children.Add(organize);
        manageTools.Children.Add(freeUp);
        bar.Children.Add(manageTools);
        var filters = new WrapPanel();
        bar.Children.Add(filters);
        host.Children.Add(bar);
        foreach (
            var pair in new[]
            {
                ("All", T("All", "الكل")),
                ("Watched", T("Watched", "مشاهد")),
                ("Unwatched", T("Unwatched", "غير مشاهد")),
                ("Downloaded by Noir", T("Downloaded by Noir", "نُزّل بواسطة Noir")),
            }
        )
        {
            var key = pair.Item1;
            var b = PillOption(
                pair.Item2,
                () =>
                {
                    downloadLibraryFilter = key;
                    _ = BuildDownloadLibrary(host);
                },
                key == downloadLibraryFilter
            );
            filters.Children.Add(b);
        }
        var sort = new ComboBox
        {
            MinWidth = 135,
            Margin = new(10, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (
            var pair in new[]
            {
                ("Recent", T("Recent", "الأحدث")),
                ("Name", T("Name", "الاسم")),
                ("Size", T("Size", "الحجم")),
            }
        )
        {
            var option = new ComboBoxItem { Content = pair.Item2, Tag = pair.Item1 };
            sort.Items.Add(option);
            if (pair.Item1 == downloadLibrarySort)
                sort.SelectedItem = option;
        }
        DownloadName(sort, T("Sort local files", "ترتيب الملفات المحلية"));
        sort.SelectionChanged += (_, _) =>
        {
            downloadLibrarySort = (string)((ComboBoxItem)sort.SelectedItem).Tag;
            _ = BuildDownloadLibrary(host);
        };
        filters.Children.Add(sort);
        var rows = new StackPanel();
        host.Children.Add(rows);
        var loading = Body(T("Reading files on this PC…", "جارٍ قراءة الملفات على هذا الكمبيوتر…"));
        rows.Children.Add(loading);
        var manager = downloads!;
        var filter = downloadLibraryFilter;
        var order = downloadLibrarySort;
        try
        {
            var groups = await Task.Run(() => manager.LocalLibrary(filter, order));
            if (!host.Children.Contains(rows))
                return;
            rows.Children.Clear();
            if (groups.Count == 0)
            {
                rows.Children.Add(
                    Body(T("No matching videos on this PC.", "لا توجد فيديوهات مطابقة على هذا الكمبيوتر."))
                );
                return;
            }
            int shown = 0;
            void More()
            {
                var batch = groups.Skip(shown).Take(60).ToList();
                shown += batch.Count;
                foreach (var group in batch)
                    rows.Children.Add(LocalGroup(group));
                if (shown < groups.Count)
                {
                    var more = ManageButton(T("Show more", "عرض المزيد"), () =>
                    {
                        rows.Children.RemoveAt(rows.Children.Count - 1);
                        More();
                    });
                    rows.Children.Add(more);
                }
            }
            FrameworkElement LocalGroup(DownloadLocalGroup group)
            {
                var all = group.Files;
                var body = new StackPanel();
                var header = LocalRow(
                    group.Key == "movies" ? T("Movies", "الأفلام") : group.Name,
                    all,
                    null,
                    group.Key == "movies" ? "" : all.Select(f => f.Item.Poster).FirstOrDefault(File.Exists) ?? ""
                );
                var expander = new Expander
                {
                    Header = header,
                    Content = body,
                    // The shared expander header pads itself by 9 px above and below; pull the cards to a 10 px gap.
                    Margin = new(0, -6, 0, -7),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Tag = "DownloadLibraryGroup:" + group.Key,
                };
                DownloadName(expander, group.Name);
                bool built = false;
                void Fill()
                {
                    if (built)
                        return;
                    built = true;
                    if (group.Key == "movies")
                        AddFiles(body, all);
                    else
                        foreach (var season in all.GroupBy(f => f.Item.Season).OrderBy(g => g.Key))
                        {
                            var key = group.Key + ":" + season.Key;
                            var seasonBody = new StackPanel { Margin = new(16, 0, 0, 0) };
                            var seasonFiles = season.ToList();
                            var seasonView = new Expander
                            {
                                Header = LocalRow(T("Season {0}", "الموسم {0}", season.Key), seasonFiles, null),
                                Content = seasonBody,
                                Margin = new(12, 0, 0, 8),
                                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                            };
                            DownloadName(seasonView, T("Season {0}", "الموسم {0}", season.Key));
                            bool populated = false;
                            seasonView.Expanded += (_, e) =>
                            {
                                e.Handled = true;
                                downloadLibraryExpanded.Add(key);
                                if (!populated)
                                {
                                    populated = true;
                                    AddFiles(seasonBody, seasonFiles);
                                }
                            };
                            seasonView.Collapsed += (_, e) =>
                            {
                                e.Handled = true;
                                downloadLibraryExpanded.Remove(key);
                            };
                            body.Children.Add(seasonView);
                            seasonView.IsExpanded = downloadLibraryExpanded.Contains(key);
                        }
                }
                expander.Expanded += (_, e) =>
                {
                    e.Handled = true;
                    downloadLibraryExpanded.Add(group.Key);
                    Fill();
                };
                expander.Collapsed += (_, e) =>
                {
                    e.Handled = true;
                    downloadLibraryExpanded.Remove(group.Key);
                };
                expander.IsExpanded = downloadLibraryExpanded.Contains(group.Key);
                return expander;
            }
            void AddFiles(StackPanel parent, List<DownloadLocalFile> files)
            {
                var sorted =
                    order == "Size" ? files.OrderByDescending(f => f.Bytes)
                    : order == "Recent" ? files.OrderByDescending(f => f.Modified)
                    : files.OrderBy(f => f.Item.Label, StringComparer.CurrentCultureIgnoreCase);
                var list = sorted.ToList();
                int count = 0;
                void Next()
                {
                    foreach (var file in list.Skip(count).Take(60))
                        parent.Children.Add(
                            LocalRow(
                                file.Item.IsMovie
                                    ? file.Item.Title
                                    : file.Item.EpisodeLabel
                                        + (file.Item.EpisodeTitle.Length > 0 ? " · " + file.Item.EpisodeTitle : ""),
                                [file],
                                file,
                                file.Item.IsMovie && File.Exists(file.Item.Poster) ? file.Item.Poster : null
                            )
                        );
                    count += 60;
                    if (count < list.Count)
                        parent.Children.Add(ManageButton(T("Show more", "عرض المزيد"), () =>
                            {
                                parent.Children.RemoveAt(parent.Children.Count - 1);
                                Next();
                            }));
                }
                Next();
            }
            // art: a poster path draws a thumbnail; "" draws the film mark (the Movies group); null draws none (seasons, episodes).
            FrameworkElement LocalRow(
                string title,
                List<DownloadLocalFile> files,
                DownloadLocalFile? file,
                string? art = null
            )
            {
                var row = new Grid { Margin = new(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                if (art != null)
                {
                    var thumb = new Border
                    {
                        Width = 36,
                        Height = 54,
                        CornerRadius = new(6),
                        Background = Brush("#1C1C1F"),
                        Margin = new(8, 5, 6, 5),
                        VerticalAlignment = VerticalAlignment.Center,
                        ClipToBounds = true,
                    };
                    if (art.Length > 0)
                    {
                        thumb.Child = Art(art, title, 54, true, false);
                        thumb.Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 36, 54), 6, 6);
                    }
                    else
                    {
                        var film = Icon("M3,4 L21,4 L21,20 L3,20 Z M7,4 L7,20 M17,4 L17,20 M3,8 L7,8 M3,12 L7,12 M3,16 L7,16 M17,8 L21,8 M17,12 L21,12 M17,16 L21,16", 20, "#C7C2BC");
                        film.HorizontalAlignment = HorizontalAlignment.Center;
                        film.VerticalAlignment = VerticalAlignment.Center;
                        thumb.Child = film;
                    }
                    row.Children.Add(thumb);
                }
                var text = new StackPanel { Margin = new(6, 8, 12, 8), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(text, 1);
                text.Children.Add(Text(title, 14, "#F4F1EB", FontWeights.SemiBold));
                var count = files.Count;
                var seen = files.Count(f => f.Watched);
                var stats = new WrapPanel();
                var summary =
                    file != null
                        ? (file.Watched ? T("Watched", "مشاهد") : T("Not watched yet", "لم تُشاهد بعد"))
                        : Videos(count) + " · " + T("{0} watched", "{0} مشاهد", seen);
                stats.Children.Add(Body(summary + " · ", 12));
                stats.Children.Add(
                    new TextBlock
                    {
                        Text = DownloadNaming.Size(files.Sum(f => f.Bytes)),
                        FlowDirection = FlowDirection.LeftToRight,
                        Foreground = Brush("#C7C2BC"),
                        FontSize = 12,
                    }
                );
                text.Children.Add(stats);
                if (file?.Noir == true)
                    text.Children.Add(Text(T("Downloaded by Noir", "نُزّل بواسطة Noir"), 11, "#E4C084"));
                row.Children.Add(text);
                var tools = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(tools, 2);
                row.Children.Add(tools);
                var trash = ManageButton("", () => ShowDownloadDelete(title, files));
                trash.FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
                trash.ToolTip = T("Delete files", "حذف الملفات");
                trash.Margin = new(4, 0, 4, 0);
                DownloadName(trash, T("Delete files for", "حذف ملفات") + " " + title);
                tools.Children.Add(trash);
                var more = ManageButton("[[more]]", () => { });
                more.Margin = new(4, 0, 4, 0);
                DownloadName(more, T("More options for", "خيارات أخرى لـ") + " " + title);
                tools.Children.Add(more);
                more.Click += (_, e) =>
                {
                    e.Handled = true;
                    var menu = new ContextMenu { FlowDirection = FlowDirection };
                    void Entry(string label, Action action)
                    {
                        var entry = new MenuItem { Header = label };
                        DownloadName(entry, label);
                        entry.Click += (_, _) => action();
                        menu.Items.Add(entry);
                    }
                    if (file != null)
                        Entry(T("Play", "تشغيل"), () =>
                        {
                            var item = store.Archive.FirstOrDefault(i => i.Id == file.Item.Id);
                            if (item != null)
                                _ = PlayVideo(item);
                        });
                    Entry(T("Show in folder", "إظهار في المجلد"), () => ShowInFolder(files[0].Path));
                    Entry(T("Delete", "حذف") + " " + Videos(files.Count) + "…", () => ShowDownloadDelete(title, files));
                    if (file == null)
                        Entry(T("Delete watched…", "حذف المشاهد…"), () =>
                            ShowDownloadDelete(title, files.Where(f => f.Watched).ToList(), true)
                        );
                    Entry(T("Move to…", "نقل إلى…"), () => ShowDownloadMove(title, files));
                    more.ContextMenu = menu;
                    menu.PlacementTarget = more;
                    menu.IsOpen = true;
                };
                return new Border
                {
                    Background = Brush("#141416"),
                    CornerRadius = new(10),
                    BorderThickness = new(1),
                    BorderBrush = Brush("#26FFFFFF"),
                    Child = row,
                    Margin = new(0, 0, 0, 5),
                    Tag = "DownloadLocalRow",
                };
            }
            More();
        }
        catch (Exception e)
        {
            loading.Text = T("Couldn't read local files: ", "تعذرت قراءة الملفات المحلية: ") + e.Message;
        }
    }

    void ShowDownloadDelete(string title, List<DownloadLocalFile> files, bool watchedOnly = false)
    {
        PrepareDownloadManagement();
        var sheet = OpenSheet(T("Delete files", "حذف الملفات"), title, SheetKind.Dialog, 560);
        var size = DownloadNaming.Size(files.Sum(f => f.Bytes));
        sheet.Add(Body(title + " · " + Videos(files.Count) + " · " + size, 14));
        sheet.Add(
            Body(
                T(
                    "Watch history, ratings, and titles stay in your library.",
                    "يبقى سجل المشاهدة والتقييم والعناوين في المكتبة."
                ),
                12
            )
        );
        var saved = downloads!.ManagementPreferences.WhenDeleting;
        var recycle = new RadioButton
        {
            Content = T("Move to Recycle Bin", "نقل إلى سلة المحذوفات"),
            GroupName = "DownloadDelete",
            IsChecked = saved != DownloadDeleteChoice.Permanent,
            Foreground = Brush("#F4F1EB"),
            Margin = new(0, 16, 0, 5),
        };
        var permanent = new RadioButton
        {
            Content = T("Delete permanently", "حذف نهائي"),
            GroupName = "DownloadDelete",
            IsChecked = saved == DownloadDeleteChoice.Permanent,
            Foreground = Brush("#E5736B"),
            Margin = new(0, 16, 0, 5),
        };
        DownloadName(recycle, (string)recycle.Content);
        DownloadName(permanent, (string)permanent.Content);
        sheet.Add(recycle);
        sheet.Add(
            Body(T("Space is freed when you empty the Recycle Bin", "تتحرر المساحة عند إفراغ سلة المحذوفات"), 12)
        );
        sheet.Add(permanent);
        sheet.Add(Body(T("Frees {0} now", "يحرر {0} الآن", size), 12));
        var remember = new CheckBox { Content = T("Remember my choice", "تذكر اختياري"), Margin = new(0, 18, 0, 12) };
        DownloadName(remember, (string)remember.Content);
        sheet.Add(remember);
        var note = Body("", 12);
        sheet.Add(note);
        sheet.Action(T("Cancel", "إلغاء"), () => sheet.Close(false));
        var submit = sheet.Action(T("Delete", "حذف") + " " + Videos(files.Count), () => { }, true);
        submit.IsEnabled = files.Count > 0;
        submit.Click += async (_, _) =>
        {
            var choice = recycle.IsChecked == true ? DownloadDeleteChoice.Recycle : DownloadDeleteChoice.Permanent;
            var save = remember.IsChecked == true;
            submit.IsEnabled = false;
            sheet.CanClose = () => false;
            try
            {
                if (save)
                    downloads.SetDeleteChoice(choice);
                var result = await Task.Run(() => downloads.DeleteLocal(files, choice, watchedOnly));
                if (result.Errors.Count > 0)
                {
                    note.Text = ManageResult(result);
                    submit.IsEnabled = true;
                }
                else
                {
                    sheet.Close(true);
                    Status.Text = ManageResult(result);
                    RefreshDownloadLibrary();
                }
            }
            catch (Exception e)
            {
                note.Text = e.Message;
                submit.IsEnabled = true;
            }
            finally
            {
                sheet.CanClose = null;
            }
        };
    }

    string ManageResult(DownloadManageResult result) =>
        Videos(result.Files)
        + " · "
        + DownloadNaming.Size(result.Bytes)
        + (result.Errors.Count > 0 ? "\n" + string.Join("\n", result.Errors) : "");

    void RefreshDownloadLibrary()
    {
        if (page == "Downloads")
            RenderQuiet();
    }

    void ShowDownloadMove(string title, List<DownloadLocalFile> files)
    {
        PrepareDownloadManagement();
        var sheet = OpenSheet(T("Move to…", "نقل إلى…"), title, SheetKind.Dialog, 580);
        var movies = files.FirstOrDefault()?.Item.IsMovie ?? false;
        if (files.Any(f => f.Item.IsMovie != movies))
        {
            sheet.Add(Body(T("Choose shows or movies separately.", "اختر المسلسلات أو الأفلام كلًا على حدة.")));
            return;
        }
        var roots = new ComboBox
        {
            Margin = new(0, 12, 0, 12),
            MinWidth = 300,
            FlowDirection = FlowDirection.LeftToRight,
        };
        DownloadName(roots, T("Destination library folder", "مجلد المكتبة الوجهة"));
        foreach (var root in store.State.Roots.Where(r => r.Movies == movies))
            roots.Items.Add(root.Path);
        if (roots.Items.Count > 0)
            roots.SelectedIndex = 0;
        sheet.Add(roots);
        var space = Body("", 12);
        sheet.Add(space);
        void Space()
        {
            space.Text = roots.SelectedItem is string path
                ? T("{0} free", "متاح {0}", DownloadNaming.Size(downloads!.FreeBytesFor(path)))
                : "";
        }
        roots.SelectionChanged += (_, _) => Space();
        Space();
        sheet.Add(ManageButton(T("Choose folder…", "اختيار مجلد…"), () =>
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = T("Choose a library folder", "اختر مجلد مكتبة"),
                };
                if (dialog.ShowDialog(this) != true)
                    return;
                var path = dialog.FolderName;
                if (
                    !store.State.Roots.Any(r =>
                        r.Movies == movies && r.Path.Equals(path, StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    store.State.Roots.Add(new() { Path = path, Movies = movies });
                    store.Save();
                    WatchLibraryFolders();
                }
                if (!roots.Items.Contains(path))
                    roots.Items.Add(path);
                roots.SelectedItem = path;
            }));
        sheet.Add(
            Body(
                T(
                    "Subtitles move with the video. Watch history and progress are kept.",
                    "تُنقل الترجمات مع الفيديو مع حفظ سجل المشاهدة والتقدم."
                ),
                12
            )
        );
        var note = Body("", 12);
        sheet.Add(note);
        sheet.Action(T("Cancel", "إلغاء"), () => sheet.Close(false));
        var move = sheet.Action(T("Move files", "نقل الملفات"), () => { }, true);
        move.Click += async (_, _) =>
        {
            if (roots.SelectedItem is not string root)
                return;
            move.IsEnabled = false;
            sheet.CanClose = () => false;
            var progress = new Progress<(long Done, long Total)>(p =>
                note.Text = DownloadNaming.Size(p.Done) + " / " + DownloadNaming.Size(p.Total)
            );
            try
            {
                var result = await Task.Run(() => downloads!.MoveLocal(files, root, progress));
                if (result.Errors.Count > 0)
                {
                    note.Text = ManageResult(result);
                    move.IsEnabled = true;
                }
                else
                {
                    sheet.Close(true);
                    Status.Text = ManageResult(result);
                    RefreshDownloadLibrary();
                }
            }
            catch (Exception e)
            {
                note.Text = e.Message;
                move.IsEnabled = true;
            }
            finally
            {
                sheet.CanClose = null;
            }
        };
    }

    // U96: WPF ignores the Unicode direction marks, so a Latin part in brackets (the extension list) is its own
    // left-to-right run inside an Arabic title.
    static TextBlock FreeSpaceGroupLabel(string title, string figure)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var last = 0;
        foreach (
            System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                title,
                @"\(([ -~]+)\)"
            )
        )
        {
            block.Inlines.Add(new System.Windows.Documents.Run(title[last..(m.Index + 1)]));
            block.Inlines.Add(
                new System.Windows.Documents.Run(m.Groups[1].Value) { FlowDirection = FlowDirection.LeftToRight }
            );
            last = m.Index + m.Length - 1;
        }
        block.Inlines.Add(new System.Windows.Documents.Run(title[last..] + (figure.Length > 0 ? " · " : "")));
        if (figure.Length > 0)
            block.Inlines.Add(new System.Windows.Documents.Run(figure) { FlowDirection = FlowDirection.LeftToRight });
        return block;
    }

    // Arabic: a Latin part (a size, a file or title name) is its own left-to-right run, so "40.0 MB" and
    // "Night Signal - S01" keep their order. Plain text in English. ltr null means the whole text.
    static object LtrInside(string text, string? ltr)
    {
        if (!Loc.Arabic)
            return text;
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var at = ltr == null ? 0 : text.IndexOf(ltr, StringComparison.Ordinal);
        if (at < 0)
            return text;
        var length = ltr == null ? text.Length : ltr.Length;
        if (at > 0)
            block.Inlines.Add(new System.Windows.Documents.Run(text[..at]));
        block.Inlines.Add(
            new System.Windows.Documents.Run(text.Substring(at, length)) { FlowDirection = FlowDirection.LeftToRight }
        );
        if (at + length < text.Length)
            block.Inlines.Add(new System.Windows.Documents.Run(text[(at + length)..]));
        return block;
    }

    async void ShowDownloadFreeSpace()
    {
        PrepareDownloadManagement();
        var manager = downloads!;
        var sheet = OpenSheet(
            T("Free up space", "تحرير مساحة"),
            T("Review files before removing them", "راجع الملفات قبل حذفها"),
            SheetKind.Side,
            720
        );
        var label = new Dictionary<string, string>
        {
            {
                "Watched episodes (older than 7 days)",
                T("Watched episodes (older than 7 days)", "حلقات شوهدت منذ أكثر من 7 أيام")
            },
            { "Finished shows", T("Finished shows (all watched)", "مسلسلات مكتملة المشاهدة") },
            { "Leftover partial downloads", T("Leftover partial downloads", "بقايا تنزيلات جزئية") },
            { "Torrents only sharing back", T("Torrents only sharing back", "تورنت يشارك الملفات فقط") },
            { PcDownloads.KindEmptyFolders, T("Empty folders", "مجلدات فارغة") },
            { PcDownloads.KindSamples, T("Sample videos", "مقاطع تجريبية") },
            {
                PcDownloads.KindLoose,
                T("Leftover files from releases (.nfo, .txt, .url)", "ملفات متبقية من الإصدارات (.nfo, .txt, .url)")
            },
            {
                PcDownloads.KindDuplicates,
                T("Duplicates · the smaller copy is ticked", "نسخ مكررة · النسخة الأصغر محددة")
            },
        };
        var choices = new ComboBox { Margin = new(0, 8, 0, 12) };
        foreach (
            var pair in new[]
            {
                (DownloadDeleteChoice.Ask, T("Ask each time", "السؤال كل مرة")),
                (DownloadDeleteChoice.Recycle, T("Recycle Bin", "سلة المحذوفات")),
                (DownloadDeleteChoice.Permanent, T("Permanently", "نهائيًا")),
            }
        )
        {
            var row = new ComboBoxItem { Content = pair.Item2, Tag = pair.Item1 };
            choices.Items.Add(row);
            if (pair.Item1 == manager.ManagementPreferences.WhenDeleting)
                choices.SelectedItem = row;
        }
        DownloadName(choices, T("When deleting", "عند الحذف"));
        sheet.Add(Body(T("When deleting:", "عند الحذف:"), 12));
        sheet.Add(choices);
        var warning = Body(
            T(
                "Recycle Bin frees space when you empty it. Sharing can be stopped while keeping the video.",
                "تتحرر مساحة سلة المحذوفات عند إفراغها. يمكن إيقاف المشاركة مع الاحتفاظ بالفيديو."
            ),
            12
        );
        sheet.Add(warning);
        var items = new List<(CheckBox Box, DownloadSpaceItem Item)>();
        var groupFigures = new List<Action>();
        var sections = new StackPanel();
        sheet.Add(sections);
        var status = Body(T("Looking for files…", "جارٍ البحث عن الملفات…"), 12);
        sheet.Add(status);
        sheet.Add(ManageButton(T("Fix names and folders", "إصلاح الأسماء والمجلدات"), () =>
            {
                manager.TrackLibraryMoves();
                OpenOrganizer();
            }));
        sheet.Action(T("Close", "إغلاق"), () => sheet.Close(false));
        var submit = sheet.Action(T("Review selected files", "مراجعة الملفات المحددة"), () => { }, true);
        submit.IsEnabled = false;
        DownloadDeleteChoice Choice() => (DownloadDeleteChoice)((ComboBoxItem)choices.SelectedItem).Tag;
        void Update()
        {
            var bytes = items.Where(x => x.Box.IsChecked == true).Sum(x => x.Item.Bytes);
            var buttonText = FreeSpaceText.Button(Choice(), bytes, Loc.Arabic);
            submit.Content = LtrInside(buttonText, bytes > 0 ? DownloadNaming.Size(bytes) : "0 KB");
            submit.IsEnabled = items.Any(x => x.Box.IsChecked == true);
            foreach (var redraw in groupFigures)
                redraw();
        }
        choices.SelectionChanged += (_, _) =>
        {
            manager.SetDeleteChoice(Choice());
            Update();
        };
        try
        {
            var suggestions = await Task.Run(() => manager.SpaceSuggestions());
            if (!sheet.IsOpen)
                return;
            status.Text =
                suggestions.Count == 0
                    ? T("No cleanup suggestions right now.", "لا توجد اقتراحات تنظيف الآن.")
                    : T("Untick anything you want to keep.", "أزل التحديد عما تريد الاحتفاظ به.");
            foreach (var group in suggestions)
            {
                var groupBox = new CheckBox
                {
                    Content = FreeSpaceGroupLabel(label[group.Kind], ""),
                    IsChecked = group.Items.Any(i => i.Preselect && !i.ListOnly),
                    Margin = new(0, 12, 0, 8),
                };
                DownloadName(groupBox, label[group.Kind]);
                // U93: the figure beside a group is what its ticked files free, and it follows the ticks.
                var groupKind = group.Kind;
                var groupTitle = label[group.Kind];
                groupFigures.Add(() =>
                    groupBox.Content = FreeSpaceGroupLabel(
                        groupTitle,
                        FreeSpaceText.GroupFigure(
                            groupKind,
                            items.Where(x => x.Item.Kind == groupKind && x.Box.IsChecked == true).Select(x => x.Item),
                            Loc.Arabic
                        )
                    )
                );
                var list = new StackPanel();
                var groupItems = new List<CheckBox>();
                var groupSpace = new List<DownloadSpaceItem>();
                foreach (var item in group.Items)
                {
                    if (item.ListOnly)
                    {
                        list.Children.Add(
                            Body(
                                item.Label
                                    + " · "
                                    + T("Program file · Noir does not remove these", "ملف برنامج · لا يحذفه Noir"),
                                12
                            )
                        );
                        continue;
                    }
                    var check = new CheckBox
                    {
                        Content = LtrInside(
                            item.Label + (item.Bytes > 0 ? " · " + DownloadNaming.Size(item.Bytes) : ""),
                            null
                        ),
                        IsChecked = item.Preselect,
                        Margin = new(16, 6, 0, 6),
                    };
                    DownloadName(check, item.Label);
                    if (item.Path.Length > 0)
                        check.ToolTip = item.Path;
                    check.Checked += (_, _) => Update();
                    check.Unchecked += (_, _) => Update();
                    list.Children.Add(check);
                    items.Add((check, item));
                    groupItems.Add(check);
                    groupSpace.Add(item);
                }
                groupBox.Checked += (_, _) =>
                {
                    for (var n = 0; n < groupItems.Count; n++)
                        groupItems[n].IsChecked = groupSpace[n].Preselect;
                    Update();
                };
                groupBox.Unchecked += (_, _) =>
                {
                    foreach (var box in groupItems)
                        box.IsChecked = false;
                    Update();
                };
                sections.Children.Add(groupBox);
                var expander = new Expander { Header = T("Review files", "مراجعة الملفات"), Content = list };
                DownloadName(expander, label[group.Kind]);
                sections.Children.Add(expander);
            }
            Update();
        }
        catch (Exception e)
        {
            store.Log(e);
            status.Text = e.Message;
        }
        submit.Click += async (_, _) =>
        {
            var selected = items.Where(x => x.Box.IsChecked == true).Select(x => x.Item).ToList();
            var choice = Choice();
            if (choice == DownloadDeleteChoice.Ask)
            {
                var confirmation = OpenSheet(
                    T("Choose how to delete", "اختر طريقة الحذف"),
                    T("Your files are kept until you confirm", "تبقى ملفاتك حتى التأكيد"),
                    SheetKind.Dialog,
                    500
                );
                confirmation.Add(
                    Body(T("Recycle Bin frees space after you empty it.", "تتحرر مساحة سلة المحذوفات بعد إفراغها."))
                );
                confirmation.Action(
                    T("Move to Recycle Bin", "نقل إلى سلة المحذوفات"),
                    () =>
                    {
                        confirmation.Close(true);
                        _ = Run(DownloadDeleteChoice.Recycle);
                    },
                    true
                );
                confirmation.Action(T("Delete permanently", "حذف نهائي"), () =>
                {
                    confirmation.Close(true);
                    _ = Run(DownloadDeleteChoice.Permanent);
                });
                return;
            }
            await Run(choice);
            async Task Run(DownloadDeleteChoice mode)
            {
                submit.IsEnabled = false;
                sheet.CanClose = () => false;
                try
                {
                    var result = await Task.Run(() => manager.FreeSpace(selected, mode));
                    if (result.Errors.Count > 0)
                    {
                        status.Text = ManageResult(result);
                        submit.IsEnabled = true;
                    }
                    else
                    {
                        sheet.Close(true);
                        Status.Text = ManageResult(result);
                        RefreshDownloadLibrary();
                    }
                }
                catch (Exception e)
                {
                    status.Text = e.Message;
                    submit.IsEnabled = true;
                }
                finally
                {
                    sheet.CanClose = null;
                }
            }
        };
    }

    void ShowDownloadCancel(PcDownloadJob job)
    {
        var sheet = OpenSheet(T("Cancel download", "إلغاء التنزيل"), job.Name, SheetKind.Dialog, 540);
        var remove = new RadioButton
        {
            Content = T("Cancel and delete the partial file", "إلغاء وحذف الملف الجزئي"),
            IsChecked = true,
            GroupName = "DownloadCancel",
            Margin = new(0, 12, 0, 12),
        };
        var keep = new RadioButton
        {
            Content = T("Keep it so I can resume later", "الاحتفاظ به للاستئناف لاحقًا"),
            GroupName = "DownloadCancel",
            Margin = new(0, 8, 0, 16),
        };
        DownloadName(remove, (string)remove.Content);
        DownloadName(keep, (string)keep.Content);
        sheet.Add(remove);
        sheet.Add(keep);
        var error = Body("", 12);
        sheet.Add(error);
        sheet.Action(T("Back", "رجوع"), () => sheet.Close(false));
        var confirm = sheet.Action(T("Confirm", "تأكيد"), () => { }, true);
        confirm.Click += async (_, _) =>
        {
            var delete = remove.IsChecked == true;
            confirm.IsEnabled = false;
            try
            {
                await downloads!.CancelDownload(job.Id, delete);
                sheet.Close(true);
                downloadPageUpdate?.Invoke();
            }
            catch (Exception e)
            {
                error.Text = e.Message;
                confirm.IsEnabled = true;
            }
        };
    }
}
