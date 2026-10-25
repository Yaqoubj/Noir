using System.IO;
using System.Text.Json;
#if !NOIR_HEADLESS
using System.Windows;
using System.Windows.Controls;
#endif
namespace Noir;
public sealed class HomeRowSetting
{
    public string Id {get;set;}="";
    public bool Visible {get;set;}=true;
    public bool Pinned {get;set;}
}
public sealed class HomeLayout
{
    public static readonly string[] Defaults=["continue","ready","unfinished","comfort","directors","short-tonight","list","top10","because","genres","collections","recently-added","services","recently-watched","shows","movies","queue","upcoming"];
    public List<HomeRowSetting> Rows {get;set;}=[];
    public List<HomeRowSetting> Resolve(IEnumerable<string> available)
    {
        var known=available.Concat(Defaults).ToHashSet(StringComparer.Ordinal);var result=new List<HomeRowSetting>{new(){Id="continue"}};var seen=new HashSet<string>{"continue"};
        foreach(var row in Rows??[])if(row!=null&&known.Contains(row.Id)&&seen.Add(row.Id))result.Add(new(){Id=row.Id,Visible=row.Visible,Pinned=row.Pinned});
        foreach(var id in Defaults.Concat(available))if(seen.Add(id))result.Add(new(){Id=id,Visible=Defaults.Contains(id),Pinned=false});
        return result;
    }
    public static HomeLayout Load(string directory){try{return JsonSerializer.Deserialize<HomeLayout>(File.ReadAllText(Path.Combine(directory,"home-layout.json")))??new();}catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){return new();}}
    public void Save(string directory){Directory.CreateDirectory(directory);var path=Path.Combine(directory,"home-layout.json");var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,JsonSerializer.Serialize(this),new System.Text.UTF8Encoding(false));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
