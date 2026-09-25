using Weymela.Application;

namespace Weymela.Infrastructure.Persistence.Records;

public enum AccountPreauthorizationStatus
{
    Pending,
    Activated,
    Expired,
    Suspended,
    Disabled,
    Cancelled,
    Revoked
}

public enum AccountLifecycleStatus
{
    Pending,
    Active,
    Suspended,
    Disabled,
    Cancelled,
    Revoked,
    Closed
}

/// <summary>
/// The single account-lifecycle transition policy used by administrative
/// account actions. Profile activation and preauthorization cancellation may
/// use the same policy when they change the account-level state.
/// </summary>
public static class AccountLifecyclePolicy
{
    public static bool TryTransition(AccountLifecycleStatus current, string action, out AccountLifecycleStatus desired)
    {
        desired = current;
        return action.Trim().ToLowerInvariant() switch
        {
            "activate" when current == AccountLifecycleStatus.Pending => Set(AccountLifecycleStatus.Active, out desired),
            "cancel" when current == AccountLifecycleStatus.Pending => Set(AccountLifecycleStatus.Cancelled, out desired),
            "suspend" when current == AccountLifecycleStatus.Active => Set(AccountLifecycleStatus.Suspended, out desired),
            "lock" when current == AccountLifecycleStatus.Active => Set(AccountLifecycleStatus.Suspended, out desired),
            "disable" when current is AccountLifecycleStatus.Active or AccountLifecycleStatus.Suspended => Set(AccountLifecycleStatus.Disabled, out desired),
            "deactivate" when current is AccountLifecycleStatus.Active or AccountLifecycleStatus.Suspended => Set(AccountLifecycleStatus.Disabled, out desired),
            "unlock" when current == AccountLifecycleStatus.Suspended => Set(AccountLifecycleStatus.Active, out desired),
            "reactivate" when current is AccountLifecycleStatus.Suspended or AccountLifecycleStatus.Disabled => Set(AccountLifecycleStatus.Active, out desired),
            "close" when current is AccountLifecycleStatus.Active or AccountLifecycleStatus.Suspended or AccountLifecycleStatus.Disabled => Set(AccountLifecycleStatus.Closed, out desired),
            "revoke" when current == AccountLifecycleStatus.Active => Set(AccountLifecycleStatus.Revoked, out desired),
            _ => false
        };
    }

    private static bool Set(AccountLifecycleStatus value, out AccountLifecycleStatus desired)
    {
        desired = value;
        return true;
    }
}

/// <summary>
/// A generalized, one-time account activation request. Only the hash of the
/// activation secret is persisted; credentials are established by the target.
/// </summary>
public sealed class AccountPreauthorizationRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public ActorRole TargetRole { get; init; }
    public string? EmailIdentifierHash { get; init; }
    public string? PhoneIdentifierHash { get; init; }
    public string DisplayName { get; init; } = "";
    public string? PublicId { get; init; }
    public string? Region { get; init; }
    public string? Category { get; init; }
    public string? SubmissionJson { get; init; }
    public AccountPreauthorizationStatus Status { get; set; } = AccountPreauthorizationStatus.Pending;
    public Guid CreatedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public string ActivationSecretHash { get; set; } = "";
    public DateTime ActivationSecretExpiresAtUtc { get; init; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? DisabledAtUtc { get; set; }
    public long Version { get; set; }
}

/// <summary>Account-level administrative state. It never replaces role membership.</summary>
public sealed class AccountLifecycleRecord
{
    public Guid UserId { get; init; }
    public AccountLifecycleStatus Status { get; set; } = AccountLifecycleStatus.Pending;
    public string? Reason { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}

/// <summary>Append-only role/profile lifecycle history for administrative review.</summary>
public sealed record AccountRoleHistoryRecord(
    Guid Id,
    Guid ActorUserId,
    Guid TargetUserId,
    ActorRole TargetRole,
    Guid? TargetSubjectId,
    Guid? BusinessId,
    string Action,
    string? Reason,
    DateTime OccurredAtUtc,
    Guid CorrelationId,
    Guid? ReferenceId = null);
