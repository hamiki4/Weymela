using System.Security.Cryptography;
using System.Text;
using Weymela.Application;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

/// <summary>
/// Pure policy calculations and record factories for a later HTTP/session slice.
/// This class performs no authentication, cookie issuance, profile selection or I/O.
/// </summary>
public static class DeviceAccessPolicy
{
    public static readonly TimeSpan IdleLock = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan AuthorizedDeviceLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan FailedAttemptCooldown = TimeSpan.FromMinutes(15);
    public const int CooldownAttemptThreshold = 5;
    public const int RecoveryAttemptThreshold = 10;

    public static DateTime DeviceExpiresAt(DateTime enrolledAtUtc) => RequireUtc(enrolledAtUtc).Add(AuthorizedDeviceLifetime);
    public static DateTime SessionExpiresAt(DateTime createdAtUtc) => RequireUtc(createdAtUtc).Add(SessionLifetime);
    public static DateTime IdleDeadline(DateTime lastActivityAtUtc) => RequireUtc(lastActivityAtUtc).Add(IdleLock);
    public static bool IsIdleLocked(DateTime lastActivityAtUtc, DateTime nowUtc) => RequireUtc(nowUtc) >= IdleDeadline(lastActivityAtUtc);
    public static bool IsDeviceExpired(DateTime expiresAtUtc, DateTime nowUtc) => RequireUtc(nowUtc) >= RequireUtc(expiresAtUtc);
    public static bool IsSessionExpired(DateTime expiresAtUtc, DateTime nowUtc) => RequireUtc(nowUtc) >= RequireUtc(expiresAtUtc);

    public static void RegisterFailedPin(AuthorizedDeviceRecord device, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(device);
        nowUtc = RequireUtc(nowUtc);
        if (device.RequiresRecovery || device.FailedAttempts >= RecoveryAttemptThreshold)
        {
            device.FailedAttempts = RecoveryAttemptThreshold;
            device.RequiresRecovery = true;
            return;
        }
        device.FailedAttempts++;
        if (device.FailedAttempts == CooldownAttemptThreshold)
            device.LockedUntilUtc = nowUtc.Add(FailedAttemptCooldown);
        if (device.FailedAttempts >= RecoveryAttemptThreshold)
        {
            device.FailedAttempts = RecoveryAttemptThreshold;
            device.RequiresRecovery = true;
            device.LockedUntilUtc = null;
        }
    }

    public static bool CanAttemptPin(AuthorizedDeviceRecord device, DateTime nowUtc) =>
        !device.RequiresRecovery && (device.LockedUntilUtc is null || RequireUtc(nowUtc) >= device.LockedUntilUtc.Value);

    public static void ResetFailuresAfterVerifiedPin(AuthorizedDeviceRecord device)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.FailedAttempts = 0;
        device.LockedUntilUtc = null;
        device.RequiresRecovery = false;
    }

    public static AuthorizedDeviceRecord NewDevice(Guid userId, string credentialDigest, string pinVerifier, DateTime enrolledAtUtc)
    {
        if (userId == Guid.Empty || !IsSha256Digest(credentialDigest)
            || string.IsNullOrEmpty(pinVerifier) || !pinVerifier.StartsWith("pin-v1$", StringComparison.Ordinal))
            throw new ApplicationFailure(FailureKind.Validation, "The device enrollment is invalid.");
        enrolledAtUtc = RequireUtc(enrolledAtUtc);
        return new AuthorizedDeviceRecord
        {
            UserId = userId,
            CredentialKind = "BrowserCookie",
            CredentialIdHash = credentialDigest,
            PinVerifier = pinVerifier,
            EnrolledAtUtc = enrolledAtUtc,
            ExpiresAtUtc = DeviceExpiresAt(enrolledAtUtc)
        };
    }

    public static DeviceSessionRecord NewSession(Guid userId, Guid identityBindingId, long identityVersion,
        Guid authorizedDeviceId, string sessionIdentifierDigest, DateTime createdAtUtc)
    {
        if (userId == Guid.Empty || identityBindingId == Guid.Empty || identityVersion < 0 || authorizedDeviceId == Guid.Empty
            || !IsSha256Digest(sessionIdentifierDigest))
            throw new ApplicationFailure(FailureKind.Validation, "The device session is invalid.");
        createdAtUtc = RequireUtc(createdAtUtc);
        return new DeviceSessionRecord
        {
            UserId = userId,
            IdentityBindingId = identityBindingId,
            IdentityVersion = identityVersion,
            AuthorizedDeviceId = authorizedDeviceId,
            SessionIdentifierHash = sessionIdentifierDigest,
            CreatedAtUtc = createdAtUtc,
            LastActivityAtUtc = createdAtUtc,
            ExpiresAtUtc = SessionExpiresAt(createdAtUtc),
            Generation = 1
        };
    }

    private static bool IsSha256Digest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static DateTime RequireUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : throw new ArgumentException("Security timestamps must use UTC.");
}

/// <summary>A raw credential exists only in memory; persistence receives Digest.</summary>
public sealed class OpaqueDeviceCredential
{
    private OpaqueDeviceCredential(string value, string digest) => (Value, Digest) = (value, digest);

    public string Value { get; }
    public string Digest { get; }

    public static OpaqueDeviceCredential Create()
    {
        var value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        return new OpaqueDeviceCredential(value, Hash(value));
    }

    public static bool Matches(string candidate, string digest)
    {
        if (candidate.Length != 64 || digest.Length != 64) return false;
        var actual = Encoding.ASCII.GetBytes(Hash(candidate));
        var expected = Encoding.ASCII.GetBytes(digest.ToUpperInvariant());
        try { return CryptographicOperations.FixedTimeEquals(actual, expected); }
        finally { CryptographicOperations.ZeroMemory(actual); CryptographicOperations.ZeroMemory(expected); }
    }

    public static bool TryDigest(string? candidate, out string digest)
    {
        digest = "";
        if (candidate is not { Length: 64 } || candidate.Any(c => !Uri.IsHexDigit(c))) return false;
        digest = Hash(candidate);
        return true;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));

    public override string ToString() => "[REDACTED DEVICE CREDENTIAL]";
}
