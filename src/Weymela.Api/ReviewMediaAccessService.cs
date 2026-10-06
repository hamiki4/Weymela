using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Api;

public sealed class ReviewMediaAccessService(WeymelaDbContext db, PrivateReviewMediaStore store,
    ICommerceAccessPolicy access)
{
    public async Task<(FileStream Stream, string ContentType)> OpenPromotionAsync(
        Actor actor, Guid submissionId, CancellationToken ct)
    {
        var row = await (from submission in db.CreatorPromotionContentSubmissions.AsNoTracking()
                         join asset in db.PrivateReviewMediaAssets.AsNoTracking()
                             on submission.ReviewMediaAssetId equals asset.Id
                         join allocation in db.CreatorAllocations.AsNoTracking()
                             on submission.CreatorAllocationId equals allocation.Id
                         join promotion in db.Promotions.AsNoTracking()
                             on allocation.PromotionId equals promotion.Id
                         where submission.Id == submissionId
                         select new { Asset = asset, Allocation = allocation, Promotion = promotion })
            .SingleOrDefaultAsync(ct)
            ?? throw new ApplicationFailure(FailureKind.NotFound, "Review media not found.");
        await DemandReadAsync(actor, row.Asset.CreatorId, row.Promotion.BusinessId, ct);
        return (store.Open(row.Asset), row.Asset.ContentType);
    }

    public async Task<(FileStream Stream, string ContentType)> OpenUgcAsync(
        Actor actor, Guid submissionId, CancellationToken ct)
    {
        var row = await (from submission in db.UgcSubmissions.AsNoTracking()
                         join asset in db.PrivateReviewMediaAssets.AsNoTracking()
                             on submission.ReviewMediaAssetId equals asset.Id
                         join assignment in db.UgcAssignments.AsNoTracking()
                             on submission.UgcAssignmentId equals assignment.Id
                         join opportunity in db.UgcOpportunities.AsNoTracking()
                             on assignment.UgcOpportunityId equals opportunity.Id
                         where submission.Id == submissionId
                         select new { Asset = asset, Assignment = assignment, Opportunity = opportunity })
            .SingleOrDefaultAsync(ct)
            ?? throw new ApplicationFailure(FailureKind.NotFound, "Review media not found.");
        await DemandReadAsync(actor, row.Asset.CreatorId, row.Opportunity.BusinessId, ct);
        return (store.Open(row.Asset), row.Asset.ContentType);
    }

    private async Task DemandReadAsync(Actor actor, Guid creatorId, Guid businessId, CancellationToken ct)
    {
        if (actor.Role == ActorRole.Creator && actor.CreatorId == creatorId)
        {
            await access.EnsureCreatorAsync(actor, creatorId, ct);
            return;
        }
        if (actor.Role == ActorRole.Business && actor.BusinessId == businessId)
        {
            if (await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == actor.UserId
                    && x.Role == ActorRole.Business && x.SubjectId == businessId && x.IsActive, ct))
            {
                await access.EnsureBusinessAsync(businessId, ct);
                return;
            }
        }
        if (actor.Role == ActorRole.PlatformAdmin) return;
        throw new ApplicationFailure(FailureKind.Forbidden,
            "This private review file is not available to this workspace.");
    }
}
