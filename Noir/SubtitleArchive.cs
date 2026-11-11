using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Noir;

public static class SubtitleArchive
{
    static List<(int Offset, int Method)> Entries(byte[] bytes, int count)
    {
        int end = -1;
        for (int i = bytes.Length - 22; i >= Math.Max(0, bytes.Length - 65557); i--)
            if (
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == 0x06054b50
                && i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i + 20)) == bytes.Length
            )
            {
                end = i;
                break;
            }
        if (
            end < 0
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 4)) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(end + 10)) != count
        )
            throw new InvalidDataException();
        var position = (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 16));
        var result = new List<(int, int)>();
        for (int i = 0; i < count; i++)
        {
            if (position < 0 || position + 46 > end)
                throw new InvalidDataException();
            int p = (int)position;
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p)) != 0x02014b50)
                throw new InvalidDataException();
            var offset = (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 42));
            if (offset + 30 > bytes.Length)
                throw new InvalidDataException();
            int local = (int)offset;
            if (
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(local)) != 0x04034b50
                || (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p + 8)) & 1) != 0
            )
                throw new InvalidDataException();
            int method = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p + 10));
            if (method is not (0 or 8) || method != BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(local + 8)))
                throw new InvalidDataException();
            int start =
                local
                + 30
                + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(local + 26))
                + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(local + 28));
            if (start > bytes.Length)
                throw new InvalidDataException();
            result.Add((start, method));
            position +=
                46
                + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p + 28))
                + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p + 30))
                + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p + 32));
        }
        return result;
    }

    public static bool HasEpisodeName(string name) =>
        Regex.IsMatch(name, @"(?i)s\d{1,2}[ ._-]*e\d|\d{1,2}x\d|(?:^|[ ._-])(?:e|ep|episode)[ ._-]*\d");

    public static bool EpisodeMatches(string name, int season, int episode)
    {
        var full = Regex.Matches(
            name,
            @"(?i)(?:s(?<s>\d{1,2})[ ._-]*e(?<e>\d{1,3})|(?<![\da-z])(?<s>\d{1,2})x(?<e>\d{1,3}))(?!\d)"
        );
        if (
            Regex.Matches(name, @"(?i)(?<![a-z])e(\d{1,3})(?!\d)").Any(m => int.Parse(m.Groups[1].Value) != episode)
            || Regex.IsMatch(name, @"(?i)(?:(?<![a-z])e\d{1,3}|(?<!\d)\d{1,2}x\d{1,3})-\d")
        )
            return false;
        if (full.Count > 0)
            return full.All(m => int.Parse(m.Groups["s"].Value) == season && int.Parse(m.Groups["e"].Value) == episode);
        var seasons = Regex.Matches(name, @"(?i)(?:^|[^a-z0-9])(?:s|season[ ._-]*)(\d{1,2})(?!\d)");
        if (seasons.Any(m => int.Parse(m.Groups[1].Value) != season))
            return false;
        var eps = Regex.Matches(name, @"(?i)(?:^|[ ._\-])(?:e|ep|episode)[ ._-]*(\d{1,3})(?!\d)");
        return eps.Count > 0 && eps.All(m => int.Parse(m.Groups[1].Value) == episode);
    }

    public static async Task<(byte[] Bytes, string Name)> Pick(
        byte[] bytes,
        VideoItem item,
        string release,
        CancellationToken ct
    )
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(35));
        ct = budget.Token;
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        if (zip.Entries.Count > 200)
            throw new IOException("Subtitle archive too large.");
        var entries = Entries(bytes, zip.Entries.Count);
        long total = 0;
        var candidates = new List<(byte[] Bytes, string Name, int Score)>();
        for (int i = 0; i < zip.Entries.Count; i++)
        {
            var entry = zip.Entries[i];
            ct.ThrowIfCancellationRequested();
            if (entry.Length > 4 * 1024 * 1024 || total + entry.Length > 16 * 1024 * 1024)
                throw new IOException("Subtitle archive too large.");
            var raw = entries[i];
            if (entry.CompressedLength > bytes.Length - raw.Offset)
                throw new InvalidDataException();
            using var compressed = new MemoryStream(bytes, raw.Offset, (int)entry.CompressedLength, false);
            using Stream stream =
                raw.Method == 8 ? new DeflateStream(compressed, CompressionMode.Decompress) : compressed;
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while (
                (
                    count = await stream.ReadAsync(
                        buffer.AsMemory(
                            0,
                            (int)
                                Math.Min(
                                    buffer.Length,
                                    Math.Min(4 * 1024 * 1024 - output.Length, 16 * 1024 * 1024 - total) + 1
                                )
                        ),
                        ct
                    )
                ) > 0
            )
            {
                total += count;
                if (output.Length + count > 4 * 1024 * 1024 || total > 16 * 1024 * 1024)
                    throw new IOException("Subtitle archive too large.");
                output.Write(buffer, 0, count);
            }
            if (output.Length != entry.Length)
                throw new InvalidDataException();
            var name = entry.FullName.Replace('\\', '/').Split('/').Last();
            if (
                !SubtitleDownloads.TextSubtitle(name)
                || !item.IsMovie && !EpisodeMatches(name, item.Season, item.Episode)
            )
                continue;
            candidates.Add((output.ToArray(), name, SubtitleDownloads.ReleaseScore(name, release)));
        }
        if (candidates.Count == 0)
            throw new IOException("No matching subtitle in archive.");
        var best = candidates.OrderByDescending(c => c.Score).First();
        return (best.Bytes, best.Name);
    }
}
