using System.IO;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Noir;

public sealed class DownloadChoice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Detail { get; set; } = "";
    public long Size { get; set; }
    public bool Cached { get; set; }
    public string CacheStatus { get; set; } = "Unknown";
    public string SizeKind { get; set; } = "Release";
    public List<string> Sources { get; set; } = [];
    public string Resolution { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public int ReferenceSeason { get; set; }
    public int ReferenceEpisode { get; set; }
    public string Warning { get; set; } = "";
    public string Languages { get; set; } = "Unknown";
    public string Codec { get; set; } = "";
    public string Picture { get; set; } = "";
    public string ReleaseKind { get; set; } = "";
    public int SourceEntries { get; set; } = 1;
    public int Seeders { get; set; } = -1;
    public string Health { get; set; } = "Unknown";
    public int FileIndex { get; set; } = -1;
}

public sealed class DownloadFileChoice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public int Season { get; set; }
    public int Episode { get; set; }
    public bool Existing { get; set; }
    public bool Selectable { get; set; }
    public bool Queued { get; set; }
    public bool Future { get; set; }
    public string Reason { get; set; } = "";
    public bool MappingRequired { get; set; }
}

public sealed class PcDownloadJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RequestId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Service { get; set; } = "TorBox";
    public string ProtectedDirectSource { get; set; } = "";
    public long TorrentId { get; set; } = -1;
    public long FileId { get; set; } = -1;
    public string RemoteName { get; set; } = "";
    public string Root { get; set; } = "";
    public string Destination { get; set; } = "";
    public long Size { get; set; }
    public long Bytes { get; set; }
    public string ETag { get; set; } = "";
    public string LastModified { get; set; } = "";
    public List<string> Trackers { get; set; } = [];

    // Set when Noir closed (or crashed) mid-transfer: only these resume by themselves, a user pause stays paused.
    public bool Interrupted { get; set; }
    public string Status { get; set; } = "Queued";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public VideoItem Target { get; set; } = new();
    public List<string> SubtitleLanguages { get; set; } = [];
    public bool AutoSyncSubtitles { get; set; }
    public DateTimeOffset? NextAttempt { get; set; }
    public string SubtitleMessage { get; set; } = "";

    // Live transfer figures: never saved, never sent to phones. EtaSeconds is -1 whenever the size or speed is unknown.
    [JsonIgnore]
    public double Speed { get; set; }

    [JsonIgnore]
    public long EtaSeconds { get; set; } = -1;

    [JsonIgnore]
    public int Peers { get; set; }

    [JsonIgnore]
    public int Seeds { get; set; }
    public string Health { get; set; } = "Unknown";
    public long Uploaded { get; set; }
    public DateTimeOffset? SeedUntil { get; set; }
    public int Switches { get; set; }
    public List<TorrentAlternative> Alternatives { get; set; } = [];
}

// Bytes per second smoothed over a short sliding window, so one fast or stalled read does not make the number jump.
public sealed class SpeedMeter
{
    readonly Queue<(DateTime At, long Bytes)> samples = new();
    readonly double window;

    public SpeedMeter(double windowSeconds = 5) => window = windowSeconds;

    public void Reset() => samples.Clear();

    public double Add(DateTime now, long bytes)
    {
        if (samples.Count > 0 && bytes < samples.Last().Bytes)
            samples.Clear();
        samples.Enqueue((now, bytes));
        while (samples.Count > 2 && (now - samples.Peek().At).TotalSeconds > window)
            samples.Dequeue();
        return Rate;
    }

    public double Rate
    {
        get
        {
            if (samples.Count < 2)
                return 0;
            var a = samples.Peek();
            var b = samples.Last();
            var seconds = (b.At - a.At).TotalSeconds;
            return seconds < 0.5 ? 0 : Math.Max(0, (b.Bytes - a.Bytes) / seconds);
        }
    }
}

public static class DownloadEta
{
    public static long Seconds(long size, long bytes, double speed) =>
        size <= 0 || speed < 1 || bytes >= size ? -1 : (long)Math.Ceiling((size - bytes) / speed);

