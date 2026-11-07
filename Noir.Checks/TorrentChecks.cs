using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.BEncoding;
using Noir;

static class TorrentChecks
{
    public static async Task Run(string parent,Action<bool,string> check)
    {
        var watch=Stopwatch.StartNew();
        var defaults=JsonSerializer.Deserialize<DownloadCredentials>("{\"Service\":\"TorBox\",\"Key\":\"existing-key\"}")!;
        check(defaults.Seeding=="ratio"&&defaults.UploadLimitKB==1024&&defaults.DownloadLimitKB==0&&defaults.AutoSwitch&&defaults.MaxActiveTorrents==3&&defaults.Key=="existing-key","Torrent defaults preserve existing debrid settings");
        foreach(var (json,n,health) in new[]{("{\"description\":\"[NOIR_SEEDERS:12]\"}",12,"Good"),("{\"title\":\"👤 5\"}",5,"Low"),("{\"title\":\"👥 11\"}",11,"Good"),("{}",-1,"Unknown"),("{\"seeders\":0}",0,"Dead"),("{\"streamData\":{\"torrent\":{\"seeders\":22}}}",22,"Good")}){using var doc=JsonDocument.Parse(json);check(TorrentReleases.Seeders(doc.RootElement)==n&&TorrentReleases.Health(n)==health,"Torrent health parses "+json);}
        var hash=new string('b',40);using var http=new HttpClient(new IndexHandler(hash));var config=new DownloadCredentials{Service="Torrents",Manifest="https://addon.invalid/secret/manifest.json"};
        var searched=await new TorrentReleases(()=>config,http).Search(new(){ImdbId="tt123",Season=1,Episode=1},default);
        check(searched.Count==3&&searched.Select(r=>r.Choice.Health).SequenceEqual(new[]{"Good","Unknown","Low"})&&searched.All(r=>r.Hash.Length==40&&!r.Choice.Cached)&&searched[0].Choice.CacheStatus=="Good"&&searched[0].Choice.FileIndex==0,"Torrent search keeps hashes, hides dead releases, and ranks Good before Unknown before Low");
        config.Service="AIOStreams links";var links=await new TorboxProvider(()=>config,http).IndexSearch(new(){ImdbId="tt123",Season=1,Episode=1},default);check(links.All(r=>r.Hash==""&&r.Direct!=null&&r.Choice.Cached),"AIOStreams links still use ready HTTP links without hashes");
        var dir=Path.Combine(Directory.GetCurrentDirectory(),"work","downloads-round","p1","tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var source=Path.Combine(dir,"source");Directory.CreateDirectory(source);var bytes=new byte[3*1024*1024];new Random(123).NextBytes(bytes);await File.WriteAllBytesAsync(Path.Combine(source,"Show.S01E01.mkv"),bytes);new Random(456).NextBytes(bytes);await File.WriteAllBytesAsync(Path.Combine(source,"Show.S01E02.mkv"),bytes);var expected=await File.ReadAllBytesAsync(Path.Combine(source,"Show.S01E01.mkv"));
        var creator=new TorrentCreator(TorrentType.V1Only){PieceLength=32768};var torrentPath=Path.Combine(dir,"fixture.torrent");await creator.CreateAsync(new TorrentFileSource(source),torrentPath);var torrent=await Torrent.LoadAsync(torrentPath);hash=torrent.InfoHashes.V1!.ToHex();
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        using var seeder=new ClientEngine(new EngineSettingsBuilder{CacheDirectory=Path.Combine(dir,"seed-cache"),MaximumUploadRate=256*1024,MaximumDiskReadRate=512*1024,DiskCacheBytes=0,AllowLocalPeerDiscovery=false,AllowPortForwarding=false,AutoSaveLoadDhtCache=false,DhtEndPoint=null,ListenEndPoints=new(){["ipv4"]=new(IPAddress.Loopback,port)}}.ToSettings());
        var seed=await seeder.AddAsync(torrent,source,new TorrentSettingsBuilder{AllowDht=false,AllowPeerExchange=false,CreateContainingDirectory=false}.ToSettings());await seed.HashCheckAsync(false);check(seed.Complete,"Loopback torrent fixture passes a real hash check");await seed.StartAsync();
        using var tracker=new LoopbackTracker(port);
        using(var stream=JsonDocument.Parse(JsonSerializer.Serialize(new{sources=new[]{"tracker:"+tracker.Url}})))TorrentReleases.Remember(hash,stream.RootElement);
        var fixture=new Fixture(hash,torrent.Files.Select((f,i)=>new TorboxFile(i,f.Path,f.Length)).ToList());
        var (store,media,item)=Library(Path.Combine(dir,"download"));var profile=store.DataDirectory;
        var options=new TorrentEngineOptions{Network=false,TestPeers=[new Uri($"ipv4://127.0.0.1:{port}")],MetadataStall=TimeSpan.FromSeconds(15),DownloadStall=TimeSpan.FromSeconds(15),PollInterval=TimeSpan.FromMilliseconds(100)};
        var downloads=new PcDownloads(store,fixture,torrentOptions:options);
        string last="";downloads.Changed+=()=>{var j=downloads.Jobs().FirstOrDefault();if(j!=null){var value=j.Status+" "+j.Bytes+" "+j.Message;if(value!=last){last=value;Console.WriteLine("TORRENT "+value);}}};
        try
        {
            downloads.Configure("https://addon.invalid/manifest.json","",media,"",subtitleLanguages:[],service:"Torrents",seeding:"off",uploadLimitKB:64,downloadLimitKB:512,autoSwitch:true,maxActiveTorrents:2);
            check(downloads.Configured&&downloads.ActiveTorrentCount==0,"Torrents need a manifest and do not start the engine when settings are saved");
            using(var reopened=new PcDownloads(store,fixture,start:false,torrentOptions:options)){var c=reopened.Config;check(c.Service=="Torrents"&&c.Seeding=="off"&&c.UploadLimitKB==64&&c.DownloadLimitKB==512&&c.AutoSwitch&&c.MaxActiveTorrents==2&&c.Key=="","Torrent preferences round-trip through DPAPI");}
            var choices=await downloads.Search(item.Id,false);var files=await downloads.Files(choices[0].Id);var id=downloads.Enqueue(new(){RequestId=Guid.NewGuid().ToString(),ReleaseId=choices[0].Id,FileIds=[files.Single(f=>f.Episode==1).Id]}).Single();
            // Loopback peers are not rate limited, so a mid-transfer pause is racy: pause at once, deterministically.
            downloads.Control(id,"pause");var before=downloads.Jobs().Single().Bytes;await Task.Delay(1500);var paused=downloads.Jobs().Single();check(paused.Status=="Paused"&&paused.Bytes==before&&!File.Exists(paused.Destination),"Pausing a torrent stops its byte counter and does not finish the file");
            await Until(()=>downloads.ActiveTorrentCount==0,3,"paused torrent has no session",downloads);var staging=Path.Combine(media,".noir-incoming",hash);Directory.CreateDirectory(staging);File.WriteAllBytes(Path.Combine(staging,"partial.bin"),[1,2,3]);
            await downloads.CancelDownload(id,true);check(!Directory.Exists(staging)&&downloads.Jobs().Single().Status=="Cancelled","Cancel and delete removes a paused torrent's partial data");downloads.ClearFinished();
            id=downloads.Enqueue(new(){RequestId=Guid.NewGuid().ToString(),ReleaseId=choices[0].Id,FileIds=[files.Single(f=>f.Episode==1).Id]}).Single();await Until(()=>downloads.Jobs().Single().Status is "Completed" or "Failed",20,"loopback finishes after resume",downloads);var job=downloads.Jobs().Single();
            check(job.Status=="Completed"&&job.Destination==DownloadNaming.Destination(media,item,"Show.S01E01.mkv")&&File.ReadAllBytes(job.Destination).SequenceEqual(expected),"Torrent saves exact episode bytes at the canonical destination");
            check(store.Archive.Any(i=>i.Id==item.Id&&i.Available&&i.Path==job.Destination),"Completed torrent joins the PC library");
            var incoming=Path.Combine(media,".noir-incoming",hash);check(!Directory.Exists(incoming)||!Directory.EnumerateFiles(incoming,"Show.S01E02.mkv",SearchOption.AllDirectories).Any(f=>new FileInfo(f).Length>0),"Other episode in the pack remains absent or zero length");
            await Until(()=>downloads.ActiveTorrentCount==0,3,"off policy removes manager",downloads);check(downloads.ActiveTorrentCount==0,"Don't share removes the torrent after completion");            check(File.Exists(Path.Combine(profile,"torrent-meta",hash+".torrent")),"Magnet metadata is cached as verified .torrent bytes");
        }
        finally{downloads.Dispose();await downloads.TorrentStopped;}
        await Stall(Path.Combine(dir,"switch"),true,hash,torrentPath,fixture,check);
        await Stall(Path.Combine(dir,"fail"),false,hash,torrentPath,fixture,check);
        await seed.StopAsync(TimeSpan.Zero);
        check(watch.Elapsed<TimeSpan.FromSeconds(60),"Offline torrent checks finish in under 60 seconds");
    }
    static async Task Stall(string dir,bool alternative,string hash,string torrentPath,Fixture fixture,Action<bool,string> check)
    {
        var (store,media,item)=Library(dir);var dead=new string('d',40);var destination=DownloadNaming.Destination(media,item,"Show.S01E01.mkv");
        // Exercise download stalls without peers; metadata is local and hash verified.
        var meta=Path.Combine(store.DataDirectory,"torrent-meta");Directory.CreateDirectory(meta);File.Copy(torrentPath,Path.Combine(meta,hash+".torrent"));
        var job=new PcDownloadJob{Service="Torrents",Name=item.Label,Target=item,Root=media,Destination=destination,Hash=hash,FileId=0,RemoteName="Show.S01E01.mkv",Size=3*1024*1024,Alternatives=alternative?[new(dead,0,"Other.S01E01.mkv",3*1024*1024)]:[]};await File.WriteAllTextAsync(Path.Combine(store.DataDirectory,"pc-downloads.json"),JsonSerializer.Serialize(new[]{job}));
        var downloads=new PcDownloads(store,fixture,torrentOptions:new(){Network=false,MetadataStall=TimeSpan.FromMilliseconds(550),DownloadStall=TimeSpan.FromMilliseconds(550),PollInterval=TimeSpan.FromMilliseconds(50)});
        try
        {
            await Until(()=>downloads.Jobs().Single().Status=="Failed",8,"no-peer stall resolves",downloads);var result=downloads.Jobs().Single();
            check(result.Status=="Failed"&&result.Message=="No one is sharing this right now. Pick another release.","No-peer torrent fails with a friendly actionable message");
            check(alternative?result.Switches==1&&result.Hash==dead:result.Switches==0,"Torrent stall "+(alternative?"switches once to an untried alternative":"does not switch without alternatives"));
            if(alternative)check(!Directory.Exists(Path.Combine(media,".noir-incoming",hash)),"Abandoned torrent partial directory is removed");
        }
        finally{downloads.Dispose();await downloads.TorrentStopped;}
    }
    static (LibraryStore Store,string Media,VideoItem Item) Library(string dir)
    {
        var media=Path.Combine(dir,"shows");Directory.CreateDirectory(media);var store=new LibraryStore(Path.Combine(dir,"profile"));store.State.Catalog.Clear();store.State.Roots=[new(){Path=media}];store.State.Language="en";var item=new VideoItem{Id="torrent-target",CollectionKey="show:torrent",Title="Show",Year=2026,Season=1,Episode=1,ImdbId="tt123",Available=false};store.State.Catalog[item.Id]=new(){Item=item};store.Save();return(store,media,item);
    }
    static async Task Until(Func<bool> done,int seconds,string label,PcDownloads downloads)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(seconds));while(!done()){if(timeout.IsCancellationRequested)throw new Exception("Torrent timeout: "+label+" "+JsonSerializer.Serialize(downloads.Jobs()));await Task.Delay(50);}
    }
    sealed class Fixture(string hash,List<TorboxFile> files):IDownloadProvider
    {
        public Task<List<IndexedRelease>> Search(VideoItem item,CancellationToken ct)=>Task.FromResult(new List<IndexedRelease>{new(new(){Id=Guid.NewGuid().ToString("N"),Name="Show.S01.1080p",SizeKind="Pack",Size=files.Sum(f=>f.Size),Health="Good",Seeders=12,CacheStatus="Good"},hash){Service="Torrents"}});
        public Task<List<TorboxFile>> Files(string hash,CancellationToken ct)=>Task.FromResult(files);
        public Task<TorboxTorrent> Prepare(string hash,long id,CancellationToken ct)=>throw new Exception("Torrents must never call a cloud provider");
        public Task<string> Link(long id,long file,CancellationToken ct)=>throw new Exception("Torrents must never request an HTTP download");
    }
    sealed class LoopbackTracker : IDisposable
    {
        readonly TcpListener listener=new(IPAddress.Loopback,0);
        readonly CancellationTokenSource stop=new();
        readonly byte[] response;
        public string Url {get;}
        public LoopbackTracker(int seedPort)
        {
            listener.Start();Url=$"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/announce";
            var bytes=new BEncodedDictionary{["interval"]=new BEncodedNumber(1),["peers"]=new BEncodedString(new byte[]{127,0,0,1,(byte)(seedPort>>8),(byte)seedPort})}.Encode();
            response=System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n").Concat(bytes).ToArray();_=Serve();
        }
        async Task Serve()
        {
            try{while(!stop.IsCancellationRequested){using var client=await listener.AcceptTcpClientAsync(stop.Token);using var stream=client.GetStream();var buffer=new byte[8192];await stream.ReadAsync(buffer,stop.Token);await stream.WriteAsync(response,stop.Token);}}
            catch(Exception e)when(e is OperationCanceledException or ObjectDisposedException or SocketException or IOException){}
        }
        public void Dispose(){stop.Cancel();listener.Stop();}
    }
    sealed class IndexHandler(string hash):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var streams=new[]{12,-1,5,0}.Select((n,i)=>new{infoHash=new string((char)('a'+i),40),fileIdx=0,name="Show.1080p",description=n<0?"No count":"[NOIR_SEEDERS:"+n+"]",url="https://cdn.invalid/video"+i,behaviorHints=new{filename="Show.S01E01.mkv",videoSize=1024}});return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{streams}))});
        }
    }
}
