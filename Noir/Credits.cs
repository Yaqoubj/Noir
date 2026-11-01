using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Noir;
public sealed class CreditPerson
{
    public string Name {get;set;}="";public string Character {get;set;}="";public string Photo {get;set;}="";public string PhotoUrl {get;set;}="";public int TmdbId {get;set;}public int TvmazeId {get;set;}
}
public sealed class TitleCredits
{
    public List<CreditPerson> Cast {get;set;}=[];public List<CreditPerson> Directors {get;set;}=[];public List<CreditPerson> Writers {get;set;}=[];
}
public sealed class CreditTitle
{
    public string ImdbId {get;set;}="";public string Name {get;set;}="";public int Year {get;set;}public bool IsMovie {get;set;}public string Poster {get;set;}="";public string PosterUrl {get;set;}="";public string Role {get;set;}="";public int SourceId {get;set;}
}
internal sealed class DetailHttp : IDisposable
{
    readonly HttpClient http;readonly string cache,key;const int MaxBytes=2_000_000;static readonly SemaphoreSlim gate=new(4);static readonly SemaphoreSlim[] fetches=Enumerable.Range(0,64).Select(_=>new SemaphoreSlim(1,1)).ToArray();
    public DetailHttp(string directory,string key,HttpMessageHandler? handler){cache=Path.Combine(directory,"credits");Directory.CreateDirectory(cache);this.key=key.Trim();http=handler==null?new(new HttpClientHandler{AllowAutoRedirect=false}):new(handler,false);http.Timeout=TimeSpan.FromSeconds(12);http.DefaultRequestHeaders.UserAgent.ParseAdd("Noir/0.14");}
    public async Task<JsonElement> Get(string url,CancellationToken ct)
    {
        var uri=new Uri(url);if(uri.Scheme!="https"||uri.UserInfo.Length>0||!uri.IsDefaultPort||uri.Host is not ("v3-cinemeta.strem.io" or "api.tvmaze.com" or "api.themoviedb.org"))throw new IOException("Unsupported credit source.");
        var file=Path.Combine(cache,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".json");
        async Task<JsonElement> Read(){if(new FileInfo(file).Length>MaxBytes)throw new IOException("Credit cache is too large.");using var d=JsonDocument.Parse(await File.ReadAllTextAsync(file,ct).ConfigureAwait(false));return d.RootElement.Clone();}
        if(File.Exists(file)&&File.GetLastWriteTimeUtc(file)>DateTime.UtcNow.AddDays(-7))try{return await Read().ConfigureAwait(false);}catch(Exception e)when(e is JsonException or IOException){}
        var keyed=fetches[(int)((uint)StringComparer.Ordinal.GetHashCode(file)%fetches.Length)];await keyed.WaitAsync(ct).ConfigureAwait(false);try{
        if(File.Exists(file)&&File.GetLastWriteTimeUtc(file)>DateTime.UtcNow.AddDays(-7))try{return await Read().ConfigureAwait(false);}catch(Exception e)when(e is JsonException or IOException){}
        await gate.WaitAsync(ct).ConfigureAwait(false);try{
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(12));var bodyToken=timeout.Token;
            var request=url;if(uri.Host=="api.themoviedb.org"&&key.Length<=40)request+=(url.Contains('?')?"&":"?")+"api_key="+Uri.EscapeDataString(key);
            using var message=new HttpRequestMessage(HttpMethod.Get,request);if(uri.Host=="api.themoviedb.org"&&key.Length>40)message.Headers.Authorization=new("Bearer",key);using var response=await http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,bodyToken).ConfigureAwait(false);response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>MaxBytes)throw new IOException("Credit response is too large.");
            using var stream=await response.Content.ReadAsStreamAsync(bodyToken).ConfigureAwait(false);using var bytes=new MemoryStream();var buffer=new byte[16_384];int count;while((count=await stream.ReadAsync(buffer,bodyToken).ConfigureAwait(false))>0){if(bytes.Length+count>MaxBytes)throw new IOException("Credit response is too large.");bytes.Write(buffer,0,count);}
            using var doc=JsonDocument.Parse(bytes.ToArray());var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllBytesAsync(temp,bytes.ToArray(),ct).ConfigureAwait(false);File.Move(temp,file,true);}finally{if(File.Exists(temp))File.Delete(temp);}Trim();return doc.RootElement.Clone();
        }catch(Exception e)when(!ct.IsCancellationRequested&&File.Exists(file)&&File.GetLastWriteTimeUtc(file)>DateTime.UtcNow.AddDays(-30)&&e is HttpRequestException or IOException or TaskCanceledException){return await Read().ConfigureAwait(false);}finally{gate.Release();}
        }finally{keyed.Release();}
    }
    void Trim(){try{long total=0;int count=0;foreach(var f in new DirectoryInfo(cache).EnumerateFiles("*.json").OrderByDescending(f=>f.LastWriteTimeUtc)){total+=f.Length;if(++count>256||total>24_000_000)try{f.Delete();}catch(IOException){}}}catch(IOException){}}
    public void Dispose()=>http.Dispose();
}
public sealed class CreditsClient : IDisposable
{
    readonly string directory,key;readonly DetailHttp http;readonly MetadataClient art;
    public CreditsClient(string dataDirectory,string tmdbKey="",HttpMessageHandler? handler=null){directory=dataDirectory;key=tmdbKey.Trim();http=new(dataDirectory,key,handler);art=new(dataDirectory,handler==null?null:new ArtHandler(handler));}
    sealed class ArtHandler(HttpMessageHandler inner):DelegatingHandler(inner){protected override void Dispose(bool disposing){}}
    internal static JsonElement C(JsonElement j,string k)=>j.ValueKind==JsonValueKind.Object&&j.TryGetProperty(k,out var v)?v:default;
    internal static string S(JsonElement j,string k)=>C(j,k).ValueKind==JsonValueKind.String?C(j,k).GetString()??"":"";
    internal static int N(JsonElement j,string k)=>C(j,k).ValueKind==JsonValueKind.Number&&C(j,k).TryGetInt32(out var n)?n:int.TryParse(S(j,k),out n)?n:0;
    internal static IEnumerable<JsonElement> A(JsonElement j)=>j.ValueKind==JsonValueKind.Array?j.EnumerateArray():[];
    internal static string Secure(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var u)&&u.Scheme=="https"&&u.UserInfo.Length==0&&u.IsDefaultPort?url:"";
    static string First(params string[] values)=>values.FirstOrDefault(v=>v.Length>0)??"";
    static string Image(JsonElement j)=>Secure(First(S(j,"photo"),S(j,"photoUrl"),S(j,"image"),S(j,"profile"),S(C(j,"image"),"original"),S(C(j,"image"),"medium")));
    static CreditPerson ParsePerson(JsonElement j)=>j.ValueKind==JsonValueKind.String?new(){Name=j.GetString()??""}:new(){Name=First(S(j,"name"),S(j,"personName")),Character=First(S(j,"character"),S(j,"role"),S(C(j,"character"),"name")),PhotoUrl=Image(j),TmdbId=N(j,"tmdb_id"),TvmazeId=N(j,"tvmaze_id")};
    static List<CreditPerson> People(JsonElement j)
    {
        if(j.ValueKind==JsonValueKind.String)return j.GetString()!.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Take(80).Select(n=>new CreditPerson{Name=n}).ToList();
        if(j.ValueKind==JsonValueKind.Object&&C(j,"name").ValueKind==JsonValueKind.Undefined)return j.EnumerateObject().Take(80).Select(p=>{var person=ParsePerson(p.Value);if(person.Name.Length==0)person.Name=p.Name;return person;}).ToList();
        return (j.ValueKind==JsonValueKind.Object?new[]{j}:A(j)).Take(80).Select(ParsePerson).Where(p=>p.Name.Length>0).ToList();
    }
    static void MergePeople(List<CreditPerson> into,IEnumerable<CreditPerson> added)
    {
        foreach(var p in added.Where(p=>p.Name.Length>0).Take(80)){var old=into.FirstOrDefault(x=>x.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase));if(old==null){if(into.Count<80)into.Add(p);}else{if(old.PhotoUrl.Length==0)old.PhotoUrl=p.PhotoUrl;if(old.Character.Length==0)old.Character=p.Character;if(old.TmdbId==0)old.TmdbId=p.TmdbId;if(old.TvmazeId==0)old.TvmazeId=p.TvmazeId;}}
    }
    public static TitleCredits ParseCinemeta(JsonElement json)
    {
        var m=C(json,"meta");if(m.ValueKind!=JsonValueKind.Object)m=json;var result=new TitleCredits();MergePeople(result.Cast,People(C(C(m,"app_extras"),"cast")));MergePeople(result.Cast,People(C(m,"credits_cast")));MergePeople(result.Cast,People(C(m,"cast")));MergePeople(result.Directors,People(C(m,"director")));MergePeople(result.Writers,People(C(m,"writer")));return result;
    }
    public static TitleCredits ParseTvmaze(JsonElement json)
    {
        var result=new TitleCredits();foreach(var entry in A(json).Take(80)){var p=C(entry,"person");var person=ParsePerson(p);person.TvmazeId=N(p,"id");person.Character=S(C(entry,"character"),"name");MergePeople(result.Cast,[person]);}return result;
    }
    public static TitleCredits ParseTmdb(JsonElement json)
    {
        CreditPerson Person(JsonElement p)=>new(){Name=S(p,"name"),Character=S(p,"character"),TmdbId=N(p,"id"),PhotoUrl=S(p,"profile_path").StartsWith('/')?"https://image.tmdb.org/t/p/w185"+S(p,"profile_path"):""};
        var result=new TitleCredits();MergePeople(result.Cast,A(C(json,"cast")).OrderBy(x=>N(x,"order")).Select(Person));foreach(var p in A(C(json,"crew"))){var job=S(p,"job");if(job is "Director" or "Series Director")MergePeople(result.Directors,[Person(p)]);if(S(p,"department")=="Writing"||job is "Writer" or "Screenplay" or "Story" or "Creator")MergePeople(result.Writers,[Person(p)]);}return result;
    }
    static void Merge(TitleCredits a,TitleCredits b){MergePeople(a.Cast,b.Cast);MergePeople(a.Directors,b.Directors);MergePeople(a.Writers,b.Writers);}
    internal async Task<int> Find(string imdb,bool movie,CancellationToken ct){var j=await http.Get("https://api.themoviedb.org/3/find/"+imdb+"?external_source=imdb_id",ct).ConfigureAwait(false);return A(C(j,movie?"movie_results":"tv_results")).Select(x=>N(x,"id")).FirstOrDefault();}
    static bool Recoverable(Exception e)=>e is HttpRequestException or IOException or JsonException or TaskCanceledException;
    public Task<TitleCredits> Title(string imdb,bool movie,CancellationToken ct=default)=>Task.Run(async()=>{
        var result=new TitleCredits();if(!Regex.IsMatch(imdb,@"^tt\d{1,12}$"))return result;
        try{Merge(result,ParseCinemeta(await http.Get($"https://v3-cinemeta.strem.io/meta/{(movie?"movie":"series")}/{imdb}.json",ct).ConfigureAwait(false)));}catch(Exception e)when(!ct.IsCancellationRequested&&Recoverable(e)){}
        if(!movie)try{var show=await http.Get("https://api.tvmaze.com/lookup/shows?imdb="+imdb,ct).ConfigureAwait(false);var id=N(show,"id");if(id>0)Merge(result,ParseTvmaze(await http.Get($"https://api.tvmaze.com/shows/{id}/cast",ct).ConfigureAwait(false)));}catch(Exception e)when(!ct.IsCancellationRequested&&Recoverable(e)){}
        if(key.Length>0)try{var id=await Find(imdb,movie,ct).ConfigureAwait(false);if(id>0)Merge(result,ParseTmdb(await http.Get($"https://api.themoviedb.org/3/{(movie?"movie":"tv")}/{id}/credits",ct).ConfigureAwait(false)));}catch(Exception e)when(!ct.IsCancellationRequested&&Recoverable(e)){}
        return result;
    },ct);
    // Cache-only lookup for History stats: never touches the network, empty when the title page was never opened.
    public static IReadOnlyList<string> CachedPeople(string dataDirectory,string imdb,bool movie)
    {
        if(!Regex.IsMatch(imdb,@"^tt\d{1,12}$"))return [];
        var url=$"https://v3-cinemeta.strem.io/meta/{(movie?"movie":"series")}/{imdb}.json";var file=Path.Combine(dataDirectory,"credits",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".json");
        try{if(!File.Exists(file)||new FileInfo(file).Length>MaxCache)return [];using var doc=JsonDocument.Parse(File.ReadAllText(file));var credits=ParseCinemeta(doc.RootElement);return credits.Directors.Take(2).Concat(credits.Cast.Take(6)).Select(p=>p.Name).Where(n=>n.Length>0).Distinct().ToList();}
        catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){return [];}
    }
    const int MaxCache=2_000_000;
    public Task<string> Photo(CreditPerson person,CancellationToken ct=default)=>Task.Run(async()=>{if(File.Exists(person.Photo))return person.Photo;try{return await art.EpisodeArt(Secure(person.PhotoUrl),ct).ConfigureAwait(false);}catch(Exception e)when(!ct.IsCancellationRequested&&Recoverable(e)){return "";}},ct);
    public Task<string> Artwork(CreditTitle title,CancellationToken ct=default)=>Task.Run(async()=>{if(File.Exists(title.Poster))return title.Poster;try{return await art.EpisodeArt(Secure(title.PosterUrl),ct).ConfigureAwait(false);}catch(Exception e)when(!ct.IsCancellationRequested&&Recoverable(e)){return "";}},ct);
    public static List<CreditTitle> ParseTmdbFilmography(JsonElement json)
    {
        return A(C(json,"cast")).Concat(A(C(json,"crew"))).Select(j=>{var date=First(S(j,"release_date"),S(j,"first_air_date"));return new CreditTitle{SourceId=N(j,"id"),IsMovie=S(j,"media_type")=="movie",Name=First(S(j,"title"),S(j,"name")),Year=int.TryParse(date.Length>=4?date[..4]:"",out var y)?y:0,Role=First(S(j,"character"),S(j,"job")),PosterUrl=S(j,"poster_path").StartsWith('/')?"https://image.tmdb.org/t/p/w342"+S(j,"poster_path"):""};}).Where(t=>t.Name.Length>0&&t.SourceId>0).DistinctBy(t=>(t.IsMovie,t.SourceId)).OrderByDescending(t=>t.Year).Take(120).ToList();
    }
    public static List<CreditTitle> ParseTvmazeFilmography(JsonElement json)=>A(json).Select(j=>C(C(j,"_embedded"),"show")).Where(j=>j.ValueKind==JsonValueKind.Object).Select(j=>{var date=S(j,"premiered");return new CreditTitle{ImdbId=S(C(j,"externals"),"imdb"),SourceId=N(j,"id"),Name=S(j,"name"),Year=int.TryParse(date.Length>=4?date[..4]:"",out var y)?y:0,PosterUrl=Image(j)};}).Where(t=>t.Name.Length>0).DistinctBy(t=>t.SourceId).OrderByDescending(t=>t.Year).Take(120).ToList();
    public Task<List<CreditTitle>> Person(CreditPerson person,CancellationToken ct=default)=>Task.Run(async()=>{
        var result=new List<CreditTitle>();if(key.Length>0&&person.TmdbId>0)try{result=ParseTmdbFilmography(await http.Get($"https://api.themoviedb.org/3/person/{person.TmdbId}/combined_credits",ct).ConfigureAwait(false));}catch(Exception e)when(!ct.IsCancellationRequested&&Recoverable(e)){}
        if(result.Count==0&&person.TvmazeId>0)try{result=ParseTvmazeFilmography(await http.Get($"https://api.tvmaze.com/people/{person.TvmazeId}/castcredits?embed=show",ct).ConfigureAwait(false));}catch(Exception e)when(!ct.IsCancellationRequested&&Recoverable(e)){}
        foreach(var t in KnownFor(person.Name,ct))if(result.All(x=>x.ImdbId!=t.ImdbId||x.ImdbId.Length==0))result.Add(t);return result.OrderByDescending(t=>t.Year).ToList();
    },ct);
    public Task<string> ResolveImdb(CreditTitle title,CancellationToken ct=default)=>Task.Run(async()=>{if(Regex.IsMatch(title.ImdbId,@"^tt\d{1,12}$"))return title.ImdbId;if(key.Length==0||title.SourceId<=0)return "";try{var json=await http.Get($"https://api.themoviedb.org/3/{(title.IsMovie?"movie":"tv")}/{title.SourceId}/external_ids",ct).ConfigureAwait(false);var id=S(json,"imdb_id");return Regex.IsMatch(id,@"^tt\d{1,12}$")?id:"";}catch(Exception e)when(!ct.IsCancellationRequested&&Recoverable(e)){return "";}},ct);
    List<CreditTitle> KnownFor(string name,CancellationToken ct)
    {
        var result=new List<CreditTitle>();if(name.Length==0)return result;long bytes=0;foreach(var folder in new[]{Path.Combine(directory,"credits"),Path.Combine(directory,"discover")}){if(!Directory.Exists(folder))continue;foreach(var path in Directory.EnumerateFiles(folder,"*.json").Take(400)){ct.ThrowIfCancellationRequested();try{var f=new FileInfo(path);bytes+=f.Length;if(bytes>24_000_000)break;if(f.Length>2_000_000)continue;using var doc=JsonDocument.Parse(File.ReadAllText(path,Encoding.UTF8));var meta=C(doc.RootElement,"meta");var entries=meta.ValueKind==JsonValueKind.Object?new[]{meta}:A(C(doc.RootElement,"metas"));foreach(var m in entries){var credit=ParseCinemeta(m);if(!credit.Cast.Concat(credit.Directors).Concat(credit.Writers).Any(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))continue;var id=S(m,"id");if(!Regex.IsMatch(id,@"^tt\d+$"))continue;var release=S(m,"releaseInfo");result.Add(new(){ImdbId=id,Name=S(m,"name"),Year=int.TryParse(release.Length>=4?release[..4]:"",out var y)?y:0,IsMovie=S(m,"type")=="movie",PosterUrl=Secure(S(m,"poster"))});}}catch(Exception e)when(e is IOException or JsonException){}}}return result.DistinctBy(t=>t.ImdbId).Take(120).ToList();
    }
    public void Dispose(){http.Dispose();art.Dispose();}
}
