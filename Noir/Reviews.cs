using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Noir;

public sealed class ReviewBrowser : Window
{
    readonly WebView2 web=new(){DefaultBackgroundColor=System.Drawing.Color.FromArgb(255,11,11,13)};
    readonly TextBlock status=new(){Foreground=MainWindow.Brush("#AAA6A0"),Text="Loading reviews…",Margin=new(14,8,14,8),TextWrapping=TextWrapping.Wrap};
    readonly TextBox address=new(){IsReadOnly=true,Margin=new(12,0,12,0)};
    readonly string directory;
    readonly string profileDirectory;
    readonly VideoItem item;
    readonly LibraryStore? library;
    CancellationTokenSource? navigation;
    readonly Dictionary<string,string> sources;
    readonly Dictionary<string,Button> tabsBySource=new();
    string targetUrl;
    string selectedSource="";
    bool closed;
    Task? initialization;
    readonly Button paste=new(){Content="Use Reddit link",Padding=new(13,9,13,9)};
    readonly Dictionary<string,string> remembered=new();
    int navigationVersion;
    string LinkKey=>item.Id.Length>0?item.Id:item.Label;
    public ReviewBrowser(VideoItem video,string dataDirectory,bool activate=true,string? profile=null,LibraryStore? store=null,string? initialSource=null)
    {
        item=video;library=store;
        directory=dataDirectory;
        try {remembered=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(directory,"community-embeds.json")))??new();} catch { }
        profileDirectory=profile??Path.Combine(directory,"review-browser");
        Background=MainWindow.Brush("#0D0D0F");Foreground=MainWindow.Brush("#F4F1EB");
        Title="Reviews · "+video.Label;Width=1120;Height=820;MinWidth=780;MinHeight=560;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowActivated=activate;
        sources=ReviewLinks.Sources(video,store?.State);
        initialSource??=store?.State.ReviewTabs.GetValueOrDefault(video.GroupKey);
        targetUrl=initialSource!=null&&sources.TryGetValue(initialSource,out var initial)?initial:sources.First().Value;
        var root=new DockPanel();
        var header=new StackPanel{Margin=new(18,16,18,10)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(MainWindow.Text(video.IsMovie?video.Title:video.Title+" · "+video.EpisodeLabel,23,weight:FontWeights.SemiBold));
        header.Children.Add(MainWindow.Body("Community ratings and conversations. Check episode numbers and spoiler labels before reading.",12));
        var tabs=new WrapPanel();header.Children.Add(tabs);
        foreach(var source in sources)
        {
            var tab=Button(source.Key,async()=>await OpenSource(source.Key));tab.Margin=new(0,0,8,8);tabs.Children.Add(tab);tabsBySource[source.Key]=tab;
        }
        SelectSource(initialSource!=null&&sources.ContainsKey(initialSource)?initialSource:sources.First().Key);
        var toolbar=new DockPanel{Margin=new(18,0,18,10)};DockPanel.SetDock(toolbar,Dock.Top);root.Children.Add(toolbar);
        toolbar.Children.Add(Button("←",()=>{if(web.CoreWebView2?.CanGoBack==true) web.GoBack();}));
        toolbar.Children.Add(Button("↻",()=>{web.CoreWebView2?.Reload();}));
        var external=Button("Open in browser",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(targetUrl){UseShellExecute=true}));DockPanel.SetDock(external,Dock.Right);toolbar.Children.Add(external);toolbar.Children.Add(address);
        var modes=new WrapPanel{Margin=new(18,0,18,10)};DockPanel.SetDock(modes,Dock.Top);root.Children.Add(modes);
        modes.Children.Add(paste);paste.Click+=(_,_)=>ChooseRedditLink();UpdateModes();
        DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);root.Children.Add(web);Content=root;
        Loaded+=async(_,_)=>await OpenSource(selectedSource);
        Closed+=(_,_)=>{closed=true;navigation?.Cancel();web.Dispose();};
    }
    static Button Button(string text,Action action) {var b=new Button{Content=text,Padding=new(13,9,13,9)};b.Click+=(_,_)=>action();return b;}
    void SelectSource(string name)
    {
        selectedSource=name;
        if(library!=null){library.State.ReviewTabs[item.GroupKey]=name;library.Save();}
        foreach(var tab in tabsBySource) {tab.Value.Background=MainWindow.Brush(tab.Key==name?"#E6C488":"#222224");tab.Value.Foreground=MainWindow.Brush(tab.Key==name?"#111111":"#F4F1EB");}
    }
    async Task OpenSource(string source)
    {
        navigation?.Cancel();navigation=new();var token=navigation.Token;
        SelectSource(source);
        UpdateModes();
        if(source=="Reddit"&&remembered.TryGetValue(LinkKey,out var saved)&&ReviewLinks.TopDiscussion(saved) is {} thread) {await Navigate(thread);return;}
        if(source!="Serializd") {await Navigate(sources[source]);return;}
        try
        {
            initialization??=InitializeBrowser();await initialization;token.ThrowIfCancellationRequested();
            status.Text="Finding this exact episode on Serializd…";
            var url=await CommunityPages.ResolveSerializd(web.CoreWebView2,item,library,token);
            if(!closed&&!token.IsCancellationRequested) {sources[source]=url;targetUrl=url;address.Text=url;status.Text=SourceHint();}
        }
        catch(OperationCanceledException) { }
        catch(Exception e) {if(!closed&&!token.IsCancellationRequested) status.Text=e.Message+" You can choose the episode here manually.";}
    }
    async Task Navigate(string url)
    {
        if(closed) return;
        var version=++navigationVersion;
        UpdateModes();targetUrl=url;address.Text=url;status.Text="Loading reviews…";
        try
        {
            initialization ??= InitializeBrowser();
            await initialization;
            if(!closed&&version==navigationVersion) web.CoreWebView2.Navigate(url);
        }
        catch(Exception e) {if(!closed) status.Text="The review browser couldn't start: "+e.Message+". You can use Open in browser.";}
    }
    async Task InitializeBrowser()
    {
                var environment=await CoreWebView2Environment.CreateAsync(userDataFolder:profileDirectory);
                if(closed) return;
                await web.EnsureCoreWebView2Async(environment);
                if(closed) return;
                web.CoreWebView2.Profile.PreferredColorScheme=CoreWebView2PreferredColorScheme.Dark;
                web.CoreWebView2.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
                web.CoreWebView2.NewWindowRequested+=(_,e)=>{e.Handled=true;if(SafeUrl(e.Uri)) web.CoreWebView2.Navigate(e.Uri);};
                web.CoreWebView2.NavigationStarting+=(_,e)=>
                {
                    if(!SafeUrl(e.Uri)){e.Cancel=true;return;}
                    targetUrl=e.Uri;address.Text=e.Uri;status.Text="Loading reviews…";UpdateModes();
                    if(selectedSource=="Reddit"&&ReviewLinks.TopDiscussion(e.Uri) is {} thread){
                        RememberThread(thread);
                        if(e.Uri!=thread){e.Cancel=true;web.CoreWebView2.Navigate(thread);}
                    }
                };
                web.CoreWebView2.NavigationCompleted+=async(_,e)=>
                {
                    if(closed) return;
                    status.Text=e.IsSuccess?SourceHint():"This page couldn't load. Try Reload, another review tab, or Open in browser.";
                    if(e.IsSuccess&&library!=null&&selectedSource=="Serializd")
                    {
                        try
                        {
                            if(await CommunityPages.VerifySerializdEpisode(web.CoreWebView2,item))
                            {
                                var url=web.CoreWebView2.Source;
                                library.State.SerializdSeasons[CommunityQueue.SeasonKey(item)]=url[..url.LastIndexOf("/episode/",StringComparison.Ordinal)];library.Save();sources["Serializd"]=url;
                            }
                        }
                        catch { } // A site changing pages never interrupts playback or clears a known match.
                    }
                };
                web.CoreWebView2.Settings.AreDefaultContextMenusEnabled=true;
    }
    static bool SafeUrl(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https";
    void UpdateModes()=>paste.Visibility=selectedSource=="Reddit"?Visibility.Visible:Visibility.Collapsed;
    void RememberThread(string thread)
    {
        if(remembered.GetValueOrDefault(LinkKey)==thread)return;
        remembered[LinkKey]=thread;
        try {var path=Path.Combine(directory,"community-embeds.json");Dictionary<string,string> latest=new();try{latest=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))??new();}catch{}latest[LinkKey]=thread;File.WriteAllText(path+".tmp",JsonSerializer.Serialize(latest));File.Move(path+".tmp",path,true);}catch(Exception e){library?.Log(e);}
    }
    void ChooseRedditLink()
    {
        var dialog=new Window{Title="Choose a Reddit discussion",Owner=this,Width=580,Height=230,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background,Foreground=Foreground};
        var content=new StackPanel{Margin=new(20)};content.Children.Add(MainWindow.Body("Paste a Reddit post or comment link. Noir remembers it for this episode or movie."));
        var link=new TextBox{Text=ReviewLinks.TopDiscussion(targetUrl)??"",Margin=new(0,12,0,12)};content.Children.Add(link);
        var error=MainWindow.Body("",12);content.Children.Add(error);
        content.Children.Add(Button("Show discussion",async()=>{if(ReviewLinks.TopDiscussion(link.Text.Trim()) is not {} thread){error.Text="Use a full https://www.reddit.com/r/…/comments/… link.";return;}RememberThread(thread);dialog.Close();await Navigate(thread);}));dialog.Content=content;dialog.ShowDialog();
    }
    string SourceHint()=>selectedSource switch
    {
        "Serializd"=>"Episode discussions · Your sign-in is kept when Noir closes. Verify season and episode numbers before reading.",
        "Trakt"=>"Episode and movie discussions · After signing in, click Trakt again to return to this episode. Your sign-in is saved across restarts.",
        "Reddit"=>"Discussions open with Top sorting. Noir remembers your chosen episode thread. Check spoiler tags before reading.",
        _=>"Online reviews · Use the site's spoiler controls before reading."
    };
