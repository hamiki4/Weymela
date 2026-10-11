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

    private static V3BootstrapTarget PilotTarget => TestBootstrapTargets.Select("pilot");

    private static PlatformAdminBootstrapRequest Request(Guid user, Guid operatorUser, string uid = "firebase-owner-uid",
        string project = "weymela-pilot") =>
        new(project, uid, user, BindingValidAfter, operatorUser,
            "owner-approved-console-bootstrap", Guid.NewGuid(), "bootstrap-once-001");

    [Fact]
    public async Task Existing_correct_binding_is_promoted_without_duplicate_binding()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var op = Guid.NewGuid();
        var bindingId = await SeedBindingAsync(db, user);
        var result = await new PlatformAdminBootstrapper(db, PilotTarget).ProvisionAsync(Request(user, op));

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
        var first = await new PlatformAdminBootstrapper(db, PilotTarget).ProvisionAsync(request);
        var replay = await new PlatformAdminBootstrapper(db, PilotTarget).ProvisionAsync(request);

        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(first.BindingId, replay.BindingId);
        Assert.Equal(1, await db.IdentityBindings.CountAsync());
        Assert.Equal(1, await db.CommercePermissions.CountAsync());
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Idempotent_replay_does_not_report_success_after_platform_admin_revocation()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var op = Guid.NewGuid();
        await SeedBindingAsync(db, user);
        var request = Request(user, op);
        var bootstrap = new PlatformAdminBootstrapper(db, PilotTarget);
        var first = await bootstrap.ProvisionAsync(request);

        db.CommercePermissions.Remove(await db.CommercePermissions.SingleAsync(
            x => x.UserId == user && x.Role == ActorRole.PlatformAdmin));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrap.ProvisionAsync(request));
        Assert.Equal(first.BindingId, (await db.IdentityBindings.SingleAsync()).Id);
        Assert.Empty(await db.CommercePermissions.ToListAsync());
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformAdminBootstrapper(db, PilotTarget)
            .ProvisionAsync(Request(user, op, "different-firebase-uid")));
        var otherUserRequest = Request(Guid.NewGuid(), op) with { IdempotencyKey = "bootstrap-once-002" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformAdminBootstrapper(db, PilotTarget).ProvisionAsync(otherUserRequest));
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformAdminBootstrapper(db, PilotTarget)
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
        await new PlatformAdminBootstrapper(db, PilotTarget).ProvisionAsync(Request(user, op));

        var second = Request(user, op) with { IdempotencyKey = "bootstrap-once-002" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformAdminBootstrapper(db, PilotTarget).ProvisionAsync(second));
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

    [Fact]
    public async Task Production_test_target_accepts_only_its_matching_firebase_project()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var op = Guid.NewGuid();
        var request = Request(user, op, "synthetic-production-test-uid", "weymela-production");
        await SeedBindingAsync(db, user, request.FirebaseUid, request.FirebaseProjectId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PlatformAdminBootstrapper(db, PilotTarget).ProvisionAsync(request));
        Assert.Empty(await db.CommercePermissions.ToListAsync());

        var result = await new PlatformAdminBootstrapper(db,
            TestBootstrapTargets.Select("production-test")).ProvisionAsync(request);

        Assert.Equal(user, result.UserId);
        Assert.Equal("weymela-production", (await db.IdentityBindings.SingleAsync()).ProjectId);
        Assert.Equal(ActorRole.PlatformAdmin, (await db.CommercePermissions.SingleAsync()).Role);
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Production_bootstrap_requires_owner_authorization_and_live_firebase_uid_verification()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var op = Guid.NewGuid();
        var request = Request(user, op, "verified-production-owner", "weymela-production") with
        {
            ProductionAuthorizationReference = "owner-approval-record-134"
        };
        await SeedBindingAsync(db, user, request.FirebaseUid, request.FirebaseProjectId);
        var target = TestBootstrapTargets.Select("production");
        var verifier = new FakeProductionUidVerifier(verified: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PlatformAdminBootstrapper(db, target, verifier).ProvisionAsync(
                request with { ProductionAuthorizationReference = null }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PlatformAdminBootstrapper(db, target).ProvisionAsync(request));
        Assert.Empty(verifier.Verified);

        var rejectedVerifier = new FakeProductionUidVerifier(verified: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PlatformAdminBootstrapper(db, target, rejectedVerifier).ProvisionAsync(request));
        Assert.Equal(new[] { ("weymela-production", "verified-production-owner") }, rejectedVerifier.Verified);
        Assert.Empty(await db.CommercePermissions.ToListAsync());
        Assert.Empty(await db.IdempotencyRecords.ToListAsync());
        Assert.Empty(await db.AuditEvents.ToListAsync());

        var first = await new PlatformAdminBootstrapper(db, target, verifier).ProvisionAsync(request);
        var replay = await new PlatformAdminBootstrapper(db, target, verifier).ProvisionAsync(request);
        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(first.BindingId, replay.BindingId);
        Assert.Equal(2, verifier.Verified.Count);
        Assert.Equal(1, await db.IdentityBindings.CountAsync());
        Assert.Equal(1, await db.CommercePermissions.CountAsync(x => x.Role == ActorRole.PlatformAdmin && x.IsActive));
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync());
        var audit = await db.AuditEvents.SingleAsync();
        Assert.Equal("PlatformAdminBootstrapProvisioned", audit.EventType);
        Assert.Contains("target=production", audit.Detail);
        Assert.Contains("authorization=owner-approval-record-134", audit.Detail);
        Assert.Empty(await db.LegalDocumentVersions.ToListAsync());
        Assert.Empty(await db.LegalAcceptances.ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
    }

    private static async Task<Guid> SeedBindingAsync(WeymelaDbContext db, Guid user, string uid = "firebase-owner-uid",
        string project = "weymela-pilot")
    {
        var binding = Binding(user, uid, project);
        db.IdentityBindings.Add(binding);
        await db.SaveChangesAsync();
        return binding.Id;
    }

    private static IdentityBinding Binding(Guid user, string uid, string project = "weymela-pilot") => new()
    {
        Provider = "Firebase", ProjectId = project, ExternalSubject = uid,
        UserId = user, IsActive = true, ValidAfterUtc = BindingValidAfter, Version = 1
    };

    private sealed class FakeProductionUidVerifier(bool verified) : IProductionFirebaseUidVerifier
    {
        public List<(string Project, string Uid)> Verified { get; } = [];

        public Task VerifyAsync(string projectId, string uid, CancellationToken cancellationToken)
        {
            Verified.Add((projectId, uid));
            return verified ? Task.CompletedTask : Task.FromException(
                new InvalidOperationException("Test verifier rejected UID."));
        }
    }
}
