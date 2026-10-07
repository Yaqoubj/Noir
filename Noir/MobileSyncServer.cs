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
    readonly Dictionary<string,(int Count,DateTimeOffset Since)> attempts=[];
    WebApplication? app;
    string pairCode="";
    DateTimeOffset pairExpires;
    public MobileHostConfig Config {get;}
    public string Status {get;private set;}="Starting…";
    public bool Running=>app!=null;
    public string Pin=>Convert.ToHexString(SHA256.HashData(certificate.RawData));
    public MobileSyncServer(LibraryStore store,Func<MobileSnapshot> snapshot,Func<List<MobileOperation>,List<string>> merge,int port=Port)
    {
        this.store=store;this.snapshot=snapshot;this.merge=merge;this.port=port;
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
            await host.StartAsync();app=host;Status=$"Ready · HTTPS port {port}";
        }
        catch(Exception e) {if(host!=null) await host.DisposeAsync();store.Log(e);Status=$"Couldn't start sync on port {port}. Check that another app isn't using the port, then enable sync again.";}
    }
    bool Authorized(string token) {if(token.Length!=64) return false;var hash=Hash(token);lock(gate) return Config.Enabled&&Config.Peers.Any(p=>p.Hash==hash);}
    static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public async ValueTask DisposeAsync() {if(app!=null){await app.StopAsync();await app.DisposeAsync();app=null;}certificate.Dispose();}
    sealed class PairInput {public string Code {get;set;}="";public string Name {get;set;}="Android";}
}
