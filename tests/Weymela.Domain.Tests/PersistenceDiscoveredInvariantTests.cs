using System;
using Weymela.Domain;
using Xunit;

namespace Weymela.Domain.Tests;

public sealed class PersistenceDiscoveredInvariantTests
{
    [Fact] public void Completed_creator_budget_cannot_create_more_financial_obligations()
    {
        var a = new CreatorAllocation(Guid.NewGuid(), Guid.NewGuid(), new Money(100), DateTime.UtcNow);
        a.Complete(DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() => a.Consume(new Money(1), DateTime.UtcNow));
    }

    [Fact] public void Exhausted_creator_participation_can_finish_with_completion_timestamp()
    {
        var now = DateTime.UtcNow;
        var a = new CreatorAllocation(Guid.NewGuid(), Guid.NewGuid(), new Money(100), now);
        a.Consume(new Money(100), now); a.Complete(now);
        Assert.Equal(CreatorAllocationStatus.Completed, a.Status); Assert.Equal(now, a.CompletedAtUtc);
        Assert.Equal(100, a.UsedAmount.Amount);
    }

    [Fact] public void Campaign_with_unassigned_reserve_is_not_exhausted_when_one_creator_finishes_budget()
    {
        var now = DateTime.UtcNow; var business = Guid.NewGuid();
        var p = new Promotion(business, "Campaign", "", PromotionType.ViewOnly, new Money(1000),
            new(null,null,null,null), now, now.AddDays(1),
            new(PromotionType.ViewOnly,1000,new Money(300),new Money(200),new Money(100),0,0,0,now,Guid.NewGuid()),now,30);
        var w = new BusinessWallet(business); w.CreditDeposit(new Money(1000),now,Guid.NewGuid()); p.Fund(w,now,Guid.NewGuid());
        p.Publish(now,Guid.NewGuid()); p.Activate(now,Guid.NewGuid());
        var a = p.Allocate(Guid.NewGuid(),new Money(300),now,Guid.NewGuid());
        p.Consume(a.Id,new Money(300),now,Guid.NewGuid());
        Assert.Equal(PromotionStatus.Active,p.Status); Assert.Equal(700,p.UnallocatedBudget.Amount);
    }

    [Fact] public void Normal_campaign_completion_preserves_committed_business_reserve()
    {
        var now = DateTime.UtcNow; var business = Guid.NewGuid();
        var p = new Promotion(business,"Campaign","",PromotionType.ViewOnly,new Money(1000),new(null,null,null,null),now,now.AddDays(1),
            new(PromotionType.ViewOnly,1000,new Money(300),new Money(200),new Money(100),0,0,0,now,Guid.NewGuid()),now,30);
        var w = new BusinessWallet(business);w.CreditDeposit(new Money(1500),now,Guid.NewGuid());p.Fund(w,now,Guid.NewGuid());
        p.Publish(now,Guid.NewGuid());p.Activate(now,Guid.NewGuid());p.Allocate(Guid.NewGuid(),new Money(500),now,Guid.NewGuid());
        p.Complete(w,now,Guid.NewGuid());
        Assert.Equal(500,w.AvailableBalance.Amount);Assert.Equal(1000,w.ReservedBalance.Amount);
        Assert.Equal(1000,p.ReservedBudget.Amount);Assert.Equal(1000,p.UnallocatedBudget.Amount);
    }
}
