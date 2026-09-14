using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Infrastructure.Identity;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class PlatformAdminBootstrapTests(PostgresFixture fixture)
{
    private static PlatformAdminBootstrapRequest Request(Guid user, Guid operatorUser, string uid = "firebase-owner-uid") =>
        new("weymela-pilot", uid, user, new DateTime(2026, 9, 12, 20, 0, 0, DateTimeKind.Utc), operatorUser,
            "owner-approved-console-bootstrap", Guid.NewGuid(), "bootstrap-once-001");

    [Fact]
    public async Task First_provision_creates_binding_permission_idempotency_and_audit()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var op = Guid.NewGuid();
        var result = await new PlatformAdminBootstrapper(db).ProvisionAsync(Request(user, op));

        Assert.False(result.Replayed);
        Assert.Equal(user, (await db.IdentityBindings.SingleAsync()).UserId);
        var permission = await db.CommercePermissions.SingleAsync();
        Assert.Equal(ActorRole.PlatformAdmin, permission.Role);
        Assert.Equal(user, permission.SubjectId);
        Assert.Equal("PlatformAdminBootstrap", (await db.IdempotencyRecords.SingleAsync()).OperationType);
        Assert.Equal("PlatformAdminBootstrapProvisioned", (await db.AuditEvents.SingleAsync()).EventType);
    }

    [Fact]
    public async Task Identical_replay_returns_existing_binding_without_duplicate_records()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var op = Guid.NewGuid(); var request = Request(user, op);
        var first = await new PlatformAdminBootstrapper(db).ProvisionAsync(request);
        var replay = await new PlatformAdminBootstrapper(db).ProvisionAsync(request);

        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(first.BindingId, replay.BindingId);
        Assert.Equal(1, await db.IdentityBindings.CountAsync());
        Assert.Equal(1, await db.CommercePermissions.CountAsync());
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Conflicting_uid_or_user_mapping_is_rejected_without_partial_write()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var op = Guid.NewGuid();
        await new PlatformAdminBootstrapper(db).ProvisionAsync(Request(user, op));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformAdminBootstrapper(db)
            .ProvisionAsync(Request(user, op, "different-firebase-uid")));
        var otherUserRequest = Request(Guid.NewGuid(), op) with { IdempotencyKey = "bootstrap-once-002" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformAdminBootstrapper(db).ProvisionAsync(otherUserRequest));
        Assert.Equal(1, await db.IdentityBindings.CountAsync());
        Assert.Equal(1, await db.CommercePermissions.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Target_guard_requires_the_expected_database_and_bootstrap_role()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var actual = new NpgsqlConnectionStringBuilder(database.ConnectionString).Database!;
        await PlatformAdminBootstrapTarget.VerifyAsync(db, actual, "v3_test");
        await Assert.ThrowsAsync<InvalidOperationException>(() => PlatformAdminBootstrapTarget.VerifyAsync(db, "weymela_v3_pilot", "v3_test"));
    }
}
