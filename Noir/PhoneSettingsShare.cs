using System.Windows;
using System.Windows.Controls;

namespace Noir;
public partial class MainWindow
{
    Task<DownloadDeleteChoice?> ConfirmPhoneDownloadDelete(DownloadLocalFile file,DownloadDeleteChoice saved,CancellationToken cancellation)
    {
        var answer=new TaskCompletionSource<DownloadDeleteChoice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.BeginInvoke(()=>
        {
            if(cancellation.IsCancellationRequested||closing){answer.TrySetResult(null);return;}
            if(!IsVisible)Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();PrepareDownloadManagement();
            var sheet=OpenSheet(T("Delete files","حذف الملفات"),file.Item.Label+" · "+DownloadNaming.Size(file.Bytes),SheetKind.Dialog,560);
            sheet.Add(Body(T("Your phone requested this deletion. Watch history, ratings, and titles stay in your library.","طلب هاتفك هذا الحذف. يبقى سجل المشاهدة والتقييم والعناوين في المكتبة."),13));
            var recycle=new RadioButton{Content=T("Move to Recycle Bin","نقل إلى سلة المحذوفات"),GroupName="PhoneDownloadDelete",IsChecked=saved!=DownloadDeleteChoice.Permanent,Foreground=Brush("#F4F1EB"),Margin=new(0,16,0,5)};
            var permanent=new RadioButton{Content=T("Delete permanently","حذف نهائي"),GroupName="PhoneDownloadDelete",IsChecked=saved==DownloadDeleteChoice.Permanent,Foreground=Brush("#E5736B"),Margin=new(0,16,0,5)};
            DownloadName(recycle,(string)recycle.Content);DownloadName(permanent,(string)permanent.Content);sheet.Add(recycle);sheet.Add(Body(T("Space is freed when you empty the Recycle Bin","تتحرر المساحة عند إفراغ سلة المحذوفات"),12));sheet.Add(permanent);
            var remember=new CheckBox{Content=T("Remember my choice","تذكر اختياري"),Margin=new(0,18,0,12)};DownloadName(remember,(string)remember.Content);sheet.Add(remember);
            sheet.Action(T("Cancel","إلغاء"),()=>sheet.Close(false));sheet.Action(T("Delete","حذف"),()=>{if(!cancellation.IsCancellationRequested){var choice=recycle.IsChecked==true?DownloadDeleteChoice.Recycle:DownloadDeleteChoice.Permanent;if(remember.IsChecked==true)downloads!.SetDeleteChoice(choice);answer.TrySetResult(choice);}sheet.Close(true);},true);
            sheet.Closed+=_=>answer.TrySetResult(null);var registration=cancellation.Register(()=>Dispatcher.BeginInvoke(()=>{answer.TrySetResult(null);sheet.Close(false);}));sheet.Closed+=_=>registration.Dispose();
        });return answer.Task;
    }
    Task<bool> ConfirmPhoneSettingsShare(string phone,CancellationToken cancellation)
    {
        var answer=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.BeginInvoke(()=>
        {
            if(cancellation.IsCancellationRequested||closing){answer.TrySetResult(false);return;}
            if(!IsVisible)Show();
            if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
            Activate();
            var dialog=OpenSheet("Share phone download settings",$"Send download settings to {phone}?",SheetKind.Dialog,460);
            dialog.Add(Body("This sends your AIOStreams manifest and download API keys once over the paired encrypted connection. The phone encrypts them locally and can download while this PC is off. This request expires after 2 minutes.",13));
            var subtitle=SubtitleSelection.From(downloads?.Config??new());
            if(subtitle.Chosen)dialog.Add(Body(subtitle.Source=="addon"?T("This also shares your subtitle addon link and its authorization."):subtitle.Name+" · "+T("This also shares this subtitle site's API key and optional login."),13));
            dialog.Action("Cancel",()=>dialog.Close(false));
            dialog.Action("Send download settings",()=>{if(!cancellation.IsCancellationRequested)answer.TrySetResult(true);dialog.Close(true);},true);
            dialog.Closed+=_=>answer.TrySetResult(false);
            var registration=cancellation.Register(()=>Dispatcher.BeginInvoke(()=>{answer.TrySetResult(false);dialog.Close(false);}));
            dialog.Closed+=_=>registration.Dispose();
        });return answer.Task;
    }
}
