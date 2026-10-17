using System.Text.RegularExpressions;
using Weymela.Application;

namespace Weymela.Infrastructure.Providers;

// Parsing proves only that a link has a supported shape. Public access, ownership,
// approved-content identity and views require independent provider/reviewer evidence.
public sealed record SocialVideoReference(string Provider, string ContentId, string Url, bool IsShareLink = false);

public static class SocialVideoLink
{
    private static bool Match(string value, string pattern) => Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant);

    public static SocialVideoReference Parse(string provider, string value)
    {
        if (!Enum.TryParse<Weymela.Domain.CreatorPlatform>(provider, true, out var platform) || !Enum.IsDefined(platform)
            || string.IsNullOrWhiteSpace(value) || value.Length > 1000
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo != "" || uri.Fragment != "")
            throw Invalid();
        provider = platform.ToString();
        var host = uri.IdnHost.ToLowerInvariant();
        var path = uri.AbsolutePath.Trim('/');
        var parts = path.Split('/');
        string? id = null;
        string? url = null;
        var share = false;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2)).Where(x => x.Length == 2)
            .GroupBy(x => x[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count() == 1 ? Uri.UnescapeDataString(x.Single()[1]) : "", StringComparer.OrdinalIgnoreCase);
        switch (platform)
        {
            case Weymela.Domain.CreatorPlatform.TikTok:
                if (TikTokVideoLink.TryNormalize(value, out var tikTok))
                {
                    id = TikTokVideoLink.VideoId(tikTok);
                    share = id is null;
                    id ??= new Uri(tikTok).AbsolutePath.Trim('/').Split('/').Last();
                    url = tikTok;
                }
                break;
            case Weymela.Domain.CreatorPlatform.YouTube:
                if (host == "youtu.be" && parts.Length == 1) id = parts[0];
                else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com")
                {
                    if (path == "watch") id = query.GetValueOrDefault("v");
                    else if (parts.Length == 2 && parts[0] is "shorts" or "embed" or "live" or "v") id = parts[1];
                }
                if (id is not null && Match(id, "^[A-Za-z0-9_-]{11}$")) url = "https://www.youtube.com/watch?v=" + id;
                break;
            case Weymela.Domain.CreatorPlatform.Instagram:
                if (host is "instagram.com" or "www.instagram.com" or "m.instagram.com")
                {
                    if (parts.Length == 2 && parts[0] is "p" or "reel" or "reels" or "tv"
                        && Match(parts[1], "^[A-Za-z0-9_-]{5,100}$"))
                    { id = parts[1]; url = $"https://www.instagram.com/{(parts[0] == "reels" ? "reel" : parts[0])}/{id}/"; }
                    else if (parts.Length == 3 && parts[0] == "share" && parts[1] is "reel" or "p"
                        && Match(parts[2], "^[A-Za-z0-9_-]{5,80}$"))
                    { id = parts[2]; url = $"https://www.instagram.com/share/{parts[1]}/{id}/"; share = true; }
                }
                break;
            case Weymela.Domain.CreatorPlatform.Facebook:
                if (host is "facebook.com" or "www.facebook.com" or "m.facebook.com" or "mbasic.facebook.com")
                {
                    if (parts.Length == 2 && parts[0] == "reel") id = parts[1];
                    else if (path is "watch" or "video.php") id = query.GetValueOrDefault("v");
                    else if (parts.Length == 3 && parts[1] == "videos" && Match(parts[0], "^[A-Za-z0-9._-]{1,100}$")) id = parts[2];
                    else if (parts.Length == 3 && parts[0] == "share" && parts[1] is "v" or "r"
                        && Match(parts[2], "^[A-Za-z0-9_-]{5,80}$"))
                    { id = parts[2]; url = $"https://www.facebook.com/share/{parts[1]}/{id}/"; share = true; }
                    if (!share && id is not null && Match(id, "^[0-9]{5,40}$")) url = "https://www.facebook.com/watch/?v=" + id;
                }
                else if (host == "fb.watch" && parts.Length == 1 && Match(parts[0], "^[A-Za-z0-9_-]{5,80}$"))
                { id = parts[0]; url = $"https://fb.watch/{id}/"; share = true; }
                break;
        }
        return url is not null && id is not null ? new(provider, id, url, share) : throw Invalid();
    }

    private static ApplicationFailure Invalid() => new(FailureKind.Validation, "Enter a supported public video link for the selected platform.", code: "InvalidPublicationUrl");
}
