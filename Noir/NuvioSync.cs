using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

// Nuvio keeps watched items per account profile on its own server (Supabase). Noir signs in the way
// a TV does: it shows a code, you approve it at nuvio.tv/link, and Noir never sees your password.
public static class NuvioConfig
{
    // From the official Nuvio app build; the key is the public "anon" client key every install carries.
    public const string Url = "https://api.nuvio.tv";
    public const string FallbackUrl = "https://api-two.nuvioapp.space";
    public const string Key =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJyb2xlIjoiYW5vbiIsImlzcyI6InN1cGFiYXNlIiwiaWF0IjoxNzgxNTIxMzQ2LCJleHAiOjE5MzkyMDEzNDZ9.tmQaj682pwzehpqlgCDMnySOqiUvpgRbrE43T4VJpDI";
    public const string LinkPage = "https://nuvio.tv/link";
}

public sealed class NuvioSession
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public int Profile { get; set; }
    public string ProfileName { get; set; } = "";
    public long Cursor { get; set; }
    public string ClientId { get; set; } = "noir-pc-" + Guid.NewGuid().ToString("N");
}

public sealed record NuvioLink(string DeviceCode, string UserCode, string Url, int Interval, string Nonce)
{
    public string Code =>
        UserCode.Length > 3
            ? UserCode[..3].ToUpperInvariant() + "-" + UserCode[3..].ToUpperInvariant()
            : UserCode.ToUpperInvariant();
}

public sealed record NuvioProfile(int Index, string Name);

public sealed record NuvioEvent(long Id, string Operation, NuvioWatched Item)
{
    public bool Deleted => Regex.IsMatch(Operation, "(?i)del|remov");
}

public sealed record NuvioLibraryItem(string ContentId, string Type, string Name);

public sealed class NuvioExpiredException() : IOException("Your Nuvio sign-in has expired. Connect again.");

public sealed class NuvioClient(HttpClient http, NuvioSession? session = null, Action<NuvioSession>? saved = null)
{
    public NuvioSession? Session { get; private set; } = session;

