using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;

namespace Weymela.Infrastructure.Finance;

internal sealed class FinancialOperation(WeymelaDbContext db)
{
    public async Task<string?> Replay(Actor actor, string operation, string key, string fingerprint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200) throw new ApplicationFailure(FailureKind.Validation, "A valid idempotency key is required.");
        var prior = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.ActorId == actor.UserId && x.OperationType == operation && x.Key == key, ct);
        if (prior is null) return null;
        if (prior.RequestFingerprint != fingerprint) throw new ApplicationFailure(FailureKind.IdempotencyConflict, "Conflicting request fingerprint.");
        return prior.ResultReference;
    }
    public void Remember(Actor actor, string operation, string key, string fingerprint, string result, DateTime at) =>
        db.IdempotencyRecords.Add(new(actor.UserId, operation, key, fingerprint, result, at));

    public void Event(string type, object data, DateTime at) =>
        db.OutboxMessages.Add(new() { EventType = type, Payload = JsonSerializer.Serialize(data), OccurredAtUtc = at });

    public void Audit(Actor actor, string type, Guid correlation, DateTime at, Guid? promotion = null, Guid? creator = null)
    {
        db.AuditEvents.Add(new(Guid.NewGuid(), type, actor.UserId, actor.BusinessId, promotion, creator, correlation, at, ""));
    }

    public async Task EmitEligibility(PayoutBeneficiary kind, Guid subject, Money before, Money after, DateTime at, CancellationToken ct)
    {
        var config = await new FinancialConfigurationResolver(db).EffectiveAsync(at, ct);
        var threshold = kind == PayoutBeneficiary.Creator ? config.CreatorPayoutThreshold : config.CustomerPayoutThreshold;
        if (before.Amount < threshold.Amount && after.Amount >= threshold.Amount)
            Event(kind == PayoutBeneficiary.Creator ? nameof(CreatorPayoutEligible) : nameof(CustomerPayoutEligible),
                new { BeneficiaryId = subject, Threshold = threshold, ConfigurationVersionId = config.Id, OccurredAtUtc = at }, at);
    }
}
