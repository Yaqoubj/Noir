using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Noir;
public partial class MainWindow
{
    PcDownloads? downloads;
    bool showAllReady;
    Action? downloadPageUpdate;
    int lastDownloadCount=-1;
    void DownloadChanged(){void Update(){if(closing)return;UpdateDownloadBadge();UpdateDownloadsTop();RefreshDownloadsPopover();UpdateBackgroundState();if(!hiddenInTray){downloadPageUpdate?.Invoke();if(page=="Downloads")readyCentreUpdate?.Invoke();}}if(Dispatcher.CheckAccess())Update();else Dispatcher.BeginInvoke(Update,DispatcherPriority.DataBind);}
    void UpdateDownloadBadge(){var count=downloads?.ActiveCount??0;if(count==lastDownloadCount)return;lastDownloadCount=count;SetNavIcon(DownloadsNav,DownloadIcon(count));UpdateNavSelection();if(!railCompact)DownloadsNav.ToolTip=count>0?$"{T("Downloads")} · {count}":null;}
    void StartDownloads()
    {
        try
        {
            downloads=new(store);downloads.Completed+=QueueFolderScan;downloads.Changed+=DownloadChanged;UpdateDownloadBadge();UpdateDownloadsTop();
            var manager=downloads;store.WatchCompleted+=()=>_=manager.PrepareReady("Episode finished");manager.Completed+=()=>_=manager.PrepareReady("Download finished");_=manager.PrepareReady("Startup");
        }
        catch(Exception){Status.Text="Download settings could not be loaded. Your library is unchanged.";}
    }
    // Private links and keys: masked, on the dark field style used across Settings.
    PasswordBox Secret(string value)=>new(){Password=value,Padding=new(10),Margin=new(0,5,0,10),Background=Brush("#1C1C1F"),Foreground=Brush("#F4F1EB"),BorderBrush=Brush("#3A3832"),CaretBrush=Brush("#E4C084"),SelectionBrush=Brush("#E4C084")};
    void RenderDownloadSettings()
    {
        ContentPanel.Children.Add(Heading("Downloads on this PC","Find releases through AIOStreams, your download service and your media servers. Completed downloads join your local library."));
        if(downloads==null){ContentPanel.Children.Add(Body("Downloads are unavailable until Noir restarts."));return;}
        var c=downloads.Config;
        ContentPanel.Children.Add(ReadySettingsPanel());
        var manifest=Secret(c.Manifest);
        var key=Secret(c.Key);
        ContentPanel.Children.Add(Body("AIOStreams installation URL (manifest.json)",12));ContentPanel.Children.Add(manifest);
        var service=new ComboBox{ItemsSource=new[]{new ComboBoxItem{Content="TorBox",Tag="TorBox"},new ComboBoxItem{Content="Real-Debrid",Tag="Real-Debrid"},new ComboBoxItem{Content=T("AIOStreams links"),Tag="AIOStreams links"},new ComboBoxItem{Content=T("Torrents (no debrid)","تورنت (دون خدمة وسيطة)"),Tag="Torrents"}},SelectedValuePath="Tag",SelectedValue=c.Service,Margin=new(0,5,0,10)};ContentPanel.Children.Add(Body("Download service",12));ContentPanel.Children.Add(service);
        var keyLabel=Body("",12);var help=Body("",12);ContentPanel.Children.Add(keyLabel);ContentPanel.Children.Add(key);ContentPanel.Children.Add(help);
        var sharing=new StackPanel{Margin=new(0,8,0,12)};sharing.Children.Add(Body(T("Sharing back","المشاركة مع الآخرين"),13));
        var seeding=new ComboBox{ItemsSource=new[]{T("Don't share","دون مشاركة"),T("Until ratio 1.0 or 2 hours","حتى نسبة 1.0 أو ساعتين"),T("While Noir is open","ما دام Noir مفتوحًا")},SelectedIndex=c.Seeding=="off"?0:c.Seeding=="open"?2:1,Margin=new(0,5,0,10)};sharing.Children.Add(seeding);System.Windows.Automation.AutomationProperties.SetName(seeding,T("Seeding","المشاركة بعد التنزيل"));
        TextBox TorrentNumber(string label,int value){sharing.Children.Add(Body(label,12));var input=new TextBox{Text=value.ToString(System.Globalization.CultureInfo.InvariantCulture),Padding=new(10),Margin=new(0,5,0,10)};System.Windows.Automation.AutomationProperties.SetName(input,label);sharing.Children.Add(input);return input;}
        var upload=TorrentNumber(T("Upload limit (KB/s) · 0 = unlimited","حد الرفع (KB/s) · 0 = بلا حد"),c.UploadLimitKB);var download=TorrentNumber(T("Download limit (KB/s) · 0 = unlimited","حد التنزيل (KB/s) · 0 = بلا حد"),c.DownloadLimitKB);
        var autoSwitch=new CheckBox{Content=T("Switch to another release if stuck","التبديل إلى إصدار آخر عند التعثر"),IsChecked=c.AutoSwitch,Margin=new(0,6,0,10)};sharing.Children.Add(autoSwitch);System.Windows.Automation.AutomationProperties.SetName(autoSwitch,T("Switch to another release if stuck","التبديل إلى إصدار آخر عند التعثر"));
        var active=TorrentNumber(T("Maximum active torrents","الحد الأقصى لتنزيلات التورنت النشطة"),c.MaxActiveTorrents);ContentPanel.Children.Add(sharing);
        System.Windows.Automation.AutomationProperties.SetName(service,T("Download service","خدمة التنزيل"));System.Windows.Automation.AutomationProperties.SetName(manifest,T("AIOStreams or Torrentio installation link","رابط تثبيت AIOStreams أو Torrentio"));
        var keys=new Dictionary<string,string>{{"TorBox",c.Service=="TorBox"?c.Key:c.TorBoxKey},{"Real-Debrid",c.Service=="Real-Debrid"?c.Key:c.RealDebridKey}};var selectedService=c.Service;
        void ServiceChanged(){if(keys.ContainsKey(selectedService))keys[selectedService]=key.Password;selectedService=service.SelectedValue as string??"TorBox";sharing.Visibility=selectedService=="Torrents"?Visibility.Visible:Visibility.Collapsed;key.Password=keys.GetValueOrDefault(selectedService,"");key.Visibility=keyLabel.Visibility=selectedService is "AIOStreams links" or "Torrents"?Visibility.Collapsed:Visibility.Visible;keyLabel.Text=selectedService=="Real-Debrid"?"Real-Debrid API token":"TorBox API key";help.Text=selectedService=="Torrents"?T("Other people sharing a torrent can see your IP address.","يمكن للأشخاص المشاركين في التورنت رؤية عنوان IP الخاص بك."):selectedService=="AIOStreams links"?"Uses the debrid or usenet services you added in AIOStreams — Real-Debrid, AllDebrid, Premiumize, Easynews, TorBox…":selectedService=="Real-Debrid"?"AIOStreams finds torrents. Your Real-Debrid account fetches them. Reading a release's file list may add its magnet to your account.":"AIOStreams finds torrents. Your TorBox account supplies the files and searches your existing library.";}
        service.SelectionChanged+=(_,_)=>ServiceChanged();ServiceChanged();
        ComboBox Folder(bool movies,string selected)
        {
            var box=new ComboBox{Margin=new(0,5,0,10),MinWidth=300};foreach(var root in store.State.Roots.Where(r=>r.Movies==movies))box.Items.Add(root.Path);
            box.SelectedItem=selected;if(box.SelectedIndex<0&&box.Items.Count>0)box.SelectedIndex=0;return box;
        }
        var shows=Folder(false,c.ShowsRoot);var movies=Folder(true,c.MoviesRoot);
        ContentPanel.Children.Add(Body("TV / anime download folder",12));ContentPanel.Children.Add(shows);ContentPanel.Children.Add(Body("Movie download folder",12));ContentPanel.Children.Add(movies);
        var quality=new ComboBox{ItemsSource=new[]{"1080p","720p","2160p","Any"},SelectedItem=c.PreferredResolution,Margin=new(0,5,0,10)};ContentPanel.Children.Add(Body("Preferred download quality",12));ContentPanel.Children.Add(quality);
        var size=new TextBox{Text=c.MaxEpisodeBytes>0?(c.MaxEpisodeBytes/1073741824d).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture):"",Padding=new(10),Margin=new(0,5,0,10)};ContentPanel.Children.Add(Body("Maximum episode size in GB · leave empty for no limit",12));ContentPanel.Children.Add(size);
        var english=new CheckBox{Content="Download English subtitles",IsChecked=c.SubtitleLanguages.Contains("en"),Margin=new(0,8,0,8)};var arabic=new CheckBox{Content="Download Arabic subtitles",IsChecked=c.SubtitleLanguages.Contains("ar"),Margin=new(0,8,0,8)};var timing=new CheckBox{Content="Automatically check subtitle timing against the local video",IsChecked=c.AutoSyncSubtitles,Margin=new(0,8,0,8)};ContentPanel.Children.Add(english);ContentPanel.Children.Add(arabic);ContentPanel.Children.Add(timing);
        ContentPanel.Children.Add(Body(T("Stremio subtitle addon link · optional"),12));var subtitleLink=Secret(c.SubtitleAddonUrl);ContentPanel.Children.Add(subtitleLink);
        var subtitleOnly=new CheckBox{Content=T("Use only this source"),IsChecked=c.SubtitleAddonOnly,IsEnabled=c.SubtitleAddonUrl.Length>0,Margin=new(0,8,0,8)};ContentPanel.Children.Add(subtitleOnly);
        var subtitleStatus=Body("",12);ContentPanel.Children.Add(subtitleStatus);bool checkingSubtitle=false;var hadSubtitleLink=c.SubtitleAddonUrl.Length>0;
        subtitleLink.PasswordChanged+=(_,_)=>{var has=subtitleLink.Password.Trim().Length>0;if(has&&!hadSubtitleLink)subtitleOnly.IsChecked=true;hadSubtitleLink=has;subtitleOnly.IsEnabled=has;subtitleStatus.Text="";};
        async Task<bool> CheckSubtitleLink()
        {
            if(checkingSubtitle)return false;var link=subtitleLink.Password;var only=subtitleOnly.IsChecked;checkingSubtitle=true;subtitleStatus.Text=T("Checking subtitle addon…");
            try{var normalized=SubtitleAddon.Normalize(link);var name=normalized.Length==0?"":await SubtitleAddon.Check(normalized,CancellationToken.None);
                if(subtitleLink.Password!=link||subtitleOnly.IsChecked!=only){subtitleStatus.Text=T("Subtitle source changed. Check again.");return false;}
                subtitleStatus.Text=name.Length==0?T("Default subtitle source"):T("Connected:")+" "+name;return true;
            }catch(Exception e){subtitleStatus.Text=e is System.IO.IOException?T(e.Message):T("Couldn't check the subtitle addon. Check the link and connection.");return false;}finally{checkingSubtitle=false;}
        }
        ContentPanel.Children.Add(ActionButton(T("Check link"),async()=>{await CheckSubtitleLink();}));
        ContentPanel.Children.Add(ActionButton(T("Save subtitle source"),async()=>{if(await CheckSubtitleLink())try{downloads.ConfigureSubtitleSource(subtitleLink.Password,subtitleOnly.IsChecked==true);Status.Text=T("Subtitle source saved securely.");}catch(Exception){subtitleStatus.Text=T("Couldn't save subtitle source.");}}));
        int TorrentLimit(string text,int min,int max){if(!int.TryParse(text,out var value)||value<min||value>max)throw new IOException(T("Enter a valid torrent limit.","أدخل حدًا صالحًا للتورنت."));return value;}
        ContentPanel.Children.Add(ActionButton("Save download settings",async()=>{try{if(SubtitleAddon.Normalize(subtitleLink.Password)!=downloads.Config.SubtitleAddonUrl&&!await CheckSubtitleLink())return;long limit=0;if(size.Text.Trim().Length>0){if(!double.TryParse(size.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var gb)||!double.IsFinite(gb)||gb<=0||gb>1000)throw new System.IO.IOException("Enter an episode size between 0 and 1000 GB, or leave it empty.");limit=(long)(gb*1073741824);}downloads.Configure(manifest.Password,key.Password,shows.SelectedItem as string??"",movies.SelectedItem as string??"",quality.SelectedItem as string??"1080p",limit,new[]{english.IsChecked==true?"en":"",arabic.IsChecked==true?"ar":""}.Where(l=>l.Length>0).ToList(),timing.IsChecked==true,selectedService,seeding.SelectedIndex==0?"off":seeding.SelectedIndex==2?"open":"ratio",TorrentLimit(upload.Text,0,2097151),TorrentLimit(download.Text,0,2097151),autoSwitch.IsChecked==true,TorrentLimit(active.Text,1,20));downloads.ConfigureSubtitleSource(subtitleLink.Password,subtitleOnly.IsChecked==true);Status.Text="Saved securely on this PC. Paired phones use these download preferences.";}catch(Exception e){Status.Text=e is System.IO.IOException?e.Message:"Couldn't save settings.";}},true));
        var servers=ActionButton("Media servers (Plex, Jellyfin, Emby) are in Connections →",()=>{settingsSection="Connections";Render();ContentScroll.ScrollToTop();});servers.Margin=new(0,14,0,10);ContentPanel.Children.Add(servers);
        ContentPanel.Children.Add(Body("All connected sources are checked for releases and existing files. Plan missing episodes checks released episodes, season coverage and local files. Subtitles come from the release or OpenSubtitles, when available, and stay beside the video for offline use. Automatic timing runs locally and keeps an original backup. Files are organized as Show (Year) / Season 01 / Show (Year) S01E01.mkv.",12));
        ContentPanel.Children.Add(ActionButton("Manage PC downloads",ShowDownloads));
        var bridge=new StackPanel();bridge.Children.Add(Body("If AIOStreams returns playback links without download references, create a separate Noir profile on the same host. Export your AIOStreams config with credentials included, then choose the export here. Noir adds download metadata to the copy. The copy omits the retired TorBox Search preset; your existing Stremio profile is unchanged.",12));
        var bridgeStatus=Body("",12);var connect=ActionButton("Connect a separate Noir download profile",async()=>{
            var file=new Microsoft.Win32.OpenFileDialog{Title="Choose AIOStreams export",Filter="AIOStreams configuration|*.json"};if(file.ShowDialog(this)!=true)return;
            bridgeStatus.Text="Creating the download profile on your existing AIOStreams host…";
            try{var link=await AioProfiles.Create(downloads.Config.Manifest,await System.IO.File.ReadAllTextAsync(file.FileName),CancellationToken.None);downloads.UseDownloadProfile(link);manifest.Password=link;bridgeStatus.Text="Download profile connected. Refresh releases to use it.";}
            catch(Exception e){bridgeStatus.Text=e is System.IO.IOException?e.Message:"Couldn't create the profile. Your existing Stremio configuration is unchanged.";}
        });bridge.Children.Add(connect);bridge.Children.Add(bridgeStatus);ContentPanel.Children.Add(new Expander{Header="AIOStreams download connection",Content=bridge,Margin=new(0,14,0,10)});
    }
    // Adding Jellyfin/Emby (username + password, never saved) or Plex (browser PIN); the server list lives in Connections.
    void AddServerControls(Panel host,Action changed)
    {
        if(downloads==null)return;
        var api=new MediaServers();
        var form=new StackPanel();TextBox Field(string label){form.Children.Add(Body(label,12));var box=new TextBox{Padding=new(10),Margin=new(0,5,0,10)};form.Children.Add(box);return box;}
        var kind=new ComboBox{ItemsSource=new[]{"Jellyfin","Emby"},SelectedIndex=0,Margin=new(0,5,0,10)};form.Children.Add(kind);var name=Field("Display name · optional");var url=Field("Server URL");var username=Field("Username");form.Children.Add(Body("Password",12));var password=new PasswordBox{Padding=new(10),Margin=new(0,5,0,10)};form.Children.Add(password);var feedback=Body("The password is used to connect and is never saved.",12);form.Children.Add(feedback);
        Button? connect=null;connect=ActionButton("Connect",async()=>{connect!.IsEnabled=false;feedback.Text="Connecting…";try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));var server=await api.Authenticate(url.Text,username.Text,password.Password,downloads.Config.DeviceId,name.Text,kind.SelectedItem as string??"Jellyfin",timeout.Token);downloads.AddServer(server);feedback.Text="Connected. This server will appear in release searches.";changed();}catch(Exception e){feedback.Text=e is System.IO.IOException?e.Message:"Couldn't connect. Check the address and account.";}finally{password.Clear();connect.IsEnabled=true;}});form.Children.Add(connect);
        host.Children.Add(new Expander{Header=T("Add Jellyfin / Emby"),Content=form,Margin=new(0,4,0,10),Foreground=Brush("#D9D4CC")});
        host.Children.Add(ActionButton("Connect Plex",async()=>{
            var dialog=OpenSheet(T("Connect Plex","ربط Plex"),null,SheetKind.Dialog,480);var note=Body("Opening Plex sign-in in your browser…",13);dialog.Add(note);
            var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(10));var token=cancel.Token;dialog.Closed+=_=>{cancel.Cancel();cancel.Dispose();};dialog.Action(T("Cancel"),()=>dialog.Close(false));
            try{
                var pin=await api.BeginPlex(downloads.Config.DeviceId,token);token.ThrowIfCancellationRequested();System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pin.BrowserUrl){UseShellExecute=true});note.Text="Sign in to Plex in your browser. Waiting for confirmation…";
                string access="";while(access.Length==0){await Task.Delay(TimeSpan.FromSeconds(2),token);access=await api.PollPlex(pin,token);}
                var servers=await api.PlexResources(access,pin.DeviceId,token);token.ThrowIfCancellationRequested();note.Text=servers.Count>0?"Choose the servers to connect:":"Plex returned no accessible servers with an HTTPS or local connection.";
                foreach(var server in servers){Button? add=null;add=ActionButton("Connect "+server.Name+(server.BaseUrl.StartsWith("http://",StringComparison.OrdinalIgnoreCase)?" · local HTTP":""),()=>{downloads.AddServer(server);add!.IsEnabled=false;add.Content="Connected · "+server.Name;changed();});add.Margin=new(0,4,0,4);dialog.Add(add);}
            }catch(OperationCanceledException){if(dialog.IsOpen)note.Text="Plex sign-in expired. Close this window and try again.";}catch(Exception e){if(dialog.IsOpen)note.Text=e is System.IO.IOException?e.Message:"Couldn't connect Plex. Close this window and retry.";}
        }));
    }
    async void FindDownloads(VideoItem item,bool season=false)
    {
        if(downloads==null){Status.Text="Downloads are starting. Try again shortly.";return;}
        if(!item.IsMovie){ShowSeasonPlanner(item,season);return;}
        var window=OpenSheet(T(season?"Download season":"Download episode / movie"),item.Title,SheetKind.Side,820);var body=window.Body;
        var cancel=new CancellationTokenSource();var token=cancel.Token;window.Closed+=_=>{cancel.Cancel();cancel.Dispose();};int searchRevision=0;
        void ClearFooter(){window.FooterButtons.Children.Clear();window.Footer.Visibility=Visibility.Collapsed;}
        async Task Search()
        {
            var view=++searchRevision;
            body.Children.Clear();ClearFooter();window.SetTitle(window.Title,item.Title+" · "+T(season?$"Season {item.Season} · find missing episodes or choose a pack":"Choose quality, size and release"));
            var states=new Dictionary<string,TextBlock>();var sourcePanel=new StackPanel();body.Children.Add(sourcePanel);
            void Source(DownloadSourceStatus s){if(!states.TryGetValue(s.Source,out var t)){t=Body("",12);states[s.Source]=t;sourcePanel.Children.Add(t);}t.Text=s.Source+" · "+s.State+(s.Message.Length>0?" · "+s.Message:"");}
            var status=Body("Checking download sources…",13);body.Children.Add(status);
            try{
                var progress=new Progress<DownloadSourceStatus>(s=>{if(view==searchRevision&&!token.IsCancellationRequested)Source(s);});
                var result=await downloads.SearchDetailed(item.Id,season,progress,token);
                if(view!=searchRevision||token.IsCancellationRequested)return;
                foreach(var source in states.Where(s=>!result.Sources.Any(r=>r.Source==s.Key)))source.Value.Text=source.Key+" · No separate source status";
                foreach(var source in result.Sources)Source(source);
                status.Text=result.Choices.Count+" unique releases · preferred quality: "+downloads.Config.PreferredResolution;
                body.Children.Add(ActionButton("Refresh sources",()=>_=Search()));
                body.Children.Add(ReleasePicker(result.Choices,downloads.Config,async release=>{
                    status.Text="Verifying this release's file list…";
                    try{var files=await downloads.Files(release.Id,token);RenderFiles(files.Select(f=>new DownloadPlanEntry{ReleaseId=release.Id,Release=release.Name,File=f}).ToList(),$"{files.Count(f=>f.Selectable)} selectable files · episode numbers checked against Season {item.Season}");}
                    catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception e){status.Text=e is System.IO.IOException?e.Message:"Couldn't load the files. Try another release.";}
                }));
            }catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception e){status.Text=e is System.IO.IOException?e.Message:"Couldn't find releases. Check download settings and connection.";body.Children.Add(ActionButton("Retry",()=>_=Search()));}
        }
        void RenderFiles(List<DownloadPlanEntry> entries,string summary)
        {
            body.Children.Clear();ClearFooter();window.SetTitle(window.Title,T("Review your download")+" · "+item.Title+(season?$" · Season {item.Season}":""));body.Children.Add(Body(summary,13));
            var selected=new List<(CheckBox Box,DownloadPlanEntry Entry)>();var numbers=new HashSet<(int,int)>();
            var total=Body("",14);
            void UpdateTotal(){var chosen=selected.Where(x=>x.Box.IsChecked==true).ToList();total.Text=$"{chosen.Count} files · {DownloadNaming.Size(chosen.Sum(x=>x.Entry.File.Size))}"+(chosen.Any(x=>x.Entry.File.Size<=0)?" + unknown sizes":"");}
            foreach(var entry in entries){var f=entry.File;var box=new CheckBox{Content=Body(f.Name+" · "+DownloadNaming.Size(f.Size)+(f.Reason.Length>0?" · "+f.Reason:""),13),IsEnabled=f.Selectable,IsChecked=f.Selectable&&numbers.Add((f.Season,f.Episode)),Margin=new(0,9,0,9)};selected.Add((box,entry));box.Checked+=(_,_)=>UpdateTotal();box.Unchecked+=(_,_)=>UpdateTotal();body.Children.Add(box);if(f.MappingRequired)body.Children.Add(ActionButton("Match this file to an episode",()=>{
                var dialog=OpenSheet(T("Confirm episode match","تأكيد مطابقة الحلقة"),null,SheetKind.Dialog,460);dialog.Add(Body(f.Name+" · Season "+item.Season,13));var number=new TextBox{Text=item.Episode.ToString(),Margin=new(0,12,0,12)};dialog.Add(number);var note=Body("Use the episode number from this season's episode list.",12);dialog.Add(note);
                dialog.Action(T("Cancel"),()=>dialog.Close(false));dialog.Action(T("Confirm match"),()=>{try{if(!int.TryParse(number.Text,out var n))throw new System.IO.IOException("Enter an episode number.");downloads.MapFile(f.Id,n);dialog.Close(true);RenderFiles(entries,summary);}catch(Exception e){note.Text=e is System.IO.IOException?e.Message:"Couldn't match this file.";}},true);
            }));if(entries.Select(e=>e.ReleaseId).Distinct().Count()>1)body.Children.Add(Body(entry.Release,11));}
            body.Children.Add(total);UpdateTotal();var c=downloads.Config;body.Children.Add(Body("Save to: "+(item.IsMovie?c.MoviesRoot:c.ShowsRoot),12));
            body.Children.Add(Body("Subtitles: "+(c.SubtitleLanguages.Count==0?"Off":string.Join(" + ",c.SubtitleLanguages.Select(l=>l=="ar"?"Arabic":"English")))+(c.AutoSyncSubtitles?" · automatic timing check":""),12));
            var feedback=Body("Only selected files are downloaded. Existing and queued episodes are skipped.",12);body.Children.Add(feedback);var requestId=Guid.NewGuid().ToString();
            window.Action(T("Back to releases"),()=>_=Search());
            window.Action(T("Download selected to this PC"),()=>{
                try{var selections=selected.Where(x=>x.Box.IsChecked==true).GroupBy(x=>x.Entry.ReleaseId).Select(g=>new DownloadSelection{ReleaseId=g.Key,FileIds=g.Select(x=>x.Entry.File.Id).ToList()}).ToList();var ids=downloads.Enqueue(new(){RequestId=requestId,Selections=selections});Status.Text=$"Queued {ids.Count} files for this PC.";window.Close(true);ShowDownloads();}
                catch(Exception e){feedback.Text=e is System.IO.IOException?e.Message:"Couldn't queue these files.";}
            },true);
        }
        await Search();
    }
    void ShowDownloads()=>Navigate("Downloads");
    void RenderDownloads()
    {
        PageTitle.Text=T("Downloads","التنزيلات");downloadPageUpdate=null;
        if(downloads==null){ContentPanel.Children.Add(Heading("Downloads"));ContentPanel.Children.Add(Body("Downloads are starting. Reopen this tab shortly."));return;}
        var page=new StackPanel();ContentPanel.Children.Add(page);
#if NOIR_VALIDATE
        QueueReadyValidation();
#endif
        var queue=new StackPanel();var library=new StackPanel();page.Children.Add(queue);page.Children.Add(library);
        downloadPageUpdate=BuildDownloadPage(queue);downloadPageUpdate();BuildDownloadTabs(page,queue,library,downloadPageUpdate);
    }
    // ---- Downloads page: one calm row per job, grouped by what the user can do about it ----
    const string DimText="#9F9A93",DoneGreen="#8FCB9B",DownloadGold="#E4C084",DownloadRed="#E5736B";
    Expander? keepReadyExpander;bool keepReadyOpen;
    sealed class DownloadRowView
    {
        public Border Root=null!;public TextBlock Status=null!,Percent=null!;public ProgressBar Bar=null!;public Grid BarRow=null!;public StackPanel Actions=null!;public Button? Play;
        public string Group="",State="",StatusKey="";public VideoItem? Library;public DateTime ResolvedAt=DateTime.MinValue;
    }
    static string DownloadGroup(string status)=>status switch{"Downloading" or "Preparing" or "Subtitles"=>"now","Queued" or "Paused"=>"waiting","Failed" or "Cancelled"=>"attention",_=>"ready"};
    static (string Word,string Color) DownloadState(string status)=>status switch{
        "Downloading"=>(T("Downloading","جارٍ التنزيل"),DownloadGold),"Preparing"=>(T("Preparing","جارٍ التحضير"),DownloadGold),"Subtitles"=>(T("Subtitles","الترجمات"),DownloadGold),
        "Queued"=>(T("Waiting","بالانتظار"),DimText),"Paused"=>(T("Paused","متوقف مؤقتًا"),"#C7C2BC"),"Failed"=>(T("Failed","فشل"),DownloadRed),"Cancelled"=>(T("Cancelled","أُلغي"),DimText),_=>(T("Ready","جاهز"),DoneGreen)};
    static string DownloadMessage(string message)=>message switch{
        "Finding people to download from…"=>T(message,"جارٍ البحث عن أشخاص للتنزيل منهم…"),"Getting the file list…"=>T(message,"جارٍ جلب قائمة الملفات…"),"Checking the file…"=>T(message,"جارٍ التحقق من الملف…"),
        "No one is sharing this right now. Pick another release."=>T(message,"لا أحد يشارك هذا الملف الآن. اختر إصدارًا آخر."),"No one is sharing this right now. Try another release."=>T(message,"لا أحد يشارك هذا الملف الآن. جرب إصدارًا آخر."),
        "Switched to another release — the first one had no one sharing"=>T(message,"تم التبديل إلى إصدار آخر — لم يكن أحد يشارك الإصدار الأول"),
        "Saved to the PC library"=>T(message,"تم الحفظ في مكتبة الكمبيوتر"),"Interrupted · resume to continue"=>T(message,"انقطع التنزيل · استأنفه للمتابعة"),
        "Video saved · checking release subtitles"=>T(message,"تم حفظ الفيديو · جارٍ فحص ترجمات الإصدار"),"Video saved · finding English / Arabic subtitles"=>T(message,"تم حفظ الفيديو · جارٍ البحث عن الترجمات"),
        _=>T(message)};
    // Speeds, sizes and drive letters get their own left-to-right element so "4.2 MB/s" keeps its reading order inside an Arabic line.
    static void FillInlines(TextBlock tb,IReadOnlyList<(string Text,string Color,bool Latin,bool Bold)> parts)
    {
        tb.Inlines.Clear();
        foreach(var p in parts)
        {
            var weight=p.Bold?FontWeights.SemiBold:FontWeights.Normal;
            if(p.Latin&&Loc.Arabic)tb.Inlines.Add(new InlineUIContainer(new TextBlock{Text=p.Text,FontSize=tb.FontSize,Foreground=Brush(p.Color),FontWeight=weight,FlowDirection=FlowDirection.LeftToRight}){BaselineAlignment=BaselineAlignment.Center});
            else tb.Inlines.Add(new Run(p.Text){Foreground=Brush(p.Color),FontWeight=weight});
        }
    }
    record DownloadSeg(string Text,string Color=DimText,bool Latin=false,string Sep=" · ");
    List<DownloadSeg> DownloadStatusParts(PcDownloadJob j)
    {
        var (word,color)=DownloadState(j.Status);var parts=new List<DownloadSeg>();
        void Add(string text,bool latin=false,string sep=" · "){if(text.Length>0)parts.Add(new(text,DimText,latin,sep));}
        switch(j.Status)
        {
            case "Completed":
                parts.Add(new(DownloadMessage(j.Message.Length>0?j.Message:"Saved to the PC library"),DoneGreen));Add(DownloadMessage(j.SubtitleMessage));return parts;
            case "Downloading":
                parts.Add(new(word,color));if(j.Service=="Torrents")Add(DownloadMessage(j.Message));if(j.Speed>=1)Add(DownloadEta.Rate(j.Speed),true);Add(DownloadEta.Left(j.EtaSeconds,Loc.Arabic));Add(DownloadEta.Transferred(j.Bytes,j.Size),true);return parts;
            case "Queued":
                parts.Add(new(word,color));Add(T("for a free slot","موقع تنزيل متاح"),false," ");return parts;
            case "Failed":
                parts.Add(new(word,color));Add(j.Message.Length>0?DownloadMessage(j.Message):T("Check the connection, then retry","تحقق من الاتصال ثم أعد المحاولة"),false," — ");return parts;
            case "Cancelled":
                parts.Add(new(word,color));return parts;
            default:
                parts.Add(new(word,color));if(j.Status=="Paused"&&j.Bytes>0)Add(DownloadEta.Transferred(j.Bytes,j.Size),true);Add(DownloadMessage(j.Message));if(j.Status=="Subtitles")Add(DownloadMessage(j.SubtitleMessage));return parts;
        }
    }
    // "14.3 GB free on D:" as (text, isLatin) chunks, so the Arabic line can isolate the numbers and the drive letter.
    List<(string Text,bool Latin)> DownloadFreeSpaceParts(PcDownloads manager)
    {
        try
        {
            var config=manager.Config;var root=config.ShowsRoot.Length>0?config.ShowsRoot:config.MoviesRoot.Length>0?config.MoviesRoot:store.State.Roots.FirstOrDefault()?.Path??"";if(root.Length==0)return [];
            var free=manager.FreeBytesFor(root);if(free<0)return [];var inv=System.Globalization.CultureInfo.InvariantCulture;
            var size=free>=1073741824?(free/1073741824d).ToString("0.0",inv)+" GB":(free/1048576d).ToString("0",inv)+" MB";var drive=(Path.GetPathRoot(Path.GetFullPath(root))??"").TrimEnd('\\','/');
            if(!Loc.Arabic)return [(drive.Length>0?$"{size} free on {drive}":$"{size} free",false)];
            return drive.Length>0?[(size,true),(" متاحة على ",false),(drive,true)]:[(size,true),(" متاحة",false)];
        }
        catch(Exception){return [];}
    }
    string DownloadPoster(PcDownloadJob j)
    {
        try{var poster=j.Target.Poster.Length>0?j.Target.Poster:store.EpisodesFor(j.Target.GroupKey).Select(i=>i.Poster).FirstOrDefault(p=>p.Length>0)??"";return poster.Length>0&&File.Exists(poster)?poster:"";}catch(Exception){return "";}
    }
    string DownloadEpisodeLine(PcDownloadJob j)
    {
        var t=j.Target;if(t.IsMovie)return t.Year>0?t.Year.ToString():"";
        var name=t.EpisodeTitle;if(name.Length==0)try{name=store.EpisodesFor(t.GroupKey).FirstOrDefault(i=>i.Season==t.Season&&i.Episode==t.Episode)?.EpisodeTitle??"";}catch(Exception){}
        return $"S{t.Season:00} · E{t.Episode:00}"+(name.Length>0?" — "+name:"");
    }
    VideoItem? ResolveDownloaded(PcDownloadJob j)
    {
        var archive=store.Archive;
        return archive.FirstOrDefault(i=>i.Id==j.Target.Id&&i.Available&&File.Exists(i.Path))??archive.FirstOrDefault(i=>i.Available&&i.Path.Equals(j.Destination,StringComparison.OrdinalIgnoreCase)&&File.Exists(i.Path));
    }
    void ResolveDownloadPlay(DownloadRowView v,PcDownloadJob j)
    {
        v.ResolvedAt=DateTime.UtcNow;v.Library=ResolveDownloaded(j);
        if(v.Play!=null){v.Play.IsEnabled=v.Library!=null;v.Play.ToolTip=v.Library!=null?null:T("Not in your library yet — scan the library","لم يظهر في مكتبتك بعد — افحص المكتبة");}
    }
    void ShowInFolder(string path)
    {
        try
        {
            if(File.Exists(path))System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",$"/select,\"{path}\""){UseShellExecute=true});
            else if(Path.GetDirectoryName(path) is {} dir&&Directory.Exists(dir))System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",$"\"{dir}\""){UseShellExecute=true});
            else Status.Text=T("That folder isn't available.","هذا المجلد غير متاح.");
        }
        catch(Exception){Status.Text=T("Couldn't open the folder.","تعذر فتح المجلد.");}
    }
    // Poster (or a letter tile) at 48×72, shared by the page rows and the top-bar popover.
    FrameworkElement DownloadThumb(PcDownloadJob j,double width,double height)
    {
        var title=j.Target.Title.Length>0?j.Target.Title:j.Name;
        var tile=new Grid{Width=width,Height=height,ClipToBounds=true,VerticalAlignment=VerticalAlignment.Top,Background=new LinearGradientBrush(Brush("#36312B").Color,Brush("#1C1C1F").Color,45)};
        tile.Children.Add(new TextBlock{Text=title.Length>0?title[..1].ToUpperInvariant():"↓",FontSize=width*.42,FontWeight=FontWeights.SemiBold,Foreground=Brush("#E4C084"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Opacity=.85});
        var poster=DownloadPoster(j);
        if(poster.Length>0){var image=new Image{Stretch=Stretch.UniformToFill,FlowDirection=FlowDirection.LeftToRight};if(CachedImage(poster,160) is {} cached)image.Source=cached;else _=SetArtwork(image,poster,160);tile.Children.Add(image);}
        return new Border{Width=width,Height=height,CornerRadius=new(width>40?8:6),ClipToBounds=true,Child=tile,VerticalAlignment=VerticalAlignment.Top};
    }
    DownloadRowView NewDownloadRow(PcDownloadJob j)
    {
        var v=new DownloadRowView();var title=j.Target.Title.Length>0?j.Target.Title:j.Name;
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        grid.Children.Add(DownloadThumb(j,48,72));
        var text=new StackPanel{Margin=new(14,0,12,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(text,1);grid.Children.Add(text);
        var name=Text(title,Density.Pick(13.5,15),"#F4F1EB",FontWeights.SemiBold);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.ToolTip=title;text.Children.Add(name);
        var line=DownloadEpisodeLine(j);if(line.Length>0){var episode=Text(line,12,DimText);episode.TextWrapping=TextWrapping.NoWrap;episode.TextTrimming=TextTrimming.CharacterEllipsis;episode.Margin=new(0,2,0,0);if(Loc.Arabic){episode.FlowDirection=FlowDirection.LeftToRight;episode.HorizontalAlignment=HorizontalAlignment.Left;}text.Children.Add(episode);}
        v.Status=Text("",12);v.Status.Margin=new(0,6,0,0);text.Children.Add(v.Status);
        v.Bar=new ProgressBar{Height=4,VerticalAlignment=VerticalAlignment.Center};v.Percent=Text("",11.5,"#C7C2BC");v.Percent.FlowDirection=FlowDirection.LeftToRight;v.Percent.Margin=new(10,0,0,0);v.Percent.MinWidth=34;v.Percent.TextAlignment=TextAlignment.Right;v.Percent.VerticalAlignment=VerticalAlignment.Center;
        v.BarRow=new Grid{Margin=new(0,8,0,0)};v.BarRow.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});v.BarRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});v.BarRow.Children.Add(v.Bar);Grid.SetColumn(v.Percent,1);v.BarRow.Children.Add(v.Percent);text.Children.Add(v.BarRow);
        v.Actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(v.Actions,2);grid.Children.Add(v.Actions);
        v.Root=new Border{Background=Brush("#141416"),BorderBrush=Brush("#1FFFFFFF"),BorderThickness=new(1),CornerRadius=new(12),Padding=new(12),Margin=new(0,0,0,8),Child=grid,Tag="DownloadRow:"+j.Id};
        System.Windows.Automation.AutomationProperties.SetName(v.Root,title+" "+line);
        return v;
    }
    void BuildDownloadActions(DownloadRowView v,PcDownloadJob j)
    {
        v.Actions.Children.Clear();v.Play=null;var id=j.Id;var label=j.Name;
        void Do(string action){try{downloads?.Control(id,action);}catch(Exception){Status.Text=T("Couldn't change this download.","تعذر تغيير هذا التنزيل.");}}
        Button Small(string text,Action action,bool primary=false)
        {
            var b=ActionButton(text,action,primary);b.Padding=new(13,5,13,5);b.FontSize=12.5;b.Margin=new(8,0,0,0);b.MinWidth=68;b.VerticalAlignment=VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(b,text+" · "+label);v.Actions.Children.Add(b);return b;
        }
        var menu=new List<(string Text,Action Act)>();
        switch(j.Status)
        {
            case "Paused":Small(T("Resume","استئناف"),()=>Do("resume"));menu.Add((T("Cancel download","إلغاء التنزيل"),()=>ShowDownloadCancel(j)));break;
            case "Failed" or "Cancelled":Small(T("Retry","إعادة المحاولة"),()=>Do("resume"));break;
            case "Completed":
                v.Play=Small(T("▶ Play","▶ تشغيل"),()=>{var item=v.Library??ResolveDownloaded(j);if(item==null){Status.Text=T("Scan the library to play this download.","افحص المكتبة لتشغيل هذا التنزيل.");return;}_=PlayVideo(item);},true);ResolveDownloadPlay(v,j);
                menu.Add((T("Show in folder","إظهار في المجلد"),()=>ShowInFolder(j.Destination)));break;
            default:Small(T("Pause","إيقاف مؤقت"),()=>Do("pause"));menu.Add((T("Cancel download","إلغاء التنزيل"),()=>ShowDownloadCancel(j)));break;
        }
        if(menu.Count==0)return;
        var more=ActionButton("⋯",()=>{});more.Padding=new(10,2,10,7);more.FontSize=16;more.Margin=new(6,0,0,0);more.VerticalAlignment=VerticalAlignment.Center;more.Background=Brush("#1FFFFFFF");more.ToolTip=T("More","المزيد");System.Windows.Automation.AutomationProperties.SetName(more,T("More options for","خيارات أخرى لـ")+" "+label);
        more.Click+=(_,e)=>{e.Handled=true;var popup=new ContextMenu{FlowDirection=FlowDirection};foreach(var (text,act) in menu){var entry=new MenuItem{Header=text};var run=act;entry.Click+=(_,_)=>run();popup.Items.Add(entry);}more.ContextMenu=popup;popup.PlacementTarget=more;popup.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;popup.IsOpen=true;};
        v.Actions.Children.Add(more);
    }
    void UpdateDownloadRow(DownloadRowView v,PcDownloadJob j)
    {
        var parts=DownloadStatusParts(j);var key=string.Join("\u0001",parts.Select(p=>p.Sep+p.Text));
        if(key!=v.StatusKey)
        {
            v.StatusKey=key;v.Status.Inlines.Clear();
            var pieces=new List<(string Text,string Color,bool Latin,bool Bold)>();
            for(var i=0;i<parts.Count;i++){var p=parts[i];if(i>0&&p.Sep.Length>0)pieces.Add((p.Sep,"#6F6B66",false,false));pieces.Add((p.Text,p.Color,p.Latin,i==0&&j.Status!="Completed"));}
            FillInlines(v.Status,pieces);
        }
        var known=j.Size>0;var indeterminate=j.Status=="Preparing";
        v.Bar.IsIndeterminate=indeterminate;v.Bar.Maximum=Math.Max(1,j.Size);v.Bar.Value=Math.Min(Math.Max(0,j.Bytes),Math.Max(1,j.Size));
        v.Percent.Text=!indeterminate&&known?$"{(int)Math.Clamp(Math.Floor(j.Bytes*100d/j.Size),0,100)}%":"";
        v.BarRow.Visibility=j.Status is "Downloading" or "Preparing" or "Subtitles"||j.Status is "Paused" or "Failed" or "Queued"&&j.Bytes>0&&known?Visibility.Visible:Visibility.Collapsed;
        if(v.State!=j.Status)
        {
            v.State=j.Status;v.Bar.Foreground=Brush(j.Status=="Failed"?DownloadRed:j.Status is "Paused" or "Queued"?"#8A857E":DownloadGold);BuildDownloadActions(v,j);
        }
        else if(j.Status=="Completed"&&v.Library==null&&DateTime.UtcNow-v.ResolvedAt>TimeSpan.FromSeconds(3))ResolveDownloadPlay(v,j);
    }
    Action BuildDownloadPage(StackPanel page)
    {
        var manager=downloads!;Action update=()=>{};
        Button Quiet(string label,Action action,bool primary=false){var b=ActionButton(label,action,primary);b.Margin=new(8,0,0,0);b.VerticalAlignment=VerticalAlignment.Center;return b;}
        var head=new Grid{Margin=new(0,Density.Pick(8,14),0,0)};head.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});head.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        // The page chrome already shows the title; this row is the live summary and the quiet tools.
        var titles=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        var summary=Text("",13,"#C7C2BC");summary.Margin=new(0,0,12,0);titles.Children.Add(summary);head.Children.Add(titles);
        var tools=new WrapPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetColumn(tools,1);head.Children.Add(tools);
        void ControlAll(string action,Func<PcDownloadJob,bool> pick){try{foreach(var j in manager.Jobs().Where(pick))manager.Control(j.Id,action);}catch(Exception){Status.Text=T("Couldn't change these downloads.","تعذر تغيير هذه التنزيلات.");}update();}
        var pauseAll=Quiet(T("Pause all","إيقاف الكل مؤقتًا"),()=>ControlAll("pause",j=>j.Status is "Queued" or "Preparing" or "Downloading" or "Subtitles"));
        var resumeAll=Quiet(T("Resume all","استئناف الكل"),()=>ControlAll("resume",j=>j.Status=="Paused"));
        var find=Quiet(T("Find something new","ابحث عن جديد"),()=>Navigate("Discover"));
        var settings=Quiet(T("Settings"),()=>{settingsSection="Downloads";Navigate("Settings");});
        foreach(var b in new[]{pauseAll,resumeAll,find,settings})tools.Children.Add(b);
        page.Children.Add(head);
        var empty=new Border{Background=Brush("#141416"),BorderBrush=Brush("#1FFFFFFF"),BorderThickness=new(1),CornerRadius=new(14),Padding=new(24,22,24,22),Margin=new(0,18,0,4),Visibility=Visibility.Collapsed,Tag="DownloadsEmpty"};
        var emptyBody=new StackPanel();emptyBody.Children.Add(Text(T("Nothing downloading yet","لا شيء قيد التنزيل بعد"),15,weight:FontWeights.SemiBold));
        var emptyText=Text(T("Downloads save movies and episodes on this PC, so they play without a connection and join your library.","التنزيلات تحفظ الأفلام والحلقات على هذا الكمبيوتر لتُشغَّل دون اتصال وتنضم إلى مكتبتك."),13,"#C7C2BC");emptyText.Margin=new(0,6,0,14);emptyBody.Children.Add(emptyText);
        emptyBody.Children.Add(Quiet(T("Find something to download","ابحث عن شيء لتنزيله"),()=>Navigate("Discover"),true));((Button)emptyBody.Children[^1]).Margin=new(0);
        var hint=Text(T("Tip: Keep ready prepares your next episodes automatically.","نصيحة: خاصية إبقاء الحلقات جاهزة تحضّر حلقاتك القادمة تلقائيًا."),12,DimText);hint.Margin=new(0,12,0,0);emptyBody.Children.Add(hint);empty.Child=emptyBody;page.Children.Add(empty);
        var sections=new Dictionary<string,(StackPanel Box,StackPanel Items,TextBlock Count)>();
        foreach(var (key,label) in new[]{("now",T("Downloading now","قيد التنزيل الآن")),("waiting",T("Waiting","بالانتظار")),("attention",T("Needs attention","تحتاج اهتمامًا")),("ready",T("Ready to watch","جاهز للمشاهدة"))})
        {
            var box=new StackPanel{Tag="DownloadsSection:"+key,Visibility=Visibility.Collapsed};System.Windows.Automation.AutomationProperties.SetName(box,label);
            var header=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,Density.Pick(18,24),0,8)};header.Children.Add(Text(label,Density.Pick(13.5,15),weight:FontWeights.SemiBold));
            var count=Text("",12.5,DimText);count.Margin=new(8,1,0,0);count.VerticalAlignment=VerticalAlignment.Center;count.FlowDirection=FlowDirection.LeftToRight;header.Children.Add(count);
            var items=new StackPanel();box.Children.Add(header);box.Children.Add(items);page.Children.Add(box);sections[key]=(box,items,count);
        }
        var showMore=Quiet("",()=>{showAllReady=!showAllReady;update();});showMore.Margin=new(0,2,0,0);showMore.Visibility=Visibility.Collapsed;sections["ready"].Box.Children.Add(showMore);
        readyCentreHost=new Border{Child=PreparationCentre()};
        var keepHeader=new StackPanel{Orientation=Orientation.Horizontal};keepHeader.Children.Add(Text(T("Keep ready","إبقاء الحلقات جاهزة"),Density.Pick(13.5,15),weight:FontWeights.SemiBold));
        var keepNote=Text(T("Next episodes prepared on this PC","الحلقات القادمة المحضّرة على هذا الكمبيوتر"),12,DimText);keepNote.Margin=new(10,1,0,0);keepHeader.Children.Add(keepNote);
        keepReadyExpander=new Expander{Header=keepHeader,Content=readyCentreHost,Margin=new(0,Density.Pick(22,28),0,12),IsExpanded=keepReadyOpen};
        keepReadyExpander.Expanded+=(_,_)=>keepReadyOpen=true;keepReadyExpander.Collapsed+=(_,_)=>keepReadyOpen=false;System.Windows.Automation.AutomationProperties.SetName(keepReadyExpander,T("Keep ready","إبقاء الحلقات جاهزة"));page.Children.Add(keepReadyExpander);
        var rows=new Dictionary<string,DownloadRowView>();List<(string Text,bool Latin)> freeParts=[];var freeAt=DateTime.MinValue;System.Windows.Threading.DispatcherTimer? resolve=null;
        void Update()
        {
            var jobs=manager.Jobs();var groups=jobs.GroupBy(j=>DownloadGroup(j.Status)).ToDictionary(g=>g.Key,g=>g.ToList());
            List<PcDownloadJob> Of(string key)=>groups.GetValueOrDefault(key)??[];
            var now=Of("now").OrderBy(j=>j.CreatedAt).ToList();var waiting=Of("waiting").OrderBy(j=>j.CreatedAt).ToList();var attention=Of("attention").OrderByDescending(j=>j.CreatedAt).ToList();var done=Of("ready").OrderByDescending(j=>j.CreatedAt).ToList();
            var desired=new Dictionary<string,List<PcDownloadJob>>{["now"]=now,["waiting"]=waiting,["attention"]=attention,["ready"]=showAllReady?done:done.Take(12).ToList()};
            var keep=desired.Values.SelectMany(l=>l).Select(j=>j.Id).ToHashSet();
            foreach(var gone in rows.Keys.Where(id=>!keep.Contains(id)).ToList()){if(rows[gone].Root.Parent is Panel owner)owner.Children.Remove(rows[gone].Root);rows.Remove(gone);}
            foreach(var (key,list) in desired)
            {
                var section=sections[key];var views=new List<UIElement>();
                foreach(var j in list){if(!rows.TryGetValue(j.Id,out var view)){view=NewDownloadRow(j);rows[j.Id]=view;}view.Group=key;UpdateDownloadRow(view,j);views.Add(view.Root);}
                for(var i=0;i<views.Count;i++)if(i>=section.Items.Children.Count||!ReferenceEquals(section.Items.Children[i],views[i])){if(views[i] is FrameworkElement element&&element.Parent is Panel old)old.Children.Remove(views[i]);section.Items.Children.Insert(i,views[i]);}
                while(section.Items.Children.Count>views.Count)section.Items.Children.RemoveAt(section.Items.Children.Count-1);
                section.Box.Visibility=list.Count>0?Visibility.Visible:Visibility.Collapsed;section.Count.Text=(key=="ready"?done.Count:list.Count).ToString(Loc.Culture);
            }
            showMore.Visibility=done.Count>12?Visibility.Visible:Visibility.Collapsed;showMore.Content=showAllReady?T("Show fewer","عرض أقل"):T("Show all ({0})","عرض الكل ({0})",done.Count);
            empty.Visibility=jobs.Count==0?Visibility.Visible:Visibility.Collapsed;
            pauseAll.Visibility=now.Count+waiting.Count(j=>j.Status=="Queued")>1?Visibility.Visible:Visibility.Collapsed;resumeAll.Visibility=waiting.Count(j=>j.Status=="Paused")>1?Visibility.Visible:Visibility.Collapsed;
            var line=new List<string>();
            if(now.Count>0)line.Add(T("{0} downloading","{0} قيد التنزيل",now.Count));if(waiting.Count>0)line.Add(T("{0} waiting","{0} بالانتظار",waiting.Count));
            if(attention.Count>0)line.Add(attention.Count==1?T("1 needs attention","1 يحتاج اهتمامًا"):T("{0} need attention","{0} تحتاج اهتمامًا",attention.Count));
            if(line.Count==0)line.Add(jobs.Count==0?T("No downloads yet","لا توجد تنزيلات بعد"):T("Nothing downloading","لا شيء قيد التنزيل"));
            if(DateTime.UtcNow-freeAt>TimeSpan.FromSeconds(10)){freeAt=DateTime.UtcNow;freeParts=DownloadFreeSpaceParts(manager);}
            var pieces=new List<(string Text,string Color,bool Latin,bool Bold)>{(string.Join(" · ",line),"#C7C2BC",false,false)};
            if(freeParts.Count>0){pieces.Add((" · ","#6F6B66",false,false));pieces.AddRange(freeParts.Select(p=>(p.Text,"#C7C2BC",p.Latin,false)));}
            if(Loc.Arabic)FillInlines(summary,pieces);else{summary.Inlines.Clear();summary.Text=string.Concat(pieces.Select(p=>p.Text));}
            // A finished file can take a moment to appear in the library; look again until it does.
            if(rows.Values.Any(r=>r.State=="Completed"&&r.Library==null)&&resolve==null){resolve=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};resolve.Tick+=(_,_)=>{resolve.Stop();resolve=null;if(!closing&&ReferenceEquals(downloadPageUpdate,update))update();};resolve.Start();}
        }
        update=Update;return update;
    }
    // ---- Top-bar Downloads button and its popover (an in-window layer, like the sheets) ----
    int topDownloadCount=-1;Grid? downloadsPopover;StackPanel? downloadsPopoverRows;
    void DownloadsTop_Click(object sender,RoutedEventArgs e)=>ToggleDownloadsPopover();
    void UpdateDownloadsTop()
    {
        if(downloads==null)return;var count=downloads.ActiveCount;var any=downloads.JobCount>0;
        DownloadsTop.Visibility=any?Visibility.Visible:Visibility.Collapsed;
        if(topDownloadCount!=count||DownloadsTop.Content==null){topDownloadCount=count;DownloadsTop.Content=DownloadIcon(count);}
        var label=T("Downloads","التنزيلات")+(count>0?" · "+count.ToString(Loc.Culture):"");DownloadsTop.ToolTip=label;System.Windows.Automation.AutomationProperties.SetName(DownloadsTop,label);
        if(!any&&downloadsPopover!=null)CloseDownloadsPopover();
    }
    void ToggleDownloadsPopover(){if(downloadsPopover!=null)CloseDownloadsPopover();else OpenDownloadsPopover();}
    void CloseDownloadsPopover()
    {
        if(downloadsPopover==null)return;var overlay=downloadsPopover;downloadsPopover=null;downloadsPopoverRows=null;
        if(overlay.Parent is Panel host)host.Children.Remove(overlay);if(DownloadsTop.IsVisible)DownloadsTop.Focus();
    }
    void OpenDownloadsPopover()
    {
        if(downloads==null||downloadsPopover!=null)return;CloseHoverCard();
        var host=(Grid)LibraryRoot.Parent;var flow=Loc.Arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        var overlay=new Grid{Background=Brush("#01000000"),FlowDirection=FlowDirection.LeftToRight,Tag="DownloadsPopover"};System.Windows.Controls.Panel.SetZIndex(overlay,90);
        var box=new StackPanel();
        var panel=new Border{Width=320,Background=Brush("#141416"),BorderBrush=Brush("#2EFFFFFF"),BorderThickness=new(1),CornerRadius=new(14),FlowDirection=flow,HorizontalAlignment=Loc.Arabic?HorizontalAlignment.Left:HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(28,Math.Max(0,TopBar.ActualHeight-6),28,0),Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=32,ShadowDepth=6,Opacity=.5,Color=Colors.Black},Child=box,Tag="DownloadsPopoverPanel"};
        var header=new DockPanel{Margin=new(16,10,8,4)};
        var seeAll=ActionButton(T("See all","عرض الكل"),()=>{CloseDownloadsPopover();Navigate("Downloads");});seeAll.Padding=new(10,4,10,4);seeAll.FontSize=12;DockPanel.SetDock(seeAll,Dock.Right);header.Children.Add(seeAll);
        var title=Text(T("Downloads","التنزيلات"),14,weight:FontWeights.SemiBold);title.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(title);box.Children.Add(header);
        downloadsPopoverRows=new StackPanel{Margin=new(8,0,8,10)};box.Children.Add(downloadsPopoverRows);
        overlay.Children.Add(panel);overlay.MouseDown+=(_,e)=>{if(ReferenceEquals(e.OriginalSource,overlay))CloseDownloadsPopover();};
        overlay.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){CloseDownloadsPopover();e.Handled=true;}};
        System.Windows.Input.KeyboardNavigation.SetTabNavigation(overlay,System.Windows.Input.KeyboardNavigationMode.Cycle);
        host.Children.Add(overlay);downloadsPopover=overlay;RefreshDownloadsPopover();panel.Loaded+=(_,_)=>seeAll.Focus();
        if(Motion.Enabled)Motion.FadeIn(panel);
    }
    void RefreshDownloadsPopover()
    {
        if(downloadsPopoverRows==null||downloads==null)return;var rows=downloadsPopoverRows;rows.Children.Clear();
        var jobs=downloads.Jobs().OrderByDescending(j=>j.CreatedAt).Take(5).ToList();
        if(jobs.Count==0){var none=Text(T("Nothing here yet.","لا شيء هنا بعد."),12.5,DimText);none.Margin=new(8,6,8,6);rows.Children.Add(none);return;}
        foreach(var j in jobs)rows.Children.Add(DownloadMiniRow(j));
    }
    FrameworkElement DownloadMiniRow(PcDownloadJob j)
    {
        var (word,color)=DownloadState(j.Status);var title=j.Target.Title.Length>0?j.Target.Title:j.Name;var line=DownloadEpisodeLine(j);
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.Children.Add(DownloadThumb(j,28,42));
        var text=new StackPanel{Margin=new(10,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(text,1);grid.Children.Add(text);
        var name=Text(title,12.5,"#F4F1EB",FontWeights.SemiBold);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(name);
        var meta=new DockPanel{Margin=new(0,2,0,0)};var state=Text(word,11.5,color,FontWeights.SemiBold);state.TextWrapping=TextWrapping.NoWrap;DockPanel.SetDock(state,Dock.Right);meta.Children.Add(state);
        var episode=Text(line,11,DimText);episode.TextWrapping=TextWrapping.NoWrap;episode.TextTrimming=TextTrimming.CharacterEllipsis;if(Loc.Arabic){episode.FlowDirection=FlowDirection.LeftToRight;episode.HorizontalAlignment=HorizontalAlignment.Left;}meta.Children.Add(episode);text.Children.Add(meta);
        var known=j.Size>0;var showBar=j.Status is "Downloading" or "Preparing" or "Subtitles"||j.Status is "Paused" or "Failed" or "Queued"&&j.Bytes>0&&known;
        if(showBar)
        {
            var barRow=new Grid{Margin=new(0,5,0,0)};barRow.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});barRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var bar=new ProgressBar{Height=4,IsIndeterminate=j.Status=="Preparing",Maximum=Math.Max(1,j.Size),Value=Math.Min(Math.Max(0,j.Bytes),Math.Max(1,j.Size)),Foreground=Brush(j.Status=="Failed"?DownloadRed:j.Status is "Paused" or "Queued"?"#8A857E":DownloadGold),VerticalAlignment=VerticalAlignment.Center};barRow.Children.Add(bar);
            var percent=Text(j.Status!="Preparing"&&known?$"{(int)Math.Clamp(Math.Floor(j.Bytes*100d/j.Size),0,100)}%":"",11,"#C7C2BC");percent.FlowDirection=FlowDirection.LeftToRight;percent.Margin=new(8,0,0,0);percent.MinWidth=30;percent.TextAlignment=TextAlignment.Right;Grid.SetColumn(percent,1);barRow.Children.Add(percent);text.Children.Add(barRow);
        }
        var row=new Border{Padding=new(8,6,8,6),CornerRadius=new(10),Background=Brush("Transparent"),Cursor=System.Windows.Input.Cursors.Hand,Child=grid,Tag="DownloadsPopoverRow",ToolTip=title+(line.Length>0?" · "+line:"")};
        System.Windows.Automation.AutomationProperties.SetName(row,title+" "+line+" "+word);
        row.MouseEnter+=(_,_)=>row.Background=Brush("#14FFFFFF");row.MouseLeave+=(_,_)=>row.Background=Brush("Transparent");
        row.MouseLeftButtonUp+=(_,_)=>{CloseDownloadsPopover();Navigate("Downloads");};
        return row;
    }
}
