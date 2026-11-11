using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Noir;

public sealed class CommunitySync : IDisposable
{
    readonly LibraryStore store;
    readonly Dispatcher dispatcher;
    readonly Action changed;
    readonly CancellationTokenSource lifetime = new();
    readonly DispatcherTimer timer;
    readonly CommunityBackoff backoff;
    public bool Paused { get; set; }
    public CommunityBackoff Backoff => backoff;

    /// <summary>F14: the daily title-ID lookup (set by the window: the Cinemeta metadata refresh).</summary>
    public Func<Task>? RefreshIds { get; set; }

    /// <summary>Clock seam: tests drive <see cref="CommunityQueue.Pass"/> with their own.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
    Window? host;
    WebView2? web;
    StremioLibraryWriter? stremio;
    StremioVideoCache? stremioVideos;

    /// <summary>The one Stremio account writer of this process (key: the import's stored key, read only).</summary>
    public StremioLibraryWriter Stremio
    {
        get
        {
            if (stremio != null)
                return stremio;
            stremioVideos = new(
                StremioLibraryWriter.CinemetaVideos(new System.Net.Http.HttpClientHandler()),
                TimeSpan.FromHours(12)
            );
            return stremio = new StremioLibraryWriter(
                store.DataDirectory,
                new System.Net.Http.HttpClientHandler(),
                stremioVideos.Get,
                new StremioMarkOptions()
            );
        }
    }

    /// <summary>Puts titles back in the Stremio account as Noir first found them, except ones changed there since.</summary>
    public Task<StremioRestoreSummary> RestoreStremio(params string[] imdbIds) =>
        Stremio.Restore(imdbIds, lifetime.Token);

    bool busy,
        disposed,
        wakeAgain;
#if NOIR_VALIDATE
    public Action<CoreWebView2>? ConfigureForTest { get; set; }

    public Task RunForTest() => Run();
#endif

    public CommunitySync(LibraryStore library, Dispatcher ui, Action onChanged)
    {
        store = library;
        backoff = new(library.State.SyncPace);
        dispatcher = ui;
        changed = onChanged;
        timer = new DispatcherTimer(TimeSpan.FromMinutes(5), DispatcherPriority.Background, (_, _) => Wake(), ui);
        store.WatchCompleted += Wake;
        timer.Start();
    }

