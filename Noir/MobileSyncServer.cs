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
public sealed class MobilePeer {public string Name {get;set;}="Android";public string Hash {get;set;}="";public DateTimeOffset PairedAt {get;set;}public HashSet<string> ProfileAccess {get;set;}=[];}
public sealed class MobileHostConfig {public HashSet<string> MigratedConfigs {get;set;}=[];public bool Enabled {get;set;}=true;public List<MobilePeer> Peers {get;set;}=[];public DateTimeOffset? LastSyncAt {get;set;}}
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
    readonly MobileSubtitleTools subtitleTools;
    readonly Func<string,CancellationToken,Task<bool>>? confirmSettingsShare;
    readonly SemaphoreSlim settingsShare=new(1,1);
    readonly SemaphoreSlim downloadDelete=new(1,1);
    readonly Func<DownloadLocalFile,DownloadDeleteChoice,CancellationToken,Task<DownloadDeleteChoice?>>? confirmDownloadDelete;
    readonly PhoneCloudDownloads? phoneCloud;
    readonly Dictionary<string,(int Count,DateTimeOffset Since)> attempts=[];
    WebApplication? app;
    string pairCode="";
    DateTimeOffset pairExpires;
    bool pairSucceeded;
    public (DateTimeOffset Expires,bool Active,bool Paired) PairingState {get{lock(gate)return(pairExpires,pairCode.Length>0&&DateTimeOffset.UtcNow<pairExpires,pairSucceeded);}}
    int activeMediaResponses;
    public int ActiveMediaResponses=>Volatile.Read(ref activeMediaResponses);
    public MobileHostConfig Config {get;}
    public string Status {get;private set;}="Starting…";
    public bool Running=>app!=null;
    public string Pin=>Convert.ToHexString(SHA256.HashData(certificate.RawData));
    /// <summary>The short code a TV shows before it sends the pairing code: the first 12 hex digits of the certificate fingerprint, "AB12 CD34 EF56". Mirrors DataStore.checkCode on Android.</summary>
    public static string CheckCode(string pin){var p=pin.ToUpperInvariant();return $"{p[..4]} {p[4..8]} {p[8..12]}";}
    public MobileSyncServer(LibraryStore store,Func<MobileSnapshot> snapshot,Func<List<MobileOperation>,List<string>> merge,int port=Port,PcDownloads? downloads=null,Func<string,CancellationToken,Task<bool>>? confirmSettingsShare=null,bool ephemeralCertificate=false,Func<DownloadLocalFile,DownloadDeleteChoice,CancellationToken,Task<DownloadDeleteChoice?>>? confirmDownloadDelete=null)
    {
        this.store=store;this.snapshot=snapshot;this.merge=merge;this.port=port;this.downloads=downloads;subtitleTools=new(store);this.confirmSettingsShare=confirmSettingsShare;this.confirmDownloadDelete=confirmDownloadDelete;phoneCloud=downloads==null?null:new(store,downloads);
        var shared=MobileProfiles.Root(store.DataDirectory);Directory.CreateDirectory(shared);
        configPath=Path.Combine(shared,"mobile-peers.json");
        if(File.Exists(configPath))
        {
            // Never replace saved pairing data merely because a scanner briefly locks the file.
            for(var attempt=0;;attempt++)
            try {Config=JsonSerializer.Deserialize<MobileHostConfig>(File.ReadAllText(configPath),Json)??throw new InvalidDataException("Stored phone pairing data is empty.");break;}
            catch(IOException) when(attempt<4) {Thread.Sleep(40*(attempt+1));}
        }
        else
        {
            var legacy=Path.Combine(store.DataDirectory,"mobile-host.json");if(!File.Exists(legacy))legacy=Path.Combine(shared,"mobile-host.json");
            Config=File.Exists(legacy)?JsonSerializer.Deserialize<MobileHostConfig>(File.ReadAllText(legacy),Json)??throw new InvalidDataException("Stored phone pairing data is empty."):new();
        }
        var sources=new List<string>{Path.Combine(shared,"mobile-host.json"),Path.Combine(store.DataDirectory,"mobile-host.json")};
        var profilesDir=Path.Combine(shared,"profiles");if(Directory.Exists(profilesDir))sources.AddRange(Directory.EnumerateDirectories(profilesDir).Select(d=>Path.Combine(d,"mobile-host.json")));
        foreach(var source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var key=Path.GetRelativePath(shared,source);if(Config.MigratedConfigs.Contains(key)||!File.Exists(source))continue;
            var legacy=JsonSerializer.Deserialize<MobileHostConfig>(File.ReadAllText(source),Json)??throw new InvalidDataException("Stored pairing data is empty.");
            foreach(var peer in legacy.Peers){var profileId=Path.GetDirectoryName(source)==shared?"default":Path.GetFileName(Path.GetDirectoryName(source)!);peer.ProfileAccess.Add(profileId);var existing=Config.Peers.FirstOrDefault(p=>p.Hash==peer.Hash);if(existing==null)Config.Peers.Add(peer);else existing.ProfileAccess.UnionWith(peer.ProfileAccess);}
            Config.MigratedConfigs.Add(key);
        }
        SaveConfig();
        var certificateStorage=ephemeralCertificate?X509KeyStorageFlags.EphemeralKeySet:X509KeyStorageFlags.DefaultKeySet;
        var certPath=Path.Combine(shared,"mobile-shared-certificate.dpapi");
        if(!File.Exists(certPath))
        {
            var legacy=Path.Combine(shared,"mobile-certificate.dpapi");if(!File.Exists(legacy))legacy=Path.Combine(store.DataDirectory,"mobile-certificate.dpapi");
            if(File.Exists(legacy))File.Copy(legacy,certPath,false);
        }
        if(File.Exists(certPath)) certificate=X509CertificateLoader.LoadPkcs12(ProtectedData.Unprotect(File.ReadAllBytes(certPath),null,DataProtectionScope.CurrentUser),null,certificateStorage);
        else
        {
            using var rsa=RSA.Create(2048);var req=new CertificateRequest("CN=Noir local sync",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
            using var issued=req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(10));
            var pfx=issued.Export(X509ContentType.Pfx);
            certificate=X509CertificateLoader.LoadPkcs12(pfx,null,certificateStorage);
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
        lock(gate) {pairCode=RandomNumberGenerator.GetInt32(10000000,100000000).ToString();pairExpires=DateTimeOffset.UtcNow.AddMinutes(10);pairSucceeded=false;attempts.Clear();
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
    internal Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? ConfigureServices {get;set;}
    public string StartFailure {get;private set;}="";
    public async Task SetEnabled(bool enabled)
    {
        StartFailure="";
        lock(gate) {Config.Enabled=enabled;SaveConfig();if(!enabled) pairCode="";}
        if(!enabled) {if(app!=null) {var old=app;app=null;await old.StopAsync();await old.DisposeAsync();}Status="Off";return;}
        if(app!=null) return;
        WebApplication? host=null;
        try
        {
            var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions {Args=[],ContentRootPath=store.DataDirectory});
            builder.Logging.ClearProviders();
            // Imported histories make the library snapshot large; phones accept gzip transparently.
            builder.Services.AddResponseCompression(o=>{o.EnableForHttps=true;o.MimeTypes=["application/json"];});
            builder.WebHost.ConfigureKestrel(o=>
            {
                o.Limits.MaxRequestBodySize=1024*1024;
                o.ListenAnyIP(port,l=>l.UseHttps(certificate));
                // Previously paired phones retain their address and certificate pin.
                if(port==Port) o.ListenAnyIP(LegacyPort,l=>l.UseHttps(certificate));
            });
            ConfigureServices?.Invoke(builder.Services);
            host=builder.Build();
            host.UseResponseCompression();
            host.Use(async(ctx,next)=>
            {
                if(!IsLocal(ctx.Connection.RemoteIpAddress)) {ctx.Response.StatusCode=403;return;}
                ctx.Response.Headers.CacheControl="no-store";
                if(ctx.Request.Path!="/v1/pair")
                {
                    var auth=ctx.Request.Headers.Authorization.ToString();
                    if(!auth.StartsWith("Bearer ",StringComparison.Ordinal)||!Authorized(auth[7..])) {ctx.Response.StatusCode=401;return;}
                }
                if(ctx.Request.Path.StartsWithSegments("/v1/subtitle-tools")&&ctx.Request.Path.Value!.EndsWith("/sync",StringComparison.Ordinal))
                {
                    var limit=ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                    if(limit is {IsReadOnly:false})limit.MaxRequestBodySize=6*1024*1024;
                }
                var requested=MobileProfiles.Resolve(ctx.Request.Headers["X-Noir-Profile"].ToString(),store.DataDirectory);
                if(ctx.Request.Path!="/v1/pair"&&!ctx.Request.Path.StartsWithSegments("/v1/profiles"))
                {
                    if(!MobileProfiles.Matches(requested,store.DataDirectory)||ctx.Request.Headers["X-Noir-Host"].ToString() is {Length:>0} requestedHost&&requestedHost!=MobileProfiles.HostId(store.DataDirectory))
                    {var active=MobileProfiles.Active(store.DataDirectory);ctx.Response.StatusCode=409;await ctx.Response.WriteAsJsonAsync(new{activeProfileId=active.Id,activeProfileName=active.Name,hostId=MobileProfiles.HostId(store.DataDirectory)},Json);return;}
                    if(!ProfileAllowed(ctx,requested)){ctx.Response.StatusCode=403;return;}
                }
                var mediaResponse=ctx.Request.Path.StartsWithSegments("/v1/media")||ctx.Request.Path.StartsWithSegments("/v1/phone-downloads/content");
                if(mediaResponse)Interlocked.Increment(ref activeMediaResponses);
                try{await next(ctx);}finally{if(mediaResponse)Interlocked.Decrement(ref activeMediaResponses);}
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
                    Config.Peers.Add(new(){Name=input.Name[..Math.Min(input.Name.Length,80)],Hash=Hash(token),PairedAt=DateTimeOffset.UtcNow,ProfileAccess=[MobileProfiles.Active(store.DataDirectory).Id]});pairCode="";pairSucceeded=true;SaveConfig();
                }
                var identity=snapshot();return Results.Json(new {token,instance=identity.Instance,hostId=identity.HostId,profileId=identity.ProfileId,profileName=identity.ProfileName,profileColor=identity.ProfileColor},Json);
            });
            host.MapGet("/v1/profiles",()=>Results.Json(MobileProfiles.List(store.DataDirectory),Json));
            host.MapPost("/v1/profiles",(ProfileInput input)=>
            {var p=MobileProfiles.Update(store.DataDirectory,null,input.Name,input.Color);return p==null?Results.BadRequest():Results.Json(new{id=p.Id,name=p.Name,color=p.Color,hostId=MobileProfiles.HostId(store.DataDirectory)},Json);});
            host.MapPost("/v1/profiles/{id}",(string id,ProfileInput input,HttpContext ctx)=>
            {if(!ProfileAllowed(ctx,id))return Results.StatusCode(403);var p=MobileProfiles.Update(store.DataDirectory,id,input.Name,input.Color);return p==null?Results.BadRequest():Results.Json(new{id=p.Id,name=p.Name,color=p.Color,hostId=MobileProfiles.HostId(store.DataDirectory)},Json);});
            host.MapPost("/v1/profiles/{id}/unlock",(string id,ProfileUnlock input,HttpContext ctx)=>
            {var status=UnlockProfile(ctx.Request.Headers.Authorization.ToString()[7..],id,input.Pin);return Results.Json(new{unlocked=status==200},Json,statusCode:status);});
            host.MapGet("/v1/library",()=>Results.Json(snapshot(),Json));
            host.MapGet("/v1/changes",()=>Results.Json(new{revision=store.MobileRevision(),profile=MobileProfiles.Active(store.DataDirectory).Id},Json));
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
            string Peer(HttpContext ctx)=>Hash(ctx.Request.Headers.Authorization.ToString()[7..]);
            host.MapPost("/v1/phone-downloads/settings",async(HttpContext ctx)=>
            {
                var reply=await ShareDownloadSettings(ctx.Request.Headers.Authorization.ToString()[7..],ctx.RequestAborted);
                return reply.Settings==null?Results.StatusCode(reply.Status):Results.Json(reply.Settings,Json);
            });
            host.MapGet("/v1/phone-downloads/search/{id}",(string id,HttpContext ctx)=>DownloadApi(async()=>await phoneCloud!.Search(id,Peer(ctx),ctx.RequestAborted)));
            host.MapPost("/v1/phone-downloads/prepare",(PhonePrepare input,HttpContext ctx)=>DownloadApi(async()=>await phoneCloud!.Prepare(input,Peer(ctx),ctx.RequestAborted)));
            host.MapGet("/v1/phone-downloads/content/{token}",async(string token,HttpContext ctx)=>{if(phoneCloud==null){ctx.Response.StatusCode=503;return;}try{await phoneCloud.Send(token,Peer(ctx),ctx);}catch(Exception){if(!ctx.Response.HasStarted)ctx.Response.StatusCode=502;else ctx.Abort();}});
            host.MapGet("/v1/pc-downloads",()=>Results.Json(DownloadQueue(),Json));
            host.MapPost("/v1/pc-downloads/delete",(DownloadDeleteInput input,HttpContext ctx)=>DeleteFinishedDownload(input.Id,ctx));
            host.MapGet("/v1/pc-downloads/search/{id}",(string id,bool? season,HttpContext ctx)=>DownloadApi(async()=>await downloads!.SearchDetailed(id,season==true,null,ctx.RequestAborted)));
            host.MapPost("/v1/pc-downloads/plan/{id}",(string id,DownloadPlanRequest input,HttpContext ctx)=>DownloadApi(async()=>await downloads!.PlanRequest(id,input,ctx.RequestAborted)));
            host.MapPost("/v1/pc-downloads/match",(DownloadMappingRequest input)=>DownloadApi(()=>Task.FromResult<object>(downloads!.MapFile(input.FileId,input.Episode))));
            host.MapGet("/v1/pc-downloads/files/{id}",(string id,HttpContext ctx)=>DownloadApi(async()=>new{files=await downloads!.Files(id,ctx.RequestAborted)}));
            host.MapPost("/v1/pc-downloads",(DownloadRequest input)=>DownloadApi(()=>Task.FromResult<object>(new{ids=downloads!.Enqueue(input)})));
            host.MapPost("/v1/pc-downloads/control",(DownloadControl input)=>DownloadApi(()=>{downloads!.Control(input.Id,input.Action);return Task.FromResult<object>(new{ok=true});}));
            // Media servers and Kodi known to this PC, read for the phone; the phone matches and logs the rows itself.
            host.MapGet("/v1/history/servers",()=>Results.Json(new{servers=(downloads?.Config.Servers??[]).Select(x=>new{name=x.Name.Length>0?x.Name:x.Kind,kind=x.Kind}).Concat(KodiAccount()!=null?[new{name="Kodi",kind="Kodi"}]:[])},Json));
            host.MapPost("/v1/history/server-watched",async(ServerPick input,HttpContext ctx)=>
            {
                try
                {
                    List<ImportRow> rows;
                    if(input.Name=="Kodi"&&KodiAccount() is {} k)rows=await new MediaServers().KodiWatched(k.Address,k.User,k.Password,ctx.RequestAborted);
                    else if((downloads?.Config.Servers??[]).FirstOrDefault(x=>(x.Name.Length>0?x.Name:x.Kind)==input.Name) is {} server)rows=await new MediaServers().Watched(server,ctx.RequestAborted);
                    else return Results.Json(new{error="That server isn't set up on the PC any more."},Json,statusCode:404);
                    return Results.Json(new{service=input.Name,rows=rows.Select(r=>new{title=r.Title,year=r.Year,movie=r.IsMovie,imdb=r.ImdbId,season=r.Season,episode=r.Episode,episodeTitle=r.EpisodeTitle,at=r.WatchedAt?.ToUnixTimeMilliseconds()??0})},Json);
                }
                catch(OperationCanceledException)when(ctx.RequestAborted.IsCancellationRequested){return Results.StatusCode(499);}
                catch(Exception e){return Results.Json(new{error=e is IOException&&downloads!=null?DownloadSecurity.SafeText(e.Message,downloads.Config):"The PC couldn't read that server."},Json,statusCode:502);}
            });
            host.MapPost("/v1/sync",async(HttpContext ctx)=>
            {
                var ops=await ctx.Request.ReadFromJsonAsync<List<MobileOperation>>(Json);
                if(ops==null||ops.Count>500) return Results.BadRequest();
                var result=SyncProfile(ctx.Request.Headers["X-Noir-Profile"].ToString(),ops,ctx.Request.Headers["X-Noir-Host"].ToString());
                if(result.Status!=200){var active=MobileProfiles.Active(store.DataDirectory);return Results.Json(new{activeProfileId=active.Id,activeProfileName=active.Name},Json,statusCode:409);}
                return Results.Json(new {acknowledged=result.Acknowledged,library=result.Library,revision=result.Library!.Revision},Json);
            });
            host.MapGet("/v1/art/{name}",(string name)=>
            {
                if(name!=Path.GetFileName(name)||name.Contains("..")) return Results.NotFound();
                var path=Path.Combine(store.DataDirectory,"art",name);
                return File.Exists(path)?Results.File(path,"image/"+(Path.GetExtension(name).Equals(".png",StringComparison.OrdinalIgnoreCase)?"png":"jpeg")):Results.NotFound();
            });
            host.MapGet("/v1/media/{id}",(string id)=>
            {
                LastMediaRequest=DateTime.UtcNow;
                var item=store.Archive.FirstOrDefault(i=>i.Id==id);
                return item!=null&&File.Exists(item.Path)?Results.File(item.Path,"application/octet-stream",lastModified:File.GetLastWriteTimeUtc(item.Path),entityTag:new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\""+new FileInfo(item.Path).Length+"-"+File.GetLastWriteTimeUtc(item.Path).Ticks+"\""),enableRangeProcessing:true):Results.NotFound();
            });
            host.MapGet("/v1/preview-frame/{id}/{seconds}",async(string id,double seconds,HttpContext ctx)=>
            {
                var (status,path)=await PreviewFrameFor(ctx.Request.Headers.Authorization.ToString()[7..],id,seconds,ctx.RequestAborted);
                return status==200?Results.File(path!,"image/png"):Results.StatusCode(status);
            });
            host.MapGet("/v1/subtitles/{id}/{subtitle}",(string id,string subtitle)=>
            {
                var item=store.Archive.FirstOrDefault(i=>i.Id==id);
                var found=item==null?null:MobileSubtitles.Find(item).Where(s=>s.Info.Id==subtitle).Select(s=>s.Path).FirstOrDefault();
                return found!=null?Results.File(found,"application/octet-stream"):Results.NotFound();
            });
            host.MapGet("/v1/subtitle-tools/{id}",async(string id,HttpContext ctx)=>
            {
                try{return Results.Json(await subtitleTools.Info(id,ctx.RequestAborted),Json);}
                catch(IOException e){return Results.Json(new{error=e.Message},Json,statusCode:404);}
            });
            host.MapPost("/v1/subtitle-tools/{id}/sync",async(string id,MobileSubtitleSyncInput input,HttpContext ctx)=>
            {
                try{return Results.Json(await subtitleTools.Sync(id,input,ctx.RequestAborted),Json);}
                catch(IOException e){return Results.Json(new{error=e.Message},Json,statusCode:400);}
            });
            await host.StartAsync();app=host;Status=$"Ready · HTTPS port {port}";
        }
        catch(Exception e) {if(host!=null) await host.DisposeAsync();store.Log(e);StartFailure=System.Text.RegularExpressions.Regex.Replace(e.GetBaseException().Message,@"(?i)\b[a-z][a-z0-9+.-]*://\S+","[link]");Status=$"Couldn't start sync on port {port}. · تعذر بدء المزامنة على المنفذ {port}. "+StartFailure;}
    }
    public async Task<(int Status,PhoneDownloadSettingsPayload? Settings)> ShareDownloadSettings(string token,CancellationToken cancellation=default)
    {
        if(!Authorized(token))return(401,null);
        if(downloads==null||confirmSettingsShare==null)return(403,null);
        if(!await settingsShare.WaitAsync(0,cancellation))return(429,null);
        try{
            string name;lock(gate)name=Config.Peers.FirstOrDefault(p=>p.Hash==Hash(token))?.Name??"";
            if(name.Length==0)return(401,null);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromMinutes(2));
            bool approved;try{approved=await confirmSettingsShare(name,timeout.Token).WaitAsync(timeout.Token);}catch(OperationCanceledException){return(403,null);}
            if(!approved||timeout.IsCancellationRequested)return(403,null);
            if(!Authorized(token))return(401,null);
            var c=downloads.Config;
            var subtitles=SubtitleSelection.From(c);
            return(200,new(){SubtitleSource=subtitles.Source,SubtitleApiKey=subtitles.ApiKey,SubtitleUsername=subtitles.Username,SubtitlePassword=subtitles.Password,SubtitleAddonAuth=subtitles.AddonAuth,SubtitleAddonUrl=c.SubtitleAddonUrl,SubtitleAddonOnly=c.SubtitleAddonOnly,Manifest=c.Manifest,TorBoxKey=c.TorBoxKey.Length>0?c.TorBoxKey:c.Service=="TorBox"?c.Key:"",RealDebridKey=c.RealDebridKey.Length>0?c.RealDebridKey:c.Service=="Real-Debrid"?c.Key:"",PreferredResolution=c.PreferredResolution,Language=c.BrowseLanguage=="All"?"Any":c.BrowseLanguage,MaxEpisodeBytes=c.MaxEpisodeBytes});
        }catch(Exception){return(403,null);}finally{settingsShare.Release();}
    }
    public object DownloadQueue()=>new{configured=downloads?.Configured??false,capabilities=new{deleteFinished=downloads!=null&&confirmDownloadDelete!=null},jobs=downloads?.PublicJobs()??[]};
    public async Task<IResult> DeleteFinishedDownload(string id,HttpContext ctx)
    {
        IResult Error(int status,string en,string ar)=>Results.Json(new{error=store.State.Language=="ar"?ar:en},Json,statusCode:status);
        var auth=ctx.Request.Headers.Authorization.ToString();if(!auth.StartsWith("Bearer ",StringComparison.Ordinal)||!Authorized(auth[7..]))return Results.Unauthorized();
        var requested=MobileProfiles.Resolve(ctx.Request.Headers["X-Noir-Profile"].ToString(),store.DataDirectory);if(!MobileProfiles.Matches(requested,store.DataDirectory))return Results.StatusCode(409);if(!ProfileAllowed(ctx,requested))return Results.StatusCode(403);
        if(downloads==null||confirmDownloadDelete==null)return Error(409,"Open Noir on your PC to confirm deletion.","افتح نوار على الكمبيوتر لتأكيد الحذف.");
        if(!await downloadDelete.WaitAsync(0,ctx.RequestAborted))return Error(409,"Another deletion is waiting for confirmation on your PC.","تنتظر عملية حذف أخرى التأكيد على الكمبيوتر.");
        try{
            var job=downloads.Jobs().FirstOrDefault(j=>j.Id==id);if(job==null)return Results.NotFound();
            if(job.Status!="Completed")return Error(409,"Wait for this download to finish before deleting it.","انتظر اكتمال هذا التنزيل قبل حذفه.");
            var file=downloads.LocalLibrary().SelectMany(g=>g.Files).FirstOrDefault(f=>f.Path.Equals(job.Destination,StringComparison.OrdinalIgnoreCase));if(file==null)return Results.NotFound();
            var profile=MobileProfiles.Active(store.DataDirectory).Id;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);timeout.CancelAfter(TimeSpan.FromMinutes(2));
            DownloadDeleteChoice? choice;try{choice=await confirmDownloadDelete(file,downloads.ManagementPreferences.WhenDeleting,timeout.Token).WaitAsync(timeout.Token);}catch(OperationCanceledException){choice=null;}
            if(choice is not (DownloadDeleteChoice.Recycle or DownloadDeleteChoice.Permanent)||timeout.IsCancellationRequested)return Error(409,"Deletion was cancelled on your PC.","أُلغي الحذف على الكمبيوتر.");
            if(!Authorized(ctx.Request.Headers.Authorization.ToString()[7..]))return Results.Unauthorized();
            if(!MobileProfiles.Matches(profile,store.DataDirectory)||!ProfileAllowed(ctx,profile))return Results.StatusCode(409);
            var current=downloads.Jobs().FirstOrDefault(j=>j.Id==id);if(current==null)return Results.NotFound();
            if(current.Status!="Completed"||!current.Destination.Equals(file.Path,StringComparison.OrdinalIgnoreCase))return Error(409,"This download changed. Refresh and try again.","تغير هذا التنزيل. حدّث وحاول مجددًا.");
            var result=await downloads.DeleteLocal([file],choice.Value,ct:ctx.RequestAborted);
            return result.Errors.Count==0&&result.Files==1?Results.Json(new{ok=true,files=result.Files,bytes=result.Bytes},Json):Error(409,"Couldn't delete this download. Check Noir on your PC and try again.","تعذر حذف هذا التنزيل. تحقق من نوار على الكمبيوتر وحاول مجددًا.");
        }finally{downloadDelete.Release();}
    }
    sealed class DownloadDeleteInput {public string Id {get;set;}="";}
    bool ProfileAllowed(HttpContext ctx,string id)
    {
        var list=Profiles.Load(MobileProfiles.Root(store.DataDirectory));var p=list.Profiles.FirstOrDefault(p=>p.Id==id);if(p==null)return false;if(p.PinHash.Length==0&&list.ReadFailurePath==null)return true;
        var hash=Hash(ctx.Request.Headers.Authorization.ToString()[7..]);lock(gate)return Config.Peers.Any(peer=>peer.Hash==hash&&peer.ProfileAccess.Contains(id));
    }
    public int UnlockProfile(string token,string id,string pin)
    {
        if(!Authorized(token))return 401;
        lock(gate){var key="unlock:"+Hash(token)+":"+id;var failed=attempts.GetValueOrDefault(key);var now=DateTimeOffset.UtcNow;
            if(now-failed.Since>TimeSpan.FromMinutes(5))failed=(0,now);if(failed.Count>=5)return 429;
            var p=Profiles.Load(MobileProfiles.Root(store.DataDirectory)).Profiles.FirstOrDefault(p=>p.Id==id);if(p==null)return 404;
            if(pin.Length>128||!Profiles.CheckPin(p,pin)){attempts[key]=(failed.Count+1,failed.Since);return 403;}
            attempts.Remove(key);Config.Peers.First(peer=>peer.Hash==Hash(token)).ProfileAccess.Add(id);SaveConfig();return 200;}
    }
    sealed class ProfileInput {public string Name {get;set;}="";public string Color {get;set;}="#E4C084";}
    sealed class ProfileUnlock {public string Pin {get;set;}="";}
    public (int Status,List<string> Acknowledged,MobileSnapshot? Library) SyncProfile(string profile,List<MobileOperation> operations,string hostId="")
    {
        if(!MobileProfiles.Matches(profile,store.DataDirectory)||hostId.Length>0&&hostId!=MobileProfiles.HostId(store.DataDirectory))return(409,[],null);
        var acknowledged=merge(operations);var library=snapshot();lock(gate){Config.LastSyncAt=DateTimeOffset.UtcNow;SaveConfig();}return(200,acknowledged,library);
    }
    public bool Authorized(string token) {if(token.Length!=64) return false;var hash=Hash(token);lock(gate) return Config.Enabled&&Config.Peers.Any(p=>p.Hash==hash);}
    static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public async ValueTask DisposeAsync() {if(app!=null){await app.StopAsync();await app.DisposeAsync();app=null;}certificate.Dispose();phoneCloud?.Dispose();}
    sealed class PairInput {public string Code {get;set;}="";public string Name {get;set;}="Android";}
    sealed class DownloadControl {public string Id {get;set;}="";public string Action {get;set;}="";}
    sealed class ServerPick {public string Name {get;set;}="";}
    (string Address,string User,string Password)? KodiAccount()
    {
        try
        {
            var path=Path.Combine(store.DataDirectory,"kodi.dpapi");if(!File.Exists(path))return null;
            var json=System.Text.Json.JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(path),null,System.Security.Cryptography.DataProtectionScope.CurrentUser))).RootElement;
            return(json.GetProperty("a").GetString()??"",json.GetProperty("u").GetString()??"",json.GetProperty("p").GetString()??"");
        }
        catch(Exception){return null;}
    }
    public event Action? Discovered;
    // Phones stream with repeated range requests; used to keep the PC awake while one is watching.
    public DateTime LastMediaRequest {get;private set;}=DateTime.MinValue;
    // Scrub thumbnails for phones playing PC files; MainWindow supplies the PNG from its preview cache.
    public Func<VideoItem,double,CancellationToken,Task<string?>>? PreviewFrame {get;set;}
    public async Task<(int Status,string? Path)> PreviewFrameFor(string token,string id,double seconds,CancellationToken cancellation=default)
    {
        if(!Authorized(token)) return(401,null);
        var item=store.Archive.FirstOrDefault(i=>i.Id==id);
        if(item==null||PreviewFrame==null||!double.IsFinite(seconds)||seconds<0||seconds>86400||!File.Exists(item.Path)) return(404,null);
        try{
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var path=await PreviewFrame(item,seconds,timeout.Token);
            return path!=null&&File.Exists(path)&&new FileInfo(path).Length<=2*1024*1024?(200,path):(404,null);
        }catch(Exception e) when(e is OperationCanceledException or IOException){return(404,null);}
    }
    sealed class DiscoverInput {public string Id {get;set;}="";public string Type {get;set;}="movie";public int Season {get;set;}=1;public int Episode {get;set;}=1;public bool Watchlist {get;set;}}
}

public sealed class PhoneDownloadSettingsPayload
{
    public string SubtitleAddonUrl {get;set;}="";
    public bool SubtitleAddonOnly {get;set;}=true;
    string subtitleSource="";
    public string SubtitleSource {get=>subtitleSource.Length==0&&SubtitleAddonUrl.Length>0?"addon":subtitleSource;set=>subtitleSource=value??"";}
    public string SubtitleApiKey {get;set;}="";
    public string SubtitleUsername {get;set;}="";
    public string SubtitlePassword {get;set;}="";
    public string SubtitleAddonAuth {get;set;}="";
    public string Manifest {get;set;}="";
    public string TorBoxKey {get;set;}="";
    public string RealDebridKey {get;set;}="";
    public string PreferredResolution {get;set;}="1080p";
    public string Language {get;set;}="Any";
    public long MaxEpisodeBytes {get;set;}
}
