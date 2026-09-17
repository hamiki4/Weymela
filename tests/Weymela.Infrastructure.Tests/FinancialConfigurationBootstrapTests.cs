using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Web;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class FinancialConfigurationBootstrapTests(PostgresFixture fixture)
{
    private static readonly DateTime Now = new(2026, 9, 17, 8, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Creates_only_first_root_version_idempotency_and_audit_with_approved_values()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var admin = Guid.NewGuid();
        await SeedAdminAsync(db, admin);
        var request = Request(admin);

        var result = await Bootstrap(db).ProvisionAsync(request);
        var replay = await Bootstrap(db).ProvisionAsync(request);

        Assert.False(result.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(result.VersionId, replay.VersionId);
        var root = await db.FinancialConfigurations.SingleAsync();
        var version = await db.FinancialConfigurationVersions.SingleAsync();
        Assert.Equal("PlatformPricing", root.Name);
        Assert.Equal(root.Id, version.ConfigurationId);
        Assert.Equal(1, version.Version);
        Assert.Equal(admin, version.ChangedBy);
        Assert.Equal(Now, version.EffectiveFromUtc);
        Assert.Equal(3000, version.ViewOnly.ViewsPerReward);
        Assert.Equal(300, version.ViewOnly.BusinessCharge.Amount);
        Assert.Equal(200, version.ViewOnly.CreatorEarning.Amount);
        Assert.Equal(100, version.ViewOnly.PlatformEarning.Amount);
        Assert.Null(version.ViewOnly.MinimumPromotionBudget);
        Assert.Equal(3000, version.ViewPlusCommission.ViewsPerReward);
        Assert.Equal(150, version.ViewPlusCommission.BusinessCharge.Amount);
        Assert.Equal(100, version.ViewPlusCommission.CreatorEarning.Amount);
        Assert.Equal(50, version.ViewPlusCommission.PlatformEarning.Amount);
        Assert.Null(version.ViewPlusCommission.MinimumPromotionBudget);
        Assert.Equal(3, version.ViewPlusCommission.CreatorCommissionPercent);
        Assert.Equal(4, version.ViewPlusCommission.CustomerCashbackPercent);
        Assert.Equal(3, version.ViewPlusCommission.PlatformPercent);
        Assert.Equal(3000, version.CreatorPayoutThreshold.Amount);
        Assert.Equal(4000, version.CustomerPayoutThreshold.Amount);
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync());
        Assert.Equal("FinancialConfigurationBootstrapProvisioned",
            (await db.AuditEvents.SingleAsync()).EventType);
        Assert.Empty(await db.BusinessWallets.ToListAsync());
        Assert.Empty(await db.Promotions.ToListAsync());
        Assert.Empty(await db.PayoutRecords.ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
        Assert.Empty(await db.FinancialJournalLines.ToListAsync());
    }

    [Fact]
    public async Task Changed_replay_and_existing_configuration_are_rejected_without_overwrite()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var admin = Guid.NewGuid();
        await SeedAdminAsync(db, admin);
        var request = Request(admin);
        var first = await Bootstrap(db).ProvisionAsync(request);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Bootstrap(db).ProvisionAsync(
            request with { Settings = request.Settings with { CreatorThreshold = 3001 } }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bootstrap(db).ProvisionAsync(
            request with { IdempotencyKey = "financial-bootstrap-002" }));
        Assert.Equal(first.VersionId, (await db.FinancialConfigurationVersions.SingleAsync()).Id);
        Assert.Equal(1, await db.FinancialConfigurations.CountAsync());
        Assert.Equal(1, await db.FinancialConfigurationVersions.CountAsync());
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Invalid_split_and_non_admin_are_rejected_without_partial_financial_data()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var admin = Guid.NewGuid();
        await SeedAdminAsync(db, admin);
        var request = Request(admin);
        var invalid = request.Settings with
        {
            ViewOnly = request.Settings.ViewOnly with { BusinessPays = 301 }
        };
        await Assert.ThrowsAsync<ArgumentException>(() => Bootstrap(db).ProvisionAsync(
            request with { Settings = invalid }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bootstrap(db).ProvisionAsync(
            request with { PlatformAdminUserId = Guid.NewGuid(), IdempotencyKey = "other-admin" }));
        Assert.Empty(await db.FinancialConfigurations.ToListAsync());
        Assert.Empty(await db.FinancialConfigurationVersions.ToListAsync());
        Assert.Empty(await db.IdempotencyRecords.ToListAsync());
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Normal_platform_admin_settings_create_version_two_after_bootstrap()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var admin = Guid.NewGuid();
        await SeedAdminAsync(db, admin);
        await Bootstrap(db).ProvisionAsync(Request(admin));
        var updated = Settings(Now.AddMinutes(5)) with { CreatorThreshold = 3500 };

        await new WorkspaceCommands(db, null!, new FixedClock(Now)).SettingsAsync(
            new Actor(admin, ActorRole.PlatformAdmin), updated, 1, "settings-v2", default);

        var versions = await db.FinancialConfigurationVersions.OrderBy(x => x.Version).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.Equal(1, versions[0].Version);
        Assert.Equal(2, versions[1].Version);
        Assert.Equal(3500, versions[1].CreatorPayoutThreshold.Amount);
        Assert.Equal(2, await db.AuditEvents.CountAsync());
    }

    private static FinancialConfigurationBootstrapper Bootstrap(WeymelaDbContext db) =>
        new(db, new FixedClock(Now));

    private static FinancialConfigurationBootstrapRequest Request(Guid admin) => new(admin,
        Settings(Now), "phase-i1-owner-approved", Guid.Parse("79fb0754-d029-43cb-b442-e58082ec10ba"),
        "financial-bootstrap-001");

    private static FinancialSettingsInput Settings(DateTime effective) => new(
        new(3000, 300, 200, 100, null),
        new(3000, 150, 100, 50, null),
        3, 4, 3, 3000, 4000, effective);

    private static async Task SeedAdminAsync(WeymelaDbContext db, Guid admin)
    {
        db.CommercePermissions.Add(new(admin, ActorRole.PlatformAdmin, admin, null, true, false));
        db.IdentityBindings.Add(new IdentityBinding
        {
            Provider = "Firebase", ProjectId = "weymela-pilot", ExternalSubject = "pilot-owner",
            UserId = admin, IsActive = true, ValidAfterUtc = Now.AddDays(-1), Version = 1
        });
        await db.SaveChangesAsync();
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
