using Microsoft.EntityFrameworkCore;
using Weymela.Domain;

namespace Weymela.Infrastructure.Persistence.Repositories;

public sealed class FinancialJournalRepository(WeymelaDbContext db)
{
    public void Append(FinancialJournal journal)
    {
        if (!journal.IsPosted) throw new InvalidOperationException("Only posted journals may be appended.");
        db.FinancialJournals.Add(journal);
    }
    public Task<FinancialJournal?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.FinancialJournals.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
}

public sealed class CreatorEarningsRepository(WeymelaDbContext db)
{
    public Task<CreatorEarningsAccount?> GetAsync(Guid creatorId, CancellationToken ct = default) =>
        db.CreatorEarningsAccounts.Include(x => x.Entries).SingleOrDefaultAsync(x => x.CreatorId == creatorId, ct);
    // Account and entries are staged together; caller links new entries to their authoritative journal.
    public void Add(CreatorEarningsAccount account) => db.CreatorEarningsAccounts.Add(account);
}

public sealed class CustomerCashbackRepository(WeymelaDbContext db)
{
    public Task<CustomerCashbackAccount?> GetAsync(Guid customerId, CancellationToken ct = default) =>
        db.CustomerCashbackAccounts.Include(x => x.Entries).SingleOrDefaultAsync(x => x.CustomerId == customerId, ct);
    public void Add(CustomerCashbackAccount account) => db.CustomerCashbackAccounts.Add(account);
}

public sealed record PlatformRevenueSummary(Money Accrued, Money Settled, Money Unsettled);
public sealed class PlatformRevenueRepository(WeymelaDbContext db)
{
    public async Task<PlatformRevenueSummary> SummaryAsync(CancellationToken ct = default)
    {
        // Status on old domain entries is descriptive; settlement records determine actual settled amount.
        var entries = await db.PlatformRevenueEntries.AsNoTracking().Select(x => x.Amount).ToListAsync(ct);
        var settlements = await db.PlatformSettlements.AsNoTracking().Select(x => x.Amount).ToListAsync(ct);
        var accrued = new Money(entries.Sum(x => x.Amount));
        var settled = new Money(settlements.Sum(x => x.Amount));
        return new(accrued, settled, accrued.Subtract(settled));
    }
}