    async Task<HttpResponseMessage> Send(string path, object? body, bool auth, CancellationToken ct)
    {
        foreach (var root in new[] { NuvioConfig.Url, NuvioConfig.FallbackUrl })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, root + path)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body ?? new { }),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
            request.Headers.Add("apikey", NuvioConfig.Key);
            request.Headers.TryAddWithoutValidation(
                "Authorization",
                "Bearer " + (auth && Session != null ? Session.AccessToken : NuvioConfig.Key)
            );
            try
            {
                var response = await http.SendAsync(request, ct);
                if ((int)response.StatusCode is 502 or 503 or 504 or >= 520 and <= 526 && root == NuvioConfig.Url)
                {
                    response.Dispose();
                    continue;
                }
                return response;
            }
            catch (HttpRequestException) when (root == NuvioConfig.Url && !ct.IsCancellationRequested) { }
        }
        throw new HttpRequestException("Nuvio couldn't be reached.");
    }

    async Task<JsonElement> Call(string path, object? body, bool auth, CancellationToken ct)
    {
        if (auth && Session != null && Session.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(1))
            await Refresh(ct);
        var response = await Send(path, body, auth, ct);
        if (auth && response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            await Refresh(ct);
            response = await Send(path, body, auth, ct);
        }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (auth && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new NuvioExpiredException();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Nuvio answered {(int)response.StatusCode}.",
                    null,
                    response.StatusCode
                );
            using var doc = JsonDocument.Parse(text.Length == 0 ? "null" : text);
            return doc.RootElement.Clone();
        }
    }

    Task<JsonElement> Rpc(string name, object? body, CancellationToken ct, bool auth = true) =>
        Call("/rest/v1/rpc/" + name, body, auth, ct);

    public async Task Refresh(CancellationToken ct)
    {
        if (Session == null || Session.RefreshToken.Length == 0)
            throw new NuvioExpiredException();
        var response = await Send(
            "/auth/v1/token?grant_type=refresh_token",
            new { refresh_token = Session.RefreshToken },
            false,
            ct
        );
        using (response)
        {
            if (
                response.StatusCode
                is HttpStatusCode.BadRequest
                    or HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden
            )
                throw new NuvioExpiredException();
            response.EnsureSuccessStatusCode();
            Tokens(JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement);
        }
    }

    void Tokens(JsonElement j)
    {
        Session ??= new();
        Session.AccessToken = HistoryData.S(j, "access_token");
        Session.RefreshToken = HistoryData.S(j, "refresh_token") is { Length: > 0 } r ? r : Session.RefreshToken;
        Session.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, HistoryData.N(j, "expires_in")));
        if (Session.AccessToken.Length == 0)
            throw new IOException("Nuvio didn't return a sign-in.");
        saved?.Invoke(Session);
    }

    static JsonElement First(JsonElement j) =>
        j.ValueKind == JsonValueKind.Array ? j.EnumerateArray().FirstOrDefault() : j;

    public async Task<NuvioLink> StartLink(string deviceName, CancellationToken ct)
    {
        var nonce = Guid.NewGuid().ToString();
        var j = First(
            await Rpc(
                "start_device_login_session",
                new
                {
                    p_device_nonce = nonce,
                    p_redirect_base_url = NuvioConfig.LinkPage,
                    p_device_name = deviceName,
                    p_device_type = "mobile",
                },
                ct,
                false
            )
        );
        var link = new NuvioLink(
            HistoryData.S(j, "device_code"),
            HistoryData.S(j, "user_code"),
            HistoryData.S(j, "verification_uri_complete"),
            Math.Clamp(HistoryData.N(j, "poll_interval_seconds") is > 0 and var n ? n : 3, 2, 10),
            nonce
        );
        if (link.DeviceCode.Length == 0 || link.UserCode.Length == 0)
            throw new IOException("Nuvio couldn't start a sign-in. Try again in a moment.");
        return link;
    }

    // "pending", "approved", or anything else (expired, denied).
    public async Task<string> PollLink(NuvioLink link, CancellationToken ct) =>
        HistoryData
            .S(
                First(
                    await Rpc(
                        "poll_tv_login_session",
                        new { p_code = link.DeviceCode, p_device_nonce = link.Nonce },
                        ct,
                        false
                    )
                ),
                "status"
            )
            .Trim()
            .ToLowerInvariant();

    public async Task Exchange(NuvioLink link, CancellationToken ct) =>
        Tokens(
            await Call(
                "/functions/v1/tv-logins-exchange",
                new { code = link.DeviceCode, device_nonce = link.Nonce },
                false,
                ct
            )
        );

    public async Task<List<NuvioProfile>> Profiles(CancellationToken ct)
    {
        var j = await Rpc("sync_pull_profiles", null, ct);
        return j.ValueKind != JsonValueKind.Array
            ? []
            : j.EnumerateArray()
                .Select(p => new NuvioProfile(
                    HistoryData.N(p, "profile_index") is > 0 and var i ? i : 1,
                    HistoryData.S(p, "name")
                ))
                .OrderBy(p => p.Index)
                .ToList();
    }

    static int? Number(JsonElement j, string key) =>
        HistoryData.Prop(j, key).ValueKind == JsonValueKind.Number ? HistoryData.Prop(j, key).GetInt32() : null;

    static NuvioWatched Watched(JsonElement j) =>
        new(
            HistoryData.S(j, "content_id"),
            HistoryData.S(j, "content_type"),
            HistoryData.S(j, "title"),
            Number(j, "season"),
            Number(j, "episode"),
            Normalize(long.TryParse(HistoryData.S(j, "watched_at"), out var at) ? at : 0)
        );

    // Some Nuvio clients store 20240131235959 instead of epoch milliseconds.
    public static long Normalize(long value)
    {
        if (value < 19000101000000L || value > 29991231235959L)
            return value;
        return DateTime.TryParseExact(
            value.ToString(),
            "yyyyMMddHHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var at
        )
            ? new DateTimeOffset(at, TimeSpan.Zero).ToUnixTimeMilliseconds()
            : value;
    }

    public async Task<List<NuvioWatched>> PullWatched(int profile, CancellationToken ct)
    {
        var all = new List<NuvioWatched>();
        for (var page = 1; page <= 200; page++)
        {
            var j = await Rpc(
                "sync_pull_watched_items",
                new
                {
                    p_profile_id = profile,
                    p_page = page,
                    p_page_size = 500,
                },
                ct
            );
            var items = j.ValueKind == JsonValueKind.Array ? j.EnumerateArray().Select(Watched).ToList() : [];
            all.AddRange(items);
            if (items.Count < 500)
                break;
        }
        return all;
    }

    public async Task<long> Cursor(int profile, CancellationToken ct)
    {
        var j = await Rpc("sync_get_watched_items_delta_cursor", new { p_profile_id = profile }, ct);
        return j.ValueKind == JsonValueKind.Number ? j.GetInt64()
            : long.TryParse(HistoryData.S(First(j), "cursor"), out var c) ? c
            : 0;
    }

    public async Task<List<NuvioEvent>> PullDelta(int profile, long since, CancellationToken ct)
    {
        var all = new List<NuvioEvent>();
        for (var round = 0; round < 100; round++)
        {
            var j = await Rpc(
                "sync_pull_watched_items_delta",
                new
                {
                    p_profile_id = profile,
                    p_since_event_id = since,
                    p_limit = 500,
                },
                ct
            );
            var events =
                j.ValueKind == JsonValueKind.Array
                    ? j.EnumerateArray()
                        .Select(e => new NuvioEvent(
                            long.TryParse(HistoryData.S(e, "event_id"), out var id) ? id : 0,
                            HistoryData.S(e, "operation"),
                            Watched(e)
                        ))
                        .ToList()
                    : [];
            all.AddRange(events);
            if (events.Count < 500)
                break;
            since = events.Max(e => e.Id);
        }
        return all;
    }

    public async Task<List<NuvioLibraryItem>> PullLibrary(int profile, CancellationToken ct)
    {
        var all = new List<NuvioLibraryItem>();
        for (var offset = 0; offset < 20000; offset += 500)
        {
            var j = await Rpc(
                "sync_pull_library",
                new
                {
                    p_profile_id = profile,
                    p_limit = 500,
                    p_offset = offset,
                },
                ct
            );
            var items =
                j.ValueKind == JsonValueKind.Array
                    ? j.EnumerateArray()
                        .Select(i => new NuvioLibraryItem(
                            HistoryData.S(i, "content_id"),
                            HistoryData.S(i, "content_type"),
                            HistoryData.S(i, "name")
                        ))
                        .ToList()
                    : [];
            all.AddRange(items);
            if (items.Count < 500)
                break;
        }
        return all;
    }

    public Task PushWatched(int profile, IEnumerable<NuvioWatched> items, CancellationToken ct) =>
        Rpc(
            "sync_push_watched_items",
            new
            {
                p_profile_id = profile,
                p_items = items
                    .Select(i => new
                    {
                        content_id = i.ContentId,
                        content_type = i.Type,
                        title = i.Title,
                        season = i.Season,
                        episode = i.Episode,
                        watched_at = i.WatchedAt,
                    })
                    .ToList(),
                p_origin_client_id = Session?.ClientId,
            },
            ct
        );

    public Task DeleteWatched(
        int profile,
        IEnumerable<(string ContentId, int? Season, int? Episode)> keys,
        CancellationToken ct
    ) =>
        Rpc(
            "sync_delete_watched_items",
            new
            {
                p_profile_id = profile,
                p_keys = keys.Select(k => new
                    {
                        content_id = k.ContentId,
                        season = k.Season,
                        episode = k.Episode,
                    })
                    .ToList(),
                p_origin_client_id = Session?.ClientId,
            },
            ct
        );

    public static ImportRow Row(NuvioWatched w)
    {
        var movie =
            w.Type.Equals("movie", StringComparison.OrdinalIgnoreCase)
            || w.Type.Equals("film", StringComparison.OrdinalIgnoreCase);
        return new(
            "Nuvio",
            w.Title,
            0,
            w.Season is null && w.Episode is null
                ? movie
                    ? true
                    : null
                : false,
            Regex.IsMatch(w.ContentId, @"^tt\d+$") ? w.ContentId : "",
            "",
            w.Season ?? 0,
            w.Episode ?? 0,
            "",
            w.WatchedAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(w.WatchedAt) : null,
            0
        );
    }

    public static string Key(string contentId, int? season, int? episode) => NuvioKeys.Key(contentId, season, episode);

    public static (string ContentId, int? Season, int? Episode) ParseKey(string key) => NuvioKeys.ParseKey(key);
}

public static class NuvioStorage
{
    public static string PathIn(string dataDirectory) => Path.Combine(dataDirectory, "nuvio.dpapi");

    public static NuvioSession? Load(string dataDirectory)
    {
        try
        {
            var path = PathIn(dataDirectory);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<NuvioSession>(
                    ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)
                )
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Save(string dataDirectory, NuvioSession session) =>
        File.WriteAllBytes(
            PathIn(dataDirectory),
            ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(session), null, DataProtectionScope.CurrentUser)
        );

    public static void Forget(string dataDirectory)
    {
        try
        {
            File.Delete(PathIn(dataDirectory));
        }
        catch (IOException) { }
    }
}