#if NOIR_VALIDATE
    public bool BrowserReady=>web.CoreWebView2!=null;
    public string CurrentAddress=>address.Text;
    public Task<string> InspectPage()=>web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({title:document.title,text:document.body.innerText.slice(0,1500),url:location.href})");
    public Task NavigateForTest(string source) {SelectSource(source);return Navigate(sources[source]);}
    public Task NavigateUrlForTest(string url)=>Navigate(url);
    public Task<string> InspectCommunityForTest()=>web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({url:location.href,title:document.title,text:document.body.innerText.slice(0,2200),links:[...document.querySelectorAll('a[href]')].map(a=>({text:a.innerText.slice(0,90),url:a.href})).filter(a=>/serializd.com\\/show|app.trakt.tv\\/(shows|movies)/.test(a.url)).slice(0,60),buttons:[...document.querySelectorAll('button,[role=button]')].map(b=>({text:b.innerText.slice(0,90),label:b.getAttribute('aria-label'),title:b.getAttribute('title'),pressed:b.getAttribute('aria-pressed'),type:b.getAttribute('type')})).slice(0,50)})");
    public bool TabsFitForTest=>tabsBySource.Values.All(b=>b.IsVisible && b.TransformToAncestor(this).Transform(new Point(b.ActualWidth,0)).X<=ActualWidth);
    public void CaptureChromeForTest(string path)
    {
        UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(this);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file=File.Create(path);encoder.Save(file);
    }
    public async Task CaptureForTest(string path)
    {
        using var file=File.Create(path);await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
    }
