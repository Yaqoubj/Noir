namespace Noir;
public sealed partial class LibraryStore
{
    void ReconcileCatalog()
    {
        foreach(var group in State.Catalog.Values.Where(e=>!e.Item.IsMovie).GroupBy(e=>MatchTitle(CleanShowTitle(e.Item.Title))))
        {
            var years=group.Select(e=>e.Item.Year).Where(y=>y>0).Distinct().ToList();
            if(years.Count>1) continue; // Same-name remakes remain separate.
            var key=group.OrderByDescending(e=>e.Item.Year>0).ThenByDescending(e=>State.Viewings.GetValueOrDefault(e.Item.Id,[]).Count).First().Item.GroupKey;
            foreach(var entry in group)
            {
                var old=entry.Item.GroupKey;if(old==key) continue;
                if(State.Watchlist.Remove(old)) State.Watchlist.Add(key);
                if(State.TitleStatuses.TryGetValue(old,out var status))State.TitleStatuses.TryAdd(key,status);
                if(State.ReviewTabs.TryGetValue(old,out var reviewTab))State.ReviewTabs.TryAdd(key,reviewTab);
                foreach(var collection in State.Collections.Values)if(collection.Remove(old))collection.Add(key);
                if(State.HiddenContinue.Remove(old)) State.HiddenContinue.Add(key);
                if(State.EpisodeSchedules.TryGetValue(old,out var schedule)) State.EpisodeSchedules[key]=State.EpisodeSchedules.GetValueOrDefault(key,[]).Concat(schedule).DistinctBy(e=>(e.Season,e.Episode)).ToList();
                if(State.PlaybackPreferences.TryGetValue(old,out var preference)) State.PlaybackPreferences.TryAdd(key,preference);
                foreach(var pair in State.Intros.Where(p=>p.Key.StartsWith(old+":season:")).ToList()) State.Intros.TryAdd(key+pair.Key[old.Length..],pair.Value);
                foreach(var pair in State.Outros.Where(p=>p.Key.StartsWith(old+":season:")).ToList()) State.Outros.TryAdd(key+pair.Key[old.Length..],pair.Value);
                entry.Item.CollectionKey=key;
            }
        }
        foreach(var duplicate in State.Catalog.Values.GroupBy(e=>(e.Item.GroupKey,e.Item.Season,e.Item.Episode)).Where(g=>g.Count()>1).ToList())
        {
            var canonical=duplicate.First();
            foreach(var other in duplicate.Skip(1).ToList())
            {
                if(!canonical.Paths.Intersect(other.Paths,StringComparer.OrdinalIgnoreCase).Any()&&
                    (canonical.Fingerprint.Length==0||canonical.Fingerprint!=other.Fingerprint)) continue;
                var from=other.Item.Id;var to=canonical.Item.Id;
                if(other.Progress!=null&&(canonical.Progress==null||other.Progress.UpdatedAt>canonical.Progress.UpdatedAt)) canonical.Progress=other.Progress;
                canonical.Paths=canonical.Paths.Concat(other.Paths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                State.Viewings[to]=State.Viewings.GetValueOrDefault(to,[]).Concat(State.Viewings.GetValueOrDefault(from,[])).DistinctBy(v=>v.Id).ToList();
                State.Viewings.Remove(from);
                if(State.PersonalReviews.TryGetValue(from,out var review)) State.PersonalReviews.TryAdd(to,review);
                if(State.Matches.TryGetValue(from,out var correction))State.Matches.TryAdd(to,correction);
                for(int n=0;n<State.PlayQueue.Count;n++)if(State.PlayQueue[n]==from)State.PlayQueue[n]=to;
                State.PlayQueue=State.PlayQueue.Distinct().ToList();
                State.CatalogAliases[from]=to;State.Catalog.Remove(from);
                foreach(var job in State.WatchSync.Values.Where(j=>j.ItemId==from)) job.ItemId=to;
            }
        }
    }
}
