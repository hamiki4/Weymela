using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Transactions;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class ConsolidatedFundingTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Zero_budget_after_QR_sales_can_be_replenished_without_views_or_new_sale_entries()
    {
        var s=await Phase4Scenario.Create(fixture,PromotionType.ViewPlusCommission,6000,6000);
        await s.Redeem(await s.Issue(),60000);
        await using var db=s.Database.Open();
        await ConsolidatedConfiguration.AddAsync(db,Scenario.Now,1,Scenario.Now.AddMonths(1));
        var queries=new Weymela.Infrastructure.Web.WorkspaceQueries(db,new TestDirectory(),s.Clock);
        var low=Assert.Single(await queries.BusinessPromotionTrackingAsync(s.Seed.Business,default));
        Assert.True(low.LowFunds);Assert.Equal("Active",low.Group);Assert.Equal(0,low.Remaining);
        var sale=await db.VerifiedSales.AsNoTracking().SingleAsync();Assert.Equal(s.Cashier.UserId,sale.CashierId);
        Assert.Empty(await db.ViewRewardReceipts.ToListAsync());
        var before=await db.Promotions.AsNoTracking().SingleAsync();
        var wallet=await db.BusinessWallets.AsNoTracking().SingleAsync();
        var commands=new FinancialCommands(db);
        await commands.AddPromotionFundsAsync(s.Seed.Business,s.Seed.PromotionId,new Money(1000),before.Version,wallet.Version,"empty-top-up",Scenario.Now,default);
        db.ChangeTracker.Clear();
        Assert.False(Assert.Single(await queries.BusinessPromotionTrackingAsync(s.Seed.Business,default)).LowFunds);
        var allocation=await db.CreatorAllocations.AsNoTracking().SingleAsync();
        await commands.IncreaseCreatorAllocationAsync(new(s.Seed.Business,allocation.Id,new Money(1000),allocation.Version,Scenario.Now),"empty-allocation");
        db.ChangeTracker.Clear();
        Assert.Equal(PromotionStatus.Active,(await db.Promotions.SingleAsync()).Status);
        Assert.Equal(1000,(await db.CreatorAllocations.SingleAsync()).RemainingAmount.Amount);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(sale),System.Text.Json.JsonSerializer.Serialize(await db.VerifiedSales.AsNoTracking().SingleAsync()));
        Assert.Empty(await new Weymela.Infrastructure.Operations.ReconciliationService(db).CheckAsync(Phase4Scenario.Admin,default));
    }

    [Fact]
    public async Task Replenishment_uses_wallet_funds_preserves_terms_and_is_idempotent()
    {
        var s=await Scenario.Create(fixture,funded:true);
        await using var db=s.Database.Open();
        var before=await db.Promotions.AsNoTracking().SingleAsync();
        var wallet=await db.BusinessWallets.AsNoTracking().SingleAsync();
        var commands=new FinancialCommands(db);
        await commands.AddPromotionFundsAsync(s.Business,s.PromotionId,new Money(1000),before.Version,wallet.Version,"top-up",Scenario.Now,default);
        db.ChangeTracker.Clear();
        var after=await db.Promotions.AsNoTracking().SingleAsync();
        var funded=await db.BusinessWallets.AsNoTracking().SingleAsync();
        Assert.Equal(before.TotalBudget.Amount+1000,after.TotalBudget.Amount);
        Assert.Equal(before.ReservedBudget.Amount+1000,after.ReservedBudget.Amount);
        Assert.Equal(wallet.AvailableBalance.Amount-1000,funded.AvailableBalance.Amount);
        Assert.Equal(wallet.TotalBalance,funded.TotalBalance);
        Assert.Equal(before.PricingSnapshot,after.PricingSnapshot);
        Assert.Equal(before.EndDateUtc,after.EndDateUtc);
        var journals=await db.FinancialJournals.CountAsync();
        await commands.AddPromotionFundsAsync(s.Business,s.PromotionId,new Money(1000),before.Version,wallet.Version,"top-up",Scenario.Now,default);
        Assert.Equal(journals,await db.FinancialJournals.CountAsync());
        var forbidden=await Assert.ThrowsAsync<ApplicationFailure>(()=>commands.AddPromotionFundsAsync(
            new Actor(Guid.NewGuid(),ActorRole.Business,Guid.NewGuid()),s.PromotionId,new Money(1),after.Version,funded.Version,"other",Scenario.Now,default));
        Assert.Equal(FailureKind.Forbidden,forbidden.Kind);
    }
}
