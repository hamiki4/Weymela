using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Notifications;

public sealed class OutboxProcessor(WeymelaDbContext db, RuntimeOptions options, INotificationPushProvider push, TimeProvider clock)
{
    private sealed class DeliveryFailure(Guid id, string code) : Exception { public Guid Id => id; public string Code => code; }
    public async Task<int> ProcessAsync(CancellationToken ct)
    {
        var processed = 0;
        for (var i = 0; i < options.WorkerBatchSize; i++)
        {
            try { if (!await OneAsync(ct)) break; processed++; }
            catch (DeliveryFailure error) { await FailedAsync(error.Id, error.Code, ct); OperationalTelemetry.OutboxFailures.Add(1); }
        }
        return processed;
    }
    private async Task<bool> OneAsync(CancellationToken ct)
    {
        Guid? selected = null;
        try { return await new EfUnitOfWork(db).ExecuteAsync(async token =>
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var row = (await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM v3.\"OutboxMessages\" WHERE \"ProcessedAtUtc\" IS NULL AND \"FailedAtUtc\" IS NULL AND (\"NextAttemptAtUtc\" IS NULL OR \"NextAttemptAtUtc\" <= {now}) ORDER BY \"OccurredAtUtc\", \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(token)).SingleOrDefault();
        if (row is null) return false;
        selected = row.Id;
        try
        {
            var plan = await new NotificationRouter(db).ResolveAsync(row, token);
            row.AttemptCount++;
            if (plan is null || plan.Audiences.Count == 0) { row.ProcessedAtUtc = now; return true; }
            var global = plan.Audiences.Where(x => x.SubjectId is null && x.UserId is null).Select(x => x.Role).ToArray();
            Guid[] Subjects(ActorRole role) => plan.Audiences.Where(x => x.Role == role && x.SubjectId is not null).Select(x => x.SubjectId!.Value).ToArray();
            var businesses = Subjects(ActorRole.Business); var creators = Subjects(ActorRole.Creator); var customers = Subjects(ActorRole.Customer);
            Guid[] Users(ActorRole role) => plan.Audiences.Where(x => x.Role == role && x.UserId is not null).Select(x => x.UserId!.Value).ToArray();
            var businessUsers=Users(ActorRole.Business);var creatorUsers=Users(ActorRole.Creator);var customerUsers=Users(ActorRole.Customer);
            var platformUsers=Users(ActorRole.PlatformAdmin);var operationsUsers=Users(ActorRole.OperationsAdmin);
            var memberships = db.CommercePermissions.AsNoTracking().Where(x => x.IsActive && (global.Contains(x.Role)
                || x.Role == ActorRole.Business && businesses.Contains(x.SubjectId) || x.Role == ActorRole.Creator && creators.Contains(x.SubjectId)
                || x.Role == ActorRole.Customer && customers.Contains(x.SubjectId)
                || x.Role == ActorRole.Business && businessUsers.Contains(x.UserId) || x.Role == ActorRole.Creator && creatorUsers.Contains(x.UserId)
                || x.Role == ActorRole.Customer && customerUsers.Contains(x.UserId) || x.Role == ActorRole.PlatformAdmin && platformUsers.Contains(x.UserId)
                || x.Role == ActorRole.OperationsAdmin && operationsUsers.Contains(x.UserId)));
            var cursor = row.RecipientCursor;
            var users = await memberships.Where(x => cursor == null || x.UserId.CompareTo(cursor.Value) > 0).Select(x => x.UserId).Distinct()
                .OrderBy(x => x).Take(options.RecipientBatchSize).ToListAsync(token);
            var recipients = await memberships.Where(x => users.Contains(x.UserId)).Select(x => new { x.UserId, x.Role }).Distinct().ToListAsync(token);
            var existing = await db.InAppNotifications.AsNoTracking().Where(x => x.SourceKey == plan.SourceKey && users.Contains(x.UserId)).Select(x => new { x.UserId, x.Role }).ToListAsync(token);
            foreach (var recipient in recipients.Where(r => !existing.Contains(r)))
                db.InAppNotifications.Add(new() { UserId = recipient.UserId, Role = recipient.Role, SourceKey = plan.SourceKey, EventType = row.EventType,
                    Title = plan.Title, Message = plan.Message, Route = NotificationRouter.Route(plan, recipient.Role), CreatedAtUtc = now,
                    PushState = push.Enabled ? PushDeliveryState.Pending : PushDeliveryState.NotRequested, NextPushAtUtc = push.Enabled ? now : null });
            if (users.Count < options.RecipientBatchSize) row.ProcessedAtUtc = now;
            else row.RecipientCursor = users[^1];
            row.LastError = null; row.FailureCount = 0; row.NextAttemptAtUtc = null;
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { throw new DeliveryFailure(row.Id, "NotificationEventInvalidOrUnavailable"); }
        }, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DeliveryFailure) { throw; }
        catch when (selected is not null) { throw new DeliveryFailure(selected.Value, "NotificationCommitUnavailable"); }
    }
    private Task<bool> FailedAsync(Guid id, string code, CancellationToken ct) => new EfUnitOfWork(db).ExecuteAsync(async token =>
    {
        var row = (await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM v3.\"OutboxMessages\" WHERE \"Id\"={id} FOR UPDATE").ToListAsync(token)).Single();
        if (row.ProcessedAtUtc is not null) return false;
        row.AttemptCount++; row.FailureCount++; row.LastError = code;
        if (row.FailureCount >= options.RetryLimit) { row.FailedAtUtc = clock.GetUtcNow().UtcDateTime; row.NextAttemptAtUtc = null; }
        else row.NextAttemptAtUtc = clock.GetUtcNow().UtcDateTime.AddSeconds(Math.Min(300, 5 * Math.Pow(2, row.FailureCount)));
        return true;
    }, ct);

    public async Task<int> PushAsync(CancellationToken ct)
    {
        if (!push.Enabled) return 0;
        var count = 0;
        for (var i = 0; i < options.WorkerBatchSize; i++)
        {
            var delivered = await new EfUnitOfWork(db).ExecuteAsync(async token =>
            {
                var now = clock.GetUtcNow().UtcDateTime;
                var row = (await db.InAppNotifications.FromSqlInterpolated($"SELECT *, xmin FROM v3.\"InAppNotifications\" WHERE \"PushState\"='Pending' AND \"NextPushAtUtc\"<={now} ORDER BY \"NextPushAtUtc\", \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(token)).SingleOrDefault();
                if (row is null) return false;
                PushDeliveryResult result;
                try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    result = await push.SendAsync(new(row.Id, row.UserId, row.Role.ToString(), row.Title, row.Route), timeout.Token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { result = new(false, true, "PushUnavailable"); }
                row.PushAttempts++;
                if (result.Delivered) { row.PushState = PushDeliveryState.Delivered; row.LastPushErrorCode = null; row.NextPushAtUtc = null; }
                else
                {
                    // Provider error bodies may contain credentials. Persist only our own bounded classification.
                    row.LastPushErrorCode = result.Retryable ? "PushTemporarilyUnavailable" : "PushRejected"; OperationalTelemetry.NotificationFailures.Add(1);
                    row.PushState = result.Retryable && row.PushAttempts < options.RetryLimit ? PushDeliveryState.Pending : PushDeliveryState.Failed;
                    row.NextPushAtUtc = row.PushState == PushDeliveryState.Pending ? now.AddSeconds(Math.Min(300, 5 * Math.Pow(2, row.PushAttempts))) : null;
                }
                return true;
            }, ct);
            if (!delivered) break; count++;
        }
        return count;
    }
}
