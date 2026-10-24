using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Noir;
// One lifetime for each element's clocks; transforms compose with card hover and button press.
static class Motion
{
    public static bool Enabled=>SystemParameters.ClientAreaAnimation;
    sealed class Lifetime
    {
        public readonly HashSet<(Animatable Target,DependencyProperty Property)> Clocks=[];
        public DispatcherTimer? Counter;
        public bool Entered,Button;
    }
    static readonly ConditionalWeakTable<FrameworkElement,Lifetime> lifetimes=new();
    static Lifetime Track(FrameworkElement element)=>lifetimes.GetValue(element,e=>
    {
        var life=new Lifetime();e.Unloaded+=(_,_)=>{e.BeginAnimation(UIElement.OpacityProperty,null);foreach(var (target,property) in life.Clocks)target.BeginAnimation(property,null);life.Clocks.Clear();life.Counter?.Stop();life.Counter=null;};return life;
    });
    static DoubleAnimationBase Tween(double from,double to,int ms,int delayMs)
    {
        if(delayMs==0)return new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(ms)){FillBehavior=FillBehavior.Stop,EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
        var animation=new DoubleAnimationUsingKeyFrames{FillBehavior=FillBehavior.Stop};animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from,KeyTime.FromTimeSpan(TimeSpan.Zero)));animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs))));animation.KeyFrames.Add(new SplineDoubleKeyFrame(to,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs+ms)),new KeySpline(.2,.8,.2,1)));return animation;
    }
    public static void To(FrameworkElement owner,Animatable target,DependencyProperty property,double value,int ms=160,int delayMs=0,double? from=null)
    {
        var start=from??(double)target.GetValue(property);target.BeginAnimation(property,null);target.SetValue(property,value);
        if(!Enabled)return;
        Track(owner).Clocks.Add((target,property));target.BeginAnimation(property,Tween(start,value,ms,delayMs));
    }
    public static void Opacity(FrameworkElement element,double value,int ms=160,int delayMs=0,double? from=null)
    {
        var start=from??element.Opacity;element.BeginAnimation(UIElement.OpacityProperty,null);element.Opacity=value;if(!Enabled)return;
        // UIElement is not Animatable, so its opacity clock has a separate unload hook.
        Track(element);element.BeginAnimation(UIElement.OpacityProperty,Tween(start,value,ms,delayMs));
    }
    static T FindTransform<T>(FrameworkElement element) where T:Transform,new()
    {
        if(element.RenderTransform is T found)return found;
        var group=element.RenderTransform as TransformGroup;
        if(group==null){group=new TransformGroup();if(element.RenderTransform!=null&&element.RenderTransform!=Transform.Identity)group.Children.Add(element.RenderTransform);element.RenderTransform=group;}
        var existing=group.Children.OfType<T>().FirstOrDefault();if(existing!=null)return existing;var transform=new T();group.Children.Add(transform);return transform;
    }
    public static void FadeIn(FrameworkElement element,int delayMs=0)=>Opacity(element,1,180,delayMs,0);
    public static void RiseIn(FrameworkElement element,int delayMs=0)
    {
        FadeIn(element,delayMs);var shift=FindTransform<TranslateTransform>(element);To(element,shift,TranslateTransform.YProperty,0,180,delayMs,8);
    }
    public static void EnterOnce(FrameworkElement element,int delayMs=0)
    {
        if(!Enabled)return;var life=Track(element);if(life.Entered)return;
        void Enter(){if(life.Entered)return;life.Entered=true;RiseIn(element,delayMs);}
        if(element.IsLoaded)Enter();else element.Loaded+=(_,_)=>Enter();
    }
    public static void Scale(FrameworkElement element,double value,int ms=140)
    {
        if(!Enabled)value=1;element.RenderTransformOrigin=new(.5,.5);var scale=FindTransform<ScaleTransform>(element);To(element,scale,ScaleTransform.ScaleXProperty,value,ms);To(element,scale,ScaleTransform.ScaleYProperty,value,ms);
    }
    public static void Pop(FrameworkElement element){if(!Enabled)return;var scale=FindTransform<ScaleTransform>(element);element.RenderTransformOrigin=new(.5,.5);To(element,scale,ScaleTransform.ScaleXProperty,1,240,0,.94);To(element,scale,ScaleTransform.ScaleYProperty,1,240,0,.94);}
    public static void Pulse(FrameworkElement element){if(Enabled)Opacity(element,1,420,0,.6);}
    public static void CountUp(TextBlock element,int from,int to)
    {
        var life=Track(element);life.Counter?.Stop();element.Text=to.ToString(Loc.Culture);if(!Enabled||from==to)return;
        var watch=System.Diagnostics.Stopwatch.StartNew();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(32)};life.Counter=timer;
        timer.Tick+=(_,_)=>{var t=Math.Min(1,watch.Elapsed.TotalMilliseconds/500);element.Text=((int)Math.Round(from+(to-from)*(1-Math.Pow(1-t,3)))).ToString(Loc.Culture);if(t>=1){timer.Stop();life.Counter=null;}};timer.Start();
    }
    public static void ButtonFeedback(Button button)
    {
        var life=Track(button);if(life.Button)return;life.Button=true;
        button.PreviewMouseLeftButtonDown+=(_,_)=>Scale(button,.97,70);button.PreviewMouseLeftButtonUp+=(_,_)=>Scale(button,1,130);button.LostMouseCapture+=(_,_)=>Scale(button,1,130);
        button.MouseLeave+=(_,_)=>Scale(button,1,130);button.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Space or Key.Enter)Scale(button,.97,70);};button.PreviewKeyUp+=(_,e)=>{if(e.Key is Key.Space or Key.Enter)Scale(button,1,130);};button.LostKeyboardFocus+=(_,_)=>Scale(button,1,130);
    }
    public static void Drift(FrameworkElement element)
    {
        if(!Enabled)return;element.RenderTransformOrigin=new(.7,.4);var scale=FindTransform<ScaleTransform>(element);var shift=FindTransform<TranslateTransform>(element);var life=Track(element);
        void Start(){if(!Enabled)return;foreach(var property in new[]{ScaleTransform.ScaleXProperty,ScaleTransform.ScaleYProperty}){life.Clocks.Add((scale,property));scale.BeginAnimation(property,new DoubleAnimation(1.01,1.065,TimeSpan.FromSeconds(24)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase()});}life.Clocks.Add((shift,TranslateTransform.XProperty));shift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,-12,TimeSpan.FromSeconds(24)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase()});}
        if(element.IsLoaded)Start();element.Loaded+=(_,_)=>Start();
    }
    public static void Shimmer(FrameworkElement element,LinearGradientBrush sheen)
    {
        if(!Enabled)return;var shift=new TranslateTransform();sheen.RelativeTransform=shift;var life=Track(element);
        void Start(){if(!Enabled)return;life.Clocks.Add((shift,TranslateTransform.XProperty));shift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-1,1,TimeSpan.FromMilliseconds(1900)){RepeatBehavior=RepeatBehavior.Forever});}if(element.IsLoaded)Start();element.Loaded+=(_,_)=>Start();
    }
}
