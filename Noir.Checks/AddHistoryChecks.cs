using Noir;
using System.Net;
using System.Text;

// Adding history: logged history stays out of "current watching", pasted lists parse, and media
// servers' watched lists are read correctly (from canned responses; nothing on a server changes).
static class AddHistoryChecks
{
    sealed class Canned(Func<HttpRequestMessage,string> reply):HttpMessageHandler
    {
        public readonly List<string> Paths=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {Paths.Add(request.RequestUri!.PathAndQuery);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(reply(request),Encoding.UTF8,"application/json")});}
    }
    public static void Run(string root,Action<bool,string> check)
    {
        var s=new LibraryStore(Path.Combine(root,"add-"+Guid.NewGuid().ToString("N")));s.State.Roots=[];var now=DateTimeOffset.UtcNow;
        VideoItem Movie(string id)=>new(){Id=id,CollectionKey="movie:"+id,Title=id,IsMovie=true};
        s.LogWatched([new(Movie("old"),now.AddYears(-3))],"Logged");
        s.LogWatched([new(Movie("weekend"),now.AddDays(-3))],"Logged");
        s.LogWatched([new(Movie("guess"),now.AddYears(-2),Approximate:true)],"Logged");
        s.LogWatched([new(Movie("netflix-old"),now.AddYears(-1)),new(Movie("netflix-new"),now.AddHours(-20))],"Imported from Netflix",queueSync:false);
        VideoItem Get(string id)=>s.Archive.First(i=>i.Id==id);
        check(s.LastActivity(Get("old"))==DateTimeOffset.MinValue,"A title logged with a date years ago isn't current watching");
        check(s.LastActivity(Get("weekend"))>DateTimeOffset.MinValue,"Something watched at the weekend and logged midweek still counts as recent");
        check(s.LastActivity(Get("netflix-old"))==DateTimeOffset.MinValue&&s.LastActivity(Get("netflix-new"))>DateTimeOffset.MinValue,"Imports are judged by their dates: last night counts, last year doesn't");
        check(s.ViewingHistory(Get("guess"))[0].Approximate,"Approximate dates are remembered as approximate");
        var export=Path.Combine(root,"approx-"+Guid.NewGuid().ToString("N")+".json");HistoryExport.Write(s,export,"noir-json");
        check(HistoryFormats.Read(export).Rows.Any(r=>r.Title=="guess"&&r.Approximate)&&!HistoryFormats.Read(export).Rows.Single(r=>r.Title=="old").Approximate,"Noir JSON keeps approximate dates approximate");
        var reopened=new LibraryStore(s.DataDirectory);
        check(reopened.ViewingHistory(reopened.Archive.First(i=>i.Id=="old"))[0].LoggedAt!=null&&reopened.LastActivity(reopened.Archive.First(i=>i.Id=="old"))==DateTimeOffset.MinValue,"The logged-history rule survives a restart");
        // Library = files, started downloads and explicit adds; history and My List alone aren't in it.
        check(!s.InLibrary(Get("old").GroupKey)&&!s.LibraryGroups().Contains(Get("old").GroupKey),"Logged or imported history doesn't put a title in the library");
        s.SetInLibrary(Get("old").GroupKey,true);check(s.InLibrary(Get("old").GroupKey),"“+ Library” adds a title without a file");
        var at=DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeMilliseconds();
        var removeOp=new MobileOperation{Id=Guid.NewGuid().ToString(),ItemId=Get("old").Id,Kind="library",At=at,Value=false};
        check(s.MergeMobile([removeOp]).Count==1&&!s.InLibrary(Get("old").GroupKey)&&s.ViewingHistory(Get("old")).Count==1,"Removing from the library on the phone hides it on the PC and keeps its history");
        check(!s.MobileSnapshot().Items.First(i=>i.Id=="old").InLibrary,"The phone is told it's no longer in the library");
        var stale=new MobileOperation{Id=Guid.NewGuid().ToString(),ItemId=Get("old").Id,Kind="library",At=at-60000,Value=true};s.MergeMobile([stale]);
        check(!s.InLibrary(Get("old").GroupKey),"An older add from another device can't undo a newer remove");
        // Pasted lists
        var list=HistoryList.Parse("Breaking Bad\n- Inception (2010)\nThe Office S4\nLost s2e10\nmovie: Dune 2021\nshow: Severance\n\n");
        check(list.Count==6,"Each non-empty pasted line becomes one entry");
        check(list[0].Name=="Breaking Bad"&&list[0].Season==0&&list[0].Movie==null,"A bare title is a whole movie or show");
        check(list[1].Name=="Inception"&&list[1].Year==2010,"A year in brackets picks the right title");
        check(list[2].Name=="The Office"&&list[2].Season==4&&list[2].Episode==0&&list[2].Movie==false,"“S4” means watched up to the end of season 4");
        check(list[3].Name=="Lost"&&list[3].Season==2&&list[3].Episode==10,"“S2E10” means watched up to that episode");
        check(list[4].Name=="Dune"&&list[4].Year==2021&&list[4].Movie==true&&list[5].Movie==false,"“movie:” and “show:” prefixes set the type");
        // Jellyfin
        var jelly=new Canned(_=>"""{"Items":[{"Type":"Movie","Name":"Arrival","ProductionYear":2016,"ProviderIds":{"Imdb":"tt2543164"},"UserData":{"LastPlayedDate":"2021-05-02T20:00:00Z"}},{"Type":"Episode","SeriesName":"Dark","ParentIndexNumber":1,"IndexNumber":3,"Name":"Past and Present","UserData":{"LastPlayedDate":"0001-01-01T00:00:00Z"}}]}""");
        var server=new MediaServerAccount{Kind="Jellyfin",Name="Home",BaseUrl="https://jelly.example",Token="t",UserId="u1",DeviceId="d"};
        var rows=new MediaServers(new HttpClient(jelly)).Watched(server,CancellationToken.None).GetAwaiter().GetResult();
        check(rows.Count==2&&rows[0].ImdbId=="tt2543164"&&rows[0].WatchedAt?.Year==2021&&rows[1].Title=="Dark"&&rows[1].Season==1&&rows[1].Episode==3&&rows[1].WatchedAt==null,"Jellyfin's played movies and episodes import with their real dates, and none when unknown");
        check(jelly.Paths.All(p=>p.Contains("IsPlayed=true")),"Jellyfin is only asked for played items");
        // Plex
        var plex=new Canned(r=>r.RequestUri!.AbsolutePath=="/library/sections"?"""{"MediaContainer":{"Directory":[{"key":"1","type":"movie"},{"key":"2","type":"show"},{"key":"3","type":"artist"}]}}"""
            :r.RequestUri.Query.Contains("type=1")?"""{"MediaContainer":{"Metadata":[{"title":"Heat","year":1995,"viewCount":2,"lastViewedAt":1600000000,"Guid":[{"id":"imdb://tt0113277"}]},{"title":"Unseen","year":2020,"viewCount":0}]}}"""
            :"""{"MediaContainer":{"Metadata":[{"grandparentTitle":"Severance","parentIndex":1,"index":2,"title":"Half Loop","viewCount":1,"lastViewedAt":1650000000}]}}""");
        var plexRows=new MediaServers(new HttpClient(plex)).Watched(new(){Kind="Plex",BaseUrl="https://plex.example",Token="t",DeviceId="d"},CancellationToken.None).GetAwaiter().GetResult();
        check(plexRows.Count==2&&plexRows[0].ImdbId=="tt0113277"&&plexRows[1].Title=="Severance"&&plexRows[1].Episode==2&&plex.Paths.All(p=>!p.Contains("sections/3")),"Plex imports only played movies and episodes, skipping music libraries");
        // Kodi
        var kodi=new Canned(r=>r.Content!.ReadAsStringAsync().Result.Contains("GetMovies")?"""{"result":{"movies":[{"title":"Alien","year":1979,"imdbnumber":"tt0078748","lastplayed":"2019-03-04 21:10:00"}]}}""":"""{"result":{"episodes":[{"showtitle":"Fargo","season":2,"episode":1,"title":"Waiting for Dutch","lastplayed":""}]}}""");
        var kodiRows=new MediaServers(new HttpClient(kodi)).KodiWatched("http://192.168.1.20:8080","kodi","pw",CancellationToken.None).GetAwaiter().GetResult();
        check(kodiRows.Count==2&&kodiRows[0].ImdbId=="tt0078748"&&kodiRows[0].WatchedAt?.Year==2019&&kodiRows[1].Title=="Fargo"&&kodiRows[1].WatchedAt==null,"Kodi's watched movies and episodes import through its JSON-RPC");
    }
}
