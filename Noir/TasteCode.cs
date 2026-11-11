using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Noir;

public sealed record TasteRating(int Id, int Rating10);

public sealed record TasteCard(string Name, int[] Favorites, TasteRating[] Ratings);

public sealed record TasteMatch(
    int? Score,
    int Overlap,
    bool Correlation,
    int[] BothLoved,
    int[] UnseenLoved,
    int[] Disagreements
);

public static class TasteCode
{
    // v1: version:u8, UTF-8 name length:u8 + bytes (NFC, <=64), favorite count:u8
    // + ordered IDs:u32be (<=4), rating count:u8 + (ID:u32be,rating10:u8) (<=60, sorted).
    public const string Prefix = "noir://taste/";
    static readonly UTF8Encoding Utf8 = new(false, true);

    public static int Id(string? value) =>
        value != null
        && value.StartsWith("tt", StringComparison.Ordinal)
        && value.Length > 2
        && value[2..].All(c => c is >= '0' and <= '9')
        && int.TryParse(value.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
        && id > 0
            ? id
            : 0;

    public static string Imdb(int id) => "tt" + id.ToString("D7", CultureInfo.InvariantCulture);

    public static string Name(string? value)
    {
        var clean = new StringBuilder();
        foreach (var rune in (value ?? "").EnumerateRunes())
            clean.Append(rune);
        var trim = Enumerable.Range(0, 33).Select(n => (char)n).ToArray();
        var s = clean.ToString().Normalize(NormalizationForm.FormC).Trim(trim);
        var b = new StringBuilder();
        var length = 0;
        foreach (var rune in s.EnumerateRunes())
        {
            if (rune.Value < 32 || rune.Value == 127)
                continue;
            var n = rune.Utf8SequenceLength;
            if (length + n > 64)
                break;
            b.Append(rune);
            length += n;
        }
        return b.ToString().Normalize(NormalizationForm.FormC).Trim(trim);
    }

    public static TasteCard Create(
        string? name,
        IEnumerable<int> favorites,
        IEnumerable<TasteRating> ratings,
        ISet<int>? hidden = null
    )
    {
        var f = favorites.Where(id => id > 0 && hidden?.Contains(id) != true).Distinct().Take(4).ToArray();
        var r = new SortedDictionary<int, int>();
        foreach (var v in ratings)
            if (v.Id > 0 && v.Rating10 is >= 1 and <= 10 && hidden?.Contains(v.Id) != true)
                r[v.Id] = v.Rating10;
        return new(Name(name), f, r.Take(60).Select(v => new TasteRating(v.Key, v.Value)).ToArray());
    }

    public static string Encode(TasteCard card)
    {
        var c = Create(card.Name, card.Favorites, card.Ratings);
        var name = Utf8.GetBytes(c.Name);
        using var output = new System.IO.MemoryStream();
        output.WriteByte(1);
        output.WriteByte((byte)name.Length);
        output.Write(name);
        output.WriteByte((byte)c.Favorites.Length);
        void Put(int id)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(b, id);
            output.Write(b);
        }
        foreach (var id in c.Favorites)
            Put(id);
        output.WriteByte((byte)c.Ratings.Length);
        foreach (var r in c.Ratings)
        {
            Put(r.Id);
            output.WriteByte((byte)r.Rating10);
        }
        return Convert.ToBase64String(output.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Link(TasteCard card) => Prefix + Encode(card);

    public static bool TryDecode(string? input, out TasteCard card)
    {
        card = new("", [], []);
        if (input == null || input.Length > 600)
            return false;
        var code = input.Trim();
        if (code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            code = code[Prefix.Length..];
        if (
            code.Length is < 6 or > 512
            || code.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
        )
            return false;
        try
        {
            var bytes = Convert.FromBase64String(
                code.Replace('-', '+').Replace('_', '/') + new string('=', (4 - code.Length % 4) % 4)
            );
            var p = 0;
            int Byte()
            {
                if (p >= bytes.Length)
                    throw new FormatException();
                return bytes[p++];
            }
            int Read()
            {
                if (p + 4 > bytes.Length)
                    throw new FormatException();
                var id = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(p, 4));
                p += 4;
                if (id <= 0)
                    throw new FormatException();
                return id;
            }
            if (Byte() != 1)
                return false;
            var len = Byte();
            if (len > 64 || p + len > bytes.Length)
                return false;
            var name = Utf8.GetString(bytes, p, len);
            p += len;
            if (Name(name) != name)
                return false;
            var nf = Byte();
            if (nf > 4)
                return false;
            var f = new int[nf];
            for (var i = 0; i < nf; i++)
                f[i] = Read();
            if (f.Distinct().Count() != nf)
                return false;
            var nr = Byte();
            if (nr > 60 || p + nr * 5 != bytes.Length)
                return false;
            var r = new TasteRating[nr];
            var last = 0;
            for (var i = 0; i < nr; i++)
            {
                var id = Read();
                var rating = Byte();
                if (id <= last || rating is < 1 or > 10)
                    return false;
                r[i] = new(id, rating);
                last = id;
            }
            var result = new TasteCard(name, f, r);
            if (Encode(result) != code)
                return false;
            card = result;
            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    static HashSet<int> Loved(TasteCard c) =>
        c.Favorites.Concat(c.Ratings.Where(r => r.Rating10 >= 8).Select(r => r.Id)).ToHashSet();

    public static TasteMatch Compare(TasteCard a, TasteCard b, IReadOnlyDictionary<int, string[]>? genres = null)
    {
        var ar = a.Ratings.ToDictionary(r => r.Id, r => r.Rating10);
        var br = b.Ratings.ToDictionary(r => r.Id, r => r.Rating10);
        var common = ar.Keys.Where(br.ContainsKey).Order().ToArray();
        var al = Loved(a);
        var bl = Loved(b);
        double total = 0,
            weight = 0;
        bool correlation = false;
        if (common.Length >= 5)
        {
            var ax = common.Average(id => (double)ar[id]);
            var bx = common.Average(id => (double)br[id]);
            double xx = 0,
                yy = 0,
                xy = 0;
            foreach (var id in common)
            {
                var x = ar[id] - ax;
                var y = br[id] - bx;
                xx += x * x;
                yy += y * y;
                xy += x * y;
            }
            correlation = xx > 0 && yy > 0;
            total = correlation
                ? (1 + Math.Clamp(xy / Math.Sqrt(xx * yy), -1, 1)) / 2
                : 1 - common.Average(id => Math.Abs(ar[id] - br[id])) / 9;
            weight = 1;
        }
        else
        {
            void Add(double value, double w)
            {
                total += value * w;
                weight += w;
            }
            if (common.Length > 0)
                Add(1 - common.Average(id => Math.Abs(ar[id] - br[id])) / 9, .4 * common.Length / 5);
            var af = a.Favorites.ToHashSet();
            var bf = b.Favorites.ToHashSet();
            if (af.Count > 0 && bf.Count > 0)
                Add((double)af.Intersect(bf).Count() / af.Union(bf).Count(), .4);
            if (genres != null)
            {
                HashSet<string> Tags(HashSet<int> ids) =>
                    ids.Where(genres.ContainsKey)
                        .SelectMany(id => genres[id])
                        .Select(s => s.Trim().ToLowerInvariant())
                        .Where(s => s.Length > 0)
                        .ToHashSet();
                var ag = Tags(al);
                var bg = Tags(bl);
                if (ag.Count > 0 && bg.Count > 0)
                    Add((double)ag.Intersect(bg).Count() / ag.Union(bg).Count(), .2);
            }
        }
        return new(
            weight == 0 ? null : (int)Math.Floor(Math.Clamp(total / weight, 0, 1) * 100 + .5),
            common.Length,
            correlation,
            al.Intersect(bl).Order().ToArray(),
            bl.Where(id => !ar.ContainsKey(id) && !al.Contains(id)).Order().ToArray(),
            common
                .Where(id => Math.Abs(ar[id] - br[id]) >= 4)
                .OrderByDescending(id => Math.Abs(ar[id] - br[id]))
                .ThenBy(id => id)
                .ToArray()
        );
    }
}
