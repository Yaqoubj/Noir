using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Noir;
public sealed class MobilePeer {public string Name {get;set;}="Android";public string Hash {get;set;}="";public DateTimeOffset PairedAt {get;set;}}
public sealed class MobileHostConfig {public bool Enabled {get;set;}=true;public List<MobilePeer> Peers {get;set;}=[];public DateTimeOffset? LastSyncAt {get;set;}}
public sealed class MobileSyncServer : IAsyncDisposable
{
    public const int Port=443;
    public const int LegacyPort=49271;
    static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};
    readonly LibraryStore store;
    readonly Func<MobileSnapshot> snapshot;
    readonly Func<List<MobileOperation>,List<string>> merge;
    readonly object gate=new();
    readonly string configPath;
    readonly X509Certificate2 certificate;
    readonly int port;
    readonly PcDownloads? downloads;
    readonly Dictionary<string,(int Count,DateTimeOffset Since)> attempts=[];
    WebApplication? app;
    string pairCode="";
    DateTimeOffset pairExpires;
    public MobileHostConfig Config {get;}
    public string Status {get;private set;}="Starting…";
    public bool Running=>app!=null;
    public string Pin=>Convert.ToHexString(SHA256.HashData(certificate.RawData));
    public MobileSyncServer(LibraryStore store,Func<MobileSnapshot> snapshot,Func<List<MobileOperation>,List<string>> merge,int port=Port,PcDownloads? downloads=null)
    {
        this.store=store;this.snapshot=snapshot;this.merge=merge;this.port=port;this.downloads=downloads;
        configPath=Path.Combine(store.DataDirectory,"mobile-host.json");
        if(File.Exists(configPath))
        {
            // Never replace saved pairing data merely because a scanner briefly locks the file.
            for(var attempt=0;;attempt++)
            try {Config=JsonSerializer.Deserialize<MobileHostConfig>(File.ReadAllText(configPath),Json)??throw new InvalidDataException("Stored phone pairing data is empty.");break;}
            catch(IOException) when(attempt<4) {Thread.Sleep(40*(attempt+1));}
        }
        else Config=new();
        var certPath=Path.Combine(store.DataDirectory,"mobile-certificate.dpapi");
        if(File.Exists(certPath)) certificate=X509CertificateLoader.LoadPkcs12(ProtectedData.Unprotect(File.ReadAllBytes(certPath),null,DataProtectionScope.CurrentUser),null);
        else
        {
            using var rsa=RSA.Create(2048);var req=new CertificateRequest("CN=Noir local sync",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
            using var issued=req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(10));
            var pfx=issued.Export(X509ContentType.Pfx);
            certificate=X509CertificateLoader.LoadPkcs12(pfx,null);
            File.WriteAllBytes(certPath,ProtectedData.Protect(pfx,null,DataProtectionScope.CurrentUser));
        }
    }
    public static List<string> Addresses()=>NetworkInterface.GetAllNetworkInterfaces()
        .Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback&&n.Name.IndexOf("vEthernet",StringComparison.OrdinalIgnoreCase)<0)
        .SelectMany(n=>n.GetIPProperties().UnicastAddresses).Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork&&IsLocal(a.Address)&&!IPAddress.IsLoopback(a.Address))
        .Select(a=>a.Address.ToString()).Distinct().ToList();
    static bool IsLocal(IPAddress? ip)
    {
        if(ip==null) return false;if(ip.IsIPv4MappedToIPv6) ip=ip.MapToIPv4();if(IPAddress.IsLoopback(ip)) return true;
        var b=ip.GetAddressBytes();return b.Length==4&&(b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1]>=16&&b[1]<=31);
    }
    public string BeginPairing(string address)
    {
        lock(gate) {pairCode=RandomNumberGenerator.GetInt32(10000000,100000000).ToString();pairExpires=DateTimeOffset.UtcNow.AddMinutes(10);attempts.Clear();
        return $"noir://pair?address={Uri.EscapeDataString(address+":"+port)}&code={pairCode}&pin={Pin}";}
    }
    public void EndPairing() {lock(gate) pairCode="";}
    public void Revoke() {lock(gate) {Config.Peers.Clear();pairCode="";SaveConfig();}}
    void SaveConfig()
    {
        var temp=configPath+".tmp";
        using(var file=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) {JsonSerializer.Serialize(file,Config,Json);file.Flush(true);}
        for(var attempt=0;;attempt++)
        try {File.Move(temp,configPath,true);break;}
        catch(IOException) when(attempt<4) {Thread.Sleep(40*(attempt+1));}
    }
    public async Task SetEnabled(bool enabled)
    {
        lock(gate) {Config.Enabled=enabled;SaveConfig();if(!enabled) pairCode="";}
        if(!enabled) {if(app!=null) {var old=app;app=null;await old.StopAsync();await old.DisposeAsync();}Status="Off";return;}
        if(app!=null) return;
        WebApplication? host=null;
        try
        {
            var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions {Args=[],ContentRootPath=store.DataDirectory});
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o=>
            {
                o.Limits.MaxRequestBodySize=1024*1024;
                o.ListenAnyIP(port,l=>l.UseHttps(certificate));
                // Previously paired phones retain their address and certificate pin.
                if(port==Port) o.ListenAnyIP(LegacyPort,l=>l.UseHttps(certificate));
            });
            host=builder.Build();
            host.Use(async(ctx,next)=>
            {
                if(!IsLocal(ctx.Connection.RemoteIpAddress)) {ctx.Response.StatusCode=403;return;}
                ctx.Response.Headers.CacheControl="no-store";
                if(ctx.Request.Path!="/v1/pair")
                {
                    var auth=ctx.Request.Headers.Authorization.ToString();
                    if(!auth.StartsWith("Bearer ",StringComparison.Ordinal)||!Authorized(auth[7..])) {ctx.Response.StatusCode=401;return;}
                }
                await next(ctx);
            });
            host.MapPost("/v1/pair",async(HttpContext ctx)=>
            {
                var input=await ctx.Request.ReadFromJsonAsync<PairInput>(Json);
                string token;
                lock(gate)
                {
                    var address=ctx.Connection.RemoteIpAddress!.ToString();var failed=attempts.GetValueOrDefault(address);
                    if(DateTimeOffset.UtcNow-failed.Since>TimeSpan.FromMinutes(1)) failed=(0,DateTimeOffset.UtcNow);
                    attempts[address]=(failed.Count+1,failed.Since);
                    if(failed.Count>=10) return Results.StatusCode(429);
                    if(input==null||pairCode.Length==0||DateTimeOffset.UtcNow>pairExpires||!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(input.Code),Encoding.UTF8.GetBytes(pairCode))) return Results.Unauthorized();
                    token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                    if(Config.Peers.Count>=16) return Results.BadRequest();
                    Config.Peers.Add(new(){Name=input.Name[..Math.Min(input.Name.Length,80)],Hash=Hash(token),PairedAt=DateTimeOffset.UtcNow});pairCode="";SaveConfig();
                }
                return Results.Json(new {token,instance=snapshot().Instance},Json);
            });
            host.MapGet("/v1/library",()=>Results.Json(snapshot(),Json));
            host.MapPost("/v1/discover/save",async(DiscoverInput input,HttpContext ctx)=>
            {
                if(input.Type is not ("movie" or "series")||!System.Text.RegularExpressions.Regex.IsMatch(input.Id??"",@"^tt\d{1,16}$"))return Results.Json(new{error="Choose a movie or show from Discover."},Json,statusCode:400);
                try{
                    using var discovery=new DiscoveryClient(store.DataDirectory);
                    var title=await discovery.Details(new(){Id=input.Id!,Type=input.Type},ctx.RequestAborted);
                    if(!input.Watchlist&&!title.IsMovie&&!title.Episodes.Any(e=>e.Season==input.Season&&e.Episode==input.Episode&&(e.AirsAt==null||e.AirsAt<=DateTimeOffset.UtcNow)))return Results.Json(new{error="This episode hasn't been released, or its schedule has changed. Refresh title details."},Json,statusCode:400);
                    var group=store.AddDiscovered(title,input.Watchlist);
                    var item=input.Watchlist||title.IsMovie?group[0]:group.First(i=>i.Season==input.Season&&i.Episode==input.Episode);
                    Discovered?.Invoke();return Results.Json(new{id=item.Id,group=item.GroupKey},Json);
                }
                catch(OperationCanceledException)when(ctx.RequestAborted.IsCancellationRequested){return Results.StatusCode(499);}
                catch(Exception){return Results.Json(new{error="Couldn't load this title on the PC. Check its internet connection and try again. Windows Noir 0.12.1 or later is required."},Json,statusCode:503);}
            });
            async Task<IResult> DownloadApi(Func<Task<object>> action)
            {
                if(downloads==null)return Results.Json(new{error="PC downloads are unavailable. Update and configure Noir on the PC."},Json,statusCode:503);
                try{return Results.Json(await action(),Json);}catch(Exception){return Results.Json(new{error="Couldn't complete this request. Check PC download settings, connection, and whether these choices have expired."},Json,statusCode:400);}
            }
            host.MapGet("/v1/pc-downloads",()=>Results.Json(new{configured=downloads?.Configured??false,jobs=downloads?.PublicJobs()??[]},Json));
            host.MapGet("/v1/pc-downloads/search/{id}",(string id,bool? season,HttpContext ctx)=>DownloadApi(async()=>await downloads!.SearchDetailed(id,season==true,null,ctx.RequestAborted)));
            host.MapPost("/v1/pc-downloads/plan/{id}",(string id,DownloadPlanRequest input,HttpContext ctx)=>DownloadApi(async()=>await downloads!.Plan(id,input.ReleaseIds,ctx.RequestAborted)));
            host.MapPost("/v1/pc-downloads/match",(DownloadMappingRequest input)=>DownloadApi(()=>Task.FromResult<object>(downloads!.MapFile(input.FileId,input.Episode))));
            host.MapGet("/v1/pc-downloads/files/{id}",(string id,HttpContext ctx)=>DownloadApi(async()=>new{files=await downloads!.Files(id,ctx.RequestAborted)}));
            host.MapPost("/v1/pc-downloads",(DownloadRequest input)=>DownloadApi(()=>Task.FromResult<object>(new{ids=downloads!.Enqueue(input)})));
            host.MapPost("/v1/pc-downloads/control",(DownloadControl input)=>DownloadApi(()=>{downloads!.Control(input.Id,input.Action);return Task.FromResult<object>(new{ok=true});}));
            host.MapPost("/v1/sync",async(HttpContext ctx)=>
            {
                var ops=await ctx.Request.ReadFromJsonAsync<List<MobileOperation>>(Json);
                if(ops==null||ops.Count>500) return Results.BadRequest();
                var acknowledged=merge(ops);var library=snapshot();
                lock(gate) {Config.LastSyncAt=DateTimeOffset.UtcNow;SaveConfig();}
                return Results.Json(new {acknowledged,library},Json);
            });
            host.MapGet("/v1/art/{name}",(string name)=>
            {
                if(name!=Path.GetFileName(name)||name.Contains("..")) return Results.NotFound();
                var path=Path.Combine(store.DataDirectory,"art",name);
                return File.Exists(path)?Results.File(path,"image/"+(Path.GetExtension(name).Equals(".png",StringComparison.OrdinalIgnoreCase)?"png":"jpeg")):Results.NotFound();
            });
            host.MapGet("/v1/media/{id}",(string id)=>
            {
                var item=store.Archive.FirstOrDefault(i=>i.Id==id);
                return item!=null&&File.Exists(item.Path)?Results.File(item.Path,"application/octet-stream",lastModified:File.GetLastWriteTimeUtc(item.Path),entityTag:new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\""+new FileInfo(item.Path).Length+"-"+File.GetLastWriteTimeUtc(item.Path).Ticks+"\""),enableRangeProcessing:true):Results.NotFound();
            });
            host.MapGet("/v1/subtitles/{id}/{subtitle}",(string id,string subtitle)=>
            {
                var item=store.Archive.FirstOrDefault(i=>i.Id==id);
                var found=item==null?null:MobileSubtitles.Find(item).Where(s=>s.Info.Id==subtitle).Select(s=>s.Path).FirstOrDefault();
                return found!=null?Results.File(found,"application/octet-stream"):Results.NotFound();
            });
            await host.StartAsync();app=host;Status=$"Ready · HTTPS port {port}";
        }
        catch(Exception e) {if(host!=null) await host.DisposeAsync();store.Log(e);Status=$"Couldn't start sync on port {port}. Check that another app isn't using the port, then enable sync again.";}
    }
    bool Authorized(string token) {if(token.Length!=64) return false;var hash=Hash(token);lock(gate) return Config.Enabled&&Config.Peers.Any(p=>p.Hash==hash);}
    static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public async ValueTask DisposeAsync() {if(app!=null){await app.StopAsync();await app.DisposeAsync();app=null;}certificate.Dispose();}
    sealed class PairInput {public string Code {get;set;}="";public string Name {get;set;}="Android";}
    sealed class DownloadControl {public string Id {get;set;}="";public string Action {get;set;}="";}
    public event Action? Discovered;
    sealed class DiscoverInput {public string Id {get;set;}="";public string Type {get;set;}="movie";public int Season {get;set;}=1;public int Episode {get;set;}=1;public bool Watchlist {get;set;}}
}
