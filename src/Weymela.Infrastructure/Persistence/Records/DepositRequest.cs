using Weymela.Application.Operations;
using Weymela.Domain;

namespace Weymela.Infrastructure.Persistence.Records;

public sealed class DepositRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BusinessId { get; init; }
    public Guid SubmittedBy { get; init; }
    public Money Amount { get; init; }
    public string Provider { get; init; } = "";
    public string ExternalReference { get; init; } = "";
    public string? ProofReference { get; init; }
    public DepositReviewStatus Status { get; set; } = DepositReviewStatus.Pending;
    public DateTime SubmittedAtUtc { get; init; }
    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedBy { get; set; }
    public string? ConfirmationReference { get; set; }
    public Guid? JournalId { get; set; }
    public long Version { get; set; }
}
