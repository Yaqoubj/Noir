using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Noir;

public partial class MainWindow
{
    public static Func<string, IReadOnlyList<string>>? PeopleFor;

    // Reads the cached credits for every title once, off the UI thread (Stats then finds them in memory).
    static void WarmPeople(List<Watched> views)
    {
        if (PeopleFor is not { } people)
            return;
        foreach (var key in views.Select(v => v.Item.GroupKey).Distinct())
            try
            {
                people(key);
            }
            catch { }
    }

    bool GoalPaused => File.Exists(System.IO.Path.Combine(store.DataDirectory, "goal-paused"));

    void PauseGoal(bool paused)
    {
        var file = System.IO.Path.Combine(store.DataDirectory, "goal-paused");
        try
        {
            if (paused)
                File.WriteAllText(file, "1", System.Text.Encoding.UTF8);
            else if (File.Exists(file))
                File.Delete(file);
        }
        catch (IOException error)
        {
            store.Log(error);
        }
        RenderQuiet();
    }

    FrameworkElement HistoryProfileHeader()
    {
#if NOIR_VALIDATE
        QueueStoryValidation();
#endif
        var shell = new Border
        {
            Background = Brush("#141416"),
            CornerRadius = new(16),
            BorderBrush = Brush("#25E4C084"),
            BorderThickness = new(1),
            Padding = new(18, 14, 18, 16),
            Margin = new(0, 16, 0, 0),
        };
        var body = new StackPanel();
        shell.Child = body;
        var head = new DockPanel();
        var hint = Text(
            T("Drag to reorder · right-click to edit", "اسحب للترتيب · انقر بالزر الأيمن للتعديل"),
            10.5,
            "#8F887D"
        );
        hint.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(hint, Dock.Right);
        head.Children.Add(hint);
        head.Children.Add(Text(T("Top four", "أفضل أربعة"), 14, "#E4C084", FontWeights.SemiBold));
        body.Children.Add(head);
        var slots = new UniformGrid
        {
            Columns = 4,
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(0, 12, 0, 0),
        };
        body.Children.Add(slots);
        var favorites = store.State.Favorites.Take(4).ToList();
        for (int n = 0; n < 4; n++)
        {
            var index = n;
            var key = n < favorites.Count ? favorites[n] : "";
            var item = key.Length > 0 ? store.Archive.FirstOrDefault(i => i.GroupKey == key) : null;
            var picture = new Grid { Height = 132, Width = 88 };
            if (item != null)
                picture.Children.Add(HistoryArt(item, 132));
            else
            {
                picture.Background = Brush("#211E1A");
                var plus = IconSet.Draw("plus", 26);
                ((System.Windows.Shapes.Shape)plus).Stroke = Brush("#E4C084");
                plus.HorizontalAlignment = HorizontalAlignment.Center;
                plus.VerticalAlignment = VerticalAlignment.Center;
                picture.Children.Add(plus);
            }
            var panel = new StackPanel { Width = 100, Margin = new(0, 0, 14, 0) };
            var button = new Button
            {
                Content = new Border
                {
                    Child = picture,
                    CornerRadius = new(8),
                    ClipToBounds = true,
                    BorderThickness = new(1),
                    BorderBrush = Brush("#45E4C084"),
                },
                Background = Brush("Transparent"),
                Padding = new(0),
                AllowDrop = true,
                ToolTip = item?.Title ?? T("Pick a top-four title", "اختر لأفضل أربعة"),
            };
            System.Windows.Automation.AutomationProperties.SetName(button, button.ToolTip.ToString());
            panel.Children.Add(button);
            var caption = Text(
                item?.Title ?? T("Pick a top-four title", "اختر لأفضل أربعة"),
                10.5,
                item == null ? "#C5B797" : "#D9D2C6"
            );
            caption.TextWrapping = TextWrapping.NoWrap;
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            caption.Margin = new(0, 6, 0, 0);
            panel.Children.Add(caption);
            slots.Children.Add(panel);
            button.Click += (_, _) =>
            {
                if (item == null)
                    PickFavorite(index);
                else
                    OpenHistoryTitle(store.EpisodesFor(key));
            };
            Point start = default;
            button.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(button);
            button.PreviewMouseMove += (_, e) =>
            {
                if (
                    item != null
                    && e.LeftButton == MouseButtonState.Pressed
                    && (e.GetPosition(button) - start).Length > 8
                )
                    DragDrop.DoDragDrop(button, new DataObject("NoirFavorite", key), DragDropEffects.Move);
            };
            button.Drop += (_, e) =>
            {
                if (e.Data.GetData("NoirFavorite") is string source && favorites.Contains(source))
                {
                    var order = favorites.ToList();
                    order.Remove(source);
                    order.Insert(Math.Min(index, order.Count), source);
                    store.SetFavorites(order);
                    e.Handled = true;
                    RenderQuiet();
                }
            };
            var menu = new ContextMenu();
            var replace = new MenuItem { Header = T("Pick a top-four title", "اختر لأفضل أربعة") };
            replace.Click += (_, _) => PickFavorite(index);
            menu.Items.Add(replace);
            if (item != null)
            {
                var remove = new MenuItem { Header = T("Remove from top four", "إزالة من أفضل أربعة") };
                remove.Click += (_, _) =>
                {
                    store.SetFavorites(favorites.Where(f => f != key));
                    RenderQuiet();
                };
                menu.Items.Add(remove);
            }
            button.ContextMenu = menu;
            Motion.ButtonFeedback(button);
        }
        Motion.EnterOnce(shell);
        return shell;
    }

