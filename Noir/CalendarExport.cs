using System.Globalization;
using System.Security.Cryptography;
using System.Text;
#if !NOIR_HEADLESS
using System.IO;
using Microsoft.Win32;
#endif

namespace Noir;

public sealed record CalendarEpisode(
    string GroupKey,
    int Season,
    int Episode,
    DateTime Date,
    string Show,
    string Title = ""
);

public static class CalendarExport
{
    public static string Escape(string value) =>
        (value ?? "")
            .Replace("\\", "\\\\")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\n", "\\n")
            .Replace(",", "\\,")
            .Replace(";", "\\;");

    public static string Uid(string groupKey, int season, int episode) =>
        Convert
            .ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        groupKey
                            + "\n"
                            + season.ToString(CultureInfo.InvariantCulture)
                            + "\n"
                            + episode.ToString(CultureInfo.InvariantCulture)
                    )
                )
            )
            .ToLowerInvariant() + "@noir.local";

    public static string Fold(string line)
    {
        var output = new StringBuilder();
        var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var count = rune.Utf8SequenceLength;
            if (bytes + count > 75)
            {
                output.Append("\r\n ");
                bytes = 1;
            }
            output.Append(rune.ToString());
            bytes += count;
        }
        return output.ToString();
    }

    public static string Build(IEnumerable<CalendarEpisode> episodes, DateTimeOffset? generated = null)
    {
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Noir//Viewing Calendar//EN",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
        };
        var stamp = (generated ?? DateTimeOffset.UtcNow).UtcDateTime.ToString(
            "yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture
        );
        foreach (
            var e in episodes
                .DistinctBy(e => (e.GroupKey, e.Season, e.Episode))
                .OrderBy(e => e.Date)
                .ThenBy(e => e.GroupKey, StringComparer.Ordinal)
                .ThenBy(e => e.Season)
                .ThenBy(e => e.Episode)
        )
        {
            var summary = $"{e.Show} S{e.Season}E{e.Episode}" + (e.Title.Length > 0 ? " · " + e.Title : "");
            lines.AddRange(
                new[]
                {
                    "BEGIN:VEVENT",
                    "UID:" + Uid(e.GroupKey, e.Season, e.Episode),
                    "DTSTAMP:" + stamp,
                    "DTSTART;VALUE=DATE:" + e.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    "DTEND;VALUE=DATE:" + e.Date.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    "SUMMARY:" + Escape(summary),
                    "TRANSP:TRANSPARENT",
                    "END:VEVENT",
                }
            );
        }
        lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines.Select(Fold)) + "\r\n";
    }
}

#if !NOIR_HEADLESS
public partial class MainWindow
{
    void ExportUpcomingCalendar()
    {
        var episodes = Airings(TrackedShows().Where(FollowsForCalendar).Where(t => t.Group.Any(TrackerFits)))
            .Where(a => a.Episode.AirsAt > DateTimeOffset.UtcNow)
            .Select(a => new CalendarEpisode(
                a.Show.Group[0].GroupKey,
                a.Episode.Season,
                a.Episode.Episode,
                a.Local.Date,
                a.Show.Group[0].Title,
                a.Episode.Title
            ))
            .ToList();
        var dialog = new SaveFileDialog
        {
            Title = T("Export calendar", "تصدير التقويم"),
            Filter = T("Calendar (*.ics)|*.ics", "تقويم (*.ics)|*.ics"),
            DefaultExt = ".ics",
            FileName = "Noir-calendar.ics",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            File.WriteAllText(dialog.FileName, CalendarExport.Build(episodes), new UTF8Encoding(false));
            Status.Text = T("Calendar exported · {0} episodes", "تم تصدير التقويم · {0} حلقة", episodes.Count);
        }
        catch (Exception e)
        {
            store.Log(e);
            Status.Text = T("Couldn't export the calendar.", "تعذّر تصدير التقويم.");
        }
    }
}
#endif
