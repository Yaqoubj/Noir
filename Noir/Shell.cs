using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Noir;
// The window frame around every page: floating top bar, page margins, entrance motion and the
// status notice that appears briefly at the bottom.
public partial class MainWindow
{
    double sidePad=56;
    bool cinematic;
    // Pages with a hero start under the top bar; the others leave room for it and show a title.
    void ApplyPageLayout()
    {
        LibraryHeader.Visibility=cinematic?Visibility.Collapsed:Visibility.Visible;
        LibraryHeader.Margin=new Thickness(sidePad,TopBar.Height+30,sidePad,10);
        ContentPanel.Margin=new Thickness(sidePad,cinematic?0:6,sidePad,64);
        UpdateTopBar();
    }
    // Lets an element span the full window width from inside the padded content column.
    FrameworkElement Bleed(FrameworkElement element,double bottom=0){element.Margin=new Thickness(-sidePad,0,-sidePad,bottom);return element;}
    void Content_ScrollChanged(object sender,ScrollChangedEventArgs e)=>UpdateTopBar();
    void UpdateTopBar()
    {
        if(TopBarFill==null) return;
        var fill=cinematic?Math.Clamp(ContentScroll.VerticalOffset/220,0,1):Math.Clamp(ContentScroll.VerticalOffset/40,0,1);
        TopBarFill.Opacity=fill;
    }
    void Logo_Click(object sender,System.Windows.Input.MouseButtonEventArgs e)=>Navigate("Home");
    void PageEntrance()
    {
        var shift=new TranslateTransform(0,14);ContentPanel.RenderTransform=shift;
        var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
        ContentPanel.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(260)){EasingFunction=ease});
        shift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(14,0,TimeSpan.FromMilliseconds(320)){EasingFunction=ease});
    }
    DispatcherTimer? toastTimer;
    void WatchStatusText()=>DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock)).AddValueChanged(Status,(_,_)=>ShowToast());
    // Every Status.Text update appears as a short notice; longer messages stay a little longer.
    void ShowToast()
    {
        if(string.IsNullOrWhiteSpace(Status.Text)){StatusToast.BeginAnimation(OpacityProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(200)));return;}
        StatusToast.BeginAnimation(OpacityProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(180)));
        toastTimer??=new DispatcherTimer();toastTimer.Stop();toastTimer.Interval=TimeSpan.FromSeconds(Math.Clamp(Status.Text.Length/14.0,3,9));
        toastTimer.Tick-=HideToast;toastTimer.Tick+=HideToast;toastTimer.Start();
    }
    void HideToast(object? sender,EventArgs e){toastTimer?.Stop();StatusToast.BeginAnimation(OpacityProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(400)));}
    static new FrameworkElement Icon(string geometry,double size=20,string color="#E9E4DC")=>new System.Windows.Shapes.Path{Data=Geometry.Parse(geometry),Stroke=Brush(color),StrokeThickness=1.7,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Width=size,Height=size,Stretch=Stretch.Uniform};
    const string DownloadGlyph="M12,1 L12,16 M6,10 L12,16 L18,10 M2,17 L2,23 L22,23 L22,17",ScanGlyph="M3,8 A10,10 0 0 1 20,4 M20,1 L20,7 L14,7 M21,16 A10,10 0 0 1 4,20 M4,23 L4,17 L10,17",SettingsGlyph="M1,5 L23,5 M1,12 L23,12 M1,19 L23,19 M7,2 L7,8 M17,9 L17,15 M10,16 L10,22";
    FrameworkElement DownloadIcon(int active)
    {
        var grid=new Grid{Width=26,Height=26};grid.Children.Add(Icon(DownloadGlyph,19));
        if(active>0)grid.Children.Add(new Border{Background=Brush("#E4C084"),CornerRadius=new(8),MinWidth=16,Height=16,Padding=new(4,0,4,0),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,-4,-6,0),Child=new TextBlock{Text=active.ToString(),FontSize=10,FontWeight=FontWeights.Bold,Foreground=Brush("#16120C"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}});
        return grid;
    }
}
