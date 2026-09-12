namespace Weymela.Application.Operations;

// These contracts carry references, never passwords, raw provider tokens or payment proofs.
public sealed record VerifiedIdentity(string Provider, string ProjectId, string Subject, DateTime AuthenticatedAtUtc, DateTime ExpiresAtUtc);
public interface IIdentityTokenVerifier
{
    Task<VerifiedIdentity> VerifyAsync(string sensitiveIdToken, CancellationToken ct);
}

public sealed record ProviderCapabilities(string Name, bool AccountOwnership, bool ContentIdentity, bool VerifiedViews);
public enum ProviderHealthState { Healthy, Unavailable, Disabled }
public sealed record ProviderHealth(string Provider, ProviderHealthState State, string Code, DateTime CheckedAtUtc);
public sealed record VerifiedContentEvidence(Guid CreatorId, string Provider, string ExternalContentId, bool OwnershipVerified,
    long Views, DateTime ObservedAtUtc, string EvidenceReference);
public interface ISocialVerificationAdapter
{
    ProviderCapabilities Capabilities { get; }
    Task<VerifiedContentEvidence> VerifyAsync(VerifiedViewRequest request, CancellationToken ct);
    Task<ProviderHealth> HealthAsync(CancellationToken ct);
}

public enum DepositReviewStatus { Pending, Approved, Rejected }
public sealed record DepositSubmission(decimal Amount, string ExternalReference, string? ProofReference);
public sealed record DepositReview(bool Approve, long ExpectedVersion, string ConfirmationReference);
public sealed record DepositReceipt(Guid Id, decimal Amount, string Status, DateTime SubmittedAtUtc, DateTime? ReviewedAtUtc);
public sealed record DepositEvidence(string Provider, string ExternalReference, string? ProofReference, bool RequiresAdminApproval);
public interface IDepositProvider
{
    string Name { get; }
    Task<DepositEvidence> SubmitAsync(Guid businessId, DepositSubmission submission, string requestKey, CancellationToken ct);
}

public sealed record NotificationDto(Guid Id, string Title, string Message, string Route, DateTime CreatedAtUtc, DateTime? ReadAtUtc);
public sealed record NotificationPage(IReadOnlyList<NotificationDto> Items, int UnreadCount);
public sealed record PushNotification(Guid NotificationId, Guid UserId, string Role, string Title, string Route);
public sealed record PushDeliveryResult(bool Delivered, bool Retryable, string Code);
public interface INotificationPushProvider
{
    bool Enabled { get; }
    Task<PushDeliveryResult> SendAsync(PushNotification notification, CancellationToken ct);
}

public sealed record LegalDocumentDto(Guid Id, string Type, string Version, DateTime EffectiveFromUtc, bool Accepted);
