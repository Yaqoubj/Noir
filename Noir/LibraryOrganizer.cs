using System.IO;
using System.Text.Json;

namespace Noir;

public sealed class OrganizationMove
{
    public string ItemId { get; set; } = "";
    public string Root { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public bool Video { get; set; }
    public bool Done { get; set; }
    public string Fingerprint { get; set; } = "";

    /// <summary>U91: a path as shown in lists, relative to the library folder it is in (the path itself when outside it).</summary>
    public static string Relative(string root, string path)
    {
        if (root.Length == 0 || path.Length == 0)
            return path;
        var folder = root.TrimEnd('\\', '/');
        return
            path.Length > folder.Length + 1
            && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
            && (path[folder.Length] == '\\' || path[folder.Length] == '/')
            ? path[(folder.Length + 1)..]
            : path;
    }

    // O1: the show or movie this move belongs to, so the preview can tick it as a whole.
    public string Group { get; set; } = "";
    public string GroupName { get; set; } = "";
}

public sealed class OrganizationGroup
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Movie { get; set; }
    public int Videos { get; set; }
    public List<OrganizationMove> Moves { get; set; } = [];
}

public sealed class OrganizationPlan
{
    public string Id { get; set; } = "";
    public List<OrganizationMove> Moves { get; set; } = [];
    public List<string> Skipped { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Set for runs started by "Organize new files automatically".
    public bool Auto { get; set; }

    public List<OrganizationGroup> Groups() =>
        Moves
            .GroupBy(m => m.Group.Length > 0 ? m.Group : m.ItemId)
            .Select(g => new OrganizationGroup
            {
                Key = g.Key,
                Name = g.Select(m => m.GroupName).FirstOrDefault(n => n.Length > 0) ?? Path.GetFileName(g.First().From),
                Movie = g.Key.StartsWith("movie:", StringComparison.Ordinal),
                Videos = g.Count(m => m.Video),
                Moves = g.ToList(),
            })
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    // The same plan without the groups the user unticked; those stay "Left as they are".
    public OrganizationPlan Without(ISet<string> unticked) =>
        new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            Auto = Auto,
            Skipped = Skipped,
            Moves = Moves.Where(m => !unticked.Contains(m.Group.Length > 0 ? m.Group : m.ItemId)).ToList(),
        };
}

// O1: every organizing run is kept, newest last. Undo takes back the last one that still has moved files.
public sealed class OrganizationJournal
{
    public List<OrganizationPlan> Runs { get; set; } = [];
}

public sealed partial class LibraryStore
{
    public string OrganizerJournal => Path.Combine(DataDirectory, "organization.json");

    const int MaxOrganizationRuns = 30;
    readonly object organizeGate = new();
    static readonly JsonSerializerOptions JournalOptions = new() { PropertyNameCaseInsensitive = true };

    static bool InsideRoot(string path, string root) =>
        Path.GetFullPath(path)
            .StartsWith(
                Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            );

