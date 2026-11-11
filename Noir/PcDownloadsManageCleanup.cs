using System.IO;
using System.Text.RegularExpressions;

namespace Noir;

// O4 (empty folders, samples, loose files) and O5 (duplicates) for "Free up space". Everything here stays inside the
// library folders, works from one walk of the folders, and only suggests: FreeSpace re-checks before it deletes.
/// <summary>U92-U95: the words of the Free up space sheet, kept apart from the view so they can be checked.</summary>
public static class FreeSpaceText
{
    /// <summary>A group's figure is what its ticked files free; empty folders have no size, so they are counted.</summary>
    public static string GroupFigure(string kind, IEnumerable<DownloadSpaceItem> ticked, bool arabic)
    {
        var list = ticked.ToList();
        if (kind == PcDownloads.KindEmptyFolders)
            return !arabic ? (list.Count == 1 ? "1 folder" : list.Count + " folders")
                : list.Count == 1 ? "مجلد واحد"
                : list.Count == 2 ? "مجلدان"
                : list.Count is >= 3 and <= 10 ? list.Count + " مجلدات"
                : list.Count + " مجلدًا";
        return list.Count == 0 ? "0 KB" : DownloadNaming.Size(list.Sum(i => i.Bytes));
    }

    /// <summary>U94: the button says what will really happen for the chosen delete option.</summary>
    public static string Button(DownloadDeleteChoice choice, long bytes, bool arabic)
    {
        var size = bytes > 0 ? DownloadNaming.Size(bytes) : "0 KB";
        return choice switch
        {
            DownloadDeleteChoice.Permanent => arabic ? $"حذف {size}" : $"Delete {size}",
            DownloadDeleteChoice.Recycle => arabic ? $"نقل {size} إلى سلة المحذوفات" : $"Move {size} to Recycle Bin",
            _ => arabic ? $"إزالة {size}…" : $"Remove {size}…",
        };
    }

    /// <summary>U95: long folder names are cut in the middle, so both the start and the end still show.</summary>
    public static string MiddleTrim(string text, int max)
    {
        if (text.Length <= max || max < 5)
            return text;
        var keep = max - 1;
        var head = (keep + 1) / 2;
        return text[..head] + "…" + text[^(keep - head)..];
    }
}

public sealed partial class PcDownloads
{
    public const string KindEmptyFolders = "Empty folders";
    public const string KindSamples = "Samples";
    public const string KindLoose = "Loose files";
    public const string KindDuplicates = "Duplicates";
    const long SampleLimit = 100L * 1024 * 1024;

    sealed class FolderScan
    {
        public string Path = "";
        public List<string> Files = [];
        public List<FolderScan> Folders = [];
        public bool Blocked;
    }

    static bool Redirected(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // Links, hidden-by-dot folders (Noir's own .noir-incoming) and folders a download is writing to are never entered.
    static FolderScan Walk(string dir, HashSet<string> protectedDirs, int depth = 0)
    {
        var scan = new FolderScan { Path = dir };
        if (depth > 12 || Redirected(dir) || protectedDirs.Contains(dir))
        {
            scan.Blocked = true;
            return scan;
        }
        try
        {
            scan.Files.AddRange(Directory.EnumerateFiles(dir));
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (Path.GetFileName(sub).StartsWith('.'))
                    scan.Blocked = true;
                else
                    scan.Folders.Add(Walk(sub, protectedDirs, depth + 1));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            scan.Blocked = true;
        }
        return scan;
    }

    static bool Junk(string file) =>
        Path.GetFileName(file).Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);

    static IEnumerable<(FolderScan Folder, FolderScan? Parent)> All(FolderScan scan, FolderScan? parent = null) =>
        new[] { (scan, parent) }.Concat(scan.Folders.SelectMany(f => All(f, scan)));

