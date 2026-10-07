#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Noir;
public partial class MainWindow
{
    async Task ValidateArchiveVisibility()
    {
        var results=new List<string>();
        void Assert(bool condition,string name){if(!condition) throw new Exception("FAIL: "+name);results.Add("PASS: "+name);}
        bool HasText(string text)=>Descendants(ContentPanel).OfType<TextBlock>().Any(t=>t.Text.Contains(text,StringComparison.OrdinalIgnoreCase));
        try
        {
            var deletedMovie=store.Archive.First(i=>i.IsMovie&&store.EverWatched(i)&&!File.Exists(i.Path));
            historyFilter="In progress";historyType="Shows";SearchBox.Text="no match";
            History_Click(this,new RoutedEventArgs());UpdateLayout();
            Assert(historyFilter=="All"&&historyType=="All"&&SearchBox.Text==""&&HasText(deletedMovie.Title),"Opening History clears stale filters and shows the deleted watched movie");
            Assert(HasText("✓ Watched")&&File.Exists(deletedMovie.Poster),"Deleted movie has its watched badge and permanent cached poster");
            CaptureLibrary(Path.Combine(store.DataDirectory,"history-real-deleted.png"));
            Navigate("Movies");UpdateLayout();Assert(HasText(deletedMovie.Title)&&HasText("Not on this PC"),"Movies keeps the deleted title's poster card and labels its availability");CaptureLibrary(Path.Combine(store.DataDirectory,"movies-real-deleted.png"));
            Navigate("Home");UpdateLayout();Assert(HasText("Recently watched")&&HasText(deletedMovie.Title),"Home has a visible Recently watched section including deleted movies");CaptureLibrary(Path.Combine(store.DataDirectory,"home-archive.png"));
            var folder=Path.Combine(store.DataDirectory,"fixture-TV","Archive Fixture (2024)");Directory.CreateDirectory(folder);
            foreach(var number in new[]{1,2,3}) File.WriteAllBytes(Path.Combine(folder,$"Archive Fixture S01E{number:00}.mkv"),[1,2,(byte)number]);
            File.Copy(deletedMovie.Poster,Path.Combine(folder,"poster.jpg"),true);
            store.State.SyncTrakt=store.State.SyncSerializd=false;store.State.Roots.Add(new(){Path=Path.GetDirectoryName(folder)!});await ScanLibrary();
            var fixture=items.Where(i=>i.Title=="Archive Fixture").OrderBy(i=>i.Episode).ToList();var key=fixture[0].GroupKey;
            store.Mark(fixture[0],true);store.Mark(fixture[1],true);
            store.State.EpisodeSchedules[key]=Enumerable.Range(1,4).Select(n=>new KnownEpisode{Season=1,Episode=n,Title="Episode "+n,AirsAt=DateTimeOffset.UtcNow.AddDays(-n)}).ToList();store.Save();
            var before=store.EpisodesFor(key);var dates=fixture.Take(2).Select(i=>store.ViewingHistory(i)[0].WatchedAt).ToArray();
            File.Delete(fixture[0].Path);File.Delete(fixture[1].Path);await ScanLibrary();
            var partial=store.EpisodesFor(key);
            Assert(partial.Count==4&&partial.Count(CanPlay)==1&&partial.Count(store.EverWatched)==2,"Deleting watched episodes keeps four total episodes, one available and two watched");
            Navigate("Shows");SearchBox.Text="Archive Fixture";Render();UpdateLayout();
            Assert(HasText("4 episodes")&&HasText("1 on PC")&&HasText("2 watched"),"Show poster displays total, available and watched episode counts separately");CaptureLibrary(Path.Combine(store.DataDirectory,"show-partial-delete.png"));
            OpenDetails(partial);UpdateLayout();Assert(HasText("S01 · E01")&&HasText("S01 · E04")&&HasText("Unavailable"),"Season page includes removed and metadata-only episodes with unavailable playback");CaptureLibrary(Path.Combine(store.DataDirectory,"episodes-partial-delete.png"));
            File.Delete(fixture[2].Path);File.Delete(Path.Combine(folder,"poster.jpg"));await ScanLibrary();
            var reopened=new LibraryStore(store.DataDirectory);reopened.Scan(out _);var complete=reopened.EpisodesFor(key);
            Assert(complete.Count==4&&complete.Count(CanPlay)==0&&complete.Count(reopened.EverWatched)==2,"Full deletion and restarting preserve total episode count and watched history");
            Assert(fixture.Take(2).Select(i=>reopened.ViewingHistory(i)[0].WatchedAt).SequenceEqual(dates)&&File.Exists(complete[0].Poster),"Original watched dates and cached poster survive deleting every source file");
            Navigate("History");UpdateLayout();Assert(HasText("Archive Fixture")&&HasText("0 on PC"),"Completely deleted show remains a visible History poster with its full count");CaptureLibrary(Path.Combine(store.DataDirectory,"history-complete-delete.png"));
            results.Add("ALL ARCHIVE VISIBILITY CHECKS PASSED");
        }
        catch(Exception error){results.Add(error.ToString());store.Log(error);}
        finally {File.WriteAllText(Path.Combine(store.DataDirectory,"archive-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
    }
}
#endif
