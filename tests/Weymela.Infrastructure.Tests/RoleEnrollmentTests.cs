using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class RoleEnrollmentTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Customer_activation_is_immediate_idempotent_and_zero_balance()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        var service = new RoleEnrollmentService(db, TimeProvider.System);

        var first = await service.SubmitAsync(new Actor(user, ActorRole.Customer),
            new RoleEnrollmentRequest(ActorRole.Customer, "Hana", "CU-1", null, null, null), "customer-1", default);
        var replay = await service.SubmitAsync(new Actor(user, ActorRole.Customer),
            new RoleEnrollmentRequest(ActorRole.Customer, "Hana", "CU-1", null, null, null), "customer-1", default);

        Assert.Equal(RoleEnrollmentStatus.Approved, first.Status);
        Assert.Equal(first.Id, replay.Id);
        Assert.Single(await db.RoleEnrollments.Where(x => x.UserId == user && x.RequestedRole == ActorRole.Customer).ToListAsync());
        Assert.Empty(await service.PendingAsync(default));
        var permission = await db.CommercePermissions.SingleAsync(x => x.UserId == user && x.Role == ActorRole.Customer);
        Assert.True(permission.IsActive);
        Assert.True(await db.PublicWorkspaceProfiles.AnyAsync(x => x.SubjectId == permission.SubjectId && x.Role == ActorRole.Customer));
        var cashback = await db.CustomerCashbackAccounts.SingleAsync(x => x.CustomerId == permission.SubjectId);
        Assert.Equal(0m, cashback.AvailableCashback.Amount);
        Assert.Contains(await db.AuditEvents.Where(x => x.ActorId == user).ToListAsync(), x => x.EventType == "CustomerProfileActivated");
    }

    [Fact]
    public async Task Customer_activation_is_resolved_from_persisted_profile_on_session_refresh()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        db.IdentityBindings.Add(new IdentityBinding
        {
            Provider = "Firebase", ProjectId = "isolated-v3-test", ExternalSubject = "uid", UserId = user,
            IsActive = true, ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1), Version = 1
        });
        await db.SaveChangesAsync();

        var enrollment = new RoleEnrollmentService(db, TimeProvider.System);
        await enrollment.SubmitAsync(new Actor(user, ActorRole.Customer),
            new RoleEnrollmentRequest(ActorRole.Customer, "Hana", "CU-REFRESH", null, null, null), "customer-refresh", default);

        var result = await new TrustedIdentityService(db, new Verifier(), new PersistentWorkspaceDirectory(db)).SignInAsync("token", default);
        Assert.Equal(ActorRole.Customer, result.Actor.Role);
        Assert.NotEqual(Guid.Empty, result.Actor.CustomerId);
        Assert.Single(result.Profiles);
        Assert.Equal("CU-REFRESH", result.PublicId);
    }

    [Fact]
    public async Task Existing_customer_can_request_creator_without_losing_customer_access()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var customer = Guid.NewGuid();
        db.CommercePermissions.Add(new CommercePermission(user, ActorRole.Customer, customer, null, true, false));
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = customer, Role = ActorRole.Customer, DisplayName = "Customer", PublicId = "CU-1" });
        await db.SaveChangesAsync();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        var request = new RoleEnrollmentRequest(ActorRole.Creator, "Bella", "CR-1", "Addis", "Food", "Short bio");
        var result = await service.SubmitAsync(new Actor(user, ActorRole.Customer, CustomerId: customer), request, "enroll-1", default);
        Assert.Equal(RoleEnrollmentStatus.Pending, result.Status);
        Assert.True(await db.CommercePermissions.AnyAsync(x => x.UserId == user && x.Role == ActorRole.Customer && x.IsActive));
    }

    [Fact]
    public async Task Business_approval_creates_new_scoped_business_and_is_idempotent()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var customer = Guid.NewGuid(); var admin = Guid.NewGuid();
        db.CommercePermissions.Add(new CommercePermission(user, ActorRole.Customer, customer, null, true, false));
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = customer, Role = ActorRole.Customer, DisplayName = "Customer", PublicId = "CU-2" });
        await db.SaveChangesAsync();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        var row = await service.SubmitAsync(new Actor(user, ActorRole.Customer, CustomerId: customer), new RoleEnrollmentRequest(ActorRole.Business, "ABC Café", "BUS-2", "Addis", "Restaurant", "Business details"), "enroll-business", default);
        var approved = await service.ReviewAsync(new Actor(admin, ActorRole.PlatformAdmin), row.Id, true, null, row.Version, "review-1", default);
        var replay = await service.ReviewAsync(new Actor(admin, ActorRole.PlatformAdmin), row.Id, true, null, approved.Version, "review-2", default);
        var permission = await db.CommercePermissions.SingleAsync(x => x.UserId == user && x.Role == ActorRole.Business);
        Assert.Equal(RoleEnrollmentStatus.Approved, replay.Status);
        Assert.Equal(permission.SubjectId, permission.BusinessId);
        Assert.Single(await db.CommercePermissions.Where(x => x.UserId == user && x.Role == ActorRole.Business).ToListAsync());
        Assert.True(await db.BusinessWallets.AnyAsync(x => x.BusinessId == permission.BusinessId));
    }

    [Fact]
    public async Task Conflicting_submission_reference_and_tampered_business_are_rejected()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        var first = new RoleEnrollmentRequest(ActorRole.Creator, "Bella", "CR-3", null, null, null);
        await service.SubmitAsync(new Actor(user, ActorRole.Customer, CustomerId: Guid.NewGuid()), first, "same-key", default);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(new Actor(user, ActorRole.Customer), first, "another-key", default));
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(new Actor(user, ActorRole.Customer), first with { DisplayName = "Other" }, "same-key", default));
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(new Actor(user, ActorRole.Customer), first with { ProposedBusinessId = Guid.NewGuid() }, "other-key", default));
    }

    [Fact]
    public async Task Suspended_membership_cannot_be_resurrected_by_a_new_request()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        db.CommercePermissions.Add(new CommercePermission(user, ActorRole.Creator, Guid.NewGuid(), null, false, false));
        await db.SaveChangesAsync();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(new Actor(user, ActorRole.Customer), new RoleEnrollmentRequest(ActorRole.Creator, "Bella", "CR-4", null, null, null), "new-request", default));
    }

    [Fact]
    public async Task Public_submission_cannot_request_cashier_or_admin()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        foreach (var role in new[] { ActorRole.Cashier, ActorRole.PlatformAdmin })
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(new Actor(Guid.NewGuid(), ActorRole.Customer), new RoleEnrollmentRequest(role, "No", "NO", null, null, null), Guid.NewGuid().ToString("N"), default));
    }

    private sealed class Verifier : IIdentityTokenVerifier
    {
        public Task<VerifiedIdentity> VerifyAsync(string _, CancellationToken __)
            => Task.FromResult(new VerifiedIdentity("Firebase", "isolated-v3-test", "uid", DateTime.UtcNow, DateTime.UtcNow.AddHours(1)));
    }
}
