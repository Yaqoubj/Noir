using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noir;

public sealed class DirectDownloadSource
{
    public string Url {get;set;}="";
    public Dictionary<string,string> Headers {get;set;}=new(StringComparer.OrdinalIgnoreCase);
    public string Name {get;set;}="";
    public long Size {get;set;}
    public int Season {get;set;}
    public int Episode {get;set;}
    public string ServerId {get;set;}="";
    public string Resolution {get;set;}="";
    public string Language {get;set;}="Unknown";
}
public static class DownloadSecurity
{
    public static bool ValidUrl(string value,string? serverBase=null)
        =>Uri.TryCreate(value,UriKind.Absolute,out var u)&&u.UserInfo.Length==0&&(u.Scheme=="https"||u.Scheme=="http"&&Uri.TryCreate(serverBase,UriKind.Absolute,out var b)&&b.UserInfo.Length==0&&b.Host.Equals(u.Host,StringComparison.OrdinalIgnoreCase)&&b.Port==u.Port);
    public static void ValidateHeaders(IReadOnlyDictionary<string,string> headers)
    {
        foreach(var h in headers)if(!Regex.IsMatch(h.Key,@"\A[A-Za-z0-9!#$%&'*+.^_`|~-]+\z")||h.Value.Contains('\r')||h.Value.Contains('\n')||h.Key.Equals("Host",StringComparison.OrdinalIgnoreCase)||h.Key.Equals("Range",StringComparison.OrdinalIgnoreCase)||h.Key.Equals("If-Range",StringComparison.OrdinalIgnoreCase)||h.Key.Equals("Content-Length",StringComparison.OrdinalIgnoreCase))throw new IOException("The source returned invalid download headers.");
    }
    public static string Protect(DirectDownloadSource source)=>Convert.ToBase64String(ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(source),null,DataProtectionScope.CurrentUser));
    public static IEnumerable<string> SourceSecrets(DirectDownloadSource source)
    {
        yield return source.Url;
        if(Uri.TryCreate(source.Url,UriKind.Absolute,out var uri))foreach(var part in uri.AbsolutePath.Split('/').Concat(uri.Query.TrimStart('?').Split('&').Select(q=>q[(q.IndexOf('=')+1)..])))if(part.Length>=12)yield return Uri.UnescapeDataString(part);
        foreach(var h in source.Headers.Where(h=>new[]{"authorization","cookie","token","key"}.Any(k=>h.Key.Contains(k,StringComparison.OrdinalIgnoreCase)))){
            yield return h.Value;
            if(h.Key.Equals("Authorization",StringComparison.OrdinalIgnoreCase)&&h.Value.IndexOf(' ') is var space&&space>=0)yield return h.Value[(space+1)..];
            if(h.Key.Equals("Cookie",StringComparison.OrdinalIgnoreCase))foreach(var cookie in h.Value.Split(';'))if(cookie.IndexOf('=') is var equals&&equals>=0)yield return cookie[(equals+1)..].Trim();
        }
    }
    public static DirectDownloadSource Unprotect(string value)
    {
        try{return JsonSerializer.Deserialize<DirectDownloadSource>(ProtectedData.Unprotect(Convert.FromBase64String(value),null,DataProtectionScope.CurrentUser))??throw new IOException("The saved download source is unavailable. Search again.");}
        catch(Exception e)when(e is CryptographicException or FormatException or JsonException){throw new IOException("The saved download source cannot be opened by this Windows account. Search again.");}
    }
    public static string SafeText(string value,DownloadCredentials config,IEnumerable<string>? secrets=null)
    {
        value=Regex.Replace(value,@"(?i)https?://[^\s\]<>""']+","[link]");
        var auth=config.SubtitleAddonAuth;var subtitleSecrets=new[]{config.SubtitleApiKey,config.SubtitlePassword,auth,auth.Contains(' ')?auth[(auth.IndexOf(' ')+1)..]:""}.Concat(config.SubtitleSiteKeys.Values).SelectMany(s=>new[]{s,Uri.EscapeDataString(s)});
        foreach(var secret in new[]{config.Key,config.TorBoxKey,config.RealDebridKey,config.Manifest,config.OriginalManifest,config.SubtitleAddonUrl}.Concat(subtitleSecrets).Concat(config.Servers.Select(s=>s.Token)).Concat(secrets??[]).Where(s=>s.Length>0).OrderByDescending(s=>s.Length))value=value.Replace(secret,"[private]",StringComparison.Ordinal);
        return value[..Math.Min(1500,value.Length)];
    }
    public static HttpClient Client(TimeSpan timeout)=>new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=timeout};
}
