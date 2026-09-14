using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

public sealed class DeviceAccessPolicyTests
{
    private static readonly DateTime Start = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Approved_security_boundaries_are_exact()
    {
        Assert.Equal(TimeSpan.FromMinutes(20), DeviceAccessPolicy.IdleLock);
        Assert.Equal(TimeSpan.FromHours(1), DeviceAccessPolicy.SessionLifetime);
        Assert.Equal(TimeSpan.FromDays(30), DeviceAccessPolicy.AuthorizedDeviceLifetime);
        Assert.Equal(TimeSpan.FromMinutes(15), DeviceAccessPolicy.FailedAttemptCooldown);
        Assert.Equal(Start.AddDays(30), DeviceAccessPolicy.DeviceExpiresAt(Start));
        Assert.Equal(Start.AddHours(1), DeviceAccessPolicy.SessionExpiresAt(Start));
    }

    [Fact]
    public void Idle_and_absolute_expiry_use_closed_boundaries()
    {
        Assert.False(DeviceAccessPolicy.IsIdleLocked(Start, Start.AddMinutes(20).AddTicks(-1)));
        Assert.True(DeviceAccessPolicy.IsIdleLocked(Start, Start.AddMinutes(20)));
        Assert.False(DeviceAccessPolicy.IsSessionExpired(Start.AddHours(1), Start.AddHours(1).AddTicks(-1)));
        Assert.True(DeviceAccessPolicy.IsSessionExpired(Start.AddHours(1), Start.AddHours(1)));
        Assert.False(DeviceAccessPolicy.IsDeviceExpired(Start.AddDays(30), Start.AddDays(30).AddTicks(-1)));
        Assert.True(DeviceAccessPolicy.IsDeviceExpired(Start.AddDays(30), Start.AddDays(30)));
    }

    [Fact]
    public void Five_failures_cool_down_and_ten_require_recovery()
    {
        var device = Device();
        for (var attempt = 1; attempt <= 5; attempt++) DeviceAccessPolicy.RegisterFailedPin(device, Start);
        Assert.Equal(5, device.FailedAttempts);
        Assert.Equal(Start.AddMinutes(15), device.LockedUntilUtc);
        Assert.False(device.RequiresRecovery);
        Assert.False(DeviceAccessPolicy.CanAttemptPin(device, Start.AddMinutes(15).AddTicks(-1)));
        Assert.True(DeviceAccessPolicy.CanAttemptPin(device, Start.AddMinutes(15)));
        for (var attempt = 6; attempt <= 10; attempt++) DeviceAccessPolicy.RegisterFailedPin(device, Start.AddMinutes(15));
        Assert.Equal(10, device.FailedAttempts);
        Assert.True(device.RequiresRecovery);
        Assert.Null(device.LockedUntilUtc);
        Assert.False(DeviceAccessPolicy.CanAttemptPin(device, Start.AddDays(1)));
    }

    [Fact]
    public void Successful_verified_pin_resets_only_attempt_state()
    {
        var device = Device();
        for (var attempt = 0; attempt < 10; attempt++) DeviceAccessPolicy.RegisterFailedPin(device, Start);
        var expiry = device.ExpiresAtUtc;
        DeviceAccessPolicy.ResetFailuresAfterVerifiedPin(device);
        Assert.Equal(0, device.FailedAttempts);
        Assert.False(device.RequiresRecovery);
        Assert.Null(device.LockedUntilUtc);
        Assert.Equal(expiry, device.ExpiresAtUtc);
    }

