using Noir;
public static class BrowsingChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        var now=new DateTimeOffset(2026,10,24,12,0,0,TimeSpan.Zero);StatusEpisode E(int season,int episode,int days)=>new(season,episode,now.AddDays(days));
        check(StatusLabels.For([E(2,1,-2),E(2,2,5)],now)=="New season","Browsing labels identify a newly aired season without using future episodes");
        check(StatusLabels.For([E(1,1,-40),E(1,2,-2),E(1,3,5)],now)=="New episode","Browsing labels identify a new mid-season episode");
        check(StatusLabels.For([E(1,1,-40),E(1,2,-2)],now)=="Finale","Browsing labels identify a season finale aired in the last fourteen days");
        check(StatusLabels.For([E(1,2,-14)],now)=="Finale"&&StatusLabels.For([new(1,2,now.AddDays(-14).AddTicks(-1))],now)=="","Browsing recency has an inclusive fourteen-day boundary");
        check(StatusLabels.For([E(0,1,-1),E(1,1,1),new(1,2,null)],now)=="","Browsing labels exclude specials, unknown dates and future episodes");
        check(StatusLabels.For([],now,3)=="#3 this week"&&StatusLabels.For([],now,11)=="","Browsing ranks use only the existing Top 10 range");
        check(StatusLabels.For([],now,planned:true)==""&&StatusLabels.For([],now,planned:true,myListFilter:true)=="Still planned","Still planned is restricted to the My List filter");
        var path=Path.Combine(root,"browsing-"+Guid.NewGuid().ToString("N"));var layout=new HomeLayout{Rows=[new(){Id="services"},new(){Id="continue",Visible=false},new(){Id="list",Visible=false},new(){Id="genre:Drama",Pinned=true},new(){Id="removed"},new(){Id="services"}]};layout.Save(path);var loaded=HomeLayout.Load(path).Resolve(["genre:Drama","collection:Weekend"]);
        check(loaded[0].Id=="continue"&&loaded[0].Visible&&loaded.Count(r=>r.Id=="continue")==1,"Continue Watching is always first and visible after saving and reopening");
        check(loaded[1].Id=="services"&&!loaded.Single(r=>r.Id=="list").Visible&&loaded.Single(r=>r.Id=="genre:Drama").Pinned,"Home order, hidden rows and pinned genres survive persistence");
        check(loaded.All(r=>r.Id!="removed")&&loaded.Select(r=>r.Id).Distinct().Count()==loaded.Count,"Home ignores unknown rows and duplicate identifiers safely");
        check(!loaded.Single(r=>r.Id=="collection:Weekend").Visible&&loaded.Count(r=>r.Id=="top10")==1,"New extra rows start hidden while new standard rows remain available");
        check(HomeLayout.Load(Path.Combine(path,"other-profile")).Rows.Count==0,"Home layouts remain separate between profile folders");
        File.WriteAllText(Path.Combine(path,"home-layout.json"),"{broken");check(HomeLayout.Load(path).Resolve([])[0].Id=="continue","An unreadable Home layout falls back to a safe default");
    }
}
