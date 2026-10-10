using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace Noir;
internal static class WindowAppearance
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd,int attribute,ref int value,int size);
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint hwnd,uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public uint Flags;}
    internal static void Fit(Window window)
    {
        if(window.WindowState!=WindowState.Normal||window.WindowStyle==WindowStyle.None)return;
        var hwnd=new WindowInteropHelper(window).Handle;var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromWindow(hwnd,2),ref monitor))return;
        var transform=PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice??Matrix.Identity;
        var top=transform.Transform(new Point(monitor.Work.Left,monitor.Work.Top));var bottom=transform.Transform(new Point(monitor.Work.Right,monitor.Work.Bottom));
        var width=bottom.X-top.X;var height=bottom.Y-top.Y;
        window.MinWidth=Math.Min(window.MinWidth,width);window.MinHeight=Math.Min(window.MinHeight,height);
        if(window.ActualWidth>width)window.Width=width;if(window.ActualHeight>height)window.Height=height;
        window.Left=Math.Clamp(window.Left,top.X,Math.Max(top.X,bottom.X-window.ActualWidth));window.Top=Math.Clamp(window.Top,top.Y,Math.Max(top.Y,bottom.Y-window.ActualHeight));
    }
    internal static bool Apply(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==0)return false;
        var dark=Set(handle,20,1);var caption=Set(handle,35,0x00121010);var text=Set(handle,36,0x00EBF1F4);return dark==0&&caption==0&&text==0;
    }
    static int Set(nint handle,int attribute,int value)=>DwmSetWindowAttribute(handle,attribute,ref value,sizeof(int));
}
