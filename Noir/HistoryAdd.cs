using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noir;
// Adding a long history quickly: tap through popular titles, paste a list, or pull what your media
// servers already know. Everything lands in one review where you choose what to keep and, for
// entries without dates, roughly when you watched them. Logged history never shows up as current.
public partial class MainWindow
{
    sealed record Pick(DiscoverTitle Title,int Season,int Episode);
    readonly Dictionary<string,Pick> picks=[];
    string addKind="TV shows",addQuery="",pasteText="";int addSkip;bool addLoading;List<DiscoverTitle>? addTitles;CancellationTokenSource? addCancel;
    readonly HashSet<string> importExcluded=[];string lastImport="";
    // Lines that matched several titles ("The Office"): the review offers them as one-click choices.
    readonly Dictionary<ImportRow,List<DiscoverTitle>> importChoices=[];string importWhen="";DateTime importDay=DateTime.Today;
    string KodiPath=>Path.Combine(store.DataDirectory,"kodi.dpapi");

    void RenderAddHistory()
    {
        if(importBusy||importPlan!=null||importMessage.Length>0)ContentPanel.Children.Add(ImportPanel());
        ContentPanel.Children.Add(Heading("Add your history","Tap through popular titles, paste a list, or bring it in from another app. Logged history fills your stats and diary but never crowds Up next or Recently watched."));
        RenderQuickAdd();RenderPasteList();RenderServerImports();RenderImportSync();
    }

