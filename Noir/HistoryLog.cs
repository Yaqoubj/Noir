namespace Noir;

// One thing watched outside Noir: logged by hand (TV Time style) or imported from another service.
// Source/LoggedAt override the import's label when a Noir export is read back losslessly.
public sealed record LogEntry(
    VideoItem Item,
    DateTimeOffset At,
    bool DateUnknown = false,
    int Rating = 0,
    bool Approximate = false,
    string Source = "",
    DateTimeOffset? LoggedAt = null
);

public sealed record HistorySource(string Source, int Viewings, int Titles);

public sealed record ViewingSnapshot(string ItemId, List<ViewingRecord> Viewings, Bookmark? Progress);

public sealed partial class LibraryStore
{
    public event Action? HistoryChanged;
    int batchDepth;
    bool batchDirty;

    // Large imports touch hundreds of titles; writing library.json once at the end keeps them fast.
    public IDisposable Batch()
    {
        lock (gate)
            batchDepth++;
        return new SaveBatch(this);
    }

    sealed class SaveBatch(LibraryStore store) : IDisposable
    {
        bool done;

        public void Dispose()
        {
            if (done)
                return;
            done = true;
            bool save;
            lock (store.gate)
            {
                save = --store.batchDepth == 0 && store.batchDirty;
                if (save)
                    store.batchDirty = false;
            }
            if (save)
                store.Save();
        }
    }

    // Adds diary entries without needing a file. Re-logging the same item on the same day (or an
    // undated item that already has history) adds nothing, so importing a file twice is harmless.
    // Imports pass queueSync=false: those viewings already live on the service they came from, and
    // exact=true so two viewings of the same title hours apart in one export both survive.
    public int LogWatched(
        IEnumerable<LogEntry> entries,
        string source,
        bool queueSync = true,
        bool exact = false,
        string batch = ""
    )
    {
        int added = 0;
        lock (gate)
        {
            var record = batch.Length > 0 ? State.ImportBatches.FirstOrDefault(b => b.Id == batch) : null;
            foreach (var given in entries)
            {
                var e = given;
                if (e.Item.Id.Length == 0 && e.Item.Title.Length == 0)
                    continue;
                var entry = Remember(e.Item);
                SeedViewing(entry);
                var item = entry.Item;
                if (!e.DateUnknown)
                    e = e with { At = NotBeforeAiring(item, e.At) };
                var label = e.Source.Length > 0 ? e.Source : source;
                if (!State.Viewings.TryGetValue(item.Id, out var list))
                    State.Viewings[item.Id] = list = [];
                if (e.Rating is > 0 and <= 5 && !State.PersonalReviews.ContainsKey(item.Id))
                {
                    State.PersonalReviews[item.Id] = new() { Rating = e.Rating };
                    record?.RatedItemIds.Add(item.Id);
                }
                bool Same(ViewingRecord v) =>
                    !v.DateUnknown
                    && Math.Abs((v.WatchedAt - e.At).TotalMinutes) < (exact && v.Source == label ? 1 : 720);
                if (e.DateUnknown ? list.Count > 0 : list.Any(Same))
                    continue;
                // A dated record replaces a guess logged earlier for the same item.
                if (!e.DateUnknown)
                {
                    if (record != null)
                        record.Displaced.AddRange(
                            list.Where(v => v.DateUnknown)
                                .Select(v => new DisplacedViewing { ItemId = item.Id, Viewing = Copy(v) })
                        );
                    list.RemoveAll(v => v.DateUnknown);
                }
                var id = Guid.NewGuid().ToString("N");
                list.Add(
                    new()
                    {
                        Id = id,
                        WatchedAt = e.At,
                        Source = label,
                        DateUnknown = e.DateUnknown,
                        Approximate = e.Approximate && !e.DateUnknown,
                        LoggedAt = e.LoggedAt ?? DateTimeOffset.UtcNow,
                        Batch = batch,
                    }
                );
                added++;
                var activity = e.DateUnknown ? DateTimeOffset.UtcNow : e.At;
                // An unfinished rewatch that is newer than this log keeps its resume point.
                if (entry.Progress is not { Completed: false, Position: > 0 } p || p.UpdatedAt < activity)
                {
                    var duration = entry.Progress?.Duration > 0 ? entry.Progress.Duration : item.Duration;
                    entry.Progress = new()
                    {
                        Position = duration,
                        Duration = duration,
                        Completed = true,
                        UpdatedAt = activity,
                        ViewingId = id,
                    };
                    if (item.Path.Length > 0)
                        State.Progress[item.Path] = entry.Progress;
                }
                // An undated viewing has no real date to send; Trakt would file it as watched today.
                if (queueSync && !e.DateUnknown)
                    CommunityQueue.Enqueue(State, item, e.At);
            }
            if (record != null)
                record.Added += added;
            if (added > 0 || record != null)
                Save();
        }
        if (added > 0)
        {
            if (queueSync)
                WatchCompleted?.Invoke();
            HistoryChanged?.Invoke();
        }
        return added;
    }

