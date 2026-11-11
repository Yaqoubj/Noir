using System.IO;
using System.Text.Json;
using Noir;

static class SmartListChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        SmartEpisode Ep(
            int e,
            bool watched = false,
            int count = 0,
            int rating = 0,
            DateTimeOffset? at = null,
            double min = 24,
            bool play = true,
            DateTimeOffset? air = null,
            bool started = false
        ) =>
            new(
                "e" + e,
                1,
                e,
                watched,
                started,
                count,
                rating,
                at ?? DateTimeOffset.MinValue,
                min,
                play,
                air,
                "Summary " + e
            );
        SmartTitle Title(
            List<SmartEpisode> eps,
            bool movie = false,
            bool dropped = false,
            string key = "show",
            string[]? directors = null
        ) => new(key, movie, dropped, eps, directors ?? []);
        List<SmartPick> Row(SmartTitle title, string id) => SmartLists.Build([title], now)[id];
        var stalled = Title([Ep(1, true, 1, at: now.AddDays(-14)), Ep(2, air: now.AddDays(-1))]);
        check(
            Row(stalled, "unfinished").Single().Reason.Contains("S1E2"),
            "Smart lists: 14-day boundary includes aired next episode and a reason"
        );
        check(
            Row(Title([Ep(1, true, 1, at: now.AddDays(-14).AddSeconds(1)), Ep(2)]), "unfinished").Count == 0,
            "Smart lists: a gap under 14 days is excluded"
        );
        check(Row(stalled with { Dropped = true }, "unfinished").Count == 0, "Smart lists: Dropped shows are excluded");
        check(
            Row(Title([Ep(1, true, 1, at: now.AddDays(-30)), Ep(2, play: false)]), "unfinished").Count == 0,
            "Smart lists: unknown air date without availability is excluded"
        );
        check(
            Row(
                Title([Ep(1, true, 1, at: now.AddDays(-30)), Ep(2, play: false, air: now.AddDays(-1))]),
                "unfinished"
            ).Count == 1,
            "Smart lists: aired metadata-only next episode qualifies"
        );
        check(
            Row(
                Title([Ep(1, true, 1, at: now.AddDays(-30)), Ep(2, air: now.AddDays(1)), Ep(3, air: now.AddDays(-1))]),
                "unfinished"
            ).Count == 0,
            "Smart lists: never skip the future next episode to a later episode"
        );
        check(
            Row(Title([Ep(1)], true), "unfinished").Count == 0 && Row(Title([Ep(1)]), "unfinished").Count == 0,
            "Smart lists: unstarted titles and movies are not unfinished shows"
        );
        check(
            Row(Title([Ep(1), Ep(4, started: true, at: now.AddDays(-21)), Ep(5)]), "unfinished").Single().EpisodeId
                == "e4",
            "Smart lists: a stalled partial episode takes priority over unmarked earlier episodes"
        );
        check(
            Row(Title([Ep(1), Ep(4, true, 1, at: now.AddDays(-21)), Ep(5)]), "unfinished").Single().EpisodeId == "e5",
            "Smart lists: next follows actual progress instead of an older unmarked gap"
        );
        check(
            Row(Title([Ep(1, true, 2, at: now.AddDays(-1))], true), "comfort").Count == 1,
            "Smart lists: two completed watches qualify without a six-month wait"
        );
        check(
            Row(Title([Ep(1, true, 1), Ep(2, true, 1)]), "comfort").Count == 0,
            "Smart lists: two episodes watched once do not count as two title watches"
        );
        check(
            Row(Title([Ep(1, true, 1, 9, now.AddMonths(-6))], true), "comfort").Count == 1,
            "Smart lists: 4.5 stars at exactly six calendar months qualifies"
        );
        check(
            Row(Title([Ep(1, true, 1, 9, now.AddMonths(-6).AddSeconds(1))], true), "comfort").Count == 0
                && Row(Title([Ep(1, true, 1, 8, now.AddMonths(-7))], true), "comfort").Count == 0,
            "Smart lists: comfort age and half-star thresholds are strict"
        );
        check(
            Row(Title([Ep(1, false, 0, 10, now.AddMonths(-7))], true), "comfort").Count == 0,
            "Smart lists: rating alone without a watched title is insufficient"
        );
        var one = Title([Ep(1, true, 1, 8)], true, key: "one", directors: ["Director"]);
        var two = Title([Ep(1, true, 1, 8)], true, key: "two", directors: ["DIRECTOR"]);
        var third = Title([Ep(1)], true, key: "third", directors: ["Director"]);
        check(
            SmartLists.Build([one, two, third], now)["directors"].Count == 3,
            "Smart lists: two distinct four-star titles qualify a director case-insensitively"
        );
        check(
            SmartLists.Build([one, third], now)["directors"].Count == 0
                && SmartLists.Build([one with { Directors = [] }, two with { Directors = [] }], now)["directors"].Count
                    == 0,
            "Smart lists: fewer than two rated titles or an empty credits cache hides directors"
        );
        check(
            SmartLists.Build([one with { Directors = ["Director", "Director"] }, third], now)["directors"].Count == 0,
            "Smart lists: duplicate director credits never inflate rated-title counts"
        );
        check(
            Row(Title([Ep(1, min: 99.9)], true), "short-tonight").Count == 1
                && Row(Title([Ep(1, min: 100)], true), "short-tonight").Count == 0,
            "Smart lists: unwatched movie must be strictly under 100 minutes"
        );
        check(
            Row(Title([Ep(1, min: 29.9)]), "short-tonight").Count == 1
                && Row(Title([Ep(1, min: 30)]), "short-tonight").Count == 0,
            "Smart lists: next episode must be strictly under 30 minutes"
        );
        check(
            Row(Title([Ep(1, min: 0)]), "short-tonight").Count == 0
                && Row(Title([Ep(1, play: false)]), "short-tonight").Count == 0
                && Row(Title([Ep(1, air: now.AddDays(1))]), "short-tonight").Count == 0,
            "Smart lists: unknown runtime, unavailable and future episodes do not qualify as short tonight"
        );
        check(
            Row(Title([Ep(1, min: 80, started: true)], true), "short-tonight").Count == 0,
            "Smart lists: partially watched movies are not unwatched short picks"
        );
        var settings = new CatchUp();
        var show = Title([
            Ep(1, true, 1, at: now.AddDays(-18)),
            Ep(2, true, 1, at: now.AddDays(-17)),
            Ep(3, true, 1, at: now.AddDays(-14)),
            Ep(4, started: true),
            Ep(5),
        ]);
        var recap = settings.Build(show, now)!;
        check(
            recap.Episodes.Select(e => e.Episode).SequenceEqual(new[] { 2, 3 }) && recap.Stopped.Episode == 3,
            "Catch-up: at most the last two watched episodes, never an unwatched summary"
        );
        var partial = show with
        {
            Episodes =
            [
                Ep(1, true, 1, at: now.AddDays(-18)),
                Ep(2, true, 1, at: now.AddDays(-17)),
                Ep(3, started: true, at: now.AddDays(-14)),
                Ep(4, true, 1, at: now.AddDays(-30)),
            ],
        };
        check(
            settings.Build(partial, now)!.Episodes.Select(e => e.Episode).SequenceEqual(new[] { 1, 2 }),
            "Catch-up: a partial episode and previously watched later episodes cannot leak past the stopping point"
        );
        check(
            settings.Build(show with { Movie = true }, now) == null
                && settings.Build(show with { Dropped = true }, now) == null
                && new CatchUp { ThresholdDays = 0 }.Build(show, now) == null,
            "Catch-up: movies, Dropped shows and Off setting hide the card"
        );
        check(
            new CatchUp { ThresholdDays = 15 }.Build(show, now) == null
                && settings.Build(show with { Episodes = [Ep(1, true, 1, at: now.AddDays(-14).AddSeconds(1))] }, now)
                    == null,
            "Catch-up: configurable return threshold respects exact boundaries"
        );
        settings.Dismiss(recap);
        check(settings.Build(show, now) == null, "Catch-up: dismissed return does not show again");
        var dir = Path.Combine(root, "smart-lists-" + Guid.NewGuid().ToString("N"));
        settings.Save(dir);
        check(
            CatchUp.Load(dir).Build(show, now) == null,
            "Catch-up: dismissal survives reopening and remains profile-local"
        );
        var returned = show with
        {
            Episodes =
            [
                Ep(1, true, 1, at: now.AddDays(-18)),
                Ep(2, true, 1, at: now.AddDays(-17)),
                Ep(3, true, 2, at: now),
            ],
        };
        check(
            settings.Build(returned, now) == null && settings.Build(returned, now.AddDays(14)) != null,
            "Catch-up: new viewing allows exactly one new return after another gap"
        );
        CatchUp.SetThresholdDays(dir, 0);
        check(CatchUp.GetThresholdDays(dir) == 0, "Catch-up: persisted threshold setter supports disabling");
        var cached = new VideoItem { ImdbId = "tt123456", IsMovie = true };
        check(
            SmartLists.CachedCredits(dir, cached).Directors.Count == 0,
            "Smart lists: a missing credits cache returns no director without a request"
        );
        var cache = Path.Combine(dir, "credits");
        Directory.CreateDirectory(cache);
        var url = "https://v3-cinemeta.strem.io/meta/movie/tt123456.json";
        var file = Path.Combine(
            cache,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))
                + ".json"
        );
        File.WriteAllText(file, "{\"meta\":{\"cast\":[\"Actor only\"]}}");
        check(
            SmartLists.CachedCredits(dir, cached).Directors.Count == 0,
            "Smart lists: cached actors cannot become directors"
        );
        File.WriteAllText(file, "{\"meta\":{\"director\":[\"Director\"],\"cast\":[\"Actor only\"]}}");
        check(
            SmartLists.CachedCredits(dir, cached).Directors.Single().Name == "Director",
            "Smart lists: director roles are read only from the existing credits cache"
        );
        SmartLists.RememberStream(dir, "stream", true, now);
        check(
            SmartLists.StreamReady(dir, "stream", now)
                && !SmartLists.StreamReady("other-profile", "stream", now)
                && !SmartLists.StreamReady(dir, "stream", now.AddMinutes(31)),
            "Smart lists: streaming eligibility requires a recent confirmed source in this profile"
        );
        SmartLists.RememberStream(dir, "stream", false, now);
        check(
            !SmartLists.StreamReady(dir, "stream", now),
            "Smart lists: failed or unready stream checks clear eligibility"
        );
        var large = Enumerable.Range(0, 11000).Select(n => Title([Ep(1, min: 90)], true, key: n.ToString())).ToList();
        var enumerations = 0;
        IEnumerable<SmartTitle> Once()
        {
            foreach (var title in large)
            {
                enumerations++;
                yield return title;
            }
        }
        var result = SmartLists.Build(Once(), now);
        check(
            enumerations == 11000 && result["short-tonight"].Count == 11000,
            "Smart lists: all 11k titles classified in a single library pass"
        );
    }
}
