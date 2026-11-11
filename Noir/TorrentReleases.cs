using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using MonoTorrent;

namespace Noir;

public sealed record TorrentAlternative(
    string Hash,
    int FileIndex,
    string Name,
    long Size,
    List<string>? Trackers = null
);

public sealed class TorrentReleases : IDownloadProvider
{
    readonly TorboxProvider index;
    readonly Func<DownloadCredentials> config;
    readonly Func<TorrentRuntime>? runtime;
    static readonly ConcurrentDictionary<string, List<string>> trackers = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string, List<TorboxFile>> fileLists = new(StringComparer.OrdinalIgnoreCase);

    public TorrentReleases(
        Func<DownloadCredentials> config,
        HttpClient? http = null,
        Func<TorrentRuntime>? runtime = null
    )
    {
        this.config = config;
        this.runtime = runtime;
        index = new(config, http);
    }

    public static int Seeders(JsonElement stream)
    {
        static int Count(JsonElement e)
        {
            if (
                e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("seeders", out var n)
                && int.TryParse(n.ToString(), out var count)
                && count >= 0
            )
                return count;
            return -1;
        }
        var text = Text(stream, "description") + " " + Text(stream, "title");
        var marker = Regex.Match(text, @"\[NOIR_SEEDERS:(\d+)\]");
        if (marker.Success && int.TryParse(marker.Groups[1].Value, out var marked))
            return marked;
        if (stream.TryGetProperty("streamData", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("torrent", out var torrent) && Count(torrent) >= 0)
                return Count(torrent);
            if (Count(data) >= 0)
                return Count(data);
        }
        if (Count(stream) >= 0)
            return Count(stream);
        if (stream.TryGetProperty("torrent", out var nested) && Count(nested) >= 0)
            return Count(nested);
        var people = Regex.Match(text, @"(?:👤|👥)\s*([\d,]+)");
        return people.Success && int.TryParse(people.Groups[1].Value.Replace(",", ""), out var count) ? count : -1;
    }

    static string Text(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    public static string Health(int seeders) =>
        seeders < 0 ? "Unknown"
        : seeders == 0 ? "Dead"
        : seeders < 10 ? "Low"
        : "Good";

    public static int FileIndex(JsonElement stream) =>
        stream.TryGetProperty("fileIdx", out var n) && int.TryParse(n.ToString(), out var i) && i >= 0 ? i : -1;

    internal static void Remember(string hash, JsonElement stream)
    {
        if (stream.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
        {
            var list = sources
                .EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString() ?? "")
                .Where(v => v.StartsWith("tracker:"))
                .Select(v => v[8..])
                .Where(ValidTracker)
                .Distinct()
                .Take(30)
                .ToList();
            trackers.AddOrUpdate(hash, list, (_, old) => old.Concat(list).Distinct().Take(30).ToList());
        }
    }

    // Used when neither the source nor the torrent named any tracker.
    internal static readonly string[] DefaultTrackers =
    [
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://open.stealth.si:80/announce",
        "udp://tracker.torrent.eu.org:451/announce",
        "udp://exodus.desync.com:6969/announce",
        "udp://open.demonii.com:1337/announce",
    ];

    internal static bool ValidTracker(string v) =>
        Uri.TryCreate(v, UriKind.Absolute, out var u) && u.Scheme is "udp" or "https" or "http";

    internal static List<string> TrackersFor(string hash) =>
        trackers.TryGetValue(hash, out var list) ? list.ToList() : [];

    // Trackers stored on a job re-enter the table, so a restart (empty table) still reaches them.
    internal static void AddTrackers(string hash, IEnumerable<string>? stored)
    {
        var list = (stored ?? []).Where(ValidTracker).Distinct().Take(30).ToList();
        if (list.Count > 0)
            trackers.AddOrUpdate(hash, list, (_, old) => old.Concat(list).Distinct().Take(30).ToList());
    }

    internal static MagnetLink Magnet(string hash, bool network = true) =>
        MagnetLink.Parse(
            "magnet:?xt=urn:btih:"
                + hash
                + string.Concat(
                    trackers
                        .GetValueOrDefault(hash, [])
                        .Where(t => network || new Uri(t).IsLoopback)
                        .Select(t => "&tr=" + Uri.EscapeDataString(t))
                )
        );

    internal static void RememberFiles(string hash, Torrent torrent) =>
        fileLists[hash] = torrent.Files.Select((f, i) => new TorboxFile(i, f.Path, f.Length)).ToList();

    internal static int Index(string hash, string name, long size) =>
        fileLists.TryGetValue(hash, out var files)
            ? (int)(
                files
                    .FirstOrDefault(f =>
                        f.Name.Replace('\\', '/').Equals(name.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)
                        && (size <= 0 || size == f.Size)
                    )
                    ?.Id
                ?? -1
            )
            : -1;

    public static List<IndexedRelease> Rank(IEnumerable<IndexedRelease> releases, DownloadCredentials c) =>
        releases
            .Where(r => r.Choice.Seeders != 0)
            .GroupBy(r =>
                r.Choice.Health == "Good" ? 0
                : r.Choice.Health == "Unknown" ? 1
                : 2
            )
            .OrderBy(g => g.Key)
            .SelectMany(g =>
                ReleasePresentation
                    .Recommend(g.Select(r => r.Choice), c.PreferredResolution, c.MaxEpisodeBytes, int.MaxValue)
                    .Select(choice => g.First(r => r.Choice == choice))
            )
            .ToList();

    public async Task<List<IndexedRelease>> Search(VideoItem item, CancellationToken ct) =>
        Rank(await index.Search(item, ct), config());

    public async Task<List<IndexedRelease>> SearchSeason(VideoItem item, CancellationToken ct) =>
        Rank(await index.SearchSeason(item, ct), config());

    public async Task<ProviderSearchResult> Discover(
        VideoItem item,
        bool season,
        List<VideoItem> probes,
        IProgress<DownloadSourceStatus>? progress,
        CancellationToken ct
    )
    {
        var result = await index.Discover(item, season, probes, progress, ct);
        result.Releases = Rank(result.Releases, config());
        return result;
    }

    public async Task<List<TorboxFile>> Files(string hash, CancellationToken ct)
    {
        if (runtime == null)
            throw new IOException("Torrent downloads are unavailable. Restart Noir and try again.");
        var torrent = await runtime().Metadata(hash, TimeSpan.FromSeconds(90), ct);
        RememberFiles(hash, torrent);
        return fileLists[hash].ToList();
    }

    public Task<TorboxTorrent> Prepare(string hash, long torrentId, CancellationToken ct) =>
        throw new IOException("Choose Torrents in PC Settings to download this release.");

    public Task<string> Link(long torrentId, long fileId, CancellationToken ct) =>
        throw new IOException("This release downloads through the PC torrent engine.");
}
