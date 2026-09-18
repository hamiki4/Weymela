using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Repositories;

namespace Weymela.Infrastructure.Persistence.Transactions;

public sealed partial class FinancialCommands
{
    public Task<Guid> CreateCampaignOnceAsync(CreatePromotionCommand c, string key, CancellationToken ct = default) => Uow.ExecuteAsync(async token =>
    {
        DemandBusiness(c.Actor); var fp = RequestFingerprint.Create(c.Actor.BusinessId.ToString()!, c.Title, c.Description, c.Type.ToString(), RequestFingerprint.Amount(c.TotalBudget),
            c.Eligibility.Category ?? "", c.Eligibility.Market ?? "", c.Eligibility.MinimumVerifiedFollowers?.ToString() ?? "", c.Eligibility.Requirements ?? "", c.StartDateUtc.ToString("O"), c.EndDateUtc.ToString("O"),
            c.Slogan??"",c.Location??"",c.ResourcesJson??"",string.Join(',',c.Platforms?.OrderBy(x=>x.Platform).Select(x=>$"{x.Platform}:{x.Capacity}")??[]));
        var prior = await Replay(c.Actor, "CreateCampaign", key, fp, token); if (prior is not null) return prior.Value;
        var p = await Promotions.CreateAsync(c, token);
        await Idempotency.SaveAsync(new(key, "CreateCampaign", c.Actor.UserId, fp, p.Id, c.Now), token);
        Audit(c.Actor, "PromotionCreated", c.Now, Guid.NewGuid(), p.Id); return p.Id;
    }, ct);

    public Task<Guid> PublishCampaignOnceAsync(PublishPromotionCommand c, string key, bool activateOnly = false, CancellationToken ct = default) => Uow.ExecuteAsync(async token =>
    {
        DemandBusiness(c.Actor); var op = activateOnly ? "StartCampaign" : "PublishCampaign"; var fp = RequestFingerprint.Create(c.PromotionId.ToString());
        var prior = await Replay(c.Actor, op, key, fp, token); if (prior is not null) return prior.Value;
        var p = await new PromotionRepository(db).GetAsync(c.PromotionId, token) ?? throw new ApplicationFailure(FailureKind.NotFound, "Promotion not found.");
        if (p.BusinessId != c.Actor.BusinessId) throw new ApplicationFailure(FailureKind.Forbidden, "Promotion belongs to another Business.");
        if (c.Now >= p.EndDateUtc) throw new ApplicationFailure(FailureKind.Validation, "This Promotion's end date has passed.");
        await new LegalAcceptanceGate(db, clock ?? TimeProvider.System).EnsureCurrentAcceptedAsync(c.Actor.UserId, LegalRole.Business,
            [LegalDocumentType.BusinessAgreement, LegalDocumentType.AntiCircumventionAgreement], token);
        if (!activateOnly) { await Promotions.PublishAsync(c, token); Audit(c.Actor, "PromotionPublished", c.Now, Guid.NewGuid(), p.Id); }
        if (activateOnly && c.Now < p.StartDateUtc) throw new ApplicationFailure(FailureKind.Validation, "This Promotion cannot start before its start date.");
        if (c.Now >= p.StartDateUtc)
        {
            // Both changes belong to this same loaded aggregate/version and commit together.
            await Promotions.ActivateAsync(new(c.Actor, p.Id, c.ExpectedVersion, c.Now), token);
            Audit(c.Actor, "PromotionActivated", c.Now, Guid.NewGuid(), p.Id);
        }
        await Idempotency.SaveAsync(new(key, op, c.Actor.UserId, fp, p.Id, c.Now), token); return p.Id;
    }, ct);

