#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Noir;
public partial class MainWindow
{
    async Task ValidateHomeLayout()
    {
        var results=new List<string>();void Check(bool condition,string label){if(!condition)throw new Exception(label);results.Add("PASS: "+label);}async Task Settle(int ms=600){UpdateLayout();await Task.Delay(ms);UpdateLayout();}
        void Capture(FrameworkElement element,string name){element.UpdateLayout();var bitmap=new RenderTargetBitmap(Math.Max(1,(int)element.ActualWidth),Math.Max(1,(int)element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(store.DataDirectory,"browsing-"+name+".png"));encoder.Save(file);}
        try
        {
            WindowState=WindowState.Normal;Width=1320;Height=900;SetLanguage("en");Navigate("Shows");SaveLibraryView("Poster","M");Render();await Settle();var group=store.LibraryGroups().Select(store.EpisodesFor).First(g=>!g[0].IsMovie);var card=Descendants(ContentPanel).OfType<Button>().First(b=>System.Windows.Automation.AutomationProperties.GetName(b)==group[0].Title+" details");card.BringIntoView();await Settle(200);var anchor=(FrameworkElement)card.Parent;ShowHoverCard(anchor,anchor,group);await Settle();Check(hoverCard?.IsOpen==true,"Hover preview expands without reflowing the library");Capture((FrameworkElement)hoverCard!.Child,"hover-expanded");CloseHoverCard();
            SaveLibraryView("List","M");Render();await Settle();Check(Descendants(ContentPanel).OfType<DockPanel>().Any(),"Library list view renders compact title rows");CaptureLibrary(Path.Combine(store.DataDirectory,"browsing-list-view.png"));SaveLibraryView("Landscape","S");Render();await Settle();CaptureLibrary(Path.Combine(store.DataDirectory,"browsing-landscape-view.png"));SaveLibraryView("Poster","M");
            Navigate("Home");await Settle();var home=HomeLayout.Load(store.DataDirectory);home.Rows=[new(){Id="services"},new(){Id="continue",Visible=false},new(){Id="list",Visible=false},new(){Id="unknown-row"}];home.Save(store.DataDirectory);var resolved=HomeLayout.Load(store.DataDirectory).Resolve([]);Check(resolved[0].Id=="continue"&&resolved[0].Visible&&resolved.All(r=>r.Id!="unknown-row"),"Home persistence keeps Continue first and ignores removed rows");new HomeLayout().Save(store.DataDirectory);Render();await Settle();var editor=CreateHomeLayoutEditor(HomeLayout.Defaults.Concat(["genre:Drama","collection:Weekend"]));editor.Show();await Settle();Capture((FrameworkElement)editor.Content,"home-editor");editor.Close();
            discoveryFactory=()=>new DiscoveryClient(store.DataDirectory,new DiscoveryFixtureHandler());await OpenDiscover(new DiscoverTitle{Id="tt0903747",Type="series",Name="Breaking Bad"});await Settle();Check(discoverPanel!=null&&Descendants(discoverPanel).OfType<Button>().Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Full page"),"Discover preview exposes a full-page link");Capture((FrameworkElement)LibraryRoot.Parent,"discover-panel");CloseDiscoverPanel();Check(discoverPanel==null,"Discover preview closes without replacing the current page");
            SetLanguage("ar");Navigate("Home");await Settle();var arabic=CreateHomeLayoutEditor(HomeLayout.Defaults);arabic.Show();await Settle();Capture((FrameworkElement)arabic.Content,"home-editor-ar");arabic.Close();SetLanguage("en");
        }
        catch(Exception error){results.Add("FAIL: "+error);}
        finally{CloseHoverCard();CloseDiscoverPanel();File.WriteAllText(Path.Combine(store.DataDirectory,"home-layout-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
    }
}
#endif
