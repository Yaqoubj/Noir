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
            var b=new Button{Content=T(tab),Style=(Style)FindResource("TopNav"),FontSize=Density.Pick(15,18),Padding=Density.Compact?new(4,8,4,10):new(4,10,4,12),Margin=new(0,0,30,0),Tag=tab==selected?"Selected":null};
            var value=tab;b.Click+=(_,_)=>choose(value);row.Children.Add(b);
        }
        return bar;
    }
    // Labelled icon buttons under the hero; the rarely used items stay in the final menu.
    // U27: this is the title page's one action row. RenderDetail puts it inside the hero (back is then null and placed by the hero); items that do not fit fold into More.
    FrameworkElement DetailActions(VideoItem item,List<VideoItem> group,Button? back)
    {
        var row=new FoldRow{Margin=new(0,0,0,4)};
        Button Tile(string glyph,string label,Action action,string? tip=null,bool primary=false){var b=DetailTile(glyph,label,action,tip,primary);row.Children.Add(b);return b;}
        var season=group.Where(i=>i.Season==(detailSeason??item.Season)).ToList();
        var extras=new List<(string Header,Action Run)>();
        if(item.IsMovie)
        {
            if(CanPlay(item)){var resume=store.GetProgress(item) is {Completed:false,Position:>0};Tile("[[play]]",resume?"Resume":"Play",()=>{_=PlayVideo(item);},null,true);}
            else{if(StreamOffered)Tile("[[play]]","Stream",()=>StreamItem(item),T("Stream it now through AIOStreams","بث الآن عبر AIOStreams"),true);Tile("[[download]]","Download",()=>FindDownloads(item),null,!StreamOffered);}
            extras.Add((store.EverWatched(item)?"Watched":"Mark watched",()=>{if(store.EverWatched(item))SmartWatchedClick(item);else SmartCheckOff(item,item.Title+" watched");}));
        }
        else
        {
            // Streams the season's next aired episode that isn't on this PC, the first unwatched one if possible.
            var toStream=season.Where(e=>e.Season>0&&!CanPlay(e)&&store.Aired(e,DateTimeOffset.UtcNow)).OrderBy(e=>store.EverWatched(e)).ThenBy(e=>e.Episode).FirstOrDefault();
            var playable=(store.ContinueFor(group) is {} cont&&CanPlay(cont)?cont:null)??(store.UpNextFor(group)?.Item is {} up&&CanPlay(up)?up:null)??season.Where(CanPlay).OrderBy(e=>e.Episode).FirstOrDefault();
            if(playable!=null){var resume=store.GetProgress(playable) is {Completed:false,Position:>0};Tile("[[play]]",T(resume?"Resume":"Play")+" "+EpisodeTag(playable),()=>{_=PlayVideo(playable);},null,true);}
            else if(StreamOffered&&toStream!=null)Tile("[[play]]",T("Stream")+" "+EpisodeTag(toStream),()=>StreamItem(toStream),T("Stream it now through AIOStreams","بث الآن عبر AIOStreams"),true);
            // U26 (P4): nothing to play or stream here: the hero already offers "Get it on this PC" and the next button is "Download season", so no third download button.
            var keeping=downloads!=null&&NoirReadyPolicy.Enabled(downloads.SeasonPreferences(item.Id));
            var downloadTile=Tile("[[download]]",T("Download season","تنزيل الموسم"),()=>{},T("Choose episodes to download on this PC","اختر الحلقات لتنزيلها على هذا الكمبيوتر"),playable==null&&!(StreamOffered&&toStream!=null));
            downloadTile.Click+=(_,_)=>{var menu=new ContextMenu{FlowDirection=LibraryRoot.FlowDirection};var choose=new MenuItem{Header=T("Download…","تنزيل…")};choose.Click+=(_,_)=>FindDownloads(season.FirstOrDefault()??item,true);menu.Items.Add(choose);var keep=new MenuItem{Header=keeping?T("Keeping ready · change","جاهزة · تغيير"):T("Keep ready","إبقاء الحلقات جاهزة"),ToolTip=T("Keep the next episodes downloaded on this PC","إبقاء الحلقات التالية منزّلة على هذا الكمبيوتر")};keep.Click+=(_,_)=>{if(downloads==null)Status.Text="Downloads are starting. Try again shortly.";else ShowKeepReady(season.FirstOrDefault()??item);};menu.Items.Add(keep);menu.PlacementTarget=downloadTile;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;};
            var released=season.Where(e=>e.Season>0&&store.Aired(e,DateTimeOffset.UtcNow)).ToList();var allSeen=released.Count>0&&released.All(store.EverWatched);
            if(released.Count>0)extras.Add((allSeen?"Season watched":"Mark season watched",()=>{if(allSeen)UncheckAll(released,$"Season {released[0].Season} unmarked");else BulkCheckOff(released,$"{item.Title} season {released[0].Season} marked watched");}));
            var whole=ShowEpisodes(item);var wholeSeen=whole.Count>0&&whole.All(store.EverWatched);
            if(whole.Count>0&&!wholeSeen)extras.Add(("Mark show watched",()=>MarkWholeShow(item)));
            if(whole.Count>1)extras.Add(("Watched up to…",()=>UpToPicker(item.Title,whole.Where(i=>i.Season>0).Select(i=>(i.Season,i.Episode)).Distinct().ToList(),(s,e)=>BulkCheckOff(whole.Where(i=>i.Season>0&&(i.Season<s||i.Season==s&&i.Episode<=e)).ToList(),$"{item.Title}: watched up to S{s:00}E{e:00}"))));
        }
        var listed=store.State.Watchlist.Contains(item.GroupKey);
        Tile(listed?"[[check]]":"[[plus]]",listed?T("In My List","في قائمتي"):T("Add to My List","أضف إلى قائمتي"),()=>ToggleWatchlist(item),T(listed?"In My List · click to remove":"Add to My List",listed?"في قائمتي · انقر للإزالة":"أضف إلى قائمتي"));
        Tile(HistoryLiked(item)?"[[heart-filled]]":"[[heart]]",HistoryFavouriteLabel(item),()=>ToggleHistoryFavourite(item));
        if(!item.IsMovie)
        {
            var stars=ShowStars(item.GroupKey);
            var rate=Tile("[[star-filled]]",stars.Length>0?stars:"Rate show",()=>{},T("Your rating for the whole show","تقييمك للمسلسل كله"));rate.Click+=(_,_)=>{var body=SmartPopup(()=>{});body.Children.Add(Text(T("Rate {0}","قيّم {0}",item.Title),15,weight:FontWeights.SemiBold));body.Children.Add(RatingStrip("",item.GroupKey));};
        }
        else{var mine=store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Rating??0;Tile("[[star-filled]]",mine>0?new string('★',mine):"Rate",()=>OpenDiary(item),T("Your rating and note","تقييمك وملاحظتك"));}
        row.Children.Add(TrailerTile(item));
        if(CanPlay(item)&&store.GetProgress(item) is {Completed:false,Position:>=1})extras.Add(("Start over",()=>_=PlayVideo(item,true)));
        // Viewing status, collections and diary are already in ItemMenu, so More shows them once, with the status check marks the old tile had.
        var more=Tile("[[more]]","More",()=>{});row.More=more;more.Click+=(_,_)=>{var menu=ItemMenu(item);var at=0;foreach(var hidden in row.Folded()){var tile=hidden;var h=new MenuItem{Header=tile.Tag as string??""};h.Click+=(_,_)=>tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));menu.Items.Insert(at++,h);}foreach(var extra in extras){var run=extra.Run;var m=new MenuItem{Header=T(extra.Header)};m.Click+=(_,_)=>run();menu.Items.Insert(at++,m);}if(at>0)menu.Items.Insert(at,new Separator());
            var current=store.State.TitleStatuses.GetValueOrDefault(item.GroupKey,"Automatic");foreach(var entry in menu.Items.OfType<MenuItem>().Where(m=>m.Header as string=="Viewing status"))foreach(var option in entry.Items.OfType<MenuItem>()){option.IsCheckable=true;option.IsChecked=option.Header as string==current;}
            menu.PlacementTarget=more;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;};
        if(back==null)return row;
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
        var show=group[0];var at=ep.AirsAt!.Value.ToLocalTime();var panel=new StackPanel{Width=Density.LatestWidth,Margin=new(0,0,18,8)};
        var picture=new Grid{Height=Density.LatestHeight,ClipToBounds=true};
        var spoilerFree=!store.State.HideSpoilers&&System.IO.File.Exists(ep.Image);
        picture.Children.Add(Art(spoilerFree?ep.Image:System.IO.File.Exists(show.Backdrop)?show.Backdrop:show.Poster,show.Title,Density.LatestHeight));
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
            var get=ActionButton(ActiveDownload(item)!=null?"Downloading…":"[[download]]  Get",()=>{if(ActiveDownload(item)!=null)Navigate("Downloads");else FindDownloads(item);},true);get.HorizontalAlignment=HorizontalAlignment.Center;get.VerticalAlignment=VerticalAlignment.Center;var getRow=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};if(StreamOffered&&ActiveDownload(item)==null){var stream=ActionButton("[[play]]  Stream",()=>StreamItem(item),true);stream.Margin=new(0,0,8,0);getRow.Children.Add(stream);}get.HorizontalAlignment=HorizontalAlignment.Left;getRow.Children.Add(get);picture.Children.Add(getRow);
            panel.Children.Add(new Border{CornerRadius=new(10),ClipToBounds=true,Child=picture});
            var label=Text($"S{ep.Season:00} · E{ep.Episode:00}"+(!store.State.HideSpoilers&&ep.Title.Length>0?" · "+ep.Title:""),12.5,"#E9E4DC",FontWeights.SemiBold);label.Margin=new(0,8,0,0);label.TextTrimming=TextTrimming.CharacterEllipsis;label.TextWrapping=TextWrapping.NoWrap;panel.Children.Add(label);
            if(ep.AirsAt is {} aired)panel.Children.Add(Text("Aired "+aired.ToLocalTime().ToString("d MMM yyyy"),11,"#9F9A93"));
            cards.Add(panel);
        }
        ContentPanel.Children.Add(Rail($"{missing.Count} released episode{(missing.Count==1?"":"s")} not on this PC","Dates and numbering from TVmaze. Specials are excluded.",cards,missing.Count>1?()=>FindDownloads(all.First(i=>i.Season==missing[0].Season),true):null,T("[[download]]  Download…","[[download]]  تنزيل…")));
    }
}