    [Fact]
    public void Opaque_credential_is_256_bit_and_only_digest_enters_record_factory()
    {
        var credential = OpaqueDeviceCredential.Create();
        Assert.Equal(64, credential.Value.Length);
        Assert.Equal(64, credential.Digest.Length);
        Assert.NotEqual(credential.Value, credential.Digest);
        Assert.DoesNotContain(credential.Value, credential.ToString(), StringComparison.Ordinal);
        Assert.True(OpaqueDeviceCredential.Matches(credential.Value, credential.Digest));
        Assert.False(OpaqueDeviceCredential.Matches(new string('0', 64), credential.Digest));
        var record = DeviceAccessPolicy.NewDevice(Guid.NewGuid(), credential.Digest, "pin-v1$salt$verifier", Start);
        Assert.Equal(credential.Digest, record.CredentialIdHash);
        Assert.DoesNotContain(credential.Value, record.CredentialIdHash, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(AuthorizedDeviceRecord).GetProperties(), property => property.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Security_calculations_reject_non_utc_timestamps()
    {
        Assert.Throws<ArgumentException>(() => DeviceAccessPolicy.IdleDeadline(DateTime.SpecifyKind(Start, DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() => DeviceAccessPolicy.DeviceExpiresAt(DateTime.SpecifyKind(Start, DateTimeKind.Unspecified)));
    }

    private static AuthorizedDeviceRecord Device()
    {
        var credential = OpaqueDeviceCredential.Create();
        return DeviceAccessPolicy.NewDevice(Guid.NewGuid(), credential.Digest, "pin-v1$salt$verifier", Start);
    }
}

[Collection("V3 PostgreSQL")]
public sealed class DeviceAccessPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Device_and_session_persist_only_verifiers_and_digests_with_fixed_expiry()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        var userId = Guid.NewGuid();
        var binding = new IdentityBinding
        {
            UserId = userId, ProjectId = "isolated-v3-test", ExternalSubject = Guid.NewGuid().ToString("N"),
            IsActive = true, ValidAfterUtc = now, Version = 1
        };
        db.IdentityBindings.Add(binding);
        var credential = OpaqueDeviceCredential.Create();
        const string rawPin = "12345";
        var pinVerifier = await DevicePinVerifier.HashAsync(rawPin,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), default);
        var device = DeviceAccessPolicy.NewDevice(userId, credential.Digest, pinVerifier, now);
        db.AuthorizedDevices.Add(device);
        var sessionCredential = OpaqueDeviceCredential.Create();
        var session = DeviceAccessPolicy.NewSession(userId, binding.Id, binding.Version, device.Id, sessionCredential.Digest, now);
        db.DeviceSessions.Add(session);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var savedDevice = await db.AuthorizedDevices.SingleAsync();
        var savedSession = await db.DeviceSessions.SingleAsync();
        Assert.Equal(pinVerifier, savedDevice.PinVerifier);
        Assert.NotEqual(rawPin, savedDevice.PinVerifier);
        Assert.DoesNotContain(typeof(AuthorizedDeviceRecord).GetProperties(), property =>
            property.Name.Equals("Pin", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(credential.Digest, savedDevice.CredentialIdHash);
        Assert.Equal(now.AddDays(30), savedDevice.ExpiresAtUtc);
        Assert.Equal(sessionCredential.Digest, savedSession.SessionIdentifierHash);
        Assert.Equal(now.AddHours(1), savedSession.ExpiresAtUtc);
        Assert.DoesNotContain(credential.Value, savedDevice.CredentialIdHash, StringComparison.Ordinal);
        Assert.DoesNotContain(sessionCredential.Value, savedSession.SessionIdentifierHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorized_device_uses_optimistic_concurrency()
    {
        var database = await fixture.CreateAsync();
        Guid id;
        await using (var seed = database.Open())
        {
            var credential = OpaqueDeviceCredential.Create();
            var device = DeviceAccessPolicy.NewDevice(Guid.NewGuid(), credential.Digest, "pin-v1$salt$verifier",
                new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc));
            seed.AuthorizedDevices.Add(device);
            await seed.SaveChangesAsync();
            id = device.Id;
        }
        await using var first = database.Open();
        await using var second = database.Open();
        var firstCopy = await first.AuthorizedDevices.SingleAsync(x => x.Id == id);
        var secondCopy = await second.AuthorizedDevices.SingleAsync(x => x.Id == id);
        firstCopy.FailedAttempts = 1;
        secondCopy.FailedAttempts = 1;
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Device_session_uses_optimistic_concurrency()
    {
        var database = await fixture.CreateAsync();
        Guid id;
        await using (var seed = database.Open())
        {
            var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
            var userId = Guid.NewGuid();
            var binding = new IdentityBinding
            {
                UserId = userId, ProjectId = "isolated-v3-test", ExternalSubject = Guid.NewGuid().ToString("N"),
                IsActive = true, ValidAfterUtc = now, Version = 1
            };
            var deviceCredential = OpaqueDeviceCredential.Create();
            var device = DeviceAccessPolicy.NewDevice(userId, deviceCredential.Digest, "pin-v1$salt$verifier", now);
            var sessionCredential = OpaqueDeviceCredential.Create();
            var session = DeviceAccessPolicy.NewSession(userId, binding.Id, binding.Version, device.Id,
                sessionCredential.Digest, now);
            seed.AddRange(binding, device, session);
            await seed.SaveChangesAsync();
            id = session.Id;
        }
        await using var first = database.Open();
        await using var second = database.Open();
        var firstCopy = await first.DeviceSessions.SingleAsync(x => x.Id == id);
        var secondCopy = await second.DeviceSessions.SingleAsync(x => x.Id == id);
        firstCopy.LastActivityAtUtc = firstCopy.LastActivityAtUtc.AddMinutes(1);
        secondCopy.LastActivityAtUtc = secondCopy.LastActivityAtUtc.AddMinutes(1);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
