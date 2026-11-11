using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public sealed class DownloadCredentials
{
    public string Service { get; set; } = "TorBox";
    public string TorBoxKey { get; set; } = "";
    public string RealDebridKey { get; set; } = "";
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public List<MediaServerAccount> Servers { get; set; } = [];
    public string Manifest { get; set; } = "";
    public string Key { get; set; } = "";
    public string ShowsRoot { get; set; } = "";
    public string MoviesRoot { get; set; } = "";
    public string PreferredResolution { get; set; } = "1080p";
    public long MaxEpisodeBytes { get; set; }
    public List<string> SubtitleLanguages { get; set; } = ["en", "ar"];
    public bool AutoSyncSubtitles { get; set; } = true;
    public string SubtitleAddonUrl { get; set; } = "";
    public bool SubtitleAddonOnly { get; set; } = true;
    string subtitleSource = "";
    public string SubtitleSource
    {
        get => subtitleSource.Length == 0 && SubtitleAddonUrl.Length > 0 ? "addon" : subtitleSource;
        set => subtitleSource = value ?? "";
    }
    public string SubtitleApiKey { get; set; } = "";
    public string SubtitleUsername { get; set; } = "";
    public string SubtitlePassword { get; set; } = "";
    public string SubtitleAddonAuth { get; set; } = "";
    public Dictionary<string, string> SubtitleSiteKeys { get; set; } = [];

    // F8: a preferred uploader name; an ordering preference, not a secret, so it is kept apart from the site keys.
    public string SubtitleUploader { get; set; } = "";
    public string BrowseQuality { get; set; } = "All";
    public string BrowseLanguage { get; set; } = "All";
    public bool BrowseReadyOnly { get; set; }
    public bool BrowseSizeLimit { get; set; }
    public string Seeding { get; set; } = "ratio";
    public int UploadLimitKB { get; set; } = 1024;
    public int DownloadLimitKB { get; set; }
    public bool AutoSwitch { get; set; } = true;
    public int MaxActiveTorrents { get; set; } = 3;
    public string OriginalManifest { get; set; } = "";
}

public sealed record IndexedRelease(
    DownloadChoice Choice,
    string Hash,
    [property: System.Text.Json.Serialization.JsonIgnore] List<DirectDownloadSource>? Direct = null
)
{
    public string Service { get; set; } = "";
}

public sealed record TorboxFile(long Id, string Name, long Size);

public sealed record TorboxTorrent(long Id, string Hash, bool Ready, List<TorboxFile> Files);

public interface IDownloadProvider
{
    Task<List<IndexedRelease>> Search(VideoItem item, CancellationToken ct);
    Task<List<IndexedRelease>> SearchSeason(VideoItem item, CancellationToken ct) => Search(item, ct);
    async Task<ProviderSearchResult> Discover(
        VideoItem item,
        bool season,
        List<VideoItem> probes,
        IProgress<DownloadSourceStatus>? progress,
        CancellationToken ct
    )
    {
        var found = season ? await SearchSeason(item, ct) : await Search(item, ct);
        return new()
        {
            Releases = found,
            Sources =
            [
                new()
                {
                    Source = "Provider",
                    State = found.Count > 0 ? "Ready" : "Empty",
                    Count = found.Count,
                },
            ],
        };
    }
    Task<List<TorboxFile>> Files(string hash, CancellationToken ct);
    Task<TorboxTorrent> Prepare(string hash, long torrentId, CancellationToken ct);
    Task<string> Link(long torrentId, long fileId, CancellationToken ct);
}

public sealed class TorboxProvider(Func<DownloadCredentials> config, HttpClient? client = null) : IDownloadProvider
{
    readonly HttpClient http = client ?? DownloadSecurity.Client(TimeSpan.FromSeconds(45));
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, TorboxTorrent> owned = new(
        StringComparer.OrdinalIgnoreCase
    );

    public static Uri Manifest(string text)
    {
        text = text.Trim();
        if (text.StartsWith("stremio://", StringComparison.OrdinalIgnoreCase))
            text = "https://" + text[10..];
        if (
            !Uri.TryCreate(text, UriKind.Absolute, out var u)
            || u.Scheme != "https"
            || u.UserInfo.Length > 0
            || !u.AbsolutePath.EndsWith("/manifest.json")
        )
            throw new IOException("Paste the full HTTPS AIOStreams installation URL ending in /manifest.json.");
        return u;
    }

