using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;

namespace Noir;
public sealed class PreviewFrames : IAsyncDisposable
{
    readonly string directory;
    readonly SemaphoreSlim gate=new(1,1);
    LibVLC? previewEngine;
    bool disposed;
    public PreviewFrames(string dataDirectory) {directory=Path.Combine(dataDirectory,"previews");Directory.CreateDirectory(directory);}
    public static double Slot(double seconds) => Math.Max(0,Math.Floor(seconds/10)*10);
    string CachePath(VideoItem item,string fingerprint,double seconds)=>Path.Combine(directory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Id+":"+fingerprint+":"+Slot(seconds))))+".png");
    public BitmapSource? Cached(VideoItem item,string fingerprint,double seconds)
    {
        var path=CachePath(item,fingerprint,seconds);if(!File.Exists(path))return null;
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(path);image.EndInit();image.Freeze();return image;
    }
    public async Task<BitmapSource?> Get(VideoItem item,string fingerprint,double seconds,CancellationToken token)
    {
        if(disposed)return null;
        var slot=Slot(seconds);var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Id+":"+fingerprint+":"+slot)));
        var path=Path.Combine(directory,key+".png");
        BitmapSource Read(){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(path);image.EndInit();image.Freeze();return image;}
        if(File.Exists(path)) return Read();
        await gate.WaitAsync(token);
        try
        {
            if(disposed)return null;token.ThrowIfCancellationRequested();
            if(File.Exists(path)) return Read();
            var pixels=await Task.Run(async()=>
            {
                var vlc=previewEngine??=new LibVLC("--quiet","--no-audio","--no-spu","--avcodec-hw=none","--avcodec-threads=1","--no-video-title-show");
                using var decoder=new LibVLCSharp.Shared.MediaPlayer(vlc);
                using var media=new Media(vlc,new Uri(item.Path));media.AddOption(":start-time="+slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var allocation=Marshal.AllocHGlobal(256*144*4+32);var buffer=new IntPtr((allocation.ToInt64()+31)&~31L);
                var completed=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                var bufferGate=new object();
                decoder.SetVideoFormat("RV32",256,144,1024);
                decoder.SetVideoCallbacks((_,planes)=>{System.Threading.Monitor.Enter(bufferGate);Marshal.WriteIntPtr(planes,buffer);return IntPtr.Zero;},(_,_,_)=>
                {
                    try {if(!completed.Task.IsCompleted && Math.Abs(decoder.Time/1000d-slot)<2) {var copy=new byte[256*144*4];Marshal.Copy(buffer,copy,0,copy.Length);completed.TrySetResult(copy);}}
                    catch(Exception e){completed.TrySetException(e);}
                    finally {System.Threading.Monitor.Exit(bufferGate);}
                },(_,_)=>{});
                try
                {
                    token.ThrowIfCancellationRequested();if(!decoder.Play(media)) return null;
                    return await completed.Task.WaitAsync(TimeSpan.FromSeconds(8),token);
                }
                catch(TimeoutException){return null;}
                finally {decoder.Stop();decoder.Dispose();Marshal.FreeHGlobal(allocation);}
            },token);
            if(pixels==null) return null;token.ThrowIfCancellationRequested();
            var bitmap=BitmapSource.Create(256,144,96,96,PixelFormats.Bgr32,null,pixels,1024);bitmap.Freeze();
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path+".tmp")) encoder.Save(file);File.Move(path+".tmp",path,true);
            // Generated previews are disposable and bounded; library history and artwork are separate.
            var files=new DirectoryInfo(directory).EnumerateFiles("*.png").OrderBy(f=>f.LastWriteTimeUtc).ToList();long size=files.Sum(f=>f.Length);
            foreach(var file in files) {if(size<=128*1024*1024) break;size-=file.Length;try{file.Delete();}catch(IOException){}}
            return bitmap;
        }
        finally {gate.Release();}
    }
    public async ValueTask DisposeAsync(){disposed=true;await gate.WaitAsync();try{if(previewEngine is {} engine)await Task.Run(engine.Dispose);previewEngine=null;}finally{gate.Release();}}
}
public partial class MainWindow
{
    PreviewFrames? previews;
    CancellationTokenSource? previewCancellation;
    Border? previewPanel;
    Image? previewImage;
    TextBlock? previewTime;
    double previewSlot=-1;
    async void Seek_Preview(object sender,MouseEventArgs e)
    {
        if(current==null||currentDuration<=0||!ControlsVisible||openingVideo||pendingTarget!=null||seeking) return;
        var x=Math.Clamp(e.GetPosition(SeekBar).X,0,SeekBar.ActualWidth);var seconds=x/Math.Max(1,SeekBar.ActualWidth)*currentDuration;
        if(previewPanel==null)
        {
            var content=new StackPanel();previewImage=new Image{Width=240,Height=135,Stretch=Stretch.Uniform};previewTime=Text("",12);previewTime.TextAlignment=TextAlignment.Center;previewTime.Margin=new(8);content.Children.Add(previewImage);content.Children.Add(previewTime);
            previewPanel=new Border{Width=242,Background=Brush("#ED171719"),CornerRadius=new(8),BorderThickness=new(1),BorderBrush=Brush("#45413B"),Child=content,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false};PlayerOverlay.Children.Add(previewPanel);
        }
        previewTime!.Text=LibraryStore.Time(seconds);var point=SeekBar.TranslatePoint(new Point(x,0),PlayerOverlay);
        previewPanel.Margin=new(Math.Clamp(point.X-121,12,Math.Max(12,PlayerOverlay.ActualWidth-254)),0,0,PlayerFooter.ActualHeight+12);previewPanel.Visibility=Visibility.Visible;
        var slot=PreviewFrames.Slot(seconds);if(slot==previewSlot) return;previewSlot=slot;previewImage!.Source=null;previewImage.Visibility=Visibility.Collapsed;
        previewCancellation?.Cancel();var request=previewCancellation=new CancellationTokenSource();var item=current;var thisSerial=serial;
        try
        {
            previews??=new PreviewFrames(store.DataDirectory);
            var cached=previews.Cached(item,store.State.Catalog[item.Id].Fingerprint,seconds);
            if(cached!=null){previewImage.Source=cached;previewImage.Visibility=Visibility.Visible;return;}
            await Task.Delay(550,request.Token);
            var image=await previews.Get(item,store.State.Catalog[item.Id].Fingerprint,seconds,request.Token);
            if(request.IsCancellationRequested||serial!=thisSerial||current!=item||!SeekBar.IsMouseOver) return;
            if(image!=null) {previewImage.Source=image;previewImage.Visibility=Visibility.Visible;}
        }
        catch(OperationCanceledException){}
        catch(Exception error){store.Log(error);}
        finally{if(ReferenceEquals(previewCancellation,request))previewCancellation=null;request.Dispose();}
    }
    void HidePreview() {previewCancellation?.Cancel();previewSlot=-1;if(previewPanel!=null) previewPanel.Visibility=Visibility.Collapsed;}
    void Seek_Leave(object sender,MouseEventArgs e)=>HidePreview();
}
