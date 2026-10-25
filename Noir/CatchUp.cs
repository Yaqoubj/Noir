using System.IO;
using System.Text.Json;
#if !NOIR_HEADLESS
using System.Windows;
using System.Windows.Controls;
#endif
namespace Noir;
public sealed record CatchUpRecap(string Key,string ReturnId,SmartEpisode Stopped,DateTimeOffset LastViewed,List<SmartEpisode> Episodes);
public sealed class CatchUp
{
    public int ThresholdDays {get;set;}=14;
    public Dictionary<string,string> Dismissals {get;set;}=new();
    public static CatchUp Load(string directory){try{return JsonSerializer.Deserialize<CatchUp>(File.ReadAllText(Path.Combine(directory,"catch-up.json")))??new();}catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){return new();}}
    public void Save(string directory){Directory.CreateDirectory(directory);var path=Path.Combine(directory,"catch-up.json");var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,JsonSerializer.Serialize(this),new System.Text.UTF8Encoding(false));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
    public static int GetThresholdDays(string directory)=>Math.Clamp(Load(directory).ThresholdDays,0,365);
    public static void SetThresholdDays(string directory,int days){var settings=Load(directory);settings.ThresholdDays=Math.Clamp(days,0,365);settings.Save(directory);}
    public CatchUpRecap? Build(SmartTitle title,DateTimeOffset now)
    {
        if(title.Movie||title.Dropped||ThresholdDays<=0)return null;SmartEpisode? stopped=null;
        foreach(var ep in title.Episodes)if((ep.Watched||ep.Started)&&ep.LastViewed>DateTimeOffset.MinValue&&ep.LastViewed<=now&&(stopped==null||ep.LastViewed>stopped.LastViewed||ep.LastViewed==stopped.LastViewed&&(ep.Season>stopped.Season||ep.Season==stopped.Season&&ep.Episode>stopped.Episode)))stopped=ep;
        if(stopped==null||stopped.LastViewed>now.AddDays(-Math.Clamp(ThresholdDays,1,365)))return null;
        var token=$"{stopped.LastViewed.ToUnixTimeMilliseconds()}:{stopped.Season}:{stopped.Episode}";if(Dismissals.GetValueOrDefault(title.Key)==token)return null;
        var watched=title.Episodes.Where(e=>e.Watched&&e.Season==stopped.Season&&e.Episode<=stopped.Episode||e.Watched&&e.Season>0&&e.Season<stopped.Season).OrderByDescending(e=>e.Season).ThenByDescending(e=>e.Episode).Take(2).OrderBy(e=>e.Season).ThenBy(e=>e.Episode).ToList();
        return watched.Count==0?null:new(title.Key,token,stopped,stopped.LastViewed,watched);
    }
    public void Dismiss(CatchUpRecap recap)=>Dismissals[recap.Key]=recap.ReturnId;
}
#if !NOIR_HEADLESS
public partial class MainWindow
{
#if NOIR_VALIDATE
    void CaptureW3Element(FrameworkElement element,string name)
    {
        if(!validationMode||!Environment.GetCommandLineArgs().Contains("--w3-feature-shots"))return;var directory=store.DataDirectory;var suffix=Loc.Arabic?"-ar":"";
        element.Loaded+=async(_,_)=>{await Task.Delay(250);if(!element.IsLoaded||element.ActualWidth<1||element.ActualHeight<1)return;element.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);var drawing=new System.Windows.Media.DrawingVisual();using(var context=drawing.RenderOpen()){if(suffix.Length>0)context.PushTransform(new System.Windows.Media.ScaleTransform(-1,1,element.ActualWidth/2,0));context.DrawRectangle(new System.Windows.Media.VisualBrush(element){Stretch=System.Windows.Media.Stretch.Fill},null,new Rect(0,0,element.ActualWidth,element.ActualHeight));if(suffix.Length>0)context.Pop();}bitmap.Render(drawing);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,"w3-"+name+suffix+".png"));encoder.Save(file);};
    }
