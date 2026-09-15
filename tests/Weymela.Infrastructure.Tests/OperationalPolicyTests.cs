using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Notifications;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class OperationalPolicyTests(PostgresFixture fixture)
{
    [Fact] public async Task Future_financial_configuration_does_not_notify_early_and_only_affected_role_is_notified_at_boundary()
    {
        var s=await Phase4Scenario.Create(fixture); await using var db=s.Database.Open();
        var worker=new WorkerPump(db,new RuntimeOptions { WorkerBatchSize=50 },new DisabledPushProvider(),s.Clock);
        await worker.RunOnceAsync(default); await worker.RunOnceAsync(default);
        var current=await db.FinancialConfigurationVersions.SingleAsync(); var id=Guid.NewGuid(); var effective=s.Clock.Now.AddHours(1);
        db.FinancialConfigurationVersions.Add(new(id,current.ConfigurationId,2,Phase4Scenario.Admin.UserId,effective,
            current.ViewOnly with { ConfigurationVersionId=id, EffectiveFromUtc=effective }, current.ViewPlusCommission with { ConfigurationVersionId=id, EffectiveFromUtc=effective },
            current.CreatorPayoutThreshold,new Money(6000)));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); await worker.RunOnceAsync(default);
        Assert.False(await db.InAppNotifications.AnyAsync(x=>x.SourceKey=="FinancialConfigurationEffective:"+id));
        s.Clock.Now=effective; await worker.RunOnceAsync(default); await worker.RunOnceAsync(default);
        var notice=await db.InAppNotifications.SingleAsync(x=>x.SourceKey=="FinancialConfigurationEffective:"+id);
        Assert.Equal(s.Customer.UserId,notice.UserId); Assert.Equal(ActorRole.Customer,notice.Role);
        Assert.Equal(s.Seed.PricingVersionId,(await db.Promotions.SingleAsync()).PricingSnapshot.ConfigurationVersionId);
    }
    [Fact] public async Task Real_budget_topup_event_targets_its_creator_and_routes_to_own_budget()
    {
        var s=await Phase4Scenario.Create(fixture); await using var db=s.Database.Open();
        var version=(await db.CreatorAllocations.SingleAsync()).Version; db.ChangeTracker.Clear();
        await new FinancialCommands(db,s.Clock).IncreaseCreatorAllocationAsync(new(s.Seed.Business,s.AllocationId,new Money(50),version,s.Clock.Now),"topup");
        await new OutboxProcessor(db,new RuntimeOptions { WorkerBatchSize=50 },new DisabledPushProvider(),s.Clock).ProcessAsync(default);
        var notice=await db.InAppNotifications.SingleAsync(x=>x.EventType=="CreatorBudgetIncreasedAudit");
        Assert.Equal(s.Creator.UserId,notice.UserId); Assert.Equal("/creator/campaigns/"+s.AllocationId,notice.Route);
    }
    [Fact] public async Task Payout_eligibility_and_paid_events_target_only_the_beneficiary_and_reconcile_after_settlement()
    {
        var s=await Phase4Scenario.Create(fixture,threshold:20); await s.Refresh(3000); await s.Redeem(await s.Issue()); await using var db=s.Database.Open();
        var payout=await s.Payouts(db).PrepareAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,"prepare");
        await s.Payouts(db).MarkPaidAsync(Phase4Scenario.Admin,payout,"confirmed","paid");
        await s.Payouts(db).SettlePlatformAsync(Phase4Scenario.Admin,new Money(30),"settled","settle");
        var worker=new OutboxProcessor(db,new RuntimeOptions { WorkerBatchSize=50 },new DisabledPushProvider(),s.Clock);
        await worker.ProcessAsync(default); await worker.ProcessAsync(default);
        Assert.Equal(s.Creator.UserId,(await db.InAppNotifications.SingleAsync(x=>x.EventType=="CreatorPayoutEligible")).UserId);
        Assert.Equal(s.Customer.UserId,(await db.InAppNotifications.SingleAsync(x=>x.EventType=="CustomerPayoutEligible")).UserId);
        Assert.Equal(s.Creator.UserId,(await db.InAppNotifications.SingleAsync(x=>x.EventType=="PayoutPaid")).UserId);
        Assert.Empty(await new ReconciliationService(db).CheckAsync(Phase4Scenario.Admin,default));
    }
    [Fact] public async Task Missing_current_legal_acceptance_blocks_until_exact_version_is_explicitly_accepted()
    {
        var s=await Phase4Scenario.Create(fixture); await using var db=s.Database.Open(); var service=new LegalWorkspaceService(db,s.Clock);
        var id=Guid.NewGuid(); s.Clock.Now=s.Clock.Now.AddMinutes(1);
        db.LegalDocumentVersions.Add(new(id,LegalDocumentType.CreatorAgreement,"2","new-content-hash",s.Clock.Now)); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Contains(await service.CurrentAsync(s.Creator,default),x=>x.Id==id&&!x.Accepted);
        await Assert.ThrowsAsync<ApplicationFailure>(()=>service.AcceptAsync(s.Creator,id,"old-content-hash",true,default));
        await Assert.ThrowsAsync<ApplicationFailure>(()=>service.AcceptAsync(s.Creator,id,"new-content-hash",false,default));
        await service.AcceptAsync(s.Creator,id,"new-content-hash",true,default); await service.AcceptAsync(s.Creator,id,"new-content-hash",true,default);
        Assert.Single(await db.LegalAcceptances.Where(x=>x.DocumentVersionId==id).ToListAsync());
        Assert.Contains(await service.CurrentAsync(s.Creator,default),x=>x.Id==id&&x.Accepted);
    }
    [Fact] public async Task Readiness_rejects_missing_worker_heartbeat_and_accepts_successful_bounded_cycle()
    {
        var s=await Phase4Scenario.Create(fixture); await using var db=s.Database.Open(); var options=new RuntimeOptions { Development=true,WorkerEnabled=true };
        var health=new OperationalHealth(db,options,s.Clock); Assert.False((await health.ReadinessAsync(default)).Ready);
        await new WorkerPump(db,options,new DisabledPushProvider(),s.Clock).RunOnceAsync(default); Assert.True((await health.ReadinessAsync(default)).Ready);
        s.Clock.Now=s.Clock.Now.AddMinutes(2); Assert.False((await health.ReadinessAsync(default)).Ready);
    }
    [Fact] public async Task Readiness_requires_recent_success_seen_state_and_no_failed_worker_cycle()
    {
        var s=await Phase4Scenario.Create(fixture); await using var db=s.Database.Open();
        var options=new RuntimeOptions { Development=true,WorkerEnabled=true,WorkerIntervalSeconds=5 };
        db.WorkerCheckpoints.Add(new WorkerCheckpoint { Name="operational-worker",LastSeenAtUtc=s.Clock.Now,LastSuccessAtUtc=s.Clock.Now.AddMinutes(-2) });
        await db.SaveChangesAsync();
        var health=new OperationalHealth(db,options,s.Clock);
        Assert.False((await health.ReadinessAsync(default)).Ready);
        var checkpoint=await db.WorkerCheckpoints.SingleAsync();
        checkpoint.LastSuccessAtUtc=s.Clock.Now; checkpoint.LastErrorCode="WorkerUnavailable"; await db.SaveChangesAsync();
        Assert.False((await health.ReadinessAsync(default)).Ready);
        checkpoint.LastErrorCode=null; await db.SaveChangesAsync();
        Assert.True((await health.ReadinessAsync(default)).Ready);
    }
    [Fact] public async Task Live_readiness_rejects_missing_trusted_admin_identity_even_when_database_is_healthy()
    {
        var s=await Phase4Scenario.Create(fixture); await using var db=s.Database.Open();
        Assert.False((await new OperationalHealth(db,new RuntimeOptions { FirebaseProjectId="isolated-v3-test",WorkerEnabled=false },s.Clock).ReadinessAsync(default)).Ready);
    }
    [Fact] public async Task Live_readiness_accepts_only_exact_active_platform_admin_mapping()
    {
        var s=await Phase4Scenario.Create(fixture); await using var db=s.Database.Open();
        var options=new RuntimeOptions { FirebaseProjectId="isolated-v3-test",WorkerEnabled=false };
        db.IdentityBindings.Add(new IdentityBinding { Provider="Firebase",ProjectId="isolated-v3-test",ExternalSubject="admin-ready",
            UserId=Phase4Scenario.Admin.UserId,IsActive=true,ValidAfterUtc=s.Clock.Now.AddMinutes(-1),Version=1 });
        db.CommercePermissions.Add(new(Phase4Scenario.Admin.UserId,ActorRole.PlatformAdmin,
            Phase4Scenario.Admin.UserId,null,true,false));
        db.LegalDocumentVersions.AddRange(
            new(Guid.NewGuid(),LegalDocumentType.TermsOfService,"pilot-1","terms-hash",s.Clock.Now.AddMinutes(-1)),
            new(Guid.NewGuid(),LegalDocumentType.PrivacyPolicy,"pilot-1","privacy-hash",s.Clock.Now.AddMinutes(-1)));
        await db.SaveChangesAsync();
        var health=new OperationalHealth(db,options,s.Clock);
        Assert.True((await health.ReadinessAsync(default)).Ready);
        var conflictingUser=Guid.NewGuid();
        db.IdentityBindings.Add(new IdentityBinding { Provider="Firebase",ProjectId="other-project",ExternalSubject="conflicting-admin",
            UserId=conflictingUser,IsActive=true,ValidAfterUtc=s.Clock.Now.AddMinutes(-1),Version=1 });
        db.CommercePermissions.Add(new(conflictingUser,ActorRole.PlatformAdmin,conflictingUser,null,true,false));
        await db.SaveChangesAsync();
        Assert.False((await health.ReadinessAsync(default)).Ready);
        await db.CommercePermissions.Where(x=>x.UserId==conflictingUser).ExecuteDeleteAsync();
        await db.IdentityBindings.Where(x=>x.UserId==conflictingUser).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        Assert.True((await health.ReadinessAsync(default)).Ready);
        await db.CommercePermissions.Where(x=>x.UserId==Phase4Scenario.Admin.UserId&&x.Role==ActorRole.PlatformAdmin).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        db.CommercePermissions.Add(new(Phase4Scenario.Admin.UserId,ActorRole.PlatformAdmin,Guid.NewGuid(),null,true,false));
        await db.SaveChangesAsync();
        Assert.False((await health.ReadinessAsync(default)).Ready);
    }
    [Fact] public async Task Creator_completion_reconciles_campaign_reserve_without_refunding_business_available()
    {
        var s=await Phase4Scenario.Create(fixture); await s.Refresh(3000); await using var db=s.Database.Open();
        var available=(await db.BusinessWallets.SingleAsync()).AvailableBalance; var a=await db.CreatorAllocations.SingleAsync(); db.ChangeTracker.Clear();
        await new FinancialCommands(db,s.Clock).CompleteCreatorParticipationAsync(new(s.Creator,a.Id,a.Version,s.Clock.Now),"complete");
        Assert.Equal(available,(await db.BusinessWallets.SingleAsync()).AvailableBalance);
        Assert.Empty(await new ReconciliationService(db).CheckAsync(Phase4Scenario.Admin,default));
    }
}
