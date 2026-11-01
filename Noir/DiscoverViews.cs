using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Noir;
public partial class MainWindow
{
    Grid? discoverPanel;
    CancellationTokenSource? discoverPanelCancel;
    FrameworkElement? discoverPanelFocus;
    FrameworkElement? CastStripFor(DiscoverTitle t)=>System.Text.RegularExpressions.Regex.IsMatch(t.Id,@"^tt\d+$")?DetailPeople(new VideoItem{ImdbId=t.Id,IsMovie=t.IsMovie,Title=t.Name}):null;
    void CloseDiscoverPanel()
    {
        discoverPanelCancel?.Cancel();discoverPanelCancel?.Dispose();discoverPanelCancel=null;if(discoverPanel!=null)((Grid)LibraryRoot.Parent).Children.Remove(discoverPanel);discoverPanel=null;discoverPanelFocus?.Focus();discoverPanelFocus=null;
    }
    async Task OpenDiscover(DiscoverTitle title)
    {
        CloseHoverCard();CloseDiscoverPanel();var cancellation=new CancellationTokenSource();discoverPanelCancel=cancellation;var token=cancellation.Token;discoverPanelFocus=System.Windows.Input.Keyboard.FocusedElement as FrameworkElement;
        var overlay=new Grid{Background=Brush("#A6000000"),FlowDirection=FlowDirection.LeftToRight};discoverPanel=overlay;System.Windows.Controls.Panel.SetZIndex(overlay,90);var content=new StackPanel();var panel=new Border{Width=Math.Min(420,Math.Max(300,ActualWidth-48)),HorizontalAlignment=HorizontalAlignment.Right,Background=Brush("#141416"),BorderBrush=Brush("#32FFFFFF"),BorderThickness=new(1,0,0,0),Child=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},FlowDirection=Loc.Arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight};overlay.Children.Add(panel);((Grid)LibraryRoot.Parent).Children.Add(overlay);
        overlay.MouseDown+=(_,e)=>{if(ReferenceEquals(e.OriginalSource,overlay))CloseDiscoverPanel();};overlay.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){CloseDiscoverPanel();e.Handled=true;}};System.Windows.Input.KeyboardNavigation.SetTabNavigation(overlay,System.Windows.Input.KeyboardNavigationMode.Cycle);
        Button? close=null;string cachedRuntime="";var activeTitle=title;TextBlock? nameText=null,metaText=null,synopsisText=null;Grid? imageArt=null;
        string FactsFor(DiscoverTitle t){var existing=store.Archive.FirstOrDefault(i=>i.ImdbId==t.Id&&i.IsMovie==t.IsMovie);var runtime=existing?.Duration>0?Loc.Runtime(existing.Duration):cachedRuntime.Length>0?cachedRuntime:titleExtras?.GetValueOrDefault(t.Id)?.Runtime??"";return string.Join(" · ",new[]{t.Years,runtime,t.Rating.Length>0?"★ "+t.Rating+" IMDb":""}.Concat(t.Genres.Take(3).Select(T)).Where(x=>x.Length>0));}
        void Paint(DiscoverTitle t,bool loaded)
        {
            activeTitle=t;
            if(metaText!=null){nameText!.Text=t.Name;metaText.Text=FactsFor(t);synopsisText!.Text=t.Summary.Length>0?t.Summary:T("No synopsis available.","لا يتوفر ملخص.");imageArt!.Children.RemoveAt(0);imageArt.Children.Insert(0,Art(File.Exists(t.Background)?t.Background:t.Poster,t.Name,236));return;}
            content.Children.Clear();var image=new Grid{Height=236,ClipToBounds=true};imageArt=image;image.Children.Add(Art(File.Exists(t.Background)?t.Background:t.Poster,t.Name,236));var shade=new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Colors.Transparent,Brush("#141416").Color,90);image.Children.Add(new Border{Background=shade,VerticalAlignment=VerticalAlignment.Bottom,Height=100,IsHitTestVisible=false});close=ActionButton("✕",CloseDiscoverPanel);close.HorizontalAlignment=HorizontalAlignment.Right;close.VerticalAlignment=VerticalAlignment.Top;close.Margin=new(14);close.Width=38;close.Height=38;close.Padding=new(0);close.ToolTip=T("Close");System.Windows.Automation.AutomationProperties.SetName(close,T("Close"));image.Children.Add(close);content.Children.Add(image);
            var info=new StackPanel{Margin=new(24,0,24,24)};content.Children.Add(info);nameText=Text(t.Name,30,weight:FontWeights.Bold);info.Children.Add(nameText);var group=TrackedGroup(t);var existing=group?.FirstOrDefault();var meta=Text(FactsFor(t),12,"#E4C084");metaText=meta;meta.Margin=new(0,10,0,16);info.Children.Add(meta);var synopsis=Text(t.Summary.Length>0?t.Summary:loaded?T("No synopsis available.","لا يتوفر ملخص."):T("Loading details…","جارٍ تحميل التفاصيل…"),14,"#C9C4BD");synopsisText=synopsis;synopsis.LineHeight=22;info.Children.Add(synopsis);if(CastStripFor(t) is {} cast)info.Children.Add(cast);
            var actions=new WrapPanel{Margin=new(0,20,0,0)};info.Children.Add(actions);void Act(string text,Action action,bool primary=false){var b=ActionButton(text,()=>{CloseDiscoverPanel();action();},primary);b.Margin=new(0,0,8,8);b.Padding=new(16,10,16,10);actions.Children.Add(b);}
            if(!TrackerOnly){var local=group!=null&&group.Any(CanPlay)?ChoiceFor(group):null;if(local!=null)Act(T(store.GetProgress(local) is {Completed:false,Position:>0}?"Resume":"Play"),()=>_=PlayVideo(local),true);Act(T("Stream"),()=>{var episode=activeTitle.IsMovie?null:NextToStream(activeTitle,existing);DiscoverStream(activeTitle,episode?.Season??0,episode?.Episode??0);},local==null);Act(T("Download"),()=>{var episode=activeTitle.IsMovie?null:NextToStream(activeTitle,existing);DiscoverDownload(activeTitle,episode?.Season??0,episode?.Episode??0,false);});}var listed=existing!=null&&store.State.Watchlist.Contains(existing.GroupKey);Act(listed?T("In My List · click to remove","في قائمتي · انقر للإزالة"):T("Add to My List","إضافة إلى قائمتي"),()=>ToggleDiscoveredWatchlist(activeTitle));Act(T("Log","تسجيل المشاهدة"),()=>OpenLog(activeTitle));
            var full=ActionButton(T("Full page ›","الصفحة الكاملة ›"),()=>{CloseDiscoverPanel();Navigate("Discover");_=OpenDiscoverFull(activeTitle);});full.Foreground=Brush("#E4C084");full.Background=Brush("Transparent");full.Margin=new(0,10,0,0);System.Windows.Automation.AutomationProperties.SetName(full,"Full page");info.Children.Add(full);
        }
        Paint(title,false);close?.Focus();if(Motion.Enabled){var slide=new TranslateTransform(64,0);panel.RenderTransform=slide;slide.BeginAnimation(TranslateTransform.XProperty,new System.Windows.Media.Animation.DoubleAnimation(64,0,TimeSpan.FromMilliseconds(240)){EasingFunction=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut}});Motion.FadeIn(panel);}
        try{using var client=DiscoverClient();var details=await client.Details(title,token);cachedRuntime=await Task.Run(()=>{try{var key=$"meta/{details.Type}/{details.Id}.json";var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(store.DataDirectory,"discover",hash+".json")));return json.RootElement.GetProperty("meta").TryGetProperty("runtime",out var runtime)?runtime.ToString():"";}catch{return "";}},token);if(!closing&&!token.IsCancellationRequested&&discoverPanel==overlay){Paint(details,true);}}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception error){store.Log(error);if(discoverPanel==overlay&&!token.IsCancellationRequested){Paint(title,true);}}
    }
    Func<DiscoveryClient>? discoveryFactory {get;set;}
    DiscoveryClient DiscoverClient()=>discoveryFactory?.Invoke()??new DiscoveryClient(store.DataDirectory);
    string discoverFilter="All";
    DiscoverTitle? discoverTitle;
    DiscoverResults? discoverResults;
    string discoverQuery="",discoverResultsFilter="",discoverMessage="";
    CancellationTokenSource? discoverCancellation;
    int discoverRevision,discoverSeason;
    void CancelDiscover(){discoverRevision++;discoverCancellation?.Cancel();discoverCancellation?.Dispose();discoverCancellation=null;}
    CancellationToken BeginDiscover(){CancelDiscover();discoverCancellation=new();return discoverCancellation.Token;}
    async Task SearchDiscover()
    {
        if(page!="Discover"||closing)return;
        var token=BeginDiscover();var revision=discoverRevision;var query=SearchBox.Text.Trim();var filter=discoverFilter;discoverTitle=null;
        // Keep the search field stable while results load.
        ContentPanel.Children.Clear();PageTitle.Text="Discover";RenderDiscoverLoading(query);
        try{using var client=DiscoverClient();var results=await client.Search(query,filter,token);if(closing||page!="Discover"||revision!=discoverRevision)return;discoverResults=results;discoverQuery=query;discoverResultsFilter=filter;discoverMessage=results.Message;Render();ContentScroll.ScrollToTop();}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error){if(revision!=discoverRevision||closing)return;store.Log(error);ContentPanel.Children.Add(Body("Couldn’t reach Discover. Your saved library still works."));ContentPanel.Children.Add(ActionButton("Retry",()=>_=SearchDiscover()));}
    }
    async Task OpenDiscoverFull(DiscoverTitle title)
    {
        var token=BeginDiscover();var revision=discoverRevision;cinematic=false;ApplyPageLayout();PageTitle.Text=title.Name;ContentPanel.Children.Clear();ContentPanel.Children.Add(Heading("Opening "+title.Name+"…"));ContentPanel.Children.Add(ActionButton("Back to results",()=>{CancelDiscover();discoverTitle=null;Render();}));
        try{using var client=DiscoverClient();var details=await client.Details(title,token);if(closing||page!="Discover"||revision!=discoverRevision)return;discoverTitle=details;discoverSeason=details.Episodes.FirstOrDefault(e=>e.AirsAt==null||e.AirsAt<=DateTimeOffset.UtcNow)?.Season??1;Render();ContentScroll.ScrollToTop();}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error){if(revision!=discoverRevision||closing)return;store.Log(error);ContentPanel.Children.Add(Body("Details couldn’t be loaded. Please try again."));ContentPanel.Children.Add(ActionButton("Retry",()=>_=OpenDiscoverFull(title)));}
    }
}
