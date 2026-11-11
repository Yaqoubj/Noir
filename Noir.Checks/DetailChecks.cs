using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Noir;

public static class DetailChecks
{
    static JsonElement J(string json)
    {
        using var d = JsonDocument.Parse(json);
        return d.RootElement.Clone();
    }

    public static async Task Run(string root, Action<bool, string> check)
    {
        var cast = CreditsClient.ParseCinemeta(
            J(
                """{"meta":{"app_extras":{"cast":[{"name":"A Person","character":"Lead","photo":"https://example.test/a.jpg"}]},"credits_cast":[{"name":"A Person","photo":"https://example.test/b.jpg"},{"name":"Other","character":"Friend"}],"cast":["Third"],"director":["Director"],"writer":"Writer One, Writer Two"}}"""
            )
        );
        check(
            cast.Cast.Count == 3
                && cast.Cast[0].Character == "Lead"
                && cast.Cast[0].PhotoUrl == "https://example.test/a.jpg"
                && cast.Directors.Single().Name == "Director"
                && cast.Writers.Count == 2,
            "Detail: Cinemeta cast shapes merge in source order with directors and writers"
        );
        var keyed = CreditsClient.ParseCinemeta(
            J(
                """{"credits_cast":{"Named Person":{"character":"Role","image":{"original":"https://example.test/p.jpg"}}},"director":{"name":"Director"},"writer":[{"name":"Author"}] }"""
            )
        );
        check(
            keyed.Cast.Single().Name == "Named Person"
                && keyed.Cast[0].PhotoUrl.EndsWith("p.jpg")
                && keyed.Directors.Count == 1
                && keyed.Writers.Count == 1,
            "Detail: Cinemeta keyed cast and person objects parse"
        );
        var tv = CreditsClient.ParseTvmaze(
            J(
                """[{"person":{"id":12,"name":"A Person","image":{"original":"https://example.test/a.jpg"}},"character":{"name":"Lead"}}]"""
            )
        );
        check(
            tv.Cast.Single() is { TvmazeId: 12, Character: "Lead", PhotoUrl: "https://example.test/a.jpg" },
            "Detail: TVmaze cast keeps person identity, character and portrait"
        );
        var tmdb = CreditsClient.ParseTmdb(
            J(
                """{"cast":[{"id":3,"name":"Second","order":2,"profile_path":"/p.jpg"},{"id":2,"name":"First","order":0,"character":"Hero"}],"crew":[{"id":4,"name":"Director","job":"Director"},{"id":5,"name":"Writer","department":"Writing","job":"Screenplay"}]}"""
            )
        );
        check(
            tmdb.Cast.First().Name == "First"
                && tmdb.Cast[1].PhotoUrl == "https://image.tmdb.org/t/p/w185/p.jpg"
                && tmdb.Directors[0].TmdbId == 4
                && tmdb.Writers[0].TmdbId == 5,
            "Detail: TMDB credits order cast and parse writing and directing crew"
        );
        var film = CreditsClient.ParseTmdbFilmography(
            J(
                """{"cast":[{"id":8,"media_type":"tv","name":"Series","first_air_date":"2024-01-01"},{"id":9,"media_type":"movie","title":"Film","release_date":"2025-01-01","poster_path":"/film.jpg"}],"crew":[{"id":9,"media_type":"movie","title":"Film","release_date":"2025-01-01","job":"Director"}]}"""
            )
        );
        check(
            film.Count == 2 && film[0] is { Name: "Film", Year: 2025, IsMovie: true } && film[1].IsMovie == false,
            "Detail: filmography deduplicates titles and sorts newest first"
        );
        var television = CreditsClient.ParseTvmazeFilmography(
            J(
                """[{"_embedded":{"show":{"id":7,"name":"Show","premiered":"2018-01-01","externals":{"imdb":"tt77"},"image":{"original":"https://example.test/show.jpg"}}}}]"""
            )
        );
        check(
            television.Single() is { Name: "Show", ImdbId: "tt77", Year: 2018, IsMovie: false },
            "Detail: TVmaze filmography keeps IMDb title links"
        );
        var trailers = TrailersClient.ParseCinemeta(
            J(
                """{"meta":{"trailers":[{"source":"abcdefghijk","type":"Teaser"},{"source":"https://www.youtube.com/watch?v=12345678901","type":"Trailer"}],"trailerStreams":[{"ytId":"12345678901","title":"Duplicate"},{"ytId":"ABCDEFGHIJK","title":"A featurette"}]}}"""
            )
        );
        check(
            trailers.Count == 3
                && trailers[0].YouTubeId == "12345678901"
                && trailers[1].Kind == "Teaser"
                && trailers[2].Kind == "Featurette",
            "Detail: Cinemeta trailer IDs deduplicate and prefer trailer then teaser then extras"
        );
        var videos = TrailersClient.ParseTmdb(
            J(
                """{"results":[{"site":"YouTube","type":"Trailer","key":"abcdefghijk","official":false},{"site":"YouTube","type":"Trailer","key":"12345678901","official":true},{"site":"Vimeo","type":"Trailer","key":"ABCDEFGHIJK"},{"site":"YouTube","type":"Featurette","key":"ABCDEFGHIJK"}]}"""
            )
        );
        check(
            videos.Count == 3
                && videos[0].YouTubeId == "12345678901"
                && videos[0].Official
                && videos[2].Thumbnail == "https://i.ytimg.com/vi/ABCDEFGHIJK/hqdefault.jpg",
            "Detail: TMDB videos prefer official YouTube trailers and retain featurettes"
        );
        check(
            TrailersClient.YouTubeId("https://youtu.be/abcdefghijk?t=4") == "abcdefghijk"
                && TrailersClient.YouTubeId("https://www.youtube-nocookie.com/embed/12345678901") == "12345678901"
                && new[]
                {
                    "http://youtu.be/abcdefghijk",
                    "https://youtube.com.evil.test/watch?v=abcdefghijk",
                    "https://evil@youtube.com/watch?v=abcdefghijk",
                    "\";alert(1)//",
                    "abcd",
                }.All(x => TrailersClient.YouTubeId(x) == ""),
            "Detail: trailer ID parser rejects insecure, spoofed and injected URLs"
        );
        check(
            PreviewRules.StartSeconds(1000, 40, false, false) <= 40
                && PreviewRules.StartSeconds(1000, 0, false, false) == 0
                && PreviewRules.StartSeconds(1000, 0, false, true) == 200
                && PreviewRules.StartSeconds(1000, 0, true, false) == 200,
            "Detail: episode previews never pass unwatched progress while watched episodes and movies start at twenty percent"
        );
        check(
            PreviewSettings.Parse("{}") == PreviewMode.Trailer
                && PreviewSettings.Parse("{\"enabled\":false}") == PreviewMode.Off
                && PreviewSettings.Parse("{\"enabled\":true}") == PreviewMode.Trailer
                && PreviewSettings.Parse("{\"mode\":\"title itself\"}") == PreviewMode.Title,
            "Detail: preview settings default to Trailer and migrate legacy enable/title values"
        );
        check(
            new[] { "null", "[]", "7", "broken", "{\"mode\":null}", "{\"mode\":\"future-value\"}" }.All(s =>
                PreviewSettings.Parse(s) == PreviewMode.Trailer
            )
                && PreviewSettings.Parse("false") == PreviewMode.Off
                && PreviewSettings.Parse("\"self\"") == PreviewMode.Title,
            "Detail: malformed, unknown and legacy preview settings migrate safely"
        );
        check(
            PreviewRules.StartSeconds(double.NaN, 0, false, false) == 0
                && PreviewRules.StartSeconds(0, 0, true, false) == 0
                && PreviewRules.StartSeconds(1000, -8, false, false) == 0
                && PreviewRules.EndSeconds(1000, 40, false, false) == 40
                && PreviewRules.EndSeconds(1000, 0, true, false) == 1000,
            "Detail: preview start positions sanitize invalid duration and unwatched preview endings stop at progress"
        );
        var dir = Path.Combine(root, "detail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        check(PreviewSettings.Load(dir) == PreviewMode.Trailer, "Detail: missing preview settings use Trailer");
        PreviewSettings.Save(dir, PreviewMode.Off);
        check(PreviewSettings.Load(dir) == PreviewMode.Off, "Detail: preview setting survives saving and reopening");
        using var handler = new FixtureHandler();
        using (var client = new CreditsClient(dir, "", handler))
        {
            var first = await client.Title("tt123", true);
            var second = await client.Title("tt123", true);
            check(
                first.Cast.Count == 1 && second.Cast.Count == 1 && handler.Requests == 1,
                "Detail: repeated credit lookups use profile cache"
            );
            await client.Title("https://evil.test", true);
            check(handler.Requests == 1, "Detail: invalid IMDb identities never start requests");
        }
        var files = Directory.GetFiles(Path.Combine(dir, "credits"), "*.json");
        foreach (var file in files)
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-8));
        using (var offline = new CreditsClient(dir, "", new OfflineHandler()))
            check(
                (await offline.Title("tt123", true)).Cast.Count == 1,
                "Detail: network failures retain bounded stale credit cache"
            );
        using var auth = new AuthHandler();
        using (var client = new CreditsClient(Path.Combine(dir, "auth"), new string('a', 64), auth))
        {
            await client.Title("tt999", true);
            check(auth.CinemetaWithoutToken && auth.TmdbWithToken, "Detail: TMDB credentials are sent only to TMDB");
        }
        using var lazy = new PersonHandler();
        using (var client = new CreditsClient(Path.Combine(dir, "person"), "fixture-key", lazy))
        {
            var titles = await client.Person(new() { Name = "Fixture Person", TmdbId = 7 });
            check(
                titles.Count == 1 && lazy.Requests == 1,
                "Detail: person filmography never starts per-title requests"
            );
            var imdb = await client.ResolveImdb(titles.Single());
            await client.ResolveImdb(titles.Single());
            check(
                imdb == "tt777" && lazy.Requests == 2,
                "Detail: clicking a filmography title resolves only that IMDb identity and caches it"
            );
        }
    }

    sealed class FixtureHandler : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            if (request.RequestUri?.Scheme != "https")
                throw new Exception("HTTPS required");
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"meta":{"cast":["Cached Person"]}}""",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }

    sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("Offline fixture");
    }

    sealed class AuthHandler : HttpMessageHandler
    {
        public bool CinemetaWithoutToken,
            TmdbWithToken;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (r.RequestUri!.Host == "v3-cinemeta.strem.io")
                CinemetaWithoutToken = r.Headers.Authorization == null;
            else if (r.RequestUri.Host == "api.themoviedb.org")
                TmdbWithToken = r.Headers.Authorization?.Scheme == "Bearer";
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        r.RequestUri.Host == "api.themoviedb.org" ? "{\"movie_results\":[]}" : "{\"meta\":{}}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }

    sealed class PersonHandler : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Requests++;
            var json = r.RequestUri!.AbsolutePath.EndsWith("combined_credits")
                ? "{\"cast\":[{\"id\":77,\"media_type\":\"movie\",\"title\":\"Fixture Film\"}],\"crew\":[]}"
                : "{\"imdb_id\":\"tt777\"}";
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