    // Opens an undoable import; pass its id to LogWatched.
    public string BeginImport(string service)
    {
        lock (gate)
        {
            var id = Guid.NewGuid().ToString("N");
            State.ImportBatches.Add(
                new()
                {
                    Id = id,
                    Service = service,
                    At = DateTimeOffset.UtcNow,
                }
            );
            if (State.ImportBatches.Count > 30)
                State.ImportBatches.RemoveRange(0, State.ImportBatches.Count - 30);
            return id;
        }
    }

    // Drops an import that added nothing, so the recent list only shows real imports.
    public void EndImport(string id)
    {
        lock (gate)
        {
            if (State.ImportBatches.RemoveAll(b => b.Id == id && b.Added == 0 && b.RatedItemIds.Count == 0) > 0)
                Save();
        }
    }

    public List<HistorySource> Sources()
    {
        lock (gate)
            return State
                .Viewings.SelectMany(p => p.Value.Select(v => (Item: p.Key, v.Source)))
                .GroupBy(x => x.Source)
                .Select(g => new HistorySource(g.Key, g.Count(), g.Select(x => x.Item).Distinct().Count()))
                .OrderByDescending(s => s.Viewings)
                .ToList();
    }

    // Removes viewings matching a rule; items left without any viewing go back to unwatched.
    // Returns an undo snapshot. Removals reach the phone with the next sync, which mirrors the PC.
    List<ViewingSnapshot> RemoveViewings(Func<string, ViewingRecord, bool> remove)
    {
        var undo = new List<ViewingSnapshot>();
        lock (gate)
        {
            foreach (var (itemId, list) in State.Viewings.ToList())
            {
                if (!list.Any(v => remove(itemId, v)))
                    continue;
                var entry = State.Catalog.GetValueOrDefault(itemId);
                undo.Add(new(itemId, list.Select(Copy).ToList(), entry?.Progress));
                list.RemoveAll(v => remove(itemId, v));
                if (list.Count > 0)
                    continue;
                State.Viewings.Remove(itemId);
                if (entry is { Progress: { Completed: true } })
                {
                    entry.Progress = new()
                    {
                        Position = 0,
                        Duration = entry.Progress.Duration,
                        Completed = false,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        ViewingId = Guid.NewGuid().ToString("N"),
                    };
                    if (entry.Item.Path.Length > 0)
                        State.Progress[entry.Item.Path] = entry.Progress;
                }
                CommunityQueue.CancelPending(State, itemId);
            }
            if (undo.Count > 0)
                Save();
        }
        if (undo.Count > 0)
            HistoryChanged?.Invoke();
        return undo;
    }

    static ViewingRecord Copy(ViewingRecord v) =>
        new()
        {
            Id = v.Id,
            WatchedAt = v.WatchedAt,
            Source = v.Source,
            DateUnknown = v.DateUnknown,
            Approximate = v.Approximate,
            LoggedAt = v.LoggedAt,
            Batch = v.Batch,
        };

    public List<ViewingSnapshot> RemoveSource(string source) => RemoveViewings((_, v) => v.Source == source);

