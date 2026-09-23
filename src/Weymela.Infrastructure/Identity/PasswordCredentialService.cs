using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

public sealed record AccountSecurityStatus(bool PasswordEnrolled, bool PhoneEnrolled);
public sealed record PasswordSignInResult(bool Succeeded, FirebaseCustomTokenResult? Token);

/// <summary>
/// Owns the private phone/password credential for an existing, email-verified V3
/// identity. It never creates users, bindings, profiles or permissions.
/// </summary>
public sealed class PasswordCredentialService(
    WeymelaDbContext db,
    IFirebaseCustomTokenIssuer tokenIssuer,
    RuntimeOptions options,
    TimeProvider clock)
{
    private static readonly TimeSpan FirstLockout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ExtendedLockout = TimeSpan.FromMinutes(30);

    public async Task<AccountSecurityStatus> StatusAsync(Guid userId, CancellationToken ct) => new(
        await db.PasswordCredentials.AsNoTracking().AnyAsync(x => x.UserId == userId, ct),
        await db.AuthIdentifiers.AsNoTracking().AnyAsync(x => x.UserId == userId && x.Kind == "Phone", ct));

    public async Task EnrollAsync(
        DeviceSessionIdentity identity,
        string? phone,
        string password,
        string confirmPassword,
        CancellationToken ct)
    {
        PasswordCredentialHasher.ValidateNew(password, confirmPassword);
        var verifier = PasswordCredentialHasher.Hash(password);
        var canonicalPhone = string.IsNullOrWhiteSpace(phone) ? null : NormalizePhone(phone);
        var phoneHash = canonicalPhone is null ? null : EmailAuthService.HashIdentifier(canonicalPhone);
        var now = clock.GetUtcNow().UtcDateTime;

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var verifiedEmail = await db.AuthIdentifiers.AsNoTracking().AnyAsync(x => x.UserId == identity.UserId
                && x.Kind == "Email" && x.IsVerified && x.DeliveryAddress != null, ct);
            var cashierPhone = await db.AuthIdentifiers.AsNoTracking().AnyAsync(x => x.UserId == identity.UserId
                && x.Kind == "Phone" && x.IsVerified, ct)
                && await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == identity.UserId
                    && x.Role == ActorRole.Cashier && x.IsActive && x.CanCheckout, ct);
            if (!await ValidBinding(identity, ct) || (!verifiedEmail && !cashierPhone))
                throw new ApplicationFailure(FailureKind.Forbidden, "Complete verified account sign-in before securing your account.");

            var phones = await db.AuthIdentifiers.AsTracking()
                .Where(x => x.UserId == identity.UserId && x.Kind == "Phone").ToListAsync(ct);
            if (phones.Count > 1)
                throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "Account security setup could not be completed.");
            if (phones.Count == 0 && phoneHash is null)
                throw new ApplicationFailure(FailureKind.Validation, "Enter a valid phone number.");
            if (phones.Count == 1 && phoneHash is not null && phones[0].IdentifierHash != phoneHash)
                throw new ApplicationFailure(FailureKind.Validation, "Use the phone number already registered to this account.");
            if (phoneHash is not null)
            {
                var claimed = await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Phone"
                    && x.IdentifierHash == phoneHash, ct);
                if (claimed is not null && claimed.UserId != identity.UserId) throw PhoneCollision();
                if (phones.Count == 0)
                    db.AuthIdentifiers.Add(new AuthIdentifierRecord
                    {
                        UserId = identity.UserId,
                        Kind = "Phone",
                        IdentifierHash = phoneHash,
                        DeliveryAddress = canonicalPhone,
                        IsVerified = false,
                        CreatedAtUtc = now
                    });
                else if (claimed is not null)
                    claimed.DeliveryAddress = canonicalPhone;
            }

            var existing = await db.PasswordCredentials.AsTracking()
                .SingleOrDefaultAsync(x => x.UserId == identity.UserId, ct);
            if (existing is not null)
            {
                if (!PasswordCredentialHasher.Verify(password, existing))
                    throw new ApplicationFailure(FailureKind.IdempotencyConflict, "Password sign-in is already set up.");
                await tx.CommitAsync(ct);
                return;
            }

            db.PasswordCredentials.Add(new PasswordCredentialRecord
            {
                UserId = identity.UserId,
                PasswordHash = verifier,
                Algorithm = PasswordCredentialHasher.Algorithm,
                HashVersion = PasswordCredentialHasher.Version,
                WorkFactor = PasswordCredentialHasher.Iterations,
                CreatedAtUtc = now,
                ChangedAtUtc = now,
                Version = 1
            });
            db.AuditEvents.Add(Audit("PasswordCredentialEnrolled", identity.UserId, now));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception exception) when (DatabaseCollision(exception))
        {
            throw PhoneCollision();
        }
    }

    public async Task<PasswordSignInResult> SignInAsync(string phone, string password, CancellationToken ct)
    {
        var canonical = NormalizePhone(phone);
        if (string.IsNullOrEmpty(password) || password.Length > PasswordCredentialHasher.MaximumLength)
        {
            PasswordCredentialHasher.VerifyDummy(password);
            return new(false, null);
        }

        var phoneHash = EmailAuthService.HashIdentifier(canonical);
        var now = clock.GetUtcNow().UtcDateTime;
        Guid? userId = null;
        var succeeded = false;

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var identifier = await db.AuthIdentifiers.AsTracking()
                    .SingleOrDefaultAsync(x => x.Kind == "Phone" && x.IdentifierHash == phoneHash, ct);
            PasswordCredentialRecord? credential = null;
            if (identifier is not null)
            {
                credential = await db.PasswordCredentials.FromSqlInterpolated(
                    $"SELECT *, xmin FROM v3.\"PasswordCredentials\" WHERE \"UserId\" = {identifier.UserId} FOR UPDATE")
                    .SingleOrDefaultAsync(ct);
            }

            if (credential is null)
            {
                PasswordCredentialHasher.VerifyDummy(password);
            }
            else if (credential.LockedUntilUtc is not null && credential.LockedUntilUtc > now)
            {
                PasswordCredentialHasher.VerifyDummy(password);
            }
            else
            {
                if (credential.LockedUntilUtc is not null)
                {
                    credential.FailedAttempts = 0;
                    credential.LockedUntilUtc = null;
                }
                succeeded = PasswordCredentialHasher.Verify(password, credential);
                userId = credential.UserId;
                if (succeeded && await HasAuthoritativeIdentity(credential.UserId, ct))
                {
                    identifier!.DeliveryAddress = canonical;
                    credential.FailedAttempts = 0;
                    credential.LockedUntilUtc = null;
                    db.AuditEvents.Add(Audit("PasswordSignInSucceeded", credential.UserId, now));
                }
                else
                {
                    succeeded = false;
                    credential.FailedAttempts = Math.Min(10, credential.FailedAttempts + 1);
                    credential.LockedUntilUtc = credential.FailedAttempts switch
                    {
                        >= 10 => now.Add(ExtendedLockout),
                        >= 5 => now.Add(FirstLockout),
                        _ => null
                    };
                    db.AuditEvents.Add(Audit("PasswordSignInFailed", credential.UserId, now));
                }
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (Exception exception) when (DatabaseCollision(exception))
        {
            PasswordCredentialHasher.VerifyDummy(password);
            return new(false, null);
        }

        if (!succeeded || userId is null) return new(false, null);
        return new(true, await tokenIssuer.IssueAsync(userId.Value, options.FirebaseProjectId, ct));
    }

    public async Task ResetAsync(
        string recoveryGrant,
        string newPassword,
        string confirmPassword,
        CancellationToken ct)
    {
        PasswordCredentialHasher.ValidateNew(newPassword, confirmPassword);
        if (string.IsNullOrWhiteSpace(recoveryGrant) || recoveryGrant.Length > 128)
            throw new PasswordRecoveryTransactionInvalidException();
        var grantHash = HashRecoveryGrant(recoveryGrant);
        var verifier = PasswordCredentialHasher.Hash(newPassword);
        var now = clock.GetUtcNow().UtcDateTime;

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var challenge = await db.EmailAuthChallenges.FromSqlInterpolated(
                $"SELECT * FROM v3.\"EmailAuthChallenges\" WHERE \"RecoveryGrantHash\" = {grantHash} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            if (challenge is null || challenge.Purpose != EmailCodePurpose.PasswordRecovery.ToString()
                || challenge.RecoveryGrantConsumedAtUtc is not null
                || challenge.RecoveryGrantExpiresAtUtc is null
                || challenge.RecoveryGrantExpiresAtUtc <= now || challenge.UserId is null
                || challenge.EmailIdentifierHash != challenge.IdentifierHash)
                throw new PasswordRecoveryTransactionInvalidException();
            var emailIdentifier = await db.AuthIdentifiers.AsNoTracking().SingleOrDefaultAsync(x => x.Kind == "Email"
                && x.IdentifierHash == challenge.IdentifierHash && x.UserId == challenge.UserId && x.IsVerified, ct);
            var credential = await db.PasswordCredentials.FromSqlInterpolated(
                $"SELECT *, xmin FROM v3.\"PasswordCredentials\" WHERE \"UserId\" = {challenge.UserId.Value} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            if (emailIdentifier is null || credential is null) throw new PasswordRecoveryTransactionInvalidException();

            credential.PasswordHash = verifier;
            credential.Algorithm = PasswordCredentialHasher.Algorithm;
            credential.HashVersion = PasswordCredentialHasher.Version;
            credential.WorkFactor = PasswordCredentialHasher.Iterations;
            credential.ChangedAtUtc = now;
            credential.FailedAttempts = 0;
            credential.LockedUntilUtc = null;
            challenge.RecoveryGrantConsumedAtUtc = now;

            var sessions = await db.DeviceSessions.AsTracking()
                .Where(x => x.UserId == credential.UserId && x.RevokedAtUtc == null).ToListAsync(ct);
            foreach (var session in sessions) session.RevokedAtUtc = now;
            db.AuditEvents.Add(Audit("PasswordCredentialReset", credential.UserId, now));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception exception) when (DatabaseCollision(exception))
        {
            throw new PasswordRecoveryTransactionInvalidException();
        }
    }

    public async Task CancelResetAsync(string recoveryGrant, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recoveryGrant) || recoveryGrant.Length > 128) return;
        var grantHash = HashRecoveryGrant(recoveryGrant);
        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var challenge = await db.EmailAuthChallenges.FromSqlInterpolated(
                $"SELECT * FROM v3.\"EmailAuthChallenges\" WHERE \"RecoveryGrantHash\" = {grantHash} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            if (challenge is not null
                && challenge.Purpose == EmailCodePurpose.PasswordRecovery.ToString()
                && challenge.RecoveryGrantConsumedAtUtc is null)
            {
                challenge.RecoveryGrantConsumedAtUtc = now;
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (Exception exception) when (DatabaseCollision(exception))
        {
            // Cancellation is privacy-safe and idempotent. A concurrent reset/cancel
            // has already made this browser transaction unusable.
        }
    }

    private Task<bool> ValidBinding(DeviceSessionIdentity identity, CancellationToken ct) =>
        db.IdentityBindings.AsNoTracking().AnyAsync(x => x.Id == identity.IdentityBindingId
            && x.UserId == identity.UserId && x.Version == identity.IdentityVersion && x.IsActive
            && x.Provider == "Firebase" && x.ProjectId == options.FirebaseProjectId, ct);

    private async Task<bool> HasAuthoritativeIdentity(Guid userId, CancellationToken ct) =>
        await db.IdentityBindings.AsNoTracking().CountAsync(x => x.UserId == userId && x.IsActive
            && x.Provider == "Firebase" && x.ProjectId == options.FirebaseProjectId, ct) == 1
        && (await db.AuthIdentifiers.AsNoTracking().AnyAsync(x => x.UserId == userId
            && x.Kind == "Email" && x.IsVerified, ct)
            || await db.AuthIdentifiers.AsNoTracking().AnyAsync(x => x.UserId == userId
                && x.Kind == "Phone" && x.IsVerified, ct)
            && await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == userId
                && x.Role == ActorRole.Cashier && x.IsActive && x.CanCheckout, ct));

    private static string NormalizePhone(string phone)
    {
        try { return PhoneNumberNormalizer.Normalize(phone); }
        catch (ApplicationFailure) { throw new ApplicationFailure(FailureKind.Validation, "Enter a valid phone number."); }
    }

    internal static string HashRecoveryGrant(string grant) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(grant))).ToLowerInvariant();

    private static AuditEvent Audit(string eventType, Guid userId, DateTime at) =>
        new(Guid.NewGuid(), eventType, userId, null, null, null, Guid.NewGuid(), at, "account-security");

    private static ApplicationFailure PhoneCollision() =>
        new(FailureKind.Validation, "This phone number cannot be added to your account.");

    private static bool DatabaseCollision(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is PostgresException postgres && postgres.SqlState is
                PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation)
                return true;
        return error is DbUpdateConcurrencyException;
    }
}

