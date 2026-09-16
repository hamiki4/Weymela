namespace Weymela.Infrastructure.Persistence.Records;

public sealed class AuthIdentifierRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string Kind { get; init; } = "Email";
    public string IdentifierHash { get; init; } = "";
    // Only the server-side email delivery adapter reads this value. It is never
    // included in role projections or client responses.
    public string? DeliveryAddress { get; init; }
    public bool IsVerified { get; set; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class EmailAuthChallengeRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? UserId { get; init; }
    public string IdentifierHash { get; init; } = "";
    public string? EmailIdentifierHash { get; init; }
    public string? PhoneIdentifierHash { get; init; }
    public string Purpose { get; init; } = "Signup";
    public string CodeHash { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? LastSentAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; init; } = 5;
    public string? RecoveryGrantHash { get; set; }
    public DateTime? RecoveryGrantExpiresAtUtc { get; set; }
    public DateTime? RecoveryGrantConsumedAtUtc { get; set; }
}

/// <summary>Private Internet sign-in credential for one existing V3 identity.</summary>
public sealed class PasswordCredentialRecord
{
    public Guid UserId { get; init; }
    public string PasswordHash { get; set; } = "";
    public string Algorithm { get; set; } = "PBKDF2-SHA256";
    public int HashVersion { get; set; } = 1;
    public int WorkFactor { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime ChangedAtUtc { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public long Version { get; set; }
}

public sealed class AuthorizedDeviceRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string CredentialKind { get; init; } = "Passkey";
    public string CredentialIdHash { get; init; } = "";
    public DateTime EnrolledAtUtc { get; init; }
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public bool RequiresRecovery { get; set; }
    public long Version { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? PinVerifier { get; set; }
}

/// <summary>Server-owned ordinary access state. Never stores a PIN or bearer credential.</summary>
public sealed class DeviceSessionRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public Guid IdentityBindingId { get; init; }
    public long IdentityVersion { get; set; }
    public Guid AuthorizedDeviceId { get; init; }
    public string SessionIdentifierHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime LastActivityAtUtc { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public long Generation { get; set; } = 1;
    public long Version { get; set; }
}
