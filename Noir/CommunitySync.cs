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
    readonly CancellationTokenSource lifetime=new();
    readonly DispatcherTimer timer;
    Window? host;
    WebView2? web;
    bool busy,disposed,wakeAgain;
#if NOIR_VALIDATE
    public Action<CoreWebView2>? ConfigureForTest {get;set;}
    public Task RunForTest()=>Run();
#endif
    public CommunitySync(LibraryStore library,Dispatcher ui,Action onChanged)
    {
        store=library;dispatcher=ui;changed=onChanged;
        timer=new DispatcherTimer(TimeSpan.FromMinutes(5),DispatcherPriority.Background,(_,_)=>Wake(),ui);
        store.WatchCompleted+=Wake;timer.Start();
    }
    public void Wake()
    {
        if(disposed) return;
        if(!dispatcher.CheckAccess()) {dispatcher.BeginInvoke(Wake);return;}
        if(busy) {wakeAgain=true;return;}
        _=Run();
    }
    public void Retry()
    {
        foreach(var job in store.State.WatchSync.Values.Where(j=>j.SyncedAt==null)) job.RetryAt=DateTimeOffset.MinValue;
        store.Save();Wake();
    }
    public void QueuePrevious()
    {
        foreach(var entry in store.State.Catalog.Values.Where(e=>e.Progress?.Completed==true)) CommunityQueue.Enqueue(store.State,entry.Item,entry.Progress!.UpdatedAt);
        store.Save();Retry();changed();
    }
    bool Current(WatchSyncJob job)=>!disposed&&CommunityQueue.Enabled(store.State,job.Provider)&&store.State.WatchSync.GetValueOrDefault(job.Provider+":"+job.ItemId)==job&&store.State.Catalog.GetValueOrDefault(job.ItemId) is { } entry&&store.EverWatched(entry.Item);
    async Task<CoreWebView2> Browser()
    {
        if(web?.CoreWebView2 is { } existing) return existing;
        web=new WebView2{DefaultBackgroundColor=System.Drawing.Color.Black};
        host=new Window{Title="Noir watch sync",Width=1120,Height=820,Left=-30000,Top=-30000,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,Opacity=0,Content=web};
        host.Show();
        var environment=await CoreWebView2Environment.CreateAsync(userDataFolder:Path.Combine(store.DataDirectory,"review-browser"));
        lifetime.Token.ThrowIfCancellationRequested();await web.EnsureCoreWebView2Async(environment);
        var core=web.CoreWebView2;core.IsMuted=true;
        core.Profile.PreferredColorScheme=CoreWebView2PreferredColorScheme.Dark;
        core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
        core.NewWindowRequested+=(_,e)=>e.Handled=true;
#if NOIR_VALIDATE
        ConfigureForTest?.Invoke(core);
#endif
        core.NavigationStarting+=(_,e)=>{if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out var u)||u.Scheme!="https" || (u.Host!="www.serializd.com"&&u.Host!="serializd.com"&&u.Host!="app.trakt.tv"&&u.Host!="trakt.tv")) e.Cancel=true;};
        return core;
    }
    async Task Run()
    {
        if(busy||disposed) return;
        wakeAgain=false;
        busy=true;
        try
        {
            foreach(var job in store.State.WatchSync.Values.Where(j=>j.SyncedAt==null&&j.RetryAt<=DateTimeOffset.UtcNow&&CommunityQueue.Enabled(store.State,j.Provider)).OrderBy(j=>j.WatchedAt).ToList())
            {
                if(!Current(job)) continue;
                job.Attempts++;job.Message="Syncing…";store.Save();changed();
                try
                {
                    var item=store.State.Catalog[job.ItemId].Item;
                    if(item.Season==0&&!item.IsMovie&&job.Provider=="Trakt") throw new IOException("Specials need a confirmed Trakt match. Mark the correct special on its website; your Noir history remains saved.");
                    if(job.Provider=="Trakt"&&!System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId,@"^tt\d+$")) throw new IOException("Waiting for a verified title ID. Refresh information in Settings.");
                    var browser=await Browser();
                    var url=job.Provider=="Serializd"?await CommunityPages.ResolveSerializd(browser,item,store,lifetime.Token):ReviewLinks.TraktUrl(item);
                    if(job.Provider=="Trakt") await CommunityPages.Navigate(browser,url,lifetime.Token);
                    if(job.Provider=="Serializd"&&!await CommunityPages.VerifySerializdEpisode(browser,item)) throw new IOException("Episode match needs checking in Reviews & comments.");
                    var control=await WaitControl(browser,item,job.Provider);
                    if(control!="watched")
                    {
                        if(control=="signed-out") throw new IOException("Your website session has expired. Sign in once under Reviews & comments; Noir resumes syncing automatically.");
                        if(control!="ready") throw new IOException("The website's watched controls are unavailable. Noir will retry automatically.");
                        if(!Current(job)) continue;
                        if(await CommunityPages.Control(browser,item,job.Provider,true)!="clicked") throw new IOException("The watched control changed. Noir will retry later.");
                        if(job.Provider=="Trakt")
                        {
                            bool clicked=false;
                            for(int i=0;i<30&&!clicked;i++)
                            {
                                lifetime.Token.ThrowIfCancellationRequested();
                                if(!Current(job)) break;
                                var result=await browser.ExecuteScriptAsync(CommunityPages.TraktDateControl);
                                clicked=result=="true";if(!clicked) await Task.Delay(500,lifetime.Token);
                            }
                            if(!clicked) throw new IOException("Trakt's watched-date control is unavailable. Noir will retry automatically.");
                        }
                        // Both sites update their controls optimistically. Reload before calling the job successful.
                        await Task.Delay(4000,lifetime.Token);
                        await CommunityPages.Navigate(browser,url,lifetime.Token);
                        if(await WaitControl(browser,item,job.Provider)!="watched") throw new IOException("The website hasn't confirmed the watched mark. Noir will recheck later.");
                    }
                    if(Current(job)) {job.SyncedAt=DateTimeOffset.UtcNow;job.Message="Watched ✓";store.Save();}
                }
                catch(OperationCanceledException) {break;}
                catch(Exception e)
                {
                    if(!disposed) {job.Message=e is IOException?e.Message:"Website unavailable. Noir will retry later.";job.RetryAt=DateTimeOffset.UtcNow.AddMinutes(5);store.Save();}
                }
                if(!disposed) changed();
            }
        }
        finally
        {
            busy=false;ReleaseBrowser();
            if(wakeAgain&&!disposed) Wake();
        }
    }
    async Task<string> WaitControl(CoreWebView2 core,VideoItem item,string provider)
    {
        for(int i=0;i<50;i++)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            var result=await CommunityPages.Control(core,item,provider,false);
            if(result is "watched" or "ready") return result;
            if(result=="signed-out"&&i>=10) return result;
            await Task.Delay(500,lifetime.Token);
        }
        return "waiting";
    }
    public void Dispose()
    {
        if(disposed) return;
        disposed=true;store.WatchCompleted-=Wake;timer.Stop();lifetime.Cancel();ReleaseBrowser();
    }
    void ReleaseBrowser()
    {
        var oldWeb=web;var oldHost=host;web=null;host=null;
        oldWeb?.Dispose();oldHost?.Close();
    }
}

