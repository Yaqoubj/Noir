using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Noir;
public partial class MainWindow
{
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
    async Task OpenDiscover(DiscoverTitle title)
    {
        var token=BeginDiscover();var revision=discoverRevision;cinematic=false;ApplyPageLayout();PageTitle.Text=title.Name;ContentPanel.Children.Clear();ContentPanel.Children.Add(Heading("Opening "+title.Name+"…"));ContentPanel.Children.Add(ActionButton("Back to results",()=>{CancelDiscover();discoverTitle=null;Render();}));
        try{using var client=DiscoverClient();var details=await client.Details(title,token);if(closing||page!="Discover"||revision!=discoverRevision)return;discoverTitle=details;discoverSeason=details.Episodes.FirstOrDefault(e=>e.AirsAt==null||e.AirsAt<=DateTimeOffset.UtcNow)?.Season??1;Render();ContentScroll.ScrollToTop();}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error){if(revision!=discoverRevision||closing)return;store.Log(error);ContentPanel.Children.Add(Body("Details couldn’t be loaded. Please try again."));ContentPanel.Children.Add(ActionButton("Retry",()=>_=OpenDiscover(title)));}
    }
}
