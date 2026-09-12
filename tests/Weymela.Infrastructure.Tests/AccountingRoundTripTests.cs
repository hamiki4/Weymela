using Microsoft.EntityFrameworkCore;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Transactions;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class AccountingRoundTripTests(PostgresFixture fixture)
{
    [Fact] public async Task Creator_earnings_account_and_entries_round_trip_with_journal_reference()
    {
        var s = await Scenario.Create(fixture); var creator = Guid.NewGuid(); await using var db = s.Database.Open();
        var account = new CreatorEarningsAccount(creator);
        account.Earn(new Money(25.75m), EarningSource.ViewReward, s.PromotionId, Scenario.Now, Guid.NewGuid());
        var j = Journal(JournalSourceType.ViewReward, 25.75m, "CreatorPayable");
        db.FinancialJournals.Add(j); db.CreatorEarningsAccounts.Add(account);
        db.Entry(account.Entries.Single()).Property("JournalId").CurrentValue = j.Id;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = (await new CreatorEarningsRepository(db).GetAsync(creator))!;
        Assert.Equal(25.75m, loaded.AvailableEarnings.Amount);
        Assert.Equal(EarningSource.ViewReward, loaded.Entries.Single().Source);
    }

    [Fact] public async Task Platform_accrued_settled_and_unsettled_reconcile()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var j = Journal(JournalSourceType.ViewReward, 100, "PlatformRevenue");
        var revenue = new PlatformRevenueEntry(Guid.NewGuid(), null, PlatformRevenueSource.ViewRewardPlatformShare, new Money(100), RevenueStatus.Accrued, Scenario.Now, j.CorrelationId);
        db.FinancialJournals.Add(j); db.PlatformRevenueEntries.Add(revenue); db.Entry(revenue).Property("JournalId").CurrentValue = j.Id;
        var settlementJournal = Journal(JournalSourceType.Settlement, 30, "SettlementClearing");
        var settlement = new PlatformSettlement(new Money(30), "settlement-1", Scenario.Now);
        db.FinancialJournals.Add(settlementJournal); db.PlatformSettlements.Add(settlement);
        db.Entry(settlement).Property("JournalId").CurrentValue = settlementJournal.Id;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var summary = await new PlatformRevenueRepository(db).SummaryAsync();
        Assert.Equal(100, summary.Accrued.Amount); Assert.Equal(30, summary.Settled.Amount); Assert.Equal(70, summary.Unsettled.Amount);
    }

    [Fact] public async Task Platform_cannot_settle_more_than_accrued()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        await Assert.ThrowsAnyAsync<Exception>(() => new EfUnitOfWork(db).ExecuteAsync(async ct =>
        {
            var journal = Journal(JournalSourceType.Settlement, 1, "SettlementClearing");
            var settlement = new PlatformSettlement(new Money(1), "over", Scenario.Now);
            db.FinancialJournals.Add(journal); db.PlatformSettlements.Add(settlement);
            db.Entry(settlement).Property("JournalId").CurrentValue = journal.Id;
            await Task.CompletedTask; return true;
        }));
        Assert.Empty(await db.PlatformSettlements.ToListAsync());
    }

    [Fact] public async Task Wallet_cannot_be_credited_without_authoritative_journal()
    {
        var s = await Scenario.Create(fixture); await using var db = s.Database.Open();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE v3.\"BusinessWallets\" SET \"AvailableBalance\"=\"AvailableBalance\"+1"));
    }

    private static FinancialJournal Journal(JournalSourceType source, decimal amount, string credit)
    {
        var j = new FinancialJournal(Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), source, Scenario.Now);
        j.AddLine(JournalLineType.Debit, new Money(amount), "FixtureClearing");
        j.AddLine(JournalLineType.Credit, new Money(amount), credit); j.Post(); return j;
    }
}
