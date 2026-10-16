using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Noir;
// Title page chrome, calendar cards and other always-visible replacements for menus and drawers.
public partial class MainWindow
{
    FrameworkElement UnderlineTabs(string[] tabs,string selected,Action<string> choose)
    {
        var bar=new Grid{Margin=new(0,18,0,6)};
        bar.Children.Add(new Border{BorderBrush=Brush("#26FFFFFF"),BorderThickness=new(0,0,0,1),VerticalAlignment=VerticalAlignment.Bottom});
        var row=new StackPanel{Orientation=Orientation.Horizontal};bar.Children.Add(row);
        foreach(var tab in tabs)
        {
            var b=new Button{Content=tab,Style=(Style)FindResource("TopNav"),FontSize=18,Padding=new(4,10,4,12),Margin=new(0,0,30,0),Tag=tab==selected?"Selected":null};
            var value=tab;b.Click+=(_,_)=>choose(value);row.Children.Add(b);
        }
        return bar;
    }
    // Labelled icon buttons under the hero; the rarely used items stay in the final menu.
    FrameworkElement DetailActions(VideoItem item,List<VideoItem> group,Button back)
    {
        var row=new WrapPanel{Margin=new(0,0,0,4)};
        Button Tile(string glyph,string label,Action action,string? tip=null)
        {
            var stack=new StackPanel{Orientation=Orientation.Horizontal};stack.Children.Add(new TextBlock{Text=glyph,FontSize=16,Margin=new(0,0,9,0),VerticalAlignment=VerticalAlignment.Center,Foreground=Brush("#E4C084")});stack.Children.Add(Text(label,13.5,"#E9E4DC"));
            var b=new Button{Content=stack,Background=Brush("#1A1A1D"),Padding=new(16,10,18,10),Margin=new(0,0,10,10),ToolTip=tip??label};b.Click+=(_,_)=>action();
            System.Windows.Automation.AutomationProperties.SetName(b,label);row.Children.Add(b);return b;
        }
        var season=group.Where(i=>i.Season==(detailSeason??item.Season)).ToList();
        if(item.IsMovie)
        {
            if(!CanPlay(item))Tile("↓","Download",()=>FindDownloads(item));
            Tile(store.EverWatched(item)?"✓":"○",store.EverWatched(item)?"Watched":"Mark watched",()=>{store.Mark(item,!store.EverWatched(item));Render();});
        }
        else
        {
            Tile("↓","Download season",()=>FindDownloads(season.FirstOrDefault()??item,true),"Find this season's episodes for TorBox");
            var allSeen=season.Count>0&&season.All(store.EverWatched);
            Tile(allSeen?"✓":"○",allSeen?"Season watched":"Mark season watched",()=>{foreach(var e in season.Where(e=>e.Available||store.EverWatched(e)||store.GetProgress(e)!=null))store.Mark(e,!allSeen);Render();});
        }
        var status=store.TitleStatus(group);
        var statusButton=Tile("◐",status,()=>{},"Viewing status");statusButton.Click+=(_,_)=>{var menu=new ContextMenu();foreach(var s in LibraryStore.Statuses){var v=s;var m=new MenuItem{Header=s,IsCheckable=true,IsChecked=store.State.TitleStatuses.GetValueOrDefault(item.GroupKey,"Automatic")==s};m.Click+=(_,_)=>{store.SetStatus(item,v);Render();};menu.Items.Add(m);}menu.PlacementTarget=statusButton;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;};
        Tile("▤","Collections",()=>CollectionDialog(item));
        Tile("✎","Diary",()=>OpenDiary(item),"Viewing dates, rating and private note");
        var more=Tile("⋯","More",()=>{});more.Click+=(_,_)=>{var menu=ItemMenu(item);menu.PlacementTarget=more;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;};
        var wrap=new DockPanel();DockPanel.SetDock(back,Dock.Top);wrap.Children.Add(back);wrap.Children.Add(row);return wrap;
    }
    static string When(DateTimeOffset at)
    {
        var days=(at.ToLocalTime().Date-DateTime.Today).Days;
        return days<=0?"Tonight":days==1?"Tomorrow":days<7?"In "+days+" days":at.ToLocalTime().ToString("ddd d MMM");
    }
    // A calendar-style card for an announced episode: big date block, artwork and a countdown.
    FrameworkElement CalendarCard(List<VideoItem> group,KnownEpisode ep)
    {
        var show=group[0];var at=ep.AirsAt!.Value.ToLocalTime();var panel=new StackPanel{Width=300,Margin=new(0,0,18,8)};
        var picture=new Grid{Height=168,ClipToBounds=true};
        var spoilerFree=!store.State.HideSpoilers&&System.IO.File.Exists(ep.Image);
        picture.Children.Add(Art(spoilerFree?ep.Image:System.IO.File.Exists(show.Backdrop)?show.Backdrop:show.Poster,show.Title,168));
        var shade=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1)};shade.GradientStops.Add(new(Color.FromArgb(40,0,0,0),0));shade.GradientStops.Add(new(Color.FromArgb(235,10,10,12),1));
        picture.Children.Add(new Border{Background=shade,IsHitTestVisible=false});
        var date=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center};
        date.Children.Add(new TextBlock{Text=at.ToString("MMM").ToUpperInvariant(),FontSize=10,FontWeight=FontWeights.Bold,Foreground=Brush("#E4C084"),HorizontalAlignment=HorizontalAlignment.Center});
        date.Children.Add(new TextBlock{Text=at.Day.ToString(),FontSize=26,FontWeight=FontWeights.Bold,Foreground=Brush("#F4F1EB"),FontFamily=new FontFamily("Segoe UI Variable Display, Segoe UI"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,-4,0,-4)});
        date.Children.Add(new TextBlock{Text=at.ToString("ddd").ToUpperInvariant(),FontSize=10,Foreground=Brush("#AAA6A0"),HorizontalAlignment=HorizontalAlignment.Center});
        picture.Children.Add(new Border{Background=Brush("#E6101012"),BorderBrush=Brush("#44E4C084"),BorderThickness=new(1),CornerRadius=new(10),Padding=new(12,7,12,7),Margin=new(12),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Child=date,IsHitTestVisible=false});
        picture.Children.Add(new Border{Background=Brush("#E4C084"),CornerRadius=new(6),Padding=new(9,3,9,3),Margin=new(12),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false,Child=Text(When(ep.AirsAt.Value),11,"#16120C",FontWeights.Bold)});
        var caption=new StackPanel{VerticalAlignment=VerticalAlignment.Bottom,Margin=new(14,0,14,12),IsHitTestVisible=false};
        var t=Text(show.Title,16,weight:FontWeights.SemiBold);t.TextTrimming=TextTrimming.CharacterEllipsis;t.TextWrapping=TextWrapping.NoWrap;caption.Children.Add(t);
        caption.Children.Add(Text($"S{ep.Season:00} · E{ep.Episode:00}"+(ep.Title.Length>0&&!store.State.HideSpoilers?" · "+ep.Title:"")+" · "+at.ToString("HH:mm"),11,"#D9D4CC"));picture.Children.Add(caption);
        var open=new Button{Content=picture,Padding=new(0),Background=Brush("Transparent"),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip=show.Title+" · Details"};open.Click+=(_,_)=>OpenDetails(group);
        var edge=new Border{CornerRadius=new(11),ClipToBounds=true,Child=open,BorderThickness=new(1.5),BorderBrush=Brush("#00000000")};panel.Children.Add(edge);
        return Lift(panel,edge);
    }
    void RenderComingUp(List<List<VideoItem>> groups)
    {
        var upcoming=store.State.EpisodeSchedules.SelectMany(g=>g.Value.Where(e=>e.Season>0&&e.AirsAt>DateTimeOffset.UtcNow&&e.AirsAt<DateTimeOffset.UtcNow.AddDays(45)).Select(e=>(Group:g.Key,Episode:e)))
            .GroupBy(x=>x.Group).Select(x=>x.OrderBy(e=>e.Episode.AirsAt).First()).OrderBy(x=>x.Episode.AirsAt).Take(12).ToList();
        var cards=new List<FrameworkElement>();
        foreach(var x in upcoming){var g=groups.FirstOrDefault(g=>g[0].GroupKey==x.Group);if(g!=null)cards.Add(CalendarCard(g,x.Episode));}
        if(cards.Count>0)ContentPanel.Children.Add(Rail("Coming up","The next announced episode of each show in your library.",cards));
    }
    // Released episodes missing from your folders, as a row with a download on each card.
    void RenderMissingRail(VideoItem show)
    {
        var missing=store.MissingEpisodes(show.GroupKey,DateTimeOffset.UtcNow);if(missing.Count==0) return;
        var all=store.EpisodesFor(show.GroupKey);var cards=new List<FrameworkElement>();
        foreach(var ep in missing.Take(40))
        {
            var item=all.FirstOrDefault(i=>i.Season==ep.Season&&i.Episode==ep.Episode);if(item==null)continue;
            var panel=new StackPanel{Width=240,Margin=new(0,0,14,8)};var picture=new Grid{Height=135,ClipToBounds=true};
            picture.Children.Add(Art(!store.State.HideSpoilers&&System.IO.File.Exists(ep.Image)?ep.Image:System.IO.File.Exists(show.Backdrop)?show.Backdrop:show.Poster,show.Title,135));
            picture.Children.Add(new Border{Background=Brush("#99000000")});
            var watched=store.EverWatched(item);
            picture.Children.Add(new Border{Background=Brush("#E6101012"),CornerRadius=new(6),Padding=new(8,3,8,3),Margin=new(10),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Child=Text(watched?"✓ Watched · file gone":"Not on this PC",10,"#E4C084",FontWeights.SemiBold)});
            var get=ActionButton(ActiveDownload(item)!=null?"Downloading…":"↓  Get",()=>{if(ActiveDownload(item)!=null)Navigate("Downloads");else FindDownloads(item);},true);get.HorizontalAlignment=HorizontalAlignment.Center;get.VerticalAlignment=VerticalAlignment.Center;picture.Children.Add(get);
            panel.Children.Add(new Border{CornerRadius=new(10),ClipToBounds=true,Child=picture});
            var label=Text($"S{ep.Season:00} · E{ep.Episode:00}"+(!store.State.HideSpoilers&&ep.Title.Length>0?" · "+ep.Title:""),12.5,"#E9E4DC",FontWeights.SemiBold);label.Margin=new(0,8,0,0);label.TextTrimming=TextTrimming.CharacterEllipsis;label.TextWrapping=TextWrapping.NoWrap;panel.Children.Add(label);
            if(ep.AirsAt is {} aired)panel.Children.Add(Text("Aired "+aired.ToLocalTime().ToString("d MMM yyyy"),11,"#9F9A93"));
            cards.Add(panel);
        }
        ContentPanel.Children.Add(Rail($"{missing.Count} released episode{(missing.Count==1?"":"s")} not on this PC","Dates and numbering from TVmaze. Specials are excluded.",cards,missing.Count>1?()=>FindDownloads(all.First(i=>i.Season==missing[0].Season),true):null,"↓  Download season"));
    }
}
