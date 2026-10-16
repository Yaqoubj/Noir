using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using System.Text.RegularExpressions;

namespace Noir;
public partial class MainWindow
{
    LibVLC? vlc;
    LibVLCSharp.Shared.MediaPlayer? player;
    Media? media;
    DispatcherTimer? tick, nextTimer, hideTimer;
    EventHandler<EventArgs>? endedHandler,errorHandler,playingHandler;
    VideoItem? current, nextItem;
    bool ended, seeking, fullscreen, tracksSelected, muted;
    int serial, countdown;
    double lastPosition, currentDuration;
    double? pendingTarget;
    bool seekSent, saveAfterSeek;
    DateTime seekStarted, lastSave=DateTime.MinValue;
    DateTimeOffset lastPlaybackRevision;
    WindowState oldWindowState;
    Rect oldWindowBounds;
    WindowStyle oldWindowStyle;
    ResizeMode oldResizeMode;
    Point? lastPointer;
    string? appliedCrop;
    bool pictureConfigured;
    bool introHandled,creditsReached,chaptersChecked;
    bool creditsPromptDismissed,creditsExpanded;
    ContextMenu? trackMenu;
    bool ControlsVisible => PlayerHeader.Visibility==Visibility.Visible;
    string playerTextStyle="";
    bool openingVideo;
    DispatcherTimer? clickTimer;
    DispatcherTimer? hintTimer;
    void ShowPlayerHint(string text){PlayerHint.Text=text;PlayerHint.Visibility=Visibility.Visible;hintTimer??=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1.4)};hintTimer.Stop();hintTimer.Tick-=HidePlayerHint;hintTimer.Tick+=HidePlayerHint;hintTimer.Start();}
    void HidePlayerHint(object? sender,EventArgs e){hintTimer?.Stop();PlayerHint.Visibility=Visibility.Collapsed;}
    double libraryScroll;
    IInputElement? libraryFocus;
    bool restoreLibraryView;
    string libraryFocusName="";
    readonly SemaphoreSlim nativeTransition=new(1,1);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetDoubleClickTime();
    async Task StopPlayer(LibVLCSharp.Shared.MediaPlayer native)
    {
        await nativeTransition.WaitAsync();try{if(!ReferenceEquals(player,native)||closing)return;await Task.Run(native.Stop);}finally{nativeTransition.Release();}
    }
    DateTime pointerSuppressedUntil;
    Task<LibVLC>? warmEngine;
    string warmStyle="";
    string EngineStyle()=>$"{store.State.SubtitleSize}:{store.State.SubtitleColor}:{store.State.SubtitleBackground}:{store.State.Subtitles}";
    string[] EngineOptions()=>["--no-video-title-show","--quiet","--avcodec-hw=any",store.State.Subtitles=="Arabic"?"--sub-language=ara,ar":"--sub-language=eng,en",$"--freetype-rel-fontsize={store.State.SubtitleSize}",$"--freetype-color={store.State.SubtitleColor}",$"--freetype-background-opacity={(store.State.SubtitleBackground?180:0)}"];
    // Prepare VLC while the library is idle so the first video opens immediately.
    void WarmPlayer()
    {
        if(closing||validationMode||player!=null||warmEngine!=null) return;
        warmStyle=EngineStyle();var options=EngineOptions();
        StartupTrace.Mark("Player warm-up started");
        warmEngine=Task.Run(()=>{var engine=PlayerWarmup.CreateEngine(options);StartupTrace.Mark("Player warm-up finished");PlayerWarmup.PreloadCommonPlugins();return engine;});
    }
    async Task<LibVLC?> TakeWarmEngine(string style)
    {
        var pending=warmEngine;warmEngine=null;if(pending==null) return null;
        if(warmStyle==style) {try{return await pending;}catch(Exception e){store.Log(e);return null;}}
        _=pending.ContinueWith(t=>{if(t.IsCompletedSuccessfully)t.Result.Dispose();},TaskScheduler.Default);
        return null;
    }
    async Task<bool> EnsurePlayer()
    {
        var style=EngineStyle();
        if(player!=null&&playerTextStyle==style) return true;
        if(player!=null)
        {
            SavePlayback();DetachEvents();tick?.Stop();hideTimer?.Stop();Video.MediaPlayer=null;
            var retired=player;var retiredMedia=media;var retiredVlc=vlc;player=null;media=null;vlc=null;
            await nativeTransition.WaitAsync();try{await Task.Run(()=>{retired.Stop();retired.Dispose();retiredMedia?.Dispose();retiredVlc?.Dispose();});}finally{nativeTransition.Release();}
            if(closing)return false;
        }
        playerTextStyle=style;
        var options=EngineOptions();
        StartupTrace.Mark("Player: creating VLC engine");var engine=await TakeWarmEngine(style)??await Task.Run(()=>
        {
#if NOIR_VALIDATE
            File.AppendAllText(Path.Combine(store.DataDirectory,"engine-startup.txt"),"Core initialization started\n");
#endif
            var native=PlayerWarmup.CreateEngine(options);
#if NOIR_VALIDATE
            File.AppendAllText(Path.Combine(store.DataDirectory,"engine-startup.txt"),"VLC engine ready\n");
#endif
            return native;
        });
        StartupTrace.Mark("Player: VLC engine ready");if(closing) {engine.Dispose();return false;}
        vlc=engine;
#if NOIR_VALIDATE
        File.AppendAllText(Path.Combine(store.DataDirectory,"engine-startup.txt"),"Creating native player on UI\n");
#endif
        player=new LibVLCSharp.Shared.MediaPlayer(vlc) { EnableKeyInput=false,EnableMouseInput=false,Volume=store.State.Volume };
#if NOIR_VALIDATE
        File.AppendAllText(Path.Combine(store.DataDirectory,"engine-startup.txt"),"Native player ready\n");
#endif
        Video.MediaPlayer=player;
        tick=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(250) };tick.Tick+=(_,_)=>PlaybackTick();
        hideTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(3) };
        hideTimer.Tick+=(_,_)=>
        {
            if(!player.IsPlaying) { hideTimer.Stop();return; }
            if(player.IsPlaying && (!ended || NextPanel.Visibility!=Visibility.Visible) && !seeking &&
                trackMenu?.IsOpen!=true && !PlayerHeader.IsMouseOver && !PlayerFooter.IsMouseOver) HideControls();
        };
        return true;
    }
    void DetachEvents()
    {
        if(player==null) return;
        if(endedHandler!=null) player.EndReached-=endedHandler;
        if(errorHandler!=null) player.EncounteredError-=errorHandler;
        if(playingHandler!=null) player.Playing-=playingHandler;
    }
    async Task PlayVideo(VideoItem item,bool restart=false)
    {
        if(openingVideo||closing) return;
        if(!File.Exists(item.Path)) { Status.Text="This video is unavailable. Reconnect its drive or scan the library.";return; }
        try
        {
            openingVideo=true;Status.Text="Opening "+item.Label+"…";StartupTrace.Mark("Play requested");
            if(PlayerRoot.Visibility!=Visibility.Visible){libraryScroll=ContentScroll.VerticalOffset;libraryFocus=Keyboard.FocusedElement;libraryFocusName=libraryFocus is FrameworkElement focused?System.Windows.Automation.AutomationProperties.GetName(focused):"";restoreLibraryView=true;}
            if(!await EnsurePlayer()||closing||!IsVisible) return;
            SavePlayback();CancelNext();DetachEvents();
            HidePreview();
            tick?.Stop();serial++;var thisSerial=serial;var native=player!;
            StartupTrace.Mark("Player ensured");await StopPlayer(native);StartupTrace.Mark("Previous stopped");if(closing||serial!=thisSerial)return;media?.Dispose();media=null;
            current=item;ended=false;tracksSelected=false;pictureConfigured=false;appliedCrop=null;
            introHandled=creditsReached=chaptersChecked=false;saveAfterSeek=false;
            creditsPromptDismissed=creditsExpanded=false;endAction=null;
            store.State.HiddenContinue.Remove(item.GroupKey);
            var bookmark=store.GetProgress(item);
            lastPlaybackRevision=bookmark?.UpdatedAt??DateTimeOffset.MinValue;
            var resume=!restart && bookmark is {Completed:false,Position:>0}?bookmark.Position:0;
            store.BeginViewing(item,restart);
            lastPosition=resume;currentDuration=bookmark?.Duration>0?bookmark.Duration:item.Duration;
            pendingTarget=resume>0?Math.Min(resume,currentDuration>0?Math.Max(0,currentDuration-.5):resume):null;
            seekSent=false;seekStarted=DateTime.UtcNow;lastSave=DateTime.UtcNow;
            LibraryRoot.Visibility=Visibility.Collapsed;PlayerRoot.Visibility=Visibility.Visible;
            PlayerOverlay.Visibility=Visibility.Visible;
            Video.Content=PlayerOverlay;
            Video.MediaPlayer=player;
            UpdateLayout();
            if(Window.GetWindow(PlayerOverlay) is { } overlayWindow && overlayWindow!=this)
            {
                overlayWindow.ShowActivated=false;
                overlayWindow.Show();
            }
            ShowControls();
            NowTitle.Text=item.Title;NowEpisode.Text=item.IsMovie?"Movie":item.EpisodeLabel+(item.EpisodeTitle.Length>0?" · "+item.EpisodeTitle:"");
            PauseButton.Content="Pause";SkipIntro.Visibility=Visibility.Collapsed;
            PlayerMessage.Text=resume>0?"Resuming at "+LibraryStore.Time(resume)+"…":"Opening video…";PlayerMessage.Visibility=Visibility.Visible;
            SavedLabel.Text="";Elapsed.Text=LibraryStore.Time(resume);Total.Text=LibraryStore.Time(currentDuration);
            SeekBar.Maximum=Math.Max(1,currentDuration);SeekBar.Value=Math.Min(resume,SeekBar.Maximum);
            endedHandler=(_,_)=>Dispatcher.BeginInvoke(()=> { if(thisSerial==serial && !closing) PlaybackEnded(); });
            errorHandler=(_,_)=>Dispatcher.BeginInvoke(()=> { if(thisSerial==serial && !closing) { PlayerMessage.Text="VLC could not play this file. Return to the library and try another episode.";PlayerMessage.Visibility=Visibility.Visible;SavePlayback();tick?.Stop(); } });
            playingHandler=(_,_)=>Dispatcher.BeginInvoke(()=> { if(thisSerial==serial && !closing) { StartupTrace.Mark("VLC Playing");store.ConsumeQueue(item);if(pendingTarget==null) PlayerMessage.Visibility=Visibility.Collapsed;PauseButton.Content="Pause";ApplyPictureMode(true); } });
            native.EndReached+=endedHandler;native.EncounteredError+=errorHandler;native.Playing+=playingHandler;
            media=new Media(vlc!,new Uri(item.Path));
            media.AddOption($":freetype-rel-fontsize={store.State.SubtitleSize}");media.AddOption($":freetype-color={store.State.SubtitleColor}");media.AddOption($":freetype-background-opacity={(store.State.SubtitleBackground?180:0)}");
            native.Volume=(int)VolumeBar.Value;native.Mute=muted;
            StartupTrace.Mark("Calling Play");if(!native.Play(media)) throw new InvalidOperationException("VLC did not accept the video.");
            StartupTrace.Mark("Play returned");tick!.Start();Focus();
            if(fullscreen) ShowControls();
        }
        catch(Exception e) { store.Log(e);PlayerMessage.Text="Could not open video: "+e.Message;PlayerMessage.Visibility=Visibility.Visible; }
        finally {openingVideo=false;}
    }
    void PlaybackTick()
    {
        if(player==null || current==null || ended || closing) return;
        var nativeState=player.State;
        if(nativeState!=VLCState.Playing && nativeState!=VLCState.Paused) return;
        uint width=0,height=0;
        if(!pictureConfigured && player.Size(0,ref width,ref height) && width>0 && height>0)
        {
            ApplyPictureMode(true);pictureConfigured=true;
        }
        var duration=player.Length/1000d;
        if(duration>0) currentDuration=duration;
        var time=Math.Max(0,player.Time/1000d);
        if(!chaptersChecked && time>.3)
        {
            var chapters=player.FullChapterDescriptions(-1);
            if(chapters.Length>0 || time>4)
            {
                chaptersChecked=true;
                IntroSegment? Find(string pattern)
                {
                    var index=Array.FindIndex(chapters,c=>Regex.IsMatch((c.Name??"").Trim(),pattern,RegexOptions.IgnoreCase));
                    if(index<0) return null;
                    var ch=chapters[index];var start=ch.TimeOffset/1000d;
                    var end=ch.Duration>0?start+ch.Duration/1000d:index+1<chapters.Length?chapters[index+1].TimeOffset/1000d:currentDuration;
                    return end>start?new(){Start=start,End=end}:null;
                }
#if NOIR_VALIDATE
                File.WriteAllText(System.IO.Path.Combine(store.DataDirectory,"chapters.json"),System.Text.Json.JsonSerializer.Serialize(chapters));
#endif
                store.RememberChapters(current,Find(@"^(OP|Intro|Opening|Intro credits|Opening credits)$"),Find(@"^(ED|ED/Outro|Ending|Ending credits|Closing credits|Credits|Outro)$"));
            }
        }
        if(pendingTarget is double target)
        {
            if(!seekSent && player.IsSeekable)
            {
                player.Time=(long)(target*1000);seekSent=true;seekStarted=DateTime.UtcNow;
            }
            if(seekSent && Math.Abs(time-target)<2.5)
            {
                pendingTarget=null;lastPosition=time;PlayerMessage.Visibility=Visibility.Collapsed;StartupTrace.Mark("Resume position reached");
                if(saveAfterSeek){saveAfterSeek=false;SavePlayback();_ = FlushPlayback();}
            }
            else if((DateTime.UtcNow-seekStarted).TotalSeconds>15)
            {
                PlayerMessage.Text="The video hasn't reached your saved position yet. Try seeking again, or return to the library.";
                PlayerMessage.Visibility=Visibility.Visible;
            }
            if(pendingTarget!=null) return;
        }
        lastPosition=time;
        if(!seeking) { SeekBar.Maximum=Math.Max(1,currentDuration);SeekBar.Value=Math.Clamp(time,0,SeekBar.Maximum); }
        Elapsed.Text=LibraryStore.Time(time);Total.Text=LibraryStore.Time(currentDuration);
        PauseButton.Content=player.IsPlaying?"Pause":"Play";
        if(!tracksSelected && time>0.2)
        {
            var descriptions=player.SpuDescription;
            if(descriptions.Length>1 || time>3)
            {
                tracksSelected=true;
                ApplyPreferredTracks();
            }
        }
        var intro=store.GetIntro(current);
        var inIntro=intro is {Valid:true} && time>=intro.Start && time<intro.End-.3;
        SkipIntro.Visibility=inIntro && ControlsVisible?Visibility.Visible:Visibility.Collapsed;
        if(inIntro && !introHandled && store.State.AutoSkipIntro && player.IsPlaying && !seeking)
        {introHandled=true;SeekAbsolute(intro!.End,false,false);}
        if(!creditsReached && player.IsPlaying && !seeking && time>=store.CompletionStart(current,currentDuration) && currentDuration>0)
        {
            creditsReached=true;SavePlayback();_ = FlushPlayback();
            nextItem=store.QueuedNext(current,items)??LibraryStore.Next(current,items,store.State);
            if(nextItem!=null)
            {
                NextTitle.Text=nextItem.EpisodeLabel+(nextItem.EpisodeTitle.Length>0?" · "+nextItem.EpisodeTitle:"");
                NextLabel.Text="UP NEXT";NextPlay.Content="Play now";endAction=null;
                NextCountdown.Text="Watched · Credits are still playing";CreditsChip.Content="Up next · "+nextItem.EpisodeLabel+"  ›";
            }
        }
        if((DateTime.UtcNow-lastSave).TotalSeconds>=2) SavePlayback();
        if(creditsReached && !ended)
        {
            var visible=nextItem!=null && !creditsPromptDismissed && ControlsVisible && time>=store.CompletionStart(current,currentDuration);
            NextPanel.Visibility=visible&&creditsExpanded?Visibility.Visible:Visibility.Collapsed;
            CreditsChip.Visibility=visible&&!creditsExpanded?Visibility.Visible:Visibility.Collapsed;
        }
    }
    async Task FlushPlayback(){try{await store.FlushPendingAsync();}catch(Exception error){store.Log(error);}}
    void SavePlayback()
    {
        if(current==null || player==null || ended || pendingTarget!=null) return;
        // VLC resets its clock on Stop/EndReached; keep the last valid reading.
        if(player.State is VLCState.Playing or VLCState.Paused)
        {
            var time=player.Time/1000d;
            if(time>=0) lastPosition=time;
            var duration=player.Length/1000d;if(duration>0) currentDuration=duration;
        }
        if(lastPosition<.1) return;
        var guard=closing||player.State!=VLCState.Playing;
        if(!store.Record(current,lastPosition,currentDuration,creditsReached,guard?lastPlaybackRevision:null,true)) return;
        lastPlaybackRevision=store.GetProgress(current)?.UpdatedAt??DateTimeOffset.MinValue;lastSave=DateTime.UtcNow;
        SavedLabel.Text=(creditsReached?"Watched · ":"Saved · ")+LibraryStore.Time(lastPosition);
    }
    void SeekAbsolute(double seconds,bool revealControls=true,bool manual=true)
    {
        if(player==null || current==null) return;
        var target=Math.Clamp(seconds,0,currentDuration>0?Math.Max(0,currentDuration-.15):Math.Max(0,seconds));
        if(ended)
        {
            _ = RestartAt(current,target,revealControls,manual);return;
        }
        else
        {
            if(!player.IsSeekable) return;
            pendingTarget=target;seekSent=true;seekStarted=DateTime.UtcNow;player.Time=(long)(target*1000);
        }
        if(manual)
        {
            saveAfterSeek=true;
            lastPlaybackRevision=store.GetProgress(current)?.UpdatedAt??DateTimeOffset.MinValue;
            var opening=store.GetIntro(current);
            if(opening is {Valid:true} && target>=opening.Start && target<opening.End) introHandled=true;
            CancelNext();
        }
        SeekBar.Value=target;Elapsed.Text=LibraryStore.Time(target);if(revealControls) ShowControls();
    }
    async Task RestartAt(VideoItem item,double target,bool reveal,bool manual){await PlayVideo(item,true);if(!closing&&current==item)SeekAbsolute(target,reveal,manual);}
    double SeekOrigin => pendingTarget ?? (player?.State is VLCState.Playing or VLCState.Paused?Math.Max(0,player.Time/1000d):lastPosition);
    void PlaybackEnded()
    {
        if(current==null || player==null || ended) return;
        // Reaching credits records completion without stopping playback. Only the natural end advances automatically.
        ended=true;pendingTarget=null;tick?.Stop();CreditsChip.Visibility=Visibility.Collapsed;
        creditsReached=true;store.Record(current,currentDuration,currentDuration,true,deferSave:true);
        SavedLabel.Text="Watched";PauseButton.Content="Replay";Elapsed.Text=Total.Text;SeekBar.Value=SeekBar.Maximum;
        SkipIntro.Visibility=Visibility.Collapsed;ShowControls();
        if(sleepPlan?.EpisodeEnded()==true) {StopForSleep();return;}
        nextItem=store.QueuedNext(current,items)??LibraryStore.Next(current,items,store.State);
        PlayerMessage.Text="Episode finished";PlayerMessage.Visibility=Visibility.Visible;
        if(nextItem==null) { ShowFinishedOptions(current);return; }
        NextLabel.Text="UP NEXT";NextPlay.Content="Play now";endAction=null;
        NextTitle.Text=nextItem.EpisodeLabel+(nextItem.EpisodeTitle.Length>0?" · "+nextItem.EpisodeTitle:"");
        NextPanel.Visibility=Visibility.Visible;PlayerMessage.Visibility=Visibility.Collapsed;
        if(!store.State.AutoNext) { NextCountdown.Text="Ready when you are.";return; }
        countdown=8;NextCountdown.Text=$"Playing in {countdown} seconds";
        nextTimer ??=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
        nextTimer.Tick-=CountdownTick;nextTimer.Tick+=CountdownTick;nextTimer.Start();
    }
    void CountdownTick(object? sender,EventArgs e)
    {
        countdown--;NextCountdown.Text=$"Playing in {countdown} seconds";
        if(countdown<=0) { nextTimer?.Stop();var item=nextItem;if(item!=null) _ = PlayVideo(item); }
    }
    void CancelNext() { nextTimer?.Stop();NextPanel.Visibility=CreditsChip.Visibility=Visibility.Collapsed; }
    void CreditsChip_Click(object sender,RoutedEventArgs e){creditsExpanded=true;CreditsChip.Visibility=Visibility.Collapsed;NextPanel.Visibility=Visibility.Visible;ShowControls();}
    void CancelNext_Click(object sender,RoutedEventArgs e) { creditsPromptDismissed=true;CancelNext();if(ended) {PlayerMessage.Text="Episode finished. Your place is saved.";PlayerMessage.Visibility=Visibility.Visible;} }
    Action? endAction;
    // Nothing local to play next: offer the useful step instead of a dead end.
    void ShowFinishedOptions(VideoItem finished)
    {
        var up=finished.IsMovie?null:store.UpNextFor(store.EpisodesFor(finished.GroupKey));
        NextPanel.Visibility=Visibility.Visible;PlayerMessage.Visibility=Visibility.Collapsed;
        if(up is {Kind:UpNextKind.NotOnPc} missing)
        {
            var job=ActiveDownload(missing.Item);
            NextLabel.Text="UP NEXT · NOT ON THIS PC";NextTitle.Text=missing.Item.EpisodeLabel+(missing.Item.EpisodeTitle.Length>0&&!store.State.HideSpoilers?" · "+missing.Item.EpisodeTitle:"");
            NextCountdown.Text=job!=null?"Already downloading. It will appear in your library when ready.":"Find a release and download it to this PC.";
            NextPlay.Content=job!=null?"View download":"Download";var item=missing.Item;
            endAction=job!=null?()=>{ReturnToLibrary();Navigate("Downloads");}:()=>FindDownloads(item);
        }
        else if(up is {Kind:UpNextKind.Upcoming} soon)
        {
            NextLabel.Text="YOU'RE CAUGHT UP";NextTitle.Text=soon.Item.EpisodeLabel+" airs "+(soon.AirsAt?.ToLocalTime().ToString("dddd, d MMM")??"soon");
            NextCountdown.Text="It will wait for you on Home under Waiting for new episodes.";NextPlay.Content="Back to library";endAction=ReturnToLibrary;
        }
        else
        {
            NextLabel.Text=finished.IsMovie?"MOVIE FINISHED":"SERIES FINISHED";NextTitle.Text=finished.Title;
            var rated=store.State.PersonalReviews.GetValueOrDefault(finished.Id)?.Rating??0;
            NextCountdown.Text=rated>0?"You rated it "+new string('★',rated)+".":"How was it? Your rating and note stay private on this PC.";
            NextPlay.Content=rated>0?"View diary":"Rate it";endAction=()=>OpenDiary(finished);
        }
    }
    void NextNow_Click(object sender,RoutedEventArgs e) { if(endAction!=null&&ended){endAction();return;}var next=nextItem;if(next!=null) _ = PlayVideo(next); }
    void Pause_Click(object sender,RoutedEventArgs e)=>TogglePause();
    void TogglePause()
    {
        if(player==null || current==null) return;
        if(ended) { _ = PlayVideo(current,true);return; }
        if(player.State is not (VLCState.Playing or VLCState.Paused)) return;
        player.SetPause(player.IsPlaying);SavePlayback();_ = FlushPlayback();ShowControls();
        PauseButton.Content=player.IsPlaying?"Pause":"Play";
    }
    void Back_Click(object sender,RoutedEventArgs e)=>ReturnToLibrary();
    void PlayerEpisodes_Click(object sender,RoutedEventArgs e)
    {
        if(current==null) return;
        var key=current.GroupKey;ReturnToLibrary();detailKey=key;detailSeason=null;Render();ContentScroll.ScrollToTop();
    }
    async void ReturnToLibrary()
    {
        SavePlayback();CancelNext();tick?.Stop();DetachEvents();serial++;
        HidePreview();
        SetSleep(null);
        var native=player;current=null;pendingTarget=null;ended=false;clickTimer?.Stop();HidePlayerHint(null,EventArgs.Empty);
        if(fullscreen) ToggleFullscreen();
        hideTimer?.Stop();if(trackMenu!=null) trackMenu.IsOpen=false;
        PlayerOverlay.Visibility=Visibility.Collapsed;
        PlayerHeader.Visibility=PlayerFooter.Visibility=PlayerMessage.Visibility=SkipIntro.Visibility=Visibility.Collapsed;
        Cursor=Cursors.Arrow;PlayerOverlay.Cursor=Cursors.Arrow;
        if(Window.GetWindow(PlayerOverlay) is { } overlayWindow && overlayWindow!=this) overlayWindow.Hide();
        Video.Content=null;
        Video.MediaPlayer=null;
        seeking=false;
        PlayerRoot.Visibility=Visibility.Collapsed;LibraryRoot.Visibility=Visibility.Visible;Title="Noir";Render();
        if(restoreLibraryView){ContentScroll.ScrollToVerticalOffset(libraryScroll);restoreLibraryView=false;if(libraryFocusName.Length>0)ViewElements(ContentPanel).FirstOrDefault(e=>System.Windows.Automation.AutomationProperties.GetName(e)==libraryFocusName)?.Focus();}
        try{if(native!=null)await StopPlayer(native);await store.FlushPendingAsync();}catch(Exception error){store.Log(error);Status.Text="Couldn't finish saving playback. Check library storage.";}
    }
    void Seek_Begin(object sender,MouseButtonEventArgs e) { seeking=true; }
    void Seek_End(object sender,MouseButtonEventArgs e) { if(seeking) { seeking=false;SeekAbsolute(SeekBar.Value); } }
    void Seek_DragCompleted(object sender,DragCompletedEventArgs e) { seeking=false;SeekAbsolute(SeekBar.Value); }
    void Rewind_Click(object sender,RoutedEventArgs e)=>SeekAbsolute(SeekOrigin-10);
    void Forward_Click(object sender,RoutedEventArgs e)=>SeekAbsolute(SeekOrigin+10);
    void SkipIntro_Click(object sender,RoutedEventArgs e) { e.Handled=true;SkipKnownIntro(); }
    void SkipKnownIntro() { if(current!=null && store.GetIntro(current) is {Valid:true} intro && lastPosition>=intro.Start && lastPosition<intro.End) SeekAbsolute(intro.End); }
    void Volume_Changed(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(!ready) return;
        if(player!=null) player.Volume=(int)e.NewValue;
        store.State.Volume=(int)e.NewValue;
    }
    void Audio_Click(object sender,RoutedEventArgs e)
        =>OpenTrackMenu(BuildAudioMenu(),(Button)sender);
    ContextMenu BuildAudioMenu()
    {
        var menu=new ContextMenu();
        if(player==null)return menu;
        foreach(var track in player.AudioTrackDescription)
        {
            if(track.Id<0) continue;
            var entry=new MenuItem { Header=track.Name,IsCheckable=true,IsChecked=track.Id==player.AudioTrack };
            int id=track.Id;entry.Click+=(_,_)=>SelectTrack(false,id,track.Name);menu.Items.Add(entry);
        }
        if(menu.Items.Count==0) menu.Items.Add(new MenuItem { Header="Tracks are still loading",IsEnabled=false });
        return menu;
    }
    void Subtitles_Click(object sender,RoutedEventArgs e)
        =>OpenTrackMenu(BuildSubtitleMenu(),(Button)sender);
    ContextMenu BuildSubtitleMenu()
    {
        var menu=new ContextMenu();
        if(player==null)return menu;
        var off=new MenuItem { Header="Off",IsCheckable=true,IsChecked=player.Spu<0 };off.Click+=(_,_)=>SelectTrack(true,-1,"Off");menu.Items.Add(off);
        foreach(var track in player.SpuDescription)
        {
            if(track.Id<0) continue;
            var entry=new MenuItem { Header=track.Name,IsCheckable=true,IsChecked=track.Id==player.Spu };
            int id=track.Id;entry.Click+=(_,_)=>SelectTrack(true,id,track.Name);menu.Items.Add(entry);
        }
        menu.Items.Add(new Separator());
        var delay=new MenuItem{Header="Timing · "+(current!=null?Preference(current).SubtitleDelay.ToString("+0.0;-0.0;0.0"):"0.0")+"s"};
        foreach(var option in new[]{("Earlier by 0.5 seconds",-.5),("Later by 0.5 seconds",.5),("Reset timing",0d)})
        {var choice=option;var row=new MenuItem{Header=choice.Item1};row.Click+=(_,_)=>AdjustSubtitleDelay(choice.Item2,choice.Item2==0);delay.Items.Add(row);}menu.Items.Add(delay);
        var add=new MenuItem { Header="Load subtitle file…" };
        add.Click+=(_,_)=>
        {
            var dialog=new Microsoft.Win32.OpenFileDialog { Title="Choose subtitles",Filter="Subtitles|*.srt;*.ass;*.ssa;*.vtt;*.sub|All files|*.*" };
            if(dialog.ShowDialog(this)==true) player.AddSlave(MediaSlaveType.Subtitle,new Uri(dialog.FileName).AbsoluteUri,true);
        };
        menu.Items.Add(add);
        if(current!=null){var item=current;var fetch=new MenuItem{Header="Download English / Arabic subtitles…"};fetch.Click+=(_,_)=>DownloadSubtitleFiles(item);menu.Items.Add(fetch);var sync=new MenuItem{Header="Auto-sync saved subtitles…"};sync.Click+=(_,_)=>DownloadSubtitleFiles(item,true);menu.Items.Add(sync);}
        return menu;
    }
    void Languages_Click(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();
        void Section(string title,ContextMenu source){var section=new MenuItem{Header=title};while(source.Items.Count>0){var item=source.Items[0];source.Items.RemoveAt(0);section.Items.Add(item);}menu.Items.Add(section);}
        Section("Audio",BuildAudioMenu());Section("Subtitles & timing",BuildSubtitleMenu());OpenTrackMenu(menu,(Button)sender);
    }
    void PlayerMore_Click(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();void Add(string label,Action action){var row=new MenuItem{Header=label};row.Click+=(_,_)=>action();menu.Items.Add(row);}
        Add(store.State.FillVideo?"Picture · Fill (switch to Fit)":"Picture · Fit (switch to Fill)",()=>PictureMode_Click(sender,e));
        Add(SleepButton.Content?.ToString()??"Sleep timer",()=>Sleep_Click(sender,e));
        Add("Edit intro & credits times",()=>Intro_Click(sender,e));
        menu.Items.Add(new Separator());menu.Items.Add(new MenuItem{Header="Space: pause · Arrows: seek / volume · F: fullscreen",IsEnabled=false});OpenTrackMenu(menu,(Button)sender);
    }
    void OpenTrackMenu(ContextMenu menu,Button target)
    {
        trackMenu=menu;
        menu.Closed+=(_,_)=> { if(ReferenceEquals(trackMenu,menu)) trackMenu=null;if(current!=null && PlayerRoot.Visibility==Visibility.Visible) ShowControls(); };
        menu.PlacementTarget=target;menu.IsOpen=true;ShowControls();
    }
    void Video_Click(object sender,MouseButtonEventArgs e)
    {
        if(PlayerHeader.IsMouseOver || PlayerFooter.IsMouseOver || NextPanel.IsMouseOver || CreditsChip.IsMouseOver || SkipIntro.IsMouseOver) return;
        HandleVideoClick(e.ClickCount);e.Handled=true;
    }
    void HandleVideoClick(int clicks)
    {
        if(clicks==2){clickTimer?.Stop();ToggleFullscreen(false);HideControls();pointerSuppressedUntil=DateTime.UtcNow.AddMilliseconds(450);}
        else{
            clickTimer?.Stop();
            if(ControlsVisible){HideControls();pointerSuppressedUntil=DateTime.UtcNow.AddMilliseconds(450);}
            else{clickTimer??=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(GetDoubleClickTime())};clickTimer.Tick-=CommitVideoClick;clickTimer.Tick+=CommitVideoClick;clickTimer.Start();}
        }
    }
    void ToggleControls() { if(ControlsVisible) HideControls();else ShowControls(); }
    void CommitVideoClick(object? sender,EventArgs e){clickTimer?.Stop();if(current!=null&&!closing)ToggleControls();}
    void HideControls()
    {
        HidePreview();
        hideTimer?.Stop();
        lastPointer=Mouse.GetPosition(PlayerOverlay);
        PlayerHeader.Visibility=PlayerFooter.Visibility=Visibility.Collapsed;
        SkipIntro.Visibility=CreditsChip.Visibility=Visibility.Collapsed;
        if(!ended) NextPanel.Visibility=Visibility.Collapsed;
        PlayerOverlay.Cursor=Cursors.None;
    }
    void Fullscreen_Click(object sender,RoutedEventArgs e)=>ToggleFullscreen();
    void ToggleFullscreen(bool revealControls=true)
    {
        fullscreen=!fullscreen;
        if(fullscreen) { oldWindowState=WindowState;oldWindowBounds=WindowState==WindowState.Normal?new Rect(Left,Top,Width,Height):RestoreBounds;oldWindowStyle=WindowStyle;oldResizeMode=ResizeMode;WindowState=WindowState.Normal;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;WindowState=WindowState.Maximized; }
        else { WindowState=WindowState.Normal;WindowStyle=oldWindowStyle;ResizeMode=oldResizeMode;if(!oldWindowBounds.IsEmpty){Left=oldWindowBounds.Left;Top=oldWindowBounds.Top;Width=oldWindowBounds.Width;Height=oldWindowBounds.Height;}WindowState=oldWindowState;WindowAppearance.Apply(this);hideTimer?.Stop(); }
        if(revealControls)ShowControls();
    }
    void ShowControls()
    {
        PlayerHeader.Visibility=PlayerFooter.Visibility=Visibility.Visible;
        PlayerOverlay.Cursor=Cursors.Arrow;
        hideTimer?.Stop();if(current!=null && PlayerRoot.Visibility==Visibility.Visible) hideTimer?.Start();
    }
    void Player_MouseMove(object sender,MouseEventArgs e)
    {
        var pointer=e.GetPosition(PlayerOverlay);
        if(DateTime.UtcNow<pointerSuppressedUntil){lastPointer=pointer;return;}
        if(lastPointer is Point previous && (pointer-previous).Length<4) return;
        lastPointer=pointer;
        ShowControls();
    }
    void Video_SizeChanged(object sender,SizeChangedEventArgs e) => ApplyPictureMode();
    void ApplyPictureMode(bool force=false)
    {
        if(!ready || player==null || Video.ActualWidth<1 || Video.ActualHeight<1) return;
        var crop=store.State.FillVideo?$"{(int)Math.Round(Video.ActualWidth)}:{(int)Math.Round(Video.ActualHeight)}":"";
        PictureModeButton.Content=store.State.FillVideo?"Fill":"Fit";
        if(!force && appliedCrop==crop) return;
        player.AspectRatio="";
        player.CropGeometry=crop;
        appliedCrop=crop;
    }
    void PictureMode_Click(object sender,RoutedEventArgs e)
    {
        store.State.FillVideo=!store.State.FillVideo;store.Save();ApplyPictureMode(true);ShowControls();
    }
    void Window_KeyDown(object sender,KeyEventArgs e)
    {
        if(PlayerRoot.Visibility!=Visibility.Visible) return;
        if(e.OriginalSource is TextBox) return;
        switch(e.Key)
        {
            case Key.Space: TogglePause();break;
            case Key.Left: SeekAbsolute(SeekOrigin-10,false);ShowPlayerHint("−10 seconds");break;
            case Key.Right: SeekAbsolute(SeekOrigin+10,false);ShowPlayerHint("+10 seconds");break;
            case Key.Up: VolumeBar.Value=Math.Min(100,VolumeBar.Value+5);ShowPlayerHint("Volume · "+(int)VolumeBar.Value+"%");break;
            case Key.Down: VolumeBar.Value=Math.Max(0,VolumeBar.Value-5);ShowPlayerHint("Volume · "+(int)VolumeBar.Value+"%");break;
            case Key.F: ToggleFullscreen();break;
            case Key.Escape: if(fullscreen) ToggleFullscreen();else ReturnToLibrary();break;
            case Key.M: muted=!muted;if(player!=null) player.Mute=muted;break;
            case Key.I: SkipKnownIntro();break;
            case Key.OemOpenBrackets: AdjustSubtitleDelay(-.5);break;
            case Key.OemCloseBrackets: AdjustSubtitleDelay(.5);break;
            default: return;
        }
        e.Handled=true;
    }
}
