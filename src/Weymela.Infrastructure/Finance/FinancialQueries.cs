using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Repositories;

namespace Weymela.Infrastructure.Finance;

public sealed class FinancialQueries(WeymelaDbContext db, ICommerceAccessPolicy access, IPublicIdentityDirectory directory, TimeProvider clock) : IAdminFinancialQueries
{
    public async Task<PlatformSettlementSummary> PlatformAsync(Actor actor, CancellationToken ct = default)
    {
        PayoutService.DemandAdmin(actor);
        var summary = await new PlatformRevenueRepository(db).SummaryAsync(ct);
        var history = await db.PlatformSettlements.AsNoTracking().OrderByDescending(x => x.SettledAtUtc)
            .Select(x => new PlatformSettlementInfo(x.Id, x.Amount, x.Reference, x.SettledAtUtc, EF.Property<Guid?>(x, "SettledBy"))).ToListAsync(ct);
        var sourceRows = await db.PlatformRevenueEntries.AsNoTracking().Select(x => new { x.Source, x.Amount }).ToListAsync(ct);
        string SourceLabel(PlatformRevenueSource source) => source switch
        {
            PlatformRevenueSource.ViewRewardPlatformShare => "View rewards",
            PlatformRevenueSource.SalePlatformShare => "Sales",
            PlatformRevenueSource.UgcFee => "UGC fees",
            PlatformRevenueSource.UgcCustomerOfferSaleFee => "UGC Customer Offer Sales",
            _ => "Authorized adjustments"
        };
        var breakdown = sourceRows.GroupBy(x => x.Source)
            .Select(x => new PlatformRevenueBreakdown(SourceLabel(x.Key), new Money(x.Sum(row => row.Amount.Amount))))
            .OrderBy(x => x.Source).ToArray();
        return new(summary.Accrued, summary.Settled, summary.Unsettled, history, breakdown);
    }
    public async Task<AdminCampaignFinance> CampaignAsync(Actor actor, Guid campaignId, CancellationToken ct = default)
    {
        PayoutService.DemandAdmin(actor);
        var p = await Campaign(campaignId, ct);
        var result = new List<AdminCreatorFinance>();
        foreach (var a in p.Allocations)
        {
            var participation = await db.CreatorPromotionParticipations.AsNoTracking().SingleOrDefaultAsync(x => x.CreatorAllocationId == a.Id, ct);
            var earnings = await db.CreatorEarningEntries.AsNoTracking().Where(x => x.PromotionId == p.Id && x.CreatorId == a.CreatorId).ToListAsync(ct);
            var sales = await db.VerifiedSales.AsNoTracking().Where(x => x.CreatorAllocationId == a.Id).ToListAsync(ct);
            var platform = await (from r in db.PlatformRevenueEntries.AsNoTracking()
                join j in db.FinancialJournals on EF.Property<Guid>(r, "JournalId") equals j.Id
                where r.PromotionId == p.Id && EF.Property<Guid?>(j, "CreatorId") == a.CreatorId select r.Amount).ToListAsync(ct);
            result.Add(new(a.CreatorId,a.Id,a.OriginalAllocation,a.UsedAmount,a.RemainingAmount,
                participation?.BaselineViews ?? 0, participation?.LatestVerifiedViews ?? 0, participation?.CampaignVerifiedViews ?? 0,
                participation?.RewardedViewCount ?? 0, Sum(earnings.Where(x => x.Source == EarningSource.ViewReward).Select(x => x.Amount)),
                sales.Count, Sum(earnings.Where(x => x.Source == EarningSource.SaleCommission).Select(x => x.Amount)),
                Sum(sales.Select(x => x.CustomerCashbackAmount)), Sum(platform), State(a, participation)));
        }
        return new(p.Id,p.BusinessId,p.Title,p.TotalBudget,p.AllocatedBudget,p.UnallocatedBudget,p.UsedBudget,p.RemainingBudget,result);
    }

    public async Task<BusinessCampaignFinance> BusinessAsync(Actor actor, Guid campaignId, CancellationToken ct = default)
    {
        var p = await Campaign(campaignId, ct);
        if (actor.Role != ActorRole.Business || actor.BusinessId != p.BusinessId) throw Forbidden();
        var creators = new List<BusinessCreatorBudget>();
        foreach (var a in p.Allocations) creators.Add(new(await directory.CreatorAsync(a.CreatorId,ct),a.OriginalAllocation,a.UsedAmount,a.RemainingAmount));
        return new(p.Id,p.Title,p.TotalBudget,p.AllocatedBudget,p.UnallocatedBudget,p.UsedBudget,p.RemainingBudget,p.PricingSnapshot.ViewsPerReward,
            p.PricingSnapshot.BusinessCharge,p.PromotionType == PromotionType.ViewPlusCommission ? SnapshotPricing.TotalSalePercent(p.PricingSnapshot) : 0,creators);
    }

