using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;

namespace Weymela.Infrastructure.Web;

public sealed partial class WorkspaceQueries
{
    public async Task<IReadOnlyList<BusinessPromotionItem>> BusinessPromotionTrackingAsync(Actor actor, CancellationToken ct)
    {
        await DemandBusiness(actor, ct);
        var items = new List<BusinessPromotionItem>();
        var campaigns = await BusinessCampaignsAsync(actor, ct);
        var promotionIds = campaigns.Select(x => x.Id).ToArray();
        var applications = await db.CreatorApplications.AsNoTracking().Where(x => promotionIds.Contains(x.PromotionId)
            && x.Status == CreatorApplicationStatus.Pending).ToListAsync(ct);
        var allocations = await db.CreatorAllocations.AsNoTracking().Where(x => promotionIds.Contains(x.PromotionId)).ToListAsync(ct);
        var allocationIds = allocations.Select(x => x.Id).ToArray();
        var content = await db.CreatorPromotionContentSubmissions.AsNoTracking().Where(x => allocationIds.Contains(x.CreatorAllocationId))
            .OrderByDescending(x => x.RevisionNumber).ToListAsync(ct);
        var publications = await db.CreatorPublicationVerifications.AsNoTracking().Where(x => x.CreatorAllocationId != null
            && allocationIds.Contains(x.CreatorAllocationId.Value)).OrderByDescending(x => x.RequestedAtUtc).ToListAsync(ct);
        foreach (var campaign in campaigns)
        {
            var campaignAllocations = allocations.Where(x => x.PromotionId == campaign.Id).Select(x => x.Id).ToHashSet();
            var liveEnd = publications.Where(x => x.CreatorAllocationId is { } id && campaignAllocations.Contains(id)
                && x.Status == PublicationVerificationStatus.Verified && x.WentLiveAtUtc is not null && x.EndedAtUtc is null && x.ExpiresAtUtc > Now)
                .Select(x => x.ExpiresAtUtc).DefaultIfEmpty().Max();
            var effectiveEnd = liveEnd > campaign.EndUtc ? liveEnd.Value : campaign.EndUtc;
            var closed = effectiveEnd <= Now || campaign.Status is "Completed" or "Ended" or "Cancelled";
            var funding = !closed && (campaign.Status == "BudgetExhausted" || campaign.Remaining <= 0 && campaign.Status is not ("Draft" or "Funded"));
            var group = closed ? "Completed" : funding || campaign.Status is "Draft" or "Funded" ? "Pending" : "Active";
            items.Add(new("campaign-" + campaign.Id, campaign.Id, campaign.Id, campaign.Type, campaign.Title, group,
                effectiveEnd <= Now ? "Expired" : funding ? "FundingRequired" : campaign.Status, campaign.CampaignBudget, campaign.Remaining, campaign.ContentDueAtUtc,
                funding ? "Add Funds" : group == "Pending" ? "Complete setup" : "Manage campaign", $"/business/campaigns/{campaign.Id}",
                RemainingDays: group == "Active" ? Math.Max(0, (int)Math.Ceiling((effectiveEnd - Now).TotalDays)) : null));
            foreach (var application in applications.Where(x => x.PromotionId == campaign.Id))
                items.Add(new("application-" + application.Id, application.Id, campaign.Id, "Creator application", campaign.Title,
                    "Pending", "Awaiting Business decision", 0, 0, campaign.ApplicationClosesAtUtc,
                    "Review application", $"/business/campaigns/{campaign.Id}?tab=applicants", (await directory.CreatorCardAsync(application.CreatorId, ct)).DisplayName));
            foreach (var allocation in allocations.Where(x => x.PromotionId == campaign.Id))
            {
                var revision = content.FirstOrDefault(x => x.CreatorAllocationId == allocation.Id);
                var publication = publications.FirstOrDefault(x => x.CreatorAllocationId == allocation.Id);
                var days = publication?.WentLiveAtUtc is { } live ? Math.Max(0, (int)Math.Ceiling((live.AddDays(campaign.PromotionLiveDurationDays) - Now).TotalDays)) : (int?)null;
                var complete = closed || allocation.Status is CreatorAllocationStatus.Completed or CreatorAllocationStatus.Cancelled || days == 0;
                var needsFunding = !complete && (allocation.Status == CreatorAllocationStatus.Exhausted || allocation.RemainingAmount.Amount <= 0);
                var review = !complete && !needsFunding && revision?.ReviewStatus == PromotionContentReviewStatus.UnderReview;
                var status = complete ? days == 0 ? "Expired" : "Completed" : needsFunding ? "FundingRequired" : review ? "Pending Business review"
                    : publication?.WentLiveAtUtc != null ? "Live" : publication?.Status == PublicationVerificationStatus.Verified ? "Ready for Go Live"
                    : revision?.ReviewStatus == PromotionContentReviewStatus.Approved ? "Approved for publication" : "Creator work in progress";
                items.Add(new("work-" + allocation.Id, allocation.Id, campaign.Id, "Creator work", campaign.Title,
                    complete ? "Completed" : review || needsFunding ? "Pending" : "Active", status, allocation.OriginalAllocation.Amount,
                    complete ? 0 : allocation.RemainingAmount.Amount, allocation.ContentDueAtUtc,
                    needsFunding ? "Add Funds" : review ? "Review video" : "View Creator work", $"/business/campaigns/{campaign.Id}?tab=budgets",
                    (await directory.CreatorCardAsync(allocation.CreatorId, ct)).DisplayName, days));
            }
        }
        var ugc = await db.UgcOpportunities.AsNoTracking().Where(x => x.BusinessId == actor.BusinessId).ToListAsync(ct);
        var ugcIds = ugc.Select(x => x.Id).ToArray();
        var ugcRequests = await db.UgcCreatorRequests.AsNoTracking().Where(x => ugcIds.Contains(x.UgcOpportunityId) && x.Status == UgcRequestStatus.Pending).ToListAsync(ct);
        var assignments = await db.UgcAssignments.AsNoTracking().Where(x => ugcIds.Contains(x.UgcOpportunityId)).ToListAsync(ct);
        var assignmentIds = assignments.Select(x => x.Id).ToArray();
        var ugcPublications = await db.CreatorPublicationVerifications.AsNoTracking().Where(x => x.UgcAssignmentId != null
            && assignmentIds.Contains(x.UgcAssignmentId.Value)).OrderByDescending(x => x.RequestedAtUtc).ToListAsync(ct);
        var offers = await db.UgcCustomerOffers.AsNoTracking().Where(x => ugcIds.Contains(x.UgcOpportunityId)).ToListAsync(ct);
        foreach (var opportunity in ugc)
        {
            var offer = offers.FirstOrDefault(x => x.UgcOpportunityId == opportunity.Id);
            var offerRunning = offer is not null && offer.Status == UgcCustomerOfferStatus.Active && offer.RemainingFunding.Amount > 0 && offer.EndsAtUtc > Now;
            var offerNeedsFunding = opportunity.Status != UgcOpportunityStatus.Cancelled && offer is { Status: UgcCustomerOfferStatus.Exhausted } && offer.EndsAtUtc > Now;
            var workIds = assignments.Where(x => x.UgcOpportunityId == opportunity.Id).Select(x => x.Id).ToHashSet();
            var liveEnd = ugcPublications.Where(x => x.UgcAssignmentId is { } id && workIds.Contains(id)
                && x.Status == PublicationVerificationStatus.Verified && x.WentLiveAtUtc is not null && x.EndedAtUtc is null && x.ExpiresAtUtc > Now)
                .Select(x => x.ExpiresAtUtc).DefaultIfEmpty().Max();
            var expired = liveEnd is null && !offerRunning && !offerNeedsFunding && opportunity.DueDateUtc <= Now;
            var closed = opportunity.Status == UgcOpportunityStatus.Cancelled || liveEnd is null && !offerRunning && !offerNeedsFunding && (opportunity.Status == UgcOpportunityStatus.Completed || expired);
            items.Add(new("ugc-" + opportunity.Id, opportunity.Id, opportunity.Id, "UGC campaign", opportunity.Title,
                closed ? "Completed" : offerNeedsFunding || opportunity.Status == UgcOpportunityStatus.Draft ? "Pending" : "Active",
                offerNeedsFunding ? "Cashback funds exhausted" : liveEnd is not null && !offerRunning ? "Live" : offerRunning && opportunity.Status == UgcOpportunityStatus.Completed ? "Creator work completed · Customer offer active" : expired ? "Expired" : opportunity.Status.ToString(), opportunity.RequiredFunding.Amount + (offer?.FundedLimit.Amount ?? 0), opportunity.ReservedFunding.Amount + (offer?.RemainingFunding.Amount ?? 0),
                offerRunning || offerNeedsFunding ? offer!.EndsAtUtc : liveEnd ?? opportunity.DueDateUtc, offerNeedsFunding ? "Add Funds" : opportunity.Status == UgcOpportunityStatus.Draft ? "Complete setup" : "Manage campaign", $"/business/ugc/{opportunity.Id}",
                RemainingDays: offerRunning || offerNeedsFunding ? Math.Max(0, (int)Math.Ceiling((offer!.EndsAtUtc - Now).TotalDays)) : liveEnd is { } ends ? Math.Max(0, (int)Math.Ceiling((ends - Now).TotalDays)) : null));
            foreach (var request in ugcRequests.Where(x => x.UgcOpportunityId == opportunity.Id))
                items.Add(new("ugc-request-" + request.Id, request.Id, opportunity.Id, "Creator application", opportunity.Title,
                    "Pending", "Awaiting Business decision", opportunity.CreatorPayment.Amount, opportunity.CreatorPayment.Amount,
                    opportunity.ApplicationClosesAtUtc, "Review application", $"/business/ugc/{opportunity.Id}", (await directory.CreatorCardAsync(request.CreatorId, ct)).DisplayName));
            foreach (var assignment in assignments.Where(x => x.UgcOpportunityId == opportunity.Id))
            {
                var publication = ugcPublications.FirstOrDefault(x => x.UgcAssignmentId == assignment.Id);
                var live = publication?.WentLiveAtUtc != null && publication.EndedAtUtc is null && publication.ExpiresAtUtc > Now;
                var complete = assignment.CompletedAtUtc != null || publication?.WentLiveAtUtc != null || assignment.Status == UgcAssignmentStatus.Rejected;
                var review = assignment.Status == UgcAssignmentStatus.Submitted;
                var status = live ? "Live" : complete ? assignment.Status == UgcAssignmentStatus.Rejected ? "Rejected" : "Completed"
                    : review ? "Pending Business review" : publication?.Status == PublicationVerificationStatus.Verified ? "Ready for Go Live"
                    : publication?.Status == PublicationVerificationStatus.VerificationPending ? "Publication verification pending"
                    : assignment.Status == UgcAssignmentStatus.Approved ? "Approved for publication" : "Creator work in progress";
                items.Add(new("ugc-work-" + assignment.Id, assignment.Id, opportunity.Id, "UGC deliverable", opportunity.Title,
                    complete ? "Completed" : review ? "Pending" : "Active", status, assignment.CreatorPayment.Amount,
                    complete ? 0 : assignment.CreatorPayment.Amount, publication?.ExpiresAtUtc ?? assignment.ContentDueAtUtc ?? opportunity.DueDateUtc,
                    review ? "Review video" : "View deliverable", $"/business/ugc/{opportunity.Id}", (await directory.CreatorCardAsync(assignment.CreatorId, ct)).DisplayName));
            }
        }
        return items.OrderBy(x => x.Group).ThenBy(x => x.DueAtUtc).ThenBy(x => x.Key).ToArray();
    }
}
