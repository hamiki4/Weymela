using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Notifications;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Operations;

public sealed class WorkerPump(WeymelaDbContext db, RuntimeOptions options, INotificationPushProvider push, TimeProvider clock)
{
    public Task<int> RecordFailureAsync(CancellationToken ct) => db.WorkerCheckpoints.Where(x => x.Name == "operational-worker")
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAtUtc, clock.GetUtcNow().UtcDateTime)
            .SetProperty(x => x.LastErrorCode, "WorkerUnavailable").SetProperty(x => x.Version, x => x.Version + 1), ct);
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await ScheduleAsync(ct);
        var outbox = new OutboxProcessor(db, options, push, clock);
        var count = await outbox.ProcessAsync(ct); await outbox.PushAsync(ct);
        await ObserveExpiredQrAsync(ct);
        await new EfUnitOfWork(db).ExecuteAsync(async token =>
        {
            var checkpoint = await db.WorkerCheckpoints.SingleAsync(x => x.Name == "operational-worker", token);
            checkpoint.LastSeenAtUtc = clock.GetUtcNow().UtcDateTime; checkpoint.LastSuccessAtUtc = checkpoint.LastSeenAtUtc; checkpoint.LastErrorCode = null;
            return true;
        }, ct);
        return count;
    }
    private Task<bool> ScheduleAsync(CancellationToken ct) => new EfUnitOfWork(db).ExecuteAsync(async token =>
    {
        // A transaction-scoped V3-only lock serializes scheduler/checkpoint creation across worker instances.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7646630006)", token);
        var now = clock.GetUtcNow().UtcDateTime;
        var checkpoint = await db.WorkerCheckpoints.SingleOrDefaultAsync(x => x.Name == "operational-worker", token);
        if (checkpoint is null) { checkpoint = new() { Name = "operational-worker" }; db.WorkerCheckpoints.Add(checkpoint); }
        checkpoint.LastSeenAtUtc = now;
        var version = await db.FinancialConfigurationVersions.AsNoTracking().Where(x => x.EffectiveFromUtc <= now)
            .OrderByDescending(x => x.EffectiveFromUtc).ThenByDescending(x => x.Version).ThenByDescending(x => x.Id).FirstOrDefaultAsync(token);
        if (version is not null && checkpoint.LastEffectiveConfigurationId != version.Id)
        {
            db.OutboxMessages.Add(new() { EventType = "FinancialConfigurationEffective", OccurredAtUtc = now,
                Payload = JsonSerializer.Serialize(new { VersionId = version.Id, PreviousVersionId = checkpoint.LastEffectiveConfigurationId }) });
            checkpoint.LastEffectiveConfigurationId = version.Id;
        }
        return true;
    }, ct);
    private Task<int> ObserveExpiredQrAsync(CancellationToken ct) => new EfUnitOfWork(db).ExecuteAsync(async token =>
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.OfferQrSessions.FromSqlInterpolated($"SELECT *, xmin FROM v3.\"OfferQrSessions\" WHERE \"Status\"='Issued' AND \"ExpiresAtUtc\"<={now} ORDER BY \"ExpiresAtUtc\" LIMIT {options.RecipientBatchSize} FOR UPDATE SKIP LOCKED").ToListAsync(token);
        foreach (var row in rows) row.ObserveExpiry(now);
        return rows.Count;
    }, ct);
}
