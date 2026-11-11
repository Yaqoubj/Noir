using System.Windows;
using System.Windows.Controls;

namespace Noir;

public partial class MainWindow
{
    const int OrganizerPage = 60;

    static string OrganizerVideos(int n) =>
        Loc.Count(n, "video", "videos", "فيديو واحد", "فيديوان", "فيديوهات", "فيديو");

    // U91: one row per file, "old → new" relative to the library folder; the full paths are in the tooltip.
    FrameworkElement OrganizerFileRow(OrganizationMove move)
    {
        var line = new TextBlock
        {
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new(0, 1, 0, 3),
            FlowDirection = FlowDirection.LeftToRight,
            TextAlignment = Loc.Arabic ? TextAlignment.Right : TextAlignment.Left,
            ToolTip = move.From + "\n→ " + move.To,
        };
        line.Inlines.Add(
            new System.Windows.Documents.Run(OrganizationMove.Relative(move.Root, move.From))
            {
                Foreground = Brush("#B8B2AA"),
            }
        );
        line.Inlines.Add(new System.Windows.Documents.Run("  →  ") { Foreground = Brush("#8F8A83") });
        line.Inlines.Add(
            new System.Windows.Documents.Run(OrganizationMove.Relative(move.Root, move.To))
            {
                Foreground = Brush("#E4C084"),
            }
        );
        return line;
    }

