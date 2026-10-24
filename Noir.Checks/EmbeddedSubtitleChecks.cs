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
    }
}
