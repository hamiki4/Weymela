using System;
using Weymela.Domain;
using Xunit;

namespace Weymela.Domain.Tests;

public sealed class Phase4FinanceTests
{
    private static readonly DateTime Now = new(2026,1,1,0,0,0,DateTimeKind.Utc);
    private static PricingSnapshot Price => new(PromotionType.ViewPlusCommission,3000,new Money(300),new Money(200),new Money(100),4.5m,2,3.5m,Now,Guid.NewGuid());
    private static CreatorPromotionParticipation Participation() => new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Fake","content",1000,Now);
    private static OfferQrSession Qr() => new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new string('A',64),Now,"key");

    [Fact] public void Baseline_views_are_not_campaign_views() { var p=Participation();Assert.Equal(0,p.CampaignVerifiedViews);Assert.Equal(1000,p.BaselineViews); }
    [Fact] public void Only_complete_unpaid_blocks_are_eligible() { var p=Participation();p.Observe(8400,Now);p.Reward(1,3000);Assert.Equal(1,p.CompleteUnpaidBlocks(3000));Assert.Equal(1400,p.CampaignVerifiedViews%3000); }
    [Fact] public void Reward_cannot_advance_for_incomplete_block() { var p=Participation();p.Observe(3999,Now);Assert.Throws<InvalidOperationException>(()=>p.Reward(1,3000));Assert.Equal(0,p.RewardedViewCount); }
    [Fact] public void Lower_provider_count_is_anomaly_without_lowering_authoritative_latest() { var p=Participation();p.Observe(5000,Now);Assert.True(p.Observe(4500,Now));Assert.Equal(5000,p.LatestVerifiedViews); }
    [Fact] public void Stale_provider_timestamp_cannot_raise_authoritative_count() { var p=Participation();Assert.True(p.Observe(6000,Now.AddSeconds(-1)));Assert.Equal(1000,p.LatestVerifiedViews); }
    [Fact] public void Pause_resume_preserves_baseline_and_rewards() { var p=Participation();p.Observe(4000,Now);p.Reward(1,3000);p.Pause();p.Resume();Assert.Equal(1000,p.BaselineViews);Assert.Equal(3000,p.RewardedViewCount); }
    [Fact] public void Completed_participation_cannot_resume_or_earn() { var p=Participation();p.Observe(4000,Now);p.Complete();Assert.Throws<InvalidOperationException>(()=>p.Resume());Assert.Throws<InvalidOperationException>(()=>p.Reward(1,3000)); }
    [Fact] public void Negative_verified_count_is_rejected() { Assert.Throws<ArgumentException>(()=>Participation().Observe(-1,Now)); }
    [Fact] public void Snapshot_sale_splits_sum_to_exact_creator_budget_charge() { var a=SnapshotPricing.Sale(Price,new Money(1000));Assert.Equal(45,a.Creator.Amount);Assert.Equal(20,a.Customer.Amount);Assert.Equal(35,a.Platform.Amount);Assert.Equal(100,a.Total.Amount); }
    [Fact] public void Sale_rounding_uses_currency_precision_and_balanced_sum_of_rounded_parts() { var a=SnapshotPricing.Sale(Price,new Money(1));Assert.Equal(.05m,a.Creator.Amount);Assert.Equal(.02m,a.Customer.Amount);Assert.Equal(.04m,a.Platform.Amount);Assert.Equal(.11m,a.Total.Amount); }
    [Fact] public void View_only_rejects_sale_calculation() { Assert.Throws<InvalidOperationException>(()=>SnapshotPricing.Sale(Price with { PromotionType=PromotionType.ViewOnly },new Money(1000))); }
    [Fact] public void Invalid_view_charge_split_is_rejected() { Assert.Throws<ArgumentException>(()=>SnapshotPricing.Validate(Price with { CreatorEarning=new Money(201) })); }
    [Fact] public void Conflicting_declared_sale_total_is_rejected() { Assert.Throws<ArgumentException>(()=>SnapshotPricing.Validate(Price,9));SnapshotPricing.Validate(Price,10); }
    [Fact] public void Sale_percentage_total_above_100_is_rejected() { Assert.Throws<ArgumentException>(()=>SnapshotPricing.Validate(Price with { PlatformPercent=99 })); }
    [Fact] public void Subcent_purchase_input_is_rejected() { Assert.Throws<ArgumentException>(()=>SnapshotPricing.Sale(Price,new Money(1.001m))); }
    [Fact] public void Purchase_that_rounds_all_charges_to_zero_is_rejected() { Assert.Throws<ArgumentException>(()=>SnapshotPricing.Sale(Price,new Money(.01m))); }
    [Fact] public void Qr_has_exact_five_minute_expiry() { var qr=Qr();Assert.Equal(Now.AddMinutes(5),qr.ExpiresAtUtc);Assert.Throws<InvalidOperationException>(()=>qr.Validate(qr.BusinessId,Now.AddMinutes(5))); }
    [Fact] public void Wrong_business_attempt_does_not_consume_qr() { var qr=Qr();Assert.Throws<InvalidOperationException>(()=>qr.Use(Guid.NewGuid(),Guid.NewGuid(),Now));Assert.Equal(OfferQrStatus.Issued,qr.Status); }
    [Fact] public void Qr_cannot_be_consumed_twice() { var qr=Qr();var sale=Guid.NewGuid();qr.Use(sale,qr.BusinessId,Now);Assert.Throws<InvalidOperationException>(()=>qr.Use(Guid.NewGuid(),qr.BusinessId,Now));Assert.Equal(sale,qr.SaleId); }
    [Fact] public void Qr_rejects_non_hash_persistence_value() { Assert.Throws<ArgumentException>(()=>new OfferQrSession(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"raw-token",Now,"key")); }
    [Fact] public void Payout_records_exact_threshold_not_entire_available_balance() { var p=new PayoutRecord(PayoutBeneficiary.Creator,Guid.NewGuid(),new Money(5400),new Money(5000),Guid.NewGuid(),Now);Assert.Equal(5000,p.Amount.Amount);Assert.Equal(5000,p.ThresholdUsed.Amount); }
    [Fact] public void Payout_cannot_be_prepared_below_threshold() { Assert.Throws<InvalidOperationException>(()=>new PayoutRecord(PayoutBeneficiary.Customer,Guid.NewGuid(),new Money(4999),new Money(5000),Guid.NewGuid(),Now)); }
    [Fact] public void Payout_confirmation_requires_reference_and_is_one_time() { var p=new PayoutRecord(PayoutBeneficiary.Creator,Guid.NewGuid(),new Money(5400),new Money(5000),Guid.NewGuid(),Now);Assert.Throws<InvalidOperationException>(()=>p.MarkPaid(Guid.NewGuid(),"",Guid.NewGuid(),Now));p.MarkPaid(Guid.NewGuid(),"confirmed",Guid.NewGuid(),Now);Assert.Throws<InvalidOperationException>(()=>p.MarkPaid(Guid.NewGuid(),"again",Guid.NewGuid(),Now)); }
}
