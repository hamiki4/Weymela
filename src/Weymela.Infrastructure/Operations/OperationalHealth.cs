using Microsoft.EntityFrameworkCore;
using Weymela.Application;
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
            if (!await db.Database.CanConnectAsync(bound.Token) || (await db.Database.GetPendingMigrationsAsync(bound.Token)).Any()) return new(false, "not_ready");
            if (!options.Development && (!await db.FinancialConfigurationVersions.AnyAsync(x => x.EffectiveFromUtc <= clock.GetUtcNow().UtcDateTime, bound.Token)
                || !await db.IdentityBindings.AnyAsync(x => x.IsActive && x.Provider == "Firebase" && x.ProjectId == options.FirebaseProjectId && db.CommercePermissions.Any(p => p.UserId == x.UserId && p.Role == ActorRole.PlatformAdmin && p.IsActive), bound.Token))) return new(false, "not_ready");
            if (options.WorkerEnabled)
            {
                var since = clock.GetUtcNow().UtcDateTime.AddSeconds(-Math.Max(60, options.WorkerIntervalSeconds * 4));
                if (!await db.WorkerCheckpoints.AnyAsync(x => x.Name == "operational-worker" && x.LastSuccessAtUtc >= since, bound.Token)) return new(false, "not_ready");
            }
            return new(true, "ready");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new(false, "not_ready"); }
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
}
