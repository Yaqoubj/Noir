using System.Net;
using System.Net.Http.Headers;
using Noir;

// Download speed and time left: smoothing, formatting (English and Arabic), unknown sizes, and a real throttled transfer.
static class DownloadSpeedChecks
{
    public static async Task Run(string parent, Action<bool, string> check)
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var meter = new SpeedMeter();
        check(meter.Add(t0, 0) == 0, "A single sample has no speed yet");
        for (var s = 1; s <= 4; s++)
            meter.Add(t0.AddSeconds(s), s * 1_000_000L);
        check(Math.Abs(meter.Rate - 1_000_000) < 1, "Speed is the byte rate over the sampling window");
        for (var s = 5; s <= 12; s++)
            meter.Add(t0.AddSeconds(s), 4_000_000L + (s - 4) * 4_000_000L);
        check(
            Math.Abs(meter.Rate - 4_000_000) < 1,
            "Samples older than 5 seconds fall out so the speed follows a change"
        );
        meter.Reset();
        check(meter.Rate == 0, "Pausing resets the speed to zero");
        meter.Add(t0, 5000);
        meter.Add(t0.AddSeconds(1), 100);
        check(meter.Rate == 0, "A restarted byte counter never reports a negative speed");
        meter.Reset();
        meter.Add(t0, 0);
        meter.Add(t0.AddSeconds(.2), 500_000);
        check(meter.Rate == 0, "Two samples less than half a second apart are too noisy to report");
        check(DownloadEta.Seconds(0, 100, 1_000_000) == -1, "An unknown total size has no time left");
        check(DownloadEta.Seconds(1000, 0, 0) == -1, "A stalled transfer has no time left");
        check(DownloadEta.Seconds(1000, 1000, 5000) == -1, "A finished transfer has no time left");
        check(
            DownloadEta.Seconds(360_000_000, 0, 1_000_000) == 360,
            "Time left is the remaining bytes over the speed, rounded up"
        );
        check(
            DownloadEta.Left(360, false) == "6 min left" && DownloadEta.Left(4320, false) == "1 h 12 min left",
            "Time left reads like \"6 min left\" and \"1 h 12 min left\""
        );
        check(
            DownloadEta.Left(45, false) == "45 s left"
                && DownloadEta.Left(61, false) == "2 min left"
                && DownloadEta.Left(3599, false) == "1 h left"
                && DownloadEta.Left(93600, false) == "1 d 2 h left",
            "Seconds, rounding up to minutes, hour rollover and days read naturally"
        );
        check(
            DownloadEta.Left(0, false) == "1 s left" && DownloadEta.Left(-1, false) == "",
            "A known time is never \"0 s\" and an unknown time is empty"
        );
        check(
            DownloadEta.Left(360, true) == "باقي 6 د"
                && DownloadEta.Left(4320, true) == "باقي 1 س 12 د"
                && DownloadEta.Left(30, true) == "باقي 30 ث",
            "Time left has Arabic wording"
        );
        check(
            DownloadEta.Rate(4.2 * 1048576) == "4.2 MB/s"
                && DownloadEta.Rate(512 * 1024) == "512 KB/s"
                && DownloadEta.Rate(100) == "",
            "Speed reads in MB/s or KB/s and is blank below 1 KB/s"
        );
        check(
            DownloadEta.Transferred((long)(1.2 * 1073741824), (long)(3.4 * 1073741824)) == "1.2 of 3.4 GB"
                && DownloadEta.Transferred(512L * 1048576, 900L * 1048576) == "512 of 900 MB"
                && DownloadEta.Transferred(0, 0) == "",
            "Transferred reads \"1.2 of 3.4 GB\" and is blank when nothing is known"
        );

        var dir = Path.Combine(parent, "speed-tests");
        var media = Path.Combine(dir, "shows");
        Directory.CreateDirectory(media);
        var store = new LibraryStore(Path.Combine(dir, "profile"));
        store.State.Roots = [new() { Path = media, Movies = false }];
        store.State.Catalog.Clear();
        var item = new VideoItem
        {
            Id = "speed-target",
            CollectionKey = "show:speed",
            Title = "Speed",
            Year = 2026,
            Season = 1,
            Episode = 1,
            ImdbId = "tt1",
            Available = false,
        };
        store.State.Catalog[item.Id] = new() { Item = item };
        store.Save();
        var bytes = Enumerable.Range(0, 98304).Select(n => (byte)(n % 251)).ToArray();
        using var http = new HttpClient(new SlowHandler(bytes));
        using var downloads = new PcDownloads(store, new IdleProvider(), http, start: false);
        var job = new PcDownloadJob
        {
            Name = item.Label,
            Target = item,
            Root = media,
            Destination = DownloadNaming.Destination(media, item, "speed.mkv"),
            Size = bytes.Length,
            RemoteName = "speed.mkv",
        };
        var seen = new List<(double Speed, long Eta)>();
        downloads.Changed += () => seen.Add((job.Speed, job.EtaSeconds));
        await downloads.Transfer(job, "https://cdn.example.invalid/file", CancellationToken.None);
        check(File.ReadAllBytes(job.Destination).SequenceEqual(bytes), "A slow transfer still saves the exact file");
        check(
            seen.Any(x => x.Speed > 10_000 && x.Eta > 0),
            "A running transfer reports a smoothed speed and a time left"
        );
        check(job.Speed == 0 && job.EtaSeconds == -1, "A finished transfer clears its speed and time left");
        var copy = downloads.Jobs();
        check(
            copy.All(j => j.Speed == 0 && j.EtaSeconds == -1),
            "The queue copy carries the live figures but they are never saved"
        );
        var saved = System.Text.Json.JsonSerializer.Serialize(new PcDownloadJob { Speed = 5e6, EtaSeconds = 99 });
        check(
            !saved.Contains("Speed") && !saved.Contains("Eta"),
            "Speed and time left are not written to the queue file"
        );
    }

    sealed class IdleProvider : IDownloadProvider
    {
        public Task<List<IndexedRelease>> Search(VideoItem i, CancellationToken ct) =>
            Task.FromResult(new List<IndexedRelease>());

        public Task<List<TorboxFile>> Files(string h, CancellationToken ct) => Task.FromResult(new List<TorboxFile>());

        public Task<TorboxTorrent> Prepare(string h, long id, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string> Link(long id, long file, CancellationToken ct) => throw new NotSupportedException();
    }

    sealed class SlowHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = new StreamContent(new SlowStream(bytes));
            content.Headers.ContentLength = bytes.Length;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            response.Headers.ETag = new EntityTagHeaderValue("\"slow\"");
            return Task.FromResult(response);
        }
    }

    // Hands out 16 KB every 450 ms so the transfer loop has real time to measure.
    sealed class SlowStream(byte[] bytes) : Stream
    {
        int position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (position >= bytes.Length)
                return 0;
            await Task.Delay(450, ct);
            var n = Math.Min(Math.Min(16384, bytes.Length - position), buffer.Length);
            bytes.AsMemory(position, n).CopyTo(buffer);
            position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
