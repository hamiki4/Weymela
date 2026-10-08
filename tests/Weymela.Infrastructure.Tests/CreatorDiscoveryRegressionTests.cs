using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Web;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class CreatorDiscoveryRegressionTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData("ET", true, true)]
    [InlineData("", true, false)]
    [InlineData("Other region", true, false)]
    [InlineData("ET", false, false)]
    public async Task Weekend_special_funded_published_open_window_is_discoverable_only_to_eligible_creator(
        string region, bool published, bool visible)
    {
        var s=await Scenario.Create(fixture,funded:true,type:PromotionType.ViewPlusCommission);
        await using var db=s.Database.Open();
        var creator=new Actor(Guid.NewGuid(),ActorRole.Creator,CreatorId:Guid.NewGuid());
        db.PublicWorkspaceProfiles.AddRange(
            new PublicWorkspaceProfile { SubjectId=s.Business.BusinessId!.Value,Role=ActorRole.Business,DisplayName="Weekend Business",PublicId="BU-WEEKEND" },
            new PublicWorkspaceProfile { SubjectId=creator.CreatorId!.Value,Role=ActorRole.Creator,DisplayName="Weekend Creator",PublicId="CR-WEEKEND",Region=region });
        db.CommercePermissions.AddRange(
            new(s.Business.UserId,ActorRole.Business,s.Business.BusinessId.Value,s.Business.BusinessId,true,true),
            new(creator.UserId,ActorRole.Creator,creator.CreatorId.Value,null,true,false));
        var social=new CreatorSocialProfileRecord { CreatorId=creator.CreatorId.Value,Platform=CreatorPlatform.TikTok,
            ProfileUrl="https://www.tiktok.com/@weekend",SelfReportedAudience=30000,
            VerificationStatus="Verified",VerifiedAudience=30000,AudienceVerificationSource=SocialAudienceEligibility.AdminVerified,
            CreatedAtUtc=Scenario.Now,UpdatedAtUtc=Scenario.Now };
        db.CreatorSocialProfiles.Add(social);
        var promotion=await db.Promotions.Include(x=>x.Platforms).SingleAsync();
        db.Entry(promotion).Property(x=>x.Title).CurrentValue="Weekend Special";
        db.Entry(promotion).Property(x=>x.ApplicationClosesAtUtc).CurrentValue=Scenario.Now.AddDays(1);
        db.Entry(promotion).Property(x=>x.ContentDueAtUtc).CurrentValue=Scenario.Now.AddDays(2);
        if(published) { promotion.Publish(Scenario.Now,Guid.NewGuid()); promotion.Activate(Scenario.Now,Guid.NewGuid()); }
        await db.SaveChangesAsync();
        var clock=new TestClock();
        var query=new WorkspaceQueries(db,new PersistentWorkspaceDirectory(db),clock);
        var rows=await query.DiscoverAsync(creator,default);
        Assert.Equal(visible,rows.Any(x=>x.Id==promotion.Id));
        if(!visible)return;
        Assert.Equal("Weekend Special",Assert.Single(rows).Title);
        // Exactly at the deadline, discovery and the mutation must both reject a new applicant.
        clock.Now=Scenario.Now.AddDays(1);
        Assert.Empty(await query.DiscoverAsync(creator,default));
        await Assert.ThrowsAsync<ApplicationFailure>(()=>new FinancialCommands(db).JoinCampaignOnceAsync(
            new(creator,promotion.Id,"Join",null,new(null,region,30000,true),clock.Now,social.Id,CreatorPlatform.TikTok),"late-weekend"));
        clock.Now=Scenario.Now;
        var commands=new FinancialCommands(db);
        var application=await commands.JoinCampaignOnceAsync(
            new(creator,promotion.Id,"Join",null,new(null,region,30000,true),clock.Now,social.Id,CreatorPlatform.TikTok),"join-weekend");
        Assert.Equal(CreatorApplicationStatus.Pending,(await db.CreatorApplications.SingleAsync(x=>x.Id==application)).Status);
        var allocation=await commands.ApproveAndSetBudgetAsync(s.Business,application,new Money(1000),promotion.Version,"approve-weekend",clock.Now);
        Assert.NotEqual(Guid.Empty,allocation);
        Assert.Equal(CreatorApplicationStatus.Approved,(await db.CreatorApplications.SingleAsync(x=>x.Id==application)).Status);
        clock.Now=Scenario.Now.AddDays(1);
        Assert.Equal("Approved",Assert.Single(await query.DiscoverAsync(creator,default)).RequestStatus);
    }
}
