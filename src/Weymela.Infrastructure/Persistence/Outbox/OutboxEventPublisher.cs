using System.Text.Json;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Persistence.Outbox;

public sealed class OutboxEventPublisher(WeymelaDbContext db) : IEventPublisher
{
    private readonly HashSet<DomainEvent> staged = [];
    public Task PublishAsync(IReadOnlyCollection<DomainEvent> events, CancellationToken ct)
    {
        foreach (var e in events.Where(staged.Add))
        {
            var at = e.GetType().GetProperty("OccurredAtUtc")?.GetValue(e) as DateTime? ?? DateTime.UtcNow;
            db.OutboxMessages.Add(new OutboxMessage { EventType = e.GetType().Name, Payload = JsonSerializer.Serialize(e, e.GetType()), OccurredAtUtc = at });
        }
        return Task.CompletedTask;
    }
}
