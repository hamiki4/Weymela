using System;
using Weymela.Domain;
using Xunit;
namespace Weymela.Domain.Tests;
public sealed class ParticipationInvariantTests
{
 private static readonly DateTime Now=new(2026,1,1,0,0,0,DateTimeKind.Utc);
 private static Promotion Published(){var s=new PricingSnapshot(PromotionType.ViewOnly,1000,new Money(300),new Money(200),new Money(100),0,0,0,Now,Guid.NewGuid());var p=new Promotion(Guid.NewGuid(),"Campaign","",PromotionType.ViewOnly,new Money(6000),new(null,null,"ET",null),Now,Now.AddDays(1),s,Now,30);var w=new BusinessWallet(p.BusinessId);w.CreditDeposit(new Money(6000),Now,Guid.NewGuid());p.Fund(w,Now,Guid.NewGuid());p.Publish(Now,Guid.NewGuid());return p;}
 [Fact] public void Published_campaign_accepts_creator_application(){var p=Published();var a=new CreatorApplication(p.Id,Guid.NewGuid(),"join",null,p,Now,Guid.NewGuid());Assert.Equal(CreatorApplicationStatus.Pending,a.Status);}
 [Fact] public void Active_campaign_accepts_application_when_policy_allows(){var p=Published();p.Activate(Now,Guid.NewGuid());var a=new CreatorApplication(p.Id,Guid.NewGuid(),null,null,p,Now,Guid.NewGuid());Assert.Equal(CreatorApplicationStatus.Pending,a.Status);}
 [Fact] public void Draft_campaign_rejects_application(){var s=new PricingSnapshot(PromotionType.ViewOnly,1,new Money(1),new Money(1),new Money(1),0,0,0,Now,Guid.NewGuid());var p=new Promotion(Guid.NewGuid(),"x","",PromotionType.ViewOnly,new Money(1),new(null,null,null,null),Now,Now.AddDays(1),s,Now,30);Assert.Throws<InvalidOperationException>(()=>new CreatorApplication(p.Id,Guid.NewGuid(),null,null,p,Now,Guid.NewGuid()));}
 [Fact] public void Application_approval_and_rejection_are_explicit(){var p=Published();var approved=new CreatorApplication(p.Id,Guid.NewGuid(),null,null,p,Now,Guid.NewGuid());approved.Approve(Guid.NewGuid(),Now);Assert.Equal(CreatorApplicationStatus.Approved,approved.Status);var rejected=new CreatorApplication(p.Id,Guid.NewGuid(),null,null,p,Now,Guid.NewGuid());rejected.Reject(Guid.NewGuid(),Now);Assert.Equal(CreatorApplicationStatus.Rejected,rejected.Status);}
 [Fact] public void Allocation_cannot_exceed_unallocated_budget(){var p=Published();Assert.Throws<InvalidOperationException>(()=>p.Allocate(Guid.NewGuid(),new Money(6001),Now,Guid.NewGuid()));}
 [Fact] public void Multiple_creator_budgets_are_isolated(){var p=Published();var a=p.Allocate(Guid.NewGuid(),new Money(2000),Now,Guid.NewGuid());var b=p.Allocate(Guid.NewGuid(),new Money(1000),Now,Guid.NewGuid());p.Activate(Now,Guid.NewGuid());p.Consume(a.Id,new Money(500),Now,Guid.NewGuid());Assert.Equal(1500,a.RemainingAmount.Amount);Assert.Equal(1000,b.RemainingAmount.Amount);}
 [Fact] public void Active_creator_cannot_consume_another_creator_budget(){var p=Published();var a=p.Allocate(Guid.NewGuid(),new Money(2000),Now,Guid.NewGuid());var b=p.Allocate(Guid.NewGuid(),new Money(1000),Now,Guid.NewGuid());p.Activate(Now,Guid.NewGuid());Assert.Throws<InvalidOperationException>(()=>p.Consume(a.Id,new Money(2001),Now,Guid.NewGuid()));Assert.Equal(1000,b.RemainingAmount.Amount);}
 [Fact] public void Active_allocation_can_be_increased_but_not_reduced(){var p=Published();var a=p.Allocate(Guid.NewGuid(),new Money(1000),Now,Guid.NewGuid());p.Activate(Now,Guid.NewGuid());p.IncreaseAllocation(a.Id,new Money(500),Now,Guid.NewGuid());Assert.Equal(1500,a.OriginalAllocation.Amount);Assert.Throws<InvalidOperationException>(()=>a.Consume(new Money(2000),Now));}
 [Fact] public void Top_up_cannot_exceed_unassigned_reserve(){var p=Published();var a=p.Allocate(Guid.NewGuid(),new Money(5000),Now,Guid.NewGuid());Assert.Throws<InvalidOperationException>(()=>p.IncreaseAllocation(a.Id,new Money(1001),Now,Guid.NewGuid()));}
 [Fact] public void Completion_returns_unused_budget_to_campaign_unassigned_budget_not_business_wallet(){var p=Published();var w=new BusinessWallet(p.BusinessId);w.CreditDeposit(new Money(6000),Now,Guid.NewGuid());p.Activate(Now,Guid.NewGuid());var a=p.Allocate(Guid.NewGuid(),new Money(2000),Now,Guid.NewGuid());p.Consume(a.Id,new Money(500),Now,Guid.NewGuid());var before=w.AvailableBalance.Amount;p.CompleteParticipation(a.Id,Now,Guid.NewGuid());Assert.Equal(1500,a.RemainingAmount.Amount);Assert.Equal(before,w.AvailableBalance.Amount);Assert.Equal(5500,p.UnallocatedBudget.Amount);}
 [Fact] public void Consumed_amount_cannot_be_reclaimed_by_normal_business_operation()
 {
  var p=Published();var a=p.Allocate(Guid.NewGuid(),new Money(1000),Now,Guid.NewGuid());p.Activate(Now,Guid.NewGuid());
  p.Consume(a.Id,new Money(1000),Now,Guid.NewGuid());Assert.Equal(0,a.RemainingAmount.Amount);
  p.IncreaseAllocation(a.Id,new Money(1),Now,Guid.NewGuid());
  Assert.Equal(1000,a.UsedAmount.Amount);Assert.Equal(1,a.RemainingAmount.Amount);
  Assert.Equal(1000,p.UsedBudget.Amount);Assert.Equal(1001,a.OriginalAllocation.Amount);
 }
}
