using System.IO;
using System.Windows;

namespace Noir;
public partial class App : Application
{
    void Window_Loaded(object sender,RoutedEventArgs e){if(sender is Window window){WindowAppearance.Apply(window);WindowAppearance.Fit(window);}}
    void Scroll_Wheel(object sender,System.Windows.Input.MouseWheelEventArgs e)=>SmoothScrolling.Wheel(sender,e);
    Mutex? single;
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    protected override void OnStartup(StartupEventArgs e)
    {
        StartupTrace.Mark("App startup");base.OnStartup(e);
        if(e.Args.Contains("--build-vlc-cache")) { Shutdown(PlayerWarmup.BuildPluginCache()?0:1);return; }
        DispatcherUnhandledException += (_, args) =>
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Noir");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir,"errors.log"), args.Exception + "\n");
            MessageBox.Show("Noir encountered an error. Your saved positions are kept.\n\n" + args.Exception.Message, "Noir");
            args.Handled = true;
        };
        string? dataDirectory = null;
        string? playPath = null;
        string? initialPage = null;
        bool validate = false;
#if NOIR_VALIDATE
        validate = e.Args.Contains("--validate");
#endif
        for (int i=0;i<e.Args.Length;i++)
        {
            if(e.Args[i]=="--data-dir" && i+1<e.Args.Length) dataDirectory=e.Args[++i];
            else if(e.Args[i]=="--play" && i+1<e.Args.Length) playPath=e.Args[++i];
            else if(e.Args[i]=="--page" && i+1<e.Args.Length) initialPage=e.Args[++i];
        }
        if(validate && dataDirectory==null) { Shutdown(2);return; }
        var instanceSuffix=dataDirectory==null?"":Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory).ToUpperInvariant())))[..16];
        single = new Mutex(true, "Local\\NoirPlayer_" + Environment.UserName + instanceSuffix, out var first);
        // Launching again while Noir runs in the background brings the existing window forward.
        var showSignal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\NoirPlayerShow_" + Environment.UserName + instanceSuffix);
        if (!first) { if(!e.Args.Contains("--minimized")) {AllowSetForegroundWindow(-1);showSignal.Set();} Shutdown(); return; }
        var window = new MainWindow(dataDirectory,playPath,validate,initialPage);
        if(validate) { window.ShowActivated=false;window.Title="Noir validation"; }
        MainWindow = window;
        if(e.Args.Contains("--minimized")) { window.ShowActivated=false;window.WindowState=WindowState.Minimized;window.StartHidden=true; }
        new Thread(()=>{while(showSignal.WaitOne())Dispatcher.BeginInvoke(()=>window.ShowFromTray());}){IsBackground=true,Name="Noir activation"}.Start();
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e) { single?.Dispose(); base.OnExit(e); }
}
