using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Web;

namespace Weymela.Infrastructure.Finance;

public sealed record EligibleUgcPublication(UgcAssignment Assignment,
    CreatorPublicationVerification Publication);

/// <summary>
/// One fail-closed authority shared by Customer discovery, QR issuance,
/// scanner resolution, manual checkout, and redemption.
/// </summary>
public sealed class CustomerOfferEligibility(WeymelaDbContext db, ICommerceAccessPolicy access, TimeProvider clock)
{
    public async Task<CreatorPromotionParticipation> RequirePromotionAsync(
        Promotion promotion, CreatorAllocation allocation, CancellationToken ct)
    {
        if (promotion.PromotionType != PromotionType.ViewPlusCommission)
            throw Unavailable();
        var now = clock.GetUtcNow().UtcDateTime;
        try { VerifiedViewService.EnsureCampaignActive(promotion, now); }
        catch (ApplicationFailure ex) { throw Unavailable(ex); }
        await access.EnsureBusinessAsync(promotion.BusinessId, ct);
        if (allocation.PromotionId != promotion.Id || allocation.Status != CreatorAllocationStatus.Active
            || allocation.ActivatedAtUtc is null || allocation.RemainingAmount.Amount <= 0)
            throw Unavailable();
        if (!await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.Role == ActorRole.Creator
                && x.SubjectId == allocation.CreatorId && x.IsActive, ct))
            throw Unavailable();
        var participation = await db.CreatorPromotionParticipations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CreatorAllocationId == allocation.Id
                && x.CreatorId == allocation.CreatorId && x.Status == ParticipationStatus.Active, ct);
        if (participation is null || !participation.IsLive(now, promotion.PromotionLiveDurationDays))
            throw Unavailable();
        var latest = await db.CreatorPromotionContentSubmissions.AsNoTracking()
            .Where(x => x.CreatorAllocationId == allocation.Id)
            .OrderByDescending(x => x.RevisionNumber).FirstOrDefaultAsync(ct);
        if (latest is null || latest.ReviewStatus != PromotionContentReviewStatus.Approved
            || participation.ApprovedContentSubmissionId is { } approvedId && approvedId != latest.Id
            || participation.ApprovedContentSubmissionId is null
                && (latest.Provider != participation.Provider || latest.ContentReference != participation.ExternalContentId)
            || participation.CreatorSocialProfileId is not { } profileId
            || allocation.CreatorSocialProfileId is not { } selected || profileId != selected
            || PublicContentLink.Create(participation.Provider, participation.ExternalContentId) is null)
            throw Unavailable();
        var profile = await db.CreatorSocialProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == profileId && x.CreatorId == allocation.CreatorId, ct);
        var platform = await db.PromotionPlatforms.AsNoTracking().SingleOrDefaultAsync(x =>
            x.PromotionId == promotion.Id && x.Platform.ToString() == participation.Provider, ct);
        if (profile is null || platform is null
            || !await MeetsCurrentAudienceRequirement(profile, platform.Platform, platform.MinimumAudience, ct))
            throw Unavailable();
        return participation;
    }

    public async Task<EligibleUgcPublication> RequireUgcAsync(UgcCustomerOffer offer,
        Guid assignmentId, Guid? creatorId, CancellationToken ct)
    {
        try { offer.EnsureAvailable(clock.GetUtcNow().UtcDateTime); }
        catch (InvalidOperationException ex) { throw Unavailable(ex); }
        await access.EnsureBusinessAsync(offer.BusinessId, ct);
        var opportunity = await db.UgcOpportunities.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == offer.UgcOpportunityId && x.BusinessId == offer.BusinessId, ct)
            ?? throw Unavailable();
        var postingPlatforms = await (from requirement in db.UgcPlatformRequirements.AsNoTracking()
                                      join capacity in db.UgcPlatformCapacities.AsNoTracking()
                                          on new { requirement.UgcOpportunityId, requirement.Platform }
                                          equals new { capacity.UgcOpportunityId, capacity.Platform }
                                      where requirement.UgcOpportunityId == opportunity.Id && capacity.Capacity > 0
                                      select requirement.Platform).Distinct().ToListAsync(ct);
        if (postingPlatforms.Count == 0) throw Unavailable();
        var assignment = await db.UgcAssignments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (assignment is null || assignment.UgcOpportunityId != offer.UgcOpportunityId
            || assignment.Status != UgcAssignmentStatus.Approved
            || creatorId is not null && assignment.CreatorId != creatorId)
            throw Unavailable();
        var request = await db.UgcCreatorRequests.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == assignment.UgcCreatorRequestId && x.CreatorId == assignment.CreatorId
            && x.UgcOpportunityId == offer.UgcOpportunityId && x.Status == UgcRequestStatus.Approved, ct);
        if (request?.SelectedPlatform is null || request.VerifiedSocialProfileId is null
            || !postingPlatforms.Contains(request.SelectedPlatform.Value)
            || !await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.Role == ActorRole.Creator
                && x.SubjectId == assignment.CreatorId && x.IsActive, ct))
            throw Unavailable();
        var profile = await db.CreatorSocialProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == request.VerifiedSocialProfileId.Value && x.CreatorId == assignment.CreatorId, ct);
        var audienceMinimum = await db.UgcPlatformRequirements.AsNoTracking().Where(x =>
                x.UgcOpportunityId == opportunity.Id && x.Platform == request.SelectedPlatform.Value)
            .Select(x => x.MinimumAudience).SingleOrDefaultAsync(ct);
        if (profile is null || !await MeetsCurrentAudienceRequirement(profile,
                request.SelectedPlatform.Value, audienceMinimum, ct))
            throw Unavailable();
        var submission = await db.UgcSubmissions.AsNoTracking().Where(x => x.UgcAssignmentId == assignment.Id)
            .OrderByDescending(x => x.ContentRevisionNumber).FirstOrDefaultAsync(ct);
        if (submission is null || submission.Status != UgcAssignmentStatus.Approved) throw Unavailable();
        var publication = await db.CreatorPublicationVerifications.AsNoTracking().SingleOrDefaultAsync(x =>
            x.UgcAssignmentId == assignment.Id && x.UgcSubmissionId == submission.Id
            && x.CreatorId == assignment.CreatorId && x.CreatorSocialProfileId == request.VerifiedSocialProfileId
            && x.Provider == request.SelectedPlatform.Value.ToString()
            && x.Status == PublicationVerificationStatus.Verified && x.WentLiveAtUtc != null
            && x.EndedAtUtc == null, ct);
        if (publication is null || PublicContentLink.Create(publication.Provider, publication.ExternalContentId) is null)
            throw Unavailable();
        return new(assignment, publication);
    }

    public async Task<IReadOnlyList<EligibleUgcPublication>> EligibleUgcAsync(
        UgcCustomerOffer offer, CancellationToken ct)
    {
        var ids = await db.UgcAssignments.AsNoTracking()
            .Where(x => x.UgcOpportunityId == offer.UgcOpportunityId && x.Status == UgcAssignmentStatus.Approved)
            .Select(x => x.Id).ToListAsync(ct);
        var result = new List<EligibleUgcPublication>();
        foreach (var id in ids)
        {
            try { result.Add(await RequireUgcAsync(offer, id, null, ct)); }
            catch (ApplicationFailure) { }
        }
        return result;
    }

    private static ApplicationFailure Unavailable(Exception? inner = null) =>
        new(FailureKind.Validation, "Offer is no longer available.", inner, "NOT_ELIGIBLE");

    private async Task<bool> MeetsCurrentAudienceRequirement(CreatorSocialProfileRecord profile,
        CreatorPlatform platform, long? minimumAudience, CancellationToken ct)
    {
        var enforce = (await new FinancialConfigurationResolver(db)
            .EffectiveAsync(clock.GetUtcNow().UtcDateTime, ct)).EnforceAudienceRequirements;
        return SocialAudienceEligibility.Matches(profile, platform, minimumAudience, enforce);
    }
}
