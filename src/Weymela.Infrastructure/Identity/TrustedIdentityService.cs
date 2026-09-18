using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

public sealed record TrustedWorkspaceProfile(string Key, Actor Actor, string DisplayName, string PublicId, bool CanCheckout);
public sealed class ProfileSelectionRequiredException(IReadOnlyList<TrustedWorkspaceProfile> profiles)
    : Exception("An approved profile must be selected before opening a workspace.")
{
    public IReadOnlyList<TrustedWorkspaceProfile> Profiles { get; } = profiles;
}
public sealed record ProfileSelection(ActorRole Role, Guid SubjectId, Guid? BusinessId);
public sealed record TrustedWorkspaceIdentity(Actor Actor, string DisplayName, string PublicId, Guid BindingId, long BindingVersion,
    DateTime AuthenticatedAtUtc, DateTime ExpiresAtUtc, IReadOnlyList<TrustedWorkspaceProfile> Profiles);
public sealed class TrustedIdentityService(WeymelaDbContext db, IIdentityTokenVerifier verifier, IWorkspaceDirectory directory)
{
    public Task<TrustedWorkspaceIdentity> SignInAsync(string sensitiveIdToken, CancellationToken ct)
        => SignInAsync(sensitiveIdToken, null, ct);

    public async Task<TrustedWorkspaceIdentity> SignInAsync(string sensitiveIdToken, ProfileSelection? selection, CancellationToken ct)
    {
        var verified = await verifier.VerifyAsync(sensitiveIdToken, ct);
        var binding = await db.IdentityBindings.AsNoTracking().SingleOrDefaultAsync(x => x.Provider == verified.Provider
            && x.ProjectId == verified.ProjectId && x.ExternalSubject == verified.Subject && x.IsActive, ct);
        if (binding is null || verified.AuthenticatedAtUtc < binding.ValidAfterUtc) throw Denied();
        var profiles = await ProfilesForUserAsync(binding.UserId, ct);
        // A verified account may exist before its first approved commerce profile.
        // Give it a restricted onboarding context; all commerce policies still require
        // an active CommercePermission and therefore cannot be entered with this actor.
        if (profiles.Count == 0)
        {
            if (selection is not null) throw Denied();
            return new(new Actor(binding.UserId, ActorRole.Customer, CustomerId: Guid.Empty),
                "Account setup", "", binding.Id, binding.Version, verified.AuthenticatedAtUtc, verified.ExpiresAtUtc, profiles);
        }
        var selected = selection is null
            ? profiles.Count == 1 ? profiles[0] : throw new ProfileSelectionRequiredException(profiles)
            : profiles.SingleOrDefault(x => x.Actor.Role == selection.Role && SubjectId(x.Actor) == selection.SubjectId
                && x.Actor.BusinessId == selection.BusinessId);
        if (selected is null) throw Denied();
        return new(selected.Actor, selected.DisplayName, selected.PublicId, binding.Id, binding.Version,
            verified.AuthenticatedAtUtc, verified.ExpiresAtUtc, profiles);
    }

    public async Task<IReadOnlyList<TrustedWorkspaceProfile>> ProfilesForUserAsync(Guid userId, CancellationToken ct)
    {
        var memberships = await db.CommercePermissions.AsNoTracking().Where(x => x.UserId == userId && x.IsActive)
            .OrderBy(x => x.Role).ThenBy(x => x.SubjectId).ToListAsync(ct);
        var profiles = new List<TrustedWorkspaceProfile>(memberships.Count);
        foreach (var membership in memberships)
        {
            var actor = ActorFrom(membership);
            var name = "Weymela Admin"; var publicId = "Admin";
            switch (membership.Role)
            {
                case ActorRole.Business:
                    var b = await directory.BusinessCardAsync(membership.SubjectId, ct);
                    name = b.DisplayName;
                    publicId = (await db.PublicWorkspaceProfiles.AsNoTracking().SingleAsync(x => x.SubjectId == membership.SubjectId && x.Role == membership.Role, ct)).PublicId;
                    break;
                case ActorRole.Creator:
                    var c = await directory.CreatorCardAsync(membership.SubjectId, ct); name = c.DisplayName; publicId = c.PublicId; break;
                case ActorRole.Customer:
                    var u = await directory.CustomerCardAsync(membership.SubjectId, ct); name = u.DisplayName; publicId = u.PublicId; break;
                case ActorRole.Cashier: name = "Cashier"; publicId = "Checkout"; break;
                case ActorRole.PlatformAdmin:
                case ActorRole.OperationsAdmin:
                    name = await AdminDisplayName(userId, ct);
                    publicId = membership.Role == ActorRole.PlatformAdmin ? "Platform Admin" : "Operations Admin";
                    break;
            }
            profiles.Add(new(WorkspaceProfileKey(actor), actor, name, publicId, membership.CanCheckout));
        }
        return profiles;
    }

