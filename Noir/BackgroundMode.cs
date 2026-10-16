using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace Noir;
// Closing the window keeps Noir in the notification area so phone sync, phone streaming and
// PC downloads continue. "Quit Noir" in the tray menu (or turning the setting off) exits.
public partial class MainWindow
{
    Forms.NotifyIcon? tray;
    bool quitting,hiddenInTray,renderWhenShown;
    DispatcherTimer? awakeTimer;
    readonly HashSet<string> announcedDownloads=[];
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
    const uint EsContinuous=0x80000000,EsSystemRequired=0x00000001;
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";

    void StartBackgroundServices()
    {
        if(validationMode) return;
        var menu=new Forms.ContextMenuStrip{ShowImageMargin=false,BackColor=System.Drawing.Color.FromArgb(23,23,25),ForeColor=System.Drawing.Color.FromArgb(244,241,235),Renderer=new Forms.ToolStripProfessionalRenderer(new TrayColors())};
        menu.Items.Add("Open Noir",null,(_,_)=>ShowFromTray());
        menu.Items.Add("Downloads",null,(_,_)=>ShowFromTray("Downloads"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit Noir",null,(_,_)=>QuitFromTray());
        tray=new Forms.NotifyIcon{Text="Noir",ContextMenuStrip=menu,Visible=true};
        try{tray.Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);}catch{tray.Icon=System.Drawing.SystemIcons.Application;}
        tray.DoubleClick+=(_,_)=>ShowFromTray();
        tray.BalloonTipClicked+=(_,_)=>{var action=lastBalloonAction;ShowFromTray(lastBalloonPage);action?.Invoke();};
        if(System.Windows.Application.Current!=null) System.Windows.Application.Current.SessionEnding+=(_,_)=>quitting=true;
        foreach(var job in downloads?.Jobs()??[]) if(job.Status=="Completed") announcedDownloads.Add(job.Id);
        awakeTimer=new DispatcherTimer(TimeSpan.FromSeconds(20),DispatcherPriority.Background,(_,_)=>UpdateBackgroundState(),Dispatcher);awakeTimer.Start();
        UpdateBackgroundState();
        // Schedules change while Noir sits in the tray; refresh them so new episodes are noticed.
        scheduleTimer=new DispatcherTimer(TimeSpan.FromHours(6),DispatcherPriority.Background,async(_,_)=>{await RefreshMetadata();CheckNewEpisodes();},Dispatcher);scheduleTimer.Start();
        _=Dispatcher.InvokeAsync(async()=>{await Task.Delay(TimeSpan.FromSeconds(20));CheckNewEpisodes();},DispatcherPriority.ApplicationIdle);
    }
    DispatcherTimer? scheduleTimer;
    string? lastBalloonPage;
    Action? lastBalloonAction;
    void Notify(string title,string text,string? page=null,Action? action=null)
    {
        if(tray==null) return;
        lastBalloonPage=page;lastBalloonAction=action;tray.ShowBalloonTip(5000,title,text,Forms.ToolTipIcon.None);
    }
    // A released episode of a show you follow that aired in the last two weeks and is not watched yet.
    bool IsNewEpisode(VideoItem item)
    {
        if(item.IsMovie||store.EverWatched(item)) return false;
        var airs=store.State.EpisodeSchedules.GetValueOrDefault(item.GroupKey)?.FirstOrDefault(e=>e.Season==item.Season&&e.Episode==item.Episode)?.AirsAt;
        return airs is {} at&&at<=DateTimeOffset.UtcNow&&at>DateTimeOffset.UtcNow.AddDays(-14);
    }
    // One notification per newly aired episode that is next up for a show you are watching.
    void CheckNewEpisodes()
    {
        if(tray==null||closing) return;
        var fresh=store.Archive.GroupBy(i=>i.GroupKey).Where(g=>!g.First().IsMovie&&store.TitleStatus(g)!="Dropped"&&!store.State.HiddenContinue.Contains(g.Key))
            .Select(g=>store.UpNextFor(store.EpisodesFor(g.Key))).OfType<UpNext>()
            .Where(u=>u.Kind is UpNextKind.Ready or UpNextKind.NotOnPc&&IsNewEpisode(u.Item)&&store.State.NewEpisodeAlerts.Add(u.Item.GroupKey+":"+u.Item.Season+":"+u.Item.Episode)).ToList();
        if(fresh.Count==0) return;
        store.Save();
        var first=fresh[0].Item;var ready=fresh[0].Kind==UpNextKind.Ready;
        var text=fresh.Count>1?$"{first.Title} {first.EpisodeLabel} and {fresh.Count-1} more are out.":$"{first.Title} {first.EpisodeLabel} is out."+(ready?" It's on this PC.":" Click to download it.");
        Notify("New episode",text,"Home",fresh.Count==1&&!ready?()=>FindDownloads(first):null);
        if(!hiddenInTray&&page=="Home"&&current==null) Render();
    }
    // Called on every download change and periodically: tooltip, finished-download notices, sleep prevention.
    void UpdateBackgroundState()
    {
        if(tray==null||closing) return;
        var jobs=downloads?.Jobs()??[];
        var active=jobs.Where(j=>j.Status is "Queued" or "Preparing" or "Downloading" or "Subtitles").ToList();
        var text=active.Count==0?"Noir":active.Count==1&&active[0].Size>0?$"Noir · Downloading {Math.Clamp(active[0].Bytes*100/active[0].Size,0,100)}%":$"Noir · {active.Count} downloads in progress";
        tray.Text=text.Length>63?text[..63]:text;
        foreach(var done in jobs.Where(j=>j.Status=="Completed"&&announcedDownloads.Add(j.Id)))
            if(hiddenInTray||!IsActive) Notify("Ready to watch",done.Target.Label,"Home");
        var streaming=mobileSync!=null&&(DateTime.UtcNow-mobileSync.LastMediaRequest).TotalMinutes<3;
        // Keep the PC from sleeping only while it is doing work for you. The display may still turn off.
        var busy=store.State.KeepAwakeWhileBusy&&(active.Count>0||streaming);
        SetThreadExecutionState(busy?EsContinuous|EsSystemRequired:EsContinuous);
    }
    void HideToTray(bool quiet=false)
    {
        if(tray==null) return;
        if(PlayerRoot.Visibility==Visibility.Visible) ReturnToLibrary();
        WindowAppearance.Remember(store.DataDirectory,fullscreen?oldWindowBounds:RestoreBounds,fullscreen?oldWindowState==WindowState.Maximized:WindowState==WindowState.Maximized);
        foreach(var owned in OwnedWindows.Cast<Window>().ToList()) if(owned.Title!="LibVLCSharp.WPF") owned.Close();
        hiddenInTray=true;Hide();
        // Release what the library page holds; it is rebuilt when the window comes back.
        ContentPanel.Children.Clear();images.Clear();imageBytes=0;renderWhenShown=true;
        if(!quiet&&!store.State.BackgroundHintShown)
        {
            store.State.BackgroundHintShown=true;store.Save();
            Notify("Noir is still running","Phone sync and downloads keep working. Right-click the Noir icon to quit.");
        }
    }
    internal void ShowFromTray(string? page=null)
    {
        if(closing) return;
        hiddenInTray=false;Show();
        if(WindowState==WindowState.Minimized) WindowState=WindowState.Normal;
        WindowAppearance.Fit(this);Activate();
        if(page!=null&&PlayerRoot.Visibility!=Visibility.Visible) Navigate(page);
        else if(renderWhenShown&&current==null) Render();
        renderWhenShown=false;
    }
    void QuitFromTray(){quitting=true;if(!IsVisible)hiddenInTray=false;Close();}
    void DisposeBackgroundServices()
    {
        awakeTimer?.Stop();scheduleTimer?.Stop();SetThreadExecutionState(EsContinuous);
        if(tray!=null){tray.Visible=false;tray.Dispose();tray=null;}
    }
    static bool StartsWithWindows()
    {
        using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("Noir") is string value&&value.Contains(Environment.ProcessPath!,StringComparison.OrdinalIgnoreCase);
    }
    static void SetStartWithWindows(bool enabled)
    {
        using var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if(enabled) key.SetValue("Noir",$"\"{Environment.ProcessPath}\" --minimized");
        else if(key.GetValue("Noir")!=null) key.DeleteValue("Noir");
    }
    sealed class TrayColors:Forms.ProfessionalColorTable
    {
        static readonly System.Drawing.Color Back=System.Drawing.Color.FromArgb(23,23,25),Hover=System.Drawing.Color.FromArgb(44,39,32),Line=System.Drawing.Color.FromArgb(60,56,50);
        public override System.Drawing.Color ToolStripDropDownBackground=>Back;
        public override System.Drawing.Color MenuItemSelected=>Hover;
        public override System.Drawing.Color MenuItemBorder=>Hover;
        public override System.Drawing.Color MenuBorder=>Line;
        public override System.Drawing.Color SeparatorDark=>Line;
        public override System.Drawing.Color SeparatorLight=>Back;
        public override System.Drawing.Color ImageMarginGradientBegin=>Back;
        public override System.Drawing.Color ImageMarginGradientMiddle=>Back;
        public override System.Drawing.Color ImageMarginGradientEnd=>Back;
    }
}
