using System.IO;
using System.Text.Json;
namespace Noir;
public static class MobileProfiles
{
    static readonly object Gate=new();
    public static string Root(string directory)
    {
        var full=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);var parent=Path.GetDirectoryName(full);
        return parent!=null&&Path.GetFileName(parent)=="profiles"?Path.GetDirectoryName(parent)!:full;
    }
    public static NoirProfile Active(string directory)
    {
        var root=Root(directory);var id=string.Equals(root,Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)?"default":Path.GetFileName(directory);
        return Profiles.Load(root).Profiles.FirstOrDefault(p=>p.Id==id)??new(){Id=id,Name=id};
    }
    public static string HostId(string directory)
    {
        lock(Gate){var root=Root(directory);Directory.CreateDirectory(root);var file=Path.Combine(root,"mobile-host-id.txt");if(File.Exists(file))return File.ReadAllText(file).Trim();var id=Guid.NewGuid().ToString("N");File.WriteAllText(file+".tmp",id);File.Move(file+".tmp",file,true);return id;}
    }
    public static void Identify(MobileSnapshot snapshot,string directory)
    {var p=Active(directory);snapshot.ProfileId=p.Id;snapshot.ProfileName=p.Name;snapshot.ProfileColor=p.Color;snapshot.HostId=HostId(directory);}
    public static string Resolve(string requested,string directory)=>requested.Length==0?Active(directory).Id:requested;
    public static bool Matches(string requested,string directory)=>Resolve(requested,directory)==Active(directory).Id;
    static string Instance(string directory,string id)
    {try{using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(directory),id=="default"?"":"profiles/"+id,"library.json")));return json.RootElement.TryGetProperty("MobileInstanceId",out var value)?value.GetString()??"":"";}catch(Exception e)when(e is IOException or JsonException){return "";}}
    public static object[] List(string directory)=>Profiles.Load(Root(directory)).Profiles.Select(p=>(object)new{id=p.Id,name=p.Name,color=p.Color,locked=p.PinHash.Length>0,active=p.Id==Active(directory).Id,instance=Instance(directory,p.Id),hostId=HostId(directory)}).ToArray();
    public static NoirProfile? Update(string directory,string? id,string name,string color)
    {
        name??="";color??="";if(name.Trim().Length is <1 or >80||!System.Text.RegularExpressions.Regex.IsMatch(color,"^#[0-9a-fA-F]{6}$"))return null;
        lock(Gate){var root=Root(directory);var list=Profiles.Load(root);if(id==null)return Profiles.Create(list,name,color,root);var p=list.Profiles.FirstOrDefault(p=>p.Id==id);if(p==null)return null;p.Name=name.Trim();p.Color=color;Profiles.Save(list,root);return p;}
    }
}
