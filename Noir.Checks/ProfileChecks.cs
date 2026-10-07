using Noir;
using System.Text.Json;
static class ProfileChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        var desktop=new PlayerState();var codex=new PlayerState();var oldAt=DateTimeOffset.UtcNow.AddDays(-3);var newAt=DateTimeOffset.UtcNow;
        CatalogEntry Entry(string id,string key,int season,int episode,double position,bool complete,DateTimeOffset at)=>new(){Item=new(){Id=id,CollectionKey=key,Title="Example",Year=2020,Season=season,Episode=episode,Path=$"episode-{season}-{episode}.mkv"},Paths=[$"episode-{season}-{episode}.mkv"],Progress=new(){Position=position,Duration=1000,Completed=complete,UpdatedAt=at}};
        desktop.Catalog["old"]=Entry("old","show:EXAMPLE:2020",1,1,21,false,oldAt);
        desktop.Catalog["newSeason"]=Entry("newSeason","show:EXAMPLE:0",2,1,0,false,oldAt);
        codex.Catalog["watched"]=Entry("watched","show:EXAMPLE:2020",1,1,1000,true,newAt.AddMinutes(-5));
        codex.Catalog["latest"]=Entry("latest","show:EXAMPLE:2020",2,1,456.789,false,newAt);
        codex.Viewings["watched"]=[new(){Id="watch",WatchedAt=newAt.AddMinutes(-5)}];
        ProfileLocation.Merge(desktop,codex);
        var directory=Path.Combine(root,"profile-merge-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"library.json"),JsonSerializer.Serialize(desktop));
        var recovered=new LibraryStore(directory);recovered.Save();
        check(recovered.Archive.Select(i=>i.GroupKey).Distinct().Count()==1,"Desktop and Codex profiles reconcile split seasons into one show");
        check(recovered.Archive.Count==2&&recovered.Archive.Count(recovered.EverWatched)==1,"Duplicate episode identities merge without losing watched history");
        var next=recovered.ContinueFor(recovered.Archive);check(next?.Season==2&&recovered.GetProgress(next)!.Position==456.789,"Merged Continue uses the newest exact position instead of the older 21-second bookmark");
        var reopened=new LibraryStore(directory);next=reopened.ContinueFor(reopened.Archive);check(next?.Season==2&&reopened.GetProgress(next)!.Position==456.789&&reopened.Archive.Count(reopened.EverWatched)==1,"Merged history and continuation survive another close and reopen");
        check(recovered.State.CatalogAliases.ContainsKey("latest")&&recovered.State.CatalogAliases.ContainsKey("watched"),"Former phone episode identities remain mapped to recovered episodes");
        check(!ProfileLocation.Shared.Contains("AppData",StringComparison.OrdinalIgnoreCase),"Shared profile stays outside Windows package AppData virtualization");
    }
}
