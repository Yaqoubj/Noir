using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Noir;

// Stremio account writer (Noir/StremioLibrary.cs) and watched bitfield (Noir/StremioWatchedBitField.cs).
// Every call goes to a fake handler; nothing here reaches api.strem.io or Cinemeta.
static class StremioLibraryChecks
{
    sealed class FakeStremio : HttpMessageHandler
    {
        public readonly Dictionary<string, string> Items = [];
        public readonly List<(string Method, JsonObject Body)> Calls = [];
        public Func<HttpRequestMessage, HttpResponseMessage?>? Override;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "api.strem.io")
                throw new InvalidOperationException("Unexpected host " + request.RequestUri);
            var method = request.RequestUri.AbsolutePath.Split('/')[^1];
            var body = (JsonObject)JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            Calls.Add((method, body));
            if (Override?.Invoke(request) is { } custom)
                return custom;
            if (method == "datastoreGet")
            {
                var ids = body["ids"]!.AsArray().Select(i => i!.GetValue<string>()).ToList();
                var found = ids.Where(Items.ContainsKey).Select(i => Items[i]);
                return Json("{\"result\":[" + string.Join(",", found) + "]}");
            }
            if (method == "datastorePut")
            {
                foreach (var change in body["changes"]!.AsArray())
                    Items[change!["_id"]!.GetValue<string>()] = change.ToJsonString();
                return Json("{\"result\":{\"success\":true}}");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        public int Puts => Calls.Count(c => c.Method == "datastorePut");

        public JsonObject LastPut => (JsonObject)Calls.Last(c => c.Method == "datastorePut").Body["changes"]![0]!;
    }

