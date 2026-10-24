namespace Noir;

public sealed record RewardViewing(DateTime Day,bool Movie=false,bool DateUnknown=false,bool Approximate=false);
public sealed record HistoryRewardState(int Episodes,int Movies,int Week,int Streak);
public sealed record HistoryReward(string Kind,int Value=0);
public static class HistoryRewards
{
    public static DateTime WeekStart(DateTime day)=>day.Date.AddDays(-(((int)day.DayOfWeek+6)%7));
    public static (int Current,int Longest) Streaks(IEnumerable<RewardViewing> views,DateTime today)
    {
        today=today.Date;var days=views.Where(v=>!v.DateUnknown&&!v.Approximate&&v.Day.Date<=today).Select(v=>v.Day.Date).Distinct().Order().ToList();
        int longest=0,run=0;DateTime? previous=null;
        foreach(var day in days){run=previous==day.AddDays(-1)?run+1:1;longest=Math.Max(longest,run);previous=day;}
        var set=days.ToHashSet();var cursor=set.Contains(today)?today:today.AddDays(-1);var current=0;
        while(set.Contains(cursor)){current++;cursor=cursor.AddDays(-1);}
        return(current,longest);
    }
    public static HistoryRewardState Summary(IEnumerable<RewardViewing> source,DateTime today)
    {
        var views=source.ToList();var start=WeekStart(today);
        return new(views.Count(v=>!v.Movie),views.Count(v=>v.Movie),views.Count(v=>!v.DateUnknown&&!v.Approximate&&v.Day.Date>=start&&v.Day.Date<=today.Date),Streaks(views,today).Current);
    }
    public static IReadOnlyList<int> Milestones(int before,int after,bool movies=false)=>
        (movies?new[]{1,5,10,25,50,100,250,500,1000}:new[]{1,10,50,100,250,500,1000,2500,5000,10000}).Where(n=>n>before&&n<=after).ToArray();
    public static IReadOnlyList<HistoryReward> Earned(HistoryRewardState before,HistoryRewardState after,int weeklyGoal,int completedSeason=0)
    {
        var rewards=new List<HistoryReward>();
        if(weeklyGoal>0&&before.Week<weeklyGoal&&after.Week>=weeklyGoal)rewards.Add(new("Week",weeklyGoal));
        foreach(var n in Milestones(before.Episodes,after.Episodes))rewards.Add(new("Episode",n));
        foreach(var n in Milestones(before.Movies,after.Movies,true))rewards.Add(new("Movie",n));
        if(completedSeason>0)rewards.Add(new("Season",completedSeason));
        if(after.Streak>before.Streak&&after.Streak>1)rewards.Add(new("Streak",after.Streak));
        return rewards;
    }
}
