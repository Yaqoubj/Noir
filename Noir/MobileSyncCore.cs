using System.IO;

namespace Noir;

public sealed class MobileOperation
{
    public string Id { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string Kind { get; set; } = "";
    public long At { get; set; }
    public double Position { get; set; }
    public double Duration { get; set; }
    public bool Completed { get; set; }
    public string ViewingId { get; set; } = "";
    public int Rating { get; set; }
    public int? Rating10 { get; set; }
    public string Group { get; set; } = "";
    public List<string>? Favorites { get; set; }
    public string Reaction { get; set; } = "";
    public string Note { get; set; } = "";
    public bool Movie { get; set; }
    public bool Value { get; set; }
    public string Title { get; set; } = "";
    public string Imdb { get; set; } = "";
    public int Year { get; set; }
    public int Season { get; set; }
    public int Episode { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";

    // Logged on the phone tracker: when it was watched (0 = at At) and whether the date is a guess.
    public long WatchedAt { get; set; }
    public bool DateUnknown { get; set; }
    public bool Approximate { get; set; }

    // History imported or restored on the phone keeps where it came from ("Imported from Netflix")
    // and when it was first logged; such marks are never sent on to Trakt, Serializd or Nuvio.
    public string Source { get; set; } = "";
    public long LoggedAt { get; set; }

    // Provider ("Trakt", "Serializd") -> ISO-8601 UTC time the phone's own site sender confirmed this viewing.
    // Carried by "mark" and "progress" ops and by a "synced" op (ViewingId + this) sent when a phone job finishes later.
    public Dictionary<string, string>? Synced { get; set; }
}

/// <summary>A viewing as the phone sees it: the PC's record plus the times the PC's sender confirmed it on each site.</summary>
public sealed class MobileViewing
{
    public string Id { get; set; } = "";
    public DateTimeOffset WatchedAt { get; set; }
    public string Source { get; set; } = "";
    public bool DateUnknown { get; set; }
    public bool Approximate { get; set; }
    public DateTimeOffset? LoggedAt { get; set; }
    public Dictionary<string, string>? Synced { get; set; }
}

public sealed class MobileItem
{
    public Dictionary<string, long> ViewingTimes { get; set; } = new();
    public List<string> DeletedViewings { get; set; } = [];
    public List<MobileSubtitle> SubtitleFiles { get; set; } = [];
    public string Status { get; set; } = "";
    public bool CaughtUp { get; set; }
    public bool InLibrary { get; set; }
    public long LibraryAt { get; set; }
    public long StatusAt { get; set; }
    public DateTimeOffset? AddedAt { get; set; }
    public string EpisodeImage { get; set; } = "";
    public List<string> Collections { get; set; } = [];
    public Dictionary<string, long> CollectionTimes { get; set; } = new();
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string Title { get; set; } = "";
    public string EpisodeTitle { get; set; } = "";
    public string ReviewTitle { get; set; } = "";
    public int Year { get; set; }
    public bool Movie { get; set; }
    public int Season { get; set; }
    public int Episode { get; set; }
    public bool Available { get; set; }
    public string Summary { get; set; } = "";
    public string ShowSummary { get; set; } = "";
    public Dictionary<string, string> Reviews { get; set; } = new();
    public string Genres { get; set; } = "";
    public string Imdb { get; set; } = "";
    public string EpisodeImdb { get; set; } = "";
    public string Poster { get; set; } = "";
    public string Backdrop { get; set; } = "";
    public double Duration { get; set; }
    public Bookmark? Progress { get; set; }
    public List<MobileViewing> Viewings { get; set; } = [];
    public PersonalReview? Review { get; set; }
    public bool Listed { get; set; }
    public IntroSegment? Intro { get; set; }
    public IntroSegment? Outro { get; set; }
    public long ReviewAt { get; set; }
    public long ListAt { get; set; }
}

public sealed class MobileSnapshot
{
    public string Revision { get; set; } = "";
    public List<string> Favorites { get; set; } = [];
    public List<string> Likes { get; set; } = [];
    public Dictionary<string, int> SeasonRatings { get; set; } = new();
    public Dictionary<string, string> Reactions { get; set; } = new();
    public Dictionary<string, long> FieldTimes { get; set; } = new();
    public string Subtitles { get; set; } = "English";
    public List<string> Collections { get; set; } = [];
    public Dictionary<string, long> DeletedCollections { get; set; } = new();
    public Dictionary<string, List<KnownEpisode>> Schedules { get; set; } = new();
    public int Schema { get; set; } = 1;
    public long ServerTime { get; set; }
    public string Instance { get; set; } = "";
    public List<MobileItem> Items { get; set; } = [];
    public bool AutoNext { get; set; }
    public bool AutoSkipIntro { get; set; }
    public bool MarkWatchedAtCredits { get; set; }
    public bool HideSpoilers { get; set; }

    // The PC's watch-sync switches; the phone uses them unless its own switch overrides them.
    public bool SyncTrakt { get; set; }
    public bool SyncSerializd { get; set; }

    // The PC profile this library belongs to; the phone shows it next to the pairing.
    public string ProfileName { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ProfileColor { get; set; } = "";
    public string HostId { get; set; } = "";
    public Dictionary<string, int> ShowRatings { get; set; } = new();
    public Dictionary<string, long> ShowRatingTimes { get; set; } = new();
    public Dictionary<string, string> ShowStates { get; set; } = new();
    public Dictionary<string, int> ShowRuntimes { get; set; } = new();
    public List<string> IncludeSpecials { get; set; } = [];
}

public sealed partial class LibraryStore
{
    readonly string mobileRevisionSession = Guid.NewGuid().ToString("N");

    public string MobileRevision()
    {
        lock (gate)
        {
            var profile = MobileProfiles.Active(DataDirectory).Id;
            var profiles = Path.Combine(MobileProfiles.Root(DataDirectory), "profiles.json");
            return profile
                + ":"
                + State.MobileInstanceId
                + ":"
                + mobileRevisionSession
                + ":"
                + saveRevision
                + ":"
                + File.GetLastWriteTimeUtc(profiles).Ticks;
        }
    }

    // Set by the window: the PC profile name sent to the phone.
    public Func<string>? ProfileTitle { get; set; }

    // This is a projection, never the library file: roots, paths, cookies and account data stay on PC.
    public MobileSnapshot MobileSnapshot()
    {
        LibraryStore projection;
        string revision;
        lock (gate)
        {
            revision = MobileRevision();
            projection = new(this, LibrarySnapshot.Copy(State));
        }
        var result = projection.BuildMobileSnapshot();
        result.Revision = revision;
        return result;
    }

    MobileSnapshot BuildMobileSnapshot()
    {
        lock (gate)
        {
            // One listing per folder answers every "does this file exist" and subtitle lookup below.
            var folders = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> Folder(string dir)
            {
                if (folders.TryGetValue(dir, out var set))
                    return set;
                try
                {
                    set = Directory.Exists(dir)
                        ? Directory.EnumerateFiles(dir).Take(20000).ToHashSet(StringComparer.OrdinalIgnoreCase)
                        : [];
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    set = [];
                }
                return folders[dir] = set;
            }
            bool Has(string path)
            {
                if (!Path.IsPathFullyQualified(path))
                    return false;
                try
                {
                    return Path.GetDirectoryName(path) is { Length: > 0 } dir && Folder(dir).Contains(path);
                }
                catch (Exception e) when (e is ArgumentException or PathTooLongException)
                {
                    return false;
                }
            }
            var result = new MobileSnapshot
            {
                ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Instance = State.MobileInstanceId,
                DeletedCollections = State
                    .MobileFieldTimes.Where(p => p.Key.StartsWith("collectionDeleted:"))
                    .ToDictionary(p => p.Key[18..], p => p.Value),
                Subtitles = State.Subtitles,
                AutoNext = State.AutoNext,
                AutoSkipIntro = State.AutoSkipIntro,
                MarkWatchedAtCredits = State.MarkWatchedAtCredits,
                HideSpoilers = State.HideSpoilers,
                SyncTrakt = State.SyncTrakt,
                SyncSerializd = State.SyncSerializd,
                ProfileName = ProfileTitle?.Invoke() ?? "",
                Collections = State.Collections.Keys.ToList(),
                ShowRatings = new(State.ShowRatings),
                ShowRatingTimes = State
                    .MobileFieldTimes.Where(p => p.Key.StartsWith("showRating:", StringComparison.Ordinal))
                    .ToDictionary(p => p.Key[11..], p => p.Value),
                ShowStates = new(State.ShowStates),
                ShowRuntimes = new(State.ShowRuntimes),
                IncludeSpecials = State.IncludeSpecials.ToList(),
                Schedules = State.EpisodeSchedules.ToDictionary(
                    p => p.Key,
                    p =>
                        p.Value.Select(e => new KnownEpisode
                            {
                                Season = e.Season,
                                Episode = e.Episode,
                                Title = e.Title,
                                AirsAt = e.AirsAt,
                                Runtime = e.Runtime,
                                Image = Has(e.Image) ? Path.GetFileName(e.Image) : "",
                            })
                            .ToList()
                ),
            };
            var viewingTimes = State
                .MobileFieldTimes.Where(p =>
                    p.Key.StartsWith("viewingDate:", StringComparison.Ordinal) && p.Key.LastIndexOf(':') > 12
                )
                .GroupBy(p => p.Key[12..p.Key.LastIndexOf(':')])
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(p => p.Key[(p.Key.LastIndexOf(':') + 1)..], p => p.Value)
                );
            result.Favorites = State.Favorites.Distinct().Take(4).ToList();
            result.Likes = State.Likes.ToList();
            result.SeasonRatings = new(State.SeasonRatings);
            result.Reactions = new(State.Reactions);
            result.FieldTimes = State
                .MobileFieldTimes.Where(p =>
                    p.Key == "favorites"
                    || p.Key.StartsWith("like:", StringComparison.Ordinal)
                    || p.Key.StartsWith("seasonRating:", StringComparison.Ordinal)
                    || p.Key.StartsWith("reaction:", StringComparison.Ordinal)
                )
                .ToDictionary(p => p.Key, p => p.Value);
            var deletedViewings = State
                .MobileFieldTimes.Keys.Where(k =>
                    k.StartsWith("viewingDeleted:", StringComparison.Ordinal) && k.LastIndexOf(':') > 15
                )
                .GroupBy(k => k[15..k.LastIndexOf(':')])
                .ToDictionary(g => g.Key, g => g.Select(k => k[(k.LastIndexOf(':') + 1)..]).ToList());
            var library = LibraryGroups();
            foreach (var group in Archive.Select(i => i.GroupKey).Distinct())
            {
                var episodes = EpisodesFor(group);
                var status = TitleStatus(episodes);
                var caughtUp = CaughtUp(group, DateTimeOffset.UtcNow);
                var collections = State.Collections.Where(c => c.Value.Contains(group)).Select(c => c.Key).ToList();
                var collectionTimes = State.Collections.Keys.ToDictionary(
                    n => n,
                    n => State.MobileFieldTimes.GetValueOrDefault("collection:" + n + ":" + group)
                );
                foreach (var i in episodes)
                {
                    var p = GetProgress(i);
                    result.Items.Add(
                        new()
                        {
                            ViewingTimes = viewingTimes.GetValueOrDefault(i.Id, []),
                            DeletedViewings = deletedViewings.GetValueOrDefault(i.Id, []),
                            SubtitleFiles = !Has(i.Path)
                                ? []
                                : MobileSubtitles.Find(i, Folder).Select(s => s.Info).ToList(),
                            Id = i.Id,
                            Group = i.GroupKey,
                            Title = i.Title,
                            EpisodeTitle = i.EpisodeTitle,
                            ReviewTitle = i.ReviewTitle,
                            Year = i.Year,
                            Movie = i.IsMovie,
                            Season = i.Season,
                            Episode = i.Episode,
                            AddedAt = i.AddedAt,
                            Status = status,
                            StatusAt = State.MobileFieldTimes.GetValueOrDefault("status:" + group),
                            CaughtUp = caughtUp,
                            InLibrary = library.Contains(group),
                            LibraryAt = State.MobileFieldTimes.GetValueOrDefault("library:" + group),
                            EpisodeImage = Has(i.EpisodeImage) ? Path.GetFileName(i.EpisodeImage) : "",
                            Collections = collections.ToList(),
                            CollectionTimes = new(collectionTimes),
                            Available = i.Available && Has(i.Path),
                            Summary = i.EpisodeSummary.Length > 0 ? i.EpisodeSummary : i.Summary,
                            ShowSummary = i.Summary,
                            Reviews = ReviewLinks.Sources(i, State),
                            Genres = i.Genres,
                            Imdb = i.ImdbId,
                            EpisodeImdb = i.EpisodeImdbId,
                            Poster = Has(i.Poster) ? Path.GetFileName(i.Poster) : "",
                            Backdrop = Has(i.Backdrop) ? Path.GetFileName(i.Backdrop) : "",
                            Duration = i.Duration,
                            Progress =
                                p == null
                                    ? null
                                    : new()
                                    {
                                        ViewingId = p.ViewingId,
                                        Position = p.Position,
                                        Duration = p.Duration,
                                        Completed = p.Completed,
                                        UpdatedAt = p.UpdatedAt,
                                    },
                            Viewings = ViewingHistory(i)
                                .Select(v => new MobileViewing
                                {
                                    Id = v.Id,
                                    WatchedAt = v.WatchedAt,
                                    Source = v.Source,
                                    DateUnknown = v.DateUnknown,
                                    Approximate = v.Approximate,
                                    LoggedAt = v.LoggedAt,
                                    Synced = CommunityQueue.SyncedTimes(State, i.Id, v, ViewingHistory(i)),
                                })
                                .ToList(),
                            Review = State.PersonalReviews.TryGetValue(i.Id, out var review)
                                ? new() { Rating10 = review.Rating10, Note = review.Note }
                                : null,
                            Listed = State.Watchlist.Contains(group),
                            Intro = GetIntro(i),
                            Outro = GetOutro(i),
                            ReviewAt = State.MobileFieldTimes.GetValueOrDefault("review:" + i.Id),
                            ListAt = State.MobileFieldTimes.GetValueOrDefault("list:" + group),
                        }
                    );
                }
            }
            MobileProfiles.Identify(result, DataDirectory);
            return result;
        }
    }

    Dictionary<string, (long Date, bool Unknown, bool Approximate, long Clock)>? mobileKnownViews;
    Dictionary<string, HashSet<string>> mobileKnownCollections = [];
    Dictionary<string, long> mobileKnownCollectionTimes = [];
    Dictionary<string, string> mobileKnownHistory = [];
    Dictionary<string, long> mobileKnownHistoryTimes = [];

    Dictionary<string, string> MobileHistoryFields()
    {
        var fields = new Dictionary<string, string>
        {
            { "favorites", System.Text.Json.JsonSerializer.Serialize(State.Favorites) },
        };
        foreach (var key in State.Likes)
            fields["like:" + key] = "true";
        foreach (var p in State.SeasonRatings)
            fields["seasonRating:" + p.Key] = p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var p in State.Reactions)
            fields["reaction:" + p.Key] = p.Value;
        foreach (var p in State.PersonalReviews)
            fields["review:" + p.Key] = System.Text.Json.JsonSerializer.Serialize(p.Value);
        return fields;
    }

    void CaptureMobileChanges()
    {
        mobileKnownViews = State
            .Viewings.SelectMany(p =>
                p.Value.Select(v =>
                    (
                        Key: p.Key + ":" + v.Id,
                        Value: (
                            v.WatchedAt.ToUnixTimeMilliseconds(),
                            v.DateUnknown,
                            v.Approximate,
                            State.MobileFieldTimes.GetValueOrDefault("viewingDate:" + p.Key + ":" + v.Id)
                        )
                    )
                )
            )
            .GroupBy(p => p.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);
        mobileKnownCollections = State.Collections.ToDictionary(p => p.Key, p => new HashSet<string>(p.Value));
        mobileKnownCollectionTimes = State
            .MobileFieldTimes.Where(p => p.Key.StartsWith("collection:"))
            .ToDictionary(p => p.Key, p => p.Value);
        mobileKnownHistory = MobileHistoryFields();
        mobileKnownHistoryTimes = mobileKnownHistory.Keys.ToDictionary(
            k => k,
            k => State.MobileFieldTimes.GetValueOrDefault(k)
        );
    }

    void RememberMobileViewing(string id, ViewingRecord v)
    {
        if (mobileKnownViews != null)
            mobileKnownViews[id + ":" + v.Id] = (
                v.WatchedAt.ToUnixTimeMilliseconds(),
                v.DateUnknown,
                v.Approximate,
                State.MobileFieldTimes.GetValueOrDefault("viewingDate:" + id + ":" + v.Id)
            );
    }

    void TrackMobileChanges()
    {
        if (mobileKnownViews == null)
        {
            CaptureMobileChanges();
            return;
        }
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var seen = new HashSet<string>();
        foreach (var (id, views) in State.Viewings)
        foreach (var v in views)
        {
            var key = id + ":" + v.Id;
            if (!seen.Add(key))
                continue;
            var field = "viewingDate:" + key;
            if (
                mobileKnownViews.TryGetValue(key, out var old)
                && (
                    old.Date != v.WatchedAt.ToUnixTimeMilliseconds()
                    || old.Unknown != v.DateUnknown
                    || old.Approximate != v.Approximate
                )
                && State.MobileFieldTimes.GetValueOrDefault(field) <= old.Clock
            )
                State.MobileFieldTimes[field] = Math.Max(now, old.Clock + 1);
            RememberMobileViewing(id, v);
        }
        foreach (var key in mobileKnownViews.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            State.MobileFieldTimes.TryAdd("viewingDeleted:" + key, now);
            mobileKnownViews.Remove(key);
        }
        foreach (var (name, members) in mobileKnownCollections)
        {
            if (!State.Collections.TryGetValue(name, out var next))
            {
                State.MobileFieldTimes.TryAdd("collectionDeleted:" + name, now);
                continue;
            }
            foreach (var group in members.Except(next).Concat(next.Except(members)))
            {
                var field = "collection:" + name + ":" + group;
                if (
                    State.MobileFieldTimes.GetValueOrDefault(field)
                    <= mobileKnownCollectionTimes.GetValueOrDefault(field)
                )
                    State.MobileFieldTimes[field] = Math.Max(
                        now,
                        mobileKnownCollectionTimes.GetValueOrDefault(field) + 1
                    );
            }
        }
        mobileKnownCollections = State.Collections.ToDictionary(p => p.Key, p => new HashSet<string>(p.Value));
        mobileKnownCollectionTimes = State
            .MobileFieldTimes.Where(p => p.Key.StartsWith("collection:"))
            .ToDictionary(p => p.Key, p => p.Value);
        var history = MobileHistoryFields();
        foreach (var key in history.Keys.Union(mobileKnownHistory.Keys))
            if (
                history.GetValueOrDefault(key) != mobileKnownHistory.GetValueOrDefault(key)
                && State.MobileFieldTimes.GetValueOrDefault(key) <= mobileKnownHistoryTimes.GetValueOrDefault(key)
            )
                State.MobileFieldTimes[key] = Math.Max(now, mobileKnownHistoryTimes.GetValueOrDefault(key) + 1);
        mobileKnownHistory = history;
        mobileKnownHistoryTimes = history.Keys.ToDictionary(k => k, k => State.MobileFieldTimes.GetValueOrDefault(k));
    }

    public void SetFavorites(IEnumerable<string> keys)
    {
        lock (gate)
        {
            State.Favorites = keys.Where(HistoryKey).Distinct().Take(4).ToList();
            TouchMobileField("favorites");
            Save();
        }
    }

    public void SetLike(string key, bool liked)
    {
        if (!HistoryKey(key))
            return;
        lock (gate)
        {
            if (liked)
                State.Likes.Add(key);
            else
                State.Likes.Remove(key);
            TouchMobileField("like:" + key);
            Save();
        }
    }

    public void SetSeasonRating(string group, int season, int value10)
    {
        if (!HistoryKey(group) || season is < 0 or > 9999 || value10 is < 0 or > 10)
            return;
        lock (gate)
        {
            var key = group + ":" + season;
            if (value10 == 0)
                State.SeasonRatings.Remove(key);
            else
                State.SeasonRatings[key] = value10;
            TouchMobileField("seasonRating:" + key);
            Save();
        }
    }

    public void SetReaction(string id, string mood)
    {
        if (!HistoryKey(id) || !ValidReaction(mood))
            return;
        lock (gate)
        {
            if (mood.Length == 0)
                State.Reactions.Remove(id);
            else
                State.Reactions[id] = mood;
            TouchMobileField("reaction:" + id);
            Save();
        }
    }

    public void SetReview10(VideoItem item, int value10, string note)
    {
        if (value10 is < 0 or > 10 || note.Length > 10000)
            return;
        lock (gate)
        {
            Remember(item);
            State.PersonalReviews[item.Id] = new() { Rating10 = value10, Note = note };
            TouchMobileField("review:" + item.Id);
            Save();
        }
    }

    static bool HistoryKey(string key) =>
        !string.IsNullOrWhiteSpace(key) && key.Length <= 512 && !key.Any(char.IsControl);

    static bool ValidReaction(string mood) => mood is "" or "loved" or "funny" or "wow" or "sad" or "meh";

    public void TouchMobileField(string field)
    {
        lock (gate)
            State.MobileFieldTimes[field] = Math.Max(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                State.MobileFieldTimes.GetValueOrDefault(field) + 1
            );
    }

    public List<string> MergeMobile(IEnumerable<MobileOperation> operations)
    {
        var acknowledged = new List<string>();
        bool watched = false;
        lock (gate)
        {
            var lookup = Archive
                .Select(i => i.GroupKey)
                .Distinct()
                .SelectMany(EpisodesFor)
                .GroupBy(i => i.Id)
                .ToDictionary(g => g.Key, g => g.First());
            var knownGroups = lookup.Values.Select(i => i.GroupKey).ToHashSet();
            foreach (var alias in State.CatalogAliases)
                if (lookup.TryGetValue(alias.Value, out var canonical))
                    lookup.TryAdd(alias.Key, canonical);
            foreach (var op in operations)
            {
                if (
                    !Guid.TryParse(op.Id, out _)
                    || op.At < 946684800000
                    || op.At > DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds()
                )
                    continue;
                if (
                    op.Kind is "progress" or "mark"
                    && (
                        !double.IsFinite(op.Position)
                        || !double.IsFinite(op.Duration)
                        || op.Position < 0
                        || op.Duration < 0
                        || op.Duration > 604800
                        || op.Position > 604800
                        || !Guid.TryParse(op.ViewingId, out _)
                    )
                )
                    continue;
                lookup.TryGetValue(op.ItemId, out var item);
                var phoneTitle =
                    (
                        op.ItemId.StartsWith("discover-", StringComparison.Ordinal)
                        || op.ItemId.StartsWith("episode-", StringComparison.Ordinal)
                    )
                    && op.Imdb.Length > 0;
                // Titles logged or followed on the phone's tracker join the PC library on first contact.
                if (
                    phoneTitle
                    && op.Kind
                        is "progress"
                            or "mark"
                            or "status"
                            or "list"
                            or "review"
                            or "showRating"
                            or "library"
                            or "like"
                            or "seasonRating"
                            or "reaction"
                )
                {
                    var imported = PhoneDiscoveredItem(op);
                    if (imported != null)
                    {
                        item = imported;
                        lookup[op.ItemId] = item;
                        knownGroups.Add(item.GroupKey);
                    }
                }
                if (State.MobileReceipts.Contains(op.Id))
                {
                    acknowledged.Add(op.Id);
                    continue;
                }
                if (op.Kind is "favorites" or "like" or "seasonRating" or "reaction")
                {
                    string Canonical(string key) => lookup.TryGetValue(key, out var known) ? known.GroupKey : key;
                    var group =
                        op.Group.Length > 0
                            ? op.Kind == "like" && lookup.ContainsKey(op.Group)
                                ? lookup[op.Group].Id
                                : Canonical(op.Group)
                            : item?.GroupKey ?? Canonical(op.ItemId);
                    if (
                        item != null
                        && op.Group.Length > 0
                        && op.Group != item.Id
                        && !knownGroups.Contains(op.Group)
                        && !(op.Kind == "like" && lookup.ContainsKey(op.Group))
                    )
                        group = item.GroupKey;
                    var field =
                        op.Kind == "favorites" ? "favorites"
                        : op.Kind == "reaction" ? "reaction:" + (item?.Id ?? op.ItemId)
                        : op.Kind + ":" + group + (op.Kind == "seasonRating" ? ":" + op.Season : "");
                    if (
                        op.Kind == "favorites" && (op.Favorites == null || op.Favorites.Any(k => !HistoryKey(k)))
                        || op.Kind == "like" && !HistoryKey(group)
                        || op.Kind == "seasonRating"
                            && (!HistoryKey(group) || op.Season is < 0 or > 9999 || op.Rating10 is null or < 0 or > 10)
                        || op.Kind == "reaction" && (item == null || !ValidReaction(op.Reaction))
                    )
                        continue;
                    if (op.At > State.MobileFieldTimes.GetValueOrDefault(field))
                    {
                        if (op.Kind == "favorites")
                            State.Favorites = op.Favorites!.Select(Canonical).Distinct().Take(4).ToList();
                        else if (op.Kind == "like")
                        {
                            if (op.Value)
                                State.Likes.Add(group);
                            else
                                State.Likes.Remove(group);
                        }
                        else if (op.Kind == "seasonRating")
                        {
                            var key = group + ":" + op.Season;
                            if (op.Rating10 == 0)
                                State.SeasonRatings.Remove(key);
                            else
                                State.SeasonRatings[key] = op.Rating10!.Value;
                        }
                        else if (op.Reaction.Length == 0)
                            State.Reactions.Remove(item!.Id);
                        else
                            State.Reactions[item!.Id] = op.Reaction;
                        State.MobileFieldTimes[field] = op.At;
                    }
                    State.MobileReceipts.Add(op.Id);
                    acknowledged.Add(op.Id);
                    continue;
                }
                if (op.Kind == "unview")
                {
                    if (op.ViewingId.Length > 64)
                    {
                        continue;
                    }
                    if (item == null && phoneTitle)
                        item = Archive.FirstOrDefault(i => i.ImdbId == op.Imdb && i.IsMovie == op.Movie) is { } known
                            ? EpisodesFor(known.GroupKey)
                                .FirstOrDefault(i => op.Movie || i.Season == op.Season && i.Episode == op.Episode)
                            : null;
                    // Nothing to forget: the viewing never reached this PC.
                    if (item == null && phoneTitle && Guid.TryParse(op.Id, out _))
                    {
                        State.MobileReceipts.Add(op.Id);
                        acknowledged.Add(op.Id);
                        continue;
                    }
                }
                if (op.Kind is "collectionRename" or "collectionDelete")
                {
                    var name = op.Name.Trim();
                    var next = op.Status.Trim();
                    if (
                        name.Length is < 1 or > 60
                        || name is "All" or "My List"
                        || op.Kind == "collectionRename" && (next.Length is < 1 or > 60 || next is "All" or "My List")
                    )
                        continue;
                    if (op.At > State.MobileFieldTimes.GetValueOrDefault("collectionName:" + name))
                    {
                        if (State.Collections.Remove(name, out var members) && op.Kind == "collectionRename")
                        {
                            if (!State.Collections.TryGetValue(next, out var target))
                                State.Collections[next] = target = [];
                            target.UnionWith(members);
                            foreach (var group in members)
                                State.MobileFieldTimes["collection:" + next + ":" + group] = op.At;
                        }
                        State.MobileFieldTimes["collectionDeleted:" + name] = op.At;
                        State.MobileFieldTimes["collectionName:" + name] = op.At;
                    }
                    State.MobileReceipts.Add(op.Id);
                    acknowledged.Add(op.Id);
                    continue;
                }
                if (item == null)
                    continue;
                if (State.MobileReceipts.Contains(op.Id))
                {
                    acknowledged.Add(op.Id);
                    continue;
                }
                if (op.Kind is "progress" or "mark")
                {
                    if (
                        !double.IsFinite(op.Position)
                        || !double.IsFinite(op.Duration)
                        || op.Position < 0
                        || op.Duration < 0
                        || op.Duration > 604800
                        || op.Position > 604800
                        || !Guid.TryParse(op.ViewingId, out _)
                    )
                        continue;
                    if (State.MobileFieldTimes.ContainsKey("viewingDeleted:" + item.Id + ":" + op.ViewingId))
                    {
                        State.MobileReceipts.Add(op.Id);
                        acknowledged.Add(op.Id);
                        continue;
                    }
                    var entry = Remember(item);
                    SeedViewing(entry);
                    var at = DateTimeOffset.FromUnixTimeMilliseconds(op.At);
                    var when =
                        op.Kind == "mark" && op.WatchedAt > 0 && op.WatchedAt <= op.At
                            ? DateTimeOffset.FromUnixTimeMilliseconds(op.WatchedAt)
                            : at;
                    if (!op.DateUnknown)
                        when = NotBeforeAiring(item, when);
                    // The same viewing recorded on both devices (phone playback, then a PC check-off) is kept once.
                    var duplicate =
                        !op.Value
                        && !op.DateUnknown
                        && ViewingHistory(item)
                            .Any(v =>
                                v.Id != op.ViewingId && !v.DateUnknown && Math.Abs((v.WatchedAt - when).TotalHours) < 6
                            );
                    if (op.Completed && !duplicate && !ViewingHistory(item).Any(v => v.Id == op.ViewingId))
                    {
                        var fromImport = op.Kind == "mark" && op.Source.Length is > 0 and <= 80;
                        AddViewing(
                            item,
                            op.ViewingId,
                            when,
                            fromImport ? op.Source
                                : op.Kind == "mark" ? "Marked on Android"
                                : "Android playback"
                        );
                        var added = State.Viewings[item.Id].First(v => v.Id == op.ViewingId);
                        added.LoggedAt =
                            fromImport && op.LoggedAt > 0 && op.LoggedAt <= op.At
                                ? DateTimeOffset.FromUnixTimeMilliseconds(op.LoggedAt)
                                : at;
                        if (op.DateUnknown && op.Kind == "mark")
                            added.DateUnknown = true;
                        else if (op.Approximate && op.Kind == "mark")
                            added.Approximate = true;
                        if (!op.DateUnknown && !fromImport)
                            CommunityQueue.Enqueue(State, item, when);
                        watched = true;
                    }
                    if (op.Completed)
                        CommunityQueue.ApplyPhoneSynced(State, item.Id, op.Synced, when);
                    if (entry.Progress == null || at > entry.Progress.UpdatedAt)
                    {
                        entry.Progress = new()
                        {
                            ViewingId = op.ViewingId,
                            Position = op.Position,
                            Duration = op.Duration,
                            Completed = op.Completed,
                            UpdatedAt = at,
                        };
                        if (item.Path.Length > 0)
                            State.Progress[item.Path] = entry.Progress;
                        if (op.Kind == "mark" && !op.Completed)
                            CommunityQueue.CancelPending(State, item.Id);
                    }
                }
                else if (op.Kind is "redate" or "deleteViewing")
                {
                    if (
                        !Guid.TryParse(op.ViewingId, out _)
                        || op.Kind == "redate" && (op.WatchedAt < 0 || op.WatchedAt > op.At)
                    )
                        continue;
                    var field = "viewingDate:" + item.Id + ":" + op.ViewingId;
                    var deleted = "viewingDeleted:" + item.Id + ":" + op.ViewingId;
                    if (op.Kind == "deleteViewing")
                    {
                        State.MobileFieldTimes[deleted] = Math.Max(
                            op.At,
                            State.MobileFieldTimes.GetValueOrDefault(deleted)
                        );
                        RemoveViewing(item, op.ViewingId);
                    }
                    else if (
                        !State.MobileFieldTimes.ContainsKey(deleted)
                        && op.At > State.MobileFieldTimes.GetValueOrDefault(field)
                    )
                    {
                        if (
                            State.Viewings.GetValueOrDefault(item.Id)?.FirstOrDefault(v => v.Id == op.ViewingId) is
                            { } viewing
                        )
                        {
                            viewing.WatchedAt = DateTimeOffset.FromUnixTimeMilliseconds(op.WatchedAt);
                            viewing.DateUnknown = op.DateUnknown;
                            viewing.Approximate = op.Approximate && !op.DateUnknown;
                        }
                        State.MobileFieldTimes[field] = op.At;
                    }
                }
                else if (op.Kind == "unview")
                {
                    if (State.Viewings.TryGetValue(item.Id, out var list))
                    {
                        // An offline uncheck only forgets what existed when it happened, never newer viewings.
                        var uncheckedAt = DateTimeOffset.FromUnixTimeMilliseconds(op.At);
                        foreach (
                            var v in list.Where(v =>
                                op.ViewingId.Length > 0
                                    ? v.Id == op.ViewingId
                                    : v.DateUnknown || v.WatchedAt <= uncheckedAt
                            )
                        )
                            State.MobileFieldTimes["viewingDeleted:" + item.Id + ":" + v.Id] = op.At;
                        list.RemoveAll(v =>
                            op.ViewingId.Length > 0 ? v.Id == op.ViewingId : v.DateUnknown || v.WatchedAt <= uncheckedAt
                        );
                        if (list.Count == 0)
                        {
                            State.Viewings.Remove(item.Id);
                            if (
                                State.Catalog.GetValueOrDefault(item.Id) is { Progress: { Completed: true } } gone
                                && gone.Progress.UpdatedAt <= uncheckedAt
                            )
                            {
                                gone.Progress = new()
                                {
                                    Position = 0,
                                    Duration = gone.Progress.Duration,
                                    Completed = false,
                                    UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(op.At),
                                    ViewingId = Guid.NewGuid().ToString("N"),
                                };
                                if (item.Path.Length > 0)
                                    State.Progress[item.Path] = gone.Progress;
                            }
                            CommunityQueue.CancelPending(State, item.Id);
                            ForgetOnNuvio(item);
                        }
                    }
                }
                else if (op.Kind == "library")
                {
                    if (op.At > State.MobileFieldTimes.GetValueOrDefault("library:" + item.GroupKey))
                    {
                        SetInLibrary(item.GroupKey, op.Value);
                        State.MobileFieldTimes["library:" + item.GroupKey] = op.At;
                    }
                }
                else if (op.Kind == "showRating")
                {
                    if (
                        !System.Text.RegularExpressions.Regex.IsMatch(op.Name, @"\A(S\d{1,4})?\z")
                        || op.Rating is < 0 or > 5
                    )
                        continue;
                    var key = item.GroupKey + (op.Name.Length > 0 ? ":" + op.Name : "");
                    if (op.At > State.MobileFieldTimes.GetValueOrDefault("showRating:" + key))
                    {
                        if (op.Rating == 0)
                            State.ShowRatings.Remove(key);
                        else
                            State.ShowRatings[key] = op.Rating;
                        State.MobileFieldTimes["showRating:" + key] = op.At;
                    }
                }
                else if (op.Kind == "match")
                {
                    var field = "match:" + item.GroupKey;
                    if (op.At > State.MobileFieldTimes.GetValueOrDefault(field))
                    {
                        try
                        {
                            CorrectMatch(item, op.Title, op.Year, op.Imdb, op.Season, op.Episode, op.Value);
                        }
                        catch (ArgumentException)
                        {
                            continue;
                        }
                        State.MobileFieldTimes[field] = op.At;
                    }
                }
                else if (op.Kind is "status" or "collection")
                {
                    if (
                        op.Kind == "status" && !Statuses.Contains(op.Status)
                        || op.Kind == "collection"
                            && (op.Name.Trim().Length is < 1 or > 60 || op.Name.Trim() is "All" or "My List")
                    )
                        continue;
                    var field =
                        op.Kind + ":" + (op.Kind == "status" ? item.GroupKey : op.Name.Trim() + ":" + item.GroupKey);
                    if (op.At > State.MobileFieldTimes.GetValueOrDefault(field))
                    {
                        if (op.Kind == "status")
                        {
                            if (op.Status == "Automatic")
                                State.TitleStatuses.Remove(item.GroupKey);
                            else
                                State.TitleStatuses[item.GroupKey] = op.Status;
                        }
                        else
                        {
                            var name = op.Name.Trim();
                            if (op.At <= State.MobileFieldTimes.GetValueOrDefault("collectionDeleted:" + name))
                            {
                                State.MobileReceipts.Add(op.Id);
                                acknowledged.Add(op.Id);
                                continue;
                            }
                            if (!State.Collections.TryGetValue(name, out var members))
                                State.Collections[name] = members = [];
                            if (op.Value)
                                members.Add(item.GroupKey);
                            else
                                members.Remove(item.GroupKey);
                        }
                        State.MobileFieldTimes[field] = op.At;
                    }
                }
                else if (op.Kind is "review" or "list")
                {
                    if (op.Note.Length > 10000 || op.Rating < 0 || op.Rating > 5 || op.Rating10 is < 0 or > 10)
                        continue;
                    var field = op.Kind + ":" + (op.Kind == "list" ? item.GroupKey : item.Id);
                    if (op.At > State.MobileFieldTimes.GetValueOrDefault(field))
                    {
                        if (op.Kind == "review")
                            State.PersonalReviews[item.Id] = new()
                            {
                                Rating10 = op.Rating10 ?? op.Rating * 2,
                                Note = op.Note,
                            };
                        else if (op.Value)
                            State.Watchlist.Add(item.GroupKey);
                        else
                            State.Watchlist.Remove(item.GroupKey);
                        State.MobileFieldTimes[field] = op.At;
                    }
                }
                else if (op.Kind == "synced")
                {
                    if (!Guid.TryParse(op.ViewingId, out _))
                        continue;
                    var known = State.Viewings.GetValueOrDefault(item.Id)?.FirstOrDefault(v => v.Id == op.ViewingId);
                    CommunityQueue.ApplyPhoneSynced(
                        State,
                        item.Id,
                        op.Synced,
                        known?.WatchedAt
                            ?? DateTimeOffset.FromUnixTimeMilliseconds(
                                op.WatchedAt > 0 && op.WatchedAt <= op.At ? op.WatchedAt : op.At
                            )
                    );
                }
                else
                    continue;
                State.MobileReceipts.Add(op.Id);
                acknowledged.Add(op.Id);
            }
            // Bound transport receipts. Evicted repeats are still idempotent through field clocks
            // and permanent viewing IDs, so they cannot resurrect an older bookmark or watch date.
            if (State.MobileReceipts.Count > 8192)
                foreach (var old in State.MobileReceipts.Take(State.MobileReceipts.Count - 8192).ToList())
                    State.MobileReceipts.Remove(old);
            if (acknowledged.Count > 0)
                Save();
        }
        if (watched)
            WatchCompleted?.Invoke();
        return acknowledged;
    }
}
