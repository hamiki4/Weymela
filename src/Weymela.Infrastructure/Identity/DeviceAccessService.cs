using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

public static class DeviceAccessStates
{
    public const string Unlocked = "Unlocked";
    public const string Locked = "Locked";
    public const string Cooldown = "Cooldown";
    public const string RecoveryRequired = "RecoveryRequired";
    public const string EnrollmentRequired = "EnrollmentRequired";
    public const string FullAuthenticationRequired = "FullAuthenticationRequired";
}

public sealed record DeviceAccessStatus(
    string State,
    DateTime? IdleExpiresAtUtc,
    DateTime? SessionExpiresAtUtc,
    int? RetryAfterSeconds);

public sealed record DeviceUnlockResult(
    DeviceAccessStatus Status,
    DeviceSessionSnapshot? Session,
    OpaqueDeviceCredential? Credential,
    bool Succeeded);

public sealed class DeviceAccessUnavailableException : Exception
{
    public DeviceAccessUnavailableException() : base("Secure device access is temporarily unavailable.") { }
}

/// <summary>
/// Authoritative device-session lock and PIN operations. Every operation requires
/// an already authenticated identity plus both independent opaque credentials.
/// Neither credential is an authentication mechanism by itself.
/// </summary>
public sealed class DeviceAccessService(
    WeymelaDbContext db,
    RuntimeOptions options,
    TimeProvider clock)
{
    private const string UnlockOperation = "DevicePinUnlock";
    private const string UnlockFingerprint = "recognized-device-pin-unlock-v1";

    public Task<DeviceAccessStatus> InspectAsync(
        DeviceSessionIdentity identity,
        string? rawDeviceCredential,
        string? rawSessionCredential,
        CancellationToken ct = default)
        => EvaluateAsync(identity, rawDeviceCredential, rawSessionCredential, false, ct);

    public Task<DeviceAccessStatus> EnforceAsync(
        DeviceSessionIdentity identity,
        string? rawDeviceCredential,
        string? rawSessionCredential,
        bool meaningfulActivity,
        CancellationToken ct = default)
        => EvaluateAsync(identity, rawDeviceCredential, rawSessionCredential, meaningfulActivity, ct);

    public async Task<DeviceUnlockResult> UnlockAsync(
        DeviceSessionIdentity identity,
        string? rawDeviceCredential,
        string? rawSessionCredential,
        string pin,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        ValidateIdentity(identity);
        DevicePinVerifier.Validate(pin);
        EnsureCryptography();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            throw new ApplicationFailure(FailureKind.Validation,
                "A request reference is required. Please retry PIN unlock.");
        if (!OpaqueDeviceCredential.TryDigest(rawDeviceCredential, out var deviceDigest)
            || !OpaqueDeviceCredential.TryDigest(rawSessionCredential, out var sessionDigest))
            return new(FullAuthentication(), null, null, false);

        // Row locks serialize attempts for this exact DeviceSession/device pair so
        // concurrent wrong PINs cannot overwrite or lose durable increments.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            if (await db.IdempotencyRecords.AsNoTracking().AnyAsync(x => x.ActorId == identity.UserId
                && x.OperationType == UnlockOperation && x.Key == idempotencyKey, ct))
                throw new ApplicationFailure(FailureKind.IdempotencyConflict,
                    "This unlock request was already completed. Check the current lock state.");

            var loaded = await LoadForUpdateAsync(identity, deviceDigest, sessionDigest, ct);
            if (loaded is null) return new(FullAuthentication(), null, null, false);
            var now = UtcNow();
            var (session, device) = loaded.Value;
            var invalid = await InvalidBindingOrStateAsync(identity, session, device, now, ct);
            if (invalid) return new(FullAuthentication(), null, null, false);
            if (device.RequiresRecovery)
                return new(RecoveryRequired(session), null, null, false);
            if (!DeviceAccessPolicy.CanAttemptPin(device, now))
                return new(Cooldown(session, device.LockedUntilUtc!.Value, now), null, null, false);
            if (session.LockedAtUtc is null && !DeviceAccessPolicy.IsIdleLocked(session.LastActivityAtUtc, now))
                throw new ApplicationFailure(FailureKind.Validation, "Weymela is already unlocked.");

            var verified = await DevicePinVerifier.VerifyAsync(pin, device.PinVerifier!, options.PinPepper!, ct);
            if (!verified)
            {
                DeviceAccessPolicy.RegisterFailedPin(device, now);
                device.Version++;
                db.IdempotencyRecords.Add(new StoredIdempotencyRecord(identity.UserId, UnlockOperation,
                    idempotencyKey, UnlockFingerprint, "rejected", now));
                await SaveAsync(ct);
                await transaction.CommitAsync(ct);
                if (device.RequiresRecovery) return new(RecoveryRequired(session), null, null, false);
                if (!DeviceAccessPolicy.CanAttemptPin(device, now))
                    return new(Cooldown(session, device.LockedUntilUtc!.Value, now), null, null, false);
                return new(new DeviceAccessStatus(DeviceAccessStates.Locked,
                    DeviceAccessPolicy.IdleDeadline(session.LastActivityAtUtc), session.ExpiresAtUtc, null), null, null, false);
            }

            DeviceAccessPolicy.ResetFailuresAfterVerifiedPin(device);
            device.LastUsedAtUtc = now;
            device.Version++;
            session.LockedAtUtc = null;
            session.LastActivityAtUtc = now;
            var credential = OpaqueDeviceCredential.Create();
            session.SessionIdentifierHash = credential.Digest;
            session.Generation++;
            session.Version++;
            db.IdempotencyRecords.Add(new StoredIdempotencyRecord(identity.UserId, UnlockOperation,
                idempotencyKey, UnlockFingerprint, session.Id.ToString("D"), now));
            await SaveAsync(ct);
            await transaction.CommitAsync(ct);
            return new(Unlocked(session), ToSnapshot(session), credential, true);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            db.ChangeTracker.Clear();
            throw new ApplicationFailure(FailureKind.ConcurrencyConflict,
                "The device lock changed. Check its current state and try again.", exception);
        }
    }

    private async Task<DeviceAccessStatus> EvaluateAsync(
        DeviceSessionIdentity identity,
        string? rawDeviceCredential,
        string? rawSessionCredential,
        bool meaningfulActivity,
        CancellationToken ct)
    {
        ValidateIdentity(identity);
        if (!OpaqueDeviceCredential.TryDigest(rawDeviceCredential, out var deviceDigest))
            return EnrollmentRequired();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var now = UtcNow();
        var device = await LoadDeviceForUpdateAsync(identity, deviceDigest, ct);
        if (device is null) return EnrollmentRequired();
        if (device.RequiresRecovery) return RecoveryRequired();
        if (device.RevokedAtUtc is not null || device.ExpiresAtUtc is null
            || DeviceAccessPolicy.IsDeviceExpired(device.ExpiresAtUtc.Value, now)
            || string.IsNullOrWhiteSpace(device.PinVerifier)) return FullAuthentication();
        if (!OpaqueDeviceCredential.TryDigest(rawSessionCredential, out var sessionDigest))
            return FullAuthentication();
        var session = await LoadSessionForUpdateAsync(identity, sessionDigest, ct);
        if (session is null || session.AuthorizedDeviceId != device.Id) return FullAuthentication();
        if (await InvalidBindingOrStateAsync(identity, session, device, now, ct))
            return FullAuthentication();
        if (!DeviceAccessPolicy.CanAttemptPin(device, now))
            return Cooldown(session, device.LockedUntilUtc!.Value, now);

        if (session.LockedAtUtc is not null || DeviceAccessPolicy.IsIdleLocked(session.LastActivityAtUtc, now))
        {
            if (session.LockedAtUtc is null)
            {
                session.LockedAtUtc = now;
                session.Version++;
                await SaveAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return Locked(session);
        }

        if (meaningfulActivity)
        {
            session.LastActivityAtUtc = now;
            session.Version++;
            await SaveAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return Unlocked(session);
    }

    private async Task<(DeviceSessionRecord Session, AuthorizedDeviceRecord Device)?> LoadForUpdateAsync(
        DeviceSessionIdentity identity,
        string deviceDigest,
        string sessionDigest,
        CancellationToken ct)
    {
        var device = await LoadDeviceForUpdateAsync(identity, deviceDigest, ct);
        if (device is null) return null;
        var session = await LoadSessionForUpdateAsync(identity, sessionDigest, ct);
        if (session is null || session.AuthorizedDeviceId != device.Id) return null;
        return (session, device);
    }

    private async Task<AuthorizedDeviceRecord?> LoadDeviceForUpdateAsync(
        DeviceSessionIdentity identity, string deviceDigest, CancellationToken ct) =>
        await db.AuthorizedDevices
            .FromSqlInterpolated($"SELECT *, xmin FROM v3.\"AuthorizedDevices\" WHERE \"UserId\" = {identity.UserId} AND \"CredentialIdHash\" = {deviceDigest} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    private async Task<DeviceSessionRecord?> LoadSessionForUpdateAsync(
        DeviceSessionIdentity identity, string sessionDigest, CancellationToken ct) =>
        await db.DeviceSessions
            .FromSqlInterpolated($"SELECT *, xmin FROM v3.\"DeviceSessions\" WHERE \"UserId\" = {identity.UserId} AND \"SessionIdentifierHash\" = {sessionDigest} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    private async Task<bool> InvalidBindingOrStateAsync(
        DeviceSessionIdentity identity,
        DeviceSessionRecord session,
        AuthorizedDeviceRecord device,
        DateTime now,
        CancellationToken ct)
    {
        var bindingCurrent = await db.IdentityBindings.AsNoTracking().AnyAsync(x =>
            x.Id == identity.IdentityBindingId && x.UserId == identity.UserId && x.IsActive
            && x.Version == identity.IdentityVersion, ct);
        return !bindingCurrent || session.IdentityBindingId != identity.IdentityBindingId
            || session.IdentityVersion != identity.IdentityVersion
            || session.AuthorizedDeviceId != device.Id || session.RevokedAtUtc is not null
            || device.RevokedAtUtc is not null || device.ExpiresAtUtc is null
            || DeviceAccessPolicy.IsDeviceExpired(device.ExpiresAtUtc.Value, now)
            || DeviceAccessPolicy.IsSessionExpired(session.ExpiresAtUtc, now)
            || string.IsNullOrWhiteSpace(device.PinVerifier);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException exception)
        {
            db.ChangeTracker.Clear();
            throw new ApplicationFailure(FailureKind.ConcurrencyConflict,
                "The device lock changed. Check its current state and try again.", exception);
        }
    }

    private void EnsureCryptography()
    {
        try { DevicePinVerifier.EnsureConfigured(options.PinPepper); }
        catch (AuthChallengeUnavailableException) { throw new DeviceAccessUnavailableException(); }
    }

    private DateTime UtcNow() => clock.GetUtcNow().UtcDateTime;
    private static DeviceAccessStatus Unlocked(DeviceSessionRecord session) => new(
        DeviceAccessStates.Unlocked, DeviceAccessPolicy.IdleDeadline(session.LastActivityAtUtc),
        session.ExpiresAtUtc, null);
    private static DeviceAccessStatus Locked(DeviceSessionRecord session) => new(
        DeviceAccessStates.Locked, DeviceAccessPolicy.IdleDeadline(session.LastActivityAtUtc),
        session.ExpiresAtUtc, null);
    private static DeviceAccessStatus Cooldown(DeviceSessionRecord session, DateTime until, DateTime now) => new(
        DeviceAccessStates.Cooldown, DeviceAccessPolicy.IdleDeadline(session.LastActivityAtUtc),
        session.ExpiresAtUtc, Math.Max(1, (int)Math.Ceiling((until - now).TotalSeconds)));
    private static DeviceAccessStatus RecoveryRequired(DeviceSessionRecord session) => new(
        DeviceAccessStates.RecoveryRequired, null, session.ExpiresAtUtc, null);
    private static DeviceAccessStatus RecoveryRequired() => new(
        DeviceAccessStates.RecoveryRequired, null, null, null);
    private static DeviceAccessStatus EnrollmentRequired() => new(
        DeviceAccessStates.EnrollmentRequired, null, null, null);
    private static DeviceAccessStatus FullAuthentication() => new(
        DeviceAccessStates.FullAuthenticationRequired, null, null, null);
    private static DeviceSessionSnapshot ToSnapshot(DeviceSessionRecord session) => new(
        session.Id, session.UserId, session.AuthorizedDeviceId, session.CreatedAtUtc,
        session.LastActivityAtUtc, session.LockedAtUtc, session.ExpiresAtUtc,
        session.Generation, session.Version);
    private static void ValidateIdentity(DeviceSessionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.UserId == Guid.Empty || identity.IdentityBindingId == Guid.Empty || identity.IdentityVersion < 0)
            throw new ApplicationFailure(FailureKind.Forbidden,
                "Complete verified account authentication before using device access.");
    }
}
