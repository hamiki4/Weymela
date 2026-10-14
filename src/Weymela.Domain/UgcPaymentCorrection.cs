namespace Weymela.Domain;

/// <summary>Immutable, balanced reversal of a premature legacy approval payment.</summary>
public sealed record UgcPaymentCorrection(Guid Id, Guid UgcAssignmentId, Guid CreatorId,
    Guid OriginalEarningEntryId, Guid OriginalJournalId, Guid CorrectionJournalId,
    Money CreatorAmount, Money PlatformFee, Guid AuthorizedBy, string AuthorizationReference,
    DateTime CreatedAtUtc);
