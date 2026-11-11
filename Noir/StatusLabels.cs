namespace Noir;

public readonly record struct StatusEpisode(int Season, int Episode, DateTimeOffset? Aired);

public static class StatusLabels
{
    public static string For(
        IEnumerable<StatusEpisode> episodes,
        DateTimeOffset now,
        int rank = 0,
        bool planned = false,
        bool myListFilter = false
    )
    {
        if (rank is >= 1 and <= 10)
            return "#" + rank + " this week";
        var all = episodes.Where(e => e.Season > 0 && e.Episode > 0).ToList();
        var recent = all.Where(e => e.Aired <= now && e.Aired >= now.AddDays(-14))
            .OrderByDescending(e => e.Aired)
            .ThenByDescending(e => e.Season)
            .ThenByDescending(e => e.Episode)
            .FirstOrDefault();
        if (recent.Aired != null)
        {
            if (recent.Episode > 1 && recent.Episode == all.Where(e => e.Season == recent.Season).Max(e => e.Episode))
                return "Finale";
            if (
                all.Any(e =>
                    e.Season == recent.Season && e.Episode == 1 && e.Aired <= now && e.Aired >= now.AddDays(-14)
                )
            )
                return "New season";
            return "New episode";
        }
        return planned && myListFilter ? "Still planned" : "";
    }
}
