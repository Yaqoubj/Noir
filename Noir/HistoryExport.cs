using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Noir;

public static class HistoryExport
{
    public static int Write(LibraryStore store,string path,string format)=>Write(store,path,format,out _);
    public static int Write(LibraryStore store,string path,string format,out int skipped)
    {
        if(format is not ("noir-csv" or "noir-json" or "letterboxd" or "trakt"))throw new IOException("Choose Noir CSV, Noir JSON, Letterboxd or Trakt history.");
        var all=store.Archive.OrderBy(i=>i.Title,StringComparer.Ordinal).ThenBy(i=>i.Season).ThenBy(i=>i.Episode)
            .SelectMany(item=>store.ViewingHistory(item).OrderBy(v=>v.WatchedAt).Select(view=>(Item:item,View:view,Rating:store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Rating??0)))
            .ToList();
        var views=all.Where(v=>format!="letterboxd"||v.Item.IsMovie).Where(v=>format!="trakt"||Regex.IsMatch(v.Item.ImdbId,@"^tt\d+$")).ToList();skipped=all.Count-views.Count;
        string At(ViewingRecord v)=>v.DateUnknown?"":v.WatchedAt.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);
        string text;
        if(format is "noir-csv" or "letterboxd")
        {
            var csv=new StringBuilder(format=="noir-csv"?"type,title,year,imdb_id,season,episode,episode_title,watched_at,date_unknown,source,rating,approximate\r\n":"Title,Year,imdbID,WatchedDate,Rating,Rewatch\r\n");
            var seen=new HashSet<string>();
            foreach(var (item,view,rating) in views)
            {
                var rewatch=!seen.Add(item.Id);
                var fields=format=="noir-csv"?new[]{item.IsMovie?"movie":"episode",item.Title,item.Year.ToString(CultureInfo.InvariantCulture),item.ImdbId,(item.IsMovie?0:item.Season).ToString(CultureInfo.InvariantCulture),(item.IsMovie?0:item.Episode).ToString(CultureInfo.InvariantCulture),item.EpisodeTitle,At(view),view.DateUnknown?"true":"false",view.Source,rating.ToString(CultureInfo.InvariantCulture),view.Approximate?"true":"false"}:
                    new[]{item.Title,item.Year==0?"":item.Year.ToString(CultureInfo.InvariantCulture),item.ImdbId,view.DateUnknown?"":view.WatchedAt.ToUniversalTime().ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),rating==0?"":rating.ToString(CultureInfo.InvariantCulture),rewatch?"true":"false"};
                csv.AppendJoin(',',fields.Select(HistoryData.CsvField));csv.Append("\r\n");
            }
            text=csv.ToString();
        }
        else if(format=="noir-json")
            text=JsonSerializer.Serialize(views.Select(v=>new{type=v.Item.IsMovie?"movie":"episode",title=v.Item.Title,year=v.Item.Year,imdb_id=v.Item.ImdbId,season=v.Item.IsMovie?0:v.Item.Season,episode=v.Item.IsMovie?0:v.Item.Episode,episode_title=v.Item.EpisodeTitle,watched_at=At(v.View),date_unknown=v.View.DateUnknown,approximate=v.View.Approximate,source=v.View.Source,rating=v.Rating,id=v.View.Id,logged_at=v.View.LoggedAt?.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture)??"",note=store.State.PersonalReviews.GetValueOrDefault(v.Item.Id)?.Note??"",show_rating=v.Item.IsMovie?0:store.State.ShowRatings.GetValueOrDefault(v.Item.GroupKey)}),new JsonSerializerOptions{WriteIndented=true});
        else
        {
            Dictionary<string,object> Entry(VideoItem item)=>new(){["title"]=item.Title,["year"]=item.Year,["ids"]=new{imdb=item.ImdbId}};
            var movies=views.Where(v=>v.Item.IsMovie).Select(v=>{var movie=Entry(v.Item);if(!v.View.DateUnknown)movie["watched_at"]=At(v.View);return movie;}).ToList();
            var shows=views.Where(v=>!v.Item.IsMovie).GroupBy(v=>v.Item.ImdbId).Select(group=>
            {
                var show=Entry(group.First().Item);
                show["seasons"]=group.GroupBy(v=>v.Item.Season).Select(season=>new{number=season.Key,episodes=season.Select(v=>
                {var ep=new Dictionary<string,object>{["number"]=v.Item.Episode};if(!v.View.DateUnknown)ep["watched_at"]=At(v.View);return ep;}).ToList()}).ToList();return show;
            }).ToList();
            text=JsonSerializer.Serialize(new{movies,shows},new JsonSerializerOptions{WriteIndented=true});
        }
        var full=Path.GetFullPath(path);var temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,text,new UTF8Encoding(false));File.Move(temp,full,true);}finally{if(File.Exists(temp))File.Delete(temp);}
        return views.Count;
    }
}
