using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Noir;
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SkipMode { Manual, Auto, Off }
public enum SegmentKind { None, Intro, Recap, Credits }
// Per-profile player choices. Kept outside LibraryState so old phones and PCs never see unknown fields.
public sealed class PlaybackPrefs
{
    public SkipMode SkipIntro { get; set; } = SkipMode.Manual;
    public SkipMode SkipRecap { get; set; } = SkipMode.Manual;
    public SkipMode SkipCredits { get; set; } = SkipMode.Manual;
    public int StillWatchingAfter { get; set; } = 3;
    public double DefaultSpeed { get; set; } = 1;
    public bool VolumeBoost { get; set; }
    public static readonly int[] StillWatchingChoices=[0,2,3,5];
    public static string PathFor(string dataDirectory)=>Path.Combine(dataDirectory,"playback-prefs.json");
    public static PlaybackPrefs Load(string dataDirectory,bool legacyAutoSkipIntro)
    {
        try
        {
            var path=PathFor(dataDirectory);
            if(File.Exists(path)&&JsonSerializer.Deserialize<PlaybackPrefs>(File.ReadAllText(path)) is {} saved) return saved.Normalized();
        }
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){}
        return new PlaybackPrefs{SkipIntro=legacyAutoSkipIntro?SkipMode.Auto:SkipMode.Manual};
    }
    public PlaybackPrefs Normalized()
    {
        if(!StillWatchingChoices.Contains(StillWatchingAfter)) StillWatchingAfter=3;
        DefaultSpeed=ClampSpeed(DefaultSpeed);return this;
    }
    public void Save(string dataDirectory)
    {
        try{var path=PathFor(dataDirectory);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(path+".tmp",path,true);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){}
    }
    public SkipMode ModeFor(SegmentKind kind)=>kind switch{SegmentKind.Intro=>SkipIntro,SegmentKind.Recap=>SkipRecap,SegmentKind.Credits=>SkipCredits,_=>SkipMode.Off};
    public static double ClampSpeed(double speed)=>double.IsFinite(speed)?Math.Clamp(Math.Round(speed*4)/4,.5,2):1;
    public static readonly double[] Speeds=[.5,.75,1,1.25,1.5,1.75,2];
    // The slider stays 0–100; with boost on VLC receives up to 200.
    public static int NativeVolume(double slider,bool boost)=>(int)Math.Round(Math.Clamp(slider,0,100)*(boost?2:1));
    public static string SpeedLabel(double speed)=>speed.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"×";
    public static SegmentKind Classify(string? chapter)
    {
        var name=(chapter??"").Trim();if(name.Length==0) return SegmentKind.None;
        if(Regex.IsMatch(name,@"^(OP|Intro|Opening|Intro credits|Opening credits|Opening theme)$",RegexOptions.IgnoreCase)) return SegmentKind.Intro;
        if(Regex.IsMatch(name,@"^(Recap|Previously( on .*)?|Résumé|Resume|Last time.*)$",RegexOptions.IgnoreCase)) return SegmentKind.Recap;
        if(Regex.IsMatch(name,@"^(ED|ED/Outro|Ending|Ending credits|End credits|Closing credits|Credits|Outro)$",RegexOptions.IgnoreCase)) return SegmentKind.Credits;
        return SegmentKind.None;
    }
}
// Counts episodes that started on their own. Any input resets it; at the limit the player asks first.
public sealed class StillWatchingCounter
{
    public int Count { get; private set; }
    public void UserInput()=>Count=0;
    public bool ShouldAsk(int limit)=>limit>0&&Count>=limit;
    public void AutoAdvanced()=>Count++;
}
