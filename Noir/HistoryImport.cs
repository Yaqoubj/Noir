using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public sealed record ImportPlan(
    string Service,
    List<ImportTitle> Titles,
    List<(ImportRow Row, string Reason)> Unmatched
)
{
    public bool LookupFailed { get; init; }
}

public sealed record ImportTitle(
    DiscoverTitle Title,
    List<ImportRow> Rows,
    List<(int Season, int Episode, DateTimeOffset? At, int Rating)> Views
);

public static class HistoryFormats
{
    public static (string Service, List<ImportRow> Rows) Read(string path)
    {
        try
        {
            var files = new List<(string Name, string Text)>();
            var archive = Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);
            if (archive)
            {
                using var zip = ZipFile.OpenRead(path);
                foreach (
                    var entry in zip.Entries.Where(e =>
                        Path.GetExtension(e.Name).ToLowerInvariant() is ".csv" or ".json"
                    )
                )
                {
                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
                    files.Add((entry.Name, reader.ReadToEnd()));
                }
            }
            else if (Path.GetExtension(path).ToLowerInvariant() is ".csv" or ".json")
                files.Add((Path.GetFileName(path), File.ReadAllText(path)));
            var parsed = new List<(string Service, List<ImportRow> Rows, bool Diary)>();
            foreach (var file in files)
            {
                try
                {
                    bool fileDiary = false;
                    var result = Path.GetExtension(file.Name).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                        ? Csv(file.Name, file.Text, out fileDiary)
                        : Json(file.Name, file.Text);
                    if (result.Service.Length > 0)
                        parsed.Add((result.Service, result.Rows, fileDiary));
                }
                catch (Exception e) when (archive && e is JsonException or FormatException) { }
            }
            if (parsed.Count == 0)
                throw new IOException(
                    "No recognizable watch history was found. Choose a viewing-history CSV, JSON or export ZIP."
                );
            var rows = parsed.Where(p => p.Service != "Letterboxd").SelectMany(p => p.Rows).ToList();
            var letterboxd = parsed.Where(p => p.Service == "Letterboxd").ToList();
            var diary = letterboxd.Where(p => p.Diary).SelectMany(p => p.Rows).ToList();
            var other = letterboxd.Where(p => !p.Diary).SelectMany(p => p.Rows).ToList();
            var ratings = other
                .Where(r => r.Rating > 0)
                .GroupBy(MovieKey)
                .ToDictionary(g => g.Key, g => g.First().Rating);
            var present = diary.Select(MovieKey).ToHashSet();
            rows.AddRange(
                diary.Select(r =>
                    r.Rating == 0 && ratings.TryGetValue(MovieKey(r), out var rating) ? r with { Rating = rating } : r
                )
            );
            foreach (var r in other)
                if (present.Add(MovieKey(r)))
                    rows.Add(r with { Rating = r.Rating > 0 ? r.Rating : ratings.GetValueOrDefault(MovieKey(r)) });
            var seen = new HashSet<ImportRow>();
            rows = rows.Where(r => r.Service != "TV Time" || seen.Add(r)).ToList();
            var services = parsed.Select(p => p.Service).Distinct().ToList();
            return (services.Count == 1 ? services[0] : "Mixed history", rows);
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or FormatException)
        {
            throw new IOException(
                "This history export could not be read. Check that it is a complete CSV, JSON or ZIP export.",
                e
            );
        }
    }

    static string MovieKey(ImportRow r) => HistoryData.Normalize(r.Title) + ":" + r.Year;

    static (string Service, List<ImportRow> Rows) Csv(string name, string text, out bool diary)
    {
        diary = false;
        var table = HistoryData.Csv(text);
        if (table.Count == 0)
            return ("", []);
        var headers = table[0].Select(HistoryData.Key).ToList();
        diary =
            Path.GetFileName(name).Equals("diary.csv", StringComparison.OrdinalIgnoreCase)
            || headers.Contains("watcheddate");
        bool Has(params string[] keys) => keys.Any(k => headers.Contains(HistoryData.Key(k)));
        var netflix =
            Has("title")
            && Has("date")
            && (headers.Count == 2 || Regex.IsMatch(name, "(?i)NetflixViewingHistory|ViewingActivity"));
        var letterboxd =
            Has("name", "title")
            && (
                Has("Letterboxd URI")
                || Has("imdbID") && Has("Rewatch")
                || Regex.IsMatch(name, @"(?i)^(diary|watched|ratings)\.csv$")
            );
        var imdb = Has("Const") && Has("Your Rating");
        var noir = Has("date_unknown") && Has("source") && Has("imdb_id");
        var tvtime =
            Has("tv_show_name", "series_name", "show_name", "movie_name")
            && Has(
                "episode_number",
                "number",
                "episode_title",
                "episode_season_number",
                "season_number",
                "season",
                "watched_at",
                "created_at",
                "updated_at"
            );
        if (
            !netflix
            && !letterboxd
            && !imdb
            && !noir
            && !tvtime
            && !(
                Has("title", "name")
                && Has(
                    "imdb",
                    "imdb_id",
                    "const",
                    "date",
                    "watched_at",
                    "watched date",
                    "season",
                    "episode",
                    "episode_title",
                    "type",
                    "year",
                    "rating"
                )
            )
        )
            return ("", []);
        var service =
            netflix ? "Netflix"
            : letterboxd ? "Letterboxd"
            : imdb ? "IMDb"
            : noir ? "Noir"
            : tvtime ? "TV Time"
            : "CSV";
        var data = table.Skip(1).Where(r => r.Any(v => v.Length > 0)).ToList();
        string Get(List<string> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                var i = headers.IndexOf(HistoryData.Key(key));
                if (i >= 0 && i < row.Count && row[i].Length > 0)
                    return row[i].Trim();
            }
            return "";
        }
        var dayFirst = HistoryData.DayFirst(
            data.Select(r => Get(r, "Date", "Watched Date", "watched_at", "created_at", "updated_at", "Date Rated"))
        );
        var rows = new List<ImportRow>();
        foreach (var row in data)
        {
            var title = Get(row, "tv_show_name", "series_name", "show_name", "movie_name", "title", "name");
            var id = Get(row, "imdb_id", "imdb", "imdbID", "const");
            if (title.Length == 0 && id.Length == 0)
                continue;
            var year = HistoryData.Int(Get(row, "year"));
            var season = HistoryData.Int(Get(row, "episode_season_number", "season_number", "season"));
            var episode = HistoryData.Int(Get(row, "episode_number", "episode", "number"));
            var epTitle = Get(row, "episode_title", "episode_name");
            var type = Get(row, "entity_type", "type", "title type").ToLowerInvariant();
            bool? movie =
                type.Contains("movie") || type is "short" or "video" or "tvspecial" ? true
                : type.Contains("episode") || type.Contains("series") || type is "show" or "anime" ? false
                : null;
            if (movie == null && Get(row, "movie_name").Length > 0)
                movie = true;
            if (
                movie == null
                && (season > 0 || episode > 0 || Get(row, "tv_show_name", "series_name", "show_name").Length > 0)
            )
                movie = false;
            var date = Get(
                row,
                "watched_at",
                "Watched Date",
                "WatchedDate",
                "Date Rated",
                "created_at",
                "updated_at",
                "date"
            );
            var at = HistoryData.Date(date, dayFirst);
            var rating = HistoryData.Rating(Get(row, imdb ? "Your Rating" : "Rating"), imdb ? 2 : 1);
            if (netflix)
            {
                (title, movie, season, epTitle) = NetflixTitle(title);
                episode = 0;
            }
            if (letterboxd)
            {
                movie = true;
                if (Path.GetFileName(name).Equals("watched.csv", StringComparison.OrdinalIgnoreCase))
                    at = null;
            }
            if (noir && Get(row, "date_unknown").Equals("true", StringComparison.OrdinalIgnoreCase))
                at = null;
            rows.Add(
                new(
                    service,
                    title,
                    year,
                    movie,
                    id,
                    Get(row, "tvdb_id", "tv_show_id", "series_id", "tvdb"),
                    season,
                    episode,
                    epTitle,
                    at,
                    rating,
                    noir && Get(row, "approximate").Equals("true", StringComparison.OrdinalIgnoreCase),
                    noir ? Get(row, "source") : ""
                )
            );
        }
        return (service, rows);
    }

    static (string Title, bool? Movie, int Season, string EpisodeTitle) NetflixTitle(string value)
    {
        var parts = value.Split(": ");
        for (int i = 1; i < parts.Length; i++)
        {
            // Netflix names some seasons after the show: "Stranger Things: Stranger Things 2: Chapter One: MADMAX".
            var sequel = Regex.Match(parts[i], @"^(.+?)\s+(\d{1,2})$");
            if (sequel.Success && HistoryData.Normalize(sequel.Groups[1].Value) == HistoryData.Normalize(parts[0]))
                return (parts[0], false, int.Parse(sequel.Groups[2].Value), string.Join(": ", parts.Skip(i + 1)));
            var m = Regex.Match(
                parts[i],
                @"(?i)^(?:(?:season|series|part|volume|vol\.?|book|chapter|collection)\s+(\d+|one|two|three|four|five|six|seven|eight|nine|ten)|limited series|miniseries)$"
            );
            if (!m.Success)
                continue;
            var n = m.Groups[1].Value.ToLowerInvariant();
            var words = new[] { "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };
            var season =
                int.TryParse(n, out var number) ? number
                : n.Length == 0 ? 1
                : Array.IndexOf(words, n) + 1;
            return (string.Join(": ", parts.Take(i)), false, season, string.Join(": ", parts.Skip(i + 1)));
        }
        return (value, parts.Length == 1 ? true : null, 0, parts.Length > 1 ? parts[^1] : "");
    }

    static (string Service, List<ImportRow> Rows) Json(string name, string text)
    {
        using var doc = JsonDocument.Parse(text.TrimStart('\uFEFF'));
        var root = doc.RootElement;
        var rows = new List<ImportRow>();
        var array = root.ValueKind == JsonValueKind.Array ? root : HistoryData.Prop(root, "history");
        if (array.ValueKind == JsonValueKind.Array)
        {
            var values = array.EnumerateArray().ToList();
            if (values.Count == 0)
                return (HistoryData.Has(root, "history") ? "Trakt" : "Noir", []);
            if (values.Any(v => HistoryData.Has(v, "imdb_id") || HistoryData.Has(v, "date_unknown")))
            {
                foreach (var v in values)
                    rows.Add(
                        new(
                            "Noir",
                            HistoryData.S(v, "title"),
                            HistoryData.N(v, "year"),
                            HistoryData.S(v, "type") == "movie",
                            HistoryData.S(v, "imdb_id"),
                            "",
                            HistoryData.N(v, "season"),
                            HistoryData.N(v, "episode"),
                            HistoryData.S(v, "episode_title"),
                            HistoryData.S(v, "date_unknown") == "true"
                                ? null
                                : HistoryData.Date(HistoryData.S(v, "watched_at")),
                            HistoryData.Rating(HistoryData.S(v, "rating")),
                            HistoryData.S(v, "approximate") == "true",
                            HistoryData.S(v, "source"),
                            HistoryData.Date(HistoryData.S(v, "logged_at")),
                            HistoryData.S(v, "note"),
                            HistoryData.Rating(HistoryData.S(v, "show_rating"))
                        )
                    );
                return ("Noir", rows);
            }
            if (
                values.Any(v => HistoryData.Has(v, "rated_at"))
                && !values.Any(v => HistoryData.Has(v, "watched_at") || HistoryData.Has(v, "last_watched_at"))
            )
                return ("", []);
            if (
                values.Any(v =>
                    HistoryData.Has(v, "movie") || HistoryData.Has(v, "show") || HistoryData.Has(v, "episode")
                )
            )
            {
                foreach (var v in values)
                    TraktItem(v, rows);
                return ("Trakt", rows);
            }
        }
        if (
            root.ValueKind == JsonValueKind.Object
            && new[] { "movies", "shows", "anime" }.Any(k => HistoryData.Prop(root, k).ValueKind == JsonValueKind.Array)
        )
        {
            var simkl =
                HistoryData.Has(root, "anime")
                || new[] { "movies", "shows" }
                    .SelectMany(k => HistoryData.Array(root, k))
                    .Any(v =>
                        HistoryData.Has(v, "status") || HistoryData.Has(v, "movie") || HistoryData.Has(v, "show")
                    );
            foreach (var movie in HistoryData.Array(root, "movies"))
            {
                if (simkl && !Completed(movie))
                    continue;
                var meta = HistoryData.Has(movie, "movie") ? HistoryData.Prop(movie, "movie") : movie;
                rows.Add(
                    FromMeta(
                        simkl ? "Simkl" : "Trakt",
                        meta,
                        true,
                        0,
                        0,
                        "",
                        HistoryData.Date(HistoryData.S(movie, "watched_at", "last_watched_at"))
                    )
                );
            }
            foreach (var show in new[] { "shows", "anime" }.SelectMany(k => HistoryData.Array(root, k)))
            {
                if (
                    simkl
                    && !Completed(show)
                    && !HistoryData.Array(show, "seasons").Any(s => HistoryData.Array(s, "episodes").Any())
                )
                    continue;
                Show(show, rows, simkl ? "Simkl" : "Trakt");
            }
            return (simkl ? "Simkl" : "Trakt", rows);
        }
        return ("", []);
    }

    static bool Completed(JsonElement v) => HistoryData.S(v, "status").ToLowerInvariant() is "completed" or "watched";

    static ImportRow FromMeta(
        string service,
        JsonElement meta,
        bool movie,
        int season,
        int episode,
        string episodeTitle,
        DateTimeOffset? at
    )
    {
        var ids = HistoryData.Prop(meta, "ids");
        return new(
            service,
            HistoryData.S(meta, "title", "name"),
            HistoryData.N(meta, "year"),
            movie,
            HistoryData.S(ids, "imdb"),
            HistoryData.S(ids, "tvdb"),
            season,
            episode,
            episodeTitle,
            at,
            0
        );
    }

    static void TraktItem(JsonElement v, List<ImportRow> rows)
    {
        var at = HistoryData.Date(HistoryData.S(v, "watched_at", "last_watched_at"));
        if (HistoryData.Has(v, "movie"))
            rows.Add(FromMeta("Trakt", HistoryData.Prop(v, "movie"), true, 0, 0, "", at));
        else if (HistoryData.Has(v, "episode"))
        {
            var ep = HistoryData.Prop(v, "episode");
            rows.Add(
                FromMeta(
                    "Trakt",
                    HistoryData.Prop(v, "show"),
                    false,
                    HistoryData.N(ep, "season"),
                    HistoryData.N(ep, "number"),
                    HistoryData.S(ep, "title"),
                    at
                )
            );
        }
        else if (HistoryData.Has(v, "show"))
            Show(v, rows, "Trakt");
    }

    static void Show(JsonElement v, List<ImportRow> rows, string service)
    {
        var meta = HistoryData.Has(v, "show") ? HistoryData.Prop(v, "show") : v;
        foreach (var season in HistoryData.Array(v, "seasons"))
        foreach (var ep in HistoryData.Array(season, "episodes"))
            rows.Add(
                FromMeta(
                    service,
                    meta,
                    false,
                    HistoryData.N(season, "number"),
                    HistoryData.N(ep, "number"),
                    HistoryData.S(ep, "title"),
                    HistoryData.Date(HistoryData.S(ep, "watched_at", "last_watched_at"))
                        ?? HistoryData.Date(HistoryData.S(v, "watched_at", "last_watched_at"))
                )
            );
    }
}

