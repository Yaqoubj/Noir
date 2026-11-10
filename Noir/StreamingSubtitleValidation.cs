#if NOIR_VALIDATE
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Noir;
public partial class MainWindow
{
    async Task ValidateStreamingSubtitles()
    {
        var results=new List<string>();
        void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);results.Add("PASS: "+label);}
        async Task Wait(Func<bool> ready,string label){for(int n=0;n<200&&!ready();n++)await Task.Delay(100);Check(ready(),label);}
        using var listener=new HttpListener();using var cancel=new CancellationTokenSource();
        try
        {
            var fixture=Environment.GetEnvironmentVariable("NOIR_STREAM_FIXTURE")!;
            var portProbe=new TcpListener(IPAddress.Loopback,0);portProbe.Start();var port=((IPEndPoint)portProbe.LocalEndpoint).Port;portProbe.Stop();
            var url=$"http://127.0.0.1:{port}/video.mkv";listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
            _=Task.Run(async()=>{while(!cancel.IsCancellationRequested)try{
                var context=await listener.GetContextAsync();
                _=Task.Run(async()=>{try{
                    await using var file=File.OpenRead(fixture);var response=context.Response;var range=context.Request.Headers["Range"];
                    long start=0,end=file.Length-1;
                    if(range!=null&&System.Text.RegularExpressions.Regex.Match(range,@"bytes=(\d+)-(\d*)") is {Success:true} match){start=long.Parse(match.Groups[1].Value);if(match.Groups[2].Length>0)end=Math.Min(end,long.Parse(match.Groups[2].Value));response.StatusCode=206;response.Headers["Content-Range"]=$"bytes {start}-{end}/{file.Length}";}
                    response.ContentType="video/x-matroska";response.Headers["Accept-Ranges"]="bytes";response.ContentLength64=end-start+1;
                    if(context.Request.HttpMethod!="HEAD"){file.Position=start;var buffer=new byte[65536];long left=end-start+1;while(left>0){var count=await file.ReadAsync(buffer.AsMemory(0,(int)Math.Min(left,buffer.Length)),cancel.Token);if(count==0)break;await response.OutputStream.WriteAsync(buffer.AsMemory(0,count),cancel.Token);left-=count;}}
                }catch{}finally{context.Response.Close();}});
            }catch when(cancel.IsCancellationRequested){break;}});
            var item=new VideoItem{Id="stream-validation",ImdbId="tt123",Title="Streaming subtitle validation",Season=1,Episode=2,Path=Path.Combine(store.DataDirectory,"missing.mkv")};
            var option=new StreamOption(url,"Fixture.WEB.mkv","","1080p",0,true,"","","");
            var target=SubtitleDownloads.StreamTarget(store.DataDirectory,item,option.Name);Directory.CreateDirectory(Path.GetDirectoryName(target.Path)!);
            await File.WriteAllTextAsync(Path.ChangeExtension(target.Path,"en.srt"),"1\n00:00:00,000 --> 00:01:50,000\nSTREAMING ENGLISH\n");
            await File.WriteAllTextAsync(Path.ChangeExtension(target.Path,"ar.srt"),"1\n00:00:00,000 --> 00:01:50,000\nمرحبا\n");
            store.State.Subtitles="English";store.State.AutoSkipIntro=false;VolumeBar.Value=0;
            await PlayStream(item,option);await Wait(()=>player?.IsPlaying==true&&player.Time>500,"HTTP stream plays without a local video");
            await Wait(()=>attachedStreamSubtitles.Count==2&&player!.Spu>=0,"Cached streaming subtitles attach and select a track");
            var menu=BuildSubtitleMenu();Check(menu.Items.OfType<MenuItem>().Any(m=>m.Header?.ToString()=="Use saved Arabic subtitles"),"Saved language choices appear in the streaming subtitle menu");
            Check(!menu.Items.OfType<MenuItem>().Any(m=>m.Header?.ToString()=="Auto-sync subtitle timing…"),"Streaming menu excludes unavailable local timing analysis");
            static IEnumerable<MenuItem> AllItems(ItemsControl parent)=>parent.Items.OfType<MenuItem>().SelectMany(m=>AllItems(m).Prepend(m));
            Check(!AllItems(menu).Any(m=>m.Header?.ToString()=="Auto-sync needs a downloaded video"),"Streaming Timing menu no longer says auto-sync needs a downloaded video");
            var sync=AllItems(menu).FirstOrDefault(m=>m.Header?.ToString()=="Auto-sync to the audio…");
            Check(sync!=null&&sync.IsEnabled,"Streaming Timing menu offers Auto-sync to the audio, the same item as downloaded videos");
            sync!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Task.Delay(300);
            var syncSheet=FindSheet("Auto-sync to the audio");
            Check(syncSheet!=null&&syncSheet.FooterButtons.Children.OfType<Button>().Select(b=>b.Content?.ToString()).SequenceEqual(["Cancel","Start"]),"Stream Auto-sync opens a sheet with Cancel and Start, without reading any media yet");
            syncSheet!.FooterButtons.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(300);
            Check(FindSheet("Auto-sync to the audio")==null&&attachedStreamSubtitles.Count==2,"Cancel closes the stream Auto-sync sheet and leaves the subtitles as they were");
            var arabic=menu.Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()=="Use saved Arabic subtitles");arabic.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Task.Delay(500);
            Check(Preference(item).SubtitleTrack=="Arabic"&&player!.Spu>=0,"Selecting saved Arabic updates playback and preference");
            AdjustSubtitleDelay(.5);Check(player!.SpuDelay==500000,"Timing adjustment applies during HTTP streaming");
            SelectTrack(true,-1,"Off");await PlayStream(item,option);await Wait(()=>player!.IsPlaying&&player.Time>500,"The same stream can reopen");await Task.Delay(2000);
            Check(player!.Spu<0,"Subtitles Off survives reopening a cached stream");
            Check(player.SpuDelay==500000,"Streaming subtitle timing survives reopening");
            DownloadSubtitleFiles(item);await Task.Delay(200);
            var dialog=FindSheet("Subtitles")!;
            Check(ViewElements(dialog.Body).OfType<TextBlock>().Any(t=>t.Text.Contains("while you stream")),"Subtitle download dialog explains streaming support");dialog.Close(false);
            Check(!File.Exists(item.Path)&&!File.Exists(target.Path),"Playback never downloads a video to the subtitle cache");
            // A timing-corrected copy of a built-in track wins over the plain track of its language.
            await File.WriteAllTextAsync(SubtitleDownloads.SyncedCopy(target.Path,"en",".srt"),"1\n00:00:00,000 --> 00:01:50,000\nSYNCED ENGLISH\n");
            Preference(item).SubtitleTrack="English";await PlayStream(item,option);await Wait(()=>player?.IsPlaying==true&&player.SpuDescription.Count(t=>t.Id>=0)>=3,"Corrected subtitle copy attaches next to the others");
            ApplyPreferredTracks();var english=player!.SpuDescription.Where(t=>t.Id>=0&&t.Name.Contains("English")).ToList();
            Check(english.Count>=2&&player.Spu==english[^1].Id,"English picks the corrected copy, added after the others · "+string.Join(" | ",player.SpuDescription.Select(t=>t.Id+"="+t.Name))+" · selected "+player.Spu);
            ReturnToLibrary();
        }
        catch(Exception e){results.Add(e.Message.StartsWith("FAIL:")?e.Message:"FAIL: "+e);}
        finally{cancel.Cancel();listener.Stop();File.WriteAllText(Path.Combine(store.DataDirectory,"stream-subtitle-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
    }
}
#endif
