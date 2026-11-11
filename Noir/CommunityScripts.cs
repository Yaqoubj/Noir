using System.Text.Json;

namespace Noir;

/// <summary>The watched-control page scripts, kept free of WebView types so checks and the phone's golden file can use them.</summary>
public static class CommunityScripts
{
    public static string SerializdControl(bool click) =>
        "(()=>{const label=b=>[...b.querySelectorAll('span')].map(s=>s.textContent.trim().toLowerCase()).find(t=>t==='watched'||t==='mark watched')||b.textContent.replace(/[^\\p{L}\\p{N}\\s]/gu,'').trim().toLowerCase();const a=[...document.querySelectorAll('.common-show-interact-button')].filter(b=>b.getClientRects().length&&['watched','mark watched'].includes(label(b)));if(a.length!==1)return [...document.querySelectorAll('a[href]')].some(a=>a.getClientRects().length&&new URL(a.href).host===location.host&&new URL(a.href).pathname==='/login')?'signed-out':'waiting';if(label(a[0])==='watched')return 'watched';"
        + (click ? "a[0].click();return 'clicked';" : "return 'ready';")
        + "})()";

    public static string TraktDateControl =>
        "(()=>{const b=[...document.querySelectorAll('.trakt-drawer .trakt-dropdown-group button,.trakt-drawer .trakt-dropdown-group [role=button]')].filter(b=>b.getClientRects().length&&!b.disabled&&b.getAttribute('disabled')!=='true'&&b.getAttribute('aria-disabled')!=='true'&&[b.getAttribute('aria-label'),b.getAttribute('label'),b.textContent?.trim()].includes('Just now'));if(b.length!==1)return false;b[0].click();return true;})()";

    public static string TraktControl(VideoItem item, bool click)
    {
        var id = JsonSerializer.Serialize(item.ImdbId);
        var marker = JsonSerializer.Serialize($"S{item.Season} • E{item.Episode}");
        var movieTitle = JsonSerializer.Serialize(item.ReviewTitle.Length > 0 ? item.ReviewTitle : item.Title);
        return "(()=>{const u=new URL(location.href);if(u.host!=='app.trakt.tv')return 'mismatch';const visible=b=>b.getClientRects().length&&!b.disabled;let root=document;"
            + (
                item.IsMovie
                    ? $"if(u.pathname!=='/movies/'+{id})return 'mismatch';"
                    : $"if(u.pathname!=='/shows/'+{id}||u.searchParams.get('view')!=='episode'||Number(u.searchParams.get('season'))!=={item.Season}||Number(u.searchParams.get('episode'))!=={item.Episode})return 'mismatch';const d=[...document.querySelectorAll('.trakt-drawer')].filter(d=>d.innerText.includes({marker}));if(d.length!==1)return 'waiting';root=d[0];"
            )
            + "const buttons=[...root.querySelectorAll('button[aria-label]')].filter(visible)"
            + (
                item.IsMovie
                    ? $".filter(b=>[\"Mark \\\"\"+{movieTitle}+\"\\\" as watched\",\"Remove \\\"\"+{movieTitle}+\"\\\" from your History\"].includes(b.getAttribute('aria-label')))"
                    : ""
            )
            + ";const watched=buttons.filter(b=>/^Remove \".+\" from your History$/.test(b.getAttribute('aria-label')));if(watched.length===1)return 'watched';const marks=buttons.filter(b=>/^Mark \".+\" as watched$/.test(b.getAttribute('aria-label')));if(marks.length!==1)return 'waiting';"
            + (click ? "marks[0].click();return 'clicked';" : "return 'ready';")
            + "})()";
    }
}
