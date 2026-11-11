using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public static class EmbeddedSubtitles
{
    static bool Partial(string value) =>
        Regex.IsMatch(value, @"(?i)(?:^|[^a-z])(forced|signs?|songs?|karaoke)(?:[^a-z]|$)");

    public static string Language(string language, string title, bool forced = false)
    {
        if (forced || Partial(title))
            return "";
        if (!string.IsNullOrWhiteSpace(language) && !language.Trim().Equals("und", StringComparison.OrdinalIgnoreCase))
            return SubtitleDownloads.Language(language.Trim());
        var match = Regex.Match(title, @"(?i)(?:^|[^a-z])(eng|en|english|ara|ar|arabic)(?:[^a-z]|$)");
        return SubtitleDownloads.Language(match.Groups[1].Value);
    }

    public static HashSet<string> Parse(string json)
    {
        var result = new HashSet<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (
                doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("streams", out var streams)
                || streams.ValueKind != JsonValueKind.Array
            )
                return result;
            foreach (var stream in streams.EnumerateArray())
            {
                static string Text(JsonElement value, string key) =>
                    value.ValueKind == JsonValueKind.Object
                    && value.TryGetProperty(key, out var text)
                    && text.ValueKind == JsonValueKind.String
                        ? text.GetString() ?? ""
                        : "";
                if (Text(stream, "codec_type") != "subtitle")
                    continue;
                stream.TryGetProperty("tags", out var tags);
                stream.TryGetProperty("disposition", out var disposition);
                var forced =
                    disposition.ValueKind == JsonValueKind.Object
                    && disposition
                        .EnumerateObject()
                        .Any(p =>
                            Partial(p.Name)
                            && (
                                p.Value.ValueKind == JsonValueKind.True
                                || p.Value.ValueKind == JsonValueKind.Number
                                    && p.Value.TryGetInt32(out var flag)
                                    && flag == 1
                            )
                        );
                var language = Language(Text(tags, "language"), Text(tags, "title"), forced);
                if (language.Length > 0)
                    result.Add(language);
            }
        }
        catch (JsonException)
        {
            return [];
        }
        return result;
    }

    // Text tracks that can be pulled out and timing-checked; Index counts subtitle streams only (ffmpeg 0:s:N).
    public static List<(int Index, string Language, string Extension)> TextTracks(string json)
    {
        var result = new List<(int, string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (
                doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("streams", out var streams)
                || streams.ValueKind != JsonValueKind.Array
            )
                return result;
            var index = -1;
            foreach (var stream in streams.EnumerateArray())
            {
                static string Text(JsonElement value, string key) =>
                    value.ValueKind == JsonValueKind.Object
                    && value.TryGetProperty(key, out var text)
                    && text.ValueKind == JsonValueKind.String
                        ? text.GetString() ?? ""
                        : "";
                if (Text(stream, "codec_type") != "subtitle")
                    continue;
                index++;
                var ext = Text(stream, "codec_name") switch
                {
                    "subrip" or "srt" or "mov_text" or "webvtt" or "text" => ".srt",
                    "ass" or "ssa" => ".ass",
                    _ => "",
                };
                if (ext.Length == 0)
                    continue;
                stream.TryGetProperty("tags", out var tags);
                stream.TryGetProperty("disposition", out var disposition);
                var forced =
                    disposition.ValueKind == JsonValueKind.Object
                    && disposition
                        .EnumerateObject()
                        .Any(p =>
                            Partial(p.Name)
                            && (
                                p.Value.ValueKind == JsonValueKind.True
                                || p.Value.ValueKind == JsonValueKind.Number
                                    && p.Value.TryGetInt32(out var flag)
                                    && flag == 1
                            )
                        );
                var language = Language(Text(tags, "language"), Text(tags, "title"), forced);
                if (language.Length > 0 && result.All(t => t.Item2 != language))
                    result.Add((index, language, ext));
            }
        }
        catch (JsonException)
        {
            return [];
        }
        return result;
    }

    public static async Task<HashSet<string>> Probe(string video, CancellationToken ct) =>
        Parse(await ProbeJson(video, ct));

    public static async Task<List<(int Index, string Language, string Extension)>> ProbeTextTracks(
        string video,
        CancellationToken ct
    ) => TextTracks(await ProbeJson(video, ct));

    static Task<string> ProbeJson(string video, CancellationToken ct) =>
        Run("ffprobe.exe", ["-v", "error", "-show_streams", "-of", "json", video], TimeSpan.FromSeconds(30), ct);

    public static async Task<bool> Extract(string video, int index, string output, CancellationToken ct)
    {
        await Run(
            "ffmpeg.exe",
            [
                "-v",
                "error",
                "-nostdin",
                "-y",
                "-i",
                video,
                "-map",
                "0:s:" + index,
                "-c:s",
                Path.GetExtension(output) == ".ass" ? "ass" : "srt",
                output,
            ],
            TimeSpan.FromMinutes(2),
            ct
        );
        return File.Exists(output) && new FileInfo(output).Length > 10;
    }

    // Returns stdout, or "" when the tool is missing, fails or times out.
    static async Task<string> Run(string name, string[] arguments, TimeSpan limit, CancellationToken ct)
    {
        var tool = Path.Combine(SubtitleDownloads.ToolRoot, "ffmpeg", name);
        if (!File.Exists(tool))
            return "";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        var start = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                var text = await stdout;
                await stderr;
                return process.ExitCode == 0 ? text : "";
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(true);
                try
                {
                    await Task.WhenAll(stdout, stderr);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "";
        }
        catch (Exception e)
            when (e
                    is IOException
                        or System.ComponentModel.Win32Exception
                        or InvalidOperationException
                        or UnauthorizedAccessException
            )
        {
            return "";
        }
    }
}
