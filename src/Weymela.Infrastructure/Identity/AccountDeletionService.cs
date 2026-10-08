using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Identity;

public interface IAccountIdentityDeletionProvider
{
    bool Enabled { get; }
    Task DeleteAsync(string projectId, string externalSubject, CancellationToken ct);
}

public sealed class DisabledAccountIdentityDeletionProvider : IAccountIdentityDeletionProvider
{
    public bool Enabled => false;
    public Task DeleteAsync(string projectId, string externalSubject, CancellationToken ct)
        => throw new InvalidOperationException("External identity deletion is unavailable.");
}

public sealed record DeleteAccountRoleInput(string Role, Guid? SubjectId, string Reason);
public sealed record DeleteEntireAccountInput(string Reason, string Confirmation);
public sealed record AccountDeletionState(Guid UserId, string Status);
public sealed record OwnRoleClosureInput(string Role, Guid SubjectId, string Confirmation);
public sealed record OwnRoleClosureOption(string Role, Guid SubjectId, string DisplayName, string Status,
    IReadOnlyList<string> Blockers);
public sealed record OwnRoleClosureOverview(IReadOnlyList<OwnRoleClosureOption> Roles);
public sealed record OwnRoleClosureResult(string Role, Guid SubjectId, string Status,
    IReadOnlyList<string> Blockers, int RemainingRoles, string? NextRole = null);

