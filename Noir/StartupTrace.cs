using System.Diagnostics;
using System.IO;
using System.Windows.Threading;

namespace Noir;
// Opt-in timing for diagnosing slow launches: set NOIR_STARTUP_TRACE to a file path.
internal static class StartupTrace
{
    static readonly string? path=Environment.GetEnvironmentVariable("NOIR_STARTUP_TRACE");
    static readonly Stopwatch clock=Stopwatch.StartNew();
    static readonly object gate=new();
    internal static bool Enabled=>path!=null;
    internal static void Mark(string step)
    {
        if(path==null)return;
        lock(gate)try{File.AppendAllText(path,$"{clock.ElapsedMilliseconds,7} ms  {step}\n");}catch{}
    }
    // Reports UI-thread stalls longer than 120 ms during the first minute.
    internal static void WatchDispatcher(Dispatcher dispatcher)
    {
        if(path==null)return;
        new Thread(()=>
        {
            while(clock.Elapsed.TotalSeconds<60)
            {
                var sent=clock.ElapsedMilliseconds;
                var op=dispatcher.BeginInvoke(DispatcherPriority.Input,()=>{});
                op.Wait();
                var waited=clock.ElapsedMilliseconds-sent;
                if(waited>120)Mark($"UI thread blocked ~{waited} ms");
                Thread.Sleep(40);
            }
        }){IsBackground=true}.Start();
    }
}
