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
