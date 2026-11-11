using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public sealed class SubtitleProviderReference
{
    internal readonly SubtitleSelection Selection;
    internal readonly string Id;
    internal readonly VideoItem Item;
    internal readonly string Release;

    internal SubtitleProviderReference(SubtitleSelection selection, string id, VideoItem item, string release)
    {
        Selection = selection;
        Id = id;
        Item = item;
        Release = release;
    }
}

public sealed class SubtitleProviders
{
    readonly SubtitleTransport transport;
    readonly SemaphoreSlim sessionGate = new(1, 1);
    SubtitleSelection? sessionSource;
    string token = "",
        baseUrl = "https://api.opensubtitles.com/api/v1/";

    public SubtitleProviders(HttpClient? client = null)
    {
        transport = new(client);
    }

    public static string KeyPage(string source) =>
        source switch
        {
            "opensubtitles" => "https://www.opensubtitles.com/consumers",
            "subdl" => "https://subdl.com/panel/api",
            "subsource" => "https://subsource.net/api-docs",
            "wyzie" => "https://docs.wyzie.io/subs/usage/api-keys",
            _ => "",
        };

    static JsonElement Get(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var value) ? value : default;

    static string Text(JsonElement e, string key)
    {
        var v = Get(e, key);
        return v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : "";
    }

    static IEnumerable<JsonElement> Array(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : [];

    static string Joined(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array
            ? string.Join(" ", Array(e).Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()))
        : e.ValueKind == JsonValueKind.String ? e.GetString() ?? ""
        : "";

    static bool NumberMismatch(JsonElement e, string key, int expected) =>
        int.TryParse(Text(e, key), out var actual) && actual != expected;

    static string Query(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join("&", values.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));

    static Dictionary<string, string> Params(params (string Key, string Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value);

    async Task<JsonDocument> Json(
        SubtitleSelection s,
        string url,
        bool credentials = true,
        HttpMethod? method = null,
        object? body = null,
        bool empty404 = false,
        string access = "",
        CancellationToken ct = default
    )
    {
        var bytes = await transport.Send(s, url, credentials, method, body, empty404: empty404, token: access, ct: ct);
        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            throw new SubtitleSourceException(s, SubtitleFailureKind.Unavailable);
        }
    }

    async Task Login(SubtitleSelection s, CancellationToken ct)
    {
        baseUrl = "https://api.opensubtitles.com/api/v1/";
        token = "";
        sessionSource = null;
        using var doc = await Json(
            s,
            baseUrl + "login",
            method: HttpMethod.Post,
            body: new { username = s.Username, password = s.Password },
            ct: ct
        );
        var access = Text(doc.RootElement, "token");
        if (access.Length == 0 || access.Any(char.IsControl))
            throw new SubtitleSourceException(s, SubtitleFailureKind.Unavailable);
        var target = Text(doc.RootElement, "base_url");
        if (target.Length > 0)
        {
            if (!target.Contains("://"))
                target = "https://" + target;
            if (
                !SubtitleTransport.ValidUrl(target)
                || new Uri(target).Host is not ("api.opensubtitles.com" or "vip-api.opensubtitles.com")
                || new Uri(target).Query.Length > 0
            )
                throw new SubtitleSourceException(s, SubtitleFailureKind.Unavailable);
            baseUrl = new Uri(target).GetLeftPart(UriPartial.Authority) + "/api/v1/";
        }
        token = access;
        sessionSource = s;
    }

    async Task<JsonDocument> Open(SubtitleSelection s, string path, CancellationToken ct, object? body = null)
    {
        await sessionGate.WaitAsync(ct);
        try
        {
            if (sessionSource != s)
            {
                token = "";
                baseUrl = "https://api.opensubtitles.com/api/v1/";
                if (s.Username.Length > 0 && s.Password.Length > 0)
                    await Login(s, ct);
                else
                    sessionSource = s;
            }
            try
            {
                return await Json(
                    s,
                    baseUrl + path,
                    method: body == null ? HttpMethod.Get : HttpMethod.Post,
                    body: body,
                    access: token,
                    ct: ct
                );
            }
            catch (SubtitleSourceException e) when (e.Status == 401 && s.Username.Length > 0 && s.Password.Length > 0)
            {
                await Login(s, ct);
                return await Json(
                    s,
                    baseUrl + path,
                    method: body == null ? HttpMethod.Get : HttpMethod.Post,
                    body: body,
                    access: token,
                    ct: ct
                );
            }
        }
        finally
        {
            sessionGate.Release();
        }
    }

    public async Task Check(SubtitleSelection selection, CancellationToken ct)
    {
        var s = selection.Validate();
        if (!s.Chosen)
            return;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(35));
        try
        {
            if (s.Source == "addon")
            {
                var bytes = await transport.Send(
                    s,
                    s.AddonUrl + "/manifest.json",
                    true,
                    cap: 256 * 1024,
                    ct: budget.Token
                );
                try
                {
                    _ = SubtitleAddon.ManifestName(bytes);
                }
                catch (Exception e) when (e is IOException or JsonException)
                {
                    throw new SubtitleSourceException(s, SubtitleFailureKind.Unavailable);
                }
            }
            await Search(s, new VideoItem { ImdbId = "tt0111161", IsMovie = true }, "", ["en"], "", budget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SubtitleSourceException(s, SubtitleFailureKind.Unavailable);
        }
    }

    public async Task<List<SubtitleCandidate>> Search(
        SubtitleSelection selection,
        VideoItem item,
        string release,
        IEnumerable<string> languages,
        string hash,
        CancellationToken ct,
        Dictionary<string, SubtitleSourceException>? failures = null
    )
    {
        var s = selection.Validate();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(35));
        failures ??= [];
        try
        {
            var result = await SearchCore(s, item, release, languages, hash, budget.Token, failures);
            if (result.Count == 0 && failures.Count > 0)
                throw failures.Values.First();
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SubtitleSourceException(s, SubtitleFailureKind.Unavailable);
        }
    }

