using System.Net;
using System.Net.Http;
using System.Text.Json;
using Noir;

static class StreamingSubtitleChecks
{
    sealed class Provider : HttpMessageHandler
    {
        public int Requests;
        public bool Invalid;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            var uri = request.RequestUri!;
            if (uri.AbsoluteUri.Contains("videoHash"))
                throw new Exception("Streaming must not use a local video's hash");
            var content =
                uri.Host == "opensubtitles-v3.strem.io"
                    ? JsonSerializer.Serialize(
                        new
                        {
                            subtitles = new[]
                            {
                                new
                                {
                                    lang = "eng",
                                    id = "en",
                                    url = "https://subs.example/en",
                                    subtitleFileName = "English.srt",
                                    movieReleaseName = "Show WEB",
                                    season = 1,
                                    episode = 2,
                                },
                                new
                                {
                                    lang = "ara",
                                    id = "ar",
                                    url = "https://subs.example/ar",
                                    subtitleFileName = "Arabic.srt",
                                    movieReleaseName = "Show WEB",
                                    season = 1,
                                    episode = 2,
                                },
                            },
                        }
                    )
                : Invalid ? "<html>Not a subtitle</html>"
                : "1\n00:00:01,000 --> 00:00:04,000\n" + (uri.AbsolutePath == "/ar" ? "مرحبا" : "Hello streaming");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
        }
    }

    public static async Task Run(string root, Action<bool, string> check)
    {
        var profile = Path.Combine(root, "stream-subtitles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        var item = new VideoItem
        {
            Id = "stream-episode",
            ImdbId = "tt123",
            Title = "Show",
            Season = 1,
            Episode = 2,
            Path = Path.Combine(profile, "not-downloaded.mkv"),
        };
        var target = SubtitleDownloads.StreamTarget(profile, item, "Show.WEB.mkv");
        using var provider = new Provider();
        using var http = new HttpClient(provider);
        var engine = new SubtitleDownloads(profile, http);
        var message = await engine.Fetch(
            item,
            "Show.WEB.mkv",
            ["en", "ar"],
            true,
            CancellationToken.None,
            streamTarget: target
        );
        var files = MobileSubtitles.Find(target, requireVideo: false);
        check(
            files.Count == 2 && !File.Exists(item.Path) && !File.Exists(target.Path),
            "Streaming downloads both subtitle languages without creating or downloading a video"
        );
        check(
            files.Any(s => File.ReadAllText(s.Path).Contains("مرحبا")),
            "Streaming subtitle cache preserves Arabic text"
        );
        check(
            message.Contains("timing unchanged") && !message.Contains("auto-synced"),
            "Streaming keeps original timing instead of analyzing a different local video"
        );
        var requests = provider.Requests;
        await engine.Fetch(item, "Show.WEB.mkv", ["en", "ar"], false, CancellationToken.None, streamTarget: target);
        check(provider.Requests == requests, "Saved streaming subtitles are reused without further network requests");
        check(
            SubtitleDownloads.StreamTarget(profile, item, "Show.WEB.mkv").Path == target.Path,
            "Reopening a release finds its saved streaming subtitles"
        );
        var different = SubtitleDownloads.StreamTarget(profile, item, "Show.BluRay.mkv");
        check(
            MobileSubtitles.Find(different, requireVideo: false).Count == 0 && different.Path != target.Path,
            "Switching encodes cannot reuse subtitles timed for another release"
        );
        File.WriteAllBytes(item.Path, new byte[131072]);
        provider.Invalid = true;
        await engine.Fetch(item, "Show.BluRay.mkv", ["en"], true, CancellationToken.None, streamTarget: different);
        check(
            MobileSubtitles.Find(different, requireVideo: false).Count == 0,
            "Invalid provider responses are rejected even when a different local video exists"
        );
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool stopped = false;
        try
        {
            await engine.Fetch(item, "cancelled", ["en"], false, cancelled.Token, streamTarget: different);
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }
        check(stopped, "Closing a streaming subtitle request cancels its work");
    }
}
