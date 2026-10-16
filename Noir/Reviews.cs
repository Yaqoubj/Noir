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
        // Header: the title's artwork behind a dark fade, title, source chips and quiet icon buttons.
        var hero=new Grid{MinHeight=150,ClipToBounds=true};DockPanel.SetDock(hero,Dock.Top);root.Children.Add(hero);
        var art=File.Exists(video.Backdrop)?video.Backdrop:File.Exists(video.EpisodeImage)?video.EpisodeImage:File.Exists(video.Poster)?video.Poster:"";
        if(art.Length>0)try{var image=new System.Windows.Media.Imaging.BitmapImage();image.BeginInit();image.UriSource=new Uri(art);image.DecodePixelWidth=1280;image.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;image.EndInit();image.Freeze();hero.Background=new System.Windows.Media.ImageBrush(image){Stretch=System.Windows.Media.Stretch.UniformToFill,AlignmentY=System.Windows.Media.AlignmentY.Center,Opacity=.55};}catch{}
        var fade=new System.Windows.Media.LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1)};fade.GradientStops.Add(new(System.Windows.Media.Color.FromArgb(150,13,13,15),0));fade.GradientStops.Add(new(System.Windows.Media.Color.FromArgb(255,13,13,15),1));
        hero.Children.Add(new Border{Background=fade});
        var header=new StackPanel{Margin=new(24,20,24,12)};hero.Children.Add(header);
        var titleRow=new DockPanel();header.Children.Add(titleRow);
        var tools=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};DockPanel.SetDock(tools,Dock.Right);titleRow.Children.Add(tools);
        tools.Children.Add(Icon("←","Back",()=>{if(web.CoreWebView2?.CanGoBack==true) web.GoBack();}));
        tools.Children.Add(Icon("↻","Reload",()=>{web.CoreWebView2?.Reload();}));
        styleToggle=Icon("◐","Noir style is on · click to show the site's own look",ToggleStyle);tools.Children.Add(styleToggle);
        tools.Children.Add(Icon("↗","Open in your web browser",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(targetUrl){UseShellExecute=true})));
        var titles=new StackPanel();titleRow.Children.Add(titles);
        titles.Children.Add(MainWindow.Text("REVIEWS & DISCUSSION",10,"#E4C084",FontWeights.SemiBold));
        var name=MainWindow.Text(video.IsMovie?video.Title:video.Title+" · "+video.EpisodeLabel,26,weight:FontWeights.Bold);name.Margin=new(0,4,0,2);titles.Children.Add(name);
        if(!video.IsMovie&&video.EpisodeTitle.Length>0)titles.Children.Add(MainWindow.Text(video.EpisodeTitle,13,"#C7C2BC"));
        var tabs=new WrapPanel{Margin=new(0,16,0,0)};header.Children.Add(tabs);
        foreach(var source in sources)
        {
            var tab=Button(source.Key,async()=>await OpenSource(source.Key));tab.Margin=new(0,0,8,4);tab.Padding=new(16,7,16,7);tabs.Children.Add(tab);tabsBySource[source.Key]=tab;
        }
        paste.Margin=new(8,0,0,4);paste.Padding=new(14,7,14,7);paste.Background=MainWindow.Brush("Transparent");tabs.Children.Add(paste);paste.Click+=(_,_)=>ChooseRedditLink();
        SelectSource(initialSource!=null&&sources.ContainsKey(initialSource)?initialSource:sources.First().Key);UpdateModes();
        address.Visibility=Visibility.Collapsed;
        status.Margin=new(26,6,26,10);status.FontSize=11.5;DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);
        // The page sits in a framed panel; a Noir cover hides half-drawn pages while they load.
        var stage=new Grid{Margin=new(18,4,18,0)};
        stage.Children.Add(new Border{BorderBrush=MainWindow.Brush("#26262A"),BorderThickness=new(1),CornerRadius=new(12),Background=MainWindow.Brush("#0B0B0D"),Padding=new(1),Child=web});
        var spinner=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        spinner.Children.Add(MainWindow.Text("NOIR",22,"#E4C084",FontWeights.Bold));loadingText.Margin=new(0,8,0,0);spinner.Children.Add(loadingText);
        cover.Child=spinner;stage.Children.Add(cover);
        root.Children.Add(stage);Content=root;
        // App.xaml's implicit Window style does not reach subclasses, so fit to the screen here.
        Loaded+=async(_,_)=>{WindowAppearance.Apply(this);WindowAppearance.Fit(this);await OpenSource(selectedSource);};
        Closed+=(_,_)=>{closed=true;navigation?.Cancel();web.Dispose();};
    }
    static Button Button(string text,Action action) {var b=new Button{Content=text,Padding=new(13,9,13,9)};b.Click+=(_,_)=>action();return b;}
    static new Button Icon(string glyph,string tip,Action action)
    {
        var b=new Button{Content=glyph,ToolTip=tip,Width=40,Height=40,Padding=new(0),FontSize=17,Margin=new(6,0,0,0),Background=MainWindow.Brush("#CC1A1A1D")};
        System.Windows.Automation.AutomationProperties.SetName(b,tip);b.Click+=(_,_)=>action();return b;
    }
    readonly Border cover=new(){Background=MainWindow.Brush("#0B0B0D"),CornerRadius=new(12),Margin=new(1)};
    readonly TextBlock loadingText=new(){Foreground=MainWindow.Brush("#AAA6A0"),FontSize=13,HorizontalAlignment=HorizontalAlignment.Center};
    Button? styleToggle;
    bool noirStyle=true;
    System.Windows.Threading.DispatcherTimer? coverTimer;
    // The native page window cannot be drawn over, so it stays hidden behind the Noir cover while loading.
    void ShowCover(string source)
    {
        loadingText.Text=$"Opening {source}…";cover.Visibility=Visibility.Visible;web.Visibility=Visibility.Hidden;
        coverTimer??=new(TimeSpan.FromSeconds(10),System.Windows.Threading.DispatcherPriority.Normal,(_,_)=>HideCover(),Dispatcher);coverTimer.Stop();coverTimer.Start();
    }
    void HideCover(){coverTimer?.Stop();cover.Visibility=Visibility.Collapsed;web.Visibility=Visibility.Visible;}
    void ToggleStyle()
    {
        noirStyle=!noirStyle;if(styleToggle!=null)styleToggle.ToolTip=noirStyle?"Noir style is on · click to show the site's own look":"Showing the site's own look · click for Noir style";
        if(styleToggle!=null)styleToggle.Opacity=noirStyle?1:.55;
        _=web.CoreWebView2?.ExecuteScriptAsync($"window.__noirStyle&&window.__noirStyle({(noirStyle?"true":"false")})");
    }
    // Dark, uncluttered reading styles for the community sites. Purely cosmetic: it hides site
    // chrome (headers, sidebars, app prompts, footers) and never clicks or submits anything.
    // Sign-in screens and spoiler controls keep working; the ◐ button shows the original site.
    const string NoirStyleScript="""
    (() => {
      const host = location.hostname.replace(/^www\./, '');
      const common = `
        html, body { background: #0d0d0f !important; color-scheme: dark; }
        ::selection { background: #e4c08455; }
        ::-webkit-scrollbar { width: 10px; height: 10px; }
        ::-webkit-scrollbar-track { background: #0d0d0f; }
        ::-webkit-scrollbar-thumb { background: #3a3630; border-radius: 6px; border: 2px solid #0d0d0f; }
        ::-webkit-scrollbar-thumb:hover { background: #5e4e36; }
        a { accent-color: #e4c084; }`;
      const sites = {
        'reddit.com': `
          reddit-header-large, reddit-header-small, header.v2, #left-sidebar-container, #right-sidebar-container,
          shreddit-async-loader[bundlename="left_nav"], xpromo-app-selector, shreddit-experience-tree,
          faceplate-tracker[noun="open_app"], [slot="credit-bar"] ~ faceplate-tracker[noun="join"], #onetrust-consent-sdk ~ div[class*="xpromo"],
          shreddit-ad-post, shreddit-comments-page-ad, [bundlename="comment_body_header_ad"] { display: none !important; }
          shreddit-app { --shreddit-header-height: 0px !important; padding-top: 12px !important; }
          .grid-container { display: block !important; }
          #subgrid-container, .subgrid-container { max-width: 820px !important; margin: 0 auto !important; padding: 0 16px !important; }
          .main-container { display: block !important; max-width: none !important; }
          main.main, main#main-content { max-width: none !important; width: 100% !important; }
          .theme-rpl, shreddit-app, html { --color-neutral-background: #0d0d0f !important; --color-neutral-background-weak: #141416 !important;
            --color-neutral-background-container: #141416 !important; --color-neutral-border-weak: #26262a !important;
            --color-neutral-content-strong: #f4f1eb !important; --color-neutral-content: #d9d4cc !important; --color-neutral-content-weak: #9a958e !important;
            --color-global-brand-orangered: #e4c084 !important; --color-primary: #e4c084 !important; --color-primary-plain: #e4c084 !important; --color-a-default: #e4c084 !important; }
          shreddit-post-text-body a, .md a { color: #e4c084 !important; text-decoration-color: #e4c08466 !important; }`,
        'imdb.com': `
          #imdbHeader, footer, .ipc-page-background--baseAlt ~ footer, [data-testid="ad-wrap"], .nas-slot, [class*="InlineAd"], #inline20, #top_ad_wrapper { display: none !important; }
          .ipc-page-background, .ipc-page-background--base, .ipc-page-content-container { background: #0d0d0f !important; }`,
        'letterboxd.com': `
          #header, #footer, .banner, .ad-slot, .sidebar-ad, #sidebar-ad, .pw-tag { display: none !important; }
          body, #content { background: #0d0d0f !important; }`,
        'app.trakt.tv': `footer, [class*="advert"], [class*="Advert"], [class*="vip-upsell"] { display: none !important; }`,
        'trakt.tv': `footer, #top-nav ~ .ad, [class*="advert"] { display: none !important; }`,
        'serializd.com': `div.footer, [class*="AdBanner"], [class*="ad-container"], .pw-tag, [id^="pw-"], [class*="pw-corner"], [id^="google_ads_iframe"] { display: none !important; }
          body, #root, #__next { background: #0d0d0f !important; }`,
        'rottentomatoes.com': `rt-header, #header-main, .header_main, footer, #footer, .ad-unit, [id*="ad-unit"], [class*="AdUnit"], #header_ad, .leaderboard_wrapper, ad-unit, [data-adtype] { display: none !important; }`
      };
      // Light pages (IMDb search, Rotten Tomatoes) are re-coloured dark; photos keep their colours.
      const darken = `
        html.noir-invert { filter: invert(1) hue-rotate(180deg) brightness(.92) contrast(.94) !important; }
        html.noir-invert, html.noir-invert body { background: #f2f2f0 !important; }
        html.noir-invert img, html.noir-invert picture, html.noir-invert video, html.noir-invert iframe, html.noir-invert canvas,
        html.noir-invert svg image, html.noir-invert [style*="background-image"] { filter: invert(1) hue-rotate(180deg) brightness(1.08) !important; }`;
      const site = Object.keys(sites).find(k => host === k || host.endsWith('.' + k));
      const style = document.createElement('style');
      style.id = 'noir-style';
      style.textContent = common + darken + (site ? sites[site] : '');
      // Judge the colour behind the main content rather than <body>, which is often dark under a white column.
      const backdrop = el => { for (; el && el !== document.documentElement; el = el.parentElement) { const c = getComputedStyle(el).backgroundColor.match(/[\d.]+/g); if (c && (c.length < 4 || +c[3] > .5)) return c; } return null; };
      const light = () => { let lit = 0, seen = 0; for (const [x, y] of [[.3, .35], [.5, .5], [.7, .4], [.4, .7], [.6, .8]]) { const c = backdrop(document.elementFromPoint(innerWidth * x, innerHeight * y)); if (!c) continue; seen++; if ((.2126 * c[0] + .7152 * c[1] + .0722 * c[2]) / 255 > .6) lit++; } return seen > 0 && lit * 2 > seen; };
      const judge = () => { if (!document.body) return; const root = document.documentElement; if (!style.disabled && !root.classList.contains('noir-invert') && light()) root.classList.add('noir-invert'); };
      addEventListener('load', () => { judge(); setTimeout(judge, 1200); }); document.addEventListener('DOMContentLoaded', () => setTimeout(judge, 300));
      // A consent or sign-in overlay can hide the page at first; look again after the person dismisses it.
      let checks = 0; addEventListener('pointerup', () => { if (checks++ < 6) setTimeout(judge, 700); }, true);
      // Sites that rebuild <head> (React apps) would drop the sheet, so keep it on <html> and re-add it.
      const attach = () => { if (!style.isConnected && document.documentElement) document.documentElement.appendChild(style); };
      attach(); document.addEventListener('DOMContentLoaded', attach);
      new MutationObserver(attach).observe(document, { childList: true, subtree: false });
      new MutationObserver(attach).observe(document.documentElement, { childList: true });
      window.__noirStyle = on => { style.disabled = !on; if (on) judge(); else document.documentElement.classList.remove('noir-invert'); };
    })();
    """;
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
        UpdateModes();targetUrl=url;address.Text=url;status.Text="Loading reviews…";ShowCover(selectedSource);
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
                await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(NoirStyleScript);
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
                    HideCover();if(!noirStyle)_=web.CoreWebView2.ExecuteScriptAsync("window.__noirStyle&&window.__noirStyle(false)");
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


