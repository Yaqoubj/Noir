using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Noir;
// Shows, Movies and My List: a filter/sort bar, an A–Z jump strip and one poster grid whose
// cards reveal details and actions on hover.
public partial class MainWindow
{
    Dictionary<string,string[]>? libraryViews;
    string LibraryView=>LibraryPreference()[0];
    string LibrarySize=>LibraryPreference()[1];
    string[] LibraryPreference()
    {
        if(libraryViews==null){try{libraryViews=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string[]>>(System.IO.File.ReadAllText(System.IO.Path.Combine(store.DataDirectory,"library-views.json")))??new();}catch{libraryViews=new();}}
        if(!libraryViews.TryGetValue(page,out var value)||value.Length!=2||!new[]{"Poster","Landscape","List"}.Contains(value[0])||!new[]{"S","M","L"}.Contains(value[1]))libraryViews[page]=value=["Poster","M"];return value;
    }
    void SaveLibraryView(string view,string size){LibraryPreference();libraryViews![page]=[view,size];try{var path=System.IO.Path.Combine(store.DataDirectory,"library-views.json");var temp=path+".tmp";System.IO.File.WriteAllText(temp,System.Text.Json.JsonSerializer.Serialize(libraryViews),new System.Text.UTF8Encoding(false));System.IO.File.Move(temp,path,true);}catch(Exception e){store.Log(e);}}
    FrameworkElement LibraryListRow(List<VideoItem> group)
    {
        var item=group[0];var (watched,total)=Progress(group);var height=Density.LibraryListHeight(LibrarySize);var line=new DockPanel{Margin=new(12)};var picture=new Grid{Width=height*1.5,Height=height,ClipToBounds=true};picture.Children.Add(Art(System.IO.File.Exists(item.Backdrop)?item.Backdrop:item.Poster,item.Title,height));AddStatusPill(picture,item);DockPanel.SetDock(picture,Dock.Left);line.Children.Add(picture);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new(12,0,0,0)};DockPanel.SetDock(buttons,Dock.Right);line.Children.Add(buttons);var listed=store.State.Watchlist.Contains(item.GroupKey);var list=ActionButton(listed?"[[check]]":"[[plus]]",()=>ToggleWatchlist(item));list.ToolTip=T("My List");list.Margin=new(6,0,0,0);buttons.Children.Add(list);var more=MoreButton(item);more.Margin=new(6,0,0,0);buttons.Children.Add(more);
        var info=new StackPanel{Margin=new(16,0,8,0),VerticalAlignment=VerticalAlignment.Center};line.Children.Add(info);var title=Text(item.Title,16,weight:FontWeights.SemiBold);title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;info.Children.Add(title);info.Children.Add(Text(string.Join(" · ",new[]{item.Year>0?item.Year.ToString():"",item.Duration>0?Loc.Runtime(item.Duration):"",item.Genres}.Where(x=>x.Length>0)),12,"#AAA6A0"));info.Children.Add(Text(item.IsMovie?T(watched>0?"Watched":"Not started"):$"{watched} / {total} {T("watched")}",11,"#E4C084"));info.Children.Add(new ProgressBar{Height=3,Value=watched,Maximum=Math.Max(1,total),Margin=new(0,8,0,0),Background=Brush("#2A2A30"),Foreground=Brush("#E4C084"),BorderThickness=new(0)});
        var border=new Border{Width=Math.Max(440,ContentScroll.ViewportWidth-sidePad*2-10),CornerRadius=new(12),Margin=new(0,0,0,10),Background=Brush("#141416"),Child=line,BorderBrush=Brush("#1AFFFFFF"),BorderThickness=new(1),Cursor=System.Windows.Input.Cursors.Hand,Focusable=true};System.Windows.Automation.AutomationProperties.SetName(border,item.Title+" "+T("details","التفاصيل"));
        // The row itself opens the title now that the Details button is gone (U59).
        border.MouseLeftButtonUp+=(_,e)=>{for(var node=e.OriginalSource as DependencyObject;node!=null&&node!=border;node=node is Visual or System.Windows.Media.Media3D.Visual3D?System.Windows.Media.VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))if(node is System.Windows.Controls.Primitives.ButtonBase)return;OpenDetails(group);};
        border.KeyDown+=(_,e)=>{if(e.OriginalSource==border&&(e.Key==System.Windows.Input.Key.Enter||e.Key==System.Windows.Input.Key.Space)){e.Handled=true;OpenDetails(group);}};LazyMenu(border,item);return HoverCard(border,group);
    }
    // The My List chip is drawn only once there is a second collection to switch to (U59).
    void RenderCollectionsRow()
    {
        if(collectionFilter!="My List"&&!store.State.Collections.ContainsKey(collectionFilter))collectionFilter="My List";
        if(store.State.Collections.Count>0)ContentPanel.Children.Add(Chips(new[]{"My List"}.Concat(store.State.Collections.Keys.Order()).ToArray(),collectionFilter,v=>{collectionFilter=v;Render();}));
        ContentPanel.Children.Add(ActionButton("[[plus]] Create collection",()=>CollectionDialog(null)));
    }
    string libraryFilter="All",listType="All";
    static readonly string[] LibraryFilters=["All","Watching","Not started","Finished","On this PC"];
    static readonly string[] LibrarySorts=["A–Z","Recently watched","Recently added","Year"];
    (int Watched,int Total) Progress(List<VideoItem> group)
    {
        var item=group[0];var now=DateTimeOffset.UtcNow;
        var counted=group.Where(i=>item.IsMovie||i.Season>0&&store.Aired(i,now)).ToList();
        return(counted.Count(store.EverWatched),Math.Max(counted.Count,item.IsMovie?1:0));
    }
    string LibraryState(List<VideoItem> group)
    {
        var (watched,total)=Progress(group);
        return watched==0?(store.ContinueFor(group)!=null?"Watching":"Not started"):watched>=total?"Finished":"Watching";
    }
    void RenderLibraryPage(List<List<VideoItem>> groups)
    {
        if(page!="My List"&&libraryFilter=="Still planned")libraryFilter="All";
        if(page=="My List")
        {
            RenderCollectionsRow();
            if(groups.Count==0){ContentPanel.Children.Add(Heading("Your next watch","Use + My List on any title to keep it here."));return;}
            if(listType!="All")groups=groups.Where(g=>g[0].IsMovie==(listType=="Movies")).ToList();
        }
        var genres=groups.SelectMany(g=>g[0].Genres.Split('·',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)).Distinct().Order().ToList();
        if(genreFilter!="All genres"&&!genres.Contains(genreFilter))genreFilter="All genres";
        var smartFilter=SmartLists.Ids.Contains(libraryFilter)?BuildSmartRows(groups)[libraryFilter].Select(p=>p.Key).ToHashSet():null;
        var shown=groups.Where(g=>genreFilter=="All genres"||g[0].Genres.Split('·',StringSplitOptions.TrimEntries).Contains(genreFilter))
            .Where(g=>libraryFilter=="All"||(smartFilter!=null?smartFilter.Contains(g[0].GroupKey):libraryFilter=="Still planned"?store.TitleStatus(g)=="Planned"&&!g.Any(store.EverWatched):libraryFilter=="On this PC"?g.Any(CanPlay):LibraryState(g)==libraryFilter)).ToList();
        shown=(librarySort switch
        {
            "Recently watched"=>shown.OrderByDescending(g=>g.Max(HistoryDate)),
            "Recently added"=>shown.OrderByDescending(g=>g.Max(i=>i.AddedAt)),
            "Year"=>shown.OrderByDescending(g=>g[0].Year),
            _=>shown.OrderBy(g=>SortTitle(g[0].Title),StringComparer.OrdinalIgnoreCase)
        }).ToList();
        ContentPanel.Children.Add(LibraryToolbar(genres,shown.Count,groups.Count));
        if(shown.Count==0)
        {
            ContentPanel.Children.Add(Heading(groups.Count==0?"Your cinema starts here":"Nothing matches",groups.Count==0?"Add your movie and show folders in Settings.":"Try another filter."));
            if(groups.Count==0)ContentPanel.Children.Add(ActionButton("Add folders",()=>{settingsSection="Library & folders";Navigate("Settings");},true));
            else ContentPanel.Children.Add(ActionButton("Clear filters",()=>{libraryFilter="All";genreFilter="All genres";listType="All";Render();}));
            return;
        }
        var grid=new WrapPanel{Margin=new(0,6,0,0)};
        if(librarySort=="A–Z"&&shown.Count>12)ContentPanel.Children.Add(LetterStrip(shown,grid));
        ContentPanel.Children.Add(grid);
        var key=page+"|"+collectionFilter+"|"+libraryFilter+"|"+genreFilter+"|"+librarySort+"|"+listType+"|"+SearchBox.Text;
        var limit=Math.Min(shown.Count,cardLimits.GetValueOrDefault(key,48));
        foreach(var g in shown.Take(limit))grid.Children.Add(LibraryPoster(g));StaggerCards(grid);
        if(limit<shown.Count)
        {
            var more=ActionButton("",()=>{});more.Style=(Style)FindResource("Pill");more.Padding=new(20,10,20,10);more.HorizontalAlignment=HorizontalAlignment.Center;more.Margin=new(0,10,0,0);
            void Label()=>more.Content=$"{T("Show more")} · {shown.Count-limit} {T("remaining")}";Label();
            more.Click+=(_,_)=>{foreach(var g in shown.Skip(limit).Take(48))grid.Children.Add(LibraryPoster(g));limit=Math.Min(shown.Count,limit+48);cardLimits[key]=limit;Label();if(limit>=shown.Count)more.Visibility=Visibility.Collapsed;};
            grid.Tag=(Action<int>)(target=>{while(limit<=target&&limit<shown.Count){foreach(var g in shown.Skip(limit).Take(48))grid.Children.Add(LibraryPoster(g));limit=Math.Min(shown.Count,limit+48);}cardLimits[key]=limit;Label();if(limit>=shown.Count)more.Visibility=Visibility.Collapsed;});
            ContentPanel.Children.Add(more);
        }
    }
    static string SortTitle(string title)=>System.Text.RegularExpressions.Regex.Replace(title.Trim(),@"^(the|a|an)\s+","",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static string Letter(string title){var c=SortTitle(title).FirstOrDefault();return char.IsLetter(c)?char.ToUpperInvariant(c).ToString():"#";}
    FrameworkElement LibraryToolbar(List<string> genres,int shown,int total)
    {
        var bar=new Border{Background=Brush("#111113"),BorderBrush=Brush("#1AFFFFFF"),BorderThickness=new(1),CornerRadius=new(14),Padding=Density.Compact?new(12,8,12,8):new(14,10,14,10),Margin=new(0,10,0,10)};
        var dock=new StackPanel();bar.Child=dock;
        var right=new WrapPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,8,0,0)};DockPanel.SetDock(right,Dock.Right);dock.Children.Add(right);
        right.Children.Add(Select(["Poster","Landscape","List"],LibraryView,v=>{SaveLibraryView(v,LibrarySize);Render();},T("View","طريقة العرض")));
        right.Children.Add(Select(["S","M","L"],LibrarySize,v=>{SaveLibraryView(LibraryView,v);Render();},T("Card size","حجم البطاقة")));
        right.Children.Add(new TextBlock{Text=T("Sort"),Foreground=Brush("#8F8A83"),FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new(12,0,8,0)});
        right.Children.Add(Select(LibrarySorts,librarySort,v=>{librarySort=v;Render();},"Sort"));
        if(genres.Count>1)
        {
            right.Children.Add(new TextBlock{Text=T("Genre"),Foreground=Brush("#8F8A83"),FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new(14,0,8,0)});
            right.Children.Add(Select(new[]{"All genres"}.Concat(genres).ToArray(),genreFilter,v=>{genreFilter=v;Render();},"Genre"));
        }
        var left=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};dock.Children.Add(left);
        void Segment(string label,bool on,Action pick)
        {
            var b=new Button{Content=SmartLists.Ids.Contains(label)?SmartName(label):label=="Still planned"?StatusText(label):T(label),Padding=new(13,6,13,6),Margin=new(0,0,6,0),FontSize=12.5,Background=Brush(on?"#E4C084":"Transparent"),Foreground=Brush(on?"#16120C":"#C7C2BC"),FontWeight=on?FontWeights.SemiBold:FontWeights.Normal,Style=(Style)FindResource("Pill"),BorderBrush=Brush(on?"#E4C084":"#26FFFFFF")};
            System.Windows.Automation.AutomationProperties.SetName(b,SmartLists.Ids.Contains(label)?SmartName(label):label=="Still planned"?StatusText(label):T(label));b.Click+=(_,_)=>pick();left.Children.Add(b);
        }
        if(page=="My List")foreach(var t in new[]{"All","Shows","Movies"}){var v=t;Segment(v=="All"?"Everything":v,listType==v,()=>{listType=v;Render();});}
        if(page=="My List")left.Children.Add(new Border{Width=1,Height=20,Background=Brush("#26FFFFFF"),Margin=new(4,0,10,0)});
        foreach(var f in LibraryFilters.Concat(SmartLists.Ids).Concat(page=="My List"?new[]{"Still planned"}:Array.Empty<string>())){var v=f;Segment(v,libraryFilter==v,()=>{libraryFilter=v;Render();});}
        var count=new TextBlock{Text=shown==total?$"{total} {T("titles")}":$"{shown} {T("of")} {total}",Foreground=Brush("#8F8A83"),FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new(8,0,0,0)};left.Children.Add(count);
        return bar;
    }
    ComboBox Select(string[] options,string selected,Action<string> pick,string name)
    {
        var box=new ComboBox{MinWidth=options[0]=="S"?116:options[0]=="Poster"?120:150,FontSize=12.5,Padding=new(10,5,10,5)};
        foreach(var o in options)box.Items.Add(new ComboBoxItem{Content=o switch{"Poster"=>T("Poster","ملصق"),"Landscape"=>T("Landscape","أفقي"),"List"=>T("List","قائمة"),"S"=>T("Size: S","الحجم: صغير"),"M"=>T("Size: M","الحجم: متوسط"),"L"=>T("Size: L","الحجم: كبير"),_=>T(o)},Tag=o});
        box.SelectedIndex=Math.Max(0,Array.IndexOf(options,selected));
        System.Windows.Automation.AutomationProperties.SetName(box,T(name));
        box.SelectionChanged+=(_,_)=>{if(box.SelectedItem is ComboBoxItem {Tag:string v}&&v!=selected)pick(v);};
        return box;
    }
    // # A B C … Z: letters with titles are live; clicking scrolls to the first one (loading more if needed).
    FrameworkElement LetterStrip(List<List<VideoItem>> shown,WrapPanel grid)
    {
        var strip=new WrapPanel{Margin=new(0,0,0,8),FlowDirection=FlowDirection.LeftToRight};
        var first=new Dictionary<string,int>();for(int i=0;i<shown.Count;i++)first.TryAdd(Letter(shown[i][0].Title),i);
        foreach(var letter in new[]{"#"}.Concat(Enumerable.Range('A',26).Select(c=>((char)c).ToString())))
        {
            var has=first.TryGetValue(letter,out var index);
            var b=new Button{Content=letter,Width=26,Height=26,Padding=new(0),Margin=new(0,0,2,0),FontSize=12,Background=Brushes.Transparent,BorderThickness=new(0),Foreground=Brush(has?"#D9D4CC":"#3A3A3E"),IsEnabled=has,Cursor=System.Windows.Input.Cursors.Hand,ToolTip=has?T("Jump to")+" "+letter:null};
            System.Windows.Automation.AutomationProperties.SetName(b,"Jump to "+letter);
            b.Click+=(_,_)=>
            {
                if(index>=grid.Children.Count&&grid.Tag is Action<int> load)load(index);
                if(index<grid.Children.Count&&grid.Children[index] is FrameworkElement card){card.BringIntoView(new Rect(0,-120,card.ActualWidth,card.ActualHeight+120));card.Focus();}
            };
            strip.Children.Add(b);
        }
        return strip;
    }
    FrameworkElement LibraryPoster(List<VideoItem> group)
    {
        if(LibraryView=="List")return LibraryListRow(group);
        var item=group[0];var (watched,total)=Progress(group);var onPc=group.Any(CanPlay);var landscape=LibraryView=="Landscape";var width=Density.LibraryWidth(LibrarySize);var height=landscape?width*.5625:width*1.5;if(landscape)width=width*1.5;if(landscape)height=width*.5625;
        var panel=new StackPanel{Width=width,Margin=new(0,0,Density.CardGap,Density.Pick(18,22)),Focusable=false};
        var picture=new Grid{Height=height,ClipToBounds=true,Background=Brush("#151517")};
        picture.Children.Add(Art(landscape&&System.IO.File.Exists(item.Backdrop)?item.Backdrop:item.Poster,item.Title,height,!landscape));AddStatusPill(picture,item);
        if(!item.IsMovie&&watched>0&&watched<total)picture.Children.Add(new ProgressBar{Value=watched,Maximum=total,Height=4,VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false,Background=Brush("#99000000"),Foreground=Brush("#E4C084"),BorderThickness=new(0)});
        if(watched>0&&watched>=total){var tick=Text("✓",12,"#16120C",FontWeights.Bold);tick.HorizontalAlignment=HorizontalAlignment.Center;tick.VerticalAlignment=VerticalAlignment.Center;picture.Children.Add(new Border{IsHitTestVisible=false,Background=Brush("#E4C084"),CornerRadius=new(11),Width=22,Height=22,Margin=new(8),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Child=tick});}
        // Hover: darken, then title facts and actions rise from the bottom.
        var overlay=new Grid{Opacity=0,IsHitTestVisible=true};
        var shade=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1)};shade.GradientStops.Add(new(Color.FromArgb(40,8,8,10),0));shade.GradientStops.Add(new(Color.FromArgb(235,8,8,10),.62));
        overlay.Children.Add(new Border{Background=shade});
        var info=new StackPanel{VerticalAlignment=VerticalAlignment.Bottom,Margin=new(12,0,12,12)};overlay.Children.Add(info);
        var title=Text(item.Title,Density.CardTitle,weight:FontWeights.SemiBold);title.MaxHeight=40;title.TextTrimming=TextTrimming.CharacterEllipsis;info.Children.Add(title);
        var facts=new List<string>();if(item.Year>0)facts.Add(item.Year.ToString());var genre=item.Genres.Split('·',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Take(2).Select(T);facts.AddRange(genre);
        info.Children.Add(Text(string.Join(" · ",facts),11,"#C9C4BD"));
        info.Children.Add(Text(item.IsMovie?(watched>0?"✓ "+T("Watched"):item.Duration>0?Loc.Runtime(item.Duration):T("Movie")):$"{watched} / {total} {T("watched")}",11,"#E4C084"));
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,8,0,0)};info.Children.Add(actions);
        var next=store.UpNextFor(group)?.Item;
        if(onPc){var target=store.ContinueFor(group)??(next!=null&&CanPlay(next)?next:group.First(CanPlay));var play=new Button{Content="[[play]] "+T(store.ContinueFor(group)!=null?"Resume":"Play"),Style=(Style)FindResource("Primary"),Padding=new(12,5,12,5),FontSize=12,Margin=new(0,0,6,0)};Motion.ButtonFeedback(play);play.Click+=(_,e)=>{e.Handled=true;_=PlayVideo(target);};actions.Children.Add(play);}
        var details=new Button{Content="[[info]]",Padding=new(10,5,10,5),FontSize=12,ToolTip=T("Details & episodes")};details.Click+=(_,e)=>{e.Handled=true;OpenDetails(group);};actions.Children.Add(details);
        var listed=store.State.Watchlist.Contains(item.GroupKey);var list=ActionButton(listed?"[[check]]":"[[plus]]",()=>ToggleWatchlist(item));list.ToolTip=T(listed?"In My List · click to remove":"Add to My List");System.Windows.Automation.AutomationProperties.SetName(list,(string)list.ToolTip+" "+item.Title);list.Click+=(_,e)=>e.Handled=true;list.Padding=new(9,5,9,5);list.Margin=new(6,0,0,0);actions.Children.Add(list);
        picture.Children.Add(overlay);
        var open=new Button{Content=picture,Padding=new(0),Background=Brushes.Transparent,BorderThickness=new(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,ToolTip=null,Template=PlainTemplate()};
        open.Click+=(_,_)=>OpenDetails(group);System.Windows.Automation.AutomationProperties.SetName(open,item.Title+" details");
        var edge=new Border{CornerRadius=new(10),ClipToBounds=true,Child=open,BorderThickness=new(1.5),BorderBrush=Brush("#00000000")};panel.Children.Add(edge);
        void Fade(bool on)=>Motion.Opacity(overlay,on?1:0,on?160:120);
        edge.MouseEnter+=(_,_)=>Fade(true);edge.MouseLeave+=(_,_)=>Fade(edge.IsKeyboardFocusWithin);edge.IsKeyboardFocusWithinChanged+=(_,_)=>Fade(edge.IsMouseOver||edge.IsKeyboardFocusWithin);
        var name=Text(item.Title,Density.CardTitle,weight:FontWeights.SemiBold);name.Margin=new(0,9,0,2);name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextWrapping=TextWrapping.NoWrap;panel.Children.Add(Bidi(name));
        var meta=item.IsMovie?(item.Year>0?item.Year.ToString():T("Movie")):(watched>0?$"{watched} / {total} {T("watched")}":$"{total} {T("episodes")}");
        if(!onPc)meta+=" · "+T("Not on this PC");
        var line=Text(meta,11.5,"#9F9A93");line.TextTrimming=TextTrimming.CharacterEllipsis;line.TextWrapping=TextWrapping.NoWrap;panel.Children.Add(line);
        LazyMenu(panel,item);
        return HoverCard(Lift(panel,edge),group,edge);
    }
    static ControlTemplate PlainTemplate()
    {
        var content=new FrameworkElementFactory(typeof(ContentPresenter));
        return new ControlTemplate(typeof(Button)){VisualTree=content};
    }
}
