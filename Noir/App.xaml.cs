using System.IO;
using System.Windows;

namespace Noir;

public partial class App : Application
{
    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
        {
            WindowAppearance.Apply(window);
            WindowAppearance.Fit(window);
        }
    }

    void Scroll_Wheel(object sender, System.Windows.Input.MouseWheelEventArgs e) => SmoothScrolling.Wheel(sender, e);

    Mutex? single;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    protected override void OnStartup(StartupEventArgs e)
    {
        IconSet.Register();
        StartupTrace.Mark("App startup");
        base.OnStartup(e);
        if (e.Args.Contains("--build-vlc-cache"))
        {
            Shutdown(PlayerWarmup.BuildPluginCache() ? 0 : 1);
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Noir");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "errors.log"), args.Exception + "\n");
#if NOIR_VALIDATE
            // A validation run has nobody to close a message box: fail at once instead of hanging until the timeout.
            if (e.Args.Contains("--validate"))
                Environment.Exit(3);
#endif
            MessageBox.Show(
                "Noir encountered an error. Your saved positions are kept.\n\n" + args.Exception.Message,
                "Noir"
            );
            args.Handled = true;
        };
        string? dataDirectory = null;
        string? playPath = null;
        string? initialPage = null;
        bool validate = false;
#if NOIR_VALIDATE
        validate = e.Args.Contains("--validate");
#endif
        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--data-dir" && i + 1 < e.Args.Length)
                dataDirectory = e.Args[++i];
            else if (e.Args[i] == "--play" && i + 1 < e.Args.Length)
                playPath = e.Args[++i];
            else if (e.Args[i] == "--page" && i + 1 < e.Args.Length)
                initialPage = e.Args[++i];
        }
        var tasteLink = e.Args.FirstOrDefault(a => a.StartsWith(TasteCode.Prefix, StringComparison.OrdinalIgnoreCase));
        if (validate && dataDirectory == null)
        {
            Shutdown(2);
            return;
        }
        if (validate)
            Profiles.RootOverride = Path.GetFullPath(dataDirectory!);
        Density.Load(dataDirectory != null && !Profiles.IsProfileFolder(dataDirectory) ? dataDirectory : Profiles.Root);
        // Profiles share one running instance: a folder given on the command line is a separate
        // instance only when it isn't one of the profile folders (tests and validation copies).
        var instanceSuffix =
            dataDirectory == null || !validate && Profiles.IsProfileFolder(dataDirectory)
                ? ""
                : Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory).ToUpperInvariant())
                    )
                )[..16];
        single = new Mutex(true, "Local\\NoirPlayer_" + Environment.UserName + instanceSuffix, out var first);
        // Launching again while Noir runs in the background brings the existing window forward.
        var showSignal = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            "Local\\NoirPlayerShow_" + Environment.UserName + instanceSuffix
        );
        // After "Replace & restart" the old window may still be closing; wait for it.
        if (!first && e.Args.Contains("--restarted"))
            try
            {
                first = single.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                first = true;
            }
        // A taste link opened while Noir runs is handed over beside the show signal.
        var tasteHandoff = Path.Combine(
            Path.GetTempPath(),
            "noir-taste-" + Environment.UserName + instanceSuffix + ".txt"
        );
        if (!first)
        {
            if (tasteLink != null)
                try
                {
                    File.WriteAllText(tasteHandoff, tasteLink);
                }
                catch (IOException) { }
            if (!e.Args.Contains("--minimized") || tasteLink != null)
            {
                AllowSetForegroundWindow(-1);
                showSignal.Set();
            }
            Shutdown();
            return;
        }
        if (!validate)
            RegisterTasteLinks();
        if (dataDirectory == null && !validate)
        {
            var profiles = Profiles.Load();
            var current = profiles.Profiles.First(p => p.Id == profiles.Current);
            var ask =
                !e.Args.Contains("--minimized")
                && !e.Args.Contains("--restarted")
                && (profiles.Profiles.Count > 1 && profiles.AskOnStart || current.PinHash.Length > 0);
            if (ask)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var picked = Profiles.Pick(profiles);
                if (picked == null)
                {
                    Shutdown();
                    return;
                }
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                current = picked;
            }
            current.LastUsed = DateTimeOffset.UtcNow;
            profiles.Current = current.Id;
            try
            {
                Profiles.Save(profiles);
            }
            catch (IOException) { }
            if (profiles.ReadFailurePath is { } unreadable)
            {
                Loc.Use(Profiles.ReadLanguage(current.Id));
                MessageBox.Show(
                    string.Format(
                        Loc.T(
                            "The profile list could not be read. It was left untouched at {0}. Profile changes will not be saved until it is fixed or removed."
                        ),
                        unreadable
                    ),
                    "Noir",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
            dataDirectory = Profiles.Folder(current);
            if (dataDirectory != null)
                Directory.CreateDirectory(dataDirectory);
        }
        StartupTrace.Mark("App preparation ended");
        var window = new MainWindow(dataDirectory, playPath, validate, initialPage);
        StartupTrace.Mark("MainWindow constructor ended");
        if (validate)
        {
            window.ShowActivated = false;
            window.Title = "Noir validation";
        }
        MainWindow = window;
        if (e.Args.Contains("--minimized"))
        {
            window.ShowActivated = false;
            window.WindowState = WindowState.Minimized;
            window.StartHidden = true;
        }
        new Thread(() =>
        {
            while (showSignal.WaitOne())
            {
                string? link = null;
                try
                {
                    if (File.Exists(tasteHandoff))
                    {
                        link = File.ReadAllText(tasteHandoff).Trim();
                        File.Delete(tasteHandoff);
                    }
                }
                catch (IOException) { }
                Dispatcher.BeginInvoke(() =>
                {
                    window.ShowFromTray();
                    if (link?.StartsWith(TasteCode.Prefix, StringComparison.OrdinalIgnoreCase) == true)
                        window.OpenTaste(link);
                });
            }
        })
        {
            IsBackground = true,
            Name = "Noir activation",
        }.Start();
        if (tasteLink != null)
            window.Loaded += (_, _) => window.OpenTaste(tasteLink);
        StartupTrace.Mark("Window Show started");
        StartupTrace.FirstRendering(window, "First rendering after Show");
        window.Show();
        StartupTrace.Mark("Window Show returned");
    }

    // noir:// links open this copy of Noir (per user, no admin). Kept current when Noir moves.
    static void RegisterTasteLinks()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null || !exe.EndsWith("Noir.exe", StringComparison.OrdinalIgnoreCase))
                return;
            var command = $"\"{exe}\" \"%1\"";
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\noir");
            using var open = key.CreateSubKey(@"shell\open\command");
            if (open.GetValue("") as string == command)
                return;
            key.SetValue("", "URL:Noir link");
            key.SetValue("URL Protocol", "");
            open.SetValue("", command);
        }
        catch (Exception)
        { /* Links just won't open Noir. */
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // S6: deferred library saves (check-offs, sync jobs) finish before the process ends.
        LibraryStore.FlushAll();
        single?.Dispose();
        base.OnExit(e);
        StartupTrace.Mark("App exit");
        StartupTrace.Flush();
    }
}
