using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
namespace Noir;
public static class ProfileLocation
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern int GetCurrentPackageFullName(ref int length,System.Text.StringBuilder? name);
    public static string Shared => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".noir");
    public static string Prepare()
    {
        var shared=Shared;Directory.CreateDirectory(shared);
        var legacy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Noir");
        var package=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","Local","Packages","OpenAI.Codex_2p2nqsd0c76g0","LocalCache","Local","Noir");
        var length=0;var packaged=OperatingSystem.IsWindows()&&GetCurrentPackageFullName(ref length,null)!=15700;
        // Package virtualization makes legacy point at the package cache inside Codex.
        // Only an unpackaged launch can import the actual shortcut's profile.
        var sources=packaged?new[]{(Path:legacy,Key:"codex")} :new[]{(Path:legacy,Key:"desktop"),(Path:package,Key:"codex")};
        var options=new JsonSerializerOptions {PropertyNameCaseInsensitive=true,WriteIndented=true};
        foreach(var source in sources)
        {
            var marker=Path.Combine(shared,"imported-"+source.Key+".txt");
            if(File.Exists(marker)||!File.Exists(Path.Combine(source.Path,"library.json"))) continue;
            var incoming=JsonSerializer.Deserialize<PlayerState>(File.ReadAllText(Path.Combine(source.Path,"library.json")),options)??throw new IOException("Could not read earlier Noir history.");
            var file=Path.Combine(shared,"library.json");
            var existing=File.Exists(file)?JsonSerializer.Deserialize<PlayerState>(File.ReadAllText(file),options):null;
            // Preserve all original records, and retain the newest bookmark per identity/path.
            var merged=existing??incoming;
            if(existing!=null) Merge(merged,incoming);
            foreach(var oldFile in Directory.EnumerateFiles(source.Path,"*",SearchOption.AllDirectories))
            {
                var relative=Path.GetRelativePath(source.Path,oldFile);
                if(relative.StartsWith("backups"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(oldFile).StartsWith("library.json",StringComparison.OrdinalIgnoreCase)) continue;
                var target=Path.Combine(shared,relative);if(File.Exists(target)) continue;
                try {Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(oldFile,target);}
                catch(IOException) {} // Live browser cache files can be locked; original profile remains.
            }
            File.Copy(Path.Combine(source.Path,"library.json"),Path.Combine(shared,"before-import-"+source.Key+".json"),true);
            using(var stream=new FileStream(file+".tmp",FileMode.Create,FileAccess.Write,FileShare.None)) {JsonSerializer.Serialize(stream,merged,options);stream.Flush(true);}
            File.Move(file+".tmp",file,true);File.WriteAllText(marker,source.Path);
        }
        return shared;
    }
    public static void Merge(PlayerState target,PlayerState source)
    {
        foreach(var (id,entry) in source.Catalog)
        {
            if(!target.Catalog.TryGetValue(id,out var old)) target.Catalog[id]=entry;
            else
            {
                if(entry.Progress!=null&&(old.Progress==null||entry.Progress.UpdatedAt>old.Progress.UpdatedAt)) old.Progress=entry.Progress;
                old.Paths=old.Paths.Concat(entry.Paths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
        foreach(var (path,bookmark) in source.Progress) if(!target.Progress.TryGetValue(path,out var old)||bookmark.UpdatedAt>old.UpdatedAt) target.Progress[path]=bookmark;
        foreach(var (id,views) in source.Viewings) target.Viewings[id]=target.Viewings.GetValueOrDefault(id,[]).Concat(views).GroupBy(v=>v.Id).Select(g=>g.OrderByDescending(v=>v.WatchedAt).First()).ToList();
        target.Roots=target.Roots.Concat(source.Roots).DistinctBy(r=>r.Path,StringComparer.OrdinalIgnoreCase).ToList();
        target.Watchlist.UnionWith(source.Watchlist);target.MobileReceipts.UnionWith(source.MobileReceipts);
        foreach(var (k,v) in source.Intros) target.Intros.TryAdd(k,v);
        foreach(var (k,v) in source.Outros) target.Outros.TryAdd(k,v);
        foreach(var (k,v) in source.EpisodeSchedules) if(!target.EpisodeSchedules.ContainsKey(k)||target.EpisodeSchedules[k].Count<v.Count) target.EpisodeSchedules[k]=v;
        foreach(var (k,v) in source.PersonalReviews) target.PersonalReviews.TryAdd(k,v);
        foreach(var (k,v) in source.PlaybackPreferences) target.PlaybackPreferences.TryAdd(k,v);
        foreach(var (k,v) in source.WatchSync) target.WatchSync.TryAdd(k,v);
        foreach(var (k,v) in source.TitleStatuses) target.TitleStatuses.TryAdd(k,v);
        foreach(var (k,v) in source.Collections){if(!target.Collections.TryGetValue(k,out var members))target.Collections[k]=members=[];members.UnionWith(v);}
        foreach(var (k,v) in source.Matches) target.Matches.TryAdd(k,v);
        foreach(var (k,v) in source.ReviewTabs) target.ReviewTabs.TryAdd(k,v);
    }
}
