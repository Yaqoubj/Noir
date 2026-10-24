using System.Windows;
using System.Windows.Controls;

namespace Noir;
public partial class MainWindow
{
    Task<bool> ConfirmPhoneSettingsShare(string phone,CancellationToken cancellation)
    {
        var answer=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.BeginInvoke(()=>
        {
            if(cancellation.IsCancellationRequested||closing){answer.TrySetResult(false);return;}
            if(!IsVisible)Show();
            if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
            Activate();
            var dialog=new Window{Title="Share phone download settings",Owner=this,Width=470,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#101012"),Foreground=Brush("#F4F1EB")};
            var panel=new StackPanel{Margin=new(24)};dialog.Content=panel;
            panel.Children.Add(Text($"Send download settings to {phone}?",20,weight:FontWeights.SemiBold));
            panel.Children.Add(Body("This sends your AIOStreams manifest and download API keys once over the paired encrypted connection. The phone encrypts them locally and can download while this PC is off. This request expires after 2 minutes.",14));
            panel.Children.Add(ActionButton("Send download settings",()=>{if(!cancellation.IsCancellationRequested)answer.TrySetResult(true);dialog.Close();},true));
            panel.Children.Add(ActionButton("Cancel",()=>dialog.Close()));
            dialog.Closed+=(_,_)=>answer.TrySetResult(false);
            var registration=cancellation.Register(()=>Dispatcher.BeginInvoke(()=>{answer.TrySetResult(false);dialog.Close();}));
            dialog.Closed+=(_,_)=>registration.Dispose();dialog.Show();dialog.Activate();
        });return answer.Task;
    }
}
