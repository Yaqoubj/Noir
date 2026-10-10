using System.Text.RegularExpressions;
namespace Noir;
public sealed class ReleaseGroup
{
    public string Quality {get;set;}="";
    public string Label {get;set;}="";
    public int SourceEntries {get;set;}
    public List<DownloadChoice> Choices {get;set;}=[];
}
public static class ReleasePresentation
{
    public static void Enrich(DownloadChoice choice)
    {
        var text=choice.Name+" "+choice.Detail;
        if(choice.Resolution.Length==0)choice.Resolution=TorboxProvider.Resolution(text);
        bool Has(string pattern)=>Regex.IsMatch(text,pattern,RegexOptions.IgnoreCase);
        // Descriptions can list subtitle languages; they do not establish audio language.
        choice.Codec=Has(@"\b(hevc|x265|h[ .]?265)\b")?"HEVC":Has(@"\b(avc|x264|h[ .]?264)\b")?"H.264":Has(@"\bav1\b")?"AV1":"";
        choice.Picture=Has(@"\b(dolby[ .]?vision|dovi|dv)\b")?"Dolby Vision":Has(@"\bhdr10\+?\b")?"HDR10":Has(@"\bhdr\b")?"HDR":Has(@"\bsdr\b")?"SDR":"";
        if(choice.ReleaseKind.Length==0)choice.ReleaseKind=choice.SizeKind=="Movie"?"Movie":choice.SizeKind=="Pack"||Has(@"\b(complete|season[ ._-]*pack|batch)\b")?"Pack · verify files":"Episode / release";
    }
    public static string Quality(string value)=>value=="2160p"?"4K":value.Length>0?value:"Quality unknown";
    public static List<ReleaseGroup> Group(IEnumerable<DownloadChoice> choices,string preferred)
    {
        var list=choices.ToList();foreach(var choice in list)Enrich(choice);
        return list.GroupBy(c=>c.Resolution).OrderByDescending(g=>g.Key==preferred)
            .ThenByDescending(g=>int.TryParse(g.Key.TrimEnd('p'),out var p)?p:0)
            .Select(g=>new ReleaseGroup{Quality=g.Key,Choices=g.ToList(),SourceEntries=g.Sum(c=>Math.Max(1,c.SourceEntries)),
                Label=Quality(g.Key)+" · "+g.Count()+" releases · "+g.Sum(c=>Math.Max(1,c.SourceEntries))+" source entries"}).ToList();
    }
    public static bool Matches(DownloadChoice c,string quality,string language,long maxBytes,bool ready)
        =>(quality=="All"||c.Resolution==quality)&&(language=="All"||c.Languages.Contains(language,StringComparison.OrdinalIgnoreCase))
        &&(maxBytes<=0||c.Size<=0||c.SizeKind=="Pack"||c.Size<=maxBytes)&&(!ready||c.CacheStatus=="Ready");
    public static List<DownloadChoice> Recommend(IEnumerable<DownloadChoice> choices,string quality,long maxBytes,int count=3)
        =>choices.OrderByDescending(c=>quality=="Any"||c.Resolution==quality)
            .ThenByDescending(c=>maxBytes<=0||c.SizeKind=="Pack"||c.Size>0&&c.Size<=maxBytes)
            .ThenByDescending(c=>c.CacheStatus=="Ready").ThenBy(c=>c.Size>0?c.Size:long.MaxValue).Take(count).ToList();
    public static string Summary(DownloadChoice c)=>string.Join(" · ",new[]{Quality(c.Resolution),c.Languages=="Unknown"?"Audio unconfirmed":c.Languages,c.Picture,c.Codec,
        c.SizeKind+": "+DownloadNaming.Size(c.Size),c.CacheStatus=="Ready"?"Ready on TorBox":c.CacheStatus=="Not cached"?"Needs fetching":"Availability unknown"}.Where(s=>s.Length>0));
}
