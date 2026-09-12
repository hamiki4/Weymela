using System.Data;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Web;

namespace Weymela.Infrastructure.Deposits;

public sealed class DepositService(WeymelaDbContext db, IDepositProvider provider, TimeProvider clock)
{
    public async Task<DepositReceipt> SubmitAsync(Actor actor, DepositSubmission input, string key, CancellationToken ct)
    {
        if (actor.Role != ActorRole.Business || actor.BusinessId is null || !await db.CommercePermissions.AnyAsync(x => x.UserId == actor.UserId
            && x.Role == ActorRole.Business && x.SubjectId == actor.BusinessId && x.IsActive, ct)) throw new ApplicationFailure(FailureKind.Forbidden, "Business access is required.");
        var amount = WorkspaceCommands.Amount(input.Amount);
        // Adapter receives references only. Submission is not evidence that money reached Weymela.
        var evidence = await provider.SubmitAsync(actor.BusinessId.Value, input, key, ct);
        if (!evidence.RequiresAdminApproval) throw new ApplicationFailure(FailureKind.Validation, "Automatic deposit confirmation is not enabled.");
        return await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var op = new FinancialOperation(db);
            var fingerprint = RequestFingerprint.Create(actor.BusinessId.ToString()!, RequestFingerprint.Amount(amount), evidence.Provider, evidence.ExternalReference, evidence.ProofReference ?? "");
            var replay = await op.Replay(actor, "SubmitDeposit", key, fingerprint, token);
            if (replay is not null) return Receipt(await db.DepositRequests.SingleAsync(x => x.Id == Guid.Parse(replay), token));
            var prior = await db.DepositRequests.SingleOrDefaultAsync(x => x.BusinessId == actor.BusinessId && x.Provider == evidence.Provider && x.ExternalReference == evidence.ExternalReference, token);
            if (prior is not null) throw new ApplicationFailure(FailureKind.IdempotencyConflict, "That deposit reference has already been submitted.");
            var request = new DepositRequest { BusinessId = actor.BusinessId.Value, SubmittedBy = actor.UserId, Amount = amount,
                Provider = evidence.Provider, ExternalReference = evidence.ExternalReference, ProofReference = evidence.ProofReference, SubmittedAtUtc = clock.GetUtcNow().UtcDateTime };
            db.DepositRequests.Add(request); op.Remember(actor, "SubmitDeposit", key, fingerprint, request.Id.ToString(), request.SubmittedAtUtc);
            op.Audit(actor, "DepositSubmitted", request.Id, request.SubmittedAtUtc); op.Event("DepositSubmitted", new { DepositId = request.Id }, request.SubmittedAtUtc);
            return Receipt(request);
        }, ct);
    }
    public async Task<IReadOnlyList<DepositReceipt>> OwnAsync(Actor actor, CancellationToken ct)
    {
        if (actor.Role != ActorRole.Business || actor.BusinessId is null) throw new ApplicationFailure(FailureKind.Forbidden, "Business access is required.");
        return await db.DepositRequests.AsNoTracking().Where(x => x.BusinessId == actor.BusinessId).OrderByDescending(x => x.SubmittedAtUtc).Take(100)
            .Select(x => new DepositReceipt(x.Id, x.Amount.Amount, x.Status.ToString(), x.SubmittedAtUtc, x.ReviewedAtUtc)).ToListAsync(ct);
    }
    internal static DepositReceipt Receipt(DepositRequest row) => new(row.Id, row.Amount.Amount, row.Status.ToString(), row.SubmittedAtUtc, row.ReviewedAtUtc);
}
