using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Noir;
public partial class MainWindow
{
    readonly Dictionary<string,int> cardLimits=new();
    void PagedCards(List<List<VideoItem>> groups,string section,Func<List<VideoItem>,FrameworkElement> card)
    {
        var key=page+"|"+SearchBox.Text+"|"+section+"|"+genreFilter+"|"+librarySort+"|"+historyType+"|"+statusFilter+"|"+historySort;
        if(cardLimits.Count>100)cardLimits.Clear();
        var limit=cardLimits.GetValueOrDefault(key,40);var wrap=new System.Windows.Controls.WrapPanel();ContentPanel.Children.Add(wrap);
        foreach(var group in groups.Take(limit))wrap.Children.Add(card(group));
        if(groups.Count<=limit)return;
        var more=ActionButton("",()=>{});void Label()=>more.Content=$"Show 40 more · {Math.Max(0,groups.Count-limit)} remaining";Label();
        more.Click+=(_,_)=>{foreach(var group in groups.Skip(limit).Take(40))wrap.Children.Add(card(group));limit+=40;cardLimits[key]=limit;Label();if(limit>=groups.Count)more.Visibility=Visibility.Collapsed;};
        ContentPanel.Children.Add(more);
    }

    FrameworkElement NavigationLabel(string title,string geometry)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        row.Children.Add(new Viewbox{Width=18,Height=18,Margin=new(0,0,13,0),Child=new System.Windows.Shapes.Path{Data=Geometry.Parse(geometry),Stroke=Brush("#C7C2BC"),StrokeThickness=1.6,StrokeLineJoin=PenLineJoin.Round}});
        row.Children.Add(Text(title,13));return row;
    }
    void ApplyNavigationIcons()
    {
        foreach(var button in new[]{HomeNav,DiscoverNav,MovieNav,ShowNav,HistoryNav,DownloadsNav,WatchlistNav,ScanNav,SettingsNav})button.Padding=new Thickness(12,10,12,10);
        DiscoverNav.Content=NavigationLabel("Discover","M10,1 A9,9 0 1 1 9.99,1 M16,16 L23,23 M6,10 L14,10 M10,6 L10,14");
        DownloadsNav.Content=NavigationLabel("Downloads","M12,1 L12,16 M6,10 L12,16 L18,10 M2,17 L2,23 L22,23 L22,17");
        HomeNav.Content=NavigationLabel("Home","M1,10 L12,1 L23,10 M4,8 L4,23 L10,23 L10,15 L15,15 L15,23 L20,23 L20,8");
        MovieNav.Content=NavigationLabel("Movies","M2,2 L22,2 L22,22 L2,22 Z M2,7 L22,7 M7,2 L7,7 M14,2 L14,7 M9,11 L9,19 L16,15 Z");
        ShowNav.Content=NavigationLabel("Shows","M2,4 L22,4 L22,19 L2,19 Z M8,23 L16,23 M12,19 L12,23");
        HistoryNav.Content=NavigationLabel("History","M3,7 A10,10 0 1 1 2,15 M1,1 L1,8 L8,8 M12,6 L12,12 L17,15");
        WatchlistNav.Content=NavigationLabel("My List","M5,2 L19,2 L19,23 L12,18 L5,23 Z M8,8 L16,8 M12,4 L12,12");
        ScanNav.Content=NavigationLabel("Scan library","M3,8 A10,10 0 0 1 20,4 M20,1 L20,7 L14,7 M21,16 A10,10 0 0 1 4,20 M4,23 L4,17 L10,17");
        SettingsNav.Content=NavigationLabel("Settings","M1,5 L23,5 M1,12 L23,12 M1,19 L23,19 M7,2 L7,8 M17,9 L17,15 M10,16 L10,22");
    }
    static IEnumerable<FrameworkElement> ViewElements(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is FrameworkElement element)yield return element;foreach(var next in ViewElements(child))yield return next;}
    }
}
