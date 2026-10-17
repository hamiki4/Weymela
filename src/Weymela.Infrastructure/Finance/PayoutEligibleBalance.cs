using Microsoft.EntityFrameworkCore;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Finance;

internal static class PayoutEligibleBalance
{
    internal static async Task<decimal> AmountAsync(WeymelaDbContext db, FinancialConfigurationVersion config,
        PayoutBeneficiary kind, Guid subject, decimal available, DateTime now, CancellationToken ct)
    {
        var threshold = kind == PayoutBeneficiary.Creator ? config.CreatorPayoutThreshold : config.CustomerPayoutThreshold;
        if (threshold is null || available <= 0) return 0;
        if (available >= threshold.Value.Amount) return available;
        var cutoff = config.MonthlyPayoutEnabled != true ? null : PayoutEligibilityPolicy.MonthlyCutoff(now, config.MonthlyPayoutDay, config.MonthlyPayoutEffectiveFromUtc);
        var ugcExempt = kind == PayoutBeneficiary.Creator && config.UgcPayoutMinimumExempt == true;
        if (cutoff is null && !ugcExempt) return 0;
        var movements = new List<PayoutBalanceMovement>();
        if (kind == PayoutBeneficiary.Creator)
        {
            var entries = await db.CreatorEarningEntries.AsNoTracking().Where(x => x.CreatorId == subject
                && !db.UgcPaymentCorrections.Any(c => c.OriginalEarningEntryId == x.Id)).ToListAsync(ct);
            foreach (var entry in entries)
            {
                var at = entry.CreatedAtUtc;
                if (entry.Source == EarningSource.Ugc)
                {
                    var live = await db.CreatorPublicationVerifications.AsNoTracking().Where(x => x.UgcAssignmentId == entry.UgcAssignmentId
                        && x.WentLiveAtUtc != null).Select(x => x.WentLiveAtUtc).SingleOrDefaultAsync(ct);
                    var delivery = await db.UgcAssignments.AsNoTracking().Where(x => x.Id == entry.UgcAssignmentId
                        && x.Status == UgcAssignmentStatus.Approved && x.CompletedAtUtc != null
                        && db.UgcOpportunities.Any(o => o.Id == x.UgcOpportunityId && o.DeliverableType == UgcDeliverableType.DirectVideoDelivery))
                        .Select(x => x.CompletedAtUtc).SingleOrDefaultAsync(ct);
                    if ((live ?? delivery) is not { } earnedAt) continue;
                    if (earnedAt > at) at = earnedAt;
                }
                movements.Add(new(at, entry.Amount.Amount, entry.Source == EarningSource.Ugc));
            }
        }
        else
            movements.AddRange((await db.CustomerCashbackEntries.AsNoTracking().Where(x => x.CustomerId == subject).ToListAsync(ct))
                .Select(x => new PayoutBalanceMovement(x.CreatedAtUtc, x.Amount.Amount)));
        movements.AddRange((await db.PayoutRecords.AsNoTracking().Where(x => x.Beneficiary == kind
            && (x.CreatorId == subject || x.CustomerId == subject) && x.Status == PayoutStatus.Paid).ToListAsync(ct))
            .Select(x => new PayoutBalanceMovement(x.PaidAtUtc!.Value, -x.Amount.Amount)));
        return PayoutEligibilityPolicy.Eligible(available, threshold.Value.Amount, ugcExempt, cutoff, movements);
    }
}
