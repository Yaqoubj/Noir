namespace Noir;
// Group → catalog entries, so per-title lookups don't scan the whole catalog. Rebuilt when the
// catalog changes size or a writer calls CatalogChanged; every lookup re-checks its own entries, so
// a missed invalidation costs a rebuild rather than a wrong answer. Callers hold gate.
public sealed partial class LibraryStore
{
    Dictionary<string,List<(string Id,CatalogEntry Entry)>>? groupIndex;
    Dictionary<string,CatalogEntry>? indexedCatalog;
    int indexedCount;
    void CatalogChanged()=>groupIndex=null;
    void IndexRebuilt()
    {
        groupIndex=new();indexedCatalog=State.Catalog;indexedCount=State.Catalog.Count;
        foreach(var (id,entry) in State.Catalog){var key=entry.Item.GroupKey;if(!groupIndex.TryGetValue(key,out var list))groupIndex[key]=list=[];list.Add((id,entry));}
    }
    List<CatalogEntry> EntriesFor(string groupKey)
    {
        if(groupIndex==null||!ReferenceEquals(indexedCatalog,State.Catalog)||indexedCount!=State.Catalog.Count)IndexRebuilt();
        if(!groupIndex!.TryGetValue(groupKey,out var list))return [];
        foreach(var (id,entry) in list)
            if(entry.Item.GroupKey!=groupKey||!State.Catalog.TryGetValue(id,out var now)||!ReferenceEquals(now,entry)){IndexRebuilt();list=groupIndex.GetValueOrDefault(groupKey)??[];break;}
        return list.Select(p=>p.Entry).ToList();
    }
}
