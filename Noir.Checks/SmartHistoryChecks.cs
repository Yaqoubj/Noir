using Noir;

// The tracker's inferences: recency, air-date clamps, earlier/later/skipped episodes, specials,
// runtimes and phone sync of ratings and duplicate viewings.
static class SmartHistoryChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        var s=new LibraryStore(Path.Combine(root,"smart-"+Guid.NewGuid().ToString("N")));s.State.Roots=[];
        var now=DateTimeOffset.UtcNow;
        var show=new VideoItem{Id="smart-1-1",CollectionKey="show:SMART:2020",Title="Smart",Season=1,Episode=1};
        var key="show:SMART:2020";
        s.State.Catalog[show.Id]=new(){Item=show};s.State.Catalog["smart-0-1"]=new(){Item=new VideoItem{Id="smart-0-1",CollectionKey="show:SMART:2020",Title="Smart",Season=0,Episode=1}};
        s.State.EpisodeSchedules[key]=[
            new(){Season=0,Episode=1,AirsAt=now.AddDays(-400),Runtime=20},
            new(){Season=1,Episode=1,AirsAt=now.AddDays(-300),Runtime=50},new(){Season=1,Episode=2,AirsAt=now.AddDays(-293),Runtime=50},
            new(){Season=1,Episode=3,AirsAt=now.AddDays(-286),Runtime=50},new(){Season=2,Episode=1,AirsAt=now.AddDays(-10),Runtime=55},
            new(){Season=2,Episode=2,AirsAt=now.AddDays(20)}];
        s.State.ShowRuntimes[key]=48;s.Save();
        var eps=s.EpisodesFor(key);VideoItem E(int se,int ep)=>eps.First(i=>i.Season==se&&i.Episode==ep);
        check(s.EarlierUnwatched(E(2,1)).Select(i=>(i.Season,i.Episode)).SequenceEqual([(1,1),(1,2),(1,3)]),"Checking off S2E1 finds the 3 unwatched earlier episodes, not specials or unaired ones");
        s.LogWatched([new(E(1,1),now.AddDays(-5)),new(E(1,3),now.AddDays(-4))],"Checked off");
        check(s.Gaps(eps).Select(i=>(i.Season,i.Episode)).SequenceEqual([(1,2)]),"A skipped episode before the furthest watched one is detected");
        check(s.LaterWatched(E(1,1)).Select(i=>(i.Season,i.Episode)).SequenceEqual([(1,3)]),"Un-checking S1E1 knows S1E3 is still checked off");
        check(!s.Counts(E(0,1))&&s.EarlierUnwatched(E(2,1)).All(i=>i.Season>0),"Specials don't count unless asked");
        s.State.IncludeSpecials.Add(key);check(s.Counts(E(0,1)),"Specials count once the user includes them");s.State.IncludeSpecials.Remove(key);
        check(s.RuntimeMinutes(E(1,2))==50&&s.RuntimeMinutes(E(2,2))==48,"Runtimes use the episode's length, then the show's typical length");
        // No fake recency
        s.LogWatched([new(E(1,2),now,true)],"Checked off");
        check(s.LastActivity(E(1,2))==DateTimeOffset.MinValue,"An undated log never counts as recent activity");
        check(s.LastActivity(E(1,3))==s.ViewingHistory(E(1,3))[0].WatchedAt,"A dated viewing is the item's last activity");
        // Air-date clamp
        s.LogWatched([new(E(2,1),now.AddDays(-30))],"Checked off");
        check(Math.Abs((s.ViewingHistory(E(2,1))[0].WatchedAt-now.AddDays(-10)).TotalMinutes)<1,"A watch date before the episode aired snaps to the air date");
        // Phone: clamp, cross-device duplicate, show rating
        var at=now.ToUnixTimeMilliseconds();
        var phone=new MobileOperation{Id=Guid.NewGuid().ToString(),ItemId=E(2,1).Id,Kind="mark",ViewingId=Guid.NewGuid().ToString(),At=at,Completed=true,WatchedAt=now.AddDays(-10).AddHours(2).ToUnixTimeMilliseconds()};
        var before=s.ViewingHistory(E(2,1)).Count;s.MergeMobile([phone]);
        check(s.ViewingHistory(E(2,1)).Count==before,"The same viewing from the phone within 6 hours isn't counted twice");
        var rate=new MobileOperation{Id=Guid.NewGuid().ToString(),ItemId=E(1,1).Id,Kind="showRating",At=at,Name="S1",Rating=4};
        check(s.MergeMobile([rate]).Count==1&&s.State.ShowRatings.GetValueOrDefault(key+":S1")==4,"A season rating from the phone reaches the PC");
        var bad=new MobileOperation{Id=Guid.NewGuid().ToString(),ItemId=E(1,1).Id,Kind="showRating",At=at,Name="../x",Rating=4};
        check(s.MergeMobile([bad]).Count==0,"Malformed rating keys are refused");
        check(s.MobileSnapshot().ShowRatings.GetValueOrDefault(key+":S1")==4,"Ratings sync back to the phone");
        var rewatch=new MobileOperation{Id=Guid.NewGuid().ToString(),ItemId=E(2,1).Id,Kind="mark",ViewingId=Guid.NewGuid().ToString(),At=at,Completed=true,Value=true};
        var count=s.ViewingHistory(E(2,1)).Count;s.MergeMobile([rewatch]);check(s.ViewingHistory(E(2,1)).Count==count+1,"A deliberate rewatch from the phone is kept even right after another viewing");
        // An uncheck made offline earlier must not delete a viewing recorded after it.
        s.LogWatched([new(E(1,1),now.AddMinutes(-1))],"Playback");var stale=new MobileOperation{Id=Guid.NewGuid().ToString(),ItemId=E(1,1).Id,Kind="unview",At=now.AddHours(-2).ToUnixTimeMilliseconds(),ViewingId=""};
        s.MergeMobile([stale]);check(s.ViewingHistory(E(1,1)).Any(v=>v.WatchedAt>now.AddMinutes(-2)),"An older offline uncheck keeps viewings recorded after it");
        s.ForgetTitle(key);check(!s.State.ShowRatings.Keys.Any(k=>k.StartsWith(key)),"Forgetting a title forgets its show and season ratings");
        // Ended shows
        s.State.ShowStates["show:OTHER:1"]="Ended";check(s.ShowEnded("show:OTHER:1"),"TVmaze's Ended state is kept per show");
    }
}
