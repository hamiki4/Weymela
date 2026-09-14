using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

public sealed record DevicePinRecoveryRequest(
    DeviceSessionIdentity Identity,
    string DeviceCredential,
    string Identifier,
    string Code,
    string NewPin,
    string ConfirmPin,
    string IdempotencyKey);

public sealed record DevicePinRecoveryResult(
    Guid AuthorizedDeviceId,
    OpaqueDeviceCredential DeviceCredential,
    DeviceSessionSnapshot Session,
    OpaqueDeviceCredential DeviceSessionCredential);

public sealed class DevicePinRecoveryUnavailableException : Exception
{
    public DevicePinRecoveryUnavailableException() : base("PIN recovery is temporarily unavailable.") { }
}

/// <summary>
/// Atomic, persistence-only completion of verified-email PIN recovery. Calling code
/// must supply the identity established by the existing Firebase-backed session and
/// the current recognized-device credential. This service does not issue cookies,
/// authenticate HTTP requests, select profiles, or alter commerce authorization.
/// </summary>
public sealed class DevicePinRecoveryService(
    WeymelaDbContext db,
    RuntimeOptions options,
    TimeProvider clock)
{
    private const string Operation = "DevicePinRecovery";
    private const string RequestFingerprint = "firebase-account-recognized-device-email-code-pin-recovery-v1";

    public async Task<DevicePinRecoveryResult> CompleteAsync(
        DevicePinRecoveryRequest request,
        CancellationToken ct = default)
    {
        ValidateRequest(request);
        EnsureCryptography();

        // Slow PIN derivation is bounded by DevicePinVerifier and occurs before row
        // locks are acquired. Plaintext PIN material is never placed in an entity.
        var pinVerifier = await DevicePinVerifier.HashAsync(request.NewPin, options.PinPepper!, ct);
        var replacementDeviceCredential = OpaqueDeviceCredential.Create();
        var replacementSessionCredential = OpaqueDeviceCredential.Create();
        var identifier = EmailAuthService.NormalizeIdentifier(request.Identifier, out var identifierKind);
        var identifierHash = EmailAuthService.HashIdentifier(identifier);
        if (!OpaqueDeviceCredential.TryDigest(request.DeviceCredential, out var deviceDigest))
            throw Forbidden();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var now = UtcNow();
            if (await db.IdempotencyRecords.AsNoTracking().AnyAsync(x =>
                    x.ActorId == request.Identity.UserId && x.OperationType == Operation
                    && x.Key == request.IdempotencyKey, ct))
                throw new ApplicationFailure(FailureKind.IdempotencyConflict,
                    "This PIN recovery was already completed. Sign in again to continue.");

            var bindingIsCurrent = await db.IdentityBindings.AsNoTracking().AnyAsync(x =>
                x.Id == request.Identity.IdentityBindingId && x.UserId == request.Identity.UserId
                && x.Version == request.Identity.IdentityVersion && x.IsActive
                && x.Provider == "Firebase" && x.ProjectId == options.FirebaseProjectId, ct);
            if (!bindingIsCurrent) throw Forbidden();

            var currentDevice = await LoadCurrentDeviceForUpdateAsync(
                request.Identity.UserId, deviceDigest, ct);
            if (!UsableRecoveryDevice(currentDevice, now)) throw Forbidden();

            var verifiedEmails = await db.AuthIdentifiers.AsNoTracking().Where(x =>
                x.UserId == request.Identity.UserId && x.Kind == "Email" && x.IsVerified).ToListAsync(ct);
            if (verifiedEmails.Count != 1 || string.IsNullOrWhiteSpace(verifiedEmails[0].DeliveryAddress))
                throw InvalidCode();
            var verifiedEmail = verifiedEmails[0];
            string normalizedDeliveryEmail;
            AuthIdentifierKind deliveryKind;
            try
            {
                normalizedDeliveryEmail = EmailAuthService.NormalizeIdentifier(
                    verifiedEmail.DeliveryAddress!, out deliveryKind);
            }
            catch (ApplicationFailure)
            {
                throw InvalidCode();
            }
            if (deliveryKind != AuthIdentifierKind.Email
                || EmailAuthService.HashIdentifier(normalizedDeliveryEmail) != verifiedEmail.IdentifierHash)
                throw InvalidCode();

            var identifierBinding = await db.AuthIdentifiers.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Kind == identifierKind.ToString() && x.IdentifierHash == identifierHash, ct);
            if (identifierBinding is null || identifierBinding.UserId != request.Identity.UserId
                || identifierKind == AuthIdentifierKind.Email && !identifierBinding.IsVerified)
                throw InvalidCode();

            var challenges = await db.EmailAuthChallenges
                .FromSqlInterpolated($"SELECT * FROM v3.\"EmailAuthChallenges\" WHERE \"IdentifierHash\" = {identifierHash} AND \"Purpose\" = {EmailCodePurpose.PinRecovery.ToString()} ORDER BY \"CreatedAtUtc\" DESC LIMIT 1 FOR UPDATE")
                .AsTracking().ToListAsync(ct);
            var challenge = challenges.SingleOrDefault();
            if (!ValidChallenge(challenge, request.Identity.UserId, identifierKind,
                    identifierHash, verifiedEmail.IdentifierHash, now))
                throw InvalidCode();

            var codeIsWellFormed = request.Code.Length == 6
                && request.Code.All(c => c is >= '0' and <= '9');
            if (!codeIsWellFormed
                || !AuthCodeHashing.Verify(request.Code, challenge!.CodeHash, options.AuthCodeHashKey!))
            {
                challenge!.AttemptCount = Math.Min(challenge.AttemptCount + 1, challenge.MaxAttempts);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                throw InvalidCode();
            }

            var devices = await db.AuthorizedDevices
                .FromSqlInterpolated($"SELECT *, xmin FROM v3.\"AuthorizedDevices\" WHERE \"UserId\" = {request.Identity.UserId} FOR UPDATE")
                .AsTracking().ToListAsync(ct);
            var sessions = await db.DeviceSessions
                .FromSqlInterpolated($"SELECT *, xmin FROM v3.\"DeviceSessions\" WHERE \"UserId\" = {request.Identity.UserId} FOR UPDATE")
                .AsTracking().ToListAsync(ct);

            foreach (var device in devices.Where(x => x.RevokedAtUtc is null))
            {
                device.RevokedAtUtc = now;
                device.Version++;
            }
            foreach (var session in sessions.Where(x => x.RevokedAtUtc is null))
            {
                session.RevokedAtUtc = now;
                session.Version++;
            }

            var replacementDevice = DeviceAccessPolicy.NewDevice(request.Identity.UserId,
                replacementDeviceCredential.Digest, pinVerifier, now);
            replacementDevice.LastUsedAtUtc = now;
            var replacementSession = DeviceAccessPolicy.NewSession(request.Identity.UserId,
                request.Identity.IdentityBindingId, request.Identity.IdentityVersion,
                replacementDevice.Id, replacementSessionCredential.Digest, now);
            challenge.ConsumedAtUtc = now;

            db.AuthorizedDevices.Add(replacementDevice);
            db.DeviceSessions.Add(replacementSession);
            db.IdempotencyRecords.Add(new StoredIdempotencyRecord(request.Identity.UserId,
                Operation, request.IdempotencyKey, RequestFingerprint,
                $"device={replacementDevice.Id:D};session={replacementSession.Id:D}", now));
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "DevicePinRecovered",
                request.Identity.UserId, null, null, null, Guid.NewGuid(), now,
                $"request={request.IdempotencyKey};replacementDevice={replacementDevice.Id:D};replacementSession={replacementSession.Id:D};revokedDevices={devices.Count};revokedSessions={sessions.Count}"));

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new DevicePinRecoveryResult(replacementDevice.Id,
                replacementDeviceCredential, ToSnapshot(replacementSession),
                replacementSessionCredential);
        }
        catch (PostgresException exception) when (exception.SqlState is
            PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            db.ChangeTracker.Clear();
            throw new ApplicationFailure(FailureKind.ConcurrencyConflict,
                "PIN recovery changed concurrently. Start recovery again.", exception);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            db.ChangeTracker.Clear();
            throw new ApplicationFailure(FailureKind.ConcurrencyConflict,
                "PIN recovery changed concurrently. Start recovery again.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres
            && postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            db.ChangeTracker.Clear();
            throw new ApplicationFailure(FailureKind.ConcurrencyConflict,
                "PIN recovery changed concurrently. Start recovery again.", exception);
        }
        catch (InvalidOperationException exception) when (exception.InnerException is PostgresException postgres
            && postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            // Npgsql's retrying execution strategy wraps a serialization failure
            // raised while materializing a SELECT ... FOR UPDATE query.
            db.ChangeTracker.Clear();
            throw new ApplicationFailure(FailureKind.ConcurrencyConflict,
                "PIN recovery changed concurrently. Start recovery again.", exception);
        }
    }

    private async Task<AuthorizedDeviceRecord?> LoadCurrentDeviceForUpdateAsync(
        Guid userId, string deviceDigest, CancellationToken ct)
    {
        var devices = await db.AuthorizedDevices
            .FromSqlInterpolated($"SELECT *, xmin FROM v3.\"AuthorizedDevices\" WHERE \"UserId\" = {userId} AND \"CredentialIdHash\" = {deviceDigest} FOR UPDATE")
            .AsTracking().ToListAsync(ct);
        return devices.SingleOrDefault();
    }

    private static bool ValidChallenge(
        EmailAuthChallengeRecord? challenge,
        Guid userId,
        AuthIdentifierKind identifierKind,
        string identifierHash,
        string verifiedEmailHash,
        DateTime now) =>
        challenge is not null
        && challenge.UserId == userId
        && challenge.Purpose == EmailCodePurpose.PinRecovery.ToString()
        && challenge.IdentifierHash == identifierHash
        && challenge.EmailIdentifierHash == verifiedEmailHash
        && challenge.LastSentAtUtc is not null
        && (identifierKind == AuthIdentifierKind.Email
            ? challenge.PhoneIdentifierHash is null
            : challenge.PhoneIdentifierHash == identifierHash)
        && challenge.ConsumedAtUtc is null
        && challenge.ExpiresAtUtc > now
        && challenge.AttemptCount < challenge.MaxAttempts;

    private static bool UsableRecoveryDevice(AuthorizedDeviceRecord? device, DateTime now) =>
        device is not null && device.CredentialKind == "BrowserCookie"
        && device.RevokedAtUtc is null && device.ExpiresAtUtc is not null
        && !DeviceAccessPolicy.IsDeviceExpired(device.ExpiresAtUtc.Value, now)
        && !string.IsNullOrWhiteSpace(device.PinVerifier);

    private static void ValidateRequest(DevicePinRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Identity.UserId == Guid.Empty || request.Identity.IdentityBindingId == Guid.Empty
            || request.Identity.IdentityVersion < 0)
            throw Forbidden();
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200)
            throw new ApplicationFailure(FailureKind.Validation,
                "A request reference is required. Please restart PIN recovery.");
        if (!DevicePinVerifier.ConfirmationMatches(request.NewPin, request.ConfirmPin))
            throw new ApplicationFailure(FailureKind.Validation, "The PIN entries do not match.");
    }

    private void EnsureCryptography()
    {
        try
        {
            DevicePinVerifier.EnsureConfigured(options.PinPepper);
            if (string.IsNullOrWhiteSpace(options.AuthCodeHashKey)
                || string.IsNullOrWhiteSpace(options.FirebaseProjectId))
                throw new AuthChallengeUnavailableException("Email authentication is temporarily unavailable.");
        }
        catch (AuthChallengeUnavailableException)
        {
            throw new DevicePinRecoveryUnavailableException();
        }
    }

    private DateTime UtcNow() => clock.GetUtcNow().UtcDateTime;
    private static AuthChallengeInvalidException InvalidCode() => new();
    private static ApplicationFailure Forbidden() => new(FailureKind.Forbidden,
        "Complete verified account authentication on this recognized device before recovering the PIN.");
    private static DeviceSessionSnapshot ToSnapshot(DeviceSessionRecord session) => new(
        session.Id, session.UserId, session.AuthorizedDeviceId, session.CreatedAtUtc,
        session.LastActivityAtUtc, session.LockedAtUtc, session.ExpiresAtUtc,
        session.Generation, session.Version);
}
