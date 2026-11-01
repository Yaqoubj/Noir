using Noir;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

static class BackendAuditChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var dir=Path.Combine(root,"backend-audit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        foreach(var primary in new[]{"{ broken library","null"})
        {
            var folder=Path.Combine(dir,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var path=Path.Combine(folder,"library.json");
            File.WriteAllText(path,primary,Encoding.UTF8);File.WriteAllText(path+".bak","{ broken backup",Encoding.UTF8);var original=File.ReadAllBytes(path);var backup=File.ReadAllBytes(path+".bak");
            var failed=new LibraryStore(folder);failed.State.Volume=12;failed.Save();failed.SaveDeferred();failed.Scan(out _);await failed.FlushPendingAsync();
            check(File.ReadAllBytes(path).SequenceEqual(original)&&File.ReadAllBytes(path+".bak").SequenceEqual(backup)&&!File.Exists(path+".tmp"),"Audit: unreadable primary and backup survive synchronous, deferred and scan saves unchanged");
            check(failed.LoadWarning.Contains("disabled")&&!failed.LoadWarning.Contains("Recovered")&&Directory.GetFiles(folder,"*.unreadable-*").Length==2,"Audit: failed recovery warns that saving is blocked and preserves diagnostic copies");
        }
        var recoveredDir=Path.Combine(dir,"recovered");Directory.CreateDirectory(recoveredDir);var statePath=Path.Combine(recoveredDir,"library.json");
        File.WriteAllText(statePath,"{ broken primary");var good=JsonSerializer.SerializeToUtf8Bytes(new PlayerState{Volume=37});File.WriteAllBytes(statePath+".bak",good);
        var recovered=new LibraryStore(recoveredDir);check(recovered.State.Volume==37&&recovered.LoadWarning.Contains("Recovered"),"Audit: recovery is reported only after loading the valid backup");
        recovered.State.Volume=48;recovered.Save();check(File.ReadAllBytes(statePath+".bak").SequenceEqual(good)&&new LibraryStore(recoveredDir).State.Volume==48,"Audit: first save after recovery keeps the good backup and commits the recovered library");
        var fresh=new LibraryStore(Path.Combine(dir,"fresh"));fresh.State.Volume=31;fresh.Save();check(new LibraryStore(fresh.DataDirectory).State.Volume==31&&fresh.LoadWarning.Length==0,"Audit: a genuinely new library remains writable without a recovery warning");
        var item=new VideoItem{Id="audit",Title="Audit",CollectionKey="audit",Season=1,Episode=1,ImdbId="tt123",Available=false};fresh.State.Catalog[item.Id]=new(){Item=item};
        fresh.EpisodesFor(item.GroupKey);var index=typeof(LibraryStore).GetField("groupIndex",BindingFlags.Instance|BindingFlags.NonPublic)!;var before=index.GetValue(fresh);fresh.Record(item,15,120);fresh.EpisodesFor(item.GroupKey);
        check(ReferenceEquals(before,index.GetValue(fresh)),"Audit: saving playback progress retains the unchanged catalog index");fresh.ApplyDetails(item.GroupKey,null);check(fresh.MetadataRecentlyChecked(item.GroupKey),"Audit: unsuccessful metadata lookup records an independent cooldown");
        var handler=new DetailHandler();using(var cast=new CreditsClient(dir,handler:handler))using(var trailers=new TrailersClient(dir,handler:handler))
        {await Task.WhenAll(cast.Title("tt123",true),trailers.Resolve("tt123",true));check(handler.Calls==1,"Audit: concurrent cast and trailer lookups share the first Cinemeta request");}
        var subtitleHandler=new SubtitleFailure();var subtitles=new SubtitleDownloads(dir,new HttpClient(subtitleHandler));var target=SubtitleDownloads.StreamTarget(dir,item,"test");
        var message=await subtitles.Fetch(item,"test",["en","ar"],false,CancellationToken.None,streamTarget:target);
        check(subtitleHandler.Calls==1&&message.Contains("English lookup unavailable")&&message.Contains("Arabic lookup unavailable"),"Audit: both subtitle languages reuse a failed language-independent lookup");
        var media=Path.Combine(dir,"media");Directory.CreateDirectory(media);fresh.State.Roots=[new(){Path=media}];fresh.Save();var bytes=Enumerable.Range(0,8192).Select(n=>(byte)n).ToArray();var transferHandler=new RetryTransfer(bytes);
        using var manager=new PcDownloads(fresh,transfer:new HttpClient(transferHandler),start:false);var job=new PcDownloadJob{Root=media,Destination=Path.Combine(media,"Audit.S01E01.mkv"),Target=item,Size=bytes.Length};
        await manager.Transfer(job,"https://fixture.invalid/file",CancellationToken.None);
        check(transferHandler.Starts.SequenceEqual(new long[]{0,512})&&File.ReadAllBytes(job.Destination).SequenceEqual(bytes),"Audit: transient body read failure retries through verified ETag/range resume");
        var bounded=new RetryTransfer(bytes){AlwaysFail=true};using var failedManager=new PcDownloads(fresh,transfer:new HttpClient(bounded),start:false);var failedJob=new PcDownloadJob{Root=media,Destination=Path.Combine(media,"Fail.S01E01.mkv"),Target=new(){Id="fail",Title="Fail",Season=1,Episode=1},Size=bytes.Length};bool refused=false;
        try{await failedManager.Transfer(failedJob,"https://fixture.invalid/file",CancellationToken.None);}catch(IOException){refused=true;}
        check(refused&&bounded.Starts.Count==3&&!File.Exists(failedJob.Destination),"Audit: transient transfer retries stop after three attempts without publishing an incomplete video");
        await V2AuditChecks.Run(dir,check);
    }
    sealed class DetailHandler:HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Interlocked.Increment(ref Calls);await Task.Delay(100,ct);return new(HttpStatusCode.OK){Content=new StringContent("{\"meta\":{\"cast\":[\"Person\"],\"trailers\":[]}}")};}
    }
    sealed class SubtitleFailure:HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));}
    }
    sealed class RetryTransfer(byte[] bytes):HttpMessageHandler
    {
        public List<long> Starts=[];public bool AlwaysFail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var start=request.Headers.Range?.Ranges.Single().From??0;Starts.Add(start);var fail=AlwaysFail||Starts.Count==1;var content=fail?new StreamContent(new BrokenBody(bytes[(int)start..],start==0?512:0)):new StreamContent(new MemoryStream(bytes[(int)start..]));content.Headers.ContentLength=bytes.Length-start;
            var response=new HttpResponseMessage(start>0?HttpStatusCode.PartialContent:HttpStatusCode.OK){Content=content};response.Headers.ETag=new EntityTagHeaderValue("\"audit\"");if(start>0)content.Headers.ContentRange=new ContentRangeHeaderValue(start,bytes.Length-1,bytes.Length);return Task.FromResult(response);
        }
    }
    sealed class BrokenBody(byte[] bytes,int available):MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){if(Position>=available)throw new IOException("Transient fixture failure");return base.ReadAsync(buffer[..Math.Min(buffer.Length,available-(int)Position)],ct);}
    }
}
