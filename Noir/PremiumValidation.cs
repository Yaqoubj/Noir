#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows.Controls;
namespace Noir;
public partial class MainWindow {
 async Task ValidatePremium(){
 var results=new List<string>();void Check(bool ok,string label){if(!ok)throw new Exception(label);results.Add("PASS: "+label);}
 try{
 Navigate("History");UpdateLayout();await Task.Delay(200);Check(Descendants(ContentPanel).OfType<ProgressBar>().Any(),"Collection cards show progress");CaptureLibrary(Path.Combine(store.DataDirectory,"premium-history.png"));
 historyView="Diary";historyMonth="All time";Render();UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"premium-diary.png"));Check(Descendants(ContentPanel).OfType<TextBlock>().Any(t=>t.Text.Contains("viewings",StringComparison.OrdinalIgnoreCase)),"Diary shows monthly recap");
 Navigate("Home");UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"premium-home.png"));
 var show=store.Archive.First(i=>!i.IsMovie);detailKey=show.GroupKey;detailSeason=1;Render();UpdateLayout();ContentScroll.ScrollToVerticalOffset(480);await Task.Delay(100);CaptureLibrary(Path.Combine(store.DataDirectory,"premium-episodes.png"));Check(Descendants(ContentPanel).OfType<ProgressBar>().Any(),"Episode page shows season progress");
 store.SetCollection(show,"Evening picks",true);Navigate("My List");collectionFilter="Evening picks";Render();UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"premium-collection.png"));Check(Descendants(ContentPanel).OfType<Button>().Any(b=>b.Content?.ToString()=="Evening picks"),"Named collection is selectable");
 Check(ReviewLinks.TopDiscussion("https://www.reddit.com/r/tv/comments/abc123/title/")!.EndsWith("?sort=top"),"Discussions default to Top sorting");
 }catch(Exception e){results.Add("FAIL: "+e);}File.WriteAllText(Path.Combine(store.DataDirectory,"premium-ui-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
 }
}
#endif
