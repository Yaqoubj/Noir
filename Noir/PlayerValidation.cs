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
            Press(System.Windows.Input.Key.F1);PlayerOverlay.UpdateLayout();Assert(shortcutSheet!=null&&shortcutSheet.Overlay.Parent==PlayerOverlay&&Descendants(shortcutSheet.Overlay).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text.Contains("Subtitles earlier"))&&Descendants(shortcutSheet.Overlay).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text=="Slower / faster"),"F1 opens the player shortcut sheet with speed and subtitle timing keys");Shot("shortcuts");
            Press(System.Windows.Input.Key.Escape);Assert(shortcutSheet==null&&PlayerRoot.Visibility==Visibility.Visible&&current==episode,"Esc closes the shortcut sheet without leaving the player");
            var timing=BuildSubtitleMenu().Items.OfType<System.Windows.Controls.MenuItem>().First(m=>m.Header?.ToString()?.StartsWith("Timing")==true).Items.OfType<System.Windows.Controls.MenuItem>().ToList();
            Assert(timing.Any(m=>m.Header?.ToString()=="Auto-sync to the audio…")&&timing.Any(m=>m.InputGestureText=="[")&&timing.Any(m=>m.InputGestureText=="]"),"Subtitle timing menu shows its keys and offers auto-sync");
            // Arabic: the sheet reads right to left inside the left-to-right player, and the timing menu is translated.
            Loc.Use("ar");Press(System.Windows.Input.Key.F1);PlayerOverlay.UpdateLayout();
            Assert(shortcutSheet!=null&&Descendants(shortcutSheet.Overlay).OfType<System.Windows.Controls.Border>().Any(b=>b.FlowDirection==FlowDirection.RightToLeft&&b.CornerRadius.TopLeft==16)&&Descendants(shortcutSheet.Overlay).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text=="[  ]"&&t.FlowDirection==FlowDirection.LeftToRight),"Arabic player shortcut sheet is right to left with left-to-right key caps");Shot("shortcuts-ar");Press(System.Windows.Input.Key.Escape);
            Assert(BuildSubtitleMenu().Items.OfType<System.Windows.Controls.MenuItem>().Any(m=>m.Header?.ToString()?.StartsWith("التوقيت")==true),"Arabic subtitle menu translates Timing");Loc.Use(store.State.Language);
            Prefs.VolumeBoost=true;VolumeBar.Value=80;ApplyVolume();await Until(()=>player.Volume==160,"Volume boost sends up to twice the slider volume to VLC");Prefs.VolumeBoost=false;VolumeBar.Value=0;ApplyVolume();await Until(()=>player.Volume==0,"Turning boost off restores the plain slider volume");
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
            Assert(nextTimer?.IsEnabled!=true,"Manual credits mode waits for the viewer");await Until(()=>{PlayerOverlay.UpdateLayout();return NextMedia.Visibility==Visibility.Visible&&NextMedia.ActualHeight>150;},"Up Next reserves a 16:9 still box");
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
            await ValidateJustWatched(results,episode);
            results.Add("ALL PLAYER CHECKS PASSED");
        }
        catch(Exception error){results.Add(error.ToString());store.Log(error);}
        finally{File.WriteAllText(Path.Combine(store.DataDirectory,"player-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
    }
    async Task ValidateJustWatched(List<string> results,VideoItem episode)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);results.Add("PASS: "+label);}
        async Task Until(Func<bool> predicate,string label){var until=DateTime.UtcNow.AddSeconds(20);while(!predicate()&&DateTime.UtcNow<until)await Task.Delay(100);Check(predicate(),label);}
        void Click(string name){var button=Descendants(justWatchedCard!).OfType<System.Windows.Controls.Button>().First(b=>System.Windows.Automation.AutomationProperties.GetName(b)==name);button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
        void Key(System.Windows.UIElement element,System.Windows.Input.Key key)=>element.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,Environment.TickCount,key){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});
        async Task Shot(string name){UpdateLayout();await Task.Delay(450);CaptureLibrary(Path.Combine(store.DataDirectory,"player-just-watched"+name+".png"));}
        var language=store.State.Language;var previous=JsonSerializer.Serialize(HistoryPrefs);var review=store.State.PersonalReviews.GetValueOrDefault(episode.Id);var reaction=store.State.Reactions.GetValueOrDefault(episode.Id)??"";
        try
        {
            SetLanguage("en");SetAfterWatching("Always");HistoryPrefs.JustWatchedNoticeShown=false;validateJustWatched=true;Navigate("Home");store.SetReview10(episode,0,"");store.SetReaction(episode.Id,"");
            await PlayVideo(episode,true);await Until(()=>player?.IsPlaying==true,"Just watched: real episode starts");
            Check(justWatchedCard==null,"No corner card between autoplayed episodes or while playing");
            PlaybackEnded();CancelNext();ReturnToLibrary();await Until(()=>justWatchedCard!=null,"Finishing an episode then returning shows the corner card");
            Check(!justWatchedCard!.IsKeyboardFocusWithin,"The corner card never takes page keyboard focus");await Shot("");
            Click("Loved");Check(store.State.Reactions.GetValueOrDefault(episode.Id)=="loved","A Just watched reaction click stores the existing reaction format");
            var rating=Descendants(justWatchedCard!).OfType<System.Windows.Controls.Button>().First(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Your rating · optional");
            rating.ContextMenu!.Items.OfType<System.Windows.Controls.MenuItem>().First(m=>Equals(m.Tag,7)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Check(store.State.PersonalReviews.GetValueOrDefault(episode.Id)?.Rating10==7,"The corner card saves a half-star rating");
            Click("Add a note…");var editor=Descendants(justWatchedCard!).OfType<System.Windows.Controls.TextBox>().Single();editor.Text="A quiet ending.";Key(editor,System.Windows.Input.Key.Enter);
            Check(store.State.PersonalReviews[episode.Id].Note=="A quiet ending."&&editor.Visibility==Visibility.Collapsed,"Enter saves the inline note and shows its snippet");
            Click("Edit note");editor.Text="Cancelled edit";Key(editor,System.Windows.Input.Key.Escape);Check(store.State.PersonalReviews[episode.Id].Note=="A quiet ending."&&justWatchedCard!=null,"Escape cancels a note edit without dismissing the card");
            Click("Edit note");editor.Text="Saved on focus loss.";editor.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,Environment.TickCount,editor,SearchBox){RoutedEvent=System.Windows.Input.Keyboard.LostKeyboardFocusEvent});
            Check(store.State.PersonalReviews[episode.Id].Note=="Saved on focus loss.","Leaving the inline editor saves the note");
            Click("What others said");Check(justWatchedReviewsOpened==episode.Id,"What others said dispatches the exact episode to OpenReviews (validation stub)");
            Click("Dismiss Just watched");Check(HistoryPrefs.JustWatchedIgnores==0,"Dismissing an interacted card does not count as an ignore");
            SetLanguage("ar");OfferJustWatched(episode);await Shot("-ar");
            Check(justWatchedCard!.FlowDirection==FlowDirection.RightToLeft&&Descendants(justWatchedCard).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text=="شاهدت للتو"),"Arabic corner card translates its controls and reads right to left");
            Check(justWatchedCard.TranslatePoint(new Point(justWatchedCard.ActualWidth/2,0),this).X<ActualWidth/2,"Arabic places Just watched at the bottom-left");DismissJustWatched(false);SetLanguage("en");
            var inline=JustWatchedPanel(episode,true);inline.Measure(new Size(420,double.PositiveInfinity));inline.Arrange(new Rect(new Point(),inline.DesiredSize));
            Check(Descendants(inline).OfType<System.Windows.Controls.WrapPanel>().First().DesiredSize.Height<35&&inline.DesiredSize.Width<=420,"Compact reactions, half-stars and note/comments links fit one 420 px row");
            Check(Descendants(inline).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text=="Saved on focus loss.")&&Descendants(inline).OfType<System.Windows.Controls.Button>().Any(b=>System.Windows.Automation.AutomationProperties.GetHelpText(b)=="Selected"),"History inline controls reflect the saved note and reaction");
            for(int n=1;n<=5;n++){OfferJustWatched(episode);Click("Dismiss Just watched");Check(HistoryPrefs.JustWatchedIgnores==n,$"Closing an untouched card counts consecutive ignore {n}");}
            Check(HistoryPrefs.JustWatchedQuiet&&HistorySettings.Load(store.DataDirectory).JustWatchedQuiet&&justWatchedCard?.Tag as string=="quiet","Five ignores persist quiet mode and show its one-time notice");
            Click("Keep asking");Check(HistoryPrefs.AfterWatching=="Always"&&!HistoryPrefs.JustWatchedQuiet&&HistoryPrefs.JustWatchedIgnores==0,"Keep asking restores Always and resets ignores");
            HistoryPrefs.JustWatchedQuiet=true;SaveHistoryPrefs();OfferJustWatched(episode);Check(justWatchedCard==null,"Quiet mode suppresses the next corner card");
            Check(Descendants(JustWatchedPanel(episode,true)).OfType<System.Windows.Controls.Button>().Any(),"Quiet mode keeps History inline feedback available");
            SetAfterWatching("Never");OfferJustWatched(episode);Check(justWatchedCard==null,"Never suppresses the corner card");
            SetAfterWatching("Always");OfferJustWatched(episode,true);Check(justWatchedCard==null,"A running Up Next countdown suppresses Just watched");
            validateJustWatched=false;OfferJustWatched(episode);Check(justWatchedCard==null,"Ordinary validation never shows Just watched without an explicit trigger");validateJustWatched=true;
            await PlayVideo(episode,true);await Until(()=>player?.IsPlaying==true,"Halfway-stop test starts playback");creditsReached=false;CancelNext();ReturnToLibrary();await Until(()=>current==null&&LibraryRoot.Visibility==Visibility.Visible,"Stopping partway returns to the library");await Task.Delay(1000);
            Check(justWatchedCard==null,"Stopping halfway through an already watched episode shows no corner card");
        }
        finally
        {
            DismissJustWatched(false);validateJustWatched=false;historySettings=JsonSerializer.Deserialize<HistorySettings>(previous);SaveHistoryPrefs();store.SetReview10(episode,review?.Rating10??0,review?.Note??"");store.SetReaction(episode.Id,reaction);SetLanguage(language);
        }
    }
}
#endif
