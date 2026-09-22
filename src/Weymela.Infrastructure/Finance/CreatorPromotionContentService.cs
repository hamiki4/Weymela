using System.Data;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Finance;

public sealed class CreatorPromotionContentService(
    WeymelaDbContext db,
    ICommerceAccessPolicy access,
    IWorkspaceDirectory directory,
    TimeProvider clock)
{
    public Task<CreatorContentSubmissionStatus> SubmitAsync(
        Actor actor, Guid allocationId, ContentInput input, string idempotencyKey, CancellationToken ct)
        => new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var allocation = await db.CreatorAllocations.SingleOrDefaultAsync(x => x.Id == allocationId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Approved Promotion allocation not found.");
            await access.EnsureCreatorAsync(actor, allocation.CreatorId, token);
            var promotion = await db.Promotions.Include(x => x.Allocations)
                .SingleOrDefaultAsync(x => x.Id == allocation.PromotionId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Promotion not found.");
            await access.EnsureBusinessAsync(promotion.BusinessId, token);

            if (!await db.CreatorApplications.AnyAsync(x => x.PromotionId == promotion.Id
                    && x.CreatorId == actor.CreatorId && x.Status == CreatorApplicationStatus.Approved, token))
                throw new ApplicationFailure(FailureKind.Forbidden, "Business approval is required before submitting Promotion content.");
            if (allocation.Status != CreatorAllocationStatus.Active || allocation.RemainingAmount.Amount <= 0
                || promotion.Status is not (PromotionStatus.Published or PromotionStatus.Active)
                || promotion.EndDateUtc <= now)
                throw new ApplicationFailure(FailureKind.Validation, "This approved Promotion is no longer accepting Creator content.");
            if (await db.CreatorPromotionParticipations.AnyAsync(x => x.CreatorAllocationId == allocation.Id, token))
                throw new ApplicationFailure(FailureKind.Validation, "This Creator participation has already gone live.");
            var contentReference = InputRules.Reference(input.ExternalContentId, "video reference", 100);
            if (input.Provider is not ("TikTok" or "YouTube" or "Instagram"))
                throw new ApplicationFailure(FailureKind.Validation, "Choose a supported platform and valid content reference.");

            var latest = await db.CreatorPromotionContentSubmissions.Where(x => x.CreatorAllocationId == allocation.Id)
                .OrderByDescending(x => x.RevisionNumber).FirstOrDefaultAsync(token);
            var fingerprint = RequestFingerprint.Create(allocation.Id.ToString(), input.Provider, contentReference);
            var operation = new FinancialOperation(db);
            var replay = await operation.Replay(actor, "SubmitPromotionContent", idempotencyKey, fingerprint, token);
            if (replay is not null)
            {
                var old = await db.CreatorPromotionContentSubmissions.SingleAsync(x => x.Id == Guid.Parse(replay), token);
                return View(old);
            }
            if (latest is not null && latest.ReviewStatus != PromotionContentReviewStatus.ChangesRequested)
                throw new ApplicationFailure(FailureKind.Validation,
                    latest.ReviewStatus == PromotionContentReviewStatus.UnderReview
                        ? "This content is already under Business review."
                        : "A new content revision is allowed only after the Business requests changes.");

            var revision = (latest?.RevisionNumber ?? 0) + 1;
            var submission = new CreatorPromotionContentSubmission(allocation.Id, revision,
                input.Provider, contentReference, now);
            db.CreatorPromotionContentSubmissions.Add(submission);
            operation.Remember(actor, "SubmitPromotionContent", idempotencyKey, fingerprint, submission.Id.ToString(), now);
            operation.Audit(actor, "PromotionContentSubmitted", Guid.NewGuid(), now, promotion.Id, allocation.CreatorId);
            return View(submission);
        }, ct);

    public Task<BusinessPromotionContentReviewCard> ReviewAsync(
        Actor actor, Guid submissionId, PromotionContentReviewInput input, string idempotencyKey, CancellationToken ct)
        => new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            if (actor.Role != ActorRole.Business || actor.BusinessId is null)
                throw new ApplicationFailure(FailureKind.Forbidden, "Business access is required.");
            var submission = await db.CreatorPromotionContentSubmissions.SingleOrDefaultAsync(x => x.Id == submissionId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Promotion content submission not found.");
            var allocation = await db.CreatorAllocations.SingleAsync(x => x.Id == submission.CreatorAllocationId, token);
            var promotion = await db.Promotions.SingleAsync(x => x.Id == allocation.PromotionId, token);
            if (promotion.BusinessId != actor.BusinessId)
                throw new ApplicationFailure(FailureKind.Forbidden, "This Promotion content belongs to another Business.");
            await access.EnsureBusinessAsync(promotion.BusinessId, token);
            var action = input.Action.Trim().ToLowerInvariant();
            if (action is not ("approve" or "requestchanges" or "reject"))
                throw new ApplicationFailure(FailureKind.Validation, "Choose Approve, Request Changes, or Reject.");
            if (input.Feedback?.Length > 2000)
                throw new ApplicationFailure(FailureKind.Validation, "Review feedback is too long.");
            if (action == "requestchanges" && string.IsNullOrWhiteSpace(input.Feedback))
                throw new ApplicationFailure(FailureKind.Validation, "Feedback is required when requesting changes.");

            var fingerprint = RequestFingerprint.Create(submission.Id.ToString(), action, input.Feedback?.Trim() ?? "");
            var operation = new FinancialOperation(db);
            var replay = await operation.Replay(actor, "ReviewPromotionContent", idempotencyKey, fingerprint, token);
            if (replay is not null)
            {
                var prior = await db.CreatorPromotionContentSubmissions.SingleAsync(x => x.Id == Guid.Parse(replay), token);
                return await BusinessView(prior, allocation, promotion, token);
            }

            if (allocation.Status != CreatorAllocationStatus.Active ||
                !await db.CreatorApplications.AnyAsync(x => x.PromotionId == promotion.Id
                    && x.CreatorId == allocation.CreatorId && x.Status == CreatorApplicationStatus.Approved, token) ||
                await db.CreatorPromotionParticipations.AnyAsync(x => x.CreatorAllocationId == allocation.Id, token))
                throw new ApplicationFailure(FailureKind.Validation, "Content can be reviewed only for an approved, pre-live Creator allocation.");

            var latestRevision = await db.CreatorPromotionContentSubmissions.Where(x => x.CreatorAllocationId == allocation.Id)
                .MaxAsync(x => (int?)x.RevisionNumber, token);
            if (latestRevision != submission.RevisionNumber || submission.ReviewStatus != PromotionContentReviewStatus.UnderReview)
                throw new ApplicationFailure(FailureKind.Validation, "Only the latest content revision under review can be decided.");

            var now = clock.GetUtcNow().UtcDateTime;
            switch (action)
            {
                case "approve": submission.Approve(actor.UserId, now, input.Feedback); break;
                case "requestchanges": submission.RequestChanges(actor.UserId, now, input.Feedback!); break;
                case "reject": submission.Reject(actor.UserId, now, input.Feedback); break;
            }
            var eventType = action switch
            {
                "approve" => "PromotionContentApproved",
                "requestchanges" => "PromotionContentChangesRequested",
                _ => "PromotionContentRejected"
            };
            operation.Remember(actor, "ReviewPromotionContent", idempotencyKey, fingerprint, submission.Id.ToString(), now);
            operation.Audit(actor, eventType, Guid.NewGuid(), now, promotion.Id, allocation.CreatorId);
            operation.Event(eventType, new { PromotionId = promotion.Id, CreatorAllocationId = allocation.Id,
                RevisionNumber = submission.RevisionNumber, CreatorId = allocation.CreatorId }, now);
            return await BusinessView(submission, allocation, promotion, token);
        }, ct);

    public async Task<IReadOnlyList<BusinessPromotionContentReviewCard>> BusinessSubmissionsAsync(Actor actor, CancellationToken ct)
    {
        if (actor.Role != ActorRole.Business || actor.BusinessId is null)
            throw new ApplicationFailure(FailureKind.Forbidden, "Business access is required.");
        await access.EnsureBusinessAsync(actor.BusinessId.Value, ct);
        var rows = await (from submission in db.CreatorPromotionContentSubmissions.AsNoTracking()
                          join allocation in db.CreatorAllocations.AsNoTracking() on submission.CreatorAllocationId equals allocation.Id
                          join promotion in db.Promotions.AsNoTracking() on allocation.PromotionId equals promotion.Id
                          where promotion.BusinessId == actor.BusinessId.Value
                          orderby submission.SubmittedAtUtc descending
                          select new { Submission = submission, Allocation = allocation, Promotion = promotion })
            .ToListAsync(ct);
        var result = new List<BusinessPromotionContentReviewCard>(rows.Count);
        foreach (var row in rows)
        {
            var creator = await directory.CreatorCardAsync(row.Allocation.CreatorId, ct);
            result.Add(new(row.Submission.Id, creator.DisplayName, row.Promotion.Title, row.Submission.Provider,
                row.Submission.ContentReference, row.Submission.RevisionNumber, row.Submission.SubmittedAtUtc,
                row.Submission.ReviewStatus.ToString(), row.Submission.Feedback, row.Submission.ReviewedAtUtc));
        }
        return result;
    }

    private async Task<BusinessPromotionContentReviewCard> BusinessView(
        CreatorPromotionContentSubmission submission, CreatorAllocation allocation, Promotion promotion, CancellationToken ct)
    {
        var creator = await directory.CreatorCardAsync(allocation.CreatorId, ct);
        return new(submission.Id, creator.DisplayName, promotion.Title, submission.Provider, submission.ContentReference,
            submission.RevisionNumber, submission.SubmittedAtUtc, submission.ReviewStatus.ToString(),
            submission.Feedback, submission.ReviewedAtUtc);
    }

    private static CreatorContentSubmissionStatus View(CreatorPromotionContentSubmission row)
        => new(row.RevisionNumber, row.ReviewStatus.ToString(), row.SubmittedAtUtc, row.Feedback);
}
