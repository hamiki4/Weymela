using System.Data;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence.Repositories;

namespace Weymela.Infrastructure.Persistence.Transactions;

public sealed partial class FinancialCommands
{
    public Task<Guid> ReviewDepositAsync(Actor admin, Guid id, DepositReview review, string key, CancellationToken ct = default) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            if (admin.Role != ActorRole.PlatformAdmin || !await db.CommercePermissions.AnyAsync(x => x.UserId == admin.UserId && x.Role == admin.Role && x.IsActive, token))
                throw new ApplicationFailure(FailureKind.Forbidden, "Platform Admin approval is required.");
            InputRules.Id(id); var reference = InputRules.Reference(review.ConfirmationReference, "confirmation reference").ToUpperInvariant();
            var fingerprint = RequestFingerprint.Create(id.ToString(), review.Approve.ToString(), reference);
            var replay = await Replay(admin, "ReviewDeposit", key, fingerprint, token); if (replay is not null) return replay.Value;
            var request = await db.DepositRequests.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ApplicationFailure(FailureKind.NotFound, "Deposit request not found.");
            if (request.Version != review.ExpectedVersion) throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "Deposit request changed. Refresh before reviewing.");
            if (request.Status != DepositReviewStatus.Pending) throw new ApplicationFailure(FailureKind.Validation, "This deposit has already been reviewed.");
            var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            if (review.Approve)
            {
                if (await db.DepositRequests.AnyAsync(x => x.Provider == request.Provider && x.Status == DepositReviewStatus.Approved && x.ConfirmationReference == reference, token))
                    throw new ApplicationFailure(FailureKind.IdempotencyConflict, "That confirmed payment has already funded a wallet.");
                var wallet = await new BusinessWalletRepository(db).GetAsync(request.BusinessId, token) ?? throw new ApplicationFailure(FailureKind.NotFound, "Business wallet not found.");
                // The Business remains the beneficiary; the separate immutable review audit identifies the authorizing Admin.
                await CreditDepositWithinTransactionAsync(new(new(request.SubmittedBy, ActorRole.Business, request.BusinessId), request.Amount, $"approved-deposit-{request.Id:N}", now), wallet.Version, token);
                request.JournalId=db.FinancialJournals.Local.Single(x=>x.IdempotencyReference==$"approved-deposit-{request.Id:N}").Id;
            }
            request.Status = review.Approve ? DepositReviewStatus.Approved : DepositReviewStatus.Rejected;
            request.ReviewedAtUtc = now; request.ReviewedBy = admin.UserId; request.ConfirmationReference = reference;
            await Idempotency.SaveAsync(new(key, "ReviewDeposit", admin.UserId, fingerprint, request.Id, now), token);
            Audit(admin, review.Approve ? "DepositApproved" : "DepositRejected", now, request.Id, detail: request.Id.ToString());
            db.OutboxMessages.Add(new() { EventType = "DepositReviewed", Payload = System.Text.Json.JsonSerializer.Serialize(new { DepositId = request.Id }), OccurredAtUtc = now });
            return request.Id;
        }, ct);
}