#if !NOIR_HEADLESS
public partial class MainWindow
{
    Dictionary<string,Action>? homeRowBuilders;
    readonly Dictionary<string,string> homeRowNames=new();
    void HomeRow(string id,Action render){if(homeRowBuilders!=null)homeRowBuilders[id]=render;else render();}
    string HomeRowName(string id)=>SmartLists.Ids.Contains(id)?SmartName(id):homeRowNames.GetValueOrDefault(id)??T(id switch{"continue"=>"Continue Watching","list"=>"My List","top10"=>"Top 10 this week","because"=>"Because you watched…","genres"=>"Genres","collections"=>"Collections","recently-added"=>"Recently added","services"=>"Your services","recently-watched"=>"Recently watched","shows"=>"Your shows","movies"=>"Your movies","queue"=>"Your play queue","upcoming"=>"Coming up",_=>id});
    void RenderHomeRows(List<List<VideoItem>> groups)
    {
        foreach(var g in groups)lastWatched[g[0].GroupKey]=g.Max(HistoryDate);
        var library=store.LibraryGroups();var local=groups.Where(g=>library.Contains(g[0].GroupKey)).ToList();var watched=groups.Where(g=>LastWatched(g)>DateTimeOffset.MinValue).OrderByDescending(LastWatched).ToList();
        homeRowBuilders=new();homeRowNames.Clear();RegisterSmartRows(groups);homeRowNames["ready"]=T("Ready","جاهز");HomeRow("ready",()=>{if(ReadyShelf() is {} shelf)ContentPanel.Children.Add(shelf);});
        HomeRow("services",RenderServices);HomeRow("top10",RenderTopTen);RenderHomeExtras(groups);
        HomeRow("list",()=>RailCatalog(groups.Where(g=>store.State.Watchlist.Contains(g[0].GroupKey)).ToList(),"My List","",()=>Navigate("My List")));
        HomeRow("recently-watched",()=>RailCatalog(watched.Take(12).ToList(),"Recently watched","",()=>{trackerTab="Shows";Navigate("History");}));
        HomeRow("shows",()=>RailCatalog(local.Where(g=>!g[0].IsMovie).OrderByDescending(LastWatched).ToList(),"Your shows","",()=>Navigate("Shows")));
        HomeRow("movies",()=>RailCatalog(local.Where(g=>g[0].IsMovie).OrderByDescending(LastWatched).ToList(),"Your movies","",()=>Navigate("Movies")));
        var genres=local.SelectMany(g=>g[0].Genres.Split('·',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)).Distinct().Order().ToList();
        void Genre(string genre)=>RailCatalog(local.Where(g=>g[0].Genres.Split('·',StringSplitOptions.TrimEntries).Contains(genre)).ToList(),T(genre),limit:20);
        HomeRow("genres",()=>{foreach(var genre in genres.OrderByDescending(x=>local.Count(g=>g[0].Genres.Contains(x))).Take(3))Genre(genre);});
        foreach(var genre in genres){var id="genre:"+genre;homeRowNames[id]=T(genre);HomeRow(id,()=>Genre(genre));}
        void Collection(string name)=>RailCatalog(groups.Where(g=>store.State.Collections[name].Contains(g[0].GroupKey)).ToList(),name,limit:20);
        HomeRow("collections",()=>{foreach(var name in store.State.Collections.Keys.Take(3))Collection(name);});
        foreach(var name in store.State.Collections.Keys){var id="collection:"+name;homeRowNames[id]=name;HomeRow(id,()=>Collection(name));}
        HomeRow("because",()=>{if(watched.FirstOrDefault() is not {} seed)return;var tastes=seed[0].Genres.Split('·',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).ToHashSet();var picks=local.Where(g=>g[0].GroupKey!=seed[0].GroupKey&&!g.Any(store.EverWatched)&&g[0].Genres.Split('·',StringSplitOptions.TrimEntries).Any(tastes.Contains)).Take(20).ToList();RailCatalog(picks,T("Because you watched {0}","لأنك شاهدت {0}",seed[0].Title));});
        var builders=homeRowBuilders;homeRowBuilders=null;var rows=HomeLayout.Load(store.DataDirectory).Resolve(builders.Keys);
        foreach(var row in rows.Where(r=>r.Id!="continue"&&r.Visible))if(builders.TryGetValue(row.Id,out var build))build();
        var edit=ActionButton(T("Edit Home","تعديل الرئيسية"),()=>HomeLayoutEditor(builders.Keys));edit.Margin=new(0,36,0,24);System.Windows.Automation.AutomationProperties.SetName(edit,"Edit Home");ContentPanel.Children.Add(edit);lastWatched.Clear();
    }
    Window CreateHomeLayoutEditor(IEnumerable<string> available)
    {
        var layout=HomeLayout.Load(store.DataDirectory);layout.Rows=layout.Resolve(available);var dialog=new Window{Title=T("Edit Home","تعديل الرئيسية"),Owner=this,Width=560,Height=700,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#0D0D0F"),Foreground=Brush("#F4F1EB"),FlowDirection=Loc.Arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight};var panel=new StackPanel{Margin=new(24)};var rows=new StackPanel();panel.Children.Add(Heading(T("Make Home yours","رتّب الرئيسية كما تحب"),T("Continue Watching stays first. Pin genres and collections below.","متابعة المشاهدة أولاً دائمًا. ثبّت الأنواع والمجموعات أدناه.")));panel.Children.Add(rows);
        void Draw(){rows.Children.Clear();foreach(var row in layout.Rows){var line=new DockPanel{Margin=new(0,5,0,5)};var tools=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(tools,Dock.Right);line.Children.Add(tools);var locked=row.Id=="continue";foreach(var delta in new[]{-1,1}){var d=delta;var move=ActionButton(d<0?"▲":"▼",()=>{var i=layout.Rows.IndexOf(row);var target=i+d;if(target<1||target>=layout.Rows.Count)return;layout.Rows.RemoveAt(i);layout.Rows.Insert(target,row);Draw();});move.Padding=new(9,6,9,6);move.IsEnabled=!locked&&(d<0?layout.Rows.IndexOf(row)>1:layout.Rows.IndexOf(row)<layout.Rows.Count-1);move.ToolTip=T(d<0?"Move earlier":"Move later");System.Windows.Automation.AutomationProperties.SetName(move,(string)move.ToolTip+" "+HomeRowName(row.Id));tools.Children.Add(move);}var extra=row.Id.StartsWith("genre:")||row.Id.StartsWith("collection:");var visible=new CheckBox{Content=(locked?"🔒 ":extra?"☆ ":"")+HomeRowName(row.Id),IsChecked=row.Visible,IsEnabled=!locked,VerticalAlignment=VerticalAlignment.Center,Margin=new(2,0,10,0),FontSize=14};visible.Click+=(_,_)=>{row.Visible=visible.IsChecked==true;row.Pinned=extra&&row.Visible;};line.Children.Add(visible);rows.Children.Add(line);}}
        Draw();var save=ActionButton(T("Save Home","حفظ الرئيسية"),()=>{try{layout.Save(store.DataDirectory);dialog.Close();Render();}catch(Exception e){store.Log(e);MessageBox.Show(dialog,T("Couldn’t save Home. Please try again.","تعذّر حفظ الرئيسية. حاول مجددًا."));}},true);save.Margin=new(0,24,0,0);panel.Children.Add(save);dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};dialog.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)dialog.Close();};return dialog;
    }
    void HomeLayoutEditor(IEnumerable<string> available)=>CreateHomeLayoutEditor(available).ShowDialog();
}
#endif
