using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public sealed record SubtitleCandidate(
    string Id,
    string Language,
    string Name,
    string Release,
    string Url,
    bool HashMatch,
    int Score,
    SubtitleProviderReference? Provider = null,
    string Uploader = ""
);

public sealed record SubtitleAsset(string Language, string Name, string Url);

public sealed class SubtitleDownloads
{
    readonly HttpClient http;
    readonly string backupDirectory;
    readonly Func<SubtitleSelection> source;
    readonly SubtitleProviders providers;
    readonly SemaphoreSlim work = new(1, 1);
    public static string ToolRoot = Path.Combine(AppContext.BaseDirectory, "Tools", "subtitle-sync");

    public SubtitleDownloads(string profile, HttpClient? http = null, Func<SubtitleSelection>? source = null)
    {
        this.source = source ?? (() => new());
        this.http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        providers = new(http);
        backupDirectory = Path.Combine(profile, "subtitle-originals");
        Directory.CreateDirectory(backupDirectory);
    }

    // A preference, never a filter: matching uploaders (case-insensitive) sort first, everything else follows in its own order.
    public static bool PrefersUploader(SubtitleCandidate candidate, string uploader) =>
        uploader.Trim().Length > 0 && candidate.Uploader.Equals(uploader.Trim(), StringComparison.OrdinalIgnoreCase);

    // A refused timing check whose raw result is this far from zero belongs to some other video, not a slightly different encode.
    public static bool FarOff(double offset, double scale) =>
        !double.IsFinite(offset) || !double.IsFinite(scale) || Math.Abs(offset) > 10 || Math.Abs(scale - 1) > .003;

    public static string Language(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "eng" or "en" or "english" => "en",
            "ara" or "ar" or "arabic" => "ar",
            _ => "",
        };

    public static string SidecarLanguage(string name)
    {
        var m = Regex.Match(name, @"(?i)(?:^|[./\\ _-])(eng|english|en|ara|arabic|ar)(?:[./\\ _-]|$)");
        return Language(m.Groups[1].Value);
    }

