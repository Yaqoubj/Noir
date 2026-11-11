using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using LibVLCSharp.WPF;

namespace Noir;
// The player controls live in a template (MainWindow.xaml, PlayerRoot) and are built on first use,
// so a launch that never plays a video skips creating them. Every name below builds them on access.
public partial class MainWindow
{
    ContentControl? playerSurface;
    bool PlayerSurfaceBuilt=>playerSurface!=null;
    VideoView? video;Grid? playerOverlay,playerHeader,nextMedia,nextRingHost;TextBlock? nowTitle,nowEpisode,playerMessage,playerHint,nextLabel,nextTitle,nextCountdown,stillWatchingTitle,stillWatchingBody,elapsed,total,savedLabel;
    Button? sleepButton,skipIntro,creditsChip,nextRingPlay,nextPlay,nextCancel,stillWatchingContinue,stillWatchingBack,pauseButton,pictureModeButton,boostButton,speedButton;
    Border? nextPanel,nextStill,stillWatchingPanel;Path? nextRing,volumeIcon;Button? playerBackButton,playerEpisodesButton;StackPanel? playerFooter,playerVolume;Slider? seekBar,volumeBar;Canvas? chapterTicks;
    void EnsurePlayerSurface()
    {
        if(playerSurface!=null)return;
        StartupTrace.Mark("Player surface build started");
        var host=new ContentControl{Template=(ControlTemplate)PlayerRoot.Resources["PlayerSurface"],Focusable=false,IsTabStop=false};playerSurface=host;
        PlayerRoot.Children.Add(host);host.ApplyTemplate();
        T Part<T>(string name)where T:class=>(T)host.Template.FindName(name,host);
        video=Part<VideoView>("Video");playerOverlay=Part<Grid>("PlayerOverlay");playerHeader=Part<Grid>("PlayerHeader");nextMedia=Part<Grid>("NextMedia");nextRingHost=Part<Grid>("NextRingHost");
        nowTitle=Part<TextBlock>("NowTitle");nowEpisode=Part<TextBlock>("NowEpisode");playerMessage=Part<TextBlock>("PlayerMessage");playerHint=Part<TextBlock>("PlayerHint");nextLabel=Part<TextBlock>("NextLabel");nextTitle=Part<TextBlock>("NextTitle");nextCountdown=Part<TextBlock>("NextCountdown");
        stillWatchingTitle=Part<TextBlock>("StillWatchingTitle");stillWatchingBody=Part<TextBlock>("StillWatchingBody");elapsed=Part<TextBlock>("Elapsed");total=Part<TextBlock>("Total");savedLabel=Part<TextBlock>("SavedLabel");
        sleepButton=Part<Button>("SleepButton");skipIntro=Part<Button>("SkipIntro");creditsChip=Part<Button>("CreditsChip");nextRingPlay=Part<Button>("NextRingPlay");nextPlay=Part<Button>("NextPlay");nextCancel=Part<Button>("NextCancel");
        stillWatchingContinue=Part<Button>("StillWatchingContinue");stillWatchingBack=Part<Button>("StillWatchingBack");pauseButton=Part<Button>("PauseButton");pictureModeButton=Part<Button>("PictureModeButton");boostButton=Part<Button>("BoostButton");speedButton=Part<Button>("SpeedButton");
        nextPanel=Part<Border>("NextPanel");nextStill=Part<Border>("NextStill");stillWatchingPanel=Part<Border>("StillWatchingPanel");nextRing=Part<Path>("NextRing");volumeIcon=Part<Path>("VolumeIcon");playerBackButton=Part<Button>("PlayerBackButton");playerEpisodesButton=Part<Button>("PlayerEpisodesButton");playerFooter=Part<StackPanel>("PlayerFooter");playerVolume=Part<StackPanel>("PlayerVolume");
        seekBar=Part<Slider>("SeekBar");volumeBar=Part<Slider>("VolumeBar");chapterTicks=Part<Canvas>("ChapterTicks");
        // Same start state the constructor used to set on the always-built controls.
        playerOverlay.Visibility=Visibility.Collapsed;video.Content=null;
        var wasReady=ready;ready=false;volumeBar.Value=store.State.Volume;ready=wasReady;
        narrowVolume=ActualWidth>0&&ActualWidth<1000;volumeBar.Visibility=narrowVolume?Visibility.Collapsed:Visibility.Visible;// U80: the speaker stays at narrow widths
        StartupTrace.Mark("Player surface build ended");
    }
    internal VideoView Video{get{EnsurePlayerSurface();return video!;}}
    internal Grid PlayerOverlay{get{EnsurePlayerSurface();return playerOverlay!;}}
    internal Grid PlayerHeader{get{EnsurePlayerSurface();return playerHeader!;}}
    internal Grid NextMedia{get{EnsurePlayerSurface();return nextMedia!;}}
    internal Grid NextRingHost{get{EnsurePlayerSurface();return nextRingHost!;}}
    internal TextBlock NowTitle{get{EnsurePlayerSurface();return nowTitle!;}}
    internal TextBlock NowEpisode{get{EnsurePlayerSurface();return nowEpisode!;}}
    internal TextBlock PlayerMessage{get{EnsurePlayerSurface();return playerMessage!;}}
    internal TextBlock PlayerHint{get{EnsurePlayerSurface();return playerHint!;}}
    internal TextBlock NextLabel{get{EnsurePlayerSurface();return nextLabel!;}}
    internal TextBlock NextTitle{get{EnsurePlayerSurface();return nextTitle!;}}
    internal TextBlock NextCountdown{get{EnsurePlayerSurface();return nextCountdown!;}}
    internal TextBlock StillWatchingTitle{get{EnsurePlayerSurface();return stillWatchingTitle!;}}
    internal TextBlock StillWatchingBody{get{EnsurePlayerSurface();return stillWatchingBody!;}}
    internal TextBlock Elapsed{get{EnsurePlayerSurface();return elapsed!;}}
    internal TextBlock Total{get{EnsurePlayerSurface();return total!;}}
    internal TextBlock SavedLabel{get{EnsurePlayerSurface();return savedLabel!;}}
    internal Button SleepButton{get{EnsurePlayerSurface();return sleepButton!;}}
    internal Button SkipIntro{get{EnsurePlayerSurface();return skipIntro!;}}
    internal Button CreditsChip{get{EnsurePlayerSurface();return creditsChip!;}}
    internal Button NextRingPlay{get{EnsurePlayerSurface();return nextRingPlay!;}}
    internal Button NextPlay{get{EnsurePlayerSurface();return nextPlay!;}}
    internal Button NextCancel{get{EnsurePlayerSurface();return nextCancel!;}}
    internal Button StillWatchingContinue{get{EnsurePlayerSurface();return stillWatchingContinue!;}}
    internal Button StillWatchingBack{get{EnsurePlayerSurface();return stillWatchingBack!;}}
    internal Button PauseButton{get{EnsurePlayerSurface();return pauseButton!;}}
    internal Button PictureModeButton{get{EnsurePlayerSurface();return pictureModeButton!;}}
    internal Button BoostButton{get{EnsurePlayerSurface();return boostButton!;}}
    internal Path VolumeIcon{get{EnsurePlayerSurface();return volumeIcon!;}}
    internal Button PlayerBackButton{get{EnsurePlayerSurface();return playerBackButton!;}}
    internal Button PlayerEpisodesButton{get{EnsurePlayerSurface();return playerEpisodesButton!;}}
    internal Button SpeedButton{get{EnsurePlayerSurface();return speedButton!;}}
    internal Border NextPanel{get{EnsurePlayerSurface();return nextPanel!;}}
    internal Border NextStill{get{EnsurePlayerSurface();return nextStill!;}}
    internal Border StillWatchingPanel{get{EnsurePlayerSurface();return stillWatchingPanel!;}}
    internal Path NextRing{get{EnsurePlayerSurface();return nextRing!;}}
    internal StackPanel PlayerFooter{get{EnsurePlayerSurface();return playerFooter!;}}
    internal StackPanel PlayerVolume{get{EnsurePlayerSurface();return playerVolume!;}}
    internal Slider SeekBar{get{EnsurePlayerSurface();return seekBar!;}}
    internal Slider VolumeBar{get{EnsurePlayerSurface();return volumeBar!;}}
    internal Canvas ChapterTicks{get{EnsurePlayerSurface();return chapterTicks!;}}
}
