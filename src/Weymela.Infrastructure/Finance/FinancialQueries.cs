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
        return new(summary.Accrued, summary.Settled, summary.Unsettled, history);
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
                if (!await db.CreatorPromotionParticipations.AnyAsync(x => x.CreatorAllocationId == a.Id && x.Status == ParticipationStatus.Active,ct)) continue;
                result.Add(new(a.Id,p.Title,await directory.BusinessAsync(p.BusinessId,ct),await directory.CreatorAsync(a.CreatorId,ct),p.PricingSnapshot.CustomerCashbackPercent));
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<CustomerPurchase>> CustomerHistoryAsync(Actor actor, CancellationToken ct = default)
    {
        await access.EnsureCustomerAsync(actor,ct);
        var sales = await db.VerifiedSales.AsNoTracking().Where(x => x.CustomerId == actor.CustomerId).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var result = new List<CustomerPurchase>();
        foreach(var sale in sales)
        {
            var title = await db.Promotions.Where(x => x.Id == sale.PromotionId).Select(x => x.Title).SingleAsync(ct);
            result.Add(new(sale.Id,title,await directory.BusinessAsync(sale.BusinessId,ct),await directory.CreatorAsync(sale.CreatorId,ct),
                sale.PurchaseAmount,sale.CustomerCashbackAmount,sale.CreatedAtUtc));
        }
        return result;
    }
    private Task<Promotion> Campaign(Guid id, CancellationToken ct) => db.Promotions.AsNoTracking().Include(x => x.Allocations).SingleAsync(x => x.Id == id,ct);
    private static Money Sum(IEnumerable<Money> values) => new(values.Sum(x => x.Amount));
    private static string State(CreatorAllocation a, CreatorPromotionParticipation? p) => a.Status is CreatorAllocationStatus.Completed or CreatorAllocationStatus.Cancelled ? a.Status.ToString() : p?.Status.ToString() ?? "AwaitingContent";
    private static ApplicationFailure Forbidden() => new(FailureKind.Forbidden,"The requested financial data is not available to this actor.");
}
