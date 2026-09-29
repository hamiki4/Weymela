using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
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

public sealed class AccountDeletionService(WeymelaDbContext db, TimeProvider clock, IAccountIdentityDeletionProvider provider)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

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
    public async Task<int> ProcessAsync(CancellationToken ct)
    {
        if (!provider.Enabled) return 0;
        var processed = 0;
        for (var i = 0; i < 10; i++)
        {
            Guid? selected = null;
            try
            {
                var found = await new EfUnitOfWork(db).ExecuteAsync(async token =>
                {
                    var now = clock.GetUtcNow().UtcDateTime;
                    var row = (await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM v3.\"OutboxMessages\" WHERE \"EventType\"='AccountIdentityDeletion' AND \"ProcessedAtUtc\" IS NULL AND \"FailedAtUtc\" IS NULL AND (\"NextAttemptAtUtc\" IS NULL OR \"NextAttemptAtUtc\"<={now}) ORDER BY \"OccurredAtUtc\", \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(token)).SingleOrDefault();
                    if (row is null) return false;
                    selected = row.Id;
                    using var document = JsonDocument.Parse(row.Payload);
                    var data = document.RootElement;
                    var userId = data.GetProperty("UserId").GetGuid();
                    var adminId = data.GetProperty("AdminUserId").GetGuid();
                    var correlation = data.GetProperty("CorrelationId").GetGuid();
                    var lifecycle = await db.AccountLifecycles.AsNoTracking().SingleAsync(x => x.UserId == userId, token);
                    if (lifecycle.Status != AccountLifecycleStatus.Closed) throw new InvalidOperationException("Deletion state is invalid.");
                    var binding = await db.IdentityBindings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, token);
                    if (binding is not null) await provider.DeleteAsync(binding.ProjectId, binding.ExternalSubject, token);
                    var identifiers = await db.AuthIdentifiers.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync(token);
                    foreach (var id in identifiers)
                    {
                        var tombstone = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                        await db.AuthIdentifiers.Where(x => x.Id == id).ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.IdentifierHash, tombstone)
                            .SetProperty(x => x.DeliveryAddress, (string?)null)
                            .SetProperty(x => x.IsVerified, false), token);
                    }
                    var credential = await db.PasswordCredentials.SingleOrDefaultAsync(x => x.UserId == userId, token);
                    if (credential is not null) db.PasswordCredentials.Remove(credential);
                    db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "EntireAccountDeletionCompleted", adminId,
                        null, null, null, correlation, now, "external-identity-deleted; identifiers-released",
                        TargetUserId: userId, Operation: "delete-entire-account"));
                    row.ProcessedAtUtc = now; row.AttemptCount++; row.LastError = null; row.NextAttemptAtUtc = null;
                    return true;
                }, ct);
                if (!found) break;
                processed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                if (selected is null) throw;
                await new EfUnitOfWork(db).ExecuteAsync(async token =>
                {
                    var row = await db.OutboxMessages.SingleAsync(x => x.Id == selected.Value, token);
                    if (row.ProcessedAtUtc is not null) return true;
                    row.AttemptCount++; row.FailureCount++; row.LastError = "ExternalIdentityDeletionUnavailable";
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
}
