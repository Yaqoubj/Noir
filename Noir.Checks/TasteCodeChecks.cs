using Noir;

public static class TasteCodeChecks
{
    public const string SharedVector = "AQ1Ob2lyINmE2YrZhNmJAgACB-UAAbI5AwABsjkKAAIH5QkAFP2yCA";

    public static void Run(Action<bool, string> check)
    {
        var source = TasteCode.Create(
            "Noir ليلى",
            [133093, 111161],
            [new(1375666, 8), new(111161, 10), new(133093, 9)]
        );
        check(TasteCode.Encode(source) == SharedVector, "Taste: PC/phone shared Unicode binary vector");
        check(
            TasteCode.TryDecode(TasteCode.Prefix + SharedVector, out var decoded)
                && decoded.Name == source.Name
                && decoded.Favorites.SequenceEqual(source.Favorites)
                && decoded.Ratings.SequenceEqual(source.Ratings),
            "Taste: link round trip preserves favorite order, ratings and Unicode name"
        );
        check(
            new string?[]
            {
                null,
                "",
                "%%%%",
                "noir://other/" + SharedVector,
                SharedVector + "=",
                SharedVector + "A",
                SharedVector[..^1],
                new('A', 10000),
                "AgAAAA",
                "AQEAAA",
            }.All(s => !TasteCode.TryDecode(s, out _)),
            "Taste: bad version, length, padding, garbage and oversized input are rejected"
        );
        var raw = Convert.FromBase64String(SharedVector.Replace('-', '+').Replace('_', '/') + "==");
        string Code(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var bad = (byte[])raw.Clone();
        bad[1] = 65;
        check(!TasteCode.TryDecode(Code(bad), out _), "Taste: oversized name rejected");
        bad = (byte[])raw.Clone();
        bad[^1] = 11;
        check(!TasteCode.TryDecode(Code(bad), out _), "Taste: invalid rating rejected");
        bad = (byte[])raw.Clone();
        bad[2] = 255;
        check(!TasteCode.TryDecode(Code(bad), out _), "Taste: malformed UTF-8 rejected");
        var hidden = TasteCode.Create(source.Name, source.Favorites, source.Ratings, new HashSet<int> { 133093 });
        check(
            !hidden.Favorites.Contains(133093)
                && hidden.Ratings.All(r => r.Id != 133093)
                && TasteCode.TryDecode(TasteCode.Link(hidden), out var filtered)
                && filtered.Ratings.Length == 2,
            "Taste: hidden title removed from favorites and ratings before sharing"
        );
        var full = TasteCode.Create(
            new string('界', 100),
            Enumerable.Range(1, 10),
            Enumerable.Range(1, 100).Select(i => new TasteRating(i, 8))
        );
        check(
            full.Favorites.Length == 4
                && full.Ratings.Length == 60
                && System.Text.Encoding.UTF8.GetByteCount(full.Name) <= 64
                && TasteCode.TryDecode(TasteCode.Link(full), out _),
            "Taste: payload limits and multibyte name boundary round trip"
        );
        TasteCard Card(int[] scores) => TasteCode.Create("", [], scores.Select((r, i) => new TasteRating(i + 1, r)));
        var a = Card([1, 3, 5, 7, 9]);
        var same = TasteCode.Compare(a, a);
        check(same.Score == 100 && same.Correlation && same.Overlap == 5, "Taste: identical ratings correlate at 100");
        var opposite = TasteCode.Compare(a, Card([9, 7, 5, 3, 1]));
        check(
            opposite.Score == 0 && opposite.Disagreements.SequenceEqual(new[] { 1, 5, 2, 4 }),
            "Taste: inverse ratings correlate at zero and disagreements use gap order"
        );
        var flat = TasteCode.Compare(Card([8, 8, 8, 8, 8]), Card([8, 8, 8, 8, 8]));
        check(flat.Score == 100 && !flat.Correlation, "Taste: constant ratings use distance without division by zero");
        var fa = TasteCode.Create("a", [1, 2], []);
        var fb = TasteCode.Create("b", [1, 2], []);
        var small = TasteCode.Compare(fa, fb);
        check(
            small.Score == 100
                && small.Overlap == 0
                && !small.Correlation
                && small.BothLoved.SequenceEqual(new[] { 1, 2 }),
            "Taste: favorites provide an honest small-overlap fallback"
        );
        var genre = TasteCode.Compare(
            TasteCode.Create("a", [], [new(1, 9)]),
            TasteCode.Create("b", [], [new(2, 9)]),
            new Dictionary<int, string[]> { { 1, ["Drama"] }, { 2, ["Drama"] } }
        );
        check(genre.Score == 100 && genre.Overlap == 0, "Taste: known genres provide fallback without shared ratings");
        check(
            TasteCode.Compare(TasteCode.Create("", [], []), TasteCode.Create("", [], [])).Score == null,
            "Taste: no evidence produces no invented score"
        );
        var one = TasteCode.Compare(
            TasteCode.Create("", [], [new(1, 8)]),
            TasteCode.Create("", [2], [new(1, 8), new(2, 10)])
        );
        check(
            one.Score == 100 && one.Overlap == 1 && !one.Correlation && one.UnseenLoved.SequenceEqual(new[] { 2 }),
            "Taste: one rating remains limited and unseen loved titles are distinct"
        );
        var random = new Random(47);
        for (var i = 0; i < 300; i++)
        {
            var bytes = new byte[random.Next(0, 400)];
            random.NextBytes(bytes);
            _ = TasteCode.TryDecode(Code(bytes), out _);
        }
        check(true, "Taste: random malformed binary never crashes decoding");
    }
}
