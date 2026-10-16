#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Noir;
public partial class MainWindow
{
    async Task ValidateDownloads()
    {
        var checks=new List<string>();void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        void Picture(FrameworkElement element,string name){element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(store.DataDirectory,name));encoder.Save(output);}
        try
        {
            var media=Path.Combine(store.DataDirectory,"fixture-media");Directory.CreateDirectory(media);store.State.Roots=[new(){Path=media}];store.State.Catalog.Clear();store.State.OnlineMetadata=false;
            var item=new VideoItem{Id="download-ui",Title="Noir UI fixture",CollectionKey="ui-fixture",Year=2026,Season=1,Episode=1,ImdbId="tt123",Available=false};store.State.Catalog[item.Id]=new(){Item=item};store.Save();downloads=new(store,new UiDownloadProvider(),start:false);downloads.Configure("https://example.invalid/fixture/manifest.json","fixture-not-real",media,"");
            settingsSection="Downloads";Navigate("Settings");Check(Descendants(ContentPanel).OfType<PasswordBox>().Count()==2,"Both provider fields hide private configuration");Check(Descendants(ContentPanel).OfType<CheckBox>().Any(b=>b.Content as string=="Download Arabic subtitles"),"Settings include Arabic subtitle download and automatic timing preferences");ContentScroll.ScrollToBottom();UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"download-settings.png"));
            detailKey=item.GroupKey;page="Shows";Render();Check(Descendants(ContentPanel).OfType<Button>().Any(b=>b.Content as string=="↓ Download"),"Unavailable episode has a visible Download button");
            FindDownloads(item,true);await Task.Delay(600);var picker=OwnedWindows.Cast<Window>().Single(w=>w.Title=="Download season");Check(Descendants((DependencyObject)picker.Content).OfType<TextBlock>().Any(t=>t.Text.Contains("1080p")),"Release picker displays quality, size and TorBox cache status");Picture((FrameworkElement)picker.Content,"download-releases.png");
            Check(Descendants((DependencyObject)picker.Content).OfType<Button>().Any(b=>b.Content as string=="Plan missing episodes"),"Season discovery offers the missing-episode planner");var choice=Descendants((DependencyObject)picker.Content).OfType<Button>().Single(b=>b.Content as string=="Choose files");choice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(600);picker.UpdateLayout();Check(Descendants((DependencyObject)picker.Content).OfType<CheckBox>().Count()==2,"Season picker lists verified episode files: count="+Descendants((DependencyObject)picker.Content).OfType<CheckBox>().Count()+"; "+string.Join(" | ",Descendants((DependencyObject)picker.Content).OfType<TextBlock>().Select(t=>t.Text)));Picture((FrameworkElement)picker.Content,"download-files.png");
            var submit=Descendants((DependencyObject)picker.Content).OfType<Button>().Single(b=>b.Content as string=="Download selected to this PC");submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(300);Check(downloads.Jobs().Count==2,"Confirmation queues only the selected episode files");Check(page=="Downloads","Queued files open the permanent Downloads tab");Picture(ContentPanel,"download-queue.png");var firstRow=Descendants(ContentPanel).OfType<ProgressBar>().First();ContentScroll.ScrollToBottom();var offset=ContentScroll.VerticalOffset;downloadPageUpdate?.Invoke();Check(Descendants(ContentPanel).OfType<ProgressBar>().Contains(firstRow)&&Math.Abs(ContentScroll.VerticalOffset-offset)<1,"Queue refresh preserves existing rows and scroll position");
            downloads.Changed+=DownloadChanged;UpdateDownloadBadge();UpdateLayout();Check(Descendants(DownloadsNav).OfType<TextBlock>().Any(t=>t.Text=="2"),"Top bar badge counts active download jobs");var first=downloads.Jobs()[0];downloads.Control(first.Id,"pause");await Task.Delay(100);Check(Descendants(ContentPanel).OfType<TextBlock>().Any(t=>t.Text.StartsWith("Paused"))&&Descendants(DownloadsNav).OfType<TextBlock>().Any(t=>t.Text=="1"),"Download events update visible rows and badge automatically");downloads.Control(first.Id,"resume");await Task.Delay(100);Check(Descendants(DownloadsNav).OfType<TextBlock>().Any(t=>t.Text=="2"),"Resuming a job restores its active badge count");
            File.WriteAllText(Path.Combine(store.DataDirectory,"download-ui-checks.json"),JsonSerializer.Serialize(checks));
        }
        catch(Exception e){File.WriteAllText(Path.Combine(store.DataDirectory,"download-ui-failure.txt"),e.ToString());}finally{foreach(var window in OwnedWindows.Cast<Window>().ToList())window.Close();}
    }
    sealed class UiDownloadProvider : IDownloadProvider
    {
        public Task<List<IndexedRelease>> Search(VideoItem i,CancellationToken ct)=>Task.FromResult(new List<IndexedRelease>{new(new(){Id="ui-release",Name="1080p · HEVC · English",Detail="Season 1 pack · choose only the episodes you want",Size=2L*1024*1024*1024,Cached=true,CacheStatus="Ready",SizeKind="Pack",Sources=["TorBox library"],Resolution="1080p"},new string('a',40))});
        public Task<List<TorboxFile>> Files(string h,CancellationToken ct)=>Task.FromResult(new List<TorboxFile>{new(1,"Noir.UI.fixture.S01E01.mkv",1024L*1024*1024),new(2,"Noir.UI.fixture.S01E02.mkv",1024L*1024*1024)});
        public Task<TorboxTorrent> Prepare(string h,long id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<string> Link(long id,long file,CancellationToken ct)=>throw new NotSupportedException();
    }
}
#endif