internal static class HistoryData
{
    internal static string Key(string s) => Regex.Replace(s.TrimStart('\uFEFF').ToLowerInvariant(), @"[^a-z0-9]", "");

    internal static string Normalize(string s)
    {
        s = s.ToLowerInvariant().Replace("&", " and ").Normalize(NormalizationForm.FormD);
        s = new string(s.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        s = Regex.Replace(s, @"[^\p{L}\p{N}]+", " ").Trim();
        s = Regex.Replace(s, @"^the\s+", "");
        return s.Replace(" ", "");
    }

    internal static int Int(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    internal static int Rating(string s, int divisor = 1) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n > 0
            ? Math.Clamp((int)Math.Round(n / divisor, MidpointRounding.AwayFromZero), 1, 5)
            : 0;

    internal static bool DayFirst(IEnumerable<string> dates)
    {
        foreach (var s in dates)
        {
            var m = Regex.Match(s, @"^(\d{1,2})[/.\-](\d{1,2})[/.\-]\d{2,4}$");
            if (!m.Success)
                continue;
            if (Int(m.Groups[1].Value) > 12)
                return true;
            if (Int(m.Groups[2].Value) > 12)
                return false;
        }
        return CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern.StartsWith(
            "d",
            StringComparison.OrdinalIgnoreCase
        );
    }

    internal static DateTimeOffset? Date(string s, bool? dayFirst = null)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        if (long.TryParse(s, out var unix) && unix > 100000000)
        {
            try
            {
                return unix > 100000000000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                    : DateTimeOffset.FromUnixTimeSeconds(unix);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
        var m = Regex.Match(s, @"^(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{2}|\d{4})$");
        if (m.Success)
        {
            int a = Int(m.Groups[1].Value),
                b = Int(m.Groups[2].Value),
                y = Int(m.Groups[3].Value);
            if (y < 100)
                y += y < 50 ? 2000 : 1900;
            var d = dayFirst ?? DayFirst([s]);
            try
            {
                return new(y, d ? b : a, d ? a : b, 0, 0, 0, TimeSpan.Zero);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;
    }

    internal static List<List<string>> Csv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (i == 0 && c == '\uFEFF')
                continue;
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (quoted || field.Length == 0)
                    quoted = !quoted;
                else
                    field.Append(c);
            }
            else if (!quoted && (c == ',' || c == '\r' || c == '\n'))
            {
                row.Add(field.ToString());
                field.Clear();
                if (c != ',')
                {
                    rows.Add(row);
                    row = [];
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                }
            }
            else
                field.Append(c);
        }
        if (quoted)
            throw new FormatException("An exported CSV field has no closing quote.");
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }

    internal static string CsvField(string s) =>
        s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    internal static JsonElement Prop(JsonElement j, string key) =>
        j.ValueKind == JsonValueKind.Object && j.TryGetProperty(key, out var v) ? v : default;

    internal static bool Has(JsonElement j, string key) =>
        j.ValueKind == JsonValueKind.Object && j.TryGetProperty(key, out _);

    internal static string S(JsonElement j, params string[] keys)
    {
        foreach (var key in keys)
        {
            var v = Prop(j, key);
            if (v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                var s = v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText();
                if (s.Length > 0)
                    return s;
            }
        }
        return "";
    }

    internal static int N(JsonElement j, string key) => Int(S(j, key));

    internal static IEnumerable<JsonElement> Array(JsonElement j, string key)
    {
        var v = Prop(j, key);
        return v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];
    }
}

public static class StremioLibrary
{
    public static List<string> DecodeWatched(string watched, List<string> videoIds)
    {
        var last = watched.LastIndexOf(':');
        var previous = last > 0 ? watched.LastIndexOf(':', last - 1) : -1;
        if (previous < 0 || !int.TryParse(watched[(previous + 1)..last], out var length) || length <= 0)
            throw new FormatException("Invalid Stremio watched bitfield.");
        var anchor = videoIds.IndexOf(watched[..previous]);
        if (anchor < 0)
            throw new FormatException("The watched anchor is absent from the video list.");
        using var input = new MemoryStream(Convert.FromBase64String(watched[(last + 1)..]));
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        var bytes = output.ToArray();
        long offset = (long)length - anchor - 1;
        var result = new List<string>();
        for (int i = 0; i < videoIds.Count; i++)
        {
            var bit = i + offset;
            if (bit >= 0 && bit / 8 < bytes.Length && ((bytes[bit / 8] >> (int)(bit % 8)) & 1) == 1)
                result.Add(videoIds[i]);
        }
        return result;
    }