#endif
}

public partial class MainWindow
{
    Task? episodeLookup;
    async Task RefreshEpisodeLinks()
    {
        if(!store.State.OnlineMetadata || closing) return;
        if(episodeLookup!=null) {await episodeLookup;return;}
        episodeLookup=ResolveEpisodeLinks();
        try {await episodeLookup;}
        finally {episodeLookup=null;}
    }
    async Task ResolveEpisodeLinks()
    {
        try
        {
            var found=await EpisodeReviewIndex.Resolve(store.Archive.ToList(),store.DataDirectory,metadataCancellation.Token);
            if(closing) return;
            foreach(var pair in found) if(store.State.Catalog.TryGetValue(pair.Key,out var entry)) entry.Item.EpisodeImdbId=pair.Value;
            if(found.Count>0) store.Save();
        }
        catch(OperationCanceledException) { }
        catch(Exception e) {store.Log(e);} // Keep live series pages usable when the optional index is offline.
    }
    void OpenReviews(VideoItem item,string? source=null)
    {
        var browser=new ReviewBrowser(item,store.DataDirectory,store:store,initialSource:source){Owner=this};
        browser.Closed+=(_,_)=>watchSync?.Retry();browser.Show();
    }
    void OpenEpisodeInfo(VideoItem item)
    {
        var dialog=new Window{Title=item.Label,Width=760,Height=660,MinWidth=600,MinHeight=480,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#0D0D0F"),Foreground=Brush("#F4F1EB")};
        var panel=new StackPanel{Margin=new(28)};
        panel.Children.Add(Art(File.Exists(item.Backdrop)?item.Backdrop:item.Poster,item.Title,215));
        panel.Children.Add(Heading(item.IsMovie?item.Title:item.EpisodeLabel+" · "+item.EpisodeTitle,item.IsMovie?item.Year.ToString():item.Title));
        var bookmark=store.GetProgress(item);panel.Children.Add(Body(bookmark?.Completed==true?"✓ Watched":bookmark is {Position:>0}?"Saved at "+LibraryStore.Time(bookmark.Position):"Not watched yet"));
        var summary=Body(item.IsMovie?item.Summary:item.EpisodeSummary);
        if(!item.IsMovie && store.State.HideSpoilers && !store.EverWatched(item))
        {
            summary.Visibility=Visibility.Collapsed;
            panel.Children.Add(ActionButton("Show episode description · spoilers",()=>summary.Visibility=Visibility.Visible));
        }
        panel.Children.Add(summary);
        var actions=new WrapPanel();
        var play=ActionButton(bookmark is {Completed:false,Position:>0}?"▶ Resume":"▶ Play",()=>{dialog.Close();_ = PlayVideo(item);},true);
        play.IsEnabled=item.Available&&File.Exists(item.Path);actions.Children.Add(play);
        var reviews=ActionButton("Reviews & comments",()=>{dialog.Close();OpenReviews(item);});reviews.Margin=new(10,0,0,0);actions.Children.Add(reviews);panel.Children.Add(actions);
        panel.Children.Add(ActionButton("Viewing diary & my rating",()=>{dialog.Close();OpenDiary(item);}));
        panel.Children.Add(Body(string.Join(" · ",ReviewLinks.Sources(item).Keys),12));
        dialog.Content=new ScrollViewer{Content=panel};dialog.Show();
    }
}


