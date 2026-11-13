using System.IO;
using System.Text.Json;
#if !NOIR_HEADLESS
using System.Windows;
using System.Windows.Controls;
#endif
namespace Noir;

public sealed class HomeRowSetting
{
    public string Id { get; set; } = "";
    public bool Visible { get; set; } = true;
    public bool Pinned { get; set; }
}

public sealed class HomeLayout
{
    public const int CurrentVersion = 2;
    public int Version { get; set; }
    public string ContinueStyle { get; set; } = "Wide";
    public bool ShowPosterTitles { get; set; } = true;

    // "S", "M" or "L": how big Home's tiles are drawn (some people find the default feels zoomed in).
    public string CardSize { get; set; } = "M";

    public static double Scale(string? size) =>
        size switch
        {
            "S" => .8,
            "L" => 1.2,
            _ => 1,
        };
    public static readonly string[] DiscoveryRows = ["because-2", "picked", "fav-like", "genre-popular", "genre-top"];
    // Your own things first (what you queued, saved, kept ready, just added and what airs next), then discovery,
    // then the library's smart rows. A saved layout keeps its own order; this is only what a new Home starts with.
    public static readonly string[] Defaults =
    [
        "continue",
        "queue",
        "list",
        "ready",
        "recently-added",
        "upcoming",
        "top10",
        "because",
        "because-2",
        "picked",
        "fav-like",
        "genre-popular",
        "genre-top",
        "unfinished",
        "comfort",
        "directors",
        "short-tonight",
        "genres",
        "collections",
        "recently-watched",
        "shows",
        "movies",
        "services",
    ];

    // A row Noir fills by itself needs this many titles to be worth a row; one or two posters under a heading read as a gap.
    public const int SmartRowMin = 3;
    public const int GenreRowMin = 4;
    public List<HomeRowSetting> Rows { get; set; } = [];

    public List<HomeRowSetting> Resolve(IEnumerable<string> available)
    {
        var known = available.Concat(Defaults).ToHashSet(StringComparer.Ordinal);
        var result = new List<HomeRowSetting> { new() { Id = "continue" } };
        var seen = new HashSet<string> { "continue" };
        foreach (var row in Rows ?? [])
            if (row != null && known.Contains(row.Id) && seen.Add(row.Id))
                result.Add(
                    new()
                    {
                        Id = row.Id,
                        Visible = row.Visible,
                        Pinned = row.Pinned,
                    }
                );
        foreach (var id in Defaults.Concat(available))
            if (seen.Add(id))
                result.Add(
                    new()
                    {
                        Id = id,
                        Visible = Defaults.Contains(id),
                        Pinned = false,
                    }
                );
        if (Version < CurrentVersion && Rows is { Count: > 0 })
        {
            var added = result
                .Where(r => DiscoveryRows.Contains(r.Id) && !Rows.Any(old => old != null && old.Id == r.Id))
                .ToList();
            foreach (var row in added)
                result.Remove(row);
            var because = result.FindIndex(r => r.Id == "because");
            result.InsertRange(because + 1, added);
            var ready = result.First(r => r.Id == "ready");
            result.Remove(ready);
            result.Insert(result.FindIndex(r => r.Id == "list") + 1, ready);
        }
        return result;
    }