    public static async Task<List<ImportRow>> Fetch(
        string authKey,
        HttpClient http,
        Func<string, CancellationToken, Task<List<string>>> videoIds,
        CancellationToken ct
    )
    {
        using var body = new StringContent(
            JsonSerializer.Serialize(
                new
                {
                    authKey,
                    collection = "libraryItem",
                    ids = System.Array.Empty<string>(),
                    all = true,
                }
            ),
            Encoding.UTF8,
            "application/json"
        );
        using var response = await http.PostAsync("https://api.strem.io/api/datastoreGet", body, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var rows = new List<ImportRow>();
        if (HistoryData.Prop(doc.RootElement, "result").ValueKind != JsonValueKind.Array)
            throw new IOException("Stremio did not return a library. Check the authentication key.");
        foreach (var item in HistoryData.Array(doc.RootElement, "result"))
        {
            ct.ThrowIfCancellationRequested();
            if (HistoryData.S(item, "temp").Equals("true", StringComparison.OrdinalIgnoreCase))
                continue;
            var id = HistoryData.S(item, "_id");
            if (!Regex.IsMatch(id, @"^tt\d+$"))
                continue;
            var state = HistoryData.Prop(item, "state");
            var type = HistoryData.S(item, "type");
            var title = HistoryData.S(item, "name");
            var at = HistoryData.Date(HistoryData.S(state, "lastWatched"));
            var flagged =
                HistoryData.N(state, "timesWatched") > 0
                || HistoryData.N(state, "flaggedWatched") > 0
                || HistoryData.S(state, "flaggedWatched") == "true";
            if (type == "movie")
            {
                if (flagged)
                    rows.Add(new("Stremio", title, 0, true, id, "", 0, 0, "", at, 0));
                continue;
            }
            if (type != "series")
                continue;
            var current = HistoryData.S(state, "video_id");
            var encoded = HistoryData.S(state, "watched");
            var ids = new List<string>();
            bool failed = false;
            if (encoded.Length > 0)
                try
                {
                    ids = DecodeWatched(encoded, await videoIds(id, ct));
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    failed = true;
                }
            else
                failed = true;
            // The last opened episode may be half-watched; only trust it when Stremio counted a finished watch.
            if (failed && current.Length > 0 && flagged)
                ids.Add(current);
            foreach (var video in ids.Distinct())
            {
                var m = Regex.Match(video, @"^" + Regex.Escape(id) + @":(\d+):(\d+)$");
                if (m.Success)
                    rows.Add(
                        new(
                            "Stremio",
                            title,
                            0,
                            false,
                            id,
                            "",
                            HistoryData.Int(m.Groups[1].Value),
                            HistoryData.Int(m.Groups[2].Value),
                            "",
                            video == current ? at : null,
                            0
                        )
                    );
            }
        }
        return rows;
    }
}

public sealed class HistoryImporter(LibraryStore store, DiscoveryClient discovery, HttpClient? lookupHttp = null)
{
    static readonly HttpClient defaultLookup = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly ConcurrentDictionary<string, Lazy<Task<string>>> tvdb = new();

