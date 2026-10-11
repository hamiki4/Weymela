using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class InactiveFinancialBaselineTests(PostgresFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeProvider Clock = new FixedClock();

    [Fact]
    public async Task Approved_baseline_keeps_unknown_prices_absent_and_replays_without_financial_obligations()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var admin = await SeedAdmin(db);
        var request = Request(admin);
        var first = await Bootstrap(db).ProvisionInactiveAsync(request);
        Assert.Equal(first, await Bootstrap(db).ProvisionInactiveAsync(request));
        Assert.Equal(first, await InactiveFinancialBaselines.EffectiveAsync(db, "weymela-production", Now, default));
        Assert.Equal(4, first.Approval.CustomerCashbackPercent);
        Assert.Equal(5, first.Approval.CreatorCommissionPercent);
        Assert.Equal(1, first.Approval.PlatformPercent);
        Assert.Empty(await db.FinancialConfigurations.ToListAsync());
        Assert.Empty(await db.FinancialConfigurationVersions.ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
        Assert.Empty(await db.BusinessWallets.ToListAsync());
        Assert.Empty(await db.PayoutRecords.ToListAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync());
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync());

        // Every promotion and payout requires a complete effective pricing version.
        var resolver = new FinancialConfigurationResolver(db);
        foreach (var type in new[] { PromotionType.ViewOnly, PromotionType.ViewPlusCommission })
            await Assert.ThrowsAsync<ApplicationFailure>(() => resolver.ResolvePromotionAsync(type, Now, default));
        await Assert.ThrowsAsync<ApplicationFailure>(() => resolver.EffectiveAsync(Now));
        await Assert.ThrowsAsync<ApplicationFailure>(() => new PayoutService(db, Clock).PrepareAsync(
            new Actor(admin, ActorRole.PlatformAdmin), PayoutBeneficiary.Creator, Guid.NewGuid(), new Money(100), "payout", default));
        Assert.Empty(await db.Promotions.ToListAsync());
        Assert.Empty(await db.UgcOpportunities.ToListAsync());
        Assert.Empty(await db.PayoutRecords.ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
    }

    [Fact]
    public async Task Readiness_requires_legal_admin_and_paused_products_even_with_approved_baseline()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var admin = await SeedAdmin(db);
        await Bootstrap(db).ProvisionInactiveAsync(Request(admin));
        var options = new RuntimeOptions { EnvironmentName = "Production", FirebaseProjectId = "weymela-production" };
        Assert.False((await new OperationalHealth(db, options, Clock).ReadinessAsync(default)).Ready);
        db.LegalDocumentVersions.AddRange(
            new(Guid.NewGuid(), LegalDocumentType.TermsOfService, "test-only", "test-terms-hash", Now),
            new(Guid.NewGuid(), LegalDocumentType.PrivacyPolicy, "test-only", "test-privacy-hash", Now));
        await db.SaveChangesAsync();
        Assert.True((await new OperationalHealth(db, options, Clock).ReadinessAsync(default)).Ready);
        Assert.False((await new OperationalHealth(db, new RuntimeOptions
            { FirebaseProjectId = "weymela-production", FinancialWritesEnabled = true }, Clock).ReadinessAsync(default)).Ready);
        Assert.False((await new OperationalHealth(db, new RuntimeOptions
            { FirebaseProjectId = "weymela-production", DepositMode = "ManualApproval" }, Clock).ReadinessAsync(default)).Ready);
        Assert.Null(await InactiveFinancialBaselines.EffectiveAsync(db, "weymela-pilot", Now, default));
        await db.CommercePermissions.ExecuteDeleteAsync();
        Assert.False((await new OperationalHealth(db, options, Clock).ReadinessAsync(default)).Ready);
    }

    [Fact]
    public async Task Conflicting_replay_and_second_approval_cannot_overwrite_the_baseline()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var admin = await SeedAdmin(db);
        var request = Request(admin);
        await Bootstrap(db).ProvisionInactiveAsync(request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bootstrap(db).ProvisionInactiveAsync(
            request with { PlatformPercent = 2 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bootstrap(db).ProvisionInactiveAsync(
            request with { IdempotencyKey = "other-approval" }));
        await db.CommercePermissions.ExecuteDeleteAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bootstrap(db).ProvisionInactiveAsync(request));
        Assert.Equal(1, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Production_approval_and_real_bound_platform_admin_are_required()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var admin = await SeedAdmin(db);
        var request = Request(admin);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bootstrap(db).ProvisionInactiveAsync(
            request with { ProductionAuthorizationReference = null }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bootstrap(db).ProvisionInactiveAsync(
            request with { PlatformAdminUserId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ArgumentException>(() => Bootstrap(db).ProvisionInactiveAsync(
            request with { CustomerCashbackPercent = 99 }));
        await Assert.ThrowsAsync<ArgumentException>(() => Bootstrap(db).ProvisionInactiveAsync(
            request with { EffectiveFromUtc = Now.AddDays(1) }));
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    private static FinancialConfigurationBootstrapper Bootstrap(WeymelaDbContext db) =>
        new(db, TestBootstrapTargets.Select("production"), Clock);
    private static InactiveFinancialBaselineRequest Request(Guid admin) => new(admin, 4, 5, 1, Now,
        "isolated-test-operator", Guid.NewGuid(), "baseline-v1", "isolated-test-owner-approval");
    private static async Task<Guid> SeedAdmin(WeymelaDbContext db)
    {
        var admin = Guid.NewGuid();
        db.CommercePermissions.Add(new(admin, ActorRole.PlatformAdmin, admin, null, true, false));
        db.IdentityBindings.Add(new IdentityBinding { Provider = "Firebase", ProjectId = "weymela-production",
            ExternalSubject = "isolated-test-owner", UserId = admin, IsActive = true, Version = 1, ValidAfterUtc = Now.AddDays(-1) });
        await db.SaveChangesAsync();
        return admin;
    }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
}
