using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Noir;
public partial class MainWindow
{
    string episodeFilter="All episodes",genreFilter="All genres";
    static bool CanPlay(VideoItem item) => item.Available&&System.IO.File.Exists(item.Path);
    DateTimeOffset HistoryDate(VideoItem item) => store.LastActivity(item);
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
        // Shown only while the pointer is over the card (or it has focus), so rows stay clean.
        button.Opacity=0;button.Loaded+=(_,_)=>{if(VisualTreeHelper.GetParent(button) is UIElement host){host.MouseEnter+=(_,_)=>button.Opacity=1;host.MouseLeave+=(_,_)=>{if(!button.IsKeyboardFocused)button.Opacity=0;};}};
        button.GotKeyboardFocus+=(_,_)=>button.Opacity=1;button.LostKeyboardFocus+=(_,_)=>button.Opacity=0;
        return button;
    }
    FrameworkElement ContinueShelf(IEnumerable<UpNext> videos,string label="Continue Watching",string subtitle="Pick up from your saved time, or play the next episode.")
    {
        return Rail(label,subtitle,videos.Select(entry=>HoverCard(Lift(ContinueCard(entry.Item,entry)),store.EpisodesFor(entry.Item.GroupKey))).ToList());
    }
    void RenderEpisodeAvailability(VideoItem item)
    {
        var schedule=store.State.EpisodeSchedules.GetValueOrDefault(item.GroupKey);if(schedule==null) return;
        var now=DateTimeOffset.UtcNow;var upcoming=schedule.Where(e=>e.AirsAt>now).OrderBy(e=>e.AirsAt).FirstOrDefault();
        if(upcoming!=null) ContentPanel.Children.Add(Body($"Next airing · S{upcoming.Season:00} E{upcoming.Episode:00} · {upcoming.AirsAt!.Value.ToLocalTime():ddd, d MMM yyyy}",13));
        RenderMissingRail(item);
    }
    Window DiaryWindow(VideoItem item)
    {
        var dialog=new Window {Title="Viewing diary · "+item.Title,Width=700,Height=680,MinWidth=550,MinHeight=430,Owner=this,ShowActivated=!validationMode,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#0D0D0F"),Foreground=Brush("#F4F1EB")};
        var panel=new StackPanel{Margin=new(28)};panel.Children.Add(Text("Your viewing diary",24,weight:FontWeights.SemiBold));panel.Children.Add(Body(item.Label));
        var personal=store.State.PersonalReviews.GetValueOrDefault(item.Id)??new();
        panel.Children.Add(Heading("My rating & note","Private, saved on this PC."));
        var rating=personal.Rating10;var stars=new WrapPanel();var picker=new Button{Background=Brush("Transparent"),Padding=new(3,0,3,0),ToolTip=T("Rate with half stars · optional","قيّم بنصف نجمة · اختياري")};
        System.Windows.Automation.AutomationProperties.SetName(picker,T("Your rating · optional","تقييمك · اختياري"));
        void DrawStars()=>picker.Content=HalfRating(rating,24);
        picker.PreviewMouseLeftButtonUp+=(_,e)=>{var score=Math.Clamp((int)Math.Ceiling((e.GetPosition(picker).X-3)/26*2),1,10);rating=rating==score?0:score;DrawStars();e.Handled=true;};
        picker.Click+=(_,_)=>{var menu=new ContextMenu();for(int n=0;n<=10;n++){var score=n;var choice=new MenuItem{Header=n==0?T("Clear rating","مسح التقييم"):T("{0:0.#} stars","{0:0.#} نجمة",n/2.0),IsCheckable=true,IsChecked=rating==n};choice.Click+=(_,_)=>{rating=score;DrawStars();};menu.Items.Add(choice);}menu.PlacementTarget=picker;menu.IsOpen=true;};
        DrawStars();stars.Children.Add(picker);stars.Children.Add(ActionButton(T("Clear rating","مسح التقييم"),()=>{rating=0;DrawStars();}));panel.Children.Add(stars);
        var note=new TextBox{Text=personal.Note,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=84,MaxHeight=160,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new(0,14,0,12),MaxLength=4000};System.Windows.Automation.AutomationProperties.SetName(note,"My private note");panel.Children.Add(note);
        var saved=Body("",12);panel.Children.Add(ActionButton("Save rating & note",()=>{store.State.PersonalReviews[item.Id]=new(){Rating10=rating,Note=note.Text};store.TouchMobileField("review:"+item.Id);store.Save();saved.Text="Saved on this PC.";},true));panel.Children.Add(saved);
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
