using System.IO;
using System.Text.Json;
namespace Noir;

public sealed class OrganizationMove
{
    public string ItemId {get;set;}="";
    public string Root {get;set;}="";
    public string From {get;set;}="";
    public string To {get;set;}="";
    public bool Video {get;set;}
    public bool Done {get;set;}
    public string Fingerprint {get;set;}="";
}
public sealed class OrganizationPlan
{
    public List<OrganizationMove> Moves {get;set;}=[];
    public List<string> Skipped {get;set;}=[];
    public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
}
public sealed partial class LibraryStore
{
    public string OrganizerJournal => Path.Combine(DataDirectory,"organization.json");
    static bool InsideRoot(string path,string root) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    static string FileName(string value)
    {
        var clean=new string(value.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?' ':c).ToArray()).Trim().TrimEnd('.');
        if(clean.Length>100) clean=clean[..100].TrimEnd();
        if(clean.Length==0) throw new IOException("The title has no usable filename.");
        if(System.Text.RegularExpressions.Regex.IsMatch(clean,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)) clean="_"+clean;
        return clean;
    }
    static bool SafePath(string path,string root)
    {
        if(!InsideRoot(path,root)) return false;
        for(var folder=Path.GetDirectoryName(Path.GetFullPath(path));folder!=null&&InsideRoot(folder,root);folder=Path.GetDirectoryName(folder))
            if(Directory.Exists(folder)&&(File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0) return false;
        return !Directory.Exists(root)||(File.GetAttributes(root)&FileAttributes.ReparsePoint)==0;
    }
    public OrganizationPlan PlanOrganization()
    {
        lock(gate)
        {
            var plan=new OrganizationPlan();var destinations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in Archive.Where(i=>i.Available&&File.Exists(i.Path)))
            {
                try
                {
                    var root=State.Roots.Where(r=>r.Movies==item.IsMovie&&InsideRoot(item.Path,r.Path)).OrderByDescending(r=>r.Path.Length).FirstOrDefault();
                    if(root==null||!SafePath(item.Path,root.Path)) {plan.Skipped.Add(item.Label+" · outside a supported folder");continue;}
                    // Online confirmation prevents guessing a destructive canonical name.
                    if(item.SourceUrl.Length==0||(!item.IsMovie&&item.Episode<=0)) {plan.Skipped.Add(item.Label+" · refresh information to confirm this title first");continue;}
                    var title=FileName(item.Title)+(item.Year>0?$" ({item.Year})":"");
                    var folder=item.IsMovie?Path.Combine(root.Path,title):Path.Combine(root.Path,title,$"Season {item.Season:00}");
                    var stem=item.IsMovie?title:$"{title} S{item.Season:00}E{item.Episode:00}";
                    var target=Path.Combine(folder,stem+Path.GetExtension(item.Path).ToLowerInvariant());
                    if(string.Equals(Path.GetFullPath(item.Path),Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase)) continue;
                    var bundle=new List<OrganizationMove>{new(){ItemId=item.Id,Root=root.Path,From=item.Path,To=target,Video=true}};
                    var oldStem=Path.GetFileNameWithoutExtension(item.Path);
                    foreach(var sidecar in Directory.EnumerateFiles(Path.GetDirectoryName(item.Path)!))
                    {
                        var ext=Path.GetExtension(sidecar).ToLowerInvariant();
                        if(ext is not (".srt" or ".ass" or ".ssa" or ".vtt" or ".sub" or ".idx" or ".nfo")) continue;
                        var name=Path.GetFileNameWithoutExtension(sidecar);
                        if(!name.Equals(oldStem,StringComparison.OrdinalIgnoreCase)&&!name.StartsWith(oldStem+".",StringComparison.OrdinalIgnoreCase)) continue;
                        bundle.Add(new(){ItemId=item.Id,Root=root.Path,From=sidecar,To=Path.Combine(folder,stem+name[oldStem.Length..]+ext)});
                    }
                    if(bundle.Any(m=>!SafePath(m.To,root.Path)||File.Exists(m.To)||Directory.Exists(m.To)||destinations.Contains(m.To)))
                    {plan.Skipped.Add(item.Label+" · destination already exists or is unsafe");continue;}
                    foreach(var m in bundle) {m.Fingerprint=Fingerprint(m.From);destinations.Add(m.To);plan.Moves.Add(m);}
                }
                catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException) {plan.Skipped.Add(item.Label+" · "+e.Message);}
            }
            return plan;
        }
    }
    void WriteJournal(OrganizationPlan plan)
    {
        var temp=OrganizerJournal+".tmp";
        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) {JsonSerializer.Serialize(stream,plan,JsonOptions);stream.Flush(true);}
        File.Move(temp,OrganizerJournal,true);
    }
    void Relocate(string id,string from,string to)
    {
        if(!State.Catalog.TryGetValue(id,out var entry)) return;
        entry.Item.Path=to;entry.Item.Available=true;
        if(!entry.Paths.Contains(to,StringComparer.OrdinalIgnoreCase)) entry.Paths.Add(to);
        if(entry.Progress!=null) State.Progress[to]=entry.Progress;
        if(State.Intros.TryGetValue(from,out var intro)) State.Intros[to]=intro;
        if(State.Outros.TryGetValue(from,out var outro)) State.Outros[to]=outro;
    }
    public int Organize(OrganizationPlan plan,bool undo=false)
    {
        lock(gate)
        {
            if(!undo&&File.Exists(OrganizerJournal))
            {
                var previous=JsonSerializer.Deserialize<OrganizationPlan>(File.ReadAllText(OrganizerJournal),JsonOptions);
                if(previous?.Moves.Any(m=>m.Done)==true) throw new IOException("Undo the previous organization first, or keep its record using Archive changes.");
            }
            WriteJournal(plan);var count=0;
            foreach(var move in (undo?plan.Moves.AsEnumerable().Reverse():plan.Moves))
            {
                if(move.Done!=undo) continue;
                var from=undo?move.To:move.From;var to=undo?move.From:move.To;
                if(!SafePath(from,move.Root)||!SafePath(to,move.Root)||!File.Exists(from)||File.Exists(to)||Directory.Exists(to)||move.Fingerprint.Length==0||Fingerprint(from)!=move.Fingerprint) throw new IOException("File changed or destination exists: "+from);
                // Refuse active downloads/playback; rename never overwrites another file.
                using(var probe=new FileStream(from,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {}
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from,to);
                move.Done=!undo;WriteJournal(plan);
                if(move.Video) {Relocate(move.ItemId,from,to);Save();count++;}
            }
            return count;
        }
    }
    public OrganizationPlan? LastOrganization() => File.Exists(OrganizerJournal)?JsonSerializer.Deserialize<OrganizationPlan>(File.ReadAllText(OrganizerJournal),JsonOptions):null;
    void RecoverOrganization()
    {
        try
        {
            var plan=LastOrganization();if(plan==null) return;
            var changed=false;
            foreach(var move in plan.Moves)
            {
                if(!SafePath(move.From,move.Root)||!SafePath(move.To,move.Root)||move.Fingerprint.Length==0) continue;
                if(!File.Exists(move.From)&&File.Exists(move.To)&&Fingerprint(move.To)==move.Fingerprint)
                {move.Done=true;if(move.Video) Relocate(move.ItemId,move.From,move.To);changed=true;}
                else if(!File.Exists(move.To)&&File.Exists(move.From)&&Fingerprint(move.From)==move.Fingerprint)
                {move.Done=false;if(move.Video) Relocate(move.ItemId,move.To,move.From);changed=true;}
            }
            if(changed) {WriteJournal(plan);Save();}
        }
        catch(Exception e) {Log(e);LoadWarning="An interrupted organization needs attention. Its original paths are saved.";}
    }
    public void ArchiveOrganization()
    {
        if(File.Exists(OrganizerJournal)) File.Move(OrganizerJournal,Path.Combine(DataDirectory,"organization-"+DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".json"));
    }
}
