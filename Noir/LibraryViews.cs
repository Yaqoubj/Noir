using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noir;
public partial class MainWindow
{
    FrameworkElement Hero(VideoItem item,List<VideoItem> group,bool continuing,bool justFinished=false)=>CinematicHero(item,group,continuing,justFinished);
    PcDownloadJob? ActiveDownload(VideoItem item)=>downloads?.Jobs().FirstOrDefault(j=>j.Target.GroupKey==item.GroupKey&&j.Target.Season==item.Season&&j.Target.Episode==item.Episode&&j.Status is "Queued" or "Preparing" or "Downloading" or "Subtitles" or "Paused");
    // Landscape artwork, remaining time and play / remove controls on hover or focus.
    FrameworkElement ContinueCard(VideoItem item,UpNext? next=null)
    {
        var panel=new StackPanel {Width=Density.Pick(248,272),Margin=new(0,0,16,12)};
        var picture=new Grid {Height=Density.Pick(140,153),ClipToBounds=true};
        var kind=next?.Kind??UpNextKind.Ready;var job=kind==UpNextKind.NotOnPc?ActiveDownload(item):null;
        Action open=kind switch
        {
            UpNextKind.Upcoming=>()=>OpenDetails(store.EpisodesFor(item.GroupKey)),
            UpNextKind.NotOnPc when job!=null=>()=>Navigate("Downloads"),
            UpNextKind.NotOnPc when CanStream=>()=>StreamItem(item),
            UpNextKind.NotOnPc=>()=>FindDownloads(item),
            _=>()=>_ = PlayVideo(item)
        };
        var episodeArt=item.EpisodeImage.Length>0&&System.IO.File.Exists(item.EpisodeImage)&&(!store.State.HideSpoilers||kind==UpNextKind.Resume||store.EverWatched(item));
        var image=episodeArt?item.EpisodeImage:System.IO.File.Exists(item.Backdrop)?item.Backdrop:item.Poster;
        var play=ActionButton("",open);play.Content=Art(image,item.Title,Density.Pick(140,153));play.Padding=new(0);play.HorizontalAlignment=HorizontalAlignment.Stretch;
        var verb=kind switch{UpNextKind.Upcoming=>"Details for ",UpNextKind.NotOnPc=>job!=null?"Download progress for ":CanStream?"Stream ":"Download ",UpNextKind.Resume=>"Resume ",_=>"Play "};
        play.ToolTip=verb+item.Label;System.Windows.Automation.AutomationProperties.SetName(play,"Continue "+item.Label);picture.Children.Add(play);
        var shade=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1)};shade.GradientStops.Add(new(Color.FromArgb(0,0,0,0),.35));shade.GradientStops.Add(new(Color.FromArgb(kind is UpNextKind.Upcoming or UpNextKind.NotOnPc?(byte)200:(byte)170,8,8,10),1));
        picture.Children.Add(new Border{IsHitTestVisible=false,Background=shade});
        var bookmark=store.GetProgress(item);
        if(kind is UpNextKind.Upcoming or UpNextKind.NotOnPc){var badge=Badge(kind==UpNextKind.Upcoming?"NEW EPISODE SOON":job!=null?"DOWNLOADING":CanStream?"▶  STREAM":"↓  GET IT ON THIS PC");badge.IsHitTestVisible=false;picture.Children.Add(badge);}
        else
        {
            var circle=PlayCircle();circle.IsHitTestVisible=false;circle.Opacity=0;picture.Children.Add(circle);
            RevealOnFocus(panel,circle);
            // "▶ 44 min left" or "▶ S02 E05" in a small dark pill at the trailing bottom corner.
            var label=bookmark is {Completed:false,Position:>=1,Duration:>0}?Loc.MinutesLeft(Math.Max(1,(int)Math.Ceiling((bookmark.Duration-bookmark.Position)/60))):item.IsMovie?T("Play"):item.EpisodeLabel.Replace(" · "," ");
            var pill=new Border{IsHitTestVisible=false,Background=Brush("#D9101012"),CornerRadius=new(12),Padding=new(9,3,11,4),Margin=new(10),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom};
            var pillRow=new StackPanel{Orientation=Orientation.Horizontal};pillRow.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M0,0 L0,10 L8,5 Z"),Fill=Brush("#F4F1EB"),Width=8,Height=10,Margin=new(0,0,6,0),VerticalAlignment=VerticalAlignment.Center,FlowDirection=FlowDirection.LeftToRight});
            pillRow.Children.Add(Text(label,11,"#F4F1EB",FontWeights.SemiBold));pill.Child=pillRow;picture.Children.Add(pill);
        }
        picture.Children.Add(CardMenu(item));
        // ✕ hides the title from this row (it comes back when you play it again).
        var remove=new Button{Content="✕",Width=28,Height=28,Padding=new(0),FontSize=12,Background=Brush("#D9101012"),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new(8),ToolTip=T("Remove from Continue Watching"),Opacity=0};
        remove.Template=RoundTemplate();System.Windows.Automation.AutomationProperties.SetName(remove,"Remove "+item.Label+" from Continue Watching");
        remove.Click+=(_,e)=>{e.Handled=true;store.State.HiddenContinue.Add(item.GroupKey);store.Save();Render();};
        if(next!=null)
        {
            picture.Children.Add(remove);
            RevealOnFocus(panel,remove);Motion.ButtonFeedback(remove);
        }
        if(kind is UpNextKind.Ready or UpNextKind.NotOnPc&&IsNewEpisode(item))
            picture.Children.Add(new Border{IsHitTestVisible=false,Background=Brush("#E4C084"),CornerRadius=new(6),Padding=new(8,3,8,3),Margin=new(next!=null?44:10,10,10,10),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Child=Text(T("NEW EPISODE"),10,"#111111",FontWeights.Bold)});
        var progress=bookmark?.Duration>0?Math.Clamp(bookmark.Position/bookmark.Duration,0,1):0;
        if(progress>0)picture.Children.Add(new ProgressBar { Value=progress*100,Maximum=100,Height=3,VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false,Background=Brush("#55000000"),Foreground=Brush("#E4C084"),BorderThickness=new(0) });
        var edge=new Border {CornerRadius=new(12),Child=picture,ClipToBounds=true,BorderThickness=new(1.5),BorderBrush=Brush("#00000000")};panel.Children.Add(edge);

        var title=ActionButton(item.Title,()=>OpenDetails(store.EpisodesFor(item.GroupKey)));title.FontWeight=FontWeights.SemiBold;title.FontSize=14;title.Padding=new(0);title.Background=Brush("Transparent");title.Margin=new(2,10,0,3);title.ToolTip="Details & episodes";panel.Children.Add(title);
        var remaining=bookmark is {Completed:false,Position:>=1,Duration:>0}?Loc.MinutesLeft(Math.Max(1,(int)Math.Ceiling((bookmark.Duration-bookmark.Position)/60))):"";
        var episode=Bidi(Text((item.IsMovie?T("Movie"):$"S{item.Season}E{item.Episode}")+(remaining.Length>0?" · "+remaining:item.EpisodeTitle.Length>0&&(!store.State.HideSpoilers||store.EverWatched(item))?" · "+item.EpisodeTitle:""),12,"#9F9A93"));episode.Margin=new(2,0,0,0);episode.TextWrapping=TextWrapping.NoWrap;episode.TextTrimming=TextTrimming.CharacterEllipsis;panel.Children.Add(episode);
        var status=kind switch
        {
            UpNextKind.Upcoming=>next?.AirsAt is {} airs?"Airs "+airs.ToLocalTime().ToString("ddd, d MMM")+" · You're caught up":"You're caught up",
            UpNextKind.NotOnPc when job!=null=>job.Status=="Paused"?"Download paused · Open Downloads":job.Size>0?$"Downloading · {Math.Clamp(job.Bytes*100/job.Size,0,100)}%":"Download queued",
            UpNextKind.NotOnPc=>bookmark is {Completed:false,Position:>0}?"Saved at "+LibraryStore.Time(bookmark.Position)+" · File not on this PC":"Not on this PC · Click to find a download",
            _=>""
        };
        if(status.Length>0){var line=Body(status,11);line.Margin=new(2,4,0,0);panel.Children.Add(line);}
        if(next!=null&&CatchUpCard(store.EpisodesFor(item.GroupKey),true) is {} recap)panel.Children.Add(recap);
        LazyMenu(panel,item);return Lift(panel,edge);
    }
    FrameworkElement Badge(string text)=>new Border
    {
        Background=Brush("#E6101012"),BorderBrush=Brush("#80E4C084"),BorderThickness=new(1),CornerRadius=new(14),Padding=new(14,6,14,6),
        HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,
        Child=Text(T(text),11,"#E4C084",FontWeights.SemiBold)
    };
    void RenderDetail(List<VideoItem> group)
    {
        var choice=ChoiceFor(group);PageTitle.Text=choice.Title;
        var backPage=detailOrigin?.Page??(page is "History" or "My List"?page:choice.IsMovie?"Movies":"Shows");
        ContentPanel.Children.Add(Hero(choice,group,store.ContinueFor(group)!=null));
        if(CatchUpCard(group) is {} recap)ContentPanel.Children.Add(recap);
        var back=ActionButton(T("←  Back to {0}","←  العودة إلى {0}",T(backPage)),()=>BackFromDetails(backPage));back.Background=Brush("Transparent");back.Foreground=Brush("#AAA6A0");back.Padding=new(0,4,0,4);back.HorizontalAlignment=HorizontalAlignment.Left;
        ContentPanel.Children.Add(DetailActions(choice,group,back));
        if(ReadyTitleInfo(choice.GroupKey) is {} readyInfo)ContentPanel.Children.Add(Body(readyInfo,12));
        if(downloads!=null&&!TrackerOnly&&store.State.Watchlist.Contains(choice.GroupKey))ContentPanel.Children.Add(AlsoDownloadToggle(choice));
        if(!choice.IsMovie&&group.Any(i=>i.Season>0))ContentPanel.Children.Add(SeasonStrip(group));
        ContentPanel.Children.Add(DetailPeople(choice));
        if(!group.Any(CanPlay))ContentPanel.Children.Add(WatchOptions(choice.ImdbId,choice.IsMovie,choice.Title,choice.IsMovie?0:(store.UpNextFor(group)?.Item??choice).Season,choice.IsMovie?0:(store.UpNextFor(group)?.Item??choice).Episode));
        var tabs=choice.IsMovie?new[]{"About","Activity"}:new[]{"Episodes","About","Activity"};
        if(!tabs.Contains(detailTab))detailTab=tabs[0];
        ContentPanel.Children.Add(UnderlineTabs(tabs,detailTab,tab=>{detailTab=tab;Render();}));
        if(detailTab=="Activity")
        {
            ContentPanel.Children.Add(Heading("Your activity","Viewing dates, ratings and discussions for this title."));
            var tools=new WrapPanel();tools.Children.Add(ActionButton("Viewing diary & rating",()=>OpenDiary(choice),true));var reviews=ActionButton("Reviews & comments",()=>OpenReviews(choice));reviews.Margin=new(10,0,0,0);tools.Children.Add(reviews);ContentPanel.Children.Add(tools);
            ContentPanel.Children.Add(Chips(LibraryStore.Statuses,store.State.TitleStatuses.GetValueOrDefault(choice.GroupKey,"Automatic"),state=>{store.SetStatus(choice,state);Render();}));
            foreach(var entry in group.OrderByDescending(HistoryDate).Where(i=>HistoryDate(i)!=DateTimeOffset.MinValue).Take(40))ContentPanel.Children.Add(EpisodeRow(entry));return;
        }
        if(detailTab=="About")
        {
            ContentPanel.Children.Add(Heading("About "+choice.Title));if(choice.Summary.Length>0)ContentPanel.Children.Add(Body(choice.Summary));if(choice.Genres.Length>0)ContentPanel.Children.Add(Body(choice.Genres,12));
            if(Uri.TryCreate(choice.SourceUrl,UriKind.Absolute,out var source)&&source.Scheme=="https")ContentPanel.Children.Add(ActionButton("Details from "+choice.SourceName,()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(source.AbsoluteUri){UseShellExecute=true})));
            ContentPanel.Children.Add(MoreLikeThis(choice.ImdbId,choice.IsMovie,choice.Genres.Split('·',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).ToList()));
            if(!choice.IsMovie){RenderEpisodeAvailability(choice);return;}
        }
        if(choice.IsMovie)
        {
            ContentPanel.Children.Add(Heading(CanPlay(choice)?"Playback":"Saved movie · History"));
            ContentPanel.Children.Add(EpisodeRow(choice));
            ContentPanel.Children.Add(Body(System.IO.Path.GetFileName(choice.Path),12));
            return;
        }
        var seasons=group.Select(x=>x.Season).Distinct().OrderBy(x=>x==0?int.MaxValue:x).ToList();
        if(detailSeason==null || !seasons.Contains(detailSeason.Value)) detailSeason=choice.Season;
        var selected=detailSeason.Value;

        // Seasons and filters are visible pills rather than a drop-down.
        var seasonTools=new DockPanel{Margin=new(0,6,0,0)};
        var episodeChips=Chips(["All episodes","Unwatched","In progress"],episodeFilter,label=>{episodeFilter=label;Render();});((FrameworkElement)episodeChips).Margin=new(16,0,0,0);DockPanel.SetDock(episodeChips,Dock.Right);seasonTools.Children.Add(episodeChips);
        var seasonChips=Chips(seasons.Select(n=>n==0?"Specials":"Season "+n).ToArray(),selected==0?"Specials":"Season "+selected,label=>{detailSeason=label=="Specials"?0:int.Parse(label[7..]);Render();});((FrameworkElement)seasonChips).Margin=new(0);seasonTools.Children.Add(seasonChips);
        var seasonHeader=new Border{Background=Brush("#F50B0B0D"),Padding=new(0,4,0,4),Child=seasonTools};var sticky=new TranslateTransform();seasonHeader.RenderTransform=sticky;Panel.SetZIndex(seasonHeader,30);ContentPanel.Children.Add(seasonHeader);
        void PinSeason(object? sender,ScrollChangedEventArgs e){if(!seasonHeader.IsLoaded||seasonHeader.Parent!=ContentPanel)return;var top=seasonHeader.TransformToAncestor(ContentPanel).Transform(new Point()).Y-sticky.Y;sticky.Y=Math.Max(0,ContentScroll.VerticalOffset-top+TopBar.ActualHeight);}
        seasonHeader.Loaded+=(_,_)=>ContentScroll.ScrollChanged+=PinSeason;seasonHeader.Unloaded+=(_,_)=>ContentScroll.ScrollChanged-=PinSeason;
        foreach(var season in group.Where(x=>x.Season==selected).GroupBy(x=>x.Season))
        {
            ContentPanel.Children.Add(Heading(season.Key==0?"Specials":$"Season {season.Key}",EpisodeCountText(season.ToList())));
            ContentPanel.Children.Add(new ProgressBar{Height=5,Maximum=Math.Max(1,season.Count()),Value=season.Count(store.EverWatched),Background=Brush("#353238"),Foreground=Brush("#E4C084"),BorderThickness=new(0),Margin=new(0,0,0,14)});
            var visible=season.Where(x=>episodeFilter!="Unwatched"||!store.EverWatched(x)).Where(x=>episodeFilter!="In progress"||store.GetProgress(x) is {Completed:false,Position:>0}).OrderBy(x=>x.Episode).ToList();
            foreach(var episode in visible) ContentPanel.Children.Add(EpisodeRow(episode));
            if(visible.Count==0) ContentPanel.Children.Add(Body("No episodes match this filter."));
        }
    }
    FrameworkElement EpisodeRow(VideoItem item)
    {
        var bookmark=store.GetProgress(item);
        var g=new Grid { Margin=new(17,13,17,13) };
        g.ColumnDefinitions.Add(new(){Width=new(160)});
        g.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) });g.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        var info=new StackPanel { VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,20,0) };
        var reveal=!store.State.HideSpoilers||store.EverWatched(item);
        var episodeName=Text(item.IsMovie?item.Title:item.EpisodeLabel+"  "+(reveal&&item.EpisodeTitle.Length>0?item.EpisodeTitle:T("Episode {0}","الحلقة {0}",item.Episode)),15,weight:FontWeights.SemiBold);
        var details=ActionButton("",()=>OpenEpisodeInfo(item));details.Content=episodeName;details.Background=Brush("Transparent");details.Padding=new(0,3,0,5);info.Children.Add(details);
        var message=bookmark?.Completed==true?T("✓ Watched","✓ تمت المشاهدة"):bookmark is {Position:>0}?T("Resume at {0}","استئناف عند {0}",LibraryStore.Time(bookmark.Position)):store.EverWatched(item)?T("✓ Watched previously","✓ تمت مشاهدته سابقًا"):CanPlay(item)?T("Ready to play","جاهز للتشغيل"):T("Not on this PC","غير موجود على هذا الكمبيوتر");
        if(bookmark is {Completed:false,Position:>0}&&store.EverWatched(item))message=T("Rewatch","إعادة مشاهدة")+" · "+message;
        if(item.Duration>0)message+="  ·  "+T("{0} min","{0} د",(int)(item.Duration/60));
        if(bookmark!=null)message+="  ·  "+bookmark.UpdatedAt.ToLocalTime().ToString("d MMM yyyy, HH:mm",Loc.Culture);
        if(!item.Available||!System.IO.File.Exists(item.Path))message+="  ·  "+T("File unavailable · History kept","الملف غير متاح · تم حفظ السجل");
        info.Children.Add(Body(message,12));Grid.SetColumn(info,1);g.Children.Add(info);
        var image=reveal&&System.IO.File.Exists(item.EpisodeImage)?item.EpisodeImage:item.Backdrop;
        var still=new Border{Width=144,Height=81,CornerRadius=new(6),ClipToBounds=true,Background=Brush("#252326"),Child=Art(image,item.Title,81),Margin=new(0,0,15,0),HorizontalAlignment=HorizontalAlignment.Left};g.Children.Add(still);Motion.EnterOnce(still);
        if(bookmark is {Duration:>0})info.Children.Add(new ProgressBar{Maximum=bookmark.Duration,Value=bookmark.Position,Height=3,Foreground=Brush("#E4C084"),Background=Brush("#353238"),BorderThickness=new(0)});
        if(item.EpisodeSummary.Length>0&&reveal){var summary=Body(item.EpisodeSummary,12);summary.LineHeight=19;summary.MaxHeight=38;summary.TextTrimming=TextTrimming.WordEllipsis;summary.Cursor=System.Windows.Input.Cursors.Hand;summary.ToolTip=T("Expand synopsis","توسيع الملخص");summary.MouseLeftButtonUp+=(_,e)=>{summary.MaxHeight=double.IsPositiveInfinity(summary.MaxHeight)?38:double.PositiveInfinity;summary.ToolTip=T(double.IsPositiveInfinity(summary.MaxHeight)?"Collapse synopsis":"Expand synopsis",double.IsPositiveInfinity(summary.MaxHeight)?"طي الملخص":"توسيع الملخص");e.Handled=true;};info.Children.Add(summary);}
        var actions=new WrapPanel { VerticalAlignment=VerticalAlignment.Center };
        var available=CanPlay(item);var stream=!available&&StreamOffered&&store.Aired(item,DateTimeOffset.UtcNow);
        var play=ActionButton(stream?"▶ Stream":!available?"↓ Download":bookmark is {Completed:false,Position:>0}?"▶ Resume":"▶ Play",()=>{if(available)_=PlayVideo(item);else if(stream)StreamItem(item);else FindDownloads(item);},true);play.MinWidth=Density.EpisodeActionMin;actions.Children.Add(play);
        if(stream){var get=ActionButton("↓",()=>FindDownloads(item));get.ToolTip=T("Download to this PC");get.Margin=new(8,0,0,0);actions.Children.Add(get);}
        var reviews=ActionButton("Reviews",()=>OpenReviews(item));reviews.Margin=new(8,0,0,0);actions.Children.Add(reviews);
        var seen=store.EverWatched(item);var mark=CheckCircle(seen,seen?"Mark unwatched":"Mark watched",()=>{if(seen)SmartWatchedClick(item);else SmartCheckOff(item,item.Label+" watched");},36);mark.Margin=new(12,0,0,0);actions.Children.Add(mark);actions.Children.Add(MoreButton(item));
        Grid.SetColumn(actions,2);g.Children.Add(actions);
        g.RowDefinitions.Add(new(){Height=GridLength.Auto});g.RowDefinitions.Add(new(){Height=GridLength.Auto});
        g.SizeChanged+=(_,_)=>{var small=g.ActualWidth<760;g.ColumnDefinitions[0].Width=new GridLength(small?120:160);still.Width=small?104:144;still.Height=still.Width*9/16;Grid.SetRow(actions,small?1:0);Grid.SetColumn(actions,small?1:2);Grid.SetColumnSpan(actions,small?2:1);actions.Margin=new Thickness(0,small?10:0,0,0);};
        var border=new Border { Background=Brush("#171719"),CornerRadius=new(9),Margin=new(0,0,0,9),Child=g };LazyMenu(border,item);
        return border;
    }
    // A full menu per card is costly on long pages; build it on right-click instead.
    void LazyMenu(FrameworkElement target,VideoItem item){target.ContextMenu=new ContextMenu();target.ContextMenuOpening+=(_,_)=>target.ContextMenu=ItemMenu(item);}
    ContextMenu ItemMenu(VideoItem item)
    {
        var menu=new ContextMenu{FlowDirection=LibraryRoot.FlowDirection};AddHistoryTitleActions(menu,item);
        if(StreamOffered&&System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId,@"^tt\d+$"))
        {
            var stream=new MenuItem{Header="Stream now"};stream.Click+=(_,_)=>StreamItem(item);menu.Items.Add(stream);
            var choose=new MenuItem{Header="Choose a stream…"};choose.Click+=(_,_)=>StreamItem(item,true);menu.Items.Add(choose);
        }
        var download=new MenuItem{Header="Download to this PC"};download.Click+=(_,_)=>FindDownloads(item);menu.Items.Add(download);
        if(!item.IsMovie){var seasonDownload=new MenuItem{Header="Download this season to PC"};seasonDownload.Click+=(_,_)=>FindDownloads(item,true);menu.Items.Add(seasonDownload);}
        if(CanPlay(item)){var subtitles=new MenuItem{Header="Download English / Arabic subtitles"};subtitles.Click+=(_,_)=>DownloadSubtitleFiles(item);menu.Items.Add(subtitles);}
        var queue=new MenuItem{Header="Add to play queue",IsEnabled=CanPlay(item)};queue.Click+=(_,_)=>{store.Queue(item);Status.Text="Added to your play queue.";};menu.Items.Add(queue);
        var collections=new MenuItem{Header="Save to collection"};collections.Click+=(_,_)=>CollectionDialog(item);menu.Items.Add(collections);
        var statuses=new MenuItem{Header="Viewing status"};foreach(var status in LibraryStore.Statuses){var value=status;var option=new MenuItem{Header=status};option.Click+=(_,_)=>{store.SetStatus(item,value);Render();};statuses.Items.Add(option);}menu.Items.Add(statuses);
        var match=new MenuItem{Header="Correct match / episode numbers"};match.Click+=(_,_)=>MatchDialog(item);menu.Items.Add(match);
        var restart=new MenuItem { Header="Play from beginning",IsEnabled=item.Available && System.IO.File.Exists(item.Path) };restart.Click+=(_,_)=>_ = PlayVideo(item,true);menu.Items.Add(restart);
        var isWatched=store.EverWatched(item);
        var mark=new MenuItem { Header=isWatched?"Mark unwatched":"Mark watched" };mark.Click+=(_,_)=> { if(isWatched)SmartWatchedClick(item);else SmartCheckOff(item,item.Label+" watched"); };menu.Items.Add(mark);
        if(!item.IsMovie&&item.Season>0)
        {
            var upTo=new MenuItem{Header="Mark watched up to here"};
            upTo.Click+=(_,_)=>{var now=DateTimeOffset.UtcNow;var earlier=store.EpisodesFor(item.GroupKey).Where(i=>i.Season>0&&(i.Season<item.Season||i.Season==item.Season&&i.Episode<=item.Episode)&&store.Aired(i,now)&&!store.EverWatched(i)).ToList();if(earlier.Count>0)BulkCheckOff(earlier,$"{earlier.Count} episode{(earlier.Count==1?"":"s")} of {item.Title} marked watched");};
            menu.Items.Add(upTo);
            var whole=ShowEpisodes(item);var wholeSeen=whole.Count>0&&whole.All(store.EverWatched);
            var show=new MenuItem{Header=wholeSeen?"Unmark whole show":"Mark whole show watched",IsEnabled=whole.Count>0};
            show.Click+=(_,_)=>{if(wholeSeen)UncheckAll(store.EpisodesFor(item.GroupKey).Where(store.EverWatched).ToList(),$"{item.Title} unmarked");else MarkWholeShow(item);};menu.Items.Add(show);
            var specials=new MenuItem{Header="Count specials in progress",IsCheckable=true,IsChecked=store.State.IncludeSpecials.Contains(item.GroupKey)};
            specials.Click+=(_,_)=>{if(!store.State.IncludeSpecials.Remove(item.GroupKey))store.State.IncludeSpecials.Add(item.GroupKey);store.Save();Render();};menu.Items.Add(specials);
        }
        if(System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId,@"^tt\d+$")){var stream=new MenuItem{Header="Stream in Stremio"};stream.Click+=(_,_)=>OpenInStremio(item.ImdbId,item.IsMovie,item.Season,item.Episode);menu.Items.Add(stream);
            var nuvio=new MenuItem{Header="Stream in Nuvio"};nuvio.Click+=(_,_)=>OpenInNuvio(item.ImdbId,item.IsMovie,item.Title);menu.Items.Add(nuvio);}
        var reviews=new MenuItem{Header="Reviews & comments"};reviews.Click+=(_,_)=>OpenReviews(item);menu.Items.Add(reviews);
        var diary=new MenuItem{Header="Viewing diary & my rating"};diary.Click+=(_,_)=>OpenDiary(item);menu.Items.Add(diary);
        var info=new MenuItem{Header="Episode / movie information"};info.Click+=(_,_)=>OpenEpisodeInfo(item);menu.Items.Add(info);
        var saved=new MenuItem{Header=store.State.Watchlist.Contains(item.GroupKey)?"Remove from My List":"Add to My List"};saved.Click+=(_,_)=>ToggleWatchlist(item);menu.Items.Add(saved);
        if(store.IsHidden(item.GroupKey)){var back=new MenuItem{Header="Show in library again"};back.Click+=(_,_)=>{store.SetHidden(item.GroupKey,false);Render();};menu.Items.Add(back);}
        else{var remove=new MenuItem{Header="Remove from library…"};remove.Click+=(_,_)=>RemoveTitleDialog(item);menu.Items.Add(remove);}
        var hide=new MenuItem{Header=store.State.HiddenContinue.Contains(item.GroupKey)?"Show in Continue Watching":"Hide from Continue Watching"};
        hide.Click+=(_,_)=>{if(!store.State.HiddenContinue.Add(item.GroupKey)) store.State.HiddenContinue.Remove(item.GroupKey);store.Save();Render();};menu.Items.Add(hide);
        return menu;
    }
    void ToggleWatchlist(VideoItem item) {if(!store.State.Watchlist.Add(item.GroupKey)) store.State.Watchlist.Remove(item.GroupKey);store.TouchMobileField("list:"+item.GroupKey);store.Save();Render();}
    FrameworkElement Chips(string[] options,string selected,Action<string> choose)
    {
        var row=new WrapPanel{Margin=new(0,14,0,8)};
        foreach(var option in options)
        {
            var chip=ActionButton(option,()=>choose(option));chip.Style=(Style)FindResource("Pill");chip.Padding=Density.ChipPadding;chip.FontSize=Density.ChipFont;chip.Margin=new(0,0,8,8);
            chip.Background=Brush(option==selected?"#E4C084":"#14FFFFFF");chip.Foreground=Brush(option==selected?"#16120C":"#D9D4CC");chip.BorderBrush=Brush(option==selected?"#E4C084":"#2EFFFFFF");chip.FontWeight=option==selected?FontWeights.SemiBold:FontWeights.Normal;
            row.Children.Add(chip);
        }
        return row;
    }
    void RenderBackgroundSettings()
    {
        ContentPanel.Children.Add(Heading("Running in the background","Phone sync, streaming to your phone and PC downloads need Noir running on this PC."));
        var background=new CheckBox{Content="Keep Noir running in the notification area when I close the window",IsChecked=store.State.RunInBackground,Margin=new(0,6,0,6)};
        background.Click+=(_,_)=>{store.State.RunInBackground=background.IsChecked==true;store.Save();};ContentPanel.Children.Add(background);
        var awake=new CheckBox{Content="Keep the PC from sleeping while downloading or streaming to my phone",IsChecked=store.State.KeepAwakeWhileBusy,Margin=new(0,6,0,6)};
        awake.Click+=(_,_)=>{store.State.KeepAwakeWhileBusy=awake.IsChecked==true;store.Save();UpdateBackgroundState();};ContentPanel.Children.Add(awake);
        var startup=new CheckBox{Content="Start Noir in the background when I sign in to Windows",IsChecked=StartsWithWindows(),Margin=new(0,6,0,6)};
        startup.Click+=(_,_)=>{try{SetStartWithWindows(startup.IsChecked==true);}catch(Exception e){store.Log(e);startup.IsChecked=StartsWithWindows();Status.Text="Windows didn't allow changing the startup setting.";}};ContentPanel.Children.Add(startup);
        ContentPanel.Children.Add(Body("Right-click the Noir icon near the clock to open or quit it. The screen can still turn off; only sleep is postponed while work is in progress.",12));
    }
    void RenderPlaybackSettings()
    {
        ContentPanel.Children.Add(Heading("Playback"));
        var next=new CheckBox { Content="Play the next episode after this one ends",IsChecked=store.State.AutoNext };
        next.Click+=(_,_)=> { store.State.AutoNext=next.IsChecked==true;store.Save(); };ContentPanel.Children.Add(next);
        ContentPanel.Children.Add(Body("A short countdown starts after the episode finishes. You can cancel it.",12));
        ContentPanel.Children.Add(PlaybackPrefsPanel());
        ContentPanel.Children.Add(PreviewSettingsPanel());
        ContentPanel.Children.Add(CatchUpSettingsRow());
        var credits=new CheckBox{Content="Mark watched when credits begin",IsChecked=store.State.MarkWatchedAtCredits};
        credits.Click+=(_,_)=>{store.State.MarkWatchedAtCredits=credits.IsChecked==true;store.Save();};ContentPanel.Children.Add(credits);
        ContentPanel.Children.Add(Body("Uses labelled ending chapters or saved credits times. Without a marker, uses the final 5% (up to 90 seconds for episodes or 3 minutes for movies). Credits keep playing; auto-next waits until the actual end.",12));
        ContentPanel.Children.Add(Body("Preferred subtitle language",12));
        ContentPanel.Children.Add(Chips(["English","Arabic","Off"],store.State.Subtitles,label=>{store.State.Subtitles=label;store.Save();Render();}));
        RenderSubtitleAppearance(ContentPanel);
    }
    void RenderLibrarySettings()
    {
        ContentPanel.Children.Add(Heading("Artwork & information","Saved on this PC, available offline."));
        var online=new CheckBox {Content="Download missing artwork and information when online",IsChecked=store.State.OnlineMetadata};
        online.Click+=async(_,_)=> {store.State.OnlineMetadata=online.IsChecked==true;store.Save();if(store.State.OnlineMetadata) await RefreshMetadata();};ContentPanel.Children.Add(online);
        ContentPanel.Children.Add(Body("Noir checks titles online and keeps downloaded details and pictures. Your videos stay on this PC. Removing a media folder keeps your watch history and its artwork.",12));
        ContentPanel.Children.Add(ActionButton("Refresh artwork & information",async()=> {Status.Text="Checking for new files…";await ScanLibrary(false);await RefreshMetadata(true);}));
        ContentPanel.Children.Add(Heading("Your folders","Scanning reads your videos and leaves the files in place."));
        ContentPanel.Children.Add(ActionButton("Organize filenames & folders",OpenOrganizer));
        foreach(var root in store.State.Roots.ToList())
        {
            var row=new DockPanel { Margin=new(0,0,0,13) };
            var remove=ActionButton("Remove from library",async()=> { store.State.Roots.Remove(root);store.Save();await ScanLibrary(); });DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);
            var label=new StackPanel();label.Children.Add(Text(root.Movies?"Movies":"Shows",14,weight:FontWeights.SemiBold));label.Children.Add(Body(root.Path,12));row.Children.Add(label);ContentPanel.Children.Add(row);
        }
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,Margin=new(0,10,0,0) };
        buttons.Children.Add(ActionButton("Add movie folder",()=>AddFolder(true),true));var shows=ActionButton("Add show folder",()=>AddFolder(false));shows.Margin=new(12,0,0,0);buttons.Children.Add(shows);ContentPanel.Children.Add(buttons);
    }
    void RenderAdvancedSettings()
    {
        RenderBackgroundSettings();
        ContentPanel.Children.Add(Heading("Keyboard shortcuts"));
        ContentPanel.Children.Add(Body(T("Press ? anywhere in Noir to see these. In the library:","اضغط ? في أي مكان في Noir لعرضها. في المكتبة:"),12));ContentPanel.Children.Add(ShortcutRows(LibraryShortcuts,false));
        ContentPanel.Children.Add(Body(T("In the player:","في المشغّل:"),12));ContentPanel.Children.Add(ShortcutRows(PlayerShortcuts,false));
        var sheet=ActionButton(T("Shortcuts & tips","الاختصارات والنصائح"),()=>ToggleShortcutSheet(false));sheet.Margin=new(0,6,0,0);ContentPanel.Children.Add(sheet);
        var appVersion=typeof(App).Assembly.GetName().Version?.ToString(3)??"";
        ContentPanel.Children.Add(Heading("Noir "+appVersion,"Built for your local library. Playback powered by VLC."));
        ContentPanel.Children.Add(Body("TV information: TVmaze (CC BY-SA). Movie information: Wikipedia (CC BY-SA). Source links are saved with each title. Artwork belongs to its respective owners.",12));
    }
    async void AddFolder(bool movies)
    {
        var dialog=new Microsoft.Win32.OpenFolderDialog { Title=movies?"Choose your movie folder":"Choose your show folder" };
        if(dialog.ShowDialog(this)!=true) return;
        if(!store.State.Roots.Any(x=>string.Equals(x.Path,dialog.FolderName,StringComparison.OrdinalIgnoreCase))) store.State.Roots.Add(new() { Path=dialog.FolderName,Movies=movies });
        store.Save();await ScanLibrary();
    }
}

