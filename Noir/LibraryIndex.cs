namespace Noir;

// Group → catalog entries, so per-title lookups don't scan the whole catalog. Rebuilt when the
// catalog changes size or a writer calls CatalogChanged; every lookup re-checks its own entries, so
// a missed invalidation costs a rebuild rather than a wrong answer. Callers hold gate.
public sealed partial class LibraryStore
{
    Dictionary<string, List<(string Id, CatalogEntry Entry)>>? groupIndex;
    Dictionary<string, CatalogEntry>? indexedCatalog;
    int indexedCount;

    void CatalogChanged()
    {
        groupIndex = null;
        matchIndex = null;
    }

    // S6: Remember changed or added one entry. Keep the group index instead of rebuilding it for the
    // next lookup (a rebuild walks all 40,000 entries); an entry left in its old group's list is caught
    // by EntriesFor's own check. Any other change in the catalog's size drops it as before.
    void Remembered(string id, CatalogEntry entry, int countBefore)
    {
        if (groupIndex != null && ReferenceEquals(indexedCatalog, State.Catalog) && indexedCount == countBefore)
        {
            var key = entry.Item.GroupKey;
            if (!groupIndex.TryGetValue(key, out var list))
                groupIndex[key] = list = [];
            var at = list.FindIndex(p => p.Id == id);
            if (at >= 0)
                list[at] = (id, entry);
            else
                list.Add((id, entry));
            indexedCount = State.Catalog.Count;
        }
        else
            groupIndex = null;
        if (matchIndex != null && matchIndex.Valid(State.Catalog, countBefore))
            matchIndex.Add(entry, State.Catalog.Count);
        else
            matchIndex = null;
    }

    // An entry's keys changed in place (not through Remember).
    void Reindexed(CatalogEntry entry)
    {
        if (matchIndex != null && matchIndex.Valid(State.Catalog, State.Catalog.Count))
            matchIndex.Add(entry, State.Catalog.Count);
        else
            matchIndex = null;
    }

    // S6: imports and backup merges call Remember/AddDiscovered for thousands of titles, and each call
    // scanned the whole catalog (by path, fingerprint, title and show). Inside a MatchScope those
    // lookups use this index instead. It holds every entry under its current keys (built from all of
    // them, kept in sync by Remember and AddDiscovered, dropped by CatalogChanged and whenever the
    // catalog's size changes behind it) but may keep stale keys, so every candidate is re-checked with
    // the original test, and whenever more than one candidate passes, the original scan in catalog
    // order decides. Outside a scope the original scans run unchanged.
    int matchDepth;
    MatchIndex? matchIndex;

    public IDisposable MatchScope()
    {
        lock (gate)
            matchDepth++;
        return new MatchScopeEnd(this);
    }

    sealed class MatchScopeEnd(LibraryStore store) : IDisposable
    {
        bool done;

        public void Dispose()
        {
            if (done)
                return;
            done = true;
            lock (store.gate)
                if (--store.matchDepth == 0)
                    store.matchIndex = null;
        }
    }

    // Checks only: run the original catalog scans, to compare both ways on the same input.
    internal static bool MatchIndexOff = false; // test seam (Noir.Checks): compare against the old scans

    MatchIndex? ActiveMatchIndex()
    {
        if (matchDepth == 0 || MatchIndexOff)
            return null;
        if (matchIndex == null || !matchIndex.Valid(State.Catalog, State.Catalog.Count))
            matchIndex = new MatchIndex(State.Catalog);
        return matchIndex;
    }

    // The entry the catalog scan (FirstOrDefault in catalog order) would find, given complete candidates.
    CatalogEntry? Unique(HashSet<CatalogEntry>? candidates, Func<CatalogEntry, bool> test)
    {
        CatalogEntry? found = null;
        foreach (var e in candidates ?? [])
        {
            if (!test(e))
                continue;
            if (found != null || !InCatalog(e))
                return State.Catalog.Values.FirstOrDefault(test);
            found = e;
        }
        return found;
    }

    // Every entry that passes the test (order is not kept: callers only count or de-duplicate them).
    List<CatalogEntry> AllPassing(HashSet<CatalogEntry>? candidates, Func<CatalogEntry, bool> test)
    {
        var found = new List<CatalogEntry>();
        foreach (var e in candidates ?? [])
            if (test(e))
            {
                if (!InCatalog(e))
                    return State.Catalog.Values.Where(test).ToList();
                found.Add(e);
            }
        return found;
    }

    bool InCatalog(CatalogEntry e) => State.Catalog.TryGetValue(e.Item.Id, out var now) && ReferenceEquals(now, e);

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> matchTitles = new();

    // The normalized title key, cached: imports and merges compare the same titles many times.
    static string MatchTitle(string title)
    {
        if (matchTitles.TryGetValue(title, out var key))
            return key;
        key = System.Text.RegularExpressions.Regex.Replace(title.ToUpperInvariant(), @"[^\p{L}\p{N}]", "");
        if (matchTitles.Count > 200_000)
            matchTitles.Clear();
        matchTitles[title] = key;
        return key;
    }

    static string ShowKey(string title) => MatchTitle(CleanShowTitle(title));

    sealed class MatchIndex
    {
        public readonly Dictionary<string, HashSet<CatalogEntry>> Path = new(StringComparer.OrdinalIgnoreCase),
            Fingerprint = new(),
            Title = new(),
            Show = new(),
            Imdb = new();
        readonly Dictionary<string, string> shows = new();
        readonly Dictionary<string, CatalogEntry> catalog;
        int count;

