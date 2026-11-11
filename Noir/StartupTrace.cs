using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Noir;

// Opt-in timing for diagnosing slow launches: set NOIR_STARTUP_TRACE to a file path.
internal static class StartupTrace
{
    static readonly string? path = Environment.GetEnvironmentVariable("NOIR_STARTUP_TRACE");
    static readonly Stopwatch? clock = path == null ? null : Stopwatch.StartNew();
    static readonly object gate = new();
    static readonly StringBuilder? pending = path == null ? null : new();
    static readonly long processAge = ProcessAge();

    static long ProcessAge()
    {
        if (path == null)
            return 0;
        long age;
        try
        {
            using var process = Process.GetCurrentProcess();
            age =
                (long)(DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds
                - clock!.ElapsedMilliseconds;
        }
        catch
        {
            age = 0;
        }
        pending!.AppendLine("      0 ms  Process start");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
        return age;
    }

    internal static bool Enabled => path != null;

    internal static void Mark(string step)
    {
        if (path == null)
            return;
        lock (gate)
            pending!.AppendLine($"{processAge + clock!.ElapsedMilliseconds, 7} ms  {step}");
    }

    internal static void Flush()
    {
        if (path == null)
            return;
        lock (gate)
            try
            {
                if (pending!.Length == 0)
                    return;
                File.AppendAllText(path, pending.ToString());
                pending.Clear();
            }
            catch { }
    }

    internal static void PageReady(Window window)
    {
        if (path == null)
            return;
        FirstRendering(window, "First saved page rendering");
        window.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Mark("Saved page input ready"));
    }

    internal static void FirstRendering(Window window, string step)
    {
        if (path == null)
            return;
        EventHandler? frame = null;
        EventHandler? closed = null;
        frame = (_, _) =>
        {
            if (!window.IsLoaded || !window.IsVisible || PresentationSource.FromVisual(window) == null)
                return;
            CompositionTarget.Rendering -= frame;
            window.Closed -= closed;
            Mark(step);
        };
        closed = (_, _) =>
        {
            CompositionTarget.Rendering -= frame;
            window.Closed -= closed;
        };
        CompositionTarget.Rendering += frame;
        window.Closed += closed;
    }

    // Reports UI-thread stalls longer than 120 ms during the first minute.
    internal static void WatchDispatcher(Dispatcher dispatcher)
    {
        if (path == null)
            return;
        new Thread(() =>
        {
            while (clock!.Elapsed.TotalSeconds < 60)
            {
                var sent = clock.ElapsedMilliseconds;
                try
                {
                    var op = dispatcher.BeginInvoke(DispatcherPriority.Input, () => { });
                    op.Wait();
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                var waited = clock.ElapsedMilliseconds - sent;
                if (waited > 120)
                    Mark($"UI thread blocked ~{waited} ms");
                Flush();
                Thread.Sleep(40);
            }
        })
        {
            IsBackground = true,
        }.Start();
    }
}
