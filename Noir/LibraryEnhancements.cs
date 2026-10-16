using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Noir;
public partial class MainWindow
{
    string episodeFilter="All episodes",genreFilter="All genres";
    static bool CanPlay(VideoItem item) => item.Available&&System.IO.File.Exists(item.Path);
    DateTimeOffset HistoryDate(VideoItem item) => new[]{store.GetProgress(item)?.UpdatedAt??DateTimeOffset.MinValue,store.ViewingHistory(item).FirstOrDefault()?.WatchedAt??DateTimeOffset.MinValue}.Max();
    VideoItem ChoiceFor(List<VideoItem> group)
    {
        var playable=group.Where(CanPlay).ToList();
        return playable.Count>0?store.DefaultFor(playable):group.OrderByDescending(HistoryDate).First();
    }
    string EpisodeCountText(List<VideoItem> group,bool withWatched=true)
    {
        var regular=group.Count(i=>i.Season>0);var specials=group.Count(i=>i.Season==0);
        var total=regular>0?$"{regular} episodes"+ (specials>0?$" + {specials} special{(specials==1?"":"s")}":""):$"{specials} specials";
        return total+$" · {group.Count(CanPlay)} on PC"+(withWatched?$" · {group.Count(store.EverWatched)} watched":"");
    }
    FrameworkElement PlayCircle() => new Border
    {
        Width=54,Height=54,CornerRadius=new(27),Background=Brush("#E6101012"),BorderBrush=Brush("#D9FFFFFF"),BorderThickness=new(1.5),
        HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,
        Child=new System.Windows.Shapes.Path {Data=Geometry.Parse("M 0,0 L 0,22 L 18,11 Z"),Fill=Brush("#FFFFFF"),Width=18,Height=22,Margin=new(5,0,0,0),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}
    };
    Button CardMenu(VideoItem item)
    {
        var button=new Button {Content="⋯",Width=34,Height=30,FontSize=22,Padding=new(0),Background=Brush("#D9101012"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(8),ToolTip="Options for "+item.Label};
        System.Windows.Automation.AutomationProperties.SetName(button,"Options for "+item.Label);
        button.Click+=(_,e)=>{e.Handled=true;var menu=ItemMenu(item);menu.PlacementTarget=button;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;};
        return button;
    }
    FrameworkElement ContinueShelf(IEnumerable<UpNext> videos,string label="Continue Watching",string subtitle="Pick up from your saved time, or play the next episode.")
    {
        var container=new StackPanel();var title=new DockPanel{Margin=new(0,24,0,15)};
        var arrows=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(arrows,Dock.Right);title.Children.Add(arrows);
        var heading=new StackPanel();heading.Children.Add(Text(label,21,weight:FontWeights.SemiBold));heading.Children.Add(Body(subtitle,12));title.Children.Add(heading);
        var row=new StackPanel{Orientation=Orientation.Horizontal};foreach(var entry in videos) row.Children.Add(ContinueCard(entry.Item,entry));
        var scroll=new ScrollViewer {Content=row,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,PanningMode=PanningMode.HorizontalOnly};
        var previous=ActionButton("‹",()=>scroll.ScrollToHorizontalOffset(Math.Max(0,scroll.HorizontalOffset-576)));previous.ToolTip="Previous titles";previous.FontSize=22;previous.Padding=new(14,3,14,3);
        var next=ActionButton("›",()=>scroll.ScrollToHorizontalOffset(Math.Min(scroll.ScrollableWidth,scroll.HorizontalOffset+576)));next.ToolTip="More titles";next.FontSize=22;next.Padding=new(14,3,14,3);next.Margin=new(8,0,0,0);arrows.Children.Add(previous);arrows.Children.Add(next);
        void UpdateArrows(){previous.IsEnabled=scroll.HorizontalOffset>1;next.IsEnabled=scroll.HorizontalOffset<scroll.ScrollableWidth-1;arrows.Visibility=scroll.ScrollableWidth>1?Visibility.Visible:Visibility.Collapsed;}
        scroll.ScrollChanged+=(_,_)=>UpdateArrows();scroll.Loaded+=(_,_)=>UpdateArrows();
        scroll.PreviewMouseWheel+=(_,e)=>{if(System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)){scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-e.Delta);e.Handled=true;}};
        container.Children.Add(title);container.Children.Add(scroll);return container;
    }
    void RenderEpisodeAvailability(VideoItem item)
    {
        var schedule=store.State.EpisodeSchedules.GetValueOrDefault(item.GroupKey);if(schedule==null) return;
        var now=DateTimeOffset.UtcNow;var upcoming=schedule.Where(e=>e.AirsAt>now).OrderBy(e=>e.AirsAt).FirstOrDefault();
        if(upcoming!=null) ContentPanel.Children.Add(Body($"Next airing · S{upcoming.Season:00} E{upcoming.Episode:00} · {upcoming.AirsAt!.Value.ToLocalTime():ddd, d MMM yyyy}",13));
        var missing=store.MissingEpisodes(item.GroupKey,now);if(missing.Count==0) return;
        var rows=new StackPanel();rows.Children.Add(Body("Released episodes that are not in your local folders. Dates and numbering come from TVmaze; specials are excluded.",12));
        foreach(var ep in missing)
        {
            var existing=store.Archive.FirstOrDefault(x=>x.GroupKey==item.GroupKey&&x.Season==ep.Season&&x.Episode==ep.Episode);
            rows.Children.Add(Body($"S{ep.Season:00} · E{ep.Episode:00}"+(!store.State.HideSpoilers?" · "+ep.Title:"")+(existing!=null&&store.EverWatched(existing)?" · Watched · File unavailable":" · Not in folder"),12));
        }
        ContentPanel.Children.Add(new Expander {Header=$"{missing.Count} released episodes not in your folders",Content=rows,Margin=new(0,14,0,2),Foreground=Brush("#AAA6A0")});
    }
    Window DiaryWindow(VideoItem item)
    {
        var dialog=new Window {Title="Viewing diary · "+item.Title,Width=700,Height=680,MinWidth=550,MinHeight=430,Owner=this,ShowActivated=!validationMode,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#0D0D0F"),Foreground=Brush("#F4F1EB")};
        var panel=new StackPanel{Margin=new(28)};panel.Children.Add(Text("Your viewing diary",24,weight:FontWeights.SemiBold));panel.Children.Add(Body(item.Label));
        var personal=store.State.PersonalReviews.GetValueOrDefault(item.Id)??new();
        panel.Children.Add(Heading("My rating & note","Private, saved on this PC."));
        var rating=personal.Rating;var stars=new WrapPanel();
        void DrawStars(){stars.Children.Clear();for(int i=1;i<=5;i++){var value=i;var button=ActionButton(i<=rating?"★":"☆",()=>{rating=value;DrawStars();});button.FontSize=24;button.Foreground=Brush("#E4C084");button.Padding=new(9,3,9,3);button.Margin=new(0,0,6,0);button.ToolTip=value+" out of 5";stars.Children.Add(button);}stars.Children.Add(ActionButton("Clear",()=>{rating=0;DrawStars();}));}
        DrawStars();panel.Children.Add(stars);
        var note=new TextBox{Text=personal.Note,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=84,MaxHeight=160,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new(0,14,0,12),MaxLength=4000};System.Windows.Automation.AutomationProperties.SetName(note,"My private note");panel.Children.Add(note);
        var saved=Body("",12);panel.Children.Add(ActionButton("Save rating & note",()=>{store.State.PersonalReviews[item.Id]=new(){Rating=rating,Note=note.Text};store.TouchMobileField("review:"+item.Id);store.Save();saved.Text="Saved on this PC.";},true));panel.Children.Add(saved);
        panel.Children.Add(Heading("Viewing dates","Earlier dates stay here when you replay or remove the file."));
        var records=store.Archive.Where(x=>x.GroupKey==item.GroupKey).SelectMany(x=>store.ViewingHistory(x).Select(v=>(Item:x,View:v))).OrderByDescending(x=>x.View.WatchedAt).ToList();
        if(records.Count==0) panel.Children.Add(Body("No completed viewings yet. Your saved position is kept separately."));
        foreach(var record in records)
        {
            var label=(record.Item.IsMovie?record.Item.Title:record.Item.EpisodeLabel)+" · "+record.View.WatchedAt.ToLocalTime().ToString("d MMM yyyy, HH:mm")+" · "+record.View.Source;
            var row=ActionButton(label,()=>{dialog.Close();OpenEpisodeInfo(record.Item);});row.Background=Brush("#171719");row.Margin=new(0,0,0,8);row.HorizontalAlignment=HorizontalAlignment.Stretch;panel.Children.Add(row);
        }
        dialog.Content=new ScrollViewer{Content=panel};return dialog;
    }
    void OpenDiary(VideoItem item) => DiaryWindow(item).Show();
}
