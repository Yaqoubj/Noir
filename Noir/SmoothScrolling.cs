using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace Noir;
internal static class SmoothScrolling
{
    static readonly ConditionalWeakTable<ScrollViewer,Motion> motions=new();
    internal static void Wheel(object sender,MouseWheelEventArgs e)
    {
        if(e.Handled||sender is not ScrollViewer viewer)return;
        var horizontal=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
        // The outer preview event runs first: only the nearest usable scroll surface handles it.
        DependencyObject? node=e.OriginalSource as DependencyObject;
        while(node!=null){if(node is ScrollViewer candidate&&(horizontal?candidate.ScrollableWidth>0:candidate.ScrollableHeight>0)){if(candidate!=viewer)return;break;}node=node is Visual||node is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);}
        if(horizontal?viewer.ScrollableWidth<=0:viewer.ScrollableHeight<=0)return;
        if(SystemParameters.WheelScrollLines==0)return;
        var pixels=SystemParameters.WheelScrollLines<0?(horizontal?viewer.ViewportWidth:viewer.ViewportHeight)*.85:SystemParameters.WheelScrollLines*24;
        if(motions.GetValue(viewer,v=>new Motion(v)).Move(-e.Delta/120d*pixels,horizontal))e.Handled=true;
    }
    internal static void Stop(ScrollViewer viewer){if(motions.TryGetValue(viewer,out var motion))motion.Cancel();}
    sealed class Motion
    {
        readonly ScrollViewer viewer;readonly DispatcherTimer timer;
        double target,last;bool horizontal,applying;
        double Offset=>horizontal?viewer.HorizontalOffset:viewer.VerticalOffset;
        public Motion(ScrollViewer viewer){this.viewer=viewer;timer=new DispatcherTimer(DispatcherPriority.Render,viewer.Dispatcher){Interval=TimeSpan.FromMilliseconds(16)};timer.Tick+=(_,_)=>Tick();viewer.Unloaded+=(_,_)=>Cancel();viewer.ScrollChanged+=(_,_)=>{if(!applying&&Math.Abs(Offset-last)>1)Cancel();};}
        public bool Move(double delta,bool axis)
        {
            if(!timer.IsEnabled||horizontal!=axis){Cancel();horizontal=axis;target=Offset;}
            var next=Math.Clamp(target+delta,0,horizontal?viewer.ScrollableWidth:viewer.ScrollableHeight);
            if(Math.Abs(next-target)<.1)return false;target=next;last=Offset;
            if(!SystemParameters.ClientAreaAnimation){Set(target);return true;}timer.Start();return true;
        }
        void Set(double value){applying=true;last=value;if(horizontal)viewer.ScrollToHorizontalOffset(value);else viewer.ScrollToVerticalOffset(value);viewer.UpdateLayout();last=Offset;applying=false;}
        void Tick(){var distance=target-Offset;if(Math.Abs(distance)<.8){Set(target);Cancel();}else Set(Offset+distance*.32);}
        public void Cancel()=>timer.Stop();
    }
}
