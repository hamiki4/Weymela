using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;

namespace Weymela.Infrastructure.Web;

// Read-only Platform Admin projections. Balances and movements remain owned by the existing journal and domain models.
public sealed partial class WorkspaceQueries
{
    public async Task<AdminWallets> AdminWalletsAsync(Actor actor, CancellationToken ct)
    {
        DemandPlatformAdmin(actor);
        var wallets = await db.BusinessWallets.AsNoTracking().ToListAsync(ct);
        var promotions = await db.Promotions.AsNoTracking().ToListAsync(ct);
        var deposits = await db.DepositRequests.AsNoTracking().Where(x => x.Status == DepositReviewStatus.Pending).ToListAsync(ct);
        var names = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == ActorRole.Business).ToDictionaryAsync(x => x.SubjectId, x => x.DisplayName, ct);
        var permissions = await db.CommercePermissions.AsNoTracking().Where(x => x.Role == ActorRole.Business && x.IsActive).Select(x => x.SubjectId).ToListAsync(ct);
        var active = permissions.ToHashSet();
        var rows = wallets.Select(w => new AdminBusinessWallet(w.BusinessId, names.GetValueOrDefault(w.BusinessId, "Business"),
            w.TotalBalance.Amount, w.AvailableBalance.Amount, w.ReservedBalance.Amount,
            deposits.Where(x => x.BusinessId == w.BusinessId).Sum(x => x.Amount.Amount),
            promotions.Count(x => x.BusinessId == w.BusinessId && x.PromotionType == PromotionType.ViewOnly && x.Status == PromotionStatus.Active),
            promotions.Count(x => x.BusinessId == w.BusinessId && x.PromotionType == PromotionType.ViewPlusCommission && x.Status == PromotionStatus.Active),
            active.Contains(w.BusinessId) ? "Active" : "Inactive")).OrderBy(x => x.Business).ToArray();
        var current = promotions.Where(x => x.Status == PromotionStatus.Active).ToArray();
        var ids = current.Select(x => x.Id).ToArray();
        var views = await db.PromotionViewVerifications.AsNoTracking().Where(x => ids.Contains(x.PromotionId) && !x.IsAnomaly && !x.IsBaseline).ToListAsync(ct);
        var sales = await db.VerifiedSales.AsNoTracking().Where(x => ids.Contains(x.PromotionId) && x.Status == VerifiedSaleStatus.Recorded).ToListAsync(ct);
        var promotionRows = current.Select(x => new AdminPromotionWallet(x.Id, names.GetValueOrDefault(x.BusinessId, "Business"), x.Title,
            PromotionTypeLabel(x.PromotionType), x.TotalBudget.Amount, x.UsedBudget.Amount, x.RemainingBudget.Amount,
            views.Where(v => v.PromotionId == x.Id).Sum(v => Math.Max(0, v.CurrentVerifiedViews - v.PreviousVerifiedViews)),
            sales.Count(s => s.PromotionId == x.Id), PromotionStatusLabel(x.Status))).OrderBy(x => x.Business).ToArray();
        return new(rows, promotionRows);
    }

    public async Task<IReadOnlyList<AdminUgcFinance>> AdminUgcFinanceAsync(Actor actor, CancellationToken ct)
    {
        DemandPlatformAdmin(actor);
        var opportunities = await db.UgcOpportunities.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var ids = opportunities.Select(x => x.Id).ToArray();
        var offers = await db.UgcCustomerOffers.AsNoTracking().Where(x => ids.Contains(x.UgcOpportunityId)).ToDictionaryAsync(x => x.UgcOpportunityId, ct);
        var sales = await db.UgcCustomerOfferSales.AsNoTracking().Where(x => ids.Contains(x.UgcOpportunityId)).ToListAsync(ct);
        var names = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == ActorRole.Business).ToDictionaryAsync(x => x.SubjectId, x => x.DisplayName, ct);
        return opportunities.Select(x =>
        {
            offers.TryGetValue(x.Id, out var offer);
            return new AdminUgcFinance(x.Id, names.GetValueOrDefault(x.BusinessId, "Business"), x.Title,
                offer is null ? "UGC Only" : "UGC + Discount Sale",
                x.RequiredFunding.Amount + (offer?.FundedLimit.Amount ?? 0), x.CreatorPayment.Amount,
                offer?.CustomerDiscountPercent, x.UsedFunding.Amount, offer?.UsedFunding.Amount ?? 0,
                sales.Where(s => s.UgcOpportunityId == x.Id).Sum(s => s.CustomerDiscountAmount.Amount),
                (x.Status is UgcOpportunityStatus.Open or UgcOpportunityStatus.InProgress ? x.RemainingFunding.Amount : 0)
                  + (offer?.Status == UgcCustomerOfferStatus.Active ? offer.RemainingFunding.Amount : 0),
                sales.Count(s => s.UgcOpportunityId == x.Id),
                x.Status.ToString());
        }).ToArray();
    }

    public async Task<AdminReport> AdminReportAsync(Actor actor, DateOnly from, DateOnly to, CancellationToken ct)
    {
        DemandPlatformAdmin(actor);
        if (to < from || to == DateOnly.MaxValue || to.DayNumber - from.DayNumber > 3660)
            throw new ApplicationFailure(FailureKind.Validation, "Choose a valid report range of ten years or less.");
        var start = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var wallets = await db.BusinessWallets.AsNoTracking().ToListAsync(ct);
        var entries = await db.WalletEntries.AsNoTracking().Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end).ToListAsync(ct);
        var promotionEntries = await db.PromotionBudgetEntries.AsNoTracking().Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end).ToListAsync(ct);
        var ugcEntries = await db.UgcBudgetEntries.AsNoTracking().Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end).ToListAsync(ct);
        var offerEntries = await db.UgcCustomerOfferBudgetEntries.AsNoTracking().Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end).ToListAsync(ct);
        var promotions = await db.Promotions.AsNoTracking().ToListAsync(ct);
        var ugc = await db.UgcOpportunities.AsNoTracking().ToListAsync(ct);
        var offers = await db.UgcCustomerOffers.AsNoTracking().ToListAsync(ct);
        var assignments = await db.UgcAssignments.AsNoTracking().ToListAsync(ct);
        var assignmentUgc = assignments.ToDictionary(x => x.Id, x => x.UgcOpportunityId);
        var views = await db.PromotionViewVerifications.AsNoTracking().Where(x => x.VerifiedAtUtc >= start && x.VerifiedAtUtc < end && !x.IsAnomaly && !x.IsBaseline).ToListAsync(ct);
        var sales = await db.VerifiedSales.AsNoTracking().Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end && x.Status == VerifiedSaleStatus.Recorded).ToListAsync(ct);
        var ugcSales = await db.UgcCustomerOfferSales.AsNoTracking().Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end).ToListAsync(ct);
        var earnings = await db.CreatorEarningEntries.AsNoTracking().Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end).ToListAsync(ct);
        var cashback = await db.CustomerCashbackEntries.AsNoTracking().Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end).ToListAsync(ct);
        var revenue = await db.PlatformRevenueEntries.AsNoTracking().Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end).ToListAsync(ct);
        var payouts = await db.PayoutRecords.AsNoTracking().Where(x => x.PaidAtUtc >= start && x.PaidAtUtc < end && x.Status == PayoutStatus.Paid).ToListAsync(ct);
        var funding = await db.PlatformPromotionalFundings.AsNoTracking().Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end).ToListAsync(ct);
        var inactiveUsers = (await db.AccountLifecycles.AsNoTracking()
            .Where(x => x.Status != AccountLifecycleStatus.Active).Select(x => x.UserId).ToListAsync(ct)).ToHashSet();
        var permissions = (await db.CommercePermissions.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct))
            .Where(x => !inactiveUsers.Contains(x.UserId)).ToList();
        var activeUserIds = permissions.Where(x => x.Role is ActorRole.Customer or ActorRole.Creator or ActorRole.Business)
            .Select(x => x.UserId).ToHashSet();
        var firstIdentifiers = await db.AuthIdentifiers.AsNoTracking().GroupBy(x => x.UserId)
            .Select(group => new { UserId = group.Key, First = group.Min(x => x.CreatedAtUtc) })
            .Where(x => x.First >= start && x.First < end).ToListAsync(ct);
        var platform = await new PlatformRevenueRepository(db).SummaryAsync(ct);
        decimal SumEntries(string movement) => entries.Where(x => x.Movement == movement).Sum(x => x.Amount.Amount);
        var period = new AdminReportPeriod(SumEntries("Deposit"), funding.Sum(x => x.Amount.Amount), SumEntries("Consumed"),
            ugcEntries.Where(x => x.Movement == "Approved").Sum(x => x.Amount.Amount),
            offerEntries.Where(x => x.Movement == "Sale").Sum(x => x.Amount.Amount),
            earnings.Where(x => x.Source == EarningSource.Ugc).Sum(x => x.Amount.Amount), earnings.Sum(x => x.Amount.Amount),
            cashback.Sum(x => x.Amount.Amount), ugcSales.Sum(x => x.CustomerDiscountAmount.Amount), revenue.Sum(x => x.Amount.Amount),
            payouts.Where(x => x.Beneficiary == PayoutBeneficiary.Creator).Sum(x => x.Amount.Amount),
            payouts.Where(x => x.Beneficiary == PayoutBeneficiary.Customer).Sum(x => x.Amount.Amount),
            views.Sum(x => Math.Max(0, x.CurrentVerifiedViews - x.PreviousVerifiedViews)), sales.Count, ugcSales.Count);
        var promotionTypes = Enum.GetValues<PromotionType>().Select(type =>
        {
            var ids = promotions.Where(x => x.PromotionType == type).Select(x => x.Id).ToHashSet();
            return new AdminReportType(PromotionTypeLabel(type), promotionEntries.Where(x => ids.Contains(x.PromotionId) && x.Movement == "Funded").Sum(x => x.Amount.Amount),
                promotionEntries.Where(x => ids.Contains(x.PromotionId) && x.Movement is "ViewReward" or "VerifiedSale").Sum(x => x.Amount.Amount),
                views.Where(x => ids.Contains(x.PromotionId)).Sum(x => Math.Max(0, x.CurrentVerifiedViews - x.PreviousVerifiedViews)),
                sales.Count(x => ids.Contains(x.PromotionId)),
                promotions.Where(x => ids.Contains(x.Id) && x.Status is PromotionStatus.Active or PromotionStatus.Funded or PromotionStatus.Published).Sum(x => x.RemainingBudget.Amount));
        }).ToArray();
        var offerIds = offers.Select(x => x.UgcOpportunityId).ToHashSet();
        var ugcTypes = new[] { false, true }.Select(hasOffer =>
        {
            var ids = ugc.Where(x => offerIds.Contains(x.Id) == hasOffer).Select(x => x.Id).ToHashSet();
            var matchingOffers = offers.Where(x => ids.Contains(x.UgcOpportunityId)).Select(x => x.Id).ToHashSet();
            return new AdminReportType(hasOffer ? "UGC + Discount Sale" : "UGC Only",
                ugcEntries.Where(x => ids.Contains(x.UgcOpportunityId) && x.Movement == "Reserved").Sum(x => x.Amount.Amount)
                  + offerEntries.Where(x => matchingOffers.Contains(x.UgcCustomerOfferId) && x.Movement == "Reserved").Sum(x => x.Amount.Amount),
                ugcEntries.Where(x => ids.Contains(x.UgcOpportunityId) && x.Movement == "Approved").Sum(x => x.Amount.Amount)
                  + offerEntries.Where(x => matchingOffers.Contains(x.UgcCustomerOfferId) && x.Movement == "Sale").Sum(x => x.Amount.Amount),
                0, ugcSales.Count(x => ids.Contains(x.UgcOpportunityId)),
                ugc.Where(x => ids.Contains(x.Id) && x.Status is UgcOpportunityStatus.Open or UgcOpportunityStatus.InProgress).Sum(x => x.RemainingFunding.Amount)
                  + offers.Where(x => matchingOffers.Contains(x.Id) && x.Status == UgcCustomerOfferStatus.Active).Sum(x => x.RemainingFunding.Amount),
                earnings.Where(x => x.Source == EarningSource.Ugc && x.UgcAssignmentId is { } assignmentId
                    && assignmentUgc.TryGetValue(assignmentId, out var opportunityId) && ids.Contains(opportunityId)).Sum(x => x.Amount.Amount),
                ugcSales.Where(x => ids.Contains(x.UgcOpportunityId)).Sum(x => x.CustomerDiscountAmount.Amount));
        }).ToArray();
        var accounts = new AdminReportAccounts(
            permissions.Where(x => x.Role == ActorRole.Customer).Select(x => x.SubjectId).Distinct().Count(),
            permissions.Where(x => x.Role == ActorRole.Creator).Select(x => x.SubjectId).Distinct().Count(),
            permissions.Where(x => x.Role == ActorRole.Business).Select(x => x.SubjectId).Distinct().Count(),
            firstIdentifiers.Count(x => activeUserIds.Contains(x.UserId)));
        return new(start, end, period, promotionTypes, ugcTypes, accounts,
            wallets.Sum(x => x.TotalBalance.Amount), platform.Unsettled.Amount);
    }
}
