using System.Text.Json.Serialization;
using Weymela.Application.Operations;
using Weymela.Domain;

namespace Weymela.Application.Web;

public sealed record CreatorRequestCard(Guid Id,Guid CampaignId,string Campaign,string Business,string Type,string Status,DateTime AppliedAtUtc);

// Public profiles are adapter-provided, not copies of private authentication records.
public sealed record BusinessCard(Guid Id, string DisplayName, string Region, string? DirectionsUrl);
public sealed record CustomerCard(Guid Id, string DisplayName, string PublicId);
public sealed record CreatorCard(Guid Id, string DisplayName, string PublicId, string Region, string Category,
    long VerifiedFollowers, long VerifiedViews, bool SocialVerified, string? PortfolioUrl);
public interface IWorkspaceDirectory : IPublicIdentityDirectory
{
    Task<BusinessCard> BusinessCardAsync(Guid id, CancellationToken ct);
    Task<CustomerOfferBusiness> CustomerOfferBusinessAsync(Guid id, CancellationToken ct);
    Task<CreatorCard> CreatorCardAsync(Guid id, CancellationToken ct);
    Task<CustomerCard> CustomerCardAsync(Guid id, CancellationToken ct);
}
// A session has one active profile, while the account may have several approved memberships.
// The profile list is deliberately limited to public/workspace-safe identity data.
public sealed record SessionProfile(string Role, Guid SubjectId, Guid? BusinessId, string DisplayName, string PublicId, bool CanCheckout);
public sealed record SessionUser(string Role, string DisplayName, string PublicId, bool DevelopmentMode, bool CanCheckout,
    IReadOnlyList<SessionProfile>? Profiles = null, string? ActiveProfileKey = null);
public sealed record ActivityItem(Guid Id, string Title, DateTime AtUtc, string Reference);
public sealed record WalletMovement(Guid Id, string Label, decimal Amount, DateTime AtUtc, string Reference);
public sealed record WalletWorkspace(decimal TotalBalance, decimal Available, decimal Reserved, long Version, IReadOnlyList<WalletMovement> History);
public sealed record BusinessHome(BusinessCard Business, WalletWorkspace Wallet, int ActiveCampaigns, int CreatorRequests, int ConfirmedSales);
public sealed record BusinessPrice(string Type, int Views, decimal BusinessPays, decimal SaleCostPercent, decimal? MinimumCampaignBudget);
public sealed record CreatorPrice(string Type, int Views, decimal YouEarn, decimal SaleCommissionPercent);
public sealed record BusinessPricing(IReadOnlyList<BusinessPrice> Rows, DateTime EffectiveFromUtc, int PromotionLiveDurationDays);
public sealed record UgcPricing(decimal MinimumCreatorPayment, decimal PlatformFeePercent,
    decimal? MinimumUgcBudget, decimal? CustomerOfferPlatformSalePercent,
    int FinancialConfigurationVersion, DateTime EffectiveFromUtc);
public sealed record CreatorPricing(IReadOnlyList<CreatorPrice> Rows, decimal MinimumToCashOut, DateTime EffectiveFromUtc);
public sealed record PromotionPlatformView(string Platform, int Approved, int Capacity, int Available);
public sealed record CreatorSocialProfileView(Guid Id, string Platform, string ProfileUrl, long SelfReportedAudience,
    string VerificationStatus, long? VerifiedAudience);
public sealed record CampaignRow(Guid Id, string PublicId, Guid BusinessId, string Business, string Title, string Type,
    decimal CampaignBudget, decimal AssignedToCreators, decimal AvailableCampaignBudget, decimal Used, decimal Remaining,
    int CreatorCount, DateTime StartUtc, DateTime EndUtc, string Status, long Version, int PromotionLiveDurationDays, string? Slogan = null,
    string? Location = null, IReadOnlyList<PromotionPlatformView>? Platforms = null);
public sealed record ApplicantCard(Guid Id, CreatorCard Creator, string Message, string? ContentConcept, string Status, DateTime AppliedAtUtc,
    string? Platform = null);
public sealed record CreatorBudgetCard(Guid Id, CreatorCard Creator, decimal CreatorBudget, decimal Used, decimal BudgetRemaining,
    long Views, int Sales, string Status, long Version, bool CanIncrease);
public sealed record BusinessCampaign(CampaignRow Campaign, string Description, string? Requirements, string? Category, string? Region,
    long? MinimumVerifiedFollowers, BusinessPrice Pricing, IReadOnlyList<ApplicantCard> Applicants,
    IReadOnlyList<CreatorBudgetCard> Creators, IReadOnlyList<ActivityItem> History);
