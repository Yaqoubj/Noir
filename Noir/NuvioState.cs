namespace Noir;
// Nuvio bookkeeping inside the library: which items Nuvio has, and un-checks waiting to reach it.
public sealed record NuvioWatched(string ContentId,string Type,string Title,int? Season,int? Episode,long WatchedAt);
public static class NuvioKeys
{
    // Delete keys are stored as "tt123|1|2" (movies: "tt123||").
    public static string Key(string contentId,int? season,int? episode)=>contentId+"|"+season+"|"+episode;
    public static (string ContentId,int? Season,int? Episode) ParseKey(string key){var p=key.Split('|');return(p[0],int.TryParse(p.ElementAtOrDefault(1),out var s)?s:null,int.TryParse(p.ElementAtOrDefault(2),out var e)?e:null);}
}
public sealed partial class LibraryStore
{
    // Nuvio removed something it had sent: drop only the viewings that came from Nuvio.
    public int RemoveNuvioViewings(IEnumerable<NuvioWatched> gone)
    {
        var keys=gone.Select(g=>NuvioKeys.Key(g.ContentId,g.Season,g.Episode)).ToHashSet();
        HashSet<string> items;
        lock(gate)items=State.Catalog.Values.Where(e=>e.Item.ImdbId.Length>0&&keys.Contains(e.Item.IsMovie?NuvioKeys.Key(e.Item.ImdbId,null,null):NuvioKeys.Key(e.Item.ImdbId,e.Item.Season,e.Item.Episode))).Select(e=>e.Item.Id).ToHashSet();
        if(items.Count==0)return 0;
        var undo=RemoveViewings((id,v)=>items.Contains(id)&&v.Source=="Imported from Nuvio");
        return undo.Sum(s=>s.Viewings.Count(v=>v.Source=="Imported from Nuvio"));
    }
    // Un-checking in Noir removes it from Nuvio too, for items Nuvio is known to have.
    internal void ForgetOnNuvio(VideoItem item)
    {
        if(!State.NuvioKnown.Contains(item.Id)||!System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId,@"^tt\d+$"))return;
        State.NuvioDeletes.Add(item.IsMovie?NuvioKeys.Key(item.ImdbId,null,null):NuvioKeys.Key(item.ImdbId,item.Season,item.Episode));State.NuvioKnown.Remove(item.Id);State.WatchSync.Remove("Nuvio:"+item.Id);
    }
    // Undo after an un-check: cancel a removal still waiting, or send the watch again if it already went.
    void RememberOnNuvio(VideoItem item,List<ViewingRecord> viewings)
    {
        if(!State.NuvioConnected||!System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId,@"^tt\d+$"))return;
        var key=item.IsMovie?NuvioKeys.Key(item.ImdbId,null,null):NuvioKeys.Key(item.ImdbId,item.Season,item.Episode);
        if(State.NuvioDeletes.Remove(key)){State.NuvioKnown.Add(item.Id);return;}
        if(viewings.FirstOrDefault(v=>!v.DateUnknown) is not {} dated||State.NuvioKnown.Contains(item.Id))return;
        State.WatchSync.Remove("Nuvio:"+item.Id);CommunityQueue.Enqueue(State,item,dated.WatchedAt);
    }
    public void MarkNuvioKnown(IEnumerable<string> itemIds){lock(gate){State.NuvioKnown.UnionWith(itemIds);Save();}}
}
