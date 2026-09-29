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
    public async Task<Guid> SaveAsync(Actor actor, CreatorPlatform platform, string? profileUrl, CancellationToken ct)
    {
        await OwnCreator(actor, ct);
        var url = Normalize(platform, profileUrl);
        var now = clock.GetUtcNow().UtcDateTime;
        var row = await db.CreatorSocialProfiles
            .Where(x => x.CreatorId == actor.CreatorId && x.Platform == platform)
            .OrderByDescending(x => x.IsActive).ThenByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = new CreatorSocialProfileRecord { CreatorId = actor.CreatorId!.Value, Platform = platform,
                ProfileUrl = url, CreatedAtUtc = now, UpdatedAtUtc = now, VerificationStatus = "SelfReported" };
            db.CreatorSocialProfiles.Add(row);
        }
        else
        {
            row.ProfileUrl = url;
            row.IsActive = true;
            row.UpdatedAtUtc = now;
            row.SelfReportedAudience = 0;
            row.VerificationStatus = "SelfReported";
            row.VerifiedAudience = null;
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
        var valid = platform switch
        {
            CreatorPlatform.TikTok => Host(host, "tiktok.com", "www.tiktok.com", "m.tiktok.com")
                && Regex.IsMatch(path, "^/@[A-Za-z0-9._]{1,24}$") && query.Length == 0,
            CreatorPlatform.YouTube => Host(host, "youtube.com", "www.youtube.com", "m.youtube.com")
                && (Regex.IsMatch(path, "^/@[A-Za-z0-9._-]{1,100}$")
                    || Regex.IsMatch(path, "^/(channel/UC[A-Za-z0-9_-]{10,}|c/[A-Za-z0-9._-]+|user/[A-Za-z0-9._-]+)$"))
                && query.Length == 0,
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
        return $"https://{host}{path}{query}";
    }

    private static bool Host(string value, params string[] allowed) => allowed.Contains(value, StringComparer.Ordinal);
    private static ApplicationFailure Invalid() => new(FailureKind.Validation,
        "Enter an HTTPS profile link for the selected social platform.");
}