    async Task<string> Imdb(string id, CancellationToken ct)
    {
        try
        {
            return await tvdb.GetOrAdd(id, key => new(async () =>
                {
                    using var response = await (lookupHttp ?? defaultLookup).GetAsync(
                        "https://api.tvmaze.com/lookup/shows?thetvdb=" + Uri.EscapeDataString(key),
                        ct
                    );
                    response.EnsureSuccessStatusCode();
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    return HistoryData.S(HistoryData.Prop(doc.RootElement, "externals"), "imdb");
                })).Value;
        }
        catch
        {
            tvdb.TryRemove(id, out _);
            throw;
        }
    }

    public async Task<ImportPlan> Resolve(
        string service,
        List<ImportRow> rows,
        IProgress<(int Done, int Total)>? progress,
        CancellationToken ct
    )
    {
        var groups = rows.GroupBy(r =>
                (
                    Identity: Regex.IsMatch(r.ImdbId, @"^tt\d+$") ? r.ImdbId
                    : r.TvdbId.Length > 0 ? "tvdb:" + r.TvdbId
                    : HistoryData.Normalize(r.Title) + ":" + r.Year,
                    r.IsMovie
                )
            )
            .ToList();
        using var slots = new SemaphoreSlim(4);
        int done = 0;
        var progressGate = new object();
        var results = await Task.WhenAll(
            groups.Select(async group =>
            {
                await slots.WaitAsync(ct);
                var titles = new List<ImportTitle>();
                var unmatched = new List<(ImportRow Row, string Reason)>();
                var grouped = group.ToList();
                var lookupFailed = false;
                try
                {
                    var row = grouped[0];
                    var id = Regex.IsMatch(row.ImdbId, @"^tt\d+$") ? row.ImdbId : "";
                    if (id.Length == 0 && row.TvdbId.Length > 0)
                        try
                        {
                            id = await Imdb(row.TvdbId, ct);
                        }
                        catch (Exception) when (!ct.IsCancellationRequested)
                        {
                            lookupFailed = true;
                        }
                    DiscoverTitle? details = null;
                    foreach (var movie in row.IsMovie.HasValue ? new[] { row.IsMovie.Value } : new[] { true, false })
                    {
                        ct.ThrowIfCancellationRequested();
                        DiscoverTitle? match = null;
                        if (Regex.IsMatch(id, @"^tt\d+$"))
                            match = new()
                            {
                                Id = id,
                                Type = movie ? "movie" : "series",
                                Name = row.Title,
                            };
                        else
                        {
                            var query = !movie && row.IsMovie == null ? row.Title.Split(": ")[0] : row.Title;
                            var found = await discovery.Search(query, movie ? "Movies" : "TV shows", ct);
                            lookupFailed |= found.Message.Length > 0;
                            var wanted = HistoryData.Normalize(query);
                            bool YearFits(DiscoverTitle t) =>
                                row.Year == 0 || t.Year == 0 || Math.Abs(t.Year - row.Year) <= 1;
                            var exact = found
                                .Titles.Where(t =>
                                    t.IsMovie == movie && HistoryData.Normalize(t.Name) == wanted && YearFits(t)
                                )
                                .OrderBy(t => row.Year == 0 ? 0 : Math.Abs(t.Year - row.Year))
                                .ToList();
                            // Without a year, two remakes with the same title ("Arrival" 1996/2016) are a guess: leave it for manual matching.
                            match =
                                row.Year == 0 && exact.Select(t => t.Year).Distinct().Count() > 1
                                    ? null
                                    : exact.FirstOrDefault();
                            if (match == null && exact.Count > 1)
                                continue;
                            // Services add or drop subtitles ("The Office (US)", "Narcos: Mexico"); accept a near match only when it's the only one.
                            if (match == null && wanted.Length >= 4)
                            {
                                var near = found
                                    .Titles.Where(t =>
                                        t.IsMovie == movie
                                        && YearFits(t)
                                        && HistoryData.Normalize(t.Name) is { Length: > 0 } n
                                        && (n.StartsWith(wanted) || wanted.StartsWith(n))
                                        && Math.Min(n.Length, wanted.Length) >= .6 * Math.Max(n.Length, wanted.Length)
                                    )
                                    .ToList();
                                if (near.Count == 1)
                                    match = near[0];
                            }
                            // "Glass Onion: A Knives Out Mystery" is "Glass Onion" in Cinemeta: accept the main title alone when it's unique.
                            if (match == null && query.Contains(": "))
                            {
                                var head = HistoryData.Normalize(query.Split(": ")[0]);
                                var main = found
                                    .Titles.Where(t =>
                                        t.IsMovie == movie && YearFits(t) && HistoryData.Normalize(t.Name) == head
                                    )
                                    .ToList();
                                if (head.Length >= 4 && main.Count == 1)
                                    match = main[0];
                            }
                        }
                        if (match == null)
                            continue;
                        try
                        {
                            details = await discovery.Details(match, ct);
                            break;
                        }
                        catch (Exception) when (!ct.IsCancellationRequested)
                        {
                            lookupFailed = true;
                        }
                    }
                    if (details == null)
                    {
                        unmatched.AddRange(
                            grouped.Select(r =>
                                (
                                    r,
                                    lookupFailed
                                        ? "Title lookup failed. Try again later."
                                        : "No matching title or title details were found."
                                )
                            )
                        );
                        return (titles, unmatched, lookupFailed);
                    }
                    lookupFailed = false;
                    var accepted = new List<ImportRow>();
                    var views = new List<(int Season, int Episode, DateTimeOffset? At, int Rating)>();
                    foreach (var r in grouped)
                    {
                        if (details.IsMovie)
                        {
                            accepted.Add(r);
                            views.Add((0, 0, r.WatchedAt, r.Rating));
                            continue;
                        }
                        var ep = Episode(details, r);
                        if (ep == null)
                        {
                            unmatched.Add((r, "No matching episode was found in the show's metadata."));
                            continue;
                        }
                        accepted.Add(r);
                        views.Add((ep.Season, ep.Episode, r.WatchedAt, r.Rating));
                    }
                    if (views.Count > 0)
                        titles.Add(new(details, accepted, views));
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    lookupFailed = true;
                    unmatched.AddRange(grouped.Select(r => (r, "Could not resolve title: " + e.Message)));
                }
                finally
                {
                    slots.Release();
                    lock (progressGate)
                        progress?.Report((++done, groups.Count));
                }
                return (titles, unmatched, lookupFailed);
            })
        );
        ct.ThrowIfCancellationRequested();
        return new(service, results.SelectMany(r => r.titles).ToList(), results.SelectMany(r => r.unmatched).ToList())
        {
            LookupFailed = results.Any(r => r.lookupFailed),
        };
    }

