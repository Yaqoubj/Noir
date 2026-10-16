using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace Noir;
internal static class WindowAppearance
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd,int attribute,ref int value,int size);
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint hwnd,uint flags);
    [DllImport("user32.dll")] static extern nint MonitorFromRect(ref NativeRect rect,uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public uint Flags;}
    // The visible work area (excluding the taskbar) of the window's monitor, in WPF units.
    static Rect? WorkArea(Window window,Rect? near=null)
    {
        var hwnd=new WindowInteropHelper(window).Handle;if(hwnd==0)return null;
        var source=PresentationSource.FromVisual(window)?.CompositionTarget;
        var fromDevice=source?.TransformFromDevice??Matrix.Identity;var toDevice=source?.TransformToDevice??Matrix.Identity;
        nint monitorHandle;
        if(near is Rect r)
        {
            var a=toDevice.Transform(r.TopLeft);var b=toDevice.Transform(r.BottomRight);
            var native=new NativeRect{Left=(int)a.X,Top=(int)a.Y,Right=(int)b.X,Bottom=(int)b.Y};monitorHandle=MonitorFromRect(ref native,2);
        }
        else monitorHandle=MonitorFromWindow(hwnd,2);
        var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(monitorHandle,ref monitor))return null;
        var top=fromDevice.Transform(new Point(monitor.Work.Left,monitor.Work.Top));var bottom=fromDevice.Transform(new Point(monitor.Work.Right,monitor.Work.Bottom));
        return new Rect(top,bottom);
    }
    // Keeps the whole window, and especially its title bar, on the visible screen.
    internal static void Fit(Window window)
    {
        if(window.WindowState!=WindowState.Normal||window.WindowStyle==WindowStyle.None)return;
        var width=double.IsNaN(window.Width)?window.ActualWidth:window.Width;var height=double.IsNaN(window.Height)?window.ActualHeight:window.Height;
        if(WorkArea(window,new Rect(window.Left,window.Top,Math.Max(1,width),Math.Max(1,height))) is not Rect work)return;
        window.MinWidth=Math.Min(window.MinWidth,work.Width);window.MinHeight=Math.Min(window.MinHeight,work.Height);
        width=Math.Min(width,work.Width);height=Math.Min(height,work.Height);
        window.Width=width;window.Height=height;
        window.Left=Math.Clamp(window.Left,work.Left,Math.Max(work.Left,work.Right-width));
        window.Top=Math.Clamp(window.Top,work.Top,Math.Max(work.Top,work.Bottom-height));
    }
    internal static bool Apply(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==0)return false;
        var dark=Set(handle,20,1);var caption=Set(handle,35,0x00121010);var text=Set(handle,36,0x00EBF1F4);return dark==0&&caption==0&&text==0;
    }
    static int Set(nint handle,int attribute,int value)=>DwmSetWindowAttribute(handle,attribute,ref value,sizeof(int));

    sealed class Placement {public double Left{get;set;}public double Top{get;set;}public double Width{get;set;}public double Height{get;set;}public bool Maximized{get;set;}}
    static string PlacementPath(string dataDirectory)=>Path.Combine(dataDirectory,"window.json");
    // Called from SourceInitialized, before the window is first drawn: restore the last size and
    // position, or choose a comfortable size for this screen on first launch.
    internal static void Restore(Window window,string dataDirectory)
    {
        Placement? saved=null;
        try{if(File.Exists(PlacementPath(dataDirectory)))saved=JsonSerializer.Deserialize<Placement>(File.ReadAllText(PlacementPath(dataDirectory)));}catch{}
        if(saved is {Width:>=200,Height:>=200} && double.IsFinite(saved.Left) && double.IsFinite(saved.Top) && WorkArea(window,new Rect(saved.Left,saved.Top,saved.Width,saved.Height)) is Rect target && target.IntersectsWith(new Rect(saved.Left,saved.Top,saved.Width,saved.Height)))
        {
            window.Left=saved.Left;window.Top=saved.Top;window.Width=saved.Width;window.Height=saved.Height;Fit(window);
            if(saved.Maximized&&window.WindowState==WindowState.Normal)window.WindowState=WindowState.Maximized;
            return;
        }
        if(WorkArea(window) is not Rect work)return;
        // Small or heavily scaled screens get the whole work area; larger ones a centred window.
        if(work.Width<1280||work.Height<760){window.Width=Math.Min(window.Width,work.Width);window.Height=Math.Min(window.Height,work.Height);window.Left=work.Left;window.Top=work.Top;if(window.WindowState==WindowState.Normal)window.WindowState=WindowState.Maximized;return;}
        window.Width=Math.Min(1440,Math.Round(work.Width*.86));window.Height=Math.Min(940,Math.Round(work.Height*.88));
        window.Left=work.Left+(work.Width-window.Width)/2;window.Top=work.Top+(work.Height-window.Height)/2;
    }
    internal static void Remember(string dataDirectory,Rect bounds,bool maximized)
    {
        try
        {
            if(bounds.IsEmpty||bounds.Width<200||bounds.Height<200)return;
            File.WriteAllText(PlacementPath(dataDirectory),JsonSerializer.Serialize(new Placement{Left=bounds.Left,Top=bounds.Top,Width=bounds.Width,Height=bounds.Height,Maximized=maximized}));
        }
        catch{}
    }
}