    public static string Hash(JsonElement stream)
    {
        var candidates = new List<string> { Text(stream, "infoHash"), Text(stream, "magnet"), Text(stream, "url") };
        var marker = Regex.Match(
            Text(stream, "description") + " " + Text(stream, "title"),
            @"\[NOIR_HASH:([a-fA-F0-9]{40})\]"
        );
        if (marker.Success)
            candidates.Add(marker.Groups[1].Value);
        if (stream.ValueKind != JsonValueKind.Object)
            return "";
        if (
            stream.TryGetProperty("streamData", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("torrent", out var torrent)
        )
            candidates.Add(Text(torrent, "infoHash"));
        if (stream.TryGetProperty("torrent", out var nested))
            candidates.Add(Text(nested, "infoHash"));
        if (stream.TryGetProperty("behaviorHints", out var hints))
            candidates.Add(Text(hints, "bingeGroup"));
        foreach (var value in candidates)
        {
            if (Regex.IsMatch(value, @"\A[a-fA-F0-9]{40}\z"))
                return value.ToLowerInvariant();
            var m = Regex.Match(value, @"(?i)(?:btih:|torbox\||torrentio\||infohash[=:])([a-f0-9]{40})(?![a-f0-9])");
            if (m.Success)
                return m.Groups[1].Value.ToLowerInvariant();
        }
        // Comet/StremThru links can identify a torrent immediately before its numeric file index.
        if (Uri.TryCreate(Text(stream, "url"), UriKind.Absolute, out var releaseUri))
        {
            var match = Regex.Match(releaseUri.AbsolutePath, @"(?i)/([a-f0-9]{40})/(?:-?\d+)(?:/|$)");
            if (match.Success)
                return match.Groups[1].Value.ToLowerInvariant();
        }
        // TorBox's Stremio endpoint encodes the torrent hash in a base64 JSON path segment.
        if (
            Uri.TryCreate(Text(stream, "url"), UriKind.Absolute, out var uri)
            && (
                uri.Host.Equals("torbox.app", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".torbox.app", StringComparison.OrdinalIgnoreCase)
            )
        )
            foreach (var part in uri.AbsolutePath.Split('/'))
                try
                {
                    var encoded = Uri.UnescapeDataString(part).Replace('-', '+').Replace('_', '/');
                    encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
                    using var payload = JsonDocument.Parse(Convert.FromBase64String(encoded));
                    if (
                        payload.RootElement.ValueKind == JsonValueKind.Array
                        && payload.RootElement.GetArrayLength() > 0
                    )
                    {
                        var hash = payload.RootElement[0].GetString() ?? "";
                        if (Regex.IsMatch(hash, @"\A[a-fA-F0-9]{40}\z"))
                            return hash.ToLowerInvariant();
                    }
                }
                catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException) { }
        return "";
    }

    public static Uri StreamUri(string manifestUrl, VideoItem item)
    {
        var manifest = Manifest(manifestUrl);
        var id = item.IsMovie ? item.ImdbId : $"{item.ImdbId}:{item.Season}:{item.Episode}";
        return new UriBuilder(manifest)
        {
            Path =
                manifest.AbsolutePath[..^"manifest.json".Length]
                + "stream/"
                + (item.IsMovie ? "movie/" : "series/")
                + Uri.EscapeDataString(id)
                + ".json",
        }.Uri;
    }

    public async Task<List<IndexedRelease>> Search(VideoItem item, CancellationToken ct) =>
        Require(await Discover(item, false, [item], null, ct));

    public async Task<List<IndexedRelease>> SearchSeason(VideoItem item, CancellationToken ct) =>
        Require(await Discover(item, true, [item], null, ct));

    static List<IndexedRelease> Require(ProviderSearchResult result)
    {
        if (result.Releases.Count > 0)
            return result.Releases;
        throw new IOException(
            string.Join(
                " · ",
                result.Sources.Select(s => s.Source + ": " + (s.Message.Length > 0 ? s.Message : s.State))
            )
        );
    }

    public Task<ProviderSearchResult> Discover(
        VideoItem item,
        bool season,
        List<VideoItem> probes,
        IProgress<DownloadSourceStatus>? progress,
        CancellationToken ct
    ) => Discover(item, season, probes, progress, ct, true);