    static KnownEpisode? Episode(DiscoverTitle title, ImportRow row)
    {
        if (row.Season > 0 && row.Episode > 0)
            return title.Episodes.FirstOrDefault(e => e.Season == row.Season && e.Episode == row.Episode);
        var candidates = title.Episodes.Where(e => row.Season == 0 || e.Season == row.Season).ToList();
        var name = HistoryData.Normalize(row.EpisodeTitle);
        KnownEpisode? Named(IEnumerable<KnownEpisode> episodes) =>
            name.Length == 0 ? null : episodes.FirstOrDefault(e => HistoryData.Normalize(e.Title) == name);
        var found = Named(candidates) ?? Named(title.Episodes);
        if (found != null)
            return found;
        // Netflix drops prefixes like "Chapter One:"; a unique containment match is still safe.
        if (name.Length >= 5)
            foreach (var pool in new[] { candidates, title.Episodes })
            {
                var near = pool.Where(e =>
                        HistoryData.Normalize(e.Title) is { Length: >= 5 } t && (t.Contains(name) || name.Contains(t))
                    )
                    .ToList();
                if (near.Count == 1)
                    return near[0];
            }
        var number = Regex.Match(row.EpisodeTitle, @"(?i)^(?:episode|chapter)\s+(\d+)\s*$");
        var n =
            row.Episode > 0 ? row.Episode
            : number.Success ? HistoryData.Int(number.Groups[1].Value)
            : 0;
        var numbered = candidates.Where(e => e.Episode == n && n > 0).ToList();
        return numbered.Count == 1 ? numbered[0] : null;
    }