public sealed record CampaignOpportunity(Guid Id, string PublicId, BusinessCard Business, string Title, string Description, string Type,
    string? Requirements, string? Category, string? Region, long? MinimumVerifiedFollowers, DateTime StartUtc, DateTime EndUtc,
    int PromotionLiveDurationDays, CreatorPrice Earnings, string? RequestStatus, string Eligibility, string? Slogan = null, string? Location = null,
    IReadOnlyList<PromotionPlatformView>? Platforms = null, IReadOnlyList<CreatorSocialProfileView>? EligibleSocialProfiles = null,
    int ApprovedCreators = 0, int CreatorCapacity = 0);
public sealed record CreatorCampaignCard(Guid Id, Guid BudgetId, Guid? ParticipationId, string Title, BusinessCard Business, string Type,
    decimal YourBudget, decimal BudgetRemaining, long VerifiedViews, long RewardedViews, decimal ViewEarnings, decimal SaleCommissionEarnings,
    string Status, string ContentStatus, string? Provider, string? ExternalContentId, DateTime StartUtc, DateTime EndUtc, int PromotionLiveDurationDays,
    int? ContentRevisionNumber = null, string? ContentReviewStatus = null, string? ContentFeedback = null,
    DateTime? ContentSubmittedAtUtc = null, DateTime? WentLiveAtUtc = null, DateTime? ExpiresAtUtc = null,
    int? RemainingDays = null);
public sealed record CreatorContentSubmissionStatus(int RevisionNumber, string ReviewStatus,
    DateTime SubmittedAtUtc, string? Feedback);
public sealed record BusinessPromotionContentReviewCard(Guid SubmissionId, string Creator, string Promotion,
    string Provider, string ContentReference, int RevisionNumber, DateTime SubmittedAtUtc,
    string ReviewStatus, string? Feedback, DateTime? ReviewedAtUtc);
public sealed record EarningItem(Guid Id, string Campaign, string Source, decimal Amount, DateTime AtUtc);
public sealed record PayoutItem(Guid Id, string Kind, string Name, decimal Amount, decimal Threshold, string Status,
    DateTime EligibleAtUtc, DateTime? PaidAtUtc, string? Reference);
public sealed record EarningsWorkspace(decimal AvailableEarnings, decimal MinimumToCashOut, decimal AmountNeeded, decimal EligibleAmount,
    IReadOnlyList<EarningItem> History, IReadOnlyList<PayoutItem> PayoutHistory, decimal ViewEarnings = 0,
    decimal SaleEarnings = 0, decimal UgcEarnings = 0);
public sealed record CreatorHome(CreatorCard Creator, int Requests, int ActiveCampaigns, EarningsWorkspace Earnings);
public sealed record AdminHome(int Businesses, int Creators, int ActiveCampaigns, decimal CampaignSpend, decimal CreatorEarnings,
    decimal CustomerCashback, decimal PlatformRevenue, IReadOnlyList<ActivityItem> Activity);
public sealed record OperationsHome(int PendingReviews, int Businesses, int Creators, int Customers,
    int ActiveCampaigns, int PendingCreatorPayouts, int PendingCustomerPayouts);
public sealed record BusinessOversight(BusinessCard Business, string Status, decimal TotalBalance, decimal Available, decimal Reserved,
    int ActiveCampaigns, DateTime? LastDepositUtc);
public sealed record CreatorOversight(CreatorCard Creator, string Status, int ActiveCampaigns, decimal AvailableEarnings, bool PayoutEligible);
public sealed record OperationsBusinessView(BusinessCard Business, string Status, int ActiveCampaigns, DateTime? LastDepositUtc);
public sealed record OperationsCreatorView(CreatorCard Creator, string Status, int ActiveCampaigns, bool PayoutEligible);
public sealed record OperationsCustomerView(CustomerCard Customer, string Status);
public sealed record OperationsCampaignView(Guid Id, string PublicId, Guid BusinessId, string Business, string Title,
    string Type, int CreatorCount, DateTime StartUtc, DateTime EndUtc, string Status, long Version,
    int PromotionLiveDurationDays, string? Slogan = null, string? Location = null);
public sealed record AdminCreatorRow(CreatorCard Creator, decimal CreatorBudget, decimal Used, decimal BudgetRemaining, long BaselineViews,
    long LatestVerifiedViews, long VerifiedViews, long RewardedViews, decimal ViewEarnings, int VerifiedSales, decimal SaleCommission,
    decimal CustomerCashback, decimal PlatformRevenue, string Status);
public sealed record AdminCampaign(CampaignRow Campaign, IReadOnlyList<AdminCreatorRow> Creators, decimal CreatorEarnings,
    decimal CustomerCashback, decimal PlatformRevenue, IReadOnlyList<ActivityItem> History, int FinancialConfigurationVersion);
