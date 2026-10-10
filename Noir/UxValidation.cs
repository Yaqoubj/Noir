#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Noir;
public partial class MainWindow
{
    async Task ValidatePlayerSmoothness()
    {
        var samples=new List<double>();var gaps=new List<double>();var memory=new List<long>();var report=new Dictionary<string,object>();
        var clock=System.Diagnostics.Stopwatch.StartNew();var last=clock.Elapsed.TotalMilliseconds;
        var heartbeat=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};heartbeat.Tick+=(_,_)=>{var now=clock.Elapsed.TotalMilliseconds;gaps.Add(now-last);last=now;};
        async Task Wait(Func<bool> condition){var timeout=DateTime.UtcNow.AddSeconds(25);while(!condition()&&DateTime.UtcNow<timeout)await Task.Delay(80);if(!condition())throw new Exception("Playback transition timed out");}
        try
        {
            var episode=items.First(i=>!i.IsMovie&&CanPlay(i));store.State.Volume=0;VolumeBar.Value=0;store.State.AutoSkipIntro=false;store.State.AutoNext=false;heartbeat.Start();
            for(int i=0;i<10;i++)
            {
                var start=clock.Elapsed.TotalMilliseconds;await PlayVideo(episode,true);await Wait(()=>player?.IsPlaying==true&&player.Time>350);samples.Add(clock.Elapsed.TotalMilliseconds-start);
                SeekAbsolute(41.75,false);await Wait(()=>pendingTarget==null);player!.SetPause(true);await Task.Delay(100);SavePlayback();ReturnToLibrary();await store.FlushPendingAsync();memory.Add(System.Diagnostics.Process.GetCurrentProcess().WorkingSet64);
            }
            await PlayVideo(episode,true);await Wait(()=>player?.IsPlaying==true&&player.Time>500);HideControls();
            uint width=0,height=0;player!.Size(0,ref width,ref height);report["VideoSize"]=$"{width}x{height}";
            var warmed=System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
            await Task.Delay(TimeSpan.FromMinutes(10));
            report["StillPlayingAfterTenMinutes"]=player.IsPlaying;report["TenMinutePositionSeconds"]=player.Time/1000d;
            var stats=media!.Statistics;report["DecodedVideoFrames"]=stats.DecodedVideo;report["LostPictures"]=stats.LostPictures;
            report["WorkingSetGrowthMB"]=(System.Diagnostics.Process.GetCurrentProcess().WorkingSet64-warmed)/1048576d;
            ReturnToLibrary();await store.FlushPendingAsync();
            report["TenOpenSeekPauseBackCyclesCompleted"]=samples.Count;report["OpenToAdvancingVideoMs"]=samples;
            report["WorkingSetAfterCyclesMB"]=memory.Select(n=>n/1048576d).ToList();
            var sorted=gaps.Order().ToList();report["DispatcherGapP95Ms"]=sorted[(int)(sorted.Count*.95)];report["DispatcherGapMaxMs"]=sorted[^1];report["DispatcherGapsAbove200Ms"]=gaps.Count(n=>n>200);
        }
        catch(Exception error){report["Failure"]=error.ToString();}
        finally{heartbeat.Stop();File.WriteAllText(Path.Combine(store.DataDirectory,"player-smoothness.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}
    }
    async Task ValidateUx()
    {
        var checks=new List<string>();void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add(text);}
        try
        {
            Navigate("Home");foreach(var width in new[]{940,1320,1920}){Width=width;Height=900;UpdateLayout();await Task.Delay(100);CaptureLibrary(Path.Combine(store.DataDirectory,$"home-{width}.png"));Check(ContentPanel.ActualWidth>500,$"Home fits at width {width}");}
            Width=1320;Navigate("History");UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"history-refined.png"));
            Check(!Descendants(ContentPanel).OfType<Button>().Any(b=>b.ToolTip?.ToString()?.StartsWith("Rate ")==true),"History cards keep rating controls out of the browsing grid");
            var group=store.Archive.Where(i=>!i.IsMovie).GroupBy(i=>i.GroupKey).First().ToList();OpenDetails(group);UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"detail-refined.png"));
            Check(Descendants(ContentPanel).OfType<Button>().Any(b=>b.Content as string=="About")&&Descendants(ContentPanel).OfType<Button>().Any(b=>b.Content as string=="Activity"),"Details separate episodes, information and activity");
            detailTab="About";Render();Check(!Descendants(ContentPanel).OfType<Button>().Any(b=>b.Content as string=="Correct match / episode numbers"),"Secondary matching tools stay in the title menu");
            Navigate("Settings");Check(Descendants(ContentPanel).OfType<Button>().Any(b=>b.Content as string=="Downloads")&&!Descendants(ContentPanel).OfType<PasswordBox>().Any(),"Settings show one category without exposing unrelated credential controls");
            ContentPanel.Children.Clear();var generated=Enumerable.Range(1,101).Select(n=>new List<VideoItem>{new(){Title="Fixture "+n}}).ToList();var created=0;
            PagedCards(generated,"Large catalogue",g=>{created++;return new TextBlock{Text=g[0].Title};});Check(created==40,"Large catalogues create only forty initial cards");
            var more=Descendants(ContentPanel).OfType<Button>().First();more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(created==80,"Loading more adds cards without rebuilding existing ones");
            var list=Enumerable.Range(1,300).Select(n=>new DownloadChoice{Id=n.ToString(),Name="Release "+n,Resolution=n%2==0?"1080p":"2160p",Size=1500000000,CacheStatus=n%3==0?"Ready":"Unknown",Languages="English",SourceEntries=3}).ToList();
            var host=new Window{Owner=this,ShowActivated=false,Title="Release grouping validation",Width=820,Height=730,Background=Brush("#101012")};var content=ReleasePicker(list,new(){PreferredResolution="1080p"},_=>{});host.Content=new ScrollViewer{Content=content};host.Show();host.UpdateLayout();
            Check(Descendants(content).OfType<Button>().Count(b=>b.Content as string=="Choose files")==3,"Hundreds of releases initially show only three suggested choices");
            var quality=Descendants(content).OfType<Expander>().First(e=>e.Header?.ToString()?.StartsWith("1080p")==true);quality.IsExpanded=true;host.UpdateLayout();
            Check(Descendants(content).OfType<Button>().Count(b=>b.Content as string=="Choose files")==23,"Expanding a quality renders only one batch of twenty releases");
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(820,730,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render((System.Windows.Media.Visual)host.Content);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(store.DataDirectory,"grouped-releases.png")))encoder.Save(output);host.Close();
            File.WriteAllText(Path.Combine(store.DataDirectory,"ux-checks.json"),JsonSerializer.Serialize(checks));
        }
        catch(Exception error){File.WriteAllText(Path.Combine(store.DataDirectory,"ux-failure.txt"),error.ToString());}
    }
}
#endif
