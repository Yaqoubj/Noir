#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows.Controls;
namespace Noir;
public partial class MainWindow
{
    async Task ValidateMetadataRefresh()
    {
        var results=new List<string>();void Check(bool ok,string label){if(!ok)throw new Exception(label);results.Add("PASS: "+label);}
        try
        {
            var history=JsonSerializer.Serialize(store.State.Viewings);var progress=JsonSerializer.Serialize(store.State.Progress);
            store.State.OnlineMetadata=true;
            var automatic=RefreshMetadata();await Task.Delay(100);
            if(metadataRefreshing) {await RefreshMetadata(true);Check(metadataRefreshRequested,"Manual refresh is queued while an automatic update runs");}
            else _=RefreshMetadata(true);
            var deadline=DateTime.UtcNow.AddSeconds(90);
            while((metadataRefreshing||metadataRefreshRequested)&&DateTime.UtcNow<deadline) await Task.Delay(100);
            await automatic;
            Check(!metadataRefreshing&&!metadataRefreshRequested&&Status.Text.StartsWith("Refresh finished"),"Manual refresh completes with a visible result");
            var hannibal=store.Archive.First(i=>i.Title=="Hannibal");
            Check(hannibal.Summary.Length>20&&File.Exists(hannibal.Poster)&&hannibal.EpisodeTitle.Length>0,"Real Hannibal information, episode names and artwork download successfully");
            var reopened=new LibraryStore(store.DataDirectory);
            Check(reopened.Archive.Any(i=>i.Title=="Hannibal"&&File.Exists(i.Poster)&&i.Summary==hannibal.Summary),"Updated show details and pictures survive reopening");
            Check(history==JsonSerializer.Serialize(store.State.Viewings)&&progress==JsonSerializer.Serialize(store.State.Progress),"Refresh leaves viewing history and exact resume times unchanged");
            Navigate("Settings");Check(Descendants(ContentPanel).OfType<TextBlock>().Any(t=>t.Text=="Noir 0.9.1"),"Settings shows the actual installed build version");
            CaptureLibrary(Path.Combine(store.DataDirectory,"metadata-refresh-settings.png"));
            File.WriteAllText(Path.Combine(store.DataDirectory,"metadata-refresh-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception e){File.WriteAllText(Path.Combine(store.DataDirectory,"metadata-refresh-failure.txt"),e.ToString());}
    }
}
#endif
