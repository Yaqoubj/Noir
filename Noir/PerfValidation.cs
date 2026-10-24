#if NOIR_VALIDATE
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Noir;
public partial class MainWindow
{
    // --perf-only [--perf-synthetic]: render time per page (median of 3) on the profile copy, optionally
    // grown to ~300 shows and ~2000 movies first, plus the phone snapshot and working set. Writes perf.json.
    async Task ValidatePerf()
    {
        var report=new Dictionary<string,object>();var results=new List<string>();
        var synthetic=Environment.GetCommandLineArgs().Contains("--perf-synthetic");
        try
        {
            WindowState=WindowState.Normal;Width=1320;Height=900;
            if(synthetic){var clock=Stopwatch.StartNew();AddSyntheticLibrary(300,2000);report["synthetic-build-ms"]=clock.ElapsedMilliseconds;}
            report["catalog"]=store.State.Catalog.Count;report["library-groups"]=store.LibraryGroups().Count;report["sync-jobs"]=store.State.WatchSync.Count;
            async Task<long> Time(string name,Action show)
            {
                var runs=new List<(long Total,long Build)>();
                for(var i=0;i<3;i++){var clock=Stopwatch.StartNew();show();var build=clock.ElapsedMilliseconds;UpdateLayout();runs.Add((clock.ElapsedMilliseconds,build));await Task.Delay(120);}
                var median=runs.OrderBy(r=>r.Total).ElementAt(1);report["render:"+name]=median.Total;report["build:"+name]=median.Build;
                results.Add($"{name}: {median.Total} ms (build {median.Build}; {string.Join("/",runs.Select(r=>r.Total))})");return median.Total;
            }
            void Page(string target){Navigate(target);}
            await Time("Home",()=>Page("Home"));
            foreach(var sort in new[]{"A–Z","Recently watched"}){librarySort=sort;await Time("Shows "+sort,()=>Page("Shows"));await Time("Movies "+sort,()=>Page("Movies"));}
            librarySort="A–Z";collectionFilter="My List";await Time("My List",()=>Page("My List"));
            foreach(var tab in TrackerTabs.Where(t=>t!="Add history")){await Time("History "+tab,()=>{trackerTab=tab;Navigate("History");});}
            foreach(var section in SettingsSections.Select(s=>s.Name)){await Time("Settings "+section,()=>{settingsSection=section;Navigate("Settings");});}
            discoveryFactory=()=>new DiscoveryClient(store.DataDirectory,new DiscoveryFixtureHandler());
            await Time("Discover",()=>{Navigate("Discover");});CancelDiscover();
            var detail=store.LibraryGroups().FirstOrDefault(k=>k.StartsWith("show:"));
            if(detail!=null)await Time("Show detail",()=>{Navigate("Shows");detailKey=detail;Render();});
            await Time("Rail footer",BuildRailFooter);
            await Time("Phone snapshot",()=>JsonSerializer.Serialize(store.MobileSnapshot()));
            await Time("Save",store.Save);
            if(store.LibraryGroups().Select(store.EpisodesFor).Where(g=>g.Count>1&&!g[0].IsMovie).SelectMany(g=>g).FirstOrDefault(i=>!store.EverWatched(i)) is {} episode)
            {
                trackerTab="Up next";Navigate("History");UpdateLayout();var flip=true;
                await Time("Check-off + re-render",()=>{store.Mark(episode,flip);flip=!flip;RenderQuiet();});
            }
            foreach(var target in new[]{"Home","Shows","Movies","History","Settings","Home","Shows","Movies","History","Settings"}){Navigate(target);UpdateLayout();await Task.Delay(60);}
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            using var self=Process.GetCurrentProcess();self.Refresh();
            report["working-set-mb"]=self.WorkingSet64/1048576;report["managed-mb"]=GC.GetTotalMemory(false)/1048576;report["image-cache-mb"]=imageBytes/1048576;
            results.Add("PASS: perf run finished");
        }
        catch(Exception error){results.Add("FAIL: "+error);}
        report["log"]=results;
        File.WriteAllText(Path.Combine(store.DataDirectory,"perf.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
    // Tracker-style titles with a fake file path so they count as library titles; about a third watched.
    void AddSyntheticLibrary(int shows,int movies)
    {
        var random=new Random(7);var now=DateTimeOffset.UtcNow;var genres=new[]{"Drama","Comedy","Crime","Sci-Fi","Animation","Thriller"};
        void Add(VideoItem item,bool watched)
        {
            store.State.Catalog[item.Id]=new CatalogEntry{Item=item,Paths=[item.Path]};
            if(watched)store.State.Viewings[item.Id]=[new ViewingRecord{Id=Guid.NewGuid().ToString("N"),WatchedAt=now.AddDays(-random.Next(1,900)),Source="Perf"}];
        }
        for(var s=0;s<shows;s++)
        {
            var key=$"show:PERF SHOW {s}:{2000+s%25}";var seasons=1+random.Next(4);var started=random.Next(3)==0;var upTo=random.Next(1,30);var n=0;
            for(var season=1;season<=seasons;season++)for(var e=1;e<=8;e++,n++)
                Add(new VideoItem{Id=$"perf-s{s}-{season}-{e}",CollectionKey=key,Title=$"Perf Show {s}",Year=2000+s%25,Season=season,Episode=e,Path=$@"C:\perf\Perf Show {s}\S{season:00}E{e:00}.mkv",Available=false,
                    Genres=genres[s%genres.Length],ImdbId=$"tt9{s:000000}",Duration=2600,AddedAt=now.AddDays(-s)},started&&n<upTo);
        }
        for(var m=0;m<movies;m++)
            Add(new VideoItem{Id=$"perf-m{m}",CollectionKey=$"movie:perf-m{m}",Title=$"Perf Movie {m}",Year=1980+m%45,IsMovie=true,Path=$@"C:\perf\movies\Perf Movie {m}.mkv",Available=false,
                Genres=genres[m%genres.Length],ImdbId=$"tt8{m:000000}",Duration=6500,AddedAt=now.AddDays(-m)},random.Next(3)==0);
    }
}
#endif
