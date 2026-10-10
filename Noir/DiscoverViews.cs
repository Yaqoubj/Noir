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
    void RenderDiscover()
    {
        PageTitle.Text=discoverTitle?.Name??"Discover";
        if(discoverTitle!=null){RenderDiscoverDetail(discoverTitle);return;}
        ContentPanel.Children.Add(Body("Find something new. Choose a title, then download a movie, an episode or a whole season to your PC.",13));
        ContentPanel.Children.Add(Chips(["All","Movies","TV shows"],discoverFilter,label=>{discoverFilter=label;_=SearchDiscover();}));
        if(discoverResults==null||discoverQuery!=SearchBox.Text.Trim()||discoverResultsFilter!=discoverFilter){ContentPanel.Children.Add(Heading("Finding your next watch…"));_=SearchDiscover();return;}
        if(discoverMessage.Length>0)ContentPanel.Children.Add(Body(discoverMessage,12));
        ContentPanel.Children.Add(Heading(discoverQuery.Length==0?"Popular movies & shows":"Search results",$"{discoverResults.Titles.Count} titles"+(discoverResults.Cached?" · cached results available offline":"")));
        if(discoverResults.Titles.Count==0){ContentPanel.Children.Add(Body("No results. Try another title or check your connection."));ContentPanel.Children.Add(ActionButton("Try again",()=>_=SearchDiscover()));return;}
        var panel=new WrapPanel();ContentPanel.Children.Add(panel);var rendered=0;
        var token=discoverCancellation?.Token??BeginDiscover();var revision=discoverRevision;
        var more=ActionButton("",()=>{});
        void Batch(){foreach(var title in discoverResults.Titles.Skip(rendered).Take(30))panel.Children.Add(DiscoverCard(title,token,revision));rendered=Math.Min(rendered+30,discoverResults.Titles.Count);more.Content=$"Show more · {discoverResults.Titles.Count-rendered} remaining";more.Visibility=rendered<discoverResults.Titles.Count?Visibility.Visible:Visibility.Collapsed;}
        Batch();more.Click+=(_,_)=>Batch();ContentPanel.Children.Add(more);
        var credit=Body("Title information and artwork: Cinemeta. Downloads use your AIOStreams and TorBox connection.",11);credit.Margin=new(0,24,0,10);ContentPanel.Children.Add(credit);
    }
    async Task SearchDiscover()
    {
        if(page!="Discover"||closing)return;
        var token=BeginDiscover();var revision=discoverRevision;var query=SearchBox.Text.Trim();var filter=discoverFilter;discoverTitle=null;
        // Keep the search field stable while results load.
        ContentPanel.Children.Clear();PageTitle.Text="Discover";ContentPanel.Children.Add(Chips(["All","Movies","TV shows"],filter,label=>{discoverFilter=label;_=SearchDiscover();}));ContentPanel.Children.Add(Heading(query.Length>0?$"Searching for “{query}”…":"Finding popular movies & shows…"));
        try{using var client=DiscoverClient();var results=await client.Search(query,filter,token);if(closing||page!="Discover"||revision!=discoverRevision)return;discoverResults=results;discoverQuery=query;discoverResultsFilter=filter;discoverMessage=results.Message;Render();ContentScroll.ScrollToTop();}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error){if(revision!=discoverRevision||closing)return;store.Log(error);ContentPanel.Children.Add(Body("Couldn’t reach Discover. Your saved library still works."));ContentPanel.Children.Add(ActionButton("Retry",()=>_=SearchDiscover()));}
    }
    FrameworkElement DiscoverCard(DiscoverTitle title,CancellationToken token,int revision)
    {
        var body=new StackPanel{Width=174,Margin=new(0,0,18,22)};
        var picture=new Grid{Height=255,ClipToBounds=true};picture.Children.Add(Art(title.Poster,title.Name,255,true));
        var button=ActionButton("",()=>_=OpenDiscover(title));button.Content=picture;button.Padding=new(0);button.HorizontalAlignment=HorizontalAlignment.Stretch;button.ToolTip="Explore "+title.Name;body.Children.Add(button);
        var name=Text(title.Name,15,weight:FontWeights.SemiBold);name.Margin=new(0,9,0,5);body.Children.Add(name);body.Children.Add(Body((title.IsMovie?"Movie":"TV show")+" · "+title.Years,12));
        var existing=store.Archive.Where(i=>i.ImdbId==title.Id&&i.IsMovie==title.IsMovie).ToList();if(existing.Count>0)body.Children.Add(Text(existing.Any(CanPlay)?"On your PC":store.State.Watchlist.Contains(existing[0].GroupKey)?"In My List":"In your library",11,"#E4C084"));
        if(!File.Exists(title.Poster))_=Load();
        async Task Load(){try{using var client=DiscoverClient();await client.Artwork(title,token);if(token.IsCancellationRequested||revision!=discoverRevision||page!="Discover"||closing)return;var source=ImageSource(title.Poster,384);if(source!=null){picture.Children.Clear();picture.Children.Add(new Image{Source=source,Stretch=Stretch.UniformToFill});}}catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception e){store.Log(e);}}
        return body;
    }
    async Task OpenDiscover(DiscoverTitle title)
    {
        var token=BeginDiscover();var revision=discoverRevision;ContentPanel.Children.Clear();ContentPanel.Children.Add(Heading(title.Name,"Opening title details…"));ContentPanel.Children.Add(ActionButton("Back to results",()=>{CancelDiscover();discoverTitle=null;Render();}));
        try{using var client=DiscoverClient();var details=await client.Details(title,token);if(closing||page!="Discover"||revision!=discoverRevision)return;discoverTitle=details;discoverSeason=details.Episodes.FirstOrDefault(e=>e.AirsAt==null||e.AirsAt<=DateTimeOffset.UtcNow)?.Season??1;Render();ContentScroll.ScrollToTop();}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error){if(revision!=discoverRevision||closing)return;store.Log(error);ContentPanel.Children.Add(Body("Details couldn’t be loaded. Please try again."));ContentPanel.Children.Add(ActionButton("Retry",()=>_=OpenDiscover(title)));}
    }
    void RenderDiscoverDetail(DiscoverTitle title)
    {
        ContentPanel.Children.Add(ActionButton("← Back to results",()=>{CancelDiscover();discoverTitle=null;Render();ContentScroll.ScrollToTop();}));
        var info=new StackPanel{Margin=new(24),MaxWidth=740,HorizontalAlignment=HorizontalAlignment.Left};info.Children.Add(Text(title.Name,34,weight:FontWeights.Bold));info.Children.Add(Body((title.IsMovie?"Movie":"TV show")+" · "+title.Years+(title.Rating.Length>0?" · IMDb "+title.Rating:""),13));if(title.Genres.Count>0)info.Children.Add(Body(string.Join(" · ",title.Genres),12));info.Children.Add(Body(title.Summary,14));
        var hero=new Grid{MinHeight=235,Margin=new(0,16,0,12)};var art=ImageSource(title.Background)??ImageSource(title.Poster);if(art!=null)hero.Background=new ImageBrush(art){Stretch=Stretch.UniformToFill};hero.Children.Add(new Border{Background=Brush("#DC101012")});hero.Children.Add(info);ContentPanel.Children.Add(hero);
        var tools=new WrapPanel();var existing=store.Archive.FirstOrDefault(i=>i.ImdbId==title.Id&&i.IsMovie==title.IsMovie);
        tools.Children.Add(ActionButton(existing!=null&&store.State.Watchlist.Contains(existing.GroupKey)?"✓ In My List":"+ My List",()=>{store.AddDiscovered(title,true);Render();Status.Text="Saved to My List. Watching history stays unchanged.";}));
        if(existing!=null){var open=ActionButton("Open in library",()=>{var group=store.EpisodesFor(existing.GroupKey);Navigate(existing.IsMovie?"Movies":"Shows");OpenDetails(group);});open.Margin=new(10,0,0,0);tools.Children.Add(open);}ContentPanel.Children.Add(tools);
        void Download(int season,int episode,bool whole){try{var group=store.AddDiscovered(title,false);var item=title.IsMovie?group[0]:group.FirstOrDefault(i=>i.Season==season&&i.Episode==episode);if(item==null){Status.Text="This episode is missing from the title schedule. Refresh details.";return;}FindDownloads(item,whole);}catch(Exception e){store.Log(e);Status.Text="Couldn't prepare this title for downloading.";}}
        if(title.IsMovie){var download=ActionButton("Find movie downloads",()=>Download(0,0,false),true);download.Margin=new(0,18,0,12);ContentPanel.Children.Add(download);return;}
        var seasons=title.Episodes.Select(e=>e.Season).Distinct().Order().ToList();if(seasons.Count==0){ContentPanel.Children.Add(Body("No episode schedule is available for this title yet."));return;}
        ContentPanel.Children.Add(Heading("Episodes","Choose a single episode or find a season pack. Already downloaded episodes are skipped."));
        if(!seasons.Contains(discoverSeason))discoverSeason=seasons[0];var row=new WrapPanel{Margin=new(0,0,0,18)};var picker=new ComboBox{ItemsSource=seasons.Select(s=>"Season "+s).ToList(),SelectedItem="Season "+discoverSeason,MinWidth=150};picker.SelectionChanged+=(_,_)=>{if(picker.SelectedItem is string label){discoverSeason=int.Parse(label[7..]);Render();}};row.Children.Add(picker);
        var released=title.Episodes.Where(e=>e.Season==discoverSeason&&(e.AirsAt==null||e.AirsAt<=DateTimeOffset.UtcNow)).ToList();var seasonButton=ActionButton("Find season downloads",()=>Download(discoverSeason,released[0].Episode,true),true);seasonButton.IsEnabled=released.Count>0;seasonButton.Margin=new(12,0,0,0);row.Children.Add(seasonButton);ContentPanel.Children.Add(row);
        foreach(var episode in title.Episodes.Where(e=>e.Season==discoverSeason)){
            var available=episode.AirsAt==null||episode.AirsAt<=DateTimeOffset.UtcNow;var panel=new DockPanel{Margin=new(14)};var local=existing==null?null:store.EpisodesFor(existing.GroupKey).FirstOrDefault(i=>i.Season==episode.Season&&i.Episode==episode.Episode);
            var button=ActionButton(local!=null&&CanPlay(local)?"▶ Play":"↓ Download",()=>{if(local!=null&&CanPlay(local))_=PlayVideo(local);else Download(episode.Season,episode.Episode,false);},true);button.IsEnabled=available;DockPanel.SetDock(button,Dock.Right);panel.Children.Add(button);
            var text=new StackPanel{Margin=new(0,0,12,0)};text.Children.Add(Text($"E{episode.Episode:00} · "+episode.Title,15,weight:FontWeights.SemiBold));text.Children.Add(Body(available?(local!=null&&CanPlay(local)?"On your PC":local!=null&&store.EverWatched(local)?"Watched · file unavailable":"Released"):$"Upcoming · {episode.AirsAt:dd MMM yyyy}",12));panel.Children.Add(text);ContentPanel.Children.Add(new Border{Background=Brush("#18181B"),CornerRadius=new(9),Margin=new(0,0,0,8),Child=panel});
        }
        ContentPanel.Children.Add(Body("Information: Cinemeta · Artwork and schedules are cached for later browsing.",11));
    }
}
