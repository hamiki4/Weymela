using Weymela.Domain;

namespace Weymela.Infrastructure.Persistence.Records;

// Operational records reference the authoritative journal; none is a second balance engine.
public sealed record PromotionReservation(Guid PromotionId, Guid BusinessId, Money OriginalAmount, Guid JournalId, DateTime CreatedAtUtc);
public sealed record PromotionBudgetEntry(Guid Id, Guid PromotionId, Guid? AllocationId, Money Amount, string Movement, Guid JournalId, DateTime CreatedAtUtc);
public sealed record UgcReservation(Guid UgcOpportunityId, Guid BusinessId, Money OriginalAmount, Guid JournalId, DateTime CreatedAtUtc);
public sealed record UgcBudgetEntry(Guid Id, Guid UgcOpportunityId, Guid? AssignmentId, Money Amount, string Movement, Guid JournalId, DateTime CreatedAtUtc);
public sealed record UgcCustomerOfferReservation(Guid UgcCustomerOfferId, Guid BusinessId, Money OriginalAmount, Guid JournalId, DateTime CreatedAtUtc);
public sealed record UgcCustomerOfferBudgetEntry(Guid Id, Guid UgcCustomerOfferId, Guid? SaleId, Money Amount, string Movement, Guid JournalId, DateTime CreatedAtUtc);
public sealed record WalletEntry(Guid Id, Guid BusinessId, Guid? PromotionId, Money Amount, string Movement, Guid JournalId, DateTime CreatedAtUtc,
    Guid? UgcOpportunityId = null, Guid? UgcCustomerOfferId = null);
public sealed record StoredIdempotencyRecord(Guid ActorId, string OperationType, string Key, string RequestFingerprint, string ResultReference, DateTime CreatedAtUtc);
public sealed record AuditEvent(Guid Id, string EventType, Guid ActorId, Guid? BusinessId, Guid? PromotionId, Guid? CreatorId, Guid CorrelationId, DateTime OccurredAtUtc, string Detail,
    Guid? UgcOpportunityId = null, Guid? UgcCustomerOfferId = null);
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string EventType { get; init; } = "";
    public string Payload { get; init; } = "";
    public DateTime OccurredAtUtc { get; init; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public Guid? RecipientCursor { get; set; }
    public int FailureCount { get; set; }
}
