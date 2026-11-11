using System.Text.RegularExpressions;

namespace Noir;

// A pasted list of titles: one per line, with an optional year, type prefix and how far you got.
public static class HistoryList
{
    // "Lost S2E10", "Inception (2010)", "show: The Office S4", "Dune 2021"
    public static List<(
        string Line,
        DiscoverTitle? Title,
        string Name,
        int Year,
        bool? Movie,
        int Season,
        int Episode
    )> Parse(string text)
    {
        var result = new List<(string, DiscoverTitle?, string, int, bool?, int, int)>();
        foreach (
            var raw in text.Split('\n')
                .Select(l => l.Trim().TrimStart('-', '*', '•', ' ').Trim())
                .Where(l => l.Length > 0)
                .Take(500)
        )
        {
            var line = raw;
            bool? movie = null;
            var kind = Regex.Match(line, @"^(movie|film|show|series|tv)\s*:\s*", RegexOptions.IgnoreCase);
            if (kind.Success)
            {
                movie = kind.Groups[1].Value.ToLowerInvariant() is "movie" or "film";
                line = line[kind.Length..];
            }
            int season = 0,
                episode = 0;
            var ep = Regex.Match(
                line,
                @"[\s,;-]*(?:up\s+to\s+)?(?:S(?:eason)?\s*(\d{1,3})(?:\s*E(?:pisode)?\s*(\d{1,4}))?)\s*$",
                RegexOptions.IgnoreCase
            );
            if (ep.Success)
            {
                season = int.Parse(ep.Groups[1].Value);
                episode = ep.Groups[2].Success ? int.Parse(ep.Groups[2].Value) : 0;
                line = line[..ep.Index];
                movie ??= false;
            }
            var year = 0;
            var y = Regex.Match(line, @"[\s,;-]*\(?((?:19|20)\d{2})\)?\s*$");
            if (y.Success && y.Index > 0)
            {
                year = int.Parse(y.Groups[1].Value);
                line = line[..y.Index];
            }
            line = line.Trim().TrimEnd(',', ';', '-').Trim();
            if (line.Length == 0)
                continue;
            result.Add((raw, null, line, year, movie, season, episode));
        }
        return result;
    }
}
