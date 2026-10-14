using System.Net;
using System.Net.Sockets;
using Weymela.Application;

namespace Weymela.Infrastructure.Providers;

public sealed class TikTokLinkResolver(HttpClient client)
{
    public static HttpMessageHandler SecureHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
        MaxConnectionsPerServer = 2, MaxResponseHeadersLength = 16,
        ConnectCallback = async (context, ct) =>
        {
            if (!AllowedHost(context.DnsEndPoint.Host) || context.DnsEndPoint.Port != 443)
                throw new HttpRequestException("Unsupported TikTok redirect host.");
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            foreach (var address in addresses.Where(PublicAddress))
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(address, 443), ct); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); }
            }
            throw new HttpRequestException("TikTok public address unavailable.");
        }
    };

    public async Task<string> ResolveAsync(string value, CancellationToken ct)
    {
        if (!TikTokVideoLink.TryNormalize(value, out var link))
            throw new ApplicationFailure(FailureKind.Validation, "Enter a supported HTTPS TikTok video link.", code: "InvalidTikTokUrl");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            for (var hop = 0; hop < 6; hop++)
            {
                if (TikTokVideoLink.VideoId(link) is not null) return link;
                using var request = new HttpRequestMessage(HttpMethod.Get, link);
                request.Headers.UserAgent.ParseAdd("Weymela-Link-Resolver/3.0");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.Forbidden)
                    throw new ApplicationFailure(FailureKind.Validation, "The TikTok post is inaccessible. Check that it is public.", code: "PostInaccessible");
                if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))
                    throw new ApplicationFailure(FailureKind.Validation,
                        response.IsSuccessStatusCode ? "TikTok did not return a canonical public video link." : "TikTok is temporarily unavailable. Try again later.",
                        code: response.IsSuccessStatusCode ? "PostInaccessible" : "ProviderUnavailable");
                if (response.Headers.Location is not { } location
                    || !Uri.TryCreate(new Uri(link), location, out var next)
                    || !TikTokVideoLink.TryNormalize(next.AbsoluteUri, out link))
                    throw new ApplicationFailure(FailureKind.Validation, "TikTok returned an unsupported redirect. Use the canonical video link.", code: "UnsafeTikTokRedirect");
            }
            throw new ApplicationFailure(FailureKind.Validation, "The TikTok share link has too many redirects. Use the canonical video link.", code: "InvalidTikTokUrl");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        { throw new ApplicationFailure(FailureKind.Validation, "TikTok is temporarily unavailable. Try again later.", error, "ProviderUnavailable"); }
    }

    private static bool AllowedHost(string host) => host is "tiktok.com" or "www.tiktok.com" or "m.tiktok.com" or "vm.tiktok.com" or "vt.tiktok.com";
    internal static bool PublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return (bytes[0] & 0xe0) == 0x20 && !(bytes[0] == 0x20 && bytes[1] == 2)
                && !(bytes[0] == 0x20 && bytes[1] == 1 && (bytes[2] == 0 || bytes[2] == 0x0d && bytes[3] == 0xb8));
        return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224
            && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            && !(bytes[0] == 169 && bytes[1] == 254) && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            && !(bytes[0] == 192 && (bytes[1] == 168 || bytes[1] == 0 || bytes[1] == 88 && bytes[2] == 99))
            && !(bytes[0] == 198 && bytes[1] is 18 or 19)
            && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
            && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
    }
}
