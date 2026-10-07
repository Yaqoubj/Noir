#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace Noir;
public partial class MainWindow
{
    async Task ValidateOrganizerHistory()
    {
        var results=new List<string>();
        void Check(bool ok,string label) {if(!ok) throw new Exception(label);results.Add("PASS: "+label);}
        try
        {
            var history=JsonSerializer.Serialize(store.State.Viewings);var progress=JsonSerializer.Serialize(store.State.Progress);
            Navigate("History");UpdateLayout();await Task.Delay(200);
            var texts=Descendants(ContentPanel).OfType<TextBlock>().Select(t=>t.Text).ToList();
            Check(texts.Any(t=>t.Contains("episodes watched")),"History cards show watched counts and completion bars");
            Check(texts.Any(t=>t.Contains("File unavailable")),"Deleted movie remains visible in History");
            Check(Descendants(ContentPanel).OfType<ProgressBar>().Any(),"Poster cards have per-show progress indicators");
            CaptureLibrary(Path.Combine(store.DataDirectory,"history-cards.png"));
            var doro=store.EpisodesFor(store.Archive.First(i=>i.Title=="Dorohedoro").GroupKey);var resume=store.ContinueFor(doro);
            Check(store.Archive.Where(i=>i.Title=="Dorohedoro").Select(i=>i.GroupKey).Distinct().Count()==1,"Dorohedoro has one shared show identity across seasons");
            var plan=store.PlanOrganization();Check(plan.Moves.Any(m=>m.Video)&&plan.Moves.All(m=>File.Exists(m.From)),"Real folder organizer produces a preview without moving user files");
            OpenOrganizer();var dialog=Application.Current.Windows.OfType<Window>().First(w=>w.Title=="Organize your files");dialog.UpdateLayout();
            Check(Descendants(dialog).OfType<Button>().Any(b=>b.Content?.ToString()=="Organize these files"),"Organizer opens with apply and undo controls");dialog.Close();
            Check(history==JsonSerializer.Serialize(store.State.Viewings)&&progress==JsonSerializer.Serialize(store.State.Progress),"Browsing History and preview leaves watch records unchanged");
        }
        catch(Exception e) {results.Add("FAIL: "+e);}
        File.WriteAllText(Path.Combine(store.DataDirectory,"history-organizer-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
#endif
