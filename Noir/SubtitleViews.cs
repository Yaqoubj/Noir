using System.IO;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;

namespace Noir;
public partial class MainWindow
{
    CancellationTokenSource? streamSubtitleCancellation;
    int streamSubtitleAttempt=-1;
    async void EnsureStreamSubtitles()
    {
        if(currentStream==null||current==null||streamSubtitleAttempt==serial)return;
        streamSubtitleAttempt=serial;var session=serial;var item=current;var stream=currentStream;
        streamSubtitleCancellation?.Cancel();streamSubtitleCancellation=new CancellationTokenSource();var ct=streamSubtitleCancellation.Token;
        try{
            // Let embedded tracks arrive before looking for an external subtitle.
            await Task.Delay(2500,ct);
            if(session!=serial||closing||player==null)return;
            var preferred=Preference(item).SubtitleTrack;var language=SubtitleDownloads.Language(preferred.Length==0?store.State.Subtitles:preferred);
            if(language.Length==0||attachedStreamSubtitles.Any(p=>SubtitleDownloads.SidecarLanguage(p)==language))return;
            if(player.SpuDescription.Any(t=>t.Id>=0&&System.Text.RegularExpressions.Regex.IsMatch(t.Name,language=="ar"?@"(?i)\b(arabic|ara|ar)\b":@"(?i)\b(english|eng|en)\b")))return;
            SavedLabel.Text="Finding "+(language=="ar"?"Arabic":"English")+" subtitles…";
            var engine=new SubtitleDownloads(store.DataDirectory,source:()=>{var c=downloads?.Config;return(c?.SubtitleAddonUrl??"",c?.SubtitleAddonOnly??true);});
            var target=SubtitleDownloads.StreamTarget(store.DataDirectory,item,stream.Name);
            var result=await engine.Fetch(item,stream.Name,[language],false,ct,streamTarget:target);
            if(session!=serial||closing)return;
            // Respect a manual Off/track change made while the request was running.
            if(Preference(item).SubtitleTrack==preferred){AttachStreamSubtitles();ApplyPreferredTracks();}
            SavedLabel.Text=result;
        }catch(OperationCanceledException){}catch(Exception e){store.Log(e);if(session==serial&&!closing)SavedLabel.Text="Subtitles unavailable · retry in Subtitles";}
    }
    void DownloadSubtitleFiles(VideoItem item,bool syncOnly=false)
    {
        var stream=current?.Id==item.Id?currentStream:null;var playbackSerial=serial;
        if(stream!=null)streamSubtitleCancellation?.Cancel();
        var streamTarget=stream==null?null:SubtitleDownloads.StreamTarget(store.DataDirectory,item,stream.Name);
        if(syncOnly&&stream!=null){SavedLabel.Text="Streaming subtitles: use Subtitles → Timing (Earlier / Later). Automatic analysis needs a local video.";ShowControls();return;}
        var window=new Window{ShowActivated=!validationMode,Owner=this,Title="Subtitles",Width=560,Height=440,Background=Brush("#101012"),Foreground=Brush("#F4F1EB"),WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var body=new StackPanel{Margin=new(24)};window.Content=body;body.Children.Add(Heading(syncOnly?"Check subtitle timing":"Download subtitles",item.Label));
        var english=new CheckBox{Content="English",IsChecked=true,Margin=new(0,12,0,8)};var arabic=new CheckBox{Content="Arabic",IsChecked=true,Margin=new(0,8,0,12)};var auto=new CheckBox{Content="Automatically adjust timing when the match is plausible",IsChecked=true,Margin=new(0,8,0,12)};
        if(!syncOnly){body.Children.Add(english);body.Children.Add(arabic);if(stream==null)body.Children.Add(auto);}
        body.Children.Add(Body(stream!=null?"Download subtitles while you stream. They are saved on this PC for this release and load again next time. Use Subtitles → Timing if the text appears early or late.":"Subtitles stay beside your video and work offline on PC and phone. Timing analysis uses the local video and keeps the original subtitle backup.",12));
        var feedback=Body("",13);body.Children.Add(feedback);var cancellation=new CancellationTokenSource();window.Closed+=(_,_)=>{cancellation.Cancel();};
        Button? start=null;start=ActionButton(syncOnly?"Auto-sync saved subtitles":"Find and download",async()=>{
            if(!syncOnly&&english.IsChecked!=true&&arabic.IsChecked!=true){feedback.Text="Choose at least one language.";return;}
            start!.IsEnabled=false;feedback.Text=syncOnly?"Analyzing subtitle timing in the background…":"Matching subtitles for this episode / movie…";
            try{
                var engine=new SubtitleDownloads(store.DataDirectory,source:()=>{var c=downloads?.Config;return(c?.SubtitleAddonUrl??"",c?.SubtitleAddonOnly??true);});string message;
                if(syncOnly){var saved=MobileSubtitles.Find(item);var results=new List<string>();foreach(var sub in saved)results.Add(sub.Info.Name+" · "+await engine.Sync(item.Path,sub.Path,cancellation.Token));message=results.Count>0?string.Join("\n",results):"No external subtitles saved. Download or load a subtitle file first.";}
                else{
                    if(item.ImdbId.Length==0){using var metadata=new MetadataClient(store.DataDirectory);var id=await metadata.DownloadIdentity(item,cancellation.Token);if(id.Length==0)throw new IOException("Correct this title's match before finding subtitles.");store.SetDownloadIdentity(item.GroupKey,id);item.ImdbId=id;}
                    var release=stream?.Name??downloads?.Jobs().Where(j=>j.Target.Id==item.Id).OrderByDescending(j=>j.CreatedAt).FirstOrDefault()?.RemoteName??Path.GetFileName(item.Path);
                    message=await engine.Fetch(item,release,new[]{english.IsChecked==true?"en":"",arabic.IsChecked==true?"ar":""}.Where(l=>l.Length>0).ToList(),auto.IsChecked==true,cancellation.Token,streamTarget:streamTarget);
                }
                if(cancellation.IsCancellationRequested)return;feedback.Text=message;
                if(current?.Id==item.Id&&serial==playbackSerial&&player!=null){if(stream!=null)AttachStreamSubtitles();else foreach(var sub in MobileSubtitles.Find(item))player.AddSlave(MediaSlaveType.Subtitle,new Uri(sub.Path).AbsoluteUri,false);ApplyPreferredTracks();}
            }catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}catch(Exception e){feedback.Text=e is IOException?e.Message:"Couldn't get subtitles. Playback is unaffected; retry when connected.";}finally{if(!cancellation.IsCancellationRequested)start.IsEnabled=true;}
        },true);body.Children.Add(start);window.Show();
    }
    readonly HashSet<string> attachedStreamSubtitles=[];
    void AttachStreamSubtitles()
    {
        if(current==null||currentStream==null||player==null)return;
        var target=SubtitleDownloads.StreamTarget(store.DataDirectory,current,currentStream.Name);
        var preference=Preference(current).SubtitleTrack;var language=preference.Length==0?store.State.Subtitles:preference;
        foreach(var sub in MobileSubtitles.Find(target,requireVideo:false))
            if(!attachedStreamSubtitles.Contains(sub.Path)&&player.AddSlave(MediaSlaveType.Subtitle,new Uri(sub.Path).AbsoluteUri,SubtitleDownloads.Language(language)==SubtitleDownloads.SidecarLanguage(sub.Path)))attachedStreamSubtitles.Add(sub.Path);
    }
}
