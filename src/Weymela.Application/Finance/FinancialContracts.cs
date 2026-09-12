using System.Text.Json.Serialization;
using Weymela.Domain;

namespace Weymela.Application;

public sealed record VerifiedViewRequest(Guid CreatorId, Guid PromotionId, string Provider, string ExternalContentId);
public sealed record VerifiedViewResult(long Count, string Provider, string ExternalContentId, DateTime VerifiedAtUtc, string EvidenceReference);
public interface IVerifiedViewProvider { Task<VerifiedViewResult> VerifyAsync(VerifiedViewRequest request, CancellationToken ct); }

// Hosts supply authenticated identities/permissions. No claim or UI flag grants checkout on its own.
public interface ICommerceAccessPolicy
{
    Task EnsureCustomerAsync(Actor actor, CancellationToken ct);
    Task EnsureCustomerIdAsync(Guid customerId, CancellationToken ct);
    Task EnsureBusinessAsync(Guid businessId, CancellationToken ct);
    Task EnsureCreatorAsync(Actor actor, Guid creatorId, CancellationToken ct);
    Task EnsureScannerAsync(Actor actor, Guid businessId, CancellationToken ct);
}

public sealed class SensitiveQrToken
{
    [JsonIgnore] public string Value { get; }
    public SensitiveQrToken(string value) { Value = value; }
    public override string ToString() => "[REDACTED QR TOKEN]";
}
public sealed record IssueOfferQrCommand(Actor Actor, Guid CreatorAllocationId, string IdempotencyKey);
public sealed record IssuedOfferQr(Guid SessionId, DateTime ExpiresAtUtc, SensitiveQrToken? Token, bool Replayed);
public sealed record RedeemOfferCommand(Actor Actor, SensitiveQrToken Token, Money PurchaseAmount, string IdempotencyKey);
public sealed record SaleResult(Guid SaleId, Money PurchaseAmount, Money TotalBusinessCharge, DateTime CreatedAtUtc);
public sealed record SafeCheckoutOffer(Guid SessionId, string Campaign, PublicBusiness Business, PublicCreator Creator, DateTime ExpiresAtUtc);
public sealed record PublicBusiness(Guid Id, string DisplayName);
public sealed record PublicCreator(Guid Id, string PublicId, string DisplayName);
public interface IPublicIdentityDirectory
{
    Task<PublicBusiness> BusinessAsync(Guid id, CancellationToken ct);
    Task<PublicCreator> CreatorAsync(Guid id, CancellationToken ct);
}
// A future manual resolver creates this internal binding; it cannot provide percentages or funding accounts.
public sealed record CheckoutBinding(Guid CustomerId, Guid PromotionId, Guid CreatorId, Guid CreatorAllocationId, Guid BusinessId, string SourceReference);
public interface IManualCheckoutResolver
{
    Task<CheckoutBinding> ResolveAsync(Actor scanner, string creatorPublicId, string customerReference, Guid promotionId, CancellationToken ct);
}
public sealed record GoLiveCommand(Actor Actor, Guid AllocationId, string Provider, string ExternalContentId, string IdempotencyKey);
public sealed record RefreshViewsCommand(Actor Actor, Guid ParticipationId, string IdempotencyKey);
public sealed record ViewRewardResult(Guid ParticipationId, long CampaignViews, long RewardedViews, long CarryForward, ParticipationStatus Status);
public sealed record PayoutEligibility(PayoutBeneficiary Beneficiary, Guid BeneficiaryId, Money Available, Money Threshold, Money EligibleAmount, Guid ConfigurationVersionId);
