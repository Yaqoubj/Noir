using Noir;
using System.Text.Json;
static class OrganizerChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        var dir=Path.Combine(root,"organizer-"+Guid.NewGuid().ToString("N"));var videos=Path.Combine(dir,"TV");
        Directory.CreateDirectory(videos);var source=Path.Combine(videos,"Example.S02E01.1080p.mkv");File.WriteAllText(source,"unique video data");
        var sub=Path.ChangeExtension(source,"en.srt");File.WriteAllText(sub,"unique subtitle data");
        var store=new LibraryStore(Path.Combine(dir,"profile"));store.State.Roots=[new(){Path=videos}];
        var item=store.Scan(out _).Single();store.ApplyDetails(item.GroupKey,new(){Title="Example",Year=2020,SourceUrl="https://example.org/verified"});
        item=store.Archive.Single();store.Record(item,83.42,1000);var before=store.GetProgress(item)!;var id=item.Id;
        var plan=store.PlanOrganization();check(plan.Moves.Count==2&&plan.Moves.Any(m=>m.To.EndsWith("Example (2020) S02E01.en.srt")),"Organizer previews standard season folders and matching language subtitles");
        check(store.Organize(plan)==1&&File.Exists(plan.Moves[0].To)&&!File.Exists(source),"Organizer moves video and subtitle without copying or overwriting");
        store=new LibraryStore(store.DataDirectory);var rescanned=store.Scan(out _).Single();
        check(rescanned.Id==id&&store.GetProgress(rescanned)!.Position==83.42&&store.GetProgress(rescanned)!.UpdatedAt==before.UpdatedAt,"Organized episode preserves identity and exact resume through reopening and scanning");
        store.Mark(rescanned,true);var history=store.ViewingHistory(rescanned).Single().Id;
        check(store.Organize(store.LastOrganization()!,true)==1&&File.Exists(source)&&File.Exists(sub),"Undo returns video and subtitle to their original paths");
        store=new LibraryStore(store.DataDirectory);check(store.Archive.Single().Path==source&&store.ViewingHistory(store.Archive.Single()).Single().Id==history,"Undo retains permanent watch history");
        var nextPlan=store.PlanOrganization();Directory.CreateDirectory(Path.GetDirectoryName(nextPlan.Moves[0].To)!);File.WriteAllText(nextPlan.Moves[0].To,"different file");
        check(store.PlanOrganization().Moves.Count==0,"Organizer leaves conflicting destinations untouched");
        bool refused=false;try {store.Organize(nextPlan);}catch(IOException){refused=true;}check(refused&&File.ReadAllText(nextPlan.Moves[0].To)=="different file"&&File.Exists(source),"Stale previews cannot overwrite a file added later");
        var recoveryDir=Path.Combine(dir,"recovery");var recovery=new LibraryStore(recoveryDir);recovery.State.Roots=[new(){Path=videos}];
        var recoveryItem=recovery.Scan(out _).First(i=>i.Path==source);recovery.ApplyDetails(recoveryItem.GroupKey,new(){Title="Recovered",SourceUrl="https://example.org/verified"});
        var interrupted=recovery.PlanOrganization();var first=interrupted.Moves.First(m=>m.Video);
        File.WriteAllText(recovery.OrganizerJournal,JsonSerializer.Serialize(interrupted));Directory.CreateDirectory(Path.GetDirectoryName(first.To)!);File.Move(first.From,first.To);
        var restored=new LibraryStore(recoveryDir);check(restored.Archive.First(i=>i.Id==first.ItemId).Path==first.To&&restored.LastOrganization()!.Moves.First().Done,"Interrupted rename is recovered before scanning or playback");
        var continueStore=new LibraryStore(Path.Combine(dir,"continue"));continueStore.State.Roots=[];
        var episodes=Enumerable.Range(1,3).Select(e=>new VideoItem{Path=$"episode-{e}.mkv",Title="Resume",CollectionKey="resume",Season=e==3?2:1,Episode=e==3?1:e,Duration=100}).ToList();
        continueStore.Record(episodes[1],100,100,true);var extra=new VideoItem{Path="special.mkv",Title="Resume",CollectionKey="resume",Season=0,Episode=1};continueStore.Record(extra,3.622,100);
        check(continueStore.ContinueFor(episodes.Append(extra))==extra&&continueStore.GetProgress(extra)!.Position==3.622,"Continue follows the exact latest bookmark including specials");
        continueStore.Record(extra,40,100);check(continueStore.ContinueFor(episodes.Append(extra))==extra,"An intentionally watched special still becomes Continue Watching");
        continueStore.Record(episodes[2],23.456,100);var reopen=new LibraryStore(continueStore.DataDirectory);var selected=reopen.ContinueFor(reopen.Archive);
        check(selected?.Season==2&&reopen.GetProgress(selected)!.Position==23.456,"Continue Watching keeps the latest episode and exact time after restart");
        var regular=reopen.Archive.First(i=>i.Season==2);reopen.GetProgress(regular)!.UpdatedAt=DateTimeOffset.UtcNow.AddMinutes(-1);reopen.State.Viewings[regular.Id]=[new(){Id=Guid.NewGuid().ToString("N"),WatchedAt=DateTimeOffset.UtcNow}];
        check(reopen.ContinueFor(reopen.Archive)==null,"A newer permanent watched receipt prevents resuming its older unfinished bookmark");
        reopen.Record(regular,12.345,100);check(reopen.ContinueFor(reopen.Archive)==regular,"A deliberate later rewatch can resume despite earlier watch history");
    }
}
