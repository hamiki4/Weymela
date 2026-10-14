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
            var closed = campaign.EndUtc <= Now || campaign.Status is "Completed" or "Ended" or "Cancelled" or "BudgetExhausted" || campaign.Remaining <= 0 && campaign.Status != "Draft";
            var group = closed ? "Completed" : campaign.Status is "Draft" or "Funded" ? "Pending" : "Active";
            items.Add(new("campaign-" + campaign.Id, campaign.Id, campaign.Id, campaign.Type, campaign.Title, group,
                campaign.EndUtc <= Now ? "Expired" : campaign.Status, campaign.CampaignBudget, campaign.Remaining, campaign.ContentDueAtUtc,
                group == "Pending" ? "Complete setup" : "Manage campaign", $"/business/campaigns/{campaign.Id}",
                RemainingDays: group == "Active" ? Math.Max(0, (int)Math.Ceiling((campaign.EndUtc - Now).TotalDays)) : null));
            foreach (var application in applications.Where(x => x.PromotionId == campaign.Id))
                items.Add(new("application-" + application.Id, application.Id, campaign.Id, "Creator application", campaign.Title,
                    "Pending", "Awaiting Business decision", 0, 0, campaign.ApplicationClosesAtUtc,
                    "Review application", $"/business/campaigns/{campaign.Id}?tab=applicants", (await directory.CreatorCardAsync(application.CreatorId, ct)).DisplayName));
            foreach (var allocation in allocations.Where(x => x.PromotionId == campaign.Id))
            {
                var revision = content.FirstOrDefault(x => x.CreatorAllocationId == allocation.Id);
                var publication = publications.FirstOrDefault(x => x.CreatorAllocationId == allocation.Id);
                var days = publication?.WentLiveAtUtc is { } live ? Math.Max(0, (int)Math.Ceiling((live.AddDays(campaign.PromotionLiveDurationDays) - Now).TotalDays)) : (int?)null;
                var complete = closed || allocation.Status is CreatorAllocationStatus.Completed or CreatorAllocationStatus.Cancelled or CreatorAllocationStatus.Exhausted || days == 0;
                var review = !complete && revision?.ReviewStatus == PromotionContentReviewStatus.UnderReview;
                var status = complete ? days == 0 ? "Expired" : "Completed" : review ? "Pending Business review"
                    : publication?.WentLiveAtUtc != null ? "Live" : publication?.Status == PublicationVerificationStatus.Verified ? "Ready for Go Live"
                    : revision?.ReviewStatus == PromotionContentReviewStatus.Approved ? "Approved for publication" : "Creator work in progress";
                items.Add(new("work-" + allocation.Id, allocation.Id, campaign.Id, "Creator work", campaign.Title,
                    complete ? "Completed" : review ? "Pending" : "Active", status, allocation.OriginalAllocation.Amount,
                    complete ? 0 : allocation.RemainingAmount.Amount, allocation.ContentDueAtUtc,
                    review ? "Review video" : "View Creator work", $"/business/campaigns/{campaign.Id}?tab=budgets",
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
            var expired = !offerRunning && opportunity.DueDateUtc <= Now;
            var closed = opportunity.Status == UgcOpportunityStatus.Cancelled || !offerRunning && (opportunity.Status == UgcOpportunityStatus.Completed || expired);
            items.Add(new("ugc-" + opportunity.Id, opportunity.Id, opportunity.Id, "UGC campaign", opportunity.Title,
                closed ? "Completed" : opportunity.Status == UgcOpportunityStatus.Draft ? "Pending" : "Active",
                offerRunning && opportunity.Status == UgcOpportunityStatus.Completed ? "Creator work completed · Customer offer active" : expired ? "Expired" : opportunity.Status.ToString(), opportunity.RequiredFunding.Amount + (offer?.FundedLimit.Amount ?? 0), opportunity.ReservedFunding.Amount + (offer?.RemainingFunding.Amount ?? 0),
                offerRunning ? offer!.EndsAtUtc : opportunity.DueDateUtc, opportunity.Status == UgcOpportunityStatus.Draft ? "Complete setup" : "Manage campaign", $"/business/ugc/{opportunity.Id}",
                RemainingDays: offerRunning ? Math.Max(0, (int)Math.Ceiling((offer!.EndsAtUtc - Now).TotalDays)) : null));
            foreach (var request in ugcRequests.Where(x => x.UgcOpportunityId == opportunity.Id))
                items.Add(new("ugc-request-" + request.Id, request.Id, opportunity.Id, "Creator application", opportunity.Title,
                    "Pending", "Awaiting Business decision", opportunity.CreatorPayment.Amount, opportunity.CreatorPayment.Amount,
                    opportunity.ApplicationClosesAtUtc, "Review application", $"/business/ugc/{opportunity.Id}", (await directory.CreatorCardAsync(request.CreatorId, ct)).DisplayName));
            foreach (var assignment in assignments.Where(x => x.UgcOpportunityId == opportunity.Id))
            {
                var publication = ugcPublications.FirstOrDefault(x => x.UgcAssignmentId == assignment.Id);
                var complete = publication?.WentLiveAtUtc != null || assignment.Status == UgcAssignmentStatus.Rejected;
                var review = assignment.Status == UgcAssignmentStatus.Submitted;
                var status = complete ? publication?.WentLiveAtUtc != null ? "Published · fixed fee earned" : "Rejected"
                    : review ? "Pending Business review" : publication?.Status == PublicationVerificationStatus.Verified ? "Ready for Go Live"
                    : publication?.Status == PublicationVerificationStatus.VerificationPending ? "Publication verification pending"
                    : assignment.Status == UgcAssignmentStatus.Approved ? "Approved for publication" : "Creator work in progress";
                items.Add(new("ugc-work-" + assignment.Id, assignment.Id, opportunity.Id, "UGC deliverable", opportunity.Title,
                    complete ? "Completed" : review ? "Pending" : "Active", status, assignment.CreatorPayment.Amount,
                    complete ? 0 : assignment.CreatorPayment.Amount, assignment.ContentDueAtUtc ?? opportunity.DueDateUtc,
                    review ? "Review video" : "View deliverable", $"/business/ugc/{opportunity.Id}", (await directory.CreatorCardAsync(assignment.CreatorId, ct)).DisplayName));
            }
        }
        return items.OrderBy(x => x.Group).ThenBy(x => x.DueAtUtc).ThenBy(x => x.Key).ToArray();
    }
}