public sealed record ViewPriceInput(int ViewsPerReward, decimal BusinessPays, decimal CreatorEarns, decimal PlatformKeeps, decimal? MinimumCampaignBudget);
public sealed record UgcSettingsInput(decimal MinimumCreatorPayment, decimal PlatformFeePercent,
    decimal? MinimumUgcBudget, decimal? CustomerOfferPlatformSalePercent = null);
public sealed record FinancialSettingsInput(ViewPriceInput ViewOnly, ViewPriceInput ViewPlusCommission, decimal CreatorCommissionPercent,
    decimal CustomerCashbackPercent, decimal PlatformPercent, decimal CreatorThreshold, decimal CustomerThreshold,
    DateTime? EffectiveFromUtc, UgcSettingsInput? Ugc = null, int PromotionLiveDurationDays = 30);
public sealed record FinancialVersionInfo(Guid Id, int Version, DateTime EffectiveFromUtc, Guid ChangedBy, FinancialSettingsInput Settings);
public sealed record FinancialSettingsWorkspace(FinancialSettingsInput Current, int Version, IReadOnlyList<FinancialVersionInfo> Versions);
public sealed record PayoutQueueRow(Guid SubjectId, Guid? PayoutId, string Name, decimal Available, decimal Threshold, decimal PayAmount,
    DateTime? EligibleSinceUtc, string Status);
public sealed record PayoutWorkspace(IReadOnlyList<PayoutQueueRow> Creators, IReadOnlyList<PayoutQueueRow> Customers,
    decimal PlatformAccrued, decimal PlatformSettled, decimal PlatformUnsettled, IReadOnlyList<PayoutItem> History);
public sealed record OperationsPayoutWorkspace(IReadOnlyList<PayoutQueueRow> Creators,
    IReadOnlyList<PayoutQueueRow> Customers, IReadOnlyList<PayoutItem> History);
public sealed record OperationsUgcView(Guid Id, Guid BusinessId, string Business, string Title, string Status,
    int CreatorsNeeded, int ApprovedCreators, DateTime DueDateUtc, string? Location, int CurrentRevision,
    string? CustomerOfferStatus = null);
public sealed record CustomerOfferBusiness(string DisplayName, string? DirectionsUrl,
    decimal? Latitude = null, decimal? Longitude = null);
public sealed record CustomerOfferCreator(string DisplayName);
public sealed record CustomerOfferCard(Guid Id, string Source, string Offer, CustomerOfferBusiness Business,
    CustomerOfferCreator? Creator, decimal BenefitPercent, string? WatchUrl, string? Slogan = null, string? Location = null,
    int? RemainingDays = null);
public sealed record QrResponse(Guid Id, string? Token, DateTime ExpiresAtUtc, bool Replayed);
public sealed record CheckoutOffer(Guid SessionId, string Offer, CustomerOfferBusiness Business, CustomerOfferCreator? Creator,
    string Customer, DateTime ExpiresAtUtc, string Source, decimal? CustomerDiscountPercent);
public sealed record CheckoutSaleRow(Guid Id, string Offer, string Source, decimal PurchaseAmount,
    decimal CustomerDiscount, decimal CustomerPays, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? PlatformFee, DateTime CreatedAtUtc,
    string? Cashier = null);

public sealed record CashierView(Guid Id, string Name, string MaskedPhone, string Status,
    DateTime CreatedAtUtc, DateTime? ActivatedAtUtc);
public sealed record CashierCreated(CashierView Cashier, string ActivationCode);
public sealed record CashierActivationResult(FirebaseCustomTokenResult Token);
public sealed record CreateCashierInput(string Name, string Phone);
public sealed record CashierActivationInput(string Phone, string ActivationCode);
public sealed record ManualCheckoutLookupInput(string CreatorId, string CustomerPhone, Guid? OfferId = null);
public sealed record ManualCheckoutChoice(Guid Id, string Source, string Label, decimal? BenefitPercent);
public sealed record ManualCheckoutResolution(IReadOnlyList<ManualCheckoutChoice> Offers);
public sealed record ManualCheckoutConfirmInput(string CreatorId, string CustomerPhone, Guid OfferId, decimal PurchaseAmount);

public sealed record PromotionPlatformInput(string Platform, int Capacity);
public sealed record CreateCampaignInput(string Title, string Description, string Type, decimal CampaignBudget, string? Requirements,
    string? Category, string? Region, long? MinimumVerifiedFollowers, DateTime StartUtc, DateTime EndUtc,
    string? Slogan = null, string? Location = null, IReadOnlyList<string>? Resources = null,
    IReadOnlyList<PromotionPlatformInput>? Platforms = null);
public sealed record DepositInput(decimal Amount, long ExpectedVersion);
public sealed record FundingInput(long CampaignVersion, long WalletVersion);
public sealed record VersionInput(long Version);
public sealed record PromotionPresentationInput(string Description, string? Slogan, string? Location,
    IReadOnlyList<string>? Resources, long Version);
