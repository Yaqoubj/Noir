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

// Copy mutable state without JSON work under the library lock. Strings and value types are immutable here.
internal static class LibrarySnapshot
{
    public static T Copy<T>(T value)=>Copier<T>.Clone(value);
    static Dictionary<K,V> Map<K,V>(Dictionary<K,V> source) where K:notnull=>source.ToDictionary(p=>p.Key,p=>Copy(p.Value),source.Comparer);
    static List<T> List<T>(List<T> source)=>source.Select(Copy).ToList();
    static HashSet<T> Set<T>(HashSet<T> source)=>new(source.Select(Copy),source.Comparer);
    static class Copier<T>
    {
        public static readonly Func<T,T> Clone=Build();
        static Func<T,T> Build()
        {
            var type=typeof(T);if(type.IsValueType||type==typeof(string))return value=>value;
            if(type.IsGenericType)
            {
                var generic=type.GetGenericTypeDefinition();var name=generic==typeof(Dictionary<,>)?nameof(Map):generic==typeof(List<>)?nameof(List):generic==typeof(HashSet<>)?nameof(Set):null;
                if(name!=null){var copy=(Func<T,T>)typeof(LibrarySnapshot).GetMethod(name,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.MakeGenericMethod(type.GetGenericArguments()).CreateDelegate(typeof(Func<T,T>));return value=>value==null?value:copy(value);}
            }
            var source=System.Linq.Expressions.Expression.Parameter(type,"source");var target=System.Linq.Expressions.Expression.Variable(type,"target");
            var body=new List<System.Linq.Expressions.Expression>{System.Linq.Expressions.Expression.Assign(target,System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Call(source,typeof(object).GetMethod("MemberwiseClone",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!),type))};
            foreach(var field in type.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
                if(!field.FieldType.IsValueType&&field.FieldType!=typeof(string))body.Add(System.Linq.Expressions.Expression.Assign(System.Linq.Expressions.Expression.Field(target,field),System.Linq.Expressions.Expression.Call(typeof(LibrarySnapshot).GetMethod(nameof(Copy))!.MakeGenericMethod(field.FieldType),System.Linq.Expressions.Expression.Field(source,field))));
            body.Add(target);var clone=System.Linq.Expressions.Expression.Lambda<Func<T,T>>(System.Linq.Expressions.Expression.Block([target],body),source).Compile();return value=>value==null?value:clone(value);
        }
    }
}
