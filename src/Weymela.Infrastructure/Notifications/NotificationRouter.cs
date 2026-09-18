using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Notifications;

public sealed record NotificationAudience(ActorRole Role, Guid? SubjectId = null, Guid? UserId = null);
public sealed record NotificationPlan(string SourceKey, string Title, string Message, Guid? CampaignId, Guid? BudgetId,
    IReadOnlyList<NotificationAudience> Audiences, string? WorkspacePath = null, Guid? UgcId = null);

public sealed class NotificationRouter(WeymelaDbContext db)
{
    public async Task<NotificationPlan?> ResolveAsync(OutboxMessage row, CancellationToken ct)
    {
        // Audit and domain events can describe the same operation. Each event below has one canonical producer.
        var supported = new[] { "CreatorAppliedAudit", "CreatorApprovedAudit", "CreatorRejectedAudit", "CreatorAllocationCreated", "CreatorBudgetIncreasedAudit",
            "PromotionFunded", "PromotionPublished", "ViewRewardEarned", "CreatorBudgetExhausted", "CreatorPayoutEligible", "CustomerPayoutEligible",
            "PayoutPaid", "FinancialConfigurationEffective", "DepositSubmitted", "DepositReviewed",
            "CreatorProfileApproved", "CreatorProfileCorrectionRequested", "CreatorProfileRejected",
            "BusinessProfileApproved", "BusinessProfileCorrectionRequested", "BusinessProfileRejected",
            "RoleEnrollmentApproved", "RoleEnrollmentRejected",
            "UgcRequestReceived", "UgcRequestApproved", "UgcRequestRejected", "UgcContentSubmitted",
            "UgcChangesRequested", "UgcContentApproved", "UgcContentRejected", "UgcMaterialRevision" };
        if (!supported.Contains(row.EventType, StringComparer.Ordinal)) return null;
        using var document = JsonDocument.Parse(row.Payload); var data = document.RootElement;
        Guid Id(string name) => data.GetProperty(name).GetGuid();
        var key = row.EventType + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Payload)));
        NotificationPlan Plan(string title, string message, Guid? campaign, Guid? budget, params NotificationAudience[] audience) => new(key, title, message, campaign, budget, audience);
        switch (row.EventType)
        {
            case "CreatorAppliedAudit":
            {
                var campaignId=Id("PromotionId");var campaign = await db.Promotions.AsNoTracking().SingleAsync(x => x.Id == campaignId, ct);
                return Plan("New Promotion request", "A Creator has requested to join your Promotion.", campaign.Id, null, new NotificationAudience(ActorRole.Business, campaign.BusinessId));
            }
            case "CreatorApprovedAudit": case "CreatorRejectedAudit":
                return Plan(row.EventType == "CreatorApprovedAudit" ? "Promotion request approved" : "Promotion request reviewed",
                    row.EventType == "CreatorApprovedAudit" ? "Your request was approved. Open your Promotion for next steps." : "The Business did not approve this request. You can discover other eligible Promotions.",
                    Id("PromotionId"), null, new NotificationAudience(ActorRole.Creator, Id("CreatorId"))) with { WorkspacePath = "requests" };
            case "CreatorAllocationCreated": case "CreatorBudgetIncreasedAudit":
            {
                CreatorAllocation budget;
                if (row.EventType == "CreatorAllocationCreated") {var allocationId=Id("AllocationId");budget = await db.CreatorAllocations.AsNoTracking().SingleAsync(x => x.Id == allocationId, ct);}
                else {var promotionId=Id("PromotionId");var creatorId=Id("CreatorId");budget = await db.CreatorAllocations.AsNoTracking().SingleAsync(x => x.PromotionId == promotionId && x.CreatorId == creatorId, ct);}
                return Plan(row.EventType == "CreatorAllocationCreated" ? "Your Creator Budget is ready" : "Your Creator Budget increased",
                    "Open your Promotion to review your saved budget and next steps.", budget.PromotionId, budget.Id, new NotificationAudience(ActorRole.Creator, budget.CreatorId));
            }
            case "PromotionFunded": case "PromotionPublished":
            {
                var promotionId=Id("PromotionId");var p = await db.Promotions.AsNoTracking().SingleAsync(x => x.Id == promotionId, ct);
                return Plan(row.EventType == "PromotionFunded" ? "Promotion funding confirmed" : "Promotion published", "The Promotion workspace shows the saved status.",
                    p.Id, null, new(ActorRole.Business, p.BusinessId), new(ActorRole.PlatformAdmin));
            }
            case "ViewRewardEarned":
                return Plan("View Reward earned", "Verified activity has added to your earnings.", null, Id("AllocationId"), new NotificationAudience(ActorRole.Creator, Id("CreatorId")));
            case "CreatorBudgetExhausted":
            {
                var allocationId=Id("AllocationId");var a = await db.CreatorAllocations.AsNoTracking().SingleAsync(x => x.Id == allocationId, ct);
                var p = await db.Promotions.AsNoTracking().SingleAsync(x => x.Id == a.PromotionId, ct);
                return Plan("Creator Budget needs funding", "The remaining Creator Budget cannot fund the next activity. No partial reward or negative balance was posted.",
                    p.Id, a.Id, new(ActorRole.Business, p.BusinessId), new(ActorRole.Creator, a.CreatorId)) with
                    { SourceKey = $"CreatorBudgetExhausted:{a.Id}:{a.OriginalAllocation.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" };
            }
            case "CreatorPayoutEligible": case "CustomerPayoutEligible":
                return Plan("Eligible for payout", "Your available balance has reached the current minimum to cash out.", null, null,
                    new NotificationAudience(row.EventType == "CreatorPayoutEligible" ? ActorRole.Creator : ActorRole.Customer, Id("BeneficiaryId"))) with { WorkspacePath = "payouts" };
            case "PayoutPaid":
            {
                var payoutId=Id("PayoutId");var payout = await db.PayoutRecords.AsNoTracking().SingleAsync(x => x.Id == payoutId, ct);
                return Plan("Payout confirmed", "Admin has recorded your confirmed external payment. Remaining funds carry forward.", null, null,
                    new NotificationAudience(payout.Beneficiary == PayoutBeneficiary.Creator ? ActorRole.Creator : ActorRole.Customer, payout.BeneficiaryId)) with { WorkspacePath = "payouts" };
            }
            case "FinancialConfigurationEffective":
            {
                var versionId=Id("VersionId");var current = await db.FinancialConfigurationVersions.AsNoTracking().SingleAsync(x => x.Id == versionId, ct);
                var priorId = data.TryGetProperty("PreviousVersionId", out var priorValue) && priorValue.ValueKind == JsonValueKind.String ? priorValue.GetGuid() : (Guid?)null;
                var prior = priorId is null ? null : await db.FinancialConfigurationVersions.AsNoTracking().SingleAsync(x => x.Id == priorId, ct);
                List<NotificationAudience> targets = [];
                if (prior is null || BusinessRates(current) != BusinessRates(prior)) targets.Add(new(ActorRole.Business));
                if (prior is null || CreatorRates(current) != CreatorRates(prior)) targets.Add(new(ActorRole.Creator));
                if (prior is null || current.CustomerPayoutThreshold != prior.CustomerPayoutThreshold || current.ViewPlusCommission.CustomerCashbackPercent != prior.ViewPlusCommission.CustomerCashbackPercent) targets.Add(new(ActorRole.Customer));
                return Plan("Financial settings updated", "Current pricing or payout terms have changed. Existing Promotion pricing stays unchanged.", null, null, targets.ToArray()) with { WorkspacePath = "pricing", SourceKey = "FinancialConfigurationEffective:" + current.Id };
            }
            case "DepositSubmitted":
                return Plan("Deposit awaiting review", "A Business submitted a deposit reference. Confirm receipt externally before any approval.", null, null, new NotificationAudience(ActorRole.PlatformAdmin)) with { WorkspacePath = "businesses" };
            case "DepositReviewed":
            {
                var depositId=Id("DepositId");var request = await db.DepositRequests.AsNoTracking().SingleAsync(x => x.Id == depositId, ct);
                return Plan(request.Status == Application.Operations.DepositReviewStatus.Approved ? "Deposit approved" : "Deposit reviewed",
                    request.Status == Application.Operations.DepositReviewStatus.Approved ? "Your saved wallet balance includes the approved funds." : "The deposit was not approved. No funds were credited.",
                    null, null, new NotificationAudience(ActorRole.Business, request.BusinessId)) with { WorkspacePath = "wallet" };
            }
            case "CreatorProfileApproved": case "CreatorProfileCorrectionRequested": case "CreatorProfileRejected":
            {
                var userId=Id("UserId");var subject=Id("ProfileSubjectId");
                var audience=row.EventType=="CreatorProfileApproved"?new NotificationAudience(ActorRole.Creator,subject):new NotificationAudience(ActorRole.Customer,UserId:userId);
                return Plan(row.EventType switch{"CreatorProfileApproved"=>"Creator profile approved","CreatorProfileCorrectionRequested"=>"Creator profile needs changes",_=>"Creator profile reviewed"},
                    row.EventType switch{"CreatorProfileApproved"=>"Your Creator profile is active.","CreatorProfileCorrectionRequested"=>"Review the requested Creator profile corrections.",_=>"Your Creator profile was not approved."},null,null,audience) with { WorkspacePath="profiles" };
            }
            case "BusinessProfileApproved": case "BusinessProfileCorrectionRequested": case "BusinessProfileRejected":
            {
                var userId=Id("UserId");var subject=Id("ProfileSubjectId");
                var audience=row.EventType=="BusinessProfileApproved"?new NotificationAudience(ActorRole.Business,subject):new NotificationAudience(ActorRole.Customer,UserId:userId);
                return Plan(row.EventType switch{"BusinessProfileApproved"=>"Business profile approved","BusinessProfileCorrectionRequested"=>"Business profile needs changes",_=>"Business profile reviewed"},
                    row.EventType switch{"BusinessProfileApproved"=>"Your Business profile is active.","BusinessProfileCorrectionRequested"=>"Review the requested Business profile corrections.",_=>"Your Business profile was not approved."},null,null,audience) with { WorkspacePath="profiles" };
            }
            case "RoleEnrollmentApproved": case "RoleEnrollmentRejected":
            {
                var enrollmentId=Id("Id");var enrollment=await db.RoleEnrollments.AsNoTracking().SingleAsync(x=>x.Id==enrollmentId,ct);
                if(enrollment.RequestedRole is not (ActorRole.Creator or ActorRole.Business))return null;
                if(row.EventType=="RoleEnrollmentApproved")
                {
                    var subject=await db.CommercePermissions.AsNoTracking().Where(x=>x.UserId==enrollment.UserId&&x.Role==enrollment.RequestedRole&&x.IsActive).Select(x=>x.SubjectId).SingleAsync(ct);
                    return Plan(enrollment.RequestedRole==ActorRole.Creator?"Creator profile approved":"Business profile approved",
                        $"Your {enrollment.RequestedRole} profile is active.",null,null,new NotificationAudience(enrollment.RequestedRole,subject)) with { WorkspacePath="profiles" };
                }
                return Plan(enrollment.RequestedRole==ActorRole.Creator?"Creator profile reviewed":"Business profile reviewed",
                    $"Your {enrollment.RequestedRole} profile was not approved.",null,null,new NotificationAudience(ActorRole.Customer,UserId:enrollment.UserId)) with { WorkspacePath="profiles" };
            }
            case "UgcRequestReceived":
                return Plan("New UGC request","A Creator requested to join your UGC opportunity.",null,null,new NotificationAudience(ActorRole.Business,Id("BusinessId"))) with { UgcId=Id("OpportunityId") };
            case "UgcRequestApproved": case "UgcRequestRejected":
                return Plan(row.EventType=="UgcRequestApproved"?"UGC request approved":"UGC request reviewed",
                    row.EventType=="UgcRequestApproved"?"Open your UGC assignment to review the requirements.":"The Business did not approve this UGC request.",null,null,
                    new NotificationAudience(ActorRole.Creator,Id("CreatorId"))) with { UgcId=Id("OpportunityId") };
            case "UgcContentSubmitted":
                return Plan("UGC content submitted","A Creator submitted content for your review.",null,null,new NotificationAudience(ActorRole.Business,Id("BusinessId"))) with { UgcId=Id("OpportunityId") };
            case "UgcChangesRequested": case "UgcContentApproved": case "UgcContentRejected":
                return Plan(row.EventType switch{"UgcChangesRequested"=>"UGC changes requested","UgcContentApproved"=>"UGC content approved",_=>"UGC content reviewed"},
                    row.EventType switch{"UgcChangesRequested"=>"Review the Business feedback and resubmit your content.","UgcContentApproved"=>"Your UGC content was approved and your earning was recorded.",_=>"The Business did not approve this content."},null,null,
                    new NotificationAudience(ActorRole.Creator,Id("CreatorId"))) with { UgcId=Id("OpportunityId") };
            case "UgcMaterialRevision":
            {
                var opportunityId=Id("OpportunityId");var creators=await db.UgcAssignments.AsNoTracking().Where(x=>x.UgcOpportunityId==opportunityId&&x.RevisionAcceptanceRequired).Select(x=>x.CreatorId).Distinct().ToListAsync(ct);
                return Plan("UGC requirements updated","Review and accept the updated requirements before continuing.",null,null,creators.Select(x=>new NotificationAudience(ActorRole.Creator,x)).ToArray()) with { UgcId=opportunityId };
            }
        }
        return null;
    }
    private static string BusinessRates(FinancialConfigurationVersion v) => System.Text.Json.JsonSerializer.Serialize(new {
        only = new { v.ViewOnly.ViewsPerReward, v.ViewOnly.BusinessCharge, v.ViewOnly.MinimumPromotionBudget },
        hybrid = new { v.ViewPlusCommission.ViewsPerReward, v.ViewPlusCommission.BusinessCharge, v.ViewPlusCommission.MinimumPromotionBudget },
        total = v.ViewPlusCommission.CreatorCommissionPercent + v.ViewPlusCommission.CustomerCashbackPercent + v.ViewPlusCommission.PlatformPercent });
    private static string CreatorRates(FinancialConfigurationVersion v) => System.Text.Json.JsonSerializer.Serialize(new {
        only = new { v.ViewOnly.ViewsPerReward, v.ViewOnly.CreatorEarning }, hybrid = new { v.ViewPlusCommission.ViewsPerReward, v.ViewPlusCommission.CreatorEarning },
        v.ViewPlusCommission.CreatorCommissionPercent, v.CreatorPayoutThreshold });
    public static string Route(NotificationPlan plan, ActorRole role) => role switch
    {
        ActorRole.PlatformAdmin or ActorRole.OperationsAdmin => plan.UgcId is {} au ? $"/admin/ugc/{au}" : plan.CampaignId is {} p ? $"/admin/promotions/{p}" : "/admin/" + (plan.WorkspacePath == "businesses" ? "businesses" : "notifications"),
        ActorRole.Business => plan.UgcId is {} bu ? $"/business/ugc/{bu}" : plan.CampaignId is {} b ? $"/business/promotions/{b}" : "/business/" + (plan.WorkspacePath == "wallet" ? "wallet" : "notifications"),
        ActorRole.Creator => plan.UgcId is {} cu ? $"/creator/ugc/{cu}" : plan.BudgetId is {} a ? $"/creator/promotions/{a}" : "/creator/" + (plan.WorkspacePath is "requests" or "payouts" ? plan.WorkspacePath : "discover"),
        ActorRole.Customer => plan.WorkspacePath=="profiles"?"/onboarding":"/shopper",
        _ => "/checkout"
    };
}