#endif
    public FrameworkElement CatchUpSettingsRow()
    {
        var row=new StackPanel{Margin=new(0,8,0,8)};row.Children.Add(Text(T("Catch me up","ذكّرني بالأحداث"),15));var choices=new[]{0,7,14,30,60};var box=new ComboBox{MinWidth=180,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,8,0,0)};var value=CatchUp.GetThresholdDays(store.DataDirectory);foreach(var days in choices)box.Items.Add(new ComboBoxItem{Content=days==0?T("Off","إيقاف"):T("After {0} days","بعد {0} أيام",days),Tag=days,IsSelected=value==days});box.SelectionChanged+=(_,_)=>{if(box.SelectedItem is ComboBoxItem{Tag:int days})CatchUp.SetThresholdDays(store.DataDirectory,days);};row.Children.Add(box);return row;
    }
    FrameworkElement? CatchUpCard(List<VideoItem> group,bool compact=false)
    {
        if(group.Count==0)return null;var settings=CatchUp.Load(store.DataDirectory);var recap=settings.Build(SmartLists.Read(store,group),DateTimeOffset.UtcNow);if(recap==null)return null;WarmSmartCredits([group]);
        var panel=new StackPanel{Margin=new(compact?12:20)};var border=new Border{Background=Brush("#EE19191C"),BorderBrush=Brush("#55E4C084"),BorderThickness=new(1),CornerRadius=new(14),Margin=new(0,14,0,8),Child=panel,MaxWidth=compact?520:850,HorizontalAlignment=HorizontalAlignment.Left};
        var top=new DockPanel();var dismiss=ActionButton("✕",()=>{var latest=CatchUp.Load(store.DataDirectory);latest.Dismiss(recap);latest.Save(store.DataDirectory);border.Visibility=Visibility.Collapsed;Render();});dismiss.Padding=new(8,2,8,2);dismiss.ToolTip=T("Dismiss catch-up","إخفاء التذكير");System.Windows.Automation.AutomationProperties.SetName(dismiss,(string)dismiss.ToolTip);DockPanel.SetDock(dismiss,Dock.Right);top.Children.Add(dismiss);top.Children.Add(Text(T("Catch me up","ذكّرني بالأحداث"),compact?14:18,"#E4C084",System.Windows.FontWeights.SemiBold));panel.Children.Add(top);
        var stopped=Text(T("You stopped at {0} · {1}","توقفت عند {0} · {1}",SmartLists.Episode(recap.Stopped),SmartLists.Ago(recap.LastViewed,DateTimeOffset.UtcNow,Loc.Arabic)),12,"#CCC5BA");stopped.Margin=new(0,6,0,8);panel.Children.Add(stopped);
        foreach(var ep in recap.Episodes){var text=Text(SmartLists.Episode(ep)+" · "+(string.IsNullOrWhiteSpace(ep.Summary)?T("No saved summary for this watched episode.","لا يوجد ملخص محفوظ لهذه الحلقة المشاهدة."):System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(ep.Summary,"<[^>]*>",""))),compact?12:14,"#D6D0C7");text.LineHeight=compact?18:21;text.MaxHeight=compact?54:84;text.TextTrimming=TextTrimming.WordEllipsis;text.Margin=new(0,5,0,0);panel.Children.Add(text);}
        if(!compact&&smartCredits.GetValueOrDefault(group[0].GroupKey) is {} credits){var cast=new WrapPanel{Margin=new(0,12,0,0)};foreach(var person in credits.Cast.Where(p=>p.Photo.Length>0&&File.Exists(p.Photo)).Take(5)){var photo=new Border{Width=40,Height=40,CornerRadius=new(20),ClipToBounds=true,Margin=new(0,0,8,0),ToolTip=person.Name,Child=Art(person.Photo,person.Name,40)};cast.Children.Add(photo);}if(cast.Children.Count>0)panel.Children.Add(cast);}
        Motion.EnterOnce(border);
#if NOIR_VALIDATE
        CaptureW3Element(border,compact?"catch-up-compact":"catch-up-detail");
#endif
        return border;
    }
}
#endif
