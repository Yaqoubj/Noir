using System.IO;
using System.Text.Json;

namespace Noir;

public sealed record SeasonEpisode(int Season, int Episode);

public sealed class EpisodeDownloadOption
{
    public long ReleaseBytes { get; set; }
    public bool IsPack { get; set; }
    public string ReleaseId { get; set; } = "";
    public string Release { get; set; } = "";
    public string Source { get; set; } = "";
    public string Resolution { get; set; } = "";
    public string Language { get; set; } = "Unknown";
    public string CacheState { get; set; } = "Needs fetching";
    public DownloadFileChoice File { get; set; } = new();
}

public sealed class PlannedEpisode
{
    public string ItemId { get; set; } = "";
    public int Season { get; set; }
    public int Episode { get; set; }
    public string Title { get; set; } = "";
    public string State { get; set; } = "Not found";
    public string Reason { get; set; } = "No source covers this episode at these preferences";
    public EpisodeDownloadOption? Chosen { get; set; }
    public List<EpisodeDownloadOption> Alternatives { get; set; } = [];
}

public sealed class SeasonDownloadConfirmation
{
    public string PlanId { get; set; } = "";
    public string RequestId { get; set; } = "";

    // Explicit list: omit an episode to exclude it; choose any alternative's file ID to swap it.
    public List<string> FileIds { get; set; } = [];
}

public sealed partial class PcDownloads
{
    readonly Dictionary<
        string,
        (DownloadPlan Plan, DateTimeOffset Expires, string Group, bool Unwatched, long MaxTotal)
    > seasonPlans = [];
    readonly Dictionary<string, HashSet<string>> plannerSearches = [];

    // Injectable disk reading keeps checks offline and makes capacity changes testable.
    readonly Func<string, long>? freeSpace;
    const long DiskReserve = 64L * 1024 * 1024;

    static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    bool Future(VideoItem item) =>
        store
            .State.EpisodeSchedules.GetValueOrDefault(item.GroupKey)
            ?.Any(e => e.Season == item.Season && e.Episode == item.Episode && e.AirsAt > DateTimeOffset.UtcNow)
        ?? false;

    long FreeSpace(string root) =>
        freeSpace?.Invoke(root) ?? new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace;

    long Reserved(string root) =>
        jobs.Where(j =>
                string.Equals(Path.GetPathRoot(j.Root), Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)
                && j.Status is not ("Completed" or "Cancelled" or "Failed")
            )
            .Sum(j => Math.Max(0, j.Size - Math.Max(0, j.Bytes)));

    void CheckCapacity(List<PcDownloadJob> pending)
    {
        foreach (var disk in pending.GroupBy(j => Path.GetPathRoot(j.Root), StringComparer.OrdinalIgnoreCase))
            if (
                disk.Sum(j => Math.Max(0, j.Size)) + Reserved(disk.First().Root) + DiskReserve
                > FreeSpace(disk.First().Root)
            )
                throw new IOException(
                    "Not enough free space in the library folder. Exclude episodes or choose smaller files."
                );
    }

    public ShowDownloadPreference SeasonPreferences(string itemId)
    {
        var group = Item(itemId).GroupKey;
        if (store.GetShowDownloadPreference(group) is { } preference)
            return preference;
        var c = Config;
        return new()
        {
            Resolution = c.PreferredResolution,
            Language = c.BrowseLanguage,
            MaxEpisodeBytes = c.MaxEpisodeBytes,
        };
    }

    public void SaveSeasonPreferences(string itemId, ShowDownloadPreference preference)
    {
        if (preference.Resolution is not ("Any" or "720p" or "1080p" or "2160p") || preference.MaxEpisodeBytes < 0)
            throw new IOException("Choose a valid quality and episode size.");
        var group = Item(itemId).GroupKey;
        store.SetShowDownloadPreference(group, Copy(preference));
        Changed?.Invoke();
    }

    public List<VideoItem> ScopeEpisodes(VideoItem item, DownloadPlanRequest request)
    {
        if (item.IsMovie)
            throw new IOException("Choose a show for a season plan.");
        var all = store
            .DownloadEpisodesFor(item.GroupKey, request.IncludeSpecials)
            .Where(i => (i.Season > 0 || request.IncludeSpecials && i.Season == 0) && i.Episode > 0)
            .DistinctBy(i => (i.Season, i.Episode))
            .OrderBy(i => i.Season)
            .ThenBy(i => i.Episode)
            .ToList();
        var seasons =
            request.Seasons.Count > 0 ? request.Seasons.ToHashSet()
            : request.Scope is "Whole show" or "Several seasons / whole show" or "Next N episodes"
                ? all.Select(i => i.Season).ToHashSet()
            : new HashSet<int> { item.Season };
        var selected = all.Where(i => seasons.Contains(i.Season));
        switch (request.Scope)
        {
            case "Missing episodes":
            case "This season":
            case "Whole show":
            case "Several seasons / whole show":
                break;
            case "This episode":
                selected = all.Where(i => i.Season == item.Season && i.Episode == item.Episode);
                break;
            case "Unwatched only":
                selected = selected.Where(i => !store.EverWatched(i));
                break;
            case "From episode N":
                if (request.FromEpisode < 1)
                    throw new IOException("Enter an episode number of 1 or more.");
                selected = selected.Where(i => i.Season > item.Season || i.Episode >= request.FromEpisode);
                break;
            case "Next N episodes":
                if (request.NextCount is < 1 or > 25)
                    throw new IOException("Choose between 1 and 25 next episodes.");
                var last = all.LastOrDefault(store.EverWatched);
                selected = selected
                    .Where(i =>
                        last == null || i.Season > last.Season || i.Season == last.Season && i.Episode > last.Episode
                    )
                    .Where(i =>
                        !OnPc(i) && !IsQueued(i) && !Future(i) && (!request.SkipWatched || !store.EverWatched(i))
                    )
                    .Take(request.NextCount);
                break;
            case "Specific episodes":
                var explicitEpisodes = request.SpecificEpisodes.ToHashSet();
                selected = all.Where(i => explicitEpisodes.Contains(new(i.Season, i.Episode)));
                break;
            default:
                throw new IOException("Choose a season scope.");
        }
        return selected.Where(i => !request.SkipWatched || !store.EverWatched(i)).ToList();
    }

