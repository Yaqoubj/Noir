using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noir;
public partial class MainWindow
{
    FrameworkElement Hero(VideoItem item,List<VideoItem> group,bool continuing)
    {
        var grid=new Grid { Height=detailKey==null?315:240,ClipToBounds=true };
        var source=ImageSource(item.Backdrop)??ImageSource(item.Poster);
        if(source!=null) grid.Background=new ImageBrush(source) { Stretch=Stretch.UniformToFill,AlignmentY=AlignmentY.Top };
        else grid.Background=Brush("#24201B");
        var shade=new LinearGradientBrush();shade.StartPoint=new(0,0.5);shade.EndPoint=new(1,0.5);
        shade.GradientStops.Add(new(Brush("#FA0D0D0F").Color,0));shade.GradientStops.Add(new(Brush("#D80D0D0F").Color,.5));shade.GradientStops.Add(new(Brush("#280D0D0F").Color,1));
        grid.Children.Add(new Border { Background=shade });
        var panel=new StackPanel { Margin=new(30,24,30,22),MaxWidth=610,HorizontalAlignment=HorizontalAlignment.Left };
        panel.Children.Add(Text(continuing?"RIGHT WHERE YOU LEFT OFF":"TONIGHT'S PICK",10,"#E4C084",FontWeights.SemiBold));
        var name=Text(item.Title,40,weight:FontWeights.Bold);name.Margin=new(0,8,0,3);panel.Children.Add(name);
        var pro=store.GetProgress(item);
        var info=item.IsMovie?(store.EverWatched(item)?"✓ Watched":"Movie"):item.EpisodeLabel+"  ·  "+EpisodeCountText(group,false);
        if(!CanPlay(item)) info+="  ·  Not on this PC";
        if(item.Year>0) info+=$"  ·  {item.Year}";
        var infoText=Text(info,12,"#C7C2BC");if(detailKey!=null) infoText.Margin=new(0,0,0,20);panel.Children.Add(infoText);
        var summary=Text(item.Summary.Length>0?item.Summary:"A little less browsing. A little more watching.",13,"#CBC6BF");
        summary.MaxHeight=56;summary.TextTrimming=TextTrimming.CharacterEllipsis;summary.Margin=new(0,12,0,20);if(detailKey==null) panel.Children.Add(summary);
        var actions=new WrapPanel();
        var play=ActionButton(pro is {Completed:false,Position:>0}?"▶  Resume · "+LibraryStore.Time(pro.Position):continuing?"▶  Play next episode":"▶  Play",()=>_ = PlayVideo(item),true);
        play.IsEnabled=item.Available && System.IO.File.Exists(item.Path);
        actions.Children.Add(play);
        var details=ActionButton(detailKey!=null?"Reviews":item.IsMovie?"Details":"Episodes",()=>{if(detailKey!=null) OpenReviews(item);else OpenDetails(group);});details.Margin=new(10,0,0,0);actions.Children.Add(details);
        var saved=ActionButton(store.State.Watchlist.Contains(item.GroupKey)?"✓ My List":"+ My List",()=>ToggleWatchlist(item));saved.Margin=new(10,0,0,0);actions.Children.Add(saved);
        panel.Children.Add(actions);grid.Children.Add(panel);
        return new Border { CornerRadius=new(13),Child=grid,ClipToBounds=true };
    }
    FrameworkElement ContinueCard(VideoItem item)
    {
        var panel=new StackPanel {Width=270,Margin=new(0,0,18,12)};
        var picture=new Grid {Height=152,ClipToBounds=true};
        var play=ActionButton("",()=>_ = PlayVideo(item));play.Content=Art(System.IO.File.Exists(item.Backdrop)?item.Backdrop:item.Poster,item.Title,152);play.Padding=new(0);play.HorizontalAlignment=HorizontalAlignment.Stretch;
        play.ToolTip="Resume "+item.Label;System.Windows.Automation.AutomationProperties.SetName(play,"Continue "+item.Label);picture.Children.Add(play);
        picture.Children.Add(new Border {IsHitTestVisible=false,Background=new LinearGradientBrush(Brush("#00000000").Color,Brush("#77000000").Color,90)});
        var icon=PlayCircle();icon.IsHitTestVisible=false;picture.Children.Add(icon);picture.Children.Add(CardMenu(item));
        panel.Children.Add(new Border {CornerRadius=new(10),Child=picture,ClipToBounds=true});
        var bookmark=store.GetProgress(item);
        var progress=bookmark?.Duration>0?Math.Clamp(bookmark.Position/bookmark.Duration,0,1):0;
        panel.Children.Add(new ProgressBar { Value=progress*100,Maximum=100,Height=3,Background=Brush("#37332E"),Foreground=Brush("#E4C084"),BorderThickness=new(0) });
        var title=ActionButton(item.Title,()=>OpenDetails(store.EpisodesFor(item.GroupKey)));title.FontWeight=FontWeights.SemiBold;title.FontSize=15;title.Padding=new(0);title.Background=Brush("Transparent");title.Margin=new(0,10,0,5);title.ToolTip="Details & episodes";panel.Children.Add(title);
        var episode=Text(item.IsMovie?"Movie":item.EpisodeLabel+(item.EpisodeTitle.Length>0?" · "+item.EpisodeTitle:""),12,"#C7C2BC");episode.TextWrapping=TextWrapping.NoWrap;episode.TextTrimming=TextTrimming.CharacterEllipsis;panel.Children.Add(episode);
        panel.Children.Add(Body(bookmark is {Completed:false,Position:>0}?"Resume · "+LibraryStore.Time(bookmark.Position)+(bookmark.Duration>0?"  ·  "+Math.Max(1,(int)Math.Ceiling((bookmark.Duration-bookmark.Position)/60))+" min left":""):"Up next · Ready to play",11));
        panel.ContextMenu=ItemMenu(item);return panel;
    }
    void Catalog(List<List<VideoItem>> groups,string title)
    {
        if(groups.Count==0) return;
        ContentPanel.Children.Add(Heading(title));
        var wrap=new WrapPanel();
        foreach(var group in groups)
        {
            var item=group[0];
            var panel=new StackPanel();
            var picture=new Grid{Height=266,ClipToBounds=true};
            var poster=ActionButton("",()=>OpenDetails(group));poster.Content=Art(item.Poster,item.Title,266,true);poster.Padding=new(0);poster.HorizontalAlignment=HorizontalAlignment.Stretch;poster.ToolTip=item.Title+" · Details & episodes";System.Windows.Automation.AutomationProperties.SetName(poster,item.Title+" details");picture.Children.Add(poster);picture.Children.Add(CardMenu(item));
            panel.Children.Add(new Border {CornerRadius=new(9),ClipToBounds=true,Child=picture});
            var name=Text(item.Title,15,weight:FontWeights.SemiBold);name.Margin=new(0,10,0,3);name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextWrapping=TextWrapping.NoWrap;panel.Children.Add(name);
            panel.Children.Add(Text(item.IsMovie?(store.EverWatched(item)?"✓ Watched":item.Duration>0?$"{(int)(item.Duration/60)} minutes":"Movie"):EpisodeCountText(group),12,"#ABA59D"));
            if(!group.Any(CanPlay)) panel.Children.Add(Body("Not on this PC · Saved in library",11));
            panel.Width=182;panel.Margin=new(0,0,20,20);panel.ContextMenu=ItemMenu(item);wrap.Children.Add(panel);
        }
        ContentPanel.Children.Add(wrap);
    }
    void RenderDetail(List<VideoItem> group)
    {
        var choice=ChoiceFor(group);PageTitle.Text=choice.Title;
        var backPage=page is "History" or "My List"?page:choice.IsMovie?"Movies":"Shows";
        var back=ActionButton("←  "+backPage,()=>Navigate(backPage));back.Margin=new(0,0,0,16);ContentPanel.Children.Add(back);
        ContentPanel.Children.Add(Hero(choice,group,store.ContinueFor(group)!=null));
        var tools=new WrapPanel{Margin=new(0,12,0,0)};var diaryButton=ActionButton("◷ Viewing diary & my rating",()=>OpenDiary(choice));diaryButton.Margin=new(0,0,8,8);tools.Children.Add(diaryButton);var collection=ActionButton("Save to collection",()=>CollectionDialog(choice));collection.Margin=new(0,0,8,8);tools.Children.Add(collection);tools.Children.Add(ActionButton("Correct match / episode numbers",()=>MatchDialog(choice)));ContentPanel.Children.Add(tools);
        ContentPanel.Children.Add(Chips(LibraryStore.Statuses,store.State.TitleStatuses.GetValueOrDefault(choice.GroupKey,"Automatic"),s=>{store.SetStatus(choice,s);Render();}));
        if(choice.Summary.Length>0)
        {
            var about=new StackPanel();about.Children.Add(Body(choice.Summary));
            if(Uri.TryCreate(choice.SourceUrl,UriKind.Absolute,out var source) && source.Scheme=="https")
                about.Children.Add(ActionButton("Details from "+choice.SourceName,()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(source.AbsoluteUri) {UseShellExecute=true})));
            ContentPanel.Children.Add(new Expander{Header="About this title",Content=about,Margin=new(0,14,0,5),Foreground=Brush("#C7C2BC"),Background=Brush("#171719")});
        }
        if(choice.Genres.Length>0) ContentPanel.Children.Add(Body(choice.Genres,12));
        if(choice.IsMovie)
        {
            ContentPanel.Children.Add(Heading(CanPlay(choice)?"Playback":"Saved movie · History"));
            ContentPanel.Children.Add(EpisodeRow(choice));
            ContentPanel.Children.Add(Body(System.IO.Path.GetFileName(choice.Path),12));
            return;
        }
        RenderEpisodeAvailability(choice);
        var seasons=group.Select(x=>x.Season).Distinct().OrderBy(x=>x==0?int.MaxValue:x).ToList();
        if(detailSeason==null || !seasons.Contains(detailSeason.Value)) detailSeason=choice.Season;
        var selected=detailSeason.Value;
        ContentPanel.Children.Add(Chips(seasons.Select(s=>s==0?"Specials":"Season "+s).ToArray(),selected==0?"Specials":"Season "+selected,label=>{detailSeason=label=="Specials"?0:int.Parse(label[7..]);Render();}));
        ContentPanel.Children.Add(Chips(["All episodes","Unwatched","In progress"],episodeFilter,label=>{episodeFilter=label;Render();}));
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
        var episodeName=Text(item.IsMovie?item.Title:item.EpisodeLabel+"  "+(item.EpisodeTitle.Length>0?item.EpisodeTitle:$"Episode {item.Episode}"),15,weight:FontWeights.SemiBold);
        var details=ActionButton("",()=>OpenEpisodeInfo(item));details.Content=episodeName;details.Background=Brush("Transparent");details.Padding=new(0,3,0,5);info.Children.Add(details);
        var message=bookmark?.Completed==true?"✓ Watched":bookmark is {Position:>0}?"Resume at "+LibraryStore.Time(bookmark.Position):store.EverWatched(item)?"✓ Watched previously":CanPlay(item)?"Ready to play":"Not on this PC";
        if(bookmark is {Completed:false,Position:>0}&&store.EverWatched(item)) message="Rewatch · "+message;
        if(item.Duration>0) message+=$"  ·  {(int)(item.Duration/60)} min";
        if(bookmark!=null) message+="  ·  "+bookmark.UpdatedAt.ToLocalTime().ToString("d MMM yyyy, HH:mm");
        if(!item.Available || !System.IO.File.Exists(item.Path)) message+="  ·  File unavailable · History kept";
        info.Children.Add(Body(message,12));Grid.SetColumn(info,1);g.Children.Add(info);
        var image=(!store.State.HideSpoilers||store.EverWatched(item))&&System.IO.File.Exists(item.EpisodeImage)?item.EpisodeImage:item.Backdrop;
        var thumbnail=Art(image,item.Title,90);thumbnail.Margin=new(0,0,15,0);g.Children.Add(thumbnail);
        if(bookmark is {Duration:>0})info.Children.Add(new ProgressBar{Maximum=bookmark.Duration,Value=bookmark.Position,Height=3,Foreground=Brush("#E4C084"),Background=Brush("#353238"),BorderThickness=new(0)});
        if(item.EpisodeSummary.Length>0 && (!store.State.HideSpoilers || store.EverWatched(item))) {var summary=Body(item.EpisodeSummary,12);summary.MaxHeight=48;summary.TextTrimming=TextTrimming.CharacterEllipsis;info.Children.Add(summary);}
        var actions=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
        var play=ActionButton(!CanPlay(item)?"Unavailable":bookmark is {Completed:false,Position:>0}?"▶ Resume":"▶ Play",()=>_ = PlayVideo(item),true);actions.Children.Add(play);
        play.IsEnabled=item.Available && System.IO.File.Exists(item.Path);
        var reviews=ActionButton("Reviews",()=>OpenReviews(item));reviews.Margin=new(8,0,0,0);actions.Children.Add(reviews);
        var mark=ActionButton(bookmark?.Completed==true?"✓":"○",()=> { store.Mark(item,bookmark?.Completed!=true);Render(); });mark.Margin=new(8,0,0,0);mark.ToolTip=bookmark?.Completed==true?"Mark unwatched":"Mark watched";actions.Children.Add(mark);
        Grid.SetColumn(actions,2);g.Children.Add(actions);
        var border=new Border { Background=Brush("#171719"),CornerRadius=new(9),Margin=new(0,0,0,9),Child=g,ContextMenu=ItemMenu(item) };
        return border;
    }
    ContextMenu ItemMenu(VideoItem item)
    {
        var menu=new ContextMenu();
        var queue=new MenuItem{Header="Add to play queue",IsEnabled=CanPlay(item)};queue.Click+=(_,_)=>{store.Queue(item);Status.Text="Added to your play queue.";};menu.Items.Add(queue);
        var collections=new MenuItem{Header="Save to collection"};collections.Click+=(_,_)=>CollectionDialog(item);menu.Items.Add(collections);
        var statuses=new MenuItem{Header="Viewing status"};foreach(var status in LibraryStore.Statuses){var value=status;var option=new MenuItem{Header=status};option.Click+=(_,_)=>{store.SetStatus(item,value);Render();};statuses.Items.Add(option);}menu.Items.Add(statuses);
        var match=new MenuItem{Header="Correct match / episode numbers"};match.Click+=(_,_)=>MatchDialog(item);menu.Items.Add(match);
        var restart=new MenuItem { Header="Play from beginning",IsEnabled=item.Available && System.IO.File.Exists(item.Path) };restart.Click+=(_,_)=>_ = PlayVideo(item,true);menu.Items.Add(restart);
        var isWatched=store.GetProgress(item)?.Completed==true;
        var mark=new MenuItem { Header=isWatched?"Mark unwatched":"Mark watched" };mark.Click+=(_,_)=> { store.Mark(item,!isWatched);Render(); };menu.Items.Add(mark);
        var reviews=new MenuItem{Header="Reviews & comments"};reviews.Click+=(_,_)=>OpenReviews(item);menu.Items.Add(reviews);
        var diary=new MenuItem{Header="Viewing diary & my rating"};diary.Click+=(_,_)=>OpenDiary(item);menu.Items.Add(diary);
        var info=new MenuItem{Header="Episode / movie information"};info.Click+=(_,_)=>OpenEpisodeInfo(item);menu.Items.Add(info);
        var saved=new MenuItem{Header=store.State.Watchlist.Contains(item.GroupKey)?"Remove from My List":"Add to My List"};saved.Click+=(_,_)=>ToggleWatchlist(item);menu.Items.Add(saved);
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
            var chip=ActionButton(option,()=>choose(option));chip.Padding=new(15,8,15,8);chip.Margin=new(0,0,8,8);
            chip.Background=Brush(option==selected?"#E4C084":"#202023");chip.Foreground=Brush(option==selected?"#16120C":"#C7C2BC");
            row.Children.Add(chip);
        }
        return row;
    }
    void RenderHistory()
    {
        PageTitle.Text="Watch history";
        ContentPanel.Children.Add(Body("Watched posters, dates and episode counts stay here after videos are deleted from your PC.",14));
        var query=SearchBox.Text.Trim();
        var watchedItems=store.Archive.Where(store.EverWatched).ToList();
        ContentPanel.Children.Add(Heading($"{watchedItems.Where(i=>!i.IsMovie).Select(i=>i.GroupKey).Distinct().Count()} shows · {watchedItems.Count(i=>!i.IsMovie)} episodes · {watchedItems.Count(i=>i.IsMovie)} movies watched","Your personal collection, including titles no longer on this PC."));
        ContentPanel.Children.Add(Chips(["All","In progress","Watched","Unavailable"],historyFilter,x=>{historyFilter=x;Render();}));
        var controls=new DockPanel();
        var sorts=Chips(["Recent","A–Z"],historySort,x=>{historySort=x;Render();});DockPanel.SetDock(sorts,Dock.Right);controls.Children.Add(sorts);
        controls.Children.Add(Chips(["All","Movies","Shows"],historyType,x=>{historyType=x;Render();}));ContentPanel.Children.Add(controls);
        var groups=store.Archive.Where(i=>store.EverWatched(i) || store.GetProgress(i) is {Position:>0})
            .Where(i=>historyType!="Movies"||i.IsMovie).Where(i=>historyType!="Shows"||!i.IsMovie)
            .Where(i=>historyFilter!="In progress"||store.GetProgress(i) is {Completed:false,Position:>0})
            .Where(i=>historyFilter!="Watched"||store.EverWatched(i))
            .Where(i=>historyFilter!="Unavailable"||!i.Available||!System.IO.File.Exists(i.Path))
            .Where(i=>query.Length==0 || i.Title.Contains(query,StringComparison.OrdinalIgnoreCase) || i.EpisodeTitle.Contains(query,StringComparison.OrdinalIgnoreCase))
            .GroupBy(i=>i.GroupKey).OrderByDescending(g=>g.Max(HistoryDate)).ToList();
        if(historySort=="A–Z") groups=groups.OrderBy(g=>g.First().Title).ToList();
        if(groups.Count==0)
        {
            ContentPanel.Children.Add(Heading("No matches in this view","Try showing all history."));
            ContentPanel.Children.Add(ActionButton("Show all history",()=>{historyFilter="All";historyType="All";SearchBox.Text="";Render();},true));return;
        }
        var cards=new WrapPanel();ContentPanel.Children.Add(cards);
        foreach(var group in groups)
        {
            var latest=group.OrderByDescending(HistoryDate).First();var bookmark=store.GetProgress(latest)??new Bookmark();
            var full=store.EpisodesFor(latest.GroupKey);var continuation=store.ContinueFor(full);
            var panel=new StackPanel{Margin=new(12)};
            var poster=new Grid{Height=267,ClipToBounds=true};poster.Children.Add(Art(latest.Poster,latest.Title,267,true));
            var badgeText=latest.IsMovie?(store.EverWatched(latest)?"✓ Watched":"Watching"):$"✓ {full.Count(store.EverWatched)} watched";
            var badge=new Border{Background=Brush("#E6101012"),CornerRadius=new(6),Padding=new(8,4,8,4),Margin=new(8),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,Child=Text(badgeText,12,"#E4C084")};poster.Children.Add(badge);
            panel.Children.Add(new Border{CornerRadius=new(10),ClipToBounds=true,Child=poster});
            var name=Text(latest.Title,17,weight:FontWeights.SemiBold);name.Margin=new(0,12,0,6);panel.Children.Add(name);
            panel.Children.Add(Text(latest.IsMovie?(store.EverWatched(latest)?"✓ Watched":"In progress"):EpisodeCountText(full),12,"#AAA6A0"));
            if(!latest.IsMovie)
            {
                var known=store.State.EpisodeSchedules.GetValueOrDefault(latest.GroupKey)?.Count(e=>e.Season>0&&e.AirsAt<=DateTimeOffset.UtcNow)??0;
                var total=Math.Max(known,full.Count(i=>i.Season>0));var watched=full.Count(i=>i.Season>0&&store.EverWatched(i));
                panel.Children.Add(new ProgressBar {Value=watched,Maximum=Math.Max(1,total),Height=4,Margin=new(0,10,0,7),Background=Brush("#353236"),Foreground=Brush("#E4C084"),BorderThickness=new(0)});
                panel.Children.Add(Body($"{watched} / {total} released episodes watched",11));
            }
            var resumeBookmark=continuation==null?null:store.GetProgress(continuation);
            panel.Children.Add(Body(continuation!=null?continuation.EpisodeLabel+" · "+(resumeBookmark is {Completed:false,Position:>0}?"Resume at "+LibraryStore.Time(resumeBookmark.Position):"Up next"):(latest.IsMovie?"":latest.EpisodeLabel+" · ")+(bookmark.Completed?"Watched":"Saved at "+LibraryStore.Time(bookmark.Position)),12));
            panel.Children.Add(Text(HistoryDate(latest).ToLocalTime().ToString("d MMM yyyy"),11,"#817E78"));
            if(!latest.Available||!System.IO.File.Exists(latest.Path)) panel.Children.Add(Body("File unavailable · History kept",11));
            var card=ActionButton("",()=>OpenDetails(full));card.Width=222;card.Content=panel;card.VerticalContentAlignment=VerticalAlignment.Top;card.Background=Brush("#19191C");card.Padding=new(0);card.Margin=new(0,14,20,20);card.ContextMenu=ItemMenu(latest);card.ToolTip="Open "+latest.Title+" · Episodes, diary and reviews";cards.Children.Add(card);
        }
    }
    void RenderSettings()
    {
        PageTitle.Text="Make yourself at home.";
        RenderMobileSettings();
        ContentPanel.Children.Add(Heading("Playback"));
        var next=new CheckBox { Content="Play the next episode after this one ends",IsChecked=store.State.AutoNext };
        next.Click+=(_,_)=> { store.State.AutoNext=next.IsChecked==true;store.Save(); };ContentPanel.Children.Add(next);
        ContentPanel.Children.Add(Body("A short countdown starts after the episode finishes. You can cancel it.",12));
        var intro=new CheckBox { Content="Automatically skip known intros",IsChecked=store.State.AutoSkipIntro };
        intro.Click+=(_,_)=> { store.State.AutoSkipIntro=intro.IsChecked==true;store.Save(); };ContentPanel.Children.Add(intro);
        ContentPanel.Children.Add(Body("Uses saved markers or labelled opening chapters. Set missing times with Intro & credits in the player.",12));
        var credits=new CheckBox{Content="Mark watched when credits begin",IsChecked=store.State.MarkWatchedAtCredits};
        credits.Click+=(_,_)=>{store.State.MarkWatchedAtCredits=credits.IsChecked==true;store.Save();};ContentPanel.Children.Add(credits);
        ContentPanel.Children.Add(Body("Uses labelled ending chapters or saved credits times. Without a marker, uses the final 5% (up to 90 seconds for episodes or 3 minutes for movies). Credits keep playing; auto-next waits until the actual end.",12));
        var spoilers=new CheckBox{Content="Hide descriptions of episodes I haven't finished",IsChecked=store.State.HideSpoilers};
        spoilers.Click+=(_,_)=>{store.State.HideSpoilers=spoilers.IsChecked==true;store.Save();};ContentPanel.Children.Add(spoilers);
        var subs=new CheckBox { Content="Prefer English subtitles when available",IsChecked=store.State.Subtitles=="English" };
        subs.Click+=(_,_)=> { store.State.Subtitles=subs.IsChecked==true?"English":"Off";store.Save(); };ContentPanel.Children.Add(subs);
        RenderSubtitleAppearance(ContentPanel);
        RenderCommunitySettings();
        ContentPanel.Children.Add(Heading("Artwork & information","Saved on this PC, available offline."));
        var online=new CheckBox {Content="Download missing artwork and information when online",IsChecked=store.State.OnlineMetadata};
        online.Click+=async(_,_)=> {store.State.OnlineMetadata=online.IsChecked==true;store.Save();if(store.State.OnlineMetadata) await RefreshMetadata();};ContentPanel.Children.Add(online);
        ContentPanel.Children.Add(Body("Noir checks titles online and keeps downloaded details and pictures. Your videos stay on this PC. Removing a media folder keeps your watch history and its artwork.",12));
        ContentPanel.Children.Add(ActionButton("Refresh artwork & information",async()=> {Status.Text="Checking for new files…";await ScanLibrary(false);await RefreshMetadata(true);}));
        ContentPanel.Children.Add(Heading("Keep your library","History, My List, pictures and settings can be backed up together."));
        ContentPanel.Children.Add(ActionButton("Export library backup",async()=>
        {
            var dialog=new Microsoft.Win32.SaveFileDialog{Title="Save Noir library backup",Filter="Noir backup|*.zip",FileName="Noir-backup-"+DateTime.Now.ToString("yyyy-MM-dd")+".zip"};
            if(dialog.ShowDialog(this)!=true) return;
            try {await Task.Run(()=>store.ExportBackup(dialog.FileName));Status.Text="Library backup saved.";}
            catch(Exception e) {store.Log(e);Status.Text="Could not save the backup: "+e.Message;}
        }));
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
        ContentPanel.Children.Add(Heading("Keyboard shortcuts"));
        ContentPanel.Children.Add(Body("Space · Play / pause\n← / → · Seek 10 seconds\n↑ / ↓ · Change volume\nF · Fullscreen\nEscape · Leave fullscreen or return to library\nM · Mute\nI · Skip a known intro",14));
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