    public void Wake()
    {
        if (disposed || Paused)
            return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Wake);
            return;
        }
        if (busy)
        {
            wakeAgain = true;
            return;
        }
        _ = Run();
    }

    public void Retry()
    {
        CommunityQueue.Retry(store.State, backoff);
        store.Save();
        Wake();
    }

    /// <summary>Stop the past-history send and remove its unsent jobs; anything already sent stays sent.</summary>
    public int CancelPrevious(string? provider = null)
    {
        var removed = CommunityQueue.CancelBacklog(store.State, backoff, provider);
        store.Save();
        changed();
        return removed;
    }

    public int QueuePrevious(string? provider = null)
    {
        var queued = CommunityQueue.QueueBacklog(store.State, provider);
        Paused = false;
        foreach (var name in CommunityQueue.Providers.Where(p => provider == null || p == provider))
            backoff.SetBacklogPaused(name, false);
        store.Save();
        Retry();
        changed();
        return queued;
    }

    /// <summary>Pause or resume only the past-history send; newer watches keep going.</summary>
    public void PausePrevious(string provider, bool paused)
    {
        backoff.SetBacklogPaused(provider, paused);
        store.Save();
        if (!paused)
            Wake();
        changed();
    }

    bool Current(WatchSyncJob job) =>
        !disposed
        && CommunityQueue.Enabled(store.State, job.Provider)
        && store.State.WatchSync.GetValueOrDefault(job.Provider + ":" + job.ItemId) == job
        && store.State.Catalog.GetValueOrDefault(job.ItemId) is { } entry
        && store.EverWatched(entry.Item);

    async Task<CoreWebView2> Browser()
    {
        if (web?.CoreWebView2 is { } existing)
            return existing;
        web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
        host = new Window
        {
            Title = "Noir watch sync",
            Width = 1120,
            Height = 820,
            Left = -30000,
            Top = -30000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Opacity = 0,
            Content = web,
        };
        host.Show();
        var environment = await CoreWebView2Environment.CreateAsync(
            userDataFolder: Path.Combine(store.DataDirectory, "review-browser")
        );
        lifetime.Token.ThrowIfCancellationRequested();
        await web.EnsureCoreWebView2Async(environment);
        var core = web.CoreWebView2;
        core.IsMuted = true;
        core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.NewWindowRequested += (_, e) => e.Handled = true;
#if NOIR_VALIDATE
        ConfigureForTest?.Invoke(core);
#endif
        core.NavigationStarting += (_, e) =>
        {
            if (
                !Uri.TryCreate(e.Uri, UriKind.Absolute, out var u)
                || u.Scheme != "https"
                || (
                    u.Host != "www.serializd.com"
                    && u.Host != "serializd.com"
                    && u.Host != "app.trakt.tv"
                    && u.Host != "trakt.tv"
                )
            )
                e.Cancel = true;
        };
        return core;
    }

    string TitleKey(WatchSyncJob job) =>
        store.State.Catalog.GetValueOrDefault(job.ItemId) is { } entry
            ? entry.Item.ImdbId
                + "|"
                + entry.Item.Season
                + "|"
                + entry.Item.Episode
                + "|"
                + CommunityQueue.SerializdEpisode(store.State, entry.Item)
            : "";

    async Task Run()
    {
        if (busy || disposed)
            return;
        wakeAgain = false;
        busy = true;
        try
        {
            Task Pass() =>
                CommunityQueue.Pass(
                    store.State,
                    backoff,
                    Clock,
                    TitleKey,
                    Current,
                    Send,
                    () =>
                    {
                        if (disposed)
                            return;
                        store.SaveDeferred();
                        changed();
                    },
                    persisted: async () => await store.FlushPendingAsync(),
                    log: store.Log
                );
            await Pass();
            if (
                RefreshIds is { } refresh
                && !disposed
                && await CommunityQueue.RetryIds(store.State, backoff, Clock(), refresh)
            )
            {
                store.SaveDeferred();
                await Pass();
            }
        }
        catch (OperationCanceledException) { }
        // Run is fired and forgotten: a failed save must not escape unobserved. The next wake tries again.
        catch (Exception e)
        {
            try
            {
                store.Log(e);
            }
            catch (Exception e2) when (e2 is IOException or UnauthorizedAccessException) { }
        }
        finally
        {
            busy = false;
            ReleaseBrowser();
            if (wakeAgain && !disposed)
                Wake();
        }
    }

    async Task<bool> Send(WatchSyncJob job)
    {
        var item = store.State.Catalog[job.ItemId].Item;
        // Stremio is an API call, no page: never opens the browser.
        if (job.Provider == CommunityQueue.Stremio)
            return await StremioSync.Send(Stremio, store.State, item, job, Loc.Arabic, lifetime.Token, stremioVideos);
        if (item.Season == 0 && !item.IsMovie && job.Provider == "Trakt")
            throw new SyncTitleException(CommunityQueue.SpecialMessage);
        if (job.Provider == "Trakt" && !System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId, @"^tt\d+$"))
            throw new SyncTitleException(CommunityQueue.NoIdMessage);
        var browser = await Browser();
        var url =
            job.Provider == "Serializd"
                ? await CommunityPages.ResolveSerializd(browser, item, store, lifetime.Token)
                : ReviewLinks.TraktUrl(item);
        if (job.Provider == "Trakt")
            await CommunityPages.Navigate(browser, url, lifetime.Token);
        if (job.Provider == "Serializd" && !await CommunityPages.VerifySerializdEpisode(browser, item))
            throw new SyncTitleException("Episode match needs checking in Reviews & comments.");
        var control = await WaitControl(browser, item, job.Provider);
        if (control != "watched")
        {
            if (control == "signed-out")
                throw new SyncSiteException(
                    "Your website session has expired. Sign in once under Reviews & comments; Noir resumes syncing automatically."
                );
            if (control != "ready")
                throw new SyncSiteException(
                    "The website's watched controls are unavailable. Noir will retry automatically."
                );
            if (!Current(job))
                return false;
            if (await CommunityPages.Control(browser, item, job.Provider, true) != "clicked")
                throw new SyncSiteException("The watched control changed. Noir will retry later.");
            if (job.Provider == "Trakt")
            {
                bool clicked = false;
                for (int i = 0; i < 30 && !clicked; i++)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    if (!Current(job))
                        break;
                    var result = await browser.ExecuteScriptAsync(CommunityPages.TraktDateControl);
                    clicked = result == "true";
                    if (!clicked)
                        await Task.Delay(500, lifetime.Token);
                }
                if (!clicked)
                    throw new SyncSiteException(
                        "Trakt's watched-date control is unavailable. Noir will retry automatically."
                    );
            }
            // Both sites update their controls optimistically. Reload before calling the job successful.
            await Task.Delay(4000, lifetime.Token);
            await CommunityPages.Navigate(browser, url, lifetime.Token);
            if (await WaitControl(browser, item, job.Provider) != "watched")
                throw new SyncSiteException("The website hasn't confirmed the watched mark. Noir will recheck later.");
        }
        return Current(job);
    }

    async Task<string> WaitControl(CoreWebView2 core, VideoItem item, string provider)
    {
        for (int i = 0; i < 50; i++)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            var result = await CommunityPages.Control(core, item, provider, false);
            if (result is "watched" or "ready")
                return result;
            if (result == "signed-out" && i >= 10)
                return result;
            await Task.Delay(500, lifetime.Token);
        }
        return "waiting";
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        store.WatchCompleted -= Wake;
        timer.Stop();
        lifetime.Cancel();
        ReleaseBrowser();
    }

    void ReleaseBrowser()
    {
        var oldWeb = web;
        var oldHost = host;
        web = null;
        host = null;
        oldWeb?.Dispose();
        oldHost?.Close();
    }
}

public partial class MainWindow
{
    CommunitySync? watchSync;
}