    // "6 min left", "1 h 12 min left"; an unknown time is an empty string and a known one is never "0 s".
    public static string Left(long seconds, bool arabic)
    {
        if (seconds < 0)
            return "";
        seconds = Math.Max(1, seconds);
        string left = arabic ? "باقي " : "",
            tail = arabic ? "" : " left";
        string s = arabic ? "ث" : "s",
            m = arabic ? "د" : "min",
            h = arabic ? "س" : "h",
            d = arabic ? "ي" : "d";
        if (seconds < 60)
            return $"{left}{seconds} {s}{tail}";
        var minutes = (seconds + 59) / 60;
        if (minutes < 60)
            return $"{left}{minutes} {m}{tail}";
        if (seconds < 86400)
        {
            var hours = seconds / 3600;
            minutes = (seconds % 3600 + 59) / 60;
            if (minutes == 60)
            {
                hours++;
                minutes = 0;
            }
            return minutes == 0 ? $"{left}{hours} {h}{tail}" : $"{left}{hours} {h} {minutes} {m}{tail}";
        }
        var days = seconds / 86400;
        var rest = (seconds % 86400 + 3599) / 3600;
        if (rest == 24)
        {
            days++;
            rest = 0;
        }
        if (days >= 100)
            return arabic ? "باقي أكثر من 99 ي" : "over 99 d left";
        return rest == 0 ? $"{left}{days} {d}{tail}" : $"{left}{days} {d} {rest} {h}{tail}";
    }

    public static string Rate(double bytesPerSecond) =>
        bytesPerSecond >= 1048576
            ? (bytesPerSecond / 1048576).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB/s"
        : bytesPerSecond >= 1024
            ? (bytesPerSecond / 1024).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " KB/s"
        : "";

    // "1.2 of 3.4 GB" while the total is known, otherwise just what has arrived.
    public static string Transferred(long bytes, long size)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (size <= 0)
            return bytes > 0 ? DownloadNaming.Size(bytes) : "";
        return size >= 1073741824
            ? $"{(bytes / 1073741824d).ToString("0.0", inv)} of {(size / 1073741824d).ToString("0.0", inv)} GB"
            : $"{(bytes / 1048576d).ToString("0", inv)} of {(size / 1048576d).ToString("0", inv)} MB";
    }
}

public sealed class DownloadRequest
{
    public string RequestId { get; set; } = "";
    public string ReleaseId { get; set; } = "";
    public List<string> FileIds { get; set; } = [];
    public List<DownloadSelection> Selections { get; set; } = [];
}

public sealed class DownloadSelection
{
    public string ReleaseId { get; set; } = "";
    public List<string> FileIds { get; set; } = [];
}

public sealed class DownloadPlanRequest
{
    public List<string> ReleaseIds { get; set; } = [];
    public string Scope { get; set; } = "Missing episodes";
    public List<int> Seasons { get; set; } = [];
    public int FromEpisode { get; set; } = 1;
    public int NextCount { get; set; } = 3;
    public List<SeasonEpisode> SpecificEpisodes { get; set; } = [];
    public ShowDownloadPreference? Preferences { get; set; }
    public long MaxTotalBytes { get; set; }
    public bool OnlyMissing { get; set; } = true;
    public bool SkipWatched { get; set; }
    public bool IncludeSpecials { get; set; }
}

public sealed class DownloadMappingRequest
{
    public string FileId { get; set; } = "";
    public int Episode { get; set; }
}

public sealed class DownloadSourceStatus
{
    public string Source { get; set; } = "";
    public string State { get; set; } = "Searching";
    public string Message { get; set; } = "";
    public int Count { get; set; }
}

public sealed class ProviderSearchResult
{
    public List<IndexedRelease> Releases { get; set; } = [];
    public List<DownloadSourceStatus> Sources { get; set; } = [];
}

public sealed class DownloadSearchResult
{
    public List<DownloadChoice> Choices { get; set; } = [];
    public List<DownloadSourceStatus> Sources { get; set; } = [];
    public List<ReleaseGroup> Groups { get; set; } = [];
}

public sealed class DownloadPlanEntry
{
    public string ReleaseId { get; set; } = "";
    public string Release { get; set; } = "";
    public DownloadFileChoice File { get; set; } = new();
}

