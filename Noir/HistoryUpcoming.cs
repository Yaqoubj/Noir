using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Noir;
// The tracker's calendar: what just came out (check it off, get it, or stream it), a month grid
// of air dates for the shows you follow, and the day-by-day agenda after it.
public partial class MainWindow
{
    string upcomingScope="My shows";
    DateTime upcomingMonth=new(DateTime.Today.Year,DateTime.Today.Month,1);
    DateTime? upcomingDay;
    sealed record Airing(Tracked Show,KnownEpisode Episode,VideoItem? Item,DateTime Local);
    bool FollowsForCalendar(Tracked t)=>t.Status is not ("Dropped" or "Unfinished")&&!store.State.HiddenTitles.Contains(t.Group[0].GroupKey)&&(upcomingScope=="Everything"||t.Watched>0||store.State.Watchlist.Contains(t.Group[0].GroupKey)||store.State.TitleStatuses.GetValueOrDefault(t.Group[0].GroupKey) is "Watching" or "Planned");
    List<Airing> Airings(IEnumerable<Tracked> shows)
    {
        var result=new List<Airing>();
        foreach(var t in shows)
        {
            var schedule=store.State.EpisodeSchedules.GetValueOrDefault(t.Group[0].GroupKey);if(schedule==null)continue;
            foreach(var e in schedule.Where(e=>e.Season>0&&e.Episode>0&&e.AirsAt!=null).DistinctBy(e=>(e.Season,e.Episode)))
                result.Add(new(t,e,t.Group.FirstOrDefault(i=>i.Season==e.Season&&i.Episode==e.Episode),e.AirsAt!.Value.ToLocalTime().DateTime));
        }
        return result.OrderBy(a=>a.Local).ThenBy(a=>a.Show.Group[0].Title).ToList();
    }
    static string Premiere(Airing a)
    {
        // No "finale": schedules often list only the episodes announced so far.
        return a.Episode.Episode!=1?"":a.Episode.Season==1?"Series premiere":"Season premiere";
    }
    void RenderUpcoming()
    {
        var now=DateTimeOffset.UtcNow;var today=DateTime.Today;
        var shows=TrackedShows().Where(FollowsForCalendar).Where(t=>t.Group.Any(TrackerFits)).ToList();
        var all=Airings(shows);
        var bar=new DockPanel{Margin=new(0,6,0,0)};var export=ActionButton(T("Export calendar (.ics)","تصدير التقويم (.ics)"),ExportUpcomingCalendar);export.Margin=new(8,14,0,8);export.Padding=new(14,8,14,8);DockPanel.SetDock(export,Dock.Right);bar.Children.Add(export);
        var alerts=ActionButton(store.State.EpisodeAlerts?"🔔  New-episode alerts on":"🔕  New-episode alerts off",()=>{store.State.EpisodeAlerts=!store.State.EpisodeAlerts;store.Save();RenderQuiet();});
        alerts.Style=(Style)FindResource("Pill");alerts.Padding=new(16,8,16,8);alerts.Margin=new(0,14,0,8);alerts.ToolTip="A notification near the clock when an episode of a show you follow comes out";DockPanel.SetDock(alerts,Dock.Right);bar.Children.Add(alerts);
        bar.Children.Add(Chips(["My shows","Everything"],upcomingScope,v=>{upcomingScope=v;upcomingDay=null;Render();}));ContentPanel.Children.Add(bar);
        if(all.Count==0)
        {
            ContentPanel.Children.Add(Heading("Nothing on the calendar yet",shows.Count==0?"Log a show you're watching and its next episodes appear here with their air dates.":"Air dates appear once Noir has looked your shows up online (Settings → Library → artwork & information)."));
            ContentPanel.Children.Add(ActionButton("＋  Log a show",()=>OpenLog(null),true));return;
        }
        // Out now: aired in the last two weeks and not checked off yet.
        var fresh=all.Where(a=>a.Episode.AirsAt<=now&&a.Episode.AirsAt>now.AddDays(-14)&&(a.Item==null||!store.EverWatched(a.Item))).OrderByDescending(a=>a.Local).ToList();
        if(fresh.Count>0){ContentPanel.Children.Add(Heading("Out now",$"{fresh.Count} new episode{(fresh.Count==1?"":"s")} from the last two weeks. Watch, get it, or check it off."));ContentPanel.Children.Add(TrackerGrid(fresh.Take(18).Select(a=>OutNowRow(a,all))));}
        ContentPanel.Children.Add(MonthCalendar(all,today));
        var agenda=upcomingDay is {} day?all.Where(a=>a.Local.Date==day).ToList():all.Where(a=>a.Episode.AirsAt>now&&a.Local<today.AddDays(120)).ToList();
        var title=upcomingDay is {} d?(d==today?"Today":d==today.AddDays(1)?"Tomorrow":d.ToString(d.Year==today.Year?"dddd, d MMMM":"dddd, d MMMM yyyy")):"Coming up";
        var head=Heading(title,upcomingDay!=null?$"{agenda.Count} episode{(agenda.Count==1?"":"s")}":agenda.Count==0?"No announced episodes in the next four months.":$"{agenda.Count} announced episode{(agenda.Count==1?"":"s")} in the next four months.");
        if(upcomingDay!=null){var clear=ActionButton("Show everything coming up",()=>{upcomingDay=null;RenderQuiet();});clear.Margin=new(0,8,0,0);head.Children.Add(clear);}
        ContentPanel.Children.Add(head);
        foreach(var group in agenda.GroupBy(a=>a.Local.Date).Take(upcomingDay!=null?1:40))
        {
            if(upcomingDay==null){var label=group.Key==today?"Today":group.Key==today.AddDays(1)?"Tomorrow":group.Key.ToString(group.Key.Year==today.Year?"dddd, d MMMM":"dddd, d MMMM yyyy");var line=new DockPanel{Margin=new(0,18,0,8)};var count=Text($"{group.Count()} episode{(group.Count()==1?"":"s")}",12,"#817E78");count.VerticalAlignment=VerticalAlignment.Bottom;DockPanel.SetDock(count,Dock.Right);line.Children.Add(count);line.Children.Add(Text(label,17,weight:FontWeights.SemiBold));ContentPanel.Children.Add(line);}
            foreach(var a in group)ContentPanel.Children.Add(AgendaRow(a,all));
        }
        // Followed shows that are caught up with nothing announced: TV Time's "waiting for news".
        var waiting=shows.Where(t=>t.Status is "Caught up" or "Finished"&&t.Watched>0&&!all.Any(a=>a.Show==t&&a.Episode.AirsAt>now)).OrderByDescending(t=>t.Last).Take(24).ToList();
        if(waiting.Count>0&&upcomingDay==null)ContentPanel.Children.Add(Rail("No new episodes announced","You're caught up. They appear on the calendar as soon as a date is set.",waiting.Select(TrackerPoster).ToList()));
    }
    FrameworkElement MonthCalendar(List<Airing> all,DateTime today)
    {
        var shell=new Border{Background=Brush("#141416"),CornerRadius=new(16),Padding=new(18,14,18,16),Margin=new(0,22,0,4)};var body=new StackPanel();shell.Child=body;
        var head=new DockPanel{Margin=new(0,0,0,10)};
        Button Nav(string glyph,string tip,int months){var b=ActionButton(glyph,()=>{upcomingMonth=upcomingMonth.AddMonths(months);upcomingDay=null;RenderQuiet();});b.ToolTip=tip;b.FontSize=18;b.Width=38;b.Height=34;b.Padding=new(0);b.Margin=new(8,0,0,0);b.Background=Brush("#1FFFFFFF");return b;}
        var nav=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(nav,Dock.Right);
        if(upcomingMonth!=new DateTime(today.Year,today.Month,1)){var back=ActionButton("This month",()=>{upcomingMonth=new(today.Year,today.Month,1);upcomingDay=null;RenderQuiet();});back.Padding=new(12,6,12,6);back.Background=Brush("Transparent");back.Foreground=Brush("#E4C084");nav.Children.Add(back);}
        nav.Children.Add(Nav("‹","Previous month",-1));nav.Children.Add(Nav("›","Next month",1));head.Children.Add(nav);
        var inMonth=all.Where(a=>a.Local.Year==upcomingMonth.Year&&a.Local.Month==upcomingMonth.Month).ToList();
        var title=new StackPanel();title.Children.Add(Text(upcomingMonth.ToString("MMMM yyyy"),20,weight:FontWeights.SemiBold));title.Children.Add(Text($"{inMonth.Count} episode{(inMonth.Count==1?"":"s")} · {inMonth.Select(a=>a.Show).Distinct().Count()} show{(inMonth.Select(a=>a.Show).Distinct().Count()==1?"":"s")}",12,"#9F9A93"));head.Children.Add(title);body.Children.Add(head);
        var grid=new UniformGrid{Columns=7};
        foreach(var d in System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames.Skip(1).Append(System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[0]))grid.Children.Add(new TextBlock{Text=d.ToUpperInvariant(),FontSize=10.5,FontWeight=FontWeights.SemiBold,Foreground=Brush("#817E78"),Margin=new(8,0,0,6)});
        for(int i=0;i<((int)upcomingMonth.DayOfWeek+6)%7;i++)grid.Children.Add(new Border());
        for(var day=upcomingMonth;day.Month==upcomingMonth.Month;day=day.AddDays(1))
        {
            var value=day;var episodes=inMonth.Where(a=>a.Local.Date==value).ToList();var selected=upcomingDay==value;var isToday=value==today;
            var cell=new StackPanel{Margin=new(6,6,6,4)};
            var number=new TextBlock{Text=value.Day.ToString(),FontSize=13,FontWeight=isToday?FontWeights.Bold:FontWeights.Normal,Foreground=Brush(isToday?"#16120C":value<today?"#6B665E":"#E9E4DC"),Padding=new(6,1,6,1)};
            cell.Children.Add(new Border{Background=Brush(isToday?"#E4C084":"Transparent"),CornerRadius=new(9),HorizontalAlignment=HorizontalAlignment.Left,Child=number});
            foreach(var a in episodes.DistinctBy(a=>a.Show).Take(2))
            {
                var seen=a.Item!=null&&store.EverWatched(a.Item);var chip=Text((seen?"✓ ":"")+a.Show.Group[0].Title,10.5,value<today?"#9F9A93":"#F4F1EB");chip.TextWrapping=TextWrapping.NoWrap;chip.TextTrimming=TextTrimming.CharacterEllipsis;
                cell.Children.Add(new Border{Background=Brush(value<today?"#1FFFFFFF":"#40E4C084"),CornerRadius=new(5),Padding=new(6,2,6,2),Margin=new(0,4,0,0),Child=chip});
            }
            var more=episodes.DistinctBy(a=>a.Show).Count()-2;if(more>0)cell.Children.Add(Text($"+{more} more",10,"#9F9A93"));
            if(episodes.Count==0){grid.Children.Add(new Border{Child=cell,Margin=new(2),MinHeight=86,CornerRadius=new(10),BorderBrush=Brush("#0FFFFFFF"),BorderThickness=new(1)});continue;}
            var button=new Button{Content=cell,Padding=new(0),Margin=new(2),MinHeight=86,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top,Background=Brush(selected?"#33E4C084":"#1C1C1F"),BorderBrush=Brush(selected?"#E4C084":"#00000000"),BorderThickness=new(1),
                ToolTip=string.Join("\n",episodes.Select(a=>$"{a.Show.Group[0].Title}  S{a.Episode.Season:00}E{a.Episode.Episode:00}"))};
            System.Windows.Automation.AutomationProperties.SetName(button,value.ToString("d MMMM")+(episodes.Count>0?$", {episodes.Count} episodes":""));
            button.Click+=(_,_)=>{upcomingDay=upcomingDay==value?null:value;RenderQuiet();};grid.Children.Add(button);
        }
        body.Children.Add(grid);return shell;
    }
    string EpisodeLine(Airing a)=>$"S{a.Episode.Season:00} · E{a.Episode.Episode:00}"+(a.Episode.Title.Length>0&&!(store.State.HideSpoilers&&a.Item!=null&&!store.EverWatched(a.Item)&&a.Episode.AirsAt<=DateTimeOffset.UtcNow&&IsSpoiler(a))?"   "+a.Episode.Title:"");
    // Generic titles ("Episode 4") are never spoilers; named ones are hidden only once they're out and unwatched.
    static bool IsSpoiler(Airing a)=>!System.Text.RegularExpressions.Regex.IsMatch(a.Episode.Title,@"^(Episode|Chapter)\s*\d+$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    FrameworkElement AgendaRow(Airing a,List<Airing> all)
    {
        var show=a.Show.Group[0];var card=new Border{Background=Brush("#141416"),CornerRadius=new(12),Padding=new(10),Margin=new(0,0,0,8),BorderBrush=Brush("#00000000"),BorderThickness=new(1)};
        var g=new Grid();card.Child=g;g.ColumnDefinitions.Add(new(){Width=new(64)});g.ColumnDefinitions.Add(new(){Width=new(52)});g.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});g.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var time=Text(a.Local.TimeOfDay==TimeSpan.Zero?"—":a.Local.ToString("HH:mm"),13,"#817E78");time.VerticalAlignment=VerticalAlignment.Center;time.Margin=new(6,0,0,0);g.Children.Add(time);
        var poster=new Border{CornerRadius=new(6),ClipToBounds=true,Child=Art(show.Poster,show.Title,72,true),Height=72};Grid.SetColumn(poster,1);g.Children.Add(poster);
        var info=new StackPanel{Margin=new(14,0,12,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(info,2);g.Children.Add(info);
        var title=ActionButton("",()=>OpenHistoryTitle(a.Show.Group));title.Content=Text(show.Title,15.5,weight:FontWeights.SemiBold);title.Background=Brush("Transparent");title.Padding=new(0);info.Children.Add(title);
        var line=Text(EpisodeLine(a),12.5,"#E4C084");line.Margin=new(0,3,0,0);line.TextTrimming=TextTrimming.CharacterEllipsis;line.TextWrapping=TextWrapping.NoWrap;info.Children.Add(line);
        var tag=Premiere(a);if(tag.Length>0)info.Children.Add(new Border{Background=Brush("#33E4C084"),CornerRadius=new(5),Padding=new(7,2,7,2),Margin=new(0,6,0,0),HorizontalAlignment=HorizontalAlignment.Left,Child=Text(tag,10.5,"#E4C084")});
        var tools=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(tools,3);g.Children.Add(tools);
        if(a.Episode.AirsAt>DateTimeOffset.UtcNow)tools.Children.Add(new Border{Background=Brush("#E4C084"),CornerRadius=new(6),Padding=new(9,3,9,3),Child=Text(When(a.Episode.AirsAt!.Value),11,"#16120C",FontWeights.Bold)});
        else if(a.Item!=null)tools.Children.Add(CheckCircle(store.EverWatched(a.Item),store.EverWatched(a.Item)?"Watched":"Mark watched",()=>{if(store.EverWatched(a.Item))SmartWatchedClick(a.Item);else SmartCheckOff(a.Item,$"{show.Title} S{a.Episode.Season:00}E{a.Episode.Episode:00} watched");},38));
        card.MouseEnter+=(_,_)=>card.BorderBrush=Brush("#2EFFFFFF");card.MouseLeave+=(_,_)=>card.BorderBrush=Brush("#00000000");if(!TrackerOnly&&a.Item!=null){LazyMenu(card,a.Item);tools.Children.Add(MoreButton(a.Item));}
        return card;
    }
    FrameworkElement OutNowRow(Airing a,List<Airing> all)
    {
        var show=a.Show.Group[0];var item=a.Item;
        var card=new Border{Background=Brush("#161618"),CornerRadius=new(14),BorderBrush=Brush("#00000000"),BorderThickness=new(1.5),Padding=new(12)};
        var g=new Grid();g.ColumnDefinitions.Add(new(){Width=new(78)});g.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});g.ColumnDefinitions.Add(new(){Width=GridLength.Auto});card.Child=g;
        var poster=new Button{Content=Art(show.Poster,show.Title,117,true),Padding=new(0),Height=117,Background=Brush("Transparent"),ToolTip="Open "+show.Title};poster.Click+=(_,_)=>OpenHistoryTitle(a.Show.Group);
        g.Children.Add(new Border{CornerRadius=new(8),ClipToBounds=true,Child=poster,VerticalAlignment=VerticalAlignment.Center});
        var info=new StackPanel{Margin=new(16,2,12,2),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(info,1);g.Children.Add(info);
        var badge=new Border{Background=Brush("#E4C084"),CornerRadius=new(5),Padding=new(7,2,7,2),HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,6),Child=Text(Premiere(a) is {Length:>0} p?p.ToUpperInvariant():"NEW EPISODE",9.5,"#16120C",FontWeights.Bold)};info.Children.Add(badge);
        var title=Text(show.Title,17,weight:FontWeights.SemiBold);title.TextTrimming=TextTrimming.CharacterEllipsis;title.TextWrapping=TextWrapping.NoWrap;info.Children.Add(title);
        var line=Text(EpisodeLine(a),13.5,"#E4C084");line.Margin=new(0,4,0,0);line.TextTrimming=TextTrimming.CharacterEllipsis;line.TextWrapping=TextWrapping.NoWrap;info.Children.Add(line);
        info.Children.Add(Text("Aired "+(a.Local.Date==DateTime.Today?"today":a.Local.Date==DateTime.Today.AddDays(-1)?"yesterday":a.Local.ToString("dddd d MMM"))+(!TrackerOnly&&item!=null&&CanPlay(item)?"  ·  On your PC":""),12,"#9F9A93"));
        var actions=new WrapPanel{Margin=new(0,8,0,0)};info.Children.Add(actions);
        Button Small(string label,string tip,Action act,bool primary=false){var b=ActionButton(label,act,primary);b.Padding=new(12,6,12,6);b.Margin=new(0,0,8,4);b.FontSize=12.5;b.ToolTip=tip;actions.Children.Add(b);return b;}
        if(!TrackerOnly&&item!=null&&CanPlay(item))Small("▶  Play","Play "+item.EpisodeLabel,()=>_=PlayVideo(item),true);
        else if(!TrackerOnly&&item!=null)
        {
            if(StreamOffered&&ActiveDownload(item)==null)Small("▶  Stream","Stream "+item.EpisodeLabel+" through AIOStreams",()=>StreamItem(item),true);
            Small(ActiveDownload(item)!=null?"Downloading…":"↓  Get","Find this episode for your download service",()=>{if(ActiveDownload(item)!=null)Navigate("Downloads");else FindDownloads(item);});
        }
        if(!TrackerOnly&&show.ImdbId.Length>0)Small("Stremio","Stream it in Stremio with your add-ons",()=>OpenInStremio(show.ImdbId,false,a.Episode.Season,a.Episode.Episode));
        if(!TrackerOnly&&show.ImdbId.Length>0)Small("Nuvio","Open it in Nuvio",()=>OpenInNuvio(show.ImdbId,false,show.Title));
        if(item!=null)
        {
            var tools=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(tools,2);g.Children.Add(tools);
            tools.Children.Add(CheckCircle(false,"Mark watched",()=>SmartCheckOff(item,$"{show.Title} S{a.Episode.Season:00}E{a.Episode.Episode:00} watched"),54));
            if(!TrackerOnly){LazyMenu(card,item);tools.Children.Add(MoreButton(item));}
        }
        card.MouseEnter+=(_,_)=>card.BorderBrush=Brush("#40E4C084");card.MouseLeave+=(_,_)=>card.BorderBrush=Brush("#00000000");
        return card;
    }
}
