using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Noir;

// Stream Auto-sync: the loopback proxy, the ffmpeg sampler and SyncStream, against a fake upstream on loopback that serves a real short fixture.
static class StreamSamplingChecks
{
    const string Secret="UPSTREAM-SECRET-TOKEN";
    sealed class Upstream:IDisposable
    {
        readonly TcpListener listener=new(IPAddress.Loopback,0);readonly string file;readonly CancellationTokenSource stop=new();
        public string Valid="good";public bool NoRange,Hang,Overshoot,ForbidAll;
        public readonly List<string> Ranges=[];public int Requests,Full200,Forbidden;public long Sent;public string UserAgent="",Referer="";
        public long Limit;public long Length=>Limit>0?Limit:new FileInfo(file).Length;public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;
        public Upstream(string file){this.file=file;listener.Start();_=Task.Run(Loop);}
        public string Url(string token="good")=>$"http://127.0.0.1:{Port}/{Secret}/video.mkv?t={token}";
        async Task Loop(){try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token);_=Task.Run(()=>Serve(client));}}catch{}}
        async Task Serve(TcpClient client)
        {
            using(client)try{
                var stream=client.GetStream();var buffer=new byte[8192];var length=0;string text="";
                while(!text.Contains("\r\n\r\n")){var n=await stream.ReadAsync(buffer.AsMemory(length),stop.Token);if(n==0)return;length+=n;text=Encoding.ASCII.GetString(buffer,0,length);}
                var lines=text.Split("\r\n");var path=lines[0].Split(' ')[1];
                lock(this){Requests++;UserAgent=Header(lines,"user-agent");Referer=Header(lines,"referer");}
                async Task Reply(string status,string extra=""){await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n{extra}\r\n"));}
                var redirect=Regex.Match(path,@"^/redir(\d+)$");
                if(redirect.Success){var left=int.Parse(redirect.Groups[1].Value);await Reply("302 Found","Location: "+(left>1?$"/redir{left-1}":$"/{Secret}/video.mkv?t={Valid}")+"\r\n");return;}
                if(path.StartsWith("/inner")){await Reply("302 Found","Location: http://inner.test:"+Port+$"/{Secret}/video.mkv?t={Valid}\r\n");return;}
                if(ForbidAll||path!=$"/{Secret}/video.mkv?t={Valid}"){lock(this)Forbidden++;await Reply("403 Forbidden");return;}
                if(Hang){await Task.Delay(Timeout.Infinite,stop.Token);return;}
                var range=Header(lines,"range");lock(this)Ranges.Add(range);
                var total=Length;long start=0,end=total-1;var partial=false;
                var m=Regex.Match(range,@"bytes=(\d+)-(\d*)");
                if(m.Success&&!NoRange){partial=true;start=long.Parse(m.Groups[1].Value);if(m.Groups[2].Length>0)end=Math.Min(end,long.Parse(m.Groups[2].Value));if(Overshoot)end=Math.Min(total-1,end+5000);}
                else lock(this)Full200++;
                var head=partial?$"HTTP/1.1 206 Partial Content\r\nContent-Range: bytes {start}-{end}/{total}\r\n":"HTTP/1.1 200 OK\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head+$"Content-Type: video/x-matroska\r\nAccept-Ranges: bytes\r\nContent-Length: {end-start+1}\r\nConnection: close\r\n\r\n"));
                if(lines[0].StartsWith("HEAD"))return;
                await using var source=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);source.Position=start;var chunk=new byte[65536];var remaining=end-start+1;
                while(remaining>0){var n=await source.ReadAsync(chunk.AsMemory(0,(int)Math.Min(remaining,chunk.Length)),stop.Token);if(n==0)break;await stream.WriteAsync(chunk.AsMemory(0,n),stop.Token);lock(this)Sent+=n;remaining-=n;}
            }catch{}
        }
        static string Header(string[] lines,string name)=>lines.Skip(1).Select(l=>l.Split(':',2)).Where(p=>p.Length==2&&p[0].Equals(name,StringComparison.OrdinalIgnoreCase)).Select(p=>p[1].Trim()).FirstOrDefault()??"";
        public void Dispose(){stop.Cancel();listener.Stop();}
    }
    static StreamOption Option(Upstream up,string token="good",string name="Show.S01E01.1080p.WEB.mkv")=>new(up.Url(token),name,"","1080p",up.Length,true,"","UA-fake/1.0","https://referrer.example/");
    static readonly HttpClient Direct=new(new SocketsHttpHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(60)};
    static async Task<(HttpResponseMessage? Response,byte[] Body)> Ask(string url,string? range,HttpMethod? method=null)
    {
        using var request=new HttpRequestMessage(method??HttpMethod.Get,url);if(range!=null)request.Headers.TryAddWithoutValidation("Range",range);
        HttpResponseMessage response;try{response=await Direct.SendAsync(request,HttpCompletionOption.ResponseHeadersRead);}catch(HttpRequestException){return (null,[]);}
        try{var bytes=await response.Content.ReadAsByteArrayAsync();return (response,bytes);}catch(Exception e)when(e is HttpRequestException or IOException){return (response,[]);}
    }
    static int TempFolders()=>Directory.GetDirectories(Path.GetTempPath(),"noir-stream-sync-*").Length;
    // On demand, on real audio: NOIR_STREAM_SYNC_VIDEO, NOIR_STREAM_SYNC_SUB, NOIR_STREAM_SYNC_DURATION, NOIR_SUBTITLE_TOOLS. Works on a copy of the subtitle.
    public static async Task RunReal(Action<bool,string> check,string video,string subtitle,double duration,string tools)
    {
        var previous=SubtitleDownloads.ToolRoot;SubtitleDownloads.ToolRoot=tools;
        var folder=Path.Combine(Directory.GetCurrentDirectory(),"work","subtitle-round","real-stream",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            using var upstream=new Upstream(video);var copy=Path.Combine(folder,"video."+SubtitleDownloads.SidecarLanguage(subtitle)+Path.GetExtension(subtitle));File.Copy(subtitle,copy);var before=File.ReadAllBytes(copy);
            var engine=new SubtitleDownloads(folder);var clock=Stopwatch.StartNew();
            var result=await engine.SyncStream(new StreamOption(upstream.Url(),"Real","","",upstream.Length,false,"","",""),[copy],duration,0,null,(i,n)=>Console.WriteLine($"sample {i}/{n}"),CancellationToken.None,new StreamSyncSettings{TrustLocal=true});
            Console.WriteLine($"RESULT: {result.Message} · corrected {result.Corrected.Count} · relayed {upstream.Sent/1048576} MiB of {upstream.Length/1048576} MiB · {upstream.Requests} requests · {clock.Elapsed.TotalSeconds:F0} s");
            check(result.Corrected.Count==1,"A real film streamed through the proxy is corrected on real audio");
            check(!File.ReadAllBytes(copy).SequenceEqual(before),"The corrected subtitle replaces the copy");
            check(upstream.Sent<upstream.Length*0.35,"Only a fraction of the film was read");
        }finally{SubtitleDownloads.ToolRoot=previous;try{Directory.Delete(folder,true);}catch(IOException){}}
    }
    public static async Task Run(string root,Action<bool,string> check)
    {
        var cwd=Directory.GetCurrentDirectory();var fixture=Path.Combine(cwd,"work","tv-speed","media","i-avc-aac-srt.mkv");var tools=Path.Combine(cwd,"work","subtitle-runtime");
        if(!File.Exists(fixture)||!File.Exists(Path.Combine(tools,"ffmpeg","ffmpeg.exe"))){check(true,"Stream sampling checks skipped: fixture or bundled ffmpeg is missing");return;}
        var profile=Path.Combine(root,"stream-sampling-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(profile);
        var previousTools=SubtitleDownloads.ToolRoot;SubtitleDownloads.ToolRoot=tools;
        try
        {
            // The proxy itself.
            using(var up=new Upstream(fixture))
            using(var proxy=new StreamSampleProxy(Option(up),0,null,true){BudgetFloor=400L*1024*1024})
            {
                var (r,body)=await Ask(proxy.LocalUrl,"bytes=1000-1999");
                check(r!.StatusCode==HttpStatusCode.PartialContent&&body.Length==1000&&r.Content.Headers.ContentRange is {From:1000,To:1999}&&r.Content.Headers.ContentRange.Length==up.Length&&up.Ranges.Last()=="bytes=1000-1999","Proxy forwards a range and answers 206 with a matching Content-Range");
                check(up.UserAgent=="UA-fake/1.0"&&up.Referer=="https://referrer.example/","Proxy sends the stream's own User-Agent and Referrer upstream");
                (r,body)=await Ask(proxy.LocalUrl,"bytes=100-");
                check(r!.StatusCode==HttpStatusCode.PartialContent&&body.Length==StreamSampleProxy.Window&&r.Content.Headers.ContentRange is {From:100}&&r.Content.Headers.ContentRange.To==100+StreamSampleProxy.Window-1&&up.Ranges.Last()==$"bytes=100-{100+StreamSampleProxy.Window-1}","An open-ended range is bounded to a 16 MiB window, upstream and in the reply");
                (r,body)=await Ask(proxy.LocalUrl,null);
                check(r!.StatusCode==HttpStatusCode.PartialContent&&body.Length==StreamSampleProxy.Window&&up.Ranges.Last().StartsWith("bytes=0-"),"A request without Range is also bounded");
                (r,body)=await Ask(proxy.LocalUrl,"bytes=0-"+(up.Length-1)+"",HttpMethod.Head);
                check(r!.StatusCode==HttpStatusCode.PartialContent&&body.Length==0&&r.Content.Headers.ContentLength==StreamSampleProxy.Window,"HEAD reports the bounded window and no body");
                var path=new Uri(proxy.LocalUrl).AbsolutePath;var origin=new Uri(proxy.LocalUrl).GetLeftPart(UriPartial.Authority);var before=up.Requests;
                var statuses=new List<HttpStatusCode>();
                foreach(var odd in new[]{origin+"/",origin+path+"/",origin+path+"?x=1",origin+"/"+new string('a',48),origin+"/video.mkv"})statuses.Add((await Ask(odd,"bytes=0-9")).Response!.StatusCode);
                statuses.Add((await Ask(proxy.LocalUrl,"bytes=0-9",HttpMethod.Post)).Response!.StatusCode);statuses.Add((await Ask(proxy.LocalUrl,"bytes=0-9",HttpMethod.Delete)).Response!.StatusCode);
                check(statuses.All(s=>s==HttpStatusCode.NotFound)&&up.Requests==before,"Only GET and HEAD of the exact random path are served; everything else is 404 and never reaches upstream");
                check(new Uri(proxy.LocalUrl).Host=="127.0.0.1"&&path.Length>=48&&!proxy.LocalUrl.Contains(Secret),"The local address is loopback with an unguessable path and carries nothing of the real link");
            }
            using(var up=new Upstream(fixture){Overshoot=true})
            using(var proxy=new StreamSampleProxy(Option(up),0,null,true))
            {
                var (r,body)=await Ask(proxy.LocalUrl,"bytes=0-99");
                check(r!.StatusCode==HttpStatusCode.PartialContent&&body.Length==100&&r.Content.Headers.ContentRange is {From:0,To:99},"The proxy never sends more than the requested end, even when upstream does");
            }
            using(var up=new Upstream(fixture){NoRange=true})
            using(var proxy=new StreamSampleProxy(Option(up),0,null,true))
            {
                var (r,body)=await Ask(proxy.LocalUrl,"bytes=0-99");
                check(r!.StatusCode==HttpStatusCode.BadGateway&&body.Length==0&&proxy.Failure==SampleFailure.NoSeeking&&proxy.BytesRelayed==0,"A 200 to a ranged request stops the job without reading the body");
            }
            using(var up=new Upstream(fixture){Limit=1_000_000})
            using(var proxy=new StreamSampleProxy(Option(up),0,null,true){BudgetFloor=100_000})
            {
                var (r,body)=await Ask(proxy.LocalUrl,"bytes=0-999999");
                check(proxy.Failure==SampleFailure.Budget&&body.Length<=350_000+65536&&proxy.BytesRelayed>=350_000&&proxy.Budget==350_000,"The data budget counts relayed bytes, cuts the transfer and stops the job");
                (r,_)=await Ask(proxy.LocalUrl,"bytes=0-9");
                check(r==null||r.StatusCode==HttpStatusCode.ServiceUnavailable,"After a stop the proxy serves nothing more");
            }
            using(var up=new Upstream(fixture))
            using(var proxy=new StreamSampleProxy(Option(up),0,null,true){MaxRequests=3})
            {
                for(var i=0;i<3;i++)await Ask(proxy.LocalUrl,"bytes=0-9");
                var (r,_)=await Ask(proxy.LocalUrl,"bytes=0-9");
                check(r!.StatusCode==HttpStatusCode.ServiceUnavailable&&proxy.Failure==SampleFailure.Budget&&up.Requests==3,"The request-count cap stops the job");
            }
            using(var up=new Upstream(fixture))
            {
                using(var proxy=new StreamSampleProxy(Option(up),0,null,false)){var (r,_)=await Ask(proxy.LocalUrl,"bytes=0-9");check(r!.StatusCode==HttpStatusCode.BadGateway&&up.Requests==0,"A loopback upstream is refused unless the stream is Noir's own");}
                var asked=new List<string>();Task<IPAddress[]> Resolve(string host,CancellationToken ct){asked.Add(host);return Task.FromResult(new[]{IPAddress.Parse(host=="inner.test"?"10.0.0.5":"192.168.1.20")});}
                using(var proxy=new StreamSampleProxy(new StreamOption("http://cdn.example.test/file.mkv","a.mkv","","",0,true,"","",""),0,null,false,Resolve)){var (r,_)=await Ask(proxy.LocalUrl,"bytes=0-9");check(r!.StatusCode==HttpStatusCode.BadGateway&&asked.Contains("cdn.example.test"),"Hosts that resolve to private addresses are refused");}
                using(var redirected=new StreamSampleProxy(new StreamOption($"http://127.0.0.1:{up.Port}/inner","a.mkv","","",0,true,"","",""),0,null,true,(host,_)=>Task.FromResult(host=="inner.test"?new[]{IPAddress.Parse("10.0.0.5")}:new[]{IPAddress.Loopback}))){var (r,_)=await Ask(redirected.LocalUrl,"bytes=0-9");check(r!.StatusCode==HttpStatusCode.BadGateway&&up.Requests==1,"A redirect to a private address is refused at that hop");}
                foreach(var (hops,ok) in new[]{(3,true),(4,false)})
                {
                    using var proxy=new StreamSampleProxy(new StreamOption($"http://127.0.0.1:{up.Port}/redir{hops}","a.mkv","","",0,true,"","",""),0,null,true);
                    var (r,body)=await Ask(proxy.LocalUrl,"bytes=0-9");
                    check(ok?r!.StatusCode==HttpStatusCode.PartialContent&&body.Length==10:r!.StatusCode==HttpStatusCode.BadGateway,ok?"Three redirects are followed":"A fourth redirect is refused");
                }
                var scheme=false;try{using var _=new StreamSampleProxy(new StreamOption("file:///C:/secret.mkv","a","","",0,true,"","",""));}catch(ArgumentException){scheme=true;}
                try{using var _=new StreamSampleProxy(new StreamOption("ftp://host/a.mkv","a","","",0,true,"","",""));scheme=false;}catch(ArgumentException){}
                check(scheme,"Only http and https upstreams are accepted");
            }
            // Expired links: one refresh, same release.
            using(var up=new Upstream(fixture))
            {
                var calls=0;Task<StreamOption?> Refresh(CancellationToken ct){calls++;return Task.FromResult<StreamOption?>(Option(up,"good"));}
                up.Valid="good";using var proxy=new StreamSampleProxy(Option(up,"stale"),0,Refresh,true);
                var (r,body)=await Ask(proxy.LocalUrl,"bytes=0-9");
                check(r!.StatusCode==HttpStatusCode.PartialContent&&body.Length==10&&calls==1,"A 403 refreshes the link once and the request then succeeds");
                up.ForbidAll=true;(r,_)=await Ask(proxy.LocalUrl,"bytes=10-19");
                check(r!.StatusCode==HttpStatusCode.BadGateway&&calls==1&&proxy.Failure==SampleFailure.Expired,"A second expiry does not refresh again and ends the job as expired");
            }
            using(var up=new Upstream(fixture))
            {
                var calls=0;Task<StreamOption?> Refresh(CancellationToken ct){calls++;return Task.FromResult<StreamOption?>(null);}
                using var proxy=new StreamSampleProxy(Option(up,"stale"),0,Refresh,true);var (r,_)=await Ask(proxy.LocalUrl,"bytes=0-9");
                check(r!.StatusCode==HttpStatusCode.BadGateway&&calls==1&&proxy.Failure==SampleFailure.Expired,"No replacement for the same release means the link has expired");
            }
            // The sampler: real ffmpeg through the proxy.
            var ffmpeg=Path.Combine(tools,"ffmpeg","ffmpeg.exe");
            using(var up=new Upstream(fixture))
            using(var proxy=new StreamSampleProxy(Option(up),0,null,true))
            {
                var started=new List<List<string>>();var options=new StreamSampler.Options{Started=(_,args)=>started.Add(args.ToList())};
                var samples=await StreamSampler.Extract(ffmpeg,proxy,[(10,5),(300,5),(590,30)],0,null,CancellationToken.None,CancellationToken.None,options);
                check(samples.Count==2&&samples[0].Start==10&&samples.All(x=>x.Pcm.Length%2==0&&x.Pcm.Length is >150000 and <=160000),"Each window yields 16 kHz mono PCM of the expected size; a sample with under half its audio is dropped · "+string.Join(",",samples.Select(x=>x.Start+":"+x.Pcm.Length)));
                check(started.Count==3&&started.All(a=>a[^1]=="pipe:1"&&a.Contains("-nostdin")&&a.Contains("-hide_banner")&&a[a.IndexOf("-i")+1]==proxy.LocalUrl&&a[a.IndexOf("-map")+1]=="0:a:0"&&a[a.IndexOf("-rw_timeout")+1]=="10000000"),"ffmpeg gets the documented argument array with only the loopback address");
                check(started.All(a=>!a.Any(x=>x.Contains(Secret)||x.Contains($":{up.Port}/")||x.Contains("UA-fake")||x.Contains("referrer.example"))),"No process argument holds the upstream link, user agent or referrer");
                check(up.Ranges.Count>0&&up.Full200==0&&up.Sent<up.Length/3,"Sampling reads only ranges of the stream and a fraction of the file · "+up.Sent/1048576+" MiB of "+up.Length/1048576+" MiB over "+up.Requests+" requests");
                var duration=await StreamSampler.Probe(Path.Combine(tools,"ffmpeg","ffprobe.exe"),proxy,CancellationToken.None,CancellationToken.None);
                check(Math.Abs(duration-600)<1,"ffprobe through the proxy finds the stream length · "+duration);
            }
            // SyncStream with the real ffmpeg and a fake tool.
            var subtitleText="1\n00:00:05,000 --> 00:00:07,000\nOriginal line\n\n2\n00:00:09,000 --> 00:00:11,000\nSecond line\n";
            string Subtitle(string name,string text=""){var folder=Path.Combine(profile,name);Directory.CreateDirectory(folder);var path=Path.Combine(folder,"video.en.srt");File.WriteAllText(path,text.Length>0?text:subtitleText);return path;}
            Func<string,string?>? perFile=null;var calls2=new List<string>();var sampleCounts=new List<int>();string firstReason="applied";Func<CancellationToken,Task>? hook=null;double planSeen=0;
            SubtitleDownloads.ToolOverride=async(args,ct)=>{
                calls2.Add(args[0]);
                if(args[0]=="--plan"){planSeen=double.Parse(args[1],System.Globalization.CultureInfo.InvariantCulture);return (0,"""{"windows":[[60,8],[200,8],[330,8],[450,8]],"extra":[[130,8],[265,8],[390,8]]}""");}
                using var job=JsonDocument.Parse(await File.ReadAllTextAsync(args[1],ct));var samples=job.RootElement.GetProperty("samples");sampleCounts.Add(samples.GetArrayLength());
                foreach(var sample in samples.EnumerateArray())if(new FileInfo(sample.GetProperty("pcm").GetString()!).Length<8*16000)throw new Exception("short sample");
                if(hook!=null)await hook(ct);
                var reason=perFile?.Invoke(args[2])??(firstReason=="two-step"?(samples.GetArrayLength()>4?"applied":"too-little-speech"):firstReason);
                if(reason!="applied"){File.Copy(args[2],args[3],true);return (0,JsonSerializer.Serialize(new{accepted=false,offset=0,scale=1,method="none",reason}));}
                File.WriteAllText(args[3],"1\n00:00:06,230 --> 00:00:08,230\nSynced line\n\n2\n00:00:10,230 --> 00:00:12,230\nSecond\n");
                return (0,JsonSerializer.Serialize(new{accepted=true,offset=1.23,scale=1.0,method="sampled",reason="applied"}));
            };
            var settings=new StreamSyncSettings{TrustLocal=true};
            using(var up=new Upstream(fixture))
            {
                var engine=new SubtitleDownloads(profile);var sub=Subtitle("applied");var started=new List<IReadOnlyList<string>>();var folders=TempFolders();var progress=new List<string>();
                var result=await engine.SyncStream(Option(up),[sub],0,0,null,(i,n)=>progress.Add(i+"/"+n),CancellationToken.None,new StreamSyncSettings{TrustLocal=true,Started=(_,a)=>started.Add(a)});
                check(result.Message=="English · auto-synced (+1.23s)"&&result.Corrected.SequenceEqual([sub])&&File.ReadAllText(sub).Contains("Synced line"),"A stream sync applies the accepted correction · "+result.Message);
                var backup=Directory.GetFiles(Path.Combine(profile,"subtitle-originals"));
                check(backup.Length==1&&File.ReadAllText(backup[0])==subtitleText,"The original subtitle is backed up once");
                check(Math.Abs(planSeen-600)<1&&calls2.SequenceEqual(["--plan","--sampled"])&&sampleCounts.SequenceEqual([4]),"With no known length ffprobe supplies it; the plan runs, then one sampled run with all four samples");
                check(progress.Count==4&&progress[0]=="1/4"&&progress[3]=="4/4","Progress counts the samples taken · "+string.Join(",",progress));
                check(started.Count>=5&&started.All(a=>!a.Any(x=>x.Contains(Secret)||x.Contains("UA-fake")))&&!result.Message.Contains(Secret),"Upstream details appear in no process argument or message");
                check(up.Full200==0&&up.Ranges.All(r=>r.StartsWith("bytes="))&&TempFolders()==folders,"Every upstream request was ranged and the temp folder is gone");
                File.WriteAllText(sub,subtitleText);await engine.SyncStream(Option(up),[sub],600,0,null,null,CancellationToken.None,settings);
                check(File.ReadAllText(Directory.GetFiles(Path.Combine(profile,"subtitle-originals"))[0])==subtitleText,"A second correction keeps the first backup");
            }
            using(var up=new Upstream(fixture))
            {
                var engine=new SubtitleDownloads(profile);var sub=Subtitle("twostep");firstReason="two-step";sampleCounts.Clear();
                var result=await engine.SyncStream(Option(up),[sub],600,0,null,null,CancellationToken.None,settings);
                check(result.Message.Contains("auto-synced")&&sampleCounts.SequenceEqual([4,7]),"Too little speech in the first samples adds the extra windows once and runs again with all samples · "+string.Join(",",sampleCounts));
                firstReason="too-little-speech";sampleCounts.Clear();File.WriteAllText(sub,subtitleText);
                result=await engine.SyncStream(Option(up),[sub],600,0,null,null,CancellationToken.None,settings);
                check(result.Message=="English · too little speech to check the timing"&&File.ReadAllText(sub)==subtitleText&&sampleCounts.SequenceEqual([4,7]),"Still too little speech leaves the subtitle alone · "+result.Message);
                foreach(var reason in new[]{"no-agreement","conflicting-sample","unstable"}){
                    firstReason=reason;result=await engine.SyncStream(Option(up),[sub],600,0,null,null,CancellationToken.None,settings);
                    check(result.Message=="English · samples disagree · timing uncertain · original kept"&&result.Corrected.Count==0&&File.ReadAllText(sub)==subtitleText,"Disagreeing samples ("+reason+") keep the original");
                }
                firstReason="already-aligned";result=await engine.SyncStream(Option(up),[sub],600,0,null,null,CancellationToken.None,settings);
                check(result.Message=="English · timing already matches"&&File.ReadAllText(sub)==subtitleText,"Matching timing is reported plainly");
                firstReason="applied";
            }
            using(var up=new Upstream(fixture))
            {
                var engine=new SubtitleDownloads(profile);var sub=Subtitle("partial");var arabic=Path.Combine(Path.GetDirectoryName(sub)!,"video.ar.srt");File.WriteAllText(arabic,subtitleText);
                perFile=p=>{if(p.EndsWith(".ar.srt",StringComparison.Ordinal)){up.ForbidAll=true;return "too-little-speech";}return "applied";};
                var result=await engine.SyncStream(Option(up),[sub,arabic],600,0,null,null,CancellationToken.None,settings);perFile=null;
                check(result.Corrected.SequenceEqual([sub])&&result.Message.Contains("English · auto-synced")&&result.Message.Contains("Arabic · ")&&File.ReadAllText(arabic)==subtitleText,"A failure during extra listening keeps earlier corrections reported · "+result.Message);
            }
            using(var up=new Upstream(fixture))
            {
                var engine=new SubtitleDownloads(profile);var sub=Subtitle("both");var arabic=Path.Combine(Path.GetDirectoryName(sub)!,"video.ar.srt");File.WriteAllText(arabic,subtitleText.Replace("Original","\u0645\u0631\u062d\u0628\u0627"));sampleCounts.Clear();
                var result=await engine.SyncStream(Option(up),[sub,arabic],600,0,null,null,CancellationToken.None,settings);
                check(result.Corrected.Count==2&&result.Message.Split('\n').Length==2&&sampleCounts.SequenceEqual([4,4])&&up.Requests<60,"English and Arabic are both corrected from the same extracted audio · "+up.Requests+" upstream requests");
                check(Directory.GetFiles(Path.GetDirectoryName(sub)!).All(f=>!Path.GetFileName(f).StartsWith("~")),"No temporary subtitle is left in the release folder");
            }
            // Failures end the job plainly.
            using(var up=new Upstream(fixture){NoRange=true})
            {
                var engine=new SubtitleDownloads(profile);var sub=Subtitle("noseek");var result=await engine.SyncStream(Option(up),[sub],600,0,null,null,CancellationToken.None,settings);
                check(result.Message=="Stream doesn't support seeking · original kept"&&File.ReadAllText(sub)==subtitleText&&up.Full200>=1&&up.Sent<8*1024*1024,"A stream that ignores ranges stops with a plain message and its body is never read · "+result.Message+" · "+up.Sent/1024+" KiB buffered by the socket");
            }
            using(var up=new Upstream(fixture))
            {
                var engine=new SubtitleDownloads(profile);var sub=Subtitle("budget");var result=await engine.SyncStream(Option(up),[sub],600,0,null,null,CancellationToken.None,new StreamSyncSettings{TrustLocal=true,BudgetFloor=200_000,BudgetShare=.0001});
                check(result.Message=="data limit reached · original kept"&&File.ReadAllText(sub)==subtitleText,"Exceeding the data budget stops the job with its own message · "+result.Message);
                var capped=await engine.SyncStream(Option(up),[sub],600,0,null,null,CancellationToken.None,new StreamSyncSettings{TrustLocal=true,MaxRequests=1});
                check(capped.Message=="data limit reached · original kept","Exceeding the request cap stops the job too · "+capped.Message);
            }
            using(var up=new Upstream(fixture))
            {
                var engine=new SubtitleDownloads(profile);var sub=Subtitle("expired");var refreshes=0;
                var result=await engine.SyncStream(Option(up,"stale"),[sub],600,0,ct=>{refreshes++;return Task.FromResult(new List<StreamOption>{Option(up,"good","Show.S01E01.2160p.BluRay.mkv")});},null,CancellationToken.None,settings);
                check(result.Message=="link expired · original kept"&&refreshes==1&&up.Ranges.Count==0,"A refreshed link for a different release is refused, so no samples are mixed · "+result.Message);
                result=await engine.SyncStream(Option(up,"stale"),[sub],600,0,ct=>{refreshes++;return Task.FromResult(new List<StreamOption>{Option(up,"good","Other.mkv"),Option(up,"good")});},null,CancellationToken.None,settings);
                check(result.Message=="English · auto-synced (+1.23s)"&&refreshes==2,"A refreshed link for the same release continues the job · "+result.Message);
            }
            // Cancel and time limits against an upstream that never answers.
            using(var up=new Upstream(fixture){Hang=true})
            {
                var engine=new SubtitleDownloads(profile);var sub=Subtitle("cancel");var folders=TempFolders();var children=new List<int>();
                using var cancel=new CancellationTokenSource();var run=engine.SyncStream(Option(up),[sub],600,0,null,null,cancel.Token,new StreamSyncSettings{TrustLocal=true,Started=(p,_)=>children.Add(p.Id)});
                for(var i=0;i<100&&children.Count==0;i++)await Task.Delay(50);var watched=Stopwatch.StartNew();cancel.Cancel();
                var cancelled=false;try{await run;}catch(OperationCanceledException){cancelled=true;}
                check(cancelled&&watched.Elapsed<TimeSpan.FromSeconds(5)&&children.Count==1,"Cancelling stops the job promptly · "+watched.ElapsedMilliseconds+" ms");
                await Task.Delay(300);
                check(children.All(id=>{try{using var p=Process.GetProcessById(id);return p.HasExited||p.ProcessName!="ffmpeg";}catch(ArgumentException){return true;}})&&File.ReadAllText(sub)==subtitleText&&TempFolders()==folders&&Directory.GetFiles(Path.GetDirectoryName(sub)!).Length==1,"Cancelling kills ffmpeg and leaves no temp folder or partial subtitle");
                var timed=Stopwatch.StartNew();var late=await engine.SyncStream(Option(up),[sub],600,0,null,null,CancellationToken.None,new StreamSyncSettings{TrustLocal=true,PerSample=TimeSpan.FromSeconds(1),Overall=TimeSpan.FromSeconds(2.5)});
                check(late.Message=="timing check timed out · original kept"&&timed.Elapsed<TimeSpan.FromSeconds(15)&&File.ReadAllText(sub)==subtitleText&&TempFolders()==folders,"The overall time limit ends a stalled job plainly · "+late.Message+" after "+timed.ElapsedMilliseconds+" ms");
            }
            using(var up=new Upstream(fixture))
            {
                var engine=new SubtitleDownloads(profile);var sub=Subtitle("lateCancel");using var cancel=new CancellationTokenSource();hook=_=>{cancel.Cancel();return Task.CompletedTask;};
                var cancelled=false;try{await engine.SyncStream(Option(up),[sub],600,0,null,null,cancel.Token,settings);}catch(OperationCanceledException){cancelled=true;}hook=null;
                check(cancelled&&File.ReadAllText(sub)==subtitleText&&Directory.GetFiles(Path.GetDirectoryName(sub)!).Length==1,"A correction ready at the moment of cancelling is never committed");
            }
            SubtitleDownloads.ToolOverride=null;
            // The real frozen tool: plan, then sampled on real samples.
            if(File.Exists(Path.Combine(tools,"NoirSubtitleSync.exe")))
            {
                using var up=new Upstream(fixture);var engine=new SubtitleDownloads(profile);var sub=Subtitle("real");
                var result=await engine.SyncStream(Option(up),[sub],600,0,null,null,CancellationToken.None,settings);
                check(result.Message=="stream too short to sample · original kept"&&up.Requests==0,"The real tool declines a stream under 12 minutes before reading anything");
                using var proxy=new StreamSampleProxy(Option(up),0,null,true);var samples=await StreamSampler.Extract(ffmpeg,proxy,[(60,30),(180,30),(300,30),(420,30)],0,null,CancellationToken.None,CancellationToken.None);
                var folder=Path.Combine(profile,"real-job");Directory.CreateDirectory(folder);var entries=new List<object>();
                for(var i=0;i<samples.Count;i++){var pcm=Path.Combine(folder,i+".pcm");File.WriteAllBytes(pcm,samples[i].Pcm);entries.Add(new{start=samples[i].Start,pcm});}
                File.WriteAllText(Path.Combine(folder,"job.json"),JsonSerializer.Serialize(new{samples=entries,duration=7200}));
                var info=new ProcessStartInfo(Path.Combine(tools,"NoirSubtitleSync.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var a in new[]{"--sampled",Path.Combine(folder,"job.json"),sub,Path.Combine(folder,"out.srt")})info.ArgumentList.Add(a);
                using var process=Process.Start(info)!;var json=await process.StandardOutput.ReadToEndAsync();_=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
                using var parsed=JsonDocument.Parse(json.Trim());
                check(samples.Count==4&&process.ExitCode==0&&parsed.RootElement.TryGetProperty("reason",out var why)&&why.GetString()!="error"&&File.Exists(Path.Combine(folder,"out.srt"))&&!json.Contains(Secret),"The frozen tool runs --sampled on real ffmpeg samples and prints one result line · "+json.Trim());
            }
        }
        finally{SubtitleDownloads.ToolOverride=null;SubtitleDownloads.ToolRoot=previousTools;try{Directory.Delete(profile,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
