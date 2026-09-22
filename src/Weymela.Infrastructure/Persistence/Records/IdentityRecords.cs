using Weymela.Application;

namespace Weymela.Infrastructure.Persistence.Records;

// Provisioned only by a trusted, separately authorized identity/account adapter. No public grant endpoint.
public sealed class IdentityBinding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Provider { get; init; } = "Firebase";
    public string ProjectId { get; init; } = "";
    public string ExternalSubject { get; init; } = "";
    public Guid UserId { get; init; }
    public bool IsActive { get; set; }
    public DateTime ValidAfterUtc { get; set; }
    public long Version { get; set; }
}

// Deliberately no phone, email, private address, bank account or provider credential.
public sealed class PublicWorkspaceProfile
{
    public Guid SubjectId { get; init; }
    public ActorRole Role { get; init; }
    public string DisplayName { get; init; } = "";
    public string PublicId { get; init; } = "";
    public string Region { get; init; } = "";
    public string Category { get; init; } = "";
    public long VerifiedFollowers { get; init; }
    public long VerifiedViews { get; init; }
    public bool SocialVerified { get; init; }
    public string? PortfolioUrl { get; init; }
    public string? DirectionsUrl { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
}
