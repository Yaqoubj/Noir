using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Noir;

// Marks titles watched in the user's Stremio account (the account Harbor also reads). Wired into the watch-sync
// queue as the "Stremio" provider (StremioSync below); off unless its own switch is on.
// Safety: read-modify-write of one library item, every unknown member kept, remote removed/temp kept,
// the remote item saved once under stremio-originals before the first write, watched bits only ever set.
public sealed class StremioMarkOptions
{
    public bool Enabled { get; set; }

    // "Add watched titles to my Stremio library": off by default; without it, titles the account lacks are skipped.
    public bool AddWatchedTitles { get; set; }
}

public enum StremioMarkStatus
{
    Marked,
    AlreadyWatched,
    Added,
    Restored,
    ChangedSince,
    Off,
    NotInLibrary,
    UnknownId,
    UnknownEpisode,
    EpisodesOutOfStep,
    NoKey,
    Auth,
    Network,
    Server,
    BadResponse,
    LocalSave,
}

// Site: stop and back off the whole Stremio queue. Title: skip this title only. None: success or switched off.
public enum StremioFailure
{
    None,
    Site,
    Title,
}

public sealed record StremioMarkResult(StremioMarkStatus Status, string ItemId, string English, string Arabic)
{
    public StremioFailure Failure =>
        Status switch
        {
            StremioMarkStatus.Marked
            or StremioMarkStatus.AlreadyWatched
            or StremioMarkStatus.Added
            or StremioMarkStatus.Restored
            or StremioMarkStatus.Off => StremioFailure.None,
            StremioMarkStatus.NotInLibrary
            or StremioMarkStatus.ChangedSince
            or StremioMarkStatus.UnknownId
            or StremioMarkStatus.UnknownEpisode
            or StremioMarkStatus.EpisodesOutOfStep => StremioFailure.Title,
            _ => StremioFailure.Site,
        };

    public bool Succeeded =>
        Status is StremioMarkStatus.Marked or StremioMarkStatus.AlreadyWatched or StremioMarkStatus.Added;

    public string Text(bool arabic) => arabic ? Arabic : English;
}

/// <summary>What one Restore press did: titles put back, titles left alone because they changed in Stremio
/// after Noir's last write, and any other failures.</summary>
public sealed record StremioRestoreSummary(int Restored, int Kept, IReadOnlyList<StremioMarkResult> Failed)
{
    public string Text(bool arabic)
    {
        var parts = new List<string>();
        if (Restored > 0 || Kept == 0 && Failed.Count == 0)
            parts.Add(
                arabic
                    ? Restored switch
                    {
                        1 => "استُعيد عنصر واحد",
                        2 => "استُعيد عنصران",
                        >= 3 and <= 10 => $"استُعيدت {Restored} عناصر",
                        _ => $"استُعيد {Restored} عنصرًا",
                    }
                    : Restored + " restored"
            );
        if (Kept > 0)
            parts.Add(
                arabic
                    ? Kept switch
                    {
                        1 => "تُرك عنصر واحد كما هو لأنه تغيّر في Stremio منذ ذلك الحين",
                        2 => "تُرك عنصران كما هما لأنهما تغيّرا في Stremio منذ ذلك الحين",
                        >= 3 and <= 10 => $"تُركت {Kept} عناصر كما هي لأنها تغيّرت في Stremio منذ ذلك الحين",
                        _ => $"تُركت {Kept} عنصرًا كما هي لأنها تغيّرت في Stremio منذ ذلك الحين",
                    }
                : Kept == 1 ? "1 left as it is because it changed in Stremio since"
                : Kept + " left as they are because they changed in Stremio since"
            );
        var text = string.Join(arabic ? "، و" : ", ", parts);
        if (Failed.Count > 0)
            text = (text.Length > 0 ? text + " · " : "") + Failed[0].Text(arabic);
        return text;
    }
}

public enum StremioWatchedState
{
    Watched,
    NotWatched,
    NotInLibrary,
    Failed,
}

public sealed record StremioWatchedRead(StremioWatchedState State, StremioMarkResult? Failure);

