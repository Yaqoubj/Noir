using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Noir;
public static class SubtitleAddon
{
    public const string Default="https://opensubtitles-v3.strem.io";
    public static string Normalize(string text)
    {
        text=text.Trim();if(text.Length==0)return "";if(text.StartsWith("stremio://",StringComparison.OrdinalIgnoreCase))text="https://"+text[10..];
        if(!Uri.TryCreate(text,UriKind.Absolute,out var u)||u.Scheme!="https"||u.Host.Length==0||u.UserInfo.Length>0||u.Port!=443||u.Query.Length>0||u.Fragment.Length>0||u.IsLoopback)throw new IOException("Paste an HTTPS Stremio subtitle addon link without credentials or a custom port.");
        var path=u.AbsolutePath.TrimEnd('/');if(path.EndsWith("/manifest.json",StringComparison.OrdinalIgnoreCase))path=path[..^14];
        return u.GetLeftPart(UriPartial.Authority)+path;
    }
    public static List<string> Sources(string link,bool only)
    {
        var baseUrl=Normalize(link);if(baseUrl.Length==0)return [Default];return only||baseUrl==Default?[baseUrl]:[baseUrl,Default];
    }
    public static string ManifestName(byte[] bytes)
    {
        using var doc=JsonDocument.Parse(bytes);var root=doc.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("resources",out var resources)||resources.ValueKind!=JsonValueKind.Array||!resources.EnumerateArray().Any(r=>r.ValueKind==JsonValueKind.String?r.GetString()=="subtitles":r.ValueKind==JsonValueKind.Object&&r.TryGetProperty("name",out var n)&&n.ValueKind==JsonValueKind.String&&n.GetString()=="subtitles"))throw new IOException("This addon does not provide subtitles.");
        var name=root.TryGetProperty("name",out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
        return name.Length==0?"Subtitle addon":name[..Math.Min(120,name.Length)].Replace('\r',' ').Replace('\n',' ');
    }
    public static async Task<string> Check(string link,CancellationToken ct,HttpClient? client=null)
    {
        var baseUrl=Normalize(link);if(baseUrl.Length==0)throw new IOException("Paste a subtitle addon link first.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var owned=client==null?new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}):null;var http=client??owned!;
        try{
            using var response=await http.GetAsync(baseUrl+"/manifest.json",HttpCompletionOption.ResponseHeadersRead,timeout.Token);response.EnsureSuccessStatusCode();
            const int max=256*1024;if(response.Content.Headers.ContentLength>max)throw new IOException("Subtitle addon manifest is too large.");
            await using var input=await response.Content.ReadAsStreamAsync(timeout.Token);using var output=new MemoryStream();var buffer=new byte[8192];int n;
            while((n=await input.ReadAsync(buffer,timeout.Token))>0){if(output.Length+n>max)throw new IOException("Subtitle addon manifest is too large.");output.Write(buffer,0,n);}return ManifestName(output.ToArray());
        }catch(HttpRequestException){throw new IOException("Couldn't check the subtitle addon. Check the link and connection.");}
        catch(JsonException){throw new IOException("The subtitle addon returned an invalid manifest.");}
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new IOException("Subtitle addon check timed out. Try again.");}
    }
}
