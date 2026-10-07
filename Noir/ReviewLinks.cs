using System.Text.RegularExpressions;
namespace Noir;
public static class ReviewLinks
{
    static bool Imdb(string id)=>Regex.IsMatch(id,@"^tt\d+$");
    static string Name(VideoItem item)=>item.ReviewTitle.Length>0?item.ReviewTitle:item.Title;
    public static string ImdbUrl(VideoItem item,bool series=false)
    {
        var id=item.IsMovie || series?item.ImdbId:item.EpisodeImdbId;
        if(Imdb(id)) return $"https://www.imdb.com/title/{id}/reviews/";
        if(!item.IsMovie && !series && Imdb(item.ImdbId)) return $"https://www.imdb.com/title/{item.ImdbId}/episodes/?season={item.Season}";
        var query=Name(item)+(item.Year>0?" "+item.Year:"");
        return "https://www.imdb.com/find/?s=tt&q="+Uri.EscapeDataString(query);
    }
    public static string LetterboxdUrl(VideoItem item)=>Imdb(item.ImdbId)?"https://letterboxd.com/imdb/"+item.ImdbId+"/":"https://letterboxd.com/search/films/"+Uri.EscapeDataString(Name(item))+"/";
    public static string RottenUrl(VideoItem item)=>"https://www.rottentomatoes.com/search?search="+Uri.EscapeDataString(Name(item));
    public static string SerializdUrl(VideoItem item,PlayerState? state=null)=>state!=null && CommunityQueue.SerializdEpisode(state,item) is {Length:>0} exact?exact:"https://www.serializd.com/search?searchQuery="+Uri.EscapeDataString(Name(item));
    public static string TraktUrl(VideoItem item)=>Imdb(item.ImdbId)?item.IsMovie?$"https://app.trakt.tv/movies/{item.ImdbId}":$"https://app.trakt.tv/shows/{item.ImdbId}/seasons/{item.Season}/episodes/{item.Episode}":"https://app.trakt.tv/search?q="+Uri.EscapeDataString(Name(item));
    public static string? TopDiscussion(string url)=>RedditDiscussion.Reddit(url) is {} post?post+"?sort=top":null;
    public static string RedditUrl(VideoItem item)
    {
        // Use title filters so unrelated posts that only mention the show in a comment stay out.
        var name=Name(item).Replace("\"","").Replace("\r"," ").Replace("\n"," ");
        var query=$"title:\"{name}\"";
        if(item.IsMovie) query+=" discussion";
        else if(item.Season==0) query+=$" (title:\"OVA\" OR title:\"Special\" OR title:\"S00E{item.Episode:00}\") discussion";
        else
        {
            var namedSeason=$"((title:\"Season {item.Season}\" OR title:\"S{item.Season}\") AND title:\"Episode {item.Episode}\")";
            var plain=item.Season==1?$"({namedSeason} OR (title:\"Episode {item.Episode}\" AND NOT title:\"Season\"))":namedSeason;
            query+=$" (title:\"S{item.Season:00}E{item.Episode:00}\" OR title:\"{item.Season}x{item.Episode:00}\" OR {plain}) discussion";
        }
        return "https://www.reddit.com/search/?q="+Uri.EscapeDataString(query)+"&type=posts";
    }
    public static Dictionary<string,string> Sources(VideoItem item,PlayerState? state=null)
    {
        var sources=new Dictionary<string,string>{{item.IsMovie?"IMDb reviews":"IMDb episode",ImdbUrl(item)}};
        if(item.IsMovie) sources.Add("Letterboxd",LetterboxdUrl(item));
        else {sources.Add("IMDb series",ImdbUrl(item,true));sources.Add("Serializd",SerializdUrl(item,state));}
        sources.Add("Trakt",TraktUrl(item));sources.Add("Reddit",RedditUrl(item));sources.Add("Rotten Tomatoes",RottenUrl(item));
        return sources;
    }
}
