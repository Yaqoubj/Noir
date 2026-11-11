namespace Noir;

// Seams between the torrent engine (TorrentEngine.cs) and download management (PcDownloadsManage.cs).
// Management calls these; the torrent engine implements them. Both stay optional so either side builds alone.
public sealed partial class PcDownloads
{
    /// <summary>Stop the torrent behind this job (download or seeding). When deleteData is true, also delete its incoming/partial data. Safe to call for non-torrent jobs.</summary>
    partial void TorrentStop(PcDownloadJob job, bool deleteData);

    /// <summary>The finished file of this job was moved to newPath by the user (Move to…/organizer). The torrent engine re-points seeding or stops it.</summary>
    partial void TorrentFileMoved(PcDownloadJob job, string newPath);
}