public sealed class AccountDeletionService(WeymelaDbContext db, TimeProvider clock, IAccountIdentityDeletionProvider provider)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<OwnRoleClosureOverview> MineAsync(Actor actor, CancellationToken ct)
    {
        var memberships = await db.CommercePermissions.AsNoTracking()
            .Where(x => x.UserId == actor.UserId && x.IsActive)
            .OrderBy(x => x.Role).ThenBy(x => x.SubjectId).ToListAsync(ct);
        var options = new List<OwnRoleClosureOption>(memberships.Count);
        foreach (var membership in memberships)
        {
            var blockers = await ClosureBlockersAsync(membership, memberships.Count, ct);
            var pending = await PendingClosureAsync(membership.UserId, membership.Role, membership.SubjectId, ct);
            options.Add(new(membership.Role.ToString(), membership.SubjectId,
                await DisplayNameAsync(membership, ct), blockers.Count == 0 ? "Eligible" : pending ? "PendingClosure" : "ActionRequired",
                blockers));
        }
        return new(options);
    }

    public async Task<OwnRoleClosureResult> CloseOwnRoleAsync(Actor actor, OwnRoleClosureInput input,
        string key, CancellationToken ct)
    {
        if (!Enum.TryParse<ActorRole>(input.Role, true, out var role) || !Enum.IsDefined(role)
            || input.SubjectId == Guid.Empty)
            throw new ApplicationFailure(FailureKind.Validation, "Choose one of your active account roles.");
        if (input.Confirmation != "DELETE")
            throw new ApplicationFailure(FailureKind.Validation, "Confirm that you want to close this role.");
        Key(key);
        var fingerprint = RequestFingerprint.Create(actor.UserId.ToString("D"), role.ToString(),
            input.SubjectId.ToString("D"), "self-role-closure");
        return await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync<OwnRoleClosureResult>(async token =>
        {
            var prior = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor.UserId
                && x.OperationType == "AccountCloseOwnRole" && x.Key == key, token);
            if (prior is not null)
            {
                if (prior.RequestFingerprint != fingerprint) throw Conflict();
                var remaining = await db.CommercePermissions.AsNoTracking().CountAsync(x => x.UserId == actor.UserId && x.IsActive, token);
                return new(role.ToString(), input.SubjectId, prior.ResultReference, [], remaining);
            }

            var permission = (await db.CommercePermissions.FromSqlInterpolated(
                    $"SELECT * FROM v3.\"CommercePermissions\" WHERE \"UserId\"={actor.UserId} AND \"Role\"={role.ToString()} AND \"SubjectId\"={input.SubjectId} FOR UPDATE")
                .ToListAsync(token)).SingleOrDefault();
            if (permission is null)
                throw new ApplicationFailure(FailureKind.NotFound, "This account role was not found.");
            if (!permission.IsActive)
            {
                var closed = await db.AccountRoleHistory.AsNoTracking().AnyAsync(x => x.TargetUserId == actor.UserId
                    && x.TargetRole == role && x.TargetSubjectId == input.SubjectId && x.Action == "Deleted", token);
                if (!closed) throw new ApplicationFailure(FailureKind.NotFound, "This account role is not active.");
                var alreadyRemaining = await db.CommercePermissions.AsNoTracking().CountAsync(x => x.UserId == actor.UserId && x.IsActive, token);
                db.IdempotencyRecords.Add(new(actor.UserId, "AccountCloseOwnRole", key, fingerprint, "Closed", Now));
                return new(role.ToString(), input.SubjectId, "Closed", [], alreadyRemaining);
            }

            var activeCount = await db.CommercePermissions.AsNoTracking().CountAsync(x => x.UserId == actor.UserId && x.IsActive, token);
            var blockers = await ClosureBlockersAsync(permission, activeCount, token);
            if (blockers.Count > 0)
            {
                if (!await PendingClosureAsync(actor.UserId, role, input.SubjectId, token))
                {
                    var now = Now; var correlation = Guid.NewGuid();
                    db.AccountRoleHistory.Add(new(Guid.NewGuid(), actor.UserId, actor.UserId, role, input.SubjectId,
                        permission.BusinessId, "ClosureRequested", BoundedReason(blockers), now, correlation));
                    db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "AccountRoleClosureRequested", actor.UserId,
                        permission.BusinessId, null, role == ActorRole.Creator ? input.SubjectId : null, correlation, now,
                        "self-service-role-closure-pending", TargetUserId: actor.UserId, TargetRole: role,
                        TargetSubjectId: input.SubjectId, Operation: "close-own-role", Reason: BoundedReason(blockers)));
                    db.OutboxMessages.Add(new OutboxMessage { EventType = "AccountRoleClosureRequested",
                        Payload = JsonSerializer.Serialize(new { UserId = actor.UserId, Role = role.ToString(), SubjectId = input.SubjectId }), OccurredAtUtc = now });
                }
                db.IdempotencyRecords.Add(new(actor.UserId, "AccountCloseOwnRole", key, fingerprint, "Pending", Now));
                return new(role.ToString(), input.SubjectId, "Pending", blockers, activeCount);
            }

            var closeAt = Now; var closeCorrelation = Guid.NewGuid();
            if (activeCount == 1)
            {
                await CloseEntireForSelfAsync(actor.UserId, permission, closeAt, closeCorrelation, token);
                db.IdempotencyRecords.Add(new(actor.UserId, "AccountCloseOwnRole", key, fingerprint,
                    "PendingIdentityDeletion", closeAt));
                return new(role.ToString(), input.SubjectId, "PendingIdentityDeletion", [], 0);
            }

            permission.IsActive = false;
            permission.CanCheckout = false;
            foreach (var grant in await db.AdminGrants.AsTracking().Where(x => x.UserId == actor.UserId
                && x.Role == role && x.IsActive).ToListAsync(token))
            { grant.IsActive = false; grant.RevokedByUserId = actor.UserId; grant.RevokedAtUtc = closeAt; }
            if (role == ActorRole.Cashier)
            {
                foreach (var cashier in await db.CashierPreauthorizations.AsTracking().Where(x => x.UserId == actor.UserId
                    && x.BusinessId == permission.BusinessId
                    && x.Status != CashierPreauthorizationStatus.Revoked).ToListAsync(token))
                {
                    cashier.Status = cashier.ActivatedAtUtc is null
                        ? CashierPreauthorizationStatus.Disabled
                        : CashierPreauthorizationStatus.Revoked;
                    cashier.DisabledAtUtc = closeAt;
                    cashier.Version++;
                }
            }
            db.AccountRoleHistory.Add(new(Guid.NewGuid(), actor.UserId, actor.UserId, role, input.SubjectId,
                permission.BusinessId, "Deleted", "Closed by account owner", closeAt, closeCorrelation));
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "AccountRoleClosed", actor.UserId,
                permission.BusinessId, null, role == ActorRole.Creator ? input.SubjectId : null, closeCorrelation, closeAt,
                "self-service-role-closed", TargetUserId: actor.UserId, TargetRole: role,
                TargetSubjectId: input.SubjectId, Operation: "close-own-role"));
            db.OutboxMessages.Add(new OutboxMessage { EventType = "AccountRoleClosed",
                Payload = JsonSerializer.Serialize(new { UserId = actor.UserId, Role = role.ToString(), SubjectId = input.SubjectId }), OccurredAtUtc = closeAt });
            db.IdempotencyRecords.Add(new(actor.UserId, "AccountCloseOwnRole", key, fingerprint, "Closed", closeAt));
            return new(role.ToString(), input.SubjectId, "Closed", [], activeCount - 1);
        }, ct);
    }

    public async Task<Guid> DeleteRoleAsync(AuthorityContext authority, Guid userId, DeleteAccountRoleInput input,
        string key, CancellationToken ct)
    {
        await DemandPlatformAsync(authority, ct);
        if (!Enum.TryParse<ActorRole>(input.Role, true, out var role) || !Enum.IsDefined(role) || role == ActorRole.Cashier)
            throw new ApplicationFailure(FailureKind.Validation, "Choose an account role to delete.");
        var reason = Reason(input.Reason);
        Key(key);
        if (role == ActorRole.PlatformAdmin && userId == authority.RealActor.UserId)
            throw new ApplicationFailure(FailureKind.Forbidden, "A Platform Admin cannot delete its own role.");
        var fingerprint = RequestFingerprint.Create(userId.ToString("D"), role.ToString(), input.SubjectId?.ToString("D") ?? "", reason);
        return await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var prior = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == authority.RealActor.UserId
                && x.OperationType == "AccountDeleteRole" && x.Key == key, token);
            if (prior is not null)
            {
                if (prior.RequestFingerprint != fingerprint) throw Conflict();
                return Guid.Parse(prior.ResultReference);
            }
            var matches = await db.CommercePermissions.AsTracking().Where(x => x.UserId == userId && x.Role == role
                && x.IsActive && (input.SubjectId == null || x.SubjectId == input.SubjectId)).ToListAsync(token);
            if (matches.Count != 1)
                throw new ApplicationFailure(matches.Count == 0 ? FailureKind.NotFound : FailureKind.Validation,
                    matches.Count == 0 ? "Active role not found." : "Choose a specific profile to delete.");
            var permission = matches[0];
            if (role == ActorRole.PlatformAdmin) await PlatformAdminSafety.RequireReplacementAsync(db, userId, token);
            var now = Now; var correlation = Guid.NewGuid();
            permission.IsActive = false;
            permission.CanCheckout = false;
            foreach (var grant in await db.AdminGrants.AsTracking().Where(x => x.UserId == userId && x.Role == role && x.IsActive).ToListAsync(token))
            { grant.IsActive = false; grant.RevokedByUserId = authority.RealActor.UserId; grant.RevokedAtUtc = now; }
            if (role == ActorRole.Business)
            {
                foreach (var cashier in await db.CommercePermissions.AsTracking().Where(x => x.Role == ActorRole.Cashier
                    && x.BusinessId == permission.SubjectId && x.IsActive).ToListAsync(token))
                { cashier.IsActive = false; cashier.CanCheckout = false; }
                foreach (var cashier in await db.CashierPreauthorizations.AsTracking().Where(x => x.BusinessId == permission.SubjectId
                    && x.Status != CashierPreauthorizationStatus.Revoked).ToListAsync(token))
                { cashier.Status = cashier.ActivatedAtUtc is null ? CashierPreauthorizationStatus.Disabled : CashierPreauthorizationStatus.Revoked; cashier.DisabledAtUtc = now; cashier.Version++; }
            }
            db.AccountRoleHistory.Add(new(Guid.NewGuid(), authority.RealActor.UserId, userId, role, permission.SubjectId,
                permission.BusinessId, "Deleted", reason, now, correlation));
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "AccountRoleDeleted", authority.RealActor.UserId,
                permission.BusinessId, null, role == ActorRole.Creator ? permission.SubjectId : null, correlation, now,
                "account-role-deleted", TargetUserId: userId, TargetRole: role, TargetSubjectId: permission.SubjectId,
                Operation: "delete-role", Reason: reason));
            db.OutboxMessages.Add(new OutboxMessage { EventType = "AccountRoleClosed",
                Payload = JsonSerializer.Serialize(new { UserId = userId, Role = role.ToString(), SubjectId = permission.SubjectId }), OccurredAtUtc = now });
            db.IdempotencyRecords.Add(new(authority.RealActor.UserId, "AccountDeleteRole", key, fingerprint,
                permission.SubjectId.ToString("D"), now));
            return permission.SubjectId;
        }, ct);
    }

    public async Task<AccountDeletionState> DeleteEntireAsync(AuthorityContext authority, Guid userId,
        DeleteEntireAccountInput input, string key, CancellationToken ct)
    {
        await DemandPlatformAsync(authority, ct);
        if (input.Confirmation != "DELETE") throw new ApplicationFailure(FailureKind.Validation, "Type DELETE to confirm entire account deletion.");
        if (userId == authority.RealActor.UserId) throw new ApplicationFailure(FailureKind.Forbidden, "A Platform Admin cannot delete its own account.");
        if (!provider.Enabled) throw new ApplicationFailure(FailureKind.Validation, "Account deletion is temporarily unavailable.");
        var reason = Reason(input.Reason); Key(key);
        var fingerprint = RequestFingerprint.Create(userId.ToString("D"), reason, "DELETE");
        return await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var prior = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == authority.RealActor.UserId
                && x.OperationType == "AccountDeleteEntire" && x.Key == key, token);
            if (prior is not null && prior.RequestFingerprint != fingerprint) throw Conflict();
            var existingEvent = await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM v3.\"OutboxMessages\" WHERE \"EventType\"='AccountIdentityDeletion' AND \"Payload\"->>'UserId'={userId.ToString("D")} ORDER BY \"OccurredAtUtc\" DESC LIMIT 1")
                .SingleOrDefaultAsync(token);
            if (existingEvent is not null)
            {
                if (existingEvent.FailedAtUtc is not null)
                { existingEvent.FailedAtUtc = null; existingEvent.NextAttemptAtUtc = null; existingEvent.FailureCount = 0; existingEvent.LastError = null; }
                if (prior is null) db.IdempotencyRecords.Add(new(authority.RealActor.UserId, "AccountDeleteEntire", key,
                    fingerprint, userId.ToString("D"), Now));
                return new AccountDeletionState(userId, existingEvent.ProcessedAtUtc is null ? "Pending" : "Completed");
            }
            var permissions = await db.CommercePermissions.AsTracking().Where(x => x.UserId == userId).ToListAsync(token);
            if (permissions.Count == 0) throw new ApplicationFailure(FailureKind.NotFound, "Account not found.");
            if (permissions.Any(x => x.Role == ActorRole.PlatformAdmin && x.IsActive))
                await PlatformAdminSafety.RequireReplacementAsync(db, userId, token);
            var now = Now; var correlation = Guid.NewGuid();
            var ownedBusinesses = permissions.Where(x => x.Role == ActorRole.Business && x.BusinessId is not null)
                .Select(x => x.BusinessId!.Value).ToArray();
            var lifecycle = await db.AccountLifecycles.AsTracking().SingleOrDefaultAsync(x => x.UserId == userId, token);
            if (lifecycle is null)
                db.AccountLifecycles.Add(new AccountLifecycleRecord { UserId = userId, Status = AccountLifecycleStatus.Closed,
                    Reason = reason, ChangedByUserId = authority.RealActor.UserId, CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1 });
            else
            { lifecycle.Status = AccountLifecycleStatus.Closed; lifecycle.Reason = reason; lifecycle.ChangedByUserId = authority.RealActor.UserId; lifecycle.UpdatedAtUtc = now; }
            foreach (var permission in permissions) { permission.IsActive = false; permission.CanCheckout = false; }
            foreach (var cashier in await db.CommercePermissions.AsTracking().Where(x => x.Role == ActorRole.Cashier
                && x.BusinessId != null && ownedBusinesses.Contains(x.BusinessId.Value) && x.IsActive).ToListAsync(token))
            { cashier.IsActive = false; cashier.CanCheckout = false; }
            foreach (var binding in await db.IdentityBindings.AsTracking().Where(x => x.UserId == userId && x.IsActive).ToListAsync(token))
            { binding.IsActive = false; binding.Version++; }
            foreach (var session in await db.DeviceSessions.AsTracking().Where(x => x.UserId == userId && x.RevokedAtUtc == null).ToListAsync(token))
                session.RevokedAtUtc = now;
            foreach (var device in await db.AuthorizedDevices.AsTracking().Where(x => x.UserId == userId && x.RevokedAtUtc == null).ToListAsync(token))
            { device.RevokedAtUtc = now; device.PinVerifier = null; }
            foreach (var grant in await db.AdminGrants.AsTracking().Where(x => x.UserId == userId && x.IsActive).ToListAsync(token))
            { grant.IsActive = false; grant.RevokedByUserId = authority.RealActor.UserId; grant.RevokedAtUtc = now; }
            foreach (var cashier in await db.CashierPreauthorizations.AsTracking().Where(x => (x.UserId == userId
                    || ownedBusinesses.Contains(x.BusinessId))
                && x.Status != CashierPreauthorizationStatus.Revoked).ToListAsync(token))
            { cashier.Status = cashier.ActivatedAtUtc is null ? CashierPreauthorizationStatus.Disabled : CashierPreauthorizationStatus.Revoked; cashier.DisabledAtUtc = now; cashier.Version++; }
            foreach (var challenge in await db.EmailAuthChallenges.AsTracking().Where(x => x.UserId == userId).ToListAsync(token))
            { challenge.ConsumedAtUtc ??= now; challenge.RecoveryGrantConsumedAtUtc ??= now; challenge.RecoveryGrantHash = null; }
            // Keep the row for the no-DELETE runtime contract, but replace the
            // verifier before the external identity operation can begin.
            var credential = await db.PasswordCredentials.AsTracking().SingleOrDefaultAsync(x => x.UserId == userId, token);
            if (credential is not null)
            {
                credential.PasswordHash = PasswordCredentialHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
                credential.ChangedAtUtc = now;
                credential.FailedAttempts = 0;
                credential.LockedUntilUtc = null;
                credential.Version++;
            }
            foreach (var permission in permissions)
                db.AccountRoleHistory.Add(new(Guid.NewGuid(), authority.RealActor.UserId, userId, permission.Role,
                    permission.SubjectId, permission.BusinessId, "Deleted", reason, now, correlation));
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "EntireAccountDeletionRequested", authority.RealActor.UserId,
                null, null, null, correlation, now, "entire-account-deletion", TargetUserId: userId,
                Operation: "delete-entire-account", Reason: reason));
            db.OutboxMessages.Add(new OutboxMessage { EventType = "AccountIdentityDeletion",
                Payload = JsonSerializer.Serialize(new { UserId = userId, AdminUserId = authority.RealActor.UserId, CorrelationId = correlation }), OccurredAtUtc = now });
            db.IdempotencyRecords.Add(new(authority.RealActor.UserId, "AccountDeleteEntire", key, fingerprint,
                userId.ToString("D"), now));
            return new AccountDeletionState(userId, "Pending");
        }, ct);
    }

    public async Task<AccountDeletionState> StatusAsync(AuthorityContext authority, Guid userId, CancellationToken ct)
    {
        await DemandPlatformAsync(authority, ct);
        var row = await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM v3.\"OutboxMessages\" WHERE \"EventType\"='AccountIdentityDeletion' AND \"Payload\"->>'UserId'={userId.ToString("D")} ORDER BY \"OccurredAtUtc\" DESC LIMIT 1")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        return row is null ? new(userId, "NotRequested") : new(userId,
            row.ProcessedAtUtc is not null ? "Completed" : row.FailedAtUtc is not null ? "RetryRequired" : "Pending");
    }

    private async Task<List<string>> ClosureBlockersAsync(CommercePermission permission, int activeRoleCount,
        CancellationToken ct)
    {
        var blockers = new List<string>();
        var subjectId = permission.SubjectId;
        switch (permission.Role)
        {
            case ActorRole.Customer:
                var cashback = await db.CustomerCashbackAccounts.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CustomerId == subjectId, ct);
                if (cashback?.AvailableCashback.Amount > 0)
                    blockers.Add("Receive the remaining cashback before closing this Customer role.");
                if (await db.PayoutRecords.AsNoTracking().AnyAsync(x => x.CustomerId == subjectId
                    && x.Status == PayoutStatus.Eligible, ct))
                    blockers.Add("Complete the pending Customer payout before closing this role.");
                break;
            case ActorRole.Creator:
                var earnings = await db.CreatorEarningsAccounts.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CreatorId == subjectId, ct);
                if (earnings?.AvailableEarnings.Amount > 0)
                    blockers.Add("Receive the remaining Creator earnings before closing this role.");
                if (await db.PayoutRecords.AsNoTracking().AnyAsync(x => x.CreatorId == subjectId
                    && x.Status == PayoutStatus.Eligible, ct))
                    blockers.Add("Complete the pending Creator payout before closing this role.");
                if (await db.CreatorApplications.AsNoTracking().AnyAsync(x => x.CreatorId == subjectId
                    && x.Status == CreatorApplicationStatus.Pending, ct))
                    blockers.Add("Resolve pending Promotion applications before closing this role.");
                if (await db.CreatorAllocations.AsNoTracking().AnyAsync(x => x.CreatorId == subjectId
                    && (x.Status == CreatorAllocationStatus.Active || x.Status == CreatorAllocationStatus.Exhausted), ct))
                    blockers.Add("Complete active Promotion work before closing this role.");
                if (await db.UgcCreatorRequests.AsNoTracking().AnyAsync(x => x.CreatorId == subjectId
                    && x.Status == UgcRequestStatus.Pending, ct))
                    blockers.Add("Resolve pending UGC requests before closing this role.");
                if (await db.UgcAssignments.AsNoTracking().AnyAsync(x => x.CreatorId == subjectId
                    && (x.Status == UgcAssignmentStatus.InProgress || x.Status == UgcAssignmentStatus.Submitted
                        || x.Status == UgcAssignmentStatus.ChangesRequested), ct))
                    blockers.Add("Complete outstanding UGC work before closing this role.");
                break;
            case ActorRole.Business:
                var wallet = await db.BusinessWallets.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.BusinessId == subjectId, ct);
                if (wallet is not null && (wallet.AvailableBalance.Amount > 0 || wallet.ReservedBalance.Amount > 0))
                    blockers.Add("Resolve all available and reserved Business funds before closing this role.");
                if (await db.DepositRequests.AsNoTracking().AnyAsync(x => x.BusinessId == subjectId
                    && x.Status == Application.Operations.DepositReviewStatus.Pending, ct))
                    blockers.Add("Resolve pending deposits before closing this role.");
                if (await db.Promotions.AsNoTracking().AnyAsync(x => x.BusinessId == subjectId
                    && x.Status != PromotionStatus.Draft && x.Status != PromotionStatus.Completed
                    && x.Status != PromotionStatus.Cancelled, ct))
                    blockers.Add("Complete or cancel active funded Promotions before closing this role.");
                if (await db.UgcOpportunities.AsNoTracking().AnyAsync(x => x.BusinessId == subjectId
                    && (x.Status == UgcOpportunityStatus.Open || x.Status == UgcOpportunityStatus.InProgress), ct))
                    blockers.Add("Complete active UGC opportunities before closing this role.");
                if (await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.BusinessId == subjectId
                    && x.Role == ActorRole.Cashier && x.IsActive, ct)
                    || await db.CashierPreauthorizations.AsNoTracking().AnyAsync(x => x.BusinessId == subjectId
                        && (x.Status == CashierPreauthorizationStatus.PendingActivation
                            || x.Status == CashierPreauthorizationStatus.Active), ct))
                    blockers.Add("Deactivate or transfer Business Cashiers before closing this role.");
                if (!await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.BusinessId == subjectId
                    && x.Role == ActorRole.Business && x.UserId != permission.UserId && x.IsActive, ct))
                    blockers.Add("Transfer Business ownership to another approved owner before closing this role.");
                break;
            case ActorRole.PlatformAdmin:
                if (await db.CommercePermissions.AsNoTracking().CountAsync(x => x.Role == ActorRole.PlatformAdmin
                    && x.IsActive, ct) <= 1)
                    blockers.Add("Create and verify a replacement Platform Admin before requesting closure.");
                blockers.Add("Another Platform Admin must review this privileged role closure.");
                break;
            case ActorRole.OperationsAdmin:
                blockers.Add("A Platform Admin must review this privileged role closure.");
                break;
        }
        if (activeRoleCount == 1 && !provider.Enabled)
            blockers.Add("Secure sign-in deletion is temporarily unavailable. Your role will remain active until an administrator can complete it.");
        return blockers.Distinct(StringComparer.Ordinal).ToList();
    }

    private async Task CloseEntireForSelfAsync(Guid userId, CommercePermission permission, DateTime now,
        Guid correlation, CancellationToken ct)
    {
        var permissions = await db.CommercePermissions.AsTracking().Where(x => x.UserId == userId).ToListAsync(ct);
        var ownedBusinesses = permissions.Where(x => x.Role == ActorRole.Business && x.BusinessId is not null)
            .Select(x => x.BusinessId!.Value).ToArray();
        var lifecycle = await db.AccountLifecycles.AsTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (lifecycle is null)
            db.AccountLifecycles.Add(new AccountLifecycleRecord { UserId = userId, Status = AccountLifecycleStatus.Closed,
                Reason = "Closed by account owner", ChangedByUserId = userId, CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1 });
        else
        { lifecycle.Status = AccountLifecycleStatus.Closed; lifecycle.Reason = "Closed by account owner"; lifecycle.ChangedByUserId = userId; lifecycle.UpdatedAtUtc = now; }
        foreach (var item in permissions) { item.IsActive = false; item.CanCheckout = false; }
        foreach (var cashier in await db.CommercePermissions.AsTracking().Where(x => x.Role == ActorRole.Cashier
            && x.BusinessId != null && ownedBusinesses.Contains(x.BusinessId.Value) && x.IsActive).ToListAsync(ct))
        { cashier.IsActive = false; cashier.CanCheckout = false; }
        foreach (var binding in await db.IdentityBindings.AsTracking().Where(x => x.UserId == userId && x.IsActive).ToListAsync(ct))
        { binding.IsActive = false; binding.Version++; }
        foreach (var session in await db.DeviceSessions.AsTracking().Where(x => x.UserId == userId && x.RevokedAtUtc == null).ToListAsync(ct))
            session.RevokedAtUtc = now;
        foreach (var device in await db.AuthorizedDevices.AsTracking().Where(x => x.UserId == userId && x.RevokedAtUtc == null).ToListAsync(ct))
        { device.RevokedAtUtc = now; device.PinVerifier = null; }
        foreach (var grant in await db.AdminGrants.AsTracking().Where(x => x.UserId == userId && x.IsActive).ToListAsync(ct))
        { grant.IsActive = false; grant.RevokedByUserId = userId; grant.RevokedAtUtc = now; }
        foreach (var cashier in await db.CashierPreauthorizations.AsTracking().Where(x => (x.UserId == userId
                || ownedBusinesses.Contains(x.BusinessId)) && x.Status != CashierPreauthorizationStatus.Revoked).ToListAsync(ct))
        { cashier.Status = cashier.ActivatedAtUtc is null ? CashierPreauthorizationStatus.Disabled : CashierPreauthorizationStatus.Revoked; cashier.DisabledAtUtc = now; cashier.Version++; }
        foreach (var challenge in await db.EmailAuthChallenges.AsTracking().Where(x => x.UserId == userId).ToListAsync(ct))
        { challenge.ConsumedAtUtc ??= now; challenge.RecoveryGrantConsumedAtUtc ??= now; challenge.RecoveryGrantHash = null; }
        var credential = await db.PasswordCredentials.AsTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (credential is not null)
        {
            credential.PasswordHash = PasswordCredentialHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            credential.ChangedAtUtc = now; credential.FailedAttempts = 0; credential.LockedUntilUtc = null; credential.Version++;
        }
        foreach (var item in permissions)
            db.AccountRoleHistory.Add(new(Guid.NewGuid(), userId, userId, item.Role, item.SubjectId,
                item.BusinessId, "Deleted", "Closed by account owner", now, correlation));
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "EntireAccountDeletionRequested", userId,
            permission.BusinessId, null, permission.Role == ActorRole.Creator ? permission.SubjectId : null,
            correlation, now, "self-service-entire-account-deletion", TargetUserId: userId,
            TargetRole: permission.Role, TargetSubjectId: permission.SubjectId,
            Operation: "delete-entire-account", Reason: "Closed by account owner"));
        db.OutboxMessages.Add(new OutboxMessage { EventType = "AccountRoleClosed",
            Payload = JsonSerializer.Serialize(new { UserId = userId, Role = permission.Role.ToString(), SubjectId = permission.SubjectId }), OccurredAtUtc = now });
        db.OutboxMessages.Add(new OutboxMessage { EventType = "AccountIdentityDeletion",
            Payload = JsonSerializer.Serialize(new { UserId = userId, AdminUserId = userId, CorrelationId = correlation }), OccurredAtUtc = now });
    }

    private async Task<bool> PendingClosureAsync(Guid userId, ActorRole role, Guid subjectId, CancellationToken ct)
    {
        var requested = await db.AccountRoleHistory.AsNoTracking().Where(x => x.TargetUserId == userId
            && x.TargetRole == role && x.TargetSubjectId == subjectId && x.Action == "ClosureRequested")
            .MaxAsync(x => (DateTime?)x.OccurredAtUtc, ct);
        if (requested is null) return false;
        var closed = await db.AccountRoleHistory.AsNoTracking().Where(x => x.TargetUserId == userId
            && x.TargetRole == role && x.TargetSubjectId == subjectId && x.Action == "Deleted")
            .MaxAsync(x => (DateTime?)x.OccurredAtUtc, ct);
        return closed is null || closed < requested;
    }

    private async Task<string> DisplayNameAsync(CommercePermission permission, CancellationToken ct)
    {
        if (permission.Role is ActorRole.PlatformAdmin or ActorRole.OperationsAdmin)
            return permission.Role == ActorRole.PlatformAdmin ? "Platform Admin" : "Operations Admin";
        if (permission.Role == ActorRole.Cashier)
            return await db.CashierPreauthorizations.AsNoTracking().Where(x => x.UserId == permission.UserId
                && x.BusinessId == permission.BusinessId).OrderByDescending(x => x.ActivatedAtUtc)
                .Select(x => x.DisplayName).FirstOrDefaultAsync(ct) ?? "Cashier";
        return await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.SubjectId == permission.SubjectId
            && x.Role == permission.Role).Select(x => x.DisplayName).SingleOrDefaultAsync(ct)
            ?? permission.Role.ToString();
    }

    private static string BoundedReason(IReadOnlyCollection<string> blockers)
    {
        var value = "User requested closure. " + string.Join(" ", blockers);
        return value.Length <= 500 ? value : value[..500];
    }

    private async Task DemandPlatformAsync(AuthorityContext authority, CancellationToken ct)
    {
        if (authority.RealActor.Role != ActorRole.PlatformAdmin || !authority.Authority.IsPlatformAdmin
            || authority.CommandActor.UserId == Guid.Empty)
            throw new ApplicationFailure(FailureKind.Forbidden, "Real Platform Admin authority is required.");
        var lifecycle = await db.AccountLifecycles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == authority.RealActor.UserId, ct);
        if (lifecycle?.Status is AccountLifecycleStatus.Suspended or AccountLifecycleStatus.Disabled
            or AccountLifecycleStatus.Closed or AccountLifecycleStatus.Revoked or AccountLifecycleStatus.Cancelled
            || !await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == authority.RealActor.UserId
                && x.Role == ActorRole.PlatformAdmin && x.SubjectId == authority.RealActor.UserId && x.IsActive, ct))
            throw new ApplicationFailure(FailureKind.Forbidden, "The Platform Admin account is not active.");
    }
    private static string Reason(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 500
        ? value.Trim() : throw new ApplicationFailure(FailureKind.Validation, "Enter a concise deletion reason.");
    private static void Key(string key) { if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
        throw new ApplicationFailure(FailureKind.Validation, "A request reference is required."); }
    private static ApplicationFailure Conflict() => new(FailureKind.IdempotencyConflict, "This request reference was already used.");
}

