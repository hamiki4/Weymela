using System.Data;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

public static class DeviceEnrollmentStates
{
    public const string EnrollmentRequired = "EnrollmentRequired";
    public const string Enrolled = "Enrolled";
    public const string Expired = "Expired";
    public const string Revoked = "Revoked";
    public const string RecoveryRequired = "RecoveryRequired";
    public const string NotRequired = "NotRequired";
}

public sealed record DeviceEnrollmentStatus(string State, DateTime? ExpiresAtUtc);
public sealed record DeviceEnrollmentResult(DeviceEnrollmentStatus Status, OpaqueDeviceCredential? Credential);

public sealed class DeviceEnrollmentUnavailableException : Exception
{
    public DeviceEnrollmentUnavailableException() : base("Secure device setup is temporarily unavailable.") { }
}

/// <summary>
/// Initial account-level PIN enrollment only. It does not authenticate a request,
/// select a profile, unlock, or recover a device. The trusted HTTP completion hook
/// may establish a separate DeviceSession after this transaction succeeds.
/// </summary>
public sealed class DeviceEnrollmentService(WeymelaDbContext db, RuntimeOptions options, TimeProvider clock)
{
    private const string Operation = "InitialDevicePinEnrollment";
    private const string RequestFingerprint = "verified-account-pin-enrollment-v1";

    public async Task<DeviceEnrollmentStatus> StatusAsync(Guid userId, string? rawCredential, CancellationToken ct)
    {
        EnsureCryptography();
        await EnsureVerifiedAccountAsync(userId, ct);
        var device = await FindCurrentDeviceAsync(userId, rawCredential, ct);
        return Status(device, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<DeviceEnrollmentResult> EnrollAsync(Guid userId, string pin, string confirmPin,
        string? rawCredential, string idempotencyKey, CancellationToken ct)
    {
        EnsureCryptography();
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            throw new ApplicationFailure(FailureKind.Validation, "A request reference is required. Please retry device setup.");
        if (!DevicePinVerifier.ConfirmationMatches(pin, confirmPin))
            throw new ApplicationFailure(FailureKind.Validation, "The PIN entries do not match.");
        await EnsureVerifiedAccountAsync(userId, ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var current = await FindCurrentDeviceAsync(userId, rawCredential, ct);
        if (current is not null)
        {
            var currentStatus = Status(current, now);
            if (currentStatus.State == DeviceEnrollmentStates.Enrolled)
                return new DeviceEnrollmentResult(currentStatus, null);
            throw new ApplicationFailure(FailureKind.Forbidden,
                "This device cannot be enrolled again. Complete the required account-security flow.");
        }

        if (await db.IdempotencyRecords.AsNoTracking().AnyAsync(x => x.ActorId == userId
            && x.OperationType == Operation && x.Key == idempotencyKey, ct))
            throw new ApplicationFailure(FailureKind.IdempotencyConflict,
                "This device setup was already completed. Sign in again if the device is not recognized.");

        var verifier = await DevicePinVerifier.HashAsync(pin, options.PinPepper!, ct);
        var credential = OpaqueDeviceCredential.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // Repeat both guards inside the transaction so retries cannot silently create
        // a second enrollment. The raw credential is never written to the database.
        current = await FindCurrentDeviceAsync(userId, rawCredential, ct);
        if (current is not null)
        {
            var currentStatus = Status(current, now);
            if (currentStatus.State == DeviceEnrollmentStates.Enrolled)
                return new DeviceEnrollmentResult(currentStatus, null);
            throw new ApplicationFailure(FailureKind.Forbidden,
                "This device cannot be enrolled again. Complete the required account-security flow.");
        }
        if (await db.IdempotencyRecords.AnyAsync(x => x.ActorId == userId
            && x.OperationType == Operation && x.Key == idempotencyKey, ct))
            throw new ApplicationFailure(FailureKind.IdempotencyConflict,
                "This device setup was already completed. Sign in again if the device is not recognized.");

        var device = DeviceAccessPolicy.NewDevice(userId, credential.Digest, verifier, now);
        device.LastUsedAtUtc = now;
        db.AuthorizedDevices.Add(device);
        db.IdempotencyRecords.Add(new StoredIdempotencyRecord(userId, Operation, idempotencyKey,
            RequestFingerprint, device.Id.ToString("D"), now));
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "AuthorizedDeviceEnrolled", userId,
            null, null, null, Guid.NewGuid(), now, $"device={device.Id:D};expiresAtUtc={device.ExpiresAtUtc:O}"));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new DeviceEnrollmentResult(Status(device, now), credential);
    }

    private async Task EnsureVerifiedAccountAsync(Guid userId, CancellationToken ct)
    {
        var verifiedEmail = userId != Guid.Empty && await db.AuthIdentifiers.AsNoTracking().AnyAsync(x => x.UserId == userId
            && x.Kind == "Email" && x.IsVerified, ct);
        var verifiedCashierPhone = userId != Guid.Empty && await db.AuthIdentifiers.AsNoTracking().AnyAsync(x => x.UserId == userId
            && x.Kind == "Phone" && x.IsVerified, ct)
            && await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == userId
                && x.Role == ActorRole.Cashier && x.IsActive && x.CanCheckout, ct);
        if (!verifiedEmail && !verifiedCashierPhone)
            throw new ApplicationFailure(FailureKind.Forbidden,
                "Complete account verification before setting up this device.");
    }

    private async Task<AuthorizedDeviceRecord?> FindCurrentDeviceAsync(Guid userId, string? rawCredential, CancellationToken ct)
    {
        if (!OpaqueDeviceCredential.TryDigest(rawCredential, out var digest)) return null;
        return await db.AuthorizedDevices.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId
            && x.CredentialIdHash == digest, ct);
    }

    private static DeviceEnrollmentStatus Status(AuthorizedDeviceRecord? device, DateTime nowUtc)
    {
        if (device is null) return new(DeviceEnrollmentStates.EnrollmentRequired, null);
        if (device.RequiresRecovery) return new(DeviceEnrollmentStates.RecoveryRequired, device.ExpiresAtUtc);
        if (device.RevokedAtUtc is not null) return new(DeviceEnrollmentStates.Revoked, device.ExpiresAtUtc);
        if (device.ExpiresAtUtc is null || DeviceAccessPolicy.IsDeviceExpired(device.ExpiresAtUtc.Value, nowUtc))
            return new(DeviceEnrollmentStates.Expired, device.ExpiresAtUtc);
        if (string.IsNullOrWhiteSpace(device.PinVerifier))
            return new(DeviceEnrollmentStates.EnrollmentRequired, device.ExpiresAtUtc);
        return new(DeviceEnrollmentStates.Enrolled, device.ExpiresAtUtc);
    }

    private void EnsureCryptography()
    {
        try { DevicePinVerifier.EnsureConfigured(options.PinPepper); }
        catch (AuthChallengeUnavailableException) { throw new DeviceEnrollmentUnavailableException(); }
    }
}
