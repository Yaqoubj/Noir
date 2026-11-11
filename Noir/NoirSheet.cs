using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Noir;
public enum SheetKind { Side, Dialog }
// One look for every panel, drawer and dialog: an in-window layer over the library and the player,
// sliding in from the end edge (left in Arabic) or centred for confirmations. Esc, the ✕ and a click on the shade close it.
public sealed class NoirSheet
{
    internal readonly Grid Overlay;internal readonly Border Panel;internal readonly StackPanel Body;internal readonly StackPanel FooterButtons;internal readonly Border Footer;internal readonly TextBlock TitleText;internal readonly TextBlock SubtitleText;internal readonly Button CloseButton;internal readonly ScrollViewer Scroll;
    readonly MainWindow owner;readonly IInputElement? returnFocus;readonly TaskCompletionSource<bool> result=new();
    public string Title=>TitleText.Text;public SheetKind Kind {get;}
    public bool IsOpen {get;private set;}=true;public bool DismissOnShade {get;set;}=true;
    public Task<bool> Done=>result.Task;public event Action<bool>? Closed;
    // Return false to keep the sheet open (e.g. a running job that must be cancelled first).
    public Func<bool>? CanClose {get;set;}
    internal NoirSheet(MainWindow window,Grid host,string title,string? subtitle,SheetKind kind,double width)
    {
        owner=window;Kind=kind;returnFocus=Keyboard.FocusedElement;
        Overlay=new Grid{Background=MainWindow.Brush("#99000000"),FlowDirection=FlowDirection.LeftToRight};System.Windows.Controls.Panel.SetZIndex(Overlay,95+MainWindow.OpenSheets.Count);Overlay.Tag="NoirSheet";
        var flow=Loc.Arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;var maxWidth=Math.Max(320,window.ActualWidth-48);
        Panel=new Border{Width=Math.Min(width,maxWidth),Background=MainWindow.Brush("#141416"),BorderBrush=MainWindow.Brush("#2EFFFFFF"),BorderThickness=new(1),CornerRadius=new(16),FlowDirection=flow,Effect=new DropShadowEffect{BlurRadius=40,ShadowDepth=8,Opacity=.55,Color=Colors.Black},Tag="NoirSheetPanel"};
        if(kind==SheetKind.Side){Panel.HorizontalAlignment=Loc.Arabic?HorizontalAlignment.Left:HorizontalAlignment.Right;Panel.Margin=new(12);}
        else{Panel.HorizontalAlignment=HorizontalAlignment.Center;Panel.VerticalAlignment=VerticalAlignment.Center;Panel.MaxHeight=Math.Max(260,window.ActualHeight-80);}
        var layout=new Grid();layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});Panel.Child=layout;
        var header=new DockPanel{Margin=new(22,16,12,10),LastChildFill=true};
        CloseButton=new Button{Content="[[close]]",Width=32,Height=32,Padding=new(0),FontSize=13,Background=MainWindow.Brush("Transparent"),Foreground=MainWindow.Brush("#C7C2BC"),BorderThickness=new(0),VerticalAlignment=VerticalAlignment.Top,ToolTip=Loc.T("Close")+" · Esc",Cursor=Cursors.Hand};System.Windows.Automation.AutomationProperties.SetName(CloseButton,Loc.T("Close"));CloseButton.Click+=(_,_)=>Close(false);DockPanel.SetDock(CloseButton,Dock.Right);header.Children.Add(CloseButton);
        var titles=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,4,8,0)};TitleText=MainWindow.Text(title,16,"#F4F1EB",FontWeights.SemiBold);TitleText.TextWrapping=TextWrapping.Wrap;titles.Children.Add(TitleText);
        SubtitleText=MainWindow.Text(subtitle??"",12,"#9F9A93");SubtitleText.TextWrapping=TextWrapping.Wrap;SubtitleText.Margin=new(0,4,0,0);SubtitleText.Visibility=string.IsNullOrEmpty(subtitle)?Visibility.Collapsed:Visibility.Visible;titles.Children.Add(SubtitleText);header.Children.Add(titles);layout.Children.Add(header);
        Body=new StackPanel{Margin=new(22,4,22,20)};Scroll=new ScrollViewer{Content=Body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=false};Grid.SetRow(Scroll,1);layout.Children.Add(Scroll);
        FooterButtons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};Footer=new Border{BorderBrush=MainWindow.Brush("#1FFFFFFF"),BorderThickness=new(0,1,0,0),Padding=new(22,12,22,14),Child=FooterButtons,Visibility=Visibility.Collapsed};Grid.SetRow(Footer,2);layout.Children.Add(Footer);
        Overlay.Children.Add(Panel);
        Overlay.MouseDown+=(_,e)=>{if(ReferenceEquals(e.OriginalSource,Overlay)&&DismissOnShade)Close(false);};
        Overlay.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close(false);e.Handled=true;}};
        KeyboardNavigation.SetTabNavigation(Overlay,KeyboardNavigationMode.Cycle);KeyboardNavigation.SetControlTabNavigation(Overlay,KeyboardNavigationMode.Cycle);
        host.Children.Add(Overlay);MainWindow.OpenSheets.Add(this);
        if(Motion.Enabled)
        {
            if(kind==SheetKind.Side){var slide=new TranslateTransform(Loc.Arabic?-48:48,0);Panel.RenderTransform=slide;slide.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(Loc.Arabic?-48:48,0,TimeSpan.FromMilliseconds(220)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});}
            else{var scale=new ScaleTransform(.97,.97);Panel.RenderTransformOrigin=new(.5,.5);Panel.RenderTransform=scale;var grow=new DoubleAnimation(.97,1,TimeSpan.FromMilliseconds(160)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};scale.BeginAnimation(ScaleTransform.ScaleXProperty,grow);scale.BeginAnimation(ScaleTransform.ScaleYProperty,grow);}
            Motion.FadeIn(Panel);Motion.FadeIn(Overlay);
        }
        Panel.Loaded+=(_,_)=>FocusFirst();
    }
    public void SetTitle(string title,string? subtitle=null){TitleText.Text=title;if(subtitle!=null){SubtitleText.Text=subtitle;SubtitleText.Visibility=subtitle.Length==0?Visibility.Collapsed:Visibility.Visible;}}
    // Footer actions sit at the end edge; the primary one is gold. The action decides whether the sheet closes.
    public Button Action(string label,Action action,bool primary=false)
    {
        var b=owner.SheetButton(label,action,primary);b.Margin=FooterButtons.Children.Count==0?new(0):new(8,0,0,0);FooterButtons.Children.Add(b);Footer.Visibility=Visibility.Visible;return b;
    }
    public void Add(UIElement element)=>Body.Children.Add(element);
    public void Clear()=>Body.Children.Clear();
    public void FocusFirst()
    {
        if(!IsOpen)return;var first=MainWindow.SheetDescendants(Scroll).OfType<Control>().FirstOrDefault(c=>c.Focusable&&c.IsEnabled&&c.IsVisible&&c is not ScrollViewer)??(Control?)FooterButtons.Children.OfType<Button>().LastOrDefault()??CloseButton;first.Focus();
    }
    public void Close(bool ok=true)
    {
        if(!IsOpen)return;if(!ok&&CanClose!=null&&!CanClose())return;IsOpen=false;MainWindow.OpenSheets.Remove(this);
        void Remove(){if(Overlay.Parent is Panel parent)parent.Children.Remove(Overlay);}
        if(Motion.Enabled){Overlay.IsHitTestVisible=false;var fade=new DoubleAnimation(Overlay.Opacity,0,TimeSpan.FromMilliseconds(120));fade.Completed+=(_,_)=>Remove();Overlay.BeginAnimation(UIElement.OpacityProperty,fade);}else Remove();
        if(returnFocus is UIElement back&&back.IsVisible)back.Focus();
        result.TrySetResult(ok);Closed?.Invoke(ok);
    }
}
public partial class MainWindow
{
    internal static readonly List<NoirSheet> OpenSheets=[];
    // Opens a sheet on the window's root layer, so it also works over the player.
    internal NoirSheet OpenSheet(string title,string? subtitle=null,SheetKind kind=SheetKind.Side,double width=480)
    {
        // During playback the video is a native surface (airspace): only VLC's overlay window draws above it.
        CloseHoverCard();var host=PlayerRoot.Visibility==Visibility.Visible?PlayerOverlay:(Grid)LibraryRoot.Parent;return new NoirSheet(this,host,title,subtitle,kind,width);
    }
    // Validators and callers find an open sheet by its title (they used to find windows by Title).
    internal static NoirSheet? FindSheet(string titleStart)=>OpenSheets.LastOrDefault(s=>s.Title.StartsWith(titleStart,StringComparison.Ordinal));
    internal static void CloseAllSheets(){foreach(var s in OpenSheets.ToList())s.Close(false);}
    internal Button SheetButton(string label,Action action,bool primary)
    {
        var b=ActionButton(label,action,primary);b.Padding=Density.ButtonPadding;b.MinWidth=84;return b;
    }
    // A labelled section inside a sheet body: small caps-style heading plus optional hint.
    internal StackPanel SheetSection(string title,string? hint=null)
    {
        var s=new StackPanel{Margin=new(0,14,0,4)};var t=Text(title,12.5,"#E4C084",FontWeights.SemiBold);s.Children.Add(t);if(!string.IsNullOrEmpty(hint)){var h=Text(hint,12,"#9F9A93");h.TextWrapping=TextWrapping.Wrap;h.Margin=new(0,3,0,0);s.Children.Add(h);}return s;
    }
    internal static IEnumerable<DependencyObject> SheetDescendants(DependencyObject root)
    {
        var count=VisualTreeHelper.GetChildrenCount(root);for(var i=0;i<count;i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var d in SheetDescendants(child))yield return d;}
    }
}
