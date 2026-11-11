using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Noir;

// History → Import & sync: bring in other services' records, keep Stremio and the website
// accounts in step, and export everything in formats other apps accept.
public partial class MainWindow
{
    sealed record ServiceCard(
        string Name,
        string Mark,
        string Color,
        string Ink,
        string What,
        string How,
        string? CaptureUrl = null
    );

    static readonly ServiceCard[] ImportServices =
    [
        new(
            "Netflix",
            "N",
            "#E50914",
            "#FFFFFF",
            "Everything in your viewing activity",
            "Sign in and press Download all — Noir picks up the file. Or choose a NetflixViewingHistory.csv you already have.",
            "https://www.netflix.com/viewingactivity"
        ),
        new(
            "Stremio",
            "S",
            "#7B5BF5",
            "#FFFFFF",
            "Library and watched episodes",
            "Sign in once. Noir reads your library directly and keeps checking for new watches."
        ),
        new(
            "Nuvio",
            "Nv",
            "#6C4CF1",
            "#FFFFFF",
            "Watched list, library — both ways",
            "Link with a code at nuvio.tv/link. Noir pulls Nuvio's watches and marks yours watched in Nuvio."
        ),
        new(
            "Trakt",
            "T",
            "#ED2224",
            "#FFFFFF",
            "Watched history export (JSON)",
            "From trakt.tv → Settings → Data, or any Trakt history JSON."
        ),
        new(
            "TV Time",
            "TV",
            "#FFD000",
            "#16120C",
            "Your data export (zip or CSV)",
            "In TV Time: Settings → Privacy → request your data, then choose the zip they send."
        ),
        new(
            "Letterboxd",
            "L",
            "#00C030",
            "#FFFFFF",
            "Diary, watched films and ratings",
            "Sign in and press Export your data — Noir picks up the zip. Or choose the export zip.",
            "https://letterboxd.com/settings/data/"
        ),
        new(
            "IMDb",
            "IMDb",
            "#F5C518",
            "#16120C",
            "Your ratings list (CSV)",
            "imdb.com → Your ratings → ⋯ → Export. Rated titles are logged as watched."
        ),
        new("Simkl", "SK", "#1E2A3A", "#FFFFFF", "Backup (JSON)", "simkl.com → Settings → Backup & export."),
        new(
            "Spreadsheet",
            "CSV",
            "#3A3833",
            "#E9E4DC",
            "Any CSV with titles and dates",
            "Columns like title, year, season, episode, date and rating are recognised automatically."
        ),
    ];
    bool importBusy;
    string importMessage = "";
    (int Done, int Total) importProgress;
    ImportPlan? importPlan;
    CancellationTokenSource? importCancel;

    void RenderImportSync()
    {
        ContentPanel.Children.Add(
            Heading(
                "Import from another app",
                "Matched against Cinemeta by IMDb ID or title. Re-importing never duplicates anything, and nothing is downloaded."
            )
        );
        var grid = new WrapPanel();
        ContentPanel.Children.Add(grid);
        foreach (var service in ImportServices)
            grid.Children.Add(ServiceTile(service));
        ContentPanel.Children.Add(Heading("Keep in sync", "What's possible differs by service."));
        // U86: the same cards, built by the same code, as Settings > Connections.
        var sync = Cards();
        sync.Children.Add(StremioCard());
        sync.Children.Add(NuvioCard());
        sync.Children.Add(HarborCard());
        sync.Children.Add(SyncSiteCard("Trakt"));
        sync.Children.Add(SyncSiteCard("Serializd"));
        sync.Children.Add(
            SyncTile(
                "Netflix",
                "N",
                "#E50914",
                "#FFFFFF",
                "Import only",
                "Netflix has no way for other apps to read or write your history. Re-import any time: only new viewings are added.",
                panel =>
                    panel.Children.Add(ActionButton("Get latest from Netflix", () => CaptureExport(ImportServices[0])))
            )
        );
        RenderExports();
        RenderBackups();
        RenderSources();
        ContentPanel.Children.Add(
            Body(
                "Exports and imports stay on this PC. Website sign-ins share the browser profile used for Reviews & comments.",
                11
            )
        );
    }

