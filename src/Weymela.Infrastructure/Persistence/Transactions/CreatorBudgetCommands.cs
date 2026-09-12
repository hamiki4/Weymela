using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;

namespace Weymela.Infrastructure.Persistence.Transactions;

public sealed partial class FinancialCommands
{
    public Task<Guid> AssignCreatorAllocationAsync(AssignCreatorAllocationCommand c, string key, CancellationToken ct = default) =>
        Uow.ExecuteAsync(async token =>
        {
            DemandBusiness(c.Actor);
            var fp = RequestFingerprint.Create(c.Actor.BusinessId.ToString()!, c.PromotionId.ToString(), c.CreatorId.ToString(), RequestFingerprint.Amount(c.Amount));
            var prior = await Replay(c.Actor, "AssignAllocation", key, fp, token);
            if (prior is not null) return prior.Value;
            var a = await Participation.AssignAsync(c, token);
            var journal = Journal(c.Actor, "Allocation", c.Amount, "CampaignUnallocatedReserve", "CreatorAllocatedReserve", c.Now, key, c.PromotionId, c.CreatorId);
            db.PromotionBudgetEntries.Add(new(Guid.NewGuid(), c.PromotionId, a.Id, c.Amount, "Assigned", journal.Id, c.Now));
            await Idempotency.SaveAsync(new(key, "AssignAllocation", c.Actor.UserId, fp, a.Id, c.Now), token);
            Audit(c.Actor, "CreatorBudgetAssigned", c.Now, journal.CorrelationId, c.PromotionId, c.CreatorId);
            return a.Id;
        }, ct);

    public Task<Guid> IncreaseCreatorAllocationAsync(IncreaseCreatorAllocationCommand c, string key, CancellationToken ct = default) =>
        Uow.ExecuteAsync(async token =>
        {
            DemandBusiness(c.Actor);
            var fp = RequestFingerprint.Create(c.Actor.BusinessId.ToString()!, c.AllocationId.ToString(), RequestFingerprint.Amount(c.Amount));
            var prior = await Replay(c.Actor, "IncreaseAllocation", key, fp, token);
            if (prior is not null) return prior.Value;
            await Participation.IncreaseAsync(c, token);
            var a = (await new CreatorAllocationRepository(db).GetAsync(c.AllocationId, token))!;
            var journal = Journal(c.Actor, "Allocation", c.Amount, "CampaignUnallocatedReserve", "CreatorAllocatedReserve", c.Now, key, a.PromotionId, a.CreatorId);
            db.PromotionBudgetEntries.Add(new(Guid.NewGuid(), a.PromotionId, a.Id, c.Amount, "Increased", journal.Id, c.Now));
            await Idempotency.SaveAsync(new(key, "IncreaseAllocation", c.Actor.UserId, fp, a.Id, c.Now), token);
            Audit(c.Actor, "CreatorBudgetIncreased", c.Now, journal.CorrelationId, a.PromotionId, a.CreatorId);
            return a.Id;
        }, ct);

    public Task<Guid> CompleteCreatorParticipationAsync(CompleteCreatorParticipationCommand c, string key, CancellationToken ct = default) =>
        Uow.ExecuteAsync(async token =>
        {
            if (c.Actor.Role is not (ActorRole.Business or ActorRole.Creator)) throw new ApplicationFailure(FailureKind.Forbidden, "Only an owning participant may complete.");
            var fp = RequestFingerprint.Create(c.Actor.BusinessId.ToString()!, c.Actor.CreatorId.ToString()!, c.AllocationId.ToString());
            var prior = await Replay(c.Actor, "CompleteParticipation", key, fp, token);
            if (prior is not null) return prior.Value;
            var a = await new CreatorAllocationRepository(db).GetAsync(c.AllocationId, token) ?? throw new ApplicationFailure(FailureKind.NotFound, "Allocation not found.");
            if (a.Status is CreatorAllocationStatus.Completed or CreatorAllocationStatus.Cancelled)
                throw new ApplicationFailure(FailureKind.Validation, "Participation already completed.");
            var unused = a.RemainingAmount;
            await Participation.CompleteAsync(c, token);
            var live = await db.CreatorPromotionParticipations.SingleOrDefaultAsync(x => x.CreatorAllocationId == a.Id, token);
            live?.Complete();
            var p = (await new PromotionRepository(db).GetAsync(a.PromotionId, token))!;
            var correlation = Guid.NewGuid();
            if (unused.Amount > 0)
            {
                var j = Journal(c.Actor with { BusinessId = p.BusinessId }, "Allocation", unused, "CreatorAllocatedReserve", "CampaignUnallocatedReserve", c.Now, key, a.PromotionId, a.CreatorId, correlation);
                db.PromotionBudgetEntries.Add(new(Guid.NewGuid(), a.PromotionId, a.Id, unused, "UnusedReturnedToCampaign", j.Id, c.Now));
            }
            await Idempotency.SaveAsync(new(key, "CompleteParticipation", c.Actor.UserId, fp, a.Id, c.Now), token);
            Audit(c.Actor, "CreatorParticipationCompleted", c.Now, correlation, a.PromotionId, a.CreatorId);
            return a.Id;
        }, ct);

    private static void DemandBusiness(Actor actor)
    {
        if (actor.Role != ActorRole.Business || actor.BusinessId is null)
            throw new ApplicationFailure(FailureKind.Forbidden, "Only the owning Business may allocate campaign funds.");
    }
}
