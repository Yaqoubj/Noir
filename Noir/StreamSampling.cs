using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Noir;
// Auto-sync on a stream listens to a few short audio samples. ffmpeg never sees the stream's link or headers: it reads
// a loopback address that only this process answers, and the proxy relays small ranges of the one real stream.
internal enum SampleFailure {None,NoSeeking,Budget,Expired,TimedOut,Unavailable}
internal sealed class StreamSampleException(SampleFailure kind):IOException(StreamSampler.Message(kind)){public SampleFailure Kind{get;}=kind;}
internal sealed record SampleAudio(double Start,byte[] Pcm);
internal sealed record StreamSyncResult(string Message,List<string> Corrected);
internal sealed class StreamSampleProxy:IDisposable
{
    public const int Window=16*1024*1024;
    public long BudgetFloor{get;init;}=400L*1024*1024;
    public double BudgetShare{get;init;}=.35;
    public int MaxRequests{get;init;}=400;
    readonly TcpListener listener=new(IPAddress.Loopback,0);
    readonly string secret=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    readonly HttpClient http;readonly bool trustLocal;readonly Func<CancellationToken,Task<StreamOption?>>? refresh;
    readonly CancellationTokenSource stop=new();readonly object gate=new();
    StreamOption upstream;Uri target;bool refreshed;long total;long relayed;int requests;SampleFailure failure;
    public StreamSampleProxy(StreamOption stream,long knownSize=0,Func<CancellationToken,Task<StreamOption?>>? refresh=null,bool trustLocal=false,Func<string,CancellationToken,Task<IPAddress[]>>? resolve=null)
    {
        if(!Remote(stream.Url,out var uri))throw new ArgumentException("Only http and https streams can be sampled.");
        upstream=stream;target=uri;total=Math.Max(0,knownSize);this.refresh=refresh;this.trustLocal=trustLocal;
        http=new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false,AutomaticDecompression=DecompressionMethods.None,ConnectCallback=(context,ct)=>Connect(context.DnsEndPoint,ct,resolve)}){Timeout=Timeout.InfiniteTimeSpan};
        listener.Start();_=Task.Run(Accept);
    }
    static bool Remote(string value,out Uri uri)=>Uri.TryCreate(value,UriKind.Absolute,out uri!)&&uri.Scheme is "http" or "https"&&uri.UserInfo.Length==0&&uri.Host.Length>0;
    // The only address ffmpeg is ever given.
    public string LocalUrl=>$"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/{secret}";
    public CancellationToken Stopped=>stop.Token;
    public SampleFailure Failure{get{lock(gate)return failure;}}
    public long BytesRelayed=>Interlocked.Read(ref relayed);
    public int Requests=>Volatile.Read(ref requests);
    public long Budget{get{lock(gate)return Math.Max(BudgetFloor,(long)(total*BudgetShare));}}
    public long TotalSize{get{lock(gate)return total;}}
    void Fail(SampleFailure kind){lock(gate){if(failure!=SampleFailure.None)return;failure=kind;}try{stop.Cancel();}catch(ObjectDisposedException){}}
    // Same rule as subtitle downloads: public addresses only; a stream served by Noir itself may use loopback.
    async ValueTask<Stream> Connect(DnsEndPoint endpoint,CancellationToken ct,Func<string,CancellationToken,Task<IPAddress[]>>? resolve)
    {
        var addresses=await (resolve??((host,token)=>Dns.GetHostAddressesAsync(host,token)))(endpoint.Host,ct);
        if(addresses.Length==0||addresses.Any(a=>!SubtitleTransport.PublicAddress(a)&&!(trustLocal&&IPAddress.IsLoopback(a))))throw new IOException("Connection refused.");
        foreach(var address in addresses){var socket=new Socket(address.AddressFamily,SocketType.Stream,ProtocolType.Tcp);try{await socket.ConnectAsync(new IPEndPoint(address,endpoint.Port),ct);return new NetworkStream(socket,true);}catch{socket.Dispose();if(ct.IsCancellationRequested)throw;}}
        throw new IOException("Connection unavailable.");
    }
    async Task Accept()
    {
        try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token);_=Task.Run(()=>Handle(client));}}catch{}
        // A stopped job accepts nothing more: ffmpeg's next connection is simply refused.
        try{listener.Stop();}catch(SocketException){}
    }
    static async Task Reply(Stream stream,string status,string extra="")
    {
        var text=$"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n{extra}\r\n";
        try{await stream.WriteAsync(Encoding.ASCII.GetBytes(text));}catch(IOException){}
    }
    static async Task<string?> ReadHead(Stream stream,CancellationToken ct)
    {
        var buffer=new byte[16384];var length=0;using var limit=CancellationTokenSource.CreateLinkedTokenSource(ct);limit.CancelAfter(TimeSpan.FromSeconds(10));
        while(length<buffer.Length){var n=await stream.ReadAsync(buffer.AsMemory(length),limit.Token);if(n==0)return null;length+=n;var text=Encoding.ASCII.GetString(buffer,0,length);if(text.Contains("\r\n\r\n"))return text;}
        return null;
    }
    async Task Handle(TcpClient client)
    {
        using(client)try{
            client.NoDelay=true;var stream=client.GetStream();var head=await ReadHead(stream,stop.Token);if(head==null)return;
            var lines=head.Split("\r\n");var first=lines[0].Split(' ');
            if(first.Length<2||first[0] is not ("GET" or "HEAD")||first[1]!="/"+secret){await Reply(stream,"404 Not Found");return;}
            long? from=null,to=null;var suffix=false;
            var range=lines.Skip(1).Select(l=>Regex.Match(l,@"^range:\s*bytes=(\d*)-(\d*)\s*$",RegexOptions.IgnoreCase)).FirstOrDefault(m=>m.Success);
            if(range!=null){
                if(range.Groups[1].Length>0){from=long.Parse(range.Groups[1].Value);if(range.Groups[2].Length>0)to=long.Parse(range.Groups[2].Value);}
                else if(range.Groups[2].Length>0){suffix=true;to=long.Parse(range.Groups[2].Value);}
            }
            if(Failure!=SampleFailure.None||stop.IsCancellationRequested){await Reply(stream,"503 Service Unavailable");return;}
            if(Interlocked.Increment(ref requests)>MaxRequests){Fail(SampleFailure.Budget);await Reply(stream,"503 Service Unavailable");return;}
            if(Interlocked.Read(ref relayed)>=Budget){Fail(SampleFailure.Budget);await Reply(stream,"503 Service Unavailable");return;}
            long start=suffix?0:from??0;long end;
            if(suffix)end=Math.Min(to!.Value,Window);else{end=start+Window-1;if(to!=null&&to<end)end=to.Value;}
            if(end<start&&!suffix){await Reply(stream,"416 Range Not Satisfiable");return;}
            var size=TotalSize;if(size>0&&!suffix){if(start>=size){await Reply(stream,"416 Range Not Satisfiable","Content-Range: bytes */"+size+"\r\n");return;}end=Math.Min(end,size-1);}
            var wanted=suffix?$"bytes=-{end}":$"bytes={start}-{(head.StartsWith("HEAD")?start:end)}";
            HttpResponseMessage? response=null;
            try{
                response=await Fetch(wanted,stop.Token);
                if(response==null){await Reply(stream,"502 Bad Gateway");return;}
                if(response.StatusCode!=HttpStatusCode.PartialContent){
                    // A plain 200 answers a ranged request with the whole file; never read it.
                    if(response.StatusCode==HttpStatusCode.OK)Fail(SampleFailure.NoSeeking);
                    await Reply(stream,response.StatusCode==HttpStatusCode.RequestedRangeNotSatisfiable?"416 Range Not Satisfiable":"502 Bad Gateway");return;
                }
                var content=response.Content.Headers.ContentRange;
                if(content?.From==null||content.To==null||content.To<content.From||(!suffix&&content.From!=start)){Fail(SampleFailure.NoSeeking);await Reply(stream,"502 Bad Gateway");return;}
                var first0=content.From.Value;var head1=head.StartsWith("HEAD");var last=suffix?content.To.Value:head1?(content.Length is {} all&&all>0?Math.Min(end,all-1):Math.Min(content.To.Value,end)):Math.Min(content.To.Value,end);
                if(content.Length is {} length&&length>0)lock(gate)total=length;
                var count=last-first0+1;var totalText=content.Length is {} known?known.ToString(CultureInfo.InvariantCulture):"*";
                var type=response.Content.Headers.ContentType?.ToString()??"application/octet-stream";if(type.Contains('\r')||type.Contains('\n'))type="application/octet-stream";
                var header=$"HTTP/1.1 206 Partial Content\r\nContent-Type: {type}\r\nAccept-Ranges: bytes\r\nContent-Range: bytes {first0}-{last}/{totalText}\r\nContent-Length: {count}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header),stop.Token);
                if(head1)return;
                await using var body=await response.Content.ReadAsStreamAsync(stop.Token);var buffer=new byte[65536];var left=count;
                while(left>0){
                    var n=await body.ReadAsync(buffer.AsMemory(0,(int)Math.Min(left,buffer.Length)),stop.Token);if(n==0)break;
                    if(Interlocked.Add(ref relayed,n)>Budget){Fail(SampleFailure.Budget);break;}
                    await stream.WriteAsync(buffer.AsMemory(0,n),stop.Token);left-=n;
                }
            }finally{response?.Dispose();}
        }catch{}
    }
    // One ranged GET, following at most three redirects. Expired links (401/403/410) get one retry from the original
    // link, then one refresh; a refresh never happens twice.
    async Task<HttpResponseMessage?> Fetch(string range,CancellationToken ct)
    {
        for(var attempt=0;attempt<3;attempt++){
            StreamOption current;Uri uri;lock(gate){current=upstream;uri=target;}
            HttpResponseMessage? response=null;
            try{
                for(var hop=0;hop<=3;hop++){
                    using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.TryAddWithoutValidation("Range",range);
                    if(current.UserAgent.Length>0)request.Headers.TryAddWithoutValidation("User-Agent",current.UserAgent);
                    if(current.Referrer.Length>0)request.Headers.TryAddWithoutValidation("Referer",current.Referrer);
                    using var limit=CancellationTokenSource.CreateLinkedTokenSource(ct);limit.CancelAfter(TimeSpan.FromSeconds(30));
                    response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,limit.Token);
                    if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308){
                        var location=response.Headers.Location;response.Dispose();response=null;
                        if(hop==3||location==null)return null;
                        uri=new Uri(uri,location);if(!Remote(uri.AbsoluteUri,out uri!))return null;continue;
                    }
                    break;
                }
            }catch(Exception e)when(e is HttpRequestException or IOException or OperationCanceledException or UriFormatException or InvalidOperationException){response?.Dispose();return null;}
            if(response==null)return null;
            if((int)response.StatusCode is 401 or 403 or 410){
                response.Dispose();
                bool again;lock(gate){
                    if(target.AbsoluteUri!=upstream.Url&&Uri.TryCreate(upstream.Url,UriKind.Absolute,out var original)){target=original;again=true;}
                    else again=false;
                }
                if(again)continue;
                if(refresh==null||refreshed){Fail(SampleFailure.Expired);return null;}
                lock(gate)refreshed=true;
                StreamOption? replacement=null;try{replacement=await refresh(ct);}catch(Exception e)when(e is IOException or HttpRequestException or OperationCanceledException){}
                if(replacement==null||!Remote(replacement.Url,out var fresh)){Fail(SampleFailure.Expired);return null;}
                lock(gate){upstream=replacement;target=fresh;}
                continue;
            }
            // Later ranges can go straight to the address this one ended up at.
            if(response.StatusCode==HttpStatusCode.PartialContent&&response.RequestMessage?.RequestUri is {} final&&Remote(final.AbsoluteUri,out var kept))lock(gate)target=kept;
            return response;
        }
        Fail(SampleFailure.Expired);return null;
    }
    public void Dispose()
    {
        try{stop.Cancel();}catch(ObjectDisposedException){}
        try{listener.Stop();}catch(SocketException){}
        http.Dispose();stop.Dispose();
    }
}
// Everything a stream sync can be tuned with; the defaults are the real limits, tests lower them.
internal sealed class StreamSyncSettings
{
    public long BudgetFloor{get;init;}=400L*1024*1024;
    public double BudgetShare{get;init;}=.35;
    public int MaxRequests{get;init;}=400;
    public TimeSpan PerSample{get;init;}=TimeSpan.FromSeconds(45);
    public TimeSpan Overall{get;init;}=TimeSpan.FromMinutes(6);
    public bool TrustLocal{get;init;}
    public Func<string,CancellationToken,Task<IPAddress[]>>? Resolve{get;init;}
    public Action<Process,IReadOnlyList<string>>? Started{get;init;}
    public string? FfmpegFolder{get;init;}
}
internal static class StreamSampler
{
    public sealed class Options
    {
        public TimeSpan PerSample{get;init;}=TimeSpan.FromSeconds(45);
        public Action<Process,IReadOnlyList<string>>? Started{get;init;}
    }
    public static string Message(SampleFailure kind)=>kind switch{SampleFailure.NoSeeking=>"Stream doesn't support seeking",SampleFailure.Budget=>"data limit reached",SampleFailure.Expired=>"link expired",SampleFailure.TimedOut=>"timed out",_=>"stream unavailable"};
    static void Kill(Process process){try{if(!process.HasExited)process.Kill(true);}catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception){}}
    static ProcessStartInfo Start(string exe,IEnumerable<string> arguments)
    {
        var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=false,WorkingDirectory=Path.GetDirectoryName(exe)!};
        foreach(var argument in arguments)info.ArgumentList.Add(argument);return info;
    }
    // One sample at a time. Throws OperationCanceledException when the user cancels (ct), StreamSampleException for a
    // failure that ends the whole job; a single sample that fails or runs too long is just left out.
    public static async Task<List<SampleAudio>> Extract(string ffmpeg,StreamSampleProxy proxy,IReadOnlyList<(double Start,double Length)> windows,int audioIndex,Action<int,int>? progress,CancellationToken ct,CancellationToken overall,Options? options=null)
    {
        options??=new();var result=new List<SampleAudio>();
        for(var i=0;i<windows.Count;i++){
            ct.ThrowIfCancellationRequested();
            if(overall.IsCancellationRequested)throw new StreamSampleException(SampleFailure.TimedOut);
            progress?.Invoke(i+1,windows.Count);
            var (start,length)=windows[i];var expected=(int)(length*32000)/2*2;
            var arguments=new List<string>{"-hide_banner","-loglevel","error","-nostdin","-rw_timeout","10000000","-ss",start.ToString("0.###",CultureInfo.InvariantCulture),"-t",length.ToString("0.###",CultureInfo.InvariantCulture),"-i",proxy.LocalUrl,
                "-map","0:a:"+Math.Max(0,audioIndex),"-vn","-sn","-dn","-ac","1","-ar","16000","-c:a","pcm_s16le","-f","s16le","pipe:1"};
            using var one=CancellationTokenSource.CreateLinkedTokenSource(ct,overall,proxy.Stopped);one.CancelAfter(options.PerSample);
            var pcm=new byte[expected];var got=0;
            using var process=new Process{StartInfo=Start(ffmpeg,arguments)};
            try{
                process.Start();options.Started?.Invoke(process,arguments);
                using var reaper=one.Token.Register(()=>Kill(process));
                try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception){}
                _=process.StandardError.BaseStream.CopyToAsync(Stream.Null);
                var output=process.StandardOutput.BaseStream;
                while(got<expected){var n=await output.ReadAsync(pcm.AsMemory(got,expected-got),one.Token);if(n==0)break;got+=n;}
                Kill(process);
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch(Exception e)when(e is OperationCanceledException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException){}
            finally{Kill(process);}
            ct.ThrowIfCancellationRequested();
            if(proxy.Failure!=SampleFailure.None)throw new StreamSampleException(proxy.Failure);
            if(overall.IsCancellationRequested)throw new StreamSampleException(SampleFailure.TimedOut);
            if(got>=expected/2)result.Add(new(start,pcm.AsSpan(0,got/2*2).ToArray()));
        }
        return result;
    }
    // The stream's length, when the player doesn't know it: one ffprobe through the same proxy.
    public static async Task<double> Probe(string ffprobe,StreamSampleProxy proxy,CancellationToken ct,CancellationToken overall,Options? options=null)
    {
        options??=new();using var one=CancellationTokenSource.CreateLinkedTokenSource(ct,overall,proxy.Stopped);one.CancelAfter(options.PerSample);
        var arguments=new List<string>{"-v","error","-rw_timeout","10000000","-show_entries","format=duration","-of","csv=p=0",proxy.LocalUrl};
        using var process=new Process{StartInfo=Start(ffprobe,arguments)};
        try{
            process.Start();options.Started?.Invoke(process,arguments);using var reaper=one.Token.Register(()=>Kill(process));
            _=process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            var text=await process.StandardOutput.ReadToEndAsync(one.Token);await process.WaitForExitAsync(one.Token);
            ct.ThrowIfCancellationRequested();if(proxy.Failure!=SampleFailure.None)throw new StreamSampleException(proxy.Failure);
            return double.TryParse(text.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds)&&double.IsFinite(seconds)&&seconds>0?seconds:0;
        }
        catch(Exception e)when(e is OperationCanceledException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception){ct.ThrowIfCancellationRequested();if(proxy.Failure!=SampleFailure.None)throw new StreamSampleException(proxy.Failure);return 0;}
        finally{Kill(process);}
    }
}