public sealed class AccountIdentityDeletionProcessor(WeymelaDbContext db, IAccountIdentityDeletionProvider provider, TimeProvider clock)
{
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    public async Task<int> ProcessAsync(CancellationToken ct)
    {
        if (!provider.Enabled) return 0;
        var processed = 0;
        for (var i = 0; i < 10; i++)
        {
            var selected = await new EfUnitOfWork(db).ExecuteAsync(async token =>
            {
                var now = clock.GetUtcNow().UtcDateTime;
                var row = (await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM v3.\"OutboxMessages\" WHERE \"EventType\"='AccountIdentityDeletion' AND \"ProcessedAtUtc\" IS NULL AND \"FailedAtUtc\" IS NULL AND (\"NextAttemptAtUtc\" IS NULL OR \"NextAttemptAtUtc\"<={now}) ORDER BY \"OccurredAtUtc\", \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(token)).SingleOrDefault();
                if (row is null) return null;
                using var document = JsonDocument.Parse(row.Payload);
                var data = document.RootElement;
                var userId = data.GetProperty("UserId").GetGuid();
                var adminId = data.GetProperty("AdminUserId").GetGuid();
                var correlation = data.GetProperty("CorrelationId").GetGuid();
                var lifecycle = await db.AccountLifecycles.AsNoTracking().SingleAsync(x => x.UserId == userId, token);
                if (lifecycle.Status != AccountLifecycleStatus.Closed) throw new InvalidOperationException("Deletion state is invalid.");
                var binding = await db.IdentityBindings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, token);
                row.AttemptCount++;
                row.NextAttemptAtUtc = now.Add(ClaimLease);
                return new ClaimedDeletion(row.Id, userId, adminId, correlation,
                    binding?.ProjectId, binding?.ExternalSubject);
            }, ct);
            if (selected is null) break;

            try
            {
                // The closure and claim are committed before calling the external
                // provider. Firebase's UserNotFound result is idempotent on replay.
                if (selected.ProjectId is not null && selected.ExternalSubject is not null)
                    await provider.DeleteAsync(selected.ProjectId, selected.ExternalSubject, ct);

                await new EfUnitOfWork(db).ExecuteAsync(async token =>
                {
                    var now = clock.GetUtcNow().UtcDateTime;
                    var row = (await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM v3.\"OutboxMessages\" WHERE \"Id\"={selected.Id} FOR UPDATE").ToListAsync(token)).Single();
                    if (row.ProcessedAtUtc is not null) return true;
                    var lifecycle = await db.AccountLifecycles.AsNoTracking().SingleAsync(x => x.UserId == selected.UserId, token);
                    if (lifecycle.Status != AccountLifecycleStatus.Closed) throw new InvalidOperationException("Deletion state is invalid.");
                    var identifiers = await db.AuthIdentifiers.AsNoTracking().Where(x => x.UserId == selected.UserId).Select(x => x.Id).ToListAsync(token);
                    foreach (var id in identifiers)
                    {
                        var tombstone = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                        await db.AuthIdentifiers.Where(x => x.Id == id).ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.IdentifierHash, tombstone)
                            .SetProperty(x => x.DeliveryAddress, (string?)null)
                            .SetProperty(x => x.IsVerified, false), token);
                    }
                    db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "EntireAccountDeletionCompleted", selected.AdminId,
                        null, null, null, selected.Correlation, now, "external-identity-deleted; identifiers-released",
                        TargetUserId: selected.UserId, Operation: "delete-entire-account"));
                    row.ProcessedAtUtc = now; row.LastError = null; row.NextAttemptAtUtc = null;
                    row.FailureCount = 0;
                    return true;
                }, ct);
                processed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                await new EfUnitOfWork(db).ExecuteAsync(async token =>
                {
                    var row = (await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM v3.\"OutboxMessages\" WHERE \"Id\"={selected.Id} FOR UPDATE").ToListAsync(token)).Single();
                    if (row.ProcessedAtUtc is not null) return true;
                    row.FailureCount++; row.LastError = "ExternalIdentityDeletionUnavailable";
                    var now = clock.GetUtcNow().UtcDateTime;
                    if (row.FailureCount >= 5) { row.FailedAtUtc = now; row.NextAttemptAtUtc = null; }
                    else row.NextAttemptAtUtc = now.AddSeconds(Math.Min(300, 5 * Math.Pow(2, row.FailureCount)));
                    return true;
                }, ct);
                processed++;
            }
        }
        return processed;
    }

    private sealed record ClaimedDeletion(Guid Id, Guid UserId, Guid AdminId, Guid Correlation,
        string? ProjectId, string? ExternalSubject);
}
