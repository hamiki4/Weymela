using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;

namespace Weymela.Infrastructure.Persistence.Transactions;

public sealed partial class FinancialCommands
{
    public Task<Guid> CompletePromotionAsync(CompletePromotionCommand c, string key, CancellationToken ct = default) =>
        Uow.ExecuteAsync(async token =>
        {
            DemandBusiness(c.Actor);
            var fp = RequestFingerprint.Create(c.Actor.BusinessId.ToString()!, c.PromotionId.ToString());
            var prior = await Replay(c.Actor, "CompletePromotion", key, fp, token);
            if (prior is not null) return prior.Value;
            var p = await new PromotionRepository(db).GetAsync(c.PromotionId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Campaign not found.");
            var unused = p.Allocations.Where(x => x.Status == CreatorAllocationStatus.Active && x.RemainingAmount.Amount > 0)
                .Select(x => new { x.Id, x.CreatorId, Amount = x.RemainingAmount }).ToArray();
            await Promotions.CompleteAsync(c, token);
            foreach (var live in await db.CreatorPromotionParticipations.Where(x => x.PromotionId == p.Id).ToListAsync(token))
                live.Complete();
            foreach (var a in unused)
            {
                var j = Journal(c.Actor, "Allocation", a.Amount, "CreatorAllocatedReserve", "CampaignUnallocatedReserve", c.Now, key, p.Id, a.CreatorId);
                db.PromotionBudgetEntries.Add(new(Guid.NewGuid(), p.Id, a.Id, a.Amount, "UnusedReturnedToCampaign", j.Id, c.Now));
            }
            await Idempotency.SaveAsync(new(key, "CompletePromotion", c.Actor.UserId, fp, p.Id, c.Now), token);
            Audit(c.Actor, "PromotionCompleted", c.Now, Guid.NewGuid(), p.Id);
            return p.Id;
        }, ct);

    public Task<Guid> ApplyToPromotionAsync(ApplyToPromotionCommand c, CancellationToken ct = default) =>
        Uow.ExecuteAsync(async token =>
        {
            if (c.Actor.Role != ActorRole.Creator || c.Actor.CreatorId is null)
                throw new ApplicationFailure(FailureKind.Forbidden, "Only a Creator may apply as self.");
            await new LegalAcceptanceGate(db, clock ?? TimeProvider.System).EnsureCurrentAcceptedAsync(c.Actor.UserId, LegalRole.Creator,
                [LegalDocumentType.CreatorAgreement, LegalDocumentType.AntiCircumventionAgreement], token);
            var a = await Participation.ApplyAsync(c, token);
            Audit(c.Actor, "CreatorApplied", c.Now, Guid.NewGuid(), c.PromotionId, c.Actor.CreatorId);
            return a.Id;
        }, ct);

    public Task<Guid> CreatePromotionAsync(CreatePromotionCommand c, CancellationToken ct = default) =>
        Uow.ExecuteAsync(async token =>
        {
            var p = await Promotions.CreateAsync(c, token);
            Audit(c.Actor, "PromotionCreated", c.Now, Guid.NewGuid(), p.Id);
            return p.Id;
        }, ct);

    public Task<Guid> PublishPromotionAsync(PublishPromotionCommand c, CancellationToken ct = default) =>
        Uow.ExecuteAsync(async token =>
        {
            await new LegalAcceptanceGate(db, clock ?? TimeProvider.System).EnsureCurrentAcceptedAsync(c.Actor.UserId, LegalRole.Business,
                [LegalDocumentType.BusinessAgreement, LegalDocumentType.AntiCircumventionAgreement], token);
            await Promotions.PublishAsync(c, token);
            Audit(c.Actor, "PromotionPublished", c.Now, Guid.NewGuid(), c.PromotionId);
            return c.PromotionId;
        }, ct);

    public Task<Guid> ActivatePromotionAsync(ActivatePromotionCommand c, CancellationToken ct = default) =>
        Uow.ExecuteAsync(async token =>
        {
            await Promotions.ActivateAsync(c, token);
            Audit(c.Actor, "PromotionActivated", c.Now, Guid.NewGuid(), c.PromotionId);
            return c.PromotionId;
        }, ct);

    public Task<Guid> ReviewCreatorAsync(Actor actor, Guid applicationId, bool approve, DateTime at, CancellationToken ct = default, string? idempotencyKey = null) =>
        new EfUnitOfWork(db, System.Data.IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            DemandBusiness(actor);
            var fingerprint = RequestFingerprint.Create(applicationId.ToString(), approve.ToString());
            if(idempotencyKey is not null)
            {
                var replay=await Replay(actor,"ReviewCreator",idempotencyKey,fingerprint,token);
                if(replay is not null)return replay.Value;
            }
            var application = await db.CreatorApplications.SingleOrDefaultAsync(x => x.Id == applicationId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Application not found.");
            var p = await db.Promotions.SingleAsync(x => x.Id == application.PromotionId, token);
            if (p.BusinessId != actor.BusinessId) throw new ApplicationFailure(FailureKind.Forbidden, "Campaign belongs to another Business.");
            if (approve)
            {
                await new LegalAcceptanceGate(db, clock ?? TimeProvider.System).EnsureCurrentAcceptedAsync(actor.UserId, LegalRole.Business,
                    [LegalDocumentType.BusinessAgreement, LegalDocumentType.AntiCircumventionAgreement], token);
                application.Approve(actor.UserId, at);
            }
            else application.Reject(actor.UserId, at);
            Audit(actor, approve ? "CreatorApproved" : "CreatorRejected", at, Guid.NewGuid(), p.Id, application.CreatorId);
            if(idempotencyKey is not null)await Idempotency.SaveAsync(new(idempotencyKey,"ReviewCreator",actor.UserId,fingerprint,application.Id,at),token);
            return application.Id;
        }, ct);

    public Task<Guid> CreateFinancialConfigurationAsync(Actor actor, FinancialConfigurationVersion version, CancellationToken ct = default) =>
        Uow.ExecuteAsync(async token =>
        {
            if (actor.Role != ActorRole.PlatformAdmin || actor.UserId != version.ChangedBy)
                throw new ApplicationFailure(FailureKind.Forbidden, "Only Platform Admin controls financial configuration.");
            db.FinancialConfigurationVersions.Add(version);
            Audit(actor, "FinancialConfigurationChanged", (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, Guid.NewGuid(), detail: version.Id.ToString());
            await Task.CompletedTask;
            return version.Id;
        }, ct);
}
