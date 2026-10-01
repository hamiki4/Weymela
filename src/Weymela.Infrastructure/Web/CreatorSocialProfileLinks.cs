using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Web;

// A Creator-entered public link is evidence of neither provider connection nor audience verification.
public sealed class CreatorSocialProfileLinks(WeymelaDbContext db, TimeProvider clock)
{
    private const long MaximumAudience = 9_000_000_000_000_000;
    public Task<Guid> SaveAsync(Actor actor, CreatorPlatform platform, string? profileUrl, CancellationToken ct)
        => SaveAsync(actor, platform, profileUrl, 0, ct);
    public async Task<Guid> SaveAsync(Actor actor, CreatorPlatform platform, string? profileUrl, long audience, CancellationToken ct)
    {
        await OwnCreator(actor, ct);
        var url = Normalize(platform, profileUrl);
        ValidateAudience(audience);
        var now = clock.GetUtcNow().UtcDateTime;
        var row = await db.CreatorSocialProfiles
            .Where(x => x.CreatorId == actor.CreatorId && x.Platform == platform)
            .OrderByDescending(x => x.IsActive).ThenByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = new CreatorSocialProfileRecord { CreatorId = actor.CreatorId!.Value, Platform = platform,
                ProfileUrl = url, SelfReportedAudience = audience, CreatedAtUtc = now, UpdatedAtUtc = now,
                VerificationStatus = "SelfReported", AudienceVerificationSource = "SelfReported" };
            db.CreatorSocialProfiles.Add(row);
        }
        else
        {
            var evidenceChanged = row.ProfileUrl != url || row.SelfReportedAudience != audience;
            row.ProfileUrl = url;
            row.IsActive = true;
            row.UpdatedAtUtc = now;
            row.SelfReportedAudience = audience;
            if (evidenceChanged)
            {
                row.VerificationStatus = "SelfReported";
                row.VerifiedAudience = null;
                row.AudienceVerificationSource = "SelfReported";
            }
        }
        Audit(actor, "CreatorSocialProfileLinkSaved", platform, now);
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    public async Task RemoveAsync(Actor actor, CreatorPlatform platform, CancellationToken ct)
    {
        await OwnCreator(actor, ct);
        var row = await db.CreatorSocialProfiles.SingleOrDefaultAsync(x => x.CreatorId == actor.CreatorId
            && x.Platform == platform && x.IsActive, ct);
        if (row is null) return;
        row.IsActive = false;
        row.VerificationStatus = "SelfReported";
        row.AudienceVerificationSource = "SelfReported";
        row.VerifiedAudience = null;
        row.SelfReportedAudience = 0;
        row.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        Audit(actor, "CreatorSocialProfileLinkRemoved", platform, row.UpdatedAtUtc);
        await db.SaveChangesAsync(ct);
    }

    private Task OwnCreator(Actor actor, CancellationToken ct)
        => new CommerceAccessPolicy(db).EnsureCreatorAsync(actor, actor.CreatorId ?? Guid.Empty, ct);

    private void Audit(Actor actor, string action, CreatorPlatform platform, DateTime now)
        => db.AuditEvents.Add(new(Guid.NewGuid(), action, actor.UserId, null, null,
            actor.CreatorId, Guid.NewGuid(), now, platform.ToString()));

    private static void ValidateAudience(long audience)
    {
        if (audience is < 0 or > MaximumAudience)
            throw new ApplicationFailure(FailureKind.Validation, "Enter a whole-number audience count between 0 and 9,000,000,000,000,000.");
    }

    public static string Normalize(CreatorPlatform platform, string? value)
    {
        var raw = value?.Trim();
        if (string.IsNullOrEmpty(raw) || raw.Length > 500 || raw.Contains('%') || raw.Any(char.IsWhiteSpace)
            || raw.Any(char.IsControl) || raw.Contains('\\') || !Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || !uri.IsDefaultPort
            || uri.Fragment.Length != 0 || uri.AbsolutePath.Contains('%'))
            throw Invalid();
        var host = uri.IdnHost.ToLowerInvariant();
        var path = uri.AbsolutePath.TrimEnd('/');
        var query = uri.Query;
        var trackingQuery = platform is CreatorPlatform.TikTok or CreatorPlatform.YouTube
            ? NormalizeTrackingQuery(query)
            : query;
        var valid = platform switch
        {
            CreatorPlatform.TikTok => Host(host, "tiktok.com", "www.tiktok.com", "m.tiktok.com")
                && Regex.IsMatch(path, "^/@[A-Za-z0-9._]{1,24}$") && trackingQuery is not null,
            CreatorPlatform.YouTube => Host(host, "youtube.com", "www.youtube.com", "m.youtube.com")
                && (Regex.IsMatch(path, "^/@[A-Za-z0-9._-]{1,100}$")
                    || Regex.IsMatch(path, "^/(channel/UC[A-Za-z0-9_-]{10,}|c/[A-Za-z0-9._-]+|user/[A-Za-z0-9._-]+)$"))
                && trackingQuery is not null,
            CreatorPlatform.Instagram => Host(host, "instagram.com", "www.instagram.com")
                && Regex.IsMatch(path, "^/[A-Za-z0-9._]{1,30}$")
                && !new[] { "p", "reel", "reels", "stories", "explore", "accounts", "direct" }.Contains(path[1..].ToLowerInvariant())
                && query.Length == 0,
            CreatorPlatform.Facebook => Host(host, "facebook.com", "www.facebook.com", "m.facebook.com")
                && ((Regex.IsMatch(path, "^/[A-Za-z0-9._-]{1,100}$") && path != "/profile.php" && query.Length == 0)
                    || (path == "/profile.php" && Regex.IsMatch(query, "^\\?id=[0-9]{1,30}$"))
                    || (Regex.IsMatch(path, "^/pages/[A-Za-z0-9._-]+/[0-9]{1,30}$") && query.Length == 0))
                && !new[] { "/watch", "/reel", "/stories", "/login", "/groups", "/photo.php",
                    "/permalink.php", "/events", "/marketplace", "/messages", "/settings", "/search", "/share", "/pages" }
                    .Contains(path.ToLowerInvariant()),
            _ => false
        };
        if (!valid) throw Invalid();
        return $"https://{host}{path}{(platform is CreatorPlatform.TikTok or CreatorPlatform.YouTube ? trackingQuery : query)}";
    }

    private static string? NormalizeTrackingQuery(string query)
    {
        if (string.IsNullOrEmpty(query)) return "";
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "_r", "_t", "feature", "si", "app", "src", "share_app_id", "share_link_id", "is_from_webapp", "lang" };
        foreach (var part in query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = part.Split('=', 2)[0];
            if (string.IsNullOrEmpty(key) || !allowed.Contains(Uri.UnescapeDataString(key))) return null;
        }
        return "";
    }

    private static bool Host(string value, params string[] allowed) => allowed.Contains(value, StringComparer.Ordinal);
    private static ApplicationFailure Invalid() => new(FailureKind.Validation,
        "Enter an HTTPS profile link for the selected social platform.");
}
