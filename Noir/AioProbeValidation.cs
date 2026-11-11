#if NOIR_VALIDATE
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public partial class MainWindow
{
    // --aio-probe: times one real AIOStreams search and TorBox lookups for a show and records the
    // response *shape* (field names, counts, which fields are present) — never URLs, keys or links.
    async Task ProbeAio()
    {
        var report = new Dictionary<string, object?>();
        try
        {
            using var dl = new PcDownloads(store, start: false);
            var c = dl.Config;
            report["service"] = c.Service;
            report["manifestSet"] = c.Manifest.Length > 0;
            var title = Environment.GetEnvironmentVariable("NOIR_PROBE_TITLE") is { Length: > 0 } t ? t : "Bureau";
            var group = store
                .Archive.First(i => !i.IsMovie && i.Title.Contains(title, StringComparison.OrdinalIgnoreCase))
                .GroupKey;
            var eps = store
                .EpisodesFor(group)
                .Where(i => i.Season > 0)
                .OrderBy(i => i.Season)
                .ThenBy(i => i.Episode)
                .ToList();
            report["episodes"] = eps.Count;
            report["imdb"] = eps[0].ImdbId.Length > 0;
            if (Environment.GetCommandLineArgs().Contains("--plan-only"))
            {
                var seasons = eps.Select(i => i.Season).Distinct().Order().ToList();
                var steps = new List<string>();
                var sw = Stopwatch.StartNew();
                var plan = await dl.PlanSeason(
                    eps[0].Id,
                    new() { Scope = "Several seasons / whole show", Seasons = seasons },
                    new Progress<DownloadSourceStatus>(p =>
                    {
                        lock (steps)
                            steps.Add($"{sw.ElapsedMilliseconds}ms {p.Source} · {p.Message}");
                    }),
                    CancellationToken.None
                );
                sw.Stop();
                report["planMs"] = sw.ElapsedMilliseconds;
                report["planEntries"] = plan.Entries.Count;
                report["planEpisodes"] = plan.Episodes.Count;
                report["planMissing"] = plan.MissingEpisodes.Count;
                report["planMessage"] = plan.Message;
                report["planReasons"] = plan
                    .Episodes.GroupBy(e => e.Reason)
                    .Select(g => $"{g.Count()} × {g.Key}")
                    .ToList();
                report["planSources"] = plan
                    .Sources.Select(x => $"{x.Source} · {x.State} · {x.Message}")
                    .Distinct()
                    .ToList();
                report["sample"] = plan
                    .Episodes.Take(3)
                    .Select(e => new
                    {
                        e.Season,
                        e.Episode,
                        release = e.Chosen?.Release,
                        res = e.Chosen?.Resolution,
                        cache = e.Chosen?.CacheState,
                        alts = e.Alternatives.Count,
                    })
                    .ToList();
                report["progressFirstLast"] = steps.Take(3).Concat(steps.TakeLast(3)).ToList();
                // Read-only: would the download worker find each chosen file inside its torrent?
                var provider0 = new TorboxProvider(() => dl.Config);
                var checks = new List<string>();
                foreach (var e in plan.Episodes.Where(e => e.Chosen != null).Take(4))
                {
                    var hash = dl.ReleaseHash(e.Chosen!.ReleaseId);
                    if (hash.Length == 0)
                    {
                        checks.Add("no hash");
                        continue;
                    }
                    var list = await provider0.Files(hash, CancellationToken.None);
                    string Base(string n) => n.Replace('\\', '/').Split('/').Last();
                    var exact = list.Count(f =>
                        Base(f.Name) == Base(e.Chosen.File.Name) && f.Size == e.Chosen.File.Size
                    );
                    var byName = list.Count(f =>
                        Base(f.Name).Equals(Base(e.Chosen.File.Name), StringComparison.OrdinalIgnoreCase)
                    );
                    checks.Add($"S{e.Season:00}E{e.Episode:00}: name+size {exact}, name {byName}, files {list.Count}");
                }
                report["fileMatch"] = checks;
                File.WriteAllText(
                    Path.Combine(store.DataDirectory, "aio-probe.json"),
                    Regex.Replace(
                        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
                        @"https?://[^\s""]+",
                        "[link]"
                    )
                );
                return;
            }
            using var http = DownloadSecurity.Client(TimeSpan.FromSeconds(60));
            var samples = new List<object>();
            foreach (var ep in new[] { eps[0], eps[Math.Min(4, eps.Count - 1)] })
            {
                var sw = Stopwatch.StartNew();
                using var request = new HttpRequestMessage(HttpMethod.Get, TorboxProvider.StreamUri(c.Manifest, ep));
                request.Headers.UserAgent.ParseAdd("AIO-Noir/0.11.0");
                using var response = await http.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                sw.Stop();
                using var doc = JsonDocument.Parse(text);
                var streams = doc.RootElement.TryGetProperty("streams", out var s) ? s.EnumerateArray().ToList() : [];
                static List<string> Keys(JsonElement e) =>
                    e.ValueKind == JsonValueKind.Object
                        ? e.EnumerateObject()
                            .Select(p =>
                                p.Name
                                + (
                                    p.Value.ValueKind == JsonValueKind.Object
                                        ? "{" + string.Join(",", p.Value.EnumerateObject().Select(q => q.Name)) + "}"
                                        : ""
                                )
                            )
                            .ToList()
                        : [];
                var hashes = streams
                    .Select(x => x.TryGetProperty("infoHash", out var h) ? h.GetString() : null)
                    .Where(h => h != null)
                    .ToList();
                samples.Add(
                    new
                    {
                        episode = $"S{ep.Season:00}E{ep.Episode:00}",
                        ms = sw.ElapsedMilliseconds,
                        status = (int)response.StatusCode,
                        streams = streams.Count,
                        withHash = hashes.Count,
                        distinctHashes = hashes.Distinct().Count(),
                        withFileIdx = streams.Count(x => x.TryGetProperty("fileIdx", out _)),
                        withFilename = streams.Count(x =>
                            x.TryGetProperty("behaviorHints", out var b) && b.TryGetProperty("filename", out _)
                        ),
                        withStreamData = streams.Count(x => x.TryGetProperty("streamData", out _)),
                        withUrl = streams.Count(x => x.TryGetProperty("url", out _)),
                        topKeys = streams.Take(3).Select(Keys).ToList(),
                        streamDataKeys = streams
                            .Where(x => x.TryGetProperty("streamData", out _))
                            .Take(2)
                            .Select(x => Keys(x.GetProperty("streamData")))
                            .ToList(),
                        // Release/file names only (no links): to see whether pack folders and episode files are named.
                        names = streams
                            .Take(6)
                            .Select(x => new
                            {
                                file = x.TryGetProperty("behaviorHints", out var b)
                                && b.TryGetProperty("filename", out var f)
                                    ? f.GetString()
                                    : "",
                                folder = x.TryGetProperty("streamData", out var d)
                                && d.TryGetProperty("folderName", out var fo)
                                    ? fo.GetString()
                                    : "",
                                size = x.TryGetProperty("behaviorHints", out var b2)
                                && b2.TryGetProperty("videoSize", out var v)
                                    ? v.ToString()
                                    : "",
                            })
                            .ToList(),
                    }
                );
            }
            report["aiostreams"] = samples;
            var provider = new TorboxProvider(() => dl.Config);
            var sw2 = Stopwatch.StartNew();
            try
            {
                await provider.IndexSearch(eps[0], CancellationToken.None);
            }
            catch (Exception e)
            {
                report["indexSearchError"] = e.GetType().Name;
            }
            report["indexSearchWithCacheCheckMs"] = sw2.ElapsedMilliseconds;
            var sw3 = Stopwatch.StartNew();
            try
            {
                var d = await ((IDownloadProvider)provider).Discover(
                    eps[0],
                    false,
                    [eps[0]],
                    null,
                    CancellationToken.None
                );
                report["discoverReleases"] = d.Releases.Count;
            }
            catch (Exception e)
            {
                report["discoverError"] = e.GetType().Name;
            }
            report["discoverMs"] = sw3.ElapsedMilliseconds;
            var sw4 = Stopwatch.StartNew();
            try
            {
                var d = await ((IDownloadProvider)provider).Discover(
                    eps[0],
                    true,
                    eps.Where(i => i.Season == 1).ToList(),
                    null,
                    CancellationToken.None
                );
                report["seasonReleases"] = d.Releases.Count;
                report["seasonPacks"] = d.Releases.Count(r => r.Direct == null);
                var first = d.Releases.FirstOrDefault(r => r.Direct == null);
                if (first != null)
                {
                    var sw5 = Stopwatch.StartNew();
                    try
                    {
                        var files = await provider.Files(first.Hash, CancellationToken.None);
                        report["oneFileListCount"] = files.Count;
                    }
                    catch (Exception e)
                    {
                        report["fileListError"] = e.GetType().Name;
                    }
                    report["oneFileListMs"] = sw5.ElapsedMilliseconds;
                }
            }
            catch (Exception e)
            {
                report["seasonError"] = e.GetType().Name;
            }
            report["seasonDiscoverMs"] = sw4.ElapsedMilliseconds;
        }
        catch (Exception e)
        {
            report["error"] = e.GetType().Name + ": " + e.Message;
        }
        var output = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        output = Regex.Replace(output, @"https?://[^\s""]+", "[link]");
        File.WriteAllText(Path.Combine(store.DataDirectory, "aio-probe.json"), output);
    }
}
#endif
