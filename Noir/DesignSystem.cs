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
    Popup? hoverCard;
    FrameworkElement? hoverOwner;
    bool hoverEvents;
    // The hover card is already under the pointer, so start the dwell without waiting for a fresh MouseEnter.
    void HoverPreview(Border media,VideoItem item){PreviewHost.Attach(media,item,700);media.Dispatcher.BeginInvoke(()=>{if(media.IsMouseOver)media.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});},System.Windows.Threading.DispatcherPriority.Loaded);}
    string TileStatus(VideoItem item)=>StatusLabels.For((store.State.EpisodeSchedules.GetValueOrDefault(item.GroupKey)??[]).Select(e=>new StatusEpisode(e.Season,e.Episode,e.AirsAt)),DateTimeOffset.UtcNow,item.ImdbId.Length>0?topTen?.FindIndex(t=>t.Id==item.ImdbId)+1??0:0);
    static string StatusText(string label)=>label switch{"New season"=>T("New season","موسم جديد"),"New episode"=>T("New episode","حلقة جديدة"),"Finale"=>T("Finale","ختام الموسم"),"Still planned"=>T("Still planned","ما زال ضمن الخطط"),_=>label.StartsWith("#")?T("{0} this week","{0} هذا الأسبوع",label.Split(' ')[0]):T(label)};
    void AddStatusPill(Grid picture,VideoItem item)
    {
        var label=TileStatus(item);if(label.Length==0)return;picture.Children.Add(new Border{Background=Brush("#E4C084"),CornerRadius=new(5),Padding=new(7,3,7,3),Margin=new(8),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false,Child=Text(StatusText(label),10,"#16120C",FontWeights.SemiBold)});
    }
    void CloseHoverCard(){if(hoverCard!=null){hoverCard.IsOpen=false;hoverCard.Child=null;hoverCard=null;}hoverOwner=null;}
    FrameworkElement HoverCard(FrameworkElement card,List<VideoItem> group,FrameworkElement? anchor=null)
    {
        anchor??=card;var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};var leave=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(160)};
        if(!hoverEvents){hoverEvents=true;ContentScroll.ScrollChanged+=(_,e)=>{if(e.VerticalChange!=0||e.HorizontalChange!=0)CloseHoverCard();};Deactivated+=(_,_)=>CloseHoverCard();PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&hoverCard!=null){var owner=hoverOwner;CloseHoverCard();owner?.Focus();e.Handled=true;}};}
        void Start(){leave.Stop();timer.Stop();if(hoverOwner!=card)timer.Start();}
        void Leave(){timer.Stop();leave.Start();}
        timer.Tick+=(_,_)=>{timer.Stop();if(card.IsLoaded&&(card.IsMouseOver||card.IsKeyboardFocusWithin))ShowHoverCard(card,anchor,group);};
        leave.Tick+=(_,_)=>{leave.Stop();if(hoverOwner==card&&hoverCard?.Child is UIElement content&&!content.IsMouseOver&&!content.IsKeyboardFocusWithin)CloseHoverCard();};
        anchor.MouseEnter+=(_,_)=>Start();anchor.MouseLeave+=(_,_)=>Leave();card.IsKeyboardFocusWithinChanged+=(_,_)=>{if(card.IsKeyboardFocusWithin)Start();else Leave();};card.Unloaded+=(_,_)=>{timer.Stop();leave.Stop();if(hoverOwner==card)CloseHoverCard();};return card;
    }
    void ShowHoverCard(FrameworkElement owner,FrameworkElement anchor,List<VideoItem> group)
    {
        CloseHoverCard();var item=group[0];var next=store.ContinueFor(group)??store.UpNextFor(group)?.Item??item;var width=Math.Clamp(anchor.ActualWidth*1.35,270,450);
        var body=new StackPanel();var media=new Border{Height=width*.56,Background=Brush("#19191C"),Child=Art(System.IO.File.Exists(item.Backdrop)?item.Backdrop:item.Poster,item.Title,width*.56),ClipToBounds=true};body.Children.Add(media);if(Motion.Enabled&&(page!="Home"||detailKey!=null))HoverPreview(media,item);
        var info=new StackPanel{Margin=new(14)};body.Children.Add(info);info.Children.Add(Text(item.Title,17,weight:FontWeights.SemiBold));var label=TileStatus(item);if(label.Length>0){var pill=new Border{HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new(5),Padding=new(7,3,7,3),Margin=new(0,8,0,4),Background=Brush("#33E4C084"),Child=Text(StatusText(label),11,"#E4C084")};info.Children.Add(pill);}
        var facts=new List<string>();if(next.Duration>0)facts.Add(Loc.Runtime(next.Duration));if(item.Year>0)facts.Add(item.Year.ToString());facts.AddRange(item.Genres.Split('·',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Take(3).Select(T));var meta=Text(string.Join(" · ",facts),11,"#BDB6AC");meta.Margin=new(0,6,0,10);info.Children.Add(meta);
        var actions=new WrapPanel();info.Children.Add(actions);void Add(string text,string name,Action action,bool primary=false){var b=ActionButton(text,()=>{CloseHoverCard();action();},primary);b.Padding=new(10,7,10,7);b.FontSize=12;b.Margin=new(0,0,6,6);System.Windows.Automation.AutomationProperties.SetName(b,name);actions.Children.Add(b);}
        Add("▶ "+T(store.GetProgress(next) is {Completed:false,Position:>0}?"Resume":"Play"),T("Play")+" "+item.Title,()=>{if(CanPlay(next))_=PlayVideo(next);else OpenDetails(group);},true);var listed=store.State.Watchlist.Contains(item.GroupKey);Add((listed?"✓ ":"＋ ")+T("My List"),T("My List")+" "+item.Title,()=>ToggleWatchlist(item));var seen=store.EverWatched(next);Add(T("✓ Watched","✓ تمت المشاهدة"),T("Watched")+" "+next.Title,()=>{if(seen)SmartWatchedClick(next);else SmartCheckOff(next,next.Label+" watched");});Add("ⓘ "+T("Details"),T("Details")+" "+item.Title,()=>OpenDetails(group));
        var progress=store.GetProgress(next);var total=progress?.Duration??next.Duration;var watched=group.Count(store.EverWatched);var bar=new ProgressBar{Height=4,Maximum=total>0?total:Math.Max(1,group.Count),Value=total>0?progress?.Position??0:watched,Foreground=Brush("#E4C084"),Background=Brush("#2A2A30"),BorderThickness=new(0),Margin=new(0,8,0,0)};info.Children.Add(bar);
        var border=new Border{Width=width,Background=Brush("#18181B"),BorderBrush=Brush("#88E4C084"),BorderThickness=new(1),CornerRadius=new(12),ClipToBounds=true,Child=body,Focusable=true};System.Windows.Automation.AutomationProperties.SetName(border,T("Title preview","معاينة العنوان")+" "+item.Title);
        hoverOwner=owner;hoverCard=new Popup{Child=border,PlacementTarget=anchor,Placement=PlacementMode.Center,AllowsTransparency=true,StaysOpen=false,IsOpen=true};var active=hoverCard;border.MouseLeave+=(_,_)=>{if(!owner.IsMouseOver&&!border.IsKeyboardFocusWithin&&hoverCard==active)CloseHoverCard();};border.IsKeyboardFocusWithinChanged+=(_,_)=>{if(!border.IsKeyboardFocusWithin&&!border.IsMouseOver&&!owner.IsMouseOver&&hoverCard==active)CloseHoverCard();};active.Closed+=(_,_)=>{if(hoverCard==active){hoverCard=null;hoverOwner=null;}};Motion.FadeIn(border);Motion.Pop(border);if(owner.IsKeyboardFocusWithin&&!owner.IsMouseOver)actions.Children.OfType<Button>().FirstOrDefault()?.Focus();
    }
    // A titled horizontal row with arrow buttons, Shift+wheel scrolling and an optional "See all".
    FrameworkElement Rail(string title,string subtitle,IEnumerable<FrameworkElement> cards,Action? seeAll=null,string seeAllLabel="See all")
    {
        var container=new StackPanel{Margin=new(0,8,0,4)};var header=new DockPanel{Margin=new(0,22,0,14)};
        var arrows=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom};DockPanel.SetDock(arrows,Dock.Right);header.Children.Add(arrows);
        var heading=new StackPanel();heading.Children.Add(Text(T(title),21,weight:FontWeights.SemiBold));if(subtitle.Length>0){var s=Body(subtitle,12);s.Margin=new(0,3,0,0);heading.Children.Add(s);}header.Children.Add(heading);
        var row=new StackPanel{Orientation=Orientation.Horizontal};foreach(var card in cards){row.Children.Add(card);if(page=="Home"&&detailKey==null)WireHomeRailCard(card);}
        if(row.Children.Count==0) return container;
        if(seeAll!=null)
        {
            var tile=ActionButton(T("See all ›","عرض الكل ›"),seeAll);tile.Tag="HomeSeeAllTile";tile.Width=156;tile.MinHeight=76;tile.VerticalAlignment=VerticalAlignment.Stretch;tile.Margin=new(0,0,18,8);tile.Background=Brush("#161618");tile.Foreground=Brush("#E4C084");tile.Template=TileTemplate();System.Windows.Automation.AutomationProperties.SetName(tile,T("See all {0}","عرض كل {0}",T(title)));row.Children.Add(tile);
            row.Loaded+=(_,_)=>{tile.Height=Math.Max(76,row.Children.OfType<FrameworkElement>().Where(c=>c!=tile).Max(c=>c.ActualHeight));};
        }
        var scroll=new ScrollViewer{Content=row,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,PanningMode=PanningMode.HorizontalOnly,Padding=new(0,6,0,6),Margin=new(-6,-6,-6,0)};
        row.Margin=new(6,0,6,0);
        Button Arrow(string glyph,string tip,int direction)
        {
            var b=ActionButton(glyph,()=>scroll.ScrollToHorizontalOffset(Math.Clamp(scroll.HorizontalOffset+direction*Math.Max(300,scroll.ViewportWidth*.8),0,scroll.ScrollableWidth)));
            b.ToolTip=T(tip);System.Windows.Automation.AutomationProperties.SetName(b,T(tip)+" "+T(title));b.FontSize=20;b.Width=38;b.Height=34;b.Padding=new(0);b.Margin=new(8,0,0,0);b.Background=Brush("#1C1C1F");return b;
        }
        if(seeAll!=null){var all=ActionButton(seeAllLabel,seeAll);System.Windows.Automation.AutomationProperties.SetName(all,T("See all {0}","عرض كل {0}",T(title)));all.Padding=new(14,7,14,7);all.Background=Brush("Transparent");all.Foreground=Brush("#E4C084");arrows.Children.Add(all);}
        var previous=Arrow("‹","Previous",-1);var next=Arrow("›","More",1);arrows.Children.Add(previous);arrows.Children.Add(next);
        void Update(){previous.IsEnabled=scroll.HorizontalOffset>1;next.IsEnabled=scroll.HorizontalOffset<scroll.ScrollableWidth-1;previous.Visibility=next.Visibility=scroll.ScrollableWidth>1?Visibility.Visible:Visibility.Collapsed;}
        scroll.ScrollChanged+=(_,_)=>Update();scroll.Loaded+=(_,_)=>Update();
        scroll.PreviewMouseWheel+=(_,e)=>{if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-e.Delta);e.Handled=true;}};
        container.Children.Add(header);container.Children.Add(scroll);StaggerCards(row);return container;
    }
    // Cards lift slightly and gain a gold edge on hover or keyboard focus.
    static FrameworkElement Lift(FrameworkElement card,Border? edge=null)
    {
        var shadow=new System.Windows.Media.Effects.DropShadowEffect{Color=Color.FromRgb(0,0,0),BlurRadius=22,ShadowDepth=5,Opacity=.5};shadow.Freeze();
        void Update(bool? hover=null)
        {
            var on=(hover??card.IsMouseOver)||card.IsKeyboardFocusWithin;Motion.Scale(card,on?1.035:1);
            if(edge!=null){edge.BorderBrush=Brush(on?"#E4C084":"#00000000");edge.Effect=on?shadow:null;}
        }
        card.MouseEnter+=(_,_)=>Update(true);card.MouseLeave+=(_,_)=>Update(false);card.IsKeyboardFocusWithinChanged+=(_,_)=>Update();
        card.Unloaded+=(_,_)=>{if(edge!=null){edge.Effect=null;edge.BorderBrush=Brush("#00000000");}};
        return card;
    }
    int cardEntranceCount;bool animateCards;
    void StaggerCards(Panel panel)
    {
        if(!animateCards||!Motion.Enabled)return;var entered=false;
        panel.Loaded+=(_,_)=>
        {
            if(entered)return;entered=true;var step=0;
            foreach(var card in panel.Children.OfType<FrameworkElement>().Take(10))
            {
                if(cardEntranceCount>=10)break;var bounds=card.TransformToAncestor(ContentScroll).TransformBounds(new Rect(card.RenderSize));
                if(bounds.Right<=0||bounds.Left>=ContentScroll.ViewportWidth||bounds.Bottom<=0||bounds.Top>=ContentScroll.ViewportHeight)continue;
                Motion.EnterOnce(card,step++*35);cardEntranceCount++;
            }
        };
    }
    static void RevealOnFocus(FrameworkElement owner,FrameworkElement reveal)
    {
        void Show(bool on)=>Motion.Opacity(reveal,on?1:0,on?150:120);
        owner.MouseEnter+=(_,_)=>Show(true);owner.MouseLeave+=(_,_)=>Show(owner.IsKeyboardFocusWithin);owner.IsKeyboardFocusWithinChanged+=(_,_)=>Show(owner.IsMouseOver||owner.IsKeyboardFocusWithin);
    }
    FrameworkElement PosterQuickActions(List<VideoItem> group)
    {
        var item=group[0];var overlay=new Grid{Opacity=0,VerticalAlignment=VerticalAlignment.Bottom,Background=new LinearGradientBrush(Colors.Transparent,Color.FromArgb(235,8,8,10),90)};
        var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(10,30,10,12)};overlay.Children.Add(row);
        var next=store.ContinueFor(group)??store.UpNextFor(group)?.Item??item;
        if(CanPlay(next)){var play=ActionButton("▶",()=>_=PlayVideo(next),true);play.ToolTip=T("Play");System.Windows.Automation.AutomationProperties.SetName(play,T("Play")+" "+item.Title);play.Padding=new(13,7,13,7);play.Margin=new(0,0,7,0);row.Children.Add(play);}
        var listed=store.State.Watchlist.Contains(item.GroupKey);var list=ActionButton(listed?"✓":"＋",()=>ToggleWatchlist(item));list.ToolTip=T(listed?"In My List · click to remove":"Add to My List");System.Windows.Automation.AutomationProperties.SetName(list,(string)list.ToolTip+" "+item.Title);list.Padding=new(12,7,12,7);row.Children.Add(list);return overlay;
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
        var quick=PosterQuickActions(group);picture.Children.Add(quick);RevealOnFocus(panel,quick);picture.Children.Add(CardMenu(item));AddStatusPill(picture,item);
        var edge=new Border{CornerRadius=new(10),ClipToBounds=true,Child=picture,BorderThickness=new(1.5),BorderBrush=Brush("#00000000")};panel.Children.Add(edge);
        var name=Text(item.Title,14,weight:FontWeights.SemiBold);name.Margin=new(0,9,0,2);name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextWrapping=TextWrapping.NoWrap;panel.Children.Add(name);
        var meta=item.IsMovie?(item.Year>0?item.Year.ToString():"Movie"):(watched>0?$"{watched} / {total} watched":$"{total} episodes");
        if(!group.Any(CanPlay))meta+=" · Not on PC";
        var line=Text(meta,11,"#9F9A93");line.TextTrimming=TextTrimming.CharacterEllipsis;line.TextWrapping=TextWrapping.NoWrap;panel.Children.Add(line);
        if(page=="Home"&&detailKey==null&&!homePosterTitles){name.Visibility=Visibility.Collapsed;line.Visibility=Visibility.Collapsed;}
        LazyMenu(panel,item);return FollowHomeHero(HoverCard(Lift(panel,edge),group,edge),()=>item);
    }
    void RailCatalog(List<List<VideoItem>> groups,string title,string subtitle="",Action? seeAll=null,int limit=30)
    {
        if(groups.Count==0) return;
        ContentPanel.Children.Add(Rail(title,subtitle,groups.Take(limit).Select(PosterCard).ToList(),seeAll));
    }
    static string Ago(DateTimeOffset at)
    {
        var span=DateTimeOffset.Now-at;
        if(Loc.Arabic)
        {
            if(span.TotalMinutes<2)return "الآن";if(span.TotalHours<1)return $"منذ {(int)span.TotalMinutes} د";if(span.TotalHours<24)return $"منذ {(int)span.TotalHours} س";
            if(span.TotalDays<2)return "أمس";return at.ToLocalTime().ToString(span.TotalDays<7?"dddd":"d MMM",Loc.Culture);
        }
        if(span.TotalMinutes<2)return "just now";if(span.TotalHours<1)return $"{(int)span.TotalMinutes} min ago";if(span.TotalHours<24)return $"{(int)span.TotalHours} h ago";
        if(span.TotalDays<2)return "yesterday";if(span.TotalDays<7)return at.ToLocalTime().ToString("dddd");return at.ToLocalTime().ToString("d MMM");
    }
    // History → Latest: the last things you watched, each with the useful next step on the card.
    void RenderLatest()
    {
        var latest=store.Archive.Select(i=>(Item:i,At:HistoryDate(i))).Where(x=>x.At>DateTimeOffset.MinValue)
            .GroupBy(x=>x.Item.GroupKey).Select(g=>g.OrderByDescending(x=>x.At).First()).OrderByDescending(x=>x.At).Take(14).ToList();
        if(latest.Count==0) return;
        ContentPanel.Children.Add(Rail("Recently watched","The last things you watched, with the next step one click away.",latest.Select(x=>LatestCard(x.Item,x.At)).ToList()));
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
        panel.Children.Add(actions);LazyMenu(panel,item);
        return Lift(panel,edge);
    }
}
