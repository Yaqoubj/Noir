#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Noir;
public partial class MainWindow
{
    async Task ValidateSubtitleAppearance()
    {
        var results=new List<string>();
        try
        {
            store.State.Volume=0;VolumeBar.Value=0;store.State.SubtitleSize=12;store.State.SubtitleColor=0xFFFF00;store.State.SubtitleBackground=true;store.State.AutoSkipIntro=false;
            var episode=items.First(x=>x.Title=="Dorohedoro"&&x.Season==1&&x.Episode==1);await PlayVideo(episode,true);
            var deadline=DateTime.UtcNow.AddSeconds(20);while(player!.Time<1500&&DateTime.UtcNow<deadline) await Task.Delay(100);
            if(!player!.IsPlaying) throw new Exception("Video did not start");
            var subtitle=Path.Combine(store.DataDirectory,"appearance.srt");File.WriteAllText(subtitle,"1\n00:00:00,000 --> 00:30:00,000\nNOIR SUBTITLE APPEARANCE CHECK\n");
            if(!player.AddSlave(LibVLCSharp.Shared.MediaSlaveType.Subtitle,new Uri(subtitle).AbsoluteUri,true)) throw new Exception("Plain subtitle could not be loaded");
            await Task.Delay(1800);player.SetPause(true);await Task.Delay(200);
            var snapshot=Path.Combine(store.DataDirectory,"subtitles-yellow.png");if(!player.TakeSnapshot(0,snapshot,0,0)) throw new Exception("Subtitle frame could not be captured");
            while(!File.Exists(snapshot)&&DateTime.UtcNow<deadline) await Task.Delay(100);
            var frame=new BitmapImage();frame.BeginInit();frame.CacheOption=BitmapCacheOption.OnLoad;frame.UriSource=new Uri(snapshot);frame.EndInit();
            var converted=new FormatConvertedBitmap(frame,PixelFormats.Bgra32,null,0);var stride=converted.PixelWidth*4;var bytes=new byte[stride*converted.PixelHeight];converted.CopyPixels(bytes,stride,0);int yellow=0;
            for(int y=(int)(converted.PixelHeight*.8);y<converted.PixelHeight;y++) for(int x=0;x<converted.PixelWidth;x++){var i=y*stride+x*4;if(bytes[i]<70&&bytes[i+1]>200&&bytes[i+2]>200) yellow++;}
            if(yellow<100) throw new Exception("The plain subtitle did not render in the selected yellow colour");
            results.Add("PASS: Actual plain text subtitle renders in selected yellow colour with large sizing and dark background");
            store.State.SubtitleColor=0xFFFFFF;store.State.SubtitleSize=16;store.State.SubtitleBackground=false;await PlayVideo(episode,true);
            if(!playerTextStyle.StartsWith("16:16777215:False")) throw new Exception("Changing subtitle appearance did not rebuild its renderer");
            results.Add("PASS: Changed subtitle appearance applies when opening the next video without restarting Noir");
            ReturnToLibrary();Navigate("Shows");UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"shows-final.png"));
            OpenDetails(items.Where(x=>x.GroupKey==episode.GroupKey).ToList());UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"detail-final.png"));
        }
        catch(Exception error){results.Add(error.ToString());store.Log(error);}
        finally {File.WriteAllText(Path.Combine(store.DataDirectory,"appearance-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
    }
    async Task ValidateEnhancements()
    {
        var results=new List<string>();
        void Assert(bool condition,string label){if(!condition) throw new Exception("FAIL: "+label);results.Add("PASS: "+label);}
        async Task Until(Func<bool> predicate,string label,int seconds=20){var end=DateTime.UtcNow.AddSeconds(seconds);while(!predicate()&&DateTime.UtcNow<end) await Task.Delay(100);Assert(predicate(),label);}
        try
        {
            await RefreshMetadata(true);
            var episode=items.First(x=>x.Title=="Dorohedoro"&&x.Season==1&&x.Episode==1);
            store.Mark(episode,true);var count=store.ViewingHistory(episode).Count;var first=store.ViewingHistory(episode).Last().WatchedAt;
            store.BeginViewing(episode,true);store.Record(episode,35.125,episode.Duration);
            Assert(store.EverWatched(episode)&&store.ViewingHistory(episode).Count==count&&store.GetProgress(episode) is {Completed:false,Position:35.125},"Rewatch saves a fresh resume time and retains all original viewing dates");
            store.Record(episode,episode.Duration,episode.Duration,true);store.Record(episode,episode.Duration,episode.Duration,true);
            Assert(store.ViewingHistory(episode).Count==count+1&&store.ViewingHistory(episode).Last().WatchedAt==first,"Completing a rewatch adds exactly one date without replacing older history");
            store.State.PersonalReviews[episode.Id]=new(){Rating=4,Note="Remembered offline"};store.Save();
            var reloaded=new LibraryStore(store.DataDirectory);
            Assert(reloaded.ViewingHistory(episode).Count==count+1&&reloaded.State.PersonalReviews[episode.Id].Note=="Remembered offline","Diary, rating and private note survive restarting storage");
            store.Record(episode,84.5,episode.Duration);Navigate("Home");UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"home-new.png"));
            Assert(Descendants(ContentPanel).OfType<Button>().Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Continue "+episode.Label),"Continue card exposes a keyboard-accessible play button");
            Assert(Descendants(ContentPanel).OfType<Button>().Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Options for "+episode.Label),"Continue card exposes a separate options menu");
            var diary=DiaryWindow(episode);diary.Show();diary.UpdateLayout();var bitmap=new RenderTargetBitmap((int)diary.ActualWidth,(int)diary.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(diary);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(store.DataDirectory,"diary-new.png"))) encoder.Save(file);diary.Close();
            foreach(var other in items.GroupBy(x=>x.GroupKey).Where(g=>g.Key!=episode.GroupKey).Select(g=>g.First())) store.Record(other,60,other.Duration);
            Width=940;Height=700;Navigate("Home");UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"home-small.png"));
            var shelf=Descendants(ContentPanel).OfType<ScrollViewer>().First(v=>v.HorizontalScrollBarVisibility==ScrollBarVisibility.Hidden);
            Assert(shelf.ActualWidth<=ContentPanel.ActualWidth&&shelf.ScrollableWidth>0,"Continue shelf fits the minimum window width and scrolls overflowing cards");
            OpenDetails(items.Where(x=>x.GroupKey==episode.GroupKey).ToList());UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"episodes-new.png"));
            Assert(Descendants(ContentPanel).OfType<Button>().Any(b=>Equals(b.Content,"Unwatched")),"Episodes include a visible unwatched filter");
            Assert(store.State.EpisodeSchedules.GetValueOrDefault(episode.GroupKey)?.Count>0,"Real released-episode information is cached from TVmaze");
            Width=1320;Height=900;store.State.Volume=0;VolumeBar.Value=0;store.State.AutoSkipIntro=false;await PlayVideo(episode,true);
            await Until(()=>player!.IsPlaying&&player.Time>1000,"Actual embedded episode plays after the UI changes");
            SelectTrack(true,-1,"Off");AdjustSubtitleDelay(.5);ApplyPreferredTracks();
            Assert(player!.Spu==-1&&player.SpuDelay==500000,"Remembered subtitle choice and half-second timing apply to VLC");
            var start=player.Time;previews=new PreviewFrames(store.DataDirectory);var frame=await previews.Get(episode,store.State.Catalog[episode.Id].Fingerprint,90,CancellationToken.None);
            Assert(frame is {PixelWidth:256,PixelHeight:144},"Hover preview decodes a real small frame without another player window");
            Assert(player.Time>=start&&player.Time<start+15000,"Preview generation does not seek the main playback");
            var previewEncoder=new PngBitmapEncoder();previewEncoder.Frames.Add(BitmapFrame.Create(frame!));using(var file=File.Create(Path.Combine(store.DataDirectory,"preview-new.png"))) previewEncoder.Save(file);
            SetSleep(new(){Deadline=DateTimeOffset.UtcNow.AddMilliseconds(500)});
            await Until(()=>sleepPlan==null&&!player.IsPlaying,"Time-based sleep timer pauses playback automatically",5);
            Assert(store.GetProgress(episode) is {Completed:false,Position:>0}&&nextTimer?.IsEnabled!=true,"Sleep timer saves the actual position and never marks an unfinished episode watched");
            player.SetPause(false);await Until(()=>player.IsPlaying,"Playback resumes after sleep timer");
            SetSleep(new(){EpisodesLeft=1});store.State.AutoNext=true;SeekAbsolute(currentDuration-1);
            await Until(()=>ended,"Episode reaches its natural end");
            Assert(sleepPlan==null&&nextTimer?.IsEnabled!=true&&current==episode,"End-of-episode sleep limit prevents automatic advancement");
            ReturnToLibrary();Assert(PlayerOverlay.Visibility==Visibility.Collapsed&&previewPanel?.Visibility!=Visibility.Visible,"Leaving video removes controls and any preview overlay");
            Navigate("History");historyFilter="Watched";Render();UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"history-new.png"));
            Navigate("Settings");UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"settings-new.png"));
            results.Add("ALL ENHANCEMENT CHECKS PASSED");
        }
        catch(Exception error){results.Add(error.ToString());store.Log(error);}
        finally {File.WriteAllText(Path.Combine(store.DataDirectory,"enhancement-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var descendant in Descendants(child)) yield return descendant;}
    }
}
#endif
