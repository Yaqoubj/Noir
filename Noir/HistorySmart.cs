using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Noir;

// The tracker's judgement: checking off S2E8 asks about the episodes before it, bulk marks ask
// when they were watched (never pretending it was just now), un-checking asks about later ones,
// and finishing a season or show offers a rating.
public partial class MainWindow
{
    static readonly string[] WhenChoices =
    [
        "Just now",
        "When they aired",
        "Pick a date",
        "Around a year",
        "Over a period",
        "Don't remember",
    ];
    Popup? smartPopup;
    FrameworkElement? smartInvoker;

    StackPanel SmartPopup(Action? dismissed = null) =>
        OpenSmartPopup(InputManager.Current.MostRecentInputDevice is KeyboardDevice, dismissed);

    StackPanel OpenSmartPopup(bool keyboard, Action? dismissed = null)
    {
        var invoker = smartInvoker ?? Keyboard.FocusedElement as FrameworkElement;
        if (smartPopup != null)
            smartPopup.IsOpen = false;
        smartInvoker = invoker;
        var focusIndex =
            invoker == null
                ? -1
                : ViewElements(ContentPanel).Where(e => e.Focusable && e.IsEnabled).ToList().IndexOf(invoker);
        var body = new StackPanel { Margin = new(16, 14, 16, 12), MaxWidth = 420 };
        var chosen = false;
        KeyboardNavigation.SetTabNavigation(body, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(body, KeyboardNavigationMode.Cycle);
        var popup = new Popup
        {
            // U90: a popup is its own window and does not inherit the app's flow direction.
            FlowDirection = LibraryRoot.FlowDirection,
            Placement = keyboard && invoker != null ? PlacementMode.Bottom : PlacementMode.MousePoint,
            PlacementTarget = keyboard ? invoker : null,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = new Border
            {
                Background = Brush("#F7141416"),
                BorderBrush = Brush("#2EFFFFFF"),
                BorderThickness = new(1),
                CornerRadius = new(14),
                Child = body,
            },
        };
        popup.Tag = (Action)(() => chosen = true);
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                popup.IsOpen = false;
            }
        };
        popup.Closed += (_, _) =>
        {
            var latest = smartPopup == popup;
            if (latest)
            {
                smartPopup = null;
                smartInvoker = null;
            }
            if (!chosen)
            {
                dismissed?.Invoke();
                RenderQuiet();
            }
            if (!chosen && latest && invoker != null)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                {
                    if (closing || smartPopup != null)
                        return;
                    ContentPanel.UpdateLayout();
                    var target =
                        invoker.IsLoaded ? invoker
                        : focusIndex >= 0
                            ? ViewElements(ContentPanel)
                                .Where(e => e.Focusable && e.IsEnabled)
                                .ElementAtOrDefault(focusIndex)
                        : null;
                    if (target?.Focus() != true)
                        ContentScroll.Focus();
                });
        };
        body.Loaded += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                if (popup.IsOpen)
                {
                    body.UpdateLayout();
                    if (
                        ViewElements(body)
                            .OfType<ButtonBase>()
                            .FirstOrDefault(b => b.Focusable && b.IsEnabled && b.IsVisible) is
                        { } first
                    )
                        Keyboard.Focus(first);
                }
            });
        smartPopup = popup;
        popup.IsOpen = true;
        return body;
    }

    void CloseSmart()
    {
        if (smartPopup is { } p)
        {
            ((Action)p.Tag)();
            p.IsOpen = false;
        }
    }

    Button SmartButton(Panel host, string label, Action act, bool primary = false)
    {
        var b = ActionButton(label, () => { }, primary);
        b.Padding = Density.ButtonPadding;
        b.Margin = new(0, 0, 8, 8);
        if (!primary)
            b.Background = Brush("#1FFFFFFF");
        b.Click += (_, _) => act();
        host.Children.Add(b);
        return b;
    }

    static string Short(VideoItem i) => i.IsMovie ? i.Title : $"S{i.Season:00}E{i.Episode:00}";

    static string Range(List<VideoItem> items) =>
        items.Count == 1 ? Short(items[0]) : Short(items[0]) + "–" + Short(items[^1]);

    // Turns a "when" choice into diary entries. "Just now" and a picked day are spaced backwards by
    // runtime so a batch reads in watching order; "When they aired" uses each air date.
    List<LogEntry> DatedEntries(List<VideoItem> items, string when, DateTime day)
    {
        var now = DateTimeOffset.UtcNow;
        var ordered = items.OrderBy(i => i.Season).ThenBy(i => i.Episode).ToList();
        var result = new List<LogEntry>();
        var end =
            when == "Pick a date"
                ? new DateTimeOffset(
                    day.Date.AddHours(day.Date == DateTime.Today ? Math.Min(DateTime.Now.Hour, 22) : 22)
                )
            : when == "Around a year" ? new DateTimeOffset(new DateTime(day.Year, 7, 1, 21, 0, 0))
            : now;
        if (end > now)
            end = now;
        var cursor = end;
        // "Over a period:2018-2020" spreads the batch evenly across those years, in watching order.
        if (
            when.StartsWith("Over a period:")
            && when[14..].Split('-') is [var first, var last]
            && int.TryParse(first, out var from)
            && int.TryParse(last, out var to)
        )
        {
            var start = new DateTimeOffset(new DateTime(Math.Min(from, to), 1, 1, 21, 0, 0));
            var stop = new DateTimeOffset(new DateTime(Math.Max(from, to), 12, 31, 21, 0, 0));
            if (stop > now)
                stop = now;
            if (start > stop)
                start = stop;
            return ordered
                .Select(
                    (item, n) =>
                        new LogEntry(item, start + (stop - start) * ((n + .5) / ordered.Count), Approximate: true)
                )
                .ToList();
        }
        foreach (var item in Enumerable.Reverse(ordered))
        {
            var aired = store.AirDate(item);
            result.Add(
                when switch
                {
                    "When they aired" when aired is { } a && a <= now => new(
                        item,
                        a.ToLocalTime().Date.AddHours(21) < now.LocalDateTime
                            ? new DateTimeOffset(a.ToLocalTime().Date.AddHours(21))
                            : a,
                        Approximate: true
                    ),
                    "When they aired" or "Don't remember" => new(
                        item,
                        aired is { } known && known <= now ? known : now,
                        true
                    ),
                    "Around a year" => new(item, cursor, Approximate: true),
                    _ => new(item, cursor),
                }
            );
            cursor = cursor.AddMinutes(-store.RuntimeMinutes(item));
        }
        result.Reverse();
        return result;
    }

    void LogBatch(List<LogEntry> entries, string message, VideoItem? focus = null)
    {
        var items = entries.Select(e => e.Item).ToList();
        var before = store.Snapshot(items);
        var rewards = RewardState();
        var target = focus ?? items.OrderBy(i => i.Season).ThenBy(i => i.Episode).Last();
        var seasonWasDone =
            target.IsMovie || ShowEpisodes(target).Where(i => i.Season == target.Season).All(store.EverWatched);
        var added = store.LogWatched(entries, "Checked off");
        RenderQuiet();
        if (added == 0)
        {
            Status.Text = "Already in your history.";
            return;
        }
        AfterCheck(focus ?? items.OrderBy(i => i.Season).ThenBy(i => i.Episode).Last(), message, () =>
        {
            store.Restore(before);
            RenderQuiet();
        });
        CelebrateHistory(
            rewards,
            !seasonWasDone && ShowEpisodes(target).Where(i => i.Season == target.Season).All(store.EverWatched)
                ? target.Season
                : 0
        );
    }

    // Asks when the episodes were watched; nothing is preselected, so a catch-up never lands as "today" by accident.
    void AskWhen(List<VideoItem> items, string question, Action<string, DateTime> chosen, Action? dismissed = null)
    {
        var body = SmartPopup(dismissed);
        body.Children.Add(Text(question, 15, weight: FontWeights.SemiBold));
        body.Children.Add(
            Body(
                items.Count == 1
                    ? Short(items[0])
                    : $"{items.Count} episodes · {Range(items.OrderBy(i => i.Season).ThenBy(i => i.Episode).ToList())}",
                12
            )
        );
        var row = new WrapPanel();
        body.Children.Add(row);
        foreach (var when in WhenChoices.Where(w => w != "When they aired" || items.Any(i => !i.IsMovie)))
        {
            var value = when;
            SmartButton(
                row,
                value,
                () =>
                {
                    if (value == "Pick a date")
                    {
                        CloseSmart();
                        DateChooser(
                            null,
                            DateTime.Today,
                            (day, unknown) => chosen(unknown ? "Don't remember" : "Pick a date", day),
                            true
                        );
                        return;
                    }
                    if (value == "Around a year")
                    {
                        CloseSmart();
                        YearPicker("Around which year?", 1950, year =>
                            chosen("Around a year", new DateTime(year, 7, 1))
                        );
                        return;
                    }
                    if (value == "Over a period")
                    {
                        CloseSmart();
                        YearPicker("From which year?", 1950, from =>
                            YearPicker("Until which year?", from, to =>
                                chosen($"Over a period:{from}-{to}", new DateTime(from, 1, 1))
                            )
                        );
                        return;
                    }
                    CloseSmart();
                    chosen(value, DateTime.Today);
                },
                value == "Don't remember"
            );
        }
    }

    // A compact grid of years, newest first, for approximate dates.
    void YearPicker(string question, int earliest, Action<int> chosen)
    {
        var body = SmartPopup();
        body.Children.Add(Text(question, 15, weight: FontWeights.SemiBold));
        var grid = new WrapPanel { Margin = new(0, 10, 0, 0), MaxWidth = 380 };
        for (var year = DateTime.Today.Year; year >= Math.Max(1950, earliest); year--)
        {
            var y = year;
            var b = ActionButton(y.ToString(), () =>
            {
                CloseSmart();
                chosen(y);
            });
            b.Padding = new(10, 5, 10, 5);
            b.Margin = new(0, 0, 6, 6);
            b.FontSize = 12.5;
            b.Background = Brush("#26FFFFFF");
            grid.Children.Add(b);
        }
        body.Children.Add(
            new ScrollViewer
            {
                Content = grid,
                MaxHeight = 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            }
        );
    }

    // One episode checked off: if earlier released episodes are unwatched, ask about them (TV Time's prompt).
    void SmartCheckOff(VideoItem target, string message, LogEntry? targetEntry = null)
    {
        var entry = targetEntry ?? new LogEntry(target, DateTimeOffset.UtcNow);
        var earlier = store.EarlierUnwatched(target);
        if (earlier.Count == 0)
        {
            LogBatch([entry], message, target);
            return;
        }
        var body = SmartPopup();
        body.Children.Add(
            Text(
                $"Did you also watch the {earlier.Count} earlier episode{(earlier.Count == 1 ? "" : "s")}?",
                15,
                weight: FontWeights.SemiBold
            )
        );
        body.Children.Add(
            Body($"{Range(earlier)} of {target.Title} {(earlier.Count == 1 ? "isn't" : "aren't")} checked off yet.", 12)
        );
        var row = new WrapPanel();
        body.Children.Add(row);
        SmartButton(row, "Just " + Short(target), () =>
        {
            CloseSmart();
            LogBatch([entry], message, target);
        });
        SmartButton(
            row,
            $"Yes, mark all {earlier.Count + 1}",
            () =>
            {
                CloseSmart();
                AskWhen(earlier, "When did you watch the earlier ones?", (when, day) =>
                    LogBatch(
                        DatedEntries(earlier, when, day).Append(entry).ToList(),
                        $"{target.Title}: {earlier.Count + 1} episodes up to {Short(target)} watched",
                        target
                    )
                );
            },
            true
        );
    }

    // Several at once (season, whole show, up to here, skipped episodes): always ask when.
    void BulkCheckOff(List<VideoItem> items, string message)
    {
        items = items.Where(i => !store.EverWatched(i)).ToList();
        if (items.Count == 0)
            return;
        AskWhen(items, $"When did you watch {(items.Count == 1 ? "it" : $"these {items.Count}")}?", (when, day) =>
            LogBatch(DatedEntries(items, when, day), message)
        );
    }

    List<VideoItem> ShowEpisodes(VideoItem show) =>
        store.EpisodesFor(show.GroupKey).Where(i => store.Counts(i) && store.Aired(i, DateTimeOffset.UtcNow)).ToList();

    void MarkWholeShow(VideoItem show) => BulkCheckOff(ShowEpisodes(show), $"{show.Title}: whole show marked watched");

    // A check that is already filled: log a rewatch or take it out, and offer to unmark later episodes too.
    void SmartWatchedClick(VideoItem item)
    {
        var views = store.ViewingHistory(item);
        var last = views.FirstOrDefault();
        var body = SmartPopup();
        body.Children.Add(
            Text(item.IsMovie ? item.Title : $"{item.Title} {Short(item)}", 15, weight: FontWeights.SemiBold)
        );
        body.Children.Add(
            Body(
                last == null
                    ? "Watched"
                    : "Watched "
                        + (last.DateUnknown ? "(date unknown)" : last.WatchedAt.ToLocalTime().ToString("d MMM yyyy"))
                        + (views.Count > 1 ? $" · {views.Count} times" : ""),
                12
            )
        );
        var row = new WrapPanel();
        body.Children.Add(row);
        SmartButton(
            row,
            "[[sync]]  Log a rewatch",
            () =>
            {
                CloseSmart();
                var before = store.Snapshot([item]);
                var rewards = RewardState();
                store.LogRewatch(new(item, DateTimeOffset.UtcNow), "Rewatch");
                RenderQuiet();
                Undoable($"Rewatch of {Short(item)} logged", () =>
                {
                    store.Restore(before);
                    RenderQuiet();
                });
                CelebrateHistory(rewards);
            },
            true
        );
        SmartButton(row, "Remove from history", () =>
        {
            CloseSmart();
            SmartUncheck(item);
        });
    }

    void SmartUncheck(VideoItem item)
    {
        var later = store.LaterWatched(item);
        if (later.Count == 0)
        {
            UncheckAll([item], $"{Short(item)} unmarked");
            return;
        }
        var body = SmartPopup();
        body.Children.Add(
            Text(
                $"Also unmark the {later.Count} later episode{(later.Count == 1 ? "" : "s")}?",
                15,
                weight: FontWeights.SemiBold
            )
        );
        body.Children.Add(Body($"{Range(later)} {(later.Count == 1 ? "is" : "are")} still checked off.", 12));
        var row = new WrapPanel();
        body.Children.Add(row);
        SmartButton(row, "Just " + Short(item), () =>
        {
            CloseSmart();
            UncheckAll([item], $"{Short(item)} unmarked");
        });
        SmartButton(
            row,
            $"Unmark all {later.Count + 1}",
            () =>
            {
                CloseSmart();
                UncheckAll([item, .. later], $"{later.Count + 1} episodes of {item.Title} unmarked");
            },
            true
        );
    }

    // After a check-off: a finished season or show offers a quick rating; an ended show becomes Finished.
    void AfterCheck(VideoItem focus, string message, Action undo)
    {
        FrameworkElement? extra = null;
        if (!focus.IsMovie)
        {
            var all = ShowEpisodes(focus);
            var season = all.Where(i => i.Season == focus.Season).ToList();
            var showDone = all.Count > 0 && all.All(store.EverWatched);
            var seasonDone = season.Count > 0 && season.All(store.EverWatched);
            if (
                showDone
                && store.ShowEnded(focus.GroupKey)
                && store.TitleStatus(store.EpisodesFor(focus.GroupKey)) != "Finished"
            )
                store.SetStatus(focus, "Finished");
            if (showDone || seasonDone)
            {
                // A finished show asks about the show; otherwise the season just completed.
                var key = showDone ? focus.GroupKey : $"{focus.GroupKey}:S{focus.Season}";
                var label = showDone
                    ? (
                        store.ShowEnded(focus.GroupKey)
                            ? T("Finished · optional rating", "انتهى المسلسل · التقييم اختياري")
                            : T("All caught up · optional rating", "شاهدت كل الحلقات · التقييم اختياري")
                    )
                    : T("Season {0} complete · optional rating", "اكتمل الموسم {0} · التقييم اختياري", focus.Season);
                extra = RatingStrip(label, key);
            }
        }
        var followup = new WrapPanel();
        if (extra != null)
            followup.Children.Add(extra);
        followup.Children.Add(ReactionRow(focus));
        Undoable(message, undo, followup);
    }

    FrameworkElement RatingStrip(string label, string key)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = new TextBlock
        {
            Text = label,
            Foreground = Brush("#E4C084"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new(0, 0, 6, 0),
        };
        row.Children.Add(text);
        var split = key.LastIndexOf(":S", StringComparison.Ordinal);
        if (split >= 0 && int.TryParse(key[(split + 2)..], out var season))
        {
            var group = key[..split];
            var score = store.State.SeasonRatings.GetValueOrDefault(group + ":" + season);
            var picker = new Button
            {
                Content = HalfRating(score, 17),
                Background = Brush("Transparent"),
                Padding = new(2, 0, 2, 0),
                ToolTip = T("Rate with half stars · optional", "قيّم بنصف نجمة · اختياري"),
            };
            picker.PreviewMouseLeftButtonUp += (_, e) =>
            {
                score = Math.Clamp((int)Math.Ceiling((e.GetPosition(picker).X - 2) / 19 * 2), 1, 10);
                store.SetSeasonRating(group, season, score);
                store.RateShow(key, (score + 1) / 2);
                picker.Content = HalfRating(score, 17);
                text.Text = T("Rated", "تم التقييم");
                e.Handled = true;
            };
            row.Children.Add(picker);
            return row;
        }
        var current = store.State.ShowRatings.GetValueOrDefault(key);
        var stars = new List<Button>();
        for (int i = 1; i <= 5; i++)
        {
            var value = i;
            var star = new Button
            {
                Content = new TextBlock
                {
                    Text = value <= current ? "★" : "☆",
                    FontSize = 15,
                    Foreground = Brush("#E4C084"),
                },
                Padding = new(2, 0, 2, 0),
                Background = Brush("Transparent"),
                ToolTip = T("{0} out of 5", "{0} من 5", value),
            };
            System.Windows.Automation.AutomationProperties.SetName(
                star,
                T("Rate {0} out of 5", "تقييم {0} من 5", value)
            );
            star.Click += (_, _) =>
            {
                store.RateShow(key, value);
                for (int n = 0; n < 5; n++)
                    ((TextBlock)stars[n].Content).Text = n < value ? "★" : "☆";
                text.Text = T("Rated", "تم التقييم");
                undoTimer?.Stop();
                undoTimer?.Start();
            };
            stars.Add(star);
            row.Children.Add(star);
        }
        return row;
    }

    // Show-level stars for posters and the detail page.
    string ShowStars(string groupKey) =>
        store.State.ShowRatings.GetValueOrDefault(groupKey) is var r && r > 0 ? new string('★', r) : "";
}
