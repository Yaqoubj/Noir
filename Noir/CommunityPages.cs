using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace Noir;

// These adapters use the sites' own signed-in controls. Credentials stay in WebView2.
public static class CommunityPages
{
    public static async Task Navigate(CoreWebView2 web,string url,CancellationToken token)
    {
        var completion=new TaskCompletionSource<bool>();
        void Completed(object? sender,CoreWebView2NavigationCompletedEventArgs e)=>completion.TrySetResult(e.IsSuccess && e.HttpStatusCode<400);
        web.NavigationCompleted+=Completed;
        try
        {
            token.ThrowIfCancellationRequested();web.Navigate(url);
            if(!await completion.Task.WaitAsync(TimeSpan.FromSeconds(40),token)) throw new IOException("The website could not load. Noir will retry later.");
        }
        finally {web.NavigationCompleted-=Completed;}
    }
    public static async Task<JsonNode?> Read(CoreWebView2 web,string script)=>JsonNode.Parse(await web.ExecuteScriptAsync(script));
    static string Normalize(string name)=>Regex.Replace(name.ToLowerInvariant(),@"[^\p{L}\p{N}]","");
    public static async Task<string> ResolveSerializd(CoreWebView2 web,VideoItem item,LibraryStore? store,CancellationToken token)
    {
        var cached=store==null?"":CommunityQueue.SerializdEpisode(store.State,item);
        if(cached.Length>0) {await Navigate(web,cached,token);return cached;}
        if(item.IsMovie || item.Episode<1) throw new IOException("Serializd tracks TV episodes only.");
        await Navigate(web,ReviewLinks.SerializdUrl(item),token);
        var name=Normalize(item.ReviewTitle.Length>0?item.ReviewTitle:item.Title);
        string? show=null;
        for(int attempt=0;attempt<40;attempt++)
        {
            token.ThrowIfCancellationRequested();
            var matches=await Read(web,"[...document.querySelectorAll('a[href]')].filter(a=>/^https:\\/\\/www.serializd.com\\/show\\/[^/?#]+$/.test(a.href)).map(a=>({url:a.href,name:a.querySelector('img')?.alt?.replace(/^Poster for /,'')||a.innerText.split('\\n')[0]}))");
            var candidates=(matches as JsonArray)?.Where(a=>Normalize(a?["name"]?.GetValue<string>()??"")==name).Select(a=>a!["url"]!.GetValue<string>()).Distinct().ToList()??[];
            if(candidates.Count>1) throw new IOException("More than one show matches. Open Serializd and select the correct episode first.");
            if(candidates.Count==1) {show=candidates[0];break;}
            await Task.Delay(500,token);
        }
        if(show==null) throw new IOException("No exact Serializd title match yet. Choose the correct episode in its review tab.");
        await Navigate(web,show,token);
        var details=await Read(web,"(()=>{try {const d=JSON.parse(document.querySelector('#__NEXT_DATA__').textContent).props.pageProps.data.showDetails;return {name:d.name,date:d.premiereDate,seasons:d.seasons?.map(s=>({id:s.seasonId||s.id,number:s.seasonNumber}))};}catch{return null;}})()");
        if(details==null || Normalize(details["name"]?.GetValue<string>()??"")!=name || (item.Year>0 && !(details["date"]?.GetValue<string>()??"").StartsWith(item.Year.ToString())))
            throw new IOException("Serializd's title/year could not be verified. Select its exact episode in Reviews & comments.");
        var season=(details["seasons"] as JsonArray)?.SingleOrDefault(s=>s?["number"]?.GetValue<int>()==item.Season);
        if(season==null) throw new IOException("Serializd does not have this season yet.");
        var seasonUrl=show+"/season/"+season["id"]!.ToString()+"/"+item.Season;
        var url=seasonUrl+"/episode/"+item.Episode;
        await Navigate(web,url,token);
        if(!await VerifySerializdEpisode(web,item)) throw new IOException("Serializd's episode could not be verified.");
        if(store!=null) {store.State.SerializdSeasons[CommunityQueue.SeasonKey(item)]=seasonUrl;store.Save();}
        return url;
    }
    public static async Task<bool> VerifySerializdEpisode(CoreWebView2 web,VideoItem item)
    {
        var details=await Read(web,"(()=>{try{const p=JSON.parse(document.querySelector('#__NEXT_DATA__').textContent).props.pageProps;const d=p.data;return {show:p.showId,season:Number(p.seasonNumber),episode:Number(p.episodeNumber),runtime:d.runtime,name:d.showDetails.name};}catch{return null;}})()");
        if(details==null || details["season"]?.GetValue<int>()!=item.Season || details["episode"]?.GetValue<int>()!=item.Episode) return false;
        var path=Uri.TryCreate(web.Source,UriKind.Absolute,out var uri)?uri.AbsolutePath:"";
        if(uri?.Host!="www.serializd.com" || !path.EndsWith($"/{item.Season}/episode/{item.Episode}") || !path.Contains("-"+details["show"]+"/season/")) return false;
        var name=Normalize(item.ReviewTitle.Length>0?item.ReviewTitle:item.Title);
        if(Normalize(details["name"]?.GetValue<string>()??"")!=name) return false;
        // Specials can be numbered differently, or combine several shorts into one file.
        if(item.Season==0 && item.Duration>0 && details["runtime"]?.GetValue<double>() is double minutes && minutes>0 && Math.Abs(item.Duration/60-minutes)>Math.Max(10,minutes*.5)) return false;
        return true;
    }
    public static string SerializdControl(bool click)=>"(()=>{const label=b=>[...b.querySelectorAll('span')].map(s=>s.textContent.trim().toLowerCase()).find(t=>t==='watched'||t==='mark watched')||b.textContent.replace(/[^\\p{L}\\p{N}\\s]/gu,'').trim().toLowerCase();const a=[...document.querySelectorAll('.common-show-interact-button')].filter(b=>b.getClientRects().length&&['watched','mark watched'].includes(label(b)));if(a.length!==1)return [...document.querySelectorAll('a[href]')].some(a=>a.getClientRects().length&&new URL(a.href).host===location.host&&new URL(a.href).pathname==='/login')?'signed-out':'waiting';if(label(a[0])==='watched')return 'watched';"+(click?"a[0].click();return 'clicked';":"return 'ready';")+"})()";
    public static string TraktDateControl=>"(()=>{const b=[...document.querySelectorAll('.trakt-drawer .trakt-dropdown-group button,.trakt-drawer .trakt-dropdown-group [role=button]')].filter(b=>b.getClientRects().length&&!b.disabled&&b.getAttribute('disabled')!=='true'&&b.getAttribute('aria-disabled')!=='true'&&[b.getAttribute('aria-label'),b.getAttribute('label'),b.textContent?.trim()].includes('Just now'));if(b.length!==1)return false;b[0].click();return true;})()";
    public static string TraktControl(VideoItem item,bool click)
    {
        var id=JsonSerializer.Serialize(item.ImdbId);
        var marker=JsonSerializer.Serialize($"S{item.Season} • E{item.Episode}");
        var movieTitle=JsonSerializer.Serialize(item.ReviewTitle.Length>0?item.ReviewTitle:item.Title);
        return "(()=>{const u=new URL(location.href);if(u.host!=='app.trakt.tv')return 'mismatch';const visible=b=>b.getClientRects().length&&!b.disabled;let root=document;"+
            (item.IsMovie?$"if(u.pathname!=='/movies/'+{id})return 'mismatch';":$"if(u.pathname!=='/shows/'+{id}||u.searchParams.get('view')!=='episode'||Number(u.searchParams.get('season'))!=={item.Season}||Number(u.searchParams.get('episode'))!=={item.Episode})return 'mismatch';const d=[...document.querySelectorAll('.trakt-drawer')].filter(d=>d.innerText.includes({marker}));if(d.length!==1)return 'waiting';root=d[0];")+
            "const buttons=[...root.querySelectorAll('button[aria-label]')].filter(visible)"+(item.IsMovie?$".filter(b=>[\"Mark \\\"\"+{movieTitle}+\"\\\" as watched\",\"Remove \\\"\"+{movieTitle}+\"\\\" from your History\"].includes(b.getAttribute('aria-label')))":"")+";const watched=buttons.filter(b=>/^Remove \".+\" from your History$/.test(b.getAttribute('aria-label')));if(watched.length===1)return 'watched';const marks=buttons.filter(b=>/^Mark \".+\" as watched$/.test(b.getAttribute('aria-label')));if(marks.length!==1)return 'waiting';"+
            (click?"marks[0].click();return 'clicked';":"return 'ready';")+"})()";
    }
    public static async Task<string> Control(CoreWebView2 web,VideoItem item,string provider,bool click)
    {
        var value=await web.ExecuteScriptAsync(provider=="Serializd"?SerializdControl(click):TraktControl(item,click));
        return JsonSerializer.Deserialize<string>(value)??"waiting";
    }
}
