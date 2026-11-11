#if NOIR_VALIDATE
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Drawing=System.Drawing;

namespace Noir;
// Real screen copies (not RenderTargetBitmap, which mirrors Arabic wrongly) of named screens for the UI decision log.
// --ui-shots <out-dir> [--ui-screens a,b,c] [--ui-sizes 940,1320]: each screen at each width x 844 client pixels, English and Arabic, Compact and Comfortable.
// Runs on a copied profile only (the validation runner's rule); it never writes the real one. No real network call: Discover uses fixture handlers, downloads use a fake provider.
// Separate popups, context menus and windows are copied from their own screen rectangle and saved as <screen>-popup-...
public partial class MainWindow
{
    sealed class ShotsEmptyHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"metas\":[]}")});
    }
    sealed class ShotsSlowHandler:HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){await Task.Delay(Timeout.Infinite,token);return new HttpResponseMessage(HttpStatusCode.OK);}
    }
    async Task CaptureUiShots()
    {
        var args=Environment.GetCommandLineArgs();
        string? Arg(string name){var i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
        var directory=Path.GetFullPath(Arg("--ui-shots")??Path.Combine(store.DataDirectory,"ui-shots"));Directory.CreateDirectory(directory);
        var only=Arg("--ui-screens")?.Split(',',StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var sizes=Arg("--ui-sizes")?.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(v=>int.TryParse(v,out var n)?n:0).Where(n=>n>=320).ToArray();if(sizes==null||sizes.Length==0)sizes=[940,1320];
        var log=new List<string>();void Note(string m){log.Add(m);File.WriteAllLines(Path.Combine(directory,"shots.txt"),log);}
        async Task Settle(int ms=600){UpdateLayout();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);await Task.Delay(ms);UpdateLayout();}
        async Task Until(Func<bool> condition,int ms=15000){var end=DateTime.UtcNow.AddMilliseconds(ms);while(!condition()&&DateTime.UtcNow<end)await Task.Delay(80);}
        string Nm(DependencyObject d)=>System.Windows.Automation.AutomationProperties.GetName(d)??"";
        Rect Client(){var root=Area();var a=root.PointToScreen(new Point());var b=root.PointToScreen(new Point(root.ActualWidth,root.ActualHeight));// the player hides LibraryRoot; measuring a collapsed root reads 0 and grew the window to full screen
        return new Rect(new Point(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y)),new Point(Math.Max(a.X,b.X),Math.Max(a.Y,b.Y)));}
        Rect Bounds(FrameworkElement e){var a=e.PointToScreen(new Point());var b=e.PointToScreen(new Point(e.ActualWidth,e.ActualHeight));return new Rect(new Point(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y)),new Point(Math.Max(a.X,b.X),Math.Max(a.Y,b.Y)));}
        FrameworkElement Area()=>current!=null&&PlayerRoot.Visibility==Visibility.Visible?PlayerRoot:LibraryRoot;
        async Task Size(int width,int height)
        {
            ShotsNative.SetThreadExecutionState(0x80000003);/* ES_CONTINUOUS|ES_SYSTEM_REQUIRED|ES_DISPLAY_REQUIRED: a display that sleeps mid-run copies black frames */
            ShowActivated=true;WindowState=WindowState.Normal;Topmost=true;Show();Activate();var dpi=VisualTreeHelper.GetDpi(this);
            Width=width/dpi.DpiScaleX+16;Height=height/dpi.DpiScaleY+39;await Settle(300);
            for(int i=0;i<5;i++){var c=Client();if(Math.Abs(c.Width-width)<.5&&Math.Abs(c.Height-height)<.5)break;Width+=(width-c.Width)/dpi.DpiScaleX;Height+=(height-c.Height)/dpi.DpiScaleY;await Settle(250);}
            var display=System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;Left=display.Left/dpi.DpiScaleX+8;Top=display.Top/dpi.DpiScaleY+8;await Settle(400);
        }
        void CopyRect(string name,Rect c,FrameworkElement? overlay=null,FrameworkElement? source=null)
        {
            int x=(int)Math.Round(c.Left),y=(int)Math.Round(c.Top),w=(int)Math.Round(c.Width),h=(int)Math.Round(c.Height);
            if(w<2||h<2)throw new InvalidOperationException("nothing to copy for "+name);
            using var bitmap=new Drawing.Bitmap(w,h,Drawing.Imaging.PixelFormat.Format32bppArgb);using(var g=Drawing.Graphics.FromImage(bitmap)){var screen=ShotsNative.GetDC(IntPtr.Zero);var target=g.GetHdc();try{ShotsNative.BitBlt(target,0,0,w,h,screen,x,y,0x00CC0020);}finally{g.ReleaseHdc(target);ShotsNative.ReleaseDC(IntPtr.Zero,screen);}}// SRCCOPY only: CAPTUREBLT also copies VLC's leftover overlay window (black over later library screens); the overlay is drawn in below instead
            // Another app's window over Noir (e.g. a full-screen night overlay) makes a screen copy useless: render Noir's own visual instead and say so.
            if(source is {ActualWidth:>1,ActualHeight:>1}&&CoveredByOtherApp(c))
            {
                using(var g=Drawing.Graphics.FromImage(bitmap))g.Clear(Drawing.Color.FromArgb(0x0B,0x0B,0x0D));DrawVisual(bitmap,x,y,source);Note($"RENDERED {name}: the screen is covered by another app; drawn from Noir's own visual");
            }
            // The player's controls live in VLC's separate overlay window, which a screen copy misses: draw that visual over the video frame.
            if(overlay is {ActualWidth:>1,ActualHeight:>1})
            {
                var o=Bounds(overlay);var rtb=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Round(o.Width),(int)Math.Round(o.Height),96*o.Width/overlay.ActualWidth,96*o.Height/overlay.ActualHeight,PixelFormats.Pbgra32);rtb.Render(overlay);
                var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));using var stream=new MemoryStream();png.Save(stream);stream.Position=0;
                using var layer=new Drawing.Bitmap(stream);using var g=Drawing.Graphics.FromImage(bitmap);g.DrawImage(layer,(int)Math.Round(o.Left)-x,(int)Math.Round(o.Top)-y,layer.Width,layer.Height);
            }
            bitmap.Save(Path.Combine(directory,name+".png"),Drawing.Imaging.ImageFormat.Png);Note($"SAVED {name}.png {w}x{h}");
        }
        bool CoveredByOtherApp(Rect c){var top=ShotsNative.WindowFromPoint(new ShotsNative.POINT{X=(int)(c.Left+c.Width/2),Y=(int)(c.Top+c.Height/2)});ShotsNative.GetWindowThreadProcessId(ShotsNative.GetAncestor(top,2),out var pid);return pid!=0&&pid!=Environment.ProcessId;}
        void DrawVisual(Drawing.Bitmap bitmap,int x,int y,FrameworkElement e){var o=Bounds(e);var rtb=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Round(o.Width),(int)Math.Round(o.Height),96*o.Width/e.ActualWidth,96*o.Height/e.ActualHeight,PixelFormats.Pbgra32);rtb.Render(e);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));using var stream=new MemoryStream();png.Save(stream);stream.Position=0;using var layer=new Drawing.Bitmap(stream);using var g=Drawing.Graphics.FromImage(bitmap);g.DrawImage(layer,(int)Math.Round(o.Left)-x,(int)Math.Round(o.Top)-y,layer.Width,layer.Height);}
        // The whole client area (or the player while it is showing).
        void Copy(string name){Activate();if(PlayerRoot.Visibility==Visibility.Visible&&current!=null)ShowControls();var playing=PlayerRoot.Visibility==Visibility.Visible&&current!=null;CopyRect(name,Bounds(Area()),playing?PlayerOverlay:null,Area());}
        // A popup, context menu or window is its own desktop surface: copy its screen rectangle (the top visual of a popup is its PopupRoot).
        FrameworkElement RootOf(FrameworkElement e){DependencyObject d=e;while(d is Visual&&VisualTreeHelper.GetParent(d) is {} p)d=p;return d as FrameworkElement??e;}
        void CopyElement(string name,FrameworkElement e){e.UpdateLayout();var root=RootOf(e);CopyRect(name,Bounds(root),source:root);}
        List<VideoItem>? Show(){return store.Archive.Where(i=>!i.IsMovie&&i.Season>0).GroupBy(i=>i.GroupKey).OrderByDescending(g=>g.Select(i=>i.Season).Distinct().Count()).ThenByDescending(g=>g.Count()).FirstOrDefault()?.ToList();}
        List<VideoItem>? Movie(){return store.Archive.Where(i=>i.IsMovie).GroupBy(i=>i.GroupKey).OrderByDescending(g=>g.Count(store.EverWatched)).Select(g=>g.ToList()).FirstOrDefault();}
        void History(string tab){Navigate("History");trackerTab=tab;Render();ContentScroll.ScrollToTop();}
        void Settings(string section){settingsSection=section;Navigate("Settings");}
        void LibraryPage(string target,string view){page=target;SaveLibraryView(view,"M");Navigate(target);}
        void Detail(List<VideoItem>? g,string tab){if(g==null)throw new InvalidOperationException("no such title in the library copy");Navigate(g[0].IsMovie?"Movies":"Shows");OpenDetails(g);detailTab=tab;Render();ContentScroll.ScrollToTop();
            // The tabs sit below the hero: bring the tab row to the top so About / Activity show their own content.
            if(tab!="Episodes"){UpdateLayout();var t=Descendants(ContentPanel).OfType<System.Windows.Controls.Primitives.ButtonBase>().FirstOrDefault(x=>x.Content as string==T(tab)||Nm(x)==tab);if(t!=null&&t.IsVisible)ContentScroll.ScrollToVerticalOffset(Math.Max(0,ContentScroll.VerticalOffset+t.TranslatePoint(new Point(),ContentScroll).Y-TopBar.ActualHeight-24));}}
        void Click(ButtonBase b)=>b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Button? Find(Func<Button,bool> test)=>Descendants(ContentPanel).OfType<Button>().FirstOrDefault(test);
        Button Need(Func<Button,bool> test,string what)=>Find(test)??throw new InvalidOperationException("control not found: "+what);
        ContextMenu? lastMenu=null;
        EventManager.RegisterClassHandler(typeof(ContextMenu),ContextMenu.OpenedEvent,new RoutedEventHandler((sender,_)=>lastMenu=sender as ContextMenu));
        var pops=new List<FrameworkElement>();var skip=false;var onlyPopups=false;
        // Opens a menu by clicking a button that builds one, and registers it for a popup copy.
        async Task<ContextMenu> MenuFrom(ButtonBase b){lastMenu=null;Click(b);await Until(()=>lastMenu is {IsOpen:true},3000);var m=lastMenu??throw new InvalidOperationException("no menu opened");await Task.Delay(400);pops.Add(m);return m;}
        async Task MenuAt(ContextMenu menu,FrameworkElement? target){menu.PlacementTarget=target??PageTitle;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;await Task.Delay(500);pops.Add(menu);}
        Func<DiscoveryClient> factoryFixture=()=>new DiscoveryClient(store.DataDirectory,new DiscoveryFixtureHandler());
        Func<DiscoveryClient> factoryEmpty=()=>new DiscoveryClient(store.DataDirectory,new ShotsEmptyHandler());
        Func<DiscoveryClient> factoryOffline=()=>new DiscoveryClient(store.DataDirectory,new DiscoveryFixtureHandler(offline:true));
        Func<DiscoveryClient> factorySlow=()=>new DiscoveryClient(store.DataDirectory,new ShotsSlowHandler());
        discoveryFactory=factoryFixture;
        async Task Search(string query){SearchBox.Text=query;searchDebounce?.Stop();await SearchDiscover();}
        async Task DiscoverFirst(){Navigate("Discover");await Until(()=>discoverResults!=null);}
        // ---- Downloads: no real provider, no network. A fake provider and fixture jobs make the real controls render. ----
        PcDownloads? emptyDownloads=null,fixtureDownloads=null;
        async Task SetUpDownloads()
        {
            var media=Path.Combine(directory,"fixture-media");Directory.CreateDirectory(media);if(!store.State.Roots.Any(r=>string.Equals(r.Path,media,StringComparison.OrdinalIgnoreCase)))store.State.Roots.Add(new(){Path=media,Movies=false}); // downloads must go to a library folder (profile copy only)
            PcDownloads Make(){var d=new PcDownloads(store,new UiDownloadProvider(),start:false,freeSpace:_=>10L*1024*1024*1024);d.Configure("https://example.invalid/fixture/manifest.json","fixture-not-real",media,"");d.Changed+=DownloadChanged;return d;}
            emptyDownloads=Make();fixtureDownloads=Make();downloads=fixtureDownloads;
            var group=Show()??Movie();var title=group?[0].Title??"Fixture title";var key=group?[0].CollectionKey??"ui-fixture";var year=group?[0].Year??2026;
            PcDownloadJob Fx(string id,int episode,string status,long size,long bytes,string message)=>new(){Id=id,Name=$"{title} — S01 · E{episode:00}",Target=new VideoItem{Id=id,CollectionKey=key,Title=title,Year=year,Season=1,Episode=episode},Status=status,Size=size,Bytes=bytes,Message=message,Destination=Path.Combine(media,id+".mkv"),CreatedAt=DateTimeOffset.UtcNow.AddMinutes(episode-12)};
            var total=(long)(3.4*1073741824);
            fixtureDownloads.Inject(Fx("ui-now",1,"Downloading",total,(long)(1.2*1073741824),""));fixtureDownloads.Mutate("ui-now",j=>{j.Speed=4.2*1048576;j.EtaSeconds=360;});
            fixtureDownloads.Inject(Fx("ui-wait",2,"Queued",total,0,""));fixtureDownloads.Inject(Fx("ui-paused",3,"Paused",total,total/4,"Interrupted · resume to continue"));
            fixtureDownloads.Inject(Fx("ui-failed",4,"Failed",total,total/10,"The source went offline"));fixtureDownloads.Inject(Fx("ui-done",5,"Completed",2048,2048,"Saved to the PC library"));
            // Preparation centre rows (same fixture as the ready validation).
            var item=store.Archive.FirstOrDefault(i=>!i.IsMovie)??store.Archive.FirstOrDefault();
            if(item!=null)await fixtureDownloads.ChangeReady(s=>
            {
                s.Titles[item.GroupKey]=new(){Group=item.GroupKey,Title=item.Title,ItemId=item.Id,State="Preparing",Reason="Preparing on this PC",Bytes=2*NoirReadyPolicy.GB,Quality="1080p",Episodes=[new(item.Id,item.GroupKey,item.Title,1,2,false,false,false,DateTimeOffset.UtcNow.AddDays(-1))]};
                s.Titles["ready-budget-fixture"]=new(){Group="ready-budget-fixture",Title="Evening watch",State="Blocked",Reason="Preparation budget reached",Quality="1080p",Episodes=[]};
                s.ManagedFiles.Add(new(){JobId="ready-validation",Title=item.Title+" · S01 E01",RemoveAt=DateTimeOffset.Now.AddDays(7)});s.Log("Queued confirmed ready file",item.Title);s.Log("Watched file removal scheduled",item.Title);
            });
            UpdateDownloadBadge();UpdateDownloadsTop();
        }
        VideoItem DownloadEpisode(){var g=Show()??Movie()??throw new InvalidOperationException("no title in the library copy");return g.FirstOrDefault(i=>!CanPlay(i))??g[0];}
        NoirSheet LastSheet(){return OpenSheets.LastOrDefault()??throw new InvalidOperationException("no sheet opened");}
        // ---- Player: a playable local file from the copied library, if any. ----
        var playerMissingNoted=false;
        async Task<bool> Player()
        {
            if(current!=null&&player?.IsPlaying==true){if(fullscreen)ToggleFullscreen(false);return true;}
            var episode=items.FirstOrDefault(i=>!i.IsMovie&&CanPlay(i))??items.FirstOrDefault(CanPlay);
            if(episode==null){if(!playerMissingNoted){Note("player: no playable file");playerMissingNoted=true;}skip=true;return false;}
            store.State.Volume=0;VolumeBar.Value=0;Prefs.StillWatchingAfter=3;Prefs.VolumeBoost=false;
            Width=Width;await PlayVideo(episode,true);await Until(()=>player?.IsPlaying==true&&player.Time>1500,25000);
            if(player?.IsPlaying!=true)throw new InvalidOperationException("the player did not start");
            if(fullscreen)ToggleFullscreen(false);if(WindowState!=WindowState.Normal)WindowState=WindowState.Normal;await Settle(400);
            await Task.Delay(700);return true;
        }
        Button PlayerButton(string name)=>Descendants(PlayerFooter).OfType<Button>().FirstOrDefault(b=>Nm(b)==name)??Descendants(PlayerHeader).OfType<Button>().FirstOrDefault(b=>Nm(b)==name)??throw new InvalidOperationException("player control not found: "+name);
        async Task PlayerMenu(Action<Button> open,string button){ShowControls();open(PlayerButton(button));await Until(()=>lastMenu is {IsOpen:true},3000);var m=lastMenu??throw new InvalidOperationException("no player menu opened");await Task.Delay(400);pops.Add(m);}
        VideoItem OtherEpisode(){var next=current==null?null:LibraryStore.Next(current,items,store.State);return next??items.FirstOrDefault(i=>i!=current&&CanPlay(i))??current!;}
        // Puts everything back to a neutral page between screens.
        var originalMode=store.State.AppMode;var originalAfter=HistoryPrefs.AfterWatching;var originalQuiet=HistoryPrefs.JustWatchedQuiet;var originalVolume=store.State.Volume;
        var originalFilters=(libraryFilter,genreFilter,librarySort,listType,collectionFilter,showsFilter,showsSort,moviesFilter,moviesSort);
        var originalViews=new Dictionary<string,string[]>();foreach(var p in new[]{"Shows","Movies","My List"}){page=p;originalViews[p]=[LibraryView,LibrarySize];}
        async Task Reset(bool keepPlayer)
        {
            CloseAllSheets();CloseShortcutSheet();CloseSmart();if(lastMenu is {IsOpen:true} open)open.IsOpen=false;lastMenu=null;CloseDownloadsPopover();DismissJustWatched(false);CloseHoverCard();
            foreach(var window in OwnedWindows.Cast<Window>().ToList())if(window.Title!="LibVLCSharp.WPF")window.Close();
            if(current!=null){CancelNext();if(!keepPlayer){ReturnToLibrary();await Until(()=>current==null&&LibraryRoot.Visibility==Visibility.Visible,8000);}}
            if(keepPlayer&&current!=null){await Task.Delay(50);return;}
            // A player shell left visible without a title would cover the page and every later capture would be black.
            if(current==null&&(PlayerRoot.Visibility==Visibility.Visible||LibraryRoot.Visibility!=Visibility.Visible)){PlayerRoot.Visibility=Visibility.Collapsed;LibraryRoot.Visibility=Visibility.Visible;if(WindowState!=WindowState.Normal)WindowState=WindowState.Normal;WindowStyle=WindowStyle.SingleBorderWindow;}
            if(store.State.AppMode!=originalMode)ChooseAppMode(originalMode);
            (libraryFilter,genreFilter,librarySort,listType,collectionFilter,showsFilter,showsSort,moviesFilter,moviesSort)=originalFilters;
            discoveryFactory=factoryFixture;discoverResults=null;discoverQuery="";discoverGenre="All genres";discoverFilter="All";discoverMessage="";logQuery="";logFilter="All";logResults=null;downloadsTab="Queue";downloads=fixtureDownloads;
            SearchBox.Text="";searchDebounce?.Stop();settingsSection="Profile";Navigate("Settings");await Task.Delay(80);
        }
        Note("UI shots · "+DateTimeOffset.Now.ToString("O")+" · widths "+string.Join(",",sizes));
        Note("MISSING phone-pairing: the Pair Noir for Android sheet needs a running mobile sync server (opens a network port); validation mode never starts it and ShowPhonePairing returns early without one.");
        Note("MISSING tv-pairing: the TV pairing address, code and check code are the lower half of the same Pair Noir for Android sheet (see phone-pairing).");
        // name, how to open it, optional scroll target (0..1 of the scrollable height)
        var screens=new List<(string Name,Func<Task> Open,double Scroll)>();
        void Add(string name,Func<Task> open,double scroll=0)=>screens.Add((name,open,scroll));
        void AddNow(string name,Action open,double scroll=0)=>screens.Add((name,()=>{open();return Task.CompletedTask;},scroll));
        // Home
        AddNow("home",()=>Navigate("Home"));
        AddNow("home-end",()=>Navigate("Home"),1);
        Add("home-edit",async()=>{Navigate("Home");await Settle(900);Click(Need(b=>Nm(b)=="Edit Home","Edit Home"));await Task.Delay(500);});
        Add("just-watched-card",async()=>{validateJustWatched=true;HistoryPrefs.AfterWatching="Always";HistoryPrefs.JustWatchedQuiet=false;var episode=RecentHistoryViewings().Select(v=>v.Item).FirstOrDefault(i=>!i.IsMovie)??store.Archive.FirstOrDefault(i=>!i.IsMovie&&store.EverWatched(i))??store.Archive.FirstOrDefault()??throw new InvalidOperationException("no title in the library copy");Navigate("Home");await Settle(900);OfferJustWatched(episode);justWatchedTimer?.Stop();});
        // Library pages in each view
        AddNow("shows",()=>Navigate("Shows"));
        foreach(var view in new[]{"Poster","Landscape","List"})
        {
            var v=view;
            AddNow("shows-"+v.ToLowerInvariant(),()=>LibraryPage("Shows",v));
            AddNow("movies-"+v.ToLowerInvariant(),()=>LibraryPage("Movies",v));
            AddNow("my-list-"+v.ToLowerInvariant(),()=>{collectionFilter="My List";LibraryPage("My List",v);});
        }
        AddNow("shows-no-match",()=>{Navigate("Shows");SearchBox.Text="zzzz no match";searchDebounce?.Stop();Render();});
        AddNow("my-list-empty",()=>{collectionFilter="zzzz no such collection";Navigate("My List");});
        // Title pages
        AddNow("show-page",()=>{Navigate("Shows");if(Show() is {} g)OpenDetails(g);});
        AddNow("show-page-episodes",()=>{Navigate("Shows");if(Show() is {} g)OpenDetails(g);},.45);
        AddNow("show-page-about",()=>Detail(Show(),"About"));
        AddNow("show-page-activity",()=>Detail(Show(),"Activity"));
        AddNow("show-page-activity-end",()=>Detail(Show(),"Activity"),1);
        AddNow("movie-page",()=>Detail(Movie(),"About"));
        AddNow("movie-page-activity",()=>Detail(Movie(),"Activity"));
        Add("person-page",async()=>
        {
            var titles=store.Archive.GroupBy(i=>i.GroupKey).Select(g=>g.First()).Take(6).Select(i=>new CreditTitle{ImdbId=i.ImdbId,Name=i.Title,Year=i.Year,IsMovie=i.IsMovie,Poster=i.Poster,Role=T("Cast","تمثيل")}).ToList();
            Navigate("Shows");var window=OpenPersonWindow(new CreditPerson{Name="Sample Person",Character="Lead"},titles);await Task.Delay(1500);onlyPopups=true;pops.Add((FrameworkElement)window.Content);
        });
        // Discover
        Add("discover",async()=>{SearchBox.Text="";Navigate("Discover");await Task.CompletedTask;});
        AddNow("discover-rails",()=>{SearchBox.Text="";Navigate("Discover");},.5);
        AddNow("discover-end",()=>{SearchBox.Text="";Navigate("Discover");},1);
        Add("discover-search",async()=>{Navigate("Discover");await Search("fixture");});
        Add("discover-panel",async()=>{await DiscoverFirst();await OpenDiscover(discoverResults!.Titles.First(t=>!t.IsMovie));await Task.Delay(700);});
        Add("discover-title",async()=>{await DiscoverFirst();await OpenDiscoverFull(discoverResults!.Titles.First(t=>!t.IsMovie));await Until(()=>discoverTitle!=null);});
        Add("discover-title-episodes",async()=>{await DiscoverFirst();await OpenDiscoverFull(discoverResults!.Titles.First(t=>!t.IsMovie));await Until(()=>discoverTitle!=null);},.5);
        Add("discover-title-movie",async()=>{await DiscoverFirst();if(discoverResults!.Titles.FirstOrDefault(t=>t.IsMovie) is not {} movie){skip=true;Note("SKIP discover-title-movie: the fixture catalog has no movie");return;}await OpenDiscoverFull(movie);await Until(()=>discoverTitle!=null);});
        Add("discover-empty",async()=>{discoveryFactory=factoryEmpty;Navigate("Discover");await Search("zzzz no match");});
        Add("discover-offline",async()=>{discoveryFactory=factoryOffline;Navigate("Discover");await Search("zzzz offline probe");});
        Add("discover-loading",async()=>{discoveryFactory=factorySlow;Navigate("Discover");SearchBox.Text="zzzz loading probe";searchDebounce?.Stop();_=SearchDiscover();await Task.Delay(300);});
        // History
        AddNow("history-upnext",()=>History("Up next"));
        AddNow("history-upnext-end",()=>History("Up next"),1);
        AddNow("history-shows",()=>History("Shows"));
        AddNow("history-movies",()=>History("Movies"));
        AddNow("history-diary",()=>History("Diary"));
        AddNow("history-diary-end",()=>History("Diary"),1);
        AddNow("history-stats",()=>History("Stats"));
        AddNow("history-stats-end",()=>History("Stats"),1);
        AddNow("history-upcoming",()=>History("Upcoming"));
        AddNow("history-upcoming-end",()=>History("Upcoming"),1);
        AddNow("history-import",()=>History("Import"));
        AddNow("history-import-end",()=>History("Import"),1);
        Add("history-log",async()=>{History("Up next");OpenLog(null);await Until(()=>logResults!=null);});
        Add("history-log-title",async()=>{History("Up next");OpenLog(null);await Until(()=>logResults!=null);await OpenLogTitle(logResults!.First(t=>!t.IsMovie));await Task.Delay(500);});
        Add("history-log-empty",async()=>{discoveryFactory=factoryEmpty;logQuery="zzzz no match";History("Up next");OpenLog(null);await Until(()=>logResults!=null);});
        Add("history-log-loading",async()=>{discoveryFactory=factorySlow;logQuery="zzzz loading probe";History("Up next");OpenLog(null);await Task.Delay(300);});
        Add("year-story",async()=>{History("Stats");OpenYearStory(historyReviewYear);await Task.Delay(1200);onlyPopups=true;var w=OwnedWindows.Cast<Window>().LastOrDefault(x=>x.Title!="LibVLCSharp.WPF")??throw new InvalidOperationException("the year story window did not open");pops.Add((FrameworkElement)w.Content);});
        AddNow("taste",()=>OpenTaste());
        Add("taste-compare",async()=>
        {
            var mine=store.Archive.Where(i=>TasteCode.Id(i.ImdbId)>0).GroupBy(i=>i.ImdbId).Select(g=>g.First()).ToList();
            var card=TasteCode.Create("Sam",mine.Take(4).Select(i=>TasteCode.Id(i.ImdbId)),mine.Skip(1).Take(12).Select((i,n)=>new TasteRating(TasteCode.Id(i.ImdbId),6+n%5)));
            OpenTaste(TasteCode.Encode(card));await Task.Delay(800);
        });
        // Upcoming is the History tab above. Downloads and sheets
        AddNow("downloads",()=>Navigate("Downloads"));
        AddNow("downloads-empty",()=>{downloads=emptyDownloads;UpdateDownloadBadge();UpdateDownloadsTop();Navigate("Downloads");});
        AddNow("downloads-on-this-pc",()=>{downloadsTab="On this PC";Navigate("Downloads");});
        Add("downloads-popover",async()=>{Navigate("Home");await Settle(700);ToggleDownloadsPopover();await Task.Delay(500);});
        Add("download-sheet-episode",async()=>{Navigate("Shows");FindDownloads(DownloadEpisode());await Until(()=>OpenSheets.Count>0);await Task.Delay(500);});
        Add("download-season-planner",async()=>
        {
            Navigate("Shows");FindDownloads(DownloadEpisode(),true);await Until(()=>OpenSheets.Count>0);await Task.Delay(300);var sheet=LastSheet();
            if(Descendants(sheet.Overlay).OfType<Button>().FirstOrDefault(b=>b.Content as string=="Find releases") is {} find){Click(find);await Task.Delay(900);}else Note("WARNING download-season-planner: no Find releases button; the scope sheet is shown instead.");
        });
        Add("keep-ready-sheet",async()=>{Navigate("Shows");ShowKeepReady(DownloadEpisode());await Until(()=>FindSheet("Keep ready")!=null);if(keepReadyPreviewTask!=null)await Task.WhenAny(keepReadyPreviewTask,Task.Delay(1500));await Task.Delay(400);});
        Add("preparation-centre",async()=>{Navigate("Downloads");await Settle(500);if(keepReadyExpander!=null){keepReadyExpander.IsExpanded=true;await Settle(300);RefreshReadyCentre();await Settle(300);if(readyCentreHost!=null&&Descendants(readyCentreHost).OfType<Expander>().FirstOrDefault() is {} more)more.IsExpanded=true;await Settle(300);}ContentScroll.ScrollToBottom();});
        // Settings, every section (top, then the end where the page is long)
        foreach(var (section,name) in new[]{("Profile","profile"),("Playback","playback"),("History","history"),("Library & folders","library"),("Connections","connections"),("Downloads","downloads"),("Phone","phone"),("Appearance & language","appearance"),("Backup","backup"),("Advanced","advanced")})
        {
            var s=section;AddNow("settings-"+name,()=>Settings(s));AddNow("settings-"+name+"-end",()=>Settings(s),1);
        }
        // Profile switcher (a popup)
        Add("profile-switcher",async()=>{Navigate("Home");await Settle(700);ShowProfileSwitcher();await Task.Delay(500);if(smartPopup?.Child is not FrameworkElement child)throw new InvalidOperationException("the profile switcher did not open");pops.Add(child);});
        // Menus reachable from title cards and rows
        Add("menu-show-card",async()=>{Navigate("Shows");await Settle(900);var g=Show()??throw new InvalidOperationException("no show");await MenuAt(ItemMenu(g.FirstOrDefault(i=>!CanPlay(i))??g[0]),Find(b=>b.IsVisible));});
        Add("menu-show-card-status",async()=>{Navigate("Shows");await Settle(900);var g=Show()??throw new InvalidOperationException("no show");var menu=ItemMenu(g[0]);await MenuAt(menu,Find(b=>b.IsVisible));var status=menu.Items.OfType<MenuItem>().First(m=>IconSet.Src(m.Header)==T("Viewing status","حالة المشاهدة"));status.IsSubmenuOpen=true;await Task.Delay(500);pops.Add((FrameworkElement)status.Items[0]);});
        Add("menu-movie-card",async()=>{Navigate("Movies");await Settle(900);var g=Movie()??throw new InvalidOperationException("no movie");await MenuAt(ItemMenu(g[0]),Find(b=>b.IsVisible));});
        Add("menu-continue-card",async()=>{Navigate("Home");await Settle(1200);await MenuFrom(Need(b=>Nm(b).StartsWith("Options for "),"a Home card ⋯"));});
        Add("menu-episode-row",async()=>{Detail(Show(),"Episodes");await Settle(900);await MenuFrom(Need(b=>Nm(b).StartsWith(T("More options for","خيارات أخرى لـ")),"an episode ⋯"));});
        Add("menu-detail-more",async()=>{Detail(Show(),"Episodes");await Settle(900);await MenuFrom(Need(b=>Nm(b)=="More"||Nm(b)==T("More"),"More tile"));});
        Add("menu-detail-download",async()=>{Detail(Show(),"Episodes");await Settle(900);await MenuFrom(Need(b=>Nm(b)==T("Download season","تنزيل الموسم"),"Download season tile"));});
        Add("menu-detail-rate",async()=>{Detail(Show(),"Episodes");await Settle(900);Click(Need(b=>Nm(b)=="Rate show"||Nm(b).StartsWith("★"),"Rate show tile"));await Until(()=>smartPopup is {IsOpen:true},3000);await Task.Delay(400);if(smartPopup?.Child is not FrameworkElement child)throw new InvalidOperationException("the rating popup did not open");pops.Add(child);});
        Add("menu-history-more",async()=>{History("Up next");await Settle(900);await MenuFrom(Need(b=>IconSet.Src(b.Content)=="[[more]]","History ⋯"));});
        Add("menu-history-card",async()=>{History("Up next");await Settle(900);await MenuFrom(Need(b=>Nm(b).StartsWith(T("More options for","خيارات أخرى لـ")),"an Up next card ⋯"));});
        // Tracker-only mode
        AddNow("tracker-only-upnext",()=>{ChooseAppMode("Tracker");History("Up next");});
        AddNow("tracker-only-shows",()=>{ChooseAppMode("Tracker");History("Shows");});
        AddNow("tracker-only-upcoming",()=>{ChooseAppMode("Tracker");History("Upcoming");});
        AddNow("tracker-only-stats",()=>{ChooseAppMode("Tracker");History("Stats");});
        // Player (needs a playable local file; the screens stay in one playback session)
        Add("player-controls",async()=>{if(await Player())ShowControls();});
        Add("player-audio-menu",async()=>{if(await Player())await PlayerMenu(b=>Audio_Click(b,new RoutedEventArgs()),"Audio and subtitles");});
        Add("player-subtitle-menu",async()=>{if(await Player())await PlayerMenu(b=>Subtitles_Click(b,new RoutedEventArgs()),"Audio and subtitles");});
        Add("player-languages-menu",async()=>{if(await Player())await PlayerMenu(b=>Languages_Click(b,new RoutedEventArgs()),"Audio and subtitles");});
        Add("player-speed-menu",async()=>{if(await Player()){ShowControls();Speed_Click(SpeedButton,new RoutedEventArgs());await Until(()=>lastMenu is {IsOpen:true},3000);await Task.Delay(400);pops.Add(lastMenu??throw new InvalidOperationException("no speed menu opened"));}});
        Add("player-more-menu",async()=>{if(await Player())await PlayerMenu(b=>PlayerMore_Click(b,new RoutedEventArgs()),"More");});
        Add("player-up-next",async()=>{if(await Player()){Prefs.SkipCredits=SkipMode.Manual;nextItem=OtherEpisode();ShowUpNext(nextItem,PL($"{current!.EpisodeLabel} marked watched",$"تم تسجيل مشاهدة {current!.EpisodeLabel}"));creditsReached=true;player!.Time=(long)((store.CompletionStart(current!,currentDuration)+4)*1000);await Until(()=>NextPanel.Visibility==Visibility.Visible,8000);if(NextPanel.Visibility!=Visibility.Visible)throw new InvalidOperationException($"the Up next card did not appear after the credits point (credits {creditsReached}, next {nextItem?.Title ?? "none"}, time {player!.Time/1000}, start {store.CompletionStart(current!,currentDuration):0}, duration {currentDuration:0}, still {StillWatchingPanel.Visibility}, dismissed {creditsPromptDismissed})");ShowControls();await Task.Delay(900);}});// seek past the credits point so the real tick shows the card (forcing it at 0:06 was collapsed by the next tick)
        Add("player-shortcuts",async()=>{if(await Player()){ToggleShortcutSheet(true);await Task.Delay(500);}});
        Add("player-still-watching",async()=>{if(await Player()){var next=OtherEpisode();stillWatching.UserInput();stillWatching.AutoAdvanced();stillWatching.AutoAdvanced();stillWatching.AutoAdvanced();AskStillWatching(next,null);await Task.Delay(600);}});
        var originalDensity=Density.Mode;var originalLanguage=Loc.Arabic?"ar":"en";
        try
        {
            try{await SetUpDownloads();Note("Downloads: fake provider attached; fixture jobs injected (no network, no real download).");}
            catch(Exception e){Note("FAILED download fixture: "+e.Message+" (download screens will show their unavailable state)");}
            foreach(var width in sizes)
            {
                await Size(width,844);
                foreach(var density in new[]{UiDensity.Compact,UiDensity.Comfortable})
                foreach(var language in new[]{"en","ar"})
                {
                    Density.Set(density,save:false);SetLanguage(language);await Settle();
                    foreach(var screen in screens.Where(x=>only==null||only.Contains(x.Name)))
                    {
                        try
                        {
                            pops.Clear();skip=false;onlyPopups=false;await Reset(screen.Name.StartsWith("player-"));
                            {var c=Client();if(Math.Abs(c.Width-width)>1||Math.Abs(c.Height-844)>1)await Size(width,844);}
                            await screen.Open();if(skip)continue;
                            for(int i=0;i<30&&page=="Discover"&&discoverResults==null&&screen.Name!="discover-loading";i++)await Task.Delay(100);await Settle(900);
                            if(screen.Scroll>0){if(ContentScroll.ScrollableHeight<8){Note($"SKIP {screen.Name} {width} {language} {density}: the page does not scroll");continue;}ContentScroll.ScrollToVerticalOffset(ContentScroll.ScrollableHeight*screen.Scroll);await Settle(500);}
                            var suffix=$"{width}-{language}-{density.ToString().ToLowerInvariant()}";
                            if(current!=null&&PlayerRoot.Visibility==Visibility.Visible){var c2=Client();if(Math.Abs(c2.Width-width)>1||Math.Abs(c2.Height-844)>1)await Size(width,844);ShowControls();await Task.Delay(600);}
                            if(!onlyPopups)Copy($"{screen.Name}-{suffix}");
                            foreach(var leak in Descendants(LibraryRoot).Concat(pops.SelectMany(x=>Descendants(x))).OfType<TextBlock>().Where(t=>t.IsVisible&&(t.Text??"").Contains("[[")).Select(t=>t.Text).Distinct().Take(5))Note($"TOKEN {screen.Name} {suffix}: {leak}"); // U58 guard: a raw icon token reached plain text
                            for(int n=0;n<pops.Count;n++)CopyElement($"{screen.Name}-popup{(pops.Count>1?"-"+(n+1):"")}-{suffix}",pops[n]);
                            if(screen.Name=="settings-downloads"&&Descendants(ContentPanel).OfType<TextBlock>().Any(t=>t.Text.Contains("unavailable until Noir restarts")))Note($"WARNING {screen.Name} {width} {language} {density}: still shows 'Downloads are unavailable until Noir restarts'");
                        }
                        catch(Exception e){Note($"FAILED {screen.Name} {width} {language} {density}: {e.Message}");}
                    }
                    try{await Reset(false);}catch(Exception e){Note($"FAILED reset {width} {language} {density}: {e.Message}");}
                }
            }
        }
        finally
        {
            try{await Reset(false);}catch{}
            try{foreach(var (p,v) in originalViews){page=p;SaveLibraryView(v[0],v[1]);}}catch{}
            validateJustWatched=false;HistoryPrefs.AfterWatching=originalAfter;HistoryPrefs.JustWatchedQuiet=originalQuiet;store.State.Volume=originalVolume;
            Density.Set(originalDensity,save:false);SetLanguage(originalLanguage);Topmost=false;Note("DONE");
        }
    }
}
#endif
#if NOIR_VALIDATE
static class ShotsNative
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]public static extern IntPtr GetDC(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]public static extern int ReleaseDC(IntPtr window,IntPtr dc);
    public struct POINT{public int X;public int Y;}
    public struct RECT{public int Left,Top,Right,Bottom;}
    [System.Runtime.InteropServices.DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr w,out RECT r);
    [System.Runtime.InteropServices.DllImport("user32.dll")]public static extern IntPtr GetWindow(IntPtr w,uint cmd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]public static extern int GetWindowLong(IntPtr w,int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")]public static extern IntPtr WindowFromPoint(POINT p);
    [System.Runtime.InteropServices.DllImport("user32.dll")]public static extern IntPtr GetAncestor(IntPtr w,uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]public static extern int GetWindowText(IntPtr w,System.Text.StringBuilder s,int n);
    [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]public static extern int GetClassName(IntPtr w,System.Text.StringBuilder s,int n);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]public static extern uint SetThreadExecutionState(uint flags);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]public static extern bool BitBlt(IntPtr target,int x,int y,int width,int height,IntPtr source,int sx,int sy,int rop);
}
#endif
