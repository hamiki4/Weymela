using Microsoft.EntityFrameworkCore;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Infrastructure.Finance;

internal sealed record UgcAdminProgress(DateTime? GoLiveAtUtc, string Status, string PaymentStatus)
{
    private sealed class Lot(CreatorEarningEntry entry, DateTime at)
    {
        internal CreatorEarningEntry Entry = entry;
        internal DateTime At = at;
        internal decimal Remaining = entry.Amount.Amount;
        internal decimal Paid;
    }

    // A read projection of the existing earning/payout journal. No payout is
    // completed here. Paid requires an actual confirmed Paid payout record.
    internal static async Task<IReadOnlyDictionary<Guid, UgcAdminProgress>> LoadAsync(
        WeymelaDbContext db, IReadOnlyList<UgcOpportunity> opportunities, DateTime now, CancellationToken ct)
    {
        var ids = opportunities.Select(x => x.Id).ToArray();
        var assignments = await db.UgcAssignments.AsNoTracking().Where(x => ids.Contains(x.UgcOpportunityId)).ToListAsync(ct);
        var creators = assignments.Select(x => x.CreatorId).Distinct().ToArray();
        var publications = await db.CreatorPublicationVerifications.AsNoTracking()
            .Where(x => creators.Contains(x.CreatorId) && x.UgcAssignmentId != null && x.WentLiveAtUtc != null).ToListAsync(ct);
        var liveDates = publications.GroupBy(x => x.UgcAssignmentId!.Value).ToDictionary(x => x.Key, x => x.Min(v => v.WentLiveAtUtc!.Value));
        var directDates = await db.UgcAssignments.AsNoTracking().Where(x => creators.Contains(x.CreatorId)
            && x.Status == UgcAssignmentStatus.Approved && x.CompletedAtUtc != null
            && db.UgcOpportunities.Any(o => o.Id == x.UgcOpportunityId && o.DeliverableType == UgcDeliverableType.DirectVideoDelivery))
            .ToDictionaryAsync(x => x.Id, x => x.CompletedAtUtc!.Value, ct);
        var entries = await db.CreatorEarningEntries.AsNoTracking().Where(x => creators.Contains(x.CreatorId)
            && !db.UgcPaymentCorrections.Any(c => c.OriginalEarningEntryId == x.Id)).ToListAsync(ct);
        var payouts = await db.PayoutRecords.AsNoTracking().Where(x => x.CreatorId != null && creators.Contains(x.CreatorId.Value)
            && x.Status == PayoutStatus.Paid).ToListAsync(ct);
        var lots = new List<Lot>();
        foreach (var creator in creators)
        {
            var earned = new List<Lot>();
            foreach (var entry in entries.Where(x => x.CreatorId == creator))
            {
                var at = entry.CreatedAtUtc;
                if (entry.Source == EarningSource.Ugc)
                {
                    if (entry.UgcAssignmentId is not { } assignment) continue;
                    if (!liveDates.TryGetValue(assignment, out var vested) && !directDates.TryGetValue(assignment, out vested)) continue;
                    if (vested > at) at = vested;
                }
                earned.Add(new(entry, at));
            }
            var movements = earned.Select(x => (x.At, Amount: x.Entry.Amount.Amount, Lot: (Lot?)x, Paid: false))
                .Concat(payouts.Where(x => x.CreatorId == creator).Select(x => (At: x.PaidAtUtc!.Value, Amount: -x.Amount.Amount, Lot: (Lot?)null, Paid: true)))
                .OrderBy(x => x.At).ThenByDescending(x => x.Amount);
            var current = new List<Lot>();
            decimal debt = 0;
            foreach (var movement in movements)
            {
                if (movement.Amount > 0)
                {
                    var lot = movement.Lot!;
                    lot.Remaining -= Math.Min(debt, lot.Remaining);
                    debt = Math.Max(0, debt - movement.Amount);
                    current.Add(lot);
                    continue;
                }
                var deduction = -movement.Amount;
                foreach (var lot in current.OrderByDescending(x => x.Entry.Source == EarningSource.Ugc).ThenBy(x => x.At))
                {
                    var used = Math.Min(deduction, lot.Remaining);
                    lot.Remaining -= used;
                    if (movement.Paid) lot.Paid += used;
                    deduction -= used;
                    if (deduction == 0) break;
                }
                debt += deduction;
            }
            lots.AddRange(current.Where(x => x.Entry.Source == EarningSource.Ugc));
        }
        return opportunities.ToDictionary(x => x.Id, x =>
        {
            var work = assignments.Where(a => a.UgcOpportunityId == x.Id).ToArray();
            var workIds = work.Select(a => a.Id).ToHashSet();
            var live = publications.Where(p => workIds.Contains(p.UgcAssignmentId!.Value)).ToArray();
            var date = live.Length == 0 ? (DateTime?)null : live.Min(p => p.WentLiveAtUtc);
            var status = live.Any(p => p.Status == PublicationVerificationStatus.Verified && p.EndedAtUtc == null && p.ExpiresAtUtc > now) ? "Live"
                : x.Status is UgcOpportunityStatus.Completed or UgcOpportunityStatus.Cancelled ? x.Status.ToString()
                : live.Length > 0 && live.All(p => p.EndedAtUtc != null || p.ExpiresAtUtc <= now) ? "Completed"
                : work.Any(a => a.Status == UgcAssignmentStatus.Approved) ? "Approved" : "Pending";
            var payments = lots.Where(l => l.Entry.UgcAssignmentId is { } id && workIds.Contains(id)).ToArray();
            var payment = payments.Any(l => l.Remaining > 0) ? "Eligible"
                : payments.Length >= x.CreatorCapacity && payments.All(l => l.Paid == l.Entry.Amount.Amount) ? "Paid" : "Pending";
            return new UgcAdminProgress(date, status, payment);
        });
    }
}
