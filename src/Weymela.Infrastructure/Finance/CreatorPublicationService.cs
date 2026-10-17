using System.Data;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Web;
using Weymela.Infrastructure.Providers;

namespace Weymela.Infrastructure.Finance;

public static class PublicContentLink
{
    public static string? Create(string provider, string content) => provider switch
    {
        "TikTok" when content.Length is >= 6 and <= 30 && content.All(char.IsAsciiDigit) => "https://www.tiktok.com/player/v1/" + content,
        "YouTube" when Safe(content) => "https://www.youtube.com/watch?v=" + content,
        "Instagram" when Safe(content) => "https://www.instagram.com/p/" + content + "/",
        "Facebook" when Safe(content) => "https://www.facebook.com/watch/?v=" + content,
        _ => null
    };

    private static bool Safe(string value) => value.Length is > 0 and <= 100
        && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_');
}

public sealed class CreatorPublicationService(WeymelaDbContext db, IVerifiedViewProvider provider,
    ICommerceAccessPolicy access, IWorkspaceDirectory directory, TimeProvider clock, TikTokLinkResolver? linkResolver = null)
{
    public async Task<PublicationStatusView> RequestPromotionAsync(Actor actor, Guid allocationId,
        PublicationInput input, string key, CancellationToken ct)
    {
        var resolved = await NormalizePublicationInputAsync(input, ct);
        input = resolved.Input;
        var publicationId = await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var allocation = await db.CreatorAllocations.SingleOrDefaultAsync(x => x.Id == allocationId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Creator work not found.");
            await access.EnsureCreatorAsync(actor, allocation.CreatorId, token);
            var promotion = await db.Promotions.SingleAsync(x => x.Id == allocation.PromotionId, token);
            await access.EnsureBusinessAsync(promotion.BusinessId, token);
            VerifiedViewService.EnsureCampaignActive(promotion, clock.GetUtcNow().UtcDateTime);
            var submission = await CurrentApprovedPromotionSubmission(allocation.Id, token);
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
            var started = await db.CreatorPublicationVerifications.SingleOrDefaultAsync(x => x.CreatorAllocationId == allocation.Id && x.WentLiveAtUtc != null, token);
            if (started is not null)
            {
                if (started.PromotionContentSubmissionId != submission.Id || started.CreatorSocialProfileId != profile.Id || started.Provider != input.Provider || started.ExternalContentId != input.ExternalContentId)
                    throw new ApplicationFailure(FailureKind.Validation, "This campaign has already gone Live.");
                operation.Remember(actor, "RequestPromotionPublication", key, fingerprint, started.Id.ToString(), clock.GetUtcNow().UtcDateTime);
                return started.Id;
            }
            var activePublication = await db.CreatorPublicationVerifications.SingleOrDefaultAsync(x =>
                x.CreatorAllocationId == allocation.Id && (x.Status == PublicationVerificationStatus.VerificationPending || x.Status == PublicationVerificationStatus.Verified), token);
            if (activePublication is not null)
            {
                if (activePublication.PromotionContentSubmissionId != submission.Id || activePublication.CreatorSocialProfileId != profile.Id
                    || activePublication.Provider != input.Provider || activePublication.ExternalContentId != input.ExternalContentId)
                    throw new ApplicationFailure(FailureKind.Validation, "A different video is already under review. Ask the Business to reject it before replacing it.");
                operation.Remember(actor, "RequestPromotionPublication", key, fingerprint, activePublication.Id.ToString(), clock.GetUtcNow().UtcDateTime);
                return activePublication.Id;
            }
            var publication = new CreatorPublicationVerification(allocation.CreatorId, profile.Id,
                allocation.Id, null, submission.Id, null, input.Provider, input.ExternalContentId,
                clock.GetUtcNow().UtcDateTime);
            if (resolved.Url is { } canonical)
            {
                var handle = TikTokVideoLink.CreatorHandle(canonical);
                var profileHandle = Uri.TryCreate(profile.ProfileUrl, UriKind.Absolute, out var profileUrl)
                    ? profileUrl.AbsolutePath.Trim('/').TrimStart('@') : null;
                if (handle is not null && !string.Equals(handle, profileHandle, StringComparison.OrdinalIgnoreCase))
                    throw new ApplicationFailure(FailureKind.Validation, "This TikTok video belongs to another Creator profile.", code: "WrongCreator");
                publication.RecordPublicLink(canonical);
            }
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
        var resolved = await NormalizePublicationInputAsync(input, ct);
        input = resolved.Input;
        var publicationId = await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var assignment = await db.UgcAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC work not found.");
            await access.EnsureCreatorAsync(actor, assignment.CreatorId, token);
            if (assignment.Status != UgcAssignmentStatus.Approved)
                throw new ApplicationFailure(FailureKind.Validation, "Business approval is required before publication.");
            var opportunity = await db.UgcOpportunities.SingleAsync(x => x.Id == assignment.UgcOpportunityId, token);
            await access.EnsureBusinessAsync(opportunity.BusinessId, token);
            if (await db.UgcOpportunities.AnyAsync(x => x.Id == assignment.UgcOpportunityId && x.DeliverableType == UgcDeliverableType.DirectVideoDelivery, token))
                throw new ApplicationFailure(FailureKind.Validation, "This campaign uses direct video delivery.");
            var submission = await CurrentApprovedUgcSubmission(assignment.Id, token);
            var request = await db.UgcCreatorRequests.AsNoTracking().SingleAsync(x => x.Id == assignment.UgcCreatorRequestId, token);
            var profile = await EligibleSocialProfile(assignment.CreatorId, request.VerifiedSocialProfileId, input, token);
            var capacity = await db.UgcPlatformCapacities.AsNoTracking().SingleOrDefaultAsync(x =>
                x.UgcOpportunityId == opportunity.Id && x.Platform == profile.Platform && x.Capacity > 0, token);
            var requirement = await db.UgcPlatformRequirements.AsNoTracking().SingleOrDefaultAsync(x =>
                x.UgcOpportunityId == opportunity.Id && x.Platform == profile.Platform, token);
            if (request.SelectedPlatform is not null && (capacity is null || requirement is null))
                throw new ApplicationFailure(FailureKind.Validation, "The selected publication platform is no longer required for this work.");
            if (requirement is not null) await EnsureAudienceAsync(profile, requirement.MinimumAudience, token);
            var fingerprint = RequestFingerprint.Create(assignment.Id.ToString(), submission.Id.ToString(),
                profile.Id.ToString(), input.Provider, input.ExternalContentId);
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "RequestUgcPublication", key, fingerprint, token) is { } replay)
                return Guid.Parse(replay);
            var started = await db.CreatorPublicationVerifications.SingleOrDefaultAsync(x => x.UgcAssignmentId == assignment.Id && x.WentLiveAtUtc != null, token);
            if (started is not null)
            {
                if (started.UgcSubmissionId != submission.Id || started.CreatorSocialProfileId != profile.Id || started.Provider != input.Provider || started.ExternalContentId != input.ExternalContentId)
                    throw new ApplicationFailure(FailureKind.Validation, "This campaign has already gone Live.");
                operation.Remember(actor, "RequestUgcPublication", key, fingerprint, started.Id.ToString(), clock.GetUtcNow().UtcDateTime);
                return started.Id;
            }
            var activePublication = await db.CreatorPublicationVerifications.SingleOrDefaultAsync(x =>
                x.UgcAssignmentId == assignment.Id && (x.Status == PublicationVerificationStatus.VerificationPending || x.Status == PublicationVerificationStatus.Verified), token);
            if (activePublication is not null)
            {
                if (activePublication.UgcSubmissionId != submission.Id || activePublication.CreatorSocialProfileId != profile.Id
                    || activePublication.Provider != input.Provider || activePublication.ExternalContentId != input.ExternalContentId)
                    throw new ApplicationFailure(FailureKind.Validation, "A different video is already under review. Ask the Business to reject it before replacing it.");
                operation.Remember(actor, "RequestUgcPublication", key, fingerprint, activePublication.Id.ToString(), clock.GetUtcNow().UtcDateTime);
                return activePublication.Id;
            }
            var publication = new CreatorPublicationVerification(assignment.CreatorId, profile.Id,
                null, assignment.Id, null, submission.Id, input.Provider, input.ExternalContentId,
                clock.GetUtcNow().UtcDateTime);
            if (resolved.Url is { } canonical)
            {
                var handle = TikTokVideoLink.CreatorHandle(canonical);
                var profileHandle = Uri.TryCreate(profile.ProfileUrl, UriKind.Absolute, out var profileUrl)
                    ? profileUrl.AbsolutePath.Trim('/').TrimStart('@') : null;
                if (handle is not null && !string.Equals(handle, profileHandle, StringComparison.OrdinalIgnoreCase))
                    throw new ApplicationFailure(FailureKind.Validation, "This TikTok video belongs to another Creator profile.", code: "WrongCreator");
                publication.RecordPublicLink(canonical);
            }
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

    public async Task<PublicationStatusView> LaunchAsync(Actor actor, Guid workId, bool ugc,
        PublicationInput? input, string key, CancellationToken ct)
    {
        Guid? requestedId = null;
        if (input is not null)
            requestedId = (ugc ? await RequestUgcAsync(actor, workId, input, RequestFingerprint.Create(key, "publication"), ct)
                : await RequestPromotionAsync(actor, workId, input, RequestFingerprint.Create(key, "publication"), ct)).Id;
        var id = await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var creatorId = ugc
                ? await db.UgcAssignments.Where(x => x.Id == workId).Select(x => (Guid?)x.CreatorId).SingleOrDefaultAsync(token)
                : await db.CreatorAllocations.Where(x => x.Id == workId).Select(x => (Guid?)x.CreatorId).SingleOrDefaultAsync(token);
            if (creatorId is null) throw new ApplicationFailure(FailureKind.NotFound, "Creator work not found.");
            await access.EnsureCreatorAsync(actor, creatorId.Value, token);
            var publication = await db.CreatorPublicationVerifications
                .Where(x => (ugc ? x.UgcAssignmentId == workId : x.CreatorAllocationId == workId) && (requestedId == null || x.Id == requestedId))
                .OrderByDescending(x => x.RequestedAtUtc).FirstOrDefaultAsync(token)
                ?? throw new ApplicationFailure(FailureKind.Validation, "Add Video Link.");
            if (publication.Status is PublicationVerificationStatus.Failed or PublicationVerificationStatus.Expired)
                throw new ApplicationFailure(FailureKind.Validation,
                    publication.VerificationCode == "WrongCreator" ? "This post does not belong to the selected Creator profile." : "Check the public video link and try again.", code: publication.VerificationCode);
            var firstRequest = publication.GoLiveRequestedAtUtc is null;
            publication.RequestGoLive(clock.GetUtcNow().UtcDateTime);
            if (firstRequest && publication.Status == PublicationVerificationStatus.VerificationPending)
                new FinancialOperation(db).Event("PublicationGoLiveRequested", new { PublicationId = publication.Id,
                    CreatorId = publication.CreatorId }, clock.GetUtcNow().UtcDateTime);
            if (publication.Status == PublicationVerificationStatus.Verified)
            {
                if (ugc) await CompleteUgcGoLiveAsync(actor, workId, RequestFingerprint.Create(key, "live"), token);
                else await CompletePromotionGoLiveAsync(actor, workId, RequestFingerprint.Create(key, "live"), token);
            }
            return publication.Id;
        }, ct);
        return await StatusAsync(id, ct);
    }

    public Task<Guid> GoLivePromotionAsync(Actor actor, Guid allocationId, string key, CancellationToken ct) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var creatorId = await db.CreatorAllocations.Where(x => x.Id == allocationId).Select(x => (Guid?)x.CreatorId).SingleOrDefaultAsync(token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Creator work not found.");
            await access.EnsureCreatorAsync(actor, creatorId, token);
            return await CompletePromotionGoLiveAsync(actor, allocationId, key, token);
        }, ct);

    private async Task<Guid> CompletePromotionGoLiveAsync(Actor actor, Guid allocationId, string key, CancellationToken token)
    {
            var allocation = await db.CreatorAllocations.SingleOrDefaultAsync(x => x.Id == allocationId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Creator work not found.");
            var promotion = await new PromotionRepository(db).GetAsync(allocation.PromotionId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Promotion not found.");
            await access.EnsureBusinessAsync(promotion.BusinessId, token);
            var now = clock.GetUtcNow().UtcDateTime;
            VerifiedViewService.EnsureCampaignActive(promotion, now);
            var submission = await CurrentApprovedPromotionSubmission(allocation.Id, token);
            var publication = db.CreatorPublicationVerifications.Local.SingleOrDefault(x => x.CreatorAllocationId == allocation.Id && x.PromotionContentSubmissionId == submission.Id && x.Status == PublicationVerificationStatus.Verified)
                ?? await db.CreatorPublicationVerifications.SingleOrDefaultAsync(x =>
                x.CreatorAllocationId == allocation.Id && x.PromotionContentSubmissionId == submission.Id
                && x.Status == PublicationVerificationStatus.Verified, token)
                ?? throw new ApplicationFailure(FailureKind.Validation, "Publication verification must be complete before Go Live.");
            var publicationProfile = await EligibleSocialProfile(allocation.CreatorId, allocation.CreatorSocialProfileId,
                new(publication.Provider, publication.ExternalContentId, publication.CreatorSocialProfileId), token);
            var minimumAudience = promotion.Platforms.SingleOrDefault(x => x.Platform == publicationProfile.Platform)?.MinimumAudience;
            await EnsureAudienceAsync(publicationProfile, minimumAudience, token);
            EnsureCurrentLinkProfile(publication, publicationProfile);
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
            publication.GoLive(now, promotion.PromotionLiveDurationDays);
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
    }

    public Task<Guid> GoLiveUgcAsync(Actor actor, Guid assignmentId, string key, CancellationToken ct) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var creatorId = await db.UgcAssignments.Where(x => x.Id == assignmentId).Select(x => (Guid?)x.CreatorId).SingleOrDefaultAsync(token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Creator work not found.");
            await access.EnsureCreatorAsync(actor, creatorId, token);
            return await CompleteUgcGoLiveAsync(actor, assignmentId, key, token);
        }, ct);

    private async Task<Guid> CompleteUgcGoLiveAsync(Actor actor, Guid assignmentId, string key, CancellationToken token)
    {
            var assignment = await db.UgcAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC work not found.");
            if (assignment.Status != UgcAssignmentStatus.Approved)
                throw new ApplicationFailure(FailureKind.Validation, "Business approval is required before Go Live.");
            var submission = await CurrentApprovedUgcSubmission(assignment.Id, token);
            var publication = db.CreatorPublicationVerifications.Local.SingleOrDefault(x => x.UgcAssignmentId == assignment.Id && x.UgcSubmissionId == submission.Id && x.Status == PublicationVerificationStatus.Verified)
                ?? await db.CreatorPublicationVerifications.SingleOrDefaultAsync(x =>
                x.UgcAssignmentId == assignment.Id && x.UgcSubmissionId == submission.Id
                && x.Status == PublicationVerificationStatus.Verified, token)
                ?? throw new ApplicationFailure(FailureKind.Validation, "Publication verification must be complete before Go Live.");
            var request = await db.UgcCreatorRequests.AsNoTracking().SingleAsync(x => x.Id == assignment.UgcCreatorRequestId, token);
            var publicationProfile = await EligibleSocialProfile(assignment.CreatorId, request.VerifiedSocialProfileId,
                new(publication.Provider, publication.ExternalContentId, publication.CreatorSocialProfileId), token);
            var requirement = await db.UgcPlatformRequirements.AsNoTracking().SingleOrDefaultAsync(x =>
                x.UgcOpportunityId == assignment.UgcOpportunityId && x.Platform == publicationProfile.Platform, token);
            if (request.SelectedPlatform is not null && requirement is null) throw new ApplicationFailure(FailureKind.Validation,
                "The selected publication platform is no longer required for this work.");
            if (requirement is not null) await EnsureAudienceAsync(publicationProfile, requirement.MinimumAudience, token);
            EnsureCurrentLinkProfile(publication, publicationProfile);
            var fingerprint = RequestFingerprint.Create(assignment.Id.ToString(), publication.Id.ToString());
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "GoLiveUgcPublication", key, fingerprint, token) is { } replay)
                return Guid.Parse(replay);
            var opportunity = await db.UgcOpportunities.Include(x => x.PlatformCapacities).SingleAsync(x => x.Id == assignment.UgcOpportunityId, token);
            await access.EnsureBusinessAsync(opportunity.BusinessId, token);
            if (opportunity.DeliverableType != UgcDeliverableType.PublicSocialVideo)
                throw new ApplicationFailure(FailureKind.Validation, "This campaign uses direct video delivery.");
            if (publication.WentLiveAtUtc is null)
            {
                if (assignment.CompletedAtUtc is not null) throw new ApplicationFailure(FailureKind.Validation, "This deliverable is already completed.");
                if (opportunity.PromotionLiveDurationDays is null)
                    opportunity.SetLegacyLiveDuration((await new FinancialConfigurationResolver(db).EffectiveAsync(clock.GetUtcNow().UtcDateTime, token)).RequirePromotionDuration());
                publication.GoLive(clock.GetUtcNow().UtcDateTime, opportunity.PromotionLiveDurationDays);
            }
            await UgcPublicationPayment.ReleaseAsync(db, actor, assignment, opportunity, publication, key, clock.GetUtcNow().UtcDateTime, token);
            operation.Remember(actor, "GoLiveUgcPublication", key, fingerprint, publication.Id.ToString(),
                clock.GetUtcNow().UtcDateTime);
            operation.Audit(actor, "UgcPublicationActivated", Guid.NewGuid(), clock.GetUtcNow().UtcDateTime,
                null, assignment.CreatorId);
            operation.Event("UgcPublicationActivated", new { PublicationId = publication.Id,
                AssignmentId = assignment.Id, CreatorId = assignment.CreatorId }, clock.GetUtcNow().UtcDateTime);
            return publication.Id;
    }

    public async Task<IReadOnlyList<AdminPublicationReviewCard>> AdminQueueAsync(Actor actor, CancellationToken ct, AdminListFilter? filter = null)
    {
        DemandReviewer(actor);
        var query = filter ?? new(); query.Validate();
        var term = query.Term;
        var source = db.CreatorPublicationVerifications.AsNoTracking();
        if (actor.Role == ActorRole.Business)
        {
            await DemandBusinessReviewAccessAsync(actor, ct);
            source = source.Where(x => db.UgcAssignments.Any(a => a.Id == x.UgcAssignmentId && db.UgcOpportunities.Any(u => u.Id == a.UgcOpportunityId && u.BusinessId == actor.BusinessId))
                || db.CreatorAllocations.Any(a => a.Id == x.CreatorAllocationId && db.Promotions.Any(p => p.Id == a.PromotionId && p.BusinessId == actor.BusinessId)));
        }
        if (term != "") source = source.Where(x => x.ExternalContentId.ToLower().Contains(term)
            || db.PublicWorkspaceProfiles.Any(p => p.SubjectId == x.CreatorId && p.Role == ActorRole.Creator && p.DisplayName.ToLower().Contains(term))
            || db.UgcAssignments.Any(a => a.Id == x.UgcAssignmentId && db.UgcOpportunities.Any(u => u.Id == a.UgcOpportunityId && (u.Title.ToLower().Contains(term)
                || db.PublicWorkspaceProfiles.Any(p => p.SubjectId == u.BusinessId && p.Role == ActorRole.Business && p.DisplayName.ToLower().Contains(term)))))
            || db.CreatorAllocations.Any(a => a.Id == x.CreatorAllocationId && db.Promotions.Any(u => u.Id == a.PromotionId && (u.Title.ToLower().Contains(term)
                || db.PublicWorkspaceProfiles.Any(p => p.SubjectId == u.BusinessId && p.Role == ActorRole.Business && p.DisplayName.ToLower().Contains(term))))));
        if (!string.IsNullOrEmpty(query.Status))
        {
            if (!Enum.TryParse<PublicationVerificationStatus>(query.Status, true, out var status))
                throw new ApplicationFailure(FailureKind.Validation, "Choose a valid publication status.");
            source = source.Where(x => x.Status == status);
        }
        var rows = await source
            .Where(x => x.EndedAtUtc == null
                && (x.Status == PublicationVerificationStatus.VerificationPending
                    || x.Status == PublicationVerificationStatus.Verified))
            .OrderBy(x => x.Status == PublicationVerificationStatus.VerificationPending ? 0 : 1)
            .ThenBy(x => x.RequestedAtUtc).ThenBy(x => x.Id).Skip(query.Offset).Take(query.PageSize).ToListAsync(ct);
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
            string? approvedMediaUrl = null; string? approvedWatchUrl = null; string? mediaType = null;
            if (row.PromotionContentSubmissionId is { } promotionSubmissionId)
            {
                var sample = await db.CreatorPromotionContentSubmissions.AsNoTracking().SingleAsync(x => x.Id == promotionSubmissionId, ct);
                if (sample.ReviewMediaAssetId is not null) { approvedMediaUrl = $"/api/review-media/promotions/{sample.Id}"; mediaType = "video/mp4"; }
                else approvedWatchUrl = TikTokVideoLink.WatchUrl(sample.Provider, sample.ContentReference);
            }
            else if (row.UgcSubmissionId is { } ugcSubmissionId)
            {
                var sample = await db.UgcSubmissions.AsNoTracking().SingleAsync(x => x.Id == ugcSubmissionId, ct);
                if (sample.ReviewMediaAssetId is { } mediaId)
                {
                    approvedMediaUrl = $"/api/review-media/ugc/{sample.Id}";
                    mediaType = await db.PrivateReviewMediaAssets.Where(x => x.Id == mediaId).Select(x => x.ContentType).SingleAsync(ct);
                }
                else approvedWatchUrl = TikTokVideoLink.WatchUrl("TikTok", sample.SubmissionUrl);
            }
            result.Add(new(row.Id, creator.DisplayName, business, work, workType, row.Provider,
                row.ExternalContentId, profile.ProfileUrl, row.Status.ToString(), row.RequestedAtUtc,
                row.CanonicalUrl ?? PublicContentLink.Create(row.Provider, row.ExternalContentId)
                    ?? throw new ApplicationFailure(FailureKind.Validation, "The publication link is invalid."),
                row.WentLiveAtUtc, approvedMediaUrl, approvedWatchUrl, mediaType, row.VerificationMethod?.ToString()));
        }
        return result;
    }

    public Task<Guid> AdminReviewAsync(Actor actor, Guid id, AdminPublicationReviewInput input,
        string key, CancellationToken ct) => new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            DemandReviewer(actor);
            var action = input.Action.Trim().ToLowerInvariant();
            if (action is not ("verify" or "fail" or "expire"))
                throw new ApplicationFailure(FailureKind.Validation, "Choose Verify, Fail, or Expire.");
            var publication = await db.CreatorPublicationVerifications.SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Publication verification not found.");
            if (actor.Role == ActorRole.Business)
            {
                await DemandBusinessReviewAccessAsync(actor, token);
                var owns = publication.UgcAssignmentId is { } assignmentId
                    ? await db.UgcAssignments.AnyAsync(a => a.Id == assignmentId && db.UgcOpportunities.Any(u => u.Id == a.UgcOpportunityId && u.BusinessId == actor.BusinessId), token)
                    : await db.CreatorAllocations.AnyAsync(a => a.Id == publication.CreatorAllocationId && db.Promotions.Any(p => p.Id == a.PromotionId && p.BusinessId == actor.BusinessId), token);
                if (!owns) throw new ApplicationFailure(FailureKind.Forbidden, "This publication belongs to another Business.");
            }
            var fingerprint = RequestFingerprint.Create(id.ToString(), action,
                input.EvidenceReference?.Trim() ?? "", input.BaselineViews?.ToString() ?? "", input.EvidenceChecked.ToString());
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "ReviewPublicationVerification", key, fingerprint, token) is { } replay)
                return Guid.Parse(replay);
            if (action == "verify")
            {
                if (publication.Status != PublicationVerificationStatus.VerificationPending)
                    throw new ApplicationFailure(FailureKind.Validation, "Only a pending publication can be verified.");
                if (!input.EvidenceChecked) throw new ApplicationFailure(FailureKind.Validation,
                    "Confirm public access, Creator ownership and the approved video before accepting evidence.");
                var evidence = string.IsNullOrWhiteSpace(input.EvidenceReference)
                    ? throw new ApplicationFailure(FailureKind.Validation, "Verification evidence is required.")
                    : input.EvidenceReference.Trim();
                publication.Verify(PublicationVerificationMethod.Manual, evidence,
                    publication.CreatorAllocationId is null ? input.BaselineViews : input.BaselineViews
                        ?? throw new ApplicationFailure(FailureKind.Validation, "Enter the observed public view count."),
                    clock.GetUtcNow().UtcDateTime, actor.UserId);
                if (publication.GoLiveRequestedAtUtc is not null)
                {
                    if (publication.UgcAssignmentId is { } ugcId) await CompleteUgcGoLiveAsync(actor, ugcId, RequestFingerprint.Create(key, "live"), token);
                    else await CompletePromotionGoLiveAsync(actor, publication.CreatorAllocationId!.Value, RequestFingerprint.Create(key, "live"), token);
                }
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
        return row is null ? null : View(row, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<PublicationStatusView?> UgcStatusAsync(Guid assignmentId, CancellationToken ct)
    {
        var row = await db.CreatorPublicationVerifications.AsNoTracking()
            .Where(x => x.UgcAssignmentId == assignmentId)
            .OrderByDescending(x => x.RequestedAtUtc).FirstOrDefaultAsync(ct);
        return row is null ? null : View(row, clock.GetUtcNow().UtcDateTime);
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
            var request = new VerifiedViewRequest(publication.CreatorId, workId, publication.Provider, publication.ExternalContentId);
            evidence = publication.UgcAssignmentId is null ? await provider.VerifyAsync(request, ct)
                : provider is IVerifiedPublicationProvider publications ? await publications.VerifyPublicationAsync(request, ct)
                : throw new ApplicationFailure(FailureKind.Validation, "Publication ownership verification is unavailable.", code: "ProviderUnavailable");
        }
        catch (ApplicationFailure failure)
        {
            await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
            {
                var pending = await db.CreatorPublicationVerifications.SingleAsync(x => x.Id == id, token);
                if (pending.Status == PublicationVerificationStatus.VerificationPending)
                {
                    pending.RecordProviderFailure(failure.Code ?? "ProviderUnavailable");
                    if (failure.Code is "WrongCreator" or "PostInaccessible") pending.Fail(false, clock.GetUtcNow().UtcDateTime);
                }
                return id;
            }, ct);
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
            )
            throw new ApplicationFailure(FailureKind.Validation, "Choose a supported platform and valid public post ID.");
        if (selectedProfileId is { } selected && selected != input.CreatorSocialProfileId)
            throw new ApplicationFailure(FailureKind.Validation, "Use the social profile selected for this Creator work.");
        var profile = await db.CreatorSocialProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == input.CreatorSocialProfileId && x.CreatorId == creatorId && x.Platform == platform && x.IsActive, ct);
        return profile ?? throw new ApplicationFailure(FailureKind.Validation,
            "Choose your active social profile for this platform.");
    }

    private async Task<(PublicationInput Input, string? Url)> NormalizePublicationInputAsync(PublicationInput input, CancellationToken ct)
    {
        if (!Enum.TryParse<CreatorPlatform>(input.Provider, true, out var platform) || !Enum.IsDefined(platform))
            throw new ApplicationFailure(FailureKind.Validation, "Choose a supported social platform.");
        input = input with { Provider = platform.ToString() };
        if (CreatorPublicationVerification.ValidContentId(input.ExternalContentId)
            && (platform != CreatorPlatform.TikTok || input.ExternalContentId.Length is >= 6 and <= 30 && input.ExternalContentId.All(char.IsAsciiDigit)))
            return (input, null); // Existing API clients; independent evidence is still mandatory.
        var link = SocialVideoLink.Parse(input.Provider, input.ExternalContentId);
        if (platform == CreatorPlatform.TikTok && link.IsShareLink && linkResolver is not null)
        {
            try { link = SocialVideoLink.Parse(input.Provider, await linkResolver.ResolveAsync(link.Url, ct)); }
            catch (ApplicationFailure error) when (error.Code is "ProviderUnavailable" or "PostInaccessible")
            { /* Keep the safe share URL for an authorized reviewer. No ownership/access claim. */ }
        }
        return (input with { ExternalContentId = link.ContentId }, link.Url);
    }

    private static void EnsureCurrentLinkProfile(CreatorPublicationVerification publication, CreatorSocialProfileRecord profile)
    {
        if (publication.Provider != "TikTok" || publication.CanonicalUrl is null) return;
        var actual = TikTokVideoLink.CreatorHandle(publication.CanonicalUrl);
        var expected = new Uri(profile.ProfileUrl).AbsolutePath.Trim('/').TrimStart('@');
        if (actual is not null && !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationFailure(FailureKind.Validation, "This post does not belong to the selected Creator profile.", code: "WrongCreator");
    }

    private static void DemandReviewer(Actor actor)
    {
        if (actor.Role is not (ActorRole.PlatformAdmin or ActorRole.OperationsAdmin or ActorRole.Business))
            throw new ApplicationFailure(FailureKind.Forbidden, "Authorized publication review is required.");
    }

    private async Task DemandBusinessReviewAccessAsync(Actor actor, CancellationToken ct)
    {
        if (actor.BusinessId is null || !await db.CommercePermissions.AnyAsync(x => x.UserId == actor.UserId && x.Role == ActorRole.Business && x.SubjectId == actor.BusinessId && x.IsActive, ct))
            throw new ApplicationFailure(FailureKind.Forbidden, "Business access is required.");
        await access.EnsureBusinessAsync(actor.BusinessId.Value, ct);
    }

    private async Task EnsureAudienceAsync(CreatorSocialProfileRecord profile, long? minimumAudience,
        CancellationToken ct)
    {
        var enforce = (await new FinancialConfigurationResolver(db)
            .EffectiveAsync(clock.GetUtcNow().UtcDateTime, ct)).RequireAudienceRules();
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
        View(await db.CreatorPublicationVerifications.AsNoTracking().SingleAsync(x => x.Id == id, ct), clock.GetUtcNow().UtcDateTime);

    internal static PublicationStatusView View(CreatorPublicationVerification row, DateTime? nowUtc = null) => new(row.Id,
        row.Provider, row.ExternalContentId, row.Status.ToString(), row.Status switch
        {
            PublicationVerificationStatus.VerificationPending => "Awaiting Review",
            PublicationVerificationStatus.Verified when row.WentLiveAtUtc is not null && row.ExpiresAtUtc <= (nowUtc ?? DateTime.UtcNow) => "Completed",
            PublicationVerificationStatus.Verified when row.WentLiveAtUtc is not null => "Live",
            PublicationVerificationStatus.Verified => "Approved",
            _ => "Action Required"
        }, row.RequestedAtUtc, row.VerifiedAtUtc, row.WentLiveAtUtc,
        row.CanonicalUrl ?? PublicContentLink.Create(row.Provider, row.ExternalContentId), row.VerificationCode,
        row.GoLiveRequestedAtUtc, row.ExpiresAtUtc,
        row.ExpiresAtUtc is { } end ? Math.Max(0, (int)Math.Ceiling((end - (nowUtc ?? DateTime.UtcNow)).TotalDays)) : null,
        row.VerificationMethod?.ToString());


}
