using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Noir;
// Discover: a cinematic spotlight and rows when browsing, a poster grid for searches, and a
// full-bleed title page with season pills and episode cards.
public partial class MainWindow
{
    bool DiscoverCinematic=>page=="Discover"&&(discoverTitle!=null||SearchBox.Text.Trim().Length==0&&discoverResults is {Titles.Count:>0}&&discoverQuery.Length==0&&discoverResultsFilter==discoverFilter);
    void RenderDiscover()
    {
        cinematic=DiscoverCinematic;ApplyPageLayout();
        PageTitle.Text=discoverTitle?.Name??(SearchBox.Text.Trim().Length>0?"Results":"Discover");
        if(discoverTitle!=null){RenderDiscoverDetail(discoverTitle);return;}
        if(discoverResults==null||discoverQuery!=SearchBox.Text.Trim()||discoverResultsFilter!=discoverFilter){RenderDiscoverLoading(SearchBox.Text.Trim());_=SearchDiscover();return;}
        var token=discoverCancellation?.Token??BeginDiscover();var revision=discoverRevision;
        if(discoverResults.Titles.Count==0)
        {
            ContentPanel.Children.Add(DiscoverFilters());
            ContentPanel.Children.Add(Heading("Nothing found","Try another title, or check your connection."));ContentPanel.Children.Add(ActionButton("Try again",()=>_=SearchDiscover(),true));return;
        }
        if(discoverQuery.Length==0)
        {
            var spotlight=discoverResults.Titles.FirstOrDefault(t=>t.BackgroundUrl.Length>0)??discoverResults.Titles[0];
            ContentPanel.Children.Add(DiscoverHero(spotlight,token,revision,false));
            ContentPanel.Children.Add(DiscoverFilters());
            if(discoverMessage.Length>0)ContentPanel.Children.Add(Body(discoverMessage,12));
            var rest=discoverResults.Titles.Where(t=>t!=spotlight).ToList();
            if(discoverFilter=="All")
            {
                ContentPanel.Children.Add(Rail("Popular shows","Trending series, including anime.",rest.Where(t=>!t.IsMovie).Take(30).Select(t=>DiscoverCard(t,token,revision)).ToList(),()=>{discoverFilter="TV shows";_=SearchDiscover();}));
                ContentPanel.Children.Add(Rail("Popular movies","What everyone is watching right now.",rest.Where(t=>t.IsMovie).Take(30).Select(t=>DiscoverCard(t,token,revision)).ToList(),()=>{discoverFilter="Movies";_=SearchDiscover();}));
                var inLibrary=rest.Where(t=>store.Archive.Any(i=>i.ImdbId==t.Id)).ToList();
                if(inLibrary.Count>0)ContentPanel.Children.Add(Rail("Already in your library",string.Empty,inLibrary.Select(t=>DiscoverCard(t,token,revision)).ToList()));
            }
            else DiscoverGrid(rest,token,revision,discoverFilter=="Movies"?"Popular movies":"Popular shows");
        }
        else
        {
            ContentPanel.Children.Add(DiscoverFilters());
            DiscoverGrid(discoverResults.Titles,token,revision,$"{discoverResults.Titles.Count} results for “{discoverQuery}”");
        }
        var credit=Body("Title information and artwork: Cinemeta. Downloads use your AIOStreams and TorBox connection."+(discoverResults.Cached?" Showing cached results.":""),11);credit.Margin=new(0,30,0,10);ContentPanel.Children.Add(credit);
    }
    FrameworkElement DiscoverFilters()
    {
        var row=Chips(["All","Movies","TV shows"],discoverFilter,label=>{discoverFilter=label;_=SearchDiscover();});
        ((FrameworkElement)row).Margin=new(0,cinematic?8:4,0,0);return row;
    }
    void DiscoverGrid(List<DiscoverTitle> titles,CancellationToken token,int revision,string heading)
    {
        ContentPanel.Children.Add(Heading(heading));
        var panel=new WrapPanel();ContentPanel.Children.Add(panel);var rendered=0;var more=ActionButton("",()=>{});more.Style=(Style)FindResource("Pill");more.Padding=new(20,10,20,10);more.HorizontalAlignment=HorizontalAlignment.Center;more.Margin=new(0,16,0,0);
        void Batch(){foreach(var t in titles.Skip(rendered).Take(30))panel.Children.Add(DiscoverCard(t,token,revision));rendered=Math.Min(rendered+30,titles.Count);more.Content=$"Show more · {titles.Count-rendered} remaining";more.Visibility=rendered<titles.Count?Visibility.Visible:Visibility.Collapsed;}
        Batch();more.Click+=(_,_)=>Batch();ContentPanel.Children.Add(more);
    }
    // Placeholder cards with a soft pulse while results load, instead of a line of text.
    void RenderDiscoverLoading(string query)
    {
        cinematic=false;ApplyPageLayout();
        ContentPanel.Children.Add(DiscoverFilters());
        ContentPanel.Children.Add(Heading(query.Length>0?$"Searching for “{query}”…":"Finding popular movies & shows…"));
        var row=new WrapPanel();var pulse=new DoubleAnimation(.35,.8,TimeSpan.FromMilliseconds(900)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever};
        for(int i=0;i<12;i++){var card=new StackPanel{Width=176,Margin=new(0,0,18,22)};card.Children.Add(new Border{Height=264,CornerRadius=new(10),Background=Brush("#1C1C1F")});card.Children.Add(new Border{Height=12,Width=120,CornerRadius=new(6),Background=Brush("#1C1C1F"),Margin=new(0,10,0,0),HorizontalAlignment=HorizontalAlignment.Left});card.BeginAnimation(OpacityProperty,pulse);row.Children.Add(card);}
        ContentPanel.Children.Add(row);
    }
    FrameworkElement DiscoverCard(DiscoverTitle title,CancellationToken token,int revision)
    {
        var body=new StackPanel{Width=176,Margin=new(0,0,18,8)};
        var picture=new Grid{Height=264,ClipToBounds=true};picture.Children.Add(Art(title.Poster,title.Name,264,true));
        if(title.Rating.Length>0)picture.Children.Add(new Border{IsHitTestVisible=false,Background=Brush("#E6101012"),CornerRadius=new(6),Padding=new(7,3,7,3),Margin=new(8),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Child=Text("★ "+title.Rating,11,"#E4C084",FontWeights.SemiBold)});
        var existing=store.Archive.Where(i=>i.ImdbId==title.Id&&i.IsMovie==title.IsMovie).ToList();
        if(existing.Count>0)picture.Children.Add(new Border{IsHitTestVisible=false,Background=Brush("#E4C084"),CornerRadius=new(6),Padding=new(7,3,7,3),Margin=new(8),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,Child=Text(existing.Any(CanPlay)?"ON YOUR PC":store.State.Watchlist.Contains(existing[0].GroupKey)?"IN MY LIST":"IN LIBRARY",10,"#16120C",FontWeights.Bold)});
        var button=new Button{Content=picture,Padding=new(0),Background=Brush("Transparent"),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip="Explore "+title.Name};button.Click+=(_,_)=>_=OpenDiscover(title);
        var edge=new Border{CornerRadius=new(10),ClipToBounds=true,Child=button,BorderThickness=new(1.5),BorderBrush=Brush("#00000000")};body.Children.Add(edge);
        var name=Text(title.Name,14,weight:FontWeights.SemiBold);name.Margin=new(0,9,0,2);name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextWrapping=TextWrapping.NoWrap;body.Children.Add(name);
        body.Children.Add(Text((title.IsMovie?"Movie":"Series")+(title.Years.Length>0?" · "+title.Years:""),11,"#9F9A93"));
        if(!File.Exists(title.Poster))_=Load();
        async Task Load(){try{using var client=DiscoverClient();await client.Artwork(title,token);if(token.IsCancellationRequested||revision!=discoverRevision||page!="Discover"||closing)return;var source=ImageSource(title.Poster,384);if(source!=null){picture.Children.RemoveAt(0);var image=new Image{Source=source,Stretch=Stretch.UniformToFill};picture.Children.Insert(0,image);image.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(350)));}}catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception e){store.Log(e);}}
        return Lift(body,edge);
    }
    // Full-bleed artwork for a Discover title; the background is fetched if it isn't cached yet.
    FrameworkElement DiscoverHero(DiscoverTitle title,CancellationToken token,int revision,bool detail)
    {
        var grid=new Grid{Height=HeroHeight(detail),ClipToBounds=true,Background=Brush("#151517")};
        var image=new Image{Stretch=Stretch.UniformToFill,VerticalAlignment=VerticalAlignment.Top};grid.Children.Add(image);
        void Paint(){var source=ImageSource(File.Exists(title.Background)?title.Background:title.Poster,1920);if(source!=null){image.Source=source;image.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(500)));}}
        if(File.Exists(title.Background))Paint();else _=Fetch();
        async Task Fetch(){try{using var client=DiscoverClient();await client.Artwork(title,token,true);if(!token.IsCancellationRequested&&revision==discoverRevision&&!closing)Paint();}catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception e){store.Log(e);}}
        var side=new LinearGradientBrush{StartPoint=new(0,.5),EndPoint=new(1,.5)};side.GradientStops.Add(new(Color.FromArgb(245,11,11,13),0));side.GradientStops.Add(new(Color.FromArgb(160,11,11,13),.4));side.GradientStops.Add(new(Color.FromArgb(0,11,11,13),.75));
        var floor=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1)};floor.GradientStops.Add(new(Color.FromArgb(150,11,11,13),0));floor.GradientStops.Add(new(Color.FromArgb(0,11,11,13),.22));floor.GradientStops.Add(new(Color.FromArgb(0,11,11,13),.55));floor.GradientStops.Add(new(Color.FromArgb(255,11,11,13),1));
        grid.Children.Add(new Border{Background=side,IsHitTestVisible=false});grid.Children.Add(new Border{Background=floor,IsHitTestVisible=false});
        var panel=new StackPanel{Margin=new(sidePad,0,sidePad,detail?42:56),MaxWidth=700,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom};
        panel.Children.Add(new Border{Background=Brush("#33E4C084"),BorderBrush=Brush("#66E4C084"),BorderThickness=new(1),CornerRadius=new(4),Padding=new(9,3,9,3),HorizontalAlignment=HorizontalAlignment.Left,Child=Text(detail?(title.IsMovie?"MOVIE":"SERIES"):"SPOTLIGHT · "+(title.IsMovie?"MOVIE":"SERIES"),11,"#F0D3A0",FontWeights.SemiBold)});
        var name=Text(title.Name,title.Name.Length>24?50:66,weight:FontWeights.Bold);name.FontFamily=new FontFamily("Segoe UI Variable Display, Segoe UI");name.LineHeight=title.Name.Length>24?56:72;name.Margin=new(0,14,0,10);panel.Children.Add(name);
        var facts=new TextBlock{FontSize=14,Foreground=Brush("#D9D4CC"),TextWrapping=TextWrapping.Wrap};
        if(title.Rating.Length>0)facts.Inlines.Add(new System.Windows.Documents.Run("★ "+title.Rating+"  IMDb      "){Foreground=Brush("#E4C084")});
        facts.Inlines.Add(new System.Windows.Documents.Run(string.Join("   ·   ",new[]{title.Years}.Concat(title.Genres.Take(3)).Where(x=>x.Length>0))));panel.Children.Add(facts);
        if(title.Summary.Length>0){var summary=Text(title.Summary,15,"#C9C4BD");summary.MaxHeight=66;summary.LineHeight=22;summary.TextTrimming=TextTrimming.WordEllipsis;summary.Margin=new(0,12,0,0);summary.MaxWidth=620;summary.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(summary);}
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,26,0,0)};
        var existing=store.Archive.FirstOrDefault(i=>i.ImdbId==title.Id&&i.IsMovie==title.IsMovie);
        if(!detail)actions.Children.Add(BigButton("ⓘ   Explore",()=>_=OpenDiscover(title),true));
        else if(title.IsMovie)actions.Children.Add(BigButton(existing!=null&&CanPlay(existing)?"▶   Play":"↓   Find movie downloads",()=>{if(existing!=null&&CanPlay(existing))_=PlayVideo(existing);else DiscoverDownload(title,0,0,false);},true));
        if(detail&&existing!=null)actions.Children.Add(BigButton("Open in library",()=>{var group=store.EpisodesFor(existing.GroupKey);Navigate(existing.IsMovie?"Movies":"Shows");OpenDetails(group);},false));
        var listed=existing!=null&&store.State.Watchlist.Contains(existing.GroupKey);
        actions.Children.Add(RoundAction(listed?"✓":"＋",listed?"In My List":"Add to My List",()=>{store.AddDiscovered(title,true);Render();Status.Text="Saved to My List. Watching history stays unchanged.";},listed));
        panel.Children.Add(actions);grid.Children.Add(panel);FitHero(grid,panel,detail);
        return Bleed(grid,detail?6:0);
    }
    void DiscoverDownload(DiscoverTitle title,int season,int episode,bool whole)
    {
        try{var group=store.AddDiscovered(title,false);var item=title.IsMovie?group[0]:group.FirstOrDefault(i=>i.Season==season&&i.Episode==episode);if(item==null){Status.Text="This episode is missing from the title schedule. Refresh details.";return;}FindDownloads(item,whole);}
        catch(Exception e){store.Log(e);Status.Text="Couldn't prepare this title for downloading.";}
    }
    void RenderDiscoverDetail(DiscoverTitle title)
    {
        var token=discoverCancellation?.Token??BeginDiscover();var revision=discoverRevision;
        ContentPanel.Children.Add(DiscoverHero(title,token,revision,true));
        var back=ActionButton("←  Back to Discover",()=>{CancelDiscover();discoverTitle=null;Render();ContentScroll.ScrollToTop();});back.Background=Brush("Transparent");back.Foreground=Brush("#AAA6A0");back.Padding=new(0,4,0,4);back.HorizontalAlignment=HorizontalAlignment.Left;ContentPanel.Children.Add(back);
        if(title.IsMovie){ContentPanel.Children.Add(Body("Review the releases, quality and size before anything downloads. The movie joins your library when it finishes.",13));return;}
        var seasons=title.Episodes.Select(e=>e.Season).Distinct().Order().ToList();if(seasons.Count==0){ContentPanel.Children.Add(Body("No episode schedule is available for this title yet."));return;}
        if(!seasons.Contains(discoverSeason))discoverSeason=seasons[0];
        var existing=store.Archive.FirstOrDefault(i=>i.ImdbId==title.Id&&i.IsMovie==title.IsMovie);
        ContentPanel.Children.Add(UnderlineTabs(seasons.Select(s=>"Season "+s).ToArray(),"Season "+discoverSeason,label=>{discoverSeason=int.Parse(label[7..]);Render();}));
        var released=title.Episodes.Where(e=>e.Season==discoverSeason&&(e.AirsAt==null||e.AirsAt<=DateTimeOffset.UtcNow)).ToList();
        var tools=new DockPanel{Margin=new(0,10,0,16)};var seasonButton=ActionButton("Find season downloads",()=>DiscoverDownload(title,discoverSeason,released[0].Episode,true),true);seasonButton.IsEnabled=released.Count>0;seasonButton.Padding=new(20,11,22,11);DockPanel.SetDock(seasonButton,Dock.Right);tools.Children.Add(seasonButton);
        tools.Children.Add(Body($"{released.Count} of {title.Episodes.Count(e=>e.Season==discoverSeason)} episodes released. Choose one, or find a season pack; episodes you have are skipped.",13));ContentPanel.Children.Add(tools);
        var grid=new WrapPanel();ContentPanel.Children.Add(grid);
        foreach(var episode in title.Episodes.Where(e=>e.Season==discoverSeason))
        {
            var available=episode.AirsAt==null||episode.AirsAt<=DateTimeOffset.UtcNow;var local=existing==null?null:store.EpisodesFor(existing.GroupKey).FirstOrDefault(i=>i.Season==episode.Season&&i.Episode==episode.Episode);
            var card=new Border{Width=300,Margin=new(0,0,16,16),CornerRadius=new(12),Background=Brush("#161618"),BorderBrush=Brush("#24FFFFFF"),BorderThickness=new(1),Padding=new(16,14,16,14)};
            var stack=new StackPanel();card.Child=stack;
            var top=new DockPanel();var number=Text(episode.Episode.ToString("00"),30,"#3A362F",FontWeights.Bold);number.FontFamily=new FontFamily("Segoe UI Variable Display, Segoe UI");DockPanel.SetDock(number,Dock.Left);top.Children.Add(number);
            var state=Text(!available?"Airs "+episode.AirsAt?.ToLocalTime().ToString("d MMM yyyy"):local!=null&&CanPlay(local)?"On your PC":local!=null&&store.EverWatched(local)?"Watched":"Released",11,available?"#E4C084":"#9F9A93",FontWeights.SemiBold);state.HorizontalAlignment=HorizontalAlignment.Right;state.VerticalAlignment=VerticalAlignment.Center;top.Children.Add(state);stack.Children.Add(top);
            var name=Text(episode.Title.Length>0?episode.Title:"Episode "+episode.Episode,14.5,"#F4F1EB",FontWeights.SemiBold);name.Height=40;name.TextTrimming=TextTrimming.WordEllipsis;name.Margin=new(0,4,0,10);stack.Children.Add(name);
            var playable=local!=null&&CanPlay(local);var act=ActionButton(playable?"▶ Play":"↓ Download",()=>{if(playable)_=PlayVideo(local!);else DiscoverDownload(title,episode.Season,episode.Episode,false);},playable||available);act.IsEnabled=available;act.HorizontalAlignment=HorizontalAlignment.Left;act.Padding=new(14,7,14,7);stack.Children.Add(act);
            grid.Children.Add(Lift(card));
        }
        ContentPanel.Children.Add(Body("Information: Cinemeta · Artwork and schedules are cached for later browsing.",11));
    }
}
