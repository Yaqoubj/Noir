#if NOIR_VALIDATE
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
namespace Noir;
public partial class MainWindow
{
    async Task ValidateNavigationDiscover()
    {
        var checks=new List<string>();void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        async Task Wait(Func<bool> condition){var deadline=DateTime.UtcNow.AddSeconds(20);while(!condition()&&DateTime.UtcNow<deadline)await Task.Delay(50);Check(condition(),"Asynchronous view reached expected state");}
        try
        {
            foreach(var size in new[]{(780,520),(940,650),(1320,900),(1920,1080)}){
                Width=size.Item1;Height=size.Item2;Navigate("Home");UpdateLayout();await Task.Delay(80);Check(ContentScroll.ActualWidth>540&&ContentScroll.ActualHeight>200,$"Home adapts to {size}");CaptureLibrary(Path.Combine(store.DataDirectory,$"navigation-{size.Item1}.png"));
                var group=store.Archive.Where(i=>!i.IsMovie).GroupBy(i=>i.GroupKey).First().ToList();OpenDetails(group);UpdateLayout();await Task.Delay(80);var primary=Descendants(ContentPanel).OfType<Button>().Where(b=>b.Content as string is "▶ Play" or "↓ Download" or "▶ Resume").ToList();Check(primary.Count>0&&primary.All(b=>b.ActualWidth>=90),$"Episode actions stay usable at {size}");CaptureLibrary(Path.Combine(store.DataDirectory,$"episodes-{size.Item1}.png"));
            }
            Width=940;Height=650;Navigate("Home");UpdateLayout();ContentScroll.ScrollToTop();UpdateLayout();
            var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=Mouse.PreviewMouseWheelEvent,Source=ContentPanel};ContentPanel.RaiseEvent(wheel);await Task.Delay(450);Check(wheel.Handled&&ContentScroll.VerticalOffset>20,"Mouse wheel moves smoothly down the page");var down=ContentScroll.VerticalOffset;
            ContentPanel.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,120){RoutedEvent=Mouse.PreviewMouseWheelEvent,Source=ContentPanel});await Task.Delay(450);Check(ContentScroll.VerticalOffset<down,"Mouse wheel reverses without jumping");
            var scroll=new ScrollViewer{Width=400,Height=80,HorizontalScrollBarVisibility=ScrollBarVisibility.Visible,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=new Border{Width=1200,Height=20}};ContentPanel.Children.Insert(0,scroll);UpdateLayout();var horizontal=Descendants(scroll).OfType<ScrollBar>().First(s=>s.Orientation==Orientation.Horizontal);var track=(Track)horizontal.Template.FindName("PART_Track",horizontal);Check(horizontal.ActualWidth>300&&track.Orientation==Orientation.Horizontal&&!track.IsDirectionReversed,"Horizontal scrollbar has correct width, orientation and direction");
            Check(WindowAppearance.Apply(this),"Windows accepts the dark title bar, caption and text color settings");
            var bounds=new Rect(Left,Top,Width,Height);ToggleFullscreen(false);await Task.Delay(100);ToggleFullscreen(false);await Task.Delay(100);Check(WindowState==WindowState.Normal&&Math.Abs(Width-bounds.Width)<1&&Math.Abs(Height-bounds.Height)<1,"Leaving fullscreen restores the original window dimensions");
            var catalogCount=store.Archive.Count;var historyCount=store.State.Viewings.Count;var progressCount=store.State.Progress.Count;
            discoveryFactory=()=>new DiscoveryClient(store.DataDirectory,new DiscoveryFixtureHandler());Navigate("Discover");await Wait(()=>discoverResults?.Titles.Any(t=>t.Id=="tt123456")==true);Check(store.Archive.Count==catalogCount,"Browsing Discover does not add titles or watched history");
            await OpenDiscover(discoverResults!.Titles.First(t=>!t.IsMovie));Check(discoverTitle?.Episodes.Count==3,"Title details contain the full known episode schedule");Check(Descendants(ContentPanel).OfType<Button>().Any(b=>b.Content as string=="Find season downloads"),"Discover exposes whole-season downloads");
            discoverSeason=2;Render();Check(Descendants(ContentPanel).OfType<Button>().Any(b=>b.Content as string=="↓ Download"&&!b.IsEnabled),"Future episodes cannot be downloaded prematurely");
            var saved=store.AddDiscovered(discoverTitle!,true);Check(saved.Count==3&&store.State.Watchlist.Contains(saved[0].GroupKey),"Saving a new show keeps its full schedule in My List");store.AddDiscovered(discoverTitle!,true);Check(store.Archive.Count==catalogCount+1,"Saving the same title twice creates no duplicate");Check(store.State.Viewings.Count==historyCount&&store.State.Progress.Count==progressCount,"Discover never marks a searched or saved title as watched");
            var reopened=new LibraryStore(store.DataDirectory);Check(reopened.EpisodesFor(saved[0].GroupKey).Count==3,"Saved discovery schedules survive reopening");
            var media=Path.Combine(store.DataDirectory,"download-fixture");Directory.CreateDirectory(media);store.State.Roots.Add(new(){Path=media});downloads=new(store,new UiDownloadProvider(),start:false);downloads.Configure("https://example.invalid/fixture/manifest.json","fixture-not-real",media,"");
            discoverSeason=1;Render();var seasonDownload=Descendants(ContentPanel).OfType<Button>().First(b=>b.Content as string=="Find season downloads");seasonDownload.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>OwnedWindows.Cast<Window>().Any(w=>w.Title=="Download season"&&Descendants((DependencyObject)w.Content).OfType<Button>().Any(b=>b.Content as string=="Choose files")));
            var picker=OwnedWindows.Cast<Window>().First(w=>w.Title=="Download season");Descendants((DependencyObject)picker.Content).OfType<Button>().First(b=>b.Content as string=="Choose files").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>Descendants((DependencyObject)picker.Content).OfType<Button>().Any(b=>b.Content as string=="Download selected to this PC"));
            Descendants((DependencyObject)picker.Content).OfType<Button>().First(b=>b.Content as string=="Download selected to this PC").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);Check(page=="Downloads"&&downloads.Jobs().Count==2&&downloads.Jobs().All(j=>j.Target.ImdbId=="tt123456"),"A newly discovered show downloads the selected season through TorBox and opens the queue");CaptureLibrary(Path.Combine(store.DataDirectory,"downloads-page.png"));
            foreach(var file in Directory.GetFiles(Path.Combine(store.DataDirectory,"discover")))File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-3));using(var offline=new DiscoveryClient(store.DataDirectory,new DiscoveryFixtureHandler(true))){var cache=await offline.Search("","All",default);Check(cache.Cached&&cache.Titles.Count==2,"Search retains cached results while offline");var details=await offline.Details(cache.Titles.First(t=>!t.IsMovie),default);Check(details.Episodes.Count==3,"Cached title details and schedules work offline");}
            discoveryFactory=null;CancelDiscover();using(var live=new DiscoveryClient(store.DataDirectory)){var results=await live.Search("Dorohedoro","TV shows",default);Check(results.Titles.Any(t=>t.Id=="tt11147852"),"Live Cinemeta search finds the correct Dorohedoro identity");var details=await live.Details(results.Titles.First(t=>t.Id=="tt11147852"),default);Check(details.Episodes.Any(e=>e.Season==2),"Live title schedule includes Dorohedoro season two");discoverResults=results;discoverQuery="Dorohedoro";discoverResultsFilter="TV shows";discoverFilter="TV shows";SearchBox.Text="Dorohedoro";searchDebounce?.Stop();discoverTitle=details;discoverSeason=2;page="Discover";Render();UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"discover-live-details.png"));}
            using(var live=new DiscoveryClient(store.DataDirectory)){var movies=await live.Search("The Matrix","Movies",default);Check(movies.Titles.Any(t=>t.Id=="tt0133093"),"Live movie search distinguishes The Matrix by its IMDb identity");var movie=await live.Details(movies.Titles.First(t=>t.Id=="tt0133093"),default);Check(movie.IsMovie&&movie.Year==1999&&movie.Summary.Length>20&&File.Exists(movie.Poster),"Movie details include its year, synopsis and cached poster");discoverTitle=movie;Render();UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"discover-movie.png"));
            var popular=await live.Search("","All",default);Check(popular.Titles.Count>30,"Popular browsing supplies both movie and TV choices");discoverResults=popular;discoverQuery="";discoverResultsFilter="All";discoverFilter="All";SearchBox.Text="";searchDebounce?.Stop();discoverTitle=null;Width=1320;Height=900;Render();await Task.Delay(2500);UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"discover-browse.png"));Check(Descendants(ContentPanel).OfType<Button>().Count(b=>b.ToolTip?.ToString()?.StartsWith("Explore ")==true)==30,"Discover creates only thirty initial cards for large result sets");}
            File.WriteAllText(Path.Combine(store.DataDirectory,"navigation-checks.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error){File.WriteAllText(Path.Combine(store.DataDirectory,"navigation-failure.txt"),error.ToString());}
    }
    sealed class DiscoveryFixtureHandler(bool offline=false):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(offline)throw new HttpRequestException("Offline fixture");var series=request.RequestUri!.AbsolutePath.Contains("/series/");
            var title=new{id=series?"tt123456":"tt987654",name=series?"Noir UI fixture":"Fixture Movie",releaseInfo="2026",description="Discovery test title",genres=new[]{"Drama"},videos=series?new[]{new{season=1,episode=1,name="One",released="2026-01-01"},new{season=1,episode=2,name="Two",released="2026-01-02"},new{season=2,episode=1,name="Future",released="2099-01-01"}}:[]};
            var json=request.RequestUri.AbsolutePath.Contains("/meta/")?JsonSerializer.Serialize(new{meta=title}):JsonSerializer.Serialize(new{metas=new[]{title,title}});
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
        }
    }
}
#endif
