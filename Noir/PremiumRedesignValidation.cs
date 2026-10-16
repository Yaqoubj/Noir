#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Noir;
public partial class MainWindow
{
    async Task ValidatePremiumRedesign()
    {
        var results=new List<string>();
        void Check(bool ok,string label){if(!ok)throw new Exception(label);results.Add("PASS: "+label);}
        async Task Settle(){UpdateLayout();await Task.Delay(450);UpdateLayout();}
        try
        {
            var group=store.Archive.Where(i=>!i.IsMovie).GroupBy(i=>i.GroupKey).First().ToList();
            WindowState=WindowState.Normal;
            foreach(var size in new[]{(780,520),(940,650),(1320,900)})
            {
                Width=size.Item1;Height=size.Item2;Navigate("Home");await Settle();
                Check(NavScroll.ScrollableWidth<1,$"All navigation links fit at {size}");
                foreach(var button in new[]{HomeNav,ShowNav,MovieNav,WatchlistNav,HistoryNav,DiscoverNav,DownloadsNav,SettingsNav})
                {
                    var rect=button.TransformToAncestor(LibraryRoot).TransformBounds(new Rect(button.RenderSize));
                    Check(rect.Left>=0&&rect.Right<=LibraryRoot.ActualWidth+1&&rect.Top>=0&&rect.Bottom<=TopBar.ActualHeight+1,$"{button.Name} stays visible at {size}");
                }
                var heroAction=Descendants(ContentPanel).OfType<Button>().FirstOrDefault(b=>b.Content as string is "▶   Resume" or "▶   Play" or "ⓘ   More info");
                Check(heroAction!=null&&heroAction.TransformToAncestor(LibraryRoot).Transform(new Point()).Y>TopBar.ActualHeight,$"Hero actions clear the top navigation at {size}");
                CaptureLibrary(Path.Combine(store.DataDirectory,$"premium-home-{size.Item1}.png"));
                OpenDetails(group);await Settle();ContentScroll.ScrollToVerticalOffset(500);await Settle();
                Check(Descendants(ContentPanel).OfType<Button>().Any(b=>b.Content as string=="Episodes"),"Title page exposes its episode tab");
                Check(Descendants(ContentPanel).OfType<ComboBox>().Count()==0,"Season controls use visible pills");
                CaptureLibrary(Path.Combine(store.DataDirectory,$"premium-detail-{size.Item1}.png"));
            }
            Navigate("Home");await Settle();
            var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=Mouse.PreviewMouseWheelEvent,Source=ContentPanel};ContentPanel.RaiseEvent(wheel);await Task.Delay(450);
            Check(wheel.Handled&&ContentScroll.VerticalOffset>20,"Scrolling moves down smoothly");
            var down=ContentScroll.VerticalOffset;ContentPanel.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,120){RoutedEvent=Mouse.PreviewMouseWheelEvent,Source=ContentPanel});await Task.Delay(450);
            Check(ContentScroll.VerticalOffset<down,"Scrolling reverses without jumping");
            foreach(var view in new[]{"Stats","Collection","Diary"})
            {
                historyView=view;historyMonth="All time";Navigate("History");await Settle();
                CaptureLibrary(Path.Combine(store.DataDirectory,"premium-history-"+view.ToLowerInvariant()+".png"));
                Check(ContentPanel.Children.Count>3,"History "+view+" renders saved library data");
            }
            Navigate("Settings");await Settle();Check(Descendants(ContentPanel).OfType<CheckBox>().Any(),"Playback preferences remain available");CaptureLibrary(Path.Combine(store.DataDirectory,"premium-settings.png"));
            // Deterministic discovery data exercises the new hero, season tabs and future-episode guard.
            discoveryFactory=()=>new DiscoveryClient(store.DataDirectory,new DiscoveryFixtureHandler());Navigate("Discover");
            var deadline=DateTime.UtcNow.AddSeconds(15);while(discoverResults==null&&DateTime.UtcNow<deadline)await Task.Delay(80);
            Check(discoverResults?.Titles.Count>0,"Discover loads its spotlight and popular rails");await Settle();CaptureLibrary(Path.Combine(store.DataDirectory,"premium-discover.png"));
            var count=store.State.Viewings.Count;await OpenDiscover(discoverResults!.Titles.First(t=>!t.IsMovie));await Settle();
            Check(Descendants(ContentPanel).OfType<Button>().Any(b=>b.Content as string=="Find season downloads"),"Discover keeps whole-season download action");
            discoverSeason=2;Render();await Settle();Check(Descendants(ContentPanel).OfType<Button>().Any(b=>b.Content as string=="↓ Download"&&!b.IsEnabled),"Future episodes cannot be downloaded");
            Check(store.State.Viewings.Count==count,"Browsing never creates watched history");CaptureLibrary(Path.Combine(store.DataDirectory,"premium-discover-detail.png"));
        }
        catch(Exception error){results.Add("FAIL: "+error);}
        finally{CancelDiscover();}
        File.WriteAllText(Path.Combine(store.DataDirectory,"premium-redesign-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
#endif
