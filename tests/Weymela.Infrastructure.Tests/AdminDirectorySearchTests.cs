using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Identity;
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

    private sealed class NoDelivery : IEmailCodeDelivery
    {
        public bool Enabled => false;
        public Task SendAsync(string destination, string code, EmailCodePurpose purpose, CancellationToken ct) => throw new InvalidOperationException("Read-only directory test must not send email.");
    }
}
