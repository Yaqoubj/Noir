using System.IO;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;

namespace Noir;
public partial class MainWindow
{
    void DownloadSubtitleFiles(VideoItem item,bool syncOnly=false)
    {
        var window=new Window{ShowActivated=!validationMode,Owner=this,Title="Subtitles",Width=560,Height=440,Background=Brush("#101012"),Foreground=Brush("#F4F1EB"),WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var body=new StackPanel{Margin=new(24)};window.Content=body;body.Children.Add(Heading(syncOnly?"Check subtitle timing":"Download subtitles",item.Label));
        var english=new CheckBox{Content="English",IsChecked=true,Margin=new(0,12,0,8)};var arabic=new CheckBox{Content="Arabic",IsChecked=true,Margin=new(0,8,0,12)};var auto=new CheckBox{Content="Automatically adjust timing when the match is plausible",IsChecked=true,Margin=new(0,8,0,12)};
        if(!syncOnly){body.Children.Add(english);body.Children.Add(arabic);body.Children.Add(auto);}
        body.Children.Add(Body("Subtitles stay beside your video and work offline on PC and phone. Timing analysis uses the local video and keeps the original subtitle backup.",12));
        var feedback=Body("",13);body.Children.Add(feedback);var cancellation=new CancellationTokenSource();window.Closed+=(_,_)=>{cancellation.Cancel();};
        Button? start=null;start=ActionButton(syncOnly?"Auto-sync saved subtitles":"Find and download",async()=>{
            start!.IsEnabled=false;feedback.Text=syncOnly?"Analyzing subtitle timing in the background…":"Matching subtitles for this episode / movie…";
            try{
                var engine=new SubtitleDownloads(store.DataDirectory);string message;
                if(syncOnly){var saved=MobileSubtitles.Find(item);var results=new List<string>();foreach(var sub in saved)results.Add(sub.Info.Name+" · "+await engine.Sync(item.Path,sub.Path,cancellation.Token));message=results.Count>0?string.Join("\n",results):"No external subtitles saved. Download or load a subtitle file first.";}
                else{
                    if(item.ImdbId.Length==0){using var metadata=new MetadataClient(store.DataDirectory);var id=await metadata.DownloadIdentity(item,cancellation.Token);if(id.Length==0)throw new IOException("Correct this title's match before finding subtitles.");store.SetDownloadIdentity(item.GroupKey,id);item.ImdbId=id;}
                    var release=downloads?.Jobs().Where(j=>j.Target.Id==item.Id).OrderByDescending(j=>j.CreatedAt).FirstOrDefault()?.RemoteName??Path.GetFileName(item.Path);
                    message=await engine.Fetch(item,release,new[]{english.IsChecked==true?"en":"",arabic.IsChecked==true?"ar":""}.Where(l=>l.Length>0).ToList(),auto.IsChecked==true,cancellation.Token);
                }
                if(cancellation.IsCancellationRequested)return;feedback.Text=message;
                if(current?.Id==item.Id&&player!=null){foreach(var sub in MobileSubtitles.Find(item))player.AddSlave(MediaSlaveType.Subtitle,new Uri(sub.Path).AbsoluteUri,false);ApplyPreferredTracks();}
            }catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}catch(Exception e){feedback.Text=e is IOException?e.Message:"Couldn't get subtitles. Your video is saved; retry when connected.";}finally{if(!cancellation.IsCancellationRequested)start.IsEnabled=true;}
        },true);body.Children.Add(start);window.Show();
    }
}