    public async Task<DownloadPlan> PlanSeason(
        string itemId,
        DownloadPlanRequest request,
        IProgress<DownloadSourceStatus>? progress = null,
        CancellationToken ct = default,
        bool readyOnly = false
    )
    {
        using var planning = PlanningRequests.Begin();
        var searchId = Guid.NewGuid().ToString("N");
        lock (gate)
            plannerSearches[searchId] = [];
        try
        {
            request = Copy(request);
            var item = Item(itemId);
            var scoped = ScopeEpisodes(item, request);
            var preference = request.Preferences ?? SeasonPreferences(itemId);
            SaveSeasonPreferences(itemId, preference);
            var plan = new DownloadPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Preferences = Copy(preference),
                Folder = Root(item),
                ScheduleKnown = store.State.EpisodeSchedules.GetValueOrDefault(item.GroupKey)?.Count > 0,
            };
            var wanted = new List<VideoItem>();
            foreach (var episode in scoped)
            {
                var state =
                    OnPc(episode) ? "On PC"
                    : IsQueued(episode) ? "Queued"
                    : Future(episode) ? "Unaired"
                    : "Not found";
                plan.Episodes.Add(
                    new()
                    {
                        ItemId = episode.Id,
                        Season = episode.Season,
                        Episode = episode.Episode,
                        Title = episode.EpisodeTitle,
                        State = state,
                        Reason =
                            state == "Not found" ? "No source covers this episode at these preferences"
                            : state == "Unaired" ? "Not aired yet"
                            : state == "On PC" ? "Already in your library"
                            : "Already in your download queue",
                    }
                );
                if (state == "Not found")
                    wanted.Add(episode);
            }
            plan.ExistingEpisodes = plan.Episodes.Count(e => e.State == "On PC");
            plan.QueuedEpisodes = plan.Episodes.Count(e => e.State == "Queued");
            plan.ReleasedEpisodes = plan.Episodes.Count(e => e.State != "Unaired");
            var releaseIds = request.ReleaseIds.Distinct().ToList();
            // File lists are fetched once per release, a few at a time; a failure only drops that release.
            var fileLists = new System.Collections.Concurrent.ConcurrentDictionary<
                string,
                Lazy<Task<List<DownloadFileChoice>?>>
            >();
            var fileSlots = new SemaphoreSlim(6);
            var failed = new System.Collections.Concurrent.ConcurrentBag<string>();
            Task<List<DownloadFileChoice>?> FilesOnce(string id) =>
                fileLists
                    .GetOrAdd(id, key => new(async () =>
                        {
                            await fileSlots.WaitAsync(ct);
                            try
                            {
                                if (readyOnly)
                                {
                                    lock (gate)
                                        if (!releases.TryGetValue(key, out var r) || !ConfirmedReady(r.Release.Choice))
                                            return [];
                                }
                                return await Files(key, ct);
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception e)
                                when (e
                                        is IOException
                                            or System.Net.Http.HttpRequestException
                                            or JsonException
                                            or OperationCanceledException
                                )
                            {
                                failed.Add(key);
                                return null;
                            }
                            finally
                            {
                                fileSlots.Release();
                            }
                        }))
                    .Value;
            var aio = releaseIds.Count == 0 && wanted.Count > 0 ? EpisodeSearcher(item) : null;
            if (aio != null)
            {
                await EpisodeFirst(
                    aio,
                    wanted,
                    progress,
                    searchId,
                    releaseIds,
                    plan,
                    (id, files) => fileLists[id] = new(Task.FromResult<List<DownloadFileChoice>?>(files)),
                    ct
                );
                // Media servers, Real-Debrid and the TorBox account library aren't all in AIOStreams' results; ask them per
                // episode; account and media-server listings are shared for this plan.
                if (provider is DownloadProviders)
                {
                    using var slots = new SemaphoreSlim(4);
                    var connected = await Task.WhenAll(
                        wanted.Select(async episode =>
                        {
                            await slots.WaitAsync(ct);
                            try
                            {
                                return await SearchDetailedCore(episode.Id, false, progress, ct, searchId, true);
                            }
                            finally
                            {
                                slots.Release();
                            }
                        })
                    );
                    foreach (var result in connected)
                    {
                        releaseIds.AddRange(result.Choices.Select(c => c.Id));
                        plan.Sources.AddRange(result.Sources);
                    }
                }
            }
            else if (releaseIds.Count == 0 && wanted.Count > 0)
            {
                // Each season: the season-pack search, then every wanted episode for single links (they are the
                // per-episode alternatives), a few searches at a time instead of one by one.
                var searchSlots = new SemaphoreSlim(4);
                var found = new System.Collections.Concurrent.ConcurrentBag<DownloadSearchResult>();
                foreach (var season in wanted.GroupBy(i => i.Season))
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(
                        new()
                        {
                            Source = "Planner",
                            State = "Searching",
                            Message = $"Season {season.Key}: looking for season packs",
                        }
                    );
                    var pack = await SearchDetailedCore(season.First().Id, true, progress, ct, searchId);
                    releaseIds.AddRange(pack.Choices.Select(c => c.Id));
                    plan.Sources.AddRange(pack.Sources);
                    var missing = season.ToList();
                    var done = 0;
                    await Task.WhenAll(
                        missing.Select(async episode =>
                        {
                            await searchSlots.WaitAsync(ct);
                            try
                            {
                                found.Add(await SearchDetailedCore(episode.Id, false, progress, ct, searchId));
                            }
                            finally
                            {
                                searchSlots.Release();
                                progress?.Report(
                                    new()
                                    {
                                        Source = "Planner",
                                        State = "Searching",
                                        Message =
                                            $"Season {season.Key}: single episodes {Interlocked.Increment(ref done)} of {missing.Count}",
                                    }
                                );
                            }
                        })
                    );
                }
                foreach (var singles in found)
                {
                    releaseIds.AddRange(singles.Choices.Select(c => c.Id));
                    plan.Sources.AddRange(singles.Sources);
                }
            }
            if (aio == null)
            {
                progress?.Report(
                    new()
                    {
                        Source = "Planner",
                        State = "Checking",
                        Message = $"Reading file lists for {releaseIds.Distinct().Count()} releases",
                    }
                );
                await Task.WhenAll(releaseIds.Distinct().Select(FilesOnce));
            }
            var options = new List<EpisodeDownloadOption>();
            var seen = new HashSet<string>();
            foreach (var id in releaseIds.Distinct())
            {
                ct.ThrowIfCancellationRequested();
                (IndexedRelease Release, VideoItem Item, bool Season, DateTimeOffset Expires, string Service) indexed;
                lock (gate)
                    if (
                        !releases.TryGetValue(id, out indexed)
                        || indexed.Expires < DateTimeOffset.UtcNow
                        || indexed.Item.GroupKey != item.GroupKey
                    )
                        continue;
                var release = indexed.Release;
                ReleasePresentation.Enrich(release.Choice);
                if (readyOnly && !ConfirmedReady(release.Choice))
                    continue;
                // Random public IDs must never affect planning order or cause duplicate file-list calls.
                var identity =
                    indexed.Service
                    + "|"
                    + indexed.Item.Season
                    + "|"
                    + (
                        release.Direct == null
                            ? release.Hash
                            : string.Join(
                                ";",
                                release.Direct.Select(d =>
                                    d.ServerId
                                    + "|"
                                    + d.Name
                                    + "|"
                                    + d.Season
                                    + "|"
                                    + d.Episode
                                    + "|"
                                    + d.Size
                                    + "|"
                                    + d.Url
                                )
                            )
                    );
                if (aio != null && !fileLists.ContainsKey(id))
                    identity += "|episode:" + indexed.Item.Episode;
                if (!seen.Add(identity))
                    continue;
                var files = await FilesOnce(id);
                if (files == null)
                {
                    plan.Sources.Add(
                        new()
                        {
                            Source = string.Join(" / ", release.Choice.Sources),
                            State = "Partial",
                            Message = "A release file list is unavailable",
                        }
                    );
                    continue;
                }
                foreach (
                    var f in files.Where(f =>
                        f.Selectable && wanted.Any(w => w.Season == f.Season && w.Episode == f.Episode)
                    )
                )
                {
                    DirectDownloadSource? direct;
                    lock (gate)
                        direct = choices.GetValueOrDefault(f.Id).Direct;
                    var resolution = direct?.Resolution ?? "";
                    if (resolution.Length == 0)
                        resolution = TorboxProvider.Resolution(f.Name);
                    if (resolution.Length == 0)
                        resolution = release.Choice.Resolution;
                    var language =
                        direct?.Language is { Length: > 0 } audio && audio != "Unknown"
                            ? audio
                            : release.Choice.Languages;
                    if (preference.MaxEpisodeBytes > 0 && (f.Size > preference.MaxEpisodeBytes))
                        continue;
                    if (
                        preference.Language is not ("All" or "Any" or "")
                        && !language.Contains(preference.Language, StringComparison.OrdinalIgnoreCase)
                    )
                        continue;
                    options.Add(
                        new()
                        {
                            ReleaseBytes = Math.Max(release.Choice.Size, files.Sum(file => file.Size)),
                            IsPack =
                                release.Choice.SizeKind == "Pack"
                                || files.Count(file => DownloadNaming.Video(file.Name)) > 1,
                            ReleaseId = id,
                            Release = release.Choice.Name,
                            Source =
                                release.Choice.Sources.Count > 0
                                    ? string.Join(" / ", release.Choice.Sources)
                                    : indexed.Service,
                            Resolution = resolution.Length > 0 ? resolution : "Unknown",
                            Language = language,
                            CacheState =
                                release.Choice.CacheStatus == "On your server" ? "On your server"
                                : ConfirmedReady(release.Choice) ? "Ready"
                                : "Needs fetching",
                            File = f,
                        }
                    );
                }
            }
            foreach (var episode in plan.Episodes.Where(e => e.State == "Not found"))
            {
                var available = options
                    .Where(o => o.File.Season == episode.Season && o.File.Episode == episode.Episode)
                    .ToList();
                // Prefer exact quality whenever available; retain other qualities as explicit alternatives.
                episode.Alternatives = SortOptions(available, preference).ToList();
            }
            foreach (var season in plan.Episodes.Where(e => e.State == "Not found").GroupBy(e => e.Season))
            {
                var remaining = season.ToList();
                var total = remaining.Count;
                bool usedPack = false;
                while (remaining.Count > 0)
                {
                    var candidates = remaining
                        .SelectMany(e =>
                            e.Alternatives.Where(o =>
                                preference.Resolution == "Any"
                                || !e.Alternatives.Any(a => a.Resolution == preference.Resolution)
                                || o.Resolution == preference.Resolution
                            )
                        )
                        .ToList();
                    // Greedy set cover: most wanted episodes, then readiness, known/smaller bytes,
                    // then stable source/release/file names. Only selected episode files are queued.
                    var best = candidates
                        .GroupBy(o => o.ReleaseId)
                        .Select(g => new
                        {
                            Options = g.ToList(),
                            Count = g.Select(o => o.File.Episode).Distinct().Count(),
                            Ready = g.Count(o => o.CacheState != "Needs fetching"),
                        })
                        .OrderByDescending(g => g.Count * 2 >= remaining.Count ? g.Count : 1)
                        .ThenByDescending(g => g.Ready > 0)
                        .ThenByDescending(g => g.Count)
                        .ThenBy(g => g.Options.Any(o => o.File.Size <= 0))
                        .ThenBy(g => g.Options.Sum(o => o.File.Size))
                        .ThenBy(g => g.Options[0].Source, StringComparer.Ordinal)
                        .ThenBy(g => g.Options[0].Release, StringComparer.Ordinal)
                        .ThenBy(g => g.Options[0].File.Name, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (best == null)
                        break;
                    var pack = best.Count > 1;
                    usedPack |= pack;
                    foreach (var episode in remaining.ToList())
                    {
                        var chosen = SortOptions(
                                best.Options.Where(o => o.File.Episode == episode.Episode).ToList(),
                                preference
                            )
                            .FirstOrDefault();
                        if (chosen == null)
                            continue;
                        episode.Chosen = chosen;
                        episode.State = chosen.CacheState;
                        episode.Reason =
                            pack ? $"Season pack covers {best.Count} of {total}"
                            : usedPack ? $"Pack missing E{episode.Episode:00} · single link"
                            : "Best available single link";
                        if (preference.Resolution != "Any" && chosen.Resolution != preference.Resolution)
                            episode.Reason += " · preferred quality unavailable";
                        remaining.Remove(episode);
                    }
                }
            }
            long budget = 0;
            bool over = false;
            foreach (
                var episode in plan.Episodes.Where(e => e.Chosen != null).OrderBy(e => e.Season).ThenBy(e => e.Episode)
            )
            {
                var size = episode.Chosen!.File.Size > 0 ? episode.Chosen.File.Size : preference.MaxEpisodeBytes;
                if (request.MaxTotalBytes > 0 && (over || size > request.MaxTotalBytes - budget))
                {
                    over = true;
                    episode.State = "Over your total limit";
                    episode.Reason =
                        store.State.Language == "ar" ? "يتجاوز حد المساحة الإجمالي" : "Over your total limit";
                }
                else
                    budget += Math.Max(0, size);
            }
            plan.Entries = plan
                .Episodes.Where(e => e.Chosen != null && e.State != "Over your total limit")
                .Select(e => new DownloadPlanEntry
                {
                    ReleaseId = e.Chosen!.ReleaseId,
                    Release = e.Chosen.Release,
                    File = e.Chosen.File,
                })
                .ToList();
            plan.MissingEpisodes = plan.Episodes.Where(e => e.State == "Not found").Select(e => e.Episode).ToList();
            plan.SelectedBytes = plan.Entries.Sum(e => e.File.Size);
            plan.HasUnknownSizes = plan.Entries.Any(e => e.File.Size <= 0);
            lock (gate)
            {
                plan.FreeBytes = Math.Max(0, FreeSpace(plan.Folder) - Reserved(plan.Folder));
                plan.Fits = plan.SelectedBytes + DiskReserve <= plan.FreeBytes;
            }
            plan.Message =
                $"{plan.Entries.Count} episodes · {DownloadNaming.Size(plan.SelectedBytes)} · {plan.MissingEpisodes.Count} not found"
                + (!plan.ScheduleKnown ? " · Schedule unavailable; known episodes only" : "")
                + (plan.HasUnknownSizes ? " · Some sizes unknown" : "");
            lock (gate)
            {
                foreach (
                    var key in seasonPlans
                        .Where(p => p.Value.Expires < DateTimeOffset.UtcNow)
                        .Select(p => p.Key)
                        .ToList()
                )
                    seasonPlans.Remove(key);
                seasonPlans[plan.Id] = (
                    Copy(plan),
                    DateTimeOffset.UtcNow.AddMinutes(20),
                    item.GroupKey,
                    request.Scope == "Unwatched only" || request.SkipWatched,
                    Math.Max(0, request.MaxTotalBytes)
                );
            }
            return plan;
        }
        finally
        {
            lock (gate)
                plannerSearches.Remove(searchId);
        }
    }

    // AIOStreams answers each episode with that episode's own file in every release, singles and
    // season packs alike (file name and size included). So planning needs one search per episode,
    // run in parallel, and no season probes or torrent file lists; the download worker re-finds the
    // file by name and size when it starts.
    internal string ReleaseHash(string releaseId)
    {
        lock (gate)
            return releases.TryGetValue(releaseId, out var r) ? r.Release.Hash : "";
    }

    TorboxProvider? EpisodeSearcher(VideoItem item)
    {
        var c = Config;
        if (c.Manifest.Length == 0 || !System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId, @"^tt\d+$"))
            return null;
        return (provider is DownloadProviders router ? router.ForService(c.Service) : provider) as TorboxProvider;
    }

    async Task EpisodeFirst(
        TorboxProvider aio,
        List<VideoItem> wanted,
        IProgress<DownloadSourceStatus>? progress,
        string searchId,
        List<string> releaseIds,
        DownloadPlan plan,
        Action<string, List<DownloadFileChoice>> files,
        CancellationToken ct
    )
    {
        var service = Config.Service;
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, List<IndexedRelease>>();
        var done = 0;
        var failures = 0;
        using var slots = new SemaphoreSlim(6);
        progress?.Report(
            new()
            {
                Source = "AIOStreams",
                State = "Searching",
                Message = $"Searching {wanted.Count} episode{(wanted.Count == 1 ? "" : "s")}…",
            }
        );
        await Task.WhenAll(
            wanted.Select(async episode =>
            {
                await slots.WaitAsync(ct);
                try
                {
                    results[episode.Id] = await aio.IndexSearch(episode, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                    when (e
                            is IOException
                                or System.Net.Http.HttpRequestException
                                or JsonException
                                or OperationCanceledException
                    )
                {
                    Interlocked.Increment(ref failures);
                }
                finally
                {
                    slots.Release();
                    progress?.Report(
                        new()
                        {
                            Source = "AIOStreams",
                            State = "Searching",
                            Message = $"{Interlocked.Increment(ref done)} of {wanted.Count} episodes searched",
                        }
                    );
                }
            })
        );
        var groups = new Dictionary<string, (string Id, IndexedRelease Release, List<DownloadFileChoice> Files)>();
        lock (gate)
        {
            foreach (var episode in wanted.OrderBy(i => i.Season).ThenBy(i => i.Episode))
            {
                if (!results.TryGetValue(episode.Id, out var found))
                    continue;
                foreach (var release in found)
                {
                    // One release per torrent: the same pack found for several episodes is one choice.
                    var key = release.Direct == null ? "hash:" + release.Hash : "link:" + release.Choice.Id;
                    if (!groups.TryGetValue(key, out var group))
                    {
                        group = (release.Choice.Id, release, []);
                        groups[key] = group;
                        releases[release.Choice.Id] = (
                            release,
                            episode,
                            false,
                            DateTimeOffset.UtcNow.AddMinutes(20),
                            service
                        );
                        if (plannerSearches.TryGetValue(searchId, out var pinned))
                            pinned.Add(release.Choice.Id);
                    }
                    var name = release.Direct?.FirstOrDefault()?.Name ?? release.Choice.Name;
                    var multi = System.Text.RegularExpressions.Regex.IsMatch(
                        name,
                        @"(?i)s\d{1,2}[ ._-]*e\d{1,3}(?:[ ._-]*e\d|[ ._-]*-[ ._-]*(?:e)?\d)"
                    );
                    var choice = new DownloadFileChoice
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = name,
                        Size = release.Choice.Size,
                        Season = episode.Season,
                        Episode = episode.Episode,
                        Selectable = !multi,
                        Reason = multi ? "Multi-episode file · needs manual organization" : "",
                    };
                    choices[choice.Id] = (group.Id, choice, release.Direct?.FirstOrDefault());
                    group.Files.Add(choice);
                    if (release.Choice.Cached)
                        group.Release.Choice.Cached = true;
                }
            }
            foreach (
                var group in groups.Values.Where(g => g.Files.Select(f => (f.Season, f.Episode)).Distinct().Count() > 1)
            )
            {
                // A pack: name it by its season rather than by the first episode's file.
                var choice = group.Release.Choice;
                choice.Name = System.Text.RegularExpressions.Regex.Replace(
                    System.Text.RegularExpressions.Regex.Replace(
                        choice.Name,
                        @"(?i)(s\d{1,2})[ ._-]*e(?:p)?\d{1,3}",
                        "$1"
                    ),
                    @"(?i)\.(mkv|mp4|avi|m4v|ts|webm)$",
                    ""
                );
                choice.SizeKind = "Pack";
                choice.Size = group.Files.Sum(f => f.Size);
            }
        }
        foreach (var group in groups.Values)
        {
            releaseIds.Add(group.Id);
            files(group.Id, group.Files);
        }
        plan.Sources.Add(
            new()
            {
                Source = "AIOStreams",
                State =
                    failures == 0
                        ? groups.Count > 0
                            ? "Ready"
                            : "Empty"
                        : "Partial",
                Count = groups.Count,
                Message =
                    $"{wanted.Count - failures} of {wanted.Count} episodes searched · {groups.Count} release{(groups.Count == 1 ? "" : "s")}"
                    + (
                        failures > 0
                            ? $" · {failures} search{(failures == 1 ? "" : "es")} failed; Refresh plan to retry"
                            : ""
                    ),
            }
        );
    }

    static IOrderedEnumerable<EpisodeDownloadOption> SortOptions(
        List<EpisodeDownloadOption> options,
        ShowDownloadPreference preference
    ) =>
        options
            .OrderByDescending(o => preference.Resolution == "Any" || o.Resolution == preference.Resolution)
            .ThenByDescending(o => o.CacheState != "Needs fetching")
            .ThenBy(o => o.File.Size <= 0)
            .ThenBy(o => o.File.Size)
            .ThenBy(o => o.Source, StringComparer.Ordinal)
            .ThenBy(o => o.Release, StringComparer.Ordinal)
            .ThenBy(o => o.File.Name, StringComparer.Ordinal);

    public List<string> EnqueuePlan(SeasonDownloadConfirmation confirmation)
    {
        lock (gate)
        {
            if (!Guid.TryParse(confirmation.RequestId, out _))
                throw new IOException("Invalid request ID.");
            var prior = jobs.Where(j => j.RequestId == confirmation.RequestId).Select(j => j.Id).ToList();
            if (prior.Count > 0)
                return prior;
            if (
                !seasonPlans.TryGetValue(confirmation.PlanId, out var session)
                || session.Expires < DateTimeOffset.UtcNow
            )
                throw new IOException("This plan expired. Build it again.");
            var representative = store.EpisodesFor(session.Group).FirstOrDefault()
                ?? throw new IOException("Refresh this show's library entry.");
            if (!Root(representative).Equals(session.Plan.Folder, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The download folder changed. Build this plan again.");
            var selected = new List<EpisodeDownloadOption>();
            foreach (var id in confirmation.FileIds.Distinct())
            {
                var episode = session.Plan.Episodes.FirstOrDefault(e => e.Alternatives.Any(o => o.File.Id == id))
                    ?? throw new IOException("Choose an alternative from this plan.");
                if (selected.Any(o => o.File.Season == episode.Season && o.File.Episode == episode.Episode))
                    throw new IOException("Choose one option per episode.");
                var item = store
                    .EpisodesFor(session.Group)
                    .FirstOrDefault(i => i.Season == episode.Season && i.Episode == episode.Episode)
                    ?? throw new IOException("Refresh this show's episode schedule.");
                if (session.Unwatched && store.EverWatched(item))
                    throw new IOException("An episode is now watched. Build the unwatched plan again.");
                selected.Add(episode.Alternatives.First(o => o.File.Id == id));
            }
            var charged = selected.Sum(o =>
                o.File.Size > 0 ? o.File.Size : Math.Max(0, session.Plan.Preferences.MaxEpisodeBytes)
            );
            if (session.MaxTotal > 0 && charged > session.MaxTotal)
                throw new IOException(
                    store.State.Language == "ar"
                        ? "الاختيارات تتجاوز الحد الإجمالي. قلل الحلقات أو غيّر الحد ثم ابحث مجددًا."
                        : "These choices exceed your total limit. Exclude episodes or change the limit and find releases again."
                );
            return EnqueueCore(
                new()
                {
                    RequestId = confirmation.RequestId,
                    Selections = selected
                        .GroupBy(o => o.ReleaseId)
                        .Select(g => new DownloadSelection
                        {
                            ReleaseId = g.Key,
                            FileIds = g.Select(o => o.File.Id).ToList(),
                        })
                        .ToList(),
                },
                null
            );
        }
    }

    public async Task<bool> TryAutoDownload(string itemId, CancellationToken ct = default)
    {
        return await Task.Run(
            async () =>
            {
                InitializeReady();
                await readyRun.WaitAsync(ct);
                try
                {
                    var item = Item(itemId);
                    var preference = SeasonPreferences(itemId);
                    if (
                        ReadyPaused
                        || !NoirReadyPolicy.Enabled(preference)
                        || !ReadyWindowOpen(ReadyState, ReadyClock())
                        || item.IsMovie
                        || Future(item)
                        || store.EverWatched(item)
                        || OnPc(item)
                        || IsQueued(item)
                        || !(
                            store
                                .State.EpisodeSchedules.GetValueOrDefault(item.GroupKey)
                                ?.Any(e =>
                                    e.Season == item.Season
                                    && e.Episode == item.Episode
                                    && e.AirsAt <= DateTimeOffset.UtcNow
                                )
                            ?? false
                        )
                    )
                        return false;
                    var plan = await PlanSeason(
                        itemId,
                        new() { Scope = "Specific episodes", SpecificEpisodes = [new(item.Season, item.Episode)] },
                        ct: ct,
                        readyOnly: true
                    );
                    var options = plan.Episodes.SingleOrDefault()?.Alternatives ?? [];
                    var decision = NoirReadyPolicy.Choose(
                        options.Select(o => new ReadySource(
                            o.File.Id,
                            o.ReleaseId,
                            o.File.Size,
                            o.ReleaseBytes,
                            o.IsPack,
                            o.CacheState is "Ready" or "On your server",
                            o.Resolution
                        )),
                        ReadyState,
                        MeasureReadyStorage(plan.Folder),
                        preference.Resolution
                    );
                    if (decision.Source == null)
                    {
                        lock (readyGate)
                        {
                            ready!.Log(decision.Reason, item.Title);
                            SaveReady();
                        }
                        return false;
                    }
                    if (
                        ReadyState.Paused
                        || !NoirReadyPolicy.Enabled(SeasonPreferences(itemId))
                        || store.EverWatched(item)
                    )
                        return false;
                    var ids = EnqueuePlan(
                        new()
                        {
                            PlanId = plan.Id,
                            RequestId = Guid.NewGuid().ToString(),
                            FileIds = [decision.Source.Id],
                        }
                    );
                    var byId = Jobs().ToDictionary(j => j.Id);
                    lock (readyGate)
                    {
                        foreach (var id in ids)
                        {
                            var job = byId[id];
                            ready!.ManagedFiles.Add(
                                new()
                                {
                                    JobId = id,
                                    ItemId = job.Target.Id,
                                    Group = item.GroupKey,
                                    Path = job.Destination,
                                    Title = job.Name,
                                    ChargedBytes = decision.Charge,
                                }
                            );
                            ready.Log("Queued confirmed ready file", job.Name);
                        }
                        SaveReady();
                    }
                    return ids.Count > 0;
                }
                catch (IOException)
                {
                    return false;
                }
                finally
                {
                    readyRun.Release();
                }
            },
            ct
        );
    }

    static bool ConfirmedReady(DownloadChoice choice) =>
        choice.Cached || choice.CacheStatus is "Ready" or "On your server";

    readonly object readyGate = new();
    readonly SemaphoreSlim readyRun = new(1, 1);
    int readyPending;
    NoirReadyState? ready;
    byte[]? lastReadySave;
    ReadyStorage readyStorage = new(0, 0, 0, 0);
    string ReadyPath => Path.Combine(store.DataDirectory, "noir-ready.json");
    public NoirReadyState ReadyState
    {
        get
        {
            lock (readyGate)
                return Copy(ready ?? new());
        }
    }
    public ReadyStorage ReadyStorage
    {
        get
        {
            lock (readyGate)
                return readyStorage;
        }
    }
    public bool ReadyPaused
    {
        get
        {
            lock (readyGate)
                return ready?.Paused == true;
        }
    }
    public NoirReadyState ReadyViewState
    {
        get
        {
            lock (readyGate)
            {
                var s = ready ?? new();
                return new()
                {
                    BudgetBytes = s.BudgetBytes,
                    KeepFreeBytes = s.KeepFreeBytes,
                    Paused = s.Paused,
                    CleanupWatched = s.CleanupWatched,
                    Migrated = s.Migrated,
                    RulesVersion = s.RulesVersion,
                    RemoveAfterHours = s.RemoveAfterHours,
                    RemoveScope = s.RemoveScope,
                    RemoveMovies = s.RemoveMovies,
                    KeepLastWatched = s.KeepLastWatched,
                    RecycleRemoved = s.RecycleRemoved,
                    ReadyCount = s.ReadyCount,
                    AutoShows = s.AutoShows,
                    WindowStart = s.WindowStart,
                    WindowEnd = s.WindowEnd,
                    Titles = s
                        .Titles.Values.Where(t => t.Episodes.Count > 0 || t.State == "Blocked")
                        .Take(40)
                        .ToDictionary(t => t.Group, t => Copy(t)),
                    ManagedFiles = s.ManagedFiles.TakeLast(60).Select(Copy).ToList(),
                    // Manual downloads show only when they are pinned or scheduled: there can be thousands.
                    NoirFiles = s
                        .NoirFiles.Where(f => f.Pinned || f.RemoveAt != null)
                        .TakeLast(60)
                        .Select(Copy)
                        .ToList(),
                    Why = s.Why.TakeLast(200).Select(Copy).ToList(),
                };
            }
        }
    }

    public bool ReadyOfferShown(string group)
    {
        lock (readyGate)
            return ready?.Offered.Contains(group) == true;
    }

    public void InitializeReady()
    {
        lock (readyGate)
        {
            if (ready != null)
                return;
            if (File.Exists(ReadyPath))
                ready = JsonSerializer.Deserialize<NoirReadyState>(File.ReadAllText(ReadyPath))
                    ?? throw new IOException("Preparation state could not be read");
            else
                ready = new();
            lastReadySave = JsonSerializer.SerializeToUtf8Bytes(ready, Json);
            if (!ready.Migrated)
            {
                store.MigrateReadyPreferences();
                ready.Migrated = true;
                ready.Log("Migrated keep ready preferences");
                SaveReady();
            }
            if (ready.RulesVersion < 1)
            {
                MigrateReadyRules(ready);
                store.MigrateReadyRules();
                ready.Log("Moved removal settings to the new rules");
                SaveReady();
            }
        }
    }

    // A1–A8 migration keeps today's behaviour: remove watched on = 7 days after watching, off = never; Keep ready
    // files only; delete permanently; 2 ahead; only shows switched on; any time. Dates already scheduled stay.
    public static void MigrateReadyRules(NoirReadyState state)
    {
        state.RemoveAfterHours = state.CleanupWatched ? 168 : -1;
        state.RemoveScope = "Keep ready";
        state.RemoveMovies = false;
        state.KeepLastWatched = 0;
        state.RecycleRemoved = false;
        state.ReadyCount = 2;
        state.AutoShows = "Switched";
        state.WindowStart = null;
        state.WindowEnd = null;
        foreach (var file in state.ManagedFiles.Where(f => f.RemoveAt != null))
            file.RuleHours = 168;
        state.RulesVersion = 1;
    }

    // A6+A7: why this show downloads automatically right now (its own switch, or the global choice), or null.
    public string? AutomaticRule(string itemId)
    {
        var item = Item(itemId);
        var preference = SeasonPreferences(itemId);
        string shows;
        lock (readyGate)
            shows = ready?.AutoShows ?? "Switched";
        var now = ReadyClock();
        return NoirReadyPolicy.AutomaticReason(
            preference,
            new() { AutoShows = shows },
            shows != "Switched" && store.WatchingShow(item.GroupKey, now),
            shows == "Watchlist" && store.ReadyWatchlist().Contains(item.GroupKey)
        );
    }

    // Test seam (A8 window, A1 schedules): the clock Keep ready decides with.
    public Func<DateTimeOffset> ReadyClock { get; set; } = () => DateTimeOffset.UtcNow;

    bool ReadyWindowOpen(NoirReadyState state, DateTimeOffset now) =>
        NoirReadyPolicy.InWindow(state.WindowStart, state.WindowEnd, now.ToLocalTime().TimeOfDay);

    void SaveReady()
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(ready, Json);
        if (lastReadySave != null && data.AsSpan().SequenceEqual(lastReadySave))
            return;
        Atomic(ReadyPath, data);
        lastReadySave = data;
    }

    public Task ChangeReady(Action<NoirReadyState> change) =>
        Task.Run(() =>
        {
            InitializeReady();
            bool paused;
            List<string> resume;
            lock (readyGate)
            {
                var wasPaused = ready!.Paused;
                var (wasCleanup, wasHours) = (ready.CleanupWatched, ready.RemoveAfterHours);
                change(ready);
                ready.BudgetBytes = Math.Max(NoirReadyPolicy.GB, ready.BudgetBytes);
                ready.KeepFreeBytes = Math.Max(10 * NoirReadyPolicy.GB, ready.KeepFreeBytes);
                // Older callers still flip the one switch: on = 7 days, off = never.
                if (ready.CleanupWatched != wasCleanup && ready.RemoveAfterHours == wasHours)
                    ready.RemoveAfterHours = ready.CleanupWatched ? 168 : -1;
                if (ready.RulesVersion >= 1)
                    ready.CleanupWatched = NoirReadyPolicy.RemoveHours(ready) > 0;
                paused = ready.Paused;
                resume = wasPaused && !paused ? ready.PausedJobs.ToList() : [];
                if (!paused)
                    ready.PausedJobs.Clear();
                SaveReady();
            }
            var state = ReadyState;
            var owned = state.ManagedFiles.Select(f => f.JobId).ToHashSet();
            foreach (var job in Jobs().Where(j => owned.Contains(j.Id)))
            {
                if (paused && job.Status is "Queued" or "Preparing" or "Downloading")
                {
                    Control(job.Id, "pause");
                    lock (readyGate)
                    {
                        ready!.PausedJobs.Add(job.Id);
                        SaveReady();
                    }
                }
                else if (resume.Contains(job.Id) && job.Status == "Paused")
                    Control(job.Id, "resume");
            }
            Changed?.Invoke();
        });

    public ReadyTitlePlan? PreparedTitle(string group)
    {
        lock (readyGate)
            return ready?.Titles.GetValueOrDefault(group) is { } title ? Copy(title) : null;
    }

    public bool ReadyRecheckDue
    {
        get
        {
            lock (readyGate)
                return ready != null
                    && !ready.Paused
                    && ready.Titles.Values.Any(t => t.RecheckAt <= DateTimeOffset.UtcNow);
        }
    }

    public Task EpisodeFinished(VideoItem finished) => PrepareReady("Episode finished");

    public Task PrepareReady(string trigger, CancellationToken ct = default) =>
        Task.Run(
            async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, stop.Token);
                var token = linked.Token;
                if (!await readyRun.WaitAsync(0, token))
                {
                    if (trigger is "Episode finished" or "Download finished" or "Preferences changed")
                        Interlocked.Exchange(ref readyPending, 1);
                    return;
                }
                try
                {
                    InitializeReady();
                    await PrepareReadyCore(trigger, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception e)
                {
                    store.Log(e);
                    lock (readyGate)
                    {
                        if (ready != null)
                        {
                            ready.Log("Preparation could not finish", detail: "Check download settings and connection");
                            SaveReady();
                        }
                    }
                }
                finally
                {
                    lock (readyGate)
                    {
                        if (ready != null)
                            SaveReady();
                    }
                    readyRun.Release();
                    Changed?.Invoke();
                    if (Interlocked.Exchange(ref readyPending, 0) == 1 && !stop.IsCancellationRequested)
                        _ = PrepareReady("Episode finished");
                }
            },
            ct
        );

    async Task PrepareReadyCore(string trigger, CancellationToken ct)
    {
        var state = ReadyState;
        if (state.Paused)
            return;
        var now = ReadyClock();
        // A6: "Shows I am watching" = the followed shows of the new-episode alert (BackgroundMode), started ones only.
        var watching = state.AutoShows is "Watching" or "Watchlist" ? store.WatchingShows(now) : [];
        var watchlist = state.AutoShows == "Watchlist" ? store.ReadyWatchlist() : [];
        var input = store.ReadyInputs(now, watching);
        var windowOpen = ReadyWindowOpen(state, now);
        var preferences = input.GroupBy(x => x.Episode.Group).ToDictionary(g => g.Key, g => g.First().Preference);
        var active = preferences.Keys.ToHashSet();
        lock (readyGate)
        {
            foreach (var key in ready!.Titles.Keys.Where(k => !active.Contains(k)).ToList())
                ready.Titles.Remove(key);
            foreach (var group in input.GroupBy(x => x.Episode.Group))
                if (!ready.Titles.ContainsKey(group.Key))
                    ready.Titles[group.Key] = new()
                    {
                        Group = group.Key,
                        Title = group.First().Episode.Title,
                        ItemId = group.First().Episode.ItemId,
                    };
            ready.Log("Preparation check", detail: trigger);
            SaveReady();
        }
        ReconcileReadyFiles(now);
        var worked = 0;
        foreach (var group in NoirReadyPolicy.OrderShows(input.Select(x => x.Episode)))
        {
            ct.ThrowIfCancellationRequested();
            if (ReadyPaused)
                break;
            var preference = preferences[group.Key];
            var episodes =
                group.First().Season == 0
                    ? group.Where(e => !e.Watched).Take(1).ToList()
                    : NoirReadyPolicy.Next(group, NoirReadyPolicy.Count(preference, state), now);
            var cached = PreparedTitle(group.Key);
            if (
                cached != null
                && cached.RecheckAt > now
                && cached.Episodes.SequenceEqual(episodes)
                && trigger is not ("Episode finished" or "Preferences changed" or "Download finished")
            )
                continue;
            if (worked++ >= 12)
                break;
            var representative = episodes.FirstOrDefault() ?? group.First();
            var title = new ReadyTitlePlan
            {
                Group = group.Key,
                Title = representative.Title,
                ItemId = representative.ItemId,
                Episodes = episodes,
                Quality = preference.Resolution,
                RecheckAt = now.AddHours(6),
            };
            var rule = NoirReadyPolicy.AutomaticReason(
                preference,
                state,
                watching.Contains(group.Key),
                watchlist.Contains(group.Key)
            );
            var automatic = rule != null;
            var missing = episodes.Where(e => !e.Local).ToList();
            if (episodes.Count == 0)
            {
                title.State = "Waiting";
                title.Reason = "Waiting for an aired unwatched episode";
                PublishReady(title);
                continue;
            }
            if (missing.Count == 0)
            {
                title.State = "Offline";
                title.Reason = "Playable offline";
                PublishReady(title);
                continue;
            }
            var pending = Jobs()
                .Where(j => j.Target.GroupKey == group.Key && j.Status is not ("Completed" or "Cancelled" or "Failed"))
                .ToList();
            missing.RemoveAll(e => pending.Any(j => j.Target.Season == e.Season && j.Target.Episode == e.Episode));
            if (missing.Count == 0)
            {
                title.State = "Preparing";
                title.Reason = pending.Any(j => j.Status == "Paused") ? "Download paused" : "Preparing on this PC";
                PublishReady(title);
                continue;
            }
            // A8: outside the download hours no new automatic job starts; running and manual downloads carry on.
            if (automatic && !windowOpen)
            {
                title.State = "Waiting";
                title.Reason = "Waiting for download hours";
                title.RecheckAt = NoirReadyPolicy.NextWindowStart(state.WindowStart!.Value, now);
                PublishReady(title);
                continue;
            }
            if (!Configured)
            {
                title.State = "Blocked";
                title.Reason = "Connect a download source in Settings";
                RetryReady(title, cached, now);
                continue;
            }
            try
            {
                var plan = await PlanReadyTitle(missing, ct);
                var options = plan.Episodes.SelectMany(e => e.Alternatives).ToDictionary(o => o.File.Id);
                long impact = 0,
                    previewDisk = 0;
                bool queued = false;
                bool blocked = false;
                var previewPacks = new Dictionary<string, long>();
                foreach (var episode in missing)
                {
                    var choices = plan.Episodes.FirstOrDefault(e => e.ItemId == episode.ItemId)?.Alternatives ?? [];
                    var disk = MeasureReadyStorage(plan.Folder);
                    state = ReadyState;
                    // A preview reserves earlier choices too; it cannot promise a season that exceeds the budget.
                    if (!automatic)
                        disk = disk with
                        {
                            ReservedBytes = disk.ReservedBytes + impact,
                            DiskReservedBytes = disk.DiskReservedBytes + previewDisk,
                        };
                    var allowance = ReadyReleaseAllowances();
                    var decision = NoirReadyPolicy.Choose(
                        choices.Select(o => new ReadySource(
                            o.File.Id,
                            o.ReleaseId,
                            o.File.Size,
                            o.ReleaseBytes,
                            o.IsPack,
                            o.CacheState is "Ready" or "On your server",
                            o.Resolution,
                            o.IsPack
                                ? Math.Max(
                                    allowance.GetValueOrDefault(ReadyReleaseKey(o)),
                                    previewPacks.GetValueOrDefault(ReadyReleaseKey(o))
                                )
                                : 0
                        )),
                        state,
                        disk,
                        preference.Resolution
                    );
                    if (decision.Source == null)
                    {
                        title.Reason = decision.Reason;
                        blocked = true;
                        continue;
                    }
                    var source = options[decision.Source.Id];
                    impact += decision.Charge;
                    previewDisk += source.File.Size;
                    title.Quality = source.Resolution;
                    var releaseKey = ReadyReleaseKey(source);
                    if (source.IsPack)
                        previewPacks[releaseKey] = Math.Max(source.ReleaseBytes, source.File.Size);
                    if (!automatic)
                        continue;
                    // Recheck consent and progress immediately before committing. No uncached selection enters the worker.
                    var current = SeasonPreferences(episode.ItemId);
                    var latestState = ReadyState;
                    if (
                        latestState.Paused
                        || NoirReadyPolicy.AutomaticReason(
                            current,
                            latestState,
                            watching.Contains(group.Key),
                            watchlist.Contains(group.Key)
                        ) == null
                        || !ReadyWindowOpen(latestState, ReadyClock())
                        || store.ReadyWatched(episode.ItemId)
                    )
                        break;
                    var ids =
                        group.First().Season == 0
                            ? Enqueue(
                                new()
                                {
                                    RequestId = Guid.NewGuid().ToString(),
                                    Selections = [new() { ReleaseId = source.ReleaseId, FileIds = [source.File.Id] }],
                                }
                            )
                            : EnqueuePlan(
                                new()
                                {
                                    PlanId = plan.Id,
                                    RequestId = Guid.NewGuid().ToString(),
                                    FileIds = [source.File.Id],
                                }
                            );
                    var jobsById = Jobs().ToDictionary(j => j.Id);
                    lock (readyGate)
                    {
                        foreach (var id in ids)
                        {
                            var job = jobsById[id];
                            ready!.ManagedFiles.Add(
                                new()
                                {
                                    JobId = id,
                                    ItemId = job.Target.Id,
                                    Group = group.Key,
                                    Path = job.Destination,
                                    Title = job.Name,
                                    ReleaseKey = source.IsPack ? releaseKey : "",
                                    ChargedBytes = source.IsPack
                                        ? Math.Max(source.ReleaseBytes, source.File.Size)
                                        : decision.Charge,
                                }
                            );
                            ready.Log(
                                "Queued confirmed ready file",
                                job.Name,
                                rule + " · " + source.Resolution + " · " + DownloadNaming.Size(decision.Charge)
                            );
                        }
                        SaveReady();
                    }
                    queued |= ids.Count > 0;
                }
                title.Bytes = impact;
                title.State =
                    blocked ? "Blocked"
                    : queued ? "Preparing"
                    : "Ready";
                if (!blocked)
                    title.Reason = queued ? "Preparing on this PC" : "Ready to download";
                if (blocked)
                    RetryReady(title, cached, now);
                else
                    PublishReady(title);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                title.State = "Blocked";
                title.Reason = "Source check failed; recheck scheduled";
                RetryReady(title, cached, now);
            }
        }
    }

    void RetryReady(ReadyTitlePlan title, ReadyTitlePlan? before, DateTimeOffset now)
    {
        title.Attempts = (before?.Attempts ?? 0) + 1;
        title.RecheckAt = NoirReadyPolicy.Backoff(now, title.Attempts);
        PublishReady(title);
    }

    void PublishReady(ReadyTitlePlan title)
    {
        lock (readyGate)
        {
            ready!.Titles[title.Group] = title;
            ready.Log(title.Reason, title.Title, title.State == "Blocked" ? title.RecheckAt.ToString("O") : "");
        }
        Changed?.Invoke();
    }

    async Task<DownloadPlan> PlanReadyTitle(List<ReadyEpisode> episodes, CancellationToken ct)
    {
        var first = episodes[0];
        if (first.Season != 0)
            return await PlanSeason(
                first.ItemId,
                new()
                {
                    Scope = "Specific episodes",
                    SpecificEpisodes = episodes.Select(e => new SeasonEpisode(e.Season, e.Episode)).ToList(),
                },
                ct: ct,
                readyOnly: true
            );
        var result = await SearchDetailed(first.ItemId, false, ct: ct);
        var item = Item(first.ItemId);
        var plan = new DownloadPlan { Folder = Root(item) };
        var episode = new PlannedEpisode { ItemId = item.Id };
        plan.Episodes.Add(episode);
        foreach (var release in result.Choices.Where(ConfirmedReady))
        foreach (var f in await Files(release.Id, ct))
            if (f.Selectable)
                episode.Alternatives.Add(
                    new()
                    {
                        ReleaseId = release.Id,
                        Release = release.Name,
                        Resolution = release.Resolution,
                        ReleaseBytes = release.Size,
                        IsPack = release.SizeKind == "Pack",
                        CacheState = "Ready",
                        File = f,
                    }
                );
        return plan;
    }

    static long ReadyLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    ReadyStorage MeasureReadyStorage(string root)
    {
        var all = Jobs();
        var state = ReadyState;
        long partial = Directory.EnumerateFiles(parts, "*.part").Sum(ReadyLength);
        long used = partial,
            reserved = 0;
        var byId = all.ToDictionary(j => j.Id);
        foreach (var group in state.ManagedFiles.GroupBy(f => f.ReleaseKey.Length > 0 ? f.ReleaseKey : f.JobId))
        {
            long transferred = 0,
                charge = 0;
            bool active = false;
            foreach (var file in group)
            {
                var bytes = ReadyLength(file.Path);
                used += bytes;
                transferred += Math.Max(bytes, ReadyLength(Path.Combine(parts, file.JobId + ".part")));
                charge = Math.Max(charge, file.ChargedBytes);
                if (
                    byId.TryGetValue(file.JobId, out var job)
                    && job.Status is not ("Completed" or "Cancelled" or "Failed")
                )
                    active = true;
            }
            if (active)
                reserved += Math.Max(0, charge - transferred);
        }
        var diskReserved = all.Where(j => j.Status is not ("Completed" or "Cancelled" or "Failed"))
            .Sum(j => Math.Max(0, j.Size - Math.Max(j.Bytes, ReadyLength(Path.Combine(parts, j.Id + ".part")))));
        var measured = new ReadyStorage(used, reserved, Math.Min(FreeSpace(root), FreeSpace(parts)), diskReserved);
        lock (readyGate)
            readyStorage = measured;
        return measured;
    }

    string ReadyReleaseKey(EpisodeDownloadOption option)
    {
        lock (gate)
            return releases.TryGetValue(option.ReleaseId, out var r)
                ? r.Item.GroupKey
                    + "|"
                    + r.Service
                    + "|"
                    + (r.Release.Hash.Length > 0 ? r.Release.Hash : option.Release)
                : option.ReleaseId;
    }

    Dictionary<string, long> ReadyReleaseAllowances()
    {
        var state = ReadyState;
        var all = Jobs().ToDictionary(j => j.Id);
        return state
            .ManagedFiles.Where(f => f.ReleaseKey.Length > 0)
            .GroupBy(f => f.ReleaseKey)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var transferred = g.Sum(f =>
                        Math.Max(ReadyLength(f.Path), ReadyLength(Path.Combine(parts, f.JobId + ".part")))
                    );
                    return g.Any(f =>
                        all.TryGetValue(f.JobId, out var j) && j.Status is not ("Completed" or "Cancelled" or "Failed")
                    )
                        ? Math.Max(transferred, g.Max(f => f.ChargedBytes))
                        : transferred;
                }
            );
    }

    public List<(PlannedEpisode Episode, ReadyDecision Decision)> PreviewReadyChoices(DownloadPlan plan)
    {
        InitializeReady();
        var state = ReadyState;
        var disk = MeasureReadyStorage(plan.Folder);
        var allowances = ReadyReleaseAllowances();
        var result = new List<(PlannedEpisode, ReadyDecision)>();
        foreach (var episode in plan.Episodes)
        {
            var decision = NoirReadyPolicy.Choose(
                episode.Alternatives.Select(o => new ReadySource(
                    o.File.Id,
                    o.ReleaseId,
                    o.File.Size,
                    o.ReleaseBytes,
                    o.IsPack,
                    o.CacheState is "Ready" or "On your server",
                    o.Resolution,
                    o.IsPack ? allowances.GetValueOrDefault(ReadyReleaseKey(o)) : 0
                )),
                state,
                disk,
                plan.Preferences.Resolution
            );
            result.Add((episode, decision));
            if (decision.Source is { } chosen)
            {
                var option = episode.Alternatives.First(o => o.File.Id == chosen.Id);
                disk = disk with
                {
                    ReservedBytes = disk.ReservedBytes + decision.Charge,
                    DiskReservedBytes = disk.DiskReservedBytes + option.File.Size,
                };
                if (option.IsPack)
                    allowances[ReadyReleaseKey(option)] = Math.Max(option.ReleaseBytes, option.File.Size);
            }
        }
        return result;
    }

    // A2: a manual download is recorded once, after it completes, with the size and date it was saved with, so
    // "Everything Noir downloaded" can judge it even after "Clear finished" removed the job. Files Noir did not
    // download (no completed job) are never recorded, so they are never removed.
    bool RecordNoirFiles(Dictionary<string, PcDownloadJob> all)
    {
        HashSet<string> known;
        List<ReadyManagedFile> records;
        lock (readyGate)
        {
            known = ready!.ManagedFiles.Select(f => f.JobId).Concat(ready.NoirFiles.Select(f => f.JobId)).ToHashSet();
            records = ready.NoirFiles.Select(f => new ReadyManagedFile { JobId = f.JobId, Path = f.Path }).ToList();
        }
        var added = new List<ReadyManagedFile>();
        foreach (var job in all.Values.Where(j => j.Status == "Completed" && j.Destination.Length > 0))
        {
            if (known.Contains(job.Id))
                continue;
            var info = new FileInfo(job.Destination);
            if (!info.Exists)
                continue;
            added.Add(
                new()
                {
                    JobId = job.Id,
                    ItemId = job.Target.Id,
                    Group = job.Target.GroupKey,
                    Path = job.Destination,
                    Title = job.Name,
                    Root = job.Root,
                    Movie = job.Target.IsMovie,
                    Season = job.Target.Season,
                    Episode = job.Target.Episode,
                    ImdbId = job.Target.ImdbId,
                    FileBytes = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc,
                }
            );
        }
        // Forget a record only when both its job and its file are gone.
        var gone = records
            .Where(f => !all.ContainsKey(f.JobId) && !File.Exists(f.Path))
            .Select(f => f.JobId)
            .ToHashSet();
        if (added.Count == 0 && gone.Count == 0)
            return false;
        lock (readyGate)
        {
            var now = ready!.ManagedFiles.Select(f => f.JobId).Concat(ready.NoirFiles.Select(f => f.JobId)).ToHashSet();
            ready.NoirFiles.AddRange(added.Where(f => !now.Contains(f.JobId)));
            ready.NoirFiles.RemoveAll(f => gone.Contains(f.JobId));
        }
        return true;
    }

    sealed record ReadyCandidate(ReadyManagedFile File, bool Noir, VideoItem Target, string Root, FileInfo Info);

    void ReconcileReadyFiles(DateTimeOffset now)
    {
        var all = Jobs().ToDictionary(j => j.Id);
        bool changed = RecordNoirFiles(all);
        var state = ReadyState;
        // One candidate per file that is still where its download put it.
        var candidates = new List<ReadyCandidate>();
        foreach (var file in state.ManagedFiles)
        {
            if (
                !all.TryGetValue(file.JobId, out var job)
                || job.Status != "Completed"
                || !file.Path.Equals(job.Destination, StringComparison.OrdinalIgnoreCase)
            )
                continue;
            var info = new FileInfo(file.Path);
            if (info.Exists)
                candidates.Add(new(file, false, job.Target, job.Root, info));
        }
        foreach (var file in state.NoirFiles)
        {
            var job = all.GetValueOrDefault(file.JobId);
            if (
                job != null
                && (job.Status != "Completed" || !file.Path.Equals(job.Destination, StringComparison.OrdinalIgnoreCase))
            )
                continue;
            var info = new FileInfo(file.Path);
            if (info.Exists)
                candidates.Add(
                    new(
                        file,
                        true,
                        job?.Target
                            ?? new VideoItem
                            {
                                Id = file.ItemId,
                                CollectionKey = file.Group,
                                IsMovie = file.Movie,
                                Season = file.Season,
                                Episode = file.Episode,
                                ImdbId = file.ImdbId,
                            },
                        job?.Root ?? file.Root,
                        info
                    )
                );
        }
        var preferences = store.ReadyCleanupPreferences(candidates.Select(c => c.File.Group));
        // A1 per show, A2 scope: Keep ready files always; manual downloads only in "Everything Noir downloaded",
        // and manual movies only with "Movies too".
        int Hours(ReadyCandidate c) =>
            !c.Noir || state.RemoveScope == "Noir" && (state.RemoveMovies || !(c.File.Movie || c.Target.IsMovie))
                ? NoirReadyPolicy.RemoveHours(state, preferences.GetValueOrDefault(c.File.Group))
                : -1;
        var live = candidates.Where(c => Hours(c) > 0).ToList();
        var inputs =
            live.Count > 0
                ? store.ReadyCleanupInputs(live.Select(c => c.File.ItemId))
                : new Dictionary<string, (bool Watched, DateTimeOffset? At)>();
        var profiles = live.Count > 0 ? ReadReadyProfiles() : null;
        // A3: the last N watched episodes of each show stay for a rewatch (most recently watched first).
        var keptLast = new HashSet<string>();
        foreach (
            var show in live.Where(c => inputs.GetValueOrDefault(c.File.ItemId) is (true, not null))
                .GroupBy(c => c.File.Group)
        )
        {
            var keep = NoirReadyPolicy.KeepLast(state, preferences.GetValueOrDefault(show.Key));
            foreach (
                var c in show.OrderByDescending(c => inputs[c.File.ItemId].At)
                    .ThenByDescending(c => c.Target.Season)
                    .ThenByDescending(c => c.Target.Episode)
                    .Take(keep)
            )
                keptLast.Add(c.File.JobId);
        }
        foreach (var candidate in candidates)
        {
            var (file, noir, target, root, info) = candidate;
            var before = (file.FileBytes, file.LastWriteUtc, file.RemoveAt, file.CleanupCancelled, file.RuleHours);
            string log = "",
                detail = "";
            if (file.LastWriteUtc == null)
            {
                file.FileBytes = info.Length;
                file.LastWriteUtc = info.LastWriteTimeUtc;
            }
            var hours = Hours(candidate);
            var watched = inputs.GetValueOrDefault(file.ItemId);
            var others = profiles?.Watched(target) ?? [];
            var why = NoirReadyPolicy.RuleLabel(hours) + " · " + (noir ? "Downloaded by Noir" : "Keep ready file");
            bool eligible =
                hours > 0
                && watched.Watched
                && !file.Pinned
                && !file.CleanupCancelled
                && watched.At != null
                && others.All(x => x == true)
                && !keptLast.Contains(file.JobId);
            if (!eligible)
                file.RemoveAt = null;
            else if (file.RemoveAt == null)
            {
                file.RemoveAt = NoirReadyPolicy.Schedule(null, 0, hours, watched.At!.Value, now);
                file.RuleHours = hours;
                log = "Watched file removal scheduled";
                detail = why;
            }
            else
            {
                // A1: a rule change recomputes the date (later at once, earlier never sooner than now + 1 day).
                var moved = NoirReadyPolicy.Schedule(file.RemoveAt, file.RuleHours, hours, watched.At!.Value, now);
                if (moved != file.RemoveAt)
                {
                    file.RemoveAt = moved;
                    log = "Removal date changed";
                    detail = why;
                }
                file.RuleHours = hours;
                if (
                    file.RemoveAt <= now
                    && NoirReadyPolicy.CanRemove(
                        true,
                        true,
                        watched.Watched,
                        file.Pinned,
                        file.CleanupCancelled,
                        watched.At,
                        others,
                        now,
                        hours
                    )
                )
                {
                    var latest = store.ReadyCleanupInputs([file.ItemId]).GetValueOrDefault(file.ItemId);
                    var preference = store.GetShowDownloadPreference(file.Group);
                    lock (readyGate)
                    {
                        var current = (noir ? ready!.NoirFiles : ready!.ManagedFiles).FirstOrDefault(f =>
                            f.JobId == file.JobId
                        );
                        var go =
                            !ready.Paused
                            && NoirReadyPolicy.RemoveHours(ready, preference) == hours
                            && (!noir || ready.RemoveScope == "Noir" && (ready.RemoveMovies || !target.IsMovie))
                            && current is { Pinned: false, CleanupCancelled: false }
                            && NoirReadyPolicy.CanRemove(
                                true,
                                true,
                                latest.Watched,
                                false,
                                false,
                                latest.At,
                                others,
                                now,
                                hours
                            )
                            && profiles?.Fresh() == true
                            && IsFilePlaying?.Invoke(file.Path) != true;
                        info.Refresh();
                        if (go && info.Exists)
                        {
                            if (info.Length == file.FileBytes && info.LastWriteTimeUtc == file.LastWriteUtc)
                            {
                                var recycle = ready.RecycleRemoved;
                                try
                                {
                                    DownloadNaming.Validate(root, file.Path);
                                    // A4: delete permanently (today) or the Recycle Bin, through the same remover as Manage.
                                    RemoveDisk(file.Path, recycle);
                                    file.RemoveAt = null;
                                    log =
                                        recycle && noir ? "Sent a watched Noir download to the Recycle Bin"
                                        : recycle ? "Sent a watched Keep ready file to the Recycle Bin"
                                        : noir ? "Removed a watched Noir download"
                                        : "Removed watched managed file";
                                    detail = why;
                                }
                                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                                {
                                    store.Log(e);
                                    log = "Removal failed; the file stays";
                                    detail = why;
                                }
                            }
                            else
                            {
                                file.CleanupCancelled = true;
                                file.RemoveAt = null;
                                log = "File changed; removal cancelled";
                            }
                        }
                    }
                }
            }
            lock (readyGate)
            {
                var current = (noir ? ready!.NoirFiles : ready!.ManagedFiles).FirstOrDefault(f =>
                    f.JobId == file.JobId
                );
                if (current == null)
                    continue;
                if (before != (file.FileBytes, file.LastWriteUtc, file.RemoveAt, file.CleanupCancelled, file.RuleHours))
                {
                    current.FileBytes = file.FileBytes;
                    current.LastWriteUtc = file.LastWriteUtc;
                    if (!current.Pinned && !current.CleanupCancelled)
                    {
                        current.RemoveAt = file.RemoveAt;
                        current.CleanupCancelled = file.CleanupCancelled;
                        current.RuleHours = file.RuleHours;
                    }
                    changed = true;
                }
                if (log.Length > 0)
                {
                    ready.Log(log, file.Title, detail, now);
                    changed = true;
                }
            }
        }
        // One write of noir-ready.json per pass, never a library save per removal (scale report item 2).
        if (changed)
            lock (readyGate)
                SaveReady();
        var showsRoot = Config.ShowsRoot;
        if (showsRoot.Length == 0)
            showsRoot = store.State.Roots.FirstOrDefault()?.Path ?? parts;
        MeasureReadyStorage(showsRoot);
    }

    sealed class ReadyProfileLibraries
    {
        public List<Dictionary<string, bool>?> Libraries = [];
        public Dictionary<string, (long Bytes, DateTime Written)> Files = [];

        public static (long Bytes, DateTime Written) Stamp(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.Length, info.LastWriteTimeUtc) : (-1, default);
        }

        public bool Fresh()
        {
            try
            {
                return Files.All(p => Stamp(p.Key) == p.Value);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        public List<bool?> Watched(VideoItem item) =>
            Libraries
                .Select(l =>
                    l == null ? (bool?)null
                    : l.TryGetValue(item.GroupKey + "|" + item.Season + "|" + item.Episode, out var watched)
                    || item.ImdbId.Length > 0
                        && l.TryGetValue("imdb:" + item.ImdbId + "|" + item.Season + "|" + item.Episode, out watched)
                        ? watched
                    : (bool?)null
                )
                .ToList();
    }

    ReadyProfileLibraries ReadReadyProfiles()
    {
        var result = new ReadyProfileLibraries();
        try
        {
            var directory = Path.GetFullPath(store.DataDirectory);
            var root = MobileProfiles.Root(directory);
            var registry = Path.Combine(root, "profiles.json");
            result.Files[registry] = ReadyProfileLibraries.Stamp(registry);
            var list = JsonSerializer.Deserialize<ProfileList>(File.ReadAllText(registry));
            if (list == null || list.Profiles.Count == 0)
            {
                result.Libraries.Add(null);
                return result;
            }
            foreach (var profile in list.Profiles)
            {
                if (!Profiles.ValidId(profile.Id))
                {
                    result.Libraries.Add(null);
                    continue;
                }
                var folder = profile.Id == "default" ? root : Path.Combine(root, "profiles", profile.Id);
                if (Path.GetFullPath(folder).Equals(directory, StringComparison.OrdinalIgnoreCase))
                    continue;
                var path = Path.Combine(folder, "library.json");
                var stamp = ReadyProfileLibraries.Stamp(path);
                var other = JsonSerializer.Deserialize<PlayerState>(File.ReadAllText(path));
                result.Files[path] = stamp;
                if (other == null || ReadyProfileLibraries.Stamp(path) != stamp)
                {
                    result.Libraries.Add(null);
                    continue;
                }
                var index = new Dictionary<string, bool>();
                foreach (var entry in other.Catalog.Values)
                {
                    var item = entry.Item;
                    var watched =
                        entry.Progress is not { Completed: false }
                        && (entry.Progress?.Completed == true || other.Viewings.GetValueOrDefault(item.Id)?.Count > 0);
                    index.TryAdd(item.GroupKey + "|" + item.Season + "|" + item.Episode, watched);
                    if (item.ImdbId.Length > 0)
                        index.TryAdd("imdb:" + item.ImdbId + "|" + item.Season + "|" + item.Episode, watched);
                }
                result.Libraries.Add(index);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            result.Libraries.Add(null);
        }
        return result;
    }

    public Task CheckReadyStorage() =>
        Task.Run(async () =>
        {
            if (!await readyRun.WaitAsync(0))
                return;
            try
            {
                InitializeReady();
                var root = Config.ShowsRoot;
                if (root.Length == 0)
                    root = parts;
                var disk = MeasureReadyStorage(root);
                var state = ReadyState;
                var reason =
                    disk.UsedBytes + disk.ReservedBytes > state.BudgetBytes ? "Preparation budget reached"
                    : disk.FreeBytes - disk.DiskReservedBytes < Math.Max(10 * NoirReadyPolicy.GB, state.KeepFreeBytes)
                        ? "Keeping free space available"
                    : "";
                if (reason.Length == 0)
                    return;
                var owned = state.ManagedFiles.Select(f => f.JobId).ToHashSet();
                foreach (
                    var job in Jobs()
                        .Where(j => owned.Contains(j.Id) && j.Status is "Queued" or "Preparing" or "Downloading")
                )
                {
                    Control(job.Id, "pause");
                    lock (readyGate)
                    {
                        ready!.Log(reason, job.Name);
                        if (ready.Titles.TryGetValue(job.Target.GroupKey, out var title))
                        {
                            title.State = "Blocked";
                            title.Reason = reason;
                            title.RecheckAt = DateTimeOffset.UtcNow.AddMinutes(15);
                        }
                        SaveReady();
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                store.Log(e);
            }
            finally
            {
                readyRun.Release();
            }
        });
}

public sealed partial class LibraryStore
{
    public List<ReadyEpisode> ReadyPreview(string key)
    {
        lock (gate)
        {
            var schedule = (State.EpisodeSchedules.GetValueOrDefault(key) ?? [])
                .GroupBy(e => (e.Season, e.Episode))
                .ToDictionary(g => g.Key, g => g.First().AirsAt);
            return EpisodesFor(key)
                .Select(i => new ReadyEpisode(
                    i.Id,
                    key,
                    i.Title,
                    i.Season,
                    i.Episode,
                    EverWatched(i),
                    GetProgress(i) is { Completed: false, Position: > 0 },
                    i.Available && File.Exists(i.Path),
                    schedule.GetValueOrDefault((i.Season, i.Episode))
                ))
                .ToList();
        }
    }

    public void MigrateReadyPreferences()
    {
        lock (gate)
        {
            foreach (var p in State.ShowDownloads.Values)
            {
                if (p.KeepReadyEnabled == null)
                {
                    p.KeepReadyEnabled = p.AutoDownload;
                    p.KeepReadyCount = 2;
                }
            }
            Save();
        }
    }

    // A1–A8 migration, once: a show that was not switched on becomes "Same as default" (still off under the default
    // "Only shows I switch on"), and a count of 2 (or one today's coercion turned into 2) follows the default of 2.
    public void MigrateReadyRules()
    {
        lock (gate)
        {
            foreach (var p in State.ShowDownloads.Values)
            {
                if (p.KeepReadyEnabled == false && !p.AutoDownload)
                    p.KeepReadyEnabled = null;
                if (p.KeepReadyCount is not (1 or 3 or -1))
                    p.KeepReadyCount = null;
            }
            Save();
        }
    }

    // The followed shows of the new-episode alert (BackgroundMode.CheckNewEpisodes): not movies, not dropped, not
    // hidden from Continue or the library.
    public List<IGrouping<string, VideoItem>> FollowedShows()
    {
        lock (gate)
            return State
                .Catalog.Values.Select(x => x.Item)
                .GroupBy(i => i.GroupKey)
                .Where(g =>
                    !g.First().IsMovie
                    && TitleStatus(g) != "Dropped"
                    && !State.HiddenContinue.Contains(g.Key)
                    && !State.HiddenTitles.Contains(g.Key)
                )
                .ToList();
    }

    // The alert's "still following" test: watched in the last 180 days, or caught up to this episode.
    public bool FollowingEpisode(VideoItem episode, DateTimeOffset now)
    {
        lock (gate)
        {
            var group = EpisodesFor(episode.GroupKey);
            return group.Max(LastActivity) > now.AddDays(-180) || EarlierUnwatched(episode).Count == 0;
        }
    }

    // A6 "Shows I am watching": a followed show whose next episode the alert would announce, started by the user.
    public bool WatchingShow(string group, DateTimeOffset now)
    {
        lock (gate)
        {
            if (
                State.HiddenContinue.Contains(group)
                || State.HiddenTitles.Contains(group)
                || EpisodesFor(group) is not { Count: > 0 } episodes
                || episodes[0].IsMovie
                || TitleStatus(episodes) == "Dropped"
                || !episodes.Any(EverWatched)
            )
                return false;
            return UpNextFor(episodes) is { Kind: UpNextKind.Ready or UpNextKind.NotOnPc } next
                && FollowingEpisode(next.Item, now);
        }
    }

    public HashSet<string> WatchingShows(DateTimeOffset now) =>
        FollowedShows().Select(g => g.Key).Where(k => WatchingShow(k, now)).ToHashSet();

    public HashSet<string> ReadyWatchlist()
    {
        lock (gate)
            return State.Watchlist.ToHashSet();
    }

    public Dictionary<string, ShowDownloadPreference> ReadyCleanupPreferences(IEnumerable<string> groups)
    {
        lock (gate)
            return groups
                .Distinct()
                .Where(State.ShowDownloads.ContainsKey)
                .ToDictionary(
                    g => g,
                    g =>
                        JsonSerializer.Deserialize<ShowDownloadPreference>(
                            JsonSerializer.Serialize(State.ShowDownloads[g])
                        )!
                );
    }

    public bool ReadyWatched(string id)
    {
        lock (gate)
            return State.Catalog.TryGetValue(id, out var e) && EverWatched(e.Item);
    }

    public List<(ReadyEpisode Episode, ShowDownloadPreference Preference)> ReadyInputs(
        DateTimeOffset now,
        IEnumerable<string>? watching = null
    )
    {
        lock (gate)
        {
            var result = new List<(ReadyEpisode, ShowDownloadPreference)>();
            var keys = State
                .ShowDownloads.Where(x => NoirReadyPolicy.Enabled(x.Value) || x.Value.AlsoDownload)
                .Select(x => x.Key)
                .Concat(State.Watchlist)
                // A6: shows I am watching, unless the show is switched off.
                .Concat(
                    (watching ?? []).Where(k => State.ShowDownloads.GetValueOrDefault(k)?.KeepReadyEnabled != false)
                )
                .Distinct();
            foreach (var key in keys)
            {
                var preference = GetShowDownloadPreference(key) ?? new();
                if (State.HiddenTitles.Contains(key))
                    continue;
                var episodes = EpisodesFor(key);
                if (episodes.Count == 0 || TitleStatus(episodes) == "Dropped")
                    continue;
                var schedule = (State.EpisodeSchedules.GetValueOrDefault(key) ?? [])
                    .GroupBy(e => (e.Season, e.Episode))
                    .ToDictionary(g => g.Key, g => g.First().AirsAt);
                foreach (var item in episodes)
                {
                    var p = GetProgress(item);
                    result.Add(
                        (
                            new(
                                item.Id,
                                key,
                                item.Title,
                                item.IsMovie ? 0 : item.Season,
                                item.Episode,
                                EverWatched(item),
                                p is { Completed: false, Position: > 0 },
                                item.Available && File.Exists(item.Path),
                                schedule.GetValueOrDefault((item.Season, item.Episode))
                            ),
                            preference
                        )
                    );
                }
            }
            return result;
        }
    }

    public Dictionary<string, (bool Watched, DateTimeOffset? At)> ReadyCleanupInputs(IEnumerable<string> ids)
    {
        lock (gate)
            return ids.Distinct()
                .ToDictionary(
                    id => id,
                    id =>
                    {
                        if (!State.Catalog.TryGetValue(id, out var e))
                            return (false, (DateTimeOffset?)null);
                        var p = GetProgress(e.Item);
                        return (
                            EverWatched(e.Item) && p is not { Completed: false },
                            ViewingHistory(e.Item).FirstOrDefault()?.WatchedAt
                                ?? (p?.Completed == true ? p.UpdatedAt : null)
                        );
                    }
                );
    }

    public ShowDownloadPreference? GetShowDownloadPreference(string group)
    {
        lock (gate)
            return State.ShowDownloads.TryGetValue(group, out var preference)
                ? JsonSerializer.Deserialize<ShowDownloadPreference>(JsonSerializer.Serialize(preference))
                : null;
    }

    public void SetShowDownloadPreference(string group, ShowDownloadPreference preference)
    {
        lock (gate)
        {
            State.ShowDownloads[group] = preference;
            Save();
        }
    }
}
