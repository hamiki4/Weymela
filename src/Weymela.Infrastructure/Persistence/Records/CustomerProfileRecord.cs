namespace Weymela.Infrastructure.Persistence.Records;

// Private durable Customer profile state. Authentication identifiers and
// credentials deliberately remain in the identity subsystem, while the
// separately projected PublicWorkspaceProfile contains shareable fields.
public sealed class CustomerProfileRecord
{
    public Guid CustomerId { get; init; }
    public Guid UserId { get; init; }
    public string PreferredName { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}
