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
public sealed record BusinessPricing(IReadOnlyList<BusinessPrice> Rows, DateTime EffectiveFromUtc);
public sealed record CreatorPricing(IReadOnlyList<CreatorPrice> Rows, decimal MinimumToCashOut, DateTime EffectiveFromUtc);
public sealed record CampaignRow(Guid Id, string PublicId, Guid BusinessId, string Business, string Title, string Type,
    decimal CampaignBudget, decimal AssignedToCreators, decimal AvailableCampaignBudget, decimal Used, decimal Remaining,
    int CreatorCount, DateTime StartUtc, DateTime EndUtc, string Status, long Version);
public sealed record ApplicantCard(Guid Id, CreatorCard Creator, string Message, string? ContentConcept, string Status, DateTime AppliedAtUtc);
public sealed record CreatorBudgetCard(Guid Id, CreatorCard Creator, decimal CreatorBudget, decimal Used, decimal BudgetRemaining,
    long Views, int Sales, string Status, long Version, bool CanIncrease);
public sealed record BusinessCampaign(CampaignRow Campaign, string Description, string? Requirements, string? Category, string? Region,
    long? MinimumVerifiedFollowers, BusinessPrice Pricing, IReadOnlyList<ApplicantCard> Applicants,
    IReadOnlyList<CreatorBudgetCard> Creators, IReadOnlyList<ActivityItem> History);
public sealed record CampaignOpportunity(Guid Id, string PublicId, BusinessCard Business, string Title, string Description, string Type,
    string? Requirements, string? Category, string? Region, long? MinimumVerifiedFollowers, DateTime StartUtc, DateTime EndUtc,
    CreatorPrice Earnings, string? RequestStatus, string Eligibility);
public sealed record CreatorCampaignCard(Guid Id, Guid BudgetId, Guid? ParticipationId, string Title, BusinessCard Business, string Type,
    decimal YourBudget, decimal BudgetRemaining, long VerifiedViews, long RewardedViews, decimal ViewEarnings, decimal SaleCommissionEarnings,
    string Status, string ContentStatus, string? Provider, string? ExternalContentId, DateTime StartUtc, DateTime EndUtc);
public sealed record EarningItem(Guid Id, string Campaign, string Source, decimal Amount, DateTime AtUtc);
public sealed record PayoutItem(Guid Id, string Kind, string Name, decimal Amount, decimal Threshold, string Status,
    DateTime EligibleAtUtc, DateTime? PaidAtUtc, string? Reference);
public sealed record EarningsWorkspace(decimal AvailableEarnings, decimal MinimumToCashOut, decimal AmountNeeded, decimal EligibleAmount,
    IReadOnlyList<EarningItem> History, IReadOnlyList<PayoutItem> PayoutHistory);
public sealed record CreatorHome(CreatorCard Creator, int Requests, int ActiveCampaigns, EarningsWorkspace Earnings);
public sealed record AdminHome(int Businesses, int Creators, int ActiveCampaigns, decimal CampaignSpend, decimal CreatorEarnings,
    decimal CustomerCashback, decimal PlatformRevenue, IReadOnlyList<ActivityItem> Activity);
public sealed record BusinessOversight(BusinessCard Business, string Status, decimal TotalBalance, decimal Available, decimal Reserved,
    int ActiveCampaigns, DateTime? LastDepositUtc);
public sealed record CreatorOversight(CreatorCard Creator, string Status, int ActiveCampaigns, decimal AvailableEarnings, bool PayoutEligible);
public sealed record AdminCreatorRow(CreatorCard Creator, decimal CreatorBudget, decimal Used, decimal BudgetRemaining, long BaselineViews,
    long LatestVerifiedViews, long VerifiedViews, long RewardedViews, decimal ViewEarnings, int VerifiedSales, decimal SaleCommission,
    decimal CustomerCashback, decimal PlatformRevenue, string Status);
public sealed record AdminCampaign(CampaignRow Campaign, IReadOnlyList<AdminCreatorRow> Creators, decimal CreatorEarnings,
    decimal CustomerCashback, decimal PlatformRevenue, IReadOnlyList<ActivityItem> History);
public sealed record ViewPriceInput(int ViewsPerReward, decimal BusinessPays, decimal CreatorEarns, decimal PlatformKeeps, decimal? MinimumCampaignBudget);
public sealed record FinancialSettingsInput(ViewPriceInput ViewOnly, ViewPriceInput ViewPlusCommission, decimal CreatorCommissionPercent,
    decimal CustomerCashbackPercent, decimal PlatformPercent, decimal CreatorThreshold, decimal CustomerThreshold, DateTime? EffectiveFromUtc);
public sealed record FinancialVersionInfo(Guid Id, int Version, DateTime EffectiveFromUtc, Guid ChangedBy, FinancialSettingsInput Settings);
public sealed record FinancialSettingsWorkspace(FinancialSettingsInput Current, int Version, IReadOnlyList<FinancialVersionInfo> Versions);
public sealed record PayoutQueueRow(Guid SubjectId, Guid? PayoutId, string Name, decimal Available, decimal Threshold, decimal PayAmount,
    DateTime? EligibleSinceUtc, string Status);
public sealed record PayoutWorkspace(IReadOnlyList<PayoutQueueRow> Creators, IReadOnlyList<PayoutQueueRow> Customers,
    decimal PlatformAccrued, decimal PlatformSettled, decimal PlatformUnsettled, IReadOnlyList<PayoutItem> History);
public sealed record CustomerOfferCard(Guid Id, Guid CampaignId, string Campaign, BusinessCard Business, CreatorCard Creator, decimal CashbackPercent, string? WatchUrl);
public sealed record QrResponse(Guid Id, string? Token, DateTime ExpiresAtUtc, bool Replayed);
public sealed record CheckoutOffer(Guid SessionId, string Campaign, PublicBusiness Business, PublicCreator Creator, string Customer, DateTime ExpiresAtUtc);

public sealed record CreateCampaignInput(string Title, string Description, string Type, decimal CampaignBudget, string? Requirements,
    string? Category, string? Region, long? MinimumVerifiedFollowers, DateTime StartUtc, DateTime EndUtc);
public sealed record DepositInput(decimal Amount, long ExpectedVersion);
public sealed record FundingInput(long CampaignVersion, long WalletVersion);
public sealed record VersionInput(long Version);
public sealed record BudgetInput(decimal Amount, long Version);
public sealed record JoinInput(string? Message, string? ContentConcept);
public sealed record ContentInput(string Provider, string ExternalContentId);
public sealed record TokenInput(string Token);
public sealed record CheckoutInput(string Token, decimal PurchaseAmount);
public sealed record ConfirmPaymentInput(string Reference);
public sealed record SettlementInput(decimal Amount, string Reference);
