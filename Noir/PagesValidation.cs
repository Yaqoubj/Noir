#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Noir;
public partial class MainWindow
{
    // --pages-only: Shows/Movies/My List grids (toolbar, filters, A–Z, hover card) and the Discover
    // spotlight, genre chips and rails on fixture data, in English and Arabic.
    async Task ValidatePages()
    {
        var results=new List<string>();
        void Check(bool ok,string label){if(!ok)throw new Exception(label);results.Add("PASS: "+label);}
        async Task Settle(int ms=450){UpdateLayout();await Task.Delay(ms);UpdateLayout();}
        void Shot(string name)=>CaptureLibrary(Path.Combine(store.DataDirectory,"pages-"+name+".png"));
        string Name(FrameworkElement e)=>System.Windows.Automation.AutomationProperties.GetName(e);
        try
        {
            WindowState=WindowState.Normal;Width=1320;Height=900;
            foreach(var target in new[]{"Shows","Movies"})
            {
                libraryFilter="All";genreFilter="All genres";librarySort="A–Z";Navigate(target);await Settle();
                var cards=Descendants(ContentPanel).OfType<Button>().Where(b=>Name(b).EndsWith(" details")).ToList();
                var expected=store.LibraryGroups().Count(k=>store.EpisodesFor(k) is {Count:>0} g&&g[0].IsMovie==(target=="Movies")&&!store.IsHidden(k));
                Check(cards.Count==Math.Min(48,expected),$"{target}: one poster per library title ({cards.Count} of {expected})");
                Check(Descendants(ContentPanel).OfType<ComboBox>().Any(c=>Name(c)=="Sort"),$"{target}: the toolbar has a sort menu");
                Check(LibraryFilters.All(f=>Descendants(ContentPanel).OfType<Button>().Any(b=>Name(b)==f)),$"{target}: the toolbar has status filters");
                Shot(target.ToLowerInvariant());
                if(cards.Count>0)
                {
                    var edge=(Border)cards[0].Parent;edge.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});await Settle(350);
                    var overlay=Descendants(cards[0]).OfType<Grid>().First(g=>g.Opacity>.9&&g.Children.Count==2);
                    Check(overlay!=null,$"{target}: hovering a poster reveals its details");Shot(target.ToLowerInvariant()+"-hover");
                    edge.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});
                }
                libraryFilter="Finished";Render();await Settle();
                var finished=Descendants(ContentPanel).OfType<Button>().Count(b=>Name(b).EndsWith(" details"));
                Check(finished<=cards.Count,$"{target}: Finished narrows the grid ({finished})");
                libraryFilter="All";librarySort="Recently added";Render();await Settle();
                Check(!Descendants(ContentPanel).OfType<Button>().Any(b=>Name(b)=="Jump to A"),$"{target}: the A–Z strip only shows for A–Z");
                librarySort="A–Z";Render();await Settle();
                if(expected>12)
                {
                    var jump=Descendants(ContentPanel).OfType<Button>().Last(b=>Name(b).StartsWith("Jump to ")&&b.IsEnabled);
                    jump.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
                    Check(ContentScroll.VerticalOffset>0||expected<20,$"{target}: A–Z jumps down the grid");Shot(target.ToLowerInvariant()+"-jump");
                }
            }
            var show=store.LibraryGroups().Select(store.EpisodesFor).First(g=>g.Count>0);
            store.State.Watchlist.Add(show[0].GroupKey);collectionFilter="My List";Navigate("My List");await Settle();
            Check(Descendants(ContentPanel).OfType<Button>().Any(b=>Name(b)=="Everything")&&Descendants(ContentPanel).OfType<Button>().Any(b=>Name(b)==show[0].Title+" details"),"My List uses the same grid with a type filter");Shot("mylist");
            SetLanguage("ar");Navigate("Shows");await Settle();Check(LibraryRoot.FlowDirection==FlowDirection.RightToLeft,"Arabic mirrors the library grid");Shot("shows-ar");SetLanguage("en");
            // Discover on fixture data.
            discoveryFactory=()=>new DiscoveryClient(store.DataDirectory,new DiscoveryFixtureHandler());discoverGenre="All genres";discoverFilter="All";Navigate("Discover");
            var deadline=DateTime.UtcNow.AddSeconds(15);while(discoverResults==null&&DateTime.UtcNow<deadline)await Task.Delay(80);await Settle(900);
            var dots=Descendants(ContentPanel).OfType<Button>().Where(b=>Name(b).StartsWith("Spotlight ")).ToList();
            Check(dots.Count is 0 or >=2,"Discover's spotlight has dots when several titles have artwork");
            if(dots.Count>=2){dots[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();Check(discoverSpot==1,"A spotlight dot switches the hero");}
            Shot("discover");
            var genre=Descendants(ContentPanel).OfType<Button>().FirstOrDefault(b=>b.Content is string s&&s!="All genres"&&s!="All"&&s!="Movies"&&s!="TV shows"&&b.Style==(Style)FindResource("Pill"));
            if(genre!=null){var name=(string)genre.Content;genre.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();Check(discoverGenre!="All genres","Genre chips filter Discover");Shot("discover-genre");discoverGenre="All genres";}
            else results.Add("SKIP: the fixture has too few genres for chips");
        }
        catch(Exception error){results.Add("FAIL: "+error);}
        finally{CancelDiscover();}
        File.WriteAllText(Path.Combine(store.DataDirectory,"pages-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
#endif
