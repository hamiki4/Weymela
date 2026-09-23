using Weymela.Application;

namespace Weymela.Infrastructure.Persistence.Records;

public enum CashierPreauthorizationStatus
{
    PendingActivation,
    Active,
    Disabled,
    Revoked
}

/// <summary>
/// Business-owned authorization for a restricted Cashier profile. The raw
/// activation code is never persisted; the phone is the canonical login alias
/// once the preauthorization is activated.
/// </summary>
public sealed class CashierPreauthorization
{
    private CashierPreauthorization() { }

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BusinessId { get; init; }
    public string DisplayName { get; set; } = "";
    public string CanonicalPhone { get; init; } = "";
    public string PhoneIdentifierHash { get; init; } = "";
    public CashierPreauthorizationStatus Status { get; set; } = CashierPreauthorizationStatus.PendingActivation;
    public string ActivationCodeHash { get; set; } = "";
    public DateTime ActivationCodeExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ActivatedAtUtc { get; set; }
    public Guid? UserId { get; set; }
    public int ActivationAttemptCount { get; set; }
    public DateTime? DisabledAtUtc { get; set; }
    public long Version { get; set; }

    public CashierPreauthorization(Guid businessId, string displayName, string canonicalPhone,
        string phoneIdentifierHash, string activationCodeHash, DateTime expiresAtUtc, DateTime now)
    {
        if (businessId == Guid.Empty || string.IsNullOrWhiteSpace(displayName)
            || string.IsNullOrWhiteSpace(canonicalPhone) || string.IsNullOrWhiteSpace(phoneIdentifierHash)
            || string.IsNullOrWhiteSpace(activationCodeHash))
            throw new ArgumentException("Cashier preauthorization is incomplete.");
        BusinessId = businessId;
        DisplayName = displayName.Trim();
        CanonicalPhone = canonicalPhone;
        PhoneIdentifierHash = phoneIdentifierHash;
        ActivationCodeHash = activationCodeHash;
        ActivationCodeExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = now;
    }
}
