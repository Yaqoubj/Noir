using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Noir;
public partial class MainWindow
{
    MobileSyncServer? mobileSync;
    bool mobileStarting;
    string mobileStartFailure="";
    async Task StartMobileSync(int port=MobileSyncServer.Port)
    {
        if(mobileStarting||closing)return;mobileStarting=true;mobileStartFailure="";
        try
        {
            if(mobileSync!=null){await mobileSync.DisposeAsync();mobileSync=null;}
            // Read fresh from profiles.json: snapshots are built on the server's threads.
            store.ProfileTitle=()=>{try{return Profiles.ForDirectory(Profiles.Load(),store.DataDirectory)?.Name??"";}catch(IOException){return "";}};
            mobileSync=new(store,store.MobileSnapshot,ops=>Dispatcher.Invoke(()=>{var ack=store.MergeMobile(ops);if(ops.Count>0&&current==null&&!closing&&page!="Settings") Render();return ack;}),port,downloads,ConfirmPhoneSettingsShare);
            mobileSync.PreviewFrame=PhonePreviewFrame;
            mobileSync.Discovered+=()=>Dispatcher.BeginInvoke(()=>{if(current==null&&!closing&&page is not ("Settings" or "Downloads" or "Discover"))Render();});
            await mobileSync.SetEnabled(mobileSync.Config.Enabled);
        }
        catch(Exception e) {store.Log(e);mobileStartFailure=System.Text.RegularExpressions.Regex.Replace(e.GetBaseException().Message,@"(?i)\b[a-z][a-z0-9+.-]*://\S+","[link]");}
        finally{mobileStarting=false;}
    }
    void RenderMobileSettings()
    {
        ContentPanel.Children.Add(Heading("Noir on Android","Sync history, resume times, ratings and My List over the same network. Stream videos from this PC, or link a copy saved on your phone."));
        var failure=mobileStartFailure.Length>0?mobileStartFailure:mobileSync?.StartFailure??"";
        if(failure.Length>0){ContentPanel.Children.Add(Body(PL("Phone sync could not start: ","تعذر بدء مزامنة الهاتف: ")+failure,12));var retry=ActionButton(PL("Retry","إعادة المحاولة"),async()=>{await StartMobileSync();if(!closing&&page=="Settings")Render();});retry.IsEnabled=!mobileStarting;ContentPanel.Children.Add(retry);return;}
        if(mobileSync==null) {ContentPanel.Children.Add(Body(mobileStarting?PL("Phone sync is starting…","جارٍ بدء مزامنة الهاتف…"):PL("Phone sync has not started.","لم تبدأ مزامنة الهاتف."),12));if(!mobileStarting)ContentPanel.Children.Add(ActionButton(PL("Retry","إعادة المحاولة"),async()=>{await StartMobileSync();if(!closing&&page=="Settings")Render();}));return;}
        var enable=new CheckBox {Content="Allow my paired Android devices to connect",IsChecked=mobileSync.Config.Enabled};
        enable.Click+=async(_,_)=>{await mobileSync.SetEnabled(enable.IsChecked==true);Render();};ContentPanel.Children.Add(enable);
        ContentPanel.Children.Add(Body(mobileSync.Status+$" · {mobileSync.Config.Peers.Count} paired devices",12));
        if(mobileSync.Config.LastSyncAt is { } synced) ContentPanel.Children.Add(Body($"Last synced: {synced.ToLocalTime():g} · saved on this PC",12));
        var pair=ActionButton(T("Pair a phone or TV","إقران هاتف أو تلفاز"),ShowPhonePairing,true);pair.ToolTip=T("The same code pairs Noir on an Android phone or on Google TV","الرمز نفسه يقرن Noir على هاتف Android أو على Google TV");pair.IsEnabled=mobileSync.Running;ContentPanel.Children.Add(pair);
        if(mobileSync.Config.Peers.Count>0) {var revoke=ActionButton("Disconnect all paired phones",()=>{mobileSync.Revoke();Render();});revoke.Margin=new(0,10,0,0);ContentPanel.Children.Add(revoke);}
        var firewall=ActionButton("Allow phone access through Windows",()=>
        {
            var script=Path.Combine(AppContext.BaseDirectory,"Allow-Noir-Phone.ps1");
            try {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",$"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\""){UseShellExecute=true,Verb="runas",WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden});Status.Text="Windows will ask for administrator approval to allow Noir phone connections.";}
            catch(System.ComponentModel.Win32Exception) {Status.Text="Windows network permission was cancelled. History is unchanged.";}
        });firewall.Margin=new(0,10,0,0);ContentPanel.Children.Add(firewall);
        ContentPanel.Children.Add(Body("Use this if Windows blocks the phone connection. It adds a rule for Noir on local networks; Windows requires administrator approval.",12));
        ContentPanel.Children.Add(Body("Noir must be running on the PC for sync or streaming; closing the window keeps it running in the notification area. Offline phone playback queues your progress for the next connection. Pairing lasts 10 minutes and works once. No cloud account or upload is needed.",12));
    }
    void ShowPhonePairing()
    {
        if(mobileSync==null||!mobileSync.Running) return;
        var addresses=MobileSyncServer.Addresses();
        if(addresses.Count==0) {Status.Text="Connect this PC to Wi-Fi or Ethernet first.";return;}
        var dialog=new Window {Title=PL("Pair Noir for Android","إقران Noir على Android"),Owner=this,Width=510,Height=820,Background=Brush("#101012"),Foreground=Brush("#F4F1EB"),WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=Loc.Arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight};
        var panel=new StackPanel {Margin=new(26)};dialog.Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(Text(PL("Take Noir with you","خذ Noir معك"),24,weight:FontWeights.SemiBold));
        panel.Children.Add(Body(PL("Install the Noir APK on your Android phone. On the same Wi-Fi, scan this code with your camera and choose Noir, or copy this pairing link into the app.","ثبّت ملف Noir APK على هاتف Android. على شبكة Wi-Fi نفسها، امسح هذا الرمز بالكاميرا واختر Noir، أو انسخ رابط الاقتران هذا إلى التطبيق."),14));
        panel.Children.Add(Body(PL("New pairings use HTTPS port 443. Pair this PC again to switch an existing phone to that port; your saved history stays intact.","تستخدم عمليات الاقتران الجديدة منفذ HTTPS 443. أقرن هذا الكمبيوتر مجددًا لنقل هاتف مقترن إلى هذا المنفذ؛ ويبقى سجلك المحفوظ كما هو."),12));
        var chooser=new ComboBox {ItemsSource=addresses,SelectedIndex=0,Margin=new(0,8,0,12)};panel.Children.Add(chooser);
        var picture=new Image {Width=240,Height=240,Margin=new(0,8,0,18)};panel.Children.Add(picture);
        var linkBox=new TextBox {IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MinHeight=72,Margin=new(0,8,0,12),FlowDirection=FlowDirection.LeftToRight};panel.Children.Add(linkBox);
        panel.Children.Add(Text(PL("On a TV","على التلفزيون"),18,weight:FontWeights.SemiBold));
        panel.Children.Add(Body(PL("In Noir on the TV, choose Pair with your PC and type this address and code. Continue on the TV only if it shows the same check code.","في Noir على التلفزيون، اختر «الاقتران بالكمبيوتر» واكتب هذا العنوان والرمز. تابع على التلفزيون فقط إذا ظهر رمز التحقق نفسه."),12));
        var tvAddress=Text("",16);var tvCode=Text("",16);var tvCheck=Text("",26,weight:FontWeights.SemiBold);tvCheck.Foreground=Brush("#E4C084");tvCheck.Margin=new(0,4,0,14);
        panel.Children.Add(tvAddress);panel.Children.Add(tvCode);panel.Children.Add(tvCheck);
        void Update()
        {
            linkBox.Text=mobileSync.BeginPairing((string)chooser.SelectedItem);
            var query=System.Web.HttpUtility.ParseQueryString(new Uri(linkBox.Text).Query);
            tvAddress.Text=PL("Address  ","العنوان  ")+query["address"];tvCode.Text=PL("Code  ","الرمز  ")+query["code"];tvCheck.Text=PL("Check code  ","رمز التحقق  ")+MobileSyncServer.CheckCode(query["pin"]??"");
            using var data=QRCodeGenerator.GenerateQrCode(linkBox.Text,QRCodeGenerator.ECCLevel.Q);
            using var qr=new PngByteQRCode(data);var image=new BitmapImage();image.BeginInit();image.StreamSource=new MemoryStream(qr.GetGraphic(6));image.CacheOption=BitmapCacheOption.OnLoad;image.EndInit();picture.Source=image;
        }
        var validity=Text("",14);panel.Children.Add(validity);
        var copy=ActionButton(PL("Copy pairing link","نسخ رابط الاقتران"),()=>Clipboard.SetText(linkBox.Text),true);panel.Children.Add(copy);
        var renew=ActionButton(PL("New code","رمز جديد"),()=>{});renew.Click+=(_,_)=>{Update();RefreshValidity();};panel.Children.Add(renew);
        void RefreshValidity()
        {
            var state=mobileSync.PairingState;var active=state.Active;
            validity.Text=active?PL("Code expires in ","تنتهي صلاحية الرمز خلال ")+(state.Expires-DateTimeOffset.UtcNow).ToString(@"m\:ss"):state.Paired?PL("Paired ✓","تم الاقتران ✓"):PL("Code expired or used. Generate a new code.","انتهت صلاحية الرمز أو تم استخدامه. أنشئ رمزًا جديدًا.");
            copy.IsEnabled=active;renew.Visibility=active?Visibility.Collapsed:Visibility.Visible;picture.Visibility=active?Visibility.Visible:Visibility.Collapsed;
            if(!active){linkBox.Text="";tvAddress.Text=tvCode.Text=tvCheck.Text="";}
        }
        chooser.SelectionChanged+=(_,_)=>{Update();RefreshValidity();};Update();RefreshValidity();
        var validityTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};validityTimer.Tick+=(_,_)=>RefreshValidity();validityTimer.Start();
        panel.Children.Add(Body("After connecting once, syncing is automatic while Noir is open on both devices. Phone history and posters remain available offline.",12));
        dialog.Closed+=(_,_)=>{validityTimer.Stop();mobileSync.EndPairing();if(!closing&&page=="Settings") Render();};dialog.Show();
    }
}
