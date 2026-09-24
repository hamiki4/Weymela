using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Identity;

public sealed class AdminAccountService(WeymelaDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<AdminAccountView>> GetAsync(Actor actor, CancellationToken ct)
    {
        DemandPlatform(actor);
        var permissions = await db.CommercePermissions.AsNoTracking().Where(x =>
            (x.Role == ActorRole.PlatformAdmin || x.Role == ActorRole.OperationsAdmin)).ToListAsync(ct);
        var userIds = permissions.Select(x => x.UserId).Distinct().ToArray();
        var emails = await db.AuthIdentifiers.AsNoTracking().Where(x => userIds.Contains(x.UserId) && x.Kind == "Email" && x.IsVerified && x.DeliveryAddress != null)
            .ToDictionaryAsync(x => x.UserId, x => x.DeliveryAddress!, ct);
        var grants = await db.AdminGrants.AsNoTracking().Where(x => userIds.Contains(x.UserId)).OrderByDescending(x => x.GrantedAtUtc).ToListAsync(ct);
        var names = await db.CustomerProfiles.AsNoTracking().Where(x => userIds.Contains(x.UserId))
            .ToDictionaryAsync(x => x.UserId, x => x.PreferredName, ct);
        var activity = await db.AuditEvents.AsNoTracking().Where(x => userIds.Contains(x.ActorId)).GroupBy(x => x.ActorId)
            .Select(x => new { UserId = x.Key, Last = x.Max(y => y.OccurredAtUtc) }).ToDictionaryAsync(x => x.UserId, x => x.Last, ct);
        return permissions.GroupBy(x => x.UserId).Select(group =>
        {
            var current = group.OrderByDescending(x => x.IsActive).ThenBy(x => x.Role == ActorRole.PlatformAdmin ? 0 : 1).First();
            var grant = grants.FirstOrDefault(x => x.UserId == group.Key && x.Role == current.Role && x.IsActive)
                ?? grants.FirstOrDefault(x => x.UserId == group.Key && x.Role == current.Role);
            var email = emails.GetValueOrDefault(group.Key, "Verified Weymela account");
            return new AdminAccountView(group.Key, Name(grant?.DisplayName ?? names.GetValueOrDefault(group.Key), email), email,
                current.Role == ActorRole.PlatformAdmin ? "Platform Admin" : "Operations Admin",
                current.IsActive ? "Active" : "Inactive", grant?.GrantedAtUtc, activity.GetValueOrDefault(group.Key));
        }).OrderBy(x => x.Name).ToArray();
    }

    public Task<Guid> GrantAsync(Actor actor, AdminGrantInput input, string key, CancellationToken ct) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            DemandPlatform(actor);
            var email = input.Email.Trim().ToLowerInvariant();
            if (email.Length > 320 || !new EmailAddressAttribute().IsValid(email))
                throw new ApplicationFailure(FailureKind.Validation, "Enter an existing verified Weymela account email.");
            if (!TryRole(input.Role, out var role)) throw new ApplicationFailure(FailureKind.Validation, "Choose Platform Admin or Operations Admin.");
            var fingerprint = RequestFingerprint.Create(email, role.ToString(), input.DisplayName?.Trim() ?? "");
            var prior = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.ActorId == actor.UserId && x.OperationType == "GrantAdmin" && x.Key == key, token);
            if (prior is not null)
            {
                if (prior.RequestFingerprint != fingerprint) throw new ApplicationFailure(FailureKind.IdempotencyConflict, "This request reference was already used.");
                return Guid.Parse(prior.ResultReference);
            }
            var hash = EmailAuthService.HashIdentifier(email);
            var identity = await db.AuthIdentifiers.AsNoTracking().SingleOrDefaultAsync(x => x.Kind == "Email" && x.IdentifierHash == hash && x.IsVerified, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "No verified Weymela account matches that email.");
            var accountName = await db.CustomerProfiles.AsNoTracking().Where(x => x.UserId == identity.UserId)
                .Select(x => x.PreferredName).SingleOrDefaultAsync(token);
            var existing = await db.CommercePermissions.Where(x => x.UserId == identity.UserId &&
                (x.Role == ActorRole.PlatformAdmin || x.Role == ActorRole.OperationsAdmin)).ToListAsync(token);
            if (existing.Any(x => x.IsActive && x.Role == role))
                throw new ApplicationFailure(FailureKind.Validation, "This account already has that Admin role.");
            if (existing.Any(x => x.Role == role))
                throw new ApplicationFailure(FailureKind.Validation, "This Admin role was previously revoked and requires a new authorized lifecycle.", code: "RevokedAdminRole");
            var lifecycle = await db.AccountLifecycles.AsTracking().SingleOrDefaultAsync(x => x.UserId == identity.UserId, token);
            if (lifecycle?.Status is AccountLifecycleStatus.Suspended or AccountLifecycleStatus.Disabled or AccountLifecycleStatus.Cancelled or AccountLifecycleStatus.Revoked)
                throw new ApplicationFailure(FailureKind.Validation, "This account is not eligible for a legacy Admin grant.", code: "AccountLifecycleBlocksGrant");
            if (lifecycle?.Status == AccountLifecycleStatus.Pending)
                throw new ApplicationFailure(FailureKind.Validation, "Complete the pending account activation before using the legacy Admin grant.", code: "PendingAccountActivation");
            if (await db.AccountPreauthorizations.AnyAsync(x => x.UserId == identity.UserId && x.TargetRole == role && x.Status == AccountPreauthorizationStatus.Pending, token))
                throw new ApplicationFailure(FailureKind.Validation, "A pending account preauthorization already exists for this Admin role.", code: "PendingAccountPreauthorization");
            if (existing.Any(x => x.IsActive && x.Role == ActorRole.PlatformAdmin) && role == ActorRole.OperationsAdmin)
                await EnsureReplacementPlatformAdmin(identity.UserId, token);
            var replaced = existing.Where(x => x.IsActive).ToArray();
            foreach (var permission in replaced) permission.IsActive = false;
            var target = existing.SingleOrDefault(x => x.Role == role && x.SubjectId == identity.UserId);
            if (target is null) db.CommercePermissions.Add(new CommercePermission(identity.UserId, role, identity.UserId, null, true, false));
            else target.IsActive = true;
            foreach (var old in await db.AdminGrants.Where(x => x.UserId == identity.UserId && x.IsActive).ToListAsync(token))
            { old.IsActive = false; old.RevokedByUserId = actor.UserId; old.RevokedAtUtc = Now; }
            var grant = new AdminGrantRecord
            {
                UserId = identity.UserId, DisplayName = Name(input.DisplayName ?? accountName, email), Role = role,
                GrantedByUserId = actor.UserId, GrantedAtUtc = Now
            };
            db.AdminGrants.Add(grant);
            db.IdempotencyRecords.Add(new(actor.UserId, "GrantAdmin", key, fingerprint, grant.Id.ToString(), Now));
            foreach (var old in replaced)
                db.AccountRoleHistory.Add(new(Guid.NewGuid(), actor.UserId, identity.UserId, old.Role, old.SubjectId, old.BusinessId,
                    "Revoked", "Legacy Admin grant replaced the previous active Admin role.", Now, Guid.NewGuid(), old.UserId));
            db.AccountRoleHistory.Add(new(Guid.NewGuid(), actor.UserId, identity.UserId, role, identity.UserId, null,
                "Granted", "Legacy Admin grant compatibility path.", Now, Guid.NewGuid(), grant.Id));
            if (lifecycle is null)
                db.AccountLifecycles.Add(new AccountLifecycleRecord { UserId = identity.UserId, Status = AccountLifecycleStatus.Active,
                    ChangedByUserId = actor.UserId, CreatedAtUtc = Now, UpdatedAtUtc = Now, Version = 1 });
            Record(actor, "AdminGranted", identity.UserId, role, grant.Id);
            return grant.Id;
        }, ct);

    public Task<Guid> RevokeAsync(Actor actor, Guid userId, string key, CancellationToken ct) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            DemandPlatform(actor);
            var fingerprint = RequestFingerprint.Create(userId.ToString());
            var prior = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.ActorId == actor.UserId && x.OperationType == "RevokeAdmin" && x.Key == key, token);
            if (prior is not null)
            {
                if (prior.RequestFingerprint != fingerprint) throw new ApplicationFailure(FailureKind.IdempotencyConflict, "This request reference was already used.");
                return Guid.Parse(prior.ResultReference);
            }
            var active = await db.CommercePermissions.Where(x => x.UserId == userId && x.IsActive &&
                (x.Role == ActorRole.PlatformAdmin || x.Role == ActorRole.OperationsAdmin)).ToListAsync(token);
            if (active.Count == 0) throw new ApplicationFailure(FailureKind.NotFound, "Active Admin account not found.");
            if (active.Any(x => x.Role == ActorRole.PlatformAdmin)) await EnsureReplacementPlatformAdmin(userId, token);
            foreach (var permission in active) permission.IsActive = false;
            foreach (var grant in await db.AdminGrants.Where(x => x.UserId == userId && x.IsActive).ToListAsync(token))
            { grant.IsActive = false; grant.RevokedByUserId = actor.UserId; grant.RevokedAtUtc = Now; }
            db.IdempotencyRecords.Add(new(actor.UserId, "RevokeAdmin", key, fingerprint, userId.ToString(), Now));
            Record(actor, "AdminRevoked", userId, active[0].Role, userId);
            return userId;
        }, ct);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static void DemandPlatform(Actor actor)
    { if (actor.Role != ActorRole.PlatformAdmin) throw new ApplicationFailure(FailureKind.Forbidden, "Platform Admin access is required."); }
    private async Task EnsureReplacementPlatformAdmin(Guid removing, CancellationToken ct)
    {
        if (await db.CommercePermissions.CountAsync(x => x.Role == ActorRole.PlatformAdmin && x.IsActive && x.UserId != removing, ct) == 0)
            throw new ApplicationFailure(FailureKind.Validation, "Grant another Platform Admin before removing the last active Platform Admin.");
    }
    private void Record(Actor actor, string type, Guid target, ActorRole role, Guid reference)
    {
        var correlation = Guid.NewGuid();
        db.AuditEvents.Add(new(Guid.NewGuid(), type, actor.UserId, null, null, null, correlation, Now,
            $"target={target:D};role={role};reference={reference:D}", TargetUserId: target, TargetRole: role,
            Operation: type == "AdminGranted" ? "grant-admin" : "revoke-admin"));
        db.OutboxMessages.Add(new OutboxMessage { EventType = type,
            Payload = JsonSerializer.Serialize(new { TargetUserId = target, Role = role.ToString(), Reference = reference }), OccurredAtUtc = Now });
    }
    private static bool TryRole(string value, out ActorRole role)
    {
        var normalized = value.Replace(" ", "", StringComparison.Ordinal);
        return Enum.TryParse(normalized, true, out role) && role is ActorRole.PlatformAdmin or ActorRole.OperationsAdmin;
    }
    private static string Name(string? displayName, string email)
    {
        var value = string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0].Replace('.', ' ').Replace('_', ' ') : displayName.Trim();
        return value.Length <= 120 ? value : value[..120];
    }
}
