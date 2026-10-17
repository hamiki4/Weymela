using System.Data;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Finance;

/// <summary>Explicit operator command, never invoked by a read, upload or startup.</summary>
public sealed class UgcPaymentCorrectionService(WeymelaDbContext db, TimeProvider clock)
{
    public Task<Guid> CorrectAsync(Actor actor, Guid assignmentId, Guid originalJournalId,
        string authorizationReference, string key, CancellationToken ct) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            if (actor.Role != ActorRole.PlatformAdmin || !await db.CommercePermissions.AnyAsync(x =>
                    x.UserId == actor.UserId && x.Role == ActorRole.PlatformAdmin && x.IsActive, token))
                throw new ApplicationFailure(FailureKind.Forbidden, "Active Platform Admin correction authority is required.");
            if (string.IsNullOrWhiteSpace(authorizationReference) || authorizationReference.Length > 1000
                || string.IsNullOrWhiteSpace(key) || key.Length > 160)
                throw new ApplicationFailure(FailureKind.Validation, "Record the actual correction authorization and request reference.");
            var previous = await db.UgcPaymentCorrections.SingleOrDefaultAsync(x => x.UgcAssignmentId == assignmentId, token);
            if (previous is not null)
            {
                if (previous.OriginalJournalId != originalJournalId) throw new ApplicationFailure(FailureKind.IdempotencyConflict, "The original journal differs from the recorded correction.");
                return previous.CorrectionJournalId;
            }
            var assignment = await db.UgcAssignments.SingleAsync(x => x.Id == assignmentId, token);
            if (assignment.Status != UgcAssignmentStatus.Approved || await db.CreatorPublicationVerifications.AnyAsync(x =>
                    x.UgcAssignmentId == assignmentId && x.WentLiveAtUtc != null, token))
                throw new ApplicationFailure(FailureKind.Validation, "Only approved UGC that never went live can be corrected.");
            var original = await db.FinancialJournals.Include(x => x.Lines).SingleAsync(x => x.Id == originalJournalId, token);
            var total = assignment.CreatorPayment.Add(assignment.PlatformFee);
            if (!original.IsPosted || original.SourceType != JournalSourceType.UgcApproval
                || db.Entry(original).Property<Guid?>("UgcAssignmentId").CurrentValue != assignmentId
                || original.Lines.Where(x => x.Type == JournalLineType.Debit && x.Account == "UgcAllocatedReserve").Sum(x => x.Amount.Amount) != total.Amount
                || original.Lines.Where(x => x.Type == JournalLineType.Credit && x.Account == "CreatorPayable").Sum(x => x.Amount.Amount) != assignment.CreatorPayment.Amount
                || original.Lines.Where(x => x.Type == JournalLineType.Credit && x.Account == "PlatformRevenue").Sum(x => x.Amount.Amount) != assignment.PlatformFee.Amount)
                throw new ApplicationFailure(FailureKind.Validation, "The historical journal does not match this fixed UGC payment.");
            var earning = await db.CreatorEarningEntries.SingleAsync(x => x.UgcAssignmentId == assignmentId
                && x.Source == EarningSource.Ugc && EF.Property<Guid>(x, "JournalId") == originalJournalId, token);
            if (await db.PayoutRecords.AnyAsync(x => x.CreatorId == assignment.CreatorId && x.Status == PayoutStatus.Paid
                    && x.PaidAtUtc >= earning.CreatedAtUtc, token))
                throw new ApplicationFailure(FailureKind.Validation, "A confirmed payout requires a separately reviewed recovery; no funds were changed.");
            var opportunity = await db.UgcOpportunities.SingleAsync(x => x.Id == assignment.UgcOpportunityId, token);
            if (opportunity.DeliverableType == UgcDeliverableType.DirectVideoDelivery)
                throw new ApplicationFailure(FailureKind.Validation, "Approved direct delivery is earned work and cannot use a premature-publication correction.");
            var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == opportunity.BusinessId, token);
            var earnings = await db.CreatorEarningsAccounts.SingleAsync(x => x.CreatorId == assignment.CreatorId, token);
            if (earnings.AvailableEarnings.Amount < assignment.CreatorPayment.Amount)
                throw new ApplicationFailure(FailureKind.InsufficientFunds, "The premature payment is no longer fully available for correction.");
            var now = clock.GetUtcNow().UtcDateTime;
            var correlation = Guid.NewGuid();
            earnings.Pay(assignment.CreatorPayment, now, correlation);
            opportunity.RestoreUnpublishedDeliverable(wallet, total);
            assignment.RestorePendingPublication();
            var journal = new FinancialJournal("UGC-CORRECTION-" + assignmentId.ToString("N"), correlation,
                actor.UserId, JournalSourceType.Adjustment, now, key);
            journal.AddLine(JournalLineType.Debit, assignment.CreatorPayment, "CreatorPayable");
            if (assignment.PlatformFee.Amount > 0) journal.AddLine(JournalLineType.Debit, assignment.PlatformFee, "PlatformRevenue");
            journal.AddLine(JournalLineType.Credit, total, "UgcAllocatedReserve");
            journal.Post(); db.FinancialJournals.Add(journal);
            db.Entry(journal).Property("BusinessId").CurrentValue = opportunity.BusinessId;
            db.Entry(journal).Property("CreatorId").CurrentValue = assignment.CreatorId;
            db.Entry(journal).Property("UgcOpportunityId").CurrentValue = opportunity.Id;
            db.Entry(journal).Property("UgcAssignmentId").CurrentValue = assignment.Id;
            db.UgcPaymentCorrections.Add(new(Guid.NewGuid(), assignment.Id, assignment.CreatorId, earning.Id,
                originalJournalId, journal.Id, assignment.CreatorPayment, assignment.PlatformFee,
                actor.UserId, authorizationReference.Trim(), now));
            db.UgcBudgetEntries.Add(new(Guid.NewGuid(), opportunity.Id, assignment.Id, total, "PrematurePaymentReturnedToReserve", journal.Id, now));
            db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, total, "UgcPaymentCorrectionReserve", journal.Id, now, opportunity.Id));
            var operation = new FinancialOperation(db);
            db.AuditEvents.Add(new(Guid.NewGuid(), "PrematureUgcPaymentCorrected", actor.UserId, opportunity.BusinessId,
                null, assignment.CreatorId, correlation, now,
                $"originalJournal={originalJournalId:D};correctionJournal={journal.Id:D};authorization={authorizationReference.Trim()}"));
            operation.Event("UgcPaymentCorrected", new { OpportunityId = opportunity.Id, AssignmentId = assignment.Id,
                CreatorId = assignment.CreatorId, BusinessId = opportunity.BusinessId, Amount = assignment.CreatorPayment.Amount }, now);
            return journal.Id;
        }, ct);
}
