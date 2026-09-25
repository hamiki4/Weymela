using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Infrastructure.Operations;

public sealed record ReadinessResult(bool Ready, string Status);
public sealed class OperationalHealth(WeymelaDbContext db, RuntimeOptions options, TimeProvider clock)
{
    public async Task<ReadinessResult> ReadinessAsync(CancellationToken ct)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct); bound.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            if (options.EnvironmentName == "Pilot" && (options.EmailDeliveryMode != "Resend"
                || options.FirebaseCustomTokenMode != "FirebaseAdmin")) return new(false, "not_ready");
            if (!await db.Database.CanConnectAsync(bound.Token) || (await db.Database.GetPendingMigrationsAsync(bound.Token)).Any()) return new(false, "not_ready");
            if (!options.Development && (!await db.FinancialConfigurationVersions.AnyAsync(x => x.EffectiveFromUtc <= clock.GetUtcNow().UtcDateTime, bound.Token)
                || !await RequiredAccountLegalReadyAsync(bound.Token) || !await PlatformAdminReadyAsync(bound.Token)))
                return new(false, "not_ready");
            if (options.WorkerEnabled)
            {
                var since = clock.GetUtcNow().UtcDateTime.AddSeconds(-Math.Max(60, options.WorkerIntervalSeconds * 4));
                if (!await db.WorkerCheckpoints.AnyAsync(x => x.Name == "operational-worker"
                    && x.LastSeenAtUtc >= since && x.LastSuccessAtUtc >= since
                    && x.LastSuccessAtUtc <= x.LastSeenAtUtc && x.LastErrorCode == null, bound.Token))
                    return new(false, "not_ready");
            }
            return new(true, "ready");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new(false, "not_ready"); }
    }
    private async Task<bool> RequiredAccountLegalReadyAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.LegalDocumentVersions.AnyAsync(x => x.Type == LegalDocumentType.TermsOfService
                && x.EffectiveFromUtc <= now && x.ContentHash.Trim() != "", ct)
            && await db.LegalDocumentVersions.AnyAsync(x => x.Type == LegalDocumentType.PrivacyPolicy
                && x.EffectiveFromUtc <= now && x.ContentHash.Trim() != "", ct);
    }
    private async Task<bool> PlatformAdminReadyAsync(CancellationToken ct)
    {
        var permissions = await db.CommercePermissions.AsNoTracking()
            .Where(x => x.Role == ActorRole.PlatformAdmin && x.IsActive).ToListAsync(ct);
        if (permissions.Count == 0 || permissions.Any(x => x.SubjectId != x.UserId || x.BusinessId != null)
            || permissions.GroupBy(x => x.UserId).Any(x => x.Count() != 1)) return false;
        var userIds = permissions.Select(x => x.UserId).ToArray();
        var bindings = await db.IdentityBindings.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return permissions.All(permission => bindings.Count(x => x.UserId == permission.UserId && x.IsActive
            && x.Provider == "Firebase" && x.ProjectId == options.FirebaseProjectId
            && !string.IsNullOrWhiteSpace(x.ExternalSubject) && x.Version > 0 && x.ValidAfterUtc <= now) == 1)
            && bindings.Where(x => x.IsActive).All(x => x.Provider == "Firebase" && x.ProjectId == options.FirebaseProjectId
                && !string.IsNullOrWhiteSpace(x.ExternalSubject) && x.Version > 0 && x.ValidAfterUtc <= now);
    }
    public async Task<object> DetailsAsync(CancellationToken ct)
    {
        var ready = await ReadinessAsync(ct);
        var checkpoint = await db.WorkerCheckpoints.AsNoTracking().SingleOrDefaultAsync(x => x.Name == "operational-worker", ct);
        return new { readiness = ready.Status, worker = new { options.WorkerEnabled, checkpoint?.LastSeenAtUtc, checkpoint?.LastSuccessAtUtc, checkpoint?.LastErrorCode },
            outboxBacklog = await db.OutboxMessages.CountAsync(x => x.ProcessedAtUtc == null && x.FailedAtUtc == null, ct),
            oldestPendingOutboxAtUtc = await db.OutboxMessages.Where(x => x.ProcessedAtUtc == null && x.FailedAtUtc == null).Select(x => (DateTime?)x.OccurredAtUtc).MinAsync(ct),
            outboxFailures = await db.OutboxMessages.CountAsync(x => x.FailedAtUtc != null, ct),
            notificationFailures = await db.InAppNotifications.CountAsync(x => x.PushState == Persistence.Records.PushDeliveryState.Failed, ct),
            metrics = OperationalTelemetry.Snapshot(), options.DepositMode, options.SocialMode, options.FinancialWritesEnabled };
    }

    public async Task<object> OperationsDetailsAsync(CancellationToken ct)
    {
        var ready = await ReadinessAsync(ct);
        var checkpoint = await db.WorkerCheckpoints.AsNoTracking().SingleOrDefaultAsync(x => x.Name == "operational-worker", ct);
        return new
        {
            readiness = ready.Status,
            worker = new { options.WorkerEnabled, checkpoint?.LastSeenAtUtc, checkpoint?.LastSuccessAtUtc, checkpoint?.LastErrorCode },
            outboxBacklog = await db.OutboxMessages.CountAsync(x => x.ProcessedAtUtc == null && x.FailedAtUtc == null, ct),
            oldestPendingOutboxAtUtc = await db.OutboxMessages.Where(x => x.ProcessedAtUtc == null && x.FailedAtUtc == null)
                .Select(x => (DateTime?)x.OccurredAtUtc).MinAsync(ct),
            outboxFailures = await db.OutboxMessages.CountAsync(x => x.FailedAtUtc != null, ct),
            notificationFailures = await db.InAppNotifications.CountAsync(x => x.PushState == Persistence.Records.PushDeliveryState.Failed, ct),
            metrics = OperationalTelemetry.Snapshot()
        };
    }
}
