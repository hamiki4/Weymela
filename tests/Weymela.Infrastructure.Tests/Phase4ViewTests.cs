using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Transactions;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class Phase4ViewTests(PostgresFixture fixture)
{
    [Fact] public async Task Go_live_captures_baseline_once_even_with_a_new_request_key()
    {
        var s = await Phase4Scenario.Create(fixture); s.Provider.Count = 9000;
        await using var db = s.Database.Open();
        var id = await s.Views(db).GoLiveAsync(new(s.Creator, s.AllocationId, "TestProvider", "content-1", "repeat"));
        Assert.Equal(s.ParticipationId, id);
        Assert.Equal(1000, (await db.CreatorPromotionParticipations.SingleAsync()).BaselineViews);
        Assert.Single(await db.PromotionViewVerifications.ToListAsync());
    }
    [Fact] public async Task Pause_resume_preserves_baseline_and_reward_history()
    {
        var s = await Phase4Scenario.Create(fixture); await s.Refresh(3000);
        await using var db = s.Database.Open();
        await s.Views(db).SetPausedAsync(s.Creator, s.ParticipationId, true);
        await Assert.ThrowsAsync<ApplicationFailure>(() => s.Refresh(6000, "paused"));
        await s.Views(db).SetPausedAsync(s.Creator, s.ParticipationId, false);
        await s.Refresh(6000, "resumed");
        var p = await db.CreatorPromotionParticipations.AsNoTracking().SingleAsync();
        Assert.Equal(1000, p.BaselineViews); Assert.Equal(6000, p.RewardedViewCount);
    }
    [Fact] public async Task Verified_delta_pays_only_new_complete_blocks_and_carries_1400_views()
    {
        var s = await Phase4Scenario.Create(fixture); await s.Refresh(3000, "first");
        var result = await s.Refresh(7400, "second");
        Assert.Equal(7400, result.CampaignViews); Assert.Equal(6000, result.RewardedViews); Assert.Equal(1400, result.CarryForward);
        await using var db = s.Database.Open();
        Assert.Equal(400, (await db.CreatorEarningsAccounts.SingleAsync()).AvailableEarnings.Amount);
        Assert.Equal(600, (await db.CreatorAllocations.SingleAsync()).UsedAmount.Amount);
        Assert.Equal(200, (await db.PlatformRevenueEntries.ToListAsync()).Sum(x => x.Amount.Amount));
    }
    [Fact] public async Task Incomplete_block_creates_no_financial_obligation()
    {
        var s = await Phase4Scenario.Create(fixture); var result = await s.Refresh(2999);
        Assert.Equal(0, result.RewardedViews);
        await using var db = s.Database.Open(); Assert.Empty(await db.CreatorEarningEntries.ToListAsync());
        Assert.Empty(await db.ViewRewardReceipts.ToListAsync());
    }
    [Fact] public async Task Insufficient_budget_records_evidence_but_never_pays_or_advances_reward_count()
    {
        var s = await Phase4Scenario.Create(fixture, allocation: 299); var result = await s.Refresh(3000);
        Assert.Equal(0, result.RewardedViews); Assert.Equal(ParticipationStatus.FundingRequired, result.Status);
        await using var db = s.Database.Open();
        Assert.Equal(299, (await db.CreatorAllocations.SingleAsync()).RemainingAmount.Amount);
        Assert.Empty(await db.CreatorEarningEntries.ToListAsync()); Assert.Empty(await db.PlatformRevenueEntries.ToListAsync());
        Assert.Contains(await db.OutboxMessages.ToListAsync(), x => x.EventType == "CreatorBudgetExhausted");
    }
    [Fact] public async Task Affordable_complete_block_is_paid_without_partial_second_block()
    {
        var s = await Phase4Scenario.Create(fixture, allocation: 450); var result = await s.Refresh(6000);
        Assert.Equal(3000, result.RewardedViews); Assert.Equal(ParticipationStatus.FundingRequired, result.Status);
        await using var db = s.Database.Open();
        Assert.Equal(150, (await db.CreatorAllocations.SingleAsync()).RemainingAmount.Amount);
        Assert.Equal(200, (await db.CreatorEarningsAccounts.SingleAsync()).AvailableEarnings.Amount);
    }
    [Fact] public async Task Same_key_and_new_refresh_key_with_same_count_never_duplicate_rewards()
    {
        var s = await Phase4Scenario.Create(fixture); var first = await s.Refresh(3000);
        Assert.Equal(first, await s.Refresh(9000)); // replay ignores a later provider count
        await s.Refresh(3000, "another-key");
        await using var db = s.Database.Open(); Assert.Single(await db.ViewRewardReceipts.ToListAsync());
        Assert.Single(await db.CreatorEarningEntries.ToListAsync());
    }
    [Fact] public async Task Lower_provider_count_is_immutable_anomaly_not_authoritative_reduction()
    {
        var s = await Phase4Scenario.Create(fixture); await s.Refresh(4000); await s.Refresh(2000, "lower");
        await using var db = s.Database.Open(); var p = await db.CreatorPromotionParticipations.SingleAsync();
        Assert.Equal(5000, p.LatestVerifiedViews); Assert.Equal(3000, p.RewardedViewCount);
        var anomaly = await db.PromotionViewVerifications.SingleAsync(x => x.IsAnomaly);
        Assert.Equal(3000, anomaly.ReportedViews); Assert.Equal(5000, anomaly.CurrentVerifiedViews);
    }
    [Theory] [InlineData(PromotionType.ViewOnly)] [InlineData(PromotionType.ViewPlusCommission)]
    public async Task Both_modes_charge_only_bound_creator_budget_and_wallet_reserve(PromotionType type)
    {
        var s = await Phase4Scenario.Create(fixture, type); await s.Refresh(3000);
        await using var db = s.Database.Open(); var w = await db.BusinessWallets.SingleAsync();
        Assert.Equal(4000, w.AvailableBalance.Amount); Assert.Equal(5700, w.ReservedBalance.Amount); Assert.Equal(9700, w.TotalBalance.Amount);
        Assert.Empty(await db.CustomerCashbackEntries.ToListAsync());
        Assert.Equal(JournalSourceType.ViewReward, (await db.FinancialJournals.SingleAsync(x => x.SourceType == JournalSourceType.ViewReward)).SourceType);
    }
    [Fact] public async Task View_transaction_rolls_back_rewards_counts_wallet_and_evidence_on_outbox_failure()
    {
        var s = await Phase4Scenario.Create(fixture); await s.FailOutbox();
        await Assert.ThrowsAnyAsync<Exception>(() => s.Refresh(3000));
        await using var db = s.Database.Open();
        Assert.Equal(0, (await db.CreatorPromotionParticipations.SingleAsync()).RewardedViewCount);
        Assert.Equal(2000, (await db.CreatorAllocations.SingleAsync()).RemainingAmount.Amount);
        Assert.Equal(6000, (await db.BusinessWallets.SingleAsync()).ReservedBalance.Amount);
        Assert.Empty(await db.CreatorEarningEntries.ToListAsync()); Assert.Empty(await db.ViewRewardReceipts.ToListAsync());
        Assert.Single(await db.PromotionViewVerifications.ToListAsync());
    }
    [Fact] public async Task Exhausted_creator_budget_can_top_up_without_reclaiming_prior_earnings()
    {
        var s = await Phase4Scenario.Create(fixture, allocation: 300); await s.Refresh(6000);
        await using var db = s.Database.Open(); var a = await db.CreatorAllocations.SingleAsync(); var p = await db.Promotions.SingleAsync();
        await new FinancialCommands(db).IncreaseCreatorAllocationAsync(new(s.Seed.Business, a.Id, new Money(300), a.Version, Scenario.Now), "top-up");
        var result = await s.Refresh(6000, "after-top-up");
        Assert.Equal(6000, result.RewardedViews); Assert.Equal(ParticipationStatus.FundingRequired, result.Status);
        Assert.Equal(400, (await db.CreatorEarningsAccounts.AsNoTracking().SingleAsync()).AvailableEarnings.Amount);
    }
    [Fact] public async Task Creator_cannot_refresh_another_creators_participation()
    {
        var s = await Phase4Scenario.Create(fixture); await using var db = s.Database.Open();
        var error = await Assert.ThrowsAsync<ApplicationFailure>(() => s.Views(db).RefreshAsync(new(s.Creator with { CreatorId = Guid.NewGuid() }, s.ParticipationId, "forged")));
        Assert.Equal(FailureKind.Forbidden, error.Kind);
    }
}
