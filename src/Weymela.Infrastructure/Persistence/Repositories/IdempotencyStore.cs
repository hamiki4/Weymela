using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Persistence.Repositories;

public sealed class IdempotencyStore(WeymelaDbContext db) : IIdempotencyStore
{
    public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken ct) =>
        throw new InvalidOperationException("Persistent idempotency lookups require Actor + Operation + Key scope.");
    public async Task<IdempotencyRecord?> FindAsync(string key, string operationType, Guid actorId, CancellationToken ct)
    {
        var r = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.ActorId == actorId && x.OperationType == operationType && x.Key == key, ct);
        return r is null ? null : new(r.Key, r.OperationType, r.ActorId, r.RequestFingerprint, r.ResultReference, r.CreatedAtUtc);
    }
    public Task SaveAsync(IdempotencyRecord r, CancellationToken ct)
    {
        db.IdempotencyRecords.Add(new(r.ActorId, r.OperationType, r.Key, r.RequestFingerprint, r.ResultReference?.ToString() ?? "", r.CreatedAtUtc));
        return Task.CompletedTask;
    }
}
