using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Noir;

public partial class MainWindow
{
    bool HistoryLiked(VideoItem item) =>
        store.State.Likes.Contains(item.GroupKey)
        || store.EpisodesFor(item.GroupKey).Any(i => store.State.Likes.Contains(i.Id));

    string HistoryFavouriteLabel(VideoItem item) =>
        HistoryLiked(item)
            ? T("[[heart-filled]] Remove from favourites", "[[heart-filled]] إزالة من المفضلة")
            : T("[[heart]] Favourite", "[[heart]] أضف إلى المفضلة");

    void ToggleHistoryFavourite(VideoItem item)
    {
        var keys = store.EpisodesFor(item.GroupKey).Select(i => i.Id).Append(item.GroupKey).Distinct().ToList();
        var before = keys.Where(store.State.Likes.Contains).ToHashSet();
        var liked = HistoryLiked(item);
        if (liked)
        {
            foreach (var key in before)
                store.SetLike(key, false);
        }
        else
            store.SetLike(item.GroupKey, true);
        RenderQuiet();
        Undoable(
            liked
                ? T("Removed {0} from favourites", "أُزيل {0} من المفضلة", item.Title)
                : T("Added {0} to favourites", "أُضيف {0} إلى المفضلة", item.Title),
            () =>
            {
                foreach (var key in keys)
                    if (store.State.Likes.Contains(key) != before.Contains(key))
                        store.SetLike(key, before.Contains(key));
                RenderQuiet();
            }
        );
    }

    void StopShowing(VideoItem item)
    {
        var previous = store.State.TitleStatuses.GetValueOrDefault(item.GroupKey, "Automatic");
        store.SetStatus(item, "Dropped");
        RenderQuiet();
        Undoable(T("Stopped showing {0}", "توقف إظهار {0}", item.Title), () =>
        {
            store.SetStatus(item, previous);
            RenderQuiet();
        });
    }

    void BringBack(VideoItem item)
    {
        var previous = store.State.TitleStatuses.GetValueOrDefault(item.GroupKey, "Automatic");
        store.SetStatus(item, "Automatic");
        RenderQuiet();
        Undoable(T("Brought back {0}", "أُعيد إظهار {0}", item.Title), () =>
        {
            store.SetStatus(item, previous);
            RenderQuiet();
        });
    }

    void AddHistoryTitleActions(ContextMenu menu, VideoItem item)
    {
        var favourite = new MenuItem { Header = HistoryFavouriteLabel(item) };
        System.Windows.Automation.AutomationProperties.SetName(favourite, IconSet.Plain(HistoryFavouriteLabel(item)));
        favourite.Click += (_, _) => ToggleHistoryFavourite(item);
        menu.Items.Add(favourite);
        menu.Items.Add(new Separator());
    }

    FrameworkElement HistoryPillTabs()
    {
        var row = new WrapPanel { Margin = new(0, 16, 0, 8) };
        var buttons = new List<Button>();
        foreach (var tab in TrackerOnly ? new[] { "Shows", "Movies", "Upcoming" } : TrackerTabs)
        {
            var key = tab;
            var label = tab == "Up next" ? T("Up next", "التالي") : T(tab);
            var button = ActionButton(
                label,
                () =>
                {
                    if (key == "Discover")
                    {
                        OpenLog(null);
                        return;
                    }
                    trackerTab = key;
                    trackerLimit = 60;
                    Render();
                    ContentScroll.ScrollToTop();
                },
                trackerTab == key
            );
            button.Style = (Style)FindResource("Pill");
            button.Background = Brush(trackerTab == key ? "#E4C084" : "#222225");
            button.Foreground = Brush(trackerTab == key ? "#16120C" : "#C7C2BC");
            button.Padding = new(16, 8, 16, 8);
            button.FontSize = 13;
            button.Margin = new(0, 0, 8, 6);
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            row.Children.Add(button);
            buttons.Add(button);
            button.PreviewKeyDown += (_, e) =>
            {
                var index = buttons.IndexOf(button);
                var step =
                    e.Key == Key.Right ? (Loc.Arabic ? -1 : 1)
                    : e.Key == Key.Left ? (Loc.Arabic ? 1 : -1)
                    : 0;
                if (step != 0)
                {
                    buttons[(index + step + buttons.Count) % buttons.Count].Focus();
                    e.Handled = true;
                }
            };
        }
        return row;
    }

    List<Watched> RecentHistoryViewings() =>
        AllViewings().Where(v => v.Precise && TrackerFits(v.Item)).DistinctBy(v => v.Item.GroupKey).Take(14).ToList();

    string RecentHistoryWhen(DateTimeOffset at)
    {
        var days = Math.Max(0, (DateTime.Today - at.ToLocalTime().Date).Days);
        return days == 0 ? T("Today", "اليوم")
            : days == 1 ? T("Yesterday", "أمس")
            : T("{0} days ago", "قبل {0} أيام", days);
    }

    void RenderRecentlyWatched()
    {
        var recent = RecentHistoryViewings();
        if (recent.Count == 0)
            return;
        var rail = Rail(
            T("Recently watched", "شاهدت مؤخراً"),
            T(
                "Your last stories · react, rate or leave a note whenever you like.",
                "آخر حكاياتك · تفاعل أو قيّم أو اكتب ملاحظة وقتما تشاء."
            ),
            recent.Select((v, n) => RecentHistoryCard(v, n == 0)).ToList()
        );
        rail.Tag = "HistoryRecentlyWatched";
        ContentPanel.Children.Add(rail);
    }

