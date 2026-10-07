namespace Noir;

public sealed class WatchSyncJob
{
    public string Provider { get; set; } = "";
    public string ItemId { get; set; } = "";
    public DateTimeOffset WatchedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
    public DateTimeOffset RetryAt { get; set; }
    public int Attempts { get; set; }
    public string Message { get; set; } = "Waiting to sync";
}

public static class CommunityQueue
{
    public static string SeasonKey(VideoItem item)=>item.GroupKey+":season:"+item.Season;
    public static bool Enabled(PlayerState state,string provider)=>provider switch {"Trakt"=>state.SyncTrakt,"Serializd"=>state.SyncSerializd,_=>false};
    public static void Enqueue(PlayerState state,VideoItem item,DateTimeOffset watchedAt)
    {
        if(item.Id.Length==0) return;
        foreach(var provider in new[]{"Trakt","Serializd"})
        {
            if(!Enabled(state,provider) || (provider=="Serializd"&&item.IsMovie)) continue;
            var key=provider+":"+item.Id;
            if(!state.WatchSync.ContainsKey(key)) state.WatchSync[key]=new(){Provider=provider,ItemId=item.Id,WatchedAt=watchedAt};
        }
    }
    public static void CancelPending(PlayerState state,string itemId)
    {
        foreach(var key in state.WatchSync.Where(p=>p.Value.ItemId==itemId&&p.Value.SyncedAt==null).Select(p=>p.Key).ToList()) state.WatchSync.Remove(key);
    }
    public static string SerializdEpisode(PlayerState state,VideoItem item)
    {
        if(item.IsMovie || item.Episode<1) return "";
        var season=state.SerializdSeasons.GetValueOrDefault(SeasonKey(item),"");
        return System.Text.RegularExpressions.Regex.IsMatch(season,@"^https://www\.serializd\.com/show/[^/?#]+/season/\d+/"+item.Season+"$")?season+"/episode/"+item.Episode:"";
    }
}
