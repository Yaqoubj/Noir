using Noir;

static class EmbeddedSubtitleChecks
{
    public static void Run(Action<bool,string> check)
    {
        check(EmbeddedSubtitles.Parse("""{"streams":[{"codec_type":"subtitle","tags":{"language":"eng"}}]}""").SetEquals(["en"]),"Embedded eng subtitle satisfies English");
        check(EmbeddedSubtitles.Parse("""{"streams":[{"codec_type":"subtitle","tags":{"title":"Arabic"}}]}""").SetEquals(["ar"]),"Embedded Arabic title supplies a missing language tag");
        check(EmbeddedSubtitles.Parse("""{"streams":[{"codec_type":"subtitle","tags":{"language":"eng"},"disposition":{"forced":1}}]}""").Count==0,"Forced English alone does not satisfy full subtitles");
        check(EmbeddedSubtitles.Parse("""{"streams":[{"codec_type":"subtitle","tags":{"language":"eng","title":"Signs"}},{"codec_type":"subtitle","tags":{"language":"ara","title":"Songs"}}]}""").Count==0,"Embedded signs and songs do not satisfy full subtitles");
        check(EmbeddedSubtitles.Parse("""{"streams":[{"codec_type":"subtitle","codec_name":"hdmv_pgs_subtitle","tags":{"language":"eng"}}]}""").SetEquals(["en"]),"Embedded PGS image subtitles satisfy English");
        check(EmbeddedSubtitles.Parse("""{"streams":[{"codec_type":"video"},{"codec_type":"audio","tags":{"language":"eng"}}]}""").Count==0,"Audio and video streams never satisfy subtitles");
        check(EmbeddedSubtitles.Parse("""{"streams":[{"codec_type":"subtitle","tags":{"language":"ara"}},{"codec_type":"subtitle","tags":{"language":"en","title":"Full dialogue"},"disposition":{"forced":0}},{"codec_type":"subtitle","tags":{"language":"english"}}]}""").SetEquals(["en","ar"]),"Embedded full tracks deduplicate supported languages");
        check(EmbeddedSubtitles.Parse("""{"streams":[{"codec_type":"subtitle","tags":{"language":"und","title":"Arabic"}}]}""").SetEquals(["ar"]),"Undefined embedded language falls back to the track title");
        check(new[]{"English [Forced]","English signs & songs","Arabic karaoke"}.All(t=>EmbeddedSubtitles.Language("",t)==""),"Partial subtitle titles are excluded even without disposition flags");
        check(EmbeddedSubtitles.Language("fr","English")==""&&EmbeddedSubtitles.Language(" ARABIC ","Full")=="ar"&&EmbeddedSubtitles.Language("","[English] SDH")=="en","Embedded tags take precedence and full accessibility tracks remain eligible");
        check(new[]{"bad json","{}","null","""{"streams":null}""","""{"streams":[null,{}]}"""}.All(j=>EmbeddedSubtitles.Parse(j).Count==0),"Missing or malformed probe metadata leaves subtitle downloads enabled");
        var tracks=EmbeddedSubtitles.TextTracks("""{"streams":[{"codec_type":"video"},{"codec_type":"subtitle","codec_name":"hdmv_pgs_subtitle","tags":{"language":"ara"}},{"codec_type":"subtitle","codec_name":"subrip","tags":{"language":"eng","title":"Signs"}},{"codec_type":"subtitle","codec_name":"subrip","tags":{"language":"eng"}},{"codec_type":"subtitle","codec_name":"ass","tags":{"language":"ara"}},{"codec_type":"subtitle","codec_name":"subrip","tags":{"language":"eng","title":"SDH"}}]}""");
        check(tracks.SequenceEqual([(2,"en",".srt"),(3,"ar",".ass")]),"Built-in timing checks pick the first full text track per language and count only subtitle streams");
        check(SubtitleDownloads.SyncedCopy(@"C:\TV\Show S01E01.mkv","en",".srt")==@"C:\TV\Show S01E01.synced.en.srt"&&SubtitleDownloads.SidecarLanguage("Show S01E01.synced.ar.ass")=="ar","Corrected built-in copies sit beside the video with their language");
        check(SubtitleDownloads.IsSyncedCopy("Show.synced.en")&&!SubtitleDownloads.IsSyncedCopy("Show.en")&&!SubtitleDownloads.IsSyncedCopy("Synced.Show.en"),"Only corrected copies count as synced");
    }
    // Real-media check, run on demand: NOIR_EMBEDDED_SYNC_CLIPS=<folder with good.mkv and late.mkv (English track 3 s late)>, NOIR_SUBTITLE_TOOLS=<work/subtitle-runtime>.
    public static async Task RunMedia(Action<bool,string> check,string clips,string tools)
    {
        var previous=SubtitleDownloads.ToolRoot;SubtitleDownloads.ToolRoot=tools;try{
        var engine=new SubtitleDownloads(Path.Combine(clips,"profile"));
        foreach(var name in new[]{"good","late"})File.Delete(Path.Combine(clips,name+".synced.en.srt"));
        var good=await engine.SyncEmbedded(Path.Combine(clips,"good.mkv"),"en",CancellationToken.None);
        check(good=="timing matches the video"&&!File.Exists(Path.Combine(clips,"good.synced.en.srt")),"Matching built-in subtitles leave no extra copy · "+good);
        var late=await engine.SyncEmbedded(Path.Combine(clips,"late.mkv"),"en",CancellationToken.None);
        var offset=System.Text.RegularExpressions.Regex.Match(late,@"\(([+-][0-9.]+)s\)");
        check(late.StartsWith("auto-synced copy saved")&&offset.Success&&Math.Abs(Math.Abs(double.Parse(offset.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture))-3)<.3&&File.Exists(Path.Combine(clips,"late.synced.en.srt")),"Late built-in subtitles get a corrected copy · "+late);
        // The player takes the last track of a language as the corrected copy; VLC must list the auto-loaded file after the built-in track.
        LibVLCSharp.Shared.Core.Initialize();using var vlc=new LibVLCSharp.Shared.LibVLC("--quiet","--vout=dummy","--aout=dummy","--avcodec-hw=none");using var player=new LibVLCSharp.Shared.MediaPlayer(vlc);
        using var media=new LibVLCSharp.Shared.Media(vlc,new Uri(Path.Combine(clips,"late.mkv")));player.Play(media);
        for(var n=0;n<100&&player.SpuDescription.Count(t=>t.Id>=0)<2;n++)await Task.Delay(100);
        var tracks=player.SpuDescription.Where(t=>t.Id>=0).ToList();player.Stop();
        // The built-in track keeps its title ("Track 1 - [English]" or similar); the file is listed after it.
        check(tracks.Count==2&&tracks.All(t=>t.Name.Contains("English"))&&tracks[1].Id>tracks[0].Id,"VLC lists the corrected copy after the built-in English track · "+string.Join(" | ",tracks.Select(t=>t.Id+"="+t.Name)));
        }finally{SubtitleDownloads.ToolRoot=previous;}
    }
}
