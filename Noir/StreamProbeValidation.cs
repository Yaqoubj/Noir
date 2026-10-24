#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using LibVLCSharp.Shared;

namespace Noir;
public partial class MainWindow
{
    // --stream-probe: real AIOStreams streams for one episode (NOIR_PROBE_TITLE), ranked, then the best
    // one played for ~20 s to prove VLC starts and advances. Read-only; links never reach the report.
    async Task ProbeStream()
    {
        var report=new Dictionary<string,object>();
        try
        {
            downloads=new PcDownloads(store,start:false);var c=downloads.Config;report["manifestSet"]=StreamSources.Manifest(c).Length>0;
            var name=Environment.GetEnvironmentVariable("NOIR_PROBE_TITLE") is {Length:>0} t?t:"Bureau";
            var group=store.Archive.Where(i=>i.Title.Contains(name,StringComparison.OrdinalIgnoreCase)&&i.ImdbId.StartsWith("tt")).GroupBy(i=>i.GroupKey).First().ToList();
            var item=group.Where(i=>i.IsMovie||i.Season>0).OrderBy(i=>i.Season).ThenBy(i=>i.Episode).First();report["item"]=item.Label;
            using var http=DownloadSecurity.Client(TimeSpan.FromSeconds(40));var clock=System.Diagnostics.Stopwatch.StartNew();
            var options=StreamSources.Rank(await StreamSources.Find(c,item,http,CancellationToken.None),c.PreferredResolution,item.IsMovie);
            report["searchMs"]=clock.ElapsedMilliseconds;report["count"]=options.Count;report["ready"]=options.Count(o=>o.Cached);
            report["top"]=options.Take(6).Select(o=>new{o.Resolution,o.Size,o.Cached,o.Service,Name=o.Name.Length>80?o.Name[..80]:o.Name,Headers=o.UserAgent.Length+o.Referrer.Length>0}).ToList();
            if(options.Count==0)throw new Exception("No streams");
            WindowState=System.Windows.WindowState.Normal;Width=1280;Height=800;VolumeBar.Value=0;
            clock.Restart();streaming=options[0];await PlayVideo(item,true);
            var started=-1L;double reached=0;
            for(int i=0;i<300&&player!=null;i++){await Task.Delay(100);if(started<0&&player.State==VLCState.Playing&&player.Time>200)started=clock.ElapsedMilliseconds;if(started>=0&&clock.ElapsedMilliseconds-started>15000)break;}
            reached=(player?.Time??0)/1000d;report["startMs"]=started;report["playedSeconds"]=reached;report["state"]=player?.State.ToString()??"";report["usedCurrentStream"]=currentStream==options[0];
            report["spuTracks"]=player?.SpuCount??0;
            ReturnToLibrary();
        }
        catch(Exception e){report["failure"]=e.GetType().Name+": "+DownloadSecurity.SafeText(e.Message,downloads?.Config??new());}
        File.WriteAllText(Path.Combine(store.DataDirectory,"stream-probe.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
}
#endif