    internal async Task<ProviderSearchResult> Discover(
        VideoItem item,
        bool season,
        List<VideoItem> probes,
        IProgress<DownloadSourceStatus>? progress,
        CancellationToken ct,
        bool includeIndex
    )
    {
        string indexWarning = "";
        async Task<(List<IndexedRelease> Releases, DownloadSourceStatus Status)> Run(
            string source,
            Func<Task<List<IndexedRelease>>> fetch
        )
        {
            progress?.Report(new() { Source = source });
            List<IndexedRelease> found = [];
            var status = new DownloadSourceStatus { Source = source };
            try
            {
                found = await fetch();
                status.State = found.Count > 0 ? "Ready" : "Empty";
                status.Count = found.Count;
                status.Message = found.Count == 0 ? "No matching releases" : $"{found.Count} matching releases";
                var warnings = found.Select(r => r.Choice.Warning).Where(w => w.Length > 0).Distinct().ToList();
                if (warnings.Count > 0)
                {
                    status.State = "Partial";
                    status.Message += " · " + string.Join(" · ", warnings.Take(2));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                status.State = "Failed";
                status.Message = "Source timed out. Retry.";
            }
            catch (Exception e) when (e is IOException or JsonException or HttpRequestException)
            {
                status.State = "Failed";
                status.Message =
                    e is IOException ? SafeText(e.Message) : "Couldn't read this source's response. Retry.";
            }
            progress?.Report(status);
            return (found, status);
        }
        async Task<List<IndexedRelease>> Index()
        {
            var found = new List<IndexedRelease>();
            var errors = new List<string>();
            if (season && config().Service == "AIOStreams links")
                return await BestSeason(probes, ct);
            foreach (var probe in probes.DistinctBy(p => (p.Season, p.Episode)).Take(season ? 3 : 1))
            {
                try
                {
                    found.AddRange(await IndexSearch(probe, ct));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                    when (e is IOException or HttpRequestException or JsonException or OperationCanceledException)
                {
                    errors.Add(
                        e is IOException ? e.Message
                        : e is OperationCanceledException ? "Source timed out. Retry."
                        : "Couldn't read this source's response. Retry."
                    );
                    if (
                        found.Count == 0
                        && errors.Count == 1
                        && e.Message.Contains("has been removed", StringComparison.OrdinalIgnoreCase)
                    )
                        throw;
                }
            }
            if (errors.Count > 0)
                indexWarning = string.Join(" · ", errors.Distinct().Take(2));
            if (found.Count == 0 && errors.Count > 0)
                throw new IOException(errors[0]);
            return found;
        }
        // Both requests run even when the account already contains a matching pack.
        var libraryTask =
            config().Service == "TorBox"
                ? Run("TorBox library", () => AccountReleases(item, season, ct))
                : Task.FromResult(
                    (
                        Releases: new List<IndexedRelease>(),
                        Status: new DownloadSourceStatus { Source = "TorBox library", State = "Not selected" }
                    )
                );
        var indexTask =
            includeIndex && config().Manifest.Length > 0
                ? Run("AIOStreams", Index)
                : Task.FromResult(
                    (
                        Releases: new List<IndexedRelease>(),
                        Status: new DownloadSourceStatus
                        {
                            Source = "AIOStreams",
                            State = "Not configured",
                            Message = "Add a manifest link in PC Settings to find new releases",
                        }
                    )
                );
        await Task.WhenAll(indexTask, libraryTask);
        var indexed = await indexTask;
        var library = await libraryTask;
        if (indexWarning.Length > 0 && indexed.Status.State == "Ready")
        {
            indexed.Status.State = "Partial";
            indexed.Status.Message = indexWarning;
            progress?.Report(indexed.Status);
        }
        var merged = new List<IndexedRelease>();
        foreach (
            var group in indexed
                .Releases.Concat(library.Releases)
                .GroupBy(r => r.Direct != null ? r.Choice.Id : r.Hash, StringComparer.OrdinalIgnoreCase)
        )
        {
            var release = group.First();
            release.Choice.Sources = group.SelectMany(r => r.Choice.Sources).Distinct().ToList();
            release.Choice.SourceEntries = group.Sum(r => Math.Max(1, r.Choice.SourceEntries));
            var ownedRelease = group.FirstOrDefault(r => r.Choice.Sources.Contains("TorBox library"));
            if (ownedRelease != null)
            {
                release.Choice.Cached = ownedRelease.Choice.Cached;
                release.Choice.CacheStatus = ownedRelease.Choice.CacheStatus;
                release.Choice.Size = ownedRelease.Choice.Size;
                release.Choice.SizeKind = "Pack";
            }
            merged.Add(release);
        }
        return new()
        {
            Releases = merged,
            Sources = includeIndex
                ? config().Service == "TorBox"
                    ? [indexed.Status, library.Status]
                    : [indexed.Status]
                : config().Service == "TorBox"
                    ? [library.Status]
                    : [],
        };
    }

    public static bool AccountMatch(VideoItem item, JsonElement torrent, bool season)
    {
        static string Words(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", " ").Trim();
        var title = Words(item.Title);
        var name = Words(Text(torrent, "name"));
        if (
            title.Length == 0
            || !new[] { title, Words(item.ReviewTitle) }
                .Where(t => t.Length > 0)
                .Any(t => (" " + name + " ").Contains(" " + t + " ", StringComparison.Ordinal))
        )
            return false;
        var years = Regex
            .Matches(Text(torrent, "name"), @"(?<!\d)(?:19|20)\d{2}(?!\d)")
            .Select(m => int.Parse(m.Value))
            .ToList();
        if (item.Year > 0 && years.Count > 0 && !years.Contains(item.Year))
            return false;
        var videos = ReadFiles(torrent)
            .Where(f =>
                DownloadNaming.Video(f.Name) && !Regex.IsMatch(f.Name, @"(?i)(?:^|[/\\ ._-])sample(?:[/\\ ._-]|$)")
            )
            .ToList();
        if (item.IsMovie)
        {
            if (videos.Count != 1 || DownloadNaming.Episode(videos[0].Name, 0).Episode > 0)
                return false;
            return true;
        }
        var numbers = videos.Select(f => DownloadNaming.Episode(f.Name, item.Season)).ToList();
        return season
            ? numbers.Where(n => n.Season == item.Season && n.Episode > 0).Distinct().Count() > 1
            : numbers.Contains((item.Season, item.Episode));
    }

    // A season plan asks for each episode in parallel; one account listing per half minute serves them all.
    (DateTime At, Task<JsonElement> List)? accountList;
    readonly object accountGate = new();

    Task<JsonElement> AccountList(CancellationToken ct)
    {
        lock (accountGate)
        {
            if (
                accountList is { } known
                && DateTime.UtcNow - known.At < TimeSpan.FromSeconds(30)
                && !known.List.IsFaulted
                && !known.List.IsCanceled
            )
                return known.List;
            var fresh = Api("torrents/mylist", null, ct);
            accountList = (DateTime.UtcNow, fresh);
            return fresh;
        }
    }

    async Task<List<IndexedRelease>> AccountReleases(VideoItem item, bool season, CancellationToken ct)
    {
        var data = await AccountList(ct);
        var result = new List<IndexedRelease>();
        if (data.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var t in data.EnumerateArray())
        {
            var hash = Text(t, "hash");
            var id = Number(t, "id");
            if (!Regex.IsMatch(hash, @"\A[a-fA-F0-9]{40}\z") || id < 0 || !AccountMatch(item, t, season))
                continue;
            hash = hash.ToLowerInvariant();
            var files = ReadFiles(t);
            var ready =
                t.TryGetProperty("download_present", out var present) && present.ValueKind == JsonValueKind.True;
            owned[hash] = new(id, hash, ready, files);
            var name = SafeText(Text(t, "name"));
            var size = files.Where(f => DownloadNaming.Video(f.Name)).Sum(f => f.Size);
            result.Add(
                new(
                    new()
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = name,
                        Detail = season
                            ? $"Season {item.Season} candidate · episode coverage will be verified"
                            : "Choose this episode from its file list",
                        Cached = ready,
                        CacheStatus = ready ? "Ready" : "Not cached",
                        Size = size,
                        SizeKind = "Pack",
                        Sources = ["TorBox library"],
                        Resolution = Resolution(name),
                        ReferenceSeason = item.Season,
                        ReferenceEpisode = item.Episode,
                    },
                    hash
                )
            );
        }
        return result;
    }

    string SafeText(string value) => DownloadSecurity.SafeText(value, config());

    public async Task<List<IndexedRelease>> IndexSearch(VideoItem item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config().Manifest))
            throw new IOException(
                "Add an AIOStreams installation URL to find new releases. No matching files were found in your TorBox library."
            );
        if (!Regex.IsMatch(item.ImdbId, @"^tt\d+$"))
            throw new IOException("Correct this title's IMDb match before finding releases.");
        var url = StreamUri(config().Manifest, item);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // AIOStreams documents this client prefix for returning parsed stream metadata.
        request.Headers.UserAgent.ParseAdd("AIO-Noir/0.11.0");
        HttpResponseMessage received;
        try
        {
            received = await http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException("AIOStreams search timed out. Try again shortly.");
        }
        catch (HttpRequestException)
        {
            throw new IOException("Couldn't connect to AIOStreams.");
        }
        using var response = received;
        if (!response.IsSuccessStatusCode)
            throw new IOException(
                $"AIOStreams couldn't find releases ({(int)response.StatusCode}). Check its installation URL."
            );
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var found = new List<IndexedRelease>();
        if (!doc.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
            return found;
        var errors = new List<string>();
        var playable = 0;
        var converted = 0;
        foreach (var stream in streams.EnumerateArray())
        {
            if (stream.ValueKind != JsonValueKind.Object)
                continue;
            if (stream.TryGetProperty("streamData", out var metadata) && Text(metadata, "type") == "error")
            {
                if (metadata.TryGetProperty("error", out var error))
                    errors.Add(SafeText(Text(error, "description")));
                continue;
            }
            if (
                stream.TryGetProperty("externalUrl", out _)
                && !stream.TryGetProperty("url", out _)
                && !stream.TryGetProperty("infoHash", out _)
            )
                continue;
            playable++;
            var hash = config().Service == "AIOStreams links" ? "" : Hash(stream);
            DirectDownloadSource? direct = null;
            if (config().Service == "Torrents" && (hash.Length == 0 || TorrentReleases.Seeders(stream) == 0))
                continue;
            if (hash.Length == 0)
            {
                direct = ReadDirect(stream, item);
                if (direct == null)
                    continue;
            }
            converted++;
            var sourceMatch = Regex.Match(Text(stream, "description"), @"\[NOIR_SOURCE:([^\]\r\n]{1,100})\]");
            var source =
                sourceMatch.Success ? SafeText(sourceMatch.Groups[1].Value)
                : direct == null ? "AIOStreams"
                : SafeText(Text(stream, "name"));
            if (source.Length == 0)
                source = "AIOStreams";
            if (direct != null)
                source += " (AIOStreams links)";
            if (config().Service == "Torrents")
                TorrentReleases.Remember(hash, stream);
            if (direct == null && found.FirstOrDefault(x => x.Direct == null && x.Hash == hash) is { } duplicate)
            {
                if (config().Service == "Torrents")
                {
                    duplicate.Choice.Seeders = Math.Max(duplicate.Choice.Seeders, TorrentReleases.Seeders(stream));
                    duplicate.Choice.Health = TorrentReleases.Health(duplicate.Choice.Seeders);
                }
                duplicate.Choice.SourceEntries++;
                if (!duplicate.Choice.Sources.Contains(source))
                    duplicate.Choice.Sources.Add(source);
                continue;
            }
            var name = Text(stream, "name");
            var detail = Text(stream, "description");
            if (detail.Length == 0)
                detail = Text(stream, "title");
            // URLs and any provider account configuration never enter the phone response.
            name = Regex.Replace(name, @"https?://\S+", "[link]");
            detail = Regex.Replace(detail, @"https?://\S+", "[link]");
            long size = 0;
            if (
                stream.TryGetProperty("behaviorHints", out var hints)
                && hints.TryGetProperty("videoSize", out var bytes)
                && bytes.ValueKind == JsonValueKind.Number
            )
                bytes.TryGetInt64(out size);
            if (
                stream.TryGetProperty("behaviorHints", out var fileHints)
                && Text(fileHints, "filename") is { Length: > 0 } filename
            )
                name = filename;
            else if (Text(fileHints, "name") is { Length: > 0 } hintName)
                name = hintName;
            var audio = Regex.Match(detail, @"\[NOIR_AUDIO:([^\]\r\n]{0,200})\]");
            var language =
                audio.Success && audio.Groups[1].Value.Trim().Length > 0 ? SafeText(audio.Groups[1].Value) : "Unknown";
            name = SafeText(name);
            detail = SafeText(Regex.Replace(detail, @"\[NOIR_(?:HASH|SOURCE|AUDIO|SEEDERS):[^\]\r\n]*\]", "").Trim());
            if (direct != null)
            {
                var secrets = DownloadSecurity.SourceSecrets(direct).ToList();
                name = DownloadSecurity.SafeText(name, config(), secrets);
                detail = DownloadSecurity.SafeText(detail, config(), secrets);
                source = DownloadSecurity.SafeText(source, config(), secrets);
                language = DownloadSecurity.SafeText(language, config(), secrets);
                direct.Name = DownloadSecurity.SafeText(direct.Name, config(), secrets);
                direct.Resolution = Resolution(name + " " + detail);
                direct.Language = language;
            }
            var torrentSeeders = config().Service == "Torrents" ? TorrentReleases.Seeders(stream) : -1;
            if (config().Service == "Torrents")
                TorrentReleases.Remember(hash, stream);
            found.Add(
                new(
                    new()
                    {
                        Seeders = torrentSeeders,
                        Health = TorrentReleases.Health(torrentSeeders),
                        FileIndex = TorrentReleases.FileIndex(stream),
                        Id = Guid.NewGuid().ToString("N"),
                        Name = name.Length > 0 ? name[..Math.Min(256, name.Length)] : "Release",
                        Detail = detail,
                        Languages = language,
                        Size = size,
                        SizeKind = item.IsMovie ? "Movie" : "Episode",
                        Sources = [source],
                        Resolution = Resolution(name + " " + detail),
                        ReferenceSeason = item.Season,
                        ReferenceEpisode = item.Episode,
                        Cached = direct != null,
                        CacheStatus = direct != null ? "Ready" : "Unknown",
                    },
                    hash,
                    direct == null ? null : [direct]
                )
            );
        }
        if (found.Count == 0)
            throw new IOException(
                config().Service == "Torrents"
                    ? "No one is sharing this right now. Try another release."
                    : DiscoveryMessage(playable, errors)
            );
        if (errors.Count > 0 || playable > converted)
        {
            found[0].Choice.Warning =
                (errors.Count > 0 ? SafeText(errors[0]) + " · " : "")
                + (playable > converted ? "Some streams did not include a usable download source" : "");
        }
        try
        {
            if (config().Service == "Torrents")
            {
                foreach (var release in found)
                {
                    release.Service = "Torrents";
                    release.Choice.Cached = false;
                    release.Choice.CacheStatus = release.Choice.Health;
                }
                return TorrentReleases.Rank(found, config());
            }
            if (config().Service != "TorBox")
                return found;
            var torrents = found.Where(x => x.Direct == null).Take(100).ToList();
            if (torrents.Count == 0)
                return found;
            var hashes = string.Join("&", torrents.Select(x => "hash=" + x.Hash));
            var cached = await Api("torrents/checkcached?format=list&list_files=false&" + hashes, null, ct);
            if (cached.ValueKind == JsonValueKind.Array)
            {
                var set = cached
                    .EnumerateArray()
                    .Select(x => Text(x, "hash"))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var x in torrents)
                {
                    x.Choice.Cached = set.Contains(x.Hash);
                    x.Choice.CacheStatus = x.Choice.Cached ? "Ready" : "Not cached";
                }
            }
        }
        catch (Exception e)
            when (e is IOException or HttpRequestException
                || e is OperationCanceledException && !ct.IsCancellationRequested
            ) { } // Releases remain browsable if the cache lookup fails.
        return found;
    }

