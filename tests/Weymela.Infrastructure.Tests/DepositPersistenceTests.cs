using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Transactions;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class DepositPersistenceTests(PostgresFixture fixture)
{
    [Fact] public async Task Arbitrary_positive_deposit_persists_exact_cents()
    {
        var s = await Scenario.Create(fixture, 0);
        await using (var db = s.Database.Open())
            await new FinancialCommands(db).CreditDepositAsync(new(s.Business, new Money(37.25m), "deposit", Scenario.Now), 0);
        await using var read = s.Database.Open();
        var w = await read.BusinessWallets.SingleAsync();
        Assert.Equal(new Money(37.25m), w.AvailableBalance);
        Assert.Equal(w.TotalBalance, w.AvailableBalance.Add(w.ReservedBalance));
    }

    [Fact] public async Task Deposit_replay_returns_original_reference_and_never_credits_twice()
    {
        var s = await Scenario.Create(fixture, 0);
        var c = new CreditBusinessDepositCommand(s.Business, new Money(7.15m), "deposit", Scenario.Now);
        await using var db = s.Database.Open(); var commands = new FinancialCommands(db);
        var first = await commands.CreditDepositAsync(c, 0);
        var replay = await commands.CreditDepositAsync(c with { Now = Scenario.Now.AddHours(1) }, 0);
        Assert.Equal(first, replay);
        Assert.Equal(7.15m, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
        Assert.Single(await db.IdempotencyRecords.ToListAsync());
        Assert.Single(await db.FinancialJournals.ToListAsync());
    }

    [Fact] public async Task Conflicting_deposit_fingerprint_is_rejected_without_balance_change()
    {
        var s = await Scenario.Create(fixture, 0);
        await using var db = s.Database.Open(); var commands = new FinancialCommands(db);
        var c = new CreditBusinessDepositCommand(s.Business, new Money(10), "deposit", Scenario.Now);
        await commands.CreditDepositAsync(c, 0);
        var ex = await Assert.ThrowsAsync<ApplicationFailure>(() => commands.CreditDepositAsync(c with { Amount = new Money(20) }, 1));
        Assert.Equal(FailureKind.IdempotencyConflict, ex.Kind);
        Assert.Equal(10, (await db.BusinessWallets.SingleAsync()).TotalBalance.Amount);
    }

    [Fact] public async Task Deposit_posts_balanced_journal_wallet_projection_audit_and_outbox()
    {
        var s = await Scenario.Create(fixture, 42.75m);
        await using var db = s.Database.Open();
        var j = await db.FinancialJournals.Include(x => x.Lines).SingleAsync();
        Assert.True(j.IsPosted); Assert.Equal(JournalSourceType.Deposit, j.SourceType);
        Assert.Equal(0, j.Lines.Sum(x => x.Type == JournalLineType.Debit ? x.Amount.Amount : -x.Amount.Amount));
        Assert.Equal(j.Id, (await db.WalletEntries.SingleAsync()).JournalId);
        Assert.Equal("seed-deposit", j.IdempotencyReference);
        Assert.True(await db.OutboxMessages.AnyAsync(x => x.EventType == nameof(BusinessWalletCredited)));
        Assert.True(await db.AuditEvents.AnyAsync(x => x.EventType == "BusinessWalletCredited"));
    }

    [Fact] public async Task Failure_after_database_flush_rolls_back_every_record()
    {
        var s = await Scenario.Create(fixture, 0);
        await using var db = s.Database.Open();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfUnitOfWork(db).ExecuteAsync<bool>(async ct =>
        {
            var w = await db.BusinessWallets.SingleAsync(ct); w.CreditDeposit(new Money(99), Scenario.Now, Guid.NewGuid());
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("Injected after flush, before commit");
        }));
        await using var read = s.Database.Open();
        Assert.Equal(0, (await read.BusinessWallets.SingleAsync()).TotalBalance.Amount);
        Assert.Empty(await read.FinancialJournals.ToListAsync());
        Assert.Empty(await read.IdempotencyRecords.ToListAsync());
    }

    [Theory] [InlineData(0)] [InlineData(-1)]
    public async Task Nonpositive_deposits_do_not_persist(decimal amount)
    {
        var s = await Scenario.Create(fixture, 0); await using var db = s.Database.Open();
        await Assert.ThrowsAnyAsync<Exception>(() => new FinancialCommands(db).CreditDepositAsync(new(s.Business, new Money(amount), "invalid", Scenario.Now), 0));
        Assert.Equal(0, (await db.BusinessWallets.SingleAsync()).TotalBalance.Amount);
    }
}
