#if NOIR_VALIDATE
using System.IO;
namespace Noir;
public partial class MainWindow {
 async Task ValidateEmbeds(){
  var video=new VideoItem{Id="review-test",Title="Review fixture",IsMovie=true};
  var browser=new ReviewBrowser(video,store.DataDirectory,false,store:store,initialSource:"Reddit"){Owner=this};browser.Show();
  await Task.Delay(1500);await browser.NavigateUrlForTest("https://www.reddit.com/r/television/comments/abc123/discussion/");await Task.Delay(1000);
  File.WriteAllText(Path.Combine(store.DataDirectory,"review-checks.txt"),browser.CurrentAddress.Contains("sort=top")?"PASS: Top discussion navigation":"FAIL: Top sorting");browser.Close();
 }
}
#endif
