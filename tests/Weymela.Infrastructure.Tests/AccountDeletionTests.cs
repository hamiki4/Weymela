using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class AccountDeletionTests(PostgresFixture fixture)
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Delete_role_removes_only_selected_workspace_and_preserves_financial_records()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = Guid.NewGuid(); var user = Guid.NewGuid(); var customer = Guid.NewGuid();
        var creator = Guid.NewGuid(); var business = Guid.NewGuid();
        db.CommercePermissions.AddRange(
            new(admin, ActorRole.PlatformAdmin, admin, null, true, false),
            new(user, ActorRole.Customer, customer, null, true, false),
            new(user, ActorRole.Creator, creator, null, true, false),
            new(user, ActorRole.Business, business, business, true, true));
        db.PublicWorkspaceProfiles.AddRange(
            new PublicWorkspaceProfile { SubjectId = creator, Role = ActorRole.Creator, PublicId = "CR-DELETE-TEST", DisplayName = "Creator" },
            new PublicWorkspaceProfile { SubjectId = business, Role = ActorRole.Business, PublicId = "BU-DELETE-TEST", DisplayName = "Business" });
        db.BusinessWallets.Add(new(business));
        await db.SaveChangesAsync();
        var service = new AccountDeletionService(db, new FixedClock(), new FakeIdentityProvider());
        var authority = AuthorityContext.ForAuthenticatedActor(new(admin, ActorRole.PlatformAdmin));
        Assert.Equal(business, await service.DeleteRoleAsync(authority, user, new("Business", business, "Policy violation"), "delete-business", default));
        db.ChangeTracker.Clear();
        Assert.False((await db.CommercePermissions.SingleAsync(x => x.UserId == user && x.Role == ActorRole.Business)).IsActive);
        Assert.All(await db.CommercePermissions.Where(x => x.UserId == user && x.Role != ActorRole.Business).ToListAsync(), x => Assert.True(x.IsActive));
        Assert.True(await db.BusinessWallets.AnyAsync(x => x.BusinessId == business));
        Assert.True(await db.PublicWorkspaceProfiles.AnyAsync(x => x.SubjectId == business && x.PublicId == "BU-DELETE-TEST"));
        Assert.Contains(await db.AccountRoleHistory.ToListAsync(), x => x.TargetUserId == user && x.Action == "Deleted" && x.Reason == "Policy violation");
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.TargetUserId == user && x.ActorId == admin && x.EventType == "AccountRoleDeleted");
        Assert.Equal(business, await service.DeleteRoleAsync(authority, user, new("Business", business, "Policy violation"), "delete-business", default));
        Assert.Single(await db.AccountRoleHistory.Where(x => x.TargetUserId == user && x.Action == "Deleted").ToListAsync());
        Assert.Equal(creator, await service.DeleteRoleAsync(authority, user, new("Creator", creator, "Policy violation"), "delete-creator", default));
        Assert.True((await db.CommercePermissions.SingleAsync(x => x.UserId == user && x.Role == ActorRole.Customer)).IsActive);
    }

    [Fact]
    public async Task Entire_deletion_closes_every_role_then_releases_email_only_after_external_deletion()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = Guid.NewGuid(); var user = Guid.NewGuid(); var customer = Guid.NewGuid(); var business = Guid.NewGuid(); var cashier = Guid.NewGuid();
        var hash = EmailAuthService.HashIdentifier("reuse@example.test");
        db.CommercePermissions.AddRange(new(admin, ActorRole.PlatformAdmin, admin, null, true, false),
            new(user, ActorRole.Customer, customer, null, true, false), new(user, ActorRole.Business, business, business, true, true),
            new(cashier, ActorRole.Cashier, cashier, business, true, true));
        db.BusinessWallets.Add(new(business));
        db.AuthIdentifiers.Add(new() { UserId = user, Kind = "Email", IdentifierHash = hash, DeliveryAddress = "reuse@example.test", IsVerified = true, CreatedAtUtc = Now });
        db.IdentityBindings.Add(new() { UserId = user, ProjectId = "test", ExternalSubject = "firebase-test-user", IsActive = true, Version = 1 });
        var password = "Existing account password 123!";
        var originalPasswordHash = PasswordCredentialHasher.Hash(password);
        db.PasswordCredentials.Add(new() { UserId = user, PasswordHash = originalPasswordHash,
            WorkFactor = PasswordCredentialHasher.Iterations, CreatedAtUtc = Now, ChangedAtUtc = Now, Version = 1 });
        await db.SaveChangesAsync();
        var pendingCreator = await new RoleEnrollmentService(db, new FixedClock()).SubmitAsync(
            new(user, ActorRole.Customer), new(ActorRole.Creator, "Pending creator", null, null, null, null,
                SocialProfiles: [new("TikTok", "https://www.tiktok.com/@pending")]), "pending-creator", default);
        var provider = new FakeIdentityProvider(); var authority = AuthorityContext.ForAuthenticatedActor(new(admin, ActorRole.PlatformAdmin));
        var service = new AccountDeletionService(db, new FixedClock(), provider);
        Assert.Equal("Pending", (await service.DeleteEntireAsync(authority, user, new("Requested deletion", "DELETE"), "delete-entire", default)).Status);
        db.ChangeTracker.Clear();
        Assert.Equal(AccountLifecycleStatus.Closed, (await db.AccountLifecycles.SingleAsync(x => x.UserId == user)).Status);
        Assert.All(await db.CommercePermissions.Where(x => x.UserId == user).ToListAsync(), x => Assert.False(x.IsActive));
        Assert.False((await db.CommercePermissions.SingleAsync(x => x.UserId == cashier)).IsActive);
        Assert.Equal(FailureKind.Forbidden, (await Assert.ThrowsAsync<ApplicationFailure>(() =>
            new RoleEnrollmentService(db, new FixedClock()).ReviewAsync(new(admin, ActorRole.PlatformAdmin),
                pendingCreator.Id, true, null, pendingCreator.Version, "approve-closed", default))).Kind);
        Assert.Equal(hash, (await db.AuthIdentifiers.SingleAsync(x => x.UserId == user)).IdentifierHash);
        var disabledCredential = await db.PasswordCredentials.SingleAsync(x => x.UserId == user);
        Assert.NotEqual(originalPasswordHash, disabledCredential.PasswordHash);
        Assert.False(PasswordCredentialHasher.Verify(password, disabledCredential));
        Assert.Equal(2, disabledCredential.Version);
        Assert.True(await db.BusinessWallets.AnyAsync(x => x.BusinessId == business));
        Assert.Equal("Pending", (await service.DeleteEntireAsync(authority, user, new("Requested deletion", "DELETE"), "delete-entire", default)).Status);
        Assert.Single(await db.OutboxMessages.Where(x => x.EventType == "AccountIdentityDeletion").ToListAsync());
        Assert.Equal(1, await new AccountIdentityDeletionProcessor(db, provider, new FixedClock()).ProcessAsync(default));
        db.ChangeTracker.Clear();
        Assert.Equal(1, provider.Calls);
        Assert.Equal("Completed", (await service.StatusAsync(authority, user, default)).Status);
        var identifier = await db.AuthIdentifiers.SingleAsync(x => x.UserId == user);
        Assert.NotEqual(hash, identifier.IdentifierHash);
        Assert.Null(identifier.DeliveryAddress);
        Assert.False(identifier.IsVerified);
        Assert.False((await db.IdentityBindings.SingleAsync(x => x.UserId == user)).IsActive);
        Assert.True(await db.PasswordCredentials.AnyAsync(x => x.UserId == user));
        Assert.True(await db.BusinessWallets.AnyAsync(x => x.BusinessId == business));
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.ActorId == admin && x.TargetUserId == user && x.EventType == "EntireAccountDeletionCompleted");
        db.AuthIdentifiers.Add(new() { UserId = Guid.NewGuid(), Kind = "Email", IdentifierHash = hash, DeliveryAddress = "reuse@example.test", IsVerified = false, CreatedAtUtc = Now });
        await db.SaveChangesAsync();
        Assert.Equal(0, await new AccountIdentityDeletionProcessor(db, provider, new FixedClock()).ProcessAsync(default));
    }

    [Fact]
    public async Task Provider_failure_keeps_email_reserved_and_non_platform_roles_are_denied()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = Guid.NewGuid(); var target = Guid.NewGuid(); var other = Guid.NewGuid(); var emailHash = EmailAuthService.HashIdentifier("held@example.test");
        db.CommercePermissions.AddRange(new(admin, ActorRole.PlatformAdmin, admin, null, true, false),
            new(target, ActorRole.Customer, target, null, true, false), new(other, ActorRole.OperationsAdmin, other, null, true, false));
        db.AuthIdentifiers.Add(new() { UserId = target, Kind = "Email", IdentifierHash = emailHash, DeliveryAddress = "held@example.test", IsVerified = true, CreatedAtUtc = Now });
        db.IdentityBindings.Add(new() { UserId = target, ProjectId = "test", ExternalSubject = "external", IsActive = true, Version = 1 });
        await db.SaveChangesAsync();
        var provider = new FakeIdentityProvider { Fail = true }; var service = new AccountDeletionService(db, new FixedClock(), provider);
        foreach (var role in new[] { ActorRole.OperationsAdmin, ActorRole.Business, ActorRole.Creator, ActorRole.Customer, ActorRole.Cashier })
        {
            var denied = await Assert.ThrowsAsync<ApplicationFailure>(() => service.DeleteRoleAsync(
                AuthorityContext.ForAuthenticatedActor(new(other, role)), target, new("Customer", target, "Unauthorized"), "deny-" + role, default));
            Assert.Equal(FailureKind.Forbidden, denied.Kind);
        }
        var forged = new AuthorityContext(new RealActor(new(other, ActorRole.OperationsAdmin)),
            new AdministrativeAuthority(ActorRole.PlatformAdmin));
        Assert.Equal(FailureKind.Forbidden, (await Assert.ThrowsAsync<ApplicationFailure>(() => service.DeleteEntireAsync(forged,
            target, new("Unauthorized", "DELETE"), "forged", default))).Kind);
        var authority = AuthorityContext.ForAuthenticatedActor(new(admin, ActorRole.PlatformAdmin));
        await service.DeleteEntireAsync(authority, target, new("Requested", "DELETE"), "first", default);
        Assert.Equal(1, await new AccountIdentityDeletionProcessor(db, provider, new FixedClock()).ProcessAsync(default));
        db.ChangeTracker.Clear();
        Assert.Equal(emailHash, (await db.AuthIdentifiers.SingleAsync(x => x.UserId == target)).IdentifierHash);
        Assert.Equal("Pending", (await service.StatusAsync(authority, target, default)).Status);
        provider.Fail = false;
        // Requeue after a failed delivery without creating a second deletion request.
        var eventRow = await db.OutboxMessages.SingleAsync(x => x.EventType == "AccountIdentityDeletion");
        eventRow.NextAttemptAtUtc = Now.AddSeconds(-1); await db.SaveChangesAsync();
        Assert.Equal(1, await new AccountIdentityDeletionProcessor(db, provider, new FixedClock()).ProcessAsync(default));
        Assert.Equal("Completed", (await service.StatusAsync(authority, target, default)).Status);
    }

    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
    private sealed class FakeIdentityProvider : IAccountIdentityDeletionProvider
    {
        public bool Enabled => true;
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public Task DeleteAsync(string projectId, string externalSubject, CancellationToken ct)
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("provider secret must not be logged");
            Assert.Equal("test", projectId);
            return Task.CompletedTask;
        }
    }
}
