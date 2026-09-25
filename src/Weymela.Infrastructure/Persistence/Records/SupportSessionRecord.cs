using Weymela.Application;

namespace Weymela.Infrastructure.Persistence.Records;

/// <summary>
/// Server-controlled, read-only support context. The browser receives only the
/// opaque cookie value; the value itself is never persisted.
/// </summary>
public sealed class SupportSessionRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid RealActorUserId { get; init; }
    public Guid ViewedUserId { get; init; }
    public ActorRole ViewedRole { get; init; }
    public Guid? ViewedBusinessId { get; init; }
    public Guid? ViewedCreatorId { get; init; }
    public Guid? ViewedCustomerId { get; init; }
    public string SessionIdentifierHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? EndedAtUtc { get; set; }
    public long Version { get; set; } = 1;
}