    private async Task<string> AdminDisplayName(Guid userId, CancellationToken ct)
    {
        var grantedName = await db.AdminGrants.AsNoTracking().Where(x => x.UserId == userId && x.IsActive)
            .OrderByDescending(x => x.GrantedAtUtc).Select(x => x.DisplayName).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(grantedName)) return grantedName;
        var profileName = await (from permission in db.CommercePermissions.AsNoTracking()
            join profile in db.PublicWorkspaceProfiles.AsNoTracking()
                on new { permission.SubjectId, permission.Role } equals new { profile.SubjectId, profile.Role }
            where permission.UserId == userId && permission.IsActive
                && (permission.Role == ActorRole.Customer || permission.Role == ActorRole.Creator)
            orderby permission.Role == ActorRole.Customer ? 0 : 1
            select profile.DisplayName).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(profileName)) return profileName;
        var email = await db.AuthIdentifiers.AsNoTracking().Where(x => x.UserId == userId
                && x.Kind == "Email" && x.IsVerified && x.DeliveryAddress != null)
            .Select(x => x.DeliveryAddress).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(email) ? "Weymela Admin" : email.Split('@')[0];
    }

    public async Task<TrustedWorkspaceIdentity> SelectAsync(Guid userId, Guid bindingId, long bindingVersion,
        ProfileSelection selection, DateTime authenticatedAtUtc, DateTime expiresAtUtc, CancellationToken ct)
    {
        var binding = await db.IdentityBindings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == bindingId && x.UserId == userId
            && x.Version == bindingVersion && x.IsActive && x.ValidAfterUtc <= authenticatedAtUtc, ct);
        if (binding is null) throw Denied();
        var profiles = await ProfilesForUserAsync(userId, ct);
        var selected = profiles.SingleOrDefault(x => x.Actor.Role == selection.Role && SubjectId(x.Actor) == selection.SubjectId
            && x.Actor.BusinessId == selection.BusinessId) ?? throw Denied();
        return new(selected.Actor, selected.DisplayName, selected.PublicId, binding.Id, binding.Version,
            authenticatedAtUtc, expiresAtUtc, profiles);
    }

    public static string WorkspaceProfileKey(Actor actor) => WorkspaceProfileKey(actor.Role, SubjectId(actor), actor.BusinessId);
    public static string WorkspaceProfileKey(ActorRole role, Guid subjectId, Guid? businessId)
        => $"{role}:{subjectId:D}:{businessId?.ToString("D") ?? "-"}";

    public static Actor ActorFrom(CommercePermission permission)
    {
        if (permission.Role == ActorRole.Business && permission.BusinessId != permission.SubjectId
            || permission.Role == ActorRole.Cashier && permission.BusinessId is null) throw Denied();
        return new(permission.UserId, permission.Role,
            permission.Role == ActorRole.Business ? permission.SubjectId : permission.Role == ActorRole.Cashier ? permission.BusinessId : null,
            permission.Role == ActorRole.Creator ? permission.SubjectId : null, permission.Role == ActorRole.Customer ? permission.SubjectId : null);
    }
    private static Guid SubjectId(Actor actor) => actor.Role switch
    {
        ActorRole.Business => actor.BusinessId ?? Guid.Empty,
        ActorRole.Creator => actor.CreatorId ?? Guid.Empty,
        ActorRole.Customer => actor.CustomerId ?? Guid.Empty,
        _ => actor.UserId
    };
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