public static class PasswordCredentialHasher
{
    public const string Algorithm = "PBKDF2-SHA256";
    public const int Version = 1;
    public const int Iterations = 600_000;
    public const int MaximumLength = 128;
    private const int MinimumLength = 12;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private static readonly byte[] DummySalt = SHA256.HashData("Weymela-V3-password-dummy"u8)[..SaltBytes];
    private static readonly byte[] DummyHash = Derive("not-a-user-password", DummySalt, Iterations);

    public static void ValidateNew(string password, string confirmPassword)
    {
        if (password != confirmPassword)
            throw new ApplicationFailure(FailureKind.Validation, "Passwords don't match. Try again.");
        if (password.Length is < MinimumLength or > MaximumLength || password.Any(char.IsControl))
            throw new ApplicationFailure(FailureKind.Validation, "Use at least 12 characters for your password.");
    }

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, Iterations);
        return $"v{Version}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, PasswordCredentialRecord credential)
    {
        if (credential.Algorithm != Algorithm || credential.HashVersion != Version
            || credential.WorkFactor < 100_000 || credential.WorkFactor > 2_000_000) return false;
        try
        {
            var parts = credential.PasswordHash.Split('$');
            if (parts.Length != 3 || parts[0] != $"v{credential.HashVersion}") return false;
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            if (salt.Length != SaltBytes || expected.Length != HashBytes) return false;
            return CryptographicOperations.FixedTimeEquals(expected,
                Derive(password, salt, credential.WorkFactor));
        }
        catch (FormatException) { return false; }
    }

    public static void VerifyDummy(string password) =>
        CryptographicOperations.FixedTimeEquals(DummyHash, Derive(password, DummySalt, Iterations));

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations,
            HashAlgorithmName.SHA256, HashBytes);
}
