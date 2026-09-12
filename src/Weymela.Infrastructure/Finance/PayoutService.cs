using System.Data;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Finance;

public sealed class PayoutService(WeymelaDbContext db, TimeProvider clock)
{
    public async Task<PayoutEligibility> EligibilityAsync(Actor actor, PayoutBeneficiary kind, Guid subject, CancellationToken ct = default)
    {
        await DemandBeneficiaryAsync(actor, kind, subject, ct);
        var config = await new FinancialConfigurationResolver(db).EffectiveAsync(clock.GetUtcNow().UtcDateTime, ct);
        var threshold = kind == PayoutBeneficiary.Creator ? config.CreatorPayoutThreshold : config.CustomerPayoutThreshold;
        var available = kind == PayoutBeneficiary.Creator
            ? (await db.CreatorEarningsAccounts.SingleOrDefaultAsync(x => x.CreatorId == subject, ct))?.AvailableEarnings ?? Money.Zero()
            : (await db.CustomerCashbackAccounts.SingleOrDefaultAsync(x => x.CustomerId == subject, ct))?.AvailableCashback ?? Money.Zero();
        return new(kind, subject, available, threshold, available.Amount >= threshold.Amount ? threshold : Money.Zero(), config.Id);
    }

    public Task<Guid> PrepareAsync(Actor actor, PayoutBeneficiary kind, Guid subject, string key, CancellationToken ct = default) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            await DemandBeneficiaryAsync(actor, kind, subject, token);
            var op = new FinancialOperation(db); var fp = RequestFingerprint.Create(kind.ToString(), subject.ToString());
            var replay = await op.Replay(actor, "PreparePayout", key, fp, token);
            if (replay is not null) return Guid.Parse(replay);
            var pending = await db.PayoutRecords.SingleOrDefaultAsync(x => x.Beneficiary == kind && x.Status == PayoutStatus.Eligible &&
                (x.CreatorId == subject || x.CustomerId == subject), token);
            if (pending is not null)
            {
                op.Remember(actor, "PreparePayout", key, fp, pending.Id.ToString(), clock.GetUtcNow().UtcDateTime);
                return pending.Id;
            }
            var eligible = await EligibilityAsync(actor, kind, subject, token);
            if (eligible.EligibleAmount.Amount == 0) throw new ApplicationFailure(FailureKind.InsufficientFunds, "Payout threshold has not been reached.");
            var record = new PayoutRecord(kind, subject, eligible.Available, eligible.Threshold, eligible.ConfigurationVersionId, clock.GetUtcNow().UtcDateTime);
            db.PayoutRecords.Add(record);
            op.Remember(actor, "PreparePayout", key, fp, record.Id.ToString(), clock.GetUtcNow().UtcDateTime);
            op.Event("PayoutPrepared",
                new { PayoutId = record.Id, BeneficiaryId = subject, Amount = record.Amount }, clock.GetUtcNow().UtcDateTime);
            op.Audit(actor, "PayoutPrepared", Guid.NewGuid(), clock.GetUtcNow().UtcDateTime, creator: kind == PayoutBeneficiary.Creator ? subject : null);
            return record.Id;
        }, ct);

    public Task<Guid> MarkPaidAsync(Actor actor, Guid payoutId, string paymentReference, string key, CancellationToken ct = default) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            DemandAdmin(actor);
            if (string.IsNullOrWhiteSpace(paymentReference) || paymentReference.Length > 200)
                throw new ApplicationFailure(FailureKind.Validation, "External payment confirmation reference is required.");
            var reference = paymentReference.Trim(); var op = new FinancialOperation(db);
            var fp = RequestFingerprint.Create(payoutId.ToString(), reference);
            var replay = await op.Replay(actor, "MarkPayoutPaid", key, fp, token);
            if (replay is not null) return Guid.Parse(replay);
            var payout = await db.PayoutRecords.SingleOrDefaultAsync(x => x.Id == payoutId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Payout not found.");
            if (payout.Status != PayoutStatus.Eligible) throw new ApplicationFailure(FailureKind.Validation, "Payout has already been paid.");
            if (payout.Beneficiary == PayoutBeneficiary.Creator)
                (await db.CreatorEarningsAccounts.SingleAsync(x => x.CreatorId == payout.CreatorId, token)).Pay(payout.Amount, clock.GetUtcNow().UtcDateTime, Guid.NewGuid());
            else
                (await db.CustomerCashbackAccounts.SingleAsync(x => x.CustomerId == payout.CustomerId, token)).Pay(payout.Amount);
            var j = new FinancialJournal(Guid.NewGuid().ToString("N"), Guid.NewGuid(), actor.UserId, JournalSourceType.Payout, clock.GetUtcNow().UtcDateTime, key);
            j.AddLine(JournalLineType.Debit, payout.Amount, payout.Beneficiary == PayoutBeneficiary.Creator ? "CreatorPayable" : "CustomerCashbackPayable");
            j.AddLine(JournalLineType.Credit, payout.Amount, "CashClearing"); j.Post(); db.FinancialJournals.Add(j);
            db.Entry(j).Property("CreatorId").CurrentValue = payout.CreatorId;
            db.Entry(j).Property("CustomerId").CurrentValue = payout.CustomerId;
            payout.MarkPaid(actor.UserId, reference, j.Id, clock.GetUtcNow().UtcDateTime);
            op.Remember(actor, "MarkPayoutPaid", key, fp, payout.Id.ToString(), clock.GetUtcNow().UtcDateTime);
            op.Event("PayoutPaid", new { PayoutId = payout.Id, JournalId = j.Id }, clock.GetUtcNow().UtcDateTime);
            op.Audit(actor, "PayoutPaid", j.CorrelationId, clock.GetUtcNow().UtcDateTime, creator: payout.CreatorId);
            return payout.Id;
        }, ct);

    public Task<Guid> SettlePlatformAsync(Actor actor, Money amount, string reference, string key, CancellationToken ct = default) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            DemandAdmin(actor);
            if (amount.Amount <= 0 || string.IsNullOrWhiteSpace(reference) || reference.Length > 200)
                throw new ApplicationFailure(FailureKind.Validation, "A positive settlement amount and confirmation reference are required.");
            var op = new FinancialOperation(db); var fp = RequestFingerprint.Create(RequestFingerprint.Amount(amount), reference.Trim());
            var replay = await op.Replay(actor, "SettlePlatform", key, fp, token);
            if (replay is not null) return Guid.Parse(replay);
            var summary = await new PlatformRevenueRepository(db).SummaryAsync(token);
            if (summary.Unsettled.Amount < amount.Amount) throw new ApplicationFailure(FailureKind.InsufficientFunds, "Settlement exceeds unsettled Platform revenue.");
            var j = new FinancialJournal(Guid.NewGuid().ToString("N"), Guid.NewGuid(), actor.UserId, JournalSourceType.Settlement, clock.GetUtcNow().UtcDateTime, key);
            j.AddLine(JournalLineType.Debit, amount, "PlatformRevenue"); j.AddLine(JournalLineType.Credit, amount, "CashClearing"); j.Post();
            db.FinancialJournals.Add(j);
            var settlement = new PlatformSettlement(amount, reference.Trim(), clock.GetUtcNow().UtcDateTime);
            db.PlatformSettlements.Add(settlement); db.Entry(settlement).Property("JournalId").CurrentValue = j.Id;
            db.Entry(settlement).Property("SettledBy").CurrentValue = actor.UserId;
            op.Remember(actor, "SettlePlatform", key, fp, settlement.Id.ToString(), clock.GetUtcNow().UtcDateTime);
            op.Audit(actor, "PlatformSettled", j.CorrelationId, clock.GetUtcNow().UtcDateTime);
            op.Event("PlatformSettled", new { SettlementId = settlement.Id, JournalId = j.Id, Amount = amount }, clock.GetUtcNow().UtcDateTime);
            return settlement.Id;
        }, ct);

    internal static void DemandAdmin(Actor actor)
    {
        if (actor.Role != ActorRole.PlatformAdmin) throw new ApplicationFailure(FailureKind.Forbidden, "Platform Admin permission is required.");
    }
    private static void DemandOwnerOrAdmin(Actor actor, PayoutBeneficiary kind, Guid subject)
    {
        if (actor.Role == ActorRole.PlatformAdmin) return;
        if ((kind == PayoutBeneficiary.Creator && actor.Role == ActorRole.Creator && actor.CreatorId == subject) ||
            (kind == PayoutBeneficiary.Customer && actor.Role == ActorRole.Customer && actor.CustomerId == subject)) return;
        throw new ApplicationFailure(FailureKind.Forbidden, "Payout account belongs to another role or user.");
    }
    private async Task DemandBeneficiaryAsync(Actor actor, PayoutBeneficiary kind, Guid subject, CancellationToken ct)
    {
        DemandOwnerOrAdmin(actor, kind, subject);
        if (actor.Role == ActorRole.PlatformAdmin) return;
        var access = new CommerceAccessPolicy(db);
        if (kind == PayoutBeneficiary.Creator) await access.EnsureCreatorAsync(actor, subject, ct);
        else await access.EnsureCustomerAsync(actor, ct);
    }
}
