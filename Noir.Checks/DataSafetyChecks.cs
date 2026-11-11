using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Noir;

static class DataSafetyChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.GetFullPath(Path.Combine("NoirValidation", "sh-w1pc-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var failures = new List<string>();
        void Verify(bool ok, string label)
        {
            if (ok)
                check(true, label);
            else
            {
                failures.Add(label);
                Console.WriteLine("FAIL: " + label);
            }
        }
        async Task Case(string name, Func<string, Task> run)
        {
            var dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            try
            {
                await run(dir);
            }
            catch (Exception e)
            {
                Verify(false, name + ": " + e.Message);
            }
        }
        await Case("pin", async dir =>
        {
            var list = Profiles.Load(dir);
            list.Profiles[0].PinHash = Profiles.HashPin(list.Profiles[0], "1234");
            Profiles.Save(list, dir);
            var store = Store(dir);
            using var downloads = new PcDownloads(store, start: false);
            using var transport = new MemoryServer();
            await using var server = new MobileSyncServer(
                store,
                store.MobileSnapshot,
                store.MergeMobile,
                49291,
                downloads: downloads,
                ephemeralCertificate: true,
                confirmDownloadDelete: (_, _, _) => Task.FromResult<DownloadDeleteChoice?>(null)
            )
            {
                ConfigureServices = services => services.AddSingleton<IServer>(transport),
            };
            var token = new string('F', 64);
            var peer = new MobilePeer
            {
                Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
                ProfileAccess = ["other"],
            };
            server.Config.Peers.Add(peer);
            await server.SetEnabled(true);
            if (!server.Running)
                throw new Exception(server.StartFailure);
            using var http = new HttpClient(transport)
            {
                BaseAddress = new Uri("https://127.0.0.1:49291"),
                Timeout = TimeSpan.FromSeconds(10),
            };
            http.DefaultRequestHeaders.Authorization = new("Bearer", token);
            foreach (var header in new[] { "", "default" })
            {
                http.DefaultRequestHeaders.Remove("X-Noir-Profile");
                if (header.Length > 0)
                    http.DefaultRequestHeaders.Add("X-Noir-Profile", header);
                foreach (var route in new[] { "/v1/library", "/v1/media/missing", "/v1/sync" })
                {
                    using var response = route.EndsWith("sync")
                        ? await http.PostAsync(route, new StringContent("[]", Encoding.UTF8, "application/json"))
                        : await http.GetAsync(route);
                    Verify(
                        response.StatusCode == HttpStatusCode.Forbidden,
                        $"Data safety PIN refuses {route} with {(header.Length == 0 ? "omitted" : "explicit")} profile"
                    );
                }
                var ctx = new DefaultHttpContext();
                ctx.Request.Headers.Authorization = "Bearer " + token;
                if (header.Length > 0)
                    ctx.Request.Headers["X-Noir-Profile"] = header;
                Verify(
                    (await server.DeleteFinishedDownload("missing", ctx) as IStatusCodeHttpResult)?.StatusCode == 403,
                    "Data safety PIN refuses direct delete with "
                        + (header.Length == 0 ? "omitted" : "explicit")
                        + " profile"
                );
            }
            peer.ProfileAccess.Add("default");
            http.DefaultRequestHeaders.Remove("X-Noir-Profile");
            using var allowed = await http.GetAsync("/v1/library");
            Verify(allowed.IsSuccessStatusCode, "Data safety allowed older phone reads active profile without header");
            var allowedCtx = new DefaultHttpContext();
            allowedCtx.Request.Headers.Authorization = "Bearer " + token;
            Verify(
                (await server.DeleteFinishedDownload("missing", allowedCtx) as IStatusCodeHttpResult)?.StatusCode
                    == 404,
                "Data safety allowed older phone reaches delete without header"
            );
            // Review finding: an unreadable profile list must not unlock a PIN profile for a device that was never granted it.
            var registry = Path.Combine(MobileProfiles.Root(store.DataDirectory), "profiles.json");
            var good = File.ReadAllBytes(registry);
            File.WriteAllText(registry, "{ malformed");
            peer.ProfileAccess.Remove("default");
            using (var unreadable = await http.GetAsync("/v1/library"))
                Verify(
                    unreadable.StatusCode == HttpStatusCode.Forbidden,
                    "Data safety unreadable profile list fails closed for a device not granted the profile"
                );
            peer.ProfileAccess.Add("default");
            using (var granted = await http.GetAsync("/v1/library"))
                Verify(
                    granted.IsSuccessStatusCode,
                    "Data safety unreadable profile list still lets a granted device in"
                );
            File.WriteAllBytes(registry, good);
        });
        await Case("locks", async dir =>
        {
            bool completed = true;
            for (int round = 0; round < 50; round++)
            {
                var store = Store(Path.Combine(dir, round.ToString()));
                var path = Path.Combine(store.DataDirectory, "movie.mkv");
                File.WriteAllBytes(path, new byte[64]);
                var scanGate = typeof(LibraryStore)
                    .GetField("scanGate", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(store)!;
                var registered = new ManualResetEventSlim();
                var scanning = new ManualResetEventSlim();
                var scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                store.RegisterDownloadLocked = () =>
                {
                    registered.Set();
                    if (!scanning.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("scan did not acquire scanGate");
                };
                new Thread(() =>
                {
                    try
                    {
                        registered.Wait();
                        lock (scanGate)
                        {
                            scanning.Set();
                            store.Scan(out _);
                        }
                        scanned.SetResult();
                    }
                    catch (Exception e)
                    {
                        scanned.SetException(e);
                    }
                })
                {
                    IsBackground = true,
                }.Start();
                new Thread(() =>
                {
                    try
                    {
                        store.RegisterDownload(
                            new()
                            {
                                Id = "download",
                                Title = "Movie",
                                IsMovie = true,
                            },
                            path
                        );
                        saved.SetResult();
                    }
                    catch (Exception e)
                    {
                        saved.SetException(e);
                    }
                })
                {
                    IsBackground = true,
                }.Start();
                try
                {
                    await Task.WhenAll(scanned.Task, saved.Task).WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                    completed = false;
                    break;
                }
                registered.Dispose();
                scanning.Dispose();
            }
            Verify(
                completed,
                "Data safety scan and RegisterDownload complete 50 forced interleavings within 10 seconds each"
            );
        });
        await Case("registry", dir =>
        {
            var path = Path.Combine(dir, "profiles.json");
            var bytes = Encoding.UTF8.GetBytes("{ malformed profile registry\n");
            File.WriteAllBytes(path, bytes);
            static bool Refused(Action save)
            {
                try
                {
                    save();
                    return false;
                }
                catch (IOException)
                {
                    return true;
                }
            }
            var list = Profiles.Load(dir);
            list.Profiles[0].LastUsed = DateTimeOffset.UtcNow;
            Verify(
                Refused(() => Profiles.Save(list, dir)),
                "Data safety a refused registry save is reported, not silent"
            );
            Verify(
                File.ReadAllBytes(path).SequenceEqual(bytes),
                "Data safety malformed profile registry survives Load LastUsed Save byte for byte"
            );
            Verify(
                list.ReadFailurePath == path && list.Profiles.Single().Id == "default",
                "Data safety failed registry read reports its path and starts with default profile"
            );
            File.WriteAllText(path, "{\"Profiles\":[{\"Id\":\"default\",\"Name\":\"Recovered\"}]}");
            var repaired = File.ReadAllBytes(path);
            Refused(() => Profiles.Save(list, dir));
            Verify(
                File.ReadAllBytes(path).SequenceEqual(repaired),
                "Data safety fallback list remains unable to save after file is repaired"
            );
            var reread = Path.Combine(dir, "reread");
            Directory.CreateDirectory(reread);
            var before = Profiles.Load(reread);
            Profiles.Save(before, reread);
            File.WriteAllBytes(Path.Combine(reread, "profiles.json"), bytes);
            before.Profiles[0].Name = "Changed";
            Refused(() => Profiles.Save(before, reread));
            Verify(
                File.ReadAllBytes(Path.Combine(reread, "profiles.json")).SequenceEqual(bytes),
                "Data safety Save refuses registry corrupted after initial Load"
            );
            var missing = Path.Combine(dir, "missing");
            var fresh = Profiles.Load(missing);
            Profiles.Save(fresh, missing);
            Verify(
                Profiles.Load(missing).Profiles.Single().Id == "default"
                    && File.Exists(Path.Combine(missing, "profiles.json")),
                "Data safety missing registry creates and saves default profile"
            );
            var valid = File.ReadAllBytes(Path.Combine(missing, "profiles.json"));
            ProfileList locked;
            using (
                var held = new FileStream(
                    Path.Combine(missing, "profiles.json"),
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None
                )
            )
            {
                locked = Profiles.Load(missing);
                locked.Profiles[0].LastUsed = DateTimeOffset.UtcNow;
                Refused(() => Profiles.Save(locked, missing));
            }
            Verify(
                locked.ReadFailurePath != null
                    && File.ReadAllBytes(Path.Combine(missing, "profiles.json")).SequenceEqual(valid),
                "Data safety IOException preserves locked registry and blocks fallback saves"
            );
            return Task.CompletedTask;
        });
        await Case("ratings", dir =>
        {
            var source = Store(Path.Combine(dir, "source"));
            var item = new VideoItem
            {
                Id = "rated",
                Title = "Rated",
                IsMovie = true,
                Available = false,
            };
            source.State.Catalog[item.Id] = new() { Item = item };
            source.State.PersonalReviews[item.Id] = new() { Rating10 = 7, Note = "Backup" };
            var path = Path.Combine(dir, "ratings.noirbackup");
            source.ExportBackup(path);
            foreach (var rating in new[] { -1, 0, 4 })
            {
                var target = Store(Path.Combine(dir, "target" + rating));
                if (rating >= 0)
                {
                    target.State.Catalog[item.Id] = new() { Item = item };
                    target.State.PersonalReviews[item.Id] = new() { Rating10 = rating };
                }
                target.MergeBackup(LibraryStore.ReadBackup(path));
                var reopened = Store(target.DataDirectory);
                Verify(
                    reopened.State.PersonalReviews.Values.Single().Rating10 == (rating == 4 ? 4 : 7),
                    "Data safety backup keeps exact rating in "
                        + (
                            rating < 0 ? "empty"
                            : rating == 0 ? "unrated"
                            : "locally rated"
                        )
                        + " review"
                );
            }
            return Task.CompletedTask;
        });
        await Case("nuvio", async dir =>
        {
            var store = Store(dir);
            store.State.SyncNuvio = true;
            store.State.NuvioConnected = true;
            var item = new VideoItem
            {
                Id = "watch",
                Title = "Watch",
                ImdbId = "tt1234567",
                IsMovie = true,
                Available = false,
            };
            var kept = new VideoItem
            {
                Id = "kept",
                Title = "Kept",
                ImdbId = "tt7654321",
                IsMovie = true,
                Available = false,
            };
            store.LogWatched([new(item, DateTimeOffset.UtcNow), new(kept, DateTimeOffset.UtcNow)], "Playback");
            item = store.Archive.Single(i => i.Id == "watch");
            NuvioStorage.Save(
                dir,
                new()
                {
                    Profile = 1,
                    AccessToken = "fake",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                }
            );
            using var handler = new BlockUpload();
            using var http = new HttpClient(handler);
            using var sync = new NuvioSync(store, http);
            var run = sync.Run();
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            store.Unwatch([item]);
            handler.Release.SetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            Verify(
                store.ViewingHistory(item).Count == 0 && store.State.NuvioDeletes.Contains("tt1234567||"),
                "Data safety Nuvio queues deletion when last viewing is removed during first upload"
            );
            Verify(
                store.State.NuvioKnown.Contains(kept.Id)
                    && !store.State.NuvioDeletes.Contains("tt7654321||")
                    && store.State.WatchSync["Nuvio:" + kept.Id].SyncedAt != null,
                "Data safety Nuvio retains uploaded watch still present locally"
            );
        });
        if (failures.Count > 0)
            throw new Exception("Data safety failures: " + failures.Count);
        Directory.Delete(root, true);
    }

    static LibraryStore Store(string dir)
    {
        var store = new LibraryStore(dir);
        store.State.Roots = [];
        store.State.OnlineMetadata = false;
        return store;
    }

    sealed class MemoryServer : HttpMessageHandler, IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();
        Func<IFeatureCollection, Task>? process;

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken ct)
            where TContext : notnull
        {
            process = async features =>
            {
                var context = application.CreateContext(features);
                Exception? failure = null;
                try
                {
                    await application.ProcessRequestAsync(context);
                }
                catch (Exception e)
                {
                    failure = e;
                    throw;
                }
                finally
                {
                    application.DisposeContext(context, failure);
                }
            };
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var headers = new HeaderDictionary();
            foreach (var h in request.Headers)
                headers[h.Key] = h.Value.ToArray();
            if (request.Content != null)
                foreach (var h in request.Content.Headers)
                    headers[h.Key] = h.Value.ToArray();
            using var input = new MemoryStream(
                request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(ct)
            );
            using var output = new MemoryStream();
            var response = new HttpResponseFeature { Body = output };
            var features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(
                new HttpRequestFeature
                {
                    Method = request.Method.Method,
                    Protocol = "HTTP/1.1",
                    Scheme = "https",
                    Path = request.RequestUri!.AbsolutePath,
                    QueryString = request.RequestUri.Query,
                    RawTarget = request.RequestUri.PathAndQuery,
                    Headers = headers,
                    Body = input,
                }
            );
            features.Set<IHttpResponseFeature>(response);
            features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(output));
            features.Set<IHttpConnectionFeature>(new HttpConnectionFeature { RemoteIpAddress = IPAddress.Loopback });
            await process!(features);
            var result = new HttpResponseMessage((HttpStatusCode)response.StatusCode)
            {
                Content = new ByteArrayContent(output.ToArray()),
            };
            foreach (var h in response.Headers)
                if (!result.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray()))
                    result.Content.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
            return result;
        }
    }

    sealed class BlockUpload : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously),
            Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("sync_push_watched_items"))
                throw new Exception("Unexpected Nuvio request");
            Started.SetResult();
            await Release.Task.WaitAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent("null") };
        }
    }
}
