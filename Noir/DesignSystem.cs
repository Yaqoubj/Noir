using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Noir;
// Shared building blocks for the browsing pages: horizontal rails, hover-lift cards and the
// actionable "Latest" cards used by History.
public partial class MainWindow
{
    // A titled horizontal row with arrow buttons, Shift+wheel scrolling and an optional "See all".
    FrameworkElement Rail(string title,string subtitle,IEnumerable<FrameworkElement> cards,Action? seeAll=null,string seeAllLabel="See all")
    {
        var container=new StackPanel{Margin=new(0,8,0,4)};var header=new DockPanel{Margin=new(0,22,0,14)};
        var arrows=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom};DockPanel.SetDock(arrows,Dock.Right);header.Children.Add(arrows);
        var heading=new StackPanel();heading.Children.Add(Text(title,21,weight:FontWeights.SemiBold));if(subtitle.Length>0){var s=Body(subtitle,12);s.Margin=new(0,3,0,0);heading.Children.Add(s);}header.Children.Add(heading);
        var row=new StackPanel{Orientation=Orientation.Horizontal};foreach(var card in cards)row.Children.Add(card);
        if(row.Children.Count==0) return container;
        var scroll=new ScrollViewer{Content=row,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,PanningMode=PanningMode.HorizontalOnly,Padding=new(0,6,0,6),Margin=new(-6,-6,-6,0)};
        row.Margin=new(6,0,6,0);
        Button Arrow(string glyph,string tip,int direction)
        {
            var b=ActionButton(glyph,()=>scroll.ScrollToHorizontalOffset(Math.Clamp(scroll.HorizontalOffset+direction*Math.Max(300,scroll.ViewportWidth*.8),0,scroll.ScrollableWidth)));
            b.ToolTip=tip;b.FontSize=20;b.Width=38;b.Height=34;b.Padding=new(0);b.Margin=new(8,0,0,0);b.Background=Brush("#1C1C1F");return b;
        }
        if(seeAll!=null){var all=ActionButton(seeAllLabel,seeAll);all.Padding=new(14,7,14,7);all.Background=Brush("Transparent");all.Foreground=Brush("#E4C084");arrows.Children.Add(all);}
        var previous=Arrow("‹","Previous",-1);var next=Arrow("›","More",1);arrows.Children.Add(previous);arrows.Children.Add(next);
        void Update(){previous.IsEnabled=scroll.HorizontalOffset>1;next.IsEnabled=scroll.HorizontalOffset<scroll.ScrollableWidth-1;previous.Visibility=next.Visibility=scroll.ScrollableWidth>1?Visibility.Visible:Visibility.Collapsed;}
        scroll.ScrollChanged+=(_,_)=>Update();scroll.Loaded+=(_,_)=>Update();
        scroll.PreviewMouseWheel+=(_,e)=>{if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-e.Delta);e.Handled=true;}};
        container.Children.Add(header);container.Children.Add(scroll);return container;
    }
    // Cards lift slightly and gain a gold edge on hover or keyboard focus.
    static FrameworkElement Lift(FrameworkElement card,Border? edge=null)
    {
        var scale=new ScaleTransform(1,1);card.RenderTransform=scale;card.RenderTransformOrigin=new(.5,.5);
        void To(double value,bool on)
        {
            var animation=new DoubleAnimation(value,TimeSpan.FromMilliseconds(140)){EasingFunction=new QuadraticEase()};
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,animation);scale.BeginAnimation(ScaleTransform.ScaleYProperty,animation);
            if(edge!=null)edge.BorderBrush=Brush(on?"#E4C084":"#00000000");
        }
        card.MouseEnter+=(_,_)=>To(1.035,true);card.MouseLeave+=(_,_)=>To(1,false);
        card.IsKeyboardFocusWithinChanged+=(_,e)=>To((bool)e.NewValue?1.035:1,(bool)e.NewValue);
        return card;
    }
    FrameworkElement PosterCard(List<VideoItem> group)
    {
        var item=group[0];var panel=new StackPanel{Width=176,Margin=new(0,0,18,8)};
        var picture=new Grid{Height=264,ClipToBounds=true};
        var poster=ActionButton("",()=>OpenDetails(group));poster.Content=Art(item.Poster,item.Title,264,true);poster.Padding=new(0);poster.HorizontalAlignment=HorizontalAlignment.Stretch;
        poster.ToolTip=item.Title+" · Details & episodes";System.Windows.Automation.AutomationProperties.SetName(poster,item.Title+" details");picture.Children.Add(poster);
        var watched=group.Count(i=>(item.IsMovie||i.Season>0)&&store.EverWatched(i));var total=group.Count(i=>item.IsMovie||i.Season>0);
        if(watched>0&&!item.IsMovie)picture.Children.Add(new ProgressBar{Value=watched,Maximum=Math.Max(1,total),Height=4,VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false,Background=Brush("#99000000")});
        if(item.IsMovie&&store.EverWatched(item))picture.Children.Add(new Border{IsHitTestVisible=false,Background=Brush("#E6101012"),CornerRadius=new(6),Padding=new(7,3,7,3),Margin=new(8),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,Child=Text("✓ Watched",10,"#E4C084",FontWeights.SemiBold)});
        picture.Children.Add(CardMenu(item));
        var edge=new Border{CornerRadius=new(10),ClipToBounds=true,Child=picture,BorderThickness=new(1.5),BorderBrush=Brush("#00000000")};panel.Children.Add(edge);
        var name=Text(item.Title,14,weight:FontWeights.SemiBold);name.Margin=new(0,9,0,2);name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextWrapping=TextWrapping.NoWrap;panel.Children.Add(name);
        var meta=item.IsMovie?(item.Year>0?item.Year.ToString():"Movie"):(watched>0?$"{watched} / {total} watched":$"{total} episodes");
        if(!group.Any(CanPlay))meta+=" · Not on PC";
        var line=Text(meta,11,"#9F9A93");line.TextTrimming=TextTrimming.CharacterEllipsis;line.TextWrapping=TextWrapping.NoWrap;panel.Children.Add(line);
        panel.ContextMenu=ItemMenu(item);return Lift(panel,edge);
    }
    void RailCatalog(List<List<VideoItem>> groups,string title,string subtitle="",Action? seeAll=null,int limit=30)
    {
        if(groups.Count==0) return;
        ContentPanel.Children.Add(Rail(title,subtitle,groups.Take(limit).Select(PosterCard).ToList(),seeAll));
    }
    static string Ago(DateTimeOffset at)
    {
        var span=DateTimeOffset.Now-at;
        if(span.TotalMinutes<2)return "just now";if(span.TotalHours<1)return $"{(int)span.TotalMinutes} min ago";if(span.TotalHours<24)return $"{(int)span.TotalHours} h ago";
        if(span.TotalDays<2)return "yesterday";if(span.TotalDays<7)return at.ToLocalTime().ToString("dddd");return at.ToLocalTime().ToString("d MMM");
    }
    // History → Latest: the last things you watched, each with the useful next step on the card.
    void RenderLatest()
    {
        var latest=store.Archive.Select(i=>(Item:i,At:HistoryDate(i))).Where(x=>x.At>DateTimeOffset.MinValue)
            .GroupBy(x=>x.Item.GroupKey).Select(g=>g.OrderByDescending(x=>x.At).First()).OrderByDescending(x=>x.At).Take(14).ToList();
        if(latest.Count==0) return;
        ContentPanel.Children.Add(Rail("Latest","What you watched most recently, with the next step one click away.",latest.Select(x=>LatestCard(x.Item,x.At)).ToList()));
    }
    FrameworkElement LatestCard(VideoItem item,DateTimeOffset at)
    {
        var panel=new StackPanel{Width=300,Margin=new(0,0,18,8)};
        var picture=new Grid{Height=168,ClipToBounds=true};
        var spoilerFree=!store.State.HideSpoilers||store.EverWatched(item);
        picture.Children.Add(Art(spoilerFree&&System.IO.File.Exists(item.EpisodeImage)?item.EpisodeImage:System.IO.File.Exists(item.Backdrop)?item.Backdrop:item.Poster,item.Title,168));
        var shade=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1)};shade.GradientStops.Add(new(Color.FromArgb(0,0,0,0),.35));shade.GradientStops.Add(new(Color.FromArgb(235,10,10,12),1));
        picture.Children.Add(new Border{Background=shade,IsHitTestVisible=false});
        var caption=new StackPanel{VerticalAlignment=VerticalAlignment.Bottom,Margin=new(14,0,14,12),IsHitTestVisible=false};
        var t=Text(item.Title,16,weight:FontWeights.SemiBold);t.TextTrimming=TextTrimming.CharacterEllipsis;t.TextWrapping=TextWrapping.NoWrap;caption.Children.Add(t);
        var bookmark=store.GetProgress(item);var finished=bookmark?.Completed==true||store.EverWatched(item)&&bookmark is not {Position:>0};
        var label=(item.IsMovie?"Movie":item.EpisodeLabel+(item.EpisodeTitle.Length>0&&spoilerFree?" · "+item.EpisodeTitle:""));
        var sub=Text(label,11,"#D9D4CC");sub.TextTrimming=TextTrimming.CharacterEllipsis;sub.TextWrapping=TextWrapping.NoWrap;caption.Children.Add(sub);
        picture.Children.Add(caption);
        picture.Children.Add(new Border{IsHitTestVisible=false,Background=Brush("#D9101012"),CornerRadius=new(6),Padding=new(8,3,8,3),Margin=new(10),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,
            Child=Text((finished?"✓ Watched · ":bookmark?.Position>=1?"Stopped at "+LibraryStore.Time(bookmark.Position)+" · ":"Started · ")+Ago(at),10,"#E4C084",FontWeights.SemiBold)});
        if(bookmark is {Completed:false,Position:>0,Duration:>0})picture.Children.Add(new ProgressBar{Value=bookmark.Position,Maximum=bookmark.Duration,Height=4,VerticalAlignment=VerticalAlignment.Bottom,Background=Brush("#88000000"),IsHitTestVisible=false});
        picture.Children.Add(CardMenu(item));
        var open=new Button{Content=picture,Padding=new(0),Background=Brush("Transparent"),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip="Open "+item.Title};open.Click+=(_,_)=>OpenDetails(store.EpisodesFor(item.GroupKey));
        System.Windows.Automation.AutomationProperties.SetName(open,"Latest "+item.Label);
        var edge=new Border{CornerRadius=new(11),ClipToBounds=true,Child=open,BorderThickness=new(1.5),BorderBrush=Brush("#00000000")};panel.Children.Add(edge);
        // The main action depends on where you are with this title.
        var actions=new WrapPanel{Margin=new(0,10,0,0)};
        Button Action(string text,Action act,bool primary=false){var b=ActionButton(text,act,primary);b.Padding=new(12,7,12,7);b.FontSize=12.5;b.Margin=new(0,0,8,6);actions.Children.Add(b);return b;}
        if(!finished&&CanPlay(item)) Action("▶  Resume",()=>_=PlayVideo(item),true);
        else if(!finished) Action("↓  Get it again",()=>FindDownloads(item),true);
        else if(!item.IsMovie&&store.UpNextFor(store.EpisodesFor(item.GroupKey)) is {} up)
        {
            if(up.Kind is UpNextKind.Ready or UpNextKind.Resume) Action("▶  "+up.Item.EpisodeLabel.Replace(" · ",""),()=>_=PlayVideo(up.Item),true);
            else if(up.Kind==UpNextKind.NotOnPc) Action(ActiveDownload(up.Item)!=null?"Downloading "+up.Item.EpisodeLabel.Replace(" · ",""):"↓  Get "+up.Item.EpisodeLabel.Replace(" · ",""),()=>{if(ActiveDownload(up.Item)!=null)Navigate("Downloads");else FindDownloads(up.Item);},true);
            else Action("Airs "+(up.AirsAt?.ToLocalTime().ToString("ddd d MMM")??"soon"),()=>OpenDetails(store.EpisodesFor(item.GroupKey)));
        }
        if(finished&&CanPlay(item)) Action("↻",()=>_=PlayVideo(item,true)).ToolTip="Watch again from the start";
        var rating=store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Rating??0;
        Action(rating>0?new string('★',rating):"☆ Rate",()=>OpenDiary(item)).ToolTip="Your rating and note";
        Action("💬",()=>OpenReviews(item)).ToolTip="Reviews & comments";
        panel.Children.Add(actions);panel.ContextMenu=ItemMenu(item);
        return Lift(panel,edge);
    }
}