    async Task<List<SubtitleCandidate>> SearchCore(
        SubtitleSelection s,
        VideoItem item,
        string release,
        IEnumerable<string> languages,
        string hash,
        CancellationToken ct,
        Dictionary<string, SubtitleSourceException> failures
    )
    {
        if (!Regex.IsMatch(item.ImdbId, @"^tt\d+$"))
            return [];
        hash = Regex.IsMatch(hash, @"\A[0-9a-fA-F]{16}\z") ? hash : "";
        var langs = languages.Select(SubtitleDownloads.Language).Where(l => l.Length > 0).Distinct().Order().ToArray();
        if (langs.Length == 0)
            return [];
        var result = new List<SubtitleCandidate>();
        void Add(
            string id,
            string language,
            string name,
            string rel,
            string url,
            bool exact = false,
            string uploader = ""
        )
        {
            if (
                result.Count >= 500
                || !langs.Contains(language)
                || url.Length > 0 && (!SubtitleTransport.ValidUrl(url) || ContainsSecret(url, s))
                || !item.IsMovie
                    && SubtitleArchive.HasEpisodeName(name + " " + rel)
                    && !SubtitleArchive.EpisodeMatches(name + " " + rel, item.Season, item.Episode)
            )
                return;
            result.Add(
                new(
                    id,
                    language,
                    name,
                    rel,
                    url,
                    exact,
                    (exact ? 1000 : 0) + SubtitleDownloads.ReleaseScore(name + " " + rel, release),
                    new(s, id, item, release),
                    SubtitleSelection.CleanUploader(uploader)
                )
            );
        }
        switch (s.Source)
        {
            case "opensubtitles":
            {
                var digits = item.ImdbId[2..].TrimStart('0');
                if (digits.Length == 0)
                    digits = "0";
                var p = Params(
                    ("languages", string.Join(',', langs)),
                    (item.IsMovie ? "imdb_id" : "parent_imdb_id", digits)
                );
                if (!item.IsMovie)
                {
                    p["season_number"] = item.Season.ToString();
                    p["episode_number"] = item.Episode.ToString();
                }
                if (hash.Length > 0)
                    p["moviehash"] = hash;
                using var doc = await Open(s, "subtitles?" + Query(p), ct);
                if (Get(doc.RootElement, "data").ValueKind != JsonValueKind.Array)
                    throw new SubtitleSourceException(s, SubtitleFailureKind.Unavailable);
                foreach (var row in Array(Get(doc.RootElement, "data")).Take(500))
                {
                    var a = Get(row, "attributes");
                    var details = Get(a, "feature_details");
                    if (
                        !item.IsMovie
                        && (
                            NumberMismatch(details, "season_number", item.Season)
                            || NumberMismatch(details, "episode_number", item.Episode)
                        )
                    )
                        continue;
                    foreach (var file in Array(Get(a, "files")).Take(100))
                    {
                        var id = Text(file, "file_id");
                        if (!long.TryParse(id, out var fileId) || fileId <= 0)
                            continue;
                        Add(
                            id,
                            Text(a, "language"),
                            Text(file, "file_name"),
                            Text(a, "release"),
                            "",
                            Get(a, "moviehash_match").ValueKind == JsonValueKind.True,
                            Text(Get(a, "uploader"), "name")
                        );
                    }
                }
                break;
            }
            case "subdl":
            {
                var p = Params(
                    ("api_key", s.ApiKey),
                    ("imdb_id", item.ImdbId),
                    ("type", item.IsMovie ? "movie" : "tv"),
                    ("languages", string.Join(',', langs.Select(l => l.ToUpperInvariant()))),
                    ("subs_per_page", "30"),
                    ("releases", "1")
                );
                if (!item.IsMovie)
                {
                    p["season_number"] = item.Season.ToString();
                    p["episode_number"] = item.Episode.ToString();
                }
                if (release.Length > 0)
                    p["file_name"] = Path.GetFileName(release);
                using var doc = await Json(
                    s,
                    "https://api.subdl.com/api/v1/subtitles?" + Query(p),
                    empty404: true,
                    ct: ct
                );
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 0)
                    break;
                if (
                    Get(root, "status").ValueKind == JsonValueKind.False
                    || Get(root, "success").ValueKind == JsonValueKind.False
                )
                {
                    var error = (Text(root, "error") + " " + Text(root, "message")).ToLowerInvariant();
                    throw new SubtitleSourceException(
                        s,
                        error.Contains("key") || error.Contains("forbidden") ? SubtitleFailureKind.BadKey
                            : error.Contains("limit") || error.Contains("quota") ? SubtitleFailureKind.Limit
                            : SubtitleFailureKind.Unavailable
                    );
                }
                if (Get(root, "subtitles").ValueKind != JsonValueKind.Array)
                    throw new SubtitleSourceException(s, SubtitleFailureKind.Unavailable);
                foreach (var row in Array(Get(root, "subtitles")).Take(500))
                {
                    if (
                        !item.IsMovie
                        && (
                            NumberMismatch(row, "season", item.Season)
                            || Get(row, "full_season").ValueKind != JsonValueKind.True
                                && NumberMismatch(row, "episode", item.Episode)
                            || int.TryParse(Text(row, "episode_from"), out var from) && from > item.Episode
                            || int.TryParse(Text(row, "episode_end"), out var end) && end < item.Episode
                        )
                    )
                        continue;
                    var path = Text(row, "url");
                    if (
                        !path.StartsWith('/')
                        || path.StartsWith("//")
                        || !Uri.TryCreate("https://dl.subdl.com" + path, UriKind.Absolute, out var url)
                        || url.Host != "dl.subdl.com"
                    )
                        continue;
                    var lang = Text(row, "lang");
                    if (lang.Length == 0)
                        lang = Text(row, "language");
                    Add(
                        "",
                        lang.ToLowerInvariant() switch
                        {
                            "en" or "english" => "en",
                            "ar" or "arabic" => "ar",
                            _ => "",
                        },
                        Text(row, "name"),
                        Text(row, "release_name") + " " + Joined(Get(row, "releases")),
                        url.AbsoluteUri,
                        uploader: Text(row, "author")
                    );
                }
                break;
            }
            case "subsource":
            {
                var p = Params(("searchType", "imdb"), ("imdb", item.ImdbId));
                if (!item.IsMovie)
                    p["season"] = item.Season.ToString();
                using var lookup = await Json(
                    s,
                    "https://api.subsource.net/api/v1/movies/search?" + Query(p),
                    empty404: true,
                    ct: ct
                );
                var data =
                    lookup.RootElement.ValueKind == JsonValueKind.Array
                        ? lookup.RootElement
                        : Get(lookup.RootElement, "data");
                if (data.ValueKind != JsonValueKind.Array)
                    throw new SubtitleSourceException(s, SubtitleFailureKind.Unavailable);
                var movie = Array(data)
                    .FirstOrDefault(r =>
                        Text(r, "imdbId") == item.ImdbId
                        && (item.IsMovie || Text(r, "season") == item.Season.ToString())
                    );
                var movieId = Text(movie, "movieId");
                if (movieId.Length == 0)
                    break;
                foreach (var lang in langs)
                {
                    try
                    {
                        p = Params(
                            ("movieId", movieId),
                            ("language", lang == "en" ? "english" : "arabic"),
                            ("limit", "100")
                        );
                        if (!item.IsMovie)
                        {
                            p["seasonNumber"] = item.Season.ToString();
                            p["episodeNumber"] = item.Episode.ToString();
                        }
                        using var doc = await Json(
                            s,
                            "https://api.subsource.net/api/v1/subtitles?" + Query(p),
                            empty404: true,
                            ct: ct
                        );
                        data =
                            doc.RootElement.ValueKind == JsonValueKind.Array
                                ? doc.RootElement
                                : Get(doc.RootElement, "data");
                        if (data.ValueKind != JsonValueKind.Array)
                            throw new SubtitleSourceException(s, SubtitleFailureKind.Unavailable);
                        foreach (var row in Array(data).Take(500))
                        {
                            var id = Text(row, "subtitleId");
                            if (
                                id.Length == 0
                                || Text(row, "movieId") is { Length: > 0 } returned && returned != movieId
                            )
                                continue;
                            var language = Text(row, "language").ToLowerInvariant() switch
                            {
                                "english" => "en",
                                "arabic" => "ar",
                                _ => "",
                            };
                            var rel = Joined(Get(row, "releaseInfo"));
                            Add(id, language, "subtitle.srt", rel, "", uploader: SubSourceUploader(row));
                        }
                    }
                    catch (SubtitleSourceException e)
                    {
                        failures[lang] = e;
                    }
                }
                break;
            }
            case "wyzie":
            {
                var p = Params(
                    ("id", item.ImdbId),
                    ("language", string.Join(',', langs.OrderBy(l => l == "en" ? 0 : 1))),
                    ("key", s.ApiKey),
                    ("limit", "200")
                );
                if (!item.IsMovie)
                {
                    p["season"] = item.Season.ToString();
                    p["episode"] = item.Episode.ToString();
                }
                if (hash.Length > 0)
                    p["hash"] = hash;
                if (release.Length > 0)
                    p["video"] = Path.GetFileName(release);
                using var doc = await Json(s, "https://sub.wyzie.io/search?" + Query(p), empty404: true, ct: ct);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    throw new SubtitleSourceException(s, SubtitleFailureKind.Unavailable);
                foreach (var row in Array(doc.RootElement).Take(500))
                {
                    var url = Text(row, "url");
                    if (
                        !SubtitleTransport.ValidUrl(url)
                        || new Uri(url).Host != "sub.wyzie.io"
                        || !new Uri(url).AbsolutePath.StartsWith("/c/")
                    )
                        continue;
                    var rel = Text(row, "release") + " " + Joined(Get(row, "releases"));
                    var name = Text(row, "fileName");
                    if (!SubtitleDownloads.TextSubtitle(name))
                        name = "subtitle." + Text(row, "format");
                    if (
                        !item.IsMovie
                        && Regex.IsMatch(name + " " + rel, @"(?i)s\d+[ ._-]*e\d+|\d+x\d+")
                        && !SubtitleArchive.EpisodeMatches(name + " " + rel, item.Season, item.Episode)
                    )
                        continue;
                    Add(
                        Text(row, "id"),
                        Text(row, "language"),
                        name,
                        rel,
                        url,
                        Array(Get(Get(row, "match"), "reasons"))
                            .Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == "hash")
                    );
                }
                break;
            }
            case "addon":
            {
                var id = item.IsMovie ? item.ImdbId : $"{item.ImdbId}:{item.Season}:{item.Episode}";
                var extra = "filename=" + Uri.EscapeDataString(Path.GetFileName(release));
                if (hash.Length > 0)
                    extra +=
                        "&videoHash="
                        + hash
                        + (System.IO.File.Exists(item.Path) ? "&videoSize=" + new FileInfo(item.Path).Length : "");
                using var doc = await Json(
                    s,
                    s.AddonUrl
                        + "/subtitles/"
                        + (item.IsMovie ? "movie/" : "series/")
                        + Uri.EscapeDataString(id)
                        + "/"
                        + extra
                        + ".json",
                    ct: ct
                );
                if (Get(doc.RootElement, "subtitles").ValueKind != JsonValueKind.Array)
                    throw new SubtitleSourceException(s, SubtitleFailureKind.Unavailable);
                foreach (var row in Array(Get(doc.RootElement, "subtitles")).Take(500))
                {
                    if (
                        !item.IsMovie
                        && (NumberMismatch(row, "season", item.Season) || NumberMismatch(row, "episode", item.Episode))
                    )
                        continue;
                    Add(
                        Text(row, "id"),
                        SubtitleDownloads.Language(Text(row, "lang")),
                        Text(row, "subtitleFileName"),
                        Text(row, "movieReleaseName"),
                        Text(row, "url"),
                        Text(row, "m") == "h"
                    );
                }
                break;
            }
        }
        return result
            .OrderByDescending(r => SubtitleDownloads.PrefersUploader(r, s.Uploader))
            .ThenByDescending(r => r.Score)
            .DistinctBy(r => r.Url.Length > 0 ? r.Url : r.Language + ":" + r.Id)
            .Take(500)
            .ToList();
    }

    // SubSource's uploader field is not confirmed (no key to test with), so every documented spelling is tried.
    internal static string SubSourceUploader(JsonElement row)
    {
        static string Name(JsonElement e) =>
            e.ValueKind == JsonValueKind.String ? e.GetString() ?? ""
            : e.ValueKind == JsonValueKind.Object
                ? new[] { "displayname", "displayName", "name", "username" }
                    .Select(k => Text(e, k))
                    .FirstOrDefault(v => v.Length > 0)
                    ?? ""
            : "";
        foreach (var key in new[] { "uploader", "uploaderName", "uploadedBy" })
            if (Name(Get(row, key)) is { Length: > 0 } found)
                return found;
        return Array(Get(row, "contributors")).Select(Name).FirstOrDefault(v => v.Length > 0) ?? "";
    }

    static bool ContainsSecret(string url, SubtitleSelection s)
    {
        var decoded = Uri.UnescapeDataString(url);
        return new[]
        {
            s.ApiKey,
            s.Password,
            s.AddonAuth,
            s.AddonAuth.Contains(' ') ? s.AddonAuth[(s.AddonAuth.IndexOf(' ') + 1)..] : "",
        }
            .Where(v => v.Length > 0)
            .Any(v =>
                decoded.Contains(v, StringComparison.Ordinal)
                || url.Contains(v, StringComparison.Ordinal)
                || url.Contains(Uri.EscapeDataString(v), StringComparison.Ordinal)
            );
    }

    public async Task<(byte[] Bytes, string Name)> Download(SubtitleCandidate candidate, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(35));
        try
        {
            return await DownloadCore(candidate, budget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SubtitleSourceException(candidate.Provider!.Selection, SubtitleFailureKind.Unavailable);
        }
    }

    async Task<(byte[] Bytes, string Name)> DownloadCore(SubtitleCandidate candidate, CancellationToken ct)
    {
        var reference = candidate.Provider ?? throw new IOException("Subtitle provider unavailable.");
        var s = reference.Selection;
        try
        {
            byte[] bytes;
            var url = candidate.Url;
            if (s.Source == "opensubtitles")
            {
                if (!long.TryParse(reference.Id, out var id))
                    throw new IOException();
                using var doc = await Open(s, "download", ct, new { file_id = id, sub_format = "srt" });
                url = Text(doc.RootElement, "link");
                if (ContainsSecret(url, s))
                    throw new IOException();
                bytes = await transport.Send(s, url, json: false, ct: ct);
            }
            else if (s.Source == "subsource")
                bytes = await transport.Send(
                    s,
                    "https://api.subsource.net/api/v1/subtitles/" + Uri.EscapeDataString(reference.Id) + "/download",
                    true,
                    json: false,
                    ct: ct
                );
            else
            {
                var auth =
                    s.Source == "addon"
                    && new Uri(url).GetLeftPart(UriPartial.Authority)
                        == new Uri(s.AddonUrl).GetLeftPart(UriPartial.Authority);
                bytes = await transport.Send(s, url, auth, json: false, ct: ct);
            }
            var name = candidate.Name;
            if (s.Source is "subdl" or "subsource")
            {
                var picked = await SubtitleArchive.Pick(bytes, reference.Item, reference.Release, ct);
                bytes = picked.Bytes;
                name = picked.Name;
            }
            else if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
            {
                using var gzip = new System.IO.Compression.GZipStream(
                    new MemoryStream(bytes),
                    System.IO.Compression.CompressionMode.Decompress
                );
                using var content = new StreamContent(gzip);
                bytes = await SubtitleTransport.Read(content, 4 * 1024 * 1024, ct);
            }
            var ext = SubtitleDownloads.TextSubtitle(name) ? Path.GetExtension(name).ToLowerInvariant() : ".srt";
            if (!SubtitleDownloads.ValidText(SubtitleDownloads.Decode(bytes), ext))
                throw new IOException();
            return (bytes, name);
        }
        catch (SubtitleSourceException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
            when (e is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            throw new SubtitleSourceException(s, SubtitleFailureKind.Unavailable);
        }
    }

    public Task<byte[]> File(SubtitleSelection source, string url, CancellationToken ct) =>
        transport.Send(source, url, json: false, ct: ct);
}
