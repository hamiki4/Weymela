using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Infrastructure.Finance;

internal static class UgcPublicationPayment
{
    // The caller owns the serializable Go Live transaction. No view count is a payment condition.
    internal static async Task ReleaseAsync(WeymelaDbContext db, Actor actor, UgcAssignment assignment,
        UgcOpportunity opportunity, CreatorPublicationVerification publication, string key,
        DateTime now, CancellationToken ct)
    {
        if (publication.UgcAssignmentId != assignment.Id || publication.Status != PublicationVerificationStatus.Verified
            || publication.WentLiveAtUtc is null || assignment.Status != UgcAssignmentStatus.Approved)
            throw new ApplicationFailure(FailureKind.Validation, "Verified Go Live is required before UGC payment.");
        if (await db.CreatorEarningEntries.AnyAsync(x => x.UgcAssignmentId == assignment.Id && x.Source == EarningSource.Ugc
                && !db.UgcPaymentCorrections.Any(c => c.OriginalEarningEntryId == x.Id), ct)) return;
        var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == opportunity.BusinessId, ct);
        var total = assignment.CreatorPayment.Add(assignment.PlatformFee);
        var correlation = Guid.NewGuid();
        opportunity.RecognizeApprovedDeliverable(wallet, now, correlation);
        assignment.CompletePublication(now);
        var journal = new FinancialJournal(Guid.NewGuid().ToString("N"), correlation, actor.UserId,
            JournalSourceType.UgcPublication, now, key);
        journal.AddLine(JournalLineType.Debit, total, "UgcAllocatedReserve");
        journal.AddLine(JournalLineType.Credit, assignment.CreatorPayment, "CreatorPayable");
        if (assignment.PlatformFee.Amount > 0) journal.AddLine(JournalLineType.Credit, assignment.PlatformFee, "PlatformRevenue");
        journal.Post(); db.FinancialJournals.Add(journal);
        db.Entry(journal).Property("BusinessId").CurrentValue = opportunity.BusinessId;
        db.Entry(journal).Property("CreatorId").CurrentValue = assignment.CreatorId;
        db.Entry(journal).Property("UgcOpportunityId").CurrentValue = opportunity.Id;
        db.Entry(journal).Property("UgcAssignmentId").CurrentValue = assignment.Id;
        db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, total, "UgcConsumed", journal.Id, now, opportunity.Id));
        db.UgcBudgetEntries.Add(new(Guid.NewGuid(), opportunity.Id, assignment.Id, total, "VerifiedPublicationEarned", journal.Id, now));
        var earnings = await db.CreatorEarningsAccounts.SingleOrDefaultAsync(x => x.CreatorId == assignment.CreatorId, ct);
        if (earnings is null) { earnings = new(assignment.CreatorId); db.CreatorEarningsAccounts.Add(earnings); }
        var before = earnings.AvailableEarnings;
        earnings.EarnUgc(assignment.CreatorPayment, assignment.Id, now, correlation);
        var earning = earnings.Entries.Last(); db.CreatorEarningEntries.Add(earning);
        db.Entry(earning).Property("JournalId").CurrentValue = journal.Id;
        if (assignment.PlatformFee.Amount > 0)
        {
            var revenue = new PlatformRevenueEntry(Guid.NewGuid(), null, PlatformRevenueSource.UgcFee,
                assignment.PlatformFee, RevenueStatus.Accrued, now, correlation, assignment.Id);
            db.PlatformRevenueEntries.Add(revenue); db.Entry(revenue).Property("JournalId").CurrentValue = journal.Id;
        }
        var operation = new FinancialOperation(db);
        await operation.EmitEligibility(PayoutBeneficiary.Creator, assignment.CreatorId, before, earnings.AvailableEarnings, now, ct);
        operation.Audit(actor, "UgcFixedPaymentEarned", correlation, now, creator: assignment.CreatorId);
        operation.Event("UgcFixedPaymentEarned", new { OpportunityId = opportunity.Id, AssignmentId = assignment.Id,
            CreatorId = assignment.CreatorId, BusinessId = opportunity.BusinessId,
            Amount = assignment.CreatorPayment.Amount, JournalId = journal.Id }, now);
    }
}
