using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Transactions;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class ConfigurationAndLegalTests(PostgresFixture fixture)
{
    [Fact] public async Task Effective_pricing_returns_current_version()
    {
        var s = await Scenario.Create(fixture); await using var db = s.Database.Open();
        var v = await new FinancialConfigurationResolver(db).EffectiveAsync(Scenario.Now);
        Assert.Equal(s.PricingVersionId, v.Id);
        Assert.Equal(2500, v.CreatorPayoutThreshold.Amount);
    }
    [Fact] public async Task Future_pricing_does_not_apply_early()
    {
        var s = await Scenario.Create(fixture); await using var db = s.Database.Open();
        await AddVersion(s, 2, Scenario.Now.AddDays(1));
        Assert.Equal(1, (await new FinancialConfigurationResolver(db).EffectiveAsync(Scenario.Now)).Version);
    }
    [Fact] public async Task Pricing_becomes_effective_at_exact_boundary()
    {
        var s = await Scenario.Create(fixture); await AddVersion(s, 2, Scenario.Now.AddDays(1));
        await using var db = s.Database.Open();
        Assert.Equal(2, (await new FinancialConfigurationResolver(db).EffectiveAsync(Scenario.Now.AddDays(1))).Version);
    }
    [Fact] public async Task Equal_effective_dates_use_highest_version_deterministically()
    {
        var s = await Scenario.Create(fixture); await AddVersion(s, 3, Scenario.Now); await AddVersion(s, 2, Scenario.Now);
        await using var db = s.Database.Open();
        Assert.Equal(3, (await new FinancialConfigurationResolver(db).EffectiveAsync(Scenario.Now)).Version);
    }
    [Fact] public async Task Later_admin_configuration_does_not_rewrite_funded_snapshot()
    {
        var s = await Scenario.Create(fixture, funded: true); await AddVersion(s, 2, Scenario.Now, 600);
        await using var db = s.Database.Open(); var p = await db.Promotions.SingleAsync();
        Assert.Equal(300, p.PricingSnapshot.BusinessCharge.Amount); Assert.Equal(s.PricingVersionId, p.PricingSnapshot.ConfigurationVersionId);
    }
    [Fact] public async Task Business_cannot_create_financial_configuration()
    {
        var s = await Scenario.Create(fixture); var id = Guid.NewGuid(); await using var db = s.Database.Open();
        var v = new FinancialConfigurationVersion(id, s.ConfigurationId, 2, s.Business.UserId, Scenario.Now,
            Scenario.Price(PromotionType.ViewOnly, id), Scenario.Price(PromotionType.ViewPlusCommission, id), new Money(2500), new Money(500));
        var ex = await Assert.ThrowsAsync<ApplicationFailure>(() => new FinancialCommands(db).CreateFinancialConfigurationAsync(s.Business, v));
        Assert.Equal(FailureKind.Forbidden, ex.Kind);
    }
    [Fact] public async Task Legal_acceptance_round_trips_exact_user_role_and_document_version()
    {
        var database = await fixture.CreateAsync(); var user = Guid.NewGuid(); var version = Guid.NewGuid();
        await using var db = database.Open();
        db.LegalDocumentVersions.Add(new(version, LegalDocumentType.BusinessAgreement, "1", "hash", Scenario.Now));
        db.LegalAcceptances.Add(new(user, LegalRole.Business, version, Scenario.Now, null, null));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await new LegalAcceptanceGate(db, new FixedClock(Scenario.Now)).EnsureCurrentAcceptedAsync(user, LegalRole.Business, [LegalDocumentType.BusinessAgreement], default);
        Assert.Equal(version, (await db.LegalAcceptances.SingleAsync()).DocumentVersionId);
    }
    [Fact] public async Task New_legal_document_requires_new_acceptance()
    {
        var database = await fixture.CreateAsync(); var user = Guid.NewGuid(); var old = Guid.NewGuid(); await using var db = database.Open();
        db.LegalDocumentVersions.Add(new(old, LegalDocumentType.BusinessAgreement, "1", "a", Scenario.Now));
        db.LegalDocumentVersions.Add(new(Guid.NewGuid(), LegalDocumentType.BusinessAgreement, "2", "b", Scenario.Now.AddDays(1)));
        db.LegalAcceptances.Add(new(user, LegalRole.Business, old, Scenario.Now, null, null)); await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<ApplicationFailure>(() => new LegalAcceptanceGate(db, new FixedClock(Scenario.Now.AddDays(1)))
            .EnsureCurrentAcceptedAsync(user, LegalRole.Business, [LegalDocumentType.BusinessAgreement], default));
        Assert.Equal(FailureKind.Forbidden, ex.Kind);
    }
    [Fact] public async Task Missing_legal_document_fails_closed()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        await Assert.ThrowsAsync<ApplicationFailure>(() => new LegalAcceptanceGate(db, new FixedClock(Scenario.Now))
            .EnsureCurrentAcceptedAsync(Guid.NewGuid(), LegalRole.Creator, [LegalDocumentType.CreatorAgreement], default));
    }

    private static async Task AddVersion(Scenario s, int number, DateTime effective, decimal charge = 300)
    {
        await using var db = s.Database.Open(); var id = Guid.NewGuid(); var actor = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);
        await new FinancialCommands(db).CreateFinancialConfigurationAsync(actor, new FinancialConfigurationVersion(id, s.ConfigurationId, number, actor.UserId,
            effective, Scenario.Price(PromotionType.ViewOnly, id, charge), Scenario.Price(PromotionType.ViewPlusCommission, id, charge), new Money(2500), new Money(500)));
    }
    private sealed class FixedClock(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(now); }
}
