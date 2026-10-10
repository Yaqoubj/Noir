using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Noir;
public sealed class MobileSubtitle {public string Id {get;set;}="";public string Name {get;set;}="";public string Extension {get;set;}="";}
public static class MobileSubtitles
{
    public static List<(MobileSubtitle Info,string Path)> Find(VideoItem item)
    {
        var result=new List<(MobileSubtitle,string)>();if(!File.Exists(item.Path))return result;
        var dir=Path.GetDirectoryName(item.Path)!;var stem=Path.GetFileNameWithoutExtension(item.Path);
        try {foreach(var path in Directory.EnumerateFiles(dir).Take(3000))
        {
            var ext=Path.GetExtension(path).ToLowerInvariant();if(ext is not (".srt" or ".ass" or ".ssa" or ".vtt"))continue;
            var name=Path.GetFileNameWithoutExtension(path);if(!name.Equals(stem,StringComparison.OrdinalIgnoreCase)&&!name.StartsWith(stem+".",StringComparison.OrdinalIgnoreCase))continue;
            var info=new FileInfo(path);if(info.Length>4*1024*1024||(info.Attributes&FileAttributes.ReparsePoint)!=0)continue;
            var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Id+":"+Path.GetFileName(path)+":"+info.Length+":"+info.LastWriteTimeUtc.Ticks)))[..24];
            result.Add((new(){Id=id,Name=name.Length>stem.Length?name[(stem.Length+1)..]:"External subtitles",Extension=ext},path));
            if(result.Count>=12)break;
        }}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
        return result;
    }
}
