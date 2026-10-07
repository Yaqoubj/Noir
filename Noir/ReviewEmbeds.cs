using System.Net;
using System.Text.RegularExpressions;

namespace Noir;

public static class RedditDiscussion
{
    public static string? Reddit(string? url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var u)||u.Scheme!="https"||u.UserInfo.Length>0||!u.IsDefaultPort) return null;
        if(u.Host is not ("reddit.com" or "www.reddit.com" or "old.reddit.com" or "new.reddit.com")) return null;
        if(!Regex.IsMatch(u.AbsolutePath,@"^/r/[A-Za-z0-9_]+/comments/[a-z0-9]+(?:/[A-Za-z0-9_-]+(?:/[a-z0-9]+)?)?/?$")) return null;
        return "https://www.reddit.com"+u.AbsolutePath.TrimEnd('/')+"/";
    }
}
