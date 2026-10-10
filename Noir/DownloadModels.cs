using System.IO;
using System.Text.RegularExpressions;

namespace Noir;

public sealed class DownloadChoice
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public string Detail {get;set;}="";
    public long Size {get;set;}
    public bool Cached {get;set;}
    public string CacheStatus {get;set;}="Unknown";
    public string SizeKind {get;set;}="Release";
    public List<string> Sources {get;set;}=[];
    public string Resolution {get;set;}="";
    public string Recommendation {get;set;}="";
    public int ReferenceSeason {get;set;}
    public int ReferenceEpisode {get;set;}
    public string Warning {get;set;}="";
    public string Languages {get;set;}="Unknown";
    public string Codec {get;set;}="";
    public string Picture {get;set;}="";
    public string ReleaseKind {get;set;}="";
    public int SourceEntries {get;set;}=1;
}
public sealed class DownloadFileChoice
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public long Size {get;set;}
    public int Season {get;set;}
    public int Episode {get;set;}
    public bool Existing {get;set;}
    public bool Selectable {get;set;}
    public bool Queued {get;set;}
    public bool Future {get;set;}
    public string Reason {get;set;}="";
    public bool MappingRequired {get;set;}
}
public sealed class PcDownloadJob
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string RequestId {get;set;}="";
    public string Name {get;set;}="";
    public string Hash {get;set;}="";
    public long TorrentId {get;set;}=-1;
    public long FileId {get;set;}=-1;
    public string RemoteName {get;set;}="";
    public string Root {get;set;}="";
    public string Destination {get;set;}="";
    public long Size {get;set;}
    public long Bytes {get;set;}
    public string ETag {get;set;}="";
    public string Status {get;set;}="Queued";
    public string Message {get;set;}="";
    public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
    public VideoItem Target {get;set;}=new();
    public List<string> SubtitleLanguages {get;set;}=[];
    public bool AutoSyncSubtitles {get;set;}
    public DateTimeOffset? NextAttempt {get;set;}
    public string SubtitleMessage {get;set;}="";
}
public sealed class DownloadRequest
{
    public string RequestId {get;set;}="";
    public string ReleaseId {get;set;}="";
    public List<string> FileIds {get;set;}=[];
    public List<DownloadSelection> Selections {get;set;}=[];
}
public sealed class DownloadSelection {public string ReleaseId {get;set;}="";public List<string> FileIds {get;set;}=[];}
public sealed class DownloadPlanRequest {public List<string> ReleaseIds {get;set;}=[];}
public sealed class DownloadMappingRequest {public string FileId {get;set;}="";public int Episode {get;set;}}
public sealed class DownloadSourceStatus
{
    public string Source {get;set;}="";
    public string State {get;set;}="Searching";
    public string Message {get;set;}="";
    public int Count {get;set;}
}
public sealed class ProviderSearchResult
{
    public List<IndexedRelease> Releases {get;set;}=[];
    public List<DownloadSourceStatus> Sources {get;set;}=[];
}
public sealed class DownloadSearchResult
{
    public List<DownloadChoice> Choices {get;set;}=[];
    public List<DownloadSourceStatus> Sources {get;set;}=[];
    public List<ReleaseGroup> Groups {get;set;}=[];
}
public sealed class DownloadPlanEntry
{
    public string ReleaseId {get;set;}="";
    public string Release {get;set;}="";
    public DownloadFileChoice File {get;set;}=new();
}
public sealed class DownloadPlan
{
    public List<DownloadPlanEntry> Entries {get;set;}=[];
    public List<int> MissingEpisodes {get;set;}=[];
    public int ReleasedEpisodes {get;set;}
    public int ExistingEpisodes {get;set;}
    public int QueuedEpisodes {get;set;}
    public long SelectedBytes {get;set;}
    public bool HasUnknownSizes {get;set;}
    public bool ScheduleKnown {get;set;}
    public string Message {get;set;}="";
}
public static class DownloadNaming
{
    public static bool Video(string name)=>Path.GetExtension(name).ToLowerInvariant() is ".mkv" or ".mp4" or ".avi" or ".webm" or ".m4v" or ".mov" or ".ts";
    public static (int Season,int Episode) Episode(string name,int season)
    {
        var m=Regex.Match(name,@"(?i)(?:s(?<s>\d{1,2})[ ._-]*e(?<e>\d{1,3})|(?<s>\d{1,2})x(?<e>\d{1,3}))");
        if(m.Success) return(int.Parse(m.Groups["s"].Value),int.Parse(m.Groups["e"].Value));
        m=Regex.Match(Path.GetFileName(name),@"(?i)(?:^|[ ._-])(?:e|ep|episode)[ ._-]*(?<e>\d{1,3})(?:[ ._-]|$)");
        return m.Success?(season,int.Parse(m.Groups["e"].Value)):(season,0);
    }
    public static string Clean(string value)
    {
        var s=new string(value.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?' ':c).ToArray()).Trim().TrimEnd('.');
        if(s.Length>100)s=s[..100].TrimEnd();if(s.Length==0)throw new IOException("Set a title before downloading.");
        if(Regex.IsMatch(s,@"(?i)^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$"))s="_"+s;
        return s;
    }
    public static string Destination(string root,VideoItem item,string remoteName)
    {
        if(!Video(remoteName))throw new IOException("Choose a video file.");
        if(!item.IsMovie&&item.Episode<1)throw new IOException("This file's episode number could not be verified.");
        var title=Clean(item.Title)+(item.Year>0?$" ({item.Year})":"");
        var folder=item.IsMovie?Path.Combine(root,title):Path.Combine(root,title,$"Season {item.Season:00}");
        var stem=item.IsMovie?title:$"{title} S{item.Season:00}E{item.Episode:00}";
        var path=Path.GetFullPath(Path.Combine(folder,stem+Path.GetExtension(remoteName).ToLowerInvariant()));
        Validate(root,path);return path;
    }
    public static void Validate(string root,string path)
    {
        var fullRoot=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var full=Path.GetFullPath(path);
        if(!full.StartsWith(fullRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Download location is outside the library folder.");
        for(var p=Path.GetDirectoryName(full);p!=null;p=Path.GetDirectoryName(p))
        {
            if(Directory.Exists(p)&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a download folder without redirected links.");
            if(p.Equals(fullRoot,StringComparison.OrdinalIgnoreCase))break;
        }
    }
    public static string Size(long bytes)=>bytes>=1073741824?$"{bytes/1073741824d:0.00} GB":bytes>0?$"{bytes/1048576d:0.0} MB":"Size unknown";
}
