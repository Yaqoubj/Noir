using System.IO;
using LibVLCSharp.Shared;

namespace Noir;
// VLC probes every plugin DLL unless plugins.dat exists. On a cold disk (first run after
// install, update or reboot) that probe took ~13 s, so the package ships a prebuilt cache
// and the engine is created in the background before the first video is opened.
internal static class PlayerWarmup
{
    internal static string PluginDirectory=>Path.Combine(AppContext.BaseDirectory,"libvlc",Environment.Is64BitProcess?"win-x64":"win-x86","plugins");
    internal static bool HasPluginCache=>File.Exists(Path.Combine(PluginDirectory,"plugins.dat"));
    internal static bool BuildPluginCache()
    {
        try
        {
            Core.Initialize();
            using(new LibVLC("--quiet","--reset-plugins-cache")) {}
            return HasPluginCache;
        }
        catch { return false; }
    }
    // The largest plugins a typical local MKV/MP4 needs. Reading them early moves most of the
    // cold-disk and antivirus cost off the first click; VLC reuses the already-loaded modules.
    // Runs sequentially at low priority and nothing waits for it.
    static readonly string[] CommonPlugins=["codec/libavcodec","codec/libd3d11va","demux/libmkv","demux/libmp4","codec/liblibass","codec/libsubsdec",
        "text_renderer/libfreetype","video_output/libdirect3d11","d3d11/libdirect3d11_filters","audio_output/libmmdevice","audio_output/libwasapi",
        "packetizer/libpacketizer_h264","packetizer/libpacketizer_hevc","access/libfilesystem","video_chroma/libswscale","codec/libdav1d","codec/libaom"];
    internal static void PreloadCommonPlugins()
    {
        var thread=new Thread(()=>
        {
            foreach(var plugin in CommonPlugins)
            {
                var path=Path.Combine(PluginDirectory,plugin.Replace('/',Path.DirectorySeparatorChar)+"_plugin.dll");
                if(File.Exists(path)) System.Runtime.InteropServices.NativeLibrary.TryLoad(path,out _);
            }
            StartupTrace.Mark("Common VLC plugins preloaded");
        }){IsBackground=true,Priority=ThreadPriority.BelowNormal,Name="Noir VLC preload"};
        thread.Start();
    }
    internal static LibVLC CreateEngine(string[] options)
    {
        Core.Initialize();
        // A missing cache (developer builds) is rebuilt once when the folder is writable.
        return new LibVLC(HasPluginCache?options:[..options,"--reset-plugins-cache"]);
    }
}
