using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Web;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class AdminDirectorySearchTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Directory_filters_before_paging_and_returns_server_owned_contacts()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var users = new List<Guid>();
        for (var i = 0; i < 63; i++)
        {
            var user = Guid.NewGuid(); var subject = Guid.NewGuid(); users.Add(user);
            db.CommercePermissions.Add(new(user, ActorRole.Customer, subject, null, true, false));
            db.PublicWorkspaceProfiles.Add(new() { SubjectId = subject, Role = ActorRole.Customer,
                DisplayName = $"Customer {i:D3}", PublicId = $"CU-{i:D3}" });
            db.AuthIdentifiers.Add(new() { UserId = user, Kind = "Email", IdentifierHash = $"email-{i}",
                DeliveryAddress = $"customer-{i}@example.test", IsVerified = true, CreatedAtUtc = DateTime.UtcNow });
            db.AuthIdentifiers.Add(new() { UserId = user, Kind = "Phone", IdentifierHash = $"phone-{i}",
                DeliveryAddress = $"+25190000{i:D4}", CreatedAtUtc = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);
        var service = new PlatformAdminAccountService(db, TimeProvider.System, new NoDelivery());
        var first = await service.ListAsync(admin, new(Role: "Customer", PageSize: 50), default);
        var second = await service.ListAsync(admin, new(Role: "Customer", Page: 2, PageSize: 50), default);
        Assert.Equal(50, first.Count); Assert.Equal(13, second.Count);
        Assert.Empty(first.Select(x => x.Id).Intersect(second.Select(x => x.Id)));
        Assert.Equal(63, first.Concat(second).Select(x => x.Id).Distinct().Count());
        var phone = await service.ListAsync(admin, new(Role: "Customer", Search: "+251900000062"), default);
        var row = Assert.Single(phone);
        Assert.Equal("Customer 062", row.Name);
        Assert.Equal("customer-62@example.test", row.FullEmail);
        Assert.Equal("+251900000062", row.FullPhone);
        Assert.Equal(row.Id, Assert.Single(await service.ListAsync(admin, new(Role: "Customer", Search: "CUSTOMER-62@"), default)).Id);
        Assert.Empty(await service.ListAsync(admin, new(Role: "Creator", Search: row.FullPhone), default));
        var denied = await Assert.ThrowsAsync<ApplicationFailure>(() => service.ListAsync(new(Guid.NewGuid(), ActorRole.Customer), new(Search: row.FullPhone), default));
        Assert.Equal(FailureKind.Forbidden, denied.Kind);
    }

    [Fact]
    public async Task Directory_admin_role_and_lifecycle_filters_run_on_the_server()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        foreach (var role in new[] { ActorRole.PlatformAdmin, ActorRole.OperationsAdmin })
        {
            var user = Guid.NewGuid();
            db.CommercePermissions.Add(new(user, role, user, null, true, false));
            db.AdminGrants.Add(new() { UserId = user, Role = role, DisplayName = role + " person", GrantedAtUtc = DateTime.UtcNow });
            db.AccountLifecycles.Add(new() { UserId = user, Status = role == ActorRole.OperationsAdmin ? AccountLifecycleStatus.Suspended : AccountLifecycleStatus.Active, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();
        var service = new PlatformAdminAccountService(db, TimeProvider.System, new NoDelivery());
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);
        var row = Assert.Single(await service.ListAsync(admin, new(Role: "Admin", AdminRole: "OperationsAdmin", Status: "Suspended"), default));
        Assert.Equal("OperationsAdmin", row.Role); Assert.Equal("Suspended", row.Status);
        Assert.Empty(await service.ListAsync(admin, new(Role: "Admin", AdminRole: "OperationsAdmin", Status: "Active"), default));
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.ListAsync(admin, new(Role: "Admin", PageSize: 10000), default));
    }

    [Fact]
    public async Task Operations_phone_search_is_scoped_and_does_not_include_financial_records()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var owner = Guid.NewGuid(); var customer = Guid.NewGuid(); var business = Guid.NewGuid();
        db.CommercePermissions.AddRange(new(owner, ActorRole.Customer, customer, null, true, false),
            new(owner, ActorRole.Business, business, business, true, true));
        db.PublicWorkspaceProfiles.AddRange(new() { SubjectId = customer, Role = ActorRole.Customer, DisplayName = "Shared identity", PublicId = "CU-ONE" },
            new() { SubjectId = business, Role = ActorRole.Business, DisplayName = "Business One", PublicId = "BU-ONE" });
        db.AuthIdentifiers.Add(new() { UserId = owner, Kind = "Phone", IdentifierHash = "shared-phone", DeliveryAddress = "+251911000099", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var search = new AdminSearchQueries(db, new(Search: "11000099"));
        Assert.Equal(customer, Assert.Single(await search.Permissions(ActorRole.Customer).ToListAsync()).SubjectId);
        Assert.Equal(business, Assert.Single(await search.Permissions(ActorRole.Business).ToListAsync()).SubjectId);
        Assert.Empty(await search.Permissions(ActorRole.Creator).ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
    }

    [Fact]
    public async Task Campaign_search_finds_pending_Creators_and_social_review_groups_unverified_profiles()
    {
        var scenario = await Scenario.Create(fixture, funded: true);
        await using var db = scenario.Database.Open();
        var creator = Guid.NewGuid(); var socialId = Guid.NewGuid();
        var campaign = await db.Promotions.Include(x => x.Platforms).SingleAsync(x => x.Id == scenario.PromotionId);
        campaign.Publish(Scenario.Now, Guid.NewGuid());
        db.PublicWorkspaceProfiles.Add(new() { SubjectId = creator, Role = ActorRole.Creator,
            DisplayName = "Pending search creator", PublicId = "CR-PENDING-SEARCH" });
        db.CreatorSocialProfiles.Add(new() { Id = socialId, CreatorId = creator, Platform = CreatorPlatform.TikTok,
            ProfileUrl = "https://www.tiktok.com/@isolated_search_fixture", VerificationStatus = "SelfReported",
            AudienceVerificationSource = "SelfReported", CreatedAtUtc = Scenario.Now, UpdatedAtUtc = Scenario.Now });
        db.CreatorApplications.Add(new(campaign.Id, creator, socialId, CreatorPlatform.TikTok, "Controlled fixture", null, campaign, Scenario.Now, Guid.NewGuid()));
        await db.SaveChangesAsync();
        var query = new AdminSearchQueries(db, new(Search: "pending search creator"));
        Assert.Equal(campaign.Id, Assert.Single(await query.Campaigns().ToListAsync()).Id);
        var review = new AdminSocialProfileReviewService(db, TimeProvider.System);
        var unverified = await review.ListAsync(new(Guid.NewGuid(), ActorRole.PlatformAdmin), default,
            new(Search: "pending search creator", Status: "Unverified", Type: "TikTok"));
        Assert.Equal(socialId, Assert.Single(unverified).Id);
        Assert.Empty(await review.ListAsync(new(Guid.NewGuid(), ActorRole.PlatformAdmin), default,
            new(Search: "pending search creator", Status: "Verified")));
    }

    [Fact]
    public async Task Payout_history_filters_and_pages_Platform_settlements_without_exposing_them_to_Operations()
    {
        var scenario = await Phase4Scenario.Create(fixture);
        await scenario.Refresh(3000);
        await using var db = scenario.Database.Open();
        await new PayoutService(db, scenario.Clock).SettlePlatformAsync(Phase4Scenario.Admin, new Money(30), "FIXTURE-SETTLEMENT-ONE", "fixture-one");
        await new PayoutService(db, scenario.Clock).SettlePlatformAsync(Phase4Scenario.Admin, new Money(20), "FIXTURE-SETTLEMENT-TWO", "fixture-two");
        var queries = new WorkspaceQueries(db, new TestDirectory(), scenario.Clock, TestPayoutProtector.Instance);
        var first = await queries.PayoutsAsync(Phase4Scenario.Admin, default, new(PageSize: 1));
        var second = await queries.PayoutsAsync(Phase4Scenario.Admin, default, new(Page: 2, PageSize: 1));
        Assert.NotEqual(Assert.Single(first.History).Id, Assert.Single(second.History).Id);
        Assert.Equal(100m, first.PlatformAccrued); Assert.Equal(50m, first.PlatformSettled); Assert.Equal(50m, first.PlatformUnsettled);
        var searched = await queries.PayoutsAsync(Phase4Scenario.Admin, default, new(Search: "SETTLEMENT-TWO"));
        Assert.Equal("FIXTURE-SETTLEMENT-TWO", Assert.Single(searched.History).Reference);
        Assert.Empty((await queries.PayoutsAsync(Phase4Scenario.Admin, default, new(Search: "not-a-recipient"))).History);
        Assert.Empty((await queries.PayoutsAsync(Phase4Scenario.Admin, default, new(Type: "Bank"))).History);
        Assert.Empty((await queries.OperationsPayoutsAsync(new(Guid.NewGuid(), ActorRole.OperationsAdmin), default)).History);
    }

    private sealed class NoDelivery : IEmailCodeDelivery
    {
        public bool Enabled => false;
        public Task SendAsync(string destination, string code, EmailCodePurpose purpose, CancellationToken ct) => throw new InvalidOperationException("Read-only directory test must not send email.");
    }
}