    FrameworkElement RecentHistoryCard(Watched view, bool newest)
    {
        var item = view.Item;
        var panel = new StackPanel
        {
            Tag = item.Id,
            Width = newest ? 420 : 400,
            Margin = new(0, 0, 18, 8),
        };
        var picture = new Grid { Height = 168, ClipToBounds = true };
        var art =
            System.IO.File.Exists(item.EpisodeImage) ? item.EpisodeImage
            : System.IO.File.Exists(item.Backdrop) ? item.Backdrop
            : item.Poster;
        var open = new Button
        {
            Content = Art(art, item.Title, 168),
            Padding = new(0),
            Background = Brush("Transparent"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = T("Open {0}", "افتح {0}", item.Title),
        };
        System.Windows.Automation.AutomationProperties.SetName(open, open.ToolTip.ToString());
        open.Click += (_, _) => OpenHistoryTitle(store.EpisodesFor(item.GroupKey));
        picture.Children.Add(open);
        var more = MoreButton(item);
        more.HorizontalAlignment = HorizontalAlignment.Right;
        more.VerticalAlignment = VerticalAlignment.Top;
        more.Margin = new(8);
        picture.Children.Add(more);
        panel.Children.Add(
            new Border
            {
                CornerRadius = new(12),
                ClipToBounds = true,
                Child = picture,
            }
        );
        var title = Text(item.Title, 16, "#F4F1EB", FontWeights.SemiBold);
        title.Margin = new(0, 9, 0, 3);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.TextWrapping = TextWrapping.NoWrap;
        panel.Children.Add(title);
        panel.Children.Add(
            Text(
                (item.IsMovie ? T("Movie", "فيلم") : T("S{0} E{1}", "م{0} ح{1}", item.Season, item.Episode))
                    + "  ·  "
                    + RecentHistoryWhen(view.At),
                12,
                "#9F9A93"
            )
        );
        var rating = store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Rating10 ?? 0;
        if (rating > 0)
            panel.Children.Add(HalfRating(rating, 14));
        var moment = JustWatchedPanel(item, true);
        moment.Margin = new(0, 12, 0, 0);
        panel.Children.Add(moment);
        return panel;
    }

    void RenderHistoryFavourites()
    {
        var titles = store
            .Archive.DistinctBy(i => i.GroupKey)
            .Where(HistoryLiked)
            .Where(TrackerFits)
            .OrderBy(i =>
            {
                var n = store.State.Favorites.IndexOf(i.GroupKey);
                return n < 0 ? int.MaxValue : n;
            })
            .ThenBy(i => i.Title)
            .ToList();
        FrameworkElement shelf;
        if (titles.Count == 0)
        {
            shelf = new StackPanel();
        }
        else
            shelf = Rail(
                T("Favourites", "المفضلة"),
                T("The stories you love, always close.", "الحكايات التي تحبها، دائماً قريبة."),
                titles
                    .Select(item =>
                    {
                        var panel = new StackPanel
                        {
                            Tag = item.GroupKey,
                            Width = 160,
                            Margin = new(0, 0, 18, 8),
                        };
                        var picture = new Grid { Height = 240 };
                        var open = new Button
                        {
                            Content = HistoryArt(item, 240),
                            Padding = new(0),
                            Background = Brush("Transparent"),
                            ToolTip = item.Title,
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        };
                        System.Windows.Automation.AutomationProperties.SetName(open, item.Title);
                        open.Click += (_, _) => OpenHistoryTitle(store.EpisodesFor(item.GroupKey));
                        picture.Children.Add(open);
                        var more = MoreButton(item);
                        more.HorizontalAlignment = HorizontalAlignment.Right;
                        more.VerticalAlignment = VerticalAlignment.Top;
                        more.Margin = new(6);
                        picture.Children.Add(more);
                        if (store.State.Favorites.Contains(item.GroupKey))
                            picture.Children.Add(
                                new Border
                                {
                                    IsHitTestVisible = false,
                                    CornerRadius = new(5),
                                    Background = Brush("#E4C084"),
                                    Padding = new(7, 3, 7, 3),
                                    Margin = new(8),
                                    HorizontalAlignment = HorizontalAlignment.Left,
                                    VerticalAlignment = VerticalAlignment.Bottom,
                                    Child = Text(T("TOP 4", "أفضل 4"), 10, "#16120C", FontWeights.Bold),
                                }
                            );
                        panel.Children.Add(
                            new Border
                            {
                                CornerRadius = new(10),
                                ClipToBounds = true,
                                Child = picture,
                            }
                        );
                        var caption = Text(item.Title, 13);
                        caption.Margin = new(0, 8, 0, 0);
                        caption.TextWrapping = TextWrapping.NoWrap;
                        caption.TextTrimming = TextTrimming.CharacterEllipsis;
                        panel.Children.Add(caption);
                        return (FrameworkElement)panel;
                    })
                    .ToList()
            );
        shelf.Tag = "HistoryFavourites";
        ContentPanel.Children.Add(shelf);
    }
}