    List<DownloadSpaceItem> CleanupSuggestions()
    {
        var items = new List<DownloadSpaceItem>();
        var protectedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var busyFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootPaths = store
            .State.Roots.ToList()
            .Select(r => Path.GetFullPath(r.Path).TrimEnd(Path.DirectorySeparatorChar))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (gate)
            foreach (var job in jobs.Where(Live).Where(j => j.Destination.Length > 0))
            {
                busyFiles.Add(job.Destination);
                // The folder a download will write into, and the empty folders above it inside the library.
                for (
                    var d = Path.GetDirectoryName(job.Destination);
                    d != null && !rootPaths.Contains(d.TrimEnd(Path.DirectorySeparatorChar));
                    d = Path.GetDirectoryName(d)
                )
                    protectedDirs.Add(d);
            }
        foreach (var root in store.State.Roots.ToList().Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (root.Length == 0 || !Directory.Exists(root) || Redirected(root))
                continue;
            var top = Walk(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), protectedDirs);
            // Folders with nothing in them, outermost only (a show folder holding only empty seasons is one entry).
            bool Empty(FolderScan scan) => !scan.Blocked && scan.Files.All(Junk) && scan.Folders.All(Empty);
            void Collect(FolderScan scan)
            {
                foreach (var sub in scan.Folders)
                    if (Empty(sub))
                        items.Add(
                            new(sub.Path, KindEmptyFolders, Path.GetRelativePath(top.Path, sub.Path), 0, sub.Path)
                        );
                    else
                        Collect(sub);
            }
            Collect(top);
            foreach (var (folder, parent) in All(top).Where(f => !f.Folder.Blocked))
            {
                var videos = folder.Files.Where(DownloadNaming.Video).ToList();
                // A "Sample" folder sits beside the real video, so its parent counts as next to it.
                var near = videos
                    .Concat(
                        Path.GetFileName(folder.Path).Contains("sample", StringComparison.OrdinalIgnoreCase)
                            ? parent?.Files.Where(DownloadNaming.Video) ?? []
                            : []
                    )
                    .ToList();
                foreach (var file in folder.Files)
                {
                    if (busyFiles.Contains(file) || Redirected(file))
                        continue;
                    var name = Path.GetFileNameWithoutExtension(file);
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    try
                    {
                        if (DownloadNaming.Video(file))
                        {
                            if (!name.Contains("sample", StringComparison.OrdinalIgnoreCase))
                                continue;
                            var size = new FileInfo(file).Length;
                            if (
                                size < SampleLimit
                                && near.Any(v =>
                                    !v.Equals(file, StringComparison.OrdinalIgnoreCase)
                                    && !Path.GetFileNameWithoutExtension(v)
                                        .Contains("sample", StringComparison.OrdinalIgnoreCase)
                                    && new FileInfo(v).Length > size
                                )
                            )
                                items.Add(new(file, KindSamples, Path.GetRelativePath(top.Path, file), size, file));
                        }
                        else if (ext is ".nfo" or ".txt" or ".url" or ".exe")
                        {
                            var fileName = Path.GetFileName(file);
                            if (
                                fileName.Equals("tvshow.nfo", StringComparison.OrdinalIgnoreCase)
                                || fileName.Equals("movie.nfo", StringComparison.OrdinalIgnoreCase)
                                || fileName.Equals("season.nfo", StringComparison.OrdinalIgnoreCase)
                                || (File.GetAttributes(file) & (FileAttributes.Hidden | FileAttributes.System)) != 0
                            )
                                continue;
                            if (
                                videos.Any(v =>
                                {
                                    var stem = Path.GetFileNameWithoutExtension(v);
                                    return name.Equals(stem, StringComparison.OrdinalIgnoreCase)
                                        || name.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase);
                                })
                            )
                                continue;
                            // A program is listed so you can see it, but never ticked for you.
                            items.Add(
                                new(
                                    file,
                                    KindLoose,
                                    Path.GetRelativePath(top.Path, file),
                                    new FileInfo(file).Length,
                                    file,
                                    Preselect: ext != ".exe",
                                    ListOnly: ext == ".exe"
                                )
                            );
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
            }
        }
        return items;
    }

    static readonly Regex QualityTag = new(
        @"(?i)(?<![a-z0-9])(2160p|4k|uhd|1440p|1080[pi]|720p|576p|480p)(?![a-z0-9])",
        RegexOptions.Compiled
    );
    static readonly Regex SourceTag = new(
        @"(?i)(?<![a-z0-9])(remux|blu-?ray|bdrip|brrip|web-?dl|web-?rip|hdtv|dvdrip|hdrip)(?![a-z0-9])",
        RegexOptions.Compiled
    );

    // Quality comes from the file name when it says so; the size is always shown, so a file without a tag still compares.
    public static string QualityText(string path, long bytes, bool withSize = true)
    {
        var name = Path.GetFileName(path);
        var tags = new List<string>();
        if (QualityTag.Match(name) is { Success: true } q)
            tags.Add(q.Value.ToLowerInvariant() == "4k" ? "4K" : q.Value.ToLowerInvariant());
        if (SourceTag.Match(name) is { Success: true } s)
            tags.Add(s.Value);
        if (withSize)
            tags.Add(DownloadNaming.Size(bytes));
        return string.Join(" · ", tags);
    }

    // The same movie or the same show + season + episode saved at 2 or more paths. The largest copy is the one kept.
    public List<List<DownloadLocalFile>> DuplicateGroups(IEnumerable<DownloadLocalFile>? local = null)
    {
        local ??= LocalLibrary().SelectMany(g => g.Files);
        var busy = Jobs().Where(Live).Select(j => j.Destination).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return local
            .Where(f => !busy.Contains(f.Path) && (f.Item.IsMovie || f.Item.Episode > 0))
            .GroupBy(f =>
                f.Item.IsMovie ? "m:" + f.Item.Id : "e:" + f.Item.GroupKey + ":" + f.Item.Season + ":" + f.Item.Episode
            )
            .Select(g =>
                g.DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(f => f.Bytes)
                    .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            )
            .Where(g => g.Count > 1)
            .OrderBy(g => g[0].Item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g[0].Item.Season)
            .ThenBy(g => g[0].Item.Episode)
            .ToList();
    }

    // The folder as the library shows it: relative to the library folder it is in.
    string LibraryRelative(string folder)
    {
        foreach (var root in store.State.Roots.ToList().Select(r => r.Path).Where(r => r.Length > 0))
            if (Inside(folder + Path.DirectorySeparatorChar, root))
                return Path.GetRelativePath(root, folder) is var rel && rel != "." ? rel : "";
        return folder;
    }

    List<DownloadSpaceItem> DuplicateSuggestions(List<DownloadLocalFile> local, HashSet<string> taken)
    {
        var items = new List<DownloadSpaceItem>();
        foreach (var group in DuplicateGroups(local))
        {
            if (group.Any(f => taken.Contains(f.Path)))
                continue;
            for (var n = 0; n < group.Count; n++)
            {
                var file = group[n];
                var keep = n == 0;
                items.Add(
                    new(
                        file.Path,
                        KindDuplicates,
                        file.Item.Label
                            + " · "
                            + QualityText(file.Path, file.Bytes, false)
                            + (
                                LibraryRelative(Path.GetDirectoryName(file.Path) ?? "") is { Length: > 0 } where
                                    ? " · " + FreeSpaceText.MiddleTrim(where, 34)
                                    : ""
                            )
                            + (keep ? Message(" · kept", " · يُحتفظ به") : ""),
                        file.Bytes,
                        file.Path,
                        file,
                        Preselect: !keep,
                        KeepPath: group[0].Path
                    )
                );
            }
        }
        return items;
    }

    // Empty folders go bottom-up with the plain delete: it refuses a folder that has gained a file since the review.
    void DeleteEmptyTree(string dir)
    {
        // The top folder itself too: it may have been swapped for a link since the review.
        if (Redirected(dir))
            throw new IOException(
                Message("Choose a folder without redirected links.", "اختر مجلدًا دون روابط إعادة توجيه.")
            );
        foreach (var sub in Directory.EnumerateDirectories(dir))
            DeleteEmptyTree(sub);
        foreach (var junk in Directory.EnumerateFiles(dir).Where(Junk))
            File.Delete(junk);
        Directory.Delete(dir, false);
    }

    bool InsideLibrary(string path)
    {
        return store.State.Roots.ToList().Select(r => r.Path).Any(r => r.Length > 0 && Inside(path, r));
    }

    // Removes one suggestion that is not a tracked video, after checking again that it is inside a library folder.
    void RemoveLeftover(DownloadSpaceItem item, bool recycle)
    {
        if (!InsideLibrary(item.Path))
            throw new IOException(
                Message("This file is outside your library folders.", "هذا الملف خارج مجلدات المكتبة.")
            );
        if (item.Kind == KindEmptyFolders)
        {
            if (ManageDeleter != null)
                ManageDeleter(item.Path, recycle);
            else
                DeleteEmptyTree(item.Path);
            return;
        }
        // Program files are only listed; Noir never removes them, whatever it is handed.
        if (item.ListOnly || Path.GetExtension(item.Path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException(Message("Program file · Noir does not remove these", "ملف برنامج · لا يحذفه Noir"));
        if (Redirected(item.Path))
            throw new IOException(
                Message("Choose a file without redirected links.", "اختر ملفًا دون روابط إعادة توجيه.")
            );
        RefusePlaying(item.Path);
        if (IsDownloadingFile(item.Path))
            throw new IOException(Message("This file is still downloading.", "لا يزال هذا الملف قيد التنزيل."));
        RemoveDisk(item.Path, recycle);
    }

    // Duplicates the user chose: never every copy of one title. Returns the files to delete and where each one's
    // progress goes.
    List<(DownloadLocalFile Delete, DownloadLocalFile Keep)> DuplicatePicks(IEnumerable<DownloadSpaceItem> chosen)
    {
        var wanted = chosen.Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var picks = new List<(DownloadLocalFile, DownloadLocalFile)>();
        foreach (var group in DuplicateGroups())
        {
            var delete = group.Where(f => wanted.Contains(f.Path)).ToList();
            if (delete.Count == 0)
                continue;
            if (delete.Count == group.Count)
                delete.Remove(group[0]);
            var keep = group.First(f => !delete.Contains(f));
            picks.AddRange(delete.Select(f => (f, keep)));
        }
        return picks;
    }
}