    void OpenOrganizer()
    {
        if (current != null || scanning || metadataRefreshing)
        {
            Status.Text = T(
                "Finish playback or the library refresh before organizing files.",
                "أنهِ التشغيل أو تحديث المكتبة قبل تنظيم الملفات."
            );
            return;
        }
        var dialog = OpenSheet(T("Organize your files", "نظّم ملفاتك"), null, SheetKind.Side, 720);
        var panel = new StackPanel();
        dialog.Add(panel);
        panel.Children.Add(
            Body(
                T(
                    "Shows: Title (Year) / Season 01 / Title (Year) S01E01\nMovies: Title (Year) / Title (Year)\nMatching subtitles follow the video. Watch history and saved times stay attached.",
                    "المسلسلات: العنوان (السنة) / Season 01 / العنوان (السنة) S01E01\nالأفلام: العنوان (السنة) / العنوان (السنة)\nتتبع الترجمات المطابقة الفيديو. يبقى سجل المشاهدة والأوقات المحفوظة مرتبطين."
                ),
                13
            )
        );
        // O2: the two naming choices, saved for new downloads too. Changing one only changes the preview below.
        var naming = downloads?.ManagementPreferences.Naming ?? new NamingOptions();
        var episodeTitle = new CheckBox
        {
            Content = T("Add the episode title to file names", "إضافة عنوان الحلقة إلى أسماء الملفات"),
            IsChecked = naming.EpisodeTitle,
            Margin = new(0, 10, 0, 4),
        };
        var movieFolder = new CheckBox
        {
            Content = T("Keep each movie in its own folder", "إبقاء كل فيلم في مجلده الخاص"),
            IsChecked = naming.MovieFolder,
            Margin = new(0, 4, 0, 4),
        };
        panel.Children.Add(episodeTitle);
        panel.Children.Add(movieFolder);
        panel.Children.Add(
            Body(
                T(
                    "Name changes show in the list below. Nothing is renamed until you organize.",
                    "تظهر تغييرات الأسماء في القائمة أدناه. لا يُعاد تسمية شيء حتى تنظّم."
                ),
                12
            )
        );
        NamingOptions Naming() =>
            new() { EpisodeTitle = episodeTitle.IsChecked == true, MovieFolder = movieFolder.IsChecked == true };
        var summary = SheetSection(T("Looking at your files…", "جارٍ فحص ملفاتك…"));
        panel.Children.Add(summary);
        var message = Body(
            T(
                "Original paths are kept for Undo. Nothing is overwritten or deleted.",
                "تُحفظ المسارات الأصلية للتراجع. لا يُستبدل أو يُحذف شيء."
            ),
            12
        );
        panel.Children.Add(message);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 8, 0, 8) };
        panel.Children.Add(tools);
        var groupsPanel = new StackPanel();
        panel.Children.Add(groupsPanel);
        var leftPanel = new StackPanel();
        panel.Children.Add(leftPanel);
        var controls = dialog.FooterButtons;
        var plan = new OrganizationPlan();
        var groups = new List<OrganizationGroup>();
        var unticked = new HashSet<string>();
        var shown = OrganizerPage;
        var leftShown = OrganizerPage;
        Button? apply = null;
        Button? undo = null;

        int SelectedVideos() => groups.Where(g => !unticked.Contains(g.Key)).Sum(g => g.Videos);

        static void SetSectionTitle(StackPanel section, string title) => ((TextBlock)section.Children[0]).Text = title;

        void DrawLeft()
        {
            leftPanel.Children.Clear();
            var left = groups
                .Where(g => unticked.Contains(g.Key))
                .Select(g => g.Name + T(" · not ticked", " · غير محدد"))
                .ToList();
            left.AddRange(plan.Skipped);
            if (left.Count == 0)
                return;
            leftPanel.Children.Add(SheetSection(T("Left as they are", "تُركت كما هي")));
            foreach (var line in left.Take(leftShown))
                leftPanel.Children.Add(Body(line, 12));
            if (left.Count > leftShown)
                leftPanel.Children.Add(ActionButton(T("Show more", "عرض المزيد") + $" ({left.Count - leftShown})", () =>
                    {
                        leftShown += OrganizerPage;
                        DrawLeft();
                    }));
        }

        void Refresh()
        {
            var total = groups.Sum(g => g.Videos);
            SetSectionTitle(
                summary,
                T("{0} of {1} videos ready to organize", "{0} من {1} فيديو جاهز للتنظيم", SelectedVideos(), total)
            );
            if (apply != null)
                apply.IsEnabled = SelectedVideos() > 0;
            DrawLeft();
        }

        void DrawGroups()
        {
            groupsPanel.Children.Clear();
            foreach (var group in groups.Take(shown))
            {
                var box = new CheckBox
                {
                    Content = group.Name + " · " + OrganizerVideos(group.Videos),
                    IsChecked = !unticked.Contains(group.Key),
                    Margin = new(0, 10, 0, 2),
                };
                box.Click += (_, _) =>
                {
                    if (box.IsChecked == true)
                        unticked.Remove(group.Key);
                    else
                        unticked.Add(group.Key);
                    Refresh();
                };
                groupsPanel.Children.Add(box);
                // The files of a group are built when it is opened, so 3,000 titles stay cheap to show.
                var files = new StackPanel();
                var expander = new Expander { Header = T("Show files", "عرض الملفات"), Content = files };
                var built = false;
                expander.Expanded += (_, _) =>
                {
                    if (built)
                        return;
                    built = true;
                    foreach (var move in group.Moves.Where(m => m.Video))
                    {
                        files.Children.Add(OrganizerFileRow(move));
                    }
                };
                groupsPanel.Children.Add(expander);
            }
            if (groups.Count > shown)
                groupsPanel.Children.Add(ActionButton(T("Show more", "عرض المزيد") + $" ({groups.Count - shown})", () =>
                    {
                        shown += OrganizerPage;
                        DrawGroups();
                    }));
        }

        void RefreshUndo() =>
            _ = Task.Run(() => store.OrganizationRuns())
                .ContinueWith(
                    t =>
                    {
                        if (undo != null && t.Status == TaskStatus.RanToCompletion)
                            undo.IsEnabled = t.Result > 0;
                    },
                    TaskScheduler.FromCurrentSynchronizationContext()
                );

        async Task Replan()
        {
            SetSectionTitle(summary, T("Looking at your files…", "جارٍ فحص ملفاتك…"));
            if (apply != null)
                apply.IsEnabled = false;
            var naming = Naming();
            plan = await Task.Run(() => store.PlanOrganization(naming));
            if (!dialog.IsOpen)
                return;
            groups = plan.Groups();
            unticked.Clear();
            shown = leftShown = OrganizerPage;
            DrawGroups();
            Refresh();
            if (groups.Count == 0)
                message.Text = T("Nothing to organize right now.", "لا شيء لتنظيمه الآن.");
            RefreshUndo();
        }

        void ToolButton(string label, Action action)
        {
            var b = ActionButton(label, action);
            b.Margin = new(0, 0, 8, 0);
            tools.Children.Add(b);
        }
        ToolButton(T("Select all", "تحديد الكل"), () =>
        {
            unticked.Clear();
            DrawGroups();
            Refresh();
        });
        ToolButton(T("Select none", "إلغاء تحديد الكل"), () =>
        {
            foreach (var g in groups)
                unticked.Add(g.Key);
            DrawGroups();
            Refresh();
        });

        async Task Run(Func<int> operation)
        {
            if (current != null || scanning || metadataRefreshing)
            {
                message.Text = T("Wait for playback or refresh to finish.", "انتظر انتهاء التشغيل أو التحديث.");
                return;
            }
            controls.IsEnabled = false;
            StopWatchingFolders();
            try
            {
                var count = await Task.Run(operation);
                await ScanLibrary(false);
                message.Text = T(
                    "Finished · {0} videos. History and saved times kept.",
                    "اكتمل · {0} فيديو. بقي السجل والأوقات المحفوظة.",
                    count
                );
            }
            catch (Exception e)
            {
                store.Log(e);
                await ScanLibrary(false);
                message.Text = T(
                    "Stopped safely: {0} · You can undo completed changes.",
                    "توقف بأمان: {0} · يمكنك التراجع عن التغييرات المكتملة.",
                    e.Message
                );
            }
            finally
            {
                controls.IsEnabled = true;
                WatchLibraryFolders();
            }
            // The list was for the files before this run: look again, so the next run can start right away.
            await Replan();
        }
        dialog.Action(T("Archive changes", "أرشفة التغييرات"), () =>
        {
            store.ArchiveOrganization();
            message.Text = T(
                "Undo history archived. Earlier organizing can no longer be undone here.",
                "أُرشف سجل التراجع. لم يعد ممكنًا التراجع هنا عن التنظيم السابق."
            );
            RefreshUndo();
        });
        undo = dialog.Action(T("Undo last organization", "التراجع عن آخر تنظيم"), async () =>
            await Run(() => store.Organize(store.LastOrganization() ?? new(), true))
        );
        undo.IsEnabled = false;
        apply = dialog.Action(
            T("Organize selected", "تنظيم المحدد"),
            async () =>
            {
                var chosen = plan.Without(unticked);
                await Run(() => store.Organize(chosen));
            },
            true
        );
        apply.IsEnabled = false;
        episodeTitle.Click += async (_, _) =>
        {
            downloads?.SetNaming(Naming());
            await Replan();
        };
        movieFolder.Click += async (_, _) =>
        {
            downloads?.SetNaming(Naming());
            await Replan();
        };
        _ = Replan();
    }
}
