using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class AdminAccountServiceTests(PostgresFixture fixture)
{
    private static readonly DateTime Now = new(2026, 9, 18, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Platform_admin_grants_existing_verified_identity_without_creating_credentials_and_operations_admin_is_denied()
    {
        var state = await Setup(); await using var db = state.Database.Open();
        var service = new AdminAccountService(db, new FixedTime(Now));
        await service.GrantAsync(state.Platform, new AdminGrantInput(state.TargetEmail, "Operations Admin"), "grant-operations", default);
        db.ChangeTracker.Clear();
        var permission = await db.CommercePermissions.SingleAsync(x => x.UserId == state.TargetUser && x.Role == ActorRole.OperationsAdmin);
        Assert.True(permission.IsActive);
        Assert.False(await db.PasswordCredentials.AnyAsync(x => x.UserId == state.TargetUser));
        Assert.False(await db.DeviceSessions.AnyAsync(x => x.UserId == state.TargetUser));
        var account = Assert.Single(await service.GetAsync(state.Platform, default), x => x.UserId == state.TargetUser);
        Assert.Equal("Mimi Kibru", account.Name); Assert.Equal("Operations Admin", account.Role);
        var operations = new Actor(state.TargetUser, ActorRole.OperationsAdmin, state.TargetUser);
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.GrantAsync(operations,
            new AdminGrantInput(state.TargetEmail, "Platform Admin"), "operations-cannot-grant", default));
        Assert.Equal(FailureKind.Forbidden, failure.Kind);
    }

    [Fact]
    public async Task Last_platform_admin_cannot_be_revoked_and_role_changes_remain_audited()
    {
        var state = await Setup(); await using var db = state.Database.Open();
        var service = new AdminAccountService(db, new FixedTime(Now));
        var last = await Assert.ThrowsAsync<ApplicationFailure>(() => service.RevokeAsync(state.Platform, state.Platform.UserId, "revoke-last", default));
        Assert.Equal(FailureKind.Validation, last.Kind);
        await service.GrantAsync(state.Platform, new AdminGrantInput(state.TargetEmail, "Platform Admin"), "grant-replacement", default);
        await service.RevokeAsync(state.Platform, state.Platform.UserId, "revoke-original", default);
        db.ChangeTracker.Clear();
        Assert.False((await db.CommercePermissions.SingleAsync(x => x.UserId == state.Platform.UserId && x.Role == ActorRole.PlatformAdmin)).IsActive);
        Assert.True((await db.CommercePermissions.SingleAsync(x => x.UserId == state.TargetUser && x.Role == ActorRole.PlatformAdmin)).IsActive);
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "AdminGranted");
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "AdminRevoked");
        var replacement = new Actor(state.TargetUser, ActorRole.PlatformAdmin, state.TargetUser);
        Assert.Equal(FailureKind.Validation, (await Assert.ThrowsAsync<ApplicationFailure>(() =>
            service.RevokeAsync(replacement, state.TargetUser, "revoke-new-last", default))).Kind);
    }

    private async Task<State> Setup()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var platform = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin, Guid.NewGuid());
        var target = Guid.NewGuid(); const string email = "mimi@example.test";
        db.CommercePermissions.Add(new(platform.UserId, ActorRole.PlatformAdmin, platform.UserId, null, true, false));
        db.AuthIdentifiers.AddRange(
            new AuthIdentifierRecord { UserId = platform.UserId, Kind = "Email", IdentifierHash = EmailAuthService.HashIdentifier("owner@example.test"), DeliveryAddress = "owner@example.test", IsVerified = true, CreatedAtUtc = Now },
            new AuthIdentifierRecord { UserId = target, Kind = "Email", IdentifierHash = EmailAuthService.HashIdentifier(email), DeliveryAddress = email, IsVerified = true, CreatedAtUtc = Now });
        db.CustomerProfiles.Add(new CustomerProfileRecord { CustomerId = Guid.NewGuid(), UserId = target, PreferredName = "Mimi Kibru", CreatedAtUtc = Now, UpdatedAtUtc = Now });
        await db.SaveChangesAsync();
        return new(database, platform, target, email);
    }

    private sealed record State(TestDatabase Database, Actor Platform, Guid TargetUser, string TargetEmail);
    private sealed class FixedTime(DateTime value) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(value); }
}
