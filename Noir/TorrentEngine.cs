using System.Collections.Concurrent;
using System.IO;
using System.Net;
using MonoTorrent;
using MonoTorrent.Client;
namespace Noir;

public sealed class TorrentEngineOptions
{
    public bool Network {get;init;}=true;
    public TimeSpan MetadataStall {get;init;}=TimeSpan.FromMinutes(3);
    public TimeSpan DownloadStall {get;init;}=TimeSpan.FromMinutes(10);
    public TimeSpan PollInterval {get;init;}=TimeSpan.FromMilliseconds(300);
    public List<Uri> TestPeers {get;init;}=[];
}
public sealed class TorrentRuntime : IDisposable
{
    readonly string metadataDirectory;
    readonly TorrentEngineOptions options;
    readonly ConcurrentDictionary<string,SemaphoreSlim> metadataLocks=new(StringComparer.OrdinalIgnoreCase);
    public ClientEngine Engine {get;}
    public TorrentRuntime(string data,TorrentEngineOptions options)
    {
        this.options=options;metadataDirectory=Path.Combine(data,"torrent-meta");Directory.CreateDirectory(metadataDirectory);
        var settings=new EngineSettingsBuilder{CacheDirectory=Path.Combine(data,"torrent-engine"),AllowLocalPeerDiscovery=options.Network,AllowPortForwarding=options.Network,AutoSaveLoadDhtCache=options.Network,AutoSaveLoadFastResume=true,AutoSaveLoadMagnetLinkMetadata=true,UsePartialFiles=false,DhtEndPoint=options.Network?new IPEndPoint(IPAddress.Any,0):null,ListenEndPoints=new(){["ipv4"]=new(options.Network?IPAddress.Any:IPAddress.Loopback,0)}};
        Engine=new(settings.ToSettings());
    }
    internal TorrentSettings Settings()=>new TorrentSettingsBuilder{AllowDht=options.Network,AllowPeerExchange=options.Network,CreateContainingDirectory=false}.ToSettings();
    internal async Task Peers(TorrentManager manager){foreach(var peer in options.TestPeers)await manager.AddPeerAsync(new PeerInfo(peer));}
    public async Task<Torrent> Metadata(string hash,TimeSpan timeout,CancellationToken ct)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(hash,@"\A[a-fA-F0-9]{40}\z"))throw new IOException("This release has no usable torrent reference. Choose another release.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(timeout);
        var semaphore=metadataLocks.GetOrAdd(hash,_=>new(1,1));var entered=false;
        try
        {
            await semaphore.WaitAsync(deadline.Token);entered=true;var path=Path.Combine(metadataDirectory,hash.ToLowerInvariant()+".torrent");
            if(File.Exists(path)){try{var cached=await Torrent.LoadAsync(path);if(cached.InfoHashes.V1?.ToHex().Equals(hash,StringComparison.OrdinalIgnoreCase)==true){TorrentReleases.RememberFiles(hash,cached);return cached;}}catch(Exception e)when(e is IOException or ArgumentException or MonoTorrent.TorrentException){}File.Delete(path);}
            var download=Engine.DownloadMetadataAsync(TorrentReleases.Magnet(hash,options.Network),deadline.Token);
            while(!download.IsCompleted){foreach(var manager in Engine.Torrents.ToArray())await Peers(manager);await Task.WhenAny(download,Task.Delay(100,deadline.Token));deadline.Token.ThrowIfCancellationRequested();}
            var bytes=(await download).ToArray();var torrent=await Torrent.LoadAsync(bytes);
            if(torrent.InfoHashes.V1?.ToHex().Equals(hash,StringComparison.OrdinalIgnoreCase)!=true)throw new IOException("The release's file list could not be verified. Choose another release.");
            var temp=path+".tmp";await File.WriteAllBytesAsync(temp,bytes,deadline.Token);File.Move(temp,path,true);TorrentReleases.RememberFiles(hash,torrent);return torrent;
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new IOException("No one is sharing this right now. Try another release.");}
        finally{if(entered)semaphore.Release();}
    }
    internal async Task Limits(DownloadCredentials config)
    {
        var up=Math.Clamp(config.UploadLimitKB,0,2097151)*1024;var down=Math.Clamp(config.DownloadLimitKB,0,2097151)*1024;
        if(Engine.Settings.MaximumUploadRate==up&&Engine.Settings.MaximumDownloadRate==down)return;
        await Engine.UpdateSettingsAsync(new EngineSettingsBuilder(Engine.Settings){MaximumUploadRate=up,MaximumDownloadRate=down}.ToSettings());
    }
    public void Dispose()=>Engine.Dispose();
}
public sealed partial class PcDownloads
{
    readonly TorrentEngineOptions torrentOptions;
    TorrentRuntime? torrentRuntime;
    Task? torrentWorker;
    readonly ConcurrentDictionary<string,TorrentSession> torrentSessions=new(StringComparer.OrdinalIgnoreCase);
    TorrentRuntime TorrentRuntime{get{lock(gate)return torrentRuntime??=new(store.DataDirectory,torrentOptions);}}
    sealed class TorrentSession(string hash,string incoming,CancellationTokenSource cancel,Task<Torrent> metadata)
    {
        public string Hash=hash,Incoming=incoming;
        public CancellationTokenSource Cancel=cancel;
        public Task<Torrent> Metadata=metadata;
        public TorrentManager? Manager;
        public DateTime Started=DateTime.UtcNow;
        public readonly Dictionary<string,DateTime> Idle=[];
        public readonly Dictionary<string,SpeedMeter> Meters=[];
        public long LastUploaded;
    }
    string TorrentText(string english,string arabic)=>store.State.Language=="ar"?arabic:english;
    bool TorrentActive(PcDownloadJob j)=>j.Service=="Torrents"&&j.Status is "Queued" or "Preparing" or "Downloading" or "Subtitles";
    // Enqueue raises Changed with the review session still alive: retain alternatives before it expires.
    void CaptureTorrentJobs()
    {
        lock(gate)
        {
            var changed=false;
            foreach(var job in jobs.Where(j=>j.Service=="Torrents"&&j.Status=="Queued"&&j.Switches==0))
            {
                if(job.FileId<0){job.FileId=TorrentReleases.Index(job.Hash,job.RemoteName,job.Size);changed|=job.FileId>=0;}
                if(job.Alternatives.Count>0)continue;
                var candidates=seasonPlans.Values.SelectMany(p=>p.Plan.Episodes.Where(e=>e.Season==job.Target.Season&&e.Episode==job.Target.Episode&&p.Group==job.Target.GroupKey)).SelectMany(e=>e.Alternatives);
                foreach(var candidate in candidates){if(!releases.TryGetValue(candidate.ReleaseId,out var r)||r.Service!="Torrents"||r.Release.Hash==job.Hash||job.Alternatives.Any(a=>a.Hash==r.Release.Hash))continue;job.Alternatives.Add(new(r.Release.Hash,TorrentReleases.Index(r.Release.Hash,candidate.File.Name,candidate.File.Size),candidate.File.Name,candidate.File.Size));changed=true;}
                var current=releases.Values.FirstOrDefault(r=>r.Service=="Torrents"&&r.Release.Hash==job.Hash);if(current.Release!=null){job.Health=current.Release.Choice.Health;if(job.FileId<0&&current.Release.Choice.FileIndex>=0){job.FileId=current.Release.Choice.FileIndex;changed=true;}}
            }
            if(changed)Save();
        }
    }
    async Task TorrentWorker()
    {
        try
        {
            while(!stop.IsCancellationRequested)
            {
                CaptureTorrentJobs();List<PcDownloadJob> active;var config=Config;
                lock(gate)active=jobs.Where(TorrentActive).ToList();
                foreach(var session in torrentSessions.Values.ToArray())
                {
                    try{await TickTorrent(session,config);}catch(OperationCanceledException)when(stop.IsCancellationRequested){throw;}
                    catch(Exception e){try{store.Log(new Exception(DownloadSecurity.SafeText(e.ToString(),Config)));}catch{}List<PcDownloadJob> affected;lock(gate)affected=jobs.Where(j=>j.Hash==session.Hash&&TorrentActive(j)).ToList();await RemoveTorrent(session,false);foreach(var job in affected)Update(job,"Failed",TorrentText("Torrent download stopped. Check your connection and disk space, then Resume.","توقف تنزيل التورنت. تحقق من الاتصال ومساحة القرص ثم استأنف."));}
                }
                foreach(var group in active.Where(j=>!torrentSessions.ContainsKey(j.Hash)).GroupBy(j=>j.Hash))
                {
                    if(torrentSessions.Values.Count(s=>active.Any(j=>j.Hash==s.Hash))>=Math.Clamp(config.MaxActiveTorrents,1,20))break;
                    var job=group.First();if(!TorrentActive(job))continue;
                    try
                    {
                        if(!System.Text.RegularExpressions.Regex.IsMatch(job.Hash,@"\A[a-fA-F0-9]{40}\z"))throw new IOException("Invalid torrent");DownloadNaming.Validate(job.Root,job.Destination);var incoming=Path.Combine(job.Root,".noir-incoming",job.Hash.ToLowerInvariant());Directory.CreateDirectory(incoming);var hidden=Directory.GetParent(incoming)!;File.SetAttributes(hidden.FullName,File.GetAttributes(hidden.FullName)|FileAttributes.Hidden);
                        var cancel=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);var session=new TorrentSession(job.Hash,incoming,cancel,TorrentRuntime.Metadata(job.Hash,torrentOptions.MetadataStall,cancel.Token));torrentSessions[job.Hash]=session;
                        foreach(var pending in group)Update(pending,"Preparing","Finding people to download from…");
                    }
                    catch(Exception){foreach(var pending in group)Update(pending,"Failed",TorrentText("Couldn't start this torrent. Check the download folder and try again.","تعذر بدء التورنت. تحقق من مجلد التنزيل ثم أعد المحاولة."));}
                }
                if(torrentRuntime!=null)await torrentRuntime.Limits(config);
                await Task.Delay(torrentOptions.PollInterval,stop.Token);
            }
        }
        catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        finally
        {
            foreach(var session in torrentSessions.Values.ToArray())try{await RemoveTorrent(session,false);}catch{}
            lock(gate){if(jobs.Any(j=>j.Service=="Torrents")){foreach(var job in jobs.Where(TorrentActive)){job.Status="Paused";job.Speed=0;job.EtaSeconds=-1;job.Message="Interrupted · resume to continue";}foreach(var job in jobs.Where(j=>j.Service=="Torrents"&&j.Status=="Completed")){job.SeedUntil=null;job.Message="Saved to the PC library";}Save();}}
            torrentRuntime?.Dispose();torrentRuntime=null;
        }
    }
    async Task TickTorrent(TorrentSession session,DownloadCredentials config)
    {
        List<PcDownloadJob> group;lock(gate)group=jobs.Where(j=>j.Service=="Torrents"&&j.Hash==session.Hash&&(TorrentActive(j)||j.Status=="Completed"&&j.SeedUntil!=null)).ToList();
        if(group.Count==0){await RemoveTorrent(session,false);return;}
        if(session.Manager==null)
        {
            if(!session.Metadata.IsCompleted){if(DateTime.UtcNow-session.Started>TimeSpan.FromSeconds(1))foreach(var job in group)if(job.Message!="Getting the file list…")Update(job,"Preparing","Getting the file list…");return;}
            Torrent torrent;try{torrent=await session.Metadata;}catch(OperationCanceledException)when(session.Cancel.IsCancellationRequested){return;}catch{await Stuck(session,group,config);return;}
            await TorrentRuntime.Limits(config);session.Manager=await TorrentRuntime.Engine.AddAsync(torrent,session.Incoming,TorrentRuntime.Settings());
            // Set every priority before Start: a pack must never fetch other episodes by default.
            await Priorities(session.Manager,group);await session.Manager.StartAsync();await TorrentRuntime.Peers(session.Manager);
        }
        var manager=session.Manager;if(manager.State==TorrentState.Error)throw new IOException("Torrent unavailable");
        await Priorities(manager,group);
        var uploaded=Math.Max(0,manager.Monitor.DataBytesSent-session.LastUploaded);session.LastUploaded=manager.Monitor.DataBytesSent;
        foreach(var job in group)
        {
            if(!TorrentActive(job)&&job.Status!="Completed")continue;
            var file=TorrentFile(manager,job);if(file==null)throw new IOException("File list changed");
            if(job.Status=="Completed")
            {
                job.Uploaded+=uploaded/Math.Max(1,group.Count);var ratio=job.Size>0?job.Uploaded/(double)job.Size:0;
                if(config.Seeding=="off"||config.Seeding=="ratio"&&(job.Uploaded>=job.Size||job.SeedUntil<=DateTimeOffset.UtcNow)){lock(gate){job.SeedUntil=null;job.Message="Saved to the PC library";Save();}}
                else{lock(gate){job.Message=TorrentText("Sharing back · ratio ","المشاركة مع الآخرين · النسبة ")+ratio.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture);}}
                continue;
            }
            var now=DateTime.UtcNow;var bytes=Math.Min(file.Length,(long)(file.Length*file.BitField.PercentComplete/100));
            lock(gate){job.Size=file.Length;job.Bytes=bytes;job.Peers=manager.OpenConnections;job.Seeds=manager.Peers.Seeds;job.Uploaded+=uploaded/Math.Max(1,group.Count);if(!session.Meters.TryGetValue(job.Id,out var meter))session.Meters[job.Id]=meter=new();job.Speed=meter.Add(now,bytes);job.EtaSeconds=DownloadEta.Seconds(job.Size,bytes,job.Speed);}
            if(file.BitField.AllTrue&&manager.HashChecked)
            {
                Update(job,"Preparing","Checking the file…");DownloadNaming.Validate(job.Root,job.Destination);Directory.CreateDirectory(Path.GetDirectoryName(job.Destination)!);
                if(!TorrentActive(job))continue;
                if(!file.FullPath.Equals(job.Destination,StringComparison.OrdinalIgnoreCase))await manager.MoveFileAsync(file,job.Destination);
                if(!TorrentActive(job))continue;
                store.RegisterDownload(job.Target,job.Destination);await FinishSubtitles(job,[],session.Cancel.Token);
                lock(gate){job.Bytes=job.Size;job.Speed=0;job.EtaSeconds=-1;job.SeedUntil=config.Seeding=="off"?null:config.Seeding=="open"?DateTimeOffset.MaxValue:DateTimeOffset.UtcNow.AddHours(2);Save();}
            }
            else
            {
                var checking=manager.State==TorrentState.Hashing||manager.State==TorrentState.HashingPaused;
                lock(gate){if(!TorrentActive(job))continue;job.Status=checking?"Preparing":"Downloading";job.Message=checking?"Checking the file…":job.Switches>0?"Switched to another release — the first one had no one sharing":TorrentText($"{job.Peers} people · {job.Seeds} sharing",$"{job.Peers} أشخاص · {job.Seeds} يشاركون");}
                if(!checking&&job.Seeds==0&&job.Speed<1){if(!session.Idle.TryGetValue(job.Id,out var idle))session.Idle[job.Id]=now;else if(now-idle>=torrentOptions.DownloadStall){await Stuck(session,[job],config);return;}}else session.Idle.Remove(job.Id);
            }
        }
        lock(gate){if(DateTime.UtcNow-progressSaved>TimeSpan.FromSeconds(5))Save();}
        Changed?.Invoke();
        if(group.All(j=>!TorrentActive(j)&&j.SeedUntil==null))await RemoveTorrent(session,false);
    }
    ITorrentManagerFile? TorrentFile(TorrentManager manager,PcDownloadJob job)
    {
        if(job.FileId>=0&&job.FileId<manager.Files.Count)return manager.Files[(int)job.FileId];
        var files=manager.Files.Where(f=>f.Path.Replace('\\','/').Equals(job.RemoteName.Replace('\\','/'),StringComparison.OrdinalIgnoreCase)&&(job.Size<=0||f.Length==job.Size)).ToList();
        if(files.Count==0)files=manager.Files.Where(f=>Path.GetFileName(f.Path).Equals(Path.GetFileName(job.RemoteName),StringComparison.OrdinalIgnoreCase)&&(job.Size<=0||f.Length==job.Size)).ToList();
        if(files.Count!=1)return null;job.FileId=manager.Files.IndexOf(files[0]);return files[0];
    }
    async Task Priorities(TorrentManager manager,List<PcDownloadJob> group)
    {
        var selected=group.Where(j=>TorrentActive(j)||j.Status=="Completed"&&j.SeedUntil!=null).Select(j=>TorrentFile(manager,j)).Where(f=>f!=null).ToHashSet();
        foreach(var file in manager.Files){var priority=selected.Contains(file)?Priority.Normal:Priority.DoNotDownload;if(file.Priority!=priority)await manager.SetFilePriorityAsync(file,priority);}
    }
    async Task Stuck(TorrentSession session,List<PcDownloadJob> group,DownloadCredentials config)
    {
        foreach(var job in group)
        {
            lock(gate)
            {
                if(!TorrentActive(job))continue;
                var alternative=config.AutoSwitch&&job.Switches<2?job.Alternatives.FirstOrDefault(a=>a.Hash!=job.Hash):null;
                if(alternative==null){job.Status="Failed";job.Message=TorrentText("No one is sharing this right now. Pick another release.","لا أحد يشارك هذا الملف الآن. اختر إصدارًا آخر.");}
                else{job.Alternatives.RemoveAll(a=>a.Hash==alternative.Hash||a.Hash==job.Hash);job.Switches++;job.Hash=alternative.Hash;job.FileId=alternative.FileIndex;job.RemoteName=alternative.Name;job.Size=alternative.Size;job.Bytes=0;job.Uploaded=0;job.Status="Queued";job.Message="Switched to another release — the first one had no one sharing";}
                job.Speed=0;job.EtaSeconds=-1;Save();
            }
        }
        bool shared;lock(gate)shared=jobs.Any(j=>j.Hash==session.Hash&&(TorrentActive(j)||j.Status=="Paused"||j.SeedUntil!=null));
        if(!shared)await RemoveTorrent(session,true);Changed?.Invoke();
    }
    async Task RemoveTorrent(TorrentSession session,bool delete)
    {
        torrentSessions.TryRemove(session.Hash,out _);session.Cancel.Cancel();
        if(session.Manager is {} manager){await manager.StopAsync(TimeSpan.Zero);if(torrentRuntime!=null)await torrentRuntime.Engine.RemoveAsync(manager,RemoveMode.KeepAllData);}
        try{await session.Metadata;}catch{}
        // Incoming only; canonical library files are never deleted by the engine.
        if(Directory.Exists(session.Incoming)){if(delete)Directory.Delete(session.Incoming,true);else{foreach(var directory in Directory.EnumerateDirectories(session.Incoming,"*",SearchOption.AllDirectories).OrderByDescending(p=>p.Length))if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);if(!Directory.EnumerateFileSystemEntries(session.Incoming).Any())Directory.Delete(session.Incoming);}}
        session.Cancel.Dispose();
    }
    partial void TorrentStop(PcDownloadJob job,bool deleteData)
    {
        if(job.Service!="Torrents")return;
        job.SeedUntil=null;job.Speed=0;job.EtaSeconds=-1;
        var shared=jobs.Where(j=>j.Id!=job.Id&&j.Hash==job.Hash&&j.Service=="Torrents"&&(TorrentActive(j)||j.SeedUntil!=null||j.Status=="Paused")).ToList();
        // Cancel stops the session first and paused torrents have none, so deletion must still reach the staging folder.
        if(!torrentSessions.TryGetValue(job.Hash,out var session)){if(deleteData&&shared.Count==0)DeleteIncoming(job);return;}
        if(shared.Any(j=>TorrentActive(j)||j.SeedUntil!=null)){if(session.Manager!=null)Priorities(session.Manager,shared).GetAwaiter().GetResult();return;}
        torrentSessions.TryRemove(job.Hash,out _);session.Cancel.Cancel();
        if(session.Manager is {} manager){manager.StopAsync(TimeSpan.Zero).GetAwaiter().GetResult();torrentRuntime?.Engine.RemoveAsync(manager,RemoveMode.KeepAllData).GetAwaiter().GetResult();}
        if(deleteData&&shared.Count==0&&Directory.Exists(session.Incoming))Directory.Delete(session.Incoming,true);
    }
    static void DeleteIncoming(PcDownloadJob job)
    {
        if(string.IsNullOrWhiteSpace(job.Root)||!System.Text.RegularExpressions.Regex.IsMatch(job.Hash??"",@"\A[a-fA-F0-9]{40}\z"))return;
        var parent=Path.Combine(job.Root,".noir-incoming");var incoming=Path.Combine(parent,job.Hash!.ToLowerInvariant());
        if(!Directory.Exists(incoming)||(File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0||(File.GetAttributes(incoming)&FileAttributes.ReparsePoint)!=0)return;
        try{Directory.Delete(incoming,true);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
    }
    partial void TorrentFileMoved(PcDownloadJob job,string newPath)=>TorrentStop(job,false);
    void DisposeTorrents()
    {
        // The loop closes managers and saves resume data after cancellation without blocking the UI.
        if(torrentWorker==null){torrentRuntime?.Dispose();torrentRuntime=null;}
    }
    public int ActiveTorrentCount=>torrentRuntime?.Engine.Torrents.Count??0;
    public Task TorrentStopped=>torrentWorker??Task.CompletedTask;
}
