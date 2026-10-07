using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Noir;
public partial class MainWindow
{
    MobileSyncServer? mobileSync;
    async Task StartMobileSync(int port=MobileSyncServer.Port)
    {
        try
        {
            mobileSync=new(store,store.MobileSnapshot,ops=>Dispatcher.Invoke(()=>{var ack=store.MergeMobile(ops);if(current==null&&!closing) Render();return ack;}),port);
            await mobileSync.SetEnabled(mobileSync.Config.Enabled);
        }
        catch(Exception e) {store.Log(e);}
    }
    void RenderMobileSettings()
    {
        ContentPanel.Children.Add(Heading("Noir on Android","Sync history, resume times, ratings and My List over the same network. Stream videos from this PC, or link a copy saved on your phone."));
        if(mobileSync==null) {ContentPanel.Children.Add(Body("Phone sync is starting. Return to Settings in a moment.",12));return;}
        var enable=new CheckBox {Content="Allow my paired Android devices to connect",IsChecked=mobileSync.Config.Enabled};
        enable.Click+=async(_,_)=>{await mobileSync.SetEnabled(enable.IsChecked==true);Render();};ContentPanel.Children.Add(enable);
        ContentPanel.Children.Add(Body(mobileSync.Status+$" · {mobileSync.Config.Peers.Count} paired devices",12));
        if(mobileSync.Config.LastSyncAt is { } synced) ContentPanel.Children.Add(Body($"Last synced: {synced.ToLocalTime():g} · saved on this PC",12));
        var pair=ActionButton("Pair a phone",ShowPhonePairing,true);pair.IsEnabled=mobileSync.Running;ContentPanel.Children.Add(pair);
        if(mobileSync.Config.Peers.Count>0) {var revoke=ActionButton("Disconnect all paired phones",()=>{mobileSync.Revoke();Render();});revoke.Margin=new(0,10,0,0);ContentPanel.Children.Add(revoke);}
        var firewall=ActionButton("Allow phone access through Windows",()=>
        {
            var script=Path.Combine(AppContext.BaseDirectory,"Allow-Noir-Phone.ps1");
            try {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",$"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\""){UseShellExecute=true,Verb="runas",WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden});Status.Text="Windows will ask for administrator approval to allow Noir phone connections.";}
            catch(System.ComponentModel.Win32Exception) {Status.Text="Windows network permission was cancelled. History is unchanged.";}
        });firewall.Margin=new(0,10,0,0);ContentPanel.Children.Add(firewall);
        ContentPanel.Children.Add(Body("Use this if Windows blocks the phone connection. It adds a rule for Noir on local networks; Windows requires administrator approval.",12));
        ContentPanel.Children.Add(Body("Keep Noir open on the PC for sync or streaming. Offline phone playback queues your progress for the next connection. Pairing lasts 10 minutes and works once. No cloud account or upload is needed.",12));
    }
    void ShowPhonePairing()
    {
        if(mobileSync==null||!mobileSync.Running) return;
        var addresses=MobileSyncServer.Addresses();
        if(addresses.Count==0) {Status.Text="Connect this PC to Wi-Fi or Ethernet first.";return;}
        var dialog=new Window {Title="Pair Noir for Android",Owner=this,Width=510,Height=650,Background=Brush("#101012"),Foreground=Brush("#F4F1EB"),WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel {Margin=new(26)};dialog.Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(Text("Take Noir with you",24,weight:FontWeights.SemiBold));
        panel.Children.Add(Body("Install the Noir APK on your Android phone. On the same Wi-Fi, scan this code with your camera and choose Noir, or copy this pairing link into the app.",14));
        panel.Children.Add(Body("New pairings use HTTPS port 443. Pair this PC again to switch an existing phone to that port; your saved history stays intact.",12));
        var chooser=new ComboBox {ItemsSource=addresses,SelectedIndex=0,Margin=new(0,8,0,12)};panel.Children.Add(chooser);
        var picture=new Image {Width=240,Height=240,Margin=new(0,8,0,18)};panel.Children.Add(picture);
        var linkBox=new TextBox {IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MinHeight=72,Margin=new(0,8,0,12)};panel.Children.Add(linkBox);
        void Update()
        {
            linkBox.Text=mobileSync.BeginPairing((string)chooser.SelectedItem);
            using var data=QRCodeGenerator.GenerateQrCode(linkBox.Text,QRCodeGenerator.ECCLevel.Q);
            using var qr=new PngByteQRCode(data);var image=new BitmapImage();image.BeginInit();image.StreamSource=new MemoryStream(qr.GetGraphic(6));image.CacheOption=BitmapCacheOption.OnLoad;image.EndInit();picture.Source=image;
        }
        chooser.SelectionChanged+=(_,_)=>Update();Update();
        panel.Children.Add(ActionButton("Copy pairing link",()=>Clipboard.SetText(linkBox.Text),true));
        panel.Children.Add(Body("After connecting once, syncing is automatic while Noir is open on both devices. Phone history and posters remain available offline.",12));
        dialog.Closed+=(_,_)=>{mobileSync.EndPairing();if(!closing&&page=="Settings") Render();};dialog.Show();
    }
}
