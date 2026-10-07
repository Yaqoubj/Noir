using Noir;
using System.Text.Json;

static class MobileChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        var s=new LibraryStore(Path.Combine(root,"mobile-"+Guid.NewGuid().ToString("N")));s.State.Roots=[];
        var i=new VideoItem {Title="Phone test",Path="missing.mkv",Season=1,Episode=1,Duration=1800};s.Record(i,93.125,1800);i=s.Archive.Single();
        s.State.EpisodeSchedules[i.GroupKey]=[new(){Season=1,Episode=1},new(){Season=1,Episode=2},new(){Season=1,Episode=3}];
        var json=JsonSerializer.Serialize(s.MobileSnapshot());
        check(!json.Contains("missing.mkv")&&!json.Contains("Roots")&&!json.Contains("WatchSync"),"Mobile projection never exposes PC paths or account data");
        check(s.MobileSnapshot().Items.Count==3,"Phone receives full known episode counts including unavailable episodes");
        var at=DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeMilliseconds();
        var pcRevision=s.GetProgress(i)!.UpdatedAt;
        var op=new MobileOperation {Id=Guid.NewGuid().ToString(),ItemId=i.Id,Kind="progress",ViewingId=Guid.NewGuid().ToString(),At=at,Position=321.987,Duration=1800};
        check(s.MergeMobile([op]).Count==1&&s.GetProgress(i)!.Position==321.987,"Android exact fractional resume time merges into Windows");
        check(!s.Record(i,93.125,1800,false,pcRevision)&&s.GetProgress(i)!.Position==321.987,"Stale PC close cannot overwrite newer phone progress");
        check(new LibraryStore(s.DataDirectory).GetProgress(i)!.Position==321.987,"Exact synced phone position survives PC close and reopen");
        s.MergeMobile([op]);check(s.GetProgress(i)!.Position==321.987,"Repeated operation is acknowledged without changing progress");
        var old=new MobileOperation {Id=Guid.NewGuid().ToString(),ItemId=i.Id,Kind="progress",ViewingId=Guid.NewGuid().ToString(),At=at-5000,Position=10,Duration=1800,Completed=true};
        s.MergeMobile([old]);check(s.GetProgress(i)!.Position==321.987&&s.ViewingHistory(i).Count==1,"Older offline watch joins diary without overwriting newer resume");
        var reopened=new LibraryStore(s.DataDirectory);reopened.MergeMobile([old]);check(reopened.ViewingHistory(i).Count==1,"Watch receipt survives restart and prevents duplicate diary dates");
        var virtualItem=reopened.MobileSnapshot().Items.First(x=>x.Episode==3);
        var mark=new MobileOperation {Id=Guid.NewGuid().ToString(),ItemId=virtualItem.Id,Kind="mark",ViewingId=Guid.NewGuid().ToString(),At=at+1000,Completed=true};reopened.MergeMobile([mark]);
        check(reopened.Archive.Any(x=>x.Id==virtualItem.Id)&&reopened.ViewingHistory(reopened.Archive.Single(x=>x.Id==virtualItem.Id)).Count==1,"Android can mark unavailable known episode watched permanently");
        var invalid=new MobileOperation {Id=Guid.NewGuid().ToString(),ItemId=i.Id,Kind="progress",ViewingId=Guid.NewGuid().ToString(),At=at,Position=double.NaN};
        check(reopened.MergeMobile([invalid]).Count==0,"Invalid mobile progress is rejected without modifying the library");
        var review=new MobileOperation {Id=Guid.NewGuid().ToString(),ItemId=i.Id,Kind="review",At=at+2000,Rating=4,Note="Phone note"};reopened.MergeMobile([review]);
        check(reopened.State.PersonalReviews[i.Id].Rating==4&&reopened.State.PersonalReviews[i.Id].Note=="Phone note","Phone ratings and notes sync to PC");
        var stale=new MobileOperation {Id=Guid.NewGuid().ToString(),ItemId=i.Id,Kind="review",At=at,Rating=1};reopened.MergeMobile([stale]);check(reopened.State.PersonalReviews[i.Id].Rating==4,"Stale offline review cannot replace a more recent review");
        var list=new MobileOperation {Id=Guid.NewGuid().ToString(),ItemId=i.Id,Kind="list",At=at+2000,Value=true};reopened.MergeMobile([list]);check(reopened.State.Watchlist.Contains(i.GroupKey),"Android My List syncs by show group");
        var unknown=new MobileOperation {Id=Guid.NewGuid().ToString(),ItemId="../../private",Kind="mark",At=at,ViewingId=Guid.NewGuid().ToString()};check(reopened.MergeMobile([unknown]).Count==0,"Unknown remote IDs cannot create arbitrary catalog entries");
        var final=new LibraryStore(s.DataDirectory);check(final.MobileSnapshot().Items.Count==3&&final.ViewingHistory(i).Count==1,"Deleted files retain phone-synced history and complete counts across restarts");
    }
}
