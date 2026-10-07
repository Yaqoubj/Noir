using Noir;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

static class RefreshChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var dir=Path.Combine(root,"refresh-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var cached=Path.Combine(dir,"cached.jpg");File.WriteAllBytes(cached,[1,2,3]);
        var handler=new FixtureHandler();
        using var client=new MetadataClient(dir,handler,refreshArtwork:true);
        var details=await client.Fetch(new(){Title="Hannibal S01 Season 1",Year=2013,Season=1,Episode=1,Poster=cached,Backdrop=cached});
        check(details?.Title=="Hannibal"&&handler.SearchTitle=="Hannibal","Season-pack folder names resolve to the actual show title");
        check(details!=null&&handler.ImageReads==2&&details.Poster!=cached&&File.ReadAllBytes(details.Poster).SequenceEqual(FixtureHandler.Art),"Manual refresh downloads existing posters and backdrops again");
        check(Path.GetFileNameWithoutExtension(details!.Poster)==Convert.ToHexString(SHA256.HashData(FixtureHandler.Art)),"Changed artwork gets a content-based cache name so phone caches can refresh");
        var store=new LibraryStore(dir);store.State.Roots=[];var item=new VideoItem {Title="Hannibal S01 Season 1",Season=1,Episode=1,Year=2013,Path="missing-refresh.mkv",Poster=cached,Backdrop=cached};
        store.Record(item,71.234,1800);item=store.Archive.Single();var id=item.Id;
        store.ApplyDetails(item.GroupKey,details,true);
        var loaded=new LibraryStore(dir);var updated=loaded.Archive.Single();
        check(updated.Id==id&&updated.Title=="Hannibal"&&updated.EpisodeTitle=="New episode name"&&updated.Poster==details.Poster&&loaded.GetProgress(updated)?.Position==71.234,"Refreshed title, episode information and art survive reopening without changing history identity");
        store.Record(updated,88.765,1800);
        var locked=new FileStream(Path.Combine(dir,"library.json"),FileMode.Open,FileAccess.Read,FileShare.None);
        var release=Task.Run(async()=>{await Task.Delay(100);locked.Dispose();});
        var latest=new LibraryStore(dir);await release;
        check(latest.GetProgress(updated)?.Position==88.765&&latest.LoadWarning.Length==0,"Brief file locks do not restore an older library backup on reopening");
        latest.Record(new(){Title="Hannibal",Year=2013,Season=2,Episode=1,Path="new-season.mkv"},0,1800);
        check(latest.Archive.Select(i=>i.GroupKey).Distinct().Count()==1,"A newly added season joins the existing show after its title has been corrected");
        var tvRoot=Path.Combine(dir,"TV");var folder=Path.Combine(tvRoot,"Folder show (2024)","Season 02");Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,"03.mkv"),[]);latest.State.Roots=[new(){Path=tvRoot}];
        var episode=latest.Scan(out _).Single();
        check(episode.Title=="Folder show"&&episode.Season==2&&episode.Episode==3,"Season folders correctly identify season 2 even with a numbered episode filename");
    }
    sealed class FixtureHandler:HttpMessageHandler
    {
        public static readonly byte[] Art=[4,5,6,7];
        public int ImageReads;public string SearchTitle="";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var uri=request.RequestUri!;string json;
            if(uri.Host=="noir.test") {ImageReads++;var image=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Art)};image.Content.Headers.ContentType=new("image/jpeg");return Task.FromResult(image);}
            if(uri.AbsolutePath=="/search/shows") {SearchTitle=Uri.UnescapeDataString(uri.Query[3..]);json="""[{"show":{"id":42,"name":"Hannibal","premiered":"2013-04-04","summary":"Updated show information","image":{"original":"https://noir.test/art.jpg"},"genres":["Drama"],"externals":{"imdb":"tt2243973"}}}]""";}
            else if(uri.AbsolutePath=="/shows/42/episodes") json="""[{"season":1,"number":1,"name":"New episode name","summary":"Updated episode information","image":{"original":"https://noir.test/art.jpg"}}]""";
            else throw new Exception("Unexpected metadata fixture request: "+uri.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")});
        }
    }
}