    public int UndoImport(string id)
    {
        ImportBatch? batch;
        lock (gate)
            batch = State.ImportBatches.FirstOrDefault(b => b.Id == id);
        if (batch == null)
            return 0;
        var undo = RemoveViewings((_, v) => v.Batch == id);
        lock (gate)
        {
            // An imported rating on a review that has a note keeps the note.
            foreach (var item in batch.RatedItemIds)
                if (State.PersonalReviews.GetValueOrDefault(item) is { } review)
                {
                    if (review.Note.Length > 0)
                        review.Rating = 0;
                    else
                        State.PersonalReviews.Remove(item);
                }
            foreach (var d in batch.Displaced)
            {
                if (!State.Catalog.TryGetValue(d.ItemId, out var entry))
                    continue;
                if (!State.Viewings.TryGetValue(d.ItemId, out var list))
                    State.Viewings[d.ItemId] = list = [];
                if (list.Any(v => v.Id == d.Viewing.Id))
                    continue;
                list.Add(d.Viewing);
                if (entry.Progress is not { Completed: true })
                {
                    var duration = entry.Progress?.Duration > 0 ? entry.Progress.Duration : entry.Item.Duration;
                    entry.Progress = new()
                    {
                        Position = duration,
                        Duration = duration,
                        Completed = true,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        ViewingId = d.Viewing.Id,
                    };
                    if (entry.Item.Path.Length > 0)
                        State.Progress[entry.Item.Path] = entry.Progress;
                }
            }
            State.ImportBatches.Remove(batch);
            Save();
        }
        HistoryChanged?.Invoke();
        return undo.Sum(s => s.Viewings.Count(v => v.Batch == id));
    }

    // Un-checking in the tracker forgets the item's viewings. The snapshot lets the UI undo it.
    public List<ViewingSnapshot> Unwatch(IEnumerable<VideoItem> items)
    {
        var undo = new List<ViewingSnapshot>();
        lock (gate)
        {
            foreach (var item in items)
            {
                if (!State.Catalog.TryGetValue(item.Id, out var entry))
                    continue;
                var viewings = State.Viewings.GetValueOrDefault(item.Id) ?? [];
                if (viewings.Count == 0 && entry.Progress?.Completed != true)
                    continue;
                undo.Add(new(item.Id, viewings.ToList(), entry.Progress));
                State.Viewings.Remove(item.Id);
                ForgetOnNuvio(entry.Item);
                entry.Progress =
                    entry.Progress == null
                        ? null
                        : new()
                        {
                            Position = 0,
                            Duration = entry.Progress.Duration,
                            Completed = false,
                            UpdatedAt = DateTimeOffset.UtcNow,
                            ViewingId = Guid.NewGuid().ToString("N"),
                        };
                if (item.Path.Length > 0)
                {
                    if (entry.Progress == null)
                        State.Progress.Remove(item.Path);
                    else
                        State.Progress[item.Path] = entry.Progress;
                }
                CommunityQueue.CancelPending(State, item.Id);
            }
            // An empty list would let SeedViewing recreate an "Earlier record" from the old bookmark.
            if (undo.Count > 0)
                Save();
        }
        if (undo.Count > 0)
            HistoryChanged?.Invoke();
        return undo;
    }

    // Taken before a change so the UI can offer Undo; items not in the catalog yet snapshot as empty.
    public List<ViewingSnapshot> Snapshot(IEnumerable<VideoItem> items)
    {
        // Copies, not references: a later date edit must not change what Undo restores.
        lock (gate)
            return items
                .Select(i => new ViewingSnapshot(
                    i.Id,
                    State.Viewings.GetValueOrDefault(i.Id)?.Select(Copy).ToList() ?? [],
                    State.Catalog.GetValueOrDefault(i.Id)?.Progress
                ))
                .ToList();
    }

    public void Restore(List<ViewingSnapshot> undo)
    {
        lock (gate)
        {
            foreach (var s in undo)
            {
                if (!State.Catalog.TryGetValue(s.ItemId, out var entry))
                    continue;
                if (s.Viewings.Count > 0)
                    State.Viewings[s.ItemId] = s.Viewings;
                else
                    State.Viewings.Remove(s.ItemId);
                entry.Progress = s.Progress;
                if (entry.Item.Path.Length > 0)
                {
                    if (s.Progress != null)
                        State.Progress[entry.Item.Path] = s.Progress;
                    else
                        State.Progress.Remove(entry.Item.Path);
                }
                if (s.Viewings.Count == 0)
                    CommunityQueue.CancelPending(State, s.ItemId);
                else
                    RememberOnNuvio(entry.Item, s.Viewings);
            }
            Save();
        }
        HistoryChanged?.Invoke();
    }

