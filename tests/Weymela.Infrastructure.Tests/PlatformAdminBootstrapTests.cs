using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class PlatformAdminBootstrapTests(PostgresFixture fixture)
{
    private static readonly DateTime BindingValidAfter = new(2026, 9, 12, 20, 0, 0, DateTimeKind.Utc);

    private static PlatformAdminBootstrapRequest Request(Guid user, Guid operatorUser, string uid = "firebase-owner-uid") =>
        new("weymela-pilot", uid, user, BindingValidAfter, operatorUser,
            "owner-approved-console-bootstrap", Guid.NewGuid(), "bootstrap-once-001");

    [Fact]
    public async Task Existing_correct_binding_is_promoted_without_duplicate_binding()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var op = Guid.NewGuid();
        var bindingId = await SeedBindingAsync(db, user);
        var result = await new PlatformAdminBootstrapper(db).ProvisionAsync(Request(user, op));

        Assert.False(result.Replayed);
        Assert.Equal(bindingId, result.BindingId);
        Assert.Equal(user, (await db.IdentityBindings.SingleAsync()).UserId);
        Assert.Equal(bindingId, (await db.IdentityBindings.SingleAsync()).Id);
        var permission = await db.CommercePermissions.SingleAsync();
        Assert.Equal(ActorRole.PlatformAdmin, permission.Role);
        Assert.Equal(user, permission.SubjectId);
        Assert.Equal("PlatformAdminBootstrap", (await db.IdempotencyRecords.SingleAsync()).OperationType);
        Assert.Equal("PlatformAdminBootstrapProvisioned", (await db.AuditEvents.SingleAsync()).EventType);
        Assert.Empty(await db.PublicWorkspaceProfiles.ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
    }

    [Fact]
    public async Task Identical_replay_returns_existing_binding_without_duplicate_records()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var op = Guid.NewGuid(); var request = Request(user, op);
        await SeedBindingAsync(db, user);
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
        await SeedBindingAsync(db, user);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformAdminBootstrapper(db)
            .ProvisionAsync(Request(user, op, "different-firebase-uid")));
        var otherUserRequest = Request(Guid.NewGuid(), op) with { IdempotencyKey = "bootstrap-once-002" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformAdminBootstrapper(db).ProvisionAsync(otherUserRequest));
        Assert.Equal(1, await db.IdentityBindings.CountAsync());
        Assert.Empty(await db.CommercePermissions.ToListAsync());
        Assert.Empty(await db.AuditEvents.ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
    }

    [Fact]
    public async Task Missing_binding_is_rejected_without_creating_one()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformAdminBootstrapper(db)
            .ProvisionAsync(Request(Guid.NewGuid(), Guid.NewGuid())));

        Assert.Empty(await db.IdentityBindings.ToListAsync());
        Assert.Empty(await db.CommercePermissions.ToListAsync());
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public void Ambiguous_binding_selection_is_rejected()
    {
        var user = Guid.NewGuid(); var request = Request(user, Guid.NewGuid());
        var binding = Binding(user, request.FirebaseUid);
        var duplicate = Binding(user, request.FirebaseUid);

        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapper.RequireExistingBinding(
            new[] { binding, duplicate }, new[] { binding }, request));
    }

    [Fact]
    public async Task Second_bootstrap_is_rejected_after_first_admin_exists()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var op = Guid.NewGuid();
        await SeedBindingAsync(db, user);
        await new PlatformAdminBootstrapper(db).ProvisionAsync(Request(user, op));

        var second = Request(user, op) with { IdempotencyKey = "bootstrap-once-002" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformAdminBootstrapper(db).ProvisionAsync(second));
        Assert.Equal(1, await db.CommercePermissions.CountAsync(x => x.Role == ActorRole.PlatformAdmin && x.IsActive));
        Assert.Equal(1, await db.IdentityBindings.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public void Invalid_binding_shape_is_rejected()
    {
        var user = Guid.NewGuid(); var request = Request(user, Guid.NewGuid());
        var invalid = new IdentityBinding
        {
            Provider = "Firebase", ProjectId = "weymela-pilot", ExternalSubject = request.FirebaseUid,
            UserId = user, IsActive = false, ValidAfterUtc = BindingValidAfter, Version = 1
        };

        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapper.RequireExistingBinding(
            new[] { invalid }, new[] { invalid }, request));
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

    private static async Task<Guid> SeedBindingAsync(WeymelaDbContext db, Guid user, string uid = "firebase-owner-uid")
    {
        var binding = Binding(user, uid);
        db.IdentityBindings.Add(binding);
        await db.SaveChangesAsync();
        return binding.Id;
    }

    private static IdentityBinding Binding(Guid user, string uid) => new()
    {
        Provider = "Firebase", ProjectId = "weymela-pilot", ExternalSubject = uid,
        UserId = user, IsActive = true, ValidAfterUtc = BindingValidAfter, Version = 1
    };
}