public sealed record BudgetInput(decimal Amount, long Version);
public sealed record JoinInput(string? Message, string? ContentConcept, string? Platform = null, Guid? CreatorSocialProfileId = null);
public sealed record ContentInput(string Provider, string ExternalContentId);
public sealed record PromotionContentReviewInput(string Action, string? Feedback);
public sealed record TokenInput(string Token);
public sealed record CheckoutInput(string Token, decimal PurchaseAmount);
public sealed record ConfirmPaymentInput(string Reference);
public sealed record SettlementInput(decimal Amount, string Reference);

public sealed record UgcPlatformRequirementInput(string Platform, string Format, long? MinimumAudience);
public sealed record CreateUgcInput(string Title, string? Slogan, string ContentType, string Instructions,
    IReadOnlyList<string>? Resources, string? Location, DateTime DueDateUtc, bool ProductProvided,
    bool CreatorMustPurchase, string? UsageRights, decimal CreatorPayment, int CreatorsNeeded,
    IReadOnlyList<UgcPlatformRequirementInput>? PlatformRequirements,
    bool CustomerOfferEnabled = false, decimal? CustomerDiscountPercent = null,
    decimal? CustomerOfferFundedAllocation = null, string? CustomerFacingSlogan = null,
    DateTime? CustomerOfferStartsAtUtc = null, DateTime? CustomerOfferEndsAtUtc = null);
public sealed record UgcReviewInput(string? Reason);
public sealed record UgcSubmissionInput(string SubmissionUrl);
public sealed record UgcRevisionInput(string? Slogan, string Instructions, IReadOnlyList<string>? Resources,
    string? Location, string? UsageRights, bool IsMaterial, string? Title = null,
    string? ContentType = null, DateTime? DueDateUtc = null, bool? ProductProvided = null,
    bool? CreatorMustPurchase = null, decimal? CreatorPayment = null, int? CreatorsNeeded = null,
    IReadOnlyList<UgcPlatformRequirementInput>? PlatformRequirements = null,
    bool? CustomerOfferEnabled = null, decimal? CustomerDiscountPercent = null,
    decimal? CustomerOfferFundedAllocation = null, string? CustomerFacingSlogan = null,
    DateTime? CustomerOfferStartsAtUtc = null, DateTime? CustomerOfferEndsAtUtc = null);
public sealed record UgcPlatformRequirementView(string Platform, string Format, long? MinimumAudience);
public sealed record UgcCard(Guid Id, Guid BusinessId, string Business, string Title, string? Slogan,
    string ContentType, string Status, decimal CreatorPayment, int CreatorsNeeded, int ApprovedCreators,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? RequiredFunding,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? ReservedFunding,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? UsedFunding, DateTime DueDateUtc,
    string? Location, IReadOnlyList<UgcPlatformRequirementView> PlatformRequirements, string? RequestStatus, long Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CustomerOfferEnabled = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? CustomerDiscountPercent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? CustomerOfferFundedAllocation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? CustomerOfferRemaining = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CustomerOfferStatus = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CustomerFacingSlogan = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? PlatformFeePercent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? PlatformFee = null);
public sealed record UgcRequestView(Guid Id, Guid OpportunityId, Guid CreatorId, string Creator,
    string Status, DateTime RequestedAtUtc, string? RejectionReason);
public sealed record UgcAssignmentView(Guid Id, Guid OpportunityId, string Opportunity, Guid BusinessId,
    string Business, Guid CreatorId, string Creator, decimal CreatorPayment, string Status,
    int AcceptedRevision, bool RevisionAcceptanceRequired, DateTime DueDateUtc, string Instructions,
    IReadOnlyList<string> Resources, string? Location, IReadOnlyList<UgcPlatformRequirementView> PlatformRequirements,
    string? Feedback, string? SubmissionUrl);
public sealed record UgcRevisionView(int RevisionNumber, bool IsMaterial, DateTime CreatedAtUtc, string SnapshotJson);
public sealed record UgcDetail(UgcCard Opportunity, string Instructions, IReadOnlyList<string> Resources,
    bool ProductProvided, bool CreatorMustPurchase, string? UsageRights, int CurrentRevision,
    IReadOnlyList<UgcRequestView> Requests, IReadOnlyList<UgcAssignmentView> Assignments,
    IReadOnlyList<UgcRevisionView> Revisions);
public sealed record AdminGrantInput(string Email, string Role, string? DisplayName = null);
public sealed record AdminAccountView(Guid UserId, string Name, string Email, string Role, string Status,
    DateTime? GrantedAtUtc, DateTime? LastActivityAtUtc);
