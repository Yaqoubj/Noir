using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Noir;
public partial class MainWindow
{
    Grid? discoverPanel;
    CancellationTokenSource? discoverPanelCancel;
    FrameworkElement? CastStripFor(DiscoverTitle t)=>System.Text.RegularExpressions.Regex.IsMatch(t.Id,@"^tt\d+$")?DetailPeople(new VideoItem{ImdbId=t.Id,IsMovie=t.IsMovie,Title=t.Name}):null;
    NoirSheet? discoverSheet;
    void CloseDiscoverPanel()
    {
        var sheet=discoverSheet;if(sheet!=null)sheet.Close(false);
        if(discoverPanel!=null){discoverPanelCancel?.Cancel();discoverPanel=null;discoverSheet=null;}
    }
    async Task OpenDiscover(DiscoverTitle title)
    {
        CloseHoverCard();CloseDiscoverPanel();var cancellation=new CancellationTokenSource();discoverPanelCancel=cancellation;var token=cancellation.Token;
        var sheet=OpenSheet(title.Name,null,SheetKind.Side,440);discoverSheet=sheet;var overlay=sheet.Overlay;discoverPanel=overlay;var content=new StackPanel();sheet.Add(content);
        sheet.Closed+=_=>{cancellation.Cancel();if(discoverPanel==overlay){discoverPanel=null;discoverSheet=null;}if(discoverPanelCancel==cancellation)discoverPanelCancel=null;};
        string cachedRuntime="";var activeTitle=title;TextBlock? metaText=null,synopsisText=null;Grid? imageArt=null;
        string FactsFor(DiscoverTitle t){var existing=store.Archive.FirstOrDefault(i=>i.ImdbId==t.Id&&i.IsMovie==t.IsMovie);var runtime=existing?.Duration>0?Loc.Runtime(existing.Duration):cachedRuntime.Length>0?cachedRuntime:titleExtras?.GetValueOrDefault(t.Id)?.Runtime??"";return string.Join(" · ",new[]{t.Years,runtime,t.Rating.Length>0?"★ "+t.Rating+" IMDb":""}.Concat(t.Genres.Take(3).Select(T)).Where(x=>x.Length>0));}
        void Paint(DiscoverTitle t,bool loaded)
        {
            activeTitle=t;sheet.SetTitle(t.Name);
            if(metaText!=null){metaText.Text=FactsFor(t);synopsisText!.Text=t.Summary.Length>0?t.Summary:T("No synopsis available.","لا يتوفر ملخص.");imageArt!.Children.RemoveAt(0);imageArt.Children.Insert(0,Art(File.Exists(t.Background)?t.Background:t.Poster,t.Name,200));return;}
            content.Children.Clear();var image=new Grid{Height=200,ClipToBounds=true};imageArt=image;image.Children.Add(Art(File.Exists(t.Background)?t.Background:t.Poster,t.Name,200));var shade=new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Colors.Transparent,Brush("#141416").Color,90);image.Children.Add(new Border{Background=shade,VerticalAlignment=VerticalAlignment.Bottom,Height=80,IsHitTestVisible=false});content.Children.Add(new Border{CornerRadius=new(12),ClipToBounds=true,Child=image});
            var info=new StackPanel{Margin=new(0,12,0,0)};content.Children.Add(info);var group=TrackedGroup(t);var existing=group?.FirstOrDefault();var meta=Text(FactsFor(t),12,"#E4C084");metaText=meta;meta.Margin=new(0,0,0,10);info.Children.Add(meta);var synopsis=Text(t.Summary.Length>0?t.Summary:loaded?T("No synopsis available.","لا يتوفر ملخص."):T("Loading details…","جارٍ تحميل التفاصيل…"),13,"#C9C4BD");synopsisText=synopsis;synopsis.LineHeight=20;info.Children.Add(synopsis);if(CastStripFor(t) is {} cast)info.Children.Add(cast);
            var actions=new WrapPanel{Margin=new(0,16,0,0)};info.Children.Add(actions);void Act(string text,Action action,bool primary=false){var b=ActionButton(text,()=>{CloseDiscoverPanel();action();},primary);b.Margin=new(0,0,8,8);actions.Children.Add(b);}
            if(!TrackerOnly){var local=group!=null&&group.Any(CanPlay)?ChoiceFor(group):null;if(local!=null)Act(T(store.GetProgress(local) is {Completed:false,Position:>0}?"Resume":"Play"),()=>_=PlayVideo(local),true);Act(T("Stream"),()=>{var episode=activeTitle.IsMovie?null:NextToStream(activeTitle,existing);DiscoverStream(activeTitle,episode?.Season??0,episode?.Episode??0);},local==null);Act(T("Download"),()=>{var episode=activeTitle.IsMovie?null:NextToStream(activeTitle,existing);DiscoverDownload(activeTitle,episode?.Season??0,episode?.Episode??0,false);});}var listed=existing!=null&&store.State.Watchlist.Contains(existing.GroupKey);Act(listed?T("In My List · click to remove","في قائمتي · انقر للإزالة"):T("Add to My List","إضافة إلى قائمتي"),()=>ToggleDiscoveredWatchlist(activeTitle));Act(T("Log","تسجيل المشاهدة"),()=>OpenLog(activeTitle));
            var full=ActionButton(T("Full page [[chevron]]","الصفحة الكاملة [[chevron]]"),()=>{CloseDiscoverPanel();if(page=="Discover"){discoverReturnOffset=ContentScroll.VerticalOffset;CancelDiscover();}else{Navigate("Discover");discoverReturnOffset=-1;}_=OpenDiscoverFull(activeTitle);});full.Foreground=Brush("#E4C084");full.Background=Brush("Transparent");full.Margin=new(0,4,0,0);System.Windows.Automation.AutomationProperties.SetName(full,"Full page");info.Children.Add(full);
        }
        Paint(title,false);
        try{using var client=DiscoverClient();var details=await client.Details(title,token);cachedRuntime=await Task.Run(()=>{try{var key=$"meta/{details.Type}/{details.Id}.json";var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(store.DataDirectory,"discover",hash+".json")));return json.RootElement.GetProperty("meta").TryGetProperty("runtime",out var runtime)?runtime.ToString():"";}catch{return "";}},token);if(!closing&&!token.IsCancellationRequested&&discoverPanel==overlay){Paint(details,true);}}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception error){store.Log(error);if(discoverPanel==overlay&&!token.IsCancellationRequested){Paint(title,true);}}
    }
    Func<DiscoveryClient>? discoveryFactory {get;set;}
    DiscoveryClient DiscoverClient()=>discoveryFactory?.Invoke()??new DiscoveryClient(store.DataDirectory);
    string discoverFilter="All";
    DiscoverTitle? discoverTitle;
    // Where Discover was scrolled when a title's full page opened from it; Back returns there with the same search.
    double discoverReturnOffset=-1;
    void BackToDiscoverResults(){CancelDiscover();discoverTitle=null;Render();var offset=discoverReturnOffset;discoverReturnOffset=-1;if(offset<=0){ContentScroll.ScrollToTop();return;}Dispatcher.BeginInvoke(()=>ContentScroll.ScrollToVerticalOffset(offset),System.Windows.Threading.DispatcherPriority.Loaded);}
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
        var token=BeginDiscover();var revision=discoverRevision;cinematic=false;ApplyPageLayout();PageTitle.Text=title.Name;ContentPanel.Children.Clear();ContentPanel.Children.Add(Heading("Opening "+title.Name+"…"));ContentPanel.Children.Add(ActionButton("[[back]]  Back to Discover",BackToDiscoverResults));
        try{using var client=DiscoverClient();var details=await client.Details(title,token);if(closing||page!="Discover"||revision!=discoverRevision)return;discoverTitle=details;discoverSeason=details.Episodes.FirstOrDefault(e=>e.AirsAt==null||e.AirsAt<=DateTimeOffset.UtcNow)?.Season??1;Render();ContentScroll.ScrollToTop();}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error){if(revision!=discoverRevision||closing)return;store.Log(error);ContentPanel.Children.Add(Body("Details couldn’t be loaded. Please try again."));ContentPanel.Children.Add(ActionButton("Retry",()=>_=OpenDiscoverFull(title)));}
    }
}
