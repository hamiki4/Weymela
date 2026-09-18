using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Finance;

public sealed class VerifiedViewService(WeymelaDbContext db, IVerifiedViewProvider provider, ICommerceAccessPolicy access, TimeProvider clock)
{
    public async Task<Guid> GoLiveAsync(GoLiveCommand c, CancellationToken ct = default)
    {
        var allocation = await db.CreatorAllocations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == c.AllocationId, ct)
            ?? throw new ApplicationFailure(FailureKind.NotFound, "Creator Budget not found.");
        await access.EnsureCreatorAsync(c.Actor, allocation.CreatorId, ct);
        var result = await provider.VerifyAsync(new(allocation.CreatorId, allocation.PromotionId, c.Provider, c.ExternalContentId), ct);
        ValidateProvider(result, c.Provider, c.ExternalContentId);
        return await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var op = new FinancialOperation(db);
            var fp = RequestFingerprint.Create(c.AllocationId.ToString(), c.Provider, c.ExternalContentId);
            var replay = await op.Replay(c.Actor, "GoLive", c.IdempotencyKey, fp, token);
            if (replay is not null) return Guid.Parse(replay);
            var p = await new PromotionRepository(db).GetAsync(allocation.PromotionId, token) ?? throw new ApplicationFailure(FailureKind.NotFound, "Promotion not found.");
            var a = p.Allocations.Single(x => x.Id == c.AllocationId);
            await access.EnsureCreatorAsync(c.Actor, a.CreatorId, token);
            await access.EnsureBusinessAsync(p.BusinessId, token);
            EnsureCampaignActive(p, clock.GetUtcNow().UtcDateTime);
            await new LegalAcceptanceGate(db, clock).EnsureCurrentAcceptedAsync(c.Actor.UserId, LegalRole.Creator,
                [LegalDocumentType.CreatorAgreement, LegalDocumentType.AntiCircumventionAgreement], token);
            var existing = await db.CreatorPromotionParticipations.SingleOrDefaultAsync(x => x.CreatorAllocationId == a.Id, token);
            if (existing is not null)
            {
                if (existing.Provider != c.Provider || existing.ExternalContentId != c.ExternalContentId)
                    throw new ApplicationFailure(FailureKind.Validation, "Content baseline is already bound and cannot be replaced.");
                op.Remember(c.Actor, "GoLive", c.IdempotencyKey, fp, existing.Id.ToString(), clock.GetUtcNow().UtcDateTime);
                return existing.Id;
            }
            if (a.Status != CreatorAllocationStatus.Active || a.RemainingAmount.Amount <= 0)
                throw new ApplicationFailure(FailureKind.InsufficientFunds, "Creator Budget is not active and funded.");
            a.Activate(clock.GetUtcNow().UtcDateTime);
            var participation = new CreatorPromotionParticipation(p.Id, a.CreatorId, a.Id, c.Provider, c.ExternalContentId, result.Count, result.VerifiedAtUtc);
            db.CreatorPromotionParticipations.Add(participation);
            db.PromotionViewVerifications.Add(new(p.Id, a.CreatorId, a.Id, c.Provider, c.ExternalContentId, result.Count, result.Count, 0,
                result.VerifiedAtUtc, result.EvidenceReference, Guid.NewGuid().ToString("N"), result.Count, false, participation.Id, true));
            op.Remember(c.Actor, "GoLive", c.IdempotencyKey, fp, participation.Id.ToString(), clock.GetUtcNow().UtcDateTime);
            op.Audit(c.Actor, "CreatorParticipationActivated", Guid.NewGuid(), clock.GetUtcNow().UtcDateTime, p.Id, a.CreatorId);
            op.Event("CreatorParticipationActivated", new { ParticipationId = participation.Id }, clock.GetUtcNow().UtcDateTime);
            return participation.Id;
        }, ct);
    }

    public async Task<ViewRewardResult> RefreshAsync(RefreshViewsCommand c, CancellationToken ct = default)
    {
        var read = await db.CreatorPromotionParticipations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == c.ParticipationId, ct)
            ?? throw new ApplicationFailure(FailureKind.NotFound, "Participation not found.");
        await access.EnsureCreatorAsync(c.Actor, read.CreatorId, ct);
        var fp = RequestFingerprint.Create(c.ParticipationId.ToString());
        var replay = await new FinancialOperation(db).Replay(c.Actor, "RefreshViews", c.IdempotencyKey, fp, ct);
        if (replay is not null) return JsonSerializer.Deserialize<ViewRewardResult>(replay)!;
        var verified = await provider.VerifyAsync(new(read.CreatorId, read.PromotionId, read.Provider, read.ExternalContentId), ct);
        ValidateProvider(verified, read.Provider, read.ExternalContentId);
        return await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var op = new FinancialOperation(db);
            var duplicate = await op.Replay(c.Actor, "RefreshViews", c.IdempotencyKey, fp, token);
            if (duplicate is not null) return JsonSerializer.Deserialize<ViewRewardResult>(duplicate)!;
            var participation = await db.CreatorPromotionParticipations.SingleAsync(x => x.Id == c.ParticipationId, token);
            await access.EnsureCreatorAsync(c.Actor, participation.CreatorId, token);
            var p = (await new PromotionRepository(db).GetAsync(participation.PromotionId, token))!;
            await access.EnsureBusinessAsync(p.BusinessId, token);
            if (p.Status is not (PromotionStatus.Active or PromotionStatus.BudgetExhausted) ||
                clock.GetUtcNow().UtcDateTime < p.StartDateUtc || clock.GetUtcNow().UtcDateTime >= p.EndDateUtc ||
                participation.Status is ParticipationStatus.Paused or ParticipationStatus.Completed)
                throw new ApplicationFailure(FailureKind.Validation, "Participation is not open for verified rewards.");
            var allocation = p.Allocations.Single(x => x.Id == participation.CreatorAllocationId);
            if (allocation.Status is CreatorAllocationStatus.Completed or CreatorAllocationStatus.Cancelled)
                throw new ApplicationFailure(FailureKind.Validation, "Creator Budget is closed.");
            SnapshotPricing.Validate(p.PricingSnapshot);
            var previous = participation.LatestVerifiedViews;
            var anomaly = participation.Observe(verified.Count, verified.VerifiedAtUtc);
            var blocks = participation.CompleteUnpaidBlocks(p.PricingSnapshot.ViewsPerReward);
            var affordable = Math.Min(blocks, (long)Math.Min(long.MaxValue, decimal.Floor(allocation.RemainingAmount.Amount / p.PricingSnapshot.BusinessCharge.Amount)));
            if (affordable > 0)
            {
                var amounts = new SaleAmounts(new Money(p.PricingSnapshot.CreatorEarning.Amount * affordable),
                    Money.Zero(), new Money(p.PricingSnapshot.PlatformEarning.Amount * affordable));
                var j = await new AttributableFinance(db).PostAsync(c.Actor, p, allocation, amounts, JournalSourceType.ViewReward, c.IdempotencyKey,
                    clock.GetUtcNow().UtcDateTime, null, token);
                participation.Reward(affordable, p.PricingSnapshot.ViewsPerReward);
                db.ViewRewardReceipts.Add(new(Guid.NewGuid(), participation.Id, j.Id, affordable, participation.RewardedViewCount,
                    amounts.Total, amounts.Creator, amounts.Platform, clock.GetUtcNow().UtcDateTime));
            }
            var fundingRequired = blocks > affordable || allocation.RemainingAmount.Amount == 0;
            participation.SetFundingRequired(fundingRequired);
            if (fundingRequired)
                op.Event("CreatorBudgetExhausted", new { AllocationId = allocation.Id, ParticipationId = participation.Id }, clock.GetUtcNow().UtcDateTime);
            db.PromotionViewVerifications.Add(new(p.Id, allocation.CreatorId, allocation.Id, verified.Provider, verified.ExternalContentId,
                previous, participation.LatestVerifiedViews, participation.RewardedViewCount, verified.VerifiedAtUtc, verified.EvidenceReference,
                Guid.NewGuid().ToString("N"), verified.Count, anomaly, participation.Id));
            op.Audit(c.Actor, anomaly ? "VerifiedViewAnomaly" : "VerifiedViewsRecorded", Guid.NewGuid(), clock.GetUtcNow().UtcDateTime, p.Id, allocation.CreatorId);
            var result = new ViewRewardResult(participation.Id, participation.CampaignVerifiedViews, participation.RewardedViewCount,
                participation.CampaignVerifiedViews % p.PricingSnapshot.ViewsPerReward, participation.Status);
            op.Remember(c.Actor, "RefreshViews", c.IdempotencyKey, fp, JsonSerializer.Serialize(result), clock.GetUtcNow().UtcDateTime);
            return result;
        }, ct);
    }

    public Task<Guid> SetPausedAsync(Actor actor, Guid id, bool paused, CancellationToken ct = default) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var p = await db.CreatorPromotionParticipations.SingleAsync(x => x.Id == id, token);
            await access.EnsureCreatorAsync(actor, p.CreatorId, token);
            if (paused) p.Pause(); else p.Resume();
            new FinancialOperation(db).Audit(actor, paused ? "ParticipationPaused" : "ParticipationResumed", Guid.NewGuid(),
                clock.GetUtcNow().UtcDateTime, p.PromotionId, p.CreatorId);
            return p.Id;
        }, ct);

    internal static void EnsureCampaignActive(Promotion p, DateTime now)
    {
        if (p.Status != PromotionStatus.Active || now < p.StartDateUtc || now >= p.EndDateUtc)
            throw new ApplicationFailure(FailureKind.Validation, "Promotion is not active.");
    }
    private void ValidateProvider(VerifiedViewResult result, string providerName, string contentId)
    {
        if (result.Count < 0 || result.Provider != providerName || result.ExternalContentId != contentId ||
            result.VerifiedAtUtc.Kind != DateTimeKind.Utc || result.VerifiedAtUtc > clock.GetUtcNow().UtcDateTime ||
            string.IsNullOrWhiteSpace(result.EvidenceReference))
            throw new ApplicationFailure(FailureKind.Validation, "Invalid verified-view provider evidence.");
    }
}
