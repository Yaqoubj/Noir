using System.IO;

namespace Noir;

public sealed record SubtitleSelection(
    string Source = "",
    string ApiKey = "",
    string Username = "",
    string Password = "",
    string AddonUrl = "",
    string AddonAuth = "",
    bool Only = true,
    string Uploader = ""
)
{
    public bool Chosen => Source.Length > 0;

    // Names show up in result lines and the phone's settings: one tidy line, no control characters.
    public static string CleanUploader(string? value)
    {
        var clean = new string((value ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > 80 ? clean[..80].Trim() : clean;
    }

    public string Name =>
        Source switch
        {
            "opensubtitles" => "OpenSubtitles.com",
            "subdl" => "SubDL",
            "subsource" => "SubSource",
            "wyzie" => "Wyzie Subs",
            "addon" => SubtitleWords.T("Stremio addon"),
            _ => SubtitleWords.T("Free default"),
        };

    public override string ToString() => Name;

    public static implicit operator SubtitleSelection((string Url, bool Only) old) =>
        new(old.Url.Length > 0 ? "addon" : "", AddonUrl: old.Url, Only: old.Only);

    public static SubtitleSelection From(DownloadCredentials c) =>
        new(
            c.SubtitleSource.Length == 0 && c.SubtitleAddonUrl.Length > 0 ? "addon" : c.SubtitleSource,
            c.SubtitleApiKey,
            c.SubtitleUsername,
            c.SubtitlePassword,
            c.SubtitleAddonUrl,
            c.SubtitleAddonAuth,
            c.SubtitleAddonOnly,
            CleanUploader(c.SubtitleUploader)
        );

    public SubtitleSelection Validate()
    {
        if (Source is not ("" or "opensubtitles" or "subdl" or "subsource" or "wyzie" or "addon"))
            throw new IOException("Choose a subtitle source.");
        if (!Chosen)
            return new(Only: Only);
        if (Source == "addon" && string.IsNullOrWhiteSpace(AddonUrl))
            throw new IOException("Paste a subtitle addon link first.");
        if (Source != "addon" && string.IsNullOrWhiteSpace(ApiKey))
            throw new IOException("Enter the subtitle site API key.");
        if (Source == "opensubtitles" && (string.IsNullOrWhiteSpace(Username) != string.IsNullOrWhiteSpace(Password)))
            throw new IOException("Enter both subtitle username and password, or leave both empty.");
        var secrets =
            Source == "addon" ? new[] { AddonAuth }
            : Source == "opensubtitles" ? new[] { ApiKey, Username, Password }
            : new[] { ApiKey };
        if (secrets.Any(s => s.Any(char.IsControl)))
            throw new IOException("Subtitle credentials contain invalid characters.");
        return this with
        {
            ApiKey = Source == "addon" ? "" : ApiKey.Trim(),
            Username = Source == "opensubtitles" ? Username.Trim() : "",
            Password = Source == "opensubtitles" && !string.IsNullOrWhiteSpace(Password) ? Password : "",
            AddonUrl = Source == "addon" ? SubtitleAddon.Normalize(AddonUrl) : "",
            AddonAuth = Source == "addon" ? AddonAuth : "",
            Uploader = CleanUploader(Uploader),
        };
    }
}

public enum SubtitleFailureKind
{
    BadKey,
    Limit,
    Unavailable,
}

public sealed class SubtitleSourceException : IOException
{
    public SubtitleFailureKind Kind { get; }
    public string SourceName { get; }
    public string Reset { get; }
    public int Status { get; }

    public SubtitleSourceException(
        SubtitleSelection source,
        SubtitleFailureKind kind,
        string reset = "",
        int status = 0
    )
        : base(
            source.Name
                + " · "
                + SubtitleWords.T(
                    kind == SubtitleFailureKind.BadKey ? "key rejected"
                    : kind == SubtitleFailureKind.Limit ? "daily limit reached"
                    : "temporarily unavailable"
                )
                + (reset.Length > 0 ? " · " + SubtitleWords.T("resets") + " " + reset : "")
        )
    {
        Kind = kind;
        SourceName = source.Name;
        Reset = reset;
        Status = status;
    }
}

static class SubtitleWords
{
    public static string T(string text)
    {
#if NOIR_HEADLESS
        return text;
#else
        return Loc.T(text);
#endif
    }
}
