using System.IO;

namespace Noir;
// Removing a title: hide it from the library (history stays), optionally forget its history too,
// and optionally move its files to the Recycle Bin so a removal is never a permanent delete.
public sealed partial class LibraryStore
{
    public bool IsHidden(string groupKey){lock(gate)return State.HiddenTitles.Contains(groupKey);}
    // The library is what you have or chose to have: a file (now or once), a started download, or an
    // explicit "+ Library". Logged or imported history and My List alone live in History and My List.
    public bool InLibrary(string groupKey)
    {
        lock(gate)
        {
            if(State.HiddenTitles.Contains(groupKey))return false;
            return State.LibraryAdds.Contains(groupKey)||EntriesFor(groupKey).Any(e=>e.Item.Path.Length>0||e.Paths.Count>0);
        }
    }
    public HashSet<string> LibraryGroups()
    {
        lock(gate)
        {
            var keys=State.Catalog.Values.Where(e=>e.Item.Path.Length>0||e.Paths.Count>0).Select(e=>e.Item.GroupKey).Concat(State.LibraryAdds).ToHashSet();
            keys.ExceptWith(State.HiddenTitles);return keys;
        }
    }
    public void SetInLibrary(string groupKey,bool value)
    {
        lock(gate)
        {
            if(value){State.LibraryAdds.Add(groupKey);State.HiddenTitles.Remove(groupKey);}
            else{State.LibraryAdds.Remove(groupKey);State.HiddenTitles.Add(groupKey);}
            TouchMobileField("library:"+groupKey);Save();
        }
    }
    public void SetHidden(string groupKey,bool hidden){lock(gate){if(hidden){State.HiddenTitles.Add(groupKey);State.LibraryAdds.Remove(groupKey);}else State.HiddenTitles.Remove(groupKey);TouchMobileField("hidden:"+groupKey);Save();}}
    // Video files of the title plus their subtitle sidecars, for moving to the Recycle Bin.
    public List<string> FilesOf(string groupKey)
    {
        lock(gate)
        {
            var videos=EntriesFor(groupKey).SelectMany(e=>e.Paths.Append(e.Item.Path)).Where(p=>p.Length>0&&File.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var sidecars=videos.SelectMany(v=>Directory.EnumerateFiles(Path.GetDirectoryName(v)!,Path.GetFileNameWithoutExtension(v)+".*").Where(f=>SubtitleDownloads.TextSubtitle(f)));
            return videos.Concat(sidecars).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
    // Forgets everything Noir knows about the title. The group stays hidden so files left in a
    // library folder don't silently come back as "new" on the next scan.
    public void ForgetTitle(string groupKey)
    {
        lock(gate)
        {
            var entries=State.Catalog.Where(p=>p.Value.Item.GroupKey==groupKey).ToList();var ids=entries.Select(p=>p.Key).ToHashSet();
            foreach(var (id,entry) in entries)
            {
                State.Catalog.Remove(id);State.Viewings.Remove(id);State.PersonalReviews.Remove(id);State.Outros.Remove(id);State.Matches.Remove(id);
                foreach(var path in entry.Paths.Append(entry.Item.Path).Where(p=>p.Length>0)){State.Progress.Remove(path);State.Intros.Remove(path);if(File.Exists(path))State.RemovedFiles.Add(path.ToUpperInvariant());}
            }
            foreach(var key in State.Intros.Keys.Where(k=>k.StartsWith(groupKey+":season:",StringComparison.OrdinalIgnoreCase)).ToList())State.Intros.Remove(key);
            foreach(var key in State.Outros.Keys.Where(k=>k.StartsWith(groupKey+":season:",StringComparison.OrdinalIgnoreCase)).ToList())State.Outros.Remove(key);
            foreach(var alias in State.CatalogAliases.Where(a=>ids.Contains(a.Value)).Select(a=>a.Key).ToList())State.CatalogAliases.Remove(alias);
            foreach(var job in State.WatchSync.Where(j=>ids.Contains(j.Value.ItemId)).Select(j=>j.Key).ToList())State.WatchSync.Remove(job);
            State.PlayQueue.RemoveAll(ids.Contains);State.EpisodeSchedules.Remove(groupKey);State.Watchlist.Remove(groupKey);State.TitleStatuses.Remove(groupKey);
            State.HiddenContinue.Remove(groupKey);State.NewEpisodeAlerts.Remove(groupKey);foreach(var members in State.Collections.Values)members.Remove(groupKey);
            foreach(var rating in State.ShowRatings.Keys.Where(k=>k==groupKey||k.StartsWith(groupKey+":S",StringComparison.Ordinal)).ToList())State.ShowRatings.Remove(rating);
            State.ShowStates.Remove(groupKey);State.ShowRuntimes.Remove(groupKey);State.TrackerSnoozes.Remove(groupKey);State.IncludeSpecials.Remove(groupKey);
            State.HiddenTitles.Add(groupKey);Save();
        }
        HistoryChanged?.Invoke();
    }
}
