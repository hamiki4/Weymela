using Weymela.Application;

namespace Weymela.Infrastructure.Persistence.Records;

public enum PushDeliveryState { NotRequested, Pending, Delivered, Failed }
public sealed class InAppNotification
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public ActorRole Role { get; init; }
    public string SourceKey { get; init; } = "";
    public string EventType { get; init; } = "";
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string Route { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ReadAtUtc { get; set; }
    public PushDeliveryState PushState { get; set; }
    public int PushAttempts { get; set; }
    public DateTime? NextPushAtUtc { get; set; }
    public string? LastPushErrorCode { get; set; }
    public long Version { get; set; }
}

public sealed class WorkerCheckpoint
{
    public string Name { get; init; } = "";
    public Guid? LastEffectiveConfigurationId { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime? LastSuccessAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public long Version { get; set; }
}
