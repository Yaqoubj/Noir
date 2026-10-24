using Noir;

static class HistoryRewardsChecks
{
    public static void Run(Action<bool,string> check)
    {
        var today=new DateTime(2026,10,19);RewardViewing V(int days,bool unknown=false,bool approximate=false)=>new(today.AddDays(days),DateUnknown:unknown,Approximate:approximate);
        check(HistoryRewards.Streaks([V(-4),V(-3),V(-1),V(0),V(0)],today)==(2,2),"History rewards: gaps split streaks and duplicate days count once");
        check(HistoryRewards.Streaks([V(-3),V(-2),V(-1)],today)==(3,3),"History rewards: yesterday keeps a current streak alive");
        check(HistoryRewards.Streaks([V(-3),V(-2)],today)==(0,2),"History rewards: a missed yesterday ends the current streak");
        check(HistoryRewards.Streaks([V(-2),V(-1,true),V(0,false,true),V(1)],today)==(0,1),"History rewards: unknown, approximate and future dates never extend streaks");
        check(HistoryRewards.WeekStart(today)==today&&HistoryRewards.WeekStart(today.AddDays(-1))==today.AddDays(-7),"History rewards: weeks start Monday across Sunday boundary");
        check(HistoryRewards.WeekStart(new(2027,1,1))==new DateTime(2026,12,28),"History rewards: week boundaries cross years");
        check(HistoryRewards.Summary([V(-1),V(0),V(0,true),V(0,false,true),V(1)],today).Week==1,"History rewards: weekly progress uses precise dates in the current week");
        check(HistoryRewards.Milestones(99,100).SequenceEqual(new[]{100})&&HistoryRewards.Milestones(100,101).Count==0,"History rewards: a milestone fires only while crossing its threshold");
        check(HistoryRewards.Milestones(9,101).SequenceEqual(new[]{10,50,100})&&HistoryRewards.Milestones(4,5,true).SequenceEqual(new[]{5}),"History rewards: batch logging crosses every applicable episode or movie threshold");
        var earned=HistoryRewards.Earned(new(99,4,4,3),new(100,5,5,4),5,2);
        check(earned.Select(r=>r.Kind).SequenceEqual(new[]{"Week","Episode","Movie","Season","Streak"}),"History rewards: weekly goals, milestones, seasons and streaks celebrate together");
        check(HistoryRewards.Earned(new(1,1,4,3),new(2,1,5,3),0).Count==0,"History rewards: a disabled goal stays quiet and the same streak does not repeat");
    }
}