    void PickFavorite(int slot)
    {
        var dialog = OpenSheet(
            T("Pick a top-four title", "اختر لأفضل أربعة"),
            T("Search your library and viewing history", "ابحث في مكتبتك وسجل مشاهداتك"),
            SheetKind.Side,
            480
        );
        var search = new TextBox
        {
            Margin = new(0, 4, 0, 12),
            Padding = new(10, 8, 10, 8),
            Background = Brush("#222226"),
            Foreground = Brush("#F4F1EB"),
            FontSize = 13,
        };
        System.Windows.Automation.AutomationProperties.SetName(search, T("Search favorites", "ابحث عن عمل مفضل"));
        dialog.Add(search);
        var rows = new StackPanel();
        dialog.Add(rows);
        var library = store.Archive.DistinctBy(i => i.GroupKey).OrderBy(i => i.Title).ToArray();
        void Draw()
        {
            rows.Children.Clear();
            foreach (
                var item in library
                    .Where(i =>
                        search.Text.Trim().Length == 0
                        || i.Title.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                    )
                    .Take(36)
            )
            {
                var row = new DockPanel();
                var art = new Border
                {
                    Width = 38,
                    Height = 56,
                    CornerRadius = new(5),
                    ClipToBounds = true,
                    Child = HistoryArt(item, 56),
                    Margin = new(0, 0, 12, 0),
                };
                DockPanel.SetDock(art, Dock.Left);
                row.Children.Add(art);
                var name = Text(item.Title, 14, weight: FontWeights.SemiBold);
                name.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(name);
                var pick = new Button
                {
                    Content = row,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Background = Brush("#18181B"),
                    Padding = new(10),
                    Margin = new(0, 0, 0, 6),
                };
                pick.Click += (_, _) =>
                {
                    var chosen = store.State.Favorites.Take(4).ToList();
                    var existing = chosen.IndexOf(item.GroupKey);
                    if (existing >= 0)
                    {
                        if (slot < chosen.Count)
                            (chosen[slot], chosen[existing]) = (chosen[existing], chosen[slot]);
                        else
                        {
                            chosen.RemoveAt(existing);
                            chosen.Insert(Math.Min(slot, chosen.Count), item.GroupKey);
                        }
                    }
                    else if (slot < chosen.Count)
                        chosen[slot] = item.GroupKey;
                    else
                        chosen.Insert(Math.Min(slot, chosen.Count), item.GroupKey);
                    store.SetFavorites(chosen);
                    dialog.Close(true);
                    RenderQuiet();
                };
                rows.Children.Add(pick);
            }
        }
        search.TextChanged += (_, _) => Draw();
        Draw();
        dialog.Action(T("Cancel", "إلغاء"), () => dialog.Close(false));
        search.Focus();
    }

