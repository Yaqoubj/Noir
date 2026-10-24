package app.noir.mobile;
import java.time.*;
import java.time.temporal.TemporalAdjusters;
import java.util.*;

final class PhoneRewards {
 static final int[] MILESTONES={10,25,50,100,250,500,1000,2500,5000,10000};
 static final class Event {final LocalDate day;final boolean exact;Event(LocalDate d,boolean e){day=d;exact=e;}}
 static final class Stats {int total,week,streak;}
 static LocalDate weekStart(LocalDate day){return day.with(TemporalAdjusters.previousOrSame(DayOfWeek.MONDAY));}
 static Stats calculate(List<Event> events,LocalDate today){Stats s=new Stats();Set<LocalDate> days=new HashSet<>();LocalDate start=weekStart(today);for(Event e:events){s.total++;if(!e.exact||e.day==null||e.day.isAfter(today))continue;days.add(e.day);if(!e.day.isBefore(start))s.week++;}LocalDate cursor=days.contains(today)?today:today.minusDays(1);while(days.contains(cursor)){s.streak++;cursor=cursor.minusDays(1);}return s;}
 static List<Integer> crossed(int before,int after){List<Integer> result=new ArrayList<>();for(int n:MILESTONES)if(before<n&&after>=n)result.add(n);return result;}
 static String pacing(int yearViewings,LocalDate today){int projected=(int)Math.round(Math.max(0,yearViewings)*today.lengthOfYear()/(double)today.getDayOfYear());return Loc.f("On pace for %d this year",projected);}
 static boolean goalReached(int before,int after,int goal){return goal>0&&before<goal&&after>=goal;}
}
