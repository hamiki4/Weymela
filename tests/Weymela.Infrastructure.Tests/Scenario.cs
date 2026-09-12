using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Tests;

internal sealed record Scenario(TestDatabase Database, Actor Business, Guid PromotionId, Guid ConfigurationId, Guid PricingVersionId)
{
    public static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static PricingSnapshot Price(PromotionType type, Guid version, decimal charge = 300, DateTime? effective = null) =>
        new(type, 1000, new Money(charge), new Money(charge * 2 / 3), new Money(charge / 3), 4.5m, 2m, 3.5m, effective ?? Now, version);

    public static async Task<Scenario> Create(PostgresFixture fixture, decimal deposit = 10000, bool funded = false, decimal budget = 6000,
        PromotionType type = PromotionType.ViewOnly, int viewsPerReward = 1000, decimal creatorThreshold = 2500, decimal customerThreshold = 500)
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var business = new Actor(Guid.NewGuid(), ActorRole.Business, Guid.NewGuid());
        var configurationId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        db.BusinessWallets.Add(new BusinessWallet(business.BusinessId!.Value));
        db.FinancialConfigurations.Add(new(configurationId, "PlatformPricing"));
        var version = new FinancialConfigurationVersion(versionId, configurationId, 1, Guid.NewGuid(), Now,
            Price(PromotionType.ViewOnly, versionId) with { ViewsPerReward = viewsPerReward },
            Price(PromotionType.ViewPlusCommission, versionId) with { ViewsPerReward = viewsPerReward }, new Money(creatorThreshold), new Money(customerThreshold));
        db.FinancialConfigurationVersions.Add(version);
        var p = new Promotion(business.BusinessId.Value, "Campaign", "Brief", type, new Money(budget), new(null, null, "ET", "Content"), Now, Now.AddDays(30), (type == PromotionType.ViewOnly ? version.ViewOnly : version.ViewPlusCommission) with { }, Now);
        db.Promotions.Add(p);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var commands = new FinancialCommands(db);
        if (deposit > 0) await commands.CreditDepositAsync(new(business, new Money(deposit), "seed-deposit", Now), 0);
        if (funded) await commands.FundPromotionAsync(new(business, p.Id, 0, deposit > 0 ? 1 : 0, "seed-fund", Now));
        return new(database, business, p.Id, configurationId, versionId);
    }

    public async Task<Guid> ApprovedCreator()
    {
        await using var db = Database.Open();
        var p = await db.Promotions.Include(x => x.Allocations).SingleAsync(x => x.Id == PromotionId);
        if (p.Status == PromotionStatus.Funded) p.Publish(Now, Guid.NewGuid());
        var creator = Guid.NewGuid();
        var application = new CreatorApplication(p.Id, creator, "Join", null, p, Now, Guid.NewGuid());
        application.Approve(Business.UserId, Now); db.CreatorApplications.Add(application);
        await db.SaveChangesAsync();
        return creator;
    }

    public async Task<Guid> Assign(Guid creator, decimal amount = 2000, string key = "assign")
    {
        await using var db = Database.Open();
        var p = await db.Promotions.SingleAsync(x => x.Id == PromotionId);
        return await new FinancialCommands(db).AssignCreatorAllocationAsync(new(Business, PromotionId, creator, new Money(amount), p.Version, Now), key);
    }
}
