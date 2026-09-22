using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class Phase4PayoutTests(PostgresFixture fixture)
{
    [Fact] public async Task View_only_earnings_reach_threshold_and_paid_5000_leaves_400()
    {
        var s = await Phase4Scenario.Create(fixture, PromotionType.ViewOnly, 9000, 10000); await s.Refresh(81000);
        await using var db = s.Database.Open(); var service = s.Payouts(db);
        var eligibility = await service.EligibilityAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value);
        Assert.Equal(5400,eligibility.Available.Amount); Assert.Equal(5000,eligibility.EligibleAmount.Amount);
        var id = await service.PrepareAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value,"prepare");
        await service.MarkPaidAsync(Phase4Scenario.Admin,id,"external-confirmation","paid");
        Assert.Equal(400,(await db.CreatorEarningsAccounts.SingleAsync()).AvailableEarnings.Amount);
        var payout = await db.PayoutRecords.SingleAsync(); Assert.Equal(PayoutStatus.Paid,payout.Status); Assert.Equal(5000,payout.ThresholdUsed.Amount);
        Assert.Equal(Phase4Scenario.Admin.UserId,payout.PaidBy); Assert.NotNull(payout.JournalId);
    }
    [Fact] public async Task Hybrid_view_and_sale_earnings_accumulate_in_one_eligible_account()
    {
        var s = await Phase4Scenario.Create(fixture,allocation:9000,budget:10000); await s.Refresh(75000); // 5000 from views
        await s.Redeem(await s.Issue(),purchase:1000); // 45 commission
        await using var db = s.Database.Open(); var eligibility = await s.Payouts(db).EligibilityAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value);
        Assert.Equal(5045,eligibility.Available.Amount); Assert.Equal(5000,eligibility.EligibleAmount.Amount);
        Assert.Single(await db.CreatorEarningsAccounts.ToListAsync());
        Assert.Contains(await db.CreatorEarningEntries.ToListAsync(),x=>x.Source==EarningSource.ViewReward);
        Assert.Contains(await db.CreatorEarningEntries.ToListAsync(),x=>x.Source==EarningSource.SaleCommission);
    }
    [Fact] public async Task Sale_commission_alone_can_reach_creator_payout_threshold()
    {
        var s = await Phase4Scenario.Create(fixture,allocation:20000,budget:20000); await s.Redeem(await s.Issue(),120000);
        await using var db=s.Database.Open(); var e=await s.Payouts(db).EligibilityAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value);
        Assert.Equal(5400,e.Available.Amount); Assert.Equal(5000,e.EligibleAmount.Amount);
    }
    [Fact] public async Task Creator_payout_retry_cannot_pay_twice_and_below_threshold_cannot_prepare_again()
    {
        var s=await Phase4Scenario.Create(fixture,PromotionType.ViewOnly,9000,10000);await s.Refresh(81000);
        await using var db=s.Database.Open();var service=s.Payouts(db);
        var id=await service.PrepareAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,"prepare");
        Assert.Equal(id,await service.PrepareAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value,"another-prepare"));
        await service.MarkPaidAsync(Phase4Scenario.Admin,id,"ref-1","paid");
        Assert.Equal(id,await service.MarkPaidAsync(Phase4Scenario.Admin,id,"ref-1","paid"));
        await Assert.ThrowsAsync<ApplicationFailure>(()=>service.MarkPaidAsync(Phase4Scenario.Admin,id,"ref-2","other-paid"));
        await Assert.ThrowsAsync<ApplicationFailure>(()=>service.PrepareAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value,"next-prepare"));
        Assert.Equal(400,(await db.CreatorEarningsAccounts.SingleAsync()).AvailableEarnings.Amount);
        Assert.Single(await db.FinancialJournals.Where(x=>x.SourceType==JournalSourceType.Payout).ToListAsync());
    }
    [Fact] public async Task Customer_cashback_threshold_and_payment_preserve_400_carry_forward()
    {
        var s=await Phase4Scenario.Create(fixture,allocation:30000,budget:40000);await s.Redeem(await s.Issue(),270000);
        await using var db=s.Database.Open();var service=s.Payouts(db);
        var e=await service.EligibilityAsync(s.Customer,PayoutBeneficiary.Customer,s.Customer.CustomerId!.Value);
        Assert.Equal(5400,e.Available.Amount);Assert.Equal(5000,e.EligibleAmount.Amount);
        var id=await service.PrepareAsync(s.Customer,PayoutBeneficiary.Customer,s.Customer.CustomerId.Value,"prepare");
        await service.MarkPaidAsync(Phase4Scenario.Admin,id,"customer-ref","paid");
        await service.MarkPaidAsync(Phase4Scenario.Admin,id,"customer-ref","paid");
        Assert.Equal(400,(await db.CustomerCashbackAccounts.SingleAsync()).AvailableCashback.Amount);
        var creatorPayout = await service.PrepareAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,"creator-prepare");
        await service.MarkPaidAsync(Phase4Scenario.Admin,creatorPayout,"creator-ref","creator-paid");
        var summary = await s.Queries(db).CustomerCashbackAsync(s.Customer);
        Assert.Equal(400,summary.AvailableCashback.Amount);
        Assert.Equal(5000,summary.MinimumCashOut.Amount);
        Assert.Equal(4600,summary.RemainingToCashOut.Amount);
        Assert.False(summary.Eligible);
        Assert.Equal("BelowThreshold",summary.Status);
        var payout = Assert.Single(summary.PayoutHistory);
        Assert.Equal(5000,payout.Amount.Amount);
        Assert.Equal("Paid",payout.Status);
        Assert.NotNull(payout.PaidAtUtc);
    }
    [Fact] public async Task Customer_cashback_projection_uses_service_eligibility_and_exposes_prepared_state_safely()
    {
        var s=await Phase4Scenario.Create(fixture,allocation:30000,budget:40000);
        await s.Redeem(await s.Issue(),270000);
        await using var db=s.Database.Open();
        var before=await s.Queries(db).CustomerCashbackAsync(s.Customer);
        Assert.Equal(5400,before.AvailableCashback.Amount);
        Assert.Equal(5000,before.MinimumCashOut.Amount);
        Assert.Equal(0,before.RemainingToCashOut.Amount);
        Assert.True(before.Eligible);
        Assert.Equal("Eligible",before.Status);
        var id=await s.Payouts(db).PrepareAsync(s.Customer,PayoutBeneficiary.Customer,s.Customer.CustomerId!.Value,"customer-prepare");
        var prepared=await s.Queries(db).CustomerCashbackAsync(s.Customer);
        Assert.True(prepared.Eligible);
        Assert.Equal("PayoutPrepared",prepared.Status);
        Assert.Equal("Eligible",Assert.Single(prepared.PayoutHistory).Status);
        var json=System.Text.Json.JsonSerializer.Serialize(prepared);
        foreach(var forbidden in new[]{"CustomerId","CreatorId","JournalId","CorrelationId","ConfigurationVersionId","Reference"})
            Assert.DoesNotContain(forbidden,json);
        Assert.NotEqual(Guid.Empty,id);
    }
    [Fact] public async Task Customer_cashback_projection_never_returns_another_customer_or_creator_payouts()
    {
        var s=await Phase4Scenario.Create(fixture,allocation:70000,budget:100000);
        await s.Redeem(await s.Issue("customer-a-issue"),270000,"customer-a-sale");
        var other=new Actor(Guid.NewGuid(),ActorRole.Customer,CustomerId:Guid.NewGuid());
        await using var db=s.Database.Open();
        db.CommercePermissions.Add(new(other.UserId,ActorRole.Customer,other.CustomerId!.Value,null,true,false));
        await db.SaveChangesAsync();
        var checkout=s.Checkout(db);
        var otherQr=await checkout.IssueAsync(new(other,s.AllocationId,"customer-b-issue"));
        await checkout.RedeemAsync(new(s.Cashier,otherQr.Token!,new Money(300000),"customer-b-sale"));
        var payouts=s.Payouts(db);
        var a=await payouts.PrepareAsync(s.Customer,PayoutBeneficiary.Customer,s.Customer.CustomerId!.Value,"a-prepare");
        await payouts.MarkPaidAsync(Phase4Scenario.Admin,a,"a-paid","a-mark-paid");
        s.Clock.Now=Scenario.Now.AddHours(1);
        var b=await payouts.PrepareAsync(other,PayoutBeneficiary.Customer,other.CustomerId.Value,"b-prepare");
        await payouts.MarkPaidAsync(Phase4Scenario.Admin,b,"b-paid","b-mark-paid");
        var creator=await payouts.PrepareAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,"creator-prepare");
        await payouts.MarkPaidAsync(Phase4Scenario.Admin,creator,"creator-paid","creator-mark-paid");

        var customerA=await s.Queries(db).CustomerCashbackAsync(s.Customer);
        var customerB=await s.Queries(db).CustomerCashbackAsync(other);
        Assert.Equal(400,customerA.AvailableCashback.Amount);
        Assert.Equal(1000,customerB.AvailableCashback.Amount);
        Assert.Equal("Paid",Assert.Single(customerA.PayoutHistory).Status);
        Assert.Equal("Paid",Assert.Single(customerB.PayoutHistory).Status);
        Assert.Equal(Scenario.Now,customerA.PayoutHistory.Single().PaidAtUtc);
        Assert.Equal(Scenario.Now.AddHours(1),customerB.PayoutHistory.Single().PaidAtUtc);
        Assert.DoesNotContain("Creator",System.Text.Json.JsonSerializer.Serialize(customerA));
        Assert.DoesNotContain("Creator",System.Text.Json.JsonSerializer.Serialize(customerB));
    }
    [Fact] public async Task Prepared_payout_pins_threshold_when_admin_changes_later_effective_rate()
    {
        var s=await Phase4Scenario.Create(fixture,PromotionType.ViewOnly,9000,10000);await s.Refresh(81000);
        await using var db=s.Database.Open();var service=s.Payouts(db);
        var id=await service.PrepareAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,"prepare");
        var version=Guid.NewGuid();db.FinancialConfigurationVersions.Add(new(version,s.Seed.ConfigurationId,2,Phase4Scenario.Admin.UserId,Scenario.Now.AddMinutes(1),
            Scenario.Price(PromotionType.ViewOnly,version),Scenario.Price(PromotionType.ViewPlusCommission,version),new Money(6000),new Money(6000)));
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        Assert.Equal(5000,(await service.EligibilityAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value)).Threshold.Amount);
        s.Clock.Now=Scenario.Now.AddMinutes(1);
        Assert.Equal(6000,(await service.EligibilityAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value)).Threshold.Amount);
        await service.MarkPaidAsync(Phase4Scenario.Admin,id,"pinned","paid");
        Assert.Equal(400,(await db.CreatorEarningsAccounts.SingleAsync()).AvailableEarnings.Amount);
    }
    [Fact] public async Task Only_admin_can_confirm_external_payment_and_other_roles_cannot_read_eligibility()
    {
        var s=await Phase4Scenario.Create(fixture);await using var db=s.Database.Open();
        var e=await Assert.ThrowsAsync<ApplicationFailure>(()=>s.Payouts(db).MarkPaidAsync(s.Creator,Guid.NewGuid(),"ref","key"));
        Assert.Equal(FailureKind.Forbidden,e.Kind);
        e=await Assert.ThrowsAsync<ApplicationFailure>(()=>s.Payouts(db).EligibilityAsync(s.Seed.Business,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value));
        Assert.Equal(FailureKind.Forbidden,e.Kind);
    }
    [Fact] public async Task Payout_outbox_failure_rolls_back_payment_account_and_journal()
    {
        var s=await Phase4Scenario.Create(fixture,PromotionType.ViewOnly,9000,10000);await s.Refresh(81000);
        await using var db=s.Database.Open();var id=await s.Payouts(db).PrepareAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,"prepare");
        await s.FailOutbox();await Assert.ThrowsAnyAsync<Exception>(()=>s.Payouts(db).MarkPaidAsync(Phase4Scenario.Admin,id,"ref","paid"));
        Assert.Equal(5400,(await db.CreatorEarningsAccounts.SingleAsync()).AvailableEarnings.Amount);
        Assert.Equal(PayoutStatus.Eligible,(await db.PayoutRecords.SingleAsync()).Status);
        Assert.Empty(await db.FinancialJournals.Where(x=>x.SourceType==JournalSourceType.Payout).ToListAsync());
    }
    [Fact] public async Task Platform_view_and_sale_accrual_support_partial_settlement_and_carry_forward()
    {
        var s=await Phase4Scenario.Create(fixture);await s.Refresh(3000);await s.Redeem(await s.Issue());
        await using var db=s.Database.Open();var service=s.Payouts(db);
        await service.SettlePlatformAsync(Phase4Scenario.Admin,new Money(30),"platform-ref","settle");
        await service.SettlePlatformAsync(Phase4Scenario.Admin,new Money(30),"platform-ref","settle");
        var totals=await new PlatformRevenueRepository(db).SummaryAsync();
        Assert.Equal(135,totals.Accrued.Amount);Assert.Equal(30,totals.Settled.Amount);Assert.Equal(105,totals.Unsettled.Amount);
        Assert.Single(await db.PlatformSettlements.ToListAsync());
        Assert.Equal(Phase4Scenario.Admin.UserId,db.Entry(await db.PlatformSettlements.SingleAsync()).Property<Guid?>("SettledBy").CurrentValue);
    }
    [Fact] public async Task Platform_cannot_settle_more_than_unsettled_revenue()
    {
        var s=await Phase4Scenario.Create(fixture);await s.Refresh(3000);await using var db=s.Database.Open();
        var e=await Assert.ThrowsAsync<ApplicationFailure>(()=>s.Payouts(db).SettlePlatformAsync(Phase4Scenario.Admin,new Money(101),"ref","settle"));
        Assert.Equal(FailureKind.InsufficientFunds,e.Kind);Assert.Empty(await db.PlatformSettlements.ToListAsync());
    }
    [Fact] public async Task Platform_settlement_requires_admin_authority()
    {
        var s=await Phase4Scenario.Create(fixture);await using var db=s.Database.Open();
        var e=await Assert.ThrowsAsync<ApplicationFailure>(()=>s.Payouts(db).SettlePlatformAsync(s.Seed.Business,new Money(1),"ref","settle"));
        Assert.Equal(FailureKind.Forbidden,e.Kind);
    }
    [Fact] public async Task Payout_pending_reference_cannot_be_retrieved_by_a_forged_subject_identity()
    {
        var s=await Phase4Scenario.Create(fixture,PromotionType.ViewOnly,9000,10000);await s.Refresh(81000);await using var db=s.Database.Open();
        await s.Payouts(db).PrepareAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,"prepare");
        var e=await Assert.ThrowsAsync<ApplicationFailure>(()=>s.Payouts(db).PrepareAsync(s.Creator with { UserId=Guid.NewGuid() },PayoutBeneficiary.Creator,s.Creator.CreatorId.Value,"forged"));
        Assert.Equal(FailureKind.Forbidden,e.Kind);Assert.Single(await db.PayoutRecords.ToListAsync());
    }
    [Fact] public async Task Admin_platform_summary_and_history_reuse_existing_revenue_and_settlement_records()
    {
        var s=await Phase4Scenario.Create(fixture);await s.Refresh(3000);await using var db=s.Database.Open();
        await s.Payouts(db).SettlePlatformAsync(Phase4Scenario.Admin,new Money(25),"partial","settle");
        var summary=await s.Queries(db).PlatformAsync(Phase4Scenario.Admin);
        Assert.Equal(100,summary.Accrued.Amount);Assert.Equal(25,summary.Settled.Amount);Assert.Equal(75,summary.Unsettled.Amount);
        Assert.Equal("partial",Assert.Single(summary.History).Reference);Assert.Equal(Phase4Scenario.Admin.UserId,summary.History.Single().SettledBy);
        var e=await Assert.ThrowsAsync<ApplicationFailure>(()=>s.Queries(db).PlatformAsync(s.Seed.Business));Assert.Equal(FailureKind.Forbidden,e.Kind);
    }
}
