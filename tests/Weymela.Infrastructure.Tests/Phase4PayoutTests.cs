using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Identity;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class Phase4PayoutTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData(4999, false)]
    [InlineData(5000, true)]
    [InlineData(5200, true)]
    [InlineData(5400, true)]
    [InlineData(5401, false)]
    public async Task Admin_payout_amount_is_bounded_by_effective_threshold_and_current_available(decimal amount, bool accepted)
    {
        var s=await Phase4Scenario.Create(fixture,PromotionType.ViewOnly,9000,10000);await s.Refresh(81000);
        await using var db=s.Database.Open();var service=s.Payouts(db);
        if (!accepted)
        {
            await Assert.ThrowsAsync<ApplicationFailure>(()=>service.PrepareAsync(Phase4Scenario.Admin,
                PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,new Money(amount),$"prepare-{amount}"));
            Assert.Empty(await db.PayoutRecords.ToListAsync());
            return;
        }
        var id=await service.PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Creator,
            s.Creator.CreatorId!.Value,new Money(amount),$"prepare-{amount}");
        var payout=await db.PayoutRecords.SingleAsync(x=>x.Id==id);
        Assert.Equal(amount,payout.Amount.Amount);Assert.Equal(5000,payout.ThresholdUsed.Amount);
        Assert.Equal(5400,(await db.CreatorEarningsAccounts.SingleAsync()).AvailableEarnings.Amount);
    }

    [Fact]
    public async Task Payout_destination_uses_verified_phone_masks_bank_and_preserves_historical_snapshot()
    {
        var s=await Phase4Scenario.Create(fixture,PromotionType.ViewOnly,9000,10000);await s.Refresh(81000);
        await using var db=s.Database.Open();
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId=s.Creator.CreatorId!.Value,
            Role=ActorRole.Creator,DisplayName="Creator Legal Name",PublicId="CR-PAYOUT" });
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId=s.Creator.UserId,Kind="Phone",
            IdentifierHash=EmailAuthService.HashIdentifier("+251911223344"),DeliveryAddress="+251911223344",
            IsVerified=true,CreatedAtUtc=Scenario.Now });
        await db.SaveChangesAsync();
        var destinations=new PayoutDestinationService(db,TestPayoutProtector.Instance,s.Clock);
        var telebirr=await destinations.UpdateAsync(s.Creator,new("Telebirr",null,null));
        Assert.Equal("+251911223344",telebirr.Account);Assert.False(telebirr.IsMasked);Assert.True(telebirr.IsConfigured);
        var substitution=await Assert.ThrowsAsync<ApplicationFailure>(()=>destinations.UpdateAsync(s.Creator,
            new("Telebirr",null,"+251999999999")));
        Assert.Equal(FailureKind.Validation,substitution.Kind);
        var bank=await destinations.UpdateAsync(s.Creator,new("Bank","CBE","123456789"));
        Assert.True(bank.IsMasked);Assert.EndsWith("6789",bank.Account,StringComparison.Ordinal);
        var admin=await destinations.AdminAsync(Phase4Scenario.Admin,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value);
        Assert.Equal("123456789",admin!.Account);Assert.Equal("Creator Legal Name",admin.LegalName);
        var payoutService=new PayoutService(db,s.Clock,destinations);
        var payoutId=await payoutService.PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Creator,
            s.Creator.CreatorId.Value,new Money(5000),"snapshot-prepare");
        await destinations.UpdateAsync(s.Creator,new("Bank","Awash","9988776655"));
        var payout=await db.PayoutRecords.SingleAsync(x=>x.Id==payoutId);
        Assert.Equal("CBE",payout.DestinationProvider);
        Assert.Equal("123456789",TestPayoutProtector.Instance.Unprotect(payout.ProtectedDestinationAccount!));
        Assert.Equal("Creator Legal Name",payout.DestinationLegalName);
        await payoutService.MarkPaidAsync(Phase4Scenario.Admin,payoutId,"external-ref","snapshot-paid");
        db.ChangeTracker.Clear();payout=await db.PayoutRecords.SingleAsync(x=>x.Id==payoutId);
        Assert.Equal("CBE",payout.DestinationProvider);
        Assert.Equal("123456789",TestPayoutProtector.Instance.Unprotect(payout.ProtectedDestinationAccount!));
    }
    [Fact]
    public async Task Payout_destination_without_verified_phone_can_open_settings_and_choose_bank()
    {
        var s=await Phase4Scenario.Create(fixture,PromotionType.ViewOnly,9000,10000);
        await using var db=s.Database.Open();
        db.PayoutDestinations.Remove(await db.PayoutDestinations.SingleAsync(x=>x.Beneficiary==PayoutBeneficiary.Creator));
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId=s.Creator.CreatorId!.Value,
            Role=ActorRole.Creator,DisplayName="Creator Without Phone",PublicId="CR-BANK-ONLY" });
        await db.SaveChangesAsync();
        var destinations=new PayoutDestinationService(db,TestPayoutProtector.Instance,s.Clock);

        var initial=await destinations.OwnAsync(s.Creator);
        Assert.NotNull(initial);Assert.Equal("Telebirr",initial.Method);Assert.Equal(string.Empty,initial.Account);
        Assert.Equal("Creator Without Phone",initial.LegalName);Assert.Null(initial.UpdatedAtUtc);
        Assert.False(initial.IsMasked);Assert.False(initial.IsConfigured);

        var telebirrFailure=await Assert.ThrowsAsync<ApplicationFailure>(()=>destinations.UpdateAsync(s.Creator,
            new("Telebirr",null,null)));
        Assert.Equal(FailureKind.Validation,telebirrFailure.Kind);

        var bank=await destinations.UpdateAsync(s.Creator,new("Bank","CBE","123456789"));
        Assert.Equal("Bank",bank.Method);Assert.Equal("CBE",bank.Provider);Assert.True(bank.IsConfigured);
        Assert.True(bank.IsMasked);Assert.EndsWith("6789",bank.Account,StringComparison.Ordinal);

        var saved=await destinations.OwnAsync(s.Creator);
        Assert.NotNull(saved);Assert.Equal("Bank",saved.Method);Assert.True(saved.IsConfigured);
        Assert.True(saved.IsMasked);Assert.EndsWith("6789",saved.Account,StringComparison.Ordinal);
    }
    [Fact] public async Task View_only_earnings_reach_threshold_and_paid_5000_leaves_400()
    {
        var s = await Phase4Scenario.Create(fixture, PromotionType.ViewOnly, 9000, 10000); await s.Refresh(81000);
        await using var db = s.Database.Open(); var service = s.Payouts(db);
        var eligibility = await service.EligibilityAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value);
        Assert.Equal(5400,eligibility.Available.Amount); Assert.Equal(5000,eligibility.EligibleAmount.Amount);
        var id = await service.PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value,new Money(5000),"prepare");
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
        var id=await service.PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,new Money(5000),"prepare");
        Assert.Equal(id,await service.PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value,new Money(5000),"prepare"));
        await service.MarkPaidAsync(Phase4Scenario.Admin,id,"ref-1","paid");
        Assert.Equal(id,await service.MarkPaidAsync(Phase4Scenario.Admin,id,"ref-1","paid"));
        await Assert.ThrowsAsync<ApplicationFailure>(()=>service.MarkPaidAsync(Phase4Scenario.Admin,id,"ref-2","other-paid"));
        await Assert.ThrowsAsync<ApplicationFailure>(()=>service.PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value,new Money(5000),"next-prepare"));
        Assert.Equal(400,(await db.CreatorEarningsAccounts.SingleAsync()).AvailableEarnings.Amount);
        Assert.Single(await db.FinancialJournals.Where(x=>x.SourceType==JournalSourceType.Payout).ToListAsync());
    }
    [Fact] public async Task Customer_cashback_threshold_and_payment_preserve_400_carry_forward()
    {
        var s=await Phase4Scenario.Create(fixture,allocation:30000,budget:40000);await s.Redeem(await s.Issue(),270000);
        await using var db=s.Database.Open();var service=s.Payouts(db);
        var e=await service.EligibilityAsync(s.Customer,PayoutBeneficiary.Customer,s.Customer.CustomerId!.Value);
        Assert.Equal(5400,e.Available.Amount);Assert.Equal(5000,e.EligibleAmount.Amount);
        var id=await service.PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Customer,s.Customer.CustomerId.Value,new Money(5000),"prepare");
        await service.MarkPaidAsync(Phase4Scenario.Admin,id,"customer-ref","paid");
        await service.MarkPaidAsync(Phase4Scenario.Admin,id,"customer-ref","paid");
        Assert.Equal(400,(await db.CustomerCashbackAccounts.SingleAsync()).AvailableCashback.Amount);
        var creatorPayout = await service.PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,new Money(5000),"creator-prepare");
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
    [Fact]
    public async Task Operations_admin_can_process_creator_and_customer_payouts_without_platform_settlement_authority()
    {
        var s=await Phase4Scenario.Create(fixture,allocation:30000,budget:40000);await s.Redeem(await s.Issue(),270000);
        await using var db=s.Database.Open();var service=s.Payouts(db);var operations=new Actor(Guid.NewGuid(),ActorRole.OperationsAdmin);
        var creator=await service.PrepareAsync(operations,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,new Money(5000),"operations-creator-prepare");
        var customer=await service.PrepareAsync(operations,PayoutBeneficiary.Customer,s.Customer.CustomerId!.Value,new Money(5000),"operations-customer-prepare");
        await service.MarkPaidAsync(operations,creator,"operations-creator-paid","operations-creator-confirm");
        await service.MarkPaidAsync(operations,customer,"operations-customer-paid","operations-customer-confirm");
        Assert.All(await db.PayoutRecords.ToListAsync(), payout => Assert.Equal(PayoutStatus.Paid, payout.Status));
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SettlePlatformAsync(operations,new Money(1),"forbidden","operations-settlement"));
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
        var id=await s.Payouts(db).PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Customer,s.Customer.CustomerId!.Value,new Money(5000),"customer-prepare");
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
        db.PayoutDestinations.Add(new Weymela.Infrastructure.Persistence.Records.PayoutDestination { Beneficiary=PayoutBeneficiary.Customer,SubjectId=other.CustomerId.Value,Method=PayoutDestinationMethod.Telebirr,Provider="Telebirr",ProtectedAccount=TestPayoutProtector.Instance.Protect("0933445566"),AccountLast4="5566",LegalName="Other Customer",UpdatedAtUtc=Scenario.Now });
        await db.SaveChangesAsync();
        var checkout=s.Checkout(db);
        var otherQr=await checkout.IssueAsync(new(other,s.AllocationId,"customer-b-issue"));
        await checkout.RedeemAsync(new(s.Cashier,otherQr.Token!,new Money(300000),"customer-b-sale"));
        var payouts=s.Payouts(db);
        var a=await payouts.PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Customer,s.Customer.CustomerId!.Value,new Money(5000),"a-prepare");
        await payouts.MarkPaidAsync(Phase4Scenario.Admin,a,"a-paid","a-mark-paid");
        s.Clock.Now=Scenario.Now.AddHours(1);
        var b=await payouts.PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Customer,other.CustomerId.Value,new Money(5000),"b-prepare");
        await payouts.MarkPaidAsync(Phase4Scenario.Admin,b,"b-paid","b-mark-paid");
        var creator=await payouts.PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,new Money(5000),"creator-prepare");
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
    [Fact] public async Task Prepared_payout_is_rejected_if_effective_threshold_increases_before_confirmation()
    {
        var s=await Phase4Scenario.Create(fixture,PromotionType.ViewOnly,9000,10000);await s.Refresh(81000);
        await using var db=s.Database.Open();var service=s.Payouts(db);
        var id=await service.PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,new Money(5000),"prepare");
        var version=Guid.NewGuid();db.FinancialConfigurationVersions.Add(new(version,s.Seed.ConfigurationId,2,Phase4Scenario.Admin.UserId,Scenario.Now.AddMinutes(1),
            Scenario.Price(PromotionType.ViewOnly,version),Scenario.Price(PromotionType.ViewPlusCommission,version),new Money(6000),new Money(6000)));
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        Assert.Equal(5000,(await service.EligibilityAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value)).Threshold.Amount);
        s.Clock.Now=Scenario.Now.AddMinutes(1);
        Assert.Equal(6000,(await service.EligibilityAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value)).Threshold.Amount);
        await Assert.ThrowsAsync<ApplicationFailure>(()=>service.MarkPaidAsync(Phase4Scenario.Admin,id,"stale-threshold","paid"));
        Assert.Equal(5400,(await db.CreatorEarningsAccounts.SingleAsync()).AvailableEarnings.Amount);
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
        await using var db=s.Database.Open();var id=await s.Payouts(db).PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,new Money(5000),"prepare");
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
        await s.Payouts(db).PrepareAsync(Phase4Scenario.Admin,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,new Money(5000),"prepare");
        var e=await Assert.ThrowsAsync<ApplicationFailure>(()=>s.Payouts(db).PrepareAsync(s.Creator with { UserId=Guid.NewGuid() },PayoutBeneficiary.Creator,s.Creator.CreatorId.Value,new Money(5000),"forged"));
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
