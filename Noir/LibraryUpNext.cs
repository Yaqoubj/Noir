namespace Noir;
public enum UpNextKind {Resume,Ready,NotOnPc,Upcoming}
public sealed record UpNext(VideoItem Item,UpNextKind Kind,DateTimeOffset Activity,DateTimeOffset? AirsAt);
public sealed partial class LibraryStore
{
    // What to watch next for a title, including the cases ContinueFor leaves out: the next
    // episode is not on this PC yet, or has not aired. This keeps a title on Home after you
    // finish the last local episode instead of sending you to History to find it.
    // group must come from EpisodesFor so it includes the known schedule.
    public UpNext? UpNextFor(List<VideoItem> group)
    {
        var last=group.Where(x=>GetProgress(x) is {} b&&(b.Position>0||b.Completed))
            .Select(x=>(Item:x,At:new[]{GetProgress(x)!.UpdatedAt,ViewingHistory(x).FirstOrDefault()?.WatchedAt??DateTimeOffset.MinValue}.Max()))
            .OrderByDescending(x=>x.At).FirstOrDefault();
        if(last.Item==null) return null;
        bool Playable(VideoItem i)=>i.Available&&System.IO.File.Exists(i.Path);
        var bookmark=GetProgress(last.Item)!;
        var finished=bookmark.Completed||ViewingHistory(last.Item).FirstOrDefault()?.WatchedAt>bookmark.UpdatedAt;
        if(!finished) return new(last.Item,Playable(last.Item)?UpNextKind.Resume:UpNextKind.NotOnPc,last.At,null);
        if(last.Item.IsMovie) return null;
        if(Next(last.Item,group,State) is {} ready&&Playable(ready)) return new(ready,UpNextKind.Ready,last.At,null);
        var current=last.Item;
        var following=group.Where(x=>(current.Season==0?x.Season==0:x.Season>0)&&(x.Season>current.Season||x.Season==current.Season&&x.Episode>current.Episode)&&GetProgress(x)?.Completed!=true)
            .OrderBy(x=>x.Season).ThenBy(x=>x.Episode).FirstOrDefault();
        if(following==null) return null;
        var airs=State.EpisodeSchedules.GetValueOrDefault(current.GroupKey)?.FirstOrDefault(e=>e.Season==following.Season&&e.Episode==following.Episode)?.AirsAt;
        if(airs>DateTimeOffset.UtcNow) return new(following,UpNextKind.Upcoming,last.At,airs);
        return new(following,Playable(following)?UpNextKind.Ready:UpNextKind.NotOnPc,last.At,null);
    }
}
