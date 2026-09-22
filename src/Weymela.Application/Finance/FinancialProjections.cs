using Weymela.Domain;
namespace Weymela.Application;

public sealed record AdminCreatorFinance(Guid CreatorId, Guid AllocationId, Money StartingBudget, Money Used, Money Remaining,
    long BaselineViews, long LatestVerifiedViews, long CampaignVerifiedViews, long RewardedViews, Money ViewEarnings,
    int VerifiedSales, Money SaleCommission, Money CustomerCashback, Money PlatformRevenue, string ParticipationStatus);
public sealed record AdminCampaignFinance(Guid PromotionId, Guid BusinessId, string Campaign, Money TotalBudget,
    Money Assigned, Money Unassigned, Money Used, Money Remaining, IReadOnlyList<AdminCreatorFinance> Creators);
public sealed record BusinessCreatorBudget(PublicCreator Creator, Money StartingBudget, Money Used, Money BudgetRemaining);
public sealed record BusinessCampaignFinance(Guid PromotionId, string Campaign, Money CampaignBudget, Money AssignedToCreators,
    Money AvailableCampaignBudget, Money Used, Money Remaining, int ViewsPerReward, Money BusinessViewCharge,
    decimal TotalSaleCostPercent, IReadOnlyList<BusinessCreatorBudget> Creators);
public sealed record CreatorCampaignFinance(Guid PromotionId, string Campaign, PromotionType Type, Money YourBudget, Money BudgetRemaining,
    long VerifiedViews, Money ViewEarnings, Money SaleCommissionEarnings, string Status);
public sealed record CustomerOffer(Guid OfferId, string Source, string Offer, PublicBusiness Business,
    PublicCreator? Creator, decimal BenefitPercent, string? Slogan = null, string? Location = null)
{
    // Compatibility for the existing View & Sale projection. UGC Customer Offers expose BenefitPercent.
    public decimal CashbackPercent => BenefitPercent;
}
public sealed record CustomerTransaction(string Source, string Offer, string Business, string? Creator,
    Money PurchaseAmount, Money? CustomerPaidAmount, Money? CashbackEarned, Money? DiscountReceived,
    DateTime PurchasedAtUtc);
public sealed record CustomerPayoutHistory(Money Amount, string Status, DateTime EligibleAtUtc,
    DateTime? PaidAtUtc);
public sealed record CustomerCashbackSummary(Money AvailableCashback, Money MinimumCashOut,
    Money RemainingToCashOut, bool Eligible, string Status, IReadOnlyList<CustomerPayoutHistory> PayoutHistory);
public interface IAdminFinancialQueries { Task<AdminCampaignFinance> CampaignAsync(Actor actor, Guid campaignId, CancellationToken ct); }
public sealed record PlatformSettlementInfo(Guid Id, Money Amount, string Reference, DateTime SettledAtUtc, Guid? SettledBy);
public sealed record PlatformRevenueBreakdown(string Source, Money Accrued);
public sealed record PlatformSettlementSummary(Money Accrued, Money Settled, Money Unsettled,
    IReadOnlyList<PlatformSettlementInfo> History, IReadOnlyList<PlatformRevenueBreakdown> Breakdown);
