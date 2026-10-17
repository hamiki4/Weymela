using Microsoft.EntityFrameworkCore;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Infrastructure.Finance;

internal static class CreatorEarnedBalance
{
    internal static async Task<Money> AvailableAsync(WeymelaDbContext db, Guid creatorId, CancellationToken ct)
    {
        var balance = (await db.CreatorEarningsAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.CreatorId == creatorId, ct))?.AvailableEarnings ?? Money.Zero();
        var premature = await db.CreatorEarningEntries.AsNoTracking().Where(x => x.CreatorId == creatorId && x.Source == EarningSource.Ugc
            && !db.UgcPaymentCorrections.Any(c => c.OriginalEarningEntryId == x.Id)
            && !db.UgcAssignments.Any(a => a.Id == x.UgcAssignmentId && a.Status == UgcAssignmentStatus.Approved
                && a.CompletedAtUtc != null && db.UgcOpportunities.Any(o => o.Id == a.UgcOpportunityId && o.DeliverableType == UgcDeliverableType.DirectVideoDelivery))
            && !db.CreatorPublicationVerifications.Any(p => p.UgcAssignmentId == x.UgcAssignmentId && p.WentLiveAtUtc != null))
            .Select(x => x.Amount).ToListAsync(ct);
        return new Money(Math.Max(0, balance.Amount - premature.Sum(x => x.Amount)));
    }
}
