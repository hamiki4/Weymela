using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class DeviceSessionServiceTests(PostgresFixture fixture)
{
    private static readonly DateTime Start = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_and_resolve_persist_only_digest_and_validate_every_identity_boundary()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, Start);
        CreatedDeviceSession created;
        await using (var db = database.Open())
            created = await new DeviceSessionService(db, new ManualClock(Start))
                .CreateAsync(seeded.Identity, seeded.DeviceId);

        Assert.Equal(Start, created.Session.CreatedAtUtc);
        Assert.Equal(Start, created.Session.LastActivityAtUtc);
        Assert.Null(created.Session.LockedAtUtc);
        Assert.Equal(Start.AddHours(1), created.Session.ExpiresAtUtc);
        Assert.Equal(1, created.Session.Generation);
        Assert.Equal(64, created.Credential.Value.Length);
        Assert.DoesNotContain(created.Credential.Value, created.Credential.ToString(), StringComparison.Ordinal);

        await using (var db = database.Open())
        {
            var persisted = await db.DeviceSessions.SingleAsync(x => x.Id == created.Session.Id);
            Assert.Equal(created.Credential.Digest, persisted.SessionIdentifierHash);
            Assert.NotEqual(created.Credential.Value, persisted.SessionIdentifierHash);
            Assert.DoesNotContain(created.Credential.Value, persisted.SessionIdentifierHash, StringComparison.Ordinal);
            Assert.DoesNotContain(typeof(DeviceSessionRecord).GetProperties(), property =>
                property.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase));
        }

        await using (var db = database.Open())
        {
            var service = new DeviceSessionService(db, new ManualClock(Start));
            var resolved = await service.ResolveAsync(seeded.Identity, created.Credential.Value);
            Assert.Equal(DeviceSessionAccessState.Active, resolved.State);
            Assert.Equal(created.Session.Id, resolved.Session!.Id);

            await AssertFullAuthentication(service, seeded.Identity, new string('0', 64));
            await AssertFullAuthentication(service, seeded.Identity with { UserId = Guid.NewGuid() }, created.Credential.Value);
            await AssertFullAuthentication(service, seeded.Identity with { IdentityBindingId = Guid.NewGuid() }, created.Credential.Value);
            await AssertFullAuthentication(service, seeded.Identity with { IdentityVersion = seeded.Identity.IdentityVersion + 1 }, created.Credential.Value);
        }
    }

    [Fact]
    public async Task Creation_and_resolution_fail_closed_for_unusable_devices()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, Start);
        var clock = new ManualClock(Start);

        var expired = await AddDeviceAsync(database, seeded.Identity.UserId, Start.AddDays(-30));
        var revoked = await AddDeviceAsync(database, seeded.Identity.UserId, Start, device => device.RevokedAtUtc = Start);
        var recovery = await AddDeviceAsync(database, seeded.Identity.UserId, Start, device =>
        {
            device.FailedAttempts = DeviceAccessPolicy.RecoveryAttemptThreshold;
            device.RequiresRecovery = true;
        });

        await using (var db = database.Open())
        {
            var service = new DeviceSessionService(db, clock);
            await AssertForbidden(() => service.CreateAsync(seeded.Identity, expired.Id));
            await AssertForbidden(() => service.CreateAsync(seeded.Identity, revoked.Id));
            await AssertForbidden(() => service.CreateAsync(seeded.Identity, recovery.Id));
        }

        CreatedDeviceSession session;
        await using (var db = database.Open())
            session = await new DeviceSessionService(db, clock).CreateAsync(seeded.Identity, seeded.DeviceId);
        await using (var db = database.Open())
        {
            var device = await db.AuthorizedDevices.SingleAsync(x => x.Id == seeded.DeviceId);
            device.RequiresRecovery = true;
            device.FailedAttempts = DeviceAccessPolicy.RecoveryAttemptThreshold;
            device.Version++;
            await db.SaveChangesAsync();
        }
        await using (var db = database.Open())
        {
            var resolved = await new DeviceSessionService(db, clock)
                .ResolveAsync(seeded.Identity, session.Credential.Value);
            Assert.Equal(DeviceSessionAccessState.RecoveryRequired, resolved.State);
            Assert.Equal(session.Session.Id, resolved.Session!.Id);
        }
    }

    [Fact]
    public async Task Idle_lock_boundary_and_mutations_never_extend_absolute_expiry()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, Start);
        var clock = new ManualClock(Start);
        CreatedDeviceSession created;
        await using (var db = database.Open())
            created = await new DeviceSessionService(db, clock).CreateAsync(seeded.Identity, seeded.DeviceId);

        clock.Set(Start.AddMinutes(20).AddTicks(-1));
        await using (var db = database.Open())
            Assert.Equal(DeviceSessionAccessState.Active, (await new DeviceSessionService(db, clock)
                .ResolveAsync(seeded.Identity, created.Credential.Value)).State);

        clock.Set(Start.AddMinutes(20));
        DeviceSessionSnapshot locked;
        await using (var db = database.Open())
        {
            var service = new DeviceSessionService(db, clock);
            Assert.Equal(DeviceSessionAccessState.Locked,
                (await service.ResolveAsync(seeded.Identity, created.Credential.Value)).State);
            locked = await service.SetLockedAsync(seeded.Identity, created.Session.Id,
                created.Credential.Value, created.Session.Version);
        }
        Assert.Equal(Start.AddMinutes(20), locked.LockedAtUtc);
        Assert.Equal(created.Session.ExpiresAtUtc, locked.ExpiresAtUtc);

        clock.Set(Start.AddMinutes(21));
        DeviceSessionSnapshot unlocked;
        await using (var db = database.Open())
            unlocked = await new DeviceSessionService(db, clock).ClearLockedStateAsync(
                seeded.Identity, locked.Id, created.Credential.Value, locked.Version);
        Assert.Null(unlocked.LockedAtUtc);
        Assert.Equal(Start.AddMinutes(21), unlocked.LastActivityAtUtc);
        Assert.Equal(created.Session.ExpiresAtUtc, unlocked.ExpiresAtUtc);

        clock.Set(Start.AddMinutes(22));
        DeviceSessionSnapshot active;
        await using (var db = database.Open())
            active = await new DeviceSessionService(db, clock).UpdateMeaningfulActivityAsync(
                seeded.Identity, unlocked.Id, created.Credential.Value, unlocked.Version);
        Assert.Equal(Start.AddMinutes(22), active.LastActivityAtUtc);
        Assert.Equal(created.Session.ExpiresAtUtc, active.ExpiresAtUtc);

        clock.Set(Start.AddHours(1));
        await using (var db = database.Open())
            Assert.Equal(DeviceSessionAccessState.FullAuthenticationRequired,
                (await new DeviceSessionService(db, clock)
                    .ResolveAsync(seeded.Identity, created.Credential.Value)).State);
    }

    [Fact]
    public async Task Rotation_is_atomic_invalidates_old_credential_and_preserves_expiry()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, Start);
        var clock = new ManualClock(Start.AddMinutes(5));
        CreatedDeviceSession created;
        await using (var db = database.Open())
            created = await new DeviceSessionService(db, new ManualClock(Start))
                .CreateAsync(seeded.Identity, seeded.DeviceId);

        RotatedDeviceSession rotated;
        await using (var db = database.Open())
            rotated = await new DeviceSessionService(db, clock).RotateCredentialAsync(
                seeded.Identity, created.Session.Id, created.Credential.Value, created.Session.Version);

        Assert.NotEqual(created.Credential.Value, rotated.Credential.Value);
        Assert.Equal(created.Session.Generation + 1, rotated.Session.Generation);
        Assert.Equal(created.Session.ExpiresAtUtc, rotated.Session.ExpiresAtUtc);
        await using (var db = database.Open())
        {
            var service = new DeviceSessionService(db, clock);
            await AssertFullAuthentication(service, seeded.Identity, created.Credential.Value);
            Assert.Equal(DeviceSessionAccessState.Active,
                (await service.ResolveAsync(seeded.Identity, rotated.Credential.Value)).State);
            await AssertConcurrency(() => service.RotateCredentialAsync(seeded.Identity, rotated.Session.Id,
                rotated.Credential.Value, created.Session.Version));
        }
    }

    [Fact]
    public async Task Revoked_session_and_inactive_binding_require_full_authentication()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, Start);
        var clock = new ManualClock(Start);
        CreatedDeviceSession revoked;
        CreatedDeviceSession inactive;
        await using (var db = database.Open())
        {
            var service = new DeviceSessionService(db, clock);
            revoked = await service.CreateAsync(seeded.Identity, seeded.DeviceId);
            inactive = await service.CreateAsync(seeded.Identity, seeded.DeviceId);
        }
        await using (var db = database.Open())
            await new DeviceSessionService(db, clock).RevokeAsync(seeded.Identity, revoked.Session.Id,
                revoked.Credential.Value, revoked.Session.Version);
        await using (var db = database.Open())
            await AssertFullAuthentication(new DeviceSessionService(db, clock), seeded.Identity, revoked.Credential.Value);

        await using (var db = database.Open())
        {
            var binding = await db.IdentityBindings.SingleAsync(x => x.Id == seeded.Identity.IdentityBindingId);
            binding.IsActive = false;
            await db.SaveChangesAsync();
        }
        await using (var db = database.Open())
            await AssertFullAuthentication(new DeviceSessionService(db, clock), seeded.Identity, inactive.Credential.Value);
    }

    [Fact]
    public async Task Concurrent_activity_and_lock_writes_reject_stale_versions()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, Start);
        var activity = await CreateAsync(database, seeded, Start);
        var activityClock = new ManualClock(Start.AddMinutes(1));
        await AssertOneConcurrency(
            database,
            db => new DeviceSessionService(db, activityClock).UpdateMeaningfulActivityAsync(
                seeded.Identity, activity.Session.Id, activity.Credential.Value, activity.Session.Version));

        var locking = await CreateAsync(database, seeded, Start);
        var lockClock = new ManualClock(Start.AddMinutes(20));
        await AssertOneConcurrency(
            database,
            db => new DeviceSessionService(db, lockClock).SetLockedAsync(
                seeded.Identity, locking.Session.Id, locking.Credential.Value, locking.Session.Version));
    }

    [Fact]
    public async Task Concurrent_rotation_has_one_winner_and_only_its_credential_resolves()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, Start);
        var created = await CreateAsync(database, seeded, Start);
        var clock = new ManualClock(Start.AddMinutes(1));
        var firstDb = database.Open();
        var secondDb = database.Open();
        await using var first = firstDb;
        await using var second = secondDb;

        var results = await Task.WhenAll(
            CaptureRotation(new DeviceSessionService(first, clock), seeded.Identity, created),
            CaptureRotation(new DeviceSessionService(second, clock), seeded.Identity, created));

        var winner = Assert.Single(results, x => x.Result is not null).Result!;
        Assert.Single(results, x => x.Failure == FailureKind.ConcurrencyConflict);
        Assert.Equal(created.Session.ExpiresAtUtc, winner.Session.ExpiresAtUtc);
        Assert.Equal(created.Session.Generation + 1, winner.Session.Generation);
        await using var verification = database.Open();
        var service = new DeviceSessionService(verification, clock);
        await AssertFullAuthentication(service, seeded.Identity, created.Credential.Value);
        Assert.Equal(DeviceSessionAccessState.Active,
            (await service.ResolveAsync(seeded.Identity, winner.Credential.Value)).State);
    }

    private static async Task<Seeded> SeedAsync(TestDatabase database, DateTime now)
    {
        await using var db = database.Open();
        var userId = Guid.NewGuid();
        var binding = new IdentityBinding
        {
            UserId = userId,
            ProjectId = "isolated-v3-test",
            ExternalSubject = Guid.NewGuid().ToString("N"),
            IsActive = true,
            ValidAfterUtc = now,
            Version = 1
        };
        var deviceCredential = OpaqueDeviceCredential.Create();
        var device = DeviceAccessPolicy.NewDevice(userId, deviceCredential.Digest,
            "pin-v1$salt$verifier", now);
        db.AddRange(binding, device);
        await db.SaveChangesAsync();
        return new(new DeviceSessionIdentity(userId, binding.Id, binding.Version), device.Id);
    }

    private static async Task<AuthorizedDeviceRecord> AddDeviceAsync(TestDatabase database, Guid userId,
        DateTime enrolledAtUtc, Action<AuthorizedDeviceRecord>? configure = null)
    {
        await using var db = database.Open();
        var credential = OpaqueDeviceCredential.Create();
        var device = DeviceAccessPolicy.NewDevice(userId, credential.Digest, "pin-v1$salt$verifier", enrolledAtUtc);
        configure?.Invoke(device);
        db.AuthorizedDevices.Add(device);
        await db.SaveChangesAsync();
        return device;
    }

    private static async Task<CreatedDeviceSession> CreateAsync(TestDatabase database, Seeded seeded, DateTime now)
    {
        await using var db = database.Open();
        return await new DeviceSessionService(db, new ManualClock(now))
            .CreateAsync(seeded.Identity, seeded.DeviceId);
    }

    private static async Task AssertFullAuthentication(DeviceSessionService service,
        DeviceSessionIdentity identity, string credential)
    {
        var result = await service.ResolveAsync(identity, credential);
        Assert.Equal(DeviceSessionAccessState.FullAuthenticationRequired, result.State);
        Assert.Null(result.Session);
    }

    private static async Task AssertForbidden(Func<Task> action)
    {
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(action);
        Assert.Equal(FailureKind.Forbidden, failure.Kind);
    }

    private static async Task AssertConcurrency(Func<Task> action)
    {
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(action);
        Assert.Equal(FailureKind.ConcurrencyConflict, failure.Kind);
    }

    private static async Task AssertOneConcurrency(TestDatabase database,
        Func<Weymela.Infrastructure.Persistence.WeymelaDbContext, Task<DeviceSessionSnapshot>> operation)
    {
        var firstDb = database.Open();
        var secondDb = database.Open();
        await using var first = firstDb;
        await using var second = secondDb;
        var results = await Task.WhenAll(Capture(() => operation(first)), Capture(() => operation(second)));
        Assert.Single(results, x => x is null);
        Assert.Single(results, x => x == FailureKind.ConcurrencyConflict);
    }

    private static async Task<FailureKind?> Capture(Func<Task> action)
    {
        try { await action(); return null; }
        catch (ApplicationFailure failure) { return failure.Kind; }
    }

    private static async Task<RotationResult> CaptureRotation(DeviceSessionService service,
        DeviceSessionIdentity identity, CreatedDeviceSession created)
    {
        try
        {
            return new(await service.RotateCredentialAsync(identity, created.Session.Id,
                created.Credential.Value, created.Session.Version), null);
        }
        catch (ApplicationFailure failure) { return new(null, failure.Kind); }
    }

    private sealed record Seeded(DeviceSessionIdentity Identity, Guid DeviceId);
    private sealed record RotationResult(RotatedDeviceSession? Result, FailureKind? Failure);

    private sealed class ManualClock(DateTime initial) : TimeProvider
    {
        private DateTime current = initial;
        public void Set(DateTime value) => current = value;
        public override DateTimeOffset GetUtcNow() => new(current);
    }
}
