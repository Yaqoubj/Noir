using System.Net.Http;
using System.Text.RegularExpressions;
namespace Noir;
// Sends Noir's watches and un-checks to Nuvio in batches, retrying while offline.
public sealed class NuvioSync:IDisposable
{
    readonly LibraryStore store;readonly System.Windows.Threading.DispatcherTimer timer;readonly Action changed;bool busy,again,disposed;
    readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(30)};
    public string Message {get;private set;}="";
    public NuvioSync(LibraryStore library,Action onChanged)
    {
        store=library;changed=onChanged;
        timer=new(TimeSpan.FromMinutes(5),System.Windows.Threading.DispatcherPriority.Background,(_,_)=>Wake(),System.Windows.Application.Current.Dispatcher);
        store.WatchCompleted+=Wake;store.HistoryChanged+=WakeIfDeletes;timer.Start();Wake();
    }
    void WakeIfDeletes(){if(store.State.NuvioDeletes.Count>0)Wake();}
    public void Wake()
    {
        if(disposed)return;if(busy){again=true;return;}
        _=Run();
    }
    async Task Run()
    {
        busy=true;again=false;
        try
        {
            if(!store.State.SyncNuvio||NuvioStorage.Load(store.DataDirectory) is not {Profile:>0} session)return;
            var client=new NuvioClient(http,session,s=>NuvioStorage.Save(store.DataDirectory,s));
            List<WatchSyncJob> jobs;List<string> deletes;
            lock(store.State)
            {
                jobs=store.State.WatchSync.Values.Where(j=>j.Provider=="Nuvio"&&j.SyncedAt==null&&j.RetryAt<=DateTimeOffset.UtcNow).ToList();
                deletes=store.State.NuvioDeletes.ToList();
            }
            if(jobs.Count==0&&deletes.Count==0)return;
            var items=new List<(WatchSyncJob Job,NuvioWatched Item)>();
            foreach(var job in jobs)
            {
                if(store.State.Catalog.GetValueOrDefault(job.ItemId)?.Item is not {} item){store.State.WatchSync.Remove("Nuvio:"+job.ItemId);continue;}
                if(!Regex.IsMatch(item.ImdbId,@"^tt\d+$")){job.Message="Waiting for a verified title ID.";continue;}
                items.Add((job,new(item.ImdbId,item.IsMovie?"movie":"series",item.Title,item.IsMovie?null:item.Season,item.IsMovie?null:item.Episode,job.WatchedAt.ToUnixTimeMilliseconds())));
            }
            if(items.Count>0)
            {
                foreach(var chunk in items.Chunk(200))await client.PushWatched(session.Profile,chunk.Select(c=>c.Item),CancellationToken.None);
                var now=DateTimeOffset.UtcNow;foreach(var (job,_) in items){job.SyncedAt=now;job.Message="Sent ✓";}
                store.MarkNuvioKnown(items.Select(i=>i.Job.ItemId));
            }
            if(deletes.Count>0)
            {
                await client.DeleteWatched(session.Profile,deletes.Select(NuvioClient.ParseKey),CancellationToken.None);
                lock(store.State)store.State.NuvioDeletes.ExceptWith(deletes);
            }
            Message=items.Count+deletes.Count>0?$"Sent {items.Count} watch{(items.Count==1?"":"es")}{(deletes.Count>0?$" and {deletes.Count} removal{(deletes.Count==1?"":"s")}":"")} to Nuvio.":"";
            store.Save();if(items.Count+deletes.Count>0)changed();
        }
        catch(NuvioExpiredException e){Message=e.Message;changed();}
        catch(Exception e)
        {
            store.Log(e);Message="Nuvio couldn't be reached. Noir will retry.";
            foreach(var job in store.State.WatchSync.Values.Where(j=>j.Provider=="Nuvio"&&j.SyncedAt==null))job.RetryAt=DateTimeOffset.UtcNow.AddMinutes(5);
        }
        finally{busy=false;if(again&&!disposed)Wake();}
    }
    public void Dispose(){disposed=true;timer.Stop();store.WatchCompleted-=Wake;store.HistoryChanged-=WakeIfDeletes;http.Dispose();}
}
