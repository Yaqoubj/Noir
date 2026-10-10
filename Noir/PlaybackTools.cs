using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;

namespace Noir;
public partial class MainWindow
{
    SleepPlan? sleepPlan;
    DispatcherTimer? sleepTimer;
    PlaybackPreference Preference(VideoItem item)
    {
        if(!store.State.PlaybackPreferences.TryGetValue(item.GroupKey,out var value)) store.State.PlaybackPreferences[item.GroupKey]=value=new();
        return value;
    }
    void SelectTrack(bool subtitle,int id,string name)
    {
        if(player==null||current==null) return;
        if(!(subtitle?player.SetSpu(id):player.SetAudioTrack(id))) return;
        var preference=Preference(current);if(subtitle) preference.SubtitleTrack=id<0?"Off":name;else preference.AudioTrack=name;store.Save();
    }
    void ApplyPreferredTracks()
    {
        if(player==null||current==null) return;
        var preference=Preference(current);
        bool Select(TrackDescription[] tracks,string name,Action<int> select)
        {
            if(name.Length==0) return false;var match=tracks.FirstOrDefault(t=>t.Id>=0&&t.Name==name);
            if(string.IsNullOrEmpty(match.Name))
            {
                var token=System.Text.RegularExpressions.Regex.Match(name,@"\[([a-z]{2,3})\]",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if(token.Success) match=tracks.FirstOrDefault(t=>t.Id>=0&&t.Name.Contains(token.Value,StringComparison.OrdinalIgnoreCase));
            }
            if(string.IsNullOrEmpty(match.Name)) return false;select(match.Id);return true;
        }
        Select(player.AudioTrackDescription,preference.AudioTrack,id=>player.SetAudioTrack(id));
        if(preference.SubtitleTrack=="Off" || preference.SubtitleTrack.Length==0&&store.State.Subtitles=="Off") player.SetSpu(-1);
        else if(!Select(player.SpuDescription,preference.SubtitleTrack,id=>player.SetSpu(id))&&store.State.Subtitles is "English" or "Arabic")
        {
            var arabic=store.State.Subtitles=="Arabic";
            var english=player.SpuDescription.FirstOrDefault(x=>x.Id>=0&&(x.Name.Contains(arabic?"Arabic":"English",StringComparison.OrdinalIgnoreCase)||x.Name.Contains(arabic?"[ara]":"[eng]",StringComparison.OrdinalIgnoreCase)||System.Text.RegularExpressions.Regex.IsMatch(x.Name,arabic?@"(?i)\.ar[.\]]":@"(?i)\.en[.\]]")));
            if(!string.IsNullOrEmpty(english.Name)) player.SetSpu(english.Id);
        }
        player.SetSpuDelay((long)(preference.SubtitleDelay*1000000));
    }
    void AdjustSubtitleDelay(double change,bool reset=false)
    {
        if(current==null||player==null) return;
        var preference=Preference(current);var value=reset?0:Math.Clamp(preference.SubtitleDelay+change,-30,30);
        if(player.SetSpuDelay((long)(value*1000000))) {preference.SubtitleDelay=value;store.Save();SavedLabel.Text=$"Subtitles · {value:+0.0;-0.0;0.0}s";}ShowControls();
    }
    void RenderSubtitleAppearance(Panel panel)
    {
        var content=new StackPanel();content.Children.Add(Body("For plain text subtitles. Styled anime subtitles and image subtitles keep their own appearance. Changes apply to the next video you open.",12));
        content.Children.Add(Text("Size",12));content.Children.Add(Chips(["Small","Normal","Large"],store.State.SubtitleSize==18?"Small":store.State.SubtitleSize==12?"Large":"Normal",label=>{store.State.SubtitleSize=label=="Small"?18:label=="Large"?12:16;store.Save();Render();}));
        content.Children.Add(Text("Colour",12));content.Children.Add(Chips(["White","Yellow"],store.State.SubtitleColor==0xFFFF00?"Yellow":"White",label=>{store.State.SubtitleColor=label=="Yellow"?0xFFFF00:0xFFFFFF;store.Save();Render();}));
        var background=new CheckBox{Content="Dark background behind plain text subtitles",IsChecked=store.State.SubtitleBackground};background.Click+=(_,_)=>{store.State.SubtitleBackground=background.IsChecked==true;store.Save();};content.Children.Add(background);
        content.Children.Add(Body("Audio, subtitle tracks and subtitle timing are remembered for each show or movie.",12));
        panel.Children.Add(new Expander{Header="Subtitle appearance & preferences",Content=content,Foreground=Brush("#C7C2BC"),Margin=new(0,10,0,15)});
    }
    void Sleep_Click(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();void Add(string label,Action action){var row=new MenuItem{Header=label};row.Click+=(_,_)=>action();menu.Items.Add(row);}
        Add("Off",()=>SetSleep(null));Add("After this episode / movie",()=>SetSleep(new(){EpisodesLeft=1}));
        Add("After 2 episodes",()=>SetSleep(new(){EpisodesLeft=2}));Add("After 3 episodes",()=>SetSleep(new(){EpisodesLeft=3}));menu.Items.Add(new Separator());
        foreach(var minutes in new[]{30,60,90}) {var value=minutes;Add(value+" minutes",()=>SetSleep(new(){Deadline=DateTimeOffset.UtcNow.AddMinutes(value)}));}OpenTrackMenu(menu,(Button)sender);
    }
    void SetSleep(SleepPlan? plan)
    {
        sleepPlan=plan;sleepTimer?.Stop();if(plan==null) {SleepButton.Content="Sleep timer";return;}
        sleepTimer??=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};sleepTimer.Tick-=SleepTick;sleepTimer.Tick+=SleepTick;sleepTimer.Start();SleepTick(null,EventArgs.Empty);
    }
    void SleepTick(object? sender,EventArgs e)
    {
        if(sleepPlan==null) return;if(sleepPlan.TimeExpired(DateTimeOffset.UtcNow)) {StopForSleep();return;}
        SleepButton.Content=sleepPlan.Deadline is { } deadline?"Sleep · "+Math.Max(1,(int)Math.Ceiling((deadline-DateTimeOffset.UtcNow).TotalMinutes))+"m":"Sleep · "+sleepPlan.EpisodesLeft+" left";
    }
    void StopForSleep()
    {
        if(player?.IsPlaying==true) player.SetPause(true);SavePlayback();CancelNext();SetSleep(null);ShowControls();PlayerMessage.Text="Sleep timer finished · Your place is saved";PlayerMessage.Visibility=Visibility.Visible;
    }
}
