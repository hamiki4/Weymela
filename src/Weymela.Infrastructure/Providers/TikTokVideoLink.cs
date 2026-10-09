using Weymela.Application;

namespace Weymela.Infrastructure.Providers;

/// <summary>
/// Validates TikTok links without resolving or fetching them server-side. A link
/// is a submitted destination, not proof of ownership or publication.
/// </summary>
public static class TikTokVideoLink
{
    private static readonly System.Text.RegularExpressions.Regex CreatorVideoPath = new(
        "^/@(?<creator>[A-Za-z0-9._-]{1,50})/video/(?<id>[0-9]{6,30})/?$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex SharedVideoPath = new(
        "^/(?<id>[A-Za-z0-9_-]{6,80})/?$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static string Normalize(string value)
    {
        if (!TryNormalize(value, out var normalized))
            throw new ApplicationFailure(FailureKind.Validation, "Enter a supported HTTPS TikTok video link.");
        return normalized;
    }

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Port is not (-1 or 443) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        var host = uri.IdnHost.ToLowerInvariant();
        if (host is "www.tiktok.com" or "tiktok.com" or "m.tiktok.com")
        {
            var match = CreatorVideoPath.Match(uri.AbsolutePath);
            if (!match.Success) return false;
            normalized = $"https://www.tiktok.com/@{match.Groups["creator"].Value}/video/{match.Groups["id"].Value}";
            return true;
        }

        if (host is "vm.tiktok.com" or "vt.tiktok.com")
        {
            var match = SharedVideoPath.Match(uri.AbsolutePath);
            if (!match.Success) return false;
            normalized = $"https://{host}/{match.Groups["id"].Value}";
            return true;
        }

        return false;
    }

    public static string? WatchUrl(string? provider, string? reference)
        => string.Equals(provider, "TikTok", StringComparison.OrdinalIgnoreCase)
            && TryNormalize(reference, out var normalized) ? normalized : null;

    public static string? VideoId(string? value)
    {
        if (!TryNormalize(value, out var normalized)) return null;
        var match = CreatorVideoPath.Match(new Uri(normalized).AbsolutePath);
        return match.Success ? match.Groups["id"].Value : null;
    }
}
