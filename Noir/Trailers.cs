using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public sealed class TrailerInfo
{
    public string YouTubeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Trailer";
    public bool Official { get; set; }
    public string Thumbnail => "https://i.ytimg.com/vi/" + YouTubeId + "/hqdefault.jpg";
}

public sealed class TrailersClient : IDisposable
{
    readonly DetailHttp http;
    readonly string key;
    readonly string directory;
    readonly HttpMessageHandler? handler;

    public TrailersClient(string dataDirectory, string tmdbKey = "", HttpMessageHandler? handler = null)
    {
        directory = dataDirectory;
        key = tmdbKey.Trim();
        this.handler = handler;
        http = new(dataDirectory, key, handler);
    }

    public static string YouTubeId(string value)
    {
        if (Regex.IsMatch(value, @"^[A-Za-z0-9_-]{11}$"))
            return value;
        if (
            !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || uri.UserInfo.Length > 0
            || !uri.IsDefaultPort
        )
            return "";
        string id = "";
        if (uri.Host is "youtu.be" or "www.youtu.be")
            id = uri.AbsolutePath.Trim('/');
        else if (
            uri.Host
            is "youtube.com"
                or "www.youtube.com"
                or "m.youtube.com"
                or "www.youtube-nocookie.com"
                or "youtube-nocookie.com"
        )
        {
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0] is "embed" or "shorts")
                id = parts[1];
            else if (uri.AbsolutePath == "/watch")
                foreach (var pair in uri.Query.TrimStart('?').Split('&'))
                    if (pair.StartsWith("v="))
                        id = Uri.UnescapeDataString(pair[2..]);
        }
        return Regex.IsMatch(id, @"^[A-Za-z0-9_-]{11}$") ? id : "";
    }

    public static List<TrailerInfo> Ordered(IEnumerable<TrailerInfo> trailers) =>
        trailers
            .Where(t => YouTubeId(t.YouTubeId).Length > 0)
            .OrderBy(t =>
                t.Kind.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ? 0
                : t.Kind.Equals("Teaser", StringComparison.OrdinalIgnoreCase) ? 1
                : 2
            )
            .ThenByDescending(t => t.Official)
            .DistinctBy(t => t.YouTubeId)
            .Take(16)
            .ToList();

    public static List<TrailerInfo> ParseCinemeta(JsonElement json)
    {
        var m = CreditsClient.C(json, "meta");
        if (m.ValueKind != JsonValueKind.Object)
            m = json;
        var result = new List<TrailerInfo>();
        foreach (
            var j in CreditsClient
                .A(CreditsClient.C(m, "trailers"))
                .Concat(CreditsClient.A(CreditsClient.C(m, "trailerStreams")))
        )
        {
            var id =
                j.ValueKind == JsonValueKind.String
                    ? YouTubeId(j.GetString() ?? "")
                    : new[] { "ytId", "youtube_id", "source", "url", "id" }
                        .Select(k => YouTubeId(CreditsClient.S(j, k)))
                        .FirstOrDefault(x => x.Length > 0)
                        ?? "";
            if (id.Length == 0)
                continue;
            var kind = CreditsClient.S(j, "type");
            var name = CreditsClient.S(j, "title");
            if (name.Length == 0)
                name = CreditsClient.S(j, "name");
            if (kind.Length == 0)
                kind =
                    name.Contains("teaser", StringComparison.OrdinalIgnoreCase) ? "Teaser"
                    : name.Contains("featurette", StringComparison.OrdinalIgnoreCase) ? "Featurette"
                    : "Trailer";
            result.Add(
                new()
                {
                    YouTubeId = id,
                    Name = name,
                    Kind = kind,
                }
            );
        }
        return Ordered(result);
    }

    public static List<TrailerInfo> ParseTmdb(JsonElement json) =>
        Ordered(
            CreditsClient
                .A(CreditsClient.C(json, "results"))
                .Where(j =>
                    CreditsClient.S(j, "site") == "YouTube"
                    && CreditsClient.S(j, "type")
                        is "Trailer"
                            or "Teaser"
                            or "Featurette"
                            or "Behind the Scenes"
                            or "Clip"
                )
                .Select(j => new TrailerInfo
                {
                    YouTubeId = YouTubeId(CreditsClient.S(j, "key")),
                    Name = CreditsClient.S(j, "name"),
                    Kind = CreditsClient.S(j, "type"),
                    Official = CreditsClient.C(j, "official").ValueKind == JsonValueKind.True,
                })
        );

    public Task<List<TrailerInfo>> Resolve(string imdb, bool movie, CancellationToken ct = default) =>
        Task.Run(
            async () =>
            {
                var result = new List<TrailerInfo>();
                if (!Regex.IsMatch(imdb, @"^tt\d{1,12}$"))
                    return result;
                try
                {
                    result.AddRange(
                        ParseCinemeta(
                            await http.Get(
                                    $"https://v3-cinemeta.strem.io/meta/{(movie ? "movie" : "series")}/{imdb}.json",
                                    ct
                                )
                                .ConfigureAwait(false)
                        )
                    );
                }
                catch (Exception e)
                    when (!ct.IsCancellationRequested
                        && e is HttpRequestException or System.IO.IOException or JsonException or TaskCanceledException
                    ) { }
                if (key.Length > 0)
                    try
                    {
                        using var credits = new CreditsClient(directory, key, handler);
                        var id = await credits.Find(imdb, movie, ct).ConfigureAwait(false);
                        if (id > 0)
                            result.AddRange(
                                ParseTmdb(
                                    await http.Get(
                                            $"https://api.themoviedb.org/3/{(movie ? "movie" : "tv")}/{id}/videos",
                                            ct
                                        )
                                        .ConfigureAwait(false)
                                )
                            );
                    }
                    catch (Exception e)
                        when (!ct.IsCancellationRequested
                            && e
                                is HttpRequestException
                                    or System.IO.IOException
                                    or JsonException
                                    or TaskCanceledException
                        ) { }
                return Ordered(result);
            },
            ct
        );

    public void Dispose() => http.Dispose();
}
