using System.IO;
using System.Text;
using System.Text.RegularExpressions;
namespace Noir;

public sealed class MobileSubtitleSyncInput
{
    public string Hash {get;set;}="";
    public string Text {get;set;}="";
    public string Extension {get;set;}=".srt";
}
public sealed class MobileSubtitleTools(LibraryStore store)
{
    readonly SemaphoreSlim work=new(1,1);
    public async Task<object> Info(string id,CancellationToken token)
    {
        var item=store.Archive.FirstOrDefault(i=>i.Id==id);
        if(item==null||!File.Exists(item.Path))throw new IOException("This video is unavailable on the PC.");
        return new{hash=await SubtitleDownloads.VideoHash(item.Path,token),release=Path.GetFileName(item.Path),files=MobileSubtitles.Find(item).Select(s=>s.Info).ToList()};
    }
    public async Task<object> Sync(string id,MobileSubtitleSyncInput input,CancellationToken token)
    {
        var item=store.Archive.FirstOrDefault(i=>i.Id==id);
        if(item==null||!File.Exists(item.Path))throw new IOException("Keep this video on the PC for automatic timing. Manual timing works offline.");
        if(!Regex.IsMatch(input.Hash??"",@"^[a-fA-F0-9]{16}$")||input.Extension is not (".srt" or ".ass" or ".ssa" or ".vtt")||input.Text==null||Encoding.UTF8.GetByteCount(input.Text)>4*1024*1024||!SubtitleDownloads.ValidText(input.Text,input.Extension))throw new IOException("Invalid subtitle timing request.");
        if(!string.Equals(input.Hash,await SubtitleDownloads.VideoHash(item.Path,token),StringComparison.OrdinalIgnoreCase))throw new IOException("The phone copy differs from the PC video. Use manual timing for this copy.");
        if(!await work.WaitAsync(0,token))throw new IOException("Another subtitle timing check is running. Try again shortly.");
        var root=Path.GetFullPath(Path.Combine(store.DataDirectory,"mobile-subtitle-work"));
        var folder=Path.GetFullPath(Path.Combine(root,Guid.NewGuid().ToString("N")));
        try
        {
            Directory.CreateDirectory(folder);var subtitle=Path.Combine(folder,"phone"+input.Extension);await File.WriteAllTextAsync(subtitle,input.Text,new UTF8Encoding(false),token);
            var engine=new SubtitleDownloads(folder);var message=await engine.Sync(item.Path,subtitle,token);
            var accepted=message.StartsWith("auto-synced",StringComparison.Ordinal);
            return new{accepted,message,text=accepted?await File.ReadAllTextAsync(subtitle,token):""};
        }
        finally
        {
            work.Release();
            if(folder.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))try{Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
