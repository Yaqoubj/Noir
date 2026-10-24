using Noir;
using System.Text;
using System.Text.Json;

static class LetterboxdChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        for(int n=1;n<=10;n++){var review=new PersonalReview{Rating10=n};var round=JsonSerializer.Deserialize<PersonalReview>(JsonSerializer.Serialize(review))!;check(review.Rating==(n+1)/2&&round.Rating10==n&&round.Rating==(n+1)/2,"Letterboxd half-star "+n+" keeps legacy ceiling and round-trips");}
        var old=JsonSerializer.Deserialize<PersonalReview>("{\"Rating\":4,\"Note\":\"Old review\",\"FutureField\":true}")!;check(old.Rating10==8&&old.Note=="Old review","Legacy five-star review upgrades without losing its note");
        var reordered=JsonSerializer.Deserialize<PersonalReview>("{\"Rating10\":7,\"Rating\":4}")!;check(reordered.Rating10==7&&reordered.Rating==4,"Review compatibility is independent of JSON field order");
        var s=new LibraryStore(Path.Combine(root,"P4-letterboxd-"+Guid.NewGuid().ToString("N")));s.State.Roots=[];var episode=new VideoItem{Id="p4-episode",CollectionKey="show:p4",Title="A story",Season=1,Episode=2,Duration=1800};s.State.Catalog[episode.Id]=new(){Item=episode};s.Save();var at=DateTimeOffset.UtcNow.AddSeconds(2).ToUnixTimeMilliseconds();
        MobileOperation Op(string kind,long delta=0)=>new(){Id=Guid.NewGuid().ToString(),ItemId=episode.Id,Kind=kind,At=at+delta};
        var favorites=Op("favorites");favorites.Favorites=[episode.GroupKey,"show:second","movie:third","show:fourth","show:fifth",episode.GroupKey];
        var like=Op("like");like.Value=true;var season=Op("seasonRating");season.Season=1;season.Rating10=9;var reaction=Op("reaction");reaction.Reaction="wow";var rate=Op("review");rate.Rating=4;rate.Rating10=7;rate.Note="Half a star matters";
        check(s.MergeMobile([favorites,like,season,reaction,rate]).Count==5,"Favorites, like, seasonRating, reaction and half-star review operations merge");
        check(s.State.Favorites.SequenceEqual(new[]{"show:p4","show:second","movie:third","show:fourth"}),"Favorites preserve order, remove duplicates and cap at four");
        check(s.State.Likes.Contains(episode.GroupKey)&&s.State.SeasonRatings[episode.GroupKey+":1"]==9&&s.State.Reactions[episode.Id]=="wow"&&s.State.PersonalReviews[episode.Id] is {Rating10:7,Rating:4},"New diary fields are independent and retain half-star precision");
        check(s.State.WatchSync.Count==0,"Personal history fields never queue external provider updates");
        var itemLike=Op("like");itemLike.Group=episode.Id;itemLike.Value=true;s.MergeMobile([itemLike]);check(s.State.Likes.Contains(episode.Id),"Explicit item-ID likes remain independent of group likes");s.State.Likes.Remove(episode.Id);s.Save();
        var unlike=Op("like",100);unlike.Value=false;var unseason=Op("seasonRating",100);unseason.Season=1;unseason.Rating10=0;var unreaction=Op("reaction",100);unreaction.Reaction="";var unfavorites=Op("favorites",100);unfavorites.Favorites=[];
        s.MergeMobile([unlike,unseason,unreaction,unfavorites]);like.Id=Guid.NewGuid().ToString();season.Id=Guid.NewGuid().ToString();reaction.Id=Guid.NewGuid().ToString();favorites.Id=Guid.NewGuid().ToString();s.MergeMobile([like,season,reaction,favorites]);
        check(s.State.Likes.Count==0&&s.State.SeasonRatings.Count==0&&s.State.Reactions.Count==0&&s.State.Favorites.Count==0,"Field-clock tombstones prevent stale devices resurrecting removed diary fields");
        var snap=s.MobileSnapshot();check(snap.FieldTimes.ContainsKey("like:"+episode.GroupKey)&&snap.FieldTimes.ContainsKey("reaction:"+episode.Id)&&snap.Items.Single().Review is {Rating10:7,Rating:4},"Snapshot contains precision and removal clocks without changing schema one");
        var legacyReview=Op("review",200);legacyReview.Rating=3;legacyReview.Note="Old phone";s.MergeMobile([legacyReview]);check(s.State.PersonalReviews[episode.Id] is {Rating:3,Rating10:6,Note:"Old phone"},"An old phone review remains accepted after half-star reviews");
        var unknown=Op("futureMood",300);var before=JsonSerializer.Serialize(s.State);check(s.MergeMobile([unknown]).Count==0&&JsonSerializer.Serialize(s.State)==before,"Unknown future operation kinds are ignored without modifying history");
        var invalid=Op("reaction",400);invalid.Reaction="invalid";var badRating=Op("seasonRating",400);badRating.Season=1;badRating.Rating10=11;check(s.MergeMobile([invalid,badRating]).Count==0,"Unknown reactions and invalid half-star ratings are rejected");
        s.SetFavorites([episode.GroupKey,"movie:local"]);s.SetLike(episode.Id,true);s.SetSeasonRating(episode.GroupKey,2,7);s.SetReaction(episode.Id,"loved");s.SetReview10(episode,9,"PC note");var reopened=new LibraryStore(s.DataDirectory);
        check(reopened.State.Favorites.Count==2&&reopened.State.Likes.Contains(episode.Id)&&reopened.State.SeasonRatings[episode.GroupKey+":2"]==7&&reopened.State.Reactions[episode.Id]=="loved"&&reopened.State.PersonalReviews[episode.Id] is {Rating10:9,Rating:5},"PC diary edits survive reopening with matching legacy ratings");
        var clock=s.State.MobileFieldTimes["like:"+episode.Id];s.State.Likes.Remove(episode.Id);s.Save();check(s.State.MobileFieldTimes["like:"+episode.Id]>clock,"Direct legacy PC state edits get a new independent field clock");
        var night=Enumerable.Range(0,5).Select(n=>new DateTime(2026,10,19).AddDays(n).AddHours(23));var weekend=new[]{new DateTime(2026,10,17,14,0,0),new DateTime(2026,10,18,15,0,0)};
        check(YearStoryLogic.IdentityLabel(night)=="Night-owl binger"&&YearStoryLogic.IdentityLabel(weekend)=="Weekend cinephile"&&YearStoryLogic.IdentityLabel(Array.Empty<DateTime>())=="A new chapter","Story identity uses night, weekend and empty-history rules");
        check(YearStoryLogic.IdentityLabel(new[]{new DateTime(2026,10,19,14,0,0)},4,1)=="Cinema explorer"&&YearStoryLogic.IdentityLabel(new[]{new DateTime(2026,10,19,14,0,0)},1,4)=="Series storyteller","Story identity distinguishes movie and series rhythms");
        check(YearStoryLogic.PacingText(10,new DateTime(2026,12,31))=="On pace for 10 this year"&&YearStoryLogic.PacingText(3,DateTime.Today,true)=="3 this week"&&YearStoryLogic.PacingText(3,DateTime.Today,false,true)=="Goal paused","Goal pacing remains neutral and supports pausing");
        var cal=new CalendarEpisode("show:p4",1,2,new DateTime(2026,11,4),"Show, with; punctuation","Title\\new\nline · عنوان");var ics=CalendarExport.Build(new[]{cal,cal},DateTimeOffset.Parse("2026-10-24T12:00:00Z"));var unfolded=ics.Replace("\r\n ","");
        check(ics.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n")&&ics.EndsWith("END:VCALENDAR\r\n")&&ics.Split("BEGIN:VEVENT").Length==2&&unfolded.Contains("DTSTART;VALUE=DATE:20261104")&&unfolded.Contains("DTEND;VALUE=DATE:20261105"),"Calendar exports one all-day VEVENT per stable episode with CRLF");
        check(unfolded.Contains("SUMMARY:Show\\, with\\; punctuation S1E2 · Title\\\\new\\nline · عنوان")&&CalendarExport.Escape("a\r\nb\rc,d;e\\f")=="a\\nb\\nc\\,d\\;e\\\\f","Calendar text escapes backslashes, commas, semicolons and line breaks");
        var longLine="SUMMARY:"+string.Concat(Enumerable.Repeat("عربي · 🎬 ",25));var folded=CalendarExport.Fold(longLine);check(folded.Split("\r\n").All(l=>Encoding.UTF8.GetByteCount(l)<=75)&&folded.Replace("\r\n ","")==longLine,"RFC5545 folding uses UTF-8 octets and never splits a Unicode rune");
        check(CalendarExport.Uid("show:p4",1,2)==CalendarExport.Uid("show:p4",1,2)&&CalendarExport.Uid("show:p4",1,2)!=CalendarExport.Uid("show:p4",1,3)&&unfolded.Contains("UID:"+CalendarExport.Uid("show:p4",1,2)),"Calendar UID stays stable across title/date edits and distinguishes episodes");
        var oldProjection=JsonSerializer.Deserialize<OldSnapshot>(JsonSerializer.Serialize(s.MobileSnapshot()))!;check(oldProjection.Schema==1&&oldProjection.Items.Single().Review!.Rating==5,"Old snapshot readers ignore new kinds and fields and retain rounded review ratings");
    }
    sealed class OldSnapshot{public int Schema{get;set;}public List<OldItem> Items{get;set;}=[];}
    sealed class OldItem{public OldReview? Review{get;set;}}
    sealed class OldReview{public int Rating{get;set;}public string Note{get;set;}="";}
}
