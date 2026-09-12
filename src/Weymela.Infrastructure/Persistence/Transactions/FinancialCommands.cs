using System.Text.Json;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Outbox;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;

namespace Weymela.Infrastructure.Persistence.Transactions;

// Persistence decorator: existing Phase 2 command handlers remain responsible for domain behavior.
// Only this boundary commits command work; repositories stage changes.
public sealed partial class FinancialCommands(WeymelaDbContext db, TimeProvider? clock = null)
{
    private EfUnitOfWork Uow => new(db);
    private IdempotencyStore Idempotency => new(db);
    private PromotionApplicationService Promotions => new(new PromotionRepository(db), new BusinessWalletRepository(db),
        new FinancialConfigurationResolver(db), Idempotency, new OutboxEventPublisher(db), new LegalAcceptanceGate(db, clock ?? TimeProvider.System));
    private ParticipationApplicationService Participation => new(new PromotionRepository(db), new CreatorApplicationRepository(db),
        new CreatorAllocationRepository(db), new OutboxEventPublisher(db));

    public Task<Guid> CreditDepositAsync(CreditBusinessDepositCommand c, long expectedVersion, CancellationToken ct = default) =>
        Uow.ExecuteAsync(token => CreditDepositWithinTransactionAsync(c, expectedVersion, token), ct);

    // Reused by confirmed manual deposits inside the same UoW; not a second wallet engine.
    private async Task<Guid> CreditDepositWithinTransactionAsync(CreditBusinessDepositCommand c, long expectedVersion, CancellationToken token)
        {
            var prior = await Idempotency.FindAsync(c.IdempotencyKey, "CreditBusinessDeposit", c.Actor.UserId, token);
            var service = new WalletApplicationService(new BusinessWalletRepository(db), Idempotency, new OutboxEventPublisher(db));
            await service.CreditDepositAsync(c, expectedVersion, token);
            if (prior is not null) return Guid.Parse(prior.ResultReference!.ToString()!);
            var wallet = (await new BusinessWalletRepository(db).GetAsync(c.Actor.BusinessId!.Value, token))!;
            var correlation = wallet.DomainEvents.OfType<BusinessWalletCredited>().Last().CorrelationId;
            var journal = Journal(c.Actor, "Deposit", c.Amount, "CashClearing", "BusinessAvailable", c.Now, c.IdempotencyKey, correlation: correlation);
            db.WalletEntries.Add(new(Guid.NewGuid(), c.Actor.BusinessId!.Value, null, c.Amount, "Deposit", journal.Id, c.Now));
            Audit(c.Actor, "BusinessWalletCredited", c.Now, journal.CorrelationId);
            return c.Actor.BusinessId.Value;
        }

    public Task<Guid> FundPromotionAsync(FundPromotionCommand c, CancellationToken ct = default) =>
        Uow.ExecuteAsync(async token =>
        {
            var prior = await Idempotency.FindAsync(c.IdempotencyKey, "FundPromotion", c.Actor.UserId, token);
            await Promotions.FundAsync(c, token);
            if (prior is not null) return Guid.Parse(prior.ResultReference!.ToString()!);
            var p = (await new PromotionRepository(db).GetAsync(c.PromotionId, token))!;
            var correlation = p.DomainEvents.OfType<PromotionFunded>().Last().CorrelationId;
            var journal = Journal(c.Actor, "PromotionReservation", p.TotalBudget, "BusinessAvailable", "CampaignUnallocatedReserve", c.Now, c.IdempotencyKey, p.Id, correlation: correlation);
            db.PromotionReservations.Add(new(p.Id, p.BusinessId, p.TotalBudget, journal.Id, c.Now));
            db.WalletEntries.Add(new(Guid.NewGuid(), p.BusinessId, p.Id, p.TotalBudget, "Reserve", journal.Id, c.Now));
            db.PromotionBudgetEntries.Add(new(Guid.NewGuid(), p.Id, null, p.TotalBudget, "Funded", journal.Id, c.Now));
            Audit(c.Actor, "PromotionFunded", c.Now, correlation, p.Id);
            return p.Id;
        }, ct);

    private FinancialJournal Journal(Actor actor, string source, Money amount, string debit, string credit,
        DateTime at, string key, Guid? promotion = null, Guid? creator = null, Guid? correlation = null)
    {
        var j = new FinancialJournal(Guid.NewGuid().ToString("N"), correlation ?? Guid.NewGuid(), actor.UserId,
            Enum.Parse<JournalSourceType>(source), at, key);
        j.AddLine(JournalLineType.Debit, amount, debit);
        j.AddLine(JournalLineType.Credit, amount, credit);
        j.Post(); db.FinancialJournals.Add(j);
        db.Entry(j).Property("BusinessId").CurrentValue = actor.BusinessId;
        db.Entry(j).Property("PromotionId").CurrentValue = promotion;
        db.Entry(j).Property("CreatorId").CurrentValue = creator;
        return j;
    }

    private void Audit(Actor actor, string type, DateTime at, Guid correlation, Guid? promotion = null, Guid? creator = null, string detail = "")
    {
        var audit = new AuditEvent(Guid.NewGuid(), type, actor.UserId, actor.BusinessId, promotion, creator, correlation, at, detail);
        db.AuditEvents.Add(audit);
        // Audit envelopes cover lifecycle events not yet represented by the Phase 1 domain event set.
        db.OutboxMessages.Add(new() { EventType = type + "Audit", Payload = JsonSerializer.Serialize(audit), OccurredAtUtc = at });
    }

    private async Task<Guid?> Replay(Actor actor, string operation, string key, string fingerprint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200) throw new ApplicationFailure(FailureKind.Validation, "A valid idempotency key is required.");
        var prior = await Idempotency.FindAsync(key, operation, actor.UserId, ct);
        if (prior is null) return null;
        if (prior.RequestFingerprint != fingerprint) throw new ApplicationFailure(FailureKind.IdempotencyConflict, "Conflicting request fingerprint.");
        return Guid.Parse(prior.ResultReference!.ToString()!);
    }
}
