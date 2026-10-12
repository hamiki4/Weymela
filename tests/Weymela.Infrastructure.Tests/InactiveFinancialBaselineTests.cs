using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Web;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class InactiveFinancialBaselineTests(PostgresFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeProvider Clock = new FixedClock();

    [Fact]
    public async Task Platform_admin_can_read_unconfigured_settings_without_creating_a_pricing_version()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var admin = await SeedAdmin(db);
        await Bootstrap(db).ProvisionInactiveAsync(Request(admin));
        var workspace = await new WorkspaceQueries(db, null!, Clock, options: new RuntimeOptions
            { EnvironmentName = "Production", FirebaseProjectId = "weymela-production" })
            .SettingsAsync(new Actor(admin, ActorRole.PlatformAdmin), default);
        Assert.Null(workspace.Current);
        Assert.Equal(0, workspace.Version);
        Assert.Empty(workspace.Versions);
        Assert.NotNull(workspace.InactiveBaseline);
        Assert.Equal(4, workspace.InactiveBaseline.CustomerCashbackPercent);
        Assert.Equal(5, workspace.InactiveBaseline.CreatorCommissionPercent);
        Assert.Equal(1, workspace.InactiveBaseline.PlatformPercent);
        Assert.Empty(await db.FinancialConfigurationVersions.ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync());
        Assert.Empty(await new WorkspaceQueries(db, null!, Clock)
            .CreatorsAsync(new Actor(admin, ActorRole.PlatformAdmin), default));
        Assert.Empty(await new WorkspaceQueries(db, null!, Clock)
            .OperationsCreatorsAsync(new Actor(Guid.NewGuid(), ActorRole.OperationsAdmin), default));
        var payouts = await new WorkspaceQueries(db, null!, Clock)
            .PayoutsAsync(new Actor(admin, ActorRole.PlatformAdmin), default);
        Assert.Empty(payouts.Creators);
        Assert.Empty(payouts.Customers);
        Assert.Empty(payouts.History);
    }

    [Fact]
    public async Task Initial_settings_require_an_active_platform_admin()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var queries = new WorkspaceQueries(db, null!, Clock);
        var commands = new WorkspaceCommands(db, null!, Clock);
        var input = new Weymela.Application.Web.FinancialSettingsInput(new(1, 1, 1, 0, null), new(1, 1, 1, 0, null),
            5, 4, 1, 1, 1, null);
        foreach (var role in new[] { ActorRole.OperationsAdmin, ActorRole.Business, ActorRole.Creator, ActorRole.Customer, ActorRole.Cashier })
        {
            var actor = new Actor(Guid.NewGuid(), role);
            Assert.Equal(FailureKind.Forbidden, (await Assert.ThrowsAsync<ApplicationFailure>(
                () => queries.SettingsAsync(actor, default))).Kind);
            Assert.Equal(FailureKind.Forbidden, (await Assert.ThrowsAsync<ApplicationFailure>(
                () => commands.SettingsAsync(actor, input, 0, "denied-" + role, default))).Kind);
        }
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => commands.SettingsAsync(
            new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin), input, 0, "not-bootstrapped", default));
        Assert.Equal(FailureKind.Forbidden, failure.Kind);
        Assert.Empty(await db.FinancialConfigurations.ToListAsync());
        Assert.Empty(await db.FinancialConfigurationVersions.ToListAsync());
        Assert.Empty(await db.AuditEvents.ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
    }

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
    public async Task Readiness_is_independent_of_legal_publication_but_requires_admin_and_paused_products()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var admin = await SeedAdmin(db);
        await Bootstrap(db).ProvisionInactiveAsync(Request(admin));
        var options = new RuntimeOptions { EnvironmentName = "Production", FirebaseProjectId = "weymela-production" };
        Assert.True((await new OperationalHealth(db, options, Clock).ReadinessAsync(default)).Ready);
        var legal = await new AccountLegalOnboardingService(db, Clock).StatusAsync(admin, default);
        Assert.False(legal.Available);
        Assert.False(legal.Current);
        await Assert.ThrowsAsync<ApplicationFailure>(() => new AccountLegalOnboardingService(db, Clock)
            .AcceptCurrentAsync(admin, new(new(Guid.NewGuid(), "unpublished", true),
                new(Guid.NewGuid(), "unpublished", true)), null, null, default));
        Assert.Empty(await db.LegalAcceptances.ToListAsync());
        Assert.True((await new OperationalHealth(db, new RuntimeOptions
            { FirebaseProjectId = "weymela-production", FinancialWritesEnabled = true }, Clock).ReadinessAsync(default)).Ready);
        Assert.True((await new OperationalHealth(db, new RuntimeOptions
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