    public int Apply(ImportPlan plan)
    {
        int added = 0;
        var id = store.BeginImport(plan.Service);
        using (store.Batch())
            foreach (var title in plan.Titles)
            {
                var items = store.AddDiscovered(title.Title, false);
                var entries = new List<LogEntry>();
                for (var n = 0; n < title.Views.Count; n++)
                {
                    var view = title.Views[n];
                    var item = items.FirstOrDefault(i =>
                        title.Title.IsMovie ? i.IsMovie : i.Season == view.Season && i.Episode == view.Episode
                    );
                    if (item == null)
                        continue;
                    // Rows line up with views for file imports; an approximate date stays approximate.
                    var row = title.Rows.Count == title.Views.Count ? title.Rows[n] : null;
                    entries.Add(
                        Entry(
                            item,
                            view.At ?? store.AirDate(item) ?? DateTimeOffset.UtcNow,
                            view.At == null,
                            view.Rating,
                            row
                        )
                    );
                }
                added += store.LogWatched(
                    entries,
                    "Imported from " + plan.Service,
                    queueSync: false,
                    exact: true,
                    batch: id
                );
                KeepExtras(store, items, title);
            }
        store.EndImport(id);
        return added;
    }

    internal static LogEntry Entry(VideoItem item, DateTimeOffset at, bool dateUnknown, int rating, ImportRow? row) =>
        new(
            item,
            at,
            dateUnknown,
            rating,
            row?.Approximate == true,
            row?.Source ?? "",
            row?.Source.Length > 0 ? row.LoggedAt : null
        );

    // Notes and show ratings from a Noir export fill gaps; they never overwrite what is already here.
    internal static void KeepExtras(LibraryStore store, List<VideoItem> items, ImportTitle title)
    {
        var changed = false;
        for (var n = 0; n < title.Views.Count && title.Rows.Count == title.Views.Count; n++)
        {
            var row = title.Rows[n];
            var view = title.Views[n];
            var item = items.FirstOrDefault(i =>
                title.Title.IsMovie ? i.IsMovie : i.Season == view.Season && i.Episode == view.Episode
            );
            if (item == null)
                continue;
            if (
                row.Note.Length > 0
                && store.State.PersonalReviews.GetValueOrDefault(item.Id) is not { Note.Length: > 0 }
            )
            {
                var review = store.State.PersonalReviews.GetValueOrDefault(item.Id) ?? new();
                review.Note = row.Note;
                store.State.PersonalReviews[item.Id] = review;
                changed = true;
            }
            if (row.ShowRating > 0 && !store.State.ShowRatings.ContainsKey(item.GroupKey))
            {
                store.RateShow(item.GroupKey, row.ShowRating);
            }
        }
        if (changed)
            store.Save();
    }
}
