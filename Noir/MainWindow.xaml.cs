using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace Noir;
public partial class MainWindow : Window
{
    readonly LibraryStore store;
    List<VideoItem> items = [];
    string page = "Home";
    string? detailKey;
    int? detailSeason;
    string historyFilter="All",historyType="All",librarySort="A–Z",historySort="Recent";
    bool ready, scanning, closing;
    bool metadataRefreshing;
    bool metadataRefreshRequested;
    readonly CancellationTokenSource metadataCancellation=new();
    readonly bool validationMode;
    readonly Dictionary<string, BitmapImage> images = new(StringComparer.OrdinalIgnoreCase);
    long imageBytes;
    string renderedContext="";
    public MainWindow(string? dataDirectory=null,string? playPath=null,bool validate=false,string? initialPage=null)
    {
        InitializeComponent();
        ApplyNavigationIcons();
        SourceInitialized+=(_,_)=>WindowAppearance.Apply(this);
        PlayerOverlay.Visibility=Visibility.Collapsed;
        Video.Content=null;
        store = new LibraryStore(dataDirectory);
        store.PersistenceFailed+=_=>Dispatcher.BeginInvoke(()=>{if(!closing){SavedLabel.Text="Couldn't save progress";SavedLabel.Visibility=Visibility.Visible;Status.Text="Couldn't save the latest progress. Check your library storage.";}});
        validationMode=validate;
        if(!validate) watchSync=new CommunitySync(store,Dispatcher,()=>{if(!closing&&page=="Settings")Status.Text="Community sync updated. Reopen the Community section to see details.";});
        VolumeBar.Value = store.State.Volume;
        ready = true;
        Loaded += async (_, _) =>
        {
            foreach(Window overlay in OwnedWindows) if(overlay.Title=="LibVLCSharp.WPF") overlay.Hide();
            await ScanLibrary();
            if(!validate) {StartDownloads();await StartMobileSync();}
            if(initialPage is "Home" or "History" or "Movies" or "Shows" or "My List" or "Settings" or "Discover" or "Downloads") Navigate(initialPage);
            if(!validate) watchSync?.Wake();
#if NOIR_VALIDATE
            if(validate) { await ValidatePlayback();return; }
#endif
            if(playPath!=null && items.FirstOrDefault(x=>string.Equals(x.Path,playPath,StringComparison.OrdinalIgnoreCase)) is { } item) await PlayVideo(item);
        };
        System.Windows.Automation.AutomationProperties.SetName(SearchBox,"Search movies and shows");
    }
    void Library_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        if(MainPane==null)return;var narrow=ActualWidth<1100;
        SidebarColumn.Width=new GridLength(narrow?168:196);MainPane.Margin=new Thickness(narrow?20:34,narrow?20:26,narrow?20:34,0);
        SearchColumn.Width=narrow?new GridLength(0):new GridLength(290);
        Grid.SetRow(SearchHost,narrow?1:0);Grid.SetColumn(SearchHost,narrow?0:1);Grid.SetColumnSpan(SearchHost,narrow?2:1);
        SearchHost.Margin=new Thickness(narrow?0:16,narrow?12:0,0,0);
        if(PlayerVolume!=null)PlayerVolume.Visibility=ActualWidth<1000?Visibility.Collapsed:Visibility.Visible;
    }
    async Task ScanLibrary(bool refreshMetadata=true)
    {
        if (scanning) return;
        scanning = true;
        Status.Text = "Looking for movies and episodes…";
        try
        {
            var result = await Task.Run(() => { var found = store.Scan(out var warnings); return (found, warnings); });
            if (closing) return;
            items = result.found;
            LibraryCount.Text = $"{store.Archive.Count(x => x.IsMovie)} movies · {store.Archive.Where(x => !x.IsMovie).Select(x => x.GroupKey).Distinct().Count()} shows";
            Status.Text = result.warnings.Count > 0 ? string.Join("  ·  ",result.warnings) : $"{items.Count} videos ready";
            if (!string.IsNullOrEmpty(store.LoadWarning)) Status.Text = store.LoadWarning;
            if(current==null) Render();
            if(!validationMode) WatchLibraryFolders();
            if(!validationMode&&refreshMetadata) _=RefreshMetadata();
        }
        catch (Exception e) { store.Log(e); Status.Text = "Could not scan your library: " + e.Message; }
        finally { scanning = false; }
    }
    void Discover_Click(object sender,RoutedEventArgs e)=>Navigate("Discover");
    void Downloads_Click(object sender,RoutedEventArgs e)=>Navigate("Downloads");
    void Home_Click(object sender, RoutedEventArgs e) => Navigate("Home");
    void Movies_Click(object sender, RoutedEventArgs e) => Navigate("Movies");
    void Shows_Click(object sender, RoutedEventArgs e) => Navigate("Shows");
    void History_Click(object sender, RoutedEventArgs e) {historyFilter="All";historyType="All";Navigate("History");}
    void Watchlist_Click(object sender, RoutedEventArgs e) => Navigate("My List");
    void Settings_Click(object sender, RoutedEventArgs e) => Navigate("Settings");
    async void Scan_Click(object sender, RoutedEventArgs e) => await ScanLibrary();
    DispatcherTimer? searchDebounce;
    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if(!ready)return;
        searchDebounce??=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(220)};
        searchDebounce.Stop();searchDebounce.Tick-=CommitSearch;searchDebounce.Tick+=CommitSearch;searchDebounce.Start();
    }
    void CommitSearch(object? sender,EventArgs e){searchDebounce?.Stop();if(page=="Discover"){discoverTitle=null;_=SearchDiscover();return;}detailKey=null;if(page=="Settings")page="Home";Render();ContentScroll.ScrollToTop();}
    void Navigate(string target)
    {
        CancelDiscover();SmoothScrolling.Stop(ContentScroll);page = target; detailKey = null;detailSeason=null;discoverTitle=null;
        SearchBox.Text = "";
        searchDebounce?.Stop();
        Render();
        ContentScroll.ScrollToTop();
    }
    void Render()
    {
        var context=page+"|"+detailKey+"|"+SearchBox.Text;
        var offset=context==renderedContext?ContentScroll.VerticalOffset:0;renderedContext=context;
        var focused=Keyboard.FocusedElement as FrameworkElement;var focusName=focused!=null?System.Windows.Automation.AutomationProperties.GetName(focused):"";
        try{
        downloadPageUpdate=null;SmoothScrolling.Stop(ContentScroll);ContentPanel.Children.Clear();
        SearchHost.Visibility=page=="Downloads"?Visibility.Collapsed:Visibility.Visible;
        SearchPlaceholder.Text=page=="Discover"?"Search new movies & shows":"Search your library";
        SearchBox.ToolTip=page=="Discover"?"Find new movies and shows online":"Search your library";
        PageEyebrow.Text=page=="Discover"?"FIND YOUR NEXT WATCH":page=="Downloads"?"SAVED TO YOUR PC":"LOCAL LIBRARY";
        DiscoverNav.Background=Brush(page=="Discover"?"#2C2720":"Transparent");
        DownloadsNav.Background=Brush(page=="Downloads"?"#2C2720":"Transparent");
        HomeNav.Background = Brush(page == "Home" ? "#2C2720" : "Transparent");
        MovieNav.Background = Brush(page == "Movies" ? "#2C2720" : "Transparent");
        ShowNav.Background = Brush(page == "Shows" ? "#2C2720" : "Transparent");
        HistoryNav.Background = Brush(page == "History" ? "#2C2720" : "Transparent");
        WatchlistNav.Background = Brush(page == "My List" ? "#2C2720" : "Transparent");
        if(page=="Downloads"){RenderDownloads();return;}
        if(page=="Discover"){RenderDiscover();return;}
        if (page == "Settings") { RenderSettings(); return; }
        if (detailKey != null)
        {
            var group = store.EpisodesFor(detailKey);
            if (group.Count > 0) { RenderDetail(group); return; }
            detailKey = null;
        }
        if(page=="History") { RenderPremiumHistory();return; }
        PageTitle.Text = page == "Home" ? "Your evening, queued." : page;
        var query = SearchBox.Text.Trim();
        var groups = store.Archive.Where(x => (page != "Movies" || x.IsMovie) && (page != "Shows" || !x.IsMovie) && (page!="My List" || collectionFilter=="My List"&&store.State.Watchlist.Contains(x.GroupKey)||collectionFilter!="My List"&&store.State.Collections.GetValueOrDefault(collectionFilter)?.Contains(x.GroupKey)==true))
            .GroupBy(x => x.GroupKey).Select(x => store.EpisodesFor(x.Key))
            .Where(g => query.Length == 0 || g.Any(x => x.Title.Contains(query,StringComparison.OrdinalIgnoreCase) || x.EpisodeTitle.Contains(query,StringComparison.OrdinalIgnoreCase) || x.Genres.Contains(query,StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if(page is "Movies" or "Shows")
        {
            var genres=groups.SelectMany(g=>g[0].Genres.Split('·',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)).Distinct().Order().ToArray();
            if(genreFilter!="All genres"&&!genres.Contains(genreFilter)) genreFilter="All genres";
            if(genres.Length>1) ContentPanel.Children.Add(Chips(new[]{"All genres"}.Concat(genres).ToArray(),genreFilter,x=>{genreFilter=x;Render();}));
            if(genreFilter!="All genres") groups=groups.Where(g=>g[0].Genres.Split('·',StringSplitOptions.TrimEntries).Contains(genreFilter)).ToList();
        }
        if(page=="My List") RenderCollectionsNavigation();
        if(page!="Home") ContentPanel.Children.Add(Chips(["A–Z","Recently watched","Year"],librarySort,x=>{librarySort=x;Render();}));
        groups=(librarySort=="Recently watched"?groups.OrderByDescending(g=>g.Max(i=>store.GetProgress(i)?.UpdatedAt??DateTimeOffset.MinValue)):librarySort=="Year"?groups.OrderByDescending(g=>g[0].Year):groups.OrderBy(g=>g[0].Title)).ToList();
        if (query.Length == 0 && page == "Home" && groups.Count > 0)
        {
            var continuations = groups.Select(g => (Group:g, Item:store.ContinueFor(g)))
                .Where(x => x.Item != null && CanPlay(x.Item) && store.TitleStatus(x.Group)!="Dropped" && !store.State.HiddenContinue.Contains(x.Group[0].GroupKey)).OrderByDescending(x => x.Group.Max(i => store.GetProgress(i)?.UpdatedAt ?? DateTimeOffset.MinValue)).ToList();
            var hero = continuations.Count > 0 ? continuations[0].Item! : ChoiceFor(groups.FirstOrDefault(g => g.Any(CanPlay)) ?? groups[0]);
            ContentPanel.Children.Add(Hero(hero, groups.First(g=>g[0].GroupKey==hero.GroupKey), continuations.Count>0));
            if (continuations.Count > 0)
            {
                var resume=continuations.Select(c=>c.Item!).Where(i=>store.GetProgress(i) is {Completed:false,Position:>0}).ToList();
                var next=continuations.Select(c=>c.Item!).Except(resume).ToList();
                if(resume.Count>0)ContentPanel.Children.Add(ContinueShelf(resume,"Resume","Your exact saved positions."));
                if(next.Count>0)ContentPanel.Children.Add(ContinueShelf(next,"Next episode","Continue after the episodes you finished."));
            }
            else ContentPanel.Children.Add(Heading("Continue Watching","Start a movie or episode. It will appear here when you return."));
        }
        if(page=="Home")
        {
            RenderHomeExtras(groups);
            var watched=groups.Where(g=>g.Any(store.EverWatched)).OrderByDescending(g=>g.Max(HistoryDate)).Take(12).ToList();
            if(watched.Count>0) Catalog(watched,"Recently watched · History");
            var saved=groups.Where(g=>store.State.Watchlist.Contains(g[0].GroupKey)).ToList();
            if(saved.Count>0) Catalog(saved,"My List");
        }
        if (groups.Count == 0)
        {
            if(page=="My List") {ContentPanel.Children.Add(Heading("Your next watch","Use + My List on any title to keep it here."));return;}
            ContentPanel.Children.Add(Heading(query.Length>0?"No matches":"Your cinema starts here",query.Length>0?"Try another title.":"Add your movie and show folders in Settings."));
            if(query.Length==0) ContentPanel.Children.Add(ActionButton("Add folders",()=>Navigate("Settings"),true));
            return;
        }
        if (page != "Movies") Catalog(groups.Where(g=>!g[0].IsMovie).ToList(),"Shows");
        if (page != "Shows") Catalog(groups.Where(g=>g[0].IsMovie).ToList(),"Movies");
        }finally{ContentScroll.ScrollToVerticalOffset(offset);if(focusName.Length>0)ViewElements(ContentPanel).FirstOrDefault(e=>System.Windows.Automation.AutomationProperties.GetName(e)==focusName)?.Focus();}
    }
    string detailTab="Episodes";
    void OpenDetails(List<VideoItem> group) { if(group.Count==0) return;searchDebounce?.Stop();detailTab=group[0].IsMovie?"About":"Episodes";detailKey=group[0].GroupKey;detailSeason=null;episodeFilter="All episodes";Render();ContentScroll.ScrollToTop(); }
    async Task RefreshMetadata(bool force=false)
    {
        if(closing) return;
        if(!store.State.OnlineMetadata) {if(force) Status.Text="Enable online artwork and information in Settings first.";return;}
        if(metadataRefreshing) {if(force){metadataRefreshRequested=true;Status.Text="Refresh queued · waiting for the current information check…";}return;}
        metadataRefreshing=true;
        int updated=0,unmatched=0,failed=0,checkedTitles=0;
        try
        {
            var groups=store.Archive.GroupBy(x=>x.GroupKey).Select(g=>g.ToList()).ToList();
            using var client=new MetadataClient(store.DataDirectory,refreshArtwork:force);
            foreach(var group in groups)
            {
                if(closing || !store.State.OnlineMetadata) break;
                if(!force && (group[0].IsMovie||store.State.EpisodeSchedules.ContainsKey(group[0].GroupKey)) && group.All(i=>i.ReviewTitle.Length>0 && store.State.Catalog[i.Id].MetadataCheckedAt>DateTimeOffset.UtcNow.AddDays(-1))) continue;
                try
                {
                    if(force) Status.Text=$"Refreshing {++checkedTitles}/{groups.Count}: {LibraryStore.CleanShowTitle(group[0].Title)}…";
                    var details=await client.Fetch(group[0],metadataCancellation.Token);
                    if(closing) break;
                    if(details!=null)foreach(var episode in details.Schedule.Where(e=>group.Any(i=>i.Available&&i.Season==e.Season&&i.Episode==e.Episode)&&e.Image.Length>0))
                    {try {episode.Image=await client.EpisodeArt(episode.Image,metadataCancellation.Token);}catch(Exception e){store.Log(e);}}
                    store.ApplyDetails(group[0].GroupKey,details,force);
                    if(details!=null) {updated++;if(force){images.Clear();imageBytes=0;}if(current==null) Render();}else unmatched++;
                }
                catch(OperationCanceledException) when(metadataCancellation.IsCancellationRequested) {break;}
                catch(Exception e) {store.Log(e);failed++;}
                await Task.Delay(600,metadataCancellation.Token);
            }
            if(!closing && force) Status.Text=$"Refresh finished · {updated} titles updated"+(unmatched>0?$" · {unmatched} unmatched":"")+(failed>0?$" · {failed} could not connect":"");
            if(!closing && failed==0&&!validationMode) _=RefreshEpisodeLinks();
        }
        catch(OperationCanceledException) { }
        finally {metadataRefreshing=false;if(metadataRefreshRequested&&!closing){metadataRefreshRequested=false;_=RefreshMetadata(true);}}
    }
    internal static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    internal static TextBlock Text(string value,double size=14,string color="#F4F1EB",FontWeight? weight=null) => new() { Text=value,FontSize=size,Foreground=Brush(color),FontWeight=weight??FontWeights.Normal,TextWrapping=TextWrapping.Wrap };
    internal static TextBlock Body(string value,double size=14) { var t=Text(value,size,"#AAA6A0"); t.Margin=new(0,4,0,12); return t; }
    static StackPanel Heading(string title,string subtitle="")
    {
        var p = new StackPanel { Margin=new(0,24,0,15) };
        p.Children.Add(Text(title,21,weight:FontWeights.SemiBold));
        if(subtitle.Length>0) { var t=Body(subtitle,12);t.Margin=new(0,4,0,0);p.Children.Add(t); }
        return p;
    }
    Button ActionButton(string label,Action action,bool primary=false)
    {
        var b=new Button { Content=label,HorizontalAlignment=HorizontalAlignment.Left };
        if(primary) b.Style=(Style)FindResource("Primary");
        b.Click+=(_,_)=>action();
        return b;
    }
    BitmapImage? ImageSource(string path,int width=1200)
    {
        if(!File.Exists(path)) return null;
        var cacheKey=path+"|"+width;
        if(images.TryGetValue(cacheKey,out var cached)) return cached;
        try {
            var image=new BitmapImage();image.BeginInit();image.UriSource=new Uri(path);image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=width;image.EndInit();image.Freeze();
            var bytes=(long)image.PixelWidth*image.PixelHeight*4;
            while(images.Count>0&&(images.Count>=100||imageBytes+bytes>64L*1024*1024)){var first=images.First();imageBytes-= (long)first.Value.PixelWidth*first.Value.PixelHeight*4;images.Remove(first.Key);}
            images[cacheKey]=image;imageBytes+=bytes;return image;
        }
        catch { return null; }
    }
    FrameworkElement Art(string path,string title,double height,bool portrait=false)
    {
        var source=ImageSource(path,portrait?384:640);
        if(source!=null) return new Image { Source=source,Stretch=Stretch.UniformToFill,Height=height,ClipToBounds=true };
        var grid=new Grid { Height=height,Background=new LinearGradientBrush(Brush("#36312B").Color,Brush("#131315").Color,35) };
        var text=Text(title,portrait?25:21,weight:FontWeights.SemiBold);text.Margin=new(20);text.VerticalAlignment=VerticalAlignment.Center;
        grid.Children.Add(text);
        return grid;
    }
    bool closeReady;
    async void Window_Closing(object? sender,System.ComponentModel.CancelEventArgs e)
    {
        if(closeReady)return;
        e.Cancel=true;if(closing)return;
        closing=true;
        StopWatchingFolders();
        metadataCancellation.Cancel();CancelDiscover();SmoothScrolling.Stop(ContentScroll);downloadPageUpdate=null;
        HidePreview();
        if(previews!=null)await previews.DisposeAsync();
        SavePlayback();
        watchSync?.Dispose();
        downloads?.Dispose();
        if(mobileSync!=null) _=mobileSync.DisposeAsync();
        tick?.Stop(); nextTimer?.Stop(); hideTimer?.Stop();sleepTimer?.Stop();
        clickTimer?.Stop();hintTimer?.Stop();searchDebounce?.Stop();
        if(player!=null)
        {
            DetachEvents();
            Video.MediaPlayer=null;
            var native=player;player=null;await nativeTransition.WaitAsync();
            try{await Task.Run(()=>{native.Stop();native.Dispose();media?.Dispose();vlc?.Dispose();});}finally{nativeTransition.Release();}
        }
        store.State.Volume=(int)VolumeBar.Value;
        try{await Task.Run(store.Save);await store.FlushPendingAsync();}catch(Exception error){try{store.Log(error);}catch{}}
        Video.Dispose();
        closeReady=true;Close();
    }
}

