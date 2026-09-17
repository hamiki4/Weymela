using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class ProductIntegrationServiceTests(PostgresFixture fixture)
{
    private static readonly DateTime Start = new(2026, 9, 17, 2, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Configuration_requires_fixed_query_free_begin_and_callback_endpoints_on_one_origin()
    {
        var values = Configuration();
        var options = ProductIntegrationOptions.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(values).Build(), new RuntimeOptions
            {
                EnvironmentName = "Pilot",
                Development = false
            });
        Assert.True(options.Enabled);

        foreach (var invalid in new[]
        {
            "https://api-product-pilot.test/api/v1/integration/v3/callback?next=evil",
            "https://api-product-pilot.test/api/v1/integration/v3/other",
            "https://other-product-pilot.test/api/v1/integration/v3/callback"
        })
        {
            values["V3:ProductIntegration:CallbackUrl"] = invalid;
            Assert.Throws<InvalidOperationException>(() => ProductIntegrationOptions.Load(
                new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new RuntimeOptions
                {
                    EnvironmentName = "Pilot",
                    Development = false
                }));
        }
    }

    [Fact]
    public async Task Handoff_is_opaque_short_lived_single_use_and_exactly_one_concurrent_redeem_succeeds()
    {
        var database = await fixture.CreateAsync();
        var clock = new ManualClock(Start);
        var seeded = await SeedIdentityAsync(database, clock, acceptLegal: true);
        ProductHandoffIssueResult issued;
        await using (var db = database.Open())
        {
            issued = await Service(db, clock).IssueAsync(new(seeded.UserId, seeded.BindingId, 1, Start,
                ActorRole.Customer, ProductHandoffPurposes.ProfileOnboarding, "v2-pilot-callback",
                null, null, Guid.NewGuid()), new string('s', 43), default);
            var persisted = await db.ProductHandoffTransactions.SingleAsync();
            Assert.DoesNotContain(issued.Code, persisted.CodeHash, StringComparison.Ordinal);
            Assert.Equal(64, persisted.CodeHash.Length);
            Assert.Equal(Start.AddSeconds(45), issued.ExpiresAtUtc);
        }

        async Task<bool> Redeem()
        {
            try
            {
                await using var db = database.Open();
                var assertion = await Service(db, clock).RedeemAsync(issued.Code, "v2-pilot-callback", default);
                Assert.Equal(seeded.UserId, assertion.UserId);
                Assert.Equal("V3_DEVICE_UNLOCKED", assertion.DeviceAssurance);
                Assert.Equal(ProductHandoffPurposes.ProfileOnboarding, assertion.Purpose);
                Assert.Equal("owner@example.test", assertion.AccountEmail);
                Assert.Equal("+251911111111", assertion.AccountPhone);
                return true;
            }
            catch (ApplicationFailure) { return false; }
        }

        var outcomes = await Task.WhenAll(Redeem(), Redeem());
        Assert.Single(outcomes, x => x);
        await using var verify = database.Open();
        Assert.NotNull((await verify.ProductHandoffTransactions.SingleAsync()).ConsumedAtUtc);
        Assert.Contains(await verify.AuditEvents.ToListAsync(), x => x.EventType == "ProductHandoffRedeemed");
        Assert.Contains(await verify.AuditEvents.ToListAsync(), x => x.EventType == "ProductHandoffReplayRejected");
    }

    [Fact]
    public async Task Expired_wrong_callback_and_stale_binding_handoffs_are_rejected_without_authority_escalation()
    {
        var database = await fixture.CreateAsync();
        var clock = new ManualClock(Start);
        var seeded = await SeedIdentityAsync(database, clock, acceptLegal: true);
        ProductHandoffIssueResult issued;
        await using (var db = database.Open())
            issued = await Service(db, clock).IssueAsync(new(seeded.UserId, seeded.BindingId, 1, Start,
                ActorRole.Creator, ProductHandoffPurposes.ProfileOnboarding, "v2-pilot-callback",
                null, null, Guid.NewGuid()), new string('a', 43), default);

        await using (var db = database.Open())
            await Assert.ThrowsAsync<ApplicationFailure>(() => Service(db, clock)
                .RedeemAsync(issued.Code, "wrong-callback", default));
        clock.Set(Start.AddSeconds(46));
        await using (var db = database.Open())
            await Assert.ThrowsAsync<ApplicationFailure>(() => Service(db, clock)
                .RedeemAsync(issued.Code, "v2-pilot-callback", default));
        await using (var db = database.Open())
        {
            var authority = await Service(db, clock).AuthorityAsync(new(seeded.UserId, seeded.BindingId, 2,
                "Creator", null, null, ProductHandoffPurposes.ProfileOnboarding), default);
            Assert.False(authority.Active);
        }
    }

    [Fact]
    public async Task Profile_synchronization_is_idempotent_and_pending_roles_do_not_gain_active_authority()
    {
        var database = await fixture.CreateAsync();
        var clock = new ManualClock(Start);
        var seeded = await SeedIdentityAsync(database, clock, acceptLegal: true);
        var customer = Guid.NewGuid();
        var creator = Guid.NewGuid();
        await using var db = database.Open();
        var service = Service(db, clock);

        var first = await service.SynchronizeProfileAsync(new(seeded.UserId, seeded.BindingId, 1,
            "Customer", customer, null, "Mimi", "ACTIVE", "v2-customer-once"), default);
        var replay = await service.SynchronizeProfileAsync(new(seeded.UserId, seeded.BindingId, 1,
            "Customer", customer, null, "Mimi", "ACTIVE", "v2-customer-once"), default);
        Assert.Equal(first.V3ProfileSubjectId, replay.V3ProfileSubjectId);
        Assert.Single(await db.CustomerProfiles.Where(x => x.UserId == seeded.UserId).ToListAsync());
        Assert.Single(await db.CommercePermissions.Where(x => x.UserId == seeded.UserId
            && x.Role == ActorRole.Customer).ToListAsync());

        await service.SynchronizeProfileAsync(new(seeded.UserId, seeded.BindingId, 1,
            "Creator", creator, null, "Mimi Creates", "PENDING", "v2-creator-once"), default);
        Assert.False(await db.CommercePermissions.AnyAsync(x => x.UserId == seeded.UserId
            && x.Role == ActorRole.Creator && x.IsActive));
        Assert.Equal(RoleEnrollmentStatus.Pending, (await db.RoleEnrollments.SingleAsync(x =>
            x.UserId == seeded.UserId && x.RequestedRole == ActorRole.Creator)).Status);

        await service.SynchronizeProfileAsync(new(seeded.UserId, seeded.BindingId, 1,
            "Creator", creator, null, "Mimi Creates", "ACTIVE", "v2-creator-once"), default);
        Assert.True(await db.CommercePermissions.AnyAsync(x => x.UserId == seeded.UserId
            && x.Role == ActorRole.Creator && x.SubjectId == creator && x.IsActive));
    }

    [Fact]
    public async Task Active_business_does_not_block_a_second_business_onboarding_handoff()
    {
        var database = await fixture.CreateAsync();
        var clock = new ManualClock(Start);
        var seeded = await SeedIdentityAsync(database, clock, acceptLegal: true);
        var firstBusiness = Guid.NewGuid();
        ProductHandoffIssueResult issued;
        await using (var db = database.Open())
        {
            var service = Service(db, clock);
            await service.SynchronizeProfileAsync(new(seeded.UserId, seeded.BindingId, 1,
                "Business", firstBusiness, firstBusiness, "First Business", "ACTIVE",
                "v2-business-first"), default);
            issued = await service.IssueAsync(new(seeded.UserId, seeded.BindingId, 1, Start,
                ActorRole.Business, ProductHandoffPurposes.ProfileOnboarding, "v2-pilot-callback",
                null, null, Guid.NewGuid()), new string('b', 43), default);
        }

        await using (var db = database.Open())
        {
            var assertion = await Service(db, clock).RedeemAsync(issued.Code, "v2-pilot-callback", default);
            Assert.Equal(ActorRole.Business.ToString(), assertion.Role);
            Assert.Equal(ProductHandoffPurposes.ProfileOnboarding, assertion.Purpose);
        }

        var secondBusiness = Guid.NewGuid();
        await using (var db = database.Open())
            await Service(db, clock).SynchronizeProfileAsync(new(seeded.UserId, seeded.BindingId, 1,
                "Business", secondBusiness, secondBusiness, "Second Business", "ACTIVE",
                "v2-business-second"), default);
        await using (var db = database.Open())
            Assert.Equal(2, await db.CommercePermissions.CountAsync(x => x.UserId == seeded.UserId
                && x.Role == ActorRole.Business && x.IsActive));
    }

    [Fact]
    public async Task Platform_admin_handoff_requires_existing_V3_authority_bypasses_public_legal_and_cannot_be_used_for_onboarding()
    {
        var database = await fixture.CreateAsync();
        var clock = new ManualClock(Start);
        var seeded = await SeedIdentityAsync(database, clock, acceptLegal: false);
        ProductHandoffIssueResult issued;
        await using (var db = database.Open())
        {
            var service = Service(db, clock);
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.IssueAsync(new(seeded.UserId,
                seeded.BindingId, 1, Start, ActorRole.PlatformAdmin,
                ProductHandoffPurposes.ExistingWorkspace, "v2-pilot-callback",
                seeded.UserId, null, Guid.NewGuid()), new string('a', 43), default));
            db.CommercePermissions.Add(new(seeded.UserId, ActorRole.PlatformAdmin,
                seeded.UserId, null, true, false));
            await db.SaveChangesAsync();
            issued = await service.IssueAsync(new(seeded.UserId, seeded.BindingId, 1, Start,
                ActorRole.PlatformAdmin, ProductHandoffPurposes.ExistingWorkspace,
                "v2-pilot-callback", seeded.UserId, null, Guid.NewGuid()),
                new string('a', 43), default);
            Assert.Empty(await db.LegalAcceptances.ToListAsync());
        }

        await using var redeem = database.Open();
        var assertion = await Service(redeem, clock).RedeemAsync(
            issued.Code, "v2-pilot-callback", default);
        Assert.Equal(ActorRole.PlatformAdmin.ToString(), assertion.Role);
        Assert.Equal(seeded.UserId, assertion.ProfileSubjectId);
        Assert.Equal(ProductHandoffPurposes.ExistingWorkspace, assertion.Purpose);
    }

    [Fact]
    public async Task Handoff_rejects_malformed_unknown_cross_environment_and_unauthorized_profile_requests()
    {
        var database = await fixture.CreateAsync();
        var clock = new ManualClock(Start);
        var seeded = await SeedIdentityAsync(database, clock, acceptLegal: true);
        ProductHandoffIssueResult issued;
        await using (var db = database.Open())
        {
            var service = Service(db, clock);
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.RedeemAsync("short", "v2-pilot-callback", default));
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.RedeemAsync(new string('u', 48), "v2-pilot-callback", default));
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.IssueAsync(new(seeded.UserId,
                seeded.BindingId, 1, Start, ActorRole.Customer, ProductHandoffPurposes.ProfileOnboarding,
                "v2-pilot-callback", Guid.NewGuid(), null, Guid.NewGuid()), new string('s', 43), default));
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.IssueAsync(new(seeded.UserId,
                seeded.BindingId, 1, Start, ActorRole.PlatformAdmin, ProductHandoffPurposes.ProfileOnboarding,
                "v2-pilot-callback", null, null, Guid.NewGuid()), new string('s', 43), default));
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.IssueAsync(new(seeded.UserId,
                seeded.BindingId, 1, Start, ActorRole.Customer, "WRONG_PURPOSE",
                "v2-pilot-callback", null, null, Guid.NewGuid()), new string('s', 43), default));
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.IssueAsync(new(seeded.UserId,
                seeded.BindingId, 1, Start, ActorRole.Customer, ProductHandoffPurposes.ExistingWorkspace,
                "v2-pilot-callback", Guid.NewGuid(), null, Guid.NewGuid()), new string('s', 43), default));
            issued = await service.IssueAsync(new(seeded.UserId, seeded.BindingId, 1, Start,
                ActorRole.Business, ProductHandoffPurposes.ProfileOnboarding, "v2-pilot-callback",
                null, null, Guid.NewGuid()), new string('s', 43), default);
        }
        await using (var db = database.Open())
            await Assert.ThrowsAsync<ApplicationFailure>(() => Service(db, clock,
                Options(environment: "Other")).RedeemAsync(issued.Code, "v2-pilot-callback", default));
        await using (var db = database.Open())
            await Assert.ThrowsAsync<ApplicationFailure>(() => Service(db, clock,
                Options(audience: "another-product")).RedeemAsync(issued.Code, "v2-pilot-callback", default));
        await using (var db = database.Open())
        {
            var binding = await db.IdentityBindings.SingleAsync(x => x.Id == seeded.BindingId);
            binding.IsActive = false;
            await db.SaveChangesAsync();
        }
        await using (var db = database.Open())
            await Assert.ThrowsAsync<ApplicationFailure>(() => Service(db, clock)
                .RedeemAsync(issued.Code, "v2-pilot-callback", default));
        await using (var db = database.Open())
        {
            var row = await db.ProductHandoffTransactions.SingleAsync();
            Assert.Null(row.ConsumedAtUtc);
            var active = await Service(db, clock).AuthorityAsync(new(seeded.UserId, seeded.BindingId, 1,
                "Business", null, null, ProductHandoffPurposes.ExistingWorkspace), default);
            Assert.False(active.Active);
            var events = await db.AuditEvents.Select(x => x.EventType).ToListAsync();
            Assert.Contains("ProductHandoffProfileRejected", events);
            Assert.Contains("ProductHandoffEnvironmentRejected", events);
            Assert.Contains("ProductHandoffAudienceRejected", events);
            Assert.Contains("ProductHandoffAuthorityRejected", events);
        }
    }

    private static ProductIntegrationService Service(WeymelaDbContext db, TimeProvider clock,
        ProductIntegrationOptions? options = null) => new(db, options ?? Options(), clock);

    private static ProductIntegrationOptions Options(string environment = "Test",
        string audience = "creatorpay-v2-integration-pilot") => new()
    {
        Enabled = true,
        Issuer = "https://v3-pilot.weymela.test",
        Audience = audience,
        Environment = environment,
        CallbackId = "v2-pilot-callback",
        CallbackUrl = "https://product-pilot.weymela.test/api/v1/integration/v3/callback",
        BeginUrl = "https://product-pilot.weymela.test/api/v1/integration/v3/begin",
        ProductWebUrl = "https://product-pilot.weymela.test",
        ClientId = "test-integration-client",
        ClientSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        CodeLifetime = TimeSpan.FromSeconds(45)
    };

    private static Dictionary<string, string?> Configuration() => new()
    {
        ["V3:ProductIntegration:Enabled"] = "true",
        ["V3:ProductIntegration:Issuer"] = "weymela-v3-pilot",
        ["V3:ProductIntegration:Audience"] = "creatorpay-v2-pilot",
        ["V3:ProductIntegration:Environment"] = "Pilot",
        ["V3:ProductIntegration:CallbackId"] = "v2-pilot-callback",
        ["V3:ProductIntegration:CallbackUrl"] =
            "https://api-product-pilot.test/api/v1/integration/v3/callback",
        ["V3:ProductIntegration:BeginUrl"] =
            "https://api-product-pilot.test/api/v1/integration/v3/begin",
        ["V3:ProductIntegration:ProductWebUrl"] = "https://product-pilot.test",
        ["V3:ProductIntegration:ClientId"] = "phase-i1-test-client",
        ["V3:ProductIntegration:ClientSecret"] = new string('s', 48)
    };

    private static async Task<(Guid UserId, Guid BindingId)> SeedIdentityAsync(TestDatabase database,
        TimeProvider clock, bool acceptLegal)
    {
        await using var db = database.Open();
        var user = Guid.NewGuid();
        var binding = new IdentityBinding
        {
            Provider = "Firebase", ProjectId = "phase-i1-test", ExternalSubject = Guid.NewGuid().ToString("N"),
            UserId = user, IsActive = true, ValidAfterUtc = Start.AddMinutes(-1), Version = 1
        };
        db.IdentityBindings.Add(binding);
        db.AuthIdentifiers.AddRange(
            new AuthIdentifierRecord
            {
                UserId = user, Kind = "Email",
                IdentifierHash = EmailAuthService.HashIdentifier("owner@example.test"),
                DeliveryAddress = "owner@example.test", IsVerified = true, CreatedAtUtc = Start
            },
            new AuthIdentifierRecord
            {
                UserId = user, Kind = "Phone",
                IdentifierHash = EmailAuthService.HashIdentifier("+251911111111"),
                DeliveryAddress = "+251911111111", IsVerified = false, CreatedAtUtc = Start
            });
        if (acceptLegal)
        {
            var terms = new LegalDocumentVersion(Guid.NewGuid(), LegalDocumentType.TermsOfService,
                "phase-i1", "terms-phase-i1", Start.AddMinutes(-1));
            var privacy = new LegalDocumentVersion(Guid.NewGuid(), LegalDocumentType.PrivacyPolicy,
                "phase-i1", "privacy-phase-i1", Start.AddMinutes(-1));
            db.LegalDocumentVersions.AddRange(terms, privacy);
            db.LegalAcceptances.AddRange(
                new LegalAcceptance(user, LegalRole.Account, terms.Id, Start, null, null),
                new LegalAcceptance(user, LegalRole.Account, privacy.Id, Start, null, null));
        }
        await db.SaveChangesAsync();
        return (user, binding.Id);
    }

    private sealed class ManualClock(DateTime initial) : TimeProvider
    {
        private DateTime current = initial;
        public override DateTimeOffset GetUtcNow() => new(current);
        public void Set(DateTime value) => current = value;
    }
}