    public void RemoveViewing(VideoItem item, string viewingId)
    {
        lock (gate)
        {
            if (!State.Viewings.TryGetValue(item.Id, out var list) || list.RemoveAll(v => v.Id == viewingId) == 0)
                return;
            if (list.Count == 0)
            {
                State.Viewings.Remove(item.Id);
                ForgetOnNuvio(item);
                if (State.Catalog.GetValueOrDefault(item.Id) is { Progress: { Completed: true } } entry)
                {
                    entry.Progress = new()
                    {
                        Position = 0,
                        Duration = entry.Progress.Duration,
                        Completed = false,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        ViewingId = Guid.NewGuid().ToString("N"),
                    };
                    if (item.Path.Length > 0)
                        State.Progress[item.Path] = entry.Progress;
                }
                CommunityQueue.CancelPending(State, item.Id);
            }
            Save();
        }
        HistoryChanged?.Invoke();
    }

    public void SetViewingDate(VideoItem item, string viewingId, DateTimeOffset at, bool dateUnknown)
    {
        lock (gate)
        {
            if (State.Viewings.GetValueOrDefault(item.Id)?.FirstOrDefault(v => v.Id == viewingId) is not { } v)
                return;
            v.WatchedAt = at;
            v.DateUnknown = dateUnknown;
            Save();
        }
        HistoryChanged?.Invoke();
    }

    // A deliberate rewatch: always a new viewing, even on a day that already has one.
    public int LogRewatch(LogEntry e, string source)
    {
        lock (gate)
        {
            var entry = Remember(e.Item);
            SeedViewing(entry);
            var id = Guid.NewGuid().ToString("N");
            if (!e.DateUnknown)
                e = e with { At = NotBeforeAiring(entry.Item, e.At) };
            if (!State.Viewings.TryGetValue(entry.Item.Id, out var list))
                State.Viewings[entry.Item.Id] = list = [];
            list.Add(
                new()
                {
                    Id = id,
                    WatchedAt = e.At,
                    Source = source,
                    DateUnknown = e.DateUnknown,
                    LoggedAt = DateTimeOffset.UtcNow,
                }
            );
            var duration = entry.Progress?.Duration > 0 ? entry.Progress.Duration : entry.Item.Duration;
            entry.Progress = new()
            {
                Position = duration,
                Duration = duration,
                Completed = true,
                UpdatedAt = DateTimeOffset.UtcNow,
                ViewingId = id,
            };
            if (entry.Item.Path.Length > 0)
                State.Progress[entry.Item.Path] = entry.Progress;
            if (!e.DateUnknown)
                CommunityQueue.Enqueue(State, entry.Item, e.At);
            Save();
        }
        WatchCompleted?.Invoke();
        HistoryChanged?.Invoke();
        return 1;
    }

    // The library group a Discover title already belongs to (same rule AddDiscovered merges by).
    public string? FindDiscovered(DiscoverTitle title)
    {
        lock (gate)
            return State
                .Catalog.Values.FirstOrDefault(e =>
                    e.Item.IsMovie == title.IsMovie
                    && (
                        e.Item.ImdbId == title.Id
                        || MatchTitle(e.Item.Title) == MatchTitle(title.Name)
                            && (e.Item.Year == title.Year || e.Item.Year == 0 || title.Year == 0)
                    )
                )
                ?.Item.GroupKey;
    }

    // Air date of an episode when the schedule knows it; used for "watched when it aired".
    public DateTimeOffset? AirDate(VideoItem item)
    {
        lock (gate)
        {
            if (State.EpisodeSchedules.GetValueOrDefault(item.GroupKey) is not { } schedule)
                return null;
            // Long-running shows ask this per episode; index each schedule once (rebuilt if it grows).
            if (!airIndex.TryGetValue(schedule, out var index) || index.Item1 != schedule.Count)
            {
                index = Tuple.Create(schedule.Count, new Dictionary<(int, int), DateTimeOffset?>());
                foreach (var e in schedule)
                    index.Item2.TryAdd((e.Season, e.Episode), e.AirsAt);
                airIndex.AddOrUpdate(schedule, index);
            }
            return index.Item2.GetValueOrDefault((item.Season, item.Episode));
        }
    }

    readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        List<KnownEpisode>,
        Tuple<int, Dictionary<(int, int), DateTimeOffset?>>
    > airIndex = new();

    public bool Aired(VideoItem item, DateTimeOffset now) => AirDate(item) is not { } at || at <= now;

    // Nobody watches an episode before it airs: such dates snap to the air date.
    public DateTimeOffset NotBeforeAiring(VideoItem item, DateTimeOffset at) =>
        !item.IsMovie && AirDate(item) is { } aired && at < aired && aired <= DateTimeOffset.UtcNow ? aired : at;

    // When you last actually did something with an item: a viewing recorded as it happened, or an
    // unfinished resume point. Logged history (undated, imported, or entered days later) and
    // completion bookmarks never make a title look like something you're watching now.
    public DateTimeOffset LastActivity(VideoItem item)
    {
        lock (gate)
        {
            var dated = State
                .Viewings.GetValueOrDefault(item.Id)
                ?.Where(v => !v.Backfill)
                .Select(v => v.WatchedAt)
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Max()
                ?? DateTimeOffset.MinValue;
            var progress = ProgressFor(State, item);
            return progress is { Completed: false, Position: > 0 } && progress.UpdatedAt > dated
                ? progress.UpdatedAt
                : dated;
        }
    }

    // Episodes that count toward progress: regular seasons, plus specials when the user asked for them.
    public bool Counts(VideoItem item) =>
        !item.IsMovie && item.Episode > 0 && (item.Season > 0 || State.IncludeSpecials.Contains(item.GroupKey));

    public static bool Before(VideoItem a, VideoItem b) =>
        a.Season < b.Season || a.Season == b.Season && a.Episode < b.Episode;

    // Released, unwatched episodes earlier than this one (what TV Time asks about).
    public List<VideoItem> EarlierUnwatched(VideoItem item)
    {
        if (item.IsMovie || item.Season <= 0)
            return [];
        var now = DateTimeOffset.UtcNow;
        return EpisodesFor(item.GroupKey)
            .Where(i => i.Season > 0 && Counts(i) && Before(i, item) && Aired(i, now) && !EverWatched(i))
            .OrderBy(i => i.Season)
            .ThenBy(i => i.Episode)
            .ToList();
    }

    public List<VideoItem> LaterWatched(VideoItem item)
    {
        if (item.IsMovie)
            return [];
        return EpisodesFor(item.GroupKey)
            .Where(i => Counts(i) && Before(item, i) && EverWatched(i))
            .OrderBy(i => i.Season)
            .ThenBy(i => i.Episode)
            .ToList();
    }

    // Episodes skipped before the furthest one you've watched.
    public List<VideoItem> Gaps(List<VideoItem> group)
    {
        var now = DateTimeOffset.UtcNow;
        var regular = group
            .Where(i => Counts(i) && i.Season > 0 && Aired(i, now))
            .OrderBy(i => i.Season)
            .ThenBy(i => i.Episode)
            .ToList();
        var furthest = regular.LastOrDefault(EverWatched);
        if (furthest == null)
            return [];
        return regular.TakeWhile(i => i != furthest).Where(i => !EverWatched(i)).ToList();
    }

    public int RuntimeMinutes(VideoItem item)
    {
        lock (gate)
        {
            if (item.Duration > 60)
                return (int)(item.Duration / 60);
            if (
                !item.IsMovie
                && State
                    .EpisodeSchedules.GetValueOrDefault(item.GroupKey)
                    ?.FirstOrDefault(e => e.Season == item.Season && e.Episode == item.Episode)
                    is { Runtime: > 0 } ep
            )
                return ep.Runtime;
            var r = State.ShowRuntimes.GetValueOrDefault(item.GroupKey);
            return r > 0 ? r
                : item.IsMovie ? 110
                : 43;
        }
    }

    public bool ShowEnded(string groupKey)
    {
        lock (gate)
            return State.ShowStates.GetValueOrDefault(groupKey) == "Ended";
    }

    public void RateShow(string key, int rating)
    {
        lock (gate)
        {
            if (rating <= 0)
                State.ShowRatings.Remove(key);
            else
                State.ShowRatings[key] = Math.Clamp(rating, 1, 5);
            TouchMobileField("showRating:" + key);
            Save();
        }
        HistoryChanged?.Invoke();
    }
}
