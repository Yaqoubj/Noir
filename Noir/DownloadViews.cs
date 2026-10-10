using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Noir;
public partial class MainWindow
{
    PcDownloads? downloads;
    Action? downloadPageUpdate;
    int lastDownloadCount=-1;
    void DownloadChanged()=>Dispatcher.BeginInvoke(()=>{if(closing)return;UpdateDownloadBadge();downloadPageUpdate?.Invoke();});
    void UpdateDownloadBadge(){var count=downloads?.Jobs().Count(j=>j.Status is "Queued" or "Preparing" or "Downloading" or "Subtitles")??0;if(count==lastDownloadCount)return;lastDownloadCount=count;DownloadsNav.Content=NavigationLabel(count>0?$"Downloads · {count}":"Downloads","M12,1 L12,16 M6,10 L12,16 L18,10 M2,17 L2,23 L22,23 L22,17");}
    void StartDownloads()
    {
        try
        {
            downloads=new(store);downloads.Completed+=QueueFolderScan;downloads.Changed+=DownloadChanged;UpdateDownloadBadge();
        }
        catch(Exception){Status.Text="Download settings could not be loaded. Your library is unchanged.";}
    }
    void RenderDownloadSettings()
    {
        ContentPanel.Children.Add(Heading("Downloads on this PC","AIOStreams finds releases. TorBox supplies the files. Completed downloads join your local library."));
        if(downloads==null){ContentPanel.Children.Add(Body("Downloads are unavailable until Noir restarts."));return;}
        var c=downloads.Config;
        var manifest=new PasswordBox {Password=c.Manifest,Padding=new(10),Margin=new(0,5,0,10)};
        var key=new PasswordBox {Password=c.Key,Padding=new(10),Margin=new(0,5,0,10)};
        ContentPanel.Children.Add(Body("AIOStreams installation URL (manifest.json) · optional for new releases",12));ContentPanel.Children.Add(manifest);
        ContentPanel.Children.Add(Body("TorBox API key",12));ContentPanel.Children.Add(key);
        ComboBox Folder(bool movies,string selected)
        {
            var box=new ComboBox{Margin=new(0,5,0,10),MinWidth=300};foreach(var root in store.State.Roots.Where(r=>r.Movies==movies))box.Items.Add(root.Path);
            box.SelectedItem=selected;if(box.SelectedIndex<0&&box.Items.Count>0)box.SelectedIndex=0;return box;
        }
        var shows=Folder(false,c.ShowsRoot);var movies=Folder(true,c.MoviesRoot);
        ContentPanel.Children.Add(Body("TV / anime download folder",12));ContentPanel.Children.Add(shows);ContentPanel.Children.Add(Body("Movie download folder",12));ContentPanel.Children.Add(movies);
        var quality=new ComboBox{ItemsSource=new[]{"1080p","720p","2160p","Any"},SelectedItem=c.PreferredResolution,Margin=new(0,5,0,10)};ContentPanel.Children.Add(Body("Preferred download quality",12));ContentPanel.Children.Add(quality);
        var size=new TextBox{Text=c.MaxEpisodeBytes>0?(c.MaxEpisodeBytes/1073741824d).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture):"",Padding=new(10),Margin=new(0,5,0,10)};ContentPanel.Children.Add(Body("Maximum episode size in GB · leave empty for no limit",12));ContentPanel.Children.Add(size);
        var english=new CheckBox{Content="Download English subtitles",IsChecked=c.SubtitleLanguages.Contains("en"),Margin=new(0,8,0,8)};var arabic=new CheckBox{Content="Download Arabic subtitles",IsChecked=c.SubtitleLanguages.Contains("ar"),Margin=new(0,8,0,8)};var timing=new CheckBox{Content="Automatically check subtitle timing against the local video",IsChecked=c.AutoSyncSubtitles,Margin=new(0,8,0,8)};ContentPanel.Children.Add(english);ContentPanel.Children.Add(arabic);ContentPanel.Children.Add(timing);
        ContentPanel.Children.Add(ActionButton("Save download settings",()=>{try{long limit=0;if(size.Text.Trim().Length>0){if(!double.TryParse(size.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var gb)||!double.IsFinite(gb)||gb<=0||gb>1000)throw new System.IO.IOException("Enter an episode size between 0 and 1000 GB, or leave it empty.");limit=(long)(gb*1073741824);}downloads.Configure(manifest.Password,key.Password,shows.SelectedItem as string??"",movies.SelectedItem as string??"",quality.SelectedItem as string??"1080p",limit,new[]{english.IsChecked==true?"en":"",arabic.IsChecked==true?"ar":""}.Where(l=>l.Length>0).ToList(),timing.IsChecked==true);Status.Text="Saved securely on this PC. Paired phones use these download preferences.";}catch(Exception e){Status.Text=e is System.IO.IOException?e.Message:"Couldn't save settings.";}},true));
        ContentPanel.Children.Add(Body("Both sources are checked for new releases and existing packs. Plan missing episodes checks released episodes, pack coverage and local files. Subtitles come from the release or OpenSubtitles, when available, and stay beside the video for offline use. Automatic timing runs locally and keeps an original backup. Files are organized as Show (Year) / Season 01 / Show (Year) S01E01.mkv.",12));
        ContentPanel.Children.Add(ActionButton("Manage PC downloads",ShowDownloads));
        var bridge=new StackPanel();bridge.Children.Add(Body("If AIOStreams returns playback links without download references, create a separate Noir profile on the same host. Export your AIOStreams config with credentials included, then choose the export here. Noir adds download metadata to the copy. The copy omits the retired TorBox Search preset; your existing Stremio profile is unchanged.",12));
        var bridgeStatus=Body("",12);var connect=ActionButton("Connect a separate Noir download profile",async()=>{
            var file=new Microsoft.Win32.OpenFileDialog{Title="Choose AIOStreams export",Filter="AIOStreams configuration|*.json"};if(file.ShowDialog(this)!=true)return;
            bridgeStatus.Text="Creating the download profile on your existing AIOStreams host…";
            try{var link=await AioProfiles.Create(downloads.Config.Manifest,await System.IO.File.ReadAllTextAsync(file.FileName),CancellationToken.None);downloads.UseDownloadProfile(link);manifest.Password=link;bridgeStatus.Text="Download profile connected. Refresh releases to use it.";}
            catch(Exception e){bridgeStatus.Text=e is System.IO.IOException?e.Message:"Couldn't create the profile. Your existing Stremio configuration is unchanged.";}
        });bridge.Children.Add(connect);bridge.Children.Add(bridgeStatus);ContentPanel.Children.Add(new Expander{Header="AIOStreams download connection",Content=bridge,Margin=new(0,14,0,10)});
    }
    async void FindDownloads(VideoItem item,bool season=false)
    {
        if(downloads==null){Status.Text="Downloads are starting. Try again shortly.";return;}
        var window=new Window{ShowActivated=!validationMode,Owner=this,Title=season?"Download season":"Download episode / movie",Width=820,Height=730,Background=Brush("#101012"),Foreground=Brush("#F4F1EB"),WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var body=new StackPanel{Margin=new(24)};window.Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var cancel=new CancellationTokenSource();var token=cancel.Token;window.Closed+=(_,_)=>{cancel.Cancel();cancel.Dispose();};window.Show();int searchRevision=0;
        async Task Search()
        {
            var view=++searchRevision;
            body.Children.Clear();body.Children.Add(Heading(item.Title,season?$"Season {item.Season} · find missing episodes or choose a pack":"Choose quality, size and release"));
            var states=new Dictionary<string,TextBlock>();foreach(var source in new[]{"AIOStreams","TorBox library"}){var t=Body(source+" · Searching…",12);states[source]=t;body.Children.Add(t);}
            var status=Body("Checking both sources…",13);body.Children.Add(status);
            try{
                var progress=new Progress<DownloadSourceStatus>(s=>{if(view==searchRevision&&!token.IsCancellationRequested&&states.TryGetValue(s.Source,out var text))text.Text=s.Source+" · "+s.State+(s.Message.Length>0?" · "+s.Message:"");});
                var result=await downloads.SearchDetailed(item.Id,season,progress,token);
                if(view!=searchRevision||token.IsCancellationRequested)return;
                foreach(var source in states.Where(s=>!result.Sources.Any(r=>r.Source==s.Key)))source.Value.Text=source.Key+" · No separate source status";
                foreach(var source in result.Sources)if(states.TryGetValue(source.Source,out var t))t.Text=source.Source+" · "+source.State+" · "+source.Message;
                status.Text=result.Choices.Count+" unique releases · preferred quality: "+downloads.Config.PreferredResolution;
                body.Children.Add(ActionButton("Refresh sources",()=>_=Search()));
                if(season&&result.Choices.Count>0)body.Children.Add(ActionButton("Plan missing episodes",async()=>{
                    status.Text="Checking episode coverage and existing files…";
                    try{var plan=await downloads.Plan(item.Id,result.Choices.Select(c=>c.Id).ToList(),token);RenderFiles(plan.Entries,plan.Message+(plan.MissingEpisodes.Count>0?" · Missing: "+string.Join(", ",plan.MissingEpisodes.Select(n=>$"E{n:00}")):""));}
                    catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception e){status.Text=e is System.IO.IOException?e.Message:"Couldn't build this plan. Choose a release below.";}
                },true));
                body.Children.Add(ReleasePicker(result.Choices,downloads.Config,async release=>{
                    status.Text="Verifying TorBox's file list…";
                    try{var files=await downloads.Files(release.Id,token);RenderFiles(files.Select(f=>new DownloadPlanEntry{ReleaseId=release.Id,Release=release.Name,File=f}).ToList(),$"{files.Count(f=>f.Selectable)} selectable files · episode numbers checked against Season {item.Season}");}
                    catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception e){status.Text=e is System.IO.IOException?e.Message:"Couldn't load TorBox files. Try another release.";}
                }));
            }catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception e){status.Text=e is System.IO.IOException?e.Message:"Couldn't find releases. Check download settings and connection.";body.Children.Add(ActionButton("Retry",()=>_=Search()));}
        }
        void RenderFiles(List<DownloadPlanEntry> entries,string summary)
        {
            body.Children.Clear();body.Children.Add(Heading("Review your download",item.Title+(season?$" · Season {item.Season}":"")));body.Children.Add(Body(summary,13));
            var selected=new List<(CheckBox Box,DownloadPlanEntry Entry)>();var numbers=new HashSet<(int,int)>();
            var total=Body("",14);
            void UpdateTotal(){var chosen=selected.Where(x=>x.Box.IsChecked==true).ToList();total.Text=$"{chosen.Count} files · {DownloadNaming.Size(chosen.Sum(x=>x.Entry.File.Size))}"+(chosen.Any(x=>x.Entry.File.Size<=0)?" + unknown sizes":"");}
            foreach(var entry in entries){var f=entry.File;var box=new CheckBox{Content=Body(f.Name+" · "+DownloadNaming.Size(f.Size)+(f.Reason.Length>0?" · "+f.Reason:""),13),IsEnabled=f.Selectable,IsChecked=f.Selectable&&numbers.Add((f.Season,f.Episode)),Margin=new(0,9,0,9)};selected.Add((box,entry));box.Checked+=(_,_)=>UpdateTotal();box.Unchecked+=(_,_)=>UpdateTotal();body.Children.Add(box);if(f.MappingRequired)body.Children.Add(ActionButton("Match this file to an episode",()=>{
                var dialog=new Window{Owner=window,Title="Confirm episode match",Width=460,Height=230,Background=Brush("#101012"),Foreground=Brush("#F4F1EB"),WindowStartupLocation=WindowStartupLocation.CenterOwner};var panel=new StackPanel{Margin=new(22)};dialog.Content=panel;panel.Children.Add(Body(f.Name+" · Season "+item.Season,13));var number=new TextBox{Text=item.Episode.ToString(),Margin=new(0,12,0,12)};panel.Children.Add(number);var note=Body("Use the episode number from this season's episode list.",12);panel.Children.Add(note);panel.Children.Add(ActionButton("Confirm match",()=>{try{if(!int.TryParse(number.Text,out var n))throw new System.IO.IOException("Enter an episode number.");downloads.MapFile(f.Id,n);dialog.Close();RenderFiles(entries,summary);}catch(Exception e){note.Text=e is System.IO.IOException?e.Message:"Couldn't match this file.";}},true));dialog.Show();
            }));if(entries.Select(e=>e.ReleaseId).Distinct().Count()>1)body.Children.Add(Body(entry.Release,11));}
            body.Children.Add(total);UpdateTotal();var c=downloads.Config;body.Children.Add(Body("Save to: "+(item.IsMovie?c.MoviesRoot:c.ShowsRoot),12));
            body.Children.Add(Body("Subtitles: "+(c.SubtitleLanguages.Count==0?"Off":string.Join(" + ",c.SubtitleLanguages.Select(l=>l=="ar"?"Arabic":"English")))+(c.AutoSyncSubtitles?" · automatic timing check":""),12));
            var feedback=Body("Only selected files are downloaded. Existing and queued episodes are skipped.",12);body.Children.Add(feedback);var requestId=Guid.NewGuid().ToString();
            body.Children.Add(ActionButton("Download selected to this PC",()=>{
                try{var selections=selected.Where(x=>x.Box.IsChecked==true).GroupBy(x=>x.Entry.ReleaseId).Select(g=>new DownloadSelection{ReleaseId=g.Key,FileIds=g.Select(x=>x.Entry.File.Id).ToList()}).ToList();var ids=downloads.Enqueue(new(){RequestId=requestId,Selections=selections});Status.Text=$"Queued {ids.Count} files for this PC.";window.Close();ShowDownloads();}
                catch(Exception e){feedback.Text=e is System.IO.IOException?e.Message:"Couldn't queue these files.";}
            },true));body.Children.Add(ActionButton("Back to releases",()=>_=Search()));
        }
        await Search();
    }
    void ShowDownloads()=>Navigate("Downloads");
    void RenderDownloads()
    {
        PageTitle.Text="Downloads";
        ContentPanel.Children.Add(Heading("Your download queue","Movies and episodes from this PC or your paired phone. Finished files join your library automatically."));
        var tools=new WrapPanel{Margin=new(0,0,0,12)};tools.Children.Add(ActionButton("Find something new",()=>Navigate("Discover"),true));var settings=ActionButton("Download settings",()=>{settingsSection="Downloads";Navigate("Settings");});settings.Margin=new(10,0,0,0);tools.Children.Add(settings);ContentPanel.Children.Add(tools);
        if(downloads==null){ContentPanel.Children.Add(Body("Downloads are starting. Reopen this tab shortly."));return;}
        var body=new StackPanel();ContentPanel.Children.Add(body);downloadPageUpdate=BuildDownloadQueue(body);downloadPageUpdate();
    }
    Action BuildDownloadQueue(StackPanel body)
    {
        if(downloads==null)return ()=>{};
        var empty=Body("No downloads queued yet.",13);body.Children.Add(empty);
        var rows=new Dictionary<string,(Border Container,TextBlock Label,ProgressBar Progress,WrapPanel Actions,string State)>();
        void RenderJobs()
        {
            var jobs=downloads.Jobs().OrderByDescending(j=>j.CreatedAt).ToList();empty.Visibility=jobs.Count==0?Visibility.Visible:Visibility.Collapsed;
            foreach(var removed in rows.Keys.Where(id=>!jobs.Any(j=>j.Id==id)).ToList()){body.Children.Remove(rows[removed].Container);rows.Remove(removed);}
            foreach(var j in jobs.AsEnumerable().Reverse())
            {
                if(!rows.TryGetValue(j.Id,out var row)){
                    var content=new StackPanel{Margin=new(16)};content.Children.Add(Body(j.Name,16));var label=Body("",12);content.Children.Add(label);var bar=new ProgressBar{Height=4,Margin=new(0,10,0,10)};content.Children.Add(bar);var actions=new WrapPanel();content.Children.Add(actions);
                    var border=new Border{Background=Brush("#18181B"),CornerRadius=new(10),Margin=new(0,8,0,8),Child=content};row=(border,label,bar,actions,"");rows[j.Id]=row;body.Children.Insert(1,border);
                }
                row.Label.Text=j.Status+(j.Size>0?$" · {Math.Clamp(j.Bytes*100d/j.Size,0,100):0}%":"")+" · "+(j.Bytes==0?"0 B":DownloadNaming.Size(j.Bytes))+" / "+DownloadNaming.Size(j.Size)+(j.Message.Length>0?" · "+j.Message:"")+(j.SubtitleMessage.Length>0?" · "+j.SubtitleMessage:"");row.Progress.IsIndeterminate=j.Status=="Preparing";row.Progress.Maximum=Math.Max(1,j.Size);row.Progress.Value=j.Bytes;
                if(row.State!=j.Status){row.Actions.Children.Clear();if(j.Status!="Completed")foreach(var action in j.Status is "Paused" or "Failed" or "Cancelled"?new[]{"resume"}:new[]{"pause","cancel"}){var selected=action;var id=j.Id;row.Actions.Children.Add(ActionButton(char.ToUpper(selected[0])+selected[1..],()=>{try{downloads.Control(id,selected);RenderJobs();}catch(Exception){Status.Text="Couldn't change this download.";}}));}rows[j.Id]=(row.Container,row.Label,row.Progress,row.Actions,j.Status);}
            }
        }
        return RenderJobs;
    }
}