    public static bool TextSubtitle(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".srt" or ".ass" or ".ssa" or ".vtt";

    public static async Task<string> VideoHash(string path, CancellationToken ct)
    {
        await using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, true);
        if (f.Length < 131072)
            return "";
        ulong hash = (ulong)f.Length;
        var bytes = new byte[65536];
        foreach (var offset in new[] { 0L, f.Length - 65536 })
        {
            f.Position = offset;
            await f.ReadExactlyAsync(bytes, ct);
            for (int i = 0; i < bytes.Length; i += 8)
                hash = unchecked(hash + BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(i, 8)));
        }
        return hash.ToString("x16");
    }

    public static int ReleaseScore(string subtitle, string release)
    {
        static HashSet<string> Words(string s) =>
            Regex
                .Matches(s.ToLowerInvariant(), @"[a-z0-9]{2,}")
                .Select(m => m.Value)
                .Where(s => s is not ("mkv" or "mp4" or "srt" or "en" or "ar" or "eng" or "ara"))
                .ToHashSet();
        var a = Words(subtitle);
        var b = Words(release);
        return b.Count == 0 ? 0 : (int)(100d * a.Intersect(b).Count() / b.Count);
    }

    // A stable release key survives expiring stream links and keeps different encodes apart.
    public static VideoItem StreamTarget(string profile, VideoItem item, string release)
    {
        var key = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    item.Id + ":" + item.ImdbId + ":" + item.Season + ":" + item.Episode + ":" + release
                )
            )
        );
        return new VideoItem { Id = item.Id, Path = Path.Combine(profile, "stream-subtitles", key, "video.mkv") };
    }

    public Task<List<SubtitleCandidate>> Search(
        VideoItem item,
        string release,
        CancellationToken ct,
        bool hashLocal = true
    ) => Search(item, release, ct, hashLocal, source());

    async Task<List<SubtitleCandidate>> Search(
        VideoItem item,
        string release,
        CancellationToken ct,
        bool hashLocal,
        SubtitleSelection settings,
        bool secure = false,
        List<string>? languages = null,
        Dictionary<string, SubtitleSourceException>? failures = null
    )
    {
        var imdb = item.ImdbId;
        if (!Regex.IsMatch(imdb, @"^tt\d+$"))
            return [];
        var hash = hashLocal && File.Exists(item.Path) ? await VideoHash(item.Path, ct) : "";
        if (settings.Chosen)
        {
            languages ??= ["en", "ar"];
            List<SubtitleCandidate> chosen = [];
            try
            {
                chosen = await providers.Search(settings, item, release, languages, hash, ct, failures);
            }
            catch (SubtitleSourceException) when (!settings.Only) { }
            if (settings.Only || languages.All(l => chosen.Any(c => c.Language == l)))
                return chosen;
            try
            {
                var fallback = await Search(item, release, ct, hashLocal, new(), true);
                return chosen
                    .Concat(
                        fallback.Where(c =>
                            languages.Contains(c.Language) && !chosen.Any(x => x.Language == c.Language)
                        )
                    )
                    .Take(500)
                    .ToList();
            }
            catch (IOException) when (chosen.Count > 0)
            {
                return chosen;
            }
        }
        var id = item.IsMovie ? imdb : $"{imdb}:{item.Season}:{item.Episode}";
        var extra = "filename=" + Uri.EscapeDataString(Path.GetFileName(release));
        if (hash.Length > 0)
            extra += "&videoHash=" + hash + "&videoSize=" + new FileInfo(item.Path).Length;
        var result = new List<SubtitleCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var succeeded = false;
        var sources = SubtitleAddon.Sources("", true);
        foreach (var baseUrl in sources)
        {
            try
            {
                var url =
                    baseUrl
                    + "/subtitles/"
                    + (item.IsMovie ? "movie/" : "series/")
                    + Uri.EscapeDataString(id)
                    + "/"
                    + extra
                    + ".json";
                byte[] bytes;
                if (secure)
                    bytes = await providers.File(settings, url, ct);
                else
                {
                    using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();
                    bytes = await Limited(response.Content, 4 * 1024 * 1024, ct);
                }
                using var doc = JsonDocument.Parse(bytes);
                succeeded = true;
                if (
                    doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("subtitles", out var all)
                    || all.ValueKind != JsonValueKind.Array
                )
                    continue;
                foreach (var s in all.EnumerateArray().Take(500))
                {
                    if (s.ValueKind != JsonValueKind.Object)
                        continue;
                    string Text(string k) =>
                        s.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String
                            ? v.GetString() ?? ""
                            : "";
                    var language = Language(Text("lang"));
                    if (language.Length == 0)
                        continue;
                    if (
                        !item.IsMovie
                        && (
                            (
                                s.TryGetProperty("season", out var season)
                                && season.ValueKind == JsonValueKind.Number
                                && season.TryGetInt32(out var sn)
                                && sn != item.Season
                            )
                            || (
                                s.TryGetProperty("episode", out var ep)
                                && ep.ValueKind == JsonValueKind.Number
                                && ep.TryGetInt32(out var en)
                                && en != item.Episode
                            )
                        )
                    )
                        continue;
                    var link = Text("url");
                    if (!(secure ? SubtitleTransport.ValidUrl(link) : ValidUrl(link)) || !seen.Add(link))
                        continue;
                    var name = Text("subtitleFileName");
                    var rel = Text("movieReleaseName");
                    var exact = Text("m") == "h";
                    result.Add(
                        new(
                            Text("id"),
                            language,
                            name,
                            rel,
                            link,
                            exact,
                            (exact ? 1000 : 0) + ReleaseScore(rel + " " + name, release)
                        )
                    );
                }
            }
            catch (Exception e)
                when (e is HttpRequestException or JsonException or IOException
                    || e is OperationCanceledException && !ct.IsCancellationRequested
                )
            {
                if (sources.Count == 1)
                    throw new IOException("Subtitle lookup unavailable.");
            }
        }
        if (!succeeded)
            throw new IOException("Subtitle lookup unavailable.");
        return result.OrderByDescending(s => s.Score).Take(500).ToList();
    }

    public async Task<string> Fetch(
        VideoItem item,
        string release,
        List<string> languages,
        bool autoSync,
        CancellationToken ct,
        List<SubtitleAsset>? pack = null,
        bool skipEmbedded = false,
        VideoItem? streamTarget = null,
        bool replace = false
    )
    {
        await work.WaitAsync(ct);
        try
        {
            if (streamTarget == null && !File.Exists(item.Path))
                throw new IOException("Open a stream or save the video before downloading subtitles.");
            var target = streamTarget ?? item;
            if (streamTarget != null)
                Directory.CreateDirectory(Path.GetDirectoryName(target.Path)!);
            var settings = source();
            var requested = languages.Select(Language).Where(l => l.Length > 0).Distinct().ToList();
            var messages = new List<string>();
            var embedded =
                streamTarget == null && skipEmbedded && requested.Count > 0
                    ? await EmbeddedSubtitles.Probe(item.Path, ct)
                    : [];
            List<SubtitleCandidate>? found = null;
            bool searched = false;
            SubtitleSourceException? lookupFailure = null;
            var lookupFailures = new Dictionary<string, SubtitleSourceException>();
            foreach (var lang in requested)
            {
                ct.ThrowIfCancellationRequested();
                var label = lang == "ar" ? "Arabic" : "English";
                var existing = MobileSubtitles
                    .Find(target, requireVideo: streamTarget == null)
                    .Where(s => SidecarLanguage(s.Path) == lang)
                    .Select(s => s.Path)
                    .ToList();
                if (existing.Count > 0 && !replace)
                {
                    messages.Add(label + " already saved");
                    continue;
                }
                if (embedded.Contains(lang))
                {
                    messages.Add(
                        label
                            + " built into the video"
                            + (autoSync ? " · " + await SyncEmbedded(item.Path, lang, ct) : "")
                    );
                    continue;
                }
                var bundled = settings.Chosen ? null : pack?.FirstOrDefault(s => s.Language == lang);
                SubtitleCandidate candidate;
                var options = new List<SubtitleCandidate>();
                if (bundled != null)
                    candidate = new("", lang, bundled.Name, "", bundled.Url, true, 0);
                else
                {
                    try
                    {
                        if (!searched)
                        {
                            searched = true;
                            found = await Search(
                                item,
                                release,
                                ct,
                                streamTarget == null,
                                settings,
                                languages: requested,
                                failures: lookupFailures
                            );
                        }
                        if (found == null)
                        {
                            messages.Add(lookupFailure?.Message ?? label + " lookup unavailable");
                            continue;
                        }
                    }
                    catch (SubtitleSourceException e)
                    {
                        lookupFailure = e;
                        messages.Add(e.Message);
                        continue;
                    }
                    catch (Exception e)
                        when (e is HttpRequestException or JsonException or IOException
                            || e is OperationCanceledException && !ct.IsCancellationRequested
                        )
                    {
                        messages.Add(label + " lookup unavailable");
                        continue;
                    }
                    options = found.Where(s => s.Language == lang).ToList();
                    if (options.Count == 0)
                    {
                        messages.Add(
                            lookupFailures.TryGetValue(lang, out var failure) ? failure.Message
                            : settings.Chosen
                                ? settings.Name + " · " + SubtitleWords.T("no " + label + " subtitles found")
                            : label + " not available"
                        );
                        continue;
                    }
                    candidate = options[0];
                }
                try
                {
                    var used = candidate;
                    var fellBack = false;
                    Obtained first;
                    try
                    {
                        first = await Obtain(candidate, settings, ct);
                    }
                    catch (SubtitleSourceException) when (settings.Chosen && !settings.Only)
                    {
                        var fallback = await Search(item, release, ct, streamTarget == null, new(), true);
                        var best = fallback.FirstOrDefault(s => s.Language == lang);
                        if (best == null)
                            throw;
                        first = Ready(await providers.File(settings, best.Url, ct), best);
                        used = best;
                        fellBack = true;
                    }
                    var destination = Path.Combine(
                        Path.GetDirectoryName(target.Path)!,
                        Path.GetFileNameWithoutExtension(target.Path) + "." + lang + first.Ext
                    );
                    // The saved subtitle is moved aside only once its replacement is written beside the video;
                    // if the new file cannot be put in place, the old ones move back.
                    await WriteNew(destination, first.Text, ct, replace ? existing : null);
                    string timing;
                    SyncResult? checkedTiming = null;
                    if (autoSync && !first.Exact && streamTarget == null)
                    {
                        checkedTiming = await SyncDetailed(item.Path, destination, ct);
                        timing = checkedTiming.Value.Text;
                    }
                    else
                        timing = first.Exact
                            ? "release matched"
                            : "timing unchanged · adjust in Subtitles → Timing if needed";
                    var doubtful = false;
                    // A refusal that is far off means this subtitle probably belongs to another video: one more candidate, then stop.
                    if (checkedTiming is { Far: true } && !fellBack && bundled == null && options.Count > 1)
                    {
                        var folder = Path.Combine(
                            Path.GetTempPath(),
                            "noir-subtitle-alt-" + Guid.NewGuid().ToString("N")
                        );
                        var alt = Path.Combine(folder, Path.GetFileName(destination));
                        try
                        {
                            var second = await Obtain(options[1], settings, ct);
                            Directory.CreateDirectory(folder);
                            await File.WriteAllTextAsync(alt, second.Text, new UTF8Encoding(false), ct);
                            var other = second.Exact
                                ? new SyncResult("release matched", true, 0, 1)
                                : await SyncDetailed(item.Path, alt, ct);
                            if (other.Accepted || other.Close)
                            {
                                ReplaceSubtitle(alt, destination);
                                var altBackup = BackupFor(alt);
                                if (File.Exists(altBackup))
                                    File.Move(altBackup, BackupFor(destination), true);
                                used = options[1];
                                timing = other.Text;
                            }
                            else
                                doubtful = true;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception e)
                            when (e
                                    is IOException
                                        or HttpRequestException
                                        or InvalidDataException
                                        or UnauthorizedAccessException
                                || e is OperationCanceledException
                            )
                        {
                            doubtful = true;
                        }
                        finally
                        {
                            try
                            {
                                if (File.Exists(BackupFor(alt)))
                                    File.Delete(BackupFor(alt));
                                if (Directory.Exists(folder))
                                    Directory.Delete(folder, true);
                            }
                            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                        }
                    }
                    else if (checkedTiming is { Far: true })
                        doubtful = true;
                    messages.Add(
                        SavedFrom(label, bundled != null, used, settings, fellBack)
                            + " · "
                            + timing
                            + (doubtful ? " · may not match this video · try Replace" : "")
                    );
                }
                catch (SubtitleSourceException e)
                {
                    messages.Add(e.Message);
                }
                catch (Exception e)
                    when (e is IOException or HttpRequestException or InvalidDataException
                        || e is OperationCanceledException && !ct.IsCancellationRequested
                    )
                {
                    messages.Add(
                        settings.Chosen
                            ? settings.Name + " · " + SubtitleWords.T("temporarily unavailable")
                            : label + " download unavailable"
                    );
                }
            }
            return string.Join(" · ", messages);
        }
        finally
        {
            work.Release();
        }
    }

    internal readonly record struct SyncResult(string Text, bool Accepted, double? RawOffset, double RawScale)
    {
        // Refused, and the raw result says another video (or a badly different cut).
        public bool Far => !Accepted && RawOffset is { } o && FarOff(o, RawScale);

        // Refused only because the match was not confident, but the raw offset and scale are small.
        public bool Close => !Accepted && RawOffset is { } o && !FarOff(o, RawScale);
    }

    readonly record struct Obtained(string Text, string Ext, bool Exact, SubtitleCandidate Used);

    static Obtained Ready(byte[] data, SubtitleCandidate candidate, string? named = null)
    {
        var ext = Path.GetExtension(named ?? candidate.Name).ToLowerInvariant();
        if (!TextSubtitle(named ?? candidate.Name))
            ext = ".srt";
        var text = Decode(data);
        if (!ValidText(text, ext))
            throw new IOException("Invalid subtitle response");
        return new(text, ext, candidate.HashMatch, candidate);
    }

    async Task<Obtained> Obtain(SubtitleCandidate candidate, SubtitleSelection settings, CancellationToken ct)
    {
        if (candidate.Provider != null)
        {
            var downloaded = await providers.Download(candidate, ct);
            return Ready(downloaded.Bytes, candidate, TextSubtitle(downloaded.Name) ? downloaded.Name : null);
        }
        return Ready(
            settings.Chosen ? await providers.File(settings, candidate.Url, ct) : await Download(candidate.Url, ct),
            candidate
        );
    }

    // "English saved from SubDL by Alice", "SubDL had no Arabic · saved from the free default", "English saved from the release".
    static string SavedFrom(string label, bool pack, SubtitleCandidate used, SubtitleSelection settings, bool fellBack)
    {
        if (pack)
            return label + " saved from the release";
        if (used.Provider != null)
            return label
                + " saved from "
                + used.Provider.Selection.Name
                + (used.Uploader.Length > 0 ? " by " + used.Uploader : "");
        return settings.Chosen
            ? settings.Name + (fellBack ? " couldn't provide " : " had no ") + label + " · saved from the free default"
            : label + " saved from the free default";
    }

    string BackupFor(string subtitle) =>
        Path.Combine(
            backupDirectory,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(subtitle)))
                + Path.GetExtension(subtitle).ToLowerInvariant()
        );

    // Replace saved subtitles: the old files (and their auto-sync originals) go to the same backup folder, never deleted.
    List<(string From, string To)> KeepReplaced(IEnumerable<string> saved, string destination)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var old = saved.ToList();
        var moved = new List<(string From, string To)>();
        foreach (
            var path in old.Append(destination).Concat(old.Select(BackupFor)).Append(BackupFor(destination)).Distinct()
        )
            if (File.Exists(path))
            {
                var to = Path.Combine(
                    backupDirectory,
                    "replaced-" + stamp + "-" + Guid.NewGuid().ToString("N")[..6] + "-" + Path.GetFileName(path)
                );
                File.Move(path, to, false);
                moved.Add((path, to));
            }
        return moved;
    }

    async Task WriteNew(string destination, string text, CancellationToken ct, IEnumerable<string>? replacing = null)
    {
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false), ct);
        List<(string From, string To)> moved = [];
        try
        {
            if (replacing != null)
                moved = KeepReplaced(replacing, destination);
            File.Move(temp, destination, false);
            moved.Clear();
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
            for (var n = moved.Count - 1; n >= 0; n--)
                if (!File.Exists(moved[n].From))
                    File.Move(moved[n].To, moved[n].From, false);
        }
    }

    public async Task<string> Sync(string video, string subtitle, CancellationToken ct) =>
        (await SyncDetailed(video, subtitle, ct)).Text;

    internal async Task<SyncResult> SyncDetailed(string video, string subtitle, CancellationToken ct)
    {
        if (!File.Exists(video) || !File.Exists(subtitle))
            throw new IOException("Save the video and subtitles first.");
        var (message, offset, _, rawOffset, rawScale) = await Align(video, subtitle, ct, BackupFor(subtitle));
        return offset == null
            ? new(message, false, rawOffset, rawScale)
            : new($"auto-synced ({offset:+0.00;-0.00;0.00}s)", true, offset, rawScale);
    }

    // Subtitles built into the video can be off too; check a copy and keep it beside the video only when it moved.
    public async Task<string> SyncEmbedded(string video, string language, CancellationToken ct)
    {
        if (
            !File.Exists(Path.Combine(ToolRoot, "NoirSubtitleSync.exe"))
            || !File.Exists(Path.Combine(ToolRoot, "ffmpeg", "ffmpeg.exe"))
        )
            return "automatic timing tool unavailable";
        var track = (await EmbeddedSubtitles.ProbeTextTracks(video, ct)).FirstOrDefault(t => t.Language == language);
        if (track.Extension == null)
            return "picture subtitles · timing can't be checked";
        var folder = Path.Combine(Path.GetTempPath(), "noir-embedded-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var extracted = Path.Combine(folder, "track" + track.Extension);
            if (!await EmbeddedSubtitles.Extract(video, track.Index, extracted, ct))
                return "timing check unavailable";
            var (message, offset, scale, _, _) = await Align(video, extracted, ct, null, audio: true);
            if (offset == null)
                return message == "timing uncertain · original kept"
                    ? "timing uncertain · built-in track kept"
                    : message;
            if (Math.Abs(offset.Value) < .25 && Math.Abs(scale - 1) < .001)
                return "timing matches the video";
            var copy = SyncedCopy(video, language, track.Extension);
            if (File.Exists(copy))
                ReplaceSubtitle(extracted, copy);
            else
                File.Move(extracted, copy);
            return $"auto-synced copy saved ({offset:+0.00;-0.00;0.00}s)";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "timing check unavailable";
        }
        finally
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // Stream Auto-sync: a few short audio samples through a loopback proxy, never the whole film. Test seam: replaces the tool process.
    internal static Func<
        IReadOnlyList<string>,
        CancellationToken,
        Task<(int Exit, string Json)>
    >? ToolOverride { get; set; }
    static readonly System.Text.Json.JsonSerializerOptions Plain = new() { WriteIndented = false };

    async Task<(int Exit, string Json)> RunTool(IReadOnlyList<string> arguments, TimeSpan limit, CancellationToken ct)
    {
        if (ToolOverride != null)
            return await ToolOverride(arguments, ct);
        var tool = Path.Combine(ToolRoot, "NoirSubtitleSync.exe");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        var start = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(tool)!,
        };
        foreach (var a in arguments)
            start.ArgumentList.Add(a);
        using var process = new Process { StartInfo = start };
        process.Start();
        using var reaper = timeout.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var json = await stdout;
        await stderr;
        return (process.ExitCode, json);
    }

    static string StreamFailure(SampleFailure kind) =>
        kind switch
        {
            SampleFailure.TimedOut => "timing check timed out · original kept",
            SampleFailure.Unavailable => "couldn't read the stream · original kept",
            _ => StreamSampler.Message(kind) + " · original kept",
        };

    static string SampledOutcome(string reason) =>
        reason switch
        {
            "already-aligned" => "timing already matches",
            "too-little-speech" or "poor-coverage" => "too little speech to check the timing",
            _ => "samples disagree · timing uncertain · original kept",
        };

    // Corrected subtitle paths come back in Corrected; the message has one line per subtitle (or one line for a failure of the whole job).
    internal async Task<StreamSyncResult> SyncStream(
        StreamOption stream,
        IReadOnlyList<string> subtitles,
        double knownDuration,
        int audioIndex,
        Func<CancellationToken, Task<List<StreamOption>>>? refresh,
        Action<int, int>? progress,
        CancellationToken ct,
        StreamSyncSettings? settings = null
    )
    {
        settings ??= new();
        var corrected = new List<string>();
        if (ToolOverride == null && !File.Exists(Path.Combine(ToolRoot, "NoirSubtitleSync.exe")))
            return new("automatic timing tool unavailable", corrected);
        var ffmpegFolder = settings.FfmpegFolder ?? Path.Combine(ToolRoot, "ffmpeg");
        var ffmpeg = Path.Combine(ffmpegFolder, "ffmpeg.exe");
        if (!File.Exists(ffmpeg))
            return new("automatic timing tool unavailable", corrected);
        var targets = subtitles.Where(p => TextSubtitle(p) && File.Exists(p)).ToList();
        if (targets.Count == 0)
            return new("Download subtitles first", corrected);
        var folder = Path.Combine(Path.GetTempPath(), "noir-stream-sync-" + Guid.NewGuid().ToString("N"));
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        overall.CancelAfter(settings.Overall);
        var sampling = new StreamSampler.Options { PerSample = settings.PerSample, Started = settings.Started };
        // Same release only: another encode would have different timing, so a refreshed link never mixes samples.
        Func<CancellationToken, Task<StreamOption?>>? renewed =
            refresh == null
                ? null
                : async token =>
                    (await refresh(token)).FirstOrDefault(o =>
                        o.Name == stream.Name && (o.Size == 0 || stream.Size == 0 || o.Size == stream.Size)
                    );
        try
        {
            Directory.CreateDirectory(folder);
            StreamSampleProxy proxy;
            try
            {
                proxy = new(stream, stream.Size, renewed, settings.TrustLocal, settings.Resolve)
                {
                    BudgetFloor = settings.BudgetFloor,
                    BudgetShare = settings.BudgetShare,
                    MaxRequests = settings.MaxRequests,
                };
            }
            catch (ArgumentException)
            {
                return new("couldn't read the stream · original kept", corrected);
            }
            using (proxy)
            {
                var duration = knownDuration;
                if (!(duration > 0))
                {
                    duration = await StreamSampler.Probe(
                        Path.Combine(ffmpegFolder, "ffprobe.exe"),
                        proxy,
                        ct,
                        overall.Token,
                        sampling
                    );
                    if (!(duration > 0))
                        return new("couldn't find the length of this stream · original kept", corrected);
                }
                var (exit, planned) = await RunTool(
                    ["--plan", duration.ToString("R", System.Globalization.CultureInfo.InvariantCulture)],
                    TimeSpan.FromSeconds(60),
                    ct
                );
                if (exit != 0)
                    return new("automatic timing tool unavailable", corrected);
                List<(double, double)> Windows(JsonElement e) =>
                    e.EnumerateArray().Select(w => (w[0].GetDouble(), w[1].GetDouble())).ToList();
                using var plan = JsonDocument.Parse(planned.Trim());
                var windows = Windows(plan.RootElement.GetProperty("windows"));
                var extra = Windows(plan.RootElement.GetProperty("extra"));
                if (windows.Count == 0)
                    return new("stream too short to sample · original kept", corrected);
                var samples = await StreamSampler.Extract(
                    ffmpeg,
                    proxy,
                    windows,
                    audioIndex,
                    progress == null ? null : (i, n) => progress(i, windows.Count),
                    ct,
                    overall.Token,
                    sampling
                );
                static string i0(double start) =>
                    start.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                string Job(string name, List<SampleAudio> all)
                {
                    var entries = new List<object>();
                    for (var i = 0; i < all.Count; i++)
                    {
                        var pcm = Path.Combine(folder, "s" + i0(all[i].Start) + ".pcm");
                        if (!File.Exists(pcm))
                            File.WriteAllBytes(pcm, all[i].Pcm);
                        entries.Add(new { start = all[i].Start, pcm });
                    }
                    var path = Path.Combine(folder, name + ".json");
                    File.WriteAllText(
                        path,
                        JsonSerializer.Serialize(new { samples = entries, duration }, Plain),
                        new UTF8Encoding(false)
                    );
                    return path;
                }
                var outcomes = new Dictionary<string, (string Message, string Reason, bool Changed)>();
                var job = Job("first", samples);
                foreach (var path in targets)
                    outcomes[path] = await SampledOne(job, path, ct);
                var retry = targets.Where(p => outcomes[p].Reason is "too-little-speech" or "poor-coverage").ToList();
                if (retry.Count > 0 && extra.Count > 0)
                {
                    try
                    {
                        var more = await StreamSampler.Extract(
                            ffmpeg,
                            proxy,
                            extra,
                            audioIndex,
                            progress == null
                                ? null
                                : (i, n) => progress(windows.Count + i, windows.Count + extra.Count),
                            ct,
                            overall.Token,
                            sampling
                        );
                        var second = Job("second", [.. samples, .. more]);
                        foreach (var path in retry)
                            outcomes[path] = await SampledOne(second, path, ct);
                    }
                    // Subtitles already corrected stay reported, and get reloaded, when only the extra listening fails.
                    catch (StreamSampleException e) when (outcomes.Values.Any(o => o.Changed))
                    {
                        foreach (var path in retry)
                            outcomes[path] = (StreamFailure(e.Kind), "error", false);
                    }
                    catch (OperationCanceledException)
                        when (!ct.IsCancellationRequested && outcomes.Values.Any(o => o.Changed))
                    {
                        foreach (var path in retry)
                            outcomes[path] = (StreamFailure(SampleFailure.TimedOut), "error", false);
                    }
                }
                var lines = new List<string>();
                foreach (var path in targets)
                {
                    var outcome = outcomes[path];
                    if (outcome.Changed)
                        corrected.Add(path);
                    var language = SidecarLanguage(path);
                    lines.Add(
                        (
                            language == "ar" ? "Arabic"
                            : language == "en" ? "English"
                            : Path.GetFileName(path)
                        )
                            + " · "
                            + outcome.Message
                    );
                }
                return new(string.Join("\n", lines), corrected);
            }
        }
        catch (StreamSampleException e)
        {
            return new(StreamFailure(e.Kind), corrected);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(StreamFailure(SampleFailure.TimedOut), corrected);
        }
        catch (Exception e)
            when (e
                    is IOException
                        or JsonException
                        or System.ComponentModel.Win32Exception
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or FormatException
            )
        {
            return new("timing check unavailable · original kept", corrected);
        }
        finally
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, true);
                    break;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }

    async Task<(string Message, string Reason, bool Changed)> SampledOne(
        string job,
        string subtitle,
        CancellationToken ct
    )
    {
        var ext = Path.GetExtension(subtitle).ToLowerInvariant();
        var output = Path.Combine(Path.GetDirectoryName(subtitle)!, "~" + Guid.NewGuid().ToString("N") + ext);
        try
        {
            var (exit, json) = await RunTool(["--sampled", job, subtitle, output], TimeSpan.FromMinutes(3), ct);
            if (exit != 0 || !File.Exists(output))
                return ("timing needs manual adjustment", "error", false);
            using var result = JsonDocument.Parse(json.Trim());
            var reason =
                result.RootElement.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString() ?? ""
                    : "";
            if (!result.RootElement.TryGetProperty("accepted", out var ok) || ok.ValueKind != JsonValueKind.True)
                return (SampledOutcome(reason), reason, false);
            var offset = result.RootElement.GetProperty("offset").GetDouble();
            var scale = result.RootElement.GetProperty("scale").GetDouble();
            if (!AcceptTiming(offset, scale) || !ValidText(await File.ReadAllTextAsync(output, ct), ext))
                return (SampledOutcome("unstable"), "unstable", false);
            // Nothing is written once the user has cancelled.
            ct.ThrowIfCancellationRequested();
            var backup = Path.Combine(
                backupDirectory,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(subtitle)))
                    + ext
            );
            if (!File.Exists(backup))
                File.Copy(subtitle, backup, false);
            ReplaceSubtitle(output, subtitle);
            return ($"auto-synced ({offset:+0.00;-0.00;0.00}s)", reason, true);
        }
        catch (Exception e)
            when (e
                    is IOException
                        or JsonException
                        or System.ComponentModel.Win32Exception
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or KeyNotFoundException
            )
        {
            return ("timing check unavailable · original kept", "error", false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ("timing check timed out · original kept", "error", false);
        }
        finally
        {
            try
            {
                if (File.Exists(output))
                    File.Delete(output);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    public static string SyncedCopy(string video, string language, string extension) =>
        Path.Combine(
            Path.GetDirectoryName(video)!,
            Path.GetFileNameWithoutExtension(video) + ".synced." + language + extension
        );

    // The language stays last so VLC labels the track [English] / [Arabic].
    public static bool IsSyncedCopy(string name) =>
        Regex.IsMatch(name, @"(?i)\.synced\.(en|ar)(\.(srt|ass|ssa|vtt))?$");

    // Offset is null when the subtitle was left unchanged; on success the subtitle file is replaced in place.
    // On a refusal the tool's raw offset and scale come back too (RawOffset is null when the tool gave none).
    async Task<(string Message, double? Offset, double Scale, double? RawOffset, double RawScale)> Align(
        string video,
        string subtitle,
        CancellationToken ct,
        string? backup,
        bool audio = false
    )
    {
        var tool = Path.Combine(ToolRoot, "NoirSubtitleSync.exe");
        if (ToolOverride == null && !File.Exists(tool))
            return ("automatic timing tool unavailable", null, 1, null, 1);
        var ext = Path.GetExtension(subtitle).ToLowerInvariant();
        if (ext is not (".srt" or ".ass" or ".ssa" or ".vtt"))
            return ("timing unchanged", null, 1, null, 1);
        var output = Path.Combine(
            Path.GetDirectoryName(subtitle)!,
            Path.GetFileNameWithoutExtension(subtitle) + "." + Guid.NewGuid().ToString("N") + ext
        );
        try
        {
            var arguments = new List<string> { video, subtitle, output };
            if (audio)
                arguments.Add("--audio");
            var (exit, json) = await RunTool(arguments, TimeSpan.FromMinutes(4), ct);
            if (exit != 0)
                return ("timing needs manual adjustment", null, 1, null, 1);
            using var result = JsonDocument.Parse(json.Trim());
            double? rawOffset = null;
            var rawScale = 1d;
            if (
                result.RootElement.TryGetProperty("offset", out var o)
                && o.ValueKind == JsonValueKind.Number
                && o.TryGetDouble(out var oValue)
                && double.IsFinite(oValue)
            )
            {
                rawOffset = oValue;
                if (
                    result.RootElement.TryGetProperty("scale", out var sc)
                    && sc.ValueKind == JsonValueKind.Number
                    && sc.TryGetDouble(out var sValue)
                    && double.IsFinite(sValue)
                )
                    rawScale = sValue;
            }
            if (!result.RootElement.TryGetProperty("accepted", out var ok) || ok.ValueKind != JsonValueKind.True)
                return ("timing uncertain · original kept", null, 1, rawOffset, rawScale);
            if (!File.Exists(output))
                return ("timing needs manual adjustment", null, 1, null, 1);
            var offset = result.RootElement.GetProperty("offset").GetDouble();
            var scale = result.RootElement.GetProperty("scale").GetDouble();
            if (!AcceptTiming(offset, scale) || !ValidText(await File.ReadAllTextAsync(output, ct), ext))
                return ("timing uncertain · original kept", null, 1, offset, scale);
            if (backup != null && !File.Exists(backup))
                File.Copy(subtitle, backup, false);
            ReplaceSubtitle(output, subtitle);
            return ("", offset, scale, offset, scale);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ("timing check timed out · original kept", null, 1, null, 1);
        }
        catch (Exception e)
            when (e
                    is IOException
                        or JsonException
                        or System.ComponentModel.Win32Exception
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or KeyNotFoundException
            )
        {
            return ("timing check unavailable · original kept", null, 1, null, 1);
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    // VLC keeps a loaded subtitle open without delete sharing, so Windows refuses to swap the file during playback.
    // Its contents can still be rewritten; if that fails part-way the original bytes go back before the error surfaces.
    public static void ReplaceSubtitle(string corrected, string subtitle)
    {
        try
        {
            File.Move(corrected, subtitle, true);
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!File.Exists(subtitle))
                throw;
        }
        var bytes = File.ReadAllBytes(corrected);
        var original = File.ReadAllBytes(subtitle);
        using (
            var stream = new FileStream(
                subtitle,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete
            )
        )
            try
            {
                stream.SetLength(0);
                stream.Write(bytes);
                stream.Flush(true);
            }
            catch (IOException)
            {
                stream.Position = 0;
                stream.SetLength(0);
                stream.Write(original);
                stream.Flush(true);
                throw;
            }
        File.Delete(corrected);
    }

    public static bool AcceptTiming(double offset, double scale) =>
        double.IsFinite(offset) && double.IsFinite(scale) && Math.Abs(offset) <= 30 && Math.Abs(scale - 1) <= .05;

    public static bool ValidText(string text, string ext) =>
        text.Length is > 10 and < 4 * 1024 * 1024
        && !text.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
        && (
            ext is ".ass" or ".ssa"
                ? text.Contains("[Events]", StringComparison.OrdinalIgnoreCase)
                : Regex.IsMatch(text, @"\d{1,2}:\d{2}(?::\d{2})?[,.]\d{3}\s+-->\s+\d{1,2}:\d{2}")
        );

    static bool ValidUrl(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var u)
        && u.Scheme == "https"
        && u.UserInfo.Length == 0
        && !u.IsLoopback;

    async Task<byte[]> Download(string link, CancellationToken ct)
    {
        if (!ValidUrl(link))
            throw new IOException("Invalid subtitle link");
        using var r = await http.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, ct);
        r.EnsureSuccessStatusCode();
        var bytes = await Limited(r.Content, 4 * 1024 * 1024, ct);
        if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var data = new MemoryStream();
            var buffer = new byte[8192];
            int n;
            while ((n = await gzip.ReadAsync(buffer, ct)) > 0)
            {
                if (data.Length + n > 4 * 1024 * 1024)
                    throw new IOException("Subtitle too large");
                data.Write(buffer, 0, n);
            }
            bytes = data.ToArray();
        }
        return bytes;
    }

    public static string Decode(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (bytes.Length > 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length > 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1256).GetString(bytes);
        }
    }

    static async Task<byte[]> Limited(HttpContent content, int max, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        ct = timeout.Token;
        if (content.Headers.ContentLength > max)
            throw new IOException("Subtitle response too large");
        await using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > max)
                throw new IOException("Subtitle response too large");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
