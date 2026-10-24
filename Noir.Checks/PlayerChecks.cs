using Noir;

static class PlayerChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        check(PlaybackPrefs.Classify("Recap")==SegmentKind.Recap&&PlaybackPrefs.Classify("Previously on Severance")==SegmentKind.Recap&&PlaybackPrefs.Classify("Résumé")==SegmentKind.Recap,"Recap chapters are recognised by name");
        check(PlaybackPrefs.Classify("End Credits")==SegmentKind.Credits&&PlaybackPrefs.Classify("Outro")==SegmentKind.Credits&&PlaybackPrefs.Classify("ED")==SegmentKind.Credits,"Credits chapters are recognised by name");
        check(PlaybackPrefs.Classify("Opening")==SegmentKind.Intro&&PlaybackPrefs.Classify("Chapter 3")==SegmentKind.None&&PlaybackPrefs.Classify(null)==SegmentKind.None&&PlaybackPrefs.Classify("Recapitulation of the war")==SegmentKind.None,"Ordinary chapter names are never treated as skippable");
        var dir=Path.Combine(root,"player-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        check(PlaybackPrefs.Load(dir,true).SkipIntro==SkipMode.Auto&&PlaybackPrefs.Load(dir,false).SkipIntro==SkipMode.Manual,"Old automatic intro skip setting migrates to the new skip choice");
        var prefs=PlaybackPrefs.Load(dir,false);prefs.SkipRecap=SkipMode.Off;prefs.SkipCredits=SkipMode.Auto;prefs.StillWatchingAfter=5;prefs.DefaultSpeed=1.25;prefs.Save(dir);
        var loaded=PlaybackPrefs.Load(dir,true);
        check(loaded.SkipIntro==SkipMode.Manual&&loaded.SkipRecap==SkipMode.Off&&loaded.SkipCredits==SkipMode.Auto&&loaded.StillWatchingAfter==5&&loaded.DefaultSpeed==1.25,"Saved playback choices win over the legacy setting once a profile has them");
        File.WriteAllText(PlaybackPrefs.PathFor(dir),"{\"StillWatchingAfter\":7,\"DefaultSpeed\":9,\"SkipIntro\":\"Auto\"}");
        loaded=PlaybackPrefs.Load(dir,false);check(loaded.StillWatchingAfter==3&&loaded.DefaultSpeed==2&&loaded.SkipIntro==SkipMode.Auto,"Unsupported still-watching counts and speeds are normalised");
        File.WriteAllText(PlaybackPrefs.PathFor(dir),"not json");check(PlaybackPrefs.Load(dir,true).SkipIntro==SkipMode.Auto,"A damaged playback settings file falls back safely");
        check(PlaybackPrefs.ClampSpeed(.1)==.5&&PlaybackPrefs.ClampSpeed(3)==2&&PlaybackPrefs.ClampSpeed(1.3)==1.25&&PlaybackPrefs.ClampSpeed(double.NaN)==1,"Speed is clamped to 0.5–2× in quarter steps");
        check(PlaybackPrefs.NativeVolume(80,false)==80&&PlaybackPrefs.NativeVolume(80,true)==160&&PlaybackPrefs.NativeVolume(100,true)==200&&PlaybackPrefs.NativeVolume(140,true)==200&&PlaybackPrefs.NativeVolume(-5,true)==0,"Volume boost maps the 0–100 slider to at most 200");
        var counter=new StillWatchingCounter();
        counter.AutoAdvanced();counter.AutoAdvanced();check(!counter.ShouldAsk(3),"Still watching waits until the configured number of automatic episodes");
        counter.AutoAdvanced();check(counter.ShouldAsk(3)&&!counter.ShouldAsk(0),"Still watching asks after three automatic episodes, and never when off");
        counter.UserInput();check(!counter.ShouldAsk(2)&&counter.Count==0,"Any key, click or mouse movement resets the still-watching count");
        var store=new LibraryStore(dir);store.State.Roots=[];
        await using var host=new MobileSyncServer(store,store.MobileSnapshot,store.MergeMobile,49283,ephemeralCertificate:true);
        var token=new string('C',64);host.Config.Peers.Add(new(){Name="Player check",Hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)))});
        check((await host.PreviewFrameFor(new string('D',64),"missing",10)).Status==401,"Preview frames require a paired phone");
        check((await host.PreviewFrameFor(token,"missing",10)).Status==404,"Preview frames for unknown titles return 404");
        var video=Path.Combine(dir,"Frame Show S01E01.mkv");File.WriteAllBytes(video,[1,2,3]);var item=new VideoItem{Path=video,Title="Frame Show",Season=1,Episode=1,Duration=100,Available=true};store.Record(item,10,100);
        var id=store.Archive.First(i=>i.Title=="Frame Show").Id;var png=Path.Combine(dir,"frame.png");File.WriteAllBytes(png,[137,80,78,71]);
        check((await host.PreviewFrameFor(token,id,10)).Status==404,"Preview frames return 404 while the PC has no frame source");
        host.PreviewFrame=(_,s,_)=>Task.FromResult<string?>(s<50?png:null);
        var ok=await host.PreviewFrameFor(token,id,10);check(ok.Status==200&&ok.Path==png,"Preview frames are served from the PC preview cache");
        check((await host.PreviewFrameFor(token,id,70)).Status==404&&(await host.PreviewFrameFor(token,id,-1)).Status==404&&(await host.PreviewFrameFor(token,id,double.NaN)).Status==404,"Unavailable or invalid preview positions return 404");
    }
}
