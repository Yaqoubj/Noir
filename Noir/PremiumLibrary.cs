using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Noir;
public partial class MainWindow
{
    string collectionFilter="My List";
    FrameworkElement StatCard(string name,string value)
    {
        var p=new StackPanel{Margin=new(18,12,18,12)};p.Children.Add(Text(value,27,"#E4C084",FontWeights.SemiBold));p.Children.Add(Text(name,12,"#AAA6A0"));return new Border{MinWidth=140,Margin=new(0,0,12,8),CornerRadius=new(12),Background=Brush("#1B1B1E"),Child=p};
    }
    void RenderCollectionsNavigation()
    {
        if(collectionFilter!="My List"&&!store.State.Collections.ContainsKey(collectionFilter))collectionFilter="My List";
        ContentPanel.Children.Add(Chips(new[]{"My List"}.Concat(store.State.Collections.Keys.Order()).ToArray(),collectionFilter,v=>{collectionFilter=v;Render();}));
        ContentPanel.Children.Add(ActionButton("+ Create collection",()=>CollectionDialog(null)));
    }
    void CollectionDialog(VideoItem? item)
    {
        var w=new Window{Title="Collections",Owner=this,Width=440,Height=360,Background=Brush("#0D0D0F"),Foreground=Brush("#F4F1EB"),WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new(24)};
        p.Children.Add(Heading(item==null?"New collection":"Save to a collection"));
        if(item!=null)foreach(var c in store.State.Collections){var name=c.Key;var check=new CheckBox{Content=name,IsChecked=c.Value.Contains(item.GroupKey),Margin=new(0,6,0,6)};check.Click+=(_,_)=>{store.SetCollection(item,name,check.IsChecked==true);Render();};p.Children.Add(check);}
        var input=new TextBox{MaxLength=60,Margin=new(0,12,0,12)};p.Children.Add(input);var error=Body("",12);p.Children.Add(error);
        p.Children.Add(ActionButton("Create",()=>{var name=input.Text.Trim();if(name.Length==0){error.Text="Give the collection a name.";return;}try{if(item!=null)store.SetCollection(item,name,true);else store.CreateCollection(name);collectionFilter=name;w.Close();Render();}catch(Exception e){error.Text=e.Message;}},true));w.Content=new ScrollViewer{Content=p};w.ShowDialog();
    }
    void MatchDialog(VideoItem item)
    {
        var w=new Window{Title="Correct match",Owner=this,Width=520,Height=550,Background=Brush("#0D0D0F"),Foreground=Brush("#F4F1EB"),WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new(24)};
        p.Children.Add(Heading("Correct this match","History and file names stay intact. Preview your numbers before saving."));
        TextBox Field(string label,string value){p.Children.Add(Body(label,12));var b=new TextBox{Text=value};p.Children.Add(b);return b;}
        var title=Field("Title",item.Title);var year=Field("Year (0 if unknown)",item.Year.ToString());var imdb=Field("IMDb ID (optional)",item.ImdbId);var season=Field("Season",item.Season.ToString());var episode=Field("Episode",item.Episode.ToString());
        var whole=new CheckBox{Content="Apply title/year and season offset to the whole show",IsChecked=!item.IsMovie,Margin=new(0,16,0,10)};p.Children.Add(whole);var error=Body("",12);p.Children.Add(error);
        p.Children.Add(ActionButton("Save correction",()=>{try{store.CorrectMatch(item,title.Text,int.Parse(year.Text),imdb.Text.Trim(),int.Parse(season.Text),int.Parse(episode.Text),whole.IsChecked==true);w.Close();Render();}catch(Exception e){error.Text=e.Message;}},true));w.Content=new ScrollViewer{Content=p};w.ShowDialog();
    }
    void RenderHomeExtras(List<List<VideoItem>> groups)
    {
        // The play queue as a row; order and removal are small icon buttons on each card.
        var queued=store.State.PlayQueue.ToList().Select(id=>store.Archive.FirstOrDefault(i=>i.Id==id)).OfType<VideoItem>().ToList();
        HomeRow("queue",()=>{if(queued.Count>0)
        {
            var cards=new List<FrameworkElement>();var position=0;
            foreach(var i in queued)
            {
                var id=i.Id;var card=(StackPanel)ContinueCard(i);var tools=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,0,4)};
                card.Children.Insert(1,new TextBlock{Text=$"#{++position} in queue",FontSize=11,Foreground=Brush("#E4C084"),Margin=new(0,8,0,0)});
                Button Small(string glyph,string tip,Action act){var b=ActionButton(glyph,act);b.ToolTip=tip;b.Width=34;b.Height=30;b.Padding=new(0);b.Margin=new(0,0,6,0);b.Background=Brush("#1C1C1F");System.Windows.Automation.AutomationProperties.SetName(b,tip);tools.Children.Add(b);return b;}
                Small("‹","Move earlier",()=>{store.MoveQueue(id,-1);Render();});Small("›","Move later",()=>{store.MoveQueue(id,1);Render();});Small("✕","Remove from queue",()=>{store.State.PlayQueue.Remove(id);store.Save();Render();});
                card.Children.Add(tools);cards.Add(Lift(card));
            }
            ContentPanel.Children.Add(Rail("Your play queue","Plays in this order. Files that aren't on this PC are skipped.",cards));
        }
        });
        HomeRow("recently-added",()=>{var added=groups.Where(g=>g.Any(i=>i.AddedAt.HasValue)).OrderByDescending(g=>g.Max(i=>i.AddedAt)).Take(16).ToList();if(added.Count>0)RailCatalog(added,"Recently added");});
        HomeRow("upcoming",()=>RenderComingUp(groups));
    }
}
