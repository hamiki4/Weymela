using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Web;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class ProductFinancialSettingsTests(PostgresFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 12, 4, 0, 0, DateTimeKind.Utc);
    private static readonly TimeProvider Clock = new FixedClock();
    private static readonly RuntimeOptions Production = new() { EnvironmentName="Production", FirebaseProjectId="weymela-production" };
    private static FinancialSettingsInput Baseline => new(null,null,5,4,1,null,null,null);

    [Fact]
    public async Task Real_bound_platform_admin_can_initialize_partial_audited_settings_and_replay()
    {
        var database=await fixture.CreateAsync();await using var db=database.Open();var admin=await Admin(db);
        var commands=new WorkspaceCommands(db,null!,Clock,Production);
        var id=await commands.SettingsAsync(admin,Baseline,0,"initial-settings",default);
        Assert.Equal(id,await commands.SettingsAsync(admin,Baseline,0,"initial-settings",default));
        db.ChangeTracker.Clear();var v=await db.FinancialConfigurationVersions.SingleAsync();
        Assert.Equal(admin.UserId,v.ChangedBy);Assert.Equal(1,v.Version);
        Assert.Equal(4,v.CustomerCashbackPercent);Assert.Equal(5,v.CreatorCommissionPercent);Assert.Equal(1,v.PlatformPercent);
        Assert.Null(v.ViewOnly);Assert.Null(v.ViewPlusCommission);Assert.Null(v.Ugc);
        Assert.Null(v.CreatorPayoutThreshold);Assert.Null(v.CustomerPayoutThreshold);
        Assert.Null(v.PromotionLiveDurationDays);Assert.Null(v.EnforceAudienceRequirements);
        Assert.Single(await db.AuditEvents.Where(x=>x.EventType=="FinancialConfigurationChanged"&&x.ActorId==admin.UserId).ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());Assert.Empty(await db.BusinessWallets.ToListAsync());
        Assert.Empty(await db.PayoutRecords.ToListAsync());
        Assert.Equal(FailureKind.IdempotencyConflict,(await Assert.ThrowsAsync<ApplicationFailure>(()=>
            commands.SettingsAsync(admin,Baseline with { CustomerThreshold=100 },0,"initial-settings",default))).Kind);
        Assert.Equal(FailureKind.ConcurrencyConflict,(await Assert.ThrowsAsync<ApplicationFailure>(()=>
            commands.SettingsAsync(admin,Baseline,0,"stale-settings",default))).Kind);
    }

    [Fact]
    public async Task Missing_or_wrong_project_binding_cannot_initialize_production_settings()
    {
        var database=await fixture.CreateAsync();await using var db=database.Open();var admin=await Admin(db,"weymela-pilot");
        var commands=new WorkspaceCommands(db,null!,Clock,Production);
        Assert.Equal(FailureKind.Forbidden,(await Assert.ThrowsAsync<ApplicationFailure>(()=>commands.SettingsAsync(admin,Baseline,0,"denied",default))).Kind);
        Assert.Empty(await db.FinancialConfigurations.ToListAsync());Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task View_only_configuration_does_not_require_other_products_or_payout_minimums()
    {
        var database=await fixture.CreateAsync();await using var db=database.Open();var admin=await Admin(db);
        await new WorkspaceCommands(db,null!,Clock,Production).SettingsAsync(admin,Baseline with {
            ViewOnly=new(1000,10,7,3,null),PromotionLiveDurationDays=14,EnforceAudienceRequirements=false
        },0,"view-only",default);
        var resolver=new FinancialConfigurationResolver(db);
        var snapshot=await resolver.ResolvePromotionAsync(PromotionType.ViewOnly,Now,default);
        Assert.Equal(10,snapshot.Pricing.BusinessCharge.Amount);
        Assert.Equal("ProductPricingRequired",(await Assert.ThrowsAsync<ApplicationFailure>(()=>
            resolver.ResolvePromotionAsync(PromotionType.ViewPlusCommission,Now,default))).Code);
        Assert.Equal("PayoutConfigurationRequired",(await Assert.ThrowsAsync<ApplicationFailure>(()=>
            new PayoutService(db,Clock).PrepareAsync(admin,PayoutBeneficiary.Creator,Guid.NewGuid(),new Money(1),"payout",default))).Code);
        Assert.Empty(await db.PayoutRecords.ToListAsync());Assert.Empty(await db.FinancialJournals.ToListAsync());
    }

    [Fact]
    public async Task Missing_payout_minimum_does_not_emit_false_eligibility_or_abort_earnings_processing()
    {
        var database=await fixture.CreateAsync();await using var db=database.Open();var admin=await Admin(db);
        await new WorkspaceCommands(db,null!,Clock,Production).SettingsAsync(admin,Baseline,0,"no-thresholds",default);
        await new FinancialOperation(db).EmitEligibility(PayoutBeneficiary.Creator,Guid.NewGuid(),Money.Zero(),new Money(100),Now,default);
        await new FinancialOperation(db).EmitEligibility(PayoutBeneficiary.Customer,Guid.NewGuid(),Money.Zero(),new Money(100),Now,default);
        await db.SaveChangesAsync();
        Assert.False(await db.OutboxMessages.AnyAsync(x=>x.EventType==nameof(CreatorPayoutEligible)||x.EventType==nameof(CustomerPayoutEligible)));
    }

    [Fact]
    public async Task Wallet_balances_and_histories_remain_readable_without_payout_minimums()
    {
        var s=await Phase4Scenario.Create(fixture);
        await s.Redeem(await s.Issue(),1000);
        await using var db=s.Database.Open();
        var root=await db.FinancialConfigurationVersions.SingleAsync();
        db.FinancialConfigurationVersions.Add(FinancialConfigurationVersionFactory.Create(Guid.NewGuid(),root.ConfigurationId,2,
            Phase4Scenario.Admin.UserId,Scenario.Now,Baseline));await db.SaveChangesAsync();
        var summary=await s.Queries(db).CustomerCashbackAsync(s.Customer);
        Assert.Equal(20,summary.AvailableCashback.Amount);Assert.Null(summary.MinimumCashOut);Assert.Null(summary.RemainingToCashOut);
        Assert.False(summary.Eligible);Assert.Equal("ConfigurationRequired",summary.Status);
        var earnings=await new WorkspaceQueries(db,new TestDirectory(),s.Clock).EarningsAsync(s.Creator,default);
        Assert.True(earnings.AvailableEarnings>0);Assert.Null(earnings.MinimumToCashOut);Assert.Null(earnings.AmountNeeded);
        Assert.Equal(0,earnings.EligibleAmount);
    }

    private static async Task<Actor> Admin(WeymelaDbContext db,string project="weymela-production")
    {
        var id=Guid.NewGuid();db.CommercePermissions.Add(new(id,ActorRole.PlatformAdmin,id,null,true,false));
        db.IdentityBindings.Add(new IdentityBinding { UserId=id,Provider="Firebase",ProjectId=project,ExternalSubject="isolated-admin-"+id,
            IsActive=true,Version=1,ValidAfterUtc=Now.AddDays(-1) });await db.SaveChangesAsync();return new(id,ActorRole.PlatformAdmin);
    }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow()=>new(Now); }
}
