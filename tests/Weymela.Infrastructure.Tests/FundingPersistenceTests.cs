using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Transactions;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class FundingPersistenceTests(PostgresFixture fixture)
{
    [Fact] public async Task Funding_moves_exact_budget_from_available_to_reserved_without_changing_total()
    {
        var s = await Scenario.Create(fixture, funded: true); await using var db = s.Database.Open();
        var w = await db.BusinessWallets.SingleAsync(); Assert.Equal(4000, w.AvailableBalance.Amount);
        Assert.Equal(6000, w.ReservedBalance.Amount); Assert.Equal(10000, w.TotalBalance.Amount);
        var p = await db.Promotions.SingleAsync(); Assert.Equal(PromotionStatus.Funded, p.Status);
        Assert.Equal(p.TotalBudget, p.ReservedBudget);
    }

    [Fact] public async Task Funding_snapshot_reservation_journal_and_outbox_round_trip()
    {
        var s = await Scenario.Create(fixture, funded: true); await using var db = s.Database.Open();
        var p = await db.Promotions.SingleAsync(); Assert.Equal(s.PricingVersionId, p.PricingSnapshot.ConfigurationVersionId);
        Assert.Equal(200, p.PricingSnapshot.CreatorEarning.Amount);
        var r = await db.PromotionReservations.SingleAsync();
        var j = await db.FinancialJournals.SingleAsync(x => x.Id == r.JournalId);
        Assert.Equal(JournalSourceType.PromotionReservation, j.SourceType);
        Assert.True(await db.OutboxMessages.AnyAsync(x => x.EventType == nameof(PromotionFunded)));
    }

    [Fact] public async Task Funding_replay_with_new_request_time_does_not_reserve_twice()
    {
        var s = await Scenario.Create(fixture, funded: true); await using var db = s.Database.Open();
        var id = await new FinancialCommands(db).FundPromotionAsync(new(s.Business, s.PromotionId, 0, 1, "seed-fund", Scenario.Now.AddDays(1)));
        Assert.Equal(s.PromotionId, id);
        Assert.Equal(6000, (await db.BusinessWallets.SingleAsync()).ReservedBalance.Amount);
        Assert.Single(await db.PromotionReservations.ToListAsync());
    }

    [Fact] public async Task Insufficient_available_funds_leave_campaign_wallet_journal_and_outbox_unchanged()
    {
        var s = await Scenario.Create(fixture, 100); await using var db = s.Database.Open();
        var before = await db.OutboxMessages.CountAsync();
        var ex = await Assert.ThrowsAsync<ApplicationFailure>(() => new FinancialCommands(db).FundPromotionAsync(new(s.Business, s.PromotionId, 0, 1, "fund", Scenario.Now)));
        Assert.Equal(FailureKind.InsufficientFunds, ex.Kind);
        Assert.Equal(PromotionStatus.Draft, (await db.Promotions.SingleAsync()).Status);
        Assert.Equal(100, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
        Assert.Empty(await db.PromotionReservations.ToListAsync()); Assert.Equal(before, await db.OutboxMessages.CountAsync());
    }

    [Fact] public async Task Stale_wallet_token_rolls_back_campaign_and_financial_side_effects()
    {
        var s = await Scenario.Create(fixture); await using var db = s.Database.Open();
        var ex = await Assert.ThrowsAsync<ApplicationFailure>(() => new FinancialCommands(db).FundPromotionAsync(new(s.Business, s.PromotionId, 0, 0, "stale", Scenario.Now)));
        Assert.Equal(FailureKind.ConcurrencyConflict, ex.Kind);
        Assert.Equal(PromotionStatus.Draft, (await db.Promotions.SingleAsync()).Status);
        Assert.Empty(await db.PromotionReservations.ToListAsync());
        Assert.False(await db.IdempotencyRecords.AnyAsync(x => x.Key == "stale"));
    }

    [Fact] public async Task Two_independent_sessions_cannot_reserve_the_same_available_funds()
    {
        var s = await Scenario.Create(fixture);
        await using var first = s.Database.Open(); await using var second = s.Database.Open();
        var secondWallet = await second.BusinessWallets.SingleAsync();
        await new FinancialCommands(first).FundPromotionAsync(new(s.Business, s.PromotionId, 0, 1, "winner", Scenario.Now));
        var ex = await Assert.ThrowsAsync<ApplicationFailure>(() => new EfUnitOfWork(second).ExecuteAsync(async ct =>
        {
            secondWallet.ReserveForPromotion(new Money(6000), Scenario.Now, Guid.NewGuid());
            await new BusinessWalletRepository(second).SaveAsync(secondWallet, 1, ct);
            return true;
        }));
        Assert.Equal(FailureKind.ConcurrencyConflict, ex.Kind);
        await using var read = s.Database.Open(); Assert.Equal(4000, (await read.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
    }

    [Fact] public async Task Another_business_cannot_fund_campaign()
    {
        var s = await Scenario.Create(fixture); await using var db = s.Database.Open();
        var ex = await Assert.ThrowsAsync<ApplicationFailure>(() => new FinancialCommands(db).FundPromotionAsync(new(s.Business with { BusinessId = Guid.NewGuid() }, s.PromotionId, 0, 1, "wrong-owner", Scenario.Now)));
        Assert.Equal(FailureKind.Forbidden, ex.Kind);
    }
}
