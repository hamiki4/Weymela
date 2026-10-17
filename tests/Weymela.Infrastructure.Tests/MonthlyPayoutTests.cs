using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Web;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class MonthlyPayoutTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Monthly_controls_queue_search_and_payment_share_eligibility_and_prevent_duplicates()
    {
        var s=await Phase4Scenario.Create(fixture); await s.Refresh(3000);
        await s.Redeem(await s.Issue());
        await using var db=s.Database.Open();
        await ConsolidatedConfiguration.AddAsync(db,Scenario.Now,2,Scenario.Now);
        db.PublicWorkspaceProfiles.AddRange(new PublicWorkspaceProfile { SubjectId=s.Creator.CreatorId!.Value,Role=ActorRole.Creator,DisplayName="Monthly Creator",PublicId="CR-MONTHLY" },
            new PublicWorkspaceProfile { SubjectId=s.Customer.CustomerId!.Value,Role=ActorRole.Customer,DisplayName="Monthly Customer",PublicId="CU-MONTHLY" });
        await db.SaveChangesAsync();
        var destinations=new PayoutDestinationService(db,TestPayoutProtector.Instance,s.Clock);
        await destinations.UpdateAsync(s.Creator,new("Bank","CBE","123456789",RecipientName:"Monthly Creator"));
        await destinations.UpdateAsync(s.Customer,new("Bank","CBE","987654321",RecipientName:"Monthly Customer"));
        var payouts=new PayoutService(db,s.Clock,destinations);
        var operations=new Actor(Guid.NewGuid(),ActorRole.OperationsAdmin);
        Assert.Equal(0,(await payouts.EligibilityAsync(operations,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value)).EligibleAmount.Amount);
        s.Clock.Now=Scenario.Now.AddDays(1);
        Assert.Equal(245,(await payouts.EligibilityAsync(operations,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value)).EligibleAmount.Amount);
        Assert.Equal(20,(await payouts.EligibilityAsync(operations,PayoutBeneficiary.Customer,s.Customer.CustomerId.Value)).EligibleAmount.Amount);
        await ConsolidatedConfiguration.AddAsync(db,s.Clock.Now,2,Scenario.Now,enabled:false);
        Assert.Equal(0,(await payouts.EligibilityAsync(operations,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value)).EligibleAmount.Amount);
        await ConsolidatedConfiguration.AddAsync(db,s.Clock.Now,2,Scenario.Now);
        var queries=new WorkspaceQueries(db,new TestDirectory(),s.Clock,TestPayoutProtector.Instance);
        Assert.Equal(245,Assert.Single((await queries.OperationsPayoutsAsync(operations,default)).Creators).EligibleAmount);
        Assert.Equal(20,Assert.Single(await queries.PayoutRecipientsAsync(operations,PayoutBeneficiary.Customer,new AdminListFilter(Search:"Monthly Customer"),default)).EligibleAmount);
        await Assert.ThrowsAsync<ApplicationFailure>(()=>payouts.PrepareAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value,new Money(245),"unauthorized"));
        await Assert.ThrowsAsync<ApplicationFailure>(()=>payouts.PrepareAsync(operations,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value,new Money(246),"overpay"));
        var id=await payouts.PrepareAsync(operations,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value,new Money(245),"monthly");
        Assert.Equal(id,await payouts.PrepareAsync(operations,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value,new Money(245),"monthly"));
        await Assert.ThrowsAsync<ApplicationFailure>(()=>payouts.PrepareAsync(operations,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value,new Money(245),"duplicate"));
        await Assert.ThrowsAsync<ApplicationFailure>(()=>payouts.MarkPaidAsync(operations,id,"","missing-reference"));
        await payouts.MarkPaidAsync(operations,id,"isolated-test-confirmation","monthly-paid");
        Assert.Equal(0,(await payouts.EligibilityAsync(operations,PayoutBeneficiary.Creator,s.Creator.CreatorId.Value)).EligibleAmount.Amount);
        Assert.Single(await db.PayoutRecords.ToListAsync());
    }
}
