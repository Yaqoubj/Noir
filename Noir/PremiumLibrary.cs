using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Noir;
public partial class MainWindow
{
    string historyView="Stats",collectionFilter="My List",historyMonth="",statusFilter="All";
    int diaryLimit=60;
    void RenderPremiumHistory()
    {
        PageTitle.Text="Watch history";
        ContentPanel.Children.Add(Body("Your latest viewing, stats, collection and diary. Posters and dates stay here when files are gone."));
        if(SearchBox.Text.Trim().Length==0)RenderLatest();
        var records=store.Archive.SelectMany(i=>store.ViewingHistory(i).Select(v=>(Item:i,View:v))).OrderByDescending(r=>r.View.WatchedAt).ToList();
        var months=records.Select(r=>r.View.WatchedAt.ToLocalTime().ToString("yyyy-MM")).Append(DateTime.Now.ToString("yyyy-MM")).Distinct().OrderDescending().ToList();
        if(historyMonth.Length==0)historyMonth=months[0];
        var navigation=new WrapPanel{Margin=new(0,10,0,4)};var tabs=Chips(["Stats","Collection","Diary"],historyView,v=>{historyView=v;Render();});tabs.Margin=new(0);navigation.Children.Add(tabs);
        var month=ActionButton(historyMonth=="All time"?"All time ▾":DateTime.Parse(historyMonth+"-01").ToString("MMMM yyyy")+" ▾",()=>{});month.Margin=new(8,0,0,8);var monthMenu=new ContextMenu();foreach(var value in new[]{"All time"}.Concat(months)){var selected=value;var option=new MenuItem{Header=value=="All time"?value:DateTime.Parse(value+"-01").ToString("MMMM yyyy")};option.Click+=(_,_)=>{historyMonth=selected;diaryLimit=60;Render();};monthMenu.Items.Add(option);}month.Click+=(_,_)=>{monthMenu.PlacementTarget=month;monthMenu.IsOpen=true;};navigation.Children.Add(month);ContentPanel.Children.Add(navigation);
        if(historyView=="Stats"){month.Visibility=Visibility.Collapsed;RenderHistoryStats();return;}
        var monthly=records.Where(r=>historyMonth=="All time"||r.View.WatchedAt.ToLocalTime().ToString("yyyy-MM")==historyMonth).ToList();
        var rewindCount=monthly.Count(r=>store.ViewingHistory(r.Item).Any(v=>v.WatchedAt<r.View.WatchedAt));
        ContentPanel.Children.Add(new Border{Background=Brush("#18181B"),CornerRadius=new(10),Padding=new(16,12,16,12),Margin=new(0,8,0,12),Child=Text($"{monthly.Count} viewings   ·   {monthly.Select(r=>r.Item.GroupKey).Distinct().Count()} titles   ·   {rewindCount} rewatches   ·   {monthly.Sum(r=>r.Item.Duration)/3600:0.#} h known runtime",13,"#C7C2BC")});
        if(historyView=="Diary")ContentPanel.Children.Add(Chips(["All","Movies","Shows"],historyType,v=>{historyType=v;Render();}));
        var query=SearchBox.Text.Trim();
        bool Fits(VideoItem i)=>(historyType!="Movies"||i.IsMovie)&&(historyType!="Shows"||!i.IsMovie)&&(query.Length==0||i.Title.Contains(query,StringComparison.OrdinalIgnoreCase)||i.EpisodeTitle.Contains(query,StringComparison.OrdinalIgnoreCase));
        if(historyView=="Diary")
        {
            var filtered=monthly.Where(r=>Fits(r.Item)).ToList();
            foreach(var day in filtered.Take(diaryLimit).GroupBy(r=>r.View.WatchedAt.ToLocalTime().Date))
            {
                ContentPanel.Children.Add(Heading(day.Key.ToString("dddd, d MMMM yyyy")));
                foreach(var r in day)
                {
                    var row=new Grid{Margin=new(14)};row.ColumnDefinitions.Add(new(){Width=new(82)});row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                    var art=Art(r.Item.Poster,r.Item.Title,94,true);row.Children.Add(art);
                    var label=new StackPanel{Margin=new(18,0,18,0),VerticalAlignment=VerticalAlignment.Center};label.Children.Add(Text(r.Item.Title,17,weight:FontWeights.SemiBold));label.Children.Add(Body(r.Item.EpisodeLabel+" · "+r.View.WatchedAt.ToLocalTime().ToString("HH:mm")+" · "+r.View.Source,12));
                    var rating=store.State.PersonalReviews.GetValueOrDefault(r.Item.Id)?.Rating??0;if(rating>0)label.Children.Add(Text(new string('★',rating),16,"#E4C084"));if(!CanPlay(r.Item))label.Children.Add(Text("File unavailable · History kept",11,"#AAA6A0"));Grid.SetColumn(label,1);row.Children.Add(label);
                    var actions=new StackPanel{VerticalAlignment=VerticalAlignment.Center};actions.Children.Add(ActionButton("Details & comments",()=>OpenEpisodeInfo(r.Item)));actions.Children.Add(ActionButton("Rate / note",()=>OpenDiary(r.Item)));Grid.SetColumn(actions,2);row.Children.Add(actions);
                    ContentPanel.Children.Add(new Border{Background=Brush("#171719"),CornerRadius=new(12),Margin=new(0,0,0,10),Child=row});
                }
            }
            if(filtered.Count==0)ContentPanel.Children.Add(Heading("No viewings in this month","Choose another month or All time."));
            if(filtered.Count>diaryLimit)ContentPanel.Children.Add(ActionButton("Load more viewings",()=>{diaryLimit+=60;Render();}));return;
        }
        var filters=new WrapPanel{Margin=new(0,12,0,8)};foreach(var filter in new[]{Chips(["All","Movies","Shows"],historyType,v=>{historyType=v;Render();}),Chips(["All","Watching","Planned","Finished","Dropped","Unavailable"],statusFilter,v=>{statusFilter=v;Render();}),Chips(["Recent","A–Z"],historySort,v=>{historySort=v;Render();})}){filter.Margin=new(0);filters.Children.Add(filter);}filters.Margin=new(0,10,0,6);ContentPanel.Children.Add(filters);
        var groups=store.Archive.GroupBy(i=>i.GroupKey).Select(g=>store.EpisodesFor(g.Key)).Where(g=>g.Any(i=>store.EverWatched(i)||store.GetProgress(i) is {Position:>0})||store.State.TitleStatuses.ContainsKey(g[0].GroupKey)).Where(g=>g.Any(Fits))
            .Where(g=>statusFilter=="All"||(statusFilter=="Unavailable"?!g.Any(CanPlay):store.TitleStatus(g)==statusFilter))
            .Where(g=>historyFilter!="Unavailable"||!g.Any(CanPlay)).ToList();
        groups=(historySort=="A–Z"?groups.OrderBy(g=>g[0].Title):groups.OrderByDescending(g=>g.Max(HistoryDate))).ToList();
        if(groups.Count==0){ContentPanel.Children.Add(Heading("No matches in this view"));ContentPanel.Children.Add(ActionButton("Show all history",()=>{statusFilter=historyFilter=historyType="All";SearchBox.Text="";Render();}));return;}
        PagedCards(groups,"History collection",CollectionCard);
    }
    FrameworkElement StatCard(string name,string value)
    {
        var p=new StackPanel{Margin=new(18,12,18,12)};p.Children.Add(Text(value,27,"#E4C084",FontWeights.SemiBold));p.Children.Add(Text(name,12,"#AAA6A0"));return new Border{MinWidth=140,Margin=new(0,0,12,8),CornerRadius=new(12),Background=Brush("#1B1B1E"),Child=p};
    }
    FrameworkElement CollectionCard(List<VideoItem> group)
    {
        var last=group.OrderByDescending(HistoryDate).First();var next=store.ContinueFor(group);var p=new StackPanel();var picture=new Grid{Height=288,ClipToBounds=true};
        var poster=ActionButton("",()=>OpenDetails(group));poster.Content=Art(last.Poster,last.Title,288,true);poster.Padding=new(0);poster.HorizontalAlignment=HorizontalAlignment.Stretch;picture.Children.Add(poster);picture.Children.Add(CardMenu(last));
        var status=store.CaughtUp(last.GroupKey,DateTimeOffset.UtcNow)&&store.TitleStatus(group)!="Dropped"?"Caught up":store.TitleStatus(group);
        picture.Children.Add(new Border{HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,Background=Brush("#E8101012"),CornerRadius=new(8),Padding=new(10,5,10,5),Margin=new(10),Child=Text(status,12,"#E4C084")});p.Children.Add(picture);
        var info=new StackPanel{Margin=new(12),Height=148};var title=ActionButton(last.Title,()=>OpenDetails(group));title.FontSize=15;title.FontWeight=FontWeights.SemiBold;title.Background=Brush("Transparent");title.Padding=new(0);info.Children.Add(title);
        var total=group.Count(i=>last.IsMovie||i.Season>0);var watched=group.Count(i=>(last.IsMovie||i.Season>0)&&store.EverWatched(i));
        var countLabel=Body(last.IsMovie?(HistoryDate(last)==DateTimeOffset.MinValue?"Not watched yet":HistoryDate(last).ToLocalTime().ToString("d MMM yyyy")):$"{watched} / {total} known episodes watched",12);countLabel.Height=34;info.Children.Add(countLabel);
        info.Children.Add(new ProgressBar{Value=watched,Maximum=Math.Max(1,total),Height=4,Foreground=Brush("#E4C084"),Background=Brush("#353238"),BorderThickness=new(0),Margin=new(0,0,0,12)});
        var action=ActionButton(next!=null?(store.GetProgress(next) is {Completed:false,Position:>0}?"▶ Resume · "+LibraryStore.Time(store.GetProgress(next)!.Position):"▶ "+next.EpisodeLabel):"Details",()=>{if(next!=null&&CanPlay(next))_=PlayVideo(next);else OpenDetails(group);},next!=null);action.HorizontalAlignment=HorizontalAlignment.Stretch;info.Children.Add(action);
        var rating=store.State.PersonalReviews.GetValueOrDefault(last.Id)?.Rating??0;
        if(rating>0)picture.Children.Add(new Border{HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Background=Brush("#E8101012"),CornerRadius=new(8),Padding=new(8,4,8,4),Margin=new(10),Child=Text("★ "+rating+"/5",11,"#E4C084")});
        if(!group.Any(CanPlay))info.Children.Add(Text("Archived · files unavailable",11,"#AAA6A0"));else info.Children.Add(Text(group.Count(CanPlay)+" on PC"+(HistoryDate(last)==DateTimeOffset.MinValue?"":" · "+HistoryDate(last).ToLocalTime().ToString("d MMM")),11,"#AAA6A0"));
        p.Children.Add(info);return new Border{Width=192,Margin=new(0,14,18,18),Background=Brush("#19191C"),BorderBrush=Brush("#29292D"),BorderThickness=new(1),CornerRadius=new(13),ClipToBounds=true,Child=p,ContextMenu=ItemMenu(last)};
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
        if(queued.Count>0)
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
        var added=groups.Where(g=>g.Any(i=>i.AddedAt.HasValue)).OrderByDescending(g=>g.Max(i=>i.AddedAt)).Take(16).ToList();if(added.Count>0)RailCatalog(added,"Recently added");
        RenderComingUp(groups);
    }
}
