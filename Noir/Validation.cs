#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Noir;
public partial class MainWindow
{
    async Task ValidatePlayback()
    {
        if(Environment.GetCommandLineArgs().Contains("--navigation-only")){await ValidateNavigationDiscover();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--ux-only")){await ValidateUx();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--smoothness-only")){await ValidatePlayerSmoothness();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--downloads-only")) {await ValidateDownloads();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--premium-only")) {await ValidatePremium();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--embeds-only")) {await ValidateEmbeds();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--history-organizer-only")) {await ValidateOrganizerHistory();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--metadata-refresh-only")) {await ValidateMetadataRefresh();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--mobile-ui-only")) {await ValidateMobileUi();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--archive-only")) {await ValidateArchiveVisibility();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--appearance-only")) {await ValidateSubtitleAppearance();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--enhancements-only")) {await ValidateEnhancements();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--sync-fixtures-only")) {await ValidateSyncFixtures();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--community-only")) {await ValidateCommunity();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--reviews-only")) {await ValidateReviews();Close();return;}
        if(Environment.GetCommandLineArgs().Contains("--render-only"))
        {
            if(Environment.GetCommandLineArgs().Contains("--enrich")) await RefreshMetadata(true);
            UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"library.png"));
            Navigate("History");UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"history.png"));
            detailKey=items.First(x=>!x.IsMovie).GroupKey;Render();UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"episodes.png"));
            Navigate("History");historyFilter="Unavailable";Render();UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"history-filter.png"));historyFilter="All";
            Navigate("Settings");UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"settings.png"));Close();return;
        }
        var results=new List<string>();
        void Assert(bool condition,string label) { if(!condition) throw new Exception("FAIL: "+label);results.Add("PASS: "+label); }
        async Task Until(Func<bool> predicate,string label,int seconds=20)
        {
            var deadline=DateTime.UtcNow.AddSeconds(seconds);
            while(!predicate()&&DateTime.UtcNow<deadline) await Task.Delay(150);
            Assert(predicate(),label);
        }
        try
        {
            store.State.Progress.Clear();foreach(var entry in store.State.Catalog.Values) entry.Progress=null;
            store.State.AutoNext=true;store.State.AutoSkipIntro=false;store.State.Volume=0;VolumeBar.Value=0;store.Save();
            var episode=items.First(x=>x.Title=="Dorohedoro"&&x.Season==1&&x.Episode==1);
            await PlayVideo(episode,true);
            await Until(()=>player?.IsPlaying==true&&player.Time>1000,"Embedded video starts and time advances");
            Assert(player!.Hwnd!=IntPtr.Zero,"VLC is attached to the in-app video surface");
            ShowControls();UpdateLayout();
            var videoSize=new Size(Video.ActualWidth,Video.ActualHeight);
            Assert(Math.Abs(videoSize.Width-PlayerRoot.ActualWidth)<1 && Math.Abs(videoSize.Height-PlayerRoot.ActualHeight)<1,"Video uses the full player area even when the controls are visible");
            HandleVideoClick(1);UpdateLayout();
            Assert(!ControlsVisible && PlayerFooter.Visibility==Visibility.Collapsed,"Clicking the picture hides both control bars");
            Assert(player.IsPlaying,"Hiding controls does not pause the video");
            Assert(Math.Abs(Video.ActualHeight-videoSize.Height)<1,"Hiding controls does not resize the video or change its framing");
            HandleVideoClick(1);Assert(!ControlsVisible,"A hidden-player click waits briefly for a possible double click");
            await Until(()=>ControlsVisible,"A single click restores the controls");
            HandleVideoClick(2);Assert(fullscreen&&!ControlsVisible,"Double click enters fullscreen without revealing bars");
            HandleVideoClick(2);Assert(!fullscreen&&!ControlsVisible,"Double click exits fullscreen with controls hidden");
            ShowControls();
            await Until(()=>!ControlsVisible,"Controls also hide after three seconds without interaction",8);
            store.State.FillVideo=true;ApplyPictureMode(true);
            Assert(player.CropGeometry==$"{(int)Math.Round(videoSize.Width)}:{(int)Math.Round(videoSize.Height)}","Fill mode sets a crop matching the whole video surface");
            var snapshot=Path.Combine(store.DataDirectory,"filled-frame.png");
            await Task.Delay(400);
            Assert(player.TakeSnapshot(0,snapshot,0,0),"VLC can export the filled video frame");
            await Until(()=>File.Exists(snapshot),"Filled video frame was captured");
            var frame=new BitmapImage();frame.BeginInit();frame.UriSource=new Uri(snapshot);frame.CacheOption=BitmapCacheOption.OnLoad;frame.EndInit();
            Assert(Math.Abs((double)frame.PixelWidth/frame.PixelHeight-videoSize.Width/videoSize.Height)<.02,"The actual decoded frame is cropped to the window aspect ratio");
            store.State.FillVideo=false;ApplyPictureMode(true);
            Assert(string.IsNullOrEmpty(player.CropGeometry),"Fit restores the uncropped picture");
            store.State.FillVideo=true;ApplyPictureMode(true);ShowControls();
            SeekAbsolute(83.42);
            await Until(()=>pendingTarget==null&&Math.Abs(player.Time/1000d-83.42)<2.5,"Player controls seek to 1:23.42");
            player.SetPause(true);await Task.Delay(300);SavePlayback();
            var saved=store.GetProgress(episode)!.Position;
            Assert(!store.GetProgress(episode)!.Completed,"Pausing leaves the episode unfinished");
            ReturnToLibrary();
            await Task.Delay(300);UpdateLayout();
            Assert(PlayerOverlay.Visibility==Visibility.Collapsed && Video.MediaPlayer==null,"Returning to the library detaches the video and collapses the entire overlay");
            Assert(OwnedWindows.Cast<Window>().All(w=>w.Title!="LibVLCSharp.WPF" || !w.IsVisible),"The separate VLC controls window is hidden after returning to the library");
            Assert(PlayerHeader.Visibility==Visibility.Collapsed && PlayerFooter.Visibility==Visibility.Collapsed,"Neither video bar remains over the library");
            Navigate("History");UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"history.png"));
            Assert(PageTitle.Text=="Watch history" && ContentPanel.Children.Count>1,"The History tab displays the saved episode");Navigate("Home");
            var restored=new LibraryStore(store.DataDirectory);
            Assert(Math.Abs(restored.GetProgress(episode)!.Position-saved)<.01,"Saved time survives recreating the library from disk");
            Assert(store.ContinueFor(items.Where(x=>x.GroupKey==episode.GroupKey))?.Episode==1,"Home Continue Watching points to this unfinished episode");
            UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"library.png"));
            await PlayVideo(episode);
            Assert(PlayerOverlay.Visibility==Visibility.Visible && Window.GetWindow(PlayerOverlay)?.IsVisible==true,"The video overlay reopens correctly for another playback");
            await Until(()=>pendingTarget==null&&player.IsPlaying&&Math.Abs(player.Time/1000d-saved)<2.5,"Reopening the video actually resumes VLC at the stored time");
            await Until(()=>player.SpuDescription.Length>1,"Embedded subtitle menu loads");
            var track=player.SpuDescription.First(x=>x.Id>=0);
            Assert(player.SetSpu(track.Id),"Selecting a subtitle through the player succeeds");
            Assert(player.SetSpu(-1),"Turning subtitles off succeeds");
            Assert(player.AudioTrackDescription.Length>1,"Audio track menu loads");
            var intro=store.GetIntro(episode);
            Assert(intro is {Valid:true},"The actual opening chapter is discovered and remembered");
            store.State.AutoSkipIntro=true;
            introHandled=false;HideControls();SeekAbsolute(Math.Max(0,intro!.Start-20));
            await Until(()=>pendingTarget==null,"Seeking before the intro still works");
            Assert(!introHandled,"Seeking before the opening keeps automatic intro skipping available");
            HideControls();SeekAbsolute(intro.Start+1,false,false);
            await Until(()=>pendingTarget==null&&player.Time/1000d>=intro.End-.3,"Automatic intro skip seeks to the opening's end");
            Assert(!ControlsVisible,"Automatic intro skipping keeps the controls hidden");
            SeekAbsolute(intro.Start+6);
            await Until(()=>pendingTarget==null&&Math.Abs(player.Time/1000d-(intro.Start+6))<2.5,"Manual seeking into an intro is allowed without auto-skipping it again");
            var forwardStart=SeekOrigin;
            Forward_Click(this,new RoutedEventArgs());Forward_Click(this,new RoutedEventArgs());Forward_Click(this,new RoutedEventArgs());
            Assert(Math.Abs(pendingTarget!.Value-(forwardStart+30))<.1,"Rapid forward clicks accumulate while the decoder is seeking");
            await Until(()=>pendingTarget==null&&Math.Abs(player.Time/1000d-(forwardStart+30))<3,"Repeated forward clicks actually advance through the intro");
            var outro=store.GetOutro(episode);
            Assert(outro is {Valid:true},"The actual ending chapter is discovered and remembered");
            SeekAbsolute(outro!.Start+2);
            await Until(()=>pendingTarget==null&&creditsReached&&store.GetProgress(episode)!.Completed,"Reaching the ending marks the episode watched before the file ends");
            Assert(!ended && player.IsPlaying && nextTimer?.IsEnabled!=true,"Credits keep playing and do not trigger an early auto-next countdown");
            ShowControls();await Task.Delay(350);Assert(CreditsChip.Visibility==Visibility.Visible&&NextPanel.Visibility==Visibility.Collapsed,"Credits show a small next-episode chip");
            CreditsChip_Click(this,new RoutedEventArgs());Assert(NextPanel.Visibility==Visibility.Visible,"The credits chip expands on request");
            var outroForward=SeekOrigin;Forward_Click(this,new RoutedEventArgs());
            await Until(()=>pendingTarget==null&&player.Time/1000d>=outroForward+7,"Seeking forward through credits still works");
            SavePlayback();Assert(store.GetProgress(episode)!.Completed,"Saving during credits preserves watched status");
            HideControls();Assert(NextPanel.Visibility==Visibility.Collapsed,"Clicking to hide controls also hides the credits prompt");
            store.State.AutoSkipIntro=false;
            SeekAbsolute(currentDuration-1.2);
            await Until(()=>ended,"Decoder reaches the actual end of the episode");
            Assert(store.GetProgress(episode)!.Completed,"Watched status remains set at the actual end");
            await Until(()=>current?.Episode==2&&!ended&&player.IsPlaying,"Countdown advances to the next episode",18);
            Assert(store.ContinueFor(items.Where(x=>x.GroupKey==episode.GroupKey))?.Episode==2,"Continue advances to episode two rather than restarting episode one");
            store.State.AutoNext=false;
            await Until(()=>currentDuration>60 && player.Length>60000,"Next episode duration is ready before seeking to its end");
            SeekAbsolute(currentDuration-1);
            await Until(()=>ended,"The second episode reaches its end without automatic advance");
            SeekAbsolute(41.75);
            await Until(()=>pendingTarget==null && player.IsPlaying && Math.Abs(player.Time/1000d-41.75)<3,"Seeking back after the file has ended restarts playback at the selected time");
            var movie=items.First(x=>x.IsMovie);
            await PlayVideo(movie,true);
            await Until(()=>player.IsPlaying&&player.Time>1000,"Movie fixture starts through VLC",30);
            Assert(player.Length>60000,"Movie exposes its full duration");
            SeekAbsolute(37.25);
            await Until(()=>pendingTarget==null&&Math.Abs(player.Time/1000d-37.25)<2.5,"Movie seeking works",30);
            player.SetPause(true);SavePlayback();ReturnToLibrary();
            Assert(store.GetProgress(movie) is {Completed:false,Position:>30},"Movie progress is retained in Continue Watching");
            results.Add("ALL DESKTOP PLAYBACK CHECKS PASSED");
        }
        catch(Exception e) { results.Add(e.ToString());store.Log(e); }
        finally
        {
            File.WriteAllText(Path.Combine(store.DataDirectory,"desktop-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true}));
            Close();
        }
    }
    async Task ValidateReviews()
    {
        var log=new List<string>();ReviewBrowser? browser=null;
        try
        {
            await RefreshMetadata(true);
            var movie=items.First(x=>x.IsMovie);
            browser=new ReviewBrowser(movie,store.DataDirectory,false){Owner=this};browser.Show();
            var deadline=DateTime.UtcNow.AddSeconds(25);
            while(!browser.BrowserReady && DateTime.UtcNow<deadline) await Task.Delay(200);
            if(!browser.BrowserReady) throw new Exception("WebView2 did not initialize");
            log.Add("PASS: Embedded review browser initialized");
            await Task.Delay(5000);log.Add("IMDb: "+await browser.InspectPage());
            await browser.CaptureForTest(Path.Combine(store.DataDirectory,"imdb.png"));
            await browser.NavigateForTest("Letterboxd");await Task.Delay(5000);
            log.Add("Letterboxd: "+await browser.InspectPage());await browser.CaptureForTest(Path.Combine(store.DataDirectory,"letterboxd.png"));
            await browser.NavigateForTest("Rotten Tomatoes");await Task.Delay(5000);
            log.Add("Rotten Tomatoes: "+await browser.InspectPage());await browser.CaptureForTest(Path.Combine(store.DataDirectory,"rotten.png"));
            browser.Close();browser=null;
            var episode=items.First(x=>!x.IsMovie && x.Title=="Dorohedoro" && x.Season==1 && x.Episode==1);
            browser=new ReviewBrowser(episode,store.DataDirectory,false){Owner=this};browser.Show();
            deadline=DateTime.UtcNow.AddSeconds(25);while(!browser.BrowserReady&&DateTime.UtcNow<deadline) await Task.Delay(200);
            await Task.Delay(5000);log.Add("Episode: "+await browser.InspectPage());await browser.CaptureForTest(Path.Combine(store.DataDirectory,"episode-reviews.png"));
        }
        catch(Exception e) {log.Add(e.ToString());store.Log(e);}
        finally {browser?.Close();File.WriteAllText(Path.Combine(store.DataDirectory,"review-checks.json"),JsonSerializer.Serialize(log,new JsonSerializerOptions{WriteIndented=true}));}
    }
    async Task ValidateCommunity()
    {
        var log=new List<string>();ReviewBrowser? browser=null;
        try
        {
            foreach(var video in new[]{items.First(x=>x.IsMovie),items.First(x=>x.Title=="Dorohedoro"&&x.Season==1&&x.Episode==1)})
            {
                var kind=video.IsMovie?"movie":"episode";
                var sources=ReviewLinks.Sources(video);
                if(!sources.ContainsKey("Reddit") || !sources.ContainsKey("Trakt") || sources.ContainsKey("Serializd")==video.IsMovie) throw new Exception("Wrong community tabs for "+kind);
                log.Add("PASS: Relevant community tabs for "+kind);
                browser=new ReviewBrowser(video,store.DataDirectory,false){Owner=this};browser.Show();
                var deadline=DateTime.UtcNow.AddSeconds(25);while(!browser.BrowserReady&&DateTime.UtcNow<deadline) await Task.Delay(200);
                if(!browser.BrowserReady) throw new Exception("Review browser did not initialize");
                foreach(var source in new[]{"Serializd","Trakt","Reddit"}.Where(s=>sources.ContainsKey(s) && !Environment.GetCommandLineArgs().Contains("--chrome-only")))
                {
                    await browser.NavigateForTest(source);await Task.Delay(6000);
                    if(source=="Serializd")
                    {
                        deadline=DateTime.UtcNow.AddSeconds(25);bool found=false;
                        do
                        {
                            var page=JsonDocument.Parse(JsonSerializer.Deserialize<string>(await browser.InspectPage())!);
                            found=page.RootElement.GetProperty("text").GetString()!.Contains(video.Title);
                            page.Dispose();if(!found) await Task.Delay(1000);
                        } while(!found && DateTime.UtcNow<deadline);
                        if(!found) throw new Exception("Serializd's show search did not return this title");
                        log.Add("PASS: Serializd search shows the matching TV title");
                    }
                    log.Add(kind+" · "+source+": "+await browser.InspectPage());
                    await browser.CaptureForTest(Path.Combine(store.DataDirectory,kind+"-"+source.ToLowerInvariant()+".png"));
                }
                browser.Width=780;browser.UpdateLayout();
                if(!browser.TabsFitForTest) throw new Exception("A review tab is clipped at minimum width");
                log.Add("PASS: All tabs fit at minimum window width for "+kind);
                browser.CaptureChromeForTest(Path.Combine(store.DataDirectory,kind+"-tabs.png"));
                browser.Close();browser=null;
            }
            log.Add("COMMUNITY BROWSER CHECKS COMPLETED");
        }
        catch(Exception e) {log.Add(e.ToString());store.Log(e);}
        finally {browser?.Close();File.WriteAllText(Path.Combine(store.DataDirectory,"community-checks.json"),JsonSerializer.Serialize(log,new JsonSerializerOptions{WriteIndented=true}));}
    }
    async Task ValidateSyncFixtures()
    {
        var log=new List<string>();CommunitySync? sync=null;
        void Assert(bool value,string name) {if(!value) throw new Exception("FAIL: "+name);log.Add("PASS: "+name);}
        try
        {
            var fixtureStore=new LibraryStore(Path.Combine(store.DataDirectory,"sync-fixtures"));fixtureStore.State.Roots=[];
            fixtureStore.State.Catalog.Clear();fixtureStore.State.WatchSync.Clear();fixtureStore.State.Progress.Clear();
            fixtureStore.State.SyncTrakt=fixtureStore.State.SyncSerializd=true;
            var episode=new VideoItem{Id="fixture-episode",Path="fixture.mkv",Title="Fixture",Year=2024,ImdbId="tt500",Season=2,Episode=3,Duration=1800};
            var movie=new VideoItem{Id="fixture-movie",Path="fixture-movie.mkv",Title="Fixture movie",Year=2024,ImdbId="tt600",IsMovie=true,Duration=7200};
            foreach(var item in new[]{episode,movie}) {fixtureStore.State.Catalog[item.Id]=new(){Item=item,Paths=[item.Path],Progress=new(){Completed=true,UpdatedAt=DateTimeOffset.UtcNow}};CommunityQueue.Enqueue(fixtureStore.State,item,DateTimeOffset.UtcNow);}
            fixtureStore.State.SerializdSeasons[CommunityQueue.SeasonKey(episode)]="https://www.serializd.com/show/Fixture-55/season/66/2";fixtureStore.Save();
            var watched=new HashSet<string>();var writes=new Dictionary<string,int>();bool online=false;
            sync=new CommunitySync(fixtureStore,Dispatcher,()=>{});
            sync.ConfigureForTest=core=>
            {
                core.AddWebResourceRequestedFilter("*",Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
                core.WebMessageReceived+=(_,e)=>
                {
                    var key=e.TryGetWebMessageAsString();writes[key]=writes.GetValueOrDefault(key)+1;watched.Add(key);
                };
                core.WebResourceRequested+=(_,e)=>
                {
                    var uri=new Uri(e.Request.Uri);var provider=uri.Host=="app.trakt.tv"?"Trakt":"Serializd";
                    var item=uri.AbsolutePath.StartsWith("/movies/")?movie:episode;
                    var key=provider+":"+item.Id;var wasWatched=watched.Contains(key);
                    var metadata=JsonSerializer.Serialize(new{props=new{pageProps=new{showId=55,seasonNumber="2",episodeNumber="3",data=new{runtime=30,showDetails=new{name="Fixture"}}}}});
                    var label=wasWatched?$"Remove \"{item.Title}\" from your History":$"Mark \"{item.Title}\" as watched";
                    var labelHtml=System.Net.WebUtility.HtmlEncode(label);
                    var scriptKey=JsonSerializer.Serialize(key);
                    var body=provider=="Serializd"?$"<script id='__NEXT_DATA__' type='application/json'>{metadata}</script><div class='common-show-interact-button' style='text-transform:uppercase' onclick='window.chrome.webview.postMessage({scriptKey});this.lastElementChild.textContent=\"Watched\"'><span>&#xf06e;</span><span>{(wasWatched?"Watched":"Mark watched")}</span></div>":
                        "<button aria-label='Mark &quot;Entire show&quot; as watched' onclick='window.chrome.webview.postMessage(\"WRONG SHOW\")'>Mark entire show</button>"+
                        (item.IsMovie?"":"<div class='trakt-drawer'>S2 • E3 - Fixture")+
                        $"<button aria-label='{labelHtml}' onclick='document.getElementById(\"date\").hidden=false'>Watched control</button><div class='trakt-drawer'><ul class='trakt-dropdown-group' id='date' hidden><li role='button' label='Just now' onclick='window.chrome.webview.postMessage({scriptKey})'><p>Just now</p></li></ul></div>"+
                        (item.IsMovie?"":"</div><script>history.replaceState(null,'','/shows/tt500?view=episode&season=2&episode=3');</script>");
                    var response=new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<!doctype html><html><body>"+body+"</body></html>"));
                    e.Response=core.Environment.CreateWebResourceResponse(response,online?200:503,online?"OK":"Offline fixture","Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
                };
            };
            // Every request is intercepted locally. No account or network history is changed by this test.
            await sync.RunForTest();
            Assert(fixtureStore.State.WatchSync.Values.All(j=>j.SyncedAt==null)&&writes.Count==0,"Offline jobs remain pending without a website mutation");
            Assert(new LibraryStore(fixtureStore.DataDirectory).State.WatchSync.Count==3,"Pending episode/movie updates survive closing and reopening storage");
            online=true;foreach(var job in fixtureStore.State.WatchSync.Values) job.RetryAt=DateTimeOffset.MinValue;
            await sync.RunForTest();
            Assert(fixtureStore.State.WatchSync.Values.All(j=>j.SyncedAt!=null),"Both provider adapters confirm watched state after reloading");
            Assert(writes.Count==3&&writes.Values.All(n=>n==1)&&!writes.ContainsKey("WRONG SHOW"),"Exact episode and movie controls sync once; whole-show control is never clicked");
            foreach(var job in fixtureStore.State.WatchSync.Values) {job.SyncedAt=null;job.RetryAt=DateTimeOffset.MinValue;}
            await sync.RunForTest();
            Assert(writes.Values.All(n=>n==1),"Already-watched website items are recognized without adding plays or unwatching");
            Navigate("Settings");UpdateLayout();CaptureLibrary(Path.Combine(store.DataDirectory,"sync-settings.png"));
            log.Add("SYNC ADAPTER CHECKS COMPLETED");
        }
        catch(Exception e) {log.Add(e.ToString());store.Log(e);}
        finally {sync?.Dispose();File.WriteAllText(Path.Combine(store.DataDirectory,"sync-checks.json"),JsonSerializer.Serialize(log,new JsonSerializerOptions{WriteIndented=true}));}
    }
    void CaptureLibrary(string path)
    {
        var bitmap=new RenderTargetBitmap((int)LibraryRoot.ActualWidth,(int)LibraryRoot.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(LibraryRoot);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=File.Create(path);encoder.Save(output);
    }
}
#endif

