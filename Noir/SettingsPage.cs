using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noir;

// Settings: a section list on the left, the chosen section on the right (mirrored in Arabic).
// Connections gathers every account and service in one place, each with its status and sync queue.
public partial class MainWindow
{
    string settingsSection = "Profile";
    static readonly (string Name, string Hint)[] SettingsSections =
    [
        ("Profile", "You and how you use Noir"),
        ("Playback", "Auto-next, subtitles, after watching"),
        ("Library & folders", "Folders, artwork, organizing"),
        ("Connections", "Trakt, Nuvio, Stremio, servers"),
        ("Downloads", "Sources, quality, folders"),
        ("Phone", "Pair and sync Android"),
        ("Appearance & language", "Language, side rail, spoilers"),
        ("Backup", "Back up, restore, export"),
        ("Advanced", "Background, shortcuts, about"),
    ];
    readonly HashSet<string> openQueues = [];

    void OpenSettingsSection(string name)
    {
        settingsSection = name;
        Render();
        ContentScroll.ScrollToTop();
    }

    void RenderSettings()
    {
        PageTitle.Text = T("Settings");
        settingsSection = settingsSection switch
        {
            "Library" => "Library & folders",
            "Community" or "Discover" => "Connections",
            "Phone Sync" => "Phone",
            "History" => "Playback",
            _ => settingsSection,
        };
        if (!SettingsSections.Any(s => s.Name == settingsSection))
            settingsSection = "Profile";
        var content = new StackPanel { MaxWidth = 1240, HorizontalAlignment = HorizontalAlignment.Stretch };
        // Narrow windows keep the sections as a chip row above the content.
        if (ContentScroll.ActualWidth is > 0 and < 820)
        {
            ContentPanel.Children.Add(
                Chips(SettingsSections.Select(s => s.Name).ToArray(), settingsSection, OpenSettingsSection)
            );
            ContentPanel.Children.Add(content);
        }
        else
        {
            var grid = new Grid { Margin = new(0, 10, 0, 0) };
            grid.ColumnDefinitions.Add(new() { Width = new(224) });
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            grid.Children.Add(SettingsSectionList());
            content.Margin = new(32, 0, 0, 0);
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);
            ContentPanel.Children.Add(grid);
        }
        // U52: the section title appears once; its list hint is not repeated under it.
        content.Children.Add(Text(T(settingsSection), 28, weight: FontWeights.Bold));
        // The section renderers add to ContentPanel; point it at the right-hand column while they run.
        var outer = ContentPanel;
        ContentPanel = content;
        try
        {
            switch (settingsSection)
            {
                case "Playback":
                    RenderPlaybackSettings();
                    RenderHistorySettings();
                    break;
                case "Library & folders":
                    RenderLibrarySettings();
                    break;
                case "Connections":
                    RenderConnections();
                    break;
                case "Downloads":
                    RenderDownloadSettings();
                    break;
                case "Phone":
                    RenderMobileSettings();
                    break;
                case "Appearance & language":
                    RenderAppearanceSettings();
                    break;
                case "Backup":
                    RenderBackupSettings();
                    break;
                case "Advanced":
                    RenderAdvancedSettings();
                    break;
                default:
                    RenderProfileSettings();
                    break;
            }
        }
        finally
        {
            ContentPanel = outer;
        }
        TidySettingsSection(content);
    }

    // The section renderers live in other files; this applies the cross-section rules to what they drew.
    void TidySettingsSection(StackPanel content)
    {
        var kids = content.Children;
        // U52: a first group heading that repeats the section title is dropped (Playback / Playback).
        if (
            kids.Count > 1
            && kids[1] is StackPanel first
            && first.Children.Count > 0
            && first.Children[0] is TextBlock head
            && head.Text == T(settingsSection)
        )
            kids.RemoveAt(1);
        // U56: "Organize filenames & folders" sits after the folder list, not above its first row.
        if (
            settingsSection == "Library & folders"
            && kids.OfType<Button>().FirstOrDefault(b => b.Content as string == T("Organize filenames & folders"))
                is { } organize
        )
        {
            kids.Remove(organize);
            organize.Margin = new(0, 16, 0, 0);
            kids.Add(organize);
        }
        // U57: the shortcut table is one line and a button that opens the same sheet "?" opens.
        if (settingsSection == "Advanced")
        {
            var at = -1;
            for (var i = 0; i < kids.Count; i++)
                if (
                    kids[i] is StackPanel group
                    && group.Children.Count > 0
                    && group.Children[0] is TextBlock h
                    && h.Text == T("Keyboard shortcuts")
                )
                {
                    at = i;
                    break;
                }
            var open = kids.OfType<Button>()
                .FirstOrDefault(b => b.Content as string == T("Shortcuts & tips", "الاختصارات والنصائح"));
            if (at >= 0 && open != null && kids.IndexOf(open) is var to && to > at)
            {
                for (var i = to - 1; i > at; i--)
                    kids.RemoveAt(i);
                kids.Insert(
                    at + 1,
                    Body(
                        T(
                            "Press ? anywhere in Noir to see every shortcut.",
                            "اضغط ? في أي مكان في Noir لعرض كل الاختصارات."
                        )
                    )
                );
            }
        }
    }

    FrameworkElement SettingsSectionList()
    {
        var nav = new StackPanel();
        foreach (var (name, hint) in SettingsSections)
        {
            var on = name == settingsSection;
            var row = new DockPanel();
            // U55: no count badge on Connections; its page says how many are waiting to sync.
            var lines = new StackPanel();
            row.Children.Add(lines);
            lines.Children.Add(
                Text(T(name), 14, on ? "#E4C084" : "#E9E4DC", on ? FontWeights.SemiBold : FontWeights.Normal)
            );
            var small = Text(T(hint), 11.5, "#8F8A83");
            small.TextWrapping = TextWrapping.NoWrap;
            small.TextTrimming = TextTrimming.CharacterEllipsis;
            lines.Children.Add(small);
            var button = new Button
            {
                Content = row,
                Padding = new(14, 9, 12, 9),
                Margin = new(0, 0, 0, 3),
                Background = Brush(on ? "#1CE4C084" : "Transparent"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Template = NavTemplate(),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, T(name));
            var target = name;
            button.Click += (_, _) => OpenSettingsSection(target);
            nav.Children.Add(button);
        }
        return nav;
    }

    static ControlTemplate NavTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetBinding(
            Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent,
            }
        );
        border.SetBinding(
            Border.PaddingProperty,
            new System.Windows.Data.Binding("Padding")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent,
            }
        );
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#24FFFFFF"), "Chrome"));
        template.Triggers.Add(hover);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#E4C084"), "Chrome"));
        template.Triggers.Add(focus);
        return template;
    }

    int PendingSync() =>
        store.State.WatchSync.Values.Count(j => j.SyncedAt == null && CommunityQueue.Enabled(store.State, j.Provider))
        + (store.State.NuvioConnected ? store.State.NuvioDeletes.Count : 0);

    // ---- Profile, appearance, backup ----
    void RenderProfileSettings()
    {
        // U54: the old top card (name, Your stats, Your diary) is gone; the Profiles list shows the same person.
        RenderProfiles();
        RenderModeSettings();
    }

    void SetDensity(UiDensity mode, bool save = true)
    {
        var offset = ContentScroll.VerticalOffset;
        Density.Set(mode, save);
        CloseHoverCard();
        BuildSideRail();
        sidePad = Density.PageSide(ActualWidth);
        ApplyRailWidth();
        TopBarContent.Margin = new Thickness(Math.Max(16, sidePad - 16), 0, Math.Max(16, sidePad - 20), 0);
        ApplyPageLayout();
        RenderQuiet();
        ContentScroll.ScrollToVerticalOffset(offset);
    }

    static ControlTemplate DensityChoiceTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brush("#2EFFFFFF"));
        border.SetBinding(
            Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent,
            }
        );
        border.SetBinding(
            Border.PaddingProperty,
            new System.Windows.Data.Binding("Padding")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent,
            }
        );
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = border };
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#F4F1EB"), "Chrome"));
        template.Triggers.Add(focus);
        return template;
    }

    void RenderAppearanceSettings()
    {
        ContentPanel.Children.Add(
            Heading(
                "Size",
                "Compact fits more on screen. Comfortable gives everything more room. Applies only to this device."
            )
        );
        // U47: Compact / Comfortable are the same gold pills as everywhere else, with a gap between them.
        var sizes = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var mode in new[] { UiDensity.Compact, UiDensity.Comfortable })
        {
            var on = Density.Mode == mode;
            var choice = new RadioButton
            {
                Content = T(mode.ToString()),
                IsChecked = on,
                Padding = new(16, 8, 16, 8),
                FontSize = 13,
                MinWidth = 110,
                Margin = new(0, 0, 8, 6),
                Background = Brush(on ? "#E4C084" : "#222225"),
                Foreground = Brush(on ? "#16120C" : "#C7C2BC"),
                Template = DensityChoiceTemplate(),
            };
            System.Windows.Automation.AutomationProperties.SetName(choice, T("Size") + " · " + T(mode.ToString()));
            choice.Checked += (_, _) =>
            {
                SetDensity(mode);
                ViewElements(ContentPanel)
                    .OfType<RadioButton>()
                    .FirstOrDefault(b => Equals(b.Content, T(mode.ToString())))
                    ?.Focus();
            };
            sizes.Children.Add(choice);
        }
        ContentPanel.Children.Add(sizes);
        ContentPanel.Children.Add(
            Heading("Language", "Choose the app language. Arabic mirrors the layout right to left.")
        );
        ContentPanel.Children.Add(
            Chips(["English", "العربية"], store.State.Language == "ar" ? "العربية" : "English", v =>
                SetLanguage(v == "العربية" ? "ar" : "en")
            )
        );
        ContentPanel.Children.Add(
            Heading("Side rail", "Labels beside the icons, or icons only. Automatic folds to icons in narrow windows.")
        );
        var current = store.State.RailMode switch
        {
            "Labels" => "Labels",
            "Icons" => "Icons only",
            _ => "Automatic",
        };
        ContentPanel.Children.Add(Chips(["Automatic", "Labels", "Icons only"], current, v =>
            {
                store.State.RailMode = v switch
                {
                    "Labels" => "Labels",
                    "Icons only" => "Icons",
                    _ => "Auto",
                };
                railChoice = null;
                store.Save();
                ApplyRailWidth();
                Render();
            }));
        ContentPanel.Children.Add(Heading("Spoilers"));
        var spoilers = new CheckBox
        {
            Content = T("Hide descriptions of episodes I haven't finished"),
            IsChecked = store.State.HideSpoilers,
        };
        spoilers.Click += (_, _) =>
        {
            store.State.HideSpoilers = spoilers.IsChecked == true;
            store.Save();
        };
        ContentPanel.Children.Add(spoilers);
    }

    void RenderBackupSettings()
    {
        RenderBackups();
        RenderExports();
        RenderSources();
    }

    void RenderExports()
    {
        ContentPanel.Children.Add(
            Heading("Export", "Your complete history, including titles that were never on this PC.")
        );
        var exports = new WrapPanel();
        ContentPanel.Children.Add(exports);
        foreach (
            var (label, format, file, tip) in new[]
            {
                (
                    "Noir CSV",
                    "noir-csv",
                    "noir-history.csv",
                    "Every viewing with dates, sources and ratings — opens in Excel"
                ),
                ("Noir JSON", "noir-json", "noir-history.json", "Lossless; import it into Noir on another PC"),
                (
                    "Letterboxd CSV",
                    "letterboxd",
                    "letterboxd-import.csv",
                    "Movies with dates and ratings, for letterboxd.com/import"
                ),
                ("Trakt JSON", "trakt", "trakt-history.json", "Movies and episodes in Trakt's history format"),
            }
        )
        {
            var b = ActionButton("[[download]]  " + label, () => ExportHistory(format, file));
            b.ToolTip = tip;
            b.Padding = new(18, 11, 20, 11);
            b.Margin = new(0, 0, 10, 10);
            exports.Children.Add(b);
        }
    }

    // ---- Connections ----
    void RenderConnections()
    {
        var config = downloads?.Config;
        var servers = config?.Servers ?? [];
        var kodi = KodiAccount();
        var stremio = StremioKey() != null;
        var tmdb = TmdbKey().Length > 0;
        bool HasKey(string service) =>
            config != null
            && (
                service == "TorBox"
                    ? config.TorBoxKey.Length > 0 || config.Service == "TorBox" && config.Key.Length > 0
                    : config.RealDebridKey.Length > 0 || config.Service == "Real-Debrid" && config.Key.Length > 0
            );
        var connected =
            new[]
            {
                store.State.SyncTrakt,
                store.State.SyncSerializd,
                store.State.NuvioConnected,
                stremio,
                kodi != null,
                HasKey("TorBox"),
                HasKey("Real-Debrid"),
                config?.Manifest.Length > 0,
                tmdb,
            }.Count(x => x) + servers.Count;
        var waiting = PendingSync();
        var summary = new DockPanel { Margin = new(0, 20, 0, 0) };
        var all = ActionButton("[[sync]]  Sync everything now", SyncAllNow, true);
        all.Padding = new(16, 9, 18, 9);
        DockPanel.SetDock(all, Dock.Right);
        summary.Children.Add(all);
        summary.Children.Add(
            Text(
                $"{connected} {T("connected")}"
                    + (waiting > 0 ? $" · {waiting} {T("waiting to sync")}" : " · " + T("everything is up to date")),
                14,
                waiting > 0 ? "#E4C084" : "#C7C2BC"
            )
        );
        ((FrameworkElement)summary.Children[^1]).VerticalAlignment = VerticalAlignment.Center;
        ContentPanel.Children.Add(summary);

        Group("Sync your watches", "Kept in step automatically while Noir runs. Undo any import from Backup.");
        var sync = Cards();
        foreach (var provider in new[] { "Trakt", "Serializd" })
            sync.Children.Add(SyncSiteCard(provider));
        sync.Children.Add(NuvioCard());
        sync.Children.Add(StremioCard());
        sync.Children.Add(HarborCard());

        Group(
            "Media servers",
            "Download original files and import what you've watched. Read-only: nothing on the server changes."
        );
        var media = Cards();
        var api = new MediaServers();
        foreach (var server in servers)
        {
            var s = server;
            media.Children.Add(
                ConnectionCard(
                    s.Name.Length > 0 ? s.Name : s.Kind,
                    s.Kind[..1],
                    s.Kind switch
                    {
                        "Plex" => "#E5A00D",
                        "Jellyfin" => "#AA5CC3",
                        "Emby" => "#52B54B",
                        _ => "#7A5CC9",
                    },
                    "#FFFFFF",
                    true,
                    T("Connected") + " · " + s.Kind,
                    MediaServers.Source(s),
                    panel =>
                    {
                        var row = Buttons(panel);
                        var note = Body("", 11);
                        row.Add("Import watched", () => ImportFromConnections(() => _ = ImportServer(s)));
                        row.Add("Test", async () =>
                        {
                            note.Text = T("Testing connection…");
                            try
                            {
                                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                                await api.Test(s, timeout.Token);
                                note.Text = T("Connection working");
                            }
                            catch (Exception)
                            {
                                note.Text = T("Couldn't connect. Check the server address and account permissions.");
                            }
                        });
                        row.Add("Remove", () =>
                        {
                            downloads?.RemoveServer(s.Id);
                            RenderQuiet();
                        });
                        panel.Children.Add(note);
                    }
                )
            );
        }
        media.Children.Add(
            ConnectionCard(
                "Kodi",
                "K",
                "#17B2E7",
                "#FFFFFF",
                kodi != null,
                kodi != null ? T("Connected") + " · " + kodi.Value.Address : T("Not connected"),
                "Kodi's web control must be on (Settings → Services → Control → Allow remote control via HTTP).",
                panel =>
                {
                    KodiControls(panel);
                    if (kodi != null && panel.Children[^1] is WrapPanel done)
                        foreach (
                            var b in done
                                .Children.OfType<Button>()
                                .Where(b => b.Content as string == T("Import watched"))
                        )
                            b.ClearValue(FrameworkElement.StyleProperty);
                }
            )
        );
        if (downloads != null)
            media.Children.Add(
                ConnectionCard(
                    T("Add a media server"),
                    "+",
                    "#2A2A2E",
                    "#E4C084",
                    false,
                    "Plex · Jellyfin · Emby",
                    "Server accounts are saved securely on this PC; passwords are never stored.",
                    panel => AddServerControls(panel, RenderQuiet)
                )
            );

        Group(
            "Import history",
            "Bring in what you watched elsewhere. You review everything before it's added, and re-importing never duplicates."
        );
        var imports = Cards();
        foreach (var s in ImportServices.Where(s => s.Name is not ("Stremio" or "Nuvio" or "Trakt")))
            imports.Children.Add(
                ConnectionCard(
                    s.Name,
                    s.Mark,
                    s.Color,
                    s.Ink,
                    false,
                    s.What,
                    s.How,
                    panel =>
                    {
                        var row = Buttons(panel);
                        if (s.CaptureUrl != null)
                            row.Add("Get from " + s.Name, () => ImportFromConnections(() => CaptureExport(s)));
                        row.Add("Choose file…", () => ImportFromConnections(() => ChooseImportFile(s.Name)));
                    },
                    compact: true
                )
            );

        Group("Downloads & discovery", "Where Noir finds releases, and what powers recommendations.");
        var sources = Cards();
        void Source(string name, string mark, string color, bool on, string what) =>
            sources.Children.Add(
                ConnectionCard(
                    name,
                    mark,
                    color,
                    "#FFFFFF",
                    on,
                    on
                        ? T("Connected")
                            + (
                                config?.Service == name || name == "AIOStreams" && config?.Service == "AIOStreams links"
                                    ? " · " + T("in use for downloads")
                                    : ""
                            )
                        : T("Not connected"),
                    what,
                    panel =>
                        Buttons(panel)
                            .Add(on ? "Manage" : "Connect " + name, () => OpenSettingsSection("Downloads"), !on)
                )
            );
        Source(
            "TorBox",
            "TB",
            "#05A57A",
            HasKey("TorBox"),
            "Your TorBox account supplies the files and searches your existing TorBox library."
        );
        Source(
            "Real-Debrid",
            "RD",
            "#5E9A3A",
            HasKey("Real-Debrid"),
            "Your Real-Debrid account fetches the releases AIOStreams finds."
        );
        Source(
            "AIOStreams",
            "AIO",
            "#3D6FD6",
            config?.Manifest.Length > 0,
            "Finds releases with your AIOStreams add-ons; can also use the debrid services you added there."
        );
        ContentPanel.Children.Add(
            new Border
            {
                Height = 1,
                Background = Brush("#1AFFFFFF"),
                Margin = new(0, 10, 0, 0),
            }
        );
        RenderDiscoverSettings();
    }

    void Group(string title, string subtitle) => ContentPanel.Children.Add(Heading(title, subtitle));

    // Cards share the row evenly: as many ~330 px columns as fit.
    WrapPanel Cards()
    {
        var wrap = new WrapPanel();
        ContentPanel.Children.Add(wrap);
        void Fit()
        {
            var width = wrap.ActualWidth;
            if (width <= 0)
                return;
            var columns = Math.Max(1, (int)(width / 346));
            foreach (FrameworkElement card in wrap.Children)
                card.Width = Math.Floor(width / columns) - 16;
        }
        wrap.SizeChanged += (_, _) => Fit();
        wrap.Loaded += (_, _) => Fit();
        return wrap;
    }

    sealed class ButtonRow(MainWindow owner, WrapPanel row)
    {
        public Button Add(string label, Action act, bool primary = false)
        {
            var b = owner.ActionButton(label, act, primary);
            b.Padding = new(14, 7, 14, 7);
            b.Margin = new(0, 0, 8, 8);
            b.FontSize = 12.5;
            row.Children.Add(b);
            return b;
        }

        // The card's secondary actions, folded into a ⋯ menu.
        public Button More(params (string Label, Action Act)[] items)
        {
            var b = owner.ActionButton("[[more]]", () => { });
            b.Padding = new(11, 3, 11, 8);
            b.Margin = new(0, 0, 8, 8);
            b.FontSize = 16;
            b.ToolTip = T("More");
            System.Windows.Automation.AutomationProperties.SetName(b, T("More"));
            b.Click += (_, _) =>
            {
                var menu = new ContextMenu();
                foreach (var (label, act) in items)
                {
                    var m = new MenuItem { Header = T(label) };
                    var run = act;
                    m.Click += (_, _) => run();
                    menu.Items.Add(m);
                }
                b.ContextMenu = menu;
                menu.PlacementTarget = b;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            };
            row.Children.Add(b);
            return b;
        }
    }

    ButtonRow Buttons(StackPanel panel)
    {
        var row = new WrapPanel();
        panel.Children.Add(row);
        return new(this, row);
    }

    FrameworkElement ConnectionCard(
        string name,
        string mark,
        string color,
        string ink,
        bool on,
        string state,
        string what,
        Action<StackPanel> body,
        bool compact = false,
        string dot = ""
    )
    {
        var card = new Border
        {
            Width = 420,
            Margin = new(0, 0, 16, 16),
            CornerRadius = new(14),
            Background = Brush("#151517"),
            BorderBrush = Brush(on ? "#3DE4C084" : "#1AFFFFFF"),
            BorderThickness = new(1),
            Padding = new(18, 16, 18, 14),
        };
        var stack = new StackPanel();
        card.Child = stack;
        var head = new DockPanel();
        var mono = Monogram(mark, color, ink, 38);
        DockPanel.SetDock(mono, Dock.Left);
        head.Children.Add(mono);
        var names = new StackPanel { Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(names);
        var title = new DockPanel();
        names.Children.Add(title);
        if (on || dot.Length > 0)
        {
            var light = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new(4),
                Background = Brush(
                    dot == "amber" ? "#E4C084"
                    : dot == "grey" ? "#8F8A83"
                    : "#7BD88F"
                ),
                Margin = new(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = T("Connected"),
            };
            DockPanel.SetDock(light, Dock.Right);
            title.Children.Add(light);
        }
        title.Children.Add(Text(name, 15.5, weight: FontWeights.SemiBold));
        var status = Bidi(Text(state, 11.5, on ? "#E4C084" : "#8F8A83"));
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        names.Children.Add(status);
        stack.Children.Add(head);
        if (what.Length > 0)
        {
            var text = Bidi(Text(T(what), 12, "#8F8A83"));
            text.Margin = new(0, 10, 0, 10);
            text.LineHeight = 17;
            if (compact)
                text.MaxHeight = 52;
            stack.Children.Add(text);
        }
        else
            head.Margin = new(0, 0, 0, 10);
        body(stack);
        System.Windows.Automation.AutomationProperties.SetName(card, name);
        return card;
    }

    // Pending watches for one service, folded into one line until opened.
    void Queue(StackPanel panel, string provider)
    {
        var jobs = store
            .State.WatchSync.Values.Where(j => j.Provider == provider && j.SyncedAt == null)
            .OrderByDescending(j => j.WatchedAt)
            .ToList();
        // S4: the list shows the newest 50 with "and N more", however long the queue is.
        var shown = CommunityQueue.Newest(store.State, provider).ToHashSet();
        var deletes = provider == "Nuvio" ? store.State.NuvioDeletes.Count : 0;
        if (jobs.Count + deletes == 0)
            return;
        // Grouped by reason, so one fix (sign in, retry, refresh) covers every watch it blocks.
        var list = new StackPanel { Margin = new(0, 6, 0, 0) };
        var groups = jobs.GroupBy(j => j.Message).OrderByDescending(g => g.Count()).ToList();
        foreach (var group in groups)
        {
            var reason = group.Key;
            var head = new DockPanel { Margin = new(0, 4, 0, 4) };
            var fix = QueueFix(provider, reason, group.ToList());
            if (fix != null)
            {
                fix.Padding = new(10, 4, 10, 4);
                fix.FontSize = 11.5;
                fix.Margin = new(8, 0, 0, 0);
                fix.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(fix, Dock.Right);
                head.Children.Add(fix);
            }
            head.Children.Add(Bidi(Text($"{group.Count()} · {T(reason)}", 12, "#E9E4DC", FontWeights.SemiBold)));
            list.Children.Add(head);
            foreach (var job in group.Where(shown.Contains))
            {
                if (store.State.Catalog.GetValueOrDefault(job.ItemId)?.Item is not { } item)
                    continue;
                var row = new DockPanel { Margin = new(10, 0, 0, 3) };
                var j = job;
                if (provider is "Trakt" or "Serializd" or CommunityQueue.Stremio)
                {
                    var skip = new Button
                    {
                        Content = "[[close]]",
                        Padding = new(7, 1, 7, 1),
                        Margin = new(4, 0, 0, 0),
                        FontSize = 10.5,
                        ToolTip = T("Don't send this one"),
                    };
                    skip.Click += (_, _) =>
                    {
                        store.State.WatchSync.Remove(j.Provider + ":" + j.ItemId);
                        store.Save();
                        RenderQuiet();
                    };
                    DockPanel.SetDock(skip, Dock.Right);
                    row.Children.Add(skip);
                    var missingId = CommunityQueue.NeedsMatch(item);
                    var special = CommunityQueue.IsSpecial(job);
                    // Stremio has no page to open; only a title without an IMDb ID gets Match.
                    if (provider != CommunityQueue.Stremio || missingId)
                    {
                        var open = new Button
                        {
                            Content = missingId ? T("Match this title", "طابق هذا العنوان") : "[[external]]",
                            Padding = new(7, 1, 7, 1),
                            FontSize = 10.5,
                            ToolTip =
                                missingId ? T("Correct match / episode numbers")
                                : special
                                    ? T(
                                        "Open the page to mark this special by hand",
                                        "افتح الصفحة لتعليم هذه الحلقة الخاصة يدويًا"
                                    )
                                : T("Open on ") + provider,
                        };
                        open.Click += (_, _) =>
                        {
                            if (missingId)
                                MatchDialog(item);
                            else if (special)
                                OpenPage(ReviewLinks.TraktUrl(item));
                            else
                                OpenReviews(item, provider);
                        };
                        DockPanel.SetDock(open, Dock.Right);
                        row.Children.Add(open);
                    }
                }
                var label = Text(item.Label + (job.Attempts > 1 ? $"  ·  ×{job.Attempts}" : ""), 12, "#B8B2AA");
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.TextWrapping = TextWrapping.NoWrap;
                label.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(Bidi(label));
                list.Children.Add(row);
            }
        }
        if (jobs.Count > shown.Count)
            list.Children.Add(Text($"   …{T("and")} {(jobs.Count - shown.Count):N0} {T("more")}", 11.5, "#8F8A83"));
        if (deletes > 0)
            list.Children.Add(
                Text($"{deletes} {T("un-checked items waiting to be removed in Nuvio")}", 11.5, "#8F8A83")
            );
        var expander = new Expander
        {
            Header = Text(T("Show waiting", "اعرض ما ينتظر"), 12, "#E4C084"),
            Content = new ScrollViewer
            {
                Content = list,
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            IsExpanded = openQueues.Contains(provider),
            Foreground = Brush("#E4C084"),
            Margin = new(0, 2, 0, 0),
        };
        ((TextBlock)expander.Header).TextTrimming = TextTrimming.CharacterEllipsis;
        ((TextBlock)expander.Header).TextWrapping = TextWrapping.NoWrap;
        expander.Expanded += (_, _) => openQueues.Add(provider);
        expander.Collapsed += (_, _) => openQueues.Remove(provider);
        panel.Children.Add(expander);
    }

    // U84/U85: one state in the header (counts are on the counts line), the dot green only after a good attempt.
    (string Text, string Dot) SiteHeaderFor(string provider)
    {
        var header = CommunityQueue.Header(
            store.State,
            watchSync?.Backoff ?? new CommunityBackoff(),
            provider,
            watchSync?.Clock() ?? DateTimeOffset.UtcNow,
            Loc.Arabic,
            Loc.Culture
        );
        return (header.Text, header.Dot);
    }

    // The Trakt / Serializd card: Settings > Connections and History > Import build it the same way (U86).
    FrameworkElement SyncSiteCard(string provider)
    {
        var (mark, color, what) =
            provider == "Trakt"
                ? (
                    "T",
                    "#ED2224",
                    "Each movie or episode you finish or check off is marked watched on trakt.tv, through your website sign-in."
                )
                : ("Sz", "#1E9BE9", "Each episode you finish or check off is marked watched on Serializd (TV only).");
        var on = provider == "Trakt" ? store.State.SyncTrakt : store.State.SyncSerializd;
        var header = SiteHeaderFor(provider);
        return ConnectionCard(
            provider,
            mark,
            color,
            "#FFFFFF",
            on,
            header.Text,
            what,
            panel =>
            {
                var check = new CheckBox
                {
                    Content = T("Mark my watches on ") + provider,
                    IsChecked = on,
                    Margin = new(0, 0, 0, 8),
                };
                check.Click += (_, _) =>
                {
                    if (provider == "Trakt")
                        store.State.SyncTrakt = check.IsChecked == true;
                    else
                        store.State.SyncSerializd = check.IsChecked == true;
                    store.Save();
                    watchSync?.Retry();
                    RenderQuiet();
                };
                panel.Children.Add(check);
                var row = Buttons(panel);
                if (!on)
                {
                    row.Add("Connect " + provider, () => OpenAccountSignIn(provider), true);
                    if (provider == "Trakt")
                        row.Add("Import Trakt export…", () => ImportFromConnections(() => ChooseImportFile("Trakt")));
                }
                else
                {
                    row.Add("Sync now", () =>
                    {
                        watchSync?.Retry();
                        Status.Text = provider + ": " + T("syncing waiting watches…");
                    });
                    var extra = new List<(string, Action)> { ("Sign in again", () => OpenAccountSignIn(provider)) };
                    if (provider == "Trakt")
                        extra.Add(
                            ("Import Trakt export…", () => ImportFromConnections(() => ChooseImportFile("Trakt")))
                        );
                    row.More(extra.ToArray());
                }
                SiteLines(panel, provider, on);
                Queue(panel, provider);
            },
            dot: header.Dot
        );
    }

    FrameworkElement StremioCard()
    {
        var stremio = StremioKey() != null;
        return ConnectionCard(
            "Stremio",
            "S",
            "#7B5BF5",
            "#FFFFFF",
            stremio,
            stremio
                ? T("Connected")
                    + " · "
                    + (store.State.StremioImportedAt is { } at ? T("checked") + " " + Ago(at) : T("not checked yet"))
                : T("Not connected"),
            stremio
                ? T(
                    "New watches in Stremio come into Noir every few hours. Noir can also mark what you watch in your Stremio account.",
                    "تصل المشاهدات الجديدة في Stremio إلى Noir كل بضع ساعات. ويمكن لـ Noir أيضًا تعليم ما تشاهده في حساب Stremio."
                )
                : "New watches in Stremio come into Noir every few hours. Stremio can't receive watches from other apps.",
            panel =>
            {
                var row = Buttons(panel);
                if (!stremio)
                {
                    row.Add("Connect Stremio", ConnectStremio, true);
                    return;
                }
                var auto = new CheckBox
                {
                    Content = T("Check Stremio automatically"),
                    IsChecked = store.State.StremioAutoImport,
                    Margin = new(0, 0, 0, 8),
                };
                auto.Click += (_, _) =>
                {
                    store.State.StremioAutoImport = auto.IsChecked == true;
                    store.Save();
                };
                panel.Children.Insert(panel.Children.Count - 1, auto);
                row.Add("Sync now", async () =>
                {
                    Status.Text = T("Checking Stremio for new watches…");
                    await ImportStremio(true);
                    if (Status.Text == T("Checking Stremio for new watches…"))
                        Status.Text = T("Stremio is up to date.");
                    RenderQuiet();
                });
                row.Add("Review & import", () => ImportFromConnections(() => _ = ImportStremio(false)));
                row.More(("Disconnect", () =>
                    {
                        try
                        {
                            File.Delete(StremioKeyPath);
                        }
                        catch (IOException) { }
                        // Without a key nothing can be marked: the switch goes off with the connection.
                        CommunityQueue.SetStremio(store.State, false, null);
                        store.Save();
                        RenderQuiet();
                    }));
                StremioMarkControls(panel, RenderQuiet);
            }
        );
    }

    // H2: Harbor has no service of its own; its watched state lives in the Stremio account or Trakt it is signed in to.
    FrameworkElement HarborCard() =>
        ConnectionCard(
            "Harbor",
            "H",
            "#2F6B8F",
            "#FFFFFF",
            false,
            T("Through Stremio or Trakt", "عبر Stremio أو Trakt"),
            "",
            HarborRoutes
        );

    void HarborRoutes(StackPanel panel)
    {
        panel.Children.Add(
            Body(
                T(
                    "Use your Stremio account to bring in what Harbor marked watched there. Use Trakt so what you watch in Noir shows in Harbor too.",
                    "استخدم حساب Stremio لإحضار ما علّمه Harbor كمُشاهَد هناك. استخدم Trakt ليظهر ما تشاهده في Noir في Harbor أيضًا."
                ),
                11.5
            )
        );
        var row = Buttons(panel);
        row.Add(
            T("Use my Stremio account", "استخدم حساب Stremio"),
            () =>
                ImportFromConnections(() =>
                {
                    if (StremioKey() == null)
                        ConnectStremio();
                    else
                        _ = ImportStremio(false);
                }),
            true
        );
        row.Add(T("Use Trakt", "استخدم Trakt"), () =>
        {
            settingsSection = "Connections";
            Navigate("Settings");
        });
    }

    // A page opened in the user's own browser (specials are marked by hand on the website).
    void OpenPage(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https")
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }
                );
        }
        catch (Exception e)
        {
            store.Log(e);
        }
    }

    // F13: "Serializd has not synced since 5 Oct · 137 waiting" with Sign in; one row per stopped website.
    FrameworkElement NoticeRow(CommunityQueue.SiteNotice n, bool withWaiting = true)
    {
        var row = new DockPanel { Margin = new(0, 6, 0, 6) };
        var sign =
            n.Provider == CommunityQueue.Stremio
                ? ActionButton(T("Connect Stremio again", "اربط Stremio مجددًا"), ConnectStremio)
                : ActionButton(T("Sign in", "تسجيل الدخول"), () => OpenAccountSignIn(n.Provider));
        sign.Padding = new(10, 4, 10, 4);
        sign.FontSize = 11.5;
        sign.Margin = new(10, 0, 0, 0);
        sign.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(sign, Dock.Right);
        row.Children.Add(sign);
        var text = Bidi(Text(CommunityQueue.NoticeText(n, Loc.Arabic, Loc.Culture, withWaiting), 12, "#E4C084"));
        text.TextWrapping = TextWrapping.Wrap;
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return row;
    }

    /// <summary>The History header line for websites that stopped syncing; null when none did.</summary>
    FrameworkElement? SyncNoticeLine()
    {
        if (watchSync == null)
            return null;
        var notices = CommunityQueue.Notices(store.State, watchSync.Backoff, watchSync.Clock());
        if (notices.Count == 0)
            return null;
        var stack = new StackPanel();
        foreach (var n in notices)
            stack.Children.Add(NoticeRow(n));
        return new Border
        {
            Child = stack,
            CornerRadius = new(10),
            Padding = new(14, 2, 14, 2),
            Margin = new(0, 10, 0, 0),
            Background = Brush("#1AE4C084"),
            BorderBrush = Brush("#33E4C084"),
            BorderThickness = new(1),
        };
    }

    // One website's line of numbers (S4), its stopped-site notice (F13) and the past-history controls (S3).
    void SiteLines(StackPanel panel, string provider, bool enabled)
    {
        if (watchSync == null)
            return;
        var now = watchSync.Clock();
        var backoff = watchSync.Backoff;
        var status = CommunityQueue.Status(store.State, backoff, provider, now);
        var line = Bidi(Text(CommunityQueue.StatusText(status, Loc.Arabic, Loc.Culture), 11.5, "#B8B2AA"));
        line.TextWrapping = TextWrapping.Wrap;
        line.Margin = new(0, 2, 0, 2);
        panel.Children.Add(line);
        if (CommunityQueue.Notices(store.State, backoff, now).FirstOrDefault(n => n.Provider == provider) is { } notice)
            panel.Children.Add(NoticeRow(notice, false));
        if (!enabled)
            return;
        var row = new WrapPanel { Margin = new(0, 4, 0, 4) };
        if (status.Backlog > 0)
        {
            var paused = backoff.BacklogPaused(provider);
            var pause = ActionButton(
                paused
                    ? T("Resume past history", "استئناف السجل السابق")
                    : T("Pause past history", "إيقاف السجل السابق مؤقتًا"),
                () =>
                {
                    watchSync.PausePrevious(provider, !paused);
                    RenderQuiet();
                }
            );
            pause.Margin = new(0, 0, 8, 0);
            row.Children.Add(pause);
            row.Children.Add(ActionButton(T("Cancel past history", "إلغاء السجل السابق"), () =>
                {
                    var removed = watchSync.CancelPrevious(provider);
                    Status.Text = T(
                        "{0} unsent past titles were removed from the queue. Anything already sent stays sent.",
                        "أُزيل {0} من العناوين السابقة غير المرسلة من الطابور. ما أُرسل يبقى مُرسلًا.",
                        removed
                    );
                    RenderQuiet();
                }));
        }
        else if (CommunityQueue.PlanBacklog(store.State, provider).Titles > 0)
            row.Children.Add(
                ActionButton(T("Also send my past history", "أرسل سجلّي السابق أيضًا"), () =>
                    ConfirmPastHistory(provider)
                )
            );
        if (row.Children.Count > 0)
            panel.Children.Add(row);
    }

    // The Stremio account's mark-watched switches (shown only while a Stremio key exists), its status line,
    // notices and queue like the other sites, and Restore for every title Noir changed there.
    void StremioMarkControls(StackPanel panel, Action rerender)
    {
        var on = CommunityQueue.StremioMarkOn(store.State);
        var mark = new CheckBox
        {
            Content = new TextBlock
            {
                Text = T(
                    "Mark watched in your Stremio account (also seen by Harbor)",
                    "علّم المُشاهَد في حساب Stremio (يراه Harbor أيضًا)"
                ),
                TextWrapping = TextWrapping.Wrap,
            },
            IsChecked = on,
            Margin = new(0, 8, 0, 4),
        };
        var add = new CheckBox
        {
            Content = new TextBlock
            {
                Text = T("Add watched titles to my Stremio library", "أضف العناوين المُشاهَدة إلى مكتبتي في Stremio"),
                TextWrapping = TextWrapping.Wrap,
            },
            IsChecked = CommunityQueue.StremioAddTitles(store.State),
            IsEnabled = on,
            Margin = new(22, 0, 0, 4),
        };
        // S3: switching on sends only what is watched from now on; past history is its own button below.
        mark.Click += (_, _) =>
        {
            CommunityQueue.SetStremio(store.State, mark.IsChecked == true, null);
            store.Save();
            watchSync?.Retry();
            rerender();
        };
        add.Click += (_, _) =>
        {
            CommunityQueue.SetStremio(store.State, null, add.IsChecked == true);
            store.Save();
            watchSync?.Retry();
            rerender();
        };
        panel.Children.Add(mark);
        panel.Children.Add(add);
        var note = Body(
            T(
                "New: try it on one title first, then check it in Stremio.",
                "جديد: جرّبه على عنوان واحد أولًا، ثم تحقق منه في Stremio."
            ),
            11
        );
        note.Foreground = Brush("#E4C084");
        note.Margin = new(0, 0, 0, 4);
        panel.Children.Add(note);
        SiteLines(panel, CommunityQueue.Stremio, on);
        if (on && StremioSync.NeedsReconnect(store.State))
        {
            var again = ActionButton(T("Connect Stremio again", "اربط Stremio مجددًا"), ConnectStremio, true);
            again.Margin = new(0, 4, 0, 4);
            again.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(again);
        }
        Queue(panel, CommunityQueue.Stremio);
        var restorable = StremioSync.Restorable(store.State, store.DataDirectory);
        if (restorable.Count == 0)
            return;
        var list = new StackPanel { Margin = new(0, 4, 0, 0) };
        foreach (var (id, item) in restorable)
        {
            var row = new DockPanel { Margin = new(10, 0, 0, 3) };
            var restore = ActionButton(T("Restore", "استعادة"), () => ConfirmStremioRestore(id, item, rerender));
            restore.Padding = new(10, 3, 10, 3);
            restore.FontSize = 11;
            restore.Margin = new(8, 0, 0, 0);
            DockPanel.SetDock(restore, Dock.Right);
            row.Children.Add(restore);
            var name = Bidi(Text(item.Title, 12, "#B8B2AA"));
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            list.Children.Add(row);
        }
        panel.Children.Add(
            new Expander
            {
                Header = Text(
                    T("Changed in your Stremio account · {0}", "تغيّر في حساب Stremio · {0}", restorable.Count),
                    12,
                    "#C7C2BC"
                ),
                Content = list,
                Foreground = Brush("#C7C2BC"),
                Margin = new(0, 2, 0, 0),
            }
        );
    }

    // Restore puts back the copy Noir saved before its first change, unless the title changed in Stremio since.
    void ConfirmStremioRestore(string imdbId, VideoItem item, Action rerender)
    {
        var body = SmartPopup();
        body.Children.Add(
            Text(T("Restore {0} in Stremio?", "استعادة {0} في Stremio؟", item.Title), 15, weight: FontWeights.SemiBold)
        );
        body.Children.Add(
            Body(
                T(
                    "Puts this title back in your Stremio account as it was before Noir first changed it. If it changed in Stremio since Noir's last change, it is left as it is.",
                    "يعيد هذا العنوان في حساب Stremio كما كان قبل أول تغيير من Noir. وإن تغيّر في Stremio بعد آخر تغيير من Noir فيُترك كما هو."
                ),
                12
            )
        );
        var row = new WrapPanel { Margin = new(0, 10, 0, 0) };
        body.Children.Add(row);
        var go = ActionButton(
            T("Restore", "استعادة"),
            async () =>
            {
                CloseSmart();
                if (watchSync == null)
                    return;
                var summary = await watchSync.RestoreStremio(imdbId);
                Status.Text = summary.Text(Loc.Arabic);
                rerender();
            },
            true
        );
        go.Margin = new(0, 0, 8, 0);
        row.Children.Add(go);
        row.Children.Add(ActionButton(T("Cancel", "إلغاء"), CloseSmart));
    }

    // S3: say how many titles and how long before anything is queued.
    void ConfirmPastHistory(string provider)
    {
        var plan = CommunityQueue.PlanBacklog(store.State, provider);
        var body = SmartPopup();
        body.Children.Add(
            Text(
                T("Also send my past history to {0}?", "هل تريد إرسال سجلّي السابق إلى {0} أيضًا؟", provider),
                15,
                weight: FontWeights.SemiBold
            )
        );
        body.Children.Add(Body(CommunityQueue.BacklogText(plan, Loc.Arabic, Loc.Culture), 13));
        body.Children.Add(
            Body(
                T(
                    "It runs in the background, newest first, at a pace the website can live with. Pause or cancel any time: what was already sent stays sent.",
                    "يعمل في الخلفية بدءًا من الأحدث وبوتيرة يتحملها الموقع. يمكنك الإيقاف أو الإلغاء في أي وقت، وما أُرسل يبقى مُرسلًا."
                ),
                12
            )
        );
        var row = new WrapPanel { Margin = new(0, 10, 0, 0) };
        body.Children.Add(row);
        var go = ActionButton(
            T("Send", "إرسال"),
            () =>
            {
                CloseSmart();
                watchSync?.QueuePrevious(provider);
                RenderQuiet();
            },
            true
        );
        go.IsEnabled = plan.Titles > 0;
        go.Margin = new(0, 0, 8, 0);
        row.Children.Add(go);
        row.Children.Add(ActionButton(T("Cancel", "إلغاء"), CloseSmart));
    }

    Button? QueueFix(string provider, string reason, List<WatchSyncJob> jobs)
    {
        if (provider == CommunityQueue.Stremio && StremioSync.IsReconnect(reason))
            return ActionButton(T("Connect Stremio again", "اربط Stremio مجددًا"), ConnectStremio);
        if (reason.Contains("session has expired"))
            return ActionButton("Sign in again", () => OpenAccountSignIn(provider));
        if (reason.Contains("verified title ID"))
            return ActionButton("Refresh information", () =>
            {
                _ = RefreshMetadata(true);
                Status.Text = T("Refreshing information. Titles that still don't match need Fix match.");
            });
        if (reason.StartsWith("Specials") || reason.Contains("match needs checking"))
            return null;
        return ActionButton("Retry now", () =>
        {
            foreach (var j in jobs)
                j.RetryAt = DateTimeOffset.MinValue;
            store.Save();
            if (provider == "Nuvio")
                nuvioSync?.Wake();
            else
                watchSync?.Wake();
            Status.Text = provider + ": " + T("retrying") + $" {jobs.Count}…";
            RenderQuiet();
        });
    }

    FrameworkElement NuvioCard()
    {
        var session = NuvioSessionNow();
        var on = session is { Profile: > 0 } && store.State.NuvioConnected;
        var state = on
            ? T("Connected")
                + $" · {(session!.ProfileName.Length > 0 ? session.ProfileName : "profile " + session.Profile)} · "
                + (store.State.NuvioImportedAt is { } at ? T("checked") + " " + Ago(at) : T("not checked yet"))
            : T("Not connected");
        return ConnectionCard(
            "Nuvio",
            "Nv",
            "#6C4CF1",
            "#FFFFFF",
            on,
            state,
            on
                ? "Two-way: Nuvio's watches come into Noir every few hours, and what you finish or check off in Noir is marked watched in Nuvio."
                : "Link with a code — no password. Watches flow both ways.",
            panel =>
            {
                var row = Buttons(panel);
                if (!on)
                {
                    row.Add("Connect Nuvio", ConnectNuvio, true);
                    return;
                }
                var auto = new CheckBox
                {
                    Content = T("Check Nuvio automatically"),
                    IsChecked = store.State.NuvioAutoImport,
                    Margin = new(0, 0, 0, 2),
                };
                auto.Click += (_, _) =>
                {
                    store.State.NuvioAutoImport = auto.IsChecked == true;
                    store.Save();
                };
                var send = new CheckBox
                {
                    Content = T("Mark my Noir watches in Nuvio"),
                    IsChecked = store.State.SyncNuvio,
                    Margin = new(0, 2, 0, 8),
                };
                send.Click += (_, _) =>
                {
                    store.State.SyncNuvio = send.IsChecked == true;
                    store.Save();
                    nuvioSync?.Wake();
                    RenderQuiet();
                };
                panel.Children.Insert(panel.Children.Count - 1, auto);
                panel.Children.Insert(panel.Children.Count - 1, send);
                row.Add("Sync now", async () =>
                {
                    nuvioSync?.Wake();
                    Status.Text = T("Checking Nuvio for new watches…");
                    await ImportNuvio(true);
                    if (Status.Text == T("Checking Nuvio for new watches…"))
                        Status.Text = T("Nuvio is up to date.");
                    RenderQuiet();
                }).IsEnabled = !importBusy;
                row.Add("Review & import", () => ImportFromConnections(() => _ = ImportNuvio(false))).IsEnabled =
                    !importBusy;
                row.More(("Send earlier history", () =>
                    {
                        // Nuvio's own past-history send: queues Nuvio only, never the other sites (S3).
                        foreach (var e in store.State.Catalog.Values.Where(e => store.EverWatched(e.Item)))
                            CommunityQueue.Enqueue(
                                store.State,
                                e.Item,
                                store.ViewingHistory(e.Item).FirstOrDefault()?.WatchedAt
                                    ?? e.Progress?.UpdatedAt
                                    ?? DateTimeOffset.UtcNow,
                                "Nuvio"
                            );
                        store.Save();
                        nuvioSync?.Wake();
                        RenderQuiet();
                    }), ("Switch profile", () => _ = PickNuvioProfile(session!)), ("Disconnect", () =>
                    {
                        NuvioStorage.Forget(store.DataDirectory);
                        store.State.NuvioConnected = false;
                        store.Save();
                        RenderQuiet();
                    }));
                if (nuvioSync?.Message is { Length: > 0 } m)
                    panel.Children.Add(Body(m, 11));
                Queue(panel, "Nuvio");
            }
        );
    }

    // Imports finish on History → Add history, where the review card lives.
    void ImportFromConnections(Action start)
    {
        // Already on History > Import: no navigation, so the page does not jump.
        if (!(page == "History" && trackerTab == "Import"))
        {
            historyType = "All";
            trackerTab = "Import";
            Navigate("History");
        }
        start();
    }

    async void SyncAllNow()
    {
        watchSync?.Retry();
        nuvioSync?.Wake();
        Status.Text = T("Syncing everything…");
        if (store.State.NuvioConnected && !importBusy)
            await ImportNuvio(true);
        if (StremioKey() != null && !importBusy)
            await ImportStremio(true);
        if (Status.Text == T("Syncing everything…"))
            Status.Text = T("Sync started. Waiting watches are being sent; new watches were pulled in.");
        if (page == "Settings")
            RenderQuiet();
    }
}
