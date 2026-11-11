using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Noir;

public sealed class SubtitleTransport
{
    readonly HttpClient http;

    public SubtitleTransport(HttpClient? client = null)
    {
        http = client ?? Client();
    }

    public static bool PublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (IPAddress.IsLoopback(address))
            return false;
        if (b.Length == 4)
            return b[0] != 0
                && b[0] != 10
                && b[0] != 127
                && b[0] < 224
                && !(b[0] == 100 && b[1] is >= 64 and <= 127)
                && !(b[0] == 169 && b[1] == 254)
                && !(b[0] == 172 && b[1] is >= 16 and <= 31)
                && !(b[0] == 192 && b[1] == 168);
        return !address.IsIPv6LinkLocal
            && !address.IsIPv6Multicast
            && !address.IsIPv6SiteLocal
            && b[0] is >= 0x20 and <= 0x3f;
    }

    public static bool ValidUrl(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var u)
        && u.Scheme == "https"
        && u.Port == 443
        && u.UserInfo.Length == 0
        && u.Fragment.Length == 0
        && !u.IsLoopback
        && (!IPAddress.TryParse(u.DnsSafeHost.Trim('[', ']'), out var ip) || PublicAddress(ip));

    public static async Task<Stream> Connect(
        SocketsHttpConnectionContext context,
        CancellationToken ct,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null
    ) => await Connect(context.DnsEndPoint, ct, resolve);

    public static async Task<Stream> Connect(
        DnsEndPoint endpoint,
        CancellationToken ct,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
        Func<IPAddress, int, CancellationToken, Task<Stream>>? connect = null
    )
    {
        var addresses = await (resolve ?? ((host, token) => Dns.GetHostAddressesAsync(host, token)))(endpoint.Host, ct);
        if (addresses.Length == 0 || addresses.Any(a => !PublicAddress(a)))
            throw new IOException("Subtitle connection refused.");
        if (connect != null)
            return await connect(addresses[0], endpoint.Port, ct);
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), ct);
                return new NetworkStream(socket, true);
            }
            catch
            {
                socket.Dispose();
                if (ct.IsCancellationRequested)
                    throw;
            }
        }
        throw new IOException("Subtitle connection unavailable.");
    }

    public static HttpClient Client() =>
        new(
            new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ConnectCallback = (context, ct) => new(Connect(context, ct)),
            }
        )
        {
            Timeout = TimeSpan.FromSeconds(35),
        };

    public async Task<byte[]> Send(
        SubtitleSelection source,
        string url,
        bool credentialed = false,
        HttpMethod? method = null,
        object? body = null,
        int cap = 4 * 1024 * 1024,
        bool empty404 = false,
        string token = "",
        bool json = true,
        CancellationToken ct = default
    )
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(35));
        try
        {
            var original = new Uri(url);
            for (int hop = 0; hop <= 3; hop++)
            {
                if (!ValidUrl(url))
                    throw new IOException();
                var uri = new Uri(url);
                if (
                    credentialed
                    && (!OwnHost(source, uri) || !uri.Host.Equals(original.Host, StringComparison.OrdinalIgnoreCase))
                )
                    throw new IOException();
                using var request = new HttpRequestMessage(method ?? HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation(
                    "User-Agent",
                    "Noir/" + (typeof(SubtitleTransport).Assembly.GetName().Version?.ToString() ?? "1.0")
                );
                if (json)
                    request.Headers.TryAddWithoutValidation("Accept", "application/json");
                if (credentialed)
                {
                    switch (source.Source)
                    {
                        case "opensubtitles":
                            request.Headers.TryAddWithoutValidation("Api-Key", source.ApiKey);
                            if (token.Length > 0)
                                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                            break;
                        case "subsource":
                            request.Headers.TryAddWithoutValidation("X-API-Key", source.ApiKey);
                            break;
                        case "addon":
                            if (source.AddonAuth.Length > 0)
                                request.Headers.TryAddWithoutValidation("Authorization", source.AddonAuth);
                            break;
                    }
                }
                if (body != null)
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(body),
                        Encoding.UTF8,
                        "application/json"
                    );
                using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    budget.Token
                );
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (hop == 3 || response.Headers.Location == null)
                        throw new IOException();
                    url = new Uri(uri, response.Headers.Location).AbsoluteUri;
                    if (credentialed && !new Uri(url).Host.Equals(original.Host, StringComparison.OrdinalIgnoreCase))
                        throw new IOException();
                    continue;
                }
                if (empty404 && response.StatusCode == HttpStatusCode.NotFound)
                    return "[]"u8.ToArray();
                var bytes = await Read(response.Content, cap, budget.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    var kind =
                        status is 401 or 403 ? SubtitleFailureKind.BadKey
                        : status is 402 or 406 or 429 ? SubtitleFailureKind.Limit
                        : SubtitleFailureKind.Unavailable;
                    string reset = "";
                    if (kind == SubtitleFailureKind.Limit)
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(bytes);
                            foreach (var field in new[] { "reset_time", "reset_at" })
                                if (
                                    doc.RootElement.ValueKind == JsonValueKind.Object
                                    && doc.RootElement.TryGetProperty(field, out var value)
                                    && DateTimeOffset.TryParse(value.ToString(), out var time)
                                )
                                {
                                    reset = time.ToUniversalTime().ToString("u");
                                    break;
                                }
                        }
                        catch (JsonException) { }
                        if (reset.Length == 0)
                        {
                            if (response.Headers.RetryAfter?.Delta is { } delay)
                                reset = DateTimeOffset.UtcNow.Add(delay).ToString("u");
                            else if (response.Headers.RetryAfter?.Date is { } date)
                                reset = date.ToString("u");
                            else if (
                                response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
                                && long.TryParse(values.FirstOrDefault(), out var epoch)
                                && epoch is > 0 and < 253402300800
                            )
                                reset = DateTimeOffset.FromUnixTimeSeconds(epoch).ToString("u");
                        }
                    }
                    throw new SubtitleSourceException(source, kind, reset, status);
                }
                return bytes;
            }
            throw new IOException();
        }
        catch (SubtitleSourceException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
            when (e
                    is HttpRequestException
                        or IOException
                        or InvalidDataException
                        or OperationCanceledException
                        or UriFormatException
                        or ArgumentException
                        or InvalidOperationException
            )
        {
            throw new SubtitleSourceException(source, SubtitleFailureKind.Unavailable);
        }
    }

    static bool OwnHost(SubtitleSelection source, Uri uri) =>
        source.Source switch
        {
            "opensubtitles" => uri.Host is "api.opensubtitles.com" or "vip-api.opensubtitles.com",
            "subdl" => uri.Host == "api.subdl.com",
            "subsource" => uri.Host == "api.subsource.net",
            "wyzie" => uri.Host == "sub.wyzie.io",
            "addon" => uri.GetLeftPart(UriPartial.Authority)
                == new Uri(source.AddonUrl).GetLeftPart(UriPartial.Authority),
            _ => false,
        };

    public static async Task<byte[]> Read(HttpContent content, int cap, CancellationToken ct)
    {
        if (content.Headers.ContentLength > cap)
            throw new IOException();
        using var stream = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while (
            (
                count = await stream.ReadAsync(
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, cap - output.Length + 1)),
                    ct
                )
            ) > 0
        )
        {
            if (output.Length + count > cap)
                throw new IOException();
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
