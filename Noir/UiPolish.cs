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
        // Page links stay as text in the top bar; utilities are icon buttons with tooltips.
        DownloadsNav.Content=DownloadIcon(0);ScanNav.Content=Icon(ScanGlyph,18);SettingsNav.Content=Icon(SettingsGlyph,18);
        WatchStatusText();
    }
    static IEnumerable<FrameworkElement> ViewElements(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is FrameworkElement element)yield return element;foreach(var next in ViewElements(child))yield return next;}
    }
}
