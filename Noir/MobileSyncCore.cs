using System.IO;

namespace Noir;

public sealed class MobileOperation
{
    public string Id { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string Kind { get; set; } = "";
    public long At { get; set; }
    public double Position { get; set; }
    public double Duration { get; set; }
    public bool Completed { get; set; }
    public string ViewingId { get; set; } = "";
    public int Rating { get; set; }
    public string Note { get; set; } = "";
    public bool Value { get; set; }
    public string Title {get;set;}="";
    public string Imdb {get;set;}="";
    public int Year {get;set;}
    public int Season {get;set;}
    public int Episode {get;set;}
    public string Name {get;set;}="";
    public string Status {get;set;}="";
}
public sealed class MobileItem
{
    public string Status {get;set;}="";
    public bool CaughtUp {get;set;}
    public long StatusAt {get;set;}
    public DateTimeOffset? AddedAt {get;set;}
    public string EpisodeImage {get;set;}="";
    public List<string> Collections {get;set;}=[];
    public Dictionary<string,long> CollectionTimes {get;set;}=new();
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string Title { get; set; } = "";
    public string EpisodeTitle { get; set; } = "";
    public int Year { get; set; }
    public bool Movie { get; set; }
    public int Season { get; set; }
    public int Episode { get; set; }
    public bool Available { get; set; }
    public string Summary { get; set; } = "";
    public string ShowSummary { get; set; } = "";
    public Dictionary<string,string> Reviews { get; set; } = new();
    public string Genres { get; set; } = "";
    public string Imdb { get; set; } = "";
    public string EpisodeImdb { get; set; } = "";
    public string Poster { get; set; } = "";
    public string Backdrop { get; set; } = "";
    public double Duration { get; set; }
    public Bookmark? Progress { get; set; }
    public List<ViewingRecord> Viewings { get; set; } = [];
    public PersonalReview? Review { get; set; }
    public bool Listed { get; set; }
    public IntroSegment? Intro { get; set; }
    public IntroSegment? Outro { get; set; }
    public long ReviewAt { get; set; }
    public long ListAt { get; set; }
}
public sealed class MobileSnapshot
{
    public List<string> Collections {get;set;}=[];
    public Dictionary<string,List<KnownEpisode>> Schedules {get;set;}=new();
    public int Schema { get; set; } = 1;
    public long ServerTime { get; set; }
    public string Instance { get; set; } = "";
    public List<MobileItem> Items { get; set; } = [];
    public bool AutoNext { get; set; }
    public bool AutoSkipIntro { get; set; }
    public bool MarkWatchedAtCredits { get; set; }
    public bool HideSpoilers { get; set; }
}
public sealed partial class LibraryStore
{
    // This is a projection, never the library file: roots, paths, cookies and account data stay on PC.
    public MobileSnapshot MobileSnapshot()
    {
        lock(gate)
        {
            var result=new MobileSnapshot {ServerTime=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),Instance=State.MobileInstanceId,
                AutoNext=State.AutoNext,AutoSkipIntro=State.AutoSkipIntro,MarkWatchedAtCredits=State.MarkWatchedAtCredits,HideSpoilers=State.HideSpoilers,Collections=State.Collections.Keys.ToList(),Schedules=State.EpisodeSchedules.ToDictionary(p=>p.Key,p=>p.Value.Select(e=>new KnownEpisode{Season=e.Season,Episode=e.Episode,Title=e.Title,AirsAt=e.AirsAt,Image=File.Exists(e.Image)?Path.GetFileName(e.Image):""}).ToList())};
            foreach(var group in Archive.Select(i=>i.GroupKey).Distinct())
            {
            var episodes=EpisodesFor(group);var status=TitleStatus(episodes);var caughtUp=CaughtUp(group,DateTimeOffset.UtcNow);
            foreach(var i in episodes)
            {
                var p=GetProgress(i);
                result.Items.Add(new(){Id=i.Id,Group=i.GroupKey,Title=i.Title,EpisodeTitle=i.EpisodeTitle,Year=i.Year,Movie=i.IsMovie,Season=i.Season,Episode=i.Episode,
                    AddedAt=i.AddedAt,Status=status,StatusAt=State.MobileFieldTimes.GetValueOrDefault("status:"+group),CaughtUp=caughtUp,EpisodeImage=File.Exists(i.EpisodeImage)?Path.GetFileName(i.EpisodeImage):"",
                    Collections=State.Collections.Where(c=>c.Value.Contains(group)).Select(c=>c.Key).ToList(),CollectionTimes=State.Collections.Keys.ToDictionary(n=>n,n=>State.MobileFieldTimes.GetValueOrDefault("collection:"+n+":"+group)),
                    Available=i.Available&&File.Exists(i.Path),Summary=i.EpisodeSummary.Length>0?i.EpisodeSummary:i.Summary,ShowSummary=i.Summary,Reviews=ReviewLinks.Sources(i,State),Genres=i.Genres,Imdb=i.ImdbId,EpisodeImdb=i.EpisodeImdbId,
                    Poster=File.Exists(i.Poster)?Path.GetFileName(i.Poster):"",Backdrop=File.Exists(i.Backdrop)?Path.GetFileName(i.Backdrop):"",Duration=i.Duration,
                    Progress=p==null?null:new(){ViewingId=p.ViewingId,Position=p.Position,Duration=p.Duration,Completed=p.Completed,UpdatedAt=p.UpdatedAt},
                    Viewings=ViewingHistory(i).Select(v=>new ViewingRecord {Id=v.Id,WatchedAt=v.WatchedAt,Source=v.Source}).ToList(),
                    Review=State.PersonalReviews.TryGetValue(i.Id,out var review)?new(){Rating=review.Rating,Note=review.Note}:null,
                    Listed=State.Watchlist.Contains(group),Intro=GetIntro(i),Outro=GetOutro(i),
                    ReviewAt=State.MobileFieldTimes.GetValueOrDefault("review:"+i.Id),ListAt=State.MobileFieldTimes.GetValueOrDefault("list:"+group)});
            }
            }
            return result;
        }
    }
    public void TouchMobileField(string field) {lock(gate) State.MobileFieldTimes[field]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();}
    public List<string> MergeMobile(IEnumerable<MobileOperation> operations)
    {
        var acknowledged=new List<string>();bool watched=false;
        lock(gate)
        {
            var lookup=Archive.Select(i=>i.GroupKey).Distinct().SelectMany(EpisodesFor).GroupBy(i=>i.Id).ToDictionary(g=>g.Key,g=>g.First());
            foreach(var alias in State.CatalogAliases) if(lookup.TryGetValue(alias.Value,out var canonical)) lookup.TryAdd(alias.Key,canonical);
            foreach(var op in operations)
            {
                if(!Guid.TryParse(op.Id,out _)||!lookup.TryGetValue(op.ItemId,out var item)||op.At<946684800000||op.At>DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds()) continue;
                if(State.MobileReceipts.Contains(op.Id)) {acknowledged.Add(op.Id);continue;}
                if(op.Kind is "progress" or "mark")
                {
                    if(!double.IsFinite(op.Position)||!double.IsFinite(op.Duration)||op.Position<0||op.Duration<0||op.Duration>604800||op.Position>604800||!Guid.TryParse(op.ViewingId,out _)) continue;
                    var entry=Remember(item);SeedViewing(entry);var at=DateTimeOffset.FromUnixTimeMilliseconds(op.At);
                    if(op.Completed&&!ViewingHistory(item).Any(v=>v.Id==op.ViewingId))
                    {AddViewing(item,op.ViewingId,at,op.Kind=="mark"?"Marked on Android":"Android playback");CommunityQueue.Enqueue(State,item,at);watched=true;}
                    if(entry.Progress==null||at>entry.Progress.UpdatedAt)
                    {
                        entry.Progress=new(){ViewingId=op.ViewingId,Position=op.Position,Duration=op.Duration,Completed=op.Completed,UpdatedAt=at};
                        if(item.Path.Length>0) State.Progress[item.Path]=entry.Progress;
                        if(op.Kind=="mark"&&!op.Completed) CommunityQueue.CancelPending(State,item.Id);
                    }
                }
                else if(op.Kind=="match")
                {
                    var field="match:"+item.GroupKey;
                    if(op.At>State.MobileFieldTimes.GetValueOrDefault(field)){try{CorrectMatch(item,op.Title,op.Year,op.Imdb,op.Season,op.Episode,op.Value);}catch(ArgumentException){continue;}State.MobileFieldTimes[field]=op.At;}
                }
                else if(op.Kind is "status" or "collection")
                {
                    if(op.Kind=="status"&&!Statuses.Contains(op.Status)||op.Kind=="collection"&&(op.Name.Trim().Length is <1 or >60||op.Name.Trim() is "All" or "My List"))continue;
                    var field=op.Kind+":"+(op.Kind=="status"?item.GroupKey:op.Name.Trim()+":"+item.GroupKey);
                    if(op.At>State.MobileFieldTimes.GetValueOrDefault(field))
                    {
                        if(op.Kind=="status"){if(op.Status=="Automatic")State.TitleStatuses.Remove(item.GroupKey);else State.TitleStatuses[item.GroupKey]=op.Status;}
                        else {var name=op.Name.Trim();if(!State.Collections.TryGetValue(name,out var members))State.Collections[name]=members=[];if(op.Value)members.Add(item.GroupKey);else members.Remove(item.GroupKey);}
                        State.MobileFieldTimes[field]=op.At;
                    }
                }
                else if(op.Kind is "review" or "list")
                {
                    if(op.Note.Length>10000||op.Rating<0||op.Rating>5) continue;
                    var field=op.Kind+":"+(op.Kind=="list"?item.GroupKey:item.Id);
                    if(op.At>State.MobileFieldTimes.GetValueOrDefault(field))
                    {
                        if(op.Kind=="review") State.PersonalReviews[item.Id]=new(){Rating=op.Rating,Note=op.Note};
                        else if(op.Value) State.Watchlist.Add(item.GroupKey);else State.Watchlist.Remove(item.GroupKey);
                        State.MobileFieldTimes[field]=op.At;
                    }
                }
                else continue;
                State.MobileReceipts.Add(op.Id);acknowledged.Add(op.Id);
            }
            // Bound transport receipts. Evicted repeats are still idempotent through field clocks
            // and permanent viewing IDs, so they cannot resurrect an older bookmark or watch date.
            if(State.MobileReceipts.Count>8192) foreach(var old in State.MobileReceipts.Take(State.MobileReceipts.Count-8192).ToList()) State.MobileReceipts.Remove(old);
            if(acknowledged.Count>0) Save();
        }
        if(watched) WatchCompleted?.Invoke();
        return acknowledged;
    }
}
