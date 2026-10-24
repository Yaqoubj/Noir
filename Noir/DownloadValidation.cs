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
        void Picture(FrameworkElement element,string name){element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var context=drawing.RenderOpen())context.DrawRectangle(new VisualBrush(element),null,new Rect(0,0,element.ActualWidth,element.ActualHeight));bitmap.Render(drawing);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(store.DataDirectory,name));encoder.Save(output);}
        try
        {
            var media=Path.Combine(store.DataDirectory,"fixture-media");Directory.CreateDirectory(media);store.State.Roots=[new(){Path=media}];store.State.Catalog.Clear();store.State.OnlineMetadata=false;
            var item=new VideoItem{Id="download-ui",Title="Noir UI fixture",CollectionKey="ui-fixture",Year=2026,Season=1,Episode=1,ImdbId="tt123",Available=false};store.State.Catalog[item.Id]=new(){Item=item};store.State.EpisodeSchedules[item.GroupKey]=[new(){Season=1,Episode=1},new(){Season=1,Episode=2}];store.Save();downloads=new(store,new UiDownloadProvider(),start:false,freeSpace:_=>10L*1024*1024*1024);downloads.Configure("https://example.invalid/fixture/manifest.json","fixture-not-real",media,"");
            settingsSection="Downloads";Navigate("Settings");Check(Descendants(ContentPanel).OfType<PasswordBox>().Count()==3,"Both provider fields and the subtitle addon link hide private configuration");Check(Descendants(ContentPanel).OfType<CheckBox>().Any(b=>b.Content as string=="Download Arabic subtitles"),"Settings include Arabic subtitle download and automatic timing preferences");ContentScroll.ScrollToBottom();UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"download-settings.png"));
            detailKey=item.GroupKey;page="Shows";Render();ContentScroll.ScrollToTop();UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"download-episode-actions.png"));Check(Descendants(ContentPanel).OfType<Button>().Any(b=>b.Content as string is "↓ Download" or "▶ Stream"),"Unavailable episode has a visible Stream or Download button");
            FindDownloads(item,true);await Task.Delay(600);var picker=OwnedWindows.Cast<Window>().Single(w=>w.Title.StartsWith("Season planner"));
            var planner=(DependencyObject)picker.Content;
            Check(Descendants(planner).OfType<Button>().Any(b=>b.Content as string=="Unwatched only")&&Descendants(planner).OfType<Button>().Any(b=>b.Content as string=="Several seasons / whole show"),"Season planner exposes scope chips and whole-show planning");
            Check(Descendants(planner).OfType<TextBlock>().Any(t=>t.Text=="Ready")&&Descendants(planner).OfType<TextBlock>().Any(t=>t.Text.Contains("Season pack covers 2 of 2")),"Episode cards show ready status and an explainable pack choice");
            Picture((FrameworkElement)picker.Content,"download-releases.png");
            var change=Descendants(planner).OfType<Button>().First(b=>b.Content as string=="Change");change.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var alternative=change.ContextMenu.Items.OfType<MenuItem>().Single(m=>Descendants((DependencyObject)m.Header).OfType<TextBlock>().Any(t=>t.Text=="1080p single alternative"));alternative.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));change.ContextMenu.IsOpen=false;
            Check(Descendants(planner).OfType<TextBlock>().Any(t=>t.Text=="Your choice · Ready"),"Change menu swaps a pack episode to an available single");
            var episodeBoxes=Descendants(planner).OfType<CheckBox>().Where(b=>b.Content is TextBlock t&&t.Text.StartsWith("E")).ToList();Check(episodeBoxes.Count==2,"Season planner lists both known episodes");
            episodeBoxes[1].IsChecked=false;Check(Descendants(planner).OfType<Button>().Any(b=>b.Content as string=="Download 1 episodes"),"Excluding an episode updates the primary action");episodeBoxes[1].IsChecked=true;
            var auto=Descendants(planner).OfType<CheckBox>().Single(b=>b.Content as string=="Auto-download new episodes");auto.IsChecked=true;Check(downloads.SeasonPreferences(item.Id).AutoDownload,"The per-show automatic-download toggle persists immediately");auto.IsChecked=false;
            Picture((FrameworkElement)picker.Content,"download-files.png");
            var submit=Descendants(planner).OfType<Button>().Single(b=>b.Content as string=="Download 2 episodes");submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(300);Check(downloads.Jobs().Count(j=>j.Target.Title=="Noir UI fixture")==2&&downloads.Jobs().Any(j=>j.Hash==new string('b',40)),"One confirmation queues the chosen pack and swapped single");Check(page=="Downloads","Queued files open the permanent Downloads tab");Picture(ContentPanel,"download-queue.png");var firstRow=Descendants(ContentPanel).OfType<ProgressBar>().First();ContentScroll.ScrollToBottom();var offset=ContentScroll.VerticalOffset;downloadPageUpdate?.Invoke();Check(Descendants(ContentPanel).OfType<ProgressBar>().Contains(firstRow)&&Math.Abs(ContentScroll.VerticalOffset-offset)<1,"Queue refresh preserves existing rows and scroll position");
            downloads.Changed+=DownloadChanged;UpdateDownloadBadge();UpdateLayout();Check(Descendants(DownloadsNav).OfType<TextBlock>().Any(t=>t.Text=="2"),"Top bar badge counts active download jobs");var first=downloads.Jobs().First(j=>j.Target.Title=="Noir UI fixture");downloads.Control(first.Id,"pause");await Task.Delay(100);Check(Descendants(ContentPanel).OfType<TextBlock>().Any(t=>t.Text.StartsWith("Paused"))&&Descendants(DownloadsNav).OfType<TextBlock>().Any(t=>t.Text=="1"),"Download events update visible rows and badge automatically");downloads.Control(first.Id,"resume");await Task.Delay(100);Check(Descendants(DownloadsNav).OfType<TextBlock>().Any(t=>t.Text=="2"),"Resuming a job restores its active badge count");
            File.WriteAllText(Path.Combine(store.DataDirectory,"download-ui-checks.json"),JsonSerializer.Serialize(checks));
        }
        catch(Exception e){File.WriteAllText(Path.Combine(store.DataDirectory,"download-ui-failure.txt"),e.ToString());}finally{foreach(var window in OwnedWindows.Cast<Window>().ToList())window.Close();}
    }
    sealed class UiDownloadProvider : IDownloadProvider
    {
        public Task<List<IndexedRelease>> Search(VideoItem i,CancellationToken ct)=>Task.FromResult(new List<IndexedRelease>{new(new(){Id=Guid.NewGuid().ToString("N"),Name="1080p · HEVC · English",Detail="Season 1 pack · choose only the episodes you want",Size=2L*1024*1024*1024,Cached=true,CacheStatus="Ready",SizeKind="Pack",Sources=["TorBox library"],Resolution="1080p"},new string('a',40)),new(new(){Id=Guid.NewGuid().ToString("N"),Name="1080p single alternative",Resolution="1080p",Languages="English",Cached=true,CacheStatus="Ready",Sources=["AIOStreams"]},new string('b',40))});
        public Task<List<TorboxFile>> Files(string h,CancellationToken ct)=>Task.FromResult(h[0]=='b'?new List<TorboxFile>{new(1,"Noir.UI.fixture.S01E01.mkv",512L*1024*1024)}:new List<TorboxFile>{new(1,"Noir.UI.fixture.S01E01.mkv",1024L*1024*1024),new(2,"Noir.UI.fixture.S01E02.mkv",1024L*1024*1024)});
        public Task<TorboxTorrent> Prepare(string h,long id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<string> Link(long id,long file,CancellationToken ct)=>throw new NotSupportedException();
    }
}
#endif
