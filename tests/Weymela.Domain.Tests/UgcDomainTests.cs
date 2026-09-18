using System;
using System.Linq;
using Weymela.Domain;
using Xunit;

namespace Weymela.Domain.Tests;

public sealed class UgcDomainTests
{
    private static readonly DateTime Now = new(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc);
    private static readonly UgcPricingSnapshot Pricing = new(new Money(200), 10m, null, Now, Guid.NewGuid());

    private static UgcOpportunity New(decimal payment = 500, int creators = 3) => new(Guid.NewGuid(), "Morning transformation", null,
        UgcContentType.Video, "Create a vertical video.", "[]", "Addis Ababa", Now.AddDays(14), true, false,
        "Business may use the approved video for 90 days.", new Money(payment), creators, Pricing, Now,
        [(CreatorPlatform.TikTok, "TikTok-style", 10_000)]);

    [Fact]
    public void Required_funding_uses_decimal_creator_payment_capacity_and_versioned_fee()
    {
        var opportunity = New();
        Assert.Equal(1500m, opportunity.CreatorPayment.Amount * opportunity.CreatorCapacity);
        Assert.Equal(150m, opportunity.PlatformFee.Amount);
        Assert.Equal(1650m, opportunity.RequiredFunding.Amount);
    }

    [Fact]
    public void Rounded_per_assignment_fee_is_exactly_consumable_for_every_creator()
    {
        var opportunity = New(200.05m, 3); var wallet = new BusinessWallet(opportunity.BusinessId);
        Assert.Equal(20.01m, opportunity.PerAssignmentFee.Amount);
        Assert.Equal(60.03m, opportunity.PlatformFee.Amount);
        Assert.Equal(660.18m, opportunity.RequiredFunding.Amount);
        wallet.CreditDeposit(opportunity.RequiredFunding, Now, Guid.NewGuid());
        opportunity.Publish(wallet, Now, Guid.NewGuid());
        for (var i = 0; i < 3; i++)
        {
            opportunity.ApproveCreator();
            opportunity.RecognizeApprovedDeliverable(wallet, Now, Guid.NewGuid());
        }
        Assert.Equal(UgcOpportunityStatus.Completed, opportunity.Status);
        Assert.Equal(0m, opportunity.ReservedFunding.Amount);
        Assert.Equal(0m, wallet.ReservedBalance.Amount);
    }

    [Fact]
    public void Creator_payment_below_versioned_minimum_is_rejected() =>
        Assert.Throws<InvalidOperationException>(() => New(199));

    [Fact]
    public void Publish_reserves_full_funding_once_and_insufficient_available_funds_fail()
    {
        var opportunity = New(); var wallet = new BusinessWallet(opportunity.BusinessId);
        wallet.CreditDeposit(new Money(1650), Now, Guid.NewGuid());
        opportunity.Publish(wallet, Now, Guid.NewGuid());
        Assert.Equal(0m, wallet.AvailableBalance.Amount); Assert.Equal(1650m, wallet.ReservedBalance.Amount);
        Assert.Throws<InvalidOperationException>(() => opportunity.Publish(wallet, Now, Guid.NewGuid()));

        var insufficient = New(); var poorWallet = new BusinessWallet(insufficient.BusinessId);
        poorWallet.CreditDeposit(new Money(1649), Now, Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => insufficient.Publish(poorWallet, Now, Guid.NewGuid()));
    }

    [Fact]
    public void Approval_consumes_capacity_but_pending_request_does_not()
    {
        var opportunity = New(creators: 1); var wallet = new BusinessWallet(opportunity.BusinessId);
        wallet.CreditDeposit(opportunity.RequiredFunding, Now, Guid.NewGuid()); opportunity.Publish(wallet, Now, Guid.NewGuid());
        _ = new UgcCreatorRequest(opportunity.Id, Guid.NewGuid(), Now);
        Assert.Equal(0, opportunity.ApprovedCreatorCount);
        opportunity.ApproveCreator();
        Assert.Equal(1, opportunity.ApprovedCreatorCount);
        Assert.Throws<InvalidOperationException>(opportunity.ApproveCreator);
    }

    [Fact]
    public void Approved_deliverable_consumes_exact_assignment_and_fee()
    {
        var opportunity = New(); var wallet = new BusinessWallet(opportunity.BusinessId);
        wallet.CreditDeposit(opportunity.RequiredFunding, Now, Guid.NewGuid()); opportunity.Publish(wallet, Now, Guid.NewGuid());
        opportunity.ApproveCreator(); opportunity.RecognizeApprovedDeliverable(wallet, Now, Guid.NewGuid());
        Assert.Equal(550m, opportunity.UsedFunding.Amount); Assert.Equal(1100m, opportunity.ReservedFunding.Amount);
        Assert.Equal(1100m, wallet.ReservedBalance.Amount);
    }

    [Fact]
    public void Material_revision_requires_acceptance_before_submission()
    {
        var opportunity = New(); var creator = Guid.NewGuid(); var request = new UgcCreatorRequest(opportunity.Id, creator, Now);
        var assignment = new UgcAssignment(opportunity.Id, request.Id, creator, opportunity.CreatorPayment,
            opportunity.PerAssignmentFee, opportunity.CurrentRevision, Now);
        opportunity.StartMaterialRevision(); assignment.RequireRevisionAcceptance();
        Assert.Throws<InvalidOperationException>(assignment.Submitted);
        assignment.AcceptRevision(opportunity.CurrentRevision); assignment.Submitted();
        Assert.Equal(UgcAssignmentStatus.Submitted, assignment.Status);
    }

    [Fact]
    public void Published_ugc_without_approved_creator_can_cancel_and_release_exact_reserve()
    {
        var opportunity = New(); var wallet = new BusinessWallet(opportunity.BusinessId);
        wallet.CreditDeposit(opportunity.RequiredFunding, Now, Guid.NewGuid()); opportunity.Publish(wallet, Now, Guid.NewGuid());
        opportunity.Cancel(wallet, Now, Guid.NewGuid());
        Assert.Equal(UgcOpportunityStatus.Cancelled, opportunity.Status);
        Assert.Equal(1650m, wallet.AvailableBalance.Amount); Assert.Equal(0m, wallet.ReservedBalance.Amount);
    }
}