public sealed class StremioLibraryWriter
{
    public const string Api = "https://api.strem.io/api/";
    const string Added = "noirAdded";

    readonly string dataDirectory;
    readonly HttpClient http;
    readonly Func<string?> authKey;
    readonly Func<string, CancellationToken, Task<IReadOnlyList<StremioVideo>>> videos;
    readonly Func<DateTimeOffset> now;

    public StremioMarkOptions Options { get; }

    // handler: injectable for checks (no live calls); videos: a show's Cinemeta videos (see CinemetaVideos).
    // authKey defaults to the key Noir already stores for the Stremio import (read only).
    public StremioLibraryWriter(
        string dataDirectory,
        HttpMessageHandler handler,
        Func<string, CancellationToken, Task<IReadOnlyList<StremioVideo>>> videos,
        StremioMarkOptions? options = null,
        Func<string?>? authKey = null,
        Func<DateTimeOffset>? now = null
    )
    {
        this.dataDirectory = dataDirectory;
        http = new HttpClient(handler, false) { Timeout = TimeSpan.FromSeconds(30) };
        this.videos = videos;
        Options = options ?? new();
        this.authKey = authKey ?? (() => StoredKey(dataDirectory));
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public string OriginalsDirectory => Path.Combine(dataDirectory, "stremio-originals");

    // The import's key file (HistoryImportPage.StremioKeyPath): DPAPI, current user. Never written here.
    public static string? StoredKey(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "stremio.dpapi");
        try
        {
            if (!File.Exists(path))
                return null;
            var key = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)
            );
            return key.Length >= 8 ? key : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // A show's videos from Cinemeta's meta document, for the bitfield order.
    public static Func<string, CancellationToken, Task<IReadOnlyList<StremioVideo>>> CinemetaVideos(
        HttpMessageHandler handler
    )
    {
        var client = new HttpClient(handler, false) { Timeout = TimeSpan.FromSeconds(30) };
        return async (id, ct) =>
        {
            using var response = await client.GetAsync($"https://v3-cinemeta.strem.io/meta/series/{id}.json", ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return ParseVideos(doc.RootElement);
        };
    }

    public static List<StremioVideo> ParseVideos(JsonElement metaDocument)
    {
        var list = new List<StremioVideo>();
        if (
            !metaDocument.TryGetProperty("meta", out var meta)
            || !meta.TryGetProperty("videos", out var all)
            || all.ValueKind != JsonValueKind.Array
        )
            return list;
        foreach (var v in all.EnumerateArray())
        {
            var id =
                v.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() ?? "" : "";
            list.Add(new(id, Number(v, "season"), Number(v, "episode") ?? Number(v, "number"), Date(v, "released")));
        }
        return list;
    }

    // "Already watched?" read for one title; never writes.
    public async Task<StremioWatchedRead> IsWatched(VideoItem item, CancellationToken ct)
    {
        var key = authKey();
        if (string.IsNullOrEmpty(key))
            return new(StremioWatchedState.Failed, Result(StremioMarkStatus.NoKey, item.ImdbId));
        if (!ValidId(item.ImdbId))
            return new(StremioWatchedState.Failed, Result(StremioMarkStatus.UnknownId, item.ImdbId));
        var (remote, failure) = await Get(key, item.ImdbId, ct);
        if (failure != null)
            return new(StremioWatchedState.Failed, failure);
        if (remote == null)
            return new(StremioWatchedState.NotInLibrary, null);
        var state = remote["state"] as JsonObject;
        if (item.IsMovie)
            return new(MovieWatched(state) ? StremioWatchedState.Watched : StremioWatchedState.NotWatched, null);
        var (field, _, fieldFailure) = await Field(item, state, ct);
        if (fieldFailure != null)
            return new(StremioWatchedState.Failed, fieldFailure);
        return new(
            field!.IsWatched(VideoId(item)) ? StremioWatchedState.Watched : StremioWatchedState.NotWatched,
            null
        );
    }

    public async Task<StremioMarkResult> MarkWatched(VideoItem item, DateTimeOffset watchedAt, CancellationToken ct)
    {
        var id = item.ImdbId;
        if (!Options.Enabled)
            return Result(StremioMarkStatus.Off, id);
        if (!ValidId(id))
            return Result(StremioMarkStatus.UnknownId, id);
        if (!item.IsMovie && (item.Season < 0 || item.Episode <= 0))
            return Result(StremioMarkStatus.UnknownEpisode, id);
        var key = authKey();
        if (string.IsNullOrEmpty(key))
            return Result(StremioMarkStatus.NoKey, id);
        var (remote, failure) = await Get(key, id, ct);
        if (failure != null)
            return failure;
        var stamp = Stamp(now());
        if (remote == null)
        {
            if (!Options.AddWatchedTitles)
                return Result(StremioMarkStatus.NotInLibrary, id);
            var created = await NewItem(item, watchedAt, stamp, ct);
            if (created.Item == null)
                return created.Failure!;
            if (
                !SaveOriginal(id, new JsonObject { [Added] = true, ["_id"] = id }.ToJsonString())
                || !SaveWritten(id, stamp)
            )
                return Result(StremioMarkStatus.LocalSave, id);
            var put = await Put(key, created.Item, ct);
            return put ?? Result(StremioMarkStatus.Added, id);
        }
        var state = remote["state"] as JsonObject;
        if (state == null)
            remote["state"] = state = new JsonObject();
        if (item.IsMovie)
        {
            if (MovieWatched(state))
                return Result(StremioMarkStatus.AlreadyWatched, id);
            state["timesWatched"] = Math.Max(1, Int(state, "timesWatched"));
            state["flaggedWatched"] = 1;
        }
        else
        {
            var (field, _, fieldFailure) = await Field(item, state, ct);
            if (fieldFailure != null)
                return fieldFailure;
            if (field!.IsWatched(VideoId(item)))
                return Result(StremioMarkStatus.AlreadyWatched, id);
            field.MarkWatched(VideoId(item));
            state["watched"] = field.ToString();
        }
        var lastWatched = DateTimeOffset.TryParse(Str(state, "lastWatched"), out var previous)
            ? previous
            : (DateTimeOffset?)null;
        if (lastWatched == null || lastWatched < watchedAt)
            state["lastWatched"] = Stamp(watchedAt);
        remote["_mtime"] = stamp;
        if (!SaveOriginal(id, remote.Original) || !SaveWritten(id, stamp))
            return Result(StremioMarkStatus.LocalSave, id);
        return await Put(key, remote.Node, ct) ?? Result(StremioMarkStatus.Marked, id);
    }

    public bool HasOriginal(string id) => ValidId(id) && File.Exists(OriginalPath(id));

    /// <summary>The same test without a writer, for the UI's Restore buttons.</summary>
    public static bool OriginalExists(string dataDirectory, string id) =>
        ValidId(id) && File.Exists(Path.Combine(dataDirectory, "stremio-originals", id + ".json"));

    /// <summary>Restores each title in turn and counts what was put back and what was left alone.</summary>
    public async Task<StremioRestoreSummary> Restore(IEnumerable<string> ids, CancellationToken ct)
    {
        int restored = 0,
            kept = 0;
        var failed = new List<StremioMarkResult>();
        foreach (var id in ids)
        {
            var result = await Restore(id, ct);
            if (result.Status == StremioMarkStatus.Restored)
                restored++;
            else if (result.Status == StremioMarkStatus.ChangedSince)
                kept++;
            else
                failed.Add(result);
        }
        return new(restored, kept, failed);
    }

    // Puts the saved original back (its _mtime moved to now so it wins); a title Noir added is marked removed.
    // The remote removed/temp flags are kept as they are now. A title changed in Stremio after Noir's last write
    // to it (its _mtime is newer, or Noir has no record of that write) is left as it is.
    public async Task<StremioMarkResult> Restore(string id, CancellationToken ct)
    {
        if (!HasOriginal(id))
            return Result(StremioMarkStatus.UnknownId, id);
        var key = authKey();
        if (string.IsNullOrEmpty(key))
            return Result(StremioMarkStatus.NoKey, id);
        JsonObject original;
        try
        {
            original = JsonNode.Parse(File.ReadAllText(OriginalPath(id))) as JsonObject ?? throw new JsonException();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return Result(StremioMarkStatus.LocalSave, id);
        }
        var (remote, failure) = await Get(key, id, ct);
        if (failure != null)
            return failure;
        if (remote == null)
            return Result(StremioMarkStatus.NotInLibrary, id);
        if (
            WrittenAt(id) is not { } written
            || ParseStamp(Str(remote.Node, "_mtime")) is not { } changed
            || changed > written
        )
            return Result(StremioMarkStatus.ChangedSince, id);
        JsonObject restored;
        if (original[Added] is JsonValue added && added.TryGetValue<bool>(out var wasAdded) && wasAdded)
        {
            restored = remote.Node;
            restored["removed"] = true;
        }
        else
        {
            restored = original;
            foreach (var flag in new[] { "removed", "temp" })
                if (remote.Node.ContainsKey(flag))
                    restored[flag] = remote.Node[flag]?.DeepClone();
        }
        var stamp = Stamp(now());
        restored["_mtime"] = stamp;
        if (!SaveWritten(id, stamp))
            return Result(StremioMarkStatus.LocalSave, id);
        return await Put(key, restored, ct) ?? Result(StremioMarkStatus.Restored, id);
    }

    async Task<(StremioWatchedBitField? Field, IReadOnlyList<string>? Ids, StremioMarkResult? Failure)> Field(
        VideoItem item,
        JsonObject? state,
        CancellationToken ct
    )
    {
        IReadOnlyList<StremioVideo> list;
        try
        {
            list = await videos(item.ImdbId, ct);
        }
        catch (Exception e)
            when (!ct.IsCancellationRequested && e is HttpRequestException or TaskCanceledException or IOException)
        {
            return (null, null, Result(StremioMarkStatus.Network, item.ImdbId));
        }
        catch (JsonException)
        {
            return (null, null, Result(StremioMarkStatus.BadResponse, item.ImdbId));
        }
        var ids = StremioWatchedBitField.Order(list);
        if (!ids.Contains(VideoId(item)))
            return (null, ids, Result(StremioMarkStatus.UnknownEpisode, item.ImdbId));
        var serialized = Str(state, "watched");
        StremioWatchedBitField field;
        try
        {
            field =
                serialized.Length > 0
                    ? StremioWatchedBitField.Parse(serialized, ids)
                    : StremioWatchedBitField.Empty(ids);
        }
        catch (Exception e) when (e is FormatException or InvalidDataException)
        {
            return (null, ids, Result(StremioMarkStatus.EpisodesOutOfStep, item.ImdbId));
        }
        // Never write a field that would drop episodes the account already has as watched.
        if (field.LostRemoteBits)
            return (null, ids, Result(StremioMarkStatus.EpisodesOutOfStep, item.ImdbId));
        return (field, ids, null);
    }

    async Task<(JsonObject? Item, StremioMarkResult? Failure)> NewItem(
        VideoItem item,
        DateTimeOffset watchedAt,
        string stamp,
        CancellationToken ct
    )
    {
        var state = new JsonObject
        {
            ["lastWatched"] = Stamp(watchedAt),
            ["timeWatched"] = 0,
            ["timeOffset"] = 0,
            ["overallTimeWatched"] = 0,
            ["timesWatched"] = item.IsMovie ? 1 : 0,
            ["flaggedWatched"] = item.IsMovie ? 1 : 0,
            ["duration"] = 0,
            ["video_id"] = null,
            ["watched"] = null,
            ["noNotif"] = false,
        };
        if (!item.IsMovie)
        {
            var (field, _, failure) = await Field(item, null, ct);
            if (failure != null)
                return (null, failure);
            field!.MarkWatched(VideoId(item));
            state["watched"] = field.ToString();
        }
        var poster =
            Uri.TryCreate(item.Poster, UriKind.Absolute, out var p) && p.Scheme == Uri.UriSchemeHttps
                ? item.Poster
                : null;
        return (
            new JsonObject
            {
                ["_id"] = item.ImdbId,
                ["name"] = item.Title,
                ["type"] = item.IsMovie ? "movie" : "series",
                ["poster"] = poster,
                ["posterShape"] = "poster",
                ["removed"] = false,
                ["temp"] = false,
                ["_ctime"] = stamp,
                ["_mtime"] = stamp,
                ["state"] = state,
                ["behaviorHints"] = new JsonObject
                {
                    ["defaultVideoId"] = null,
                    ["featuredVideoId"] = null,
                    ["hasScheduledVideos"] = false,
                },
            },
            null
        );
    }

    sealed record Remote(JsonObject Node, string Original)
    {
        public JsonNode? this[string name]
        {
            get => Node[name];
            set => Node[name] = value;
        }
    }

    async Task<(Remote? Item, StremioMarkResult? Failure)> Get(string key, string id, CancellationToken ct)
    {
        var (root, failure) = await Call(
            "datastoreGet",
            new JsonObject
            {
                ["authKey"] = key,
                ["collection"] = "libraryItem",
                ["ids"] = new JsonArray(id),
                ["all"] = false,
            },
            id,
            ct
        );
        if (failure != null)
            return (null, failure);
        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return (null, Result(StremioMarkStatus.BadResponse, id));
        foreach (var element in result.EnumerateArray())
            if (
                element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("_id", out var itemId)
                && itemId.ValueKind == JsonValueKind.String
                && itemId.GetString() == id
            )
            {
                var raw = element.GetRawText();
                return (new Remote((JsonObject)JsonNode.Parse(raw)!, raw), null);
            }
        return (null, null);
    }

    async Task<StremioMarkResult?> Put(string key, JsonObject item, CancellationToken ct)
    {
        var id = item["_id"]?.GetValue<string>() ?? "";
        var (_, failure) = await Call(
            "datastorePut",
            new JsonObject
            {
                ["authKey"] = key,
                ["collection"] = "libraryItem",
                ["changes"] = new JsonArray(item),
            },
            id,
            ct
        );
        return failure;
    }

    async Task<(JsonElement Root, StremioMarkResult? Failure)> Call(
        string method,
        JsonObject body,
        string id,
        CancellationToken ct
    )
    {
        try
        {
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(Api + method, content, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return (default, Result(StremioMarkStatus.Auth, id));
            if (!response.IsSuccessStatusCode)
                return (default, Result(StremioMarkStatus.Server, id));
            var text = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Object)
                return (default, Result(StremioMarkStatus.BadResponse, id));
            if (
                root.TryGetProperty("error", out var error)
                && error.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            )
            {
                var message =
                    error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m)
                        ? m.ToString()
                        : error.ToString();
                var code =
                    error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var c)
                        ? c.ToString()
                        : "";
                var auth = code == "1" || Regex.IsMatch(message, "session|auth|token|login", RegexOptions.IgnoreCase);
                return (default, Result(auth ? StremioMarkStatus.Auth : StremioMarkStatus.Server, id));
            }
            return (root, null);
        }
        catch (Exception e)
            when (!ct.IsCancellationRequested && e is HttpRequestException or TaskCanceledException or IOException)
        {
            return (default, Result(StremioMarkStatus.Network, id));
        }
        catch (JsonException)
        {
            return (default, Result(StremioMarkStatus.BadResponse, id));
        }
    }

    // Saved once, before the first write to an item; an existing original is never replaced.
    bool SaveOriginal(string id, string json)
    {
        var path = OriginalPath(id);
        if (File.Exists(path))
            return true;
        try
        {
            Directory.CreateDirectory(OriginalsDirectory);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            try
            {
                File.Move(temp, path, false);
            }
            catch (IOException) when (File.Exists(path))
            {
                File.Delete(temp);
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    string OriginalPath(string id) => Path.Combine(OriginalsDirectory, id + ".json");

    // The _mtime of Noir's last write to an item, kept next to its original and replaced on every write.
    string WrittenPath(string id) => Path.Combine(OriginalsDirectory, id + ".written");

    bool SaveWritten(string id, string stamp)
    {
        try
        {
            Directory.CreateDirectory(OriginalsDirectory);
            var path = WrittenPath(id);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, stamp, new UTF8Encoding(false));
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    DateTimeOffset? WrittenAt(string id)
    {
        try
        {
            return File.Exists(WrittenPath(id)) ? ParseStamp(File.ReadAllText(WrittenPath(id)).Trim()) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static DateTimeOffset? ParseStamp(string text) =>
        DateTimeOffset.TryParse(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var d
        )
            ? d
            : null;

    static bool ValidId(string id) => Regex.IsMatch(id ?? "", @"^tt\d+$");

    static string VideoId(VideoItem item) => $"{item.ImdbId}:{item.Season}:{item.Episode}";

    static bool MovieWatched(JsonObject? state) => Int(state, "timesWatched") > 0 || Int(state, "flaggedWatched") > 0;

    static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    static string Str(JsonObject? o, string name) =>
        o?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    static long Int(JsonObject? o, string name)
    {
        if (o?[name] is not JsonValue v)
            return 0;
        if (v.TryGetValue<long>(out var l))
            return l;
        if (v.TryGetValue<double>(out var d))
            return (long)d;
        if (v.TryGetValue<bool>(out var b))
            return b ? 1 : 0;
        return v.TryGetValue<string>(out var s) && long.TryParse(s, out var p) ? p : 0;
    }

    static long? Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    static DateTimeOffset? Date(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(
            v.GetString(),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var d
        )
            ? d
            : null;

    static StremioMarkResult Result(StremioMarkStatus status, string id)
    {
        var (en, ar) = Message(status);
        return new(status, id, en, ar);
    }

    /// <summary>The English and Arabic text for one outcome (the queue compares stored job messages with these).</summary>
    public static (string English, string Arabic) Message(StremioMarkStatus status) =>
        status switch
        {
            StremioMarkStatus.Marked => ("Marked watched in your Stremio account", "عُلِّم كمُشاهَد في حساب Stremio"),
            StremioMarkStatus.AlreadyWatched => (
                "Already watched in your Stremio account",
                "مُشاهَد مسبقًا في حساب Stremio"
            ),
            StremioMarkStatus.Added => ("Added to your Stremio library as watched", "أُضيف إلى مكتبة Stremio كمُشاهَد"),
            StremioMarkStatus.Restored => (
                "Restored the Stremio item from Noir's saved copy",
                "استُعيد عنصر Stremio من نسخة Noir المحفوظة"
            ),
            StremioMarkStatus.ChangedSince => (
                "Left as it is because it changed in Stremio since",
                "تُرك كما هو لأنه تغيّر في Stremio منذ ذلك الحين"
            ),
            StremioMarkStatus.Off => ("Marking watched in Stremio is off", "تعليم المُشاهَد في Stremio متوقف"),
            StremioMarkStatus.NotInLibrary => ("Not in your Stremio library", "ليس في مكتبة Stremio"),
            StremioMarkStatus.UnknownId => ("This title has no IMDb identity", "لا يوجد معرّف IMDb لهذا العنوان"),
            StremioMarkStatus.UnknownEpisode => ("Stremio does not list this episode", "لا يعرض Stremio هذه الحلقة"),
            StremioMarkStatus.EpisodesOutOfStep => (
                "Stremio's episode list is out of step; nothing was changed",
                "قائمة حلقات Stremio غير متطابقة؛ لم يتغير شيء"
            ),
            StremioMarkStatus.NoKey => ("Connect Stremio first", "اربط Stremio أولًا"),
            StremioMarkStatus.Auth => (
                "Stremio sign-in expired. Connect Stremio again",
                "انتهت جلسة Stremio. اربط Stremio مجددًا"
            ),
            StremioMarkStatus.Network => ("Stremio could not be reached", "تعذّر الوصول إلى Stremio"),
            StremioMarkStatus.Server => (
                "Stremio had a problem. Noir will try later",
                "واجه Stremio مشكلة. سيحاول Noir لاحقًا"
            ),
            StremioMarkStatus.BadResponse => ("Stremio sent an unexpected reply", "أرسل Stremio ردًا غير متوقع"),
            _ => ("Noir could not save a copy of the Stremio item", "تعذّر على Noir حفظ نسخة من عنصر Stremio"),
        };
}

/// <summary>
/// A show's Stremio video list, kept for a while so a binge does not fetch Cinemeta once per episode. A miss
/// (an episode newer than the kept list) is fetched again once through <see cref="Forget"/>.
/// </summary>
public sealed class StremioVideoCache(
    Func<string, CancellationToken, Task<IReadOnlyList<StremioVideo>>> inner,
    TimeSpan keep,
    Func<DateTimeOffset>? clock = null
)
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        (DateTimeOffset At, IReadOnlyList<StremioVideo> Videos)
    > kept = new();
    readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<IReadOnlyList<StremioVideo>> Get(string id, CancellationToken ct)
    {
        var at = now();
        if (kept.TryGetValue(id, out var hit) && at - hit.At < keep)
            return hit.Videos;
        var videos = await inner(id, ct);
        kept[id] = (at, videos);
        return videos;
    }

    public void Forget(string id) => kept.TryRemove(id, out _);
}

/// <summary>The "Stremio" provider of the watch-sync queue: one job is one MarkWatched.</summary>
public static class StremioSync
{
    /// <summary>
    /// For <see cref="CommunityQueue.Pass"/>: true when Stremio has the title as watched (marked, added, or it already
    /// was: no write then), false when the switch went off. A site failure (no key, sign-in, network, server)
    /// throws <see cref="SyncSiteException"/> so the whole provider backs off; a title failure throws
    /// <see cref="SyncTitleException"/> so only this title waits. Nothing here ever un-watches.
    /// </summary>
    public static async Task<bool> Send(
        StremioLibraryWriter writer,
        PlayerState state,
        VideoItem item,
        WatchSyncJob job,
        bool arabic,
        CancellationToken ct,
        StremioVideoCache? cache = null
    )
    {
        writer.Options.Enabled = CommunityQueue.Enabled(state, CommunityQueue.Stremio);
        writer.Options.AddWatchedTitles = CommunityQueue.StremioAddTitles(state);
        var result = await writer.MarkWatched(item, job.WatchedAt, ct);
        if (result.Status == StremioMarkStatus.UnknownEpisode && cache != null)
        {
            cache.Forget(item.ImdbId);
            result = await writer.MarkWatched(item, job.WatchedAt, ct);
        }
        return result.Failure switch
        {
            StremioFailure.Site => throw new SyncSiteException(result.Text(arabic)),
            StremioFailure.Title => throw new SyncTitleException(result.Text(arabic)),
            _ => result.Status != StremioMarkStatus.Off,
        };
    }

    /// <summary>A stored job message that needs "Connect Stremio again" (no key, or the sign-in expired).</summary>
    public static bool IsReconnect(string message) =>
        new[] { StremioMarkStatus.NoKey, StremioMarkStatus.Auth }.Any(s =>
            StremioLibraryWriter.Message(s) is var (en, ar) && (message == en || message == ar)
        );

    public static bool NeedsReconnect(PlayerState state) =>
        state.WatchSync.Values.Any(j =>
            j.Provider == CommunityQueue.Stremio && j.SyncedAt == null && IsReconnect(j.Message)
        );

    /// <summary>Titles Noir changed in the Stremio account that can be put back, newest first (one row per show).</summary>
    public static List<(string ImdbId, VideoItem Item)> Restorable(
        PlayerState state,
        string dataDirectory,
        int count = 20
    ) =>
        state
            .WatchSync.Values.Where(j => j.Provider == CommunityQueue.Stremio && j.SyncedAt != null)
            .OrderByDescending(j => j.SyncedAt)
            .Select(j => state.Catalog.GetValueOrDefault(j.ItemId)?.Item)
            .Where(i => i != null && StremioLibraryWriter.OriginalExists(dataDirectory, i.ImdbId))
            .GroupBy(i => i!.ImdbId)
            .Take(count)
            .Select(g => (g.Key, g.First()!))
            .ToList();
}
