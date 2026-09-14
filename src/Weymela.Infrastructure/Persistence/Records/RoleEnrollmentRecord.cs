using Weymela.Application;
using Weymela.Application.Operations;

namespace Weymela.Infrastructure.Persistence.Records;

public sealed class RoleEnrollmentRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public ActorRole RequestedRole { get; init; }
    public Guid? ProposedBusinessId { get; init; }
    public string SubmissionJson { get; init; } = "{}";
    public RoleEnrollmentStatus Status { get; set; } = RoleEnrollmentStatus.Pending;
    public DateTime SubmittedAtUtc { get; init; }
    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedBy { get; set; }
    public string? DecisionReason { get; set; }
    public string IdempotencyKey { get; init; } = "";
    public long Version { get; set; }
}
