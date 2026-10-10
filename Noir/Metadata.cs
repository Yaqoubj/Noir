using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public sealed class OnlineDetails
{
    public List<KnownEpisode> Schedule { get; set; } = [];
    public string Summary { get; set; }="";
    public string Poster { get; set; }="";
    public string Backdrop { get; set; }="";
    public string Genres { get; set; }="";
    public int Year { get; set; }
    public string SourceName { get; set; }="";
    public string SourceUrl { get; set; }="";
    public string Title { get; set; }="";
    public string ImdbId { get; set; }="";
    public Dictionary<string,(string Title,string Summary)> Episodes { get; }=new();
}

public sealed class MetadataClient : IDisposable
{
    readonly HttpClient http;
    readonly string artDirectory;
    readonly bool refreshArtwork;
    public MetadataClient(string dataDirectory, HttpMessageHandler? handler=null,bool refreshArtwork=false)
    {
        this.refreshArtwork=refreshArtwork;
        http=handler==null?new HttpClient():new HttpClient(handler);
        http.Timeout=TimeSpan.FromSeconds(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Noir/0.4 (personal local media library)");
        artDirectory=Path.Combine(dataDirectory,"art");Directory.CreateDirectory(artDirectory);
    }
    static string Value(JsonElement e,string key) => e.ValueKind==JsonValueKind.Object && e.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    static JsonElement Child(JsonElement e,string key) => e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var v)?v:default;
    static int Number(JsonElement e,string key) => Child(e,key).TryNumber();
    public static string Plain(string value) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(value,"<[^>]+>"," ")),@"\s+"," ").Trim();
    static string Normal(string value) => Regex.Replace(value.ToUpperInvariant(),@"[^\p{L}\p{N}]","");
    async Task<JsonDocument> Json(string url,CancellationToken token)
    {
        using var response=await http.GetAsync(url,token);
        if((int)response.StatusCode==429)
        {
            await Task.Delay(TimeSpan.FromSeconds(3),token);
            using var retry=await http.GetAsync(url,token);retry.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await retry.Content.ReadAsStringAsync(token));
        }
        response.EnsureSuccessStatusCode();return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }
    public async Task<OnlineDetails?> Fetch(VideoItem item,CancellationToken token=default) => item.IsMovie?await Movie(item,token):await Show(item,token);
    public async Task<string> DownloadIdentity(VideoItem item,CancellationToken token=default)
    {
        if(Regex.IsMatch(item.ImdbId,@"^tt\d+$"))return item.ImdbId;
        if(!item.IsMovie){
            var match=Regex.Match(item.SourceUrl,@"^https://(?:www\.)?tvmaze\.com/shows/(\d+)(?:/|$)");
            if(match.Success){using var doc=await Json("https://api.tvmaze.com/shows/"+match.Groups[1].Value,token);var id=Value(Child(doc.RootElement,"externals"),"imdb");if(Regex.IsMatch(id,@"^tt\d+$"))return id;}
            using var search=await Json("https://api.tvmaze.com/search/shows?q="+Uri.EscapeDataString(item.Title),token);
            var matches=search.RootElement.EnumerateArray().Select(x=>Child(x,"show")).Where(x=>Normal(Value(x,"name"))==Normal(item.Title)&&(item.Year==0||Value(x,"premiered").StartsWith(item.Year.ToString()))).ToList();
            if(matches.Count==1){var id=Value(Child(matches[0],"externals"),"imdb");if(Regex.IsMatch(id,@"^tt\d+$"))return id;}
        }
        // Cinemeta supplies IMDb identities. Only a unique exact title/year match is accepted.
        using var catalog=await Json("https://v3-cinemeta.strem.io/catalog/"+(item.IsMovie?"movie":"series")+"/top/search="+Uri.EscapeDataString(item.Title)+".json",token);
        var metas=Child(catalog.RootElement,"metas");if(metas.ValueKind!=JsonValueKind.Array)return "";
        var candidates=metas.EnumerateArray().Where(m=>Normal(Value(m,"name"))==Normal(item.Title)&&(item.Year==0||Value(m,"releaseInfo").StartsWith(item.Year.ToString()))).ToList();
        return candidates.Count==1&&Regex.IsMatch(Value(candidates[0],"id"),@"^tt\d+$")?Value(candidates[0],"id"):"";
    }
    async Task<OnlineDetails?> Show(VideoItem item,CancellationToken token)
    {
        var title=LibraryStore.CleanShowTitle(item.Title);
        using var search=await Json("https://api.tvmaze.com/search/shows?q="+Uri.EscapeDataString(title),token);
        var candidates=search.RootElement.EnumerateArray().Select(x=>Child(x,"show")).Where(x=>Normal(Value(x,"name"))==Normal(title))
            .Where(x=>item.Year==0 || Value(x,"premiered").StartsWith(item.Year.ToString())).ToList();
        if(candidates.Count!=1) return null; // Do not silently attach a different remake's artwork.
        var show=candidates[0];var id=Number(show,"id");
        var result=new OnlineDetails {Summary=Plain(Value(show,"summary")),SourceName="TVmaze",SourceUrl=Value(show,"url"),
            Title=Value(show,"name"),ImdbId=Value(Child(show,"externals"),"imdb"),
            Year=int.TryParse(Value(show,"premiered").Split('-')[0],out var year)?year:0};
        var genres=Child(show,"genres");if(genres.ValueKind==JsonValueKind.Array) result.Genres=string.Join(" · ",genres.EnumerateArray().Select(x=>x.GetString()));
        result.Poster=File.Exists(item.Poster)&&!refreshArtwork?item.Poster:await Download(Value(Child(show,"image"),"original"),token);
        if(File.Exists(item.Backdrop)&&!refreshArtwork) result.Backdrop=item.Backdrop;
        using(var episodes=await Json($"https://api.tvmaze.com/shows/{id}/episodes",token))
        {
            foreach(var ep in episodes.RootElement.EnumerateArray())
            {
                var season=Number(ep,"season");var number=Number(ep,"number");if(number==0) continue;
                result.Episodes[$"{season}:{number}"]=(Value(ep,"name"),Plain(Value(ep,"summary")));
                result.Schedule.Add(new(){Season=season,Episode=number,Title=Value(ep,"name"),Image=Value(Child(ep,"image"),"original"),AirsAt=DateTimeOffset.TryParse(Value(ep,"airstamp"),out var airs)?airs:null});
                if((!File.Exists(item.Backdrop)||refreshArtwork) && season==item.Season && number==item.Episode)
                    result.Backdrop=await Download(Value(Child(ep,"image"),"original"),token);
            }
        }
        if(result.Backdrop.Length==0)
        {
            using var pictures=await Json($"https://api.tvmaze.com/shows/{id}/images",token);
            var background=pictures.RootElement.EnumerateArray().FirstOrDefault(x=>Value(x,"type")=="background");
            result.Backdrop=await Download(Value(Child(Child(background,"resolutions"),"original"),"url"),token);
        }
        return result;
    }
    async Task<OnlineDetails?> Movie(VideoItem item,CancellationToken token)
    {
        // Wikipedia is available without a personal API key. Existing imported movie art remains cached.
        using var search=await Json("https://en.wikipedia.org/w/rest.php/v1/search/title?q="+Uri.EscapeDataString(item.Title)+"&limit=8",token);
        var pages=Child(search.RootElement,"pages");if(pages.ValueKind!=JsonValueKind.Array) return null;
        var choices=pages.EnumerateArray().Where(p=>Regex.IsMatch(Value(p,"description"),@"(?i)\b(film|movie)\b")).ToList();
        string BaseTitle(JsonElement page)=>Normal(Regex.Replace(Value(page,"title"),@"\s*\([^)]*\)$",""));
        var exact=choices.Where(p=>BaseTitle(p)==Normal(item.Title)).ToList();
        choices=exact.Count>0?exact:choices.Where(p=>OneEdit(BaseTitle(p),Normal(item.Title))).ToList();
        if(item.Year>0) choices=choices.Where(p=>Value(p,"description").Contains(item.Year.ToString())).ToList();
        if(choices.Count!=1) return null;
        foreach(var page in choices)
        {
            var title=Value(page,"title");var baseTitle=Regex.Replace(title,@"\s*\([^)]*\)$","");
            using var summary=await Json("https://en.wikipedia.org/api/rest_v1/page/summary/"+Uri.EscapeDataString(title),token);
            var entry=summary.RootElement;var extract=Value(entry,"extract");
            if(!Regex.IsMatch(Value(entry,"description")+" "+extract,@"(?i)\b(film|movie)\b") || Value(entry,"type")=="disambiguation") continue;
            var match=Regex.Match(extract,@"\b(19\d{2}|20\d{2})\b");var year=match.Success?int.Parse(match.Value):0;
            if(item.Year>0 && year>0 && item.Year!=year) continue;
            var imdb="";
            try
            {
                using var links=await Json("https://en.wikipedia.org/w/api.php?action=parse&format=json&prop=externallinks&page="+Uri.EscapeDataString(title),token);
                var externals=Child(Child(links.RootElement,"parse"),"externallinks");
                if(externals.ValueKind==JsonValueKind.Array)
                    imdb=externals.EnumerateArray().Select(e=>Regex.Match(e.GetString()??"",@"imdb\.com/title/(tt\d+)"))
                        .FirstOrDefault(m=>m.Success)?.Groups[1].Value??"";
            }
            catch(HttpRequestException) { }
            return new OnlineDetails {Title=title,ImdbId=imdb,Summary=extract,Year=year,SourceName="Wikipedia",SourceUrl=Value(Child(Child(entry,"content_urls"),"desktop"),"page"),
                Poster=File.Exists(item.Poster)&&!refreshArtwork?item.Poster:await Download(Value(Child(entry,"thumbnail"),"source"),token)};
        }
        return null;
    }
    static bool OneEdit(string a,string b)
    {
        if(Math.Min(a.Length,b.Length)<6 || Math.Abs(a.Length-b.Length)>1) return false;
        if(a.Length>b.Length) (a,b)=(b,a);
        int i=0,j=0,edits=0;
        while(i<a.Length && j<b.Length)
        {
            if(a[i]==b[j]) {i++;j++;continue;}
            if(++edits>1) return false;
            if(a.Length==b.Length) i++;j++;
        }
        return edits+b.Length-j+a.Length-i<=1;
    }
    public Task<string> EpisodeArt(string url,CancellationToken token)=>Download(url,token);
    async Task<string> Download(string url,CancellationToken token)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https") return "";
        var path=Path.Combine(artDirectory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".jpg");
        if(File.Exists(path)&&!refreshArtwork) return path;
        using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>20_000_000 || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/")!=true) return "";
        var bytes=await response.Content.ReadAsByteArrayAsync(token);
        if(bytes.Length==0 || bytes.Length>20_000_000) return "";
        if(refreshArtwork) path=Path.Combine(artDirectory,Convert.ToHexString(SHA256.HashData(bytes))+".jpg");
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllBytesAsync(temp,bytes,token);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}return path;
    }
    public void Dispose()=>http.Dispose();
}

static class JsonNumber
{
    public static int TryNumber(this JsonElement element)=>element.ValueKind==JsonValueKind.Number&&element.TryGetInt32(out var n)?n:0;
}
