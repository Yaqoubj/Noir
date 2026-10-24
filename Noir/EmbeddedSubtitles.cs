using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public static class EmbeddedSubtitles
{
    static bool Partial(string value)=>Regex.IsMatch(value,@"(?i)(?:^|[^a-z])(forced|signs?|songs?|karaoke)(?:[^a-z]|$)");
    public static string Language(string language,string title,bool forced=false)
    {
        if(forced||Partial(title))return "";
        if(!string.IsNullOrWhiteSpace(language)&&!language.Trim().Equals("und",StringComparison.OrdinalIgnoreCase))return SubtitleDownloads.Language(language.Trim());
        var match=Regex.Match(title,@"(?i)(?:^|[^a-z])(eng|en|english|ara|ar|arabic)(?:[^a-z]|$)");return SubtitleDownloads.Language(match.Groups[1].Value);
    }
    public static HashSet<string> Parse(string json)
    {
        var result=new HashSet<string>();
        try{
            using var doc=JsonDocument.Parse(json);
            if(doc.RootElement.ValueKind!=JsonValueKind.Object||!doc.RootElement.TryGetProperty("streams",out var streams)||streams.ValueKind!=JsonValueKind.Array)return result;
            foreach(var stream in streams.EnumerateArray()){
                static string Text(JsonElement value,string key)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(key,out var text)&&text.ValueKind==JsonValueKind.String?text.GetString()??"":"";
                if(Text(stream,"codec_type")!="subtitle")continue;
                stream.TryGetProperty("tags",out var tags);stream.TryGetProperty("disposition",out var disposition);
                var forced=disposition.ValueKind==JsonValueKind.Object&&disposition.EnumerateObject().Any(p=>Partial(p.Name)&&(p.Value.ValueKind==JsonValueKind.True||p.Value.ValueKind==JsonValueKind.Number&&p.Value.TryGetInt32(out var flag)&&flag==1));
                var language=Language(Text(tags,"language"),Text(tags,"title"),forced);if(language.Length>0)result.Add(language);
            }
        }catch(JsonException){return [];}
        return result;
    }
    public static async Task<HashSet<string>> Probe(string video,CancellationToken ct)
    {
        var tool=Path.Combine(AppContext.BaseDirectory,"Tools","subtitle-sync","ffmpeg","ffprobe.exe");if(!File.Exists(tool))return [];
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var start=new ProcessStartInfo(tool){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in new[]{"-v","error","-show_streams","-of","json",video})start.ArgumentList.Add(argument);
        using var process=new Process{StartInfo=start};
        try{
            process.Start();var stdout=process.StandardOutput.ReadToEndAsync(timeout.Token);var stderr=process.StandardError.ReadToEndAsync(timeout.Token);
            try{await process.WaitForExitAsync(timeout.Token);var json=await stdout;await stderr;return process.ExitCode==0?Parse(json):[];}
            finally{if(!process.HasExited)process.Kill(true);try{await Task.WhenAll(stdout,stderr);}catch(OperationCanceledException){}catch(IOException){}}
        }catch(OperationCanceledException)when(!ct.IsCancellationRequested){return [];}
        catch(Exception e)when(e is IOException or JsonException or System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException){return [];}
    }
}
