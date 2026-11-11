using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Noir;

public partial class MainWindow
{
    string episodeFilter = "All episodes",
        genreFilter = "All genres";

    static bool CanPlay(VideoItem item) => item.Available && System.IO.File.Exists(item.Path);

    DateTimeOffset HistoryDate(VideoItem item) => store.LastActivity(item);

    VideoItem ChoiceFor(List<VideoItem> group)
    {
        var playable = group.Where(CanPlay).ToList();
        return playable.Count > 0 ? store.DefaultFor(playable) : group.OrderByDescending(HistoryDate).First();
    }

    string EpisodeCountText(List<VideoItem> group, bool withWatched = true)
    {
        var regular = group.Count(i => i.Season > 0);
        var specials = group.Count(i => i.Season == 0);
        var total =
            regular > 0
                ? $"{regular} episodes" + (specials > 0 ? $" + {specials} special{(specials == 1 ? "" : "s")}" : "")
                : $"{specials} specials";
        return total
            + $" · {group.Count(CanPlay)} on PC"
            + (withWatched ? $" · {group.Count(store.EverWatched)} watched" : "");
    }

    FrameworkElement PlayCircle() =>
        new Border
        {
            Width = 54,
            Height = 54,
            CornerRadius = new(27),
            Background = Brush("#E6101012"),
            BorderBrush = Brush("#D9FFFFFF"),
            BorderThickness = new(1.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 0,0 L 0,22 L 18,11 Z"),
                Fill = Brush("#FFFFFF"),
                Width = 18,
                Height = 22,
                Margin = new(5, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

    Button CardMenu(VideoItem item)
    {
        var button = new Button
        {
            Content = "[[more]]",
            Width = Density.MoreWidth,
            Height = Density.MoreHeight,
            FontSize = Density.MoreFont,
            Padding = new(0),
            Background = Brush("#D9101012"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new(8),
            ToolTip = "Options for " + item.Label,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, "Options for " + item.Label);
        button.Click += (_, e) =>
        {
            e.Handled = true;
            var menu = ItemMenu(item);
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        // Shown only while the pointer is over the card (or it has focus), so rows stay clean.
        button.Opacity = 0;
        button.Loaded += (_, _) =>
        {
            if (VisualTreeHelper.GetParent(button) is UIElement host)
            {
                host.MouseEnter += (_, _) => button.Opacity = 1;
                host.MouseLeave += (_, _) =>
                {
                    if (!button.IsKeyboardFocused)
                        button.Opacity = 0;
                };
            }
        };
        button.GotKeyboardFocus += (_, _) => button.Opacity = 1;
        button.LostKeyboardFocus += (_, _) => button.Opacity = 0;
        return button;
    }

    // U31 (P4): "See all" is the header link only; the trailing "See all" tile a rail adds beside it is dropped.
    FrameworkElement ContinueShelf(
        IEnumerable<UpNext> videos,
        string label = "Continue watching",
        string subtitle = "Pick up from your saved time, or play the next episode."
    )
    {
        if (page != "Home")
            return Rail(
                label,
                subtitle,
                videos
                    .Select(entry =>
                        HoverCard(Lift(ContinueCard(entry.Item, entry)), store.EpisodesFor(entry.Item.GroupKey))
                    )
                    .ToList()
            );
        var layout = HomeLayout.Load(store.DataDirectory);
        homePosterTitles = layout.ShowPosterTitles;
        return Rail(
            label,
            subtitle,
            videos.Select(entry => HomeContinueCard(entry, layout.ContinueStyle != "Poster")).ToList(),
            () =>
            {
                trackerTab = "Up next";
                Navigate("History");
            }
        );
    }

    FrameworkElement HomeContinueCard(UpNext entry, bool wide)
    {
        var item = entry.Item;
        var group = store.EpisodesFor(item.GroupKey);
        var height = wide ? Density.ContinueHeight : Density.PosterHeight;
        var panel = new StackPanel
        {
            Width = wide ? Density.ContinueWidth : Density.PosterWidth,
            Margin = new(0, 0, Density.CardGap, 12),
            Tag = wide ? "HomeWideContinue" : "HomePosterContinue",
        };
        var progress = store.GetProgress(item);
        var picture = new Grid { Height = height, ClipToBounds = true };
        var job = ActiveDownload(item);
        void Open()
        {
            if (entry.Kind == UpNextKind.Upcoming)
                OpenDetails(group);
            else if (CanPlay(item))
                _ = PlayVideo(item);
            else if (job != null)
                Navigate("Downloads");
            else if (CanStream)
                StreamItem(item);
            else
                FindDownloads(item);
        }
        var image =
            wide
            && File.Exists(item.EpisodeImage)
            && (!store.State.HideSpoilers || entry.Kind == UpNextKind.Resume || store.EverWatched(item))
                ? item.EpisodeImage
            : wide && File.Exists(item.Backdrop) ? item.Backdrop
            : item.Poster;
        var play = ActionButton("", Open);
        play.Content = Art(image, item.Title, height, !wide);
        play.Padding = new(0);
        play.HorizontalAlignment = HorizontalAlignment.Stretch;
        System.Windows.Automation.AutomationProperties.SetName(play, T("Continue {0}", "متابعة {0}", item.Label));
        picture.Children.Add(play);
        picture.Children.Add(
            new Border
            {
                IsHitTestVisible = false,
                Background = new LinearGradientBrush(Colors.Transparent, Color.FromArgb(235, 11, 11, 13), 90),
            }
        );
        var caption = new StackPanel
        {
            Margin = new(14, 0, 14, 14),
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
        };
        if (wide)
            caption.Children.Add(HomeTitleMark(item, 48, 250, 19));
        else if (homePosterTitles)
            caption.Children.Add(Text(item.Title, 15, weight: FontWeights.SemiBold));
        var episode = item.IsMovie ? "" : T("S{0} E{1}", "م{0} ح{1}", item.Season, item.Episode);
        var remaining =
            progress is { Completed: false, Position: > 0, Duration: > 0 }
                ? Loc.MinutesLeft(Math.Max(1, (int)Math.Ceiling((progress.Duration - progress.Position) / 60)))
            : entry.Kind == UpNextKind.Upcoming ? T("Coming soon", "قريبًا")
            : entry.Kind == UpNextKind.NotOnPc
                ? job != null ? T("Downloading…", "جارٍ التنزيل…")
                    : T("Not on this PC", "ليس على هذا الكمبيوتر")
            : T("Ready to play", "جاهز للمشاهدة");
        var line = Text(string.Join(" · ", new[] { episode, remaining }.Where(x => x.Length > 0)), 12, "#D9D4CC");
        caption.Children.Add(line);
        picture.Children.Add(caption);
        picture.Children.Add(CardMenu(item));
        var remove = ActionButton("[[close]]", () =>
        {
            store.State.HiddenContinue.Add(item.GroupKey);
            store.Save();
            Render();
            Undoable(T("Hidden from Continue watching", "أُخفي من متابعة المشاهدة"), () =>
            {
                store.State.HiddenContinue.Remove(item.GroupKey);
                store.Save();
                Render();
            });
        });
        remove.HorizontalAlignment = HorizontalAlignment.Left;
        remove.VerticalAlignment = VerticalAlignment.Top;
        remove.Margin = new(8);
        remove.Width = 30;
        remove.Height = 30;
        remove.Padding = new(0);
        remove.Opacity = 0;
        System.Windows.Automation.AutomationProperties.SetName(
            remove,
            T("Hide {0} from Continue watching", "إخفاء {0} من متابعة المشاهدة", item.Title)
        );
        picture.Children.Add(remove);
        RevealOnFocus(panel, remove);
        if (progress is { Completed: false, Position: > 0, Duration: > 0 })
            picture.Children.Add(
                new ProgressBar
                {
                    Maximum = progress.Duration,
                    Value = progress.Position,
                    Height = 3,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Foreground = Brush("#E4C084"),
                    Background = Brush("#55000000"),
                    IsHitTestVisible = false,
                    BorderThickness = new(0),
                }
            );
        var edge = new Border
        {
            CornerRadius = new(12),
            ClipToBounds = true,
            Child = picture,
            BorderBrush = Brush("Transparent"),
            BorderThickness = new(1.5),
        };
        panel.Children.Add(edge);
        LazyMenu(panel, item);
        return FollowHomeHero(Lift(panel, edge), () => item);
    }

    void RenderEpisodeAvailability(VideoItem item)
    {
        var schedule = store.State.EpisodeSchedules.GetValueOrDefault(item.GroupKey);
        if (schedule == null)
            return;
        var now = DateTimeOffset.UtcNow;
        var upcoming = schedule.Where(e => e.AirsAt > now).OrderBy(e => e.AirsAt).FirstOrDefault();
        if (upcoming != null)
            ContentPanel.Children.Add(
                Body(
                    $"Next airing · S{upcoming.Season:00} E{upcoming.Episode:00} · {upcoming.AirsAt!.Value.ToLocalTime():ddd, d MMM yyyy}",
                    13
                )
            );
        RenderMissingRail(item);
    }

    NoirSheet DiaryWindow(VideoItem item)
    {
        var dialog = OpenSheet(
            T("Viewing diary · ", "يوميات المشاهدة · ") + item.Title,
            item.Label,
            SheetKind.Side,
            620
        );
        var panel = new StackPanel();
        dialog.Add(panel);
        var personal = store.State.PersonalReviews.GetValueOrDefault(item.Id) ?? new();
        panel.Children.Add(
            SheetSection(
                T("My rating & note", "تقييمي وملاحظتي"),
                T("Private, saved on this PC.", "خاص، محفوظ على هذا الكمبيوتر.")
            )
        );
        var rating = personal.Rating10;
        var stars = new WrapPanel();
        var picker = new Button
        {
            Background = Brush("Transparent"),
            Padding = new(3, 0, 3, 0),
            ToolTip = T("Rate with half stars · optional", "قيّم بنصف نجمة · اختياري"),
        };
        System.Windows.Automation.AutomationProperties.SetName(picker, T("Your rating · optional", "تقييمك · اختياري"));
        void DrawStars() => picker.Content = HalfRating(rating, 24);
        picker.PreviewMouseLeftButtonUp += (_, e) =>
        {
            var score = Math.Clamp((int)Math.Ceiling((e.GetPosition(picker).X - 3) / 26 * 2), 1, 10);
            rating = rating == score ? 0 : score;
            DrawStars();
            e.Handled = true;
        };
        picker.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            for (int n = 0; n <= 10; n++)
            {
                var score = n;
                var choice = new MenuItem
                {
                    Header = n == 0 ? T("Clear rating", "مسح التقييم") : T("{0:0.#} stars", "{0:0.#} نجمة", n / 2.0),
                    IsCheckable = true,
                    IsChecked = rating == n,
                };
                choice.Click += (_, _) =>
                {
                    rating = score;
                    DrawStars();
                };
                menu.Items.Add(choice);
            }
            menu.PlacementTarget = picker;
            menu.IsOpen = true;
        };
        DrawStars();
        stars.Children.Add(picker);
        stars.Children.Add(ActionButton(T("Clear rating", "مسح التقييم"), () =>
            {
                rating = 0;
                DrawStars();
            }));
        panel.Children.Add(stars);
        var note = new TextBox
        {
            Text = personal.Note,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 84,
            MaxHeight = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new(0, 10, 0, 6),
            MaxLength = 4000,
        };
        System.Windows.Automation.AutomationProperties.SetName(note, "My private note");
        panel.Children.Add(note);
        var saved = Body("", 12);
        saved.Margin = new(0);
        panel.Children.Add(saved);
        dialog.Action(T("Close", "إغلاق"), () => dialog.Close(false));
        dialog.Action(
            T("Save rating & note", "حفظ التقييم والملاحظة"),
            () =>
            {
                store.State.PersonalReviews[item.Id] = new() { Rating10 = rating, Note = note.Text };
                store.TouchMobileField("review:" + item.Id);
                store.Save();
                saved.Text = T("Saved on this PC.", "حُفظ على هذا الكمبيوتر.");
            },
            true
        );
        panel.Children.Add(
            SheetSection(
                T("Viewing dates", "تواريخ المشاهدة"),
                T(
                    "Earlier dates stay here when you replay or remove the file.",
                    "تبقى التواريخ السابقة هنا عند إعادة المشاهدة أو حذف الملف."
                )
            )
        );
        var records = store
            .Archive.Where(x => x.GroupKey == item.GroupKey)
            .SelectMany(x => store.ViewingHistory(x).Select(v => (Item: x, View: v)))
            .OrderByDescending(x => x.View.WatchedAt)
            .ToList();
        if (records.Count == 0)
            panel.Children.Add(Body("No completed viewings yet. Your saved position is kept separately."));
        foreach (var record in records)
        {
            var label =
                (record.Item.IsMovie ? record.Item.Title : record.Item.EpisodeLabel)
                + " · "
                + record.View.WatchedAt.ToLocalTime().ToString("d MMM yyyy, HH:mm")
                + " · "
                + record.View.Source;
            var row = ActionButton(label, () =>
            {
                dialog.Close(true);
                OpenEpisodeInfo(record.Item);
            });
            row.Background = Brush("#171719");
            row.Margin = new(0, 0, 0, 6);
            row.HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.Children.Add(row);
        }
        return dialog;
    }

    void OpenDiary(VideoItem item) => DiaryWindow(item);
}
