using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noir;
public sealed record WatchProvider(string Name,string Logo,string Kind);
public sealed record WatchProviders(string Link,List<WatchProvider> Providers);
// TMDB is optional: with the user's free API key Noir gets real "more like this" lists and, via
// TMDB's JustWatch data, which services stream a title in the user's country.
public sealed class TmdbClient : IDisposable
{
    readonly HttpClient http;readonly string key,cache;
    public TmdbClient(string apiKey,string dataDirectory,HttpMessageHandler? handler=null)
    {
        key=apiKey.Trim();cache=Path.Combine(dataDirectory,"tmdb");Directory.CreateDirectory(cache);
        http=handler==null?new():new(handler,false);http.Timeout=TimeSpan.FromSeconds(15);
        // Long keys are v4 read-access tokens; short ones are v3 API keys passed as a query parameter.
        if(key.Length>40)http.DefaultRequestHeaders.Authorization=new("Bearer",key);
    }
    async Task<JsonElement> Get(string path,TimeSpan fresh,CancellationToken ct)
    {
        var file=Path.Combine(cache,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))+".json");
        if(File.Exists(file)&&File.GetLastWriteTimeUtc(file)>DateTime.UtcNow-fresh){using var cached=JsonDocument.Parse(await File.ReadAllTextAsync(file,ct));return cached.RootElement.Clone();}
        var url="https://api.themoviedb.org/3/"+path+(key.Length>40?"":(path.Contains('?')?"&":"?")+"api_key="+Uri.EscapeDataString(key));
        using var response=await http.GetAsync(url,ct);
        if(response.StatusCode==System.Net.HttpStatusCode.Unauthorized)throw new IOException("TMDB rejected the API key. Check it in Settings → Discover.");
        response.EnsureSuccessStatusCode();var json=await response.Content.ReadAsStringAsync(ct);using var doc=JsonDocument.Parse(json);
        await File.WriteAllTextAsync(file,json,ct);return doc.RootElement.Clone();
    }
    static string S(JsonElement j,string k)=>j.ValueKind==JsonValueKind.Object&&j.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    public async Task<int> Find(string imdb,bool movie,CancellationToken ct)
    {
        var found=await Get($"find/{imdb}?external_source=imdb_id",TimeSpan.FromDays(30),ct);
        return found.TryGetProperty(movie?"movie_results":"tv_results",out var list)&&list.ValueKind==JsonValueKind.Array&&list.GetArrayLength()>0&&list[0].TryGetProperty("id",out var id)?id.GetInt32():0;
    }
    public async Task<List<string>> Recommendations(string imdb,bool movie,CancellationToken ct)
    {
        var id=await Find(imdb,movie,ct);if(id==0)return [];var type=movie?"movie":"tv";
        var page=await Get($"{type}/{id}/recommendations",TimeSpan.FromDays(3),ct);
        var ids=page.TryGetProperty("results",out var results)&&results.ValueKind==JsonValueKind.Array?results.EnumerateArray().Take(16).Select(r=>r.GetProperty("id").GetInt32()).ToList():[];
        using var slots=new SemaphoreSlim(4);
        var imdbs=await Task.WhenAll(ids.Select(async other=>{await slots.WaitAsync(ct);try{return S(await Get($"{type}/{other}/external_ids",TimeSpan.FromDays(60),ct),"imdb_id");}catch(Exception e)when(e is HttpRequestException or IOException or JsonException){return "";}finally{slots.Release();}}));
        return imdbs.Where(i=>Regex.IsMatch(i,@"^tt\d+$")).Distinct().ToList();
    }
    public async Task<WatchProviders?> Providers(string imdb,bool movie,string region,CancellationToken ct)
    {
        var id=await Find(imdb,movie,ct);if(id==0)return null;
        var all=await Get($"{(movie?"movie":"tv")}/{id}/watch/providers",TimeSpan.FromDays(1),ct);
        if(!all.TryGetProperty("results",out var results)||!results.TryGetProperty(region,out var local))return new("",[]);
        var list=new List<WatchProvider>();
        foreach(var (kind,label) in new[]{("flatrate","Stream"),("free","Free"),("ads","Free with ads"),("rent","Rent"),("buy","Buy")})
            if(local.TryGetProperty(kind,out var items)&&items.ValueKind==JsonValueKind.Array)
                foreach(var p in items.EnumerateArray())if(list.All(x=>x.Name!=S(p,"provider_name")))list.Add(new(S(p,"provider_name"),S(p,"logo_path"),label));
        return new(S(local,"link"),list);
    }
    public void Dispose()=>http.Dispose();
}
public partial class MainWindow
{
    string TmdbKeyPath=>Path.Combine(store.DataDirectory,"tmdb.dpapi");
    string TmdbKey(){try{return File.Exists(TmdbKeyPath)?Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(TmdbKeyPath),null,DataProtectionScope.CurrentUser)):"";}catch(Exception e){store.Log(e);return "";}}
    static string Region(){try{return System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;}catch(ArgumentException){return "US";}}
    readonly Dictionary<string,List<DiscoverTitle>> recommendationCache=new();
    readonly Dictionary<string,WatchProviders?> providerCache=new();
    // Opens the title (or exact episode) in the Stremio app, or Stremio Web when the app isn't installed.
    void OpenInStremio(string imdb,bool movie,int season=0,int episode=0)
    {
        if(!Regex.IsMatch(imdb,@"^tt\d+$")){Status.Text="This title needs a verified match before it can open in Stremio.";return;}
        var type=movie?"movie":"series";var video=movie?imdb:season>0&&episode>0?$"{imdb}:{season}:{episode}":"";
        var path=$"/detail/{type}/{imdb}"+(video.Length>0?"/"+Uri.EscapeDataString(video):"");
        var installed=false;try{using var handler=Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("stremio");installed=handler!=null;}catch(System.Security.SecurityException){}
        var url=installed?"stremio://"+path:"https://web.stremio.com/#"+path;
        try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});Status.Text=installed?"Opening in Stremio…":"Opening Stremio Web… Install the Stremio app to open titles in it directly.";}
        catch(Exception e){store.Log(e);Status.Text="Stremio couldn't be opened.";}
    }
    // Nuvio's deep link (its AppUrlBridge: nuvio://meta?type=…&id=tt…) opens the title page in Nuvio Desktop
    // or on a phone. Without the desktop app, a QR code hands the same link to the phone.
    void OpenInNuvio(string imdb,bool movie,string name="")
    {
        if(!Regex.IsMatch(imdb,@"^tt\d+$")){Status.Text="This title needs a verified match before it can open in Nuvio.";return;}
        var url=$"nuvio://meta?type={(movie?"movie":"series")}&id={imdb}";
        var installed=false;try{using var handler=Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("nuvio");installed=handler!=null;}catch(System.Security.SecurityException){}
        if(installed)
        {
            try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});Status.Text="Opening in Nuvio…";return;}
            catch(Exception e){store.Log(e);}
        }
        var body=SmartPopup();
        body.Children.Add(Text(T("Open in Nuvio on your phone"),15,weight:FontWeights.SemiBold));
        body.Children.Add(Body((name.Length>0?name+" · ":"")+T("Scan with your phone's camera to open this title in Nuvio. Install Nuvio Desktop to open titles on this PC directly."),12));
        using(var data=QRCoder.QRCodeGenerator.GenerateQrCode(url,QRCoder.QRCodeGenerator.ECCLevel.Q))using(var qr=new QRCoder.PngByteQRCode(data))
        {
            var image=new System.Windows.Media.Imaging.BitmapImage();image.BeginInit();image.StreamSource=new MemoryStream(qr.GetGraphic(6));image.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;image.EndInit();
            body.Children.Add(new Border{Background=Brush("#FFFFFF"),CornerRadius=new(10),Padding=new(8),HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,4,0,10),Child=new Image{Source=image,Width=180,Height=180}});
        }
        var row=new WrapPanel();body.Children.Add(row);
        var get=ActionButton("Get Nuvio",()=>{CloseSmart();try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://nuvio.tv"){UseShellExecute=true});}catch(Exception e){store.Log(e);}},true);get.Margin=new(0,0,8,0);row.Children.Add(get);
        row.Children.Add(ActionButton("Close",CloseSmart));
    }
    // "Where to watch": Stremio first, then the services that stream it here (with a TMDB key).
    FrameworkElement WatchOptions(string imdb,bool movie,string name,int season=0,int episode=0)
    {
        var host=new StackPanel{Margin=new(0,6,0,8)};
        var row=new WrapPanel();host.Children.Add(Text("WHERE TO WATCH",10.5,"#817E78",FontWeights.SemiBold));host.Children.Add(row);row.Margin=new(0,8,0,0);
        var stremio=ActionButton("▶  Stream in Stremio",()=>OpenInStremio(imdb,movie,season,episode),true);stremio.Padding=new(16,9,18,9);stremio.Margin=new(0,0,10,8);stremio.ToolTip="Opens this "+(movie?"movie":"show")+" in Stremio with your add-ons";row.Children.Add(stremio);
        var nuvio=ActionButton("▶  Stream in Nuvio",()=>OpenInNuvio(imdb,movie,name));nuvio.Padding=new(16,9,18,9);nuvio.Margin=new(0,0,10,8);nuvio.ToolTip="Opens this "+(movie?"movie":"show")+" in Nuvio — on this PC with Nuvio Desktop, or on your phone with a QR code";row.Children.Add(nuvio);
        if(!Regex.IsMatch(imdb,@"^tt\d+$")){stremio.IsEnabled=nuvio.IsEnabled=false;return host;}
        var key=TmdbKey();
        if(key.Length==0)
        {
            var find=ActionButton("Find streaming services",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.justwatch.com/"+Region().ToLowerInvariant()+"/search?q="+Uri.EscapeDataString(name)){UseShellExecute=true}));
            find.Padding=new(16,9,18,9);find.Margin=new(0,0,10,8);find.ToolTip="Search JustWatch in your browser. Add a free TMDB key in Settings → Discover to see services here.";row.Children.Add(find);return host;
        }
        _=Fill();return host;
        async Task Fill()
        {
            var cacheKey=imdb+"|"+Region();
            try
            {
                if(!providerCache.TryGetValue(cacheKey,out var providers)){using var tmdb=new TmdbClient(key,store.DataDirectory);providers=providerCache[cacheKey]=await tmdb.Providers(imdb,movie,Region(),CancellationToken.None);}
                if(providers==null||providers.Providers.Count==0){row.Children.Add(Text("  Not streaming on a service in your country",12,"#8F8A83"));((FrameworkElement)row.Children[^1]).VerticalAlignment=VerticalAlignment.Center;return;}
                using var art=new MetadataClient(store.DataDirectory);
                foreach(var p in providers.Providers.Take(10))
                {
                    var chip=new Button{Padding=new(6,5,12,5),Margin=new(0,0,8,8),Background=Brush("#1C1C1F"),ToolTip=p.Kind+" on "+p.Name+" · opens JustWatch's page for this title"};
                    var content=new StackPanel{Orientation=Orientation.Horizontal};chip.Content=content;
                    var logo=p.Logo.Length>0?await Logo(art,p.Logo):"";if(ImageSource(logo,64) is {} source)content.Children.Add(new Border{Width=26,Height=26,CornerRadius=new(6),ClipToBounds=true,Margin=new(0,0,8,0),Child=new Image{Source=source,Stretch=Stretch.UniformToFill}});
                    var label=new StackPanel{VerticalAlignment=VerticalAlignment.Center};label.Children.Add(Text(p.Name,12.5,weight:FontWeights.SemiBold));label.Children.Add(Text(p.Kind,10,"#AAA6A0"));content.Children.Add(label);
                    var link=providers.Link;chip.Click+=(_,_)=>{if(Uri.TryCreate(link,UriKind.Absolute,out var u)&&u.Scheme=="https")System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri){UseShellExecute=true});};
                    row.Children.Add(chip);
                }
                host.Children.Add(Text("Streaming availability: JustWatch via TMDB",10,"#6B665E"));
            }
            catch(Exception e)when(e is HttpRequestException or IOException or JsonException or TaskCanceledException){store.Log(e);row.Children.Add(Text("  "+(e is IOException?e.Message:"Streaming services are unavailable right now."),12,"#8F8A83"));}
        }
    }
    static async Task<string> Logo(MetadataClient art,string path){try{return await art.EpisodeArt("https://image.tmdb.org/t/p/w92"+path,CancellationToken.None);}catch(Exception e)when(e is HttpRequestException or IOException or TaskCanceledException){return "";}}
    // Titles similar to one title: TMDB recommendations with a key, otherwise the best-rated titles
    // that share its genres. Anything already watched is left out.
    async Task<List<DiscoverTitle>> SimilarTo(string imdb,bool movie,List<string> genres,CancellationToken ct)
    {
        var cacheKey=imdb+"|"+(TmdbKey().Length>0);
        if(recommendationCache.TryGetValue(cacheKey,out var known))return known;
        using var discovery=DiscoverClient();var type=movie?"movie":"series";var found=new List<DiscoverTitle>();
        var key=TmdbKey();
        if(key.Length>0)
        {
            try{using var tmdb=new TmdbClient(key,store.DataDirectory);foreach(var id in await tmdb.Recommendations(imdb,movie,ct))try{found.Add(await discovery.Details(new(){Id=id,Type=type},ct));}catch(Exception e)when(e is HttpRequestException or IOException or JsonException){}}
            catch(Exception e)when(e is HttpRequestException or IOException or JsonException){store.Log(e);}
        }
        if(found.Count<6&&genres.Count>0)
        {
            var pool=new List<DiscoverTitle>();foreach(var genre in genres.Take(2))try{pool.AddRange(await discovery.Genre(type,genre,ct));}catch(Exception e)when(e is HttpRequestException or IOException or JsonException){}
            found.AddRange(pool.DistinctBy(t=>t.Id).OrderByDescending(t=>t.Genres.Intersect(genres).Count()).ThenByDescending(t=>double.TryParse(t.Rating,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var r)?r:0).Take(20));
        }
        var result=found.Where(t=>t.Id!=imdb&&!Seen(t)).DistinctBy(t=>t.Id).Take(16).ToList();
        recommendationCache[cacheKey]=result;return result;
    }
    bool Seen(DiscoverTitle t)=>store.FindDiscovered(t) is {} key&&store.EpisodesFor(key).Any(store.EverWatched);
    // Discover home: "Because you watched" for your two latest finished titles, then genre picks.
    void RenderPersonalRails(CancellationToken token,int revision)
    {
        var seeds=store.Archive.Where(i=>Regex.IsMatch(i.ImdbId,@"^tt\d+$")&&store.EverWatched(i)).GroupBy(i=>i.GroupKey).Select(g=>(Item:g.First(),At:g.Max(HistoryDate))).OrderByDescending(x=>x.At).Take(2).ToList();
        if(seeds.Count==0)return;
        foreach(var (seed,_) in seeds)
        {
            var host=new StackPanel();ContentPanel.Children.Add(host);
            _=Load();
            async Task Load()
            {
                try
                {
                    var genres=seed.Genres.Split('·',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).ToList();
                    var titles=await SimilarTo(seed.ImdbId,seed.IsMovie,genres,token);
                    if(token.IsCancellationRequested||revision!=discoverRevision||titles.Count==0)return;
                    host.Children.Add(Rail("Because you watched "+seed.Title,TmdbKey().Length>0?"Recommended by people who loved it.":"Highly rated titles that share its genres.",titles.Select(t=>DiscoverCard(t,token,revision)).ToList()));
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){}
                catch(Exception e){store.Log(e);}
            }
        }
        var weights=store.Archive.Where(store.EverWatched).SelectMany(i=>i.Genres.Split('·',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)).GroupBy(g=>g).OrderByDescending(g=>g.Count()).Take(3).Select(g=>g.Key).ToList();
        if(weights.Count==0)return;
        var picks=new StackPanel();ContentPanel.Children.Add(picks);_=Picks();
        async Task Picks()
        {
            try
            {
                using var discovery=DiscoverClient();var pool=new List<DiscoverTitle>();
                foreach(var type in new[]{"series","movie"})foreach(var genre in weights)try{pool.AddRange(await discovery.Genre(type,genre,token));}catch(Exception e)when(e is HttpRequestException or IOException or JsonException){}
                var titles=pool.DistinctBy(t=>t.Id).Where(t=>!Seen(t)).OrderByDescending(t=>t.Genres.Count(weights.Contains)).ThenByDescending(t=>double.TryParse(t.Rating,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var r)?r:0).Take(20).ToList();
                if(token.IsCancellationRequested||revision!=discoverRevision||titles.Count==0)return;
                picks.Children.Add(Rail("Picked for you","Top-rated "+string.Join(", ",weights).ToLowerInvariant()+" — the genres you watch most.",titles.Select(t=>DiscoverCard(t,token,revision)).ToList()));
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception e){store.Log(e);}
        }
    }
    // "More like this" under a title page; cards open in Discover.
    FrameworkElement MoreLikeThis(string imdb,bool movie,List<string> genres)
    {
        var host=new StackPanel();if(!Regex.IsMatch(imdb,@"^tt\d+$"))return host;
        var token=discoverCancellation?.Token??CancellationToken.None;var revision=discoverRevision;_=Load();return host;
        async Task Load()
        {
            try
            {
                var titles=await SimilarTo(imdb,movie,genres,token);if(titles.Count==0||token.IsCancellationRequested)return;
                host.Children.Add(Rail("More like this","",titles.Select(t=>DiscoverCard(t,token,revision,page!="Discover")).ToList()));
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception e){store.Log(e);}
        }
    }
    void RenderDiscoverSettings()
    {
        ContentPanel.Children.Add(Heading("Recommendations & where to watch","Optional: a free TMDB key gives better “More like this” lists and shows which services stream a title in "+new System.Globalization.RegionInfo(Region()).DisplayName+"."));
        var key=TmdbKey();
        ContentPanel.Children.Add(Body(key.Length>0?"TMDB is connected. Recommendations use it, and title pages list streaming services.":"Without a key, Noir recommends highly rated titles in the genres you watch, and “Find streaming services” opens JustWatch.",13));
        var input=new PasswordBox{Width=460,HorizontalAlignment=HorizontalAlignment.Left,Padding=new(12,9,12,9),Background=Brush("#1C1C1F"),Foreground=Brush("#F4F1EB"),BorderBrush=Brush("#343437"),Margin=new(0,6,0,10)};
        System.Windows.Automation.AutomationProperties.SetName(input,"TMDB API key or read access token");ContentPanel.Children.Add(input);
        var row=new WrapPanel();ContentPanel.Children.Add(row);var message=Body("",12);ContentPanel.Children.Add(message);
        row.Children.Add(ActionButton(key.Length>0?"Replace key":"Save key",async()=>
        {
            var value=input.Password.Trim();if(value.Length<16){message.Text="Paste the API key (v3) or API Read Access Token from your TMDB account settings.";return;}
            message.Text="Checking…";
            var checkDir=Path.Combine(Path.GetTempPath(),"noir-tmdb-check-"+Guid.NewGuid().ToString("N"));
            try{using(var tmdb=new TmdbClient(value,checkDir))await tmdb.Find("tt0111161",true,CancellationToken.None);try{Directory.Delete(checkDir,true);}catch(IOException){}File.WriteAllBytes(TmdbKeyPath,ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser));recommendationCache.Clear();providerCache.Clear();Render();Status.Text="TMDB connected.";}
            catch(Exception e){message.Text=e is IOException?e.Message:"Couldn't reach TMDB with that key.";}
        },true));
        var get=ActionButton("Get a free key",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.themoviedb.org/settings/api"){UseShellExecute=true}));get.Margin=new(10,0,0,0);row.Children.Add(get);
        if(key.Length>0){var remove=ActionButton("Remove key",()=>{try{File.Delete(TmdbKeyPath);}catch(IOException){}recommendationCache.Clear();providerCache.Clear();Render();});remove.Margin=new(10,0,0,0);row.Children.Add(remove);}
        ContentPanel.Children.Add(Body("This product uses the TMDB API but is not endorsed or certified by TMDB. Streaming data is provided by JustWatch. The key is encrypted for your Windows account.",11));
    }
}