    // ---- Quick add: TV Time-style tapping through popular titles ----
    void RenderQuickAdd()
    {
        var head=new DockPanel{Margin=new(0,10,0,6)};
        var search=new TextBox{Style=(Style)FindResource("SearchPill"),Height=42,FontSize=15,Width=320,Text=addQuery,VerticalContentAlignment=VerticalAlignment.Center};
        System.Windows.Automation.AutomationProperties.SetName(search,"Search titles to add");
        search.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){addQuery=search.Text.Trim();addTitles=null;addSkip=0;Render();}};
        var hint=new TextBlock{Text="Search a title, then press Enter",Foreground=Brush("#817E78"),FontSize=14,Margin=new(20,0,0,0),VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false,Visibility=addQuery.Length>0?Visibility.Collapsed:Visibility.Visible};
        search.TextChanged+=(_,_)=>hint.Visibility=search.Text.Length>0?Visibility.Collapsed:Visibility.Visible;
        var field=new Grid{Width=320};field.Children.Add(search);field.Children.Add(hint);DockPanel.SetDock(field,Dock.Right);head.Children.Add(field);
        var title=new StackPanel();title.Children.Add(Text("Quick add",19,weight:FontWeights.SemiBold));title.Children.Add(Body("Tap what you've seen. For shows, “Up to…” picks the last episode you reached.",12));head.Children.Add(title);
        ContentPanel.Children.Add(head);
        ContentPanel.Children.Add(Chips(["TV shows","Movies"],addKind,v=>{addKind=v;addTitles=null;addSkip=0;Render();}));
        if(picks.Count>0)
        {
            var bar=new Border{Background=new LinearGradientBrush(Brush("#2A241C").Color,Brush("#161618").Color,0),CornerRadius=new(14),Padding=new(18,12,12,12),Margin=new(0,8,0,8),BorderBrush=Brush("#55E4C084"),BorderThickness=new(1)};
            var dock=new DockPanel();bar.Child=dock;var buttons=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(buttons,Dock.Right);dock.Children.Add(buttons);
            var review=ActionButton($"Review & add {picks.Count}",()=>_=ReviewPicks(),true);review.Padding=new(18,9,18,9);buttons.Children.Add(review);
            var clear=ActionButton("Clear",()=>{picks.Clear();RenderQuiet();});clear.Margin=new(8,0,0,0);clear.Background=Brush("Transparent");buttons.Children.Add(clear);
            var names=Text($"{picks.Count} picked · "+string.Join(", ",picks.Values.Take(4).Select(p=>p.Title.Name))+(picks.Count>4?"…":""),14,"#F4F1EB");names.VerticalAlignment=VerticalAlignment.Center;names.TextTrimming=TextTrimming.CharacterEllipsis;names.TextWrapping=TextWrapping.NoWrap;dock.Children.Add(names);
            ContentPanel.Children.Add(bar);
        }
        var wrap=new WrapPanel{Margin=new(0,8,0,0)};ContentPanel.Children.Add(wrap);
        if(addTitles==null){if(!addLoading)_=LoadQuickAdd(false);wrap.Children.Add(Body(addQuery.Length>0?$"Searching for “{addQuery}”…":"Loading popular titles…",13));return;}
        if(addTitles.Count==0){wrap.Children.Add(Body("Nothing found. Try another title.",13));return;}
        foreach(var t in addTitles)wrap.Children.Add(QuickCard(t));
        if(addQuery.Length==0){var more=ActionButton("Show more popular titles",()=>_=LoadQuickAdd(true));more.Style=(Style)FindResource("Pill");more.Padding=new(18,9,18,9);more.HorizontalAlignment=HorizontalAlignment.Center;more.Margin=new(0,6,0,10);ContentPanel.Children.Add(more);}
    }
    async Task LoadQuickAdd(bool more)
    {
        addCancel?.Cancel();addCancel=new();var token=addCancel.Token;addLoading=true;
        try
        {
            using var client=DiscoverClient();List<DiscoverTitle> found;
            if(addQuery.Length>0)found=(await client.Search(addQuery,addKind,token)).Titles;
            else{if(more)addSkip+=50;found=await client.Top(addKind=="Movies"?"movie":"series",addSkip,token);}
            if(token.IsCancellationRequested)return;
            addTitles=more&&addTitles!=null?addTitles.Concat(found).DistinctBy(t=>t.Id).ToList():found;
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){return;}
        catch(Exception e){store.Log(e);addTitles=[];Status.Text="Couldn't load titles. Check your connection.";}
        finally{addLoading=false;}
        if(page=="History"&&trackerTab=="Add history")RenderQuiet();
    }
    FrameworkElement QuickCard(DiscoverTitle t)
    {
        var body=new StackPanel{Width=150,Margin=new(0,0,14,18)};var picture=new Grid{Height=225,ClipToBounds=true};
        picture.Children.Add(Art(t.Poster,t.Name,225,true));
        if(!File.Exists(t.Poster)&&t.PosterUrl.Length>0)_=Poster();
        async Task Poster(){try{using var client=DiscoverClient();await client.Artwork(t,CancellationToken.None);var source=await LoadImage(t.Poster,384);if(source!=null){picture.Children.RemoveAt(0);picture.Children.Insert(0,new Image{Source=source,Stretch=Stretch.UniformToFill});}}catch(Exception e){store.Log(e);}}
        var picked=picks.GetValueOrDefault(t.Id);var group=TrackedGroup(t);
        var seen=group==null?0:t.IsMovie?(store.EverWatched(group[0])?1:0):group.Count(i=>i.Season>0&&store.EverWatched(i));
        string label=picked!=null?(t.IsMovie||picked.Season==0?"✓ PICKED":$"✓ UP TO S{picked.Season:00}E{picked.Episode:00}"):seen>0?(t.IsMovie?"IN YOUR HISTORY":$"{seen} EPISODES LOGGED"):"";
        if(label.Length>0)picture.Children.Add(new Border{IsHitTestVisible=false,Background=Brush(picked!=null?"#E4C084":"#E6101012"),CornerRadius=new(6),Padding=new(7,3,7,3),Margin=new(8),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,Child=Text(label,9.5,picked!=null?"#16120C":"#E4C084",FontWeights.Bold)});
        var edge=new Border{CornerRadius=new(10),ClipToBounds=true,Child=picture,BorderThickness=new(2),BorderBrush=Brush(picked!=null?"#E4C084":"#00000000")};body.Children.Add(edge);
        var name=Text(t.Name,13.5,weight:FontWeights.SemiBold);name.Margin=new(0,8,0,2);name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextWrapping=TextWrapping.NoWrap;body.Children.Add(name);
        body.Children.Add(Text(t.Years,11,"#9F9A93"));
        var row=new WrapPanel{Margin=new(0,6,0,0)};body.Children.Add(row);
        Button Small(string text,Action act,bool on){var b=ActionButton(text,act,on);b.Padding=new(10,5,10,5);b.FontSize=12;b.Margin=new(0,0,6,0);if(!on)b.Background=Brush("#26FFFFFF");row.Children.Add(b);return b;}
        if(t.IsMovie)Small(picked!=null?"✓ Seen":"Seen it",()=>{if(!picks.Remove(t.Id))picks[t.Id]=new(t,0,0);RenderQuiet();},picked!=null);
        else
        {
            Small(picked is {Season:0}?"✓ All":"All",()=>{if(picks.GetValueOrDefault(t.Id) is {Season:0})picks.Remove(t.Id);else picks[t.Id]=new(t,0,0);RenderQuiet();},picked is {Season:0});
            Small(picked is {Season:>0}?"✓ Up to…":"Up to…",()=>_=PickUpTo(t),picked is {Season:>0});
        }
        return Lift(body,edge);
    }
    async Task PickUpTo(DiscoverTitle t)
    {
        try
        {
            using var client=DiscoverClient();var details=await client.Details(t,CancellationToken.None);var now=DateTimeOffset.UtcNow;
            var released=details.Episodes.Where(e=>e.Season>0&&e.Episode>0&&(e.AirsAt==null||e.AirsAt<=now)).Select(e=>(e.Season,e.Episode)).ToList();
            if(released.Count==0){Status.Text="No episode list is available for this show yet.";return;}
            UpToPicker(details.Name,released,(s,e)=>{picks[t.Id]=new(details,s,e);RenderQuiet();});
        }
        catch(Exception e){store.Log(e);Status.Text="Couldn't load this show's episodes. Check your connection.";}
    }
    // "I watched up to…": pick a season, then the last episode you reached (or the whole season).
    void UpToPicker(string name,List<(int Season,int Episode)> released,Action<int,int> chosen)
    {
        var body=SmartPopup();var seasons=released.Select(r=>r.Season).Distinct().Order().ToList();var current=seasons[^1];
        body.Children.Add(Text($"How far did you get in {name}?",15,weight:FontWeights.SemiBold));
        var seasonRow=new WrapPanel{Margin=new(0,10,0,4)};var episodes=new WrapPanel{MaxWidth=380};body.Children.Add(seasonRow);body.Children.Add(new ScrollViewer{Content=episodes,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        void Draw()
        {
            seasonRow.Children.Clear();episodes.Children.Clear();
            foreach(var s in seasons){var value=s;var b=ActionButton("S"+s,()=>{current=value;Draw();},s==current);b.Padding=new(10,5,10,5);b.Margin=new(0,0,6,6);b.FontSize=12.5;if(s!=current)b.Background=Brush("#26FFFFFF");seasonRow.Children.Add(b);}
            var inSeason=released.Where(r=>r.Season==current).Select(r=>r.Episode).Order().ToList();
            var all=ActionButton($"All of season {current}",()=>{CloseSmart();chosen(current,inSeason[^1]);},true);all.Padding=new(12,5,12,5);all.Margin=new(0,0,6,8);episodes.Children.Add(all);
            foreach(var e in inSeason){var value=e;var b=ActionButton("E"+e,()=>{CloseSmart();chosen(current,value);});b.Padding=new(9,5,9,5);b.Margin=new(0,0,5,5);b.FontSize=12;b.Background=Brush("#26FFFFFF");episodes.Children.Add(b);}
        }
        Draw();
    }
    // Picks and pasted lines become an import plan, reviewed like any other import.
    async Task ReviewPicks()
    {
        var chosen=picks.Values.ToList();if(chosen.Count==0)return;
        await BuildPlan("Quick add",chosen.Select(p=>(Line:p.Title.Name,Title:(DiscoverTitle?)p.Title,Name:p.Title.Name,Year:p.Title.Year,Movie:(bool?)p.Title.IsMovie,p.Season,p.Episode)).ToList());
        if(importPlan!=null)picks.Clear();
    }
    async Task BuildPlan(string service,List<(string Line,DiscoverTitle? Title,string Name,int Year,bool? Movie,int Season,int Episode)> wanted)
    {
        if(importBusy)return;
        importBusy=true;importPlan=null;importProgress=(0,wanted.Count);importMessage=$"Looking up {wanted.Count} title{(wanted.Count==1?"":"s")}…";importCancel=new();var token=importCancel.Token;RenderQuiet();ContentScroll.ScrollToTop();
        var titles=new List<ImportTitle>();var unmatched=new List<(ImportRow Row,string Reason)>();var now=DateTimeOffset.UtcNow;var choices=new Dictionary<ImportRow,List<DiscoverTitle>>();
        try
        {
            using var client=DiscoverClient();var done=0;
            foreach(var w in wanted)
            {
                token.ThrowIfCancellationRequested();var row=new ImportRow(service,w.Name,w.Year,w.Movie,"","",w.Season,w.Episode,"",null,0);
                try
                {
                    var match=w.Title;
                    if(match==null)
                    {
                        var found=(await client.Search(w.Name,w.Movie==true?"Movies":w.Movie==false?"TV shows":"All",token)).Titles;var wantedName=HistoryData.Normalize(w.Name);
                        var exact=found.Where(t=>HistoryData.Normalize(t.Name)==wantedName&&(w.Year==0||t.Year==0||Math.Abs(t.Year-w.Year)<=1)).ToList();
                        if(exact.Select(t=>(t.Year,t.IsMovie)).Distinct().Count()>1&&w.Year==0){unmatched.Add((row,$"Several titles are called “{w.Name}” — pick one."));choices[row]=exact.DistinctBy(t=>(t.Id,t.Type)).Take(6).ToList();continue;}
                        match=exact.FirstOrDefault()??(found.Count==1?found[0]:null);
                        if(match==null){unmatched.Add((row,"No matching title was found."));continue;}
                    }
                    var details=await client.Details(match,token);
                    var views=details.IsMovie?[(0,0,(DateTimeOffset?)null,0)]:details.Episodes.Where(e=>e.Season>0&&e.Episode>0&&(e.AirsAt==null||e.AirsAt<=now)&&(w.Season==0||e.Season<w.Season||e.Season==w.Season&&(w.Episode==0||e.Episode<=w.Episode))).Select(e=>(e.Season,e.Episode,(DateTimeOffset?)null,0)).ToList();
                    if(views.Count==0){unmatched.Add((row,"No released episodes match."));continue;}
                    titles.Add(new(details,[row],views));
                }
                catch(Exception e)when(e is not OperationCanceledException){unmatched.Add((row,"Couldn't look this title up."));}
                finally{importProgress=(++done,wanted.Count);RenderQuiet();}
            }
            importPlan=new(service,titles,unmatched);importExcluded.Clear();importWhen="";importMessage="";importChoices.Clear();foreach(var c in choices)importChoices[c.Key]=c.Value;
        }
        catch(OperationCanceledException){importMessage="";Status.Text="Cancelled.";}
        finally{importBusy=false;RenderQuiet();ContentScroll.ScrollToTop();}
    }

    // ---- Paste a list ----
    void RenderPasteList()
    {
        ContentPanel.Children.Add(Heading("Paste a list","One title per line, from notes or a spreadsheet. Add a year to pick the right one, and “S3” or “S3E5” for how far you got in a show."));
        var box=new TextBox{Text=pasteText,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=150,MaxWidth=760,HorizontalAlignment=HorizontalAlignment.Left,Width=760,Padding=new(12),Background=Brush("#161618"),Foreground=Brush("#F4F1EB"),BorderBrush=Brush("#343437"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontSize=14};
        System.Windows.Automation.AutomationProperties.SetName(box,"Titles to add, one per line");box.TextChanged+=(_,_)=>pasteText=box.Text;
        ContentPanel.Children.Add(box);
        ContentPanel.Children.Add(Text("e.g.   Breaking Bad   ·   Inception (2010)   ·   The Office S4   ·   Lost S2E10   ·   movie: Dune",11.5,"#817E78"));
        var go=ActionButton("Match titles",()=>{var lines=HistoryList.Parse(pasteText);if(lines.Count==0){Status.Text="Paste at least one title.";return;}_=BuildPlan("Pasted list",lines);},true);go.Margin=new(0,10,0,0);go.Padding=new(18,9,18,9);ContentPanel.Children.Add(go);
    }
    // ---- Media servers and Kodi ----
    void RenderServerImports()
    {
        var servers=downloads?.Config.Servers??[];
        ContentPanel.Children.Add(Heading("From your media servers","Everything marked played on Plex, Jellyfin, Emby or Kodi. Read-only: nothing on the server changes."));
        var wrap=new WrapPanel();ContentPanel.Children.Add(wrap);
        foreach(var server in servers)
            wrap.Children.Add(SyncTile(server.Name.Length>0?server.Name:server.Kind,server.Kind[..1],server.Kind=="Plex"?"#E5A00D":"#7A5CC9","#FFFFFF",server.Kind,"Pull its watched movies and episodes into your history.",panel=>panel.Children.Add(ActionButton("Import watched",()=>_=ImportServer(server),true))));
        if(servers.Count==0)wrap.Children.Add(SyncTile("Plex / Jellyfin / Emby","P","#E5A00D","#FFFFFF","Not connected","Connect a server in Settings → Connections, then import what it has marked as played.",panel=>panel.Children.Add(ActionButton("Open Connections",()=>{settingsSection="Connections";Navigate("Settings");}))));
        var kodi=KodiAccount();
        wrap.Children.Add(SyncTile("Kodi","K","#17B2E7","#FFFFFF",kodi!=null?"Connected · "+kodi.Value.Address:"Not connected","Kodi's web control must be on (Settings → Services → Control → Allow remote control via HTTP).",KodiControls));
    }
    void KodiControls(StackPanel panel)
    {
        if(KodiAccount() is {} k){var row=new WrapPanel();row.Children.Add(ActionButton("Import watched",()=>_=ImportKodi(k.Address,k.User,k.Password),true));var forget=ActionButton("Disconnect",()=>{try{File.Delete(KodiPath);}catch(IOException){}RenderQuiet();});forget.Margin=new(8,0,0,0);row.Children.Add(forget);panel.Children.Add(row);return;}
        TextBox Field(string hint){panel.Children.Add(Text(T(hint),11,"#8F8A83"));var t=new TextBox{Margin=new(0,2,0,6),Padding=new(8,5,8,5),Background=Brush("#1C1C1F"),Foreground=Brush("#F4F1EB"),BorderBrush=Brush("#343437"),ToolTip=hint};System.Windows.Automation.AutomationProperties.SetName(t,hint);panel.Children.Add(t);return t;}
        var address=Field("Address, e.g. http://192.168.1.20:8080");var user=Field("Username (if set)");
        panel.Children.Add(Text(T("Password (if set)"),11,"#8F8A83"));
        var password=new PasswordBox{Margin=new(0,2,0,8),Padding=new(8,5,8,5),Background=Brush("#1C1C1F"),Foreground=Brush("#F4F1EB"),BorderBrush=Brush("#343437"),ToolTip="Password (if set)"};panel.Children.Add(password);
        panel.Children.Add(ActionButton("Connect & import",()=>_=ImportKodi(address.Text.Trim(),user.Text.Trim(),password.Password,true),true));
    }
    (string Address,string User,string Password)? KodiAccount()
    {
        try{if(!File.Exists(KodiPath))return null;var json=JsonDocument.Parse(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KodiPath),null,DataProtectionScope.CurrentUser))).RootElement;return(json.GetProperty("a").GetString()??"",json.GetProperty("u").GetString()??"",json.GetProperty("p").GetString()??"");}
        catch(Exception e){store.Log(e);return null;}
    }
    async Task ImportServer(MediaServerAccount server)
    {
        if(importBusy)return;Status.Text="Reading watched titles from "+server.Name+"…";
        try{var rows=await new MediaServers().Watched(server,CancellationToken.None);await ResolveImport(server.Kind+(server.Name.Length>0?" · "+server.Name:""),rows);}
        catch(Exception e){store.Log(e);Status.Text=e is IOException?e.Message:"Couldn't read that server.";}
    }
    async Task ImportKodi(string address,string user,string password,bool save=false)
    {
        if(importBusy)return;Status.Text="Reading Kodi's library…";
        try
        {
            var rows=await new MediaServers().KodiWatched(address,user,password,CancellationToken.None);
            if(save)File.WriteAllBytes(KodiPath,ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{a=address,u=user,p=password})),null,DataProtectionScope.CurrentUser));
            await ResolveImport("Kodi",rows);
        }
        catch(Exception e){store.Log(e);Status.Text=e is IOException?e.Message:"Couldn't reach Kodi. Check the address and that remote control via HTTP is on.";}
    }

    // ---- Review: groups to keep or drop, and a "when" for anything without dates ----
    void ReviewGroups(StackPanel stack,ImportPlan plan)
    {
        var now=DateTimeOffset.UtcNow;
        bool Complete(ImportTitle t){if(t.Title.IsMovie)return true;var released=t.Title.Episodes.Where(e=>e.Season>0&&e.Episode>0&&(e.AirsAt==null||e.AirsAt<=now)).Select(e=>(e.Season,e.Episode)).ToHashSet();return released.Count>0&&released.All(r=>t.Views.Any(v=>v.Season==r.Season&&v.Episode==r.Episode));}
        foreach(var (label,group) in new[]{("Movies",plan.Titles.Where(t=>t.Title.IsMovie).ToList()),("Shows · watched all",plan.Titles.Where(t=>!t.Title.IsMovie&&Complete(t)).ToList()),("Shows · partly watched",plan.Titles.Where(t=>!t.Title.IsMovie&&!Complete(t)).ToList())})
        {
            if(group.Count==0)continue;
            var kept=group.Count(t=>!importExcluded.Contains(t.Title.Id));
            var all=new CheckBox{Content=$"{label} · {kept} of {group.Count}",IsChecked=kept==group.Count?true:kept==0?false:null,Foreground=Brush("#F4F1EB"),FontWeight=FontWeights.SemiBold,Margin=new(0,10,0,4)};
            all.Click+=(_,_)=>{var keep=kept<group.Count;foreach(var t in group)if(keep)importExcluded.Remove(t.Title.Id);else importExcluded.Add(t.Title.Id);RenderQuiet();};stack.Children.Add(all);
            var strip=new WrapPanel{Margin=new(22,0,0,4)};stack.Children.Add(strip);
            foreach(var t in group.OrderByDescending(t=>t.Views.Count).Take(60))
            {
                var on=!importExcluded.Contains(t.Title.Id);var id=t.Title.Id;
                var tile=new Button{Padding=new(0),Margin=new(0,0,8,8),Background=Brush("Transparent"),ToolTip=$"{t.Title.Name}{(t.Title.Year>0?$" ({t.Title.Year})":"")} · {t.Views.Count} viewing{(t.Views.Count==1?"":"s")} · click to {(on?"leave out":"include")}"};
                var g=new Grid{Width=54,Height=81};g.Children.Add(new Border{CornerRadius=new(5),ClipToBounds=true,Child=Art(t.Title.Poster,t.Title.Name,81,true),Opacity=on?1:.3});
                if(on)g.Children.Add(new Border{Width=18,Height=18,CornerRadius=new(9),Background=Brush("#E4C084"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(3),Child=Text("✓",10,"#16120C",FontWeights.Bold)});
                tile.Content=g;tile.Click+=(_,_)=>{if(!importExcluded.Remove(id))importExcluded.Add(id);RenderQuiet();};
                System.Windows.Automation.AutomationProperties.SetName(tile,(on?"Leave out ":"Include ")+t.Title.Name);strip.Children.Add(tile);
            }
            if(group.Count>60)stack.Children.Add(Text($"…and {group.Count-60} more",11.5,"#8F8A83"));
        }
        var undated=plan.Titles.Where(t=>!importExcluded.Contains(t.Title.Id)).Sum(t=>t.Views.Count(v=>v.At==null));
        if(undated==0)return;
        stack.Children.Add(Text($"{undated:N0} viewing{(undated==1?" has":"s have")} no date. When did you watch {(undated==1?"it":"them")}?",14,"#F4F1EB",FontWeights.SemiBold));((FrameworkElement)stack.Children[^1]).Margin=new(0,16,0,6);
        var row=new WrapPanel();stack.Children.Add(row);
        foreach(var option in new[]{"When they aired","Around a year","Over a period","Don't remember"})
        {
            var value=option;var selected=importWhen==value||value=="Over a period"&&importWhen.StartsWith("Over a period:");
            var text=value=="Around a year"&&importWhen==value?$"Around {importDay.Year}":selected&&value=="Over a period"?importWhen[14..].Replace("-","–"):value;
            var b=ActionButton(text,()=>
            {
                if(value=="Around a year"){YearPicker("Around which year?",1950,y=>{importWhen=value;importDay=new(y,7,1);RenderQuiet();});return;}
                if(value=="Over a period"){YearPicker("From which year?",1950,from=>YearPicker("Until which year?",from,to=>{importWhen=$"Over a period:{from}-{to}";RenderQuiet();}));return;}
                importWhen=value;RenderQuiet();
            },selected);
            b.Padding=new(14,7,14,7);b.Margin=new(0,0,8,8);if(!selected)b.Background=Brush("#26FFFFFF");row.Children.Add(b);
        }
    }
    // Resolves an ambiguous line with the title the user picked and moves it into the plan.
    async Task ChooseImportTitle(ImportRow row,DiscoverTitle pick)
    {
        if(importPlan is not {} plan)return;
        try
        {
            using var client=DiscoverClient();var details=await client.Details(pick,CancellationToken.None);var now=DateTimeOffset.UtcNow;
            var views=details.IsMovie?[(0,0,(DateTimeOffset?)null,0)]:details.Episodes.Where(e=>e.Season>0&&e.Episode>0&&(e.AirsAt==null||e.AirsAt<=now)&&(row.Season==0||e.Season<row.Season||e.Season==row.Season&&(row.Episode==0||e.Episode<=row.Episode))).Select(e=>(e.Season,e.Episode,(DateTimeOffset?)null,0)).ToList();
            if(importPlan!=plan)return;
            importPlan=plan with{Titles=[..plan.Titles,new(details,[row],views)],Unmatched=plan.Unmatched.Where(u=>u.Row!=row).ToList()};importChoices.Remove(row);RenderQuiet();
        }
        catch(Exception e){store.Log(e);Status.Text="Couldn't load that title. Check your connection.";}
    }
    void ChoiceRows(StackPanel stack,ImportPlan plan)
    {
        foreach(var (row,options) in importChoices.Where(c=>plan.Unmatched.Any(u=>u.Row==c.Key)).ToList())
        {
            stack.Children.Add(Text($"Which “{row.Title}”{(row.Season>0?$" (up to S{row.Season:00}{(row.Episode>0?$"E{row.Episode:00}":"")})":"")}?",13.5,"#F4F1EB",FontWeights.SemiBold));((FrameworkElement)stack.Children[^1]).Margin=new(0,12,0,6);
            var line=new WrapPanel();stack.Children.Add(line);
            foreach(var option in options){var pick=option;var b=ActionButton($"{option.Name} ({(option.Years.Length>0?option.Years:"?")}) · {(option.IsMovie?"movie":"show")}",()=>_=ChooseImportTitle(row,pick));b.Padding=new(12,6,12,6);b.Margin=new(0,0,8,8);b.Background=Brush("#26FFFFFF");line.Children.Add(b);}
        }
    }
    bool ImportReady(ImportPlan plan)=>importWhen.Length>0||!plan.Titles.Any(t=>!importExcluded.Contains(t.Title.Id)&&t.Views.Any(v=>v.At==null));
    // Applies the kept titles; undated viewings get the chosen "when", spread per title in watching order.
    int ApplyReviewed(ImportPlan plan)
    {
        int added=0;var id=store.BeginImport(plan.Service);lastImport=id;
        using(store.Batch())foreach(var title in plan.Titles.Where(t=>!importExcluded.Contains(t.Title.Id)))
        {
            var items=store.AddDiscovered(title.Title,false);var entries=new List<LogEntry>();var undated=new List<VideoItem>();var ratings=new Dictionary<string,int>();
            for(var n=0;n<title.Views.Count;n++)
            {
                var view=title.Views[n];var item=items.FirstOrDefault(i=>title.Title.IsMovie?i.IsMovie:i.Season==view.Season&&i.Episode==view.Episode);if(item==null)continue;
                var row=title.Rows.Count==title.Views.Count?title.Rows[n]:null;
                if(view.At is {} at)entries.Add(HistoryImporter.Entry(item,at,false,view.Rating,row));else{undated.Add(item);if(view.Rating>0)ratings[item.Id]=view.Rating;}
            }
            if(undated.Count>0)entries.AddRange((importWhen is "" or "Don't remember"?undated.Select(i=>new LogEntry(i,store.AirDate(i)??DateTimeOffset.UtcNow,true)):DatedEntries(undated,importWhen,importDay)).Select(e=>e with{Rating=ratings.GetValueOrDefault(e.Item.Id)}));
            added+=store.LogWatched(entries,"Imported from "+plan.Service,queueSync:false,exact:true,batch:id);
            HistoryImporter.KeepExtras(store,items,title);
        }
        store.EndImport(id);if(added==0)lastImport="";
        return added;
    }
}
