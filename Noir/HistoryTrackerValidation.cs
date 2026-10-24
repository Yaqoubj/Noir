#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Noir;
public partial class MainWindow
{
    // --history-tracker-only: renders every History tab and the log flow, exercises log/undo/unwatch,
    // and saves screenshots beside the profile. Uses live Cinemeta unless --offline-fixture is passed.
    async Task ValidateHistoryTracker()
    {
        var results=new List<string>();
        void Check(bool ok,string label){if(!ok)throw new Exception(label);results.Add("PASS: "+label);}
        async Task Settle(int ms=500){UpdateLayout();await Task.Delay(ms);UpdateLayout();}
        void Shot(string name)=>CaptureLibrary(Path.Combine(store.DataDirectory,"tracker-"+name+".png"));
        try
        {
            if(Environment.GetCommandLineArgs().Contains("--offline-fixture"))discoveryFactory=()=>new DiscoveryClient(store.DataDirectory,new DiscoveryFixtureHandler());
            WindowState=WindowState.Normal;Width=1320;Height=900;
            foreach(var tab in TrackerTabs)
            {
                trackerTab=tab;Navigate("History");await Settle();
                Check(ContentPanel.Children.Count>2,"History "+tab+" renders");Shot(tab.Replace(" & ","-").Replace(" ","-").ToLowerInvariant());
            }
            var before=store.State.Viewings.Values.Sum(v=>v.Count);
            OpenLog(null);var deadline=DateTime.UtcNow.AddSeconds(20);while(logResults==null&&DateTime.UtcNow<deadline)await Task.Delay(100);await Settle(1500);
            Check(logResults?.Count>0,"Log search shows titles to pick");Shot("log-search");
            var show=logResults!.First(t=>!t.IsMovie);await OpenLogTitle(show);await Settle(1200);
            Check(logTitle!=null&&logTitle.Episodes.Count>0,"Log title loads its episodes");Shot("log-title");
            var aired=logTitle!.Episodes.Where(e=>e.Season>0&&(e.AirsAt==null||e.AirsAt<=DateTimeOffset.UtcNow)).OrderBy(e=>e.Season).ThenBy(e=>e.Episode).Take(3).ToList();
            logWhen="When it aired";LogTitleItems(logTitle,aired.Select(e=>(e.Season,e.Episode)).ToList(),"fixture");await Settle();
            var group=TrackedGroup(logTitle)!;
            Check(aired.All(e=>group.Any(i=>i.Season==e.Season&&i.Episode==e.Episode&&store.EverWatched(i))),"Logging marks the chosen episodes without any file");
            Check(store.State.Viewings.Values.Sum(v=>v.Count)==before+aired.Count,"Each logged episode adds one viewing");
            LogTitleItems(logTitle,aired.Select(e=>(e.Season,e.Episode)).ToList(),"again");
            Check(store.State.Viewings.Values.Sum(v=>v.Count)==before+aired.Count,"Logging the same episodes again adds nothing");
            Shot("log-title-checked");
            var first=group.First(i=>i.Season==aired[0].Season&&i.Episode==aired[0].Episode);
            var undo=store.Unwatch([first]);Check(!store.EverWatched(first),"Unchecking forgets the viewing");
            store.Restore(undo);Check(store.EverWatched(first),"Undo restores it");
            CloseLog();trackerTab="Up next";Navigate("History");await Settle();
            Check(Descendants(ContentPanel).OfType<Button>().Any(b=>System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Mark ")),"Up next offers one-click check-offs");Shot("up-next-after-log");
            var next=NextToTrack(store.EpisodesFor(group[0].GroupKey),DateTimeOffset.UtcNow);
            if(next!=null){CheckOff([next],"next");Check(store.EverWatched(next),"Checking off Up next logs the episode");await Settle();Shot("after-check-off");}
            trackerTab="Diary";Navigate("History");await Settle();Shot("diary-after-log");
            trackerTab="Shows";Navigate("History");await Settle();Shot("shows-after-log");
            foreach(var w in new[]{780,940}){Width=w;Height=700;trackerTab="Up next";Navigate("History");await Settle();Shot("up-next-"+w);}
            if(Environment.GetEnvironmentVariable("NOIR_IMPORT_SAMPLE") is {Length:>0} sample)
            {
                Width=1320;Height=900;trackerTab="Add history";Navigate("History");
                var count=store.State.Viewings.Values.Sum(v=>v.Count);await ImportFile(sample);await Settle(800);
                Check(importPlan is {Titles.Count:>0},"A Netflix export resolves into a reviewable plan");Shot("import-review");
                File.WriteAllText(Path.Combine(store.DataDirectory,"tracker-import-plan.txt"),string.Join("\n",importPlan!.Titles.Select(t=>$"{t.Title.Name} [{t.Title.Id}] {string.Join(",",t.Views.Select(v=>$"S{v.Season}E{v.Episode}@{v.At:yyyy-MM-dd}"))}").Concat(importPlan.Unmatched.Select(u=>$"UNMATCHED {u.Row.Title} | {u.Row.EpisodeTitle} | {u.Reason}"))));
                var planned=importPlan.Titles.Sum(t=>t.Views.Count);ApplyImport(importPlan);await Settle();
                Check(store.State.Viewings.Values.Sum(v=>v.Count)==count+planned,"Applying the plan adds every matched viewing");Shot("import-done");
                await ImportFile(sample);var again=importPlan;if(again!=null)ApplyImport(again);
                Check(store.State.Viewings.Values.Sum(v=>v.Count)==count+planned,"Re-importing the same export adds nothing");
                trackerTab="Up next";Navigate("History");await Settle();Shot("up-next-after-import");
                trackerTab="Diary";Navigate("History");await Settle();Shot("diary-after-import");
            }
        }
        catch(Exception error){results.Add("FAIL: "+error);}
        File.WriteAllText(Path.Combine(store.DataDirectory,"tracker-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
#endif
