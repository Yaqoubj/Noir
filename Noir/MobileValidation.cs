#if NOIR_VALIDATE
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace Noir;
public partial class MainWindow
{
    async Task ValidateMobileUi()
    {
        var results=new List<string>();void Check(bool ok,string label){if(!ok)throw new Exception(label);results.Add("PASS: "+label);File.WriteAllText(Path.Combine(store.DataDirectory,"mobile-ui-progress.json"),JsonSerializer.Serialize(results));}
        try
        {
            await StartMobileSync(49273);Check(mobileSync?.Running==true,"Native Windows starts phone sync successfully");
            Navigate("Settings");UpdateLayout();Check(Descendants(ContentPanel).OfType<TextBlock>().Any(t=>t.Text.Contains("Noir on Android")),"Phone pairing is visible in Windows Settings");CaptureLibrary(Path.Combine(store.DataDirectory,"mobile-settings.png"));
            using var handler=new HttpClientHandler {ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert!=null&&Convert.ToHexString(SHA256.HashData(cert.RawData))==mobileSync!.Pin};
            using var http=new HttpClient(handler) {BaseAddress=new("https://127.0.0.1:49273")};
            var link=mobileSync!.BeginPairing("127.0.0.1");var code=System.Web.HttpUtility.ParseQueryString(new Uri(link).Query)["code"];
            var paired=await http.PostAsJsonAsync("/v1/pair",new{code,name="WPF validation"});paired.EnsureSuccessStatusCode();var result=await paired.Content.ReadFromJsonAsync<JsonElement>();http.DefaultRequestHeaders.Authorization=new("Bearer",result.GetProperty("token").GetString());
            var catalog=await http.GetFromJsonAsync<JsonElement>("/v1/library");Check(catalog.GetProperty("items").GetArrayLength()>20,"Phone library crosses WPF dispatcher without blocking");
            var item=store.Archive.First(i=>!i.IsMovie);var op=new MobileOperation {Id=Guid.NewGuid().ToString(),ItemId=item.Id,Kind="progress",At=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),Position=349.125,Duration=1800,ViewingId=Guid.NewGuid().ToString()};
            (await http.PostAsJsonAsync("/v1/sync",new[]{op})).EnsureSuccessStatusCode();Check(store.GetProgress(item)?.Position==349.125,"Phone progress updates actual native Windows view and saved bookmark");
            var lastSync=mobileSync.Config.LastSyncAt;
            Check(lastSync!=null,"Successful sync records its time on the PC");
            await using(var restored=new MobileSyncServer(store,store.MobileSnapshot,store.MergeMobile,49274))
            {Check(restored.Config.LastSyncAt==lastSync&&restored.Config.Peers.Count==mobileSync.Config.Peers.Count&&restored.Pin==mobileSync.Pin,"Reopening restores the last sync, paired phones and certificate");}

            var doro=items.Where(i=>i.Title=="Dorohedoro"&&i.Season==2).ToList();
            Check(doro.Count==11&&doro.All(i=>i.GroupKey==items.First(x=>x.Title=="Dorohedoro"&&x.Season==1).GroupKey),"All eleven Dorohedoro season 2 files join the existing show");
            var video=items.First(i=>i.Title=="Dorohedoro"&&i.Season==1&&i.Episode==1);
            VolumeBar.Value=0;store.State.Volume=0;store.State.AutoSkipIntro=false;
            var lastBeat=DateTime.UtcNow;double largestGap=0;var beats=0;
            var heartbeat=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(40)};
            heartbeat.Tick+=(_,_)=>{var now=DateTime.UtcNow;largestGap=Math.Max(largestGap,(now-lastBeat).TotalSeconds);lastBeat=now;beats++;};heartbeat.Start();
            await PlayVideo(video,true);
            var deadline=DateTime.UtcNow.AddSeconds(20);
            while(player?.IsPlaying!=true&&DateTime.UtcNow<deadline) await Task.Delay(100);
            await Task.Delay(1000);heartbeat.Stop();
            Check(player?.IsPlaying==true&&beats>5&&largestGap<1.5,$"First native playback stays responsive (largest UI gap {largestGap:F2}s)");
            player!.SetPause(true);await Task.Delay(200);SavePlayback();
            var phonePosition=517.375;
            var latest=new MobileOperation {Id=Guid.NewGuid().ToString(),ItemId=video.Id,Kind="progress",At=DateTimeOffset.UtcNow.AddMilliseconds(100).ToUnixTimeMilliseconds(),Position=phonePosition,Duration=1800,ViewingId=Guid.NewGuid().ToString()};
            (await http.PostAsJsonAsync("/v1/sync",new[]{latest})).EnsureSuccessStatusCode();
            SavePlayback();ReturnToLibrary();
            Check(new LibraryStore(store.DataDirectory).GetProgress(video)?.Position==phonePosition,"Leaving paused PC playback preserves the newer synced phone position across reopening");

            var watcherTitle="Watcher show "+Guid.NewGuid().ToString("N")[..8];
            var fixtureRoot=Path.Combine(store.DataDirectory,"watch-fixture",watcherTitle);var season1=Path.Combine(fixtureRoot,watcherTitle+" (2024)","Season 01");Directory.CreateDirectory(season1);
            File.WriteAllBytes(Path.Combine(season1,watcherTitle+" S01E01.mkv"),[]);
            store.State.Roots.Add(new(){Path=fixtureRoot});await ScanLibrary();WatchLibraryFolders();
            var season2=Path.Combine(fixtureRoot,watcherTitle+" (2024)","Season 02");Directory.CreateDirectory(season2);
            File.WriteAllBytes(Path.Combine(season2,watcherTitle+".S02E01.1080p.mkv"),[]);
            deadline=DateTime.UtcNow.AddSeconds(15);
            while(!store.Archive.Any(i=>i.Title==watcherTitle&&i.Season==2)&&DateTime.UtcNow<deadline) await Task.Delay(100);
            Check(store.Archive.Any(i=>i.Title==watcherTitle&&i.Season==2),"A new season is detected automatically without restarting or clicking Scan");
            var syncedCatalog=await http.GetFromJsonAsync<JsonElement>("/v1/library");
            Check(syncedCatalog.GetProperty("items").EnumerateArray().Any(i=>i.GetProperty("title").GetString()==watcherTitle&&i.GetProperty("season").GetInt32()==2),"Automatically detected new seasons reach the phone sync catalog");
            await mobileSync.DisposeAsync();mobileSync=null;
            File.WriteAllText(Path.Combine(store.DataDirectory,"mobile-ui-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true}));
        }
        catch(Exception e){File.WriteAllText(Path.Combine(store.DataDirectory,"mobile-ui-failure.txt"),e.ToString());}
    }
}
#endif
