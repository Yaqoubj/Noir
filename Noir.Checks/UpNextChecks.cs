using Noir;

static class UpNextChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        var dir=Path.Combine(root,"upnext-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var store=new LibraryStore(dir);store.State.Roots=[];store.State.Progress.Clear();
        VideoItem Episode(int e){var path=Path.Combine(dir,$"Show S01E{e:00}.mkv");File.WriteAllBytes(path,[]);return new(){Path=path,Title="Upnext Show",Season=1,Episode=e,Duration=100,Available=true};}
        var e1=Episode(1);var e2=Episode(2);
        store.Record(e1,100,100,true);store.Record(e2,40,100);
        var key=store.Archive.First(i=>i.Title=="Upnext Show").GroupKey;
        var now=DateTimeOffset.UtcNow;
        store.State.EpisodeSchedules[key]=[new(){Season=1,Episode=1,AirsAt=now.AddDays(-30)},new(){Season=1,Episode=2,AirsAt=now.AddDays(-23)},new(){Season=1,Episode=3,AirsAt=now.AddDays(-16)},new(){Season=1,Episode=4,AirsAt=now.AddDays(5)}];
        var up=store.UpNextFor(store.EpisodesFor(key));
        check(up is {Kind:UpNextKind.Resume}&&up.Item.Episode==2,"Up next resumes the interrupted local episode");
        store.Record(e2,100,100,true);
        up=store.UpNextFor(store.EpisodesFor(key));
        check(up is {Kind:UpNextKind.NotOnPc}&&up.Item.Episode==3,"Finishing the last local episode keeps the title with its released next episode marked not on this PC");
        var e3=Episode(3);store.Record(e3,100,100,true);
        up=store.UpNextFor(store.EpisodesFor(key));
        check(up is {Kind:UpNextKind.Upcoming}&&up.Item.Episode==4&&up.AirsAt>now,"When caught up, up next shows the announced episode and its air date");
        File.Delete(e2.Path);store.Record(e2,30,100);
        up=store.UpNextFor(store.EpisodesFor(key));
        check(up is {Kind:UpNextKind.NotOnPc}&&up.Item.Episode==2,"An unfinished episode whose file was deleted stays in up next with its saved position");
        var movie=new VideoItem{Path=Path.Combine(dir,"Film.mkv"),Title="Upnext Film",IsMovie=true,Duration=100,Available=true};File.WriteAllBytes(movie.Path,[]);
        store.Record(movie,100,100,true);
        check(store.UpNextFor(store.EpisodesFor(store.Archive.First(i=>i.Title=="Upnext Film").GroupKey))==null,"A finished movie has no up next entry");
    }
}
