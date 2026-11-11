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
        foreach (
            var (provider, mark, color, what) in new[]
            {
                (
                    "Trakt",
                    "T",
                    "#ED2224",
                    "Each movie or episode you finish or check off is marked watched on trakt.tv, through your website sign-in."
                ),
                (
                    "Serializd",
                    "Sz",
                    "#1E9BE9",
                    "Each episode you finish or check off is marked watched on Serializd (TV only)."
                ),
            }
        )
        {
            var on = provider == "Trakt" ? store.State.SyncTrakt : store.State.SyncSerializd;
            var jobs = store.State.WatchSync.Values.Where(j => j.Provider == provider).ToList();
            var sent = jobs.Count(j => j.SyncedAt != null);
            var last = jobs.Where(j => j.SyncedAt != null).Select(j => j.SyncedAt!.Value).DefaultIfEmpty().Max();
            sync.Children.Add(
                ConnectionCard(
                    provider,
                    mark,
                    color,
                    "#FFFFFF",
                    on,
                    on
                        ? $"{T("Sending")} · {sent:N0} {T("confirmed")}"
                            + (last > DateTimeOffset.MinValue ? " · " + T("last") + " " + Ago(last) : "")
                        : T("Off"),
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
                                row.Add("Import Trakt export…", () =>
                                    ImportFromConnections(() => ChooseImportFile("Trakt"))
                                );
                        }
                        else
                        {
                            row.Add("Sync now", () =>
                            {
                                watchSync?.Retry();
                                Status.Text = provider + ": " + T("syncing waiting watches…");
                            });
                            var extra = new List<(string, Action)>
                            {
                                ("Sign in again", () => OpenAccountSignIn(provider)),
                                ("Send earlier history", () =>
                                {
                                    watchSync?.QueuePrevious();
                                    RenderQuiet();
                                }),
                            };
                            if (provider == "Trakt")
                                extra.Add(
                                    ("Import Trakt export…", () =>
                                        ImportFromConnections(() => ChooseImportFile("Trakt"))
                                    )
                                );
                            row.More(extra.ToArray());
                        }
                        Queue(panel, provider);
                    }
                )
            );
        }
        sync.Children.Add(NuvioCard());
        sync.Children.Add(
            ConnectionCard(
                "Stremio",
                "S",
                "#7B5BF5",
                "#FFFFFF",
                stremio,
                stremio
                    ? T("Connected")
                        + " · "
                        + (
                            store.State.StremioImportedAt is { } at
                                ? T("checked") + " " + Ago(at)
                                : T("not checked yet")
                        )
                    : T("Not connected"),
                "New watches in Stremio come into Noir every few hours. Stremio can't receive watches from other apps.",
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
                            RenderQuiet();
                        }));
                }
            )
        );

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
        bool compact = false
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
        if (on)
        {
            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new(4),
                Background = Brush("#7BD88F"),
                Margin = new(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = T("Connected"),
            };
            DockPanel.SetDock(dot, Dock.Right);
            title.Children.Add(dot);
        }
        title.Children.Add(Text(name, 15.5, weight: FontWeights.SemiBold));
        var status = Bidi(Text(state, 11.5, on ? "#E4C084" : "#8F8A83"));
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        names.Children.Add(status);
        stack.Children.Add(head);
        var text = Bidi(Text(T(what), 12, "#8F8A83"));
        text.Margin = new(0, 10, 0, 10);
        text.LineHeight = 17;
        if (compact)
            text.MaxHeight = 52;
        stack.Children.Add(text);
        body(stack);
        System.Windows.Automation.AutomationProperties.SetName(card, name);
        return card;
    }

    // Pending watches for one service, folded into one line until opened.
    void Queue(StackPanel panel, string provider)
    {
        var jobs = store
            .State.WatchSync.Values.Where(j => j.Provider == provider && j.SyncedAt == null)
            .OrderBy(j => j.WatchedAt)
            .ToList();
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
            head.Children.Add(Bidi(Text($"{group.Count()} · {reason}", 12, "#E9E4DC", FontWeights.SemiBold)));
            list.Children.Add(head);
            foreach (var job in group.Take(25))
            {
                if (store.State.Catalog.GetValueOrDefault(job.ItemId)?.Item is not { } item)
                    continue;
                var row = new DockPanel { Margin = new(10, 0, 0, 3) };
                var j = job;
                if (provider is "Trakt" or "Serializd")
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
                    var missingId = !System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId, @"^tt\d+$");
                    var open = new Button
                    {
                        Content = missingId ? T("Fix match") : "[[external]]",
                        Padding = new(7, 1, 7, 1),
                        FontSize = 10.5,
                        ToolTip = missingId ? T("Correct match / episode numbers") : T("Open on ") + provider,
                    };
                    open.Click += (_, _) =>
                    {
                        if (missingId)
                            MatchDialog(item);
                        else
                            OpenReviews(item, provider);
                    };
                    DockPanel.SetDock(open, Dock.Right);
                    row.Children.Add(open);
                }
                var label = Text(item.Label + (job.Attempts > 1 ? $"  ·  ×{job.Attempts}" : ""), 12, "#B8B2AA");
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.TextWrapping = TextWrapping.NoWrap;
                label.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(Bidi(label));
                list.Children.Add(row);
            }
            if (group.Count() > 25)
                list.Children.Add(Text($"   …{T("and")} {group.Count() - 25} {T("more")}", 11.5, "#8F8A83"));
        }
        if (deletes > 0)
            list.Children.Add(
                Text($"{deletes} {T("un-checked items waiting to be removed in Nuvio")}", 11.5, "#8F8A83")
            );
        var summary =
            groups.Count == 1 ? groups[0].Key
            : groups.Count > 1 ? $"{groups.Count} {T("reasons")}"
            : "";
        var expander = new Expander
        {
            Header = Text(
                $"{jobs.Count + deletes} {T("waiting")}" + (summary.Length > 0 ? " · " + summary : ""),
                12,
                "#E4C084"
            ),
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

    Button? QueueFix(string provider, string reason, List<WatchSyncJob> jobs)
    {
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
                        foreach (var e in store.State.Catalog.Values.Where(e => store.EverWatched(e.Item)))
                            CommunityQueue.Enqueue(
                                store.State,
                                e.Item,
                                store.ViewingHistory(e.Item).FirstOrDefault()?.WatchedAt
                                    ?? e.Progress?.UpdatedAt
                                    ?? DateTimeOffset.UtcNow
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
        historyType = "All";
        trackerTab = "Import";
        Navigate("History");
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
