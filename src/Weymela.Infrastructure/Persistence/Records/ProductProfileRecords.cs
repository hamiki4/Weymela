using Weymela.Domain;
using Weymela.Application;

namespace Weymela.Infrastructure.Persistence.Records;

public sealed class CreatorSocialProfileRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CreatorId { get; init; }
    public CreatorPlatform Platform { get; init; }
    public string ProfileUrl { get; set; } = "";
    public long SelfReportedAudience { get; set; }
    public string VerificationStatus { get; set; } = "Unverified";
    public long? VerifiedAudience { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}

public sealed class AdminGrantRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string DisplayName { get; set; } = "";
    public ActorRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid GrantedByUserId { get; init; }
    public DateTime GrantedAtUtc { get; init; }
    public Guid? RevokedByUserId { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime? LastActivityAtUtc { get; set; }
    public long Version { get; set; }
}