    public static HomeLayout Load(string directory)
    {
        HomeLayout layout;
        try
        {
            layout = JsonSerializer.Deserialize<HomeLayout>(
                File.ReadAllText(Path.Combine(directory, "home-layout.json"))
            ) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
        if (layout.Version < CurrentVersion)
        {
            layout.Rows = layout.Resolve(
                (layout.Rows ?? [])
                    .Where(r => r != null)
                    .Select(r => r.Id)
                    .Where(id => id != null && (id.StartsWith("genre:") || id.StartsWith("collection:")))
            );
            layout.Version = CurrentVersion;
            try
            {
                layout.Save(directory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return layout;
    }

    public void Save(string directory)
    {
        Version = CurrentVersion;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "home-layout.json");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this), new System.Text.UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}

#if !NOIR_HEADLESS
public partial class MainWindow
{
    Dictionary<string, Action>? homeRowBuilders;
    FrameworkElement? homeHero; // this render's hero, so the status strip can sit right under it

    // Discovery titles already on Home this render, in page order: later rows show something new before repeating one.
    readonly HashSet<string> homeShown = new(StringComparer.OrdinalIgnoreCase);

    List<T> FreshFirst<T>(IEnumerable<T> candidates, Func<T, string> id, int limit = 20)
    {
        var all = candidates.ToList();
        var picks = all.Where(c => !homeShown.Contains(id(c))).Take(limit).ToList();
        var floor = Math.Min(12, limit);
        if (picks.Count < floor)
            picks.AddRange(all.Where(c => homeShown.Contains(id(c))).Take(floor - picks.Count));
        homeShown.UnionWith(picks.Select(id));
        return picks;
    }
    readonly Dictionary<string, string> homeRowNames = new();

    void HomeRow(string id, Action render)
    {
        if (homeRowBuilders != null)
            homeRowBuilders[id] = render;
        else
            render();
    }

    string HomeRowName(string id) =>
        SmartLists.Ids.Contains(id)
            ? SmartName(id)
            : homeRowNames.GetValueOrDefault(id)
                ?? (
                    id switch
                    {
                        "because" => T("Because you watched…", "لأنك شاهدت…"),
                        "because-2" => T("Because you watched… · another pick", "لأنك شاهدت… · اقتراح آخر"),
                        "picked" => T("Picked for you", "اخترنا لك"),
                        "fav-like" => T("More like your favourites", "المزيد مثل أعمالك المفضلة"),
                        "genre-popular" => T("Popular in your top genre", "الرائج في نوعك المفضل"),
                        "genre-top" => T("Top rated in your top genre", "الأعلى تقييمًا في نوعك المفضل"),
                        _ => T(
                            id switch
                            {
                                "continue" => "Continue watching",
                                "list" => "My List",
                                "top10" => "Top 10 this week",
                                "genres" => "Genres",
                                "collections" => "Collections",
                                "recently-added" => "Recently added",
                                "services" => "Your services",
                                "recently-watched" => "Recently watched",
                                "shows" => "Your shows",
                                "movies" => "Your movies",
                                "queue" => "Your play queue",
                                "upcoming" => "Coming up",
                                _ => id,
                            }
                        ),
                    }
                );

    void RenderHomeRows(List<List<VideoItem>> groups)
    {
        var placeholder = ContentPanel.Children.OfType<StackPanel>().LastOrDefault();
        if (
            placeholder?.Children.OfType<TextBlock>().FirstOrDefault()?.Text == T("Continue watching")
            && !placeholder.Children.OfType<ScrollViewer>().Any()
        )
            ContentPanel.Children.Remove(placeholder);
        foreach (var g in groups)
            lastWatched[g[0].GroupKey] = g.Max(HistoryDate);
        var library = store.LibraryGroups();
        var local = groups.Where(g => library.Contains(g[0].GroupKey)).ToList();
        var watched = groups
            .Where(g => LastWatched(g) > DateTimeOffset.MinValue)
            .OrderByDescending(LastWatched)
            .ToList();
        homeRowBuilders = new();
        homeRowNames.Clear();
        homeShown.Clear();
        StartupTrace.Mark("Home rows prepared");
        RegisterSmartRows(groups);
        StartupTrace.Mark("Home smart rows registered");
        homeRowNames["ready"] = T("Keep ready", "إبقاء الحلقات جاهزة");
        HomeRow("ready", () =>
        {
            if (ReadyShelf() is { } shelf)
                ContentPanel.Children.Add(shelf);
        });
        HomeRow("services", RenderServices);
        HomeRow("top10", RenderTopTen);
        RenderHomeExtras(groups);
        StartupTrace.Mark("Home extras registered");
        HomeRow("list", () =>
            RailCatalog(groups.Where(g => store.State.Watchlist.Contains(g[0].GroupKey)).ToList(), "My List", "", () =>
                Navigate("My List")
            )
        );
        HomeRow("recently-watched", () => RailCatalog(watched.Take(12).ToList(), "Recently watched", "", () =>
            {
                trackerTab = "Shows";
                Navigate("History");
            }));
        HomeRow("shows", () =>
            RailCatalog(local.Where(g => !g[0].IsMovie).OrderByDescending(LastWatched).ToList(), "Your shows", "", () =>
                Navigate("Shows")
            )
        );
        HomeRow("movies", () =>
            RailCatalog(local.Where(g => g[0].IsMovie).OrderByDescending(LastWatched).ToList(), "Your movies", "", () =>
                Navigate("Movies")
            )
        );
        var genres = local
            .SelectMany(g =>
                g[0].Genres.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            )
            .Distinct()
            .Order()
            .ToList();
        void Genre(string genre) =>
            RailCatalog(
                local.Where(g => g[0].Genres.Split('·', StringSplitOptions.TrimEntries).Contains(genre)).ToList(),
                T(genre),
                limit: 20
            );
        HomeRow("genres", () =>
        {
            // Up to three genre rows, biggest first. A genre with too few titles, or with only titles an earlier
            // genre row already showed (Anime under Adventure), is skipped.
            var drawn = new List<HashSet<string>>();
            foreach (var genre in genres.OrderByDescending(x => local.Count(g => g[0].Genres.Contains(x))))
            {
                if (drawn.Count == 3)
                    break;
                var keys = local
                    .Where(g => g[0].Genres.Split('·', StringSplitOptions.TrimEntries).Contains(genre))
                    .Select(g => g[0].GroupKey)
                    .ToHashSet();
                if (keys.Count < HomeLayout.GenreRowMin || drawn.Any(keys.IsSubsetOf))
                    continue;
                drawn.Add(keys);
                Genre(genre);
            }
        });
        foreach (var genre in genres)
        {
            var id = "genre:" + genre;
            homeRowNames[id] = T(genre);
            HomeRow(id, () => Genre(genre));
        }
        void Collection(string name) =>
            RailCatalog(
                groups.Where(g => store.State.Collections[name].Contains(g[0].GroupKey)).ToList(),
                name,
                limit: 20
            );
        HomeRow("collections", () =>
        {
            foreach (var name in store.State.Collections.Keys.Take(3))
                Collection(name);
        });
        foreach (var name in store.State.Collections.Keys)
        {
            var id = "collection:" + name;
            homeRowNames[id] = name;
            HomeRow(id, () => Collection(name));
        }
        RegisterHomeDiscovery(groups);
        var builders = homeRowBuilders;
        homeRowBuilders = null;
        var saved = HomeLayout.Load(store.DataDirectory);
        var rows = saved.Resolve(builders.Keys);
        StartupTrace.Mark("Home rows registered");
        foreach (var row in rows.Where(r => r.Id != "continue" && r.Visible))
            if (builders.TryGetValue(row.Id, out var build))
            {
                build();
                StartupTrace.Mark("Home row built: " + row.Id);
            }
        // The status strip sits under the hero and carries "Edit Home"; only a page with no strip keeps the button at its end.
        if (HomeStatusStrip(groups, builders.Keys) is { } strip)
            ContentPanel.Children.Insert(
                homeHero != null && ContentPanel.Children.Contains(homeHero)
                    ? ContentPanel.Children.IndexOf(homeHero) + 1
                    : 0,
                strip
            );
        else
        {
            var edit = ActionButton(T("Edit Home", "تعديل الرئيسية"), () => HomeLayoutEditor(builders.Keys));
            edit.Margin = new(0, 36, 0, 24);
            System.Windows.Automation.AutomationProperties.SetName(edit, "Edit Home");
            ContentPanel.Children.Add(edit);
        }
        lastWatched.Clear();
    }

    NoirSheet CreateHomeLayoutEditor(IEnumerable<string> available)
    {
        var layout = HomeLayout.Load(store.DataDirectory);
        layout.Rows = layout.Resolve(available);
        var changed = false;
        var failed = false;
        var dialog = OpenSheet(
            T("Edit Home", "تعديل الرئيسية"),
            T(
                "Continue watching stays first. Pin genres and collections below.",
                "متابعة المشاهدة أولاً دائمًا. ثبّت الأنواع والمجموعات أدناه."
            ),
            SheetKind.Side,
            500
        );
        var panel = new StackPanel();
        dialog.Add(panel);
        var rows = new StackPanel();
        panel.Children.Add(SheetSection(T("Continue watching card style", "نمط بطاقات متابعة المشاهدة")));
        var style = new ComboBox
        {
            Margin = new(0, 4, 0, 12),
            ItemsSource = new[] { T("Poster", "ملصق"), T("Wide", "عريض") },
            SelectedIndex = layout.ContinueStyle == "Poster" ? 0 : 1,
        };
        System.Windows.Automation.AutomationProperties.SetName(
            style,
            T("Continue watching card style", "نمط بطاقات متابعة المشاهدة")
        );
        style.SelectionChanged += (_, _) =>
        {
            layout.ContinueStyle = style.SelectedIndex == 0 ? "Poster" : "Wide";
            Commit();
        };
        panel.Children.Add(style);
        var titles = new CheckBox
        {
            Content = T("Show titles under posters", "عرض العناوين أسفل الملصقات"),
            IsChecked = layout.ShowPosterTitles,
            Margin = new(0, 0, 0, 6),
        };
        System.Windows.Automation.AutomationProperties.SetName(
            titles,
            T("Show titles under posters", "عرض العناوين أسفل الملصقات")
        );
        titles.Click += (_, _) =>
        {
            layout.ShowPosterTitles = titles.IsChecked == true;
            Commit();
        };
        panel.Children.Add(titles);
        panel.Children.Add(
            SheetSection(
                T("Tile size", "حجم البطاقات"),
                T("How big posters and cards are drawn on Home.", "حجم الملصقات والبطاقات في الرئيسية.")
            )
        );
        var sizes = new WrapPanel { Margin = new(0, 4, 0, 12) };
        void DrawSizes()
        {
            sizes.Children.Clear();
            foreach (
                var (id, label) in new[]
                {
                    ("S", T("Small", "صغير")),
                    ("M", T("Medium", "متوسط")),
                    ("L", T("Large", "كبير")),
                }
            )
            {
                var pill = PillOption(
                    label,
                    () =>
                    {
                        layout.CardSize = id;
                        Commit();
                        DrawSizes();
                    },
                    (layout.CardSize is "S" or "L" ? layout.CardSize : "M") == id
                );
                pill.Margin = new(0, 0, 8, 0);
                System.Windows.Automation.AutomationProperties.SetName(
                    pill,
                    T("Tile size", "حجم البطاقات") + " " + label
                );
                sizes.Children.Add(pill);
            }
        }
        DrawSizes();
        panel.Children.Add(sizes);
        panel.Children.Add(
            SheetSection(
                T("Rows", "الصفوف"),
                T("Show, hide and reorder the rows on Home.", "أظهر الصفوف أو أخفِها أو غيّر ترتيبها في الرئيسية.")
            )
        );
        panel.Children.Add(rows);
        // U32 (P7): every change is saved as it is made; the page redraws once when the sheet closes.
        void Commit()
        {
            try
            {
                layout.Save(store.DataDirectory);
                changed = true;
                failed = false;
            }
            catch (Exception e)
            {
                store.Log(e);
                if (!failed)
                    MessageBox.Show(
                        this,
                        T("Couldn’t save Home. Please try again.", "تعذّر حفظ الرئيسية. حاول مجددًا.")
                    );
                failed = true;
            }
        }
        void Draw()
        {
            rows.Children.Clear();
            foreach (var row in layout.Rows)
            {
                var line = new DockPanel { Margin = new(0, 5, 0, 5) };
                var tools = new StackPanel { Orientation = Orientation.Horizontal };
                DockPanel.SetDock(tools, Dock.Right);
                line.Children.Add(tools);
                var locked = row.Id == "continue";
                foreach (var delta in new[] { -1, 1 })
                {
                    var d = delta;
                    var move = ActionButton(d < 0 ? "▲" : "▼", () =>
                    {
                        var i = layout.Rows.IndexOf(row);
                        var target = i + d;
                        if (target < 1 || target >= layout.Rows.Count)
                            return;
                        layout.Rows.RemoveAt(i);
                        layout.Rows.Insert(target, row);
                        Commit();
                        Draw();
                    });
                    move.Padding = new(9, 6, 9, 6);
                    move.IsEnabled =
                        !locked
                        && (d < 0 ? layout.Rows.IndexOf(row) > 1 : layout.Rows.IndexOf(row) < layout.Rows.Count - 1);
                    move.ToolTip = T(d < 0 ? "Move earlier" : "Move later");
                    System.Windows.Automation.AutomationProperties.SetName(
                        move,
                        (string)move.ToolTip + " " + HomeRowName(row.Id)
                    );
                    tools.Children.Add(move);
                }
                var extra = row.Id.StartsWith("genre:") || row.Id.StartsWith("collection:");
                var visible = new CheckBox
                {
                    Content =
                        (
                            locked ? "🔒 "
                            : extra ? "[[star]] "
                            : ""
                        ) + HomeRowName(row.Id),
                    IsChecked = row.Visible,
                    IsEnabled = !locked,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new(2, 0, 10, 0),
                    FontSize = 14,
                };
                System.Windows.Automation.AutomationProperties.SetName(visible, HomeRowName(row.Id));
                visible.Click += (_, _) =>
                {
                    row.Visible = visible.IsChecked == true;
                    row.Pinned = extra && row.Visible;
                    Commit();
                };
                line.Children.Add(visible);
                rows.Children.Add(line);
            }
        }
        Draw();
        dialog.Closed += _ =>
        {
            if (changed)
                Render();
        };
        dialog.Action(T("Done", "تم"), () => dialog.Close(true), true);
        return dialog;
    }

    void HomeLayoutEditor(IEnumerable<string> available) => CreateHomeLayoutEditor(available);
}
#endif
