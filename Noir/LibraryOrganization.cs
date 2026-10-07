namespace Noir;
public sealed partial class LibraryStore
{
    public static readonly string[] Statuses=["Automatic","Watching","Planned","Finished","Dropped"];
    public string TitleStatus(IEnumerable<VideoItem> items)
    {
        var group=items.ToList();if(group.Count==0)return "Planned";
        if(State.TitleStatuses.TryGetValue(group[0].GroupKey,out var manual)&&Statuses.Contains(manual)&&manual!="Automatic")return manual;
        if(group.All(EverWatched))return "Finished";
        return group.Any(i=>EverWatched(i)||GetProgress(i) is {Position:>0})?"Watching":"Planned";
    }
    public bool CaughtUp(string groupKey,DateTimeOffset now)
    {
        var released=State.EpisodeSchedules.GetValueOrDefault(groupKey)?.Where(e=>e.Season>0&&e.AirsAt<=now).ToList();
        if(released==null||released.Count==0)return false;
        var episodes=EpisodesFor(groupKey);return released.All(e=>episodes.Any(i=>i.Season==e.Season&&i.Episode==e.Episode&&EverWatched(i)));
    }
    public void SetStatus(VideoItem item,string status)
    {
        if(!Statuses.Contains(status))throw new ArgumentException("Choose a viewing status.");
        lock(gate){if(status=="Automatic")State.TitleStatuses.Remove(item.GroupKey);else State.TitleStatuses[item.GroupKey]=status;TouchMobileField("status:"+item.GroupKey);Save();}
    }
    public void CreateCollection(string name){name=name.Trim();if(name.Length is <1 or >60||name is "All" or "My List")throw new ArgumentException("Choose a collection name with 1–60 characters, other than All or My List.");lock(gate){State.Collections.TryAdd(name,[]);Save();}}
    public void SetCollection(VideoItem item,string name,bool added)
    {
        name=name.Trim();if(name.Length is <1 or >60||name is "All" or "My List")throw new ArgumentException("Use a collection name with 1–60 characters.");
        lock(gate){if(!State.Collections.TryGetValue(name,out var titles))State.Collections[name]=titles=[];if(added)titles.Add(item.GroupKey);else titles.Remove(item.GroupKey);TouchMobileField("collection:"+name+":"+item.GroupKey);Save();}
    }
    public void Queue(VideoItem item)
    {
        lock(gate){if(!State.PlayQueue.Contains(item.Id))State.PlayQueue.Add(item.Id);Save();}
    }
    public VideoItem? QueuedNext(VideoItem current,IEnumerable<VideoItem> items)
    {
        var available=items.Where(i=>i.Available&&System.IO.File.Exists(i.Path)).GroupBy(i=>i.Id).ToDictionary(g=>g.Key,g=>g.First());
        return State.PlayQueue.Where(id=>id!=current.Id).Select(id=>available.GetValueOrDefault(id)).FirstOrDefault(i=>i!=null);
    }
    public void MoveQueue(string id,int step){lock(gate){var from=State.PlayQueue.IndexOf(id);var to=from+Math.Sign(step);if(from<0||to<0||to>=State.PlayQueue.Count)return;State.PlayQueue.RemoveAt(from);State.PlayQueue.Insert(to,id);Save();}}
    public void ConsumeQueue(VideoItem item){lock(gate){if(State.PlayQueue.RemoveAll(id=>id==item.Id)>0)Save();}}
    public void CorrectMatch(VideoItem selected,string title,int year,string imdb,int season,int episode,bool wholeTitle)
    {
        title=title.Trim();if(title.Length is <1 or >200||year<0||year>2100||season<0||episode<0||season>1000||episode>10000||imdb.Length>0&&!System.Text.RegularExpressions.Regex.IsMatch(imdb,@"^tt\d+$"))throw new ArgumentException("Check the title, year, IMDb ID and episode numbers.");
        lock(gate)
        {
            selected=Remember(selected).Item;
            var seasonOffset=season-selected.Season;
            var targets=wholeTitle?Archive.Where(i=>i.GroupKey==selected.GroupKey).ToList():[selected];
            foreach(var i in targets)
            {
                var correctedSeason=wholeTitle?(i.Season==0?0:i.Season+seasonOffset):season;
                var correctedEpisode=wholeTitle?i.Episode:episode;
                if(correctedSeason<0)throw new ArgumentException("The season offset would create a negative season.");
            }
            if(!wholeTitle&&!selected.IsMovie&&Archive.Any(i=>i.Id!=selected.Id&&i.GroupKey==selected.GroupKey&&i.Season==season&&i.Episode==episode))throw new ArgumentException("Another episode already uses those season and episode numbers.");
            foreach(var i in targets)
            {
                var s=wholeTitle?(i.Season==0?0:i.Season+seasonOffset):season;var e=wholeTitle?i.Episode:episode;
                var match=new MatchOverride{Title=title,Year=year,ImdbId=imdb,Season=s,Episode=e};State.Matches[i.Id]=match;
                i.Title=i.ReviewTitle=title;i.Year=year;i.ImdbId=imdb;i.Season=s;i.Episode=e;i.EpisodeImdbId="";i.EpisodeTitle="";i.EpisodeSummary="";
            }
            if(wholeTitle){State.EpisodeSchedules.Remove(selected.GroupKey);State.SerializdSeasons.Keys.Where(k=>k.StartsWith(selected.GroupKey+":")).ToList().ForEach(k=>State.SerializdSeasons.Remove(k));}
            TouchMobileField("match:"+selected.GroupKey);Save();
        }
    }
}