public partial class MainWindow
{
    CommunitySync? watchSync;
    void RenderCommunitySettings()
    {
        ContentPanel.Children.Add(Heading("Connected watch history","Finished here, remembered on your accounts."));
        void Option(string provider,bool enabled,Action<bool> save)
        {
            var check=new System.Windows.Controls.CheckBox{Content="Mark watched on "+provider+(provider=="Serializd"?" · TV episodes":" · movies & episodes"),IsChecked=enabled};
            check.Click+=(_,_)=>{save(check.IsChecked==true);store.Save();watchSync?.Retry();};ContentPanel.Children.Add(check);
            var jobs=store.State.WatchSync.Values.Where(j=>j.Provider==provider).ToList();
            var pending=jobs.Count(j=>j.SyncedAt==null);var done=jobs.Count(j=>j.SyncedAt!=null);
            ContentPanel.Children.Add(Body($"{done} confirmed · {pending} waiting"+(jobs.LastOrDefault(j=>j.SyncedAt==null) is { } last?" · "+last.Message:""),12));
        }
        Option("Trakt",store.State.SyncTrakt,value=>store.State.SyncTrakt=value);
        Option("Serializd",store.State.SyncSerializd,value=>store.State.SyncSerializd=value);
        ContentPanel.Children.Add(Body("Sync runs automatically in the background when Noir marks a video watched. You don't need to open a website or press Retry. Uses the accounts signed in under Reviews & comments; the browser closes when its work is done. Offline updates wait here and retry while Noir is open, or after its next launch. Websites can expire a login or request verification. Marking unwatched in Noir does not remove website history.",12));
        var actions=new System.Windows.Controls.WrapPanel();actions.Children.Add(ActionButton("Retry sync",()=>watchSync?.Retry()));
        var previous=ActionButton("Sync earlier watched items",()=>watchSync?.QueuePrevious());previous.Margin=new(12,0,0,0);actions.Children.Add(previous);ContentPanel.Children.Add(actions);
        foreach(var job in store.State.WatchSync.Values.Where(j=>j.SyncedAt==null).Take(8))
            if(store.State.Catalog.GetValueOrDefault(job.ItemId)?.Item is { } item)
                ContentPanel.Children.Add(ActionButton(job.Provider+" · "+item.Label,()=>OpenReviews(item,job.Provider)));
    }
}
