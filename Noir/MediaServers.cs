using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public sealed class MediaServerAccount
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Kind {get;set;}="Jellyfin";
    public string Name {get;set;}="";
    public string BaseUrl {get;set;}="";
    public string Token {get;set;}="";
    public string UserId {get;set;}="";
    public string DeviceId {get;set;}="";
}
public sealed record PlexPin(string Id,string Code,string DeviceId)
{
    public string BrowserUrl=>"https://app.plex.tv/auth#?clientID="+Uri.EscapeDataString(DeviceId)+"&code="+Uri.EscapeDataString(Code)+"&context%5Bdevice%5D%5Bproduct%5D=Noir";
}
public sealed class MediaServers(HttpClient? client=null)
{
    readonly HttpClient http=client??DownloadSecurity.Client(TimeSpan.FromSeconds(45));
    public static string Source(MediaServerAccount server)=>DownloadSecurity.SafeText(server.Name+" ("+server.Kind+")",new(){Servers=[server]});
    public static string BaseUrl(string text)
    {
        if(!Uri.TryCreate(text.Trim().TrimEnd('/'),UriKind.Absolute,out var u)||u.Scheme is not ("https" or "http")||u.UserInfo.Length>0||u.Query.Length>0||u.Fragment.Length>0)throw new IOException("Enter a media server HTTP or HTTPS address without credentials or a query.");return u.AbsoluteUri.TrimEnd('/');
    }
    public async Task<MediaServerAccount> Authenticate(string url,string username,string password,string deviceId,string name,string kind,CancellationToken ct)
    {
        var server=new MediaServerAccount{BaseUrl=BaseUrl(url),DeviceId=deviceId,Kind=kind=="Emby"?"Emby":"Jellyfin",Name=name.Trim()};
        using var content=new StringContent(JsonSerializer.Serialize(new{Username=username,Pw=password}),Encoding.UTF8,"application/json");
        var data=await Send(server,"/Users/AuthenticateByName",content,ct);server.Token=Text(data,"AccessToken");server.UserId=data.TryGetProperty("User",out var user)?Text(user,"Id"):"";
        if(server.Token.Length==0||server.UserId.Length==0)throw new IOException("The media server didn't confirm this account. Check your username and password.");
        if(server.Name.Length==0)server.Name=new Uri(server.BaseUrl).Host;return server;
    }
    public async Task<PlexPin> BeginPlex(string deviceId,CancellationToken ct)
    {
        var data=await Plex("https://plex.tv/api/v2/pins?strong=true",deviceId,"",new FormUrlEncodedContent([]),ct);var id=Scalar(data,"id");var code=Text(data,"code");
        if(id.Length==0||code.Length==0)throw new IOException("Plex didn't return a sign-in code. Retry shortly.");return new(id,code,deviceId);
    }
    public async Task<string> PollPlex(PlexPin pin,CancellationToken ct)
        =>Text(await Plex("https://plex.tv/api/v2/pins/"+Uri.EscapeDataString(pin.Id),pin.DeviceId,"",null,ct),"authToken");
    public async Task<List<MediaServerAccount>> PlexResources(string token,string deviceId,CancellationToken ct)
    {
        var data=await Plex("https://plex.tv/api/v2/resources?includeHttps=1&includeRelay=1",deviceId,token,null,ct);var result=new List<MediaServerAccount>();if(data.ValueKind!=JsonValueKind.Array)return result;
        foreach(var resource in data.EnumerateArray().Where(r=>Text(r,"provides").Split(',').Contains("server"))){
            if(!resource.TryGetProperty("connections",out var connections)||connections.ValueKind!=JsonValueKind.Array)continue;
            var connection=connections.EnumerateArray().Where(c=>DownloadSecurity.ValidUrl(Text(c,"uri"))||Flag(c,"local")&&DownloadSecurity.ValidUrl(Text(c,"uri"),Text(c,"uri")))
                .OrderByDescending(c=>Text(c,"uri").StartsWith("https://",StringComparison.OrdinalIgnoreCase)?Flag(c,"local")?3:2:1).FirstOrDefault();
            var url=Text(connection,"uri");if(url.Length==0)continue;
            var access=Text(resource,"accessToken");if(access.Length==0)access=token;
            result.Add(new(){Kind="Plex",Name=Text(resource,"name"),BaseUrl=BaseUrl(url),Token=access,DeviceId=deviceId});
        }
        return result;
    }
    public async Task Test(MediaServerAccount server,CancellationToken ct)=>_=await Send(server,server.Kind=="Plex"?"/library/sections":"/Users/"+Uri.EscapeDataString(server.UserId),null,ct);
    // Everything marked played on the server, as import rows. Read-only: nothing on the server changes.
    public async Task<List<ImportRow>> Watched(MediaServerAccount server,CancellationToken ct)
    {
        var service=server.Kind;var rows=new List<ImportRow>();
        static DateTimeOffset? When(string text)=>DateTimeOffset.TryParse(text,out var at)&&at.Year>1971?at:null;
        static DateTimeOffset? Unix(long seconds)=>seconds>0?DateTimeOffset.FromUnixTimeSeconds(seconds):null;
        if(server.Kind=="Plex")
        {
            foreach(var section in PlexDirectories(await Send(server,"/library/sections",null,ct)).Where(d=>Text(d,"type") is "movie" or "show"))
            {
                var movie=Text(section,"type")=="movie";
                var items=PlexItems(await Send(server,"/library/sections/"+Uri.EscapeDataString(Text(section,"key"))+"/all?type="+(movie?"1":"4"),null,ct));
                foreach(var i in items.Where(i=>Number(i,"viewCount")>0))
                {
                    var imdb=Array(i,"Guid").Select(g=>Text(g,"id")).Append(Text(i,"guid")).Select(g=>Regex.Match(g,@"(?:imdb://|imdb/)(tt\d+)").Groups[1].Value).FirstOrDefault(x=>x.Length>0)??"";
                    rows.Add(movie?new(service,Text(i,"title"),(int)Number(i,"year"),true,imdb,"",0,0,"",Unix(Number(i,"lastViewedAt")),0)
                        :new(service,Text(i,"grandparentTitle"),0,false,"","",(int)Number(i,"parentIndex"),(int)Number(i,"index"),Text(i,"title"),Unix(Number(i,"lastViewedAt")),0));
                }
            }
            return rows;
        }
        var data=await Send(server,"/Users/"+Uri.EscapeDataString(server.UserId)+"/Items?Recursive=true&IncludeItemTypes=Movie,Episode&IsPlayed=true&Fields=ProviderIds&EnableUserData=true",null,ct);
        foreach(var i in Array(data,"Items"))
        {
            var played=i.TryGetProperty("UserData",out var user)?When(Text(user,"LastPlayedDate")):null;
            if(Text(i,"Type")=="Movie")rows.Add(new(service,Text(i,"Name"),(int)Number(i,"ProductionYear"),true,i.TryGetProperty("ProviderIds",out var ids)?Text(ids,"Imdb"):"","",0,0,"",played,0));
            else if(Text(i,"Type")=="Episode")rows.Add(new(service,Text(i,"SeriesName"),0,false,"","",(int)Number(i,"ParentIndexNumber"),(int)Number(i,"IndexNumber"),Text(i,"Name"),played,0));
        }
        return rows;
    }
    // Kodi's JSON-RPC: watched movies and episodes from its video library.
    public async Task<List<ImportRow>> KodiWatched(string address,string user,string password,CancellationToken ct)
    {
        var url=BaseUrl(address)+"/jsonrpc";if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https"))throw new IOException("Use Kodi's web address, like http://192.168.1.20:8080.");
        async Task<JsonElement> Call(string method,string[] fields)
        {
            var body=JsonSerializer.Serialize(new{jsonrpc="2.0",id=1,method,@params=new{properties=fields,filter=new{field="playcount",@operator="greaterthan",value="0"}}});
            using var request=new HttpRequestMessage(HttpMethod.Post,uri){Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")};
            if(user.Length>0)request.Headers.Authorization=new("Basic",Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(user+":"+password)));
            var reply=await Read(request,ct);return reply.TryGetProperty("result",out var result)?result:default;
        }
        var rows=new List<ImportRow>();static DateTimeOffset? When(string text)=>DateTime.TryParse(text,out var at)?new DateTimeOffset(at):null;
        foreach(var m in Array(await Call("VideoLibrary.GetMovies",["title","year","imdbnumber","lastplayed"]),"movies"))
            rows.Add(new("Kodi",Text(m,"title"),(int)Number(m,"year"),true,Regex.IsMatch(Text(m,"imdbnumber"),@"^tt\d+$")?Text(m,"imdbnumber"):"","",0,0,"",When(Text(m,"lastplayed")),0));
        foreach(var e in Array(await Call("VideoLibrary.GetEpisodes",["showtitle","season","episode","title","lastplayed"]),"episodes"))
            rows.Add(new("Kodi",Text(e,"showtitle"),0,false,"","",(int)Number(e,"season"),(int)Number(e,"episode"),Text(e,"title"),When(Text(e,"lastplayed")),0));
        return rows;
    }
    static List<JsonElement> PlexDirectories(JsonElement root)=>Array(root.TryGetProperty("MediaContainer",out var c)?c:root,"Directory");
    public async Task<List<IndexedRelease>> Search(MediaServerAccount server,VideoItem item,bool season,CancellationToken ct)
        =>server.Kind=="Plex"?await PlexSearch(server,item,season,ct):await JellyfinSearch(server,item,season,ct);
    async Task<List<IndexedRelease>> JellyfinSearch(MediaServerAccount server,VideoItem item,bool season,CancellationToken ct)
    {
        var data=await Send(server,"/Users/"+Uri.EscapeDataString(server.UserId)+"/Items?Recursive=true&IncludeItemTypes=Movie,Series&Fields=ProviderIds,MediaSources,Path&searchTerm="+Uri.EscapeDataString(item.Title),null,ct);
        var titles=Array(data,"Items").Where(t=>Text(t,"Type")== (item.IsMovie?"Movie":"Series")&&Match(item,Text(t,"Name"),Number(t,"ProductionYear"),t.TryGetProperty("ProviderIds",out var ids)?Text(ids,"Imdb"):"")).ToList();var result=new List<IndexedRelease>();
        foreach(var title in titles){
            var files=new List<DirectDownloadSource>();
            if(item.IsMovie){var f=JellyfinFile(server,title,0,0);if(f!=null)files.Add(f);}
            else{
                var episodes=await Send(server,"/Shows/"+Uri.EscapeDataString(Text(title,"Id"))+"/Episodes?userId="+Uri.EscapeDataString(server.UserId)+"&season="+item.Season+"&Fields=ProviderIds,MediaSources,Path",null,ct);
                foreach(var ep in Array(episodes,"Items").Where(e=>Number(e,"ParentIndexNumber")==item.Season&&Number(e,"IndexNumber")>0&&(season||Number(e,"IndexNumber")==item.Episode))){var f=JellyfinFile(server,ep,(int)Number(ep,"ParentIndexNumber"),(int)Number(ep,"IndexNumber"));if(f!=null)files.Add(f);}
            }
            if(files.Count>0)result.Add(Release(server,item,season,files));
        }
        return result;
    }
    static DirectDownloadSource? JellyfinFile(MediaServerAccount server,JsonElement item,int season,int episode)
    {
        var media=Array(item,"MediaSources").FirstOrDefault();var name=FileName(Text(media,"Path"));if(name.Length==0)name=FileName(Text(item,"Path"));if(!DownloadNaming.Video(name))name=$"Video.S{season:00}E{episode:00}.mkv";
        var id=Text(item,"Id");if(id.Length==0)return null;
        var streams=Array(media,"MediaStreams").Concat(Array(item,"MediaStreams")).ToList();
        var video=streams.FirstOrDefault(s=>Text(s,"Type")=="Video");
        return new(){Url=server.BaseUrl+"/Items/"+Uri.EscapeDataString(id)+"/Download",Headers=new(){{"X-Emby-Token",server.Token},{"Authorization",Authorization(server)}},Name=name,Size=Math.Max(0,Number(media,"Size")),Season=season,Episode=episode,ServerId=server.Id,Resolution=VideoResolution(Number(video,"Height"),name),Language=AudioLanguages(streams.Where(s=>Text(s,"Type")=="Audio").Select(s=>Text(s,"Language")))};
    }
    async Task<List<IndexedRelease>> PlexSearch(MediaServerAccount server,VideoItem item,bool season,CancellationToken ct)
    {
        var titles=new List<JsonElement>();
        if(Regex.IsMatch(item.ImdbId,@"^tt\d+$")){var exact=await Send(server,"/library/all?guid="+Uri.EscapeDataString("imdb://"+item.ImdbId),null,ct);titles=PlexItems(exact).Where(t=>PlexMatch(t,item)).ToList();}
        if(titles.Count==0){var search=await Send(server,"/hubs/search?query="+Uri.EscapeDataString(item.Title),null,ct);titles=PlexItems(search).Where(t=>PlexMatch(t,item)).ToList();}
        var result=new List<IndexedRelease>();
        foreach(var title in titles.DistinctBy(t=>Text(t,"ratingKey"))){
            var files=new List<DirectDownloadSource>();
            if(item.IsMovie){var f=PlexFile(server,title,0,0);if(f!=null)files.Add(f);}
            else{
                var episodes=await Send(server,"/library/metadata/"+Uri.EscapeDataString(Text(title,"ratingKey"))+"/allLeaves",null,ct);
                foreach(var ep in PlexItems(episodes).Where(e=>Number(e,"parentIndex")==item.Season&&Number(e,"index")>0&&(season||Number(e,"index")==item.Episode))){var f=PlexFile(server,ep,(int)Number(ep,"parentIndex"),(int)Number(ep,"index"));if(f!=null)files.Add(f);}
            }
            if(files.Count>0)result.Add(Release(server,item,season,files));
        }
        return result;
    }
    static bool PlexMatch(JsonElement title,VideoItem item)
    {
        if(Text(title,"type")!=(item.IsMovie?"movie":"show"))return false;
        var imdb=Array(title,"Guid").Select(g=>Text(g,"id")).Append(Text(title,"guid")).Select(g=>Regex.Match(g,@"(?:imdb://|imdb/)(tt\d+)").Groups[1].Value).FirstOrDefault(s=>s.Length>0)??"";
        return Match(item,Text(title,"title"),Number(title,"year"),imdb);
    }
    static DirectDownloadSource? PlexFile(MediaServerAccount server,JsonElement item,int season,int episode)
    {
        var media=Array(item,"Media").FirstOrDefault();var part=Array(media,"Part").FirstOrDefault();var key=Text(part,"key");if(!key.StartsWith('/')||key.StartsWith("//")||key.Contains('\\'))return null;
        var url=server.BaseUrl+key+(key.Contains('?')?"&":"?")+"download=1";if(!DownloadSecurity.ValidUrl(url,server.BaseUrl))return null;
        var name=FileName(Text(part,"file"));if(!DownloadNaming.Video(name))name=$"Video.S{season:00}E{episode:00}.mkv";
        return new(){Url=url,Headers=new(){{"X-Plex-Token",server.Token}},Name=name,Size=Math.Max(0,Number(part,"size")),Season=season,Episode=episode,ServerId=server.Id,Resolution=VideoResolution(Number(media,"height"),name),Language=AudioLanguages(Array(part,"Stream").Where(s=>Number(s,"streamType")==2).Select(s=>Text(s,"languageCode")))};
    }
    static string VideoResolution(long height,string name)=>height>=2000?"2160p":height>=1000?"1080p":height>=700?"720p":TorboxProvider.Resolution(name);
    static string AudioLanguages(IEnumerable<string> languages)
    {
        var known=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"en","English"},{"eng","English"},{"ar","Arabic"},{"ara","Arabic"},{"ja","Japanese"},{"jpn","Japanese"},{"ko","Korean"},{"kor","Korean"},{"hi","Hindi"},{"hin","Hindi"},{"es","Spanish"},{"spa","Spanish"},{"fr","French"},{"fra","French"},{"fre","French"}};
        var audio=languages.Select(l=>known.GetValueOrDefault(l,"")).Where(l=>l.Length>0).Distinct().ToList();return audio.Count>0?string.Join(" / ",audio):"Unknown";
    }
    static IndexedRelease Release(MediaServerAccount server,VideoItem item,bool season,List<DirectDownloadSource> files)
    {
        var config=new DownloadCredentials{Servers=[server]};foreach(var f in files)f.Name=DownloadSecurity.SafeText(f.Name,config);
        return new(new(){Id=Guid.NewGuid().ToString("N"),Name=item.IsMovie||!season?files[0].Name:item.Title+$" · Season {item.Season}",Detail=$"{files.Count} files available for download from your media server",Size=files.Sum(f=>f.Size),SizeKind=season?"Pack":item.IsMovie?"Movie":"Episode",Cached=true,CacheStatus="On your server",Sources=[DownloadSecurity.SafeText(Source(server),config)],ReferenceSeason=item.Season,ReferenceEpisode=item.Episode},"",files.OrderBy(f=>f.Episode).ToList());
    }
    static bool Match(VideoItem item,string title,long year,string imdb)
    {
        if(imdb.Length>0&&item.ImdbId.Length>0)return imdb.Equals(item.ImdbId,StringComparison.OrdinalIgnoreCase);
        static string Normalize(string s)=>Regex.Replace(s.ToLowerInvariant(),@"[^\p{L}\p{Nd}]+","");
        return year==item.Year&&year>0&&new[]{item.Title,item.ReviewTitle}.Any(t=>t.Length>0&&Normalize(t)==Normalize(title));
    }
    static List<JsonElement> PlexItems(JsonElement root)
    {
        var container=root.TryGetProperty("MediaContainer",out var c)?c:root;return Array(container,"Metadata").Concat(Array(container,"Hub").SelectMany(h=>Array(h,"Metadata"))).ToList();
    }
    static string FileName(string path)=>path.Replace('\\','/').Split('/').LastOrDefault()??"";
    static string Authorization(MediaServerAccount s)=>"MediaBrowser Client=\"Noir\", Device=\"Windows PC\", DeviceId=\""+s.DeviceId+"\", Version=\"1.0\""+(s.Token.Length>0?", Token=\""+s.Token+"\"":"");
    Task<JsonElement> Send(MediaServerAccount server,string path,HttpContent? content,CancellationToken ct)
        =>content==null?PlanningRequests.Read(this,JsonSerializer.Serialize(server)+"|"+path,()=>SendCore(server,path,null,ct)):SendCore(server,path,content,ct);
    async Task<JsonElement> SendCore(MediaServerAccount server,string path,HttpContent? content,CancellationToken ct)
    {
        var url=BaseUrl(server.BaseUrl)+path;if(!DownloadSecurity.ValidUrl(url,server.BaseUrl))throw new IOException("The media server address is invalid.");
        using var request=new HttpRequestMessage(content==null?HttpMethod.Get:HttpMethod.Post,url){Content=content};request.Headers.TryAddWithoutValidation("Accept","application/json");
        var headers=server.Kind=="Plex"?new Dictionary<string,string>{{"X-Plex-Token",server.Token},{"X-Plex-Client-Identifier",server.DeviceId}}:new Dictionary<string,string>{{"Authorization",Authorization(server)},{"X-Emby-Token",server.Token}};DownloadSecurity.ValidateHeaders(headers);
        foreach(var h in headers)request.Headers.TryAddWithoutValidation(h.Key,h.Value);return await Read(request,ct);
    }
    async Task<JsonElement> Plex(string url,string deviceId,string token,HttpContent? content,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(content==null?HttpMethod.Get:HttpMethod.Post,url){Content=content};request.Headers.TryAddWithoutValidation("X-Plex-Product","Noir");request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier",deviceId);request.Headers.TryAddWithoutValidation("Accept","application/json");if(token.Length>0)request.Headers.TryAddWithoutValidation("X-Plex-Token",token);return await Read(request,ct);
    }
    async Task<JsonElement> Read(HttpRequestMessage request,CancellationToken ct)
    {
        try{using var response=await http.SendAsync(request,ct);if(!response.IsSuccessStatusCode)throw new IOException(response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden?"The media server rejected this account. Reconnect in PC Settings.":"Couldn't read the media server. Check its address and permissions.");using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));return doc.RootElement.Clone();}
        catch(HttpRequestException){throw new IOException("Couldn't connect to the media server. Check its address and connection.");}catch(JsonException){throw new IOException("The media server returned an unreadable response.");}catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new IOException("The media server request timed out. Retry shortly.");}
    }
    static List<JsonElement> Array(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var a)&&a.ValueKind==JsonValueKind.Array?a.EnumerateArray().Select(x=>x.Clone()).ToList():[];
    static string Text(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    static string Scalar(JsonElement e,string key)=>e.TryGetProperty(key,out var v)?v.ToString():"";
    static long Number(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt64(out var n)?n:0;
    static bool Flag(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&(v.ValueKind==JsonValueKind.True||v.ValueKind==JsonValueKind.Number&&v.GetInt32()==1);
}

internal static class PlanningRequests
{
    static readonly AsyncLocal<Scope?> current=new();
    sealed class Scope(Scope? previous):IDisposable
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<(object Owner,string Key,Type Type),object> Requests=new();
        public void Dispose()=>current.Value=previous;
    }
    public static IDisposable Begin(){var scope=new Scope(current.Value);current.Value=scope;return scope;}
    public static Task<T> Read<T>(object owner,string key,Func<Task<T>> fetch)=>current.Value is {} scope?((Lazy<Task<T>>)scope.Requests.GetOrAdd((owner,key,typeof(T)),_=>new Lazy<Task<T>>(fetch))).Value:fetch();
}