    DirectDownloadSource? ReadDirect(JsonElement stream, VideoItem item)
    {
        var url = Text(stream, "url");
        if (!DownloadSecurity.ValidUrl(url))
            return null;
        var hints = stream.TryGetProperty("behaviorHints", out var h) ? h : default;
        var name = Text(hints, "filename");
        if (name.Length == 0)
            name = Text(hints, "name");
        if (name.Length == 0)
            name = Text(stream, "name");
        name = SafeText(name);
        if (!DownloadNaming.Video(name))
            name = (item.IsMovie ? "Movie" : $"S{item.Season:00}E{item.Episode:00}") + ".mkv";
        var source = new DirectDownloadSource
        {
            Url = url,
            Name = name,
            Size = Math.Max(0, Number(hints, "videoSize")),
            Season = item.Season,
            Episode = item.Episode,
        };
        if (
            hints.ValueKind == JsonValueKind.Object
            && hints.TryGetProperty("proxyHeaders", out var proxy)
            && proxy.TryGetProperty("request", out var headers)
            && headers.ValueKind == JsonValueKind.Object
        )
            foreach (var header in headers.EnumerateObject())
                if (header.Value.ValueKind == JsonValueKind.String)
                    source.Headers[header.Name] = header.Value.GetString() ?? "";
        DownloadSecurity.ValidateHeaders(source.Headers);
        return source;
    }

