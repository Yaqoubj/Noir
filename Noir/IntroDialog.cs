using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Noir;
public partial class MainWindow
{
    void Intro_Click(object sender,RoutedEventArgs e)
    {
        if(current==null || player==null) return;
        var resume=player.IsPlaying;if(resume) player.SetPause(true);SavePlayback();
        var intro=store.GetIntro(current);
        var outro=store.GetOutro(current);
        var dialog=new Window { Title="Intro & credits · "+current.Title,Width=540,Height=610,MinHeight=500,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#0D0D0F"),Foreground=Brush("#F4F1EB") };
        var content=new StackPanel { Margin=new(28) };
        content.Children.Add(Text("Openings & endings.",24,weight:FontWeights.SemiBold));
        content.Children.Add(Body("Use seconds or mm:ss. Openings can be skipped; credits mark the video watched while it keeps playing.",13));
        content.Children.Add(Text("Intro starts · optional",12));var start=new TextBox { Text=intro!=null?LibraryStore.Time(intro.Start):"",Margin=new(0,6,0,12) };content.Children.Add(start);
        content.Children.Add(Text("Intro ends",12));var end=new TextBox { Text=intro!=null?LibraryStore.Time(intro.End):"",Margin=new(0,6,0,0) };content.Children.Add(end);
        content.Children.Add(Heading("Credits start","Optional. Leave blank to use the detected ending or the near-end fallback."));
        var credits=new TextBox{Text=outro!=null?LibraryStore.Time(outro.Start):"",Margin=new(0,0,0,8)};content.Children.Add(credits);
        content.Children.Add(ActionButton("Use current position for credits",()=>credits.Text=LibraryStore.Time(lastPosition)));
        var all=new CheckBox { Content="Use these times for this whole season",IsEnabled=!current.IsMovie };content.Children.Add(all);
        var error=Text("",12,"#FFAC9C");content.Children.Add(error);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,Margin=new(0,6,0,0) };
        var item=current;
        var save=ActionButton("Save times",()=>
        {
            var haveIntro=start.Text.Trim().Length>0 || end.Text.Trim().Length>0;
            double a=0,b=0,c=0;
            if(haveIntro && (!ParseTime(start.Text,out a)||!ParseTime(end.Text,out b)||!new IntroSegment { Start=a,End=b }.Valid || (currentDuration>0 && b>=currentDuration))) { error.Text="Enter a valid opening range, or leave both opening fields blank.";return; }
            var haveCredits=credits.Text.Trim().Length>0;
            if(haveCredits && (!ParseTime(credits.Text,out c) || c<currentDuration*.5 || c>=currentDuration || currentDuration-c>600)) {error.Text="Credits must start in the final 10 minutes and after halfway through the video.";return;}
            var introKey=all.IsChecked==true?item.GroupKey+":season:"+item.Season:item.Path;
            var outroKey=all.IsChecked==true?item.GroupKey+":season:"+item.Season:item.Id;
            if(haveIntro) store.State.Intros[introKey]=new(){Start=a,End=b};else store.State.Intros.Remove(introKey);
            if(haveCredits) store.State.Outros[outroKey]=new(){Start=c,End=currentDuration};else store.State.Outros.Remove(outroKey);
            if(all.IsChecked==true) store.State.Intros.Remove(item.Path);
            if(all.IsChecked==true) store.State.Outros.Remove(item.Id);
            store.Save();dialog.Close();
        },true);buttons.Children.Add(save);
        var cancel=ActionButton("Cancel",()=>dialog.Close());cancel.Margin=new(10,0,0,0);buttons.Children.Add(cancel);content.Children.Add(buttons);
        dialog.Content=new ScrollViewer{Content=content};dialog.ShowDialog();if(resume&&!ended) player.SetPause(false);
    }
    static bool ParseTime(string text,out double value)
    {
        value=0;var parts=text.Trim().Split(':');
        if(parts.Length>3||parts.Length==0) return false;
        foreach(var p in parts) { if(!double.TryParse(p,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var v)||v<0) return false;value=value*60+v; }
        return double.IsFinite(value);
    }
}