    FrameworkElement Monogram(string mark, string color, string ink, double size = 44) =>
        new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new(size / 4),
            Background = Brush(color),
            Child = new TextBlock
            {
                Text = mark,
                FontSize = mark.Length > 2 ? size * .3 : size * .42,
                FontWeight = FontWeights.Bold,
                Foreground = Brush(ink),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

    FrameworkElement ServiceTile(ServiceCard s)
    {
        var card = new Border
        {
            Width = 372,
            MinHeight = 196,
            Margin = new(0, 0, 16, 16),
            CornerRadius = new(14),
            Background = Brush("#161618"),
            BorderBrush = Brush("#22FFFFFF"),
            BorderThickness = new(1),
            Padding = new(20, 18, 20, 18),
        };
        var stack = new StackPanel();
        card.Child = stack;
        var head = new DockPanel();
        var mono = Monogram(s.Mark, s.Color, s.Ink);
        DockPanel.SetDock(mono, Dock.Left);
        head.Children.Add(mono);
        var names = new StackPanel { Margin = new(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Text(s.Name, 17, weight: FontWeights.SemiBold));
        names.Children.Add(Text(T(s.What), 12, "#AAA6A0"));
        head.Children.Add(names);
        stack.Children.Add(head);
        var how = Text(T(s.How), 12.5, "#8F8A83");
        how.Margin = new(0, 12, 0, 14);
        how.LineHeight = 18;
        stack.Children.Add(how);
        var row = new WrapPanel();
        stack.Children.Add(row);
        if (s.Name == "Nuvio")
        {
            row.Children.Add(
                ActionButton(
                    store.State.NuvioConnected ? "Import from Nuvio" : "Connect Nuvio",
                    () =>
                    {
                        if (store.State.NuvioConnected)
                            _ = ImportNuvio(false);
                        else
                            ConnectNuvio();
                    },
                    true
                )
            );
        }
        else if (s.Name == "Stremio")
        {
            row.Children.Add(
                ActionButton(
                    StremioKey() == null ? "Connect Stremio" : "Import from Stremio",
                    () =>
                    {
                        if (StremioKey() == null)
                            ConnectStremio();
                        else
                            _ = ImportStremio(false);
                    },
                    true
                )
            );
        }
        else
        {
            if (s.CaptureUrl != null)
                row.Children.Add(ActionButton("Get from " + s.Name, () => CaptureExport(s), true));
            var file = ActionButton("Choose file…", () => ChooseImportFile(s.Name), s.CaptureUrl == null);
            file.Margin = new(s.CaptureUrl != null ? 8 : 0, 0, 0, 0);
            row.Children.Add(file);
        }
        foreach (Button b in row.Children)
        {
            b.IsEnabled = !importBusy;
            b.Padding = new(16, 9, 16, 9);
        }
        card.MouseEnter += (_, _) => card.BorderBrush = Brush("#55E4C084");
        card.MouseLeave += (_, _) => card.BorderBrush = Brush("#22FFFFFF");
        return card;
    }

    FrameworkElement SyncTile(
        string name,
        string mark,
        string color,
        string ink,
        string state,
        string what,
        Action<StackPanel> body
    )
    {
        var card = new Border
        {
            Width = 372,
            Margin = new(0, 0, 16, 16),
            CornerRadius = new(14),
            Background = Brush("#141416"),
            BorderBrush = Brush("#1AFFFFFF"),
            BorderThickness = new(1),
            Padding = new(20, 18, 20, 18),
        };
        var stack = new StackPanel();
        card.Child = stack;
        var head = new DockPanel();
        var mono = Monogram(mark, color, ink, 36);
        DockPanel.SetDock(mono, Dock.Left);
        head.Children.Add(mono);
        var names = new StackPanel { Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Text(name, 15.5, weight: FontWeights.SemiBold));
        names.Children.Add(
            Text(T(state), 11.5, state.StartsWith("Off") || state.StartsWith("Not") ? "#8F8A83" : "#E4C084")
        );
        head.Children.Add(names);
        stack.Children.Add(head);
        var text = Text(T(what), 12.5, "#8F8A83");
        text.Margin = new(0, 10, 0, 8);
        text.LineHeight = 18;
        stack.Children.Add(text);
        body(stack);
        return card;
    }

    FrameworkElement ImportPanel()
    {
        var card = new Border
        {
            CornerRadius = new(16),
            Padding = new(24, 20, 24, 20),
            Margin = new(0, 14, 0, 4),
            Background = new LinearGradientBrush(Brush("#2A241C").Color, Brush("#141416").Color, 20),
            BorderBrush = Brush("#40E4C084"),
            BorderThickness = new(1),
        };
        var stack = new StackPanel();
        card.Child = stack;
        if (importBusy)
        {
            stack.Children.Add(
                Text(importMessage.Length > 0 ? importMessage : "Importing…", 18, weight: FontWeights.SemiBold)
            );
            stack.Children.Add(
                new ProgressBar
                {
                    Height = 6,
                    Margin = new(0, 14, 0, 8),
                    Maximum = Math.Max(1, importProgress.Total),
                    Value = importProgress.Done,
                    IsIndeterminate = importProgress.Total == 0,
                    Foreground = Brush("#E4C084"),
                    Background = Brush("#2E2C2F"),
                    BorderThickness = new(0),
                }
            );
            if (importProgress.Total > 0)
                stack.Children.Add(Body($"Matching titles · {importProgress.Done} of {importProgress.Total}", 12));
            stack.Children.Add(ActionButton("Cancel", () =>
                {
                    importCancel?.Cancel();
                }));
            return card;
        }
        if (importPlan is { } plan)
        {
            var viewings = plan.Titles.Sum(t => t.Views.Count);
            var shows = plan.Titles.Count(t => !t.Title.IsMovie);
            var movies = plan.Titles.Count(t => t.Title.IsMovie);
            stack.Children.Add(Text("FROM " + plan.Service.ToUpperInvariant(), 10.5, "#E4C084", FontWeights.SemiBold));
            stack.Children.Add(
                Text($"{viewings:N0} viewing{(viewings == 1 ? "" : "s")} ready to add", 24, weight: FontWeights.Bold)
            );
            stack.Children.Add(
                Body(
                    $"{shows} show{(shows == 1 ? "" : "s")} and {movies} movie{(movies == 1 ? "" : "s")} matched"
                        + (
                            plan.Unmatched.Count > 0
                                ? $" · {plan.Unmatched.Count} row{(plan.Unmatched.Count == 1 ? "" : "s")} couldn't be matched and will be skipped"
                                : ""
                        )
                        + ". Viewings you already have are skipped.",
                    13
                )
            );
            ChoiceRows(stack, plan);
            ReviewGroups(stack, plan);
            var kept = plan.Titles.Where(t => !importExcluded.Contains(t.Title.Id)).Sum(t => t.Views.Count);
            var row = new WrapPanel { Margin = new(0, 12, 0, 0) };
            stack.Children.Add(row);
            var go = BigButton($"[[check]]   Add {kept:N0} viewings", () => ApplyImport(plan), true);
            go.IsEnabled = kept > 0 && ImportReady(plan);
            if (!ImportReady(plan))
                go.ToolTip = "Choose when you watched the undated ones first";
            row.Children.Add(go);
            row.Children.Add(
                BigButton(
                    "Cancel",
                    () =>
                    {
                        importPlan = null;
                        importMessage = "";
                        Render();
                    },
                    false
                )
            );
            if (plan.Unmatched.Count > 0)
            {
                var expander = new Expander
                {
                    Header = Text($"Show {plan.Unmatched.Count} unmatched rows", 12.5, "#AAA6A0"),
                    Margin = new(0, 14, 0, 0),
                    Foreground = Brush("#AAA6A0"),
                };
                var list = new StackPanel { Margin = new(0, 8, 0, 0) };
                foreach (var (r, reason) in plan.Unmatched.Take(80))
                    list.Children.Add(
                        Text(
                            $"{r.Title}{(r.Season > 0 ? $" · S{r.Season:00}E{r.Episode:00}" : "")}{(r.EpisodeTitle.Length > 0 ? " · " + r.EpisodeTitle : "")} — {reason}",
                            11.5,
                            "#8F8A83"
                        )
                    );
                if (plan.Unmatched.Count > 80)
                    list.Children.Add(Text($"…and {plan.Unmatched.Count - 80} more", 11.5, "#8F8A83"));
                expander.Content = list;
                stack.Children.Add(expander);
            }
            return card;
        }
        stack.Children.Add(Text(importMessage, 16, weight: FontWeights.SemiBold));
        var done = new WrapPanel { Margin = new(0, 10, 0, 0) };
        done.Children.Add(
            ActionButton(
                "See what's next",
                () =>
                {
                    importMessage = "";
                    trackerTab = "Up next";
                    Render();
                    ContentScroll.ScrollToTop();
                },
                true
            )
        );
        var close = ActionButton("Dismiss", () =>
        {
            importMessage = "";
            Render();
        });
        close.Margin = new(8, 0, 0, 0);
        done.Children.Add(close);
        if (lastImport.Length > 0)
        {
            var id = lastImport;
            var undo = ActionButton("Undo this import", () =>
            {
                var n = store.UndoImport(id);
                lastImport = "";
                importMessage = $"Undone: removed {n:N0} viewings.";
                Render();
            });
            undo.Margin = new(8, 0, 0, 0);
            done.Children.Add(undo);
        }
        stack.Children.Add(done);
        return card;
    }

    void ChooseImportFile(string service)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import from " + service,
            Filter =
                "History exports and backups (*.csv;*.json;*.zip;*.noirbackup)|*.csv;*.json;*.zip;*.noirbackup|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
            _ = ImportFile(dialog.FileName);
    }

    async Task ImportFile(string path)
    {
        if (importBusy)
            return;
        if (LibraryStore.IsBackup(path))
        {
            await PreviewRestore(path);
            return;
        }
        (string Service, List<ImportRow> Rows) read;
        try
        {
            read = await Task.Run(() => HistoryFormats.Read(path));
        }
        catch (Exception e)
        {
            store.Log(e);
            importMessage = "";
            Status.Text = e is IOException ? e.Message : "That file couldn't be read as a history export.";
            return;
        }
        await ResolveImport(read.Service, read.Rows);
    }

    async Task ResolveImport(string service, List<ImportRow> rows)
    {
        if (rows.Count == 0)
        {
            Status.Text = "There's nothing new to import from " + service + ".";
            return;
        }
        importBusy = true;
        importPlan = null;
        importProgress = (0, 0);
        importMessage = $"Matching {rows.Count:N0} {service} records…";
        importCancel = new();
        var token = importCancel.Token;
        if (page == "History" && trackerTab == "Import")
            Render();
        try
        {
            using var discovery = DiscoverClient();
            var importer = new HistoryImporter(store, discovery);
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                importProgress = p;
                if (page == "History" && trackerTab == "Import" && !logging)
                    Render();
            });
            var plan = await importer.Resolve(service, rows, progress, token);
            importPlan = plan;
            importMessage = "";
            importExcluded.Clear();
            importWhen = "";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            importMessage = "";
            Status.Text = "Import cancelled.";
        }
        catch (Exception e)
        {
            store.Log(e);
            importMessage = "";
            Status.Text = "Import couldn't finish. Check your connection and try again.";
        }
        // Imports started from Settings → Connections continue on the review in History → Add history.
        finally
        {
            importBusy = false;
            if (page == "History" && !logging)
            {
                trackerTab = "Import";
                Render();
            }
            else if (importPlan != null && page == "Settings")
            {
                historyType = "All";
                trackerTab = "Import";
                Navigate("History");
            }
        }
    }

    void ApplyImport(ImportPlan plan)
    {
        try
        {
            var added = ApplyReviewed(plan);
            importPlan = null;
            importMessage =
                added == 0
                    ? $"Everything from {plan.Service} was already in your history."
                    : $"Imported {added:N0} viewings from {plan.Service}.";
            if (plan.Service == "Stremio")
            {
                store.State.StremioImportedAt = DateTimeOffset.UtcNow;
                store.Save();
            }
            if (plan.Service == "Nuvio" && !plan.LookupFailed)
                CommitNuvioCursor();
            if (plan.Service == "Nuvio")
                MarkNuvio(plan with { Titles = plan.Titles.Where(t => !importExcluded.Contains(t.Title.Id)).ToList() });
        }
        catch (Exception e)
        {
            store.Log(e);
            importMessage = "";
            Status.Text = "Import couldn't be saved. Your history is unchanged.";
        }
        Render();
    }

    void ExportHistory(string format, string file)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export watch history",
            FileName = file,
            Filter = file.EndsWith(".json") ? "JSON (*.json)|*.json" : "CSV (*.csv)|*.csv",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var count = HistoryExport.Write(store, dialog.FileName, format, out var skipped);
            Status.Text =
                $"Exported {count:N0} viewings to {Path.GetFileName(dialog.FileName)}."
                + (
                    skipped > 0
                        ? $" {skipped:N0} skipped: "
                            + (
                                format == "letterboxd"
                                    ? "Letterboxd takes movies only."
                                    : "Trakt needs an IMDb ID for each title."
                            )
                        : ""
                );
        }
        catch (Exception e)
        {
            store.Log(e);
            Status.Text = "Export failed: " + e.Message;
        }
    }

    // Opens the service's own export page in a Noir browser window and imports the file it downloads.
    void CaptureExport(ServiceCard service)
    {
        if (service.CaptureUrl == null || importBusy)
            return;
        var allowed = new Uri(service.CaptureUrl).Host.Split('.')[^2];
        var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 11, 11, 13) };
        var window = new Window
        {
            Title = service.Name + " · Noir import",
            Owner = this,
            Width = 1100,
            Height = 820,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = web,
        };
        var note = new Border
        {
            Background = Brush("#F21C1C1F"),
            Padding = new(16, 10, 16, 10),
            Child = Text(
                service.Name == "Netflix"
                    ? "Sign in if asked, then scroll down and press “Download all”. Noir imports the file and closes this window."
                    : "Sign in if asked, then press “Export your data”. Noir imports the zip and closes this window.",
                13,
                "#E9E4DC"
            ),
        };
        var dock = new DockPanel();
        DockPanel.SetDock(note, Dock.Top);
        dock.Children.Add(note);
        window.Content = dock;
        dock.Children.Add(web);
        window.Loaded += async (_, _) =>
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: Path.Combine(store.DataDirectory, "review-browser")
                );
                await web.EnsureCoreWebView2Async(environment);
                var core = web.CoreWebView2;
                core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
                core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
                core.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme == "https")
                        core.Navigate(e.Uri);
                };
                core.NavigationStarting += (_, e) =>
                {
                    if (
                        !Uri.TryCreate(e.Uri, UriKind.Absolute, out var u)
                        || u.Scheme != "https" && u.Scheme != "blob" && u.Scheme != "data"
                    )
                        e.Cancel = true;
                };
                core.DownloadStarting += (_, e) =>
                {
                    var folder = Path.Combine(store.DataDirectory, "imports");
                    Directory.CreateDirectory(folder);
                    var extension = Path.GetExtension(e.ResultFilePath);
                    if (extension is not (".csv" or ".zip" or ".json"))
                        extension = service.Name == "Netflix" ? ".csv" : ".zip";
                    var target = Path.Combine(
                        folder,
                        $"{service.Name.ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}"
                    );
                    e.ResultFilePath = target;
                    e.Handled = true;
                    e.DownloadOperation.StateChanged += (_, _) =>
                    {
                        if (e.DownloadOperation.State == CoreWebView2DownloadState.Completed)
                        {
                            window.Close();
                            trackerTab = "Import";
                            if (page == "History")
                                Render();
                            _ = ImportFile(target);
                        }
                        else if (e.DownloadOperation.State == CoreWebView2DownloadState.Interrupted)
                            Status.Text = "The download from " + service.Name + " was interrupted. Try again.";
                    };
                };
                core.Navigate(service.CaptureUrl);
            }
            catch (Exception e)
            {
                store.Log(e);
                window.Close();
                Status.Text = "The browser component couldn't start. Use Choose file instead.";
            }
        };
        window.Closed += (_, _) => web.Dispose();
        window.Show();
    }

    string StremioKeyPath => Path.Combine(store.DataDirectory, "stremio.dpapi");

    string? StremioKey()
    {
        try
        {
            return File.Exists(StremioKeyPath)
                ? Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(File.ReadAllBytes(StremioKeyPath), null, DataProtectionScope.CurrentUser)
                )
                : null;
        }
        catch (Exception e)
        {
            store.Log(e);
            return null;
        }
    }

    // Stremio's web app keeps its session in local storage; Noir reads the key once, after you sign in there.
    void ConnectStremio()
    {
        var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 11, 11, 13) };
        var window = new Window
        {
            Title = "Connect Stremio · Noir",
            Owner = this,
            Width = 1000,
            Height = 780,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var note = new Border
        {
            Background = Brush("#F21C1C1F"),
            Padding = new(16, 10, 16, 10),
            Child = Text(
                "Sign in to Stremio. Noir connects automatically and closes this window; it never sees your password.",
                13,
                "#E9E4DC"
            ),
        };
        var dock = new DockPanel();
        DockPanel.SetDock(note, Dock.Top);
        dock.Children.Add(note);
        dock.Children.Add(web);
        window.Content = dock;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: Path.Combine(store.DataDirectory, "review-browser")
                );
                await web.EnsureCoreWebView2Async(environment);
                var core = web.CoreWebView2;
                core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
                core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
                core.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme == "https")
                        core.Navigate(e.Uri);
                };
                core.Navigate("https://web.stremio.com/#/intro?form=login");
                timer.Tick += async (_, _) =>
                {
                    if (web.CoreWebView2 == null || !web.CoreWebView2.Source.Contains("stremio.com"))
                        return;
                    var result = await web.CoreWebView2.ExecuteScriptAsync(
                        "(()=>{for(const k of ['profile',...Object.keys(localStorage)]){try{const p=JSON.parse(localStorage.getItem(k)||'null');if(p&&p.auth&&typeof p.auth.key==='string')return p.auth.key;}catch(e){}}return '';})()"
                    );
                    var key = System.Text.Json.JsonSerializer.Deserialize<string>(result) ?? "";
                    if (key.Length < 8)
                        return;
                    timer.Stop();
                    File.WriteAllBytes(
                        StremioKeyPath,
                        ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser)
                    );
                    window.Close();
                    Status.Text = "Stremio connected. Reading your library…";
                    // A new key lets Stremio marks waiting on "Connect Stremio again" go at once.
                    watchSync?.Retry();
                    _ = ImportStremio(false);
                };
                timer.Start();
            }
            catch (Exception e)
            {
                store.Log(e);
                window.Close();
                Status.Text = "The browser component couldn't start.";
            }
        };
        window.Closed += (_, _) =>
        {
            timer.Stop();
            web.Dispose();
        };
        window.Show();
    }

    // Manual imports show the review card; the background check applies straight away, since it
    // only ever adds viewings Stremio itself recorded.
    async Task ImportStremio(bool silent)
    {
        if (importBusy || StremioKey() is not { } key)
            return;
        try
        {
            var token = CancellationToken.None;
            if (!silent)
            {
                importBusy = true;
                importMessage = "Reading your Stremio library…";
                importProgress = (0, 0);
                importCancel = new();
                token = importCancel.Token;
                if (page == "History")
                    Render();
            }
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var discovery = DiscoverClient();
            var rows = await StremioLibrary.Fetch(key, http, discovery.VideoIds, token);
            if (silent)
            {
                var importer = new HistoryImporter(store, discovery);
                var plan = await importer.Resolve("Stremio", rows, null, CancellationToken.None);
                var added = importer.Apply(plan);
                store.State.StremioImportedAt = DateTimeOffset.UtcNow;
                store.Save();
                if (added > 0)
                {
                    Status.Text = $"Added {added} new viewing{(added == 1 ? "" : "s")} from Stremio.";
                    if (page == "History" && !logging)
                        Render();
                }
                return;
            }
            importBusy = false;
            await ResolveImport("Stremio", rows);
        }
        catch (OperationCanceledException) when (importCancel?.IsCancellationRequested == true)
        {
            importBusy = false;
            importMessage = "";
            Status.Text = "Import cancelled.";
            if (page == "History")
                Render();
        }
        catch (Exception e)
        {
            store.Log(e);
            importBusy = false;
            importMessage = "";
            // Only a rejected key means the sign-in expired; timeouts and outages keep the connection.
            var rejected =
                e is IOException && e is not HttpRequestException
                || e
                    is HttpRequestException
                    {
                        StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                    };
            if (rejected)
                try
                {
                    File.Delete(StremioKeyPath);
                }
                catch { }
            if (!silent)
                Status.Text = rejected
                    ? "Stremio sign-in has expired. Connect again."
                    : "Stremio couldn't be reached. Try again later.";
            if (page == "History" && !silent)
                Render();
        }
    }

    System.Windows.Threading.DispatcherTimer? stremioTimer;

    void StartStremioChecks()
    {
        if (validationMode)
            return;
        if (watchSync != null)
            watchSync.RefreshIds = () => RefreshMetadata();
        stremioTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        stremioTimer.Tick += (_, _) =>
        {
            stremioTimer.Interval = TimeSpan.FromHours(4);
            if (store.State.StremioAutoImport && StremioKey() != null && !importBusy)
                _ = ImportStremio(true);
        };
        stremioTimer.Start();
    }

    void OpenAccountSignIn(string provider)
    {
        var (item, url) = ReviewLinks.AccountSignIn(store.Archive, provider);
        if (item != null)
            OpenReviews(item, provider);
        else if (url != null)
            OpenSiteSignIn(provider, url);
        else
            Status.Text = "Add or log a title first, then sign in from its Reviews & comments.";
    }

    // A site's own sign-in page, in the same browser profile the watch sync uses.
    void OpenSiteSignIn(string provider, string url)
    {
        var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 11, 11, 13) };
        var window = new Window
        {
            Title = $"Sign in to {provider} · Noir",
            Owner = this,
            Width = 1000,
            Height = 780,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = web,
        };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: Path.Combine(store.DataDirectory, "review-browser")
                );
                await web.EnsureCoreWebView2Async(environment);
                var core = web.CoreWebView2;
                core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
                core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
                core.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme == "https")
                        core.Navigate(e.Uri);
                };
                core.Navigate(url);
            }
            catch (Exception e)
            {
                store.Log(e);
                window.Close();
                Status.Text = "The browser component couldn't start.";
            }
        };
        window.Closed += (_, _) =>
        {
            web.Dispose();
            watchSync?.Retry();
        };
        window.Show();
    }
}