public sealed class DownloadPlan
{
    public string Id { get; set; } = "";
    public List<PlannedEpisode> Episodes { get; set; } = [];
    public List<DownloadSourceStatus> Sources { get; set; } = [];
    public ShowDownloadPreference Preferences { get; set; } = new();
    public string Folder { get; set; } = "";
    public long FreeBytes { get; set; }
    public bool Fits { get; set; }
    public List<DownloadPlanEntry> Entries { get; set; } = [];
    public List<int> MissingEpisodes { get; set; } = [];
    public int ReleasedEpisodes { get; set; }
    public int ExistingEpisodes { get; set; }
    public int QueuedEpisodes { get; set; }
    public long SelectedBytes { get; set; }
    public bool HasUnknownSizes { get; set; }
    public bool ScheduleKnown { get; set; }
    public string Message { get; set; } = "";
}

// Two switches shared by new downloads and the organizer. Defaults keep today's names.
public sealed class NamingOptions
{
    public bool EpisodeTitle { get; set; }
    public bool MovieFolder { get; set; } = true;
}

public static class DownloadNaming
{
    public static bool Video(string name) =>
        Path.GetExtension(name).ToLowerInvariant()
            is ".mkv"
                or ".mp4"
                or ".avi"
                or ".webm"
                or ".m4v"
                or ".mov"
                or ".ts";

    public static (int Season, int Episode) Episode(string name, int season)
    {
        var m = Regex.Match(name, @"(?i)(?:s(?<s>\d{1,2})[ ._-]*e(?<e>\d{1,3})|(?<s>\d{1,2})x(?<e>\d{1,3}))");
        if (m.Success)
            return (int.Parse(m.Groups["s"].Value), int.Parse(m.Groups["e"].Value));
        m = Regex.Match(Path.GetFileName(name), @"(?i)(?:^|[ ._-])(?:e|ep|episode)[ ._-]*(?<e>\d{1,3})(?:[ ._-]|$)");
        return m.Success ? (season, int.Parse(m.Groups["e"].Value)) : (season, 0);
    }

    public static string Clean(string value)
    {
        var s = new string(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray())
            .Trim()
            .TrimEnd('.');
        if (s.Length > 100)
            s = s[..100].TrimEnd();
        if (s.Length == 0)
            throw new IOException("Set a title before downloading.");
        if (Regex.IsMatch(s, @"(?i)^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$"))
            s = "_" + s;
        return s;
    }

    // The one naming function for new downloads and the organizer. Episode title and movie folder are the only choices.
    public static (string Folder, string Stem) Target(string root, VideoItem item, NamingOptions? options = null)
    {
        options ??= new();
        var title = Clean(item.Title) + (item.Year > 0 ? $" ({item.Year})" : "");
        if (item.IsMovie)
            return (options.MovieFolder ? Path.Combine(root, title) : root, title);
        var folder = Path.Combine(root, title, $"Season {item.Season:00}");
        var stem = $"{title} S{item.Season:00}E{item.Episode:00}";
        // No episode title known: the plain name, never a dangling dash.
        var name = options.EpisodeTitle ? EpisodeName(item.EpisodeTitle) : "";
        return (folder, name.Length > 0 ? stem + " - " + name : stem);
    }

    static string EpisodeName(string value)
    {
        var s = Regex
            .Replace(
                new string(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray()),
                @"\s+",
                " "
            )
            .Trim()
            .TrimEnd('.', ' ');
        if (s.Length > 80)
            s = s[..80].TrimEnd('.', ' ');
        return s;
    }

    public static string Destination(string root, VideoItem item, string remoteName, NamingOptions? options = null)
    {
        if (!Video(remoteName))
            throw new IOException("Choose a video file.");
        if (!item.IsMovie && item.Episode < 1)
            throw new IOException("This file's episode number could not be verified.");
        var (folder, stem) = Target(root, item, options);
        var path = Path.GetFullPath(Path.Combine(folder, stem + Path.GetExtension(remoteName).ToLowerInvariant()));
        Validate(root, path);
        return path;
    }

    public static void Validate(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Download location is outside the library folder.");
        for (var p = Path.GetDirectoryName(full); p != null; p = Path.GetDirectoryName(p))
        {
            if (Directory.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Choose a download folder without redirected links.");
            if (p.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
                break;
        }
    }

    public static string Size(long bytes) =>
        bytes >= 1073741824 ? $"{bytes / 1073741824d:0.00} GB"
        : bytes >= 104858 ? $"{bytes / 1048576d:0.0} MB"
        : bytes > 0 ? $"{bytes / 1024d:0.0} KB"
        : "Size unknown";
}
