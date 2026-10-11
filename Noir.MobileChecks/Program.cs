using Noir;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

var root=Path.Combine(Directory.GetCurrentDirectory(),"NoirValidation","android-sync");Directory.CreateDirectory(root);
var dir=Path.Combine(root,"fixture-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(dir);
// Read-only copy of the personal catalog for realistic posters/video decoding. Never touch its store or accounts.
var source=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".noir");
File.Copy(Path.Combine(source,"library.json"),Path.Combine(dir,"library.json"));
Directory.CreateDirectory(Path.Combine(dir,"art"));foreach(var image in Directory.EnumerateFiles(Path.Combine(source,"art"))) File.Copy(image,Path.Combine(dir,"art",Path.GetFileName(image)));
var store=new LibraryStore(dir);store.State.SyncTrakt=false;store.State.SyncSerializd=false;store.State.WatchSync.Clear();store.Save();
var smallRoot=Path.Combine(dir,"transfer-fixture");Directory.CreateDirectory(smallRoot);var smallFile=Path.Combine(smallRoot,"Download fixture (2026).mp4");File.WriteAllBytes(smallFile,Enumerable.Range(0,2*1024*1024).Select(n=>(byte)(n%251)).ToArray());store.State.Roots.Add(new(){Path=smallRoot,Movies=true});store.Scan(out _);
File.WriteAllText(Path.ChangeExtension(smallFile,".en.srt"),"1\n00:00:01,000 --> 00:00:03,000\nNoir subtitle fixture\n");
var commandItem=new VideoItem{Id="pc-download-fixture",CollectionKey="fixture-command",Title="PC command fixture",Season=1,Episode=1,ImdbId="tt123",Available=false};store.State.Catalog[commandItem.Id]=new(){Item=commandItem};store.Record(commandItem,421.625,1800);
var subtitleFixture=Path.Combine(root,"subtitle-fixture");
if(Directory.Exists(subtitleFixture)){store.State.Roots.Add(new(){Path=subtitleFixture,Movies=true});store.Scan(out _);}
store.State.Roots.Add(new(){Path=smallRoot,Movies=false});using var downloads=new PcDownloads(store,new CommandProvider(),start:false);downloads.Configure("https://example.invalid/fixture/manifest.json","fixture-key-not-real",smallRoot,smallRoot);
var useDefaultPort=args.Contains("--default-port");
var port=useDefaultPort?MobileSyncServer.Port:49272;
await using var host=new MobileSyncServer(store,store.MobileSnapshot,store.MergeMobile,port,downloads);await host.SetEnabled(true);
if(!host.Running) throw new Exception(host.Status);
var checks=new List<string>();void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);Console.WriteLine("PASS: "+label);}
using var handler=new HttpClientHandler {ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert!=null&&Convert.ToHexString(SHA256.HashData(cert.RawData))==host.Pin};
using var client=new HttpClient(handler){BaseAddress=new($"https://127.0.0.1:{port}")};
Check((await client.GetAsync("/v1/library")).StatusCode==HttpStatusCode.Unauthorized,"Unpaired clients cannot read library or videos");
Check((await client.GetAsync("/v1/pc-downloads")).StatusCode==HttpStatusCode.Unauthorized&&(await client.GetAsync("/v1/subtitles/any/any")).StatusCode==HttpStatusCode.Unauthorized,"Download commands and subtitles require phone authentication");
var link=host.BeginPairing("127.0.0.1");var query=System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);
Check(query["address"]==$"127.0.0.1:{port}","Pairing link advertises the listening port");
Check((await client.PostAsJsonAsync("/v1/pair",new{code="00000000",name="test"})).StatusCode==HttpStatusCode.Unauthorized,"Wrong pairing codes are rejected");
var paired=await client.PostAsJsonAsync("/v1/pair",new{code=query["code"],name="transport test"});Check(paired.IsSuccessStatusCode,"Valid one-time pairing succeeds over pinned HTTPS");
var result=await paired.Content.ReadFromJsonAsync<JsonElement>();client.DefaultRequestHeaders.Authorization=new("Bearer",result.GetProperty("token").GetString());
if(useDefaultPort)
{
    using var legacyHandler=new HttpClientHandler {ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert!=null&&Convert.ToHexString(SHA256.HashData(cert.RawData))==host.Pin};
    using var legacyClient=new HttpClient(legacyHandler){BaseAddress=new($"https://127.0.0.1:{MobileSyncServer.LegacyPort}")};
    Check((await legacyClient.GetAsync("/v1/library")).StatusCode==HttpStatusCode.Unauthorized,"Legacy port still requires phone authentication");
    legacyClient.DefaultRequestHeaders.Authorization=client.DefaultRequestHeaders.Authorization;
    Check((await legacyClient.GetAsync("/v1/library")).IsSuccessStatusCode,"Existing phone tokens work on the legacy port with the same pinned certificate");
}
Check((await client.PostAsJsonAsync("/v1/pair",new{code=query["code"],name="replay"})).StatusCode==HttpStatusCode.Unauthorized,"Consumed pairing code cannot be reused");
var response=await client.GetStringAsync("/v1/library");Check(!response.Contains("C:\\")&&!response.Contains("watchSync",StringComparison.OrdinalIgnoreCase),"HTTP snapshot does not leak local paths or provider accounts");
var sync=await client.PostAsJsonAsync("/v1/sync",new List<MobileOperation>());var synced=await sync.Content.ReadFromJsonAsync<JsonElement>();var syncedItem=synced.GetProperty("library").GetProperty("items").EnumerateArray().Single(i=>i.GetProperty("id").GetString()==commandItem.Id);Check(syncedItem.GetProperty("progress").GetProperty("position").GetDouble()==421.625,"Empty phone sync receives the exact newer PC bookmark");
var subtitleItem=store.Archive.First(i=>i.Path==smallFile);var sub=MobileSubtitles.Find(subtitleItem).Single();Check((await client.GetStringAsync("/v1/subtitles/"+subtitleItem.Id+"/"+sub.Info.Id)).Contains("Noir subtitle fixture"),"Paired phones retrieve the matching external subtitle");Check((await client.GetAsync("/v1/subtitles/"+subtitleItem.Id+"/unknown")).StatusCode==HttpStatusCode.NotFound,"Unknown subtitle IDs cannot expose arbitrary files");
var subtitleInfo=await client.GetFromJsonAsync<JsonElement>("/v1/subtitle-tools/"+subtitleItem.Id);Check(subtitleInfo.GetProperty("hash").GetString()==await SubtitleDownloads.VideoHash(smallFile,CancellationToken.None)&&subtitleInfo.GetProperty("files").GetArrayLength()==1,"Subtitle identity uses the exact video and a fresh sidecar list");
Check((await client.GetAsync("/v1/subtitle-tools/unknown")).StatusCode==HttpStatusCode.NotFound,"Unknown subtitle-video IDs cannot expose PC filenames");
Check((await client.PostAsJsonAsync("/v1/subtitle-tools/"+subtitleItem.Id+"/sync",new MobileSubtitleSyncInput{Hash="0000000000000000",Text="1\n00:00:01,000 --> 00:00:03,000\nTest subtitle",Extension=".srt"})).StatusCode==HttpStatusCode.BadRequest,"Timing checks reject a phone copy with a different video fingerprint");
var releases=await client.GetFromJsonAsync<JsonElement>("/v1/pc-downloads/search/"+commandItem.Id+"?season=true");var releaseId=releases.GetProperty("choices")[0].GetProperty("id").GetString()!;var fileResponse=await client.GetFromJsonAsync<JsonElement>("/v1/pc-downloads/files/"+releaseId);var fileId=fileResponse.GetProperty("files")[0].GetProperty("id").GetString()!;var command=new DownloadRequest{RequestId=Guid.NewGuid().ToString(),ReleaseId=releaseId,FileIds=[fileId]};var queued=await client.PostAsJsonAsync("/v1/pc-downloads",command);var receipt=await queued.Content.ReadFromJsonAsync<JsonElement>();var jobId=receipt.GetProperty("ids")[0].GetString()!;Check(queued.IsSuccessStatusCode&&downloads.Jobs().Single().Status=="Queued","Paired phone can select an opaque release and queue an episode on PC");await client.PostAsJsonAsync("/v1/pc-downloads",command);Check(downloads.Jobs().Count==1,"Retried HTTP download requests do not duplicate jobs");Check((await client.PostAsJsonAsync("/v1/pc-downloads/control",new{id=jobId,action="pause"})).IsSuccessStatusCode&&downloads.Jobs().Single().Status=="Paused","Phone can pause a persisted PC download");var queue=await client.GetStringAsync("/v1/pc-downloads");Check(!queue.Contains("fixture-key")&&!queue.Contains("example.invalid")&&!queue.Contains(smallRoot,StringComparison.OrdinalIgnoreCase),"HTTP download queue keeps provider credentials and PC paths private");Check((await client.PostAsJsonAsync("/v1/pc-downloads",new DownloadRequest{ReleaseId="unknown",FileIds=["unknown"]})).StatusCode==HttpStatusCode.BadRequest,"Unknown release and file IDs cannot schedule arbitrary downloads");
var video=store.Archive.First(i=>i.Available&&File.Exists(i.Path));
using var range=new HttpRequestMessage(HttpMethod.Get,"/v1/media/"+video.Id);range.Headers.Range=new(100,199);using var bytes=await client.SendAsync(range);
Check(bytes.StatusCode==HttpStatusCode.PartialContent&&(await bytes.Content.ReadAsByteArrayAsync()).Length==100,"PC streaming supports exact byte ranges for seeking");
Check(bytes.Headers.ETag!=null,"Video responses carry a file identity for resumable downloads");
using(var expired=new HttpRequestMessage(HttpMethod.Get,"/v1/media/"+video.Id)){expired.Headers.Range=new(100,199);expired.Headers.TryAddWithoutValidation("If-Range","\"outdated-video\"");using var restart=await client.SendAsync(expired,HttpCompletionOption.ResponseHeadersRead);Check(restart.StatusCode==HttpStatusCode.OK&&restart.Content.Headers.ContentLength==new FileInfo(video.Path).Length,"An obsolete video identity restarts a transfer instead of mixing two files");}
var actualBytes=await bytes.Content.ReadAsByteArrayAsync();
using(var input=File.OpenRead(video.Path)){input.Position=100;var expected=new byte[100];input.ReadExactly(expected);Check(expected.SequenceEqual(actualBytes),"Range bytes match the real file");}
Check((await client.GetAsync("/v1/media/unknown")).StatusCode==HttpStatusCode.NotFound,"Media endpoint serves only known catalog IDs");
var art=store.MobileSnapshot().Items.First(i=>i.Poster.Length>0).Poster;Check((await client.GetAsync("/v1/art/"+art)).IsSuccessStatusCode,"Cached poster is served to the paired phone");
host.Revoke();Check((await client.GetAsync("/v1/library")).StatusCode==HttpStatusCode.Unauthorized,"Disconnect immediately revokes the phone token");
File.WriteAllText(Path.Combine(root,"transport-checks.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions {WriteIndented=true}));
if(args.Contains("--once")) return;
// Separate single-use fixture credential for the emulator. It cannot access personal Noir or real accounts.
File.WriteAllText(Path.Combine(root,"pairing-uri.txt"),host.BeginPairing("10.0.2.2"));
File.WriteAllText(Path.Combine(root,"fixture-path.txt"),dir);
File.WriteAllText(Path.Combine(root,"video-id.txt"),video.Id);
Console.WriteLine("FIXTURE READY");
await Task.Delay(TimeSpan.FromHours(2));
sealed class CommandProvider : IDownloadProvider
{
    public Task<List<IndexedRelease>> Search(VideoItem i,CancellationToken ct)=>Task.FromResult(Enumerable.Range(1,150).Select(n=>new IndexedRelease(new(){Id=Guid.NewGuid().ToString("N"),Name="Fixture · "+(n%2==0?"1080p":"2160p")+" · "+n,Size=8192,Cached=true,CacheStatus="Ready",Languages="English",Resolution=n%2==0?"1080p":"2160p",SourceEntries=2,Sources=["Fixture source"]},n.ToString("x40"))).ToList());
    public Task<List<TorboxFile>> Files(string hash,CancellationToken ct)=>Task.FromResult(new List<TorboxFile>{new(1,"PC.command.fixture.S01E01.mkv",8192)});
    public Task<TorboxTorrent> Prepare(string hash,long id,CancellationToken ct)=>throw new NotSupportedException("Fixture worker disabled");
    public Task<string> Link(long id,long file,CancellationToken ct)=>throw new NotSupportedException("Fixture worker disabled");
}
