using Weymela.Domain;

namespace Weymela.Infrastructure.Persistence.Records;

public sealed class PayoutDestination
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public PayoutBeneficiary Beneficiary { get; init; }
    public Guid SubjectId { get; init; }
    public PayoutDestinationMethod Method { get; set; }
    public string Provider { get; set; } = "";
    public string ProtectedAccount { get; set; } = "";
    public string AccountLast4 { get; set; } = "";
    public string LegalName { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}

public sealed class PlatformReceivingDestination
{
    public Guid Id { get; init; }
    public string Method { get; set; } = "";
    public string Name { get; set; } = "";
    public string AccountReference { get; set; } = "";
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
    public long Version { get; set; }
}
