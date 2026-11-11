using System.IO;
using System.Windows.Threading;

namespace Noir;

public partial class MainWindow
{
    readonly List<FileSystemWatcher> libraryWatchers = [];
    readonly Dictionary<string, int> autoOrganizePending = new(StringComparer.OrdinalIgnoreCase);
    DispatcherTimer? autoOrganizeTimer;
    bool autoOrganizing;
    const int AutoOrganizeTries = 20;
    DispatcherTimer? folderTimer;
    bool foldersDirty;

    void QueueFolderScan()
    {
        if (closing || Dispatcher.HasShutdownStarted)
            return;
        Dispatcher.BeginInvoke(() =>
        {
            if (closing)
                return;
            foldersDirty = true;
            folderTimer ??= CreateFolderTimer();
            folderTimer.Stop();
            folderTimer.Start();
        });
    }

    DispatcherTimer CreateFolderTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += async (_, _) =>
        {
            if (!foldersDirty || closing)
            {
                timer.Stop();
                return;
            }
            // Do not compete with decoding or change a paused player's library.
            if (current != null || openingVideo || scanning || metadataRefreshing)
                return;
            foldersDirty = false;
            timer.Stop();
            await ScanLibraryAndOrganize();
        };
        return timer;
    }

    // O3: a scan that may find new files (startup, the Scan button, folder changes) remembers what the library
    // held before, and queues only the new files for automatic organizing. The organizer's own rescans after a run
    // or an Undo call ScanLibrary directly, so files it just moved (or moved back) are never organized again.
    async Task ScanLibraryAndOrganize()
    {
        var known =
            downloads?.ManagementPreferences.AutoOrganize == true
                ? store.Archive.Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : null;
        await ScanLibrary();
        if (known != null)
            QueueAutoOrganize(store.Archive.Where(i => !known.Contains(i.Path)).Select(i => i.Path));
    }

    // O3: files a scan found new are organized once they are confirmed online (that happens after the scan) and
    // nothing is playing, downloading or holding them open. Tried again every 15 seconds for a few minutes.
    void QueueAutoOrganize(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            autoOrganizePending[path] = 0;
        if (autoOrganizePending.Count == 0)
            return;
        autoOrganizeTimer ??= CreateAutoOrganizeTimer();
        autoOrganizeTimer.Start();
    }

    DispatcherTimer CreateAutoOrganizeTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += async (_, _) =>
        {
            if (closing || autoOrganizePending.Count == 0 || downloads?.ManagementPreferences.AutoOrganize != true)
            {
                autoOrganizePending.Clear();
                timer.Stop();
                return;
            }
            if (current != null || openingVideo || scanning || metadataRefreshing || autoOrganizing)
                return;
            autoOrganizing = true;
            try
            {
                var manager = downloads!;
                var wanted = autoOrganizePending.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var naming = manager.ManagementPreferences.Naming;
                var plan = await Task.Run(() =>
                    store.PlanOrganization(
                        naming,
                        i => wanted.Contains(i.Path),
                        path => manager.IsDownloadingFile(path) || manager.IsFilePlaying?.Invoke(path) == true
                    )
                );
                if (plan.Moves.Any(m => m.Video))
                {
                    plan.Auto = true;
                    // Like a manual run: the folder watcher is off while files move, so this does not trigger itself.
                    StopWatchingFolders();
                    try
                    {
                        await Task.Run(() => store.Organize(plan));
                    }
                    catch (Exception e)
                    {
                        store.Log(e);
                    }
                    finally
                    {
                        await ScanLibrary(false);
                        WatchLibraryFolders();
                    }
                }
                var present = store.Archive.Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var path in autoOrganizePending.Keys.ToList())
                    if (!present.Contains(path) || ++autoOrganizePending[path] >= AutoOrganizeTries)
                        autoOrganizePending.Remove(path);
                if (autoOrganizePending.Count == 0)
                    timer.Stop();
            }
            finally
            {
                autoOrganizing = false;
            }
        };
        return timer;
    }

    void WatchLibraryFolders()
    {
        foreach (var watcher in libraryWatchers)
            watcher.Dispose();
        libraryWatchers.Clear();
        foreach (
            var root in store
                .State.Roots.Where(r => Directory.Exists(r.Path))
                .Select(r => r.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        )
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter =
                        NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.Size,
                };
                watcher.Created += (_, _) => QueueFolderScan();
                watcher.Deleted += (_, _) => QueueFolderScan();
                watcher.Changed += (_, _) => QueueFolderScan();
                watcher.Renamed += (_, _) => QueueFolderScan();
                watcher.Error += (_, _) => QueueFolderScan();
                libraryWatchers.Add(watcher);
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                store.Log(e);
            }
    }

    void StopWatchingFolders()
    {
        folderTimer?.Stop();
        foreach (var watcher in libraryWatchers)
            watcher.Dispose();
        libraryWatchers.Clear();
    }
}