    public Task<Guid> ApproveAndSetBudgetAsync(Actor actor, Guid applicationId, Money amount, long expectedCampaignVersion, string key, DateTime now, CancellationToken ct = default) =>
        new EfUnitOfWork(db, System.Data.IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            DemandBusiness(actor); var fp = RequestFingerprint.Create(applicationId.ToString(), RequestFingerprint.Amount(amount));
            var prior = await Replay(actor, "ApproveAndSetBudget", key, fp, token); if (prior is not null) return prior.Value;
            var app = await db.CreatorApplications.SingleOrDefaultAsync(x => x.Id == applicationId, token) ?? throw new ApplicationFailure(FailureKind.NotFound, "Creator request not found.");
            var p = await new PromotionRepository(db).GetAsync(app.PromotionId, token) ?? throw new ApplicationFailure(FailureKind.NotFound, "Promotion not found.");
            if (p.BusinessId != actor.BusinessId) throw new ApplicationFailure(FailureKind.Forbidden, "Promotion belongs to another Business.");
            await new LegalAcceptanceGate(db, clock ?? TimeProvider.System).EnsureCurrentAcceptedAsync(actor.UserId, LegalRole.Business,
                [LegalDocumentType.BusinessAgreement, LegalDocumentType.AntiCircumventionAgreement], token);
            if (app.Status == CreatorApplicationStatus.Pending) app.Approve(actor.UserId, now);
            else if (app.Status != CreatorApplicationStatus.Approved) throw new ApplicationFailure(FailureKind.Validation, "This Creator request cannot be approved.");
            var a = await Participation.AssignAsync(new(actor, p.Id, app.CreatorId, amount, expectedCampaignVersion, now), token);
            var journal = Journal(actor, "Allocation", amount, "CampaignUnallocatedReserve", "CreatorAllocatedReserve", now, key, p.Id, app.CreatorId);
            db.PromotionBudgetEntries.Add(new(Guid.NewGuid(), p.Id, a.Id, amount, "Assigned", journal.Id, now));
            await Idempotency.SaveAsync(new(key, "ApproveAndSetBudget", actor.UserId, fp, a.Id, now), token);
            Audit(actor, "CreatorApproved", now, journal.CorrelationId, p.Id, app.CreatorId); Audit(actor, "CreatorBudgetAssigned", now, journal.CorrelationId, p.Id, app.CreatorId);
            return a.Id;
        }, ct);

    public Task<Guid> JoinCampaignOnceAsync(ApplyToPromotionCommand c, string key, CancellationToken ct = default) => Uow.ExecuteAsync(async token =>
    {
        if (c.Actor.Role != ActorRole.Creator || c.Actor.CreatorId is null) throw new ApplicationFailure(FailureKind.Forbidden, "Only Creators can request to join.");
        var fp = RequestFingerprint.Create(c.PromotionId.ToString(), c.Message ?? "", c.ContentConcept ?? "",c.Platform?.ToString()??"",c.CreatorSocialProfileId?.ToString()??"");
        var prior = await Replay(c.Actor, "JoinCampaign", key, fp, token); if (prior is not null) return prior.Value;
        var p = await new PromotionRepository(db).GetAsync(c.PromotionId, token) ?? throw new ApplicationFailure(FailureKind.NotFound, "Promotion not found.");
        if (p.ReservedBudget.Amount <= 0 || p.UnallocatedBudget.Amount <= 0 || p.EndDateUtc <= c.Now || !new CreatorEligibility().IsEligible(c.Actor.CreatorId.Value, p.Eligibility, c.Profile))
            throw new ApplicationFailure(FailureKind.Validation, "This Promotion does not match your current profile or has no available Creator capacity.");
        if (!await db.CommercePermissions.AnyAsync(x => x.SubjectId == p.BusinessId && x.Role == ActorRole.Business && x.IsActive, token))
            throw new ApplicationFailure(FailureKind.Validation, "This Promotion is not accepting requests.");
        if(c.Platform is {} platform && (c.CreatorSocialProfileId is not {} profileId || !await db.CreatorSocialProfiles.AnyAsync(x=>x.Id==profileId&&x.CreatorId==c.Actor.CreatorId&&x.Platform==platform&&x.IsActive,token)))
            throw new ApplicationFailure(FailureKind.Forbidden,"The selected Creator social profile is not available.");
        await new LegalAcceptanceGate(db, clock ?? TimeProvider.System).EnsureCurrentAcceptedAsync(c.Actor.UserId, LegalRole.Creator,
            [LegalDocumentType.CreatorAgreement, LegalDocumentType.AntiCircumventionAgreement], token);
        var app = await Participation.ApplyAsync(c, token);
        await Idempotency.SaveAsync(new(key, "JoinCampaign", c.Actor.UserId, fp, app.Id, c.Now), token);
        Audit(c.Actor, "CreatorApplied", c.Now, Guid.NewGuid(), p.Id, c.Actor.CreatorId); return app.Id;
    }, ct);
}