    static HttpResponseMessage Json(string text, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    const string Show = "tt2934286";

    // Cinemeta-style list, deliberately out of order (Stremio sorts by season, episode, release).
    static Task<IReadOnlyList<StremioVideo>> Videos(string id, CancellationToken ct, int from = 1)
    {
        var list = new List<StremioVideo>();
        for (int e = 9; e >= from; e--)
            list.Add(new($"{id}:1:{e}", 1, e, new DateTimeOffset(2020, 1, e, 0, 0, 0, TimeSpan.Zero)));
        return Task.FromResult<IReadOnlyList<StremioVideo>>(list);
    }

    public static async Task Run(string root, Action<bool, string> check)
    {
        Bitfield(check);
        await Writer(root, check);
        await Wiring(root, check);
    }

    static void Bitfield(Action<bool, string> check)
    {
        // Vectors from stremio-core's own tests: github.com/Stremio/stremio-core (branch development),
        // stremio-watched-bitfield/src/watched_bitfield.rs, tests parse_and_modify, construct_from_array,
        // to_string_empty, test_watched_field_de_serialize, deserialize_empty,
        // watched_field_anchor_points_to_last_watched_video.
        var nine = Enumerable.Range(1, 9).Select(i => $"tt2934286:1:{i}").ToList();
        const string parseAndModify = "tt2934286:1:5:5:eJyTZwAAAEAAIA==";
        var wb = StremioWatchedBitField.Parse(parseAndModify, nine);
        var asText = wb.ToString();
        wb.MarkWatched("tt2934286:1:6");
        check(
            StremioWatchedBitField.Parse(parseAndModify, nine).IsWatched("tt2934286:1:5")
                && !StremioWatchedBitField.Parse(parseAndModify, nine).IsWatched("tt2934286:1:6")
                && asText == parseAndModify
                && wb.IsWatched("tt2934286:1:6"),
            "Stremio bitfield: stremio-core parse_and_modify vector (5 of 9 watched, re-encodes byte for byte, set 6)"
        );
        var ids499 = Enumerable.Range(1, 499).Select(i => $"tt2934286:1:{i}").ToList();
        var blank = StremioWatchedBitField.FromArray(new bool[500], ids499);
        var allClear = Enumerable.Range(0, ids499.Count).All(i => !blank[i] && !blank.IsWatched(ids499[i]));
        var alternate = StremioWatchedBitField.FromArray(
            Enumerable.Range(0, ids499.Count).Select(i => i % 2 == 0).ToList(),
            ids499
        );
        var back = StremioWatchedBitField.Parse(alternate.ToString(), ids499);
        check(
            allClear
                && Enumerable
                    .Range(0, ids499.Count)
                    .All(i => back[i] == (i % 2 == 0) && back.IsWatched(ids499[i]) == (i % 2 == 0)),
            "Stremio bitfield: stremio-core construct_from_array vector (499 videos, every other one, round trip)"
        );
        check(
            StremioWatchedBitField.Empty([]).ToString() == "undefined:1:eJwDAAAAAAE=",
            "Stremio bitfield: stremio-core to_string_empty vector encodes as undefined:1:eJwDAAAAAAE="
        );
        var empty = StremioWatchedBitField.Parse("undefined:1:eJwDAAAAAAE=", []);
        check(
            empty.Length == 0 && empty.SetBits == 0,
            "Stremio bitfield: stremio-core deserialize_empty vector gives an empty field"
        );
        // test_watched_field_de_serialize: anchor "tt7767422:3:8", anchor_length 24, bitfield "eJz7//8/AAX9Av4=".
        const string deSerialize = "tt7767422:3:8:24:eJz7//8/AAX9Av4=";
        var show24 = Enumerable.Range(0, 24).Select(i => i == 23 ? "tt7767422:3:8" : $"tt7767422:x:{i}").ToList();
        var field24 = StremioWatchedBitField.Parse(deSerialize, show24);
        check(
            StremioWatchedBitField.Compress(StremioWatchedBitField.Decompress("eJz7//8/AAX9Av4=")) == "eJz7//8/AAX9Av4="
                && field24.IsWatched("tt7767422:3:8")
                && field24.ToString() == deSerialize,
            "Stremio bitfield: stremio-core test_watched_field_de_serialize vector (anchor tt7767422:3:8, length 24)"
        );
        var twenty = Enumerable.Range(1, 20).Select(i => $"tt0:1:{i}").ToList();
        var anchored = StremioWatchedBitField.Empty(twenty);
        anchored.MarkWatched("tt0:1:16");
        check(
            anchored.ToString().StartsWith("tt0:1:16:16:"),
            "Stremio bitfield: stremio-core anchor vector (tt0:1:16 watched starts with tt0:1:16:16:)"
        );
        // bitfield8.rs tests test_de_serialize / parse_length: "eJyTZwAAAEAAIA==" is the bytes [31, 0].
        check(
            StremioWatchedBitField.Decompress("eJyTZwAAAEAAIA==").SequenceEqual(new byte[] { 31, 0 })
                && StremioWatchedBitField.Compress([31, 0]) == "eJyTZwAAAEAAIA==",
            "Stremio bitfield: stremio-core bitfield8 vector eJyTZwAAAEAAIA== is bytes [31, 0] both ways"
        );
        // Realigning: three specials sorted in front move the anchor from index 4 to 7 (offset -3).
        var withSpecials = new[] { "tt2934286:0:1", "tt2934286:0:2", "tt2934286:0:3" }.Concat(nine).ToList();
        var moved = StremioWatchedBitField.Parse(parseAndModify, withSpecials);
        var shortList = nine.Skip(2).ToList();
        var lost = StremioWatchedBitField.Parse(parseAndModify, shortList);
        check(
            Enumerable.Range(1, 5).All(i => moved.IsWatched($"tt2934286:1:{i}"))
                && !moved.IsWatched("tt2934286:0:3")
                && !moved.LostRemoteBits
                && lost.LostRemoteBits
                && StremioWatchedBitField.Parse("tt9:1:1:1:eJyTZwAAAEAAIA==", nine).LostRemoteBits,
            "Stremio bitfield: realigns when episodes are added before the anchor and reports bits it would lose"
        );
        check(
            StremioLibrary.DecodeWatched(parseAndModify, nine).SequenceEqual(nine.Take(5))
                && StremioLibrary.DecodeWatched(moved.ToString(), withSpecials).SequenceEqual(nine.Take(5)),
            "Stremio bitfield: the import's existing decoder reads Noir's encoding the same way"
        );
        var order = StremioWatchedBitField.Order([
            new("s:2:1", 2, 1, null),
            new("s:1:2", 1, 2, null),
            new("s:0:1", 0, 1, null),
            new("s:1:1b", 1, 1, new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new("s:1:1a", 1, 1, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new("x", null, null, null),
        ]);
        using var meta = JsonDocument.Parse(
            "{\"meta\":{\"videos\":[{\"id\":\"s:1:2\",\"season\":1,\"episode\":2},{\"id\":\"s:1:1\",\"season\":1,\"number\":1,\"released\":\"2020-01-01T00:00:00.000Z\"}]}}"
        );
        check(
            order.SequenceEqual(["x", "s:0:1", "s:1:1a", "s:1:1b", "s:1:2", "s:2:1"])
                && StremioWatchedBitField
                    .Order(StremioLibraryWriter.ParseVideos(meta.RootElement))
                    .SequenceEqual(["s:1:1", "s:1:2"]),
            "Stremio bitfield: videos ordered like stremio-core (season, episode, release; missing first)"
        );
    }

    static async Task Writer(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "stremio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var now = new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
        var watchedAt = new DateTimeOffset(2026, 11, 10, 21, 30, 0, TimeSpan.Zero);
        var fake = new FakeStremio();
        var options = new StremioMarkOptions { Enabled = true };
        var writer = new StremioLibraryWriter(
            folder,
            fake,
            (id, ct) => Videos(id, ct),
            options,
            () => "key-12345678",
            () => now
        );
        var movie = new VideoItem
        {
            ImdbId = "tt0111161",
            IsMovie = true,
            Title = "Fixture Movie",
        };
        const string movieRemote =
            "{\"_id\":\"tt0111161\",\"name\":\"Fixture Movie\",\"type\":\"movie\",\"removed\":true,\"temp\":true,\"_ctime\":\"2020-01-01T00:00:00.000Z\",\"_mtime\":\"2020-01-02T00:00:00.000Z\",\"futureField\":{\"nested\":[1,2.50,\"x\"]},\"state\":{\"timesWatched\":0,\"flaggedWatched\":0,\"lastWatched\":\"2019-05-05T00:00:00.000Z\",\"timeOffset\":1234,\"stateExtra\":true},\"behaviorHints\":{\"defaultVideoId\":\"tt0111161\"}}";

        var off = new StremioLibraryWriter(folder, fake, (id, ct) => Videos(id, ct), null, () => "key-12345678");
        var offResult = await off.MarkWatched(movie, watchedAt, default);
        check(
            !new StremioMarkOptions().Enabled
                && !new StremioMarkOptions().AddWatchedTitles
                && offResult.Status == StremioMarkStatus.Off
                && fake.Calls.Count == 0,
            "Stremio mark: off by default (and the add-to-library option too); off makes no calls"
        );

        fake.Items["tt0111161"] = movieRemote;
        var marked = await writer.MarkWatched(movie, watchedAt, default);
        var put = fake.LastPut;
        var state = put["state"]!.AsObject();
        check(
            marked.Status == StremioMarkStatus.Marked
                && fake.Calls.Select(c => c.Method).SequenceEqual(["datastoreGet", "datastorePut"])
                && fake.Calls[0].Body["ids"]!.AsArray().Single()!.GetValue<string>() == "tt0111161"
                && fake.Calls.All(c => c.Body["authKey"]!.GetValue<string>() == "key-12345678")
                && put["futureField"]!.ToJsonString() == "{\"nested\":[1,2.50,\"x\"]}"
                && state["stateExtra"]!.GetValue<bool>()
                && state["timeOffset"]!.GetValue<int>() == 1234
                && put["removed"]!.GetValue<bool>()
                && put["temp"]!.GetValue<bool>()
                && put["_ctime"]!.GetValue<string>() == "2020-01-01T00:00:00.000Z"
                && put["_mtime"]!.GetValue<string>() == "2026-11-11T10:00:00.000Z"
                && state["timesWatched"]!.GetValue<int>() == 1
                && state["flaggedWatched"]!.GetValue<int>() == 1
                && state["lastWatched"]!.GetValue<string>() == "2026-11-10T21:30:00.000Z",
            "Stremio mark: movie read-modify-write keeps unknown fields, remote removed/temp, sets _mtime and watched"
        );
        var original = Path.Combine(folder, "stremio-originals", "tt0111161.json");
        check(
            File.Exists(original) && File.ReadAllText(original) == movieRemote,
            "Stremio mark: the remote item is saved under stremio-originals before the first write"
        );
        fake.Calls.Clear();
        var again = await writer.MarkWatched(movie, watchedAt, default);
        var read = await writer.IsWatched(movie, default);
        check(
            again.Status == StremioMarkStatus.AlreadyWatched
                && read.State == StremioWatchedState.Watched
                && fake.Puts == 0,
            "Stremio mark: an already watched movie is read as watched and never rewritten"
        );

        fake.Calls.Clear();
        fake.Items["tt0111161"] = fake.Items["tt0111161"].Replace("\"removed\":true", "\"removed\":false");
        var restored = await writer.Restore("tt0111161", default);
        var back = fake.LastPut;
        check(
            restored.Status == StremioMarkStatus.Restored
                && back["state"]!["timesWatched"]!.GetValue<int>() == 0
                && back["futureField"]!.ToJsonString() == "{\"nested\":[1,2.50,\"x\"]}"
                && !back["removed"]!.GetValue<bool>()
                && back["temp"]!.GetValue<bool>()
                && back["_mtime"]!.GetValue<string>() == "2026-11-11T10:00:00.000Z"
                && File.ReadAllText(original) == movieRemote,
            "Stremio restore: puts the saved original back with a new _mtime and today's removed/temp"
        );

        fake.Calls.Clear();
        var absent = new VideoItem
        {
            ImdbId = "tt0000001",
            IsMovie = true,
            Title = "Absent",
        };
        var skipped = await writer.MarkWatched(absent, watchedAt, default);
        var absentRead = await writer.IsWatched(absent, default);
        check(
            skipped.Status == StremioMarkStatus.NotInLibrary
                && skipped.Failure == StremioFailure.Title
                && skipped.English == "Not in your Stremio library"
                && absentRead.State == StremioWatchedState.NotInLibrary
                && fake.Puts == 0
                && !fake.Items.ContainsKey("tt0000001")
                && !writer.HasOriginal("tt0000001"),
            "Stremio mark: a title the account lacks is skipped as not in your Stremio library"
        );
        options.AddWatchedTitles = true;
        var added = await writer.MarkWatched(absent, watchedAt, default);
        var addedItem = fake.LastPut;
        options.AddWatchedTitles = false;
        check(
            added.Status == StremioMarkStatus.Added
                && addedItem["_id"]!.GetValue<string>() == "tt0000001"
                && addedItem["type"]!.GetValue<string>() == "movie"
                && !addedItem["removed"]!.GetValue<bool>()
                && addedItem["state"]!["timesWatched"]!.GetValue<int>() == 1
                && writer.HasOriginal("tt0000001"),
            "Stremio mark: with add watched titles on, a missing title is added as watched"
        );
        var removedAdded = await writer.Restore("tt0000001", default);
        check(
            removedAdded.Status == StremioMarkStatus.Restored && fake.LastPut["removed"]!.GetValue<bool>(),
            "Stremio restore: a title Noir added is marked removed from the library"
        );

        // Restore leaves a title alone when Stremio changed it after Noir's last write (its _mtime is newer).
        string Plain(string id) =>
            "{\"_id\":\""
            + id
            + "\",\"name\":\"R\",\"type\":\"movie\",\"removed\":false,\"temp\":false,\"_mtime\":\"2020-01-02T00:00:00.000Z\",\"state\":{\"timesWatched\":0,\"flaggedWatched\":0}}";
        foreach (var id in new[] { "tt0000201", "tt0000202", "tt0000203" })
        {
            fake.Items[id] = Plain(id);
            await writer.MarkWatched(
                new VideoItem
                {
                    ImdbId = id,
                    IsMovie = true,
                    Title = "R",
                },
                watchedAt,
                default
            );
        }
        // Watched again in Stremio an hour after Noir's write.
        string Touched(string id) =>
            fake.Items[id]
                .Replace("\"_mtime\":\"2026-11-11T10:00:00.000Z\"", "\"_mtime\":\"2026-11-11T11:00:00.000Z\"");
        fake.Items["tt0000202"] = Touched("tt0000202");
        fake.Items["tt0000203"] = Touched("tt0000203");
        var touched202 = fake.Items["tt0000202"];
        now = now.AddHours(2);
        fake.Calls.Clear();
        var single = await writer.Restore("tt0000202", default);
        check(
            single.Status == StremioMarkStatus.ChangedSince
                && fake.Puts == 0
                && fake.Items["tt0000202"] == touched202
                && writer.HasOriginal("tt0000202"),
            "Stremio restore: a title changed in Stremio after Noir's write is left as it is"
        );
        fake.Calls.Clear();
        var batch = await writer.Restore(["tt0000201", "tt0000202", "tt0000203"], default);
        var arabicText = batch.Text(true);
        check(
            batch.Restored == 1
                && batch.Kept == 2
                && batch.Failed.Count == 0
                && fake.Puts == 1
                && fake.LastPut["_id"]!.GetValue<string>() == "tt0000201"
                && fake.LastPut["state"]!["timesWatched"]!.GetValue<int>() == 0
                && fake.Items["tt0000202"] == touched202
                && batch.Text(false) == "1 restored, 2 left as they are because they changed in Stremio since"
                && arabicText == "استُعيد عنصر واحد، وتُرك عنصران كما هما لأنهما تغيّرا في Stremio منذ ذلك الحين",
            "Stremio restore: a mixed batch restores the untouched title only and says how many were left as they are"
        );
        fake.Calls.Clear();
        var again201 = await writer.Restore(["tt0000201"], default);
        check(
            again201.Restored == 1 && again201.Text(false) == "1 restored",
            "Stremio restore: an untouched title (including one Noir restored itself) is restored"
        );
        now = now.AddHours(-2);

        // Series: remote field = stremio-core parse_and_modify vector (episodes 1-5 watched).
        const string seriesRemote =
            "{\"_id\":\"tt2934286\",\"name\":\"Fixture Show\",\"type\":\"series\",\"removed\":false,\"temp\":false,\"_mtime\":\"2020-01-02T00:00:00.000Z\",\"extra\":[{}],\"state\":{\"watched\":\"tt2934286:1:5:5:eJyTZwAAAEAAIA==\",\"video_id\":\"tt2934286:1:5\",\"timesWatched\":0,\"lastWatched\":\"2027-01-01T00:00:00.000Z\"}}";
        fake.Items[Show] = seriesRemote;
        fake.Calls.Clear();
        var episode7 = new VideoItem
        {
            ImdbId = Show,
            Season = 1,
            Episode = 7,
            Title = "Fixture Show",
        };
        var series = await writer.MarkWatched(episode7, watchedAt, default);
        var seriesPut = fake.LastPut;
        var nine = Enumerable.Range(1, 9).Select(i => $"{Show}:1:{i}").ToList();
        var written = StremioWatchedBitField.Parse(seriesPut["state"]!["watched"]!.GetValue<string>(), nine);
        check(
            series.Status == StremioMarkStatus.Marked
                && seriesPut["state"]!["watched"]!.GetValue<string>().StartsWith($"{Show}:1:7:7:")
                && new[] { 1, 2, 3, 4, 5, 7 }.All(e => written.IsWatched($"{Show}:1:{e}"))
                && !written.IsWatched($"{Show}:1:6")
                && seriesPut["state"]!["video_id"]!.GetValue<string>() == $"{Show}:1:5"
                && seriesPut["state"]!["lastWatched"]!.GetValue<string>() == "2027-01-01T00:00:00.000Z"
                && seriesPut["extra"]!.ToJsonString() == "[{}]",
            "Stremio mark: series sets the episode bit, keeps every watched bit and a newer lastWatched"
        );
        var episode2 = new VideoItem
        {
            ImdbId = Show,
            Season = 1,
            Episode = 2,
        };
        fake.Calls.Clear();
        var already = await writer.MarkWatched(episode2, watchedAt, default);
        var episode8 = new VideoItem
        {
            ImdbId = Show,
            Season = 1,
            Episode = 8,
        };
        await writer.MarkWatched(episode8, watchedAt, default);
        check(
            already.Status == StremioMarkStatus.AlreadyWatched
                && fake.Puts == 1
                && File.ReadAllText(Path.Combine(folder, "stremio-originals", Show + ".json")) == seriesRemote,
            "Stremio mark: a watched episode is not rewritten and the original is saved only once"
        );
        var shorter = new StremioLibraryWriter(
            folder,
            fake,
            (id, ct) => Videos(id, ct, 3),
            options,
            () => "key-12345678"
        );
        fake.Calls.Clear();
        var outOfStep = await shorter.MarkWatched(
            new VideoItem
            {
                ImdbId = Show,
                Season = 1,
                Episode = 9,
            },
            watchedAt,
            default
        );
        var unknownEpisode = await writer.MarkWatched(
            new VideoItem
            {
                ImdbId = Show,
                Season = 4,
                Episode = 1,
            },
            watchedAt,
            default
        );
        check(
            outOfStep.Status == StremioMarkStatus.EpisodesOutOfStep
                && unknownEpisode.Status == StremioMarkStatus.UnknownEpisode
                && fake.Puts == 0,
            "Stremio mark: bits are never cleared (a list that would drop watched episodes writes nothing)"
        );

        // Failure classes for the queue: site-level backs off the whole account, per-title skips one title.
        async Task<StremioMarkResult> With(
            Func<HttpRequestMessage, HttpResponseMessage?> respond,
            Func<string?>? key = null
        )
        {
            fake.Override = respond;
            var w = new StremioLibraryWriter(
                folder,
                fake,
                (id, ct) => Videos(id, ct),
                options,
                key ?? (() => "key-12345678")
            );
            try
            {
                return await w.MarkWatched(movie, watchedAt, default);
            }
            finally
            {
                fake.Override = null;
            }
        }
        var server = await With(_ => Json("{}", HttpStatusCode.BadGateway));
        var unauthorized = await With(_ => Json("{}", HttpStatusCode.Unauthorized));
        var session = await With(_ => Json("{\"error\":{\"message\":\"Session does not exist\",\"code\":1}}"));
        var network = await With(_ => throw new HttpRequestException("offline"));
        var garbage = await With(_ => Json("<html>"));
        var noKey = await With(_ => null, () => null);
        var badId = await writer.MarkWatched(new VideoItem { ImdbId = "local-1", IsMovie = true }, watchedAt, default);
        check(
            server is { Status: StremioMarkStatus.Server, Failure: StremioFailure.Site }
                && unauthorized is { Status: StremioMarkStatus.Auth, Failure: StremioFailure.Site }
                && session is { Status: StremioMarkStatus.Auth, Failure: StremioFailure.Site }
                && network is { Status: StremioMarkStatus.Network, Failure: StremioFailure.Site }
                && garbage is { Status: StremioMarkStatus.BadResponse, Failure: StremioFailure.Site }
                && noKey is { Status: StremioMarkStatus.NoKey, Failure: StremioFailure.Site }
                && badId is { Status: StremioMarkStatus.UnknownId, Failure: StremioFailure.Title }
                && skipped.Failure == StremioFailure.Title
                && unknownEpisode.Failure == StremioFailure.Title
                && outOfStep.Failure == StremioFailure.Title
                && marked.Failure == StremioFailure.None
                && new[] { server, network, badId, skipped }.All(r => r.Arabic.Length > 0 && r.English != r.Arabic),
            "Stremio mark: failures classified site-level (network, 5xx, auth, no key) vs per-title"
        );

        var keyFolder = Path.Combine(folder, "key");
        Directory.CreateDirectory(keyFolder);
        File.WriteAllBytes(
            Path.Combine(keyFolder, "stremio.dpapi"),
            ProtectedData.Protect(Encoding.UTF8.GetBytes("stored-key-123"), null, DataProtectionScope.CurrentUser)
        );
        check(
            StremioLibraryWriter.StoredKey(keyFolder) == "stored-key-123"
                && StremioLibraryWriter.StoredKey(folder) == null
                && !File.Exists(Path.Combine(folder, "stremio.dpapi")),
            "Stremio mark: reuses the import's stored Stremio key (read only) and stores no password"
        );
    }

    // The "Stremio" provider of the watch-sync queue (CommunityQueue + StremioSync), against the fake account.
    static async Task Wiring(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "stremio-wire-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var t0 = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        var clock = t0;
        var fake = new FakeStremio();
        var videoCalls = 0;
        var cache = new StremioVideoCache(
            (id, ct) =>
            {
                videoCalls++;
                return Videos(id, ct);
            },
            TimeSpan.FromHours(12),
            () => clock
        );
        var writer = new StremioLibraryWriter(folder, fake, cache.Get, null, () => "key-12345678", () => clock);
        VideoItem Movie(string id, string imdb) =>
            new()
            {
                Id = id,
                ImdbId = imdb,
                IsMovie = true,
                Title = "Film " + id,
            };
        VideoItem Episode(string id, int episode) =>
            new()
            {
                Id = id,
                ImdbId = Show,
                Season = 1,
                Episode = episode,
                Title = "Fixture Show",
            };
        static string MovieItem(string imdb, int times) =>
            "{\"_id\":\""
            + imdb
            + "\",\"type\":\"movie\",\"removed\":false,\"temp\":false,\"extra\":7,\"state\":{\"timesWatched\":"
            + times
            + "}}";

        // Off by default, persisted in the library with the queue's state.
        var fresh = new PlayerState();
        var defaults =
            !CommunityQueue.StremioMarkOn(fresh)
            && !CommunityQueue.StremioAddTitles(fresh)
            && !CommunityQueue.Enabled(fresh, CommunityQueue.Stremio)
            && CommunityQueue.Providers.Contains(CommunityQueue.Stremio);
        CommunityQueue.SetStremio(fresh, true, true);
        var reloaded = JsonSerializer.Deserialize<PlayerState>(JsonSerializer.Serialize(fresh))!;
        CommunityQueue.SetStremio(reloaded, null, false);
        check(
            defaults
                && CommunityQueue.Enabled(reloaded, CommunityQueue.Stremio)
                && !CommunityQueue.StremioAddTitles(reloaded)
                && CommunityQueue.StremioAddTitles(fresh),
            "Stremio wiring: both switches are off by default and persist with the library"
        );

        // S3: switching on queues none of the past; new watches are queued from then on, never while off.
        var state = new PlayerState();
        foreach (var n in Enumerable.Range(0, 5))
            state.Catalog["old" + n] = new CatalogEntry
            {
                Item = Movie("old" + n, "tt10" + n),
                Progress = new Bookmark { Completed = true, UpdatedAt = t0.AddDays(-n - 1) },
            };
        var offMovie = Movie("before", "tt0000100");
        CommunityQueue.Enqueue(state, offMovie, t0);
        var queuedWhileOff = state.WatchSync.ContainsKey("Stremio:before");
        CommunityQueue.SetStremio(state, true, null);
        var queuedOnSwitch = state.WatchSync.Values.Count(j => j.Provider == CommunityQueue.Stremio);
        var items = new Dictionary<string, VideoItem>();
        void Watch(VideoItem item, DateTimeOffset at)
        {
            items[item.Id] = item;
            state.Catalog[item.Id] = new CatalogEntry
            {
                Item = item,
                Progress = new Bookmark { Completed = true, UpdatedAt = at },
            };
            CommunityQueue.Enqueue(state, item, at);
        }
        Watch(Movie("m1", "tt0000101"), t0.AddMinutes(1));
        check(
            !queuedWhileOff
                && queuedOnSwitch == 0
                && state.WatchSync.ContainsKey("Stremio:m1")
                && CommunityQueue.PlanBacklog(state, CommunityQueue.Stremio).Titles == 5,
            "Stremio wiring: switching it on sends only new watches; past history waits for its own button"
        );

        var backoff = new CommunityBackoff(state.SyncPace);
        var arabic = false;
        Task Pass() =>
            CommunityQueue.Pass(
                state,
                backoff,
                () => clock,
                j => items.TryGetValue(j.ItemId, out var i) ? i.ImdbId + "|" + i.Season + "|" + i.Episode : "",
                j => state.WatchSync.GetValueOrDefault(j.Provider + ":" + j.ItemId) == j,
                j => StremioSync.Send(writer, state, items[j.ItemId], j, arabic, default, cache),
                () => { },
                _ => Task.CompletedTask
            );

        fake.Items["tt0000101"] = MovieItem("tt0000101", 0);
        await Pass();
        var m1 = state.WatchSync["Stremio:m1"];
        check(
            m1.SyncedAt != null
                && fake.Puts == 1
                && fake.LastPut["extra"]!.GetValue<int>() == 7
                && fake.LastPut["state"]!["timesWatched"]!.GetValue<int>() == 1
                && writer.HasOriginal("tt0000101")
                && backoff.Failures(CommunityQueue.Stremio) == 0,
            "Stremio wiring: a watched movie is marked in the account through the queue (read-modify-write, original kept)"
        );

        fake.Calls.Clear();
        fake.Items["tt0000102"] = MovieItem("tt0000102", 3);
        Watch(Movie("m2", "tt0000102"), t0.AddMinutes(2));
        clock = clock.AddMinutes(1);
        await Pass();
        check(
            state.WatchSync["Stremio:m2"].SyncedAt != null && fake.Puts == 0 && fake.Calls.Count == 1,
            "Stremio wiring: already watched in Stremio counts as done with no write"
        );

        // Title failure: only that title waits (English or Arabic text), the provider does not back off.
        fake.Calls.Clear();
        Watch(Movie("m3", "tt0000103"), t0.AddMinutes(3));
        Watch(Movie("m4", "tt0000104"), t0.AddMinutes(2.5));
        fake.Items["tt0000104"] = MovieItem("tt0000104", 0);
        arabic = true;
        clock = clock.AddMinutes(1);
        await Pass();
        arabic = false;
        var m3 = state.WatchSync["Stremio:m3"];
        check(
            m3.SyncedAt == null
                && m3.BlockedKey.Length > 0
                && m3.Message == "ليس في مكتبة Stremio"
                && state.WatchSync["Stremio:m4"].SyncedAt != null
                && backoff.Failures(CommunityQueue.Stremio) == 0
                && !fake.Items.ContainsKey("tt0000103"),
            "Stremio wiring: a title not in the library is skipped with its English/Arabic reason; the next title still goes"
        );
        clock = clock.AddMinutes(1);
        fake.Calls.Clear();
        await Pass();
        var parked = fake.Calls.Count == 0;
        CommunityQueue.SetStremio(state, null, true);
        CommunityQueue.Retry(state, backoff);
        clock = clock.AddMinutes(1);
        await Pass();
        CommunityQueue.SetStremio(state, null, false);
        check(
            parked
                && state.WatchSync["Stremio:m3"].SyncedAt != null
                && fake.Items.ContainsKey("tt0000103")
                && fake.LastPut["_id"]!.GetValue<string>() == "tt0000103",
            "Stremio wiring: a skipped title waits for Retry; with the add option on it is added as watched"
        );

        // Site failure: the provider stops its pass and backs off; sign-in problems ask to connect again.
        Watch(Movie("m5", "tt0000105"), t0.AddMinutes(5));
        Watch(Movie("m6", "tt0000106"), t0.AddMinutes(4));
        fake.Calls.Clear();
        fake.Override = _ => Json("{\"error\":{\"message\":\"Session does not exist\",\"code\":1}}");
        clock = clock.AddMinutes(1);
        await Pass();
        fake.Override = null;
        var m5 = state.WatchSync["Stremio:m5"];
        check(
            m5.SyncedAt == null
                && fake.Calls.Count == 1
                && backoff.Failures(CommunityQueue.Stremio) == 1
                && m5.RetryAt == clock + CommunityBackoff.Steps[0]
                && state.WatchSync["Stremio:m6"].Attempts == 0
                && StremioSync.NeedsReconnect(state)
                && StremioSync.IsReconnect("انتهت جلسة Stremio. اربط Stremio مجددًا"),
            "Stremio wiring: a sign-in failure backs the whole account off, stops the pass and asks to Connect Stremio again"
        );
        var noKey = new StremioLibraryWriter(folder, fake, cache.Get, null, () => null, () => clock);
        var noKeyText = "";
        try
        {
            await StremioSync.Send(noKey, state, items["m6"], state.WatchSync["Stremio:m6"], false, default);
        }
        catch (SyncSiteException e)
        {
            noKeyText = e.Message;
        }
        fake.Calls.Clear();
        clock = clock.AddMinutes(2);
        await Pass();
        var waited = fake.Calls.Count == 0;
        fake.Items["tt0000105"] = MovieItem("tt0000105", 0);
        fake.Items["tt0000106"] = MovieItem("tt0000106", 0);
        clock = clock.AddMinutes(5);
        await Pass();
        check(
            noKeyText == "Connect Stremio first"
                && StremioSync.IsReconnect(noKeyText)
                && waited
                && state.WatchSync["Stremio:m5"].SyncedAt != null
                && state.WatchSync["Stremio:m6"].SyncedAt != null
                && backoff.Failures(CommunityQueue.Stremio) == 0
                && !StremioSync.NeedsReconnect(state),
            "Stremio wiring: no key is a site failure too; after the backoff the account resumes, newest first"
        );

        // Never un-watch: unchecking cancels the unsent job and nothing is sent to Stremio.
        Watch(Movie("m7", "tt0000107"), t0.AddMinutes(7));
        var marked = state.WatchSync.Values.Count(j => j.Provider == CommunityQueue.Stremio && j.SyncedAt != null);
        CommunityQueue.CancelPending(state, "m7");
        CommunityQueue.CancelPending(state, "m1");
        fake.Calls.Clear();
        clock = clock.AddMinutes(1);
        await Pass();
        check(
            !state.WatchSync.ContainsKey("Stremio:m7")
                && state.WatchSync["Stremio:m1"].SyncedAt != null
                && fake.Calls.Count == 0
                && marked
                    == state.WatchSync.Values.Count(j => j.Provider == CommunityQueue.Stremio && j.SyncedAt != null),
            "Stremio wiring: un-watching is never sent (the unsent job is dropped, sent marks stay)"
        );

        // Episodes: one Cinemeta list per show while it is fresh; a newer episode refetches once.
        fake.Items[Show] =
            "{\"_id\":\"tt2934286\",\"type\":\"series\",\"removed\":false,\"temp\":false,\"state\":{\"watched\":\"tt2934286:1:5:5:eJyTZwAAAEAAIA==\"}}";
        Watch(Episode("e6", 6), t0.AddMinutes(10));
        Watch(Episode("e7", 7), t0.AddMinutes(11));
        clock = clock.AddMinutes(1);
        await Pass();
        var callsForTwo = videoCalls;
        Watch(Episode("e12", 12), t0.AddMinutes(12));
        clock = clock.AddMinutes(1);
        await Pass();
        check(
            state.WatchSync["Stremio:e6"].SyncedAt != null
                && state.WatchSync["Stremio:e7"].SyncedAt != null
                && callsForTwo == 1
                && videoCalls == 2
                && state.WatchSync["Stremio:e12"].Message == "Stremio does not list this episode",
            "Stremio wiring: episodes of one show share one cached episode list; an unknown episode refetches once"
        );

        // Restore list: titles Noir changed, one row per show, newest first.
        var restorable = StremioSync.Restorable(state, folder);
        check(
            restorable.Count > 0
                && restorable[0].ImdbId == Show
                && restorable.Count(r => r.ImdbId == Show) == 1
                && restorable.All(r => StremioLibraryWriter.OriginalExists(folder, r.ImdbId))
                && !restorable.Any(r => r.ImdbId == "tt0000102"),
            "Stremio wiring: Restore is offered for every title with a saved original (one per show), never for untouched ones"
        );

        // P4: the phone's confirmed Stremio mark closes the PC's job, and the PC's is sent to the phone.
        var p4 = new PlayerState();
        CommunityQueue.SetStremio(p4, true, null);
        var phoneItem = Movie("ph", "tt0000200");
        p4.Catalog["ph"] = new CatalogEntry { Item = phoneItem };
        CommunityQueue.Enqueue(p4, phoneItem, t0);
        CommunityQueue.ApplyPhoneSynced(p4, "ph", new() { ["Stremio"] = "2026-11-11T12:05:00.0000000Z" }, t0);
        var viewing = new ViewingRecord { Id = "v1", WatchedAt = t0 };
        var times = CommunityQueue.SyncedTimes(p4, "ph", viewing, [viewing]);
        check(
            p4.WatchSync["Stremio:ph"].SyncedAt == t0.AddMinutes(5) && times?.ContainsKey("Stremio") == true,
            "Stremio wiring: P4 exchange carries Stremio both ways (phone-synced closes the PC job, PC times go to the phone)"
        );
    }
}
