using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

public sealed record TrustedWorkspaceIdentity(Actor Actor, string DisplayName, string PublicId, Guid BindingId, long BindingVersion, DateTime AuthenticatedAtUtc, DateTime ExpiresAtUtc);
public sealed class TrustedIdentityService(WeymelaDbContext db, IIdentityTokenVerifier verifier, IWorkspaceDirectory directory)
{
    public async Task<TrustedWorkspaceIdentity> SignInAsync(string sensitiveIdToken, CancellationToken ct)
    {
        var verified = await verifier.VerifyAsync(sensitiveIdToken, ct);
        var binding = await db.IdentityBindings.AsNoTracking().SingleOrDefaultAsync(x => x.Provider == verified.Provider
            && x.ProjectId == verified.ProjectId && x.ExternalSubject == verified.Subject && x.IsActive, ct);
        if (binding is null || verified.AuthenticatedAtUtc < binding.ValidAfterUtc) throw Denied();
        var memberships = await db.CommercePermissions.AsNoTracking().Where(x => x.UserId == binding.UserId && x.IsActive).Take(2).ToListAsync(ct);
        // Multiple memberships need an explicitly designed server-side workspace switch, not a client role claim.
        if (memberships.Count != 1) throw Denied();
        var p = memberships[0];
        var actor = ActorFrom(p);
        var name = "Weymela Admin"; var publicId = "Admin";
        switch (p.Role)
        {
            case ActorRole.Business: var b = await directory.BusinessCardAsync(p.SubjectId, ct); name = b.DisplayName; publicId = (await db.PublicWorkspaceProfiles.SingleAsync(x => x.SubjectId == p.SubjectId && x.Role == p.Role, ct)).PublicId; break;
            case ActorRole.Creator: var c = await directory.CreatorCardAsync(p.SubjectId, ct); name = c.DisplayName; publicId = c.PublicId; break;
            case ActorRole.Customer: var u = await directory.CustomerCardAsync(p.SubjectId, ct); name = u.DisplayName; publicId = u.PublicId; break;
            case ActorRole.Cashier: name = "Cashier"; publicId = "Checkout"; break;
        }
        return new(actor, name, publicId, binding.Id, binding.Version, verified.AuthenticatedAtUtc, verified.ExpiresAtUtc);
    }
    public static Actor ActorFrom(CommercePermission permission)
    {
        if (permission.Role == ActorRole.Business && permission.BusinessId != permission.SubjectId
            || permission.Role == ActorRole.Cashier && permission.BusinessId is null) throw Denied();
        return new(permission.UserId, permission.Role,
            permission.Role == ActorRole.Business ? permission.SubjectId : permission.Role == ActorRole.Cashier ? permission.BusinessId : null,
            permission.Role == ActorRole.Creator ? permission.SubjectId : null, permission.Role == ActorRole.Customer ? permission.SubjectId : null);
    }
    private static ApplicationFailure Denied() => new(FailureKind.Forbidden, "No authorized active workspace is available for this identity.");
}

public sealed class PersistentWorkspaceDirectory(WeymelaDbContext db) : IWorkspaceDirectory
{
    private async Task<PublicWorkspaceProfile> Profile(Guid id, ActorRole role, CancellationToken ct) =>
        await db.PublicWorkspaceProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.SubjectId == id && x.Role == role, ct)
        ?? throw new ApplicationFailure(FailureKind.NotFound, "The public workspace profile is unavailable.");
    public async Task<BusinessCard> BusinessCardAsync(Guid id, CancellationToken ct)
    { var p = await Profile(id, ActorRole.Business, ct); return new(id, p.DisplayName, p.Region, SafeUrl(p.DirectionsUrl)); }
    public async Task<CreatorCard> CreatorCardAsync(Guid id, CancellationToken ct)
    { var p = await Profile(id, ActorRole.Creator, ct); return new(id, p.DisplayName, p.PublicId, p.Region, p.Category, p.VerifiedFollowers, p.VerifiedViews, p.SocialVerified, SafeUrl(p.PortfolioUrl)); }
    public async Task<CustomerCard> CustomerCardAsync(Guid id, CancellationToken ct)
    { var p = await Profile(id, ActorRole.Customer, ct); return new(id, p.DisplayName, p.PublicId); }
    public async Task<PublicBusiness> BusinessAsync(Guid id, CancellationToken ct)
    { var p = await Profile(id, ActorRole.Business, ct); return new(id, p.DisplayName); }
    public async Task<PublicCreator> CreatorAsync(Guid id, CancellationToken ct)
    { var p = await Profile(id, ActorRole.Creator, ct); return new(id, p.PublicId, p.DisplayName); }
    public static string? SafeUrl(string? value)
    {
        if (value is null || value.Length > 500 || !Uri.TryCreate(value, UriKind.Absolute, out var u) || u.Scheme != "https" || !string.IsNullOrEmpty(u.UserInfo)) return null;
        return u.Host is "www.tiktok.com" or "www.youtube.com" or "youtu.be" or "www.instagram.com" or "www.google.com" or "maps.google.com" ? value : null;
    }
}
