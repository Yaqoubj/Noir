using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Noir;
public sealed class DiscoverTitle
{
    public string Id {get;set;}="";
    public string Type {get;set;}="movie";
    public string Name {get;set;}="";
    public string Years {get;set;}="";
    public string PosterUrl {get;set;}="";
    public string BackgroundUrl {get;set;}="";
    public string Poster {get;set;}="";
    public string Background {get;set;}="";
    public string Summary {get;set;}="";
    public string Rating {get;set;}="";
    public List<string> Genres {get;set;}=[];
    public List<KnownEpisode> Episodes {get;set;}=[];
    public int Year=>int.TryParse(Years.Length>=4?Years[..4]:"",out var n)?n:0;
    public bool IsMovie=>Type=="movie";
}
public sealed record DiscoverResults(List<DiscoverTitle> Titles,bool Cached,string Message);
public sealed class DiscoveryClient : IDisposable
{
    readonly HttpClient http;readonly string directory;readonly MetadataClient artwork;
    static readonly SemaphoreSlim artworkSlots=new(4);
    public DiscoveryClient(string dataDirectory,HttpMessageHandler? handler=null){directory=Path.Combine(dataDirectory,"discover");Directory.CreateDirectory(directory);http=handler==null?new():new(handler);http.Timeout=TimeSpan.FromSeconds(15);http.DefaultRequestHeaders.UserAgent.ParseAdd("Noir/0.12");artwork=new(dataDirectory,handler==null?null:new NonDisposingHandler(handler));}
    sealed class NonDisposingHandler(HttpMessageHandler inner):DelegatingHandler(inner){protected override void Dispose(bool disposing){} }
    static string S(JsonElement j,string key)=>j.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    static int N(JsonElement j,string key)=>j.TryGetProperty(key,out var v)?v.TryNumber():0;
    public static DiscoverTitle Parse(JsonElement j,string type)
    {
        var title=new DiscoverTitle{Id=S(j,"id"),Type=type,Name=S(j,"name"),Years=S(j,"releaseInfo"),PosterUrl=S(j,"poster"),BackgroundUrl=S(j,"background"),Summary=MetadataClient.Plain(S(j,"description")),Rating=S(j,"imdbRating")};
        if(j.TryGetProperty("genres",out var genres)&&genres.ValueKind==JsonValueKind.Array)title.Genres=genres.EnumerateArray().Where(g=>g.ValueKind==JsonValueKind.String).Select(g=>g.GetString()!).ToList();
        if(j.TryGetProperty("videos",out var videos)&&videos.ValueKind==JsonValueKind.Array)foreach(var video in videos.EnumerateArray()){
            var season=N(video,"season");var episode=N(video,"episode");if(episode==0)episode=N(video,"number");if(season<=0||episode<=0)continue;
            var date=S(video,"released");if(date.Length==0)date=S(video,"firstAired");title.Episodes.Add(new(){Season=season,Episode=episode,Title=S(video,"name"),Image=S(video,"thumbnail"),AirsAt=DateTimeOffset.TryParse(date,out var aired)?aired:null});
        }
        title.Episodes=title.Episodes.DistinctBy(e=>(e.Season,e.Episode)).OrderBy(e=>e.Season).ThenBy(e=>e.Episode).ToList();return title;
    }
    async Task<(string Json,bool Cached)> Read(string path,TimeSpan fresh,CancellationToken token)
    {
        var cache=Path.Combine(directory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))+".json");
        if(File.Exists(cache)&&File.GetLastWriteTimeUtc(cache)>DateTime.UtcNow-fresh)return(await File.ReadAllTextAsync(cache,token),true);
        try{using var response=await http.GetAsync("https://v3-cinemeta.strem.io/"+path,token);response.EnsureSuccessStatusCode();var json=await response.Content.ReadAsStringAsync(token);using var valid=JsonDocument.Parse(json);var temp=cache+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllTextAsync(temp,json,token);File.Move(temp,cache,true);}finally{if(File.Exists(temp))File.Delete(temp);}return(json,false);}
        catch(Exception e)when(!token.IsCancellationRequested&&File.Exists(cache)&&e is HttpRequestException or TaskCanceledException){return(await File.ReadAllTextAsync(cache,token),true);}
    }
    public async Task<DiscoverResults> Search(string query,string filter,CancellationToken token)
    {
        var types=filter=="Movies"?new[]{"movie"}:filter=="TV shows"?new[]{"series"}:new[]{"movie","series"};
        var outcomes=await Task.WhenAll(types.Select(async type=>{try{
            var path=$"catalog/{type}/top/"+(query.Trim().Length>0?"search="+Uri.EscapeDataString(query.Trim()):"skip=0")+".json";
            var response=await Read(path,TimeSpan.FromHours(6),token);using var doc=JsonDocument.Parse(response.Json);var list=new List<DiscoverTitle>();
            if(doc.RootElement.TryGetProperty("metas",out var metas)&&metas.ValueKind==JsonValueKind.Array)list=metas.EnumerateArray().Select(m=>Parse(m,type)).Where(t=>Regex.IsMatch(t.Id,@"^tt\d+$")).ToList();
            return new DiscoverResults(list,response.Cached,"");
        }catch(Exception e)when(!token.IsCancellationRequested&&e is HttpRequestException or TaskCanceledException or JsonException){return new DiscoverResults([],false,type=="movie"?"Movie search is temporarily unavailable":"TV search is temporarily unavailable");}}));
        // Interleave popular movies/shows instead of burying one type below the other.
        var all=new List<DiscoverTitle>();for(int i=0;i<outcomes.Max(o=>o.Titles.Count);i++)foreach(var o in outcomes)if(i<o.Titles.Count)all.Add(o.Titles[i]);
        return new(all.DistinctBy(t=>(t.Id,t.Type)).ToList(),outcomes.Any(o=>o.Cached),string.Join(" · ",outcomes.Select(o=>o.Message).Where(s=>s.Length>0)));
    }
    public async Task<DiscoverTitle> Details(DiscoverTitle title,CancellationToken token)
    {
        if(!Regex.IsMatch(title.Id,@"^tt\d+$")||title.Type is not ("movie" or "series"))throw new IOException("Choose a title from search.");
        var response=await Read($"meta/{title.Type}/{title.Id}.json",TimeSpan.FromHours(12),token);using var doc=JsonDocument.Parse(response.Json);if(!doc.RootElement.TryGetProperty("meta",out var meta)||meta.ValueKind!=JsonValueKind.Object)throw new IOException("Title details are unavailable.");var result=Parse(meta,title.Type);result.Poster=title.Poster;await Artwork(result,token,true);return result;
    }
    public async Task Artwork(DiscoverTitle title,CancellationToken token,bool background=false)
    {
        await artworkSlots.WaitAsync(token);try{
        if(title.Poster.Length==0)try{title.Poster=await artwork.EpisodeArt(title.PosterUrl,token);}catch(Exception e)when(!token.IsCancellationRequested&&e is HttpRequestException or TaskCanceledException or IOException){}
        if(background&&title.Background.Length==0)try{title.Background=await artwork.EpisodeArt(title.BackgroundUrl,token);}catch(Exception e)when(!token.IsCancellationRequested&&e is HttpRequestException or TaskCanceledException or IOException){}
        }finally{artworkSlots.Release();}
    }
    public void Dispose(){http.Dispose();artwork.Dispose();}
}
public sealed partial class LibraryStore
{
    public List<VideoItem> AddDiscovered(DiscoverTitle title,bool watchlist)
    {
        if(!Regex.IsMatch(title.Id,@"^tt\d+$"))throw new IOException("This title has no download identity.");
        lock(gate)
        {
            var same=State.Catalog.Values.FirstOrDefault(e=>e.Item.IsMovie==title.IsMovie&&(e.Item.ImdbId==title.Id||MatchTitle(e.Item.Title)==MatchTitle(title.Name)&&(e.Item.Year==title.Year||e.Item.Year==0||title.Year==0)));
            var representative=same?.Item;
            if(representative==null){var first=title.Episodes.FirstOrDefault(e=>e.AirsAt==null||e.AirsAt<=DateTimeOffset.UtcNow)??title.Episodes.FirstOrDefault();representative=Remember(new(){Id="discover-"+title.Type+"-"+title.Id,CollectionKey=(title.IsMovie?"movie:":"show:")+title.Id,Title=title.Name,Year=title.Year,IsMovie=title.IsMovie,ImdbId=title.Id,Available=false,Season=first?.Season??1,Episode=title.IsMovie?0:first?.Episode??1,EpisodeTitle=first?.Title??""}).Item;}
            var group=representative.GroupKey;
            if(title.Episodes.Count>0){var known=State.EpisodeSchedules.GetValueOrDefault(group)??[];State.EpisodeSchedules[group]=title.Episodes.Concat(known).DistinctBy(e=>(e.Season,e.Episode)).ToList();}
            foreach(var entry in State.Catalog.Values.Where(e=>e.Item.GroupKey==group)){var item=entry.Item;if(item.ImdbId.Length==0)item.ImdbId=title.Id;if(title.Poster.Length>0)item.Poster=title.Poster;if(title.Background.Length>0)item.Backdrop=title.Background;if(title.Summary.Length>0)item.Summary=title.Summary;if(title.Genres.Count>0)item.Genres=string.Join(" · ",title.Genres);item.ReviewTitle=title.Name;item.SourceName="Cinemeta";item.SourceUrl="https://www.imdb.com/title/"+title.Id+"/";entry.MetadataCheckedAt=DateTimeOffset.UtcNow;}
            if(watchlist)State.Watchlist.Add(group);Save();return EpisodesFor(group);
        }
    }
}
