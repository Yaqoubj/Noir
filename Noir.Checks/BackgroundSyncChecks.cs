using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Noir;

static class BackgroundSyncChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var dir = Path.Combine(root, "pr-S4-background-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var store = new LibraryStore(dir);
        store.State.Roots = [];
        var item = new VideoItem
        {
            Id = "background",
            CollectionKey = "background",
            Title = "Background",
            IsMovie = true,
            Available = false,
        };
        store.State.Catalog[item.Id] = new() { Item = item };
        store.Save();
        var revision = store.MobileRevision();
        check(
            revision == store.MobileRevision() && store.MobileSnapshot().Revision == revision,
            "Background sync: revision is stable across unchanged reads and snapshot projection"
        );
        store.State.HideSpoilers = !store.State.HideSpoilers;
        store.Save();
        check(revision != store.MobileRevision(), "Background sync: library save changes revision");
        revision = store.MobileRevision();
        store.State.AutoNext = !store.State.AutoNext;
        store.SaveDeferred();
        check(
            revision != store.MobileRevision(),
            "Background sync: deferred saves change revision before disk write completes"
        );
        await store.FlushPendingAsync();
        revision = store.MobileRevision();
        var op = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = item.Id,
            Kind = "mark",
            ViewingId = Guid.NewGuid().ToString(),
            At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Completed = true,
        };
        store.MergeMobile([op]);
        check(
            revision != store.MobileRevision() && store.EverWatched(item),
            "Background sync: applying phone operation changes revision"
        );
        revision = store.MobileRevision();
        MobileProfiles.Update(dir, "default", "Renamed", "#7AA7FF");
        check(revision != store.MobileRevision(), "Background sync: profile metadata changes revision");
        var server = new MemoryServer();
        var token = new string('D', 64);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        File.WriteAllText(
            Path.Combine(dir, "mobile-host.json"),
            JsonSerializer.Serialize(
                new MobileHostConfig { Peers = [new() { Name = "Background fixture", Hash = hash }] }
            )
        );
        await using var host = new MobileSyncServer(
            store,
            store.MobileSnapshot,
            store.MergeMobile,
            49289,
            ephemeralCertificate: true
        )
        {
            ConfigureServices = services => services.AddSingleton<IServer>(server),
        };
        await host.SetEnabled(true);
        check(host.Running, "Background sync: fixture HTTP pipeline starts");
        using var client = new HttpClient(server)
        {
            BaseAddress = new Uri("https://127.0.0.1:49289"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        using (var response = await client.GetAsync("/v1/changes"))
            check(
                response.StatusCode == HttpStatusCode.Unauthorized,
                "Background sync: changes route requires authentication"
            );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Noir-Profile", "default");
        var changes = await client.GetFromJsonAsync<JsonElement>("/v1/changes");
        check(
            changes.GetProperty("revision").GetString() == store.MobileRevision()
                && changes.GetProperty("profile").GetString() == "default"
                && changes.EnumerateObject().Count() == 2,
            "Background sync: changes response is small and contains active profile and revision"
        );
        var library = await client.GetFromJsonAsync<JsonElement>("/v1/library");
        check(
            library.GetProperty("revision").GetString() == changes.GetProperty("revision").GetString(),
            "Background sync: library response includes the same revision"
        );
        using (var response = await client.PostAsJsonAsync("/v1/sync", Array.Empty<MobileOperation>()))
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            check(
                response.IsSuccessStatusCode
                    && result.GetProperty("revision").GetString()
                        == result.GetProperty("library").GetProperty("revision").GetString(),
                "Background sync: sync response carries its snapshot revision"
            );
        }
        foreach (var header in new[] { "X-Noir-Profile", "X-Noir-Host" })
        {
            client.DefaultRequestHeaders.Remove(header);
            client.DefaultRequestHeaders.Add(header, "another-profile-or-host");
            using var changesReply = await client.GetAsync("/v1/changes");
            using var libraryReply = await client.GetAsync("/v1/library");
            check(
                changesReply.StatusCode == HttpStatusCode.Conflict
                    && libraryReply.StatusCode == changesReply.StatusCode,
                "Background sync: changes and library reject mismatched " + header + " equally"
            );
            client.DefaultRequestHeaders.Remove(header);
            if (header == "X-Noir-Profile")
                client.DefaultRequestHeaders.Add(header, "default");
        }
    }

    // Feed real HTTP features through WebApplication's complete middleware and routing pipeline.
    // This also runs under Windows sandboxes that cannot acquire Schannel TLS credentials.
    sealed class MemoryServer : HttpMessageHandler, IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();
        Func<IFeatureCollection, Task>? process;

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
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

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public new void Dispose()
        {
            base.Dispose();
        }

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
}
