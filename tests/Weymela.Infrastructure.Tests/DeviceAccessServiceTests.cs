using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class DeviceAccessServiceTests(PostgresFixture fixture)
{
    private static readonly DateTime Start = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Pepper = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task Exact_idle_boundary_locks_before_activity_can_refresh_it()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database);
        var clock = new ManualClock(Start.AddMinutes(20).AddTicks(-1));

        await using (var db = database.Open())
        {
            var status = await Service(db, clock).EnforceAsync(seeded.Identity,
                seeded.DeviceCredential.Value, seeded.SessionCredential.Value, false);
            Assert.Equal(DeviceAccessStates.Unlocked, status.State);
        }

        clock.Set(Start.AddMinutes(20));
        await using (var db = database.Open())
        {
            var status = await Service(db, clock).EnforceAsync(seeded.Identity,
                seeded.DeviceCredential.Value, seeded.SessionCredential.Value, true);
            Assert.Equal(DeviceAccessStates.Locked, status.State);
        }
        await using (var db = database.Open())
        {
            var row = await db.DeviceSessions.SingleAsync(x => x.Id == seeded.SessionId);
            Assert.Equal(Start, row.LastActivityAtUtc);
            Assert.Equal(Start.AddMinutes(20), row.LockedAtUtc);
            Assert.Equal(Start.AddHours(1), row.ExpiresAtUtc);
        }
    }

    [Fact]
    public async Task Meaningful_activity_before_deadline_refreshes_idle_only()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database);
        var clock = new ManualClock(Start.AddMinutes(19));
        await using (var db = database.Open())
        {
            var status = await Service(db, clock).EnforceAsync(seeded.Identity,
                seeded.DeviceCredential.Value, seeded.SessionCredential.Value, true);
            Assert.Equal(DeviceAccessStates.Unlocked, status.State);
            Assert.Equal(Start.AddMinutes(39), status.IdleExpiresAtUtc);
            Assert.Equal(Start.AddHours(1), status.SessionExpiresAtUtc);
        }
        await using var verify = database.Open();
        var session = await verify.DeviceSessions.SingleAsync(x => x.Id == seeded.SessionId);
        Assert.Equal(Start.AddMinutes(19), session.LastActivityAtUtc);
        Assert.Equal(Start.AddHours(1), session.ExpiresAtUtc);
    }

    [Fact]
    public async Task Correct_pin_unlocks_rotates_credential_and_resets_only_device_failures()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, locked: true);
        await using (var db = database.Open())
        {
            var device = await db.AuthorizedDevices.SingleAsync(x => x.Id == seeded.DeviceId);
            device.FailedAttempts = 4;
            device.Version++;
            await db.SaveChangesAsync();
        }
        var clock = new ManualClock(Start.AddMinutes(21));
        DeviceUnlockResult result;
        await using (var db = database.Open())
            result = await Service(db, clock).UnlockAsync(seeded.Identity,
                seeded.DeviceCredential.Value, seeded.SessionCredential.Value, "01234", "unlock-correct");

        Assert.True(result.Succeeded);
        Assert.Equal(DeviceAccessStates.Unlocked, result.Status.State);
        Assert.Equal(Start.AddHours(1), result.Status.SessionExpiresAtUtc);
        Assert.NotNull(result.Credential);
        await using (var db = database.Open())
        {
            var device = await db.AuthorizedDevices.SingleAsync(x => x.Id == seeded.DeviceId);
            var session = await db.DeviceSessions.SingleAsync(x => x.Id == seeded.SessionId);
            Assert.Equal(0, device.FailedAttempts);
            Assert.Null(device.LockedUntilUtc);
            Assert.False(device.RequiresRecovery);
            Assert.Null(session.LockedAtUtc);
            Assert.Equal(Start.AddMinutes(21), session.LastActivityAtUtc);
            Assert.Equal(Start.AddHours(1), session.ExpiresAtUtc);
            Assert.True(OpaqueDeviceCredential.Matches(result.Credential!.Value, session.SessionIdentifierHash));
            Assert.False(OpaqueDeviceCredential.Matches(seeded.SessionCredential.Value, session.SessionIdentifierHash));
        }
    }

    [Fact]
    public async Task Fifth_failure_cools_down_and_tenth_requires_recovery_durably()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, locked: true);
        var clock = new ManualClock(Start.AddMinutes(20));
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await using var db = database.Open();
            var result = await Service(db, clock).UnlockAsync(seeded.Identity,
                seeded.DeviceCredential.Value, seeded.SessionCredential.Value, "99999", $"wrong-{attempt}");
            Assert.False(result.Succeeded);
            Assert.Equal(attempt == 5 ? DeviceAccessStates.Cooldown : DeviceAccessStates.Locked, result.Status.State);
        }

        await using (var db = database.Open())
        {
            var device = await db.AuthorizedDevices.SingleAsync(x => x.Id == seeded.DeviceId);
            Assert.Equal(5, device.FailedAttempts);
            Assert.Equal(Start.AddMinutes(35), device.LockedUntilUtc);
        }
        clock.Set(Start.AddMinutes(35));
        for (var attempt = 6; attempt <= 10; attempt++)
        {
            await using var db = database.Open();
            var result = await Service(db, clock).UnlockAsync(seeded.Identity,
                seeded.DeviceCredential.Value, seeded.SessionCredential.Value, "99999", $"wrong-{attempt}");
            Assert.Equal(attempt == 10 ? DeviceAccessStates.RecoveryRequired : DeviceAccessStates.Locked, result.Status.State);
        }
        await using (var db = database.Open())
        {
            var device = await db.AuthorizedDevices.SingleAsync(x => x.Id == seeded.DeviceId);
            Assert.Equal(10, device.FailedAttempts);
            Assert.True(device.RequiresRecovery);
            Assert.Null(device.LockedUntilUtc);
        }
    }

    [Fact]
    public async Task Unknown_device_and_expired_session_fail_without_touching_attempts()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, locked: true);
        await using (var db = database.Open())
        {
            var result = await Service(db, new ManualClock(Start.AddMinutes(20))).UnlockAsync(
                seeded.Identity, new string('0', 64), seeded.SessionCredential.Value,
                "01234", "unknown-device");
            Assert.Equal(DeviceAccessStates.FullAuthenticationRequired, result.Status.State);
        }
        await using (var db = database.Open())
            Assert.Equal(0, (await db.AuthorizedDevices.SingleAsync(x => x.Id == seeded.DeviceId)).FailedAttempts);

        await using (var db = database.Open())
        {
            var result = await Service(db, new ManualClock(Start.AddHours(1))).UnlockAsync(
                seeded.Identity, seeded.DeviceCredential.Value, seeded.SessionCredential.Value,
                "01234", "expired-session");
            Assert.Equal(DeviceAccessStates.FullAuthenticationRequired, result.Status.State);
        }
    }

    private static DeviceAccessService Service(
        Weymela.Infrastructure.Persistence.WeymelaDbContext db,
        TimeProvider clock) => new(db, new RuntimeOptions { PinPepper = Pepper }, clock);

    private static async Task<Seeded> SeedAsync(TestDatabase database, bool locked = false)
    {
        var verifier = await DevicePinVerifier.HashAsync("01234", Pepper, default);
        var userId = Guid.NewGuid();
        var binding = new IdentityBinding
        {
            UserId = userId, ProjectId = "isolated-v3-test", ExternalSubject = Guid.NewGuid().ToString("N"),
            IsActive = true, ValidAfterUtc = Start, Version = 1
        };
        var deviceCredential = OpaqueDeviceCredential.Create();
        var device = DeviceAccessPolicy.NewDevice(userId, deviceCredential.Digest, verifier, Start);
        var sessionCredential = OpaqueDeviceCredential.Create();
        var session = DeviceAccessPolicy.NewSession(userId, binding.Id, binding.Version,
            device.Id, sessionCredential.Digest, Start);
        if (locked) session.LockedAtUtc = Start.AddMinutes(20);
        await using var db = database.Open();
        db.AddRange(binding, device, session);
        await db.SaveChangesAsync();
        return new(new DeviceSessionIdentity(userId, binding.Id, binding.Version), device.Id,
            session.Id, deviceCredential, sessionCredential);
    }

    private sealed record Seeded(
        DeviceSessionIdentity Identity,
        Guid DeviceId,
        Guid SessionId,
        OpaqueDeviceCredential DeviceCredential,
        OpaqueDeviceCredential SessionCredential);

    private sealed class ManualClock(DateTime initial) : TimeProvider
    {
        private DateTime current = initial;
        public void Set(DateTime value) => current = value;
        public override DateTimeOffset GetUtcNow() => new(current);
    }
}