    FrameworkElement HalfRating(int rating10, double size = 16)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int n = 0; n < 5; n++)
        {
            var star = new Grid { Width = size + 2, Height = size * 1.5 };
            star.Children.Add(
                new TextBlock
                {
                    Text = "★",
                    FontSize = size,
                    Foreground = Brush("#514C44"),
                }
            );
            var fill = new TextBlock
            {
                Text = "★",
                FontSize = size,
                Foreground = Brush("#E4C084"),
                Clip = new RectangleGeometry(
                    new Rect(0, 0, (size + 2) * Math.Clamp((rating10 - n * 2) / 2.0, 0, 1), size * 1.5)
                ),
            };
            star.Children.Add(fill);
            row.Children.Add(star);
        }
        return row;
    }

    FrameworkElement HalfRatePicker(VideoItem item, double size = 16)
    {
        var value = store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Rating10 ?? 0;
        var button = new Button
        {
            Content = HalfRating(value, size),
            Background = Brush("Transparent"),
            Padding = new(3, 0, 3, 0),
            ToolTip = T("Rate with half stars · optional", "قيّم بنصف نجمة · اختياري"),
        };
        System.Windows.Automation.AutomationProperties.SetName(button, T("Your rating · optional", "تقييمك · اختياري"));
        button.PreviewMouseLeftButtonUp += (_, e) =>
        {
            var x = e.GetPosition(button).X - 3;
            var score = Math.Clamp((int)Math.Ceiling(x / (size + 2) * 2), 1, 10);
            store.SetReview10(
                item,
                value == score ? 0 : score,
                store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Note ?? ""
            );
            e.Handled = true;
            RenderQuiet();
        };
        var menu = new ContextMenu();
        for (int n = 0; n <= 10; n++)
        {
            var score = n;
            var choice = new MenuItem
            {
                Header = n == 0 ? T("Clear rating", "مسح التقييم") : T("{0:0.#} stars", "{0:0.#} نجمة", n / 2.0),
                IsCheckable = true,
                IsChecked = value == n,
            };
            choice.Click += (_, _) =>
            {
                store.SetReview10(item, score, store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Note ?? "");
                RenderQuiet();
            };
            menu.Items.Add(choice);
        }
        menu.Items.Add(new Separator());
        var edit = new MenuItem { Header = T("Rating & note…", "التقييم والملاحظة…") };
        edit.Click += (_, _) => OpenPersonalRating(item);
        menu.Items.Add(edit);
        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        };
        return button;
    }

    FrameworkElement ReactionRow(VideoItem item)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(Text(T("How did it feel?", "ما شعورك؟"), 11, "#C5B797"));
        foreach (
            var mood in new[]
            {
                ("loved", "[[heart-filled]]", T("Loved", "أحببته")),
                ("funny", "[[smile]]", T("Funny", "مضحك")),
                ("wow", "[[sparkle]]", T("Wow", "مذهل")),
                ("sad", "[[frown]]", T("Sad", "حزين")),
                ("meh", "[[meh]]", T("Meh", "عادي")),
            }
        )
        {
            var button = ActionButton(mood.Item2, () =>
            {
                store.SetReaction(item.Id, mood.Item1);
                row.Visibility = Visibility.Collapsed;
            });
            button.ToolTip = mood.Item3;
            System.Windows.Automation.AutomationProperties.SetName(button, mood.Item3);
            button.Padding = new(8, 3, 8, 3);
            button.Margin = new(4, 0, 0, 0);
            row.Children.Add(button);
            Motion.ButtonFeedback(button);
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            row.Visibility = Visibility.Collapsed;
        };
        row.Loaded += (_, _) => timer.Start();
        row.Unloaded += (_, _) => timer.Stop();
        Motion.FadeIn(row);
        return row;
    }

    FrameworkElement SeasonStrip(List<VideoItem> group)
    {
        var body = new StackPanel { Margin = new(0, 14, 0, 10) };
        if (group.Count == 0)
            return body;
        var key = group[0].GroupKey;
        if (
            !store.State.SeasonRatings.Keys.Any(k => k.StartsWith(key + ":", StringComparison.Ordinal))
            && !group.Any(i => store.State.Reactions.GetValueOrDefault(i.Id, "").Length > 0)
        )
            return new StackPanel();
        body.Children.Add(Text(T("Seasons & mood map", "المواسم وخريطة المشاعر"), 16, "#E4C084", FontWeights.SemiBold));
        var seasons = new WrapPanel { Margin = new(0, 10, 0, 0) };
        body.Children.Add(seasons);
        foreach (var season in group.Where(i => !i.IsMovie && i.Season > 0).GroupBy(i => i.Season).OrderBy(g => g.Key))
        {
            var number = season.Key;
            var value = store.State.SeasonRatings.GetValueOrDefault(key + ":" + number);
            var stack = new StackPanel();
            stack.Children.Add(Text(T("Season {0}", "الموسم {0}", number), 12, "#D8C8AE"));
            stack.Children.Add(HalfRating(value, 15));
            var button = new Button
            {
                Content = stack,
                Background = Brush("#211D18"),
                Padding = new(14, 9, 14, 9),
                Margin = new(0, 0, 8, 8),
                ToolTip = T("Rate season · optional", "قيّم الموسم · اختياري"),
            };
            button.Click += (_, _) =>
            {
                var menu = new ContextMenu();
                for (int n = 0; n <= 10; n++)
                {
                    var score = n;
                    var option = new MenuItem
                    {
                        Header =
                            n == 0 ? T("Clear rating", "مسح التقييم") : T("{0:0.#} stars", "{0:0.#} نجمة", n / 2.0),
                        IsCheckable = true,
                        IsChecked = value == n,
                    };
                    option.Click += (_, _) =>
                    {
                        store.SetSeasonRating(key, number, score);
                        RenderQuiet();
                    };
                    menu.Items.Add(option);
                }
                menu.PlacementTarget = button;
                menu.IsOpen = true;
            };
            seasons.Children.Add(button);
        }
        var moods = group
            .Select(i => store.State.Reactions.GetValueOrDefault(i.Id, ""))
            .Where(r => r.Length > 0)
            .GroupBy(r => r)
            .OrderByDescending(g => g.Count())
            .ToList();
        var map = new WrapPanel();
        foreach (var mood in moods)
        {
            var label = mood.Key switch
            {
                "loved" => T("[[heart-filled]] Loved", "[[heart-filled]] أحببته"),
                "funny" => T("☺ Funny", "☺ مضحك"),
                "wow" => T("✦ Wow", "✦ مذهل"),
                "sad" => T("☂ Sad", "☂ حزين"),
                _ => T("− Meh", "− عادي"),
            };
            map.Children.Add(
                new Border
                {
                    Background = Brush("#1AE4C084"),
                    CornerRadius = new(7),
                    Padding = new(10, 5, 10, 5),
                    Margin = new(0, 0, 7, 6),
                    Child = Text(label + " · " + mood.Count(), 11, "#D8C8AE"),
                }
            );
        }
        if (moods.Count == 0)
            map.Children.Add(
                Text(T("Your episode reactions appear here", "ستظهر مشاعرك تجاه الحلقات هنا"), 11, "#938A7C")
            );
        body.Children.Add(map);
        return body;
    }

    void RenderProfileStats(List<Watched> views)
    {
        var decades = views
            .Where(v => v.Item.Year > 0)
            .GroupBy(v => v.Item.Year / 10 * 10)
            .OrderBy(g => g.Key)
            .ToList();
        if (decades.Count > 0)
        {
            ContentPanel.Children.Add(Heading(T("By decade", "حسب العقد")));
            var max = decades.Max(g => g.Sum(v => v.Seconds));
            foreach (var decade in decades)
                ContentPanel.Children.Add(
                    MeterRow(
                        T("{0}s", "عقد {0}", decade.Key),
                        decade.Sum(v => v.Seconds) / Math.Max(1, max),
                        Duration(decade.Sum(v => v.Seconds), true)
                    )
                );
        }
        if (PeopleFor == null)
            return;
        var people = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var title in views.GroupBy(v => v.Item.GroupKey))
        {
            IReadOnlyList<string>? names;
            try
            {
                names = PeopleFor(title.Key);
            }
            catch
            {
                continue;
            }
            if (names == null)
                continue;
            foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
                people[name] = people.GetValueOrDefault(name) + title.Sum(v => v.Seconds);
        }
        if (people.Count == 0)
            return;
        ContentPanel.Children.Add(Heading(T("People in your stories", "أشخاص في حكاياتك")));
        var top = people.OrderByDescending(p => p.Value).Take(8).ToList();
        foreach (var person in top)
            ContentPanel.Children.Add(
                MeterRow(person.Key, person.Value / Math.Max(1, top[0].Value), Duration(person.Value, true))
            );
    }
}
