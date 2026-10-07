using System.IO;
using System.Windows.Threading;

namespace Noir;
public partial class MainWindow
{
    readonly List<FileSystemWatcher> libraryWatchers=[];
    DispatcherTimer? folderTimer;
    bool foldersDirty;
    void QueueFolderScan()
    {
        if(closing||Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(()=>
        {
            if(closing) return;
            foldersDirty=true;
            folderTimer ??= CreateFolderTimer();
            folderTimer.Stop();folderTimer.Start();
        });
    }
    DispatcherTimer CreateFolderTimer()
    {
        var timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(4)};
        timer.Tick+=async(_,_)=>
        {
            if(!foldersDirty||closing) {timer.Stop();return;}
            // Do not compete with decoding or change a paused player's library.
            if(current!=null||openingVideo||scanning||metadataRefreshing) return;
            foldersDirty=false;timer.Stop();await ScanLibrary();
        };
        return timer;
    }
    void WatchLibraryFolders()
    {
        foreach(var watcher in libraryWatchers) watcher.Dispose();libraryWatchers.Clear();
        foreach(var root in store.State.Roots.Where(r=>Directory.Exists(r.Path)).Select(r=>r.Path).Distinct(StringComparer.OrdinalIgnoreCase))
        try
        {
            var watcher=new FileSystemWatcher(root) {IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite|NotifyFilters.Size};
            watcher.Created+=(_,_)=>QueueFolderScan();watcher.Deleted+=(_,_)=>QueueFolderScan();
            watcher.Changed+=(_,_)=>QueueFolderScan();watcher.Renamed+=(_,_)=>QueueFolderScan();watcher.Error+=(_,_)=>QueueFolderScan();
            libraryWatchers.Add(watcher);watcher.EnableRaisingEvents=true;
        }
        catch(Exception e){store.Log(e);}
    }
    void StopWatchingFolders()
    {
        folderTimer?.Stop();foreach(var watcher in libraryWatchers) watcher.Dispose();libraryWatchers.Clear();
    }
}
