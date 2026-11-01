#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Noir;
public partial class MainWindow
{
    // --player-only: skip pills, Up Next card, still-watching, chapters, speed and boost on a real local episode.
    async Task ValidatePlayer()
    {
        var results=new List<string>();
        void Assert(bool condition,string label){if(!condition) throw new Exception("FAIL: "+label);results.Add("PASS: "+label);}
        async Task Until(Func<bool> predicate,string label,int seconds=20){var end=DateTime.UtcNow.AddSeconds(seconds);while(!predicate()&&DateTime.UtcNow<end) await Task.Delay(100);Assert(predicate(),label);}
        void Shot(string name){PlayerOverlay.UpdateLayout();var w=(int)Math.Max(1,PlayerOverlay.ActualWidth);var h=(int)Math.Max(1,PlayerOverlay.ActualHeight);var bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var context=drawing.RenderOpen()){context.DrawRectangle(Brushes.Black,null,new Rect(0,0,w,h));context.DrawRectangle(new VisualBrush(PlayerOverlay),null,new Rect(0,0,w,h));}bitmap.Render(drawing);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(store.DataDirectory,"player-"+name+".png"));encoder.Save(file);}
        try
        {
            store.State.Volume=0;VolumeBar.Value=0;store.State.AutoNext=true;store.Save();
            var episode=items.Where(i=>!i.IsMovie&&CanPlay(i)).FirstOrDefault(i=>LibraryStore.Next(i,items,store.State) is {} n&&CanPlay(n))??throw new Exception("No playable episode with a next episode");
            Prefs.SkipIntro=Prefs.SkipRecap=Prefs.SkipCredits=SkipMode.Manual;Prefs.StillWatchingAfter=3;Prefs.VolumeBoost=false;Prefs.DefaultSpeed=1;
            Width=1320;Height=900;await PlayVideo(episode,true);
            await Until(()=>player?.IsPlaying==true&&player.Time>1500,"A real local episode plays");
            SetSpeed(1.5);await Task.Delay(300);Assert(Math.Abs(player!.Rate-1.5f)<.01&&Equals(SpeedButton.Content,"1.5×"),"Speed button changes VLC playback rate");SetSpeed(1);
            void Press(System.Windows.Input.Key key)=>Window_KeyDown(this,new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(PlayerOverlay)!,Environment.TickCount,key){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});
            Assert((SpeedButton.ToolTip as string)?.Contains("Shift")==true,"Speed button tooltip names its keyboard shortcut");
            Press(System.Windows.Input.Key.F1);PlayerOverlay.UpdateLayout();Assert(shortcutSheet?.Parent==PlayerOverlay&&Descendants(shortcutSheet).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text.Contains("Subtitles earlier"))&&Descendants(shortcutSheet).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text=="Slower / faster"),"F1 opens the player shortcut sheet with speed and subtitle timing keys");Shot("shortcuts");
            Press(System.Windows.Input.Key.Escape);Assert(shortcutSheet==null&&PlayerRoot.Visibility==Visibility.Visible&&current==episode,"Esc closes the shortcut sheet without leaving the player");
            var timing=BuildSubtitleMenu().Items.OfType<System.Windows.Controls.MenuItem>().First(m=>m.Header?.ToString()?.StartsWith("Timing")==true).Items.OfType<System.Windows.Controls.MenuItem>().ToList();
            Assert(timing.Any(m=>m.Header?.ToString()=="Auto-sync to the audio…")&&timing.Any(m=>m.InputGestureText=="[")&&timing.Any(m=>m.InputGestureText=="]"),"Subtitle timing menu shows its keys and offers auto-sync");
            // Arabic: the sheet reads right to left inside the left-to-right player, and the timing menu is translated.
            Loc.Use("ar");Press(System.Windows.Input.Key.F1);PlayerOverlay.UpdateLayout();
            Assert(shortcutSheet!=null&&Descendants(shortcutSheet).OfType<System.Windows.Controls.Border>().Any(b=>b.FlowDirection==FlowDirection.RightToLeft&&b.CornerRadius.TopLeft==16)&&Descendants(shortcutSheet).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text=="[  ]"&&t.FlowDirection==FlowDirection.LeftToRight),"Arabic player shortcut sheet is right to left with left-to-right key caps");Shot("shortcuts-ar");Press(System.Windows.Input.Key.Escape);
            Assert(BuildSubtitleMenu().Items.OfType<System.Windows.Controls.MenuItem>().Any(m=>m.Header?.ToString()?.StartsWith("التوقيت")==true),"Arabic subtitle menu translates Timing");Loc.Use(store.State.Language);
            Prefs.VolumeBoost=true;VolumeBar.Value=80;ApplyVolume();Assert(player.Volume==160,"Volume boost sends up to twice the slider volume to VLC");Prefs.VolumeBoost=false;VolumeBar.Value=0;ApplyVolume();Assert(player.Volume==0,"Turning boost off restores the plain slider volume");
            chapterMarks.Clear();chapterMarks.AddRange([(0d,"Previously"),(currentDuration*.3,"Act One"),(currentDuration*.7,"Act Two")]);DrawChapterTicks();
            Assert(ChapterTicks.Children.Count==2,"Chapter ticks are drawn on the seek bar");Assert(ChapterAt(currentDuration*.5)=="Act One","Hover label knows the chapter under the pointer");
            JumpChapter(1);await Until(()=>pendingTarget==null&&player.Time/1000d>=currentDuration*.3-2,"Next chapter jumps to the following chapter start");
            SeekAbsolute(5);await Until(()=>pendingTarget==null,"Seek back to the recap");
            recapSegment=new(){Start=0,End=40};handledSegments.Clear();ShowControls();
            await Until(()=>SkipIntro.Visibility==Visibility.Visible&&Equals(SkipIntro.Content,SkipButtonText(SegmentKind.Recap)),"Manual mode shows a Skip recap button");Shot("skip-recap");
            Prefs.SkipRecap=SkipMode.Auto;
            await Until(()=>(SkipIntro.Content as string)?.StartsWith(SkipNoticeText(SegmentKind.Recap))==true,"Auto mode first shows a Skipping recap notice with Undo");Shot("skip-notice");
            TogglePause();await Until(()=>player!.State==LibVLCSharp.Shared.VLCState.Paused,"Playback pauses during an automatic skip notice");
            var noticeRemaining=pausedNotice;var pausedPosition=player!.Time;await Task.Delay(2500);
            Assert(noticeRemaining is {TotalSeconds:>0}&&pausedNotice==noticeRemaining&&noticeTimer?.IsEnabled!=true&&Math.Abs(player.Time-pausedPosition)<500,"Pause preserves the skip notice delay without seeking");
            TogglePause();await Until(()=>player.IsPlaying,"Playback resumes with the pending skip");
            Assert((noticeEnds-DateTime.UtcNow).TotalSeconds>noticeRemaining!.Value.TotalSeconds-.5,"Resume keeps the remaining skip delay");
            await Until(()=>pendingTarget==null&&player.Time/1000d>=39,"Auto mode skips the recap after the remaining delay",8);
            recapSegment=new(){Start=player.Time/1000d+2,End=player.Time/1000d+60};handledSegments.Clear();
            await Until(()=>noticeKind==SegmentKind.Recap,"A second recap triggers the notice again",8);SkipPillClicked();await Task.Delay(2600);
            Assert(player.Time/1000d<recapSegment.End-5,"Undo cancels the automatic skip");recapSegment=null;Prefs.SkipRecap=SkipMode.Manual;
            SeekAbsolute(store.CompletionStart(episode,currentDuration)+1);
            await Until(()=>creditsReached&&NextPanel.Visibility==Visibility.Visible,"The Up Next card appears when credits start");
            Assert(nextTimer?.IsEnabled!=true,"Manual credits mode waits for the viewer");Assert(NextMedia.Visibility==Visibility.Visible&&NextMedia.ActualHeight>150,"Up Next reserves a 16:9 still box");
            await Task.Delay(600);Shot("upnext");
            StartCountdown();await Task.Delay(2500);Assert(NextRing.Data!=null&&((string)NextCountdown.Text).Contains('6'),"The ring counts down around the play button");Shot("upnext-ring");
            TogglePause();await Until(()=>player!.State==LibVLCSharp.Shared.VLCState.Paused,"Playback pauses during Up Next");
            var countdownRemaining=pausedCountdown;var countdownText=NextCountdown.Text;await Task.Delay(8500);
            Assert(countdownRemaining is {TotalSeconds:>0}&&pausedCountdown==countdownRemaining&&nextTimer?.IsEnabled!=true&&current==episode&&NextCountdown.Text==countdownText,"Pause freezes Up Next beyond its original deadline");
            TogglePause();await Until(()=>player.IsPlaying,"Playback resumes with Up Next pending");
            Assert((countdownEnds-DateTime.UtcNow).TotalSeconds>countdownRemaining!.Value.TotalSeconds-.5,"Resume keeps the remaining Up Next delay");CancelNext();
            Assert(NextPanel.Visibility==Visibility.Collapsed&&nextTimer?.IsEnabled!=true,"Cancel stops the countdown and hides the card");
            stillWatching.UserInput();stillWatching.AutoAdvanced();stillWatching.AutoAdvanced();stillWatching.AutoAdvanced();
            var next=nextItem;AutoAdvance();Assert(StillWatchingPanel.Visibility==Visibility.Visible&&current==episode,"After three automatic episodes the player asks Still watching instead of starting the next");
            Shot("still-watching");
            StillWatchingContinue_Click(this,new RoutedEventArgs());await Until(()=>current==next&&player.IsPlaying,"Continue starts the next episode");
            Assert(stillWatching.Count==0,"Continuing counts as viewer input");
            creditsReached=true;ShowUpNext(nextItem??episode,NextCountdown.Text);StartCountdown();CancelNext_Click(this,new RoutedEventArgs());PlaybackEnded();
            Assert(creditsPromptDismissed&&nextTimer?.IsEnabled!=true&&pausedCountdown==null,"Cancelling during credits prevents a new countdown at natural completion");
            Assert(nextItem==null||NextPanel.Visibility==Visibility.Visible&&NextPlay.IsEnabled&&endAction==null,"Manual Play now remains available after cancelling Up Next");
            detailTab="Activity";detailSeason=null;PlayerEpisodes_Click(this,new RoutedEventArgs());
            Assert(detailKey==next!.GroupKey&&detailTab=="Episodes"&&detailSeason==next.Season&&episodeFilter=="All episodes","Player Episodes opens the current season on the Episodes tab");
            Prefs.SkipIntro=SkipMode.Off;SavePrefs();Assert(!store.State.AutoSkipIntro&&PlaybackPrefs.Load(store.DataDirectory,true).SkipIntro==SkipMode.Off,"Skip choices persist per profile and keep the legacy flag in step");
            Navigate("Settings");UpdateLayout();
            results.Add("ALL PLAYER CHECKS PASSED");
        }
        catch(Exception error){results.Add(error.ToString());store.Log(error);}
        finally{File.WriteAllText(Path.Combine(store.DataDirectory,"player-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
    }
}
#endif