    static bool SafePath(string path, string root)
    {
        if (!InsideRoot(path, root))
            return false;
        for (
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            folder != null && InsideRoot(folder, root);
            folder = Path.GetDirectoryName(folder)
        )
            if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                return false;
        return !Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) == 0;
    }

    string OText(string en, string ar) => State.Language == "ar" ? ar : en;

    // naming: the two switches. include: only some items (new files). busy: files being downloaded or played;
    // when given, a file another program holds open is skipped too instead of stopping the run later.
    public OrganizationPlan PlanOrganization(
        NamingOptions? naming = null,
        Func<VideoItem, bool>? include = null,
        Func<string, bool>? busy = null
    )
    {
        naming ??= new();
        List<VideoItem> candidates;
        List<LibraryRoot> roots;
        // Copy what planning needs and let go of the lock: the disk work below can take a while on a big library.
        lock (gate)
        {
            roots = State.Roots.Select(r => new LibraryRoot { Path = r.Path, Movies = r.Movies }).ToList();
            candidates = State
                .Catalog.Values.Where(e => e.Item.Available)
                .Select(e => e.Item)
                .Select(i => new VideoItem
                {
                    Id = i.Id,
                    Path = i.Path,
                    Title = i.Title,
                    Year = i.Year,
                    Season = i.Season,
                    Episode = i.Episode,
                    IsMovie = i.IsMovie,
                    SourceUrl = i.SourceUrl,
                    EpisodeTitle = i.EpisodeTitle,
                    CollectionKey = i.CollectionKey,
                })
                .ToList();
        }
        var plan = new OrganizationPlan();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in candidates.Where(i => i.Path.Length > 0 && (include == null || include(i))))
        {
            try
            {
                if (!File.Exists(item.Path))
                    continue;
                var root = roots
                    .Where(r => r.Movies == item.IsMovie && InsideRoot(item.Path, r.Path))
                    .OrderByDescending(r => r.Path.Length)
                    .FirstOrDefault();
                if (root == null || !SafePath(item.Path, root.Path))
                {
                    plan.Skipped.Add(item.Label + OText(" · outside a supported folder", " · خارج مجلد مدعوم"));
                    continue;
                }
                // Online confirmation prevents guessing a destructive canonical name.
                if (item.SourceUrl.Length == 0 || (!item.IsMovie && item.Episode <= 0))
                {
                    plan.Skipped.Add(
                        item.Label
                            + OText(
                                " · refresh information to confirm this title first",
                                " · حدّث المعلومات لتأكيد هذا العنوان أولًا"
                            )
                    );
                    continue;
                }
                if (busy != null)
                {
                    if (busy(item.Path))
                    {
                        plan.Skipped.Add(
                            item.Label + OText(" · playing or downloading now", " · قيد التشغيل أو التنزيل الآن")
                        );
                        continue;
                    }
                    try
                    {
                        using var probe = new FileStream(
                            item.Path,
                            FileMode.Open,
                            FileAccess.ReadWrite,
                            FileShare.None
                        );
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        plan.Skipped.Add(item.Label + OText(" · in use by another program", " · مستخدم من برنامج آخر"));
                        continue;
                    }
                }
                var (folder, stem) = DownloadNaming.Target(root.Path, item, naming);
                var target = Path.Combine(folder, stem + Path.GetExtension(item.Path).ToLowerInvariant());
                if (
                    string.Equals(
                        Path.GetFullPath(item.Path),
                        Path.GetFullPath(target),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    continue;
                var group = item.IsMovie ? "movie:" + item.Id : item.GroupKey;
                var groupName = item.Title + (item.Year > 0 ? $" ({item.Year})" : "");
                var bundle = new List<OrganizationMove>
                {
                    new()
                    {
                        ItemId = item.Id,
                        Root = root.Path,
                        From = item.Path,
                        To = target,
                        Video = true,
                        Group = group,
                        GroupName = groupName,
                    },
                };
                var oldStem = Path.GetFileNameWithoutExtension(item.Path);
                var directory = Path.GetDirectoryName(item.Path)!;
                if (!folders.TryGetValue(directory, out var siblings))
                    folders[directory] = siblings = Directory.EnumerateFiles(directory).ToArray();
                foreach (var sidecar in siblings)
                {
                    var ext = Path.GetExtension(sidecar).ToLowerInvariant();
                    if (ext is not (".srt" or ".ass" or ".ssa" or ".vtt" or ".sub" or ".idx" or ".nfo"))
                        continue;
                    var name = Path.GetFileNameWithoutExtension(sidecar);
                    if (
                        !name.Equals(oldStem, StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith(oldStem + ".", StringComparison.OrdinalIgnoreCase)
                    )
                        continue;
                    bundle.Add(
                        new()
                        {
                            ItemId = item.Id,
                            Root = root.Path,
                            From = sidecar,
                            To = Path.Combine(folder, stem + name[oldStem.Length..] + ext),
                            Group = group,
                            GroupName = groupName,
                        }
                    );
                }
                if (
                    bundle.Any(m =>
                        !SafePath(m.To, root.Path)
                        || File.Exists(m.To)
                        || Directory.Exists(m.To)
                        || destinations.Contains(m.To)
                    )
                )
                {
                    plan.Skipped.Add(
                        item.Label + OText(" · destination already exists or is unsafe", " · الوجهة موجودة أو غير آمنة")
                    );
                    continue;
                }
                foreach (var m in bundle)
                {
                    m.Fingerprint = Fingerprint(m.From);
                    destinations.Add(m.To);
                    plan.Moves.Add(m);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                plan.Skipped.Add(item.Label + " · " + e.Message);
            }
        }
        return plan;
    }

    OrganizationJournal LoadJournal()
    {
        var journal = new OrganizationJournal();
        if (!File.Exists(OrganizerJournal))
            return journal;
        var text = File.ReadAllText(OrganizerJournal);
        using (var document = JsonDocument.Parse(text))
        {
            // Before O1 the file held one plan; it loads as the only run.
            if (
                document.RootElement.ValueKind == JsonValueKind.Object
                && document
                    .RootElement.EnumerateObject()
                    .Any(p => p.Name.Equals("Runs", StringComparison.OrdinalIgnoreCase))
            )
                journal = JsonSerializer.Deserialize<OrganizationJournal>(text, JournalOptions) ?? new();
            else if (JsonSerializer.Deserialize<OrganizationPlan>(text, JournalOptions) is { } single)
                journal.Runs.Add(single);
        }
        for (var n = 0; n < journal.Runs.Count; n++)
            if (journal.Runs[n].Id.Length == 0)
                journal.Runs[n].Id = "run-" + n;
        return journal;
    }

    void WriteJournal(OrganizationJournal journal)
    {
        var temp = OrganizerJournal + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, journal, JournalOptions);
            stream.Flush(true);
        }
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, OrganizerJournal, true);
                break;
            }
            catch (Exception e) when (attempt < 5 && (e is IOException or UnauthorizedAccessException))
            {
                Thread.Sleep(40 * (attempt + 1));
            }
        }
    }

    static OrganizationPlan Stored(OrganizationPlan plan) =>
        new()
        {
            Id = plan.Id,
            Moves = plan.Moves,
            CreatedAt = plan.CreatedAt,
            Auto = plan.Auto,
        };

    void Relocate(string id, string from, string to)
    {
        if (!State.Catalog.TryGetValue(id, out var entry))
            return;
        entry.Item.Path = to;
        entry.Item.Available = true;
        if (!entry.Paths.Contains(to, StringComparer.OrdinalIgnoreCase))
            entry.Paths.Add(to);
        CatalogChanged();
        if (entry.Progress != null)
            State.Progress[to] = entry.Progress;
        if (State.Intros.TryGetValue(from, out var intro))
            State.Intros[to] = intro;
        if (State.Outros.TryGetValue(from, out var outro))
            State.Outros[to] = outro;
    }

    // Runs one plan (or takes one back). The journal lists every run, is written before the first move and then
    // every few files, and the library is saved once at the end, not once per file.
    public int Organize(OrganizationPlan plan, bool undo = false)
    {
        var moved = new List<(string From, string To)>();
        try
        {
            lock (organizeGate)
            {
                var journal = LoadJournal();
                if (plan.Id.Length == 0)
                    plan.Id = Guid.NewGuid().ToString("N");
                if (undo)
                {
                    var at = journal.Runs.FindIndex(r => r.Id == plan.Id);
                    if (at < 0)
                        return 0;
                    journal.Runs[at] = Stored(plan);
                }
                else
                {
                    journal.Runs.RemoveAll(r => r.Id != plan.Id && r.Moves.All(m => !m.Done));
                    journal.Runs.RemoveAll(r => r.Id == plan.Id);
                    journal.Runs.Add(Stored(plan));
                    if (journal.Runs.Count > MaxOrganizationRuns)
                    {
                        var old = new OrganizationJournal
                        {
                            Runs = journal.Runs.Take(journal.Runs.Count - MaxOrganizationRuns).ToList(),
                        };
                        journal.Runs.RemoveRange(0, old.Runs.Count);
                        File.WriteAllText(
                            Path.Combine(
                                DataDirectory,
                                "organization-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json"
                            ),
                            JsonSerializer.Serialize(old, JournalOptions)
                        );
                    }
                }
                var count = 0;
                var sinceWrite = 0;
                var finished = false;
                try
                {
                    WriteJournal(journal);
                    foreach (var move in (undo ? plan.Moves.AsEnumerable().Reverse() : plan.Moves).ToList())
                    {
                        if (move.Done != undo)
                            continue;
                        var from = undo ? move.To : move.From;
                        var to = undo ? move.From : move.To;
                        if (
                            !SafePath(from, move.Root)
                            || !SafePath(to, move.Root)
                            || !File.Exists(from)
                            || File.Exists(to)
                            || Directory.Exists(to)
                            || move.Fingerprint.Length == 0
                            || Fingerprint(from) != move.Fingerprint
                        )
                            throw new IOException("File changed or destination exists: " + from);
                        // Refuse active downloads/playback; rename never overwrites another file.
                        using (var probe = new FileStream(from, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        { }
                        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                        File.Move(from, to);
                        move.Done = !undo;
                        if (move.Video)
                        {
                            lock (gate)
                                Relocate(move.ItemId, from, to);
                            moved.Add((from, to));
                            count++;
                        }
                        if (++sinceWrite >= 25)
                        {
                            WriteJournal(journal);
                            sinceWrite = 0;
                        }
                    }
                    finished = true;
                    return count;
                }
                finally
                {
                    try
                    {
                        if (undo && finished)
                            journal.Runs.RemoveAll(r => r.Id == plan.Id && r.Moves.All(m => !m.Done));
                        WriteJournal(journal);
                    }
                    finally
                    {
                        if (moved.Count > 0)
                            lock (gate)
                                Save();
                    }
                }
            }
        }
        finally
        {
            if (moved.Count > 0)
                DownloadFilesMoved?.Invoke(moved);
        }
    }

    // The newest run that still has moved files (what Undo takes back), else the newest run.
    public OrganizationPlan? LastOrganization()
    {
        lock (organizeGate)
        {
            var runs = LoadJournal().Runs;
            return runs.LastOrDefault(r => r.Moves.Any(m => m.Done)) ?? runs.LastOrDefault();
        }
    }

    public int OrganizationRuns()
    {
        lock (organizeGate)
            return LoadJournal().Runs.Count(r => r.Moves.Any(m => m.Done));
    }

    // O3: organizes the confirmed files among these new paths. Anything playing, downloading or held open by
    // another program is left for next time.
    public int OrganizeNewFiles(ISet<string> newPaths, NamingOptions naming, Func<string, bool>? busy = null)
    {
        var plan = PlanOrganization(naming, i => newPaths.Contains(i.Path), busy ?? (_ => false));
        if (!plan.Moves.Any(m => m.Video))
            return 0;
        plan.Auto = true;
        return Organize(plan);
    }

    void RecoverOrganization()
    {
        try
        {
            lock (organizeGate)
            {
                var journal = LoadJournal();
                // Only the newest run can have been interrupted.
                var plan = journal.Runs.LastOrDefault();
                if (plan == null)
                    return;
                var changed = false;
                foreach (var move in plan.Moves)
                {
                    if (
                        !SafePath(move.From, move.Root)
                        || !SafePath(move.To, move.Root)
                        || move.Fingerprint.Length == 0
                    )
                        continue;
                    var fromExists = File.Exists(move.From);
                    var toExists = File.Exists(move.To);
                    // Already consistent with the journal (or ambiguous): nothing to repair.
                    if (fromExists == toExists || (move.Done && toExists) || (!move.Done && fromExists))
                        continue;
                    if (!fromExists && !move.Done && Fingerprint(move.To) == move.Fingerprint)
                    {
                        move.Done = true;
                        if (move.Video)
                            Relocate(move.ItemId, move.From, move.To);
                        changed = true;
                    }
                    else if (!toExists && move.Done && Fingerprint(move.From) == move.Fingerprint)
                    {
                        move.Done = false;
                        if (move.Video)
                            Relocate(move.ItemId, move.To, move.From);
                        changed = true;
                    }
                }
                if (changed)
                {
                    WriteJournal(journal);
                    Save();
                }
            }
        }
        catch (Exception e)
        {
            Log(e);
            LoadWarning = "An interrupted organization needs attention. Its original paths are saved.";
        }
    }

    public void ArchiveOrganization()
    {
        lock (organizeGate)
            if (File.Exists(OrganizerJournal))
                File.Move(
                    OrganizerJournal,
                    Path.Combine(
                        DataDirectory,
                        "organization-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json"
                    )
                );
    }

    public event Action<IReadOnlyList<(string From, string To)>>? DownloadFilesMoved;
}
