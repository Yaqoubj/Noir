using System.Text.Json;
using Noir;

static class StreamChecks
{
    public static void Run(Action<bool, string> check)
    {
        var c = new DownloadCredentials();
        using var doc = JsonDocument.Parse(
            """
            {"streams":[
             {"name":"[TB+] 1080p","url":"https://aio.example/play/1","behaviorHints":{"filename":"Show.S01E01.1080p.WEB.mkv","videoSize":2000000000},"streamData":{"type":"debrid","service":{"id":"torbox","cached":true}}},
             {"name":"[TB] 2160p","url":"https://aio.example/play/2","behaviorHints":{"filename":"Show.S01E01.2160p.mkv","videoSize":9000000000},"streamData":{"type":"debrid","service":{"id":"torbox","cached":false}}},
             {"name":"⚡ 1080p Remux","url":"https://aio.example/play/3","behaviorHints":{"filename":"Show.S01E01.1080p.BluRay.REMUX.mkv","videoSize":14000000000}},
             {"name":"P2P","url":"https://aio.example/p2p","streamData":{"type":"p2p"}},
             {"name":"Error","streamData":{"type":"error","error":{"description":"no results"}}},
             {"name":"Proxy","url":"https://proxy.example/x","behaviorHints":{"proxyHeaders":{"request":{"Authorization":"Bearer x"}}}},
             {"name":"UA","url":"https://proxy.example/ua","behaviorHints":{"filename":"Show.S01E01.720p.mkv","proxyHeaders":{"request":{"User-Agent":"Kodi"}}}},
             {"name":"magnet only","infoHash":"0123456789abcdef0123456789abcdef01234567"}
            ]}
            """
        );
        var parsed = StreamSources.Parse(doc.RootElement, c);
        using var badHeaders = JsonDocument.Parse(
            """{"streams":[{"url":"https://a/x","behaviorHints":{"proxyHeaders":{"request":{"User-Agent":42}}}},{"url":"https://a/y","behaviorHints":{"proxyHeaders":{"request":{"Referer":"value\r\ninjected"}}}}]}"""
        );
        check(
            StreamSources.Parse(badHeaders.RootElement, c).Count == 0,
            "Malformed stream headers are skipped instead of crashing playback discovery"
        );
        check(
            parsed.Count == 4,
            "Streams keep playable HTTP links and drop p2p, errors, magnets and unsupported headers"
        );
        check(parsed.Single(o => o.Url.EndsWith("/ua")).UserAgent == "Kodi", "A required user agent is passed to VLC");
        check(
            parsed.Single(o => o.Url.EndsWith("/1")).Cached
                && !parsed.Single(o => o.Url.EndsWith("/2")).Cached
                && parsed.Single(o => o.Url.EndsWith("/3")).Cached,
            "Ready flags come from AIOStreams' service data or its ⚡ marker"
        );
        var ranked = StreamSources.Rank(parsed, "1080p");
        check(ranked[0].Url.EndsWith("/1"), "A ready, comfortably sized stream in the preferred quality plays first");
        check(
            ranked.FindIndex(o => o.Url.EndsWith("/3")) < ranked.FindIndex(o => o.Url.EndsWith("/2")),
            "A ready remux still beats a stream that isn't ready"
        );
        check(
            ranked.FindIndex(o => o.Url.EndsWith("/1")) < ranked.FindIndex(o => o.Url.EndsWith("/3")),
            "Episode remuxes rank below lighter ready releases"
        );
        check(
            StreamSources
                .Rank(
                    [
                        new("https://a/1", "Movie.1080p.REMUX.mkv", "", "1080p", 20L << 30, true, "", "", " "),
                        new("https://a/2", "Movie.720p.mkv", "", "720p", 3L << 30, true, "", "", ""),
                    ],
                    "1080p",
                    movie: true
                )[0]
                .Url == "https://a/2",
            "Movies treat remuxes as heavy too"
        );
        check(
            StreamSources.Manifest(
                new() { Manifest = "https://a/noir/manifest.json", OriginalManifest = "https://a/mine/manifest.json" }
            ) == "https://a/mine/manifest.json",
            "Streaming uses the user's own AIOStreams profile, not the Noir download profile"
        );
    }
}
