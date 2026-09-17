using Weymela.Application;

namespace Weymela.Infrastructure.Persistence.Records;

public sealed class ProductHandoffTransaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string CodeHash { get; init; } = "";
    public Guid UserId { get; init; }
    public Guid IdentityBindingId { get; init; }
    public long IdentityBindingVersion { get; init; }
    public ActorRole Role { get; init; }
    public Guid? ProfileSubjectId { get; init; }
    public Guid? BusinessId { get; init; }
    public string Purpose { get; init; } = "";
    public string Audience { get; init; } = "";
    public string Environment { get; init; } = "";
    public string CallbackId { get; init; } = "";
    public DateTime AuthenticatedAtUtc { get; init; }
    public DateTime IssuedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? ConsumedAtUtc { get; set; }
    public Guid CorrelationId { get; init; }
    public long Version { get; set; }
}