    async Task<List<IndexedRelease>> BestSeason(List<VideoItem> probes, CancellationToken ct)
    {
        using var slots = new SemaphoreSlim(3);
        var c = config();
        var failed = 0;
        var tasks = probes
            .DistinctBy(p => (p.Season, p.Episode))
            .Take(40)
            .Select(async item =>
            {
                await slots.WaitAsync(ct);
                try
                {
                    var found = await IndexSearch(item, ct);
                    var eligible = found
                        .Where(r =>
                            r.Direct != null
                            && (c.MaxEpisodeBytes <= 0 || r.Choice.Size > 0 && r.Choice.Size <= c.MaxEpisodeBytes)
                            && (
                                c.BrowseLanguage == "All"
                                || r.Choice.Languages.Contains(c.BrowseLanguage, StringComparison.OrdinalIgnoreCase)
                            )
                        )
                        .ToList();
                    var best = ReleasePresentation
                        .Recommend(eligible.Select(r => r.Choice), c.PreferredResolution, c.MaxEpisodeBytes, 1)
                        .FirstOrDefault();
                    return eligible.FirstOrDefault(r => r.Choice == best);
                }
                catch (Exception e)
                    when (e is IOException or HttpRequestException or JsonException
                        || e is OperationCanceledException && !ct.IsCancellationRequested
                    )
                {
                    Interlocked.Increment(ref failed);
                    return null;
                }
                finally
                {
                    slots.Release();
                }
            })
            .ToList();
        var picked = (await Task.WhenAll(tasks)).Where(r => r != null).Select(r => r!).ToList();
        if (picked.Count == 0)
            return [];
        return
        [
            new(
                new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "Best link per episode",
                    Detail = $"{picked.Count} episodes · up to 40 missing released episodes checked",
                    SizeKind = "Pack",
                    Size = picked.Sum(r => r.Choice.Size),
                    Cached = true,
                    CacheStatus = "Ready",
                    Sources = picked.SelectMany(r => r.Choice.Sources).Distinct().ToList(),
                    Warning = failed > 0 ? $"{failed} episode lookups unavailable" : "",
                },
                "",
                picked.SelectMany(r => r.Direct!).OrderBy(d => d.Episode).ToList()
            ),
        ];
    }

    public static string Resolution(string name) =>
        Regex
            .Match(name, @"(?i)(?<!\d)(2160p|1080p|720p|480p|4k)(?!\d)")
            .Value.ToLowerInvariant()
            .Replace("4k", "2160p");

    public static string DiscoveryMessage(int playable, IEnumerable<string> errors)
    {
        var notes = string.Join(" · ", errors.Where(e => e.Length > 0).Distinct().Take(2));
        return playable > 0
                ? $"{playable} releases found, but AIOStreams supplied playback links without usable download references. Connect a Noir download profile in Settings."
                    + (notes.Length > 0 ? " · Source note: " + notes : "")
            : notes.Length > 0 ? "AIOStreams: " + notes
            : "AIOStreams returned no releases. Check its indexers and filters.";
    }

    public async Task<List<TorboxFile>> Files(string hash, CancellationToken ct)
    {
        if (owned.TryGetValue(hash, out var saved) && saved.Files.Count > 0)
            return saved.Files;
        var cached = await Api("torrents/checkcached?hash=" + hash + "&format=list&list_files=true", null, ct);
        if (cached.ValueKind == JsonValueKind.Array && cached.GetArrayLength() > 0)
        {
            var files = ReadFiles(cached[0]);
            if (files.Count > 0)
                return files;
        }
        var info = await Api("torrents/torrentinfo?hash=" + hash + "&timeout=20&use_cache_lookup=true", null, ct);
        var result = ReadFiles(info);
        if (result.Count == 0)
            throw new IOException(
                "TorBox has no file list for this release yet. Try another release or retry after its metadata is available."
            );
        return result;
    }

    public async Task<TorboxTorrent> Prepare(string hash, long torrentId, CancellationToken ct)
    {
        if (torrentId < 0 && owned.TryGetValue(hash, out var saved))
            torrentId = saved.Id;
        if (torrentId < 0)
        {
            using var body = new MultipartFormDataContent();
            body.Add(new StringContent("magnet:?xt=urn:btih:" + hash), "magnet");
            body.Add(new StringContent("0"), "seed");
            body.Add(new StringContent("false"), "allow_zip");
            var created = await Api("torrents/createtorrent", body, ct);
            torrentId = Number(created, "torrent_id");
            if (torrentId < 0)
                torrentId = Number(created, "id");
            if (torrentId < 0)
                throw new IOException("TorBox didn't return a torrent ID. Check your account's download limits.");
        }
        var data = await Api("torrents/mylist?id=" + torrentId + "&bypass_cache=true", null, ct);
        if (data.ValueKind == JsonValueKind.Array)
            data = data.GetArrayLength() > 0 ? data[0] : default;
        if (data.ValueKind != JsonValueKind.Object)
            throw new IOException("TorBox is preparing this release. Resume shortly.");
        var actual = Text(data, "hash");
        if (actual.Length > 0 && !actual.Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("TorBox returned a different release.");
        var ready = data.TryGetProperty("download_present", out var present) && present.ValueKind == JsonValueKind.True;
        return new(torrentId, hash, ready, ReadFiles(data));
    }

    public async Task<string> Link(long torrentId, long fileId, CancellationToken ct)
    {
        var data = await Api(
            "torrents/requestdl?token="
                + Uri.EscapeDataString(config().Key)
                + "&torrent_id="
                + torrentId
                + "&file_id="
                + fileId
                + "&redirect=false",
            null,
            ct
        );
        var url = data.ValueKind == JsonValueKind.String ? data.GetString() ?? "" : Text(data, "url");
        if (
            !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || uri.UserInfo.Length > 0
            || uri.IsLoopback
        )
            throw new IOException("TorBox returned an invalid download link.");
        return url;
    }

    async Task<JsonElement> Api(string path, HttpContent? content, CancellationToken ct)
    {
        try
        {
            var key = config().Key;
            if (key.Length == 0)
                throw new IOException("Add your TorBox API key in PC Settings first.");
            using var request = new HttpRequestMessage(
                content == null ? HttpMethod.Get : HttpMethod.Post,
                "https://api.torbox.app/v1/api/" + path
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Content = content;
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new IOException(
                    response.StatusCode == HttpStatusCode.TooManyRequests
                        ? "TorBox is busy. Wait a minute before retrying."
                        : $"TorBox request failed ({(int)response.StatusCode}). Check your key and account limits."
                );
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
                throw new IOException(
                    "TorBox could not complete this request. Check the release and account limits in your TorBox dashboard."
                );
            if (!root.TryGetProperty("data", out var data))
                throw new IOException("TorBox returned an incomplete response.");
            return data.Clone();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException("TorBox request timed out. Try again shortly.");
        }
        catch (HttpRequestException)
        {
            throw new IOException("Couldn't connect to TorBox. Check your connection and retry.");
        }
        catch (JsonException)
        {
            throw new IOException("Couldn't read TorBox's response. Retry shortly.");
        }
    }

    public static List<TorboxFile> ReadFiles(JsonElement data)
    {
        var result = new List<TorboxFile>();
        if (
            data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("files", out var files)
            || files.ValueKind != JsonValueKind.Array
        )
            return result;
        foreach (var f in files.EnumerateArray())
        {
            var name = Text(f, "short_name");
            if (name.Length == 0)
                name = Text(f, "shortName");
            if (name.Length == 0)
                name = Text(f, "name");
            var id = Number(f, "id");
            if (id >= 0)
                result.Add(new(id, name, Math.Max(0, Number(f, "size"))));
        }
        return result;
    }

    static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    static long Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt64(out var n)
            ? n
            : -1;
}

public sealed class DownloadProviders : IDownloadProvider
{
    readonly Func<DownloadCredentials> config;
    readonly TorboxProvider torbox,
        links;
    readonly RealDebridProvider rd;
    readonly TorrentReleases torrents;
    readonly MediaServers servers;

    public DownloadProviders(
        Func<DownloadCredentials> config,
        HttpClient? http = null,
        Func<TorrentRuntime>? torrentRuntime = null
    )
    {
        this.config = config;
        torrents = new(() => For("Torrents"), http, torrentRuntime);
        torbox = new(() => For("TorBox"), http);
        links = new(() => For("AIOStreams links"), http);
        rd = new(() => For("Real-Debrid"), http);
        servers = new(http);
    }

    DownloadCredentials For(string service)
    {
        var c = config();
        c.Key =
            service == "Real-Debrid"
                ? c.RealDebridKey.Length > 0
                    ? c.RealDebridKey
                    : c.Service == service
                        ? c.Key
                        : ""
                : c.TorBoxKey.Length > 0
                    ? c.TorBoxKey
                    : c.Service == "TorBox"
                        ? c.Key
                        : "";
        c.Service = service;
        return c;
    }

    public IDownloadProvider ForService(string service) =>
        service == "Torrents" ? torrents
        : service == "Real-Debrid" ? rd
        : service == "AIOStreams links" ? links
        : torbox;

    public Task<List<IndexedRelease>> Search(VideoItem item, CancellationToken ct) => SearchCore(item, false, ct);

    public Task<List<IndexedRelease>> SearchSeason(VideoItem item, CancellationToken ct) => SearchCore(item, true, ct);

    async Task<List<IndexedRelease>> SearchCore(VideoItem item, bool season, CancellationToken ct) =>
        (await Discover(item, season, [item], null, ct)).Releases;

    public Task<ProviderSearchResult> Discover(
        VideoItem item,
        bool season,
        List<VideoItem> probes,
        IProgress<DownloadSourceStatus>? progress,
        CancellationToken ct
    ) => Discover(item, season, probes, progress, ct, false);

    internal async Task<ProviderSearchResult> Discover(
        VideoItem item,
        bool season,
        List<VideoItem> probes,
        IProgress<DownloadSourceStatus>? progress,
        CancellationToken ct,
        bool indexSearched
    )
    {
        var c = config();
        var services = new List<string> { c.Service };
        if (c.TorBoxKey.Length > 0 && !services.Contains("TorBox"))
            services.Add("TorBox");
        if (c.RealDebridKey.Length > 0 && !services.Contains("Real-Debrid"))
            services.Add("Real-Debrid");
        if (!indexSearched && c.Manifest.Length > 0 && !services.Contains("AIOStreams links"))
            services.Add("AIOStreams links");
        var tasks = services
            .Select(async service =>
            {
                var active = ForService(service);
                var result =
                    indexSearched && active is TorboxProvider aio
                        ? await aio.Discover(item, season, probes, progress, ct, false)
                        : await active.Discover(item, season, probes, progress, ct);
                foreach (var release in result.Releases)
                    release.Service = service;
                return result;
            })
            .ToList();
        tasks.AddRange(
            c.Servers.Select(async server =>
            {
                var status = new DownloadSourceStatus { Source = MediaServers.Source(server) };
                progress?.Report(status);
                var found = new List<IndexedRelease>();
                try
                {
                    found = await servers.Search(server, item, season, ct);
                    status.State = found.Count > 0 ? "Ready" : "Empty";
                    status.Count = found.Count;
                    status.Message = found.Count > 0 ? $"{found.Count} matches on your server" : "No matching files";
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    status.State = "Failed";
                    status.Message = "Couldn't read this server. Test its connection in PC Settings.";
                }
                progress?.Report(status);
                return new ProviderSearchResult { Releases = found, Sources = [status] };
            })
        );
        var all = await Task.WhenAll(tasks);
        return new()
        {
            Releases = all.SelectMany(r => r.Releases).ToList(),
            Sources = all.SelectMany(r => r.Sources).ToList(),
        };
    }

    public Task<List<TorboxFile>> Files(string hash, CancellationToken ct) =>
        ForService(config().Service).Files(hash, ct);

    public Task<TorboxTorrent> Prepare(string hash, long id, CancellationToken ct) =>
        ForService(config().Service).Prepare(hash, id, ct);

    public Task<string> Link(long id, long file, CancellationToken ct) =>
        ForService(config().Service).Link(id, file, ct);
}