    public async Task<CreatorCampaignFinance> CreatorAsync(Actor actor, Guid allocationId, CancellationToken ct = default)
    {
        var a = await db.CreatorAllocations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == allocationId, ct) ?? throw Forbidden();
        await access.EnsureCreatorAsync(actor, a.CreatorId, ct);
        var p = await Campaign(a.PromotionId,ct);
        var participation = await db.CreatorPromotionParticipations.AsNoTracking().SingleOrDefaultAsync(x => x.CreatorAllocationId == a.Id,ct);
        var earnings = await db.CreatorEarningEntries.AsNoTracking().Where(x => x.CreatorId == a.CreatorId && x.PromotionId == p.Id).ToListAsync(ct);
        return new(p.Id,p.Title,p.PromotionType,a.OriginalAllocation,a.RemainingAmount,participation?.CampaignVerifiedViews ?? 0,
            Sum(earnings.Where(x => x.Source == EarningSource.ViewReward).Select(x => x.Amount)),
            Sum(earnings.Where(x => x.Source == EarningSource.SaleCommission).Select(x => x.Amount)),State(a,participation));
    }

    public async Task<IReadOnlyList<CustomerOffer>> CustomerOffersAsync(Actor actor, CancellationToken ct = default)
    {
        await access.EnsureCustomerAsync(actor,ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var campaigns = await db.Promotions.AsNoTracking().Include(x => x.Allocations)
            .Where(x => x.PromotionType == PromotionType.ViewPlusCommission && x.Status == PromotionStatus.Active && x.StartDateUtc <= now && x.EndDateUtc > now).ToListAsync(ct);
        var result = new List<CustomerOffer>();
        foreach(var p in campaigns)
        {
            if (!await db.CommercePermissions.AnyAsync(x => x.Role == ActorRole.Business && x.SubjectId == p.BusinessId && x.IsActive,ct)) continue;
            foreach(var a in p.Allocations.Where(x => x.Status == CreatorAllocationStatus.Active && x.ActivatedAtUtc != null && x.RemainingAmount.Amount > 0))
            {
                var participation = await db.CreatorPromotionParticipations.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CreatorAllocationId == a.Id && x.Status == ParticipationStatus.Active,ct);
                if (participation is null || !participation.IsLive(now,p.PromotionLiveDurationDays)) continue;
                result.Add(new(a.Id,"VIEW_AND_SALE_PROMOTION",p.Title,await directory.BusinessAsync(p.BusinessId,ct),
                    await directory.CreatorAsync(a.CreatorId,ct),p.PricingSnapshot.CustomerCashbackPercent,p.Slogan,p.Location,
                    participation.WentLiveAtUtc,participation.ExpiresAtUtc(p.PromotionLiveDurationDays),participation.RemainingDays(now,p.PromotionLiveDurationDays)));
            }
        }
        var ugcOffers = await db.UgcCustomerOffers.AsNoTracking()
            .Where(x => x.Status == UgcCustomerOfferStatus.Active && x.StartsAtUtc <= now && x.EndsAtUtc > now)
            .ToListAsync(ct);
        ugcOffers = ugcOffers.Where(x => x.ReservedFunding.Amount > 0).ToList();
        var opportunityIds = ugcOffers.Select(x => x.UgcOpportunityId).Distinct().ToArray();
        var opportunities = await db.UgcOpportunities.AsNoTracking().Where(x => opportunityIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        foreach (var offer in ugcOffers)
        {
            if (!opportunities.TryGetValue(offer.UgcOpportunityId, out var ugc)
                || !await db.CommercePermissions.AnyAsync(x => x.Role == ActorRole.Business
                    && x.SubjectId == offer.BusinessId && x.IsActive, ct)) continue;
            result.Add(new(offer.Id,"UGC_CUSTOMER_OFFER",offer.CustomerFacingSlogan ?? $"{offer.CustomerDiscountPercent:0.####}% off",
                await directory.BusinessAsync(offer.BusinessId,ct),null,offer.CustomerDiscountPercent,
                offer.CustomerFacingSlogan,ugc.Location));
        }
        return result;
    }

    public async Task<IReadOnlyList<CustomerTransaction>> CustomerTransactionsAsync(Actor actor, CancellationToken ct = default)
    {
        await access.EnsureCustomerAsync(actor,ct);
        var customerId = actor.CustomerId!.Value;
        var result = new List<CustomerTransaction>();
        var sales = await db.VerifiedSales.AsNoTracking().Where(x => x.CustomerId == customerId).ToListAsync(ct);
        foreach (var sale in sales)
        {
            var title = await db.Promotions.Where(x => x.Id == sale.PromotionId).Select(x => x.Title).SingleAsync(ct);
            var business = await directory.BusinessAsync(sale.BusinessId, ct);
            var creator = await directory.CreatorAsync(sale.CreatorId, ct);
            result.Add(new("VIEW_AND_SALE_PROMOTION", title, business.DisplayName, creator.DisplayName,
                sale.PurchaseAmount, null, sale.CustomerCashbackAmount, null, sale.CreatedAtUtc));
        }

        var ugcSales = await db.UgcCustomerOfferSales.AsNoTracking()
            .Where(x => x.CustomerId == customerId).ToListAsync(ct);
        var offerIds = ugcSales.Select(x => x.UgcCustomerOfferId).Distinct().ToArray();
        var offers = await db.UgcCustomerOffers.AsNoTracking().Where(x => offerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        foreach (var sale in ugcSales)
        {
            if (!offers.TryGetValue(sale.UgcCustomerOfferId, out var offer)) continue;
            var business = await directory.BusinessAsync(sale.BusinessId, ct);
            var label = offer.CustomerFacingSlogan ?? $"{offer.CustomerDiscountPercent:0.####}% off";
            result.Add(new("UGC_CUSTOMER_OFFER", label, business.DisplayName, null,
                sale.PurchaseAmount, sale.CustomerPaysAmount, null, sale.CustomerDiscountAmount,
                sale.CreatedAtUtc));
        }

        return result.OrderByDescending(x => x.PurchasedAtUtc).ToArray();
    }

    // Kept as a compatibility alias; both routes use the same normalized projection.
    public Task<IReadOnlyList<CustomerTransaction>> CustomerHistoryAsync(Actor actor, CancellationToken ct = default)
        => CustomerTransactionsAsync(actor, ct);

    public async Task<CustomerCashbackSummary> CustomerCashbackAsync(Actor actor, CancellationToken ct = default)
    {
        await access.EnsureCustomerAsync(actor, ct);
        var customerId = actor.CustomerId!.Value;
        var eligibility = await new PayoutService(db, clock)
            .EligibilityAsync(actor, PayoutBeneficiary.Customer, customerId, ct);
        var payouts = await db.PayoutRecords.AsNoTracking()
            .Where(x => x.Beneficiary == PayoutBeneficiary.Customer && x.CustomerId == customerId)
            .OrderByDescending(x => x.PaidAtUtc ?? x.EligibleAtUtc)
            .ToListAsync(ct);
        var eligible = eligibility.EligibleAmount.Amount > 0;
        var hasPreparedPayout = payouts.Any(x => x.Status == PayoutStatus.Eligible);
        var status = hasPreparedPayout ? "PayoutPrepared" : eligible ? "Eligible" : "BelowThreshold";
        var remaining = Math.Max(eligibility.Threshold.Amount - eligibility.Available.Amount, 0m);
        return new(eligibility.Available, eligibility.Threshold,
            new Money(remaining, eligibility.Threshold.Currency), eligible, status,
            payouts.Select(x => new CustomerPayoutHistory(x.Amount, x.Status.ToString(),
                x.EligibleAtUtc, x.PaidAtUtc)).ToArray());
    }
    private Task<Promotion> Campaign(Guid id, CancellationToken ct) => db.Promotions.AsNoTracking().Include(x => x.Allocations).SingleAsync(x => x.Id == id,ct);
    private static Money Sum(IEnumerable<Money> values) => new(values.Sum(x => x.Amount));
    private static string State(CreatorAllocation a, CreatorPromotionParticipation? p) => a.Status is CreatorAllocationStatus.Completed or CreatorAllocationStatus.Cancelled ? a.Status.ToString() : p?.Status.ToString() ?? "AwaitingContent";
    private static ApplicationFailure Forbidden() => new(FailureKind.Forbidden,"The requested financial data is not available to this actor.");
}