        public MatchIndex(Dictionary<string, CatalogEntry> catalog)
        {
            this.catalog = catalog;
            foreach (var entry in catalog.Values)
                Add(entry, catalog.Count);
        }

        public bool Valid(Dictionary<string, CatalogEntry> now, int countNow) =>
            ReferenceEquals(catalog, now) && count == countNow;

        static void Put(Dictionary<string, HashSet<CatalogEntry>> map, string key, CatalogEntry entry)
        {
            if (!map.TryGetValue(key, out var set))
                map[key] = set = [];
            set.Add(entry);
        }

        public string ShowKeyOf(string title)
        {
            if (!shows.TryGetValue(title, out var key))
                shows[title] = key = ShowKey(title);
            return key;
        }

        // Files the entry under its current keys; old keys stay until the next rebuild (re-checked on use).
        public void Add(CatalogEntry entry, int countNow)
        {
            count = countNow;
            foreach (var path in entry.Paths)
                Put(Path, path, entry);
            if (entry.Fingerprint.Length > 0)
                Put(Fingerprint, entry.Fingerprint, entry);
            Put(Title, MatchTitle(entry.Item.Title), entry);
            if (!entry.Item.IsMovie)
                Put(Show, ShowKeyOf(entry.Item.Title), entry);
            if (entry.Item.ImdbId.Length > 0)
                Put(Imdb, entry.Item.ImdbId, entry);
        }
    }

    void IndexRebuilt()
    {
        groupIndex = new();
        indexedCatalog = State.Catalog;
        indexedCount = State.Catalog.Count;
        foreach (var (id, entry) in State.Catalog)
        {
            var key = entry.Item.GroupKey;
            if (!groupIndex.TryGetValue(key, out var list))
                groupIndex[key] = list = [];
            list.Add((id, entry));
        }
    }

    List<CatalogEntry> EntriesFor(string groupKey)
    {
        if (
            groupIndex == null
            || !ReferenceEquals(indexedCatalog, State.Catalog)
            || indexedCount != State.Catalog.Count
        )
            IndexRebuilt();
        if (!groupIndex!.TryGetValue(groupKey, out var list))
            return [];
        foreach (var (id, entry) in list)
            if (
                entry.Item.GroupKey != groupKey
                || !State.Catalog.TryGetValue(id, out var now)
                || !ReferenceEquals(now, entry)
            )
            {
                IndexRebuilt();
                list = groupIndex.GetValueOrDefault(groupKey) ?? [];
                break;
            }
        return list.Select(p => p.Entry).ToList();
    }
}

// Copy mutable state without JSON work under the library lock. Strings and value types are immutable here.
internal static class LibrarySnapshot
{
    public static T Copy<T>(T value) => Copier<T>.Clone(value);

    static Dictionary<K, V> Map<K, V>(Dictionary<K, V> source)
        where K : notnull => source.ToDictionary(p => p.Key, p => Copy(p.Value), source.Comparer);

    static List<T> List<T>(List<T> source) => source.Select(Copy).ToList();

    static HashSet<T> Set<T>(HashSet<T> source) => new(source.Select(Copy), source.Comparer);

    static class Copier<T>
    {
        public static readonly Func<T, T> Clone = Build();

        static Func<T, T> Build()
        {
            var type = typeof(T);
            if (type.IsValueType || type == typeof(string))
                return value => value;
            if (type.IsGenericType)
            {
                var generic = type.GetGenericTypeDefinition();
                var name =
                    generic == typeof(Dictionary<,>) ? nameof(Map)
                    : generic == typeof(List<>) ? nameof(List)
                    : generic == typeof(HashSet<>) ? nameof(Set)
                    : null;
                if (name != null)
                {
                    var copy =
                        (Func<T, T>)
                            typeof(LibrarySnapshot)
                                .GetMethod(
                                    name,
                                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
                                )!
                                .MakeGenericMethod(type.GetGenericArguments())
                                .CreateDelegate(typeof(Func<T, T>));
                    return value => value == null ? value : copy(value);
                }
            }
            var source = System.Linq.Expressions.Expression.Parameter(type, "source");
            var target = System.Linq.Expressions.Expression.Variable(type, "target");
            var body = new List<System.Linq.Expressions.Expression>
            {
                System.Linq.Expressions.Expression.Assign(
                    target,
                    System.Linq.Expressions.Expression.Convert(
                        System.Linq.Expressions.Expression.Call(
                            source,
                            typeof(object).GetMethod(
                                "MemberwiseClone",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                            )!
                        ),
                        type
                    )
                ),
            };
            foreach (
                var field in type.GetFields(
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                )
            )
                if (!field.FieldType.IsValueType && field.FieldType != typeof(string))
                    body.Add(
                        System.Linq.Expressions.Expression.Assign(
                            System.Linq.Expressions.Expression.Field(target, field),
                            System.Linq.Expressions.Expression.Call(
                                typeof(LibrarySnapshot).GetMethod(nameof(Copy))!.MakeGenericMethod(field.FieldType),
                                System.Linq.Expressions.Expression.Field(source, field)
                            )
                        )
                    );
            body.Add(target);
            var clone = System
                .Linq.Expressions.Expression.Lambda<Func<T, T>>(
                    System.Linq.Expressions.Expression.Block([target], body),
                    source
                )
                .Compile();
            return value => value == null ? value : clone(value);
        }
    }
}
