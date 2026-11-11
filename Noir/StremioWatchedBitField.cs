using System.IO;
using System.IO.Compression;

namespace Noir;

// Stremio's series watched state (stremio-core, stremio-watched-bitfield/src/watched_bitfield.rs and bitfield8.rs):
// "<anchorVideoId>:<anchorLength>:<base64(zlib(bytes))>", one bit per video in Stremio's video order
// (season, episode, release date), bit i in byte i/8 at bit i%8 (least significant first). The anchor is the
// last watched video and anchorLength its 1-based index, so the field can be realigned when the video list grows.
// Noir only ever sets bits: there is deliberately no way to clear one.
public sealed class StremioWatchedBitField
{
    byte[] values;

    public IReadOnlyList<string> VideoIds { get; }

    // Bits set in the remote field before realigning, and bits that survived realigning onto this video list.
    // If they differ, writing this field back would erase watched episodes from the account.
    public int RemoteSetBits { get; }

    StremioWatchedBitField(byte[] values, IReadOnlyList<string> videoIds, int remoteSetBits)
    {
        this.values = values;
        VideoIds = videoIds;
        RemoteSetBits = remoteSetBits;
    }

    public int Length => VideoIds.Count;

    public int SetBits => Count(values);

    public bool LostRemoteBits => SetBits < RemoteSetBits;

    // construct_from_array(vec![], video_ids): nothing watched.
    public static StremioWatchedBitField Empty(IReadOnlyList<string> videoIds) =>
        new(new byte[(videoIds.Count + 7) / 8], videoIds, 0);

    public static StremioWatchedBitField FromArray(IReadOnlyList<bool> watched, IReadOnlyList<string> videoIds)
    {
        var field = Empty(videoIds);
        for (int i = 0; i < watched.Count; i++)
            if (watched[i])
                field.SetIndex(i);
        return field;
    }

    // construct_with_videos: realign a serialized field onto the current video list.
    public static StremioWatchedBitField Parse(string serialized, IReadOnlyList<string> videoIds)
    {
        var last = serialized.LastIndexOf(':');
        var previous = last > 0 ? serialized.LastIndexOf(':', last - 1) : -1;
        if (
            previous <= 0
            || !int.TryParse(
                serialized[(previous + 1)..last],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var anchorLength
            )
        )
            throw new FormatException("Invalid Stremio watched field.");
        var anchorVideo = serialized[..previous];
        var remote = Decompress(serialized[(last + 1)..]);
        var remoteBits = Count(remote);
        var size = (videoIds.Count + 7) / 8;
        var anchor = IndexOf(videoIds, anchorVideo);
        if (anchor < 0)
            return new(new byte[size], videoIds, remoteBits);
        long offset = (long)anchorLength - anchor - 1;
        if (offset == 0)
        {
            var kept = remote.Length >= size ? remote : [.. remote, .. new byte[size - remote.Length]];
            return new(kept, videoIds, remoteBits);
        }
        var moved = new byte[size];
        for (int i = 0; i < videoIds.Count; i++)
        {
            var from = i + offset;
            if (from >= 0 && from < videoIds.Count && Get(remote, from))
                moved[i / 8] |= (byte)(1 << (i % 8));
        }
        return new(moved, videoIds, remoteBits);
    }

    public bool this[int index] => Get(values, index);

    public bool IsWatched(string videoId)
    {
        var i = IndexOf(VideoIds, videoId);
        return i >= 0 && this[i];
    }

    // Returns false when the video is not in the list (Stremio's set_video is then a no-op too).
    public bool MarkWatched(string videoId)
    {
        var i = IndexOf(VideoIds, videoId);
        if (i < 0)
            return false;
        SetIndex(i);
        return true;
    }

    public void SetIndex(int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (index / 8 >= values.Length)
            Array.Resize(ref values, index / 8 + 1);
        values[index / 8] |= (byte)(1 << (index % 8));
    }

    public int LastWatchedIndex()
    {
        for (int i = Length - 1; i >= 0; i--)
            if (this[i])
                return i;
        return -1;
    }

    // From<WatchedBitField> for WatchedField: anchor = last watched video (index 0 when none), length 1-based,
    // "undefined" when the list is empty.
    public override string ToString()
    {
        var lastId = Math.Max(0, LastWatchedIndex());
        var anchor = lastId < VideoIds.Count ? VideoIds[lastId] : "undefined";
        return $"{anchor}:{lastId + 1}:{Compress(values)}";
    }

    public static string Compress(byte[] bytes)
    {
        // .NET writes no zlib stream at all for zero bytes; this is zlib's empty stream, as flate2 writes it.
        if (bytes.Length == 0)
            return "eJwDAAAAAAE=";
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
            zlib.Write(bytes);
        return Convert.ToBase64String(output.ToArray());
    }

    public static byte[] Decompress(string base64)
    {
        using var input = new MemoryStream(Convert.FromBase64String(base64));
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    // Stremio's order for the bitfield: season, episode, release date; missing values first; stable otherwise.
    public static List<string> Order(IEnumerable<StremioVideo> videos) =>
        videos
            .Where(v => v.Id.Length > 0)
            .OrderBy(v => v.Season ?? long.MinValue)
            .ThenBy(v => v.Episode ?? long.MinValue)
            .ThenBy(v => v.Released?.ToUnixTimeMilliseconds() ?? long.MinValue)
            .Select(v => v.Id)
            .ToList();

    static bool Get(byte[] bytes, long index) =>
        index >= 0 && index / 8 < bytes.Length && ((bytes[index / 8] >> (int)(index % 8)) & 1) == 1;

    static int Count(byte[] bytes) => bytes.Sum(b => System.Numerics.BitOperations.PopCount(b));

    static int IndexOf(IReadOnlyList<string> ids, string id)
    {
        for (int i = 0; i < ids.Count; i++)
            if (ids[i] == id)
                return i;
        return -1;
    }
}

public sealed record StremioVideo(string Id, long? Season, long? Episode, DateTimeOffset? Released);
