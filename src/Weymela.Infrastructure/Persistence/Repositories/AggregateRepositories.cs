using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;

namespace Weymela.Infrastructure.Persistence.Repositories;

internal static class ExpectedVersion
{
    public static void Check<T>(WeymelaDbContext db, T entity, long expected) where T : class
    {
        var entry = db.Entry(entity);
        if (entry.State == EntityState.Detached || entry.Property<long>("Version").OriginalValue != expected)
            throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "The aggregate changed. Reload before submitting another operation.");
    }
}

public sealed class BusinessWalletRepository(WeymelaDbContext db) : IBusinessWalletRepository
{
    public Task<BusinessWallet?> GetAsync(Guid businessId, CancellationToken ct) => db.BusinessWallets.SingleOrDefaultAsync(x => x.BusinessId == businessId, ct);
    public Task SaveAsync(BusinessWallet wallet, long expectedVersion, CancellationToken ct)
    { ExpectedVersion.Check(db, wallet, expectedVersion); return Task.CompletedTask; }
}

public sealed class PromotionRepository(WeymelaDbContext db) : IPromotionRepository
{
    public Task<Promotion?> GetAsync(Guid id, CancellationToken ct)
    {
        // Preserve the loaded aggregate snapshot. Re-querying only its children can mix
        // current allocations with stale parent totals/version and misclassify a race.
        var local = db.Promotions.Local.SingleOrDefault(x => x.Id == id);
        if (local is not null && db.Entry(local).Collection(x => x.Allocations).IsLoaded
            && db.Entry(local).Collection(x => x.Platforms).IsLoaded)
            return Task.FromResult<Promotion?>(local);
        return db.Promotions.Include(x => x.Allocations).Include(x => x.Platforms).SingleOrDefaultAsync(x => x.Id == id, ct);
    }
    public Task AddAsync(Promotion p, CancellationToken ct) { db.Promotions.Add(p); return Task.CompletedTask; }
    public Task SaveAsync(Promotion p, long expectedVersion, CancellationToken ct)
    { ExpectedVersion.Check(db, p, expectedVersion); return Task.CompletedTask; }
    public async Task<IReadOnlyList<Promotion>> QueryAsync(CancellationToken ct) => await db.Promotions.Include(x => x.Allocations).Include(x => x.Platforms).ToListAsync(ct);
}

public sealed class CreatorApplicationRepository(WeymelaDbContext db) : ICreatorApplicationRepository
{
    public Task<CreatorApplication?> FindActiveAsync(Guid promotionId, Guid creatorId, CancellationToken ct) => db.CreatorApplications.SingleOrDefaultAsync(
        x => x.PromotionId == promotionId && x.CreatorId == creatorId && (x.Status == CreatorApplicationStatus.Pending || x.Status == CreatorApplicationStatus.Approved), ct);
    public Task<CreatorApplication?> GetAsync(Guid id, CancellationToken ct) => db.CreatorApplications.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task AddAsync(CreatorApplication a, CancellationToken ct) { db.CreatorApplications.Add(a); return Task.CompletedTask; }
    public Task SaveAsync(CreatorApplication a, CancellationToken ct) => Task.CompletedTask;
}

public sealed class CreatorAllocationRepository(WeymelaDbContext db) : IAllocationRepository
{
    public Task<CreatorAllocation?> GetAsync(Guid id, CancellationToken ct) => db.CreatorAllocations.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task AddAsync(CreatorAllocation a, CancellationToken ct) { db.CreatorAllocations.Add(a); return Task.CompletedTask; }
    public Task SaveAsync(CreatorAllocation a, long expectedVersion, CancellationToken ct)
    { ExpectedVersion.Check(db, a, expectedVersion); return Task.CompletedTask; }
}
