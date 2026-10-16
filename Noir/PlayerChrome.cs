using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Noir;
// Icon artwork for the player controls. Each control keeps a text name for tooltips and screen readers.
public partial class MainWindow
{
    const string PlayIcon="M3,1 L3,23 L21,12 Z",PauseIcon="M3,1 L9,1 L9,23 L3,23 Z M15,1 L21,1 L21,23 L15,23 Z",ReplayIcon="M12,3 A9,9 0 1 1 3.3,9.6 M3,2 L3,10 L11,10";
    string playState="";
    void SetPlayState(string state)
    {
        if(state==playState) return;playState=state;
        var path=new Path{Data=Geometry.Parse(state=="Pause"?PauseIcon:state=="Replay"?ReplayIcon:PlayIcon),Width=18,Height=18,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        if(state=="Replay"){path.Stroke=Brush("#16120C");path.StrokeThickness=2.6;path.StrokeStartLineCap=path.StrokeEndLineCap=PenLineCap.Round;}else{path.Fill=Brush("#16120C");if(state!="Pause")path.Margin=new(3,0,0,0);}
        PauseButton.Content=path;PauseButton.ToolTip=state+" (Space)";System.Windows.Automation.AutomationProperties.SetName(PauseButton,state);
    }
    // Remaining time is what people usually want while watching; click the time to switch.
    bool showRemaining=true;
    void Total_Click(object sender,System.Windows.Input.MouseButtonEventArgs e){showRemaining=!showRemaining;UpdateTotalLabel(lastPosition);e.Handled=true;}
    void UpdateTotalLabel(double position)=>Total.Text=showRemaining&&currentDuration>0?"−"+LibraryStore.Time(Math.Max(0,currentDuration-position)):LibraryStore.Time(currentDuration);
}
