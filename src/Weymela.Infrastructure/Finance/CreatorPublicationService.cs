using System.Data;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Web;

namespace Weymela.Infrastructure.Finance;

public static class PublicContentLink
{
    public static string? Create(string provider, string content) => provider switch
    {
        "TikTok" when content.All(char.IsAsciiDigit) => "https://www.tiktok.com/@creator/video/" + content,
        "YouTube" when Safe(content) => "https://www.youtube.com/watch?v=" + content,
        "Instagram" when Safe(content) => "https://www.instagram.com/p/" + content + "/",
        _ => null
    };

    private static bool Safe(string value) => value.Length is > 0 and <= 100
        && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_');
}

public sealed class CreatorPublicationService(WeymelaDbContext db, IVerifiedViewProvider provider,
    ICommerceAccessPolicy access, IWorkspaceDirectory directory, TimeProvider clock)
{
    public async Task<PublicationStatusView> RequestPromotionAsync(Actor actor, Guid allocationId,
        PublicationInput input, string key, CancellationToken ct)
    {
        var publicationId = await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var allocation = await db.CreatorAllocations.SingleOrDefaultAsync(x => x.Id == allocationId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Creator work not found.");
            await access.EnsureCreatorAsync(actor, allocation.CreatorId, token);
            var promotion = await db.Promotions.SingleAsync(x => x.Id == allocation.PromotionId, token);
            await access.EnsureBusinessAsync(promotion.BusinessId, token);
            VerifiedViewService.EnsureCampaignActive(promotion, clock.GetUtcNow().UtcDateTime);
            var submission = await CurrentApprovedPromotionSubmission(allocation.Id, token);
            if (submission.ReviewMediaAssetId is null)
                throw new ApplicationFailure(FailureKind.Validation, "Submit a private review copy before publication.");
            var profile = await EligibleSocialProfile(allocation.CreatorId, allocation.CreatorSocialProfileId,
                input, token);
            var minimumAudience = await db.PromotionPlatforms.AsNoTracking().Where(x => x.PromotionId == promotion.Id
                    && x.Platform == profile.Platform && x.Capacity > 0)
                .Select(x => x.MinimumAudience).SingleOrDefaultAsync(token);
            await EnsureAudienceAsync(profile, minimumAudience, token);
            var fingerprint = RequestFingerprint.Create(allocation.Id.ToString(), submission.Id.ToString(),
                profile.Id.ToString(), input.Provider, input.ExternalContentId);
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "RequestPromotionPublication", key, fingerprint, token) is { } replay)
                return Guid.Parse(replay);
            if (await db.CreatorPublicationVerifications.AnyAsync(x => x.CreatorAllocationId == allocation.Id
                    && (x.Status == PublicationVerificationStatus.VerificationPending
                        || x.Status == PublicationVerificationStatus.Verified), token))
                throw new ApplicationFailure(FailureKind.Validation, "Publication verification is already in progress for this work.");
            var publication = new CreatorPublicationVerification(allocation.CreatorId, profile.Id,
                allocation.Id, null, submission.Id, null, input.Provider, input.ExternalContentId,
                clock.GetUtcNow().UtcDateTime);
            db.CreatorPublicationVerifications.Add(publication);
            operation.Remember(actor, "RequestPromotionPublication", key, fingerprint, publication.Id.ToString(),
                clock.GetUtcNow().UtcDateTime);
            operation.Audit(actor, "PublicationVerificationRequested", Guid.NewGuid(),
                clock.GetUtcNow().UtcDateTime, promotion.Id, allocation.CreatorId);
            operation.Event("PublicationVerificationRequested", new { PublicationId = publication.Id,
                PromotionId = promotion.Id, CreatorId = allocation.CreatorId }, clock.GetUtcNow().UtcDateTime);
            return publication.Id;
        }, ct);
        await TryProviderVerificationAsync(publicationId, ct);
        return await StatusAsync(publicationId, ct);
    }

    public async Task<PublicationStatusView> RequestUgcAsync(Actor actor, Guid assignmentId,
        PublicationInput input, string key, CancellationToken ct)
    {
        var publicationId = await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var assignment = await db.UgcAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC work not found.");
            await access.EnsureCreatorAsync(actor, assignment.CreatorId, token);
            if (assignment.Status != UgcAssignmentStatus.Approved)
                throw new ApplicationFailure(FailureKind.Validation, "Business approval is required before publication.");
            var opportunity = await db.UgcOpportunities.SingleAsync(x => x.Id == assignment.UgcOpportunityId, token);
            await access.EnsureBusinessAsync(opportunity.BusinessId, token);
            var submission = await CurrentApprovedUgcSubmission(assignment.Id, token);
            if (submission.ReviewMediaAssetId is null)
                throw new ApplicationFailure(FailureKind.Validation, "Submit a private review copy before publication.");
            var request = await db.UgcCreatorRequests.AsNoTracking().SingleAsync(x => x.Id == assignment.UgcCreatorRequestId, token);
            if (request.SelectedPlatform is null || request.VerifiedSocialProfileId is null)
                throw new ApplicationFailure(FailureKind.Validation, "This delivery-only UGC work does not require social publication.");
            var profile = await EligibleSocialProfile(assignment.CreatorId, request.VerifiedSocialProfileId, input, token);
            var capacity = await db.UgcPlatformCapacities.AsNoTracking().SingleOrDefaultAsync(x =>
                x.UgcOpportunityId == opportunity.Id && x.Platform == profile.Platform && x.Capacity > 0, token);
            var requirement = await db.UgcPlatformRequirements.AsNoTracking().SingleOrDefaultAsync(x =>
                x.UgcOpportunityId == opportunity.Id && x.Platform == profile.Platform, token);
            if (capacity is null || requirement is null)
                throw new ApplicationFailure(FailureKind.Validation, "The selected publication platform is no longer required for this work.");
            await EnsureAudienceAsync(profile, requirement.MinimumAudience, token);
            var fingerprint = RequestFingerprint.Create(assignment.Id.ToString(), submission.Id.ToString(),
                profile.Id.ToString(), input.Provider, input.ExternalContentId);
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "RequestUgcPublication", key, fingerprint, token) is { } replay)
                return Guid.Parse(replay);
            if (await db.CreatorPublicationVerifications.AnyAsync(x => x.UgcAssignmentId == assignment.Id
                    && (x.Status == PublicationVerificationStatus.VerificationPending
                        || x.Status == PublicationVerificationStatus.Verified), token))
                throw new ApplicationFailure(FailureKind.Validation, "Publication verification is already in progress for this work.");
            var publication = new CreatorPublicationVerification(assignment.CreatorId, profile.Id,
                null, assignment.Id, null, submission.Id, input.Provider, input.ExternalContentId,
                clock.GetUtcNow().UtcDateTime);
            db.CreatorPublicationVerifications.Add(publication);
            operation.Remember(actor, "RequestUgcPublication", key, fingerprint, publication.Id.ToString(),
                clock.GetUtcNow().UtcDateTime);
            operation.Audit(actor, "UgcPublicationVerificationRequested", Guid.NewGuid(),
                clock.GetUtcNow().UtcDateTime, null, assignment.CreatorId);
            operation.Event("UgcPublicationVerificationRequested", new { PublicationId = publication.Id,
                OpportunityId = opportunity.Id, AssignmentId = assignment.Id, CreatorId = assignment.CreatorId },
                clock.GetUtcNow().UtcDateTime);
            return publication.Id;
        }, ct);
        await TryProviderVerificationAsync(publicationId, ct);
        return await StatusAsync(publicationId, ct);
    }

    public Task<Guid> GoLivePromotionAsync(Actor actor, Guid allocationId, string key, CancellationToken ct) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var allocation = await db.CreatorAllocations.SingleOrDefaultAsync(x => x.Id == allocationId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Creator work not found.");
            await access.EnsureCreatorAsync(actor, allocation.CreatorId, token);
            var promotion = await new PromotionRepository(db).GetAsync(allocation.PromotionId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Promotion not found.");
            await access.EnsureBusinessAsync(promotion.BusinessId, token);
            var now = clock.GetUtcNow().UtcDateTime;
            VerifiedViewService.EnsureCampaignActive(promotion, now);
            var submission = await CurrentApprovedPromotionSubmission(allocation.Id, token);
            var publication = await db.CreatorPublicationVerifications.SingleOrDefaultAsync(x =>
                x.CreatorAllocationId == allocation.Id && x.PromotionContentSubmissionId == submission.Id
                && x.Status == PublicationVerificationStatus.Verified, token)
                ?? throw new ApplicationFailure(FailureKind.Validation, "Publication verification must be complete before Go Live.");
            var publicationProfile = await EligibleSocialProfile(allocation.CreatorId, allocation.CreatorSocialProfileId,
                new(publication.Provider, publication.ExternalContentId, publication.CreatorSocialProfileId), token);
            var minimumAudience = promotion.Platforms.SingleOrDefault(x => x.Platform == publicationProfile.Platform)?.MinimumAudience;
            await EnsureAudienceAsync(publicationProfile, minimumAudience, token);
            var fingerprint = RequestFingerprint.Create(allocation.Id.ToString(), publication.Id.ToString());
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "GoLiveVerifiedPublication", key, fingerprint, token) is { } replay)
                return Guid.Parse(replay);
            var existing = await db.CreatorPromotionParticipations.SingleOrDefaultAsync(x => x.CreatorAllocationId == allocation.Id, token);
            if (existing is not null)
            {
                if (existing.CreatorId != allocation.CreatorId
                    || existing.Provider != publication.Provider
                    || existing.ExternalContentId != publication.ExternalContentId
                    || existing.CreatorSocialProfileId != publication.CreatorSocialProfileId
                    || existing.ApprovedContentSubmissionId is { } approvedId && approvedId != submission.Id)
                    throw new ApplicationFailure(FailureKind.Validation,
                        "Live content is already bound to a different approved publication.");
                operation.Remember(actor, "GoLiveVerifiedPublication", key, fingerprint, existing.Id.ToString(), now);
                return existing.Id;
            }
            if (allocation.Status != CreatorAllocationStatus.Active || allocation.RemainingAmount.Amount <= 0)
                throw new ApplicationFailure(FailureKind.InsufficientFunds, "Creator Budget is not active and funded.");
            publication.GoLive(now);
            allocation.Activate(now);
            var participation = new CreatorPromotionParticipation(promotion.Id, allocation.CreatorId, allocation.Id,
                publication.Provider, publication.ExternalContentId, publication.BaselineViews!.Value, now,
                publication.VerifiedAtUtc!.Value, submission.Id, publication.CreatorSocialProfileId);
            db.CreatorPromotionParticipations.Add(participation);
            db.PromotionViewVerifications.Add(new(promotion.Id, allocation.CreatorId, allocation.Id,
                publication.Provider, publication.ExternalContentId, publication.BaselineViews.Value,
                publication.BaselineViews.Value, 0, publication.VerifiedAtUtc.Value,
                publication.EvidenceReference!, Guid.NewGuid().ToString("N"), publication.BaselineViews.Value,
                false, participation.Id, true));
            operation.Remember(actor, "GoLiveVerifiedPublication", key, fingerprint, participation.Id.ToString(), now);
            operation.Audit(actor, "CreatorParticipationActivated", Guid.NewGuid(), now, promotion.Id, allocation.CreatorId);
            operation.Event("CreatorParticipationActivated", new { ParticipationId = participation.Id,
                PublicationId = publication.Id }, now);
            return participation.Id;
        }, ct);

    public Task<Guid> GoLiveUgcAsync(Actor actor, Guid assignmentId, string key, CancellationToken ct) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var assignment = await db.UgcAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC work not found.");
            await access.EnsureCreatorAsync(actor, assignment.CreatorId, token);
            if (assignment.Status != UgcAssignmentStatus.Approved)
                throw new ApplicationFailure(FailureKind.Validation, "Business approval is required before Go Live.");
            var submission = await CurrentApprovedUgcSubmission(assignment.Id, token);
            var publication = await db.CreatorPublicationVerifications.SingleOrDefaultAsync(x =>
                x.UgcAssignmentId == assignment.Id && x.UgcSubmissionId == submission.Id
                && x.Status == PublicationVerificationStatus.Verified, token)
                ?? throw new ApplicationFailure(FailureKind.Validation, "Publication verification must be complete before Go Live.");
            var request = await db.UgcCreatorRequests.AsNoTracking().SingleAsync(x => x.Id == assignment.UgcCreatorRequestId, token);
            var publicationProfile = await EligibleSocialProfile(assignment.CreatorId, request.VerifiedSocialProfileId,
                new(publication.Provider, publication.ExternalContentId, publication.CreatorSocialProfileId), token);
            var requirement = await db.UgcPlatformRequirements.AsNoTracking().SingleOrDefaultAsync(x =>
                x.UgcOpportunityId == assignment.UgcOpportunityId && x.Platform == publicationProfile.Platform, token);
            if (requirement is null) throw new ApplicationFailure(FailureKind.Validation,
                "The selected publication platform is no longer required for this work.");
            await EnsureAudienceAsync(publicationProfile, requirement.MinimumAudience, token);
            var fingerprint = RequestFingerprint.Create(assignment.Id.ToString(), publication.Id.ToString());
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "GoLiveUgcPublication", key, fingerprint, token) is { } replay)
                return Guid.Parse(replay);
            publication.GoLive(clock.GetUtcNow().UtcDateTime);
            operation.Remember(actor, "GoLiveUgcPublication", key, fingerprint, publication.Id.ToString(),
                clock.GetUtcNow().UtcDateTime);
            operation.Audit(actor, "UgcPublicationActivated", Guid.NewGuid(), clock.GetUtcNow().UtcDateTime,
                null, assignment.CreatorId);
            operation.Event("UgcPublicationActivated", new { PublicationId = publication.Id,
                AssignmentId = assignment.Id, CreatorId = assignment.CreatorId }, clock.GetUtcNow().UtcDateTime);
            return publication.Id;
        }, ct);

    public async Task<IReadOnlyList<AdminPublicationReviewCard>> AdminQueueAsync(Actor actor, CancellationToken ct)
    {
        DemandPlatformAdmin(actor);
        var rows = await db.CreatorPublicationVerifications.AsNoTracking()
            .Where(x => x.EndedAtUtc == null
                && (x.Status == PublicationVerificationStatus.VerificationPending
                    || x.Status == PublicationVerificationStatus.Verified))
            .OrderBy(x => x.Status == PublicationVerificationStatus.VerificationPending ? 0 : 1)
            .ThenBy(x => x.RequestedAtUtc).ToListAsync(ct);
        var result = new List<AdminPublicationReviewCard>();
        foreach (var row in rows)
        {
            var creator = await directory.CreatorCardAsync(row.CreatorId, ct);
            var profile = await db.CreatorSocialProfiles.AsNoTracking().SingleAsync(x => x.Id == row.CreatorSocialProfileId, ct);
            string business;
            string work;
            string workType;
            if (row.CreatorAllocationId is { } allocationId)
            {
                var allocation = await db.CreatorAllocations.AsNoTracking().SingleAsync(x => x.Id == allocationId, ct);
                var promotion = await db.Promotions.AsNoTracking().SingleAsync(x => x.Id == allocation.PromotionId, ct);
                business = (await directory.BusinessCardAsync(promotion.BusinessId, ct)).DisplayName;
                work = promotion.Title;
                workType = "Promotion";
            }
            else
            {
                var assignment = await db.UgcAssignments.AsNoTracking().SingleAsync(x => x.Id == row.UgcAssignmentId, ct);
                var opportunity = await db.UgcOpportunities.AsNoTracking().SingleAsync(x => x.Id == assignment.UgcOpportunityId, ct);
                business = (await directory.BusinessCardAsync(opportunity.BusinessId, ct)).DisplayName;
                work = opportunity.Title;
                workType = "UGC";
            }
            result.Add(new(row.Id, creator.DisplayName, business, work, workType, row.Provider,
                row.ExternalContentId, profile.ProfileUrl, row.Status.ToString(), row.RequestedAtUtc,
                PublicContentLink.Create(row.Provider, row.ExternalContentId)
                    ?? throw new ApplicationFailure(FailureKind.Validation, "The publication link is invalid."),
                row.WentLiveAtUtc));
        }
        return result;
    }

    public Task<Guid> AdminReviewAsync(Actor actor, Guid id, AdminPublicationReviewInput input,
        string key, CancellationToken ct) => new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            DemandPlatformAdmin(actor);
            var action = input.Action.Trim().ToLowerInvariant();
            if (action is not ("verify" or "fail" or "expire"))
                throw new ApplicationFailure(FailureKind.Validation, "Choose Verify, Fail, or Expire.");
            var publication = await db.CreatorPublicationVerifications.SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Publication verification not found.");
            var fingerprint = RequestFingerprint.Create(id.ToString(), action,
                input.EvidenceReference?.Trim() ?? "", input.BaselineViews?.ToString() ?? "");
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "ReviewPublicationVerification", key, fingerprint, token) is { } replay)
                return Guid.Parse(replay);
            if (action == "verify")
            {
                if (publication.Status != PublicationVerificationStatus.VerificationPending)
                    throw new ApplicationFailure(FailureKind.Validation, "Only a pending publication can be verified.");
                var evidence = string.IsNullOrWhiteSpace(input.EvidenceReference)
                    ? throw new ApplicationFailure(FailureKind.Validation, "Verification evidence is required.")
                    : input.EvidenceReference.Trim();
                publication.Verify(PublicationVerificationMethod.Manual, evidence,
                    publication.CreatorAllocationId is null ? input.BaselineViews : input.BaselineViews
                        ?? throw new ApplicationFailure(FailureKind.Validation, "Enter the observed public view count."),
                    clock.GetUtcNow().UtcDateTime, actor.UserId);
            }
            else
            {
                publication.Fail(action == "expire", clock.GetUtcNow().UtcDateTime);
                if (publication.CreatorAllocationId is { } allocationId && publication.WentLiveAtUtc is not null)
                {
                    var participation = await db.CreatorPromotionParticipations.SingleOrDefaultAsync(
                        x => x.CreatorAllocationId == allocationId, token);
                    if (participation?.Status is ParticipationStatus.Active or ParticipationStatus.FundingRequired)
                        participation.Pause();
                }
            }
            operation.Remember(actor, "ReviewPublicationVerification", key, fingerprint, publication.Id.ToString(),
                clock.GetUtcNow().UtcDateTime);
            operation.Audit(actor, action == "verify" ? "PublicationManuallyVerified" : "PublicationVerificationFailed",
                Guid.NewGuid(), clock.GetUtcNow().UtcDateTime, null, publication.CreatorId);
            operation.Event(action == "verify" ? "PublicationVerified" : "PublicationVerificationFailed",
                new { PublicationId = publication.Id, CreatorId = publication.CreatorId }, clock.GetUtcNow().UtcDateTime);
            return publication.Id;
        }, ct);

    public async Task<PublicationStatusView?> PromotionStatusAsync(Guid allocationId, CancellationToken ct)
    {
        var row = await db.CreatorPublicationVerifications.AsNoTracking()
            .Where(x => x.CreatorAllocationId == allocationId)
            .OrderByDescending(x => x.RequestedAtUtc).FirstOrDefaultAsync(ct);
        return row is null ? null : View(row);
    }

    public async Task<PublicationStatusView?> UgcStatusAsync(Guid assignmentId, CancellationToken ct)
    {
        var row = await db.CreatorPublicationVerifications.AsNoTracking()
            .Where(x => x.UgcAssignmentId == assignmentId)
            .OrderByDescending(x => x.RequestedAtUtc).FirstOrDefaultAsync(ct);
        return row is null ? null : View(row);
    }

    private async Task TryProviderVerificationAsync(Guid id, CancellationToken ct)
    {
        var publication = await db.CreatorPublicationVerifications.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        if (publication.Status != PublicationVerificationStatus.VerificationPending) return;
        Guid workId;
        if (publication.CreatorAllocationId is { } allocationId)
        {
            workId = await db.CreatorAllocations.AsNoTracking().Where(x => x.Id == allocationId)
                .Select(x => x.PromotionId).SingleAsync(ct);
        }
        else
        {
            var assignmentId = publication.UgcAssignmentId!.Value;
            workId = await db.UgcAssignments.AsNoTracking().Where(x => x.Id == assignmentId)
                .Select(x => x.UgcOpportunityId).SingleAsync(ct);
        }
        VerifiedViewResult evidence;
        try
        {
            evidence = await provider.VerifyAsync(new(publication.CreatorId, workId,
                publication.Provider, publication.ExternalContentId), ct);
        }
        catch (ApplicationFailure)
        {
            // The provider may be unsupported or temporarily unavailable. The
            // record remains explicitly pending for governed Admin review.
            return;
        }
        await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var current = await db.CreatorPublicationVerifications.SingleAsync(x => x.Id == id, token);
            if (current.Status == PublicationVerificationStatus.VerificationPending)
            {
                current.Verify(PublicationVerificationMethod.Provider, evidence.EvidenceReference,
                    current.CreatorAllocationId is null ? null : evidence.Count, evidence.VerifiedAtUtc);
                db.OutboxMessages.Add(new() { EventType = "PublicationVerified",
                    Payload = System.Text.Json.JsonSerializer.Serialize(new { PublicationId = current.Id,
                        CreatorId = current.CreatorId }), OccurredAtUtc = evidence.VerifiedAtUtc });
            }
            return current.Id;
        }, ct);
    }

    private async Task<CreatorSocialProfileRecord> EligibleSocialProfile(Guid creatorId, Guid? selectedProfileId,
        PublicationInput input, CancellationToken ct)
    {
        if (!CreatorPublicationVerification.ValidContentId(input.ExternalContentId)
            || !Enum.TryParse<CreatorPlatform>(input.Provider, true, out var platform) || !Enum.IsDefined(platform)
            || PublicContentLink.Create(platform.ToString(), input.ExternalContentId) is null)
            throw new ApplicationFailure(FailureKind.Validation, "Choose a supported platform and valid public post ID.");
        if (selectedProfileId is { } selected && selected != input.CreatorSocialProfileId)
            throw new ApplicationFailure(FailureKind.Validation, "Use the social profile selected for this Creator work.");
        var profile = await db.CreatorSocialProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == input.CreatorSocialProfileId && x.CreatorId == creatorId && x.Platform == platform && x.IsActive, ct);
        return profile ?? throw new ApplicationFailure(FailureKind.Validation,
            "Choose your active social profile for this platform.");
    }

    private async Task EnsureAudienceAsync(CreatorSocialProfileRecord profile, long? minimumAudience,
        CancellationToken ct)
    {
        var enforce = (await new FinancialConfigurationResolver(db)
            .EffectiveAsync(clock.GetUtcNow().UtcDateTime, ct)).EnforceAudienceRequirements;
        if (!SocialAudienceEligibility.Matches(profile, profile.Platform, minimumAudience, enforce))
            throw new ApplicationFailure(FailureKind.Validation,
                "The selected social profile no longer meets this Promotion's verified audience requirement.");
    }

    private async Task<CreatorPromotionContentSubmission> CurrentApprovedPromotionSubmission(Guid allocationId, CancellationToken ct)
    {
        var submission = await db.CreatorPromotionContentSubmissions.Where(x => x.CreatorAllocationId == allocationId)
            .OrderByDescending(x => x.RevisionNumber).FirstOrDefaultAsync(ct)
            ?? throw new ApplicationFailure(FailureKind.Validation, "Submit content and wait for Business approval before publication.");
        if (submission.ReviewStatus != PromotionContentReviewStatus.Approved)
            throw new ApplicationFailure(FailureKind.Validation, "The latest content revision is not approved.");
        return submission;
    }

    private async Task<UgcSubmission> CurrentApprovedUgcSubmission(Guid assignmentId, CancellationToken ct)
    {
        var submission = await db.UgcSubmissions.Where(x => x.UgcAssignmentId == assignmentId)
            .OrderByDescending(x => x.ContentRevisionNumber).FirstOrDefaultAsync(ct)
            ?? throw new ApplicationFailure(FailureKind.Validation, "Submit content and wait for Business approval before publication.");
        if (submission.Status != UgcAssignmentStatus.Approved)
            throw new ApplicationFailure(FailureKind.Validation, "The latest content revision is not approved.");
        return submission;
    }

    private async Task<PublicationStatusView> StatusAsync(Guid id, CancellationToken ct) =>
        View(await db.CreatorPublicationVerifications.AsNoTracking().SingleAsync(x => x.Id == id, ct));

    internal static PublicationStatusView View(CreatorPublicationVerification row) => new(row.Id,
        row.Provider, row.ExternalContentId, row.Status.ToString(), row.Status switch
        {
            PublicationVerificationStatus.VerificationPending => "Verification pending",
            PublicationVerificationStatus.Verified when row.VerificationMethod == PublicationVerificationMethod.Provider => "Provider verified",
            PublicationVerificationStatus.Verified => "Manually verified",
            PublicationVerificationStatus.Expired => "Verification expired",
            _ => "Verification failed"
        }, row.RequestedAtUtc, row.VerifiedAtUtc, row.WentLiveAtUtc,
        PublicContentLink.Create(row.Provider, row.ExternalContentId));

    private static void DemandPlatformAdmin(Actor actor)
    {
        if (actor.Role != ActorRole.PlatformAdmin)
            throw new ApplicationFailure(FailureKind.Forbidden, "Platform Admin access is required.");
    }
}
