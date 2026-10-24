package app.noir.mobile;
import org.junit.Test;
import java.time.*;
import java.util.*;
import static org.junit.Assert.*;

public final class PhoneRewardsTest {
 PhoneRewards.Event exact(LocalDate d){return new PhoneRewards.Event(d,true);}
 @Test public void streakCountsDaysAndExcludesApproximateUnknownAndFuture(){LocalDate today=LocalDate.of(2026,10,24);PhoneRewards.Stats s=PhoneRewards.calculate(List.of(exact(today),exact(today),exact(today.minusDays(1)),new PhoneRewards.Event(today.minusDays(2),false),new PhoneRewards.Event(null,false),exact(today.plusDays(1))),today);assertEquals(6,s.total);assertEquals(2,s.streak);assertEquals(3,s.week);}
 @Test public void streakSurvivesUntilTheEndOfToday(){LocalDate today=LocalDate.of(2026,10,24);assertEquals(3,PhoneRewards.calculate(List.of(exact(today.minusDays(1)),exact(today.minusDays(2)),exact(today.minusDays(3))),today).streak);assertEquals(0,PhoneRewards.calculate(List.of(exact(today.minusDays(2))),today).streak);}
 @Test public void weekStartsMondayAcrossYearBoundary(){LocalDate monday=LocalDate.of(2024,12,30);assertEquals(monday,PhoneRewards.weekStart(LocalDate.of(2025,1,5)));assertEquals(LocalDate.of(2025,1,6),PhoneRewards.weekStart(LocalDate.of(2025,1,6)));assertEquals(1,PhoneRewards.calculate(List.of(exact(monday.minusDays(1)),exact(monday)),monday).week);}
 @Test public void leapDayAndGoalCrossings(){LocalDate day=LocalDate.of(2024,3,1);assertEquals(3,PhoneRewards.calculate(List.of(exact(day),exact(day.minusDays(1)),exact(day.minusDays(2))),day).streak);assertTrue(PhoneRewards.goalReached(4,6,5));assertFalse(PhoneRewards.goalReached(5,6,5));assertFalse(PhoneRewards.goalReached(0,1,0));assertEquals(List.of(10,25,50),PhoneRewards.crossed(9,50));assertTrue(PhoneRewards.crossed(50,49).isEmpty());}
 @Test public void sharedTitlesAndProviderLinks(){assertEquals("tt0133093",ShareToLogActivity.parse("https://www.imdb.com/title/tt0133093/?ref_=share",null).imdb);assertEquals("The Matrix",ShareToLogActivity.parse("The Matrix",null).title);assertEquals("breaking bad",ShareToLogActivity.parse("https://trakt.tv/shows/breaking-bad/seasons/1",null).title);assertEquals("the matrix",ShareToLogActivity.parse("https://letterboxd.com/film/the-matrix/",null).title);assertEquals("the matrix",ShareToLogActivity.parse("https://www.themoviedb.org/movie/603-the-matrix?language=en",null).title);assertEquals("81234567",ShareToLogActivity.parse("https://www.netflix.com/watch/81234567",null).providerId);assertEquals("Dark",ShareToLogActivity.parse("https://www.netflix.com/title/81234567","Dark | Netflix").title);assertFalse(ShareToLogActivity.supported("netflix.com.evil.example"));assertTrue(ShareToLogActivity.parse("https://example.org/film/fake",null).url.isEmpty());}
}
