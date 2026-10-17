using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence.Repositories;

namespace Weymela.Infrastructure.Web;

public sealed partial class WorkspaceQueries
{
    public async Task<IReadOnlyList<CreatorSocialProfileView>> CreatorSocialAccountsAsync(Actor actor,CancellationToken ct)
    {
        await DemandCreator(actor,ct);
        return await db.CreatorSocialProfiles.AsNoTracking()
            .Where(x=>x.CreatorId==actor.CreatorId&&x.IsActive)
            .OrderBy(x=>x.Platform)
            .Select(x=>new CreatorSocialProfileView(x.Id,x.Platform.ToString(),x.ProfileUrl,
                x.SelfReportedAudience,x.VerificationStatus,x.VerifiedAudience,x.AudienceVerificationSource))
            .ToListAsync(ct);
    }
    public async Task<IReadOnlyList<CreatorRequestCard>> CreatorRequestsAsync(Actor actor,CancellationToken ct)
    {
        await DemandCreator(actor,ct);
        var requests=await db.CreatorApplications.AsNoTracking().Where(x=>x.CreatorId==actor.CreatorId).OrderByDescending(x=>x.AppliedAtUtc).ToListAsync(ct);
        var result=new List<CreatorRequestCard>();
        foreach(var r in requests)
        {
            var p=await Campaign(r.PromotionId,ct);var business=await directory.BusinessCardAsync(p.BusinessId,ct);
            result.Add(new(r.Id,p.Id,p.Title,business.DisplayName,PromotionTypeLabel(p.PromotionType),r.Status.ToString(),r.AppliedAtUtc));
        }
        return result;
    }
    public async Task<CreatorHome> CreatorHomeAsync(Actor actor,CancellationToken ct)
    {
        await DemandCreator(actor,ct);
        var now=Now;
        var activeAllocations=await (from participation in db.CreatorPromotionParticipations.AsNoTracking()
            join promotion in db.Promotions.AsNoTracking() on participation.PromotionId equals promotion.Id
            where participation.CreatorId==actor.CreatorId&&participation.Status==ParticipationStatus.Active
            select new { Participation=participation, promotion.PromotionLiveDurationDays }).ToListAsync(ct);
        return new(await directory.CreatorCardAsync(actor.CreatorId!.Value,ct),
            await db.CreatorApplications.CountAsync(x=>x.CreatorId==actor.CreatorId&&x.Status==CreatorApplicationStatus.Pending,ct),
            activeAllocations.Count(x=>x.Participation.IsLive(now,x.PromotionLiveDurationDays)),await EarningsAsync(actor,ct));
    }
    public async Task<CreatorPricing> CreatorPricingAsync(Actor actor,CancellationToken ct)
    {
        await DemandCreator(actor,ct);var v=await new FinancialConfigurationResolver(db).FindEffectiveAsync(Now,ct);
        var rows=new[] { v?.ViewOnly, v?.ViewPlusCommission }.OfType<PricingSnapshot>().Select(CreatorPrice).ToArray();
        return new(rows,v?.CreatorPayoutThreshold?.Amount,v?.EffectiveFromUtc,v?.MonthlyPayoutEnabled==true,v?.UgcPayoutMinimumExempt==true);
    }
    public async Task<IReadOnlyList<CampaignOpportunity>> DiscoverAsync(Actor actor,CancellationToken ct)
    {
        await DemandCreator(actor,ct);var creator=await directory.CreatorCardAsync(actor.CreatorId!.Value,ct);
        var enforceAudience = (await new FinancialConfigurationResolver(db).FindEffectiveAsync(Now,ct))?.EnforceAudienceRequirements;
        var profile=new CreatorVerifiedProfile(creator.Category,creator.Region,creator.VerifiedFollowers,creator.SocialVerified);
        var campaigns=await db.Promotions.AsNoTracking().Include(x=>x.Allocations).Include(x=>x.Platforms).Where(x=>(x.Status==PromotionStatus.Published||x.Status==PromotionStatus.Active)&&x.EndDateUtc>Now).ToListAsync(ct);
        var socials=await db.CreatorSocialProfiles.AsNoTracking().Where(x=>x.CreatorId==actor.CreatorId&&x.IsActive).ToListAsync(ct);
        var businessIds=campaigns.Select(x=>x.BusinessId).Distinct().ToArray();
        var activeBusinesses=await db.CommercePermissions.AsNoTracking().Where(x=>x.Role==ActorRole.Business&&businessIds.Contains(x.SubjectId)&&x.IsActive).Select(x=>x.SubjectId).Distinct().ToListAsync(ct);
        var businessCards=new Dictionary<Guid,BusinessCard>();foreach(var businessId in activeBusinesses)businessCards[businessId]=await directory.BusinessCardAsync(businessId,ct);
        var requests=await db.CreatorApplications.AsNoTracking().Where(x=>x.CreatorId==actor.CreatorId&&campaigns.Select(p=>p.Id).Contains(x.PromotionId)).OrderByDescending(x=>x.AppliedAtUtc).ToListAsync(ct);
        var result=new List<CampaignOpportunity>();
        foreach(var p in campaigns)
        {
            if(p.ReservedBudget.Amount<=0||!new CreatorEligibility().IsEligible(actor.CreatorId.Value,p.Eligibility,profile))continue;
            if(!activeBusinesses.Contains(p.BusinessId))continue;
            var request=requests.FirstOrDefault(x=>x.PromotionId==p.Id);
            if(p.ApplicationClosesAtUtc is { } closes && Now>=closes && request is null)continue;
            var allPlatformsFull=p.Platforms.Count>0&&p.Platforms.All(slot=>slot.Available<=0);
            if(p.UnallocatedBudget.Amount<=0&&request is null&&!allPlatformsFull)continue;
            var eligibleSocials=socials.Where(s=>p.Platforms.Any(slot=>slot.Available>0 && SocialAudienceEligibility.Matches(s,slot.Platform,slot.MinimumAudience,enforceAudience ?? true))).Select(s=>new CreatorSocialProfileView(s.Id,s.Platform.ToString(),s.ProfileUrl,s.SelfReportedAudience,s.VerificationStatus,s.VerifiedAudience,s.AudienceVerificationSource)).ToArray();
            if(p.Platforms.Count>0&&eligibleSocials.Length==0&&request is null&&!allPlatformsFull)continue;
            result.Add(new(p.Id,p.PublicPromotionId,businessCards[p.BusinessId],p.Title,p.Description,PromotionTypeLabel(p.PromotionType),
                p.Eligibility.Requirements,p.Eligibility.Category,p.Eligibility.Market,p.Eligibility.MinimumVerifiedFollowers,p.StartDateUtc,p.EndDateUtc,
                p.PromotionLiveDurationDays,CreatorPrice(p.PricingSnapshot),request?.Status.ToString(),"Your Creator profile meets this Promotion's requirements.",p.Slogan,p.Location,
                p.Platforms.Select(x=>new PromotionPlatformView(x.Platform.ToString(),x.ApprovedCount,x.Capacity,x.Available,x.MinimumAudience)).ToArray(),eligibleSocials,
                p.Platforms.Sum(x=>x.ApprovedCount),p.Platforms.Sum(x=>x.Capacity),p.ApplicationClosesAtUtc,p.ContentDueAtUtc));
        }
        return result;
    }
    public async Task<CampaignOpportunity> OpportunityAsync(Actor actor,Guid id,CancellationToken ct) =>
        (await DiscoverAsync(actor,ct)).SingleOrDefault(x=>x.Id==id)??throw new ApplicationFailure(FailureKind.NotFound,"This Promotion is not available for you to join.");
    public async Task<IReadOnlyList<CreatorCampaignCard>> CreatorCampaignsAsync(Actor actor,CancellationToken ct)
    {
        await DemandCreator(actor,ct);var allocations=await db.CreatorAllocations.AsNoTracking().Where(x=>x.CreatorId==actor.CreatorId).ToListAsync(ct);
        var result=new List<CreatorCampaignCard>();
        foreach(var a in allocations)
        {
            var p=await Campaign(a.PromotionId,ct);var f=await Financial.CreatorAsync(actor,a.Id,ct);
            var live=await db.CreatorPromotionParticipations.AsNoTracking().SingleOrDefaultAsync(x=>x.CreatorAllocationId==a.Id,ct);
            var submission=await db.CreatorPromotionContentSubmissions.AsNoTracking().Where(x=>x.CreatorAllocationId==a.Id)
                .OrderByDescending(x=>x.RevisionNumber).FirstOrDefaultAsync(ct);
            var publication=await db.CreatorPublicationVerifications.AsNoTracking().Where(x=>x.CreatorAllocationId==a.Id)
                .OrderByDescending(x=>x.RequestedAtUtc).FirstOrDefaultAsync(ct);
            var socialProfile=a.CreatorSocialProfileId is {} socialId
                ? await db.CreatorSocialProfiles.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==socialId,ct):null;
            var closed=a.Status is CreatorAllocationStatus.Completed or CreatorAllocationStatus.Cancelled;
            var remaining=live is null||live.Status==ParticipationStatus.Completed?null:live.RemainingDays(Now,p.PromotionLiveDurationDays);
            var status=closed?a.Status.ToString():live is not null
                ? remaining is null?"Ended":live.Status.ToString()
                : publication?.Status==PublicationVerificationStatus.VerificationPending?"VerificationPending"
                : publication?.Status==PublicationVerificationStatus.Verified?"ReadyToGoLive"
                : submission?.ReviewStatus switch
                {
                    PromotionContentReviewStatus.UnderReview=>"UnderReview",
                    PromotionContentReviewStatus.ChangesRequested=>"ChangesRequested",
                    PromotionContentReviewStatus.Approved=>"ReadyToPublish",
                    PromotionContentReviewStatus.Rejected=>"Rejected",
                    _=>"Approved"
                };
            var contentStatus=live is not null
                ? remaining is null?"Ended":"Live"
                : publication?.Status==PublicationVerificationStatus.VerificationPending?"VerificationPending"
                : publication?.Status==PublicationVerificationStatus.Verified?"ReadyToGoLive"
                : submission?.ReviewStatus.ToString()??"AddContent";
            result.Add(new(p.Id,a.Id,live?.Id,p.Title,await directory.BusinessCardAsync(p.BusinessId,ct),PromotionTypeLabel(p.PromotionType),a.OriginalAllocation.Amount,
                closed?0:a.RemainingAmount.Amount,live?.CampaignVerifiedViews??0,live?.RewardedViewCount??0,f.ViewEarnings.Amount,f.SaleCommissionEarnings.Amount,
                status,contentStatus,live?.Provider,live?.ExternalContentId,p.StartDateUtc,p.EndDateUtc,p.PromotionLiveDurationDays,
                submission?.RevisionNumber,submission?.ReviewStatus.ToString(),submission?.Feedback,submission?.SubmittedAtUtc,
                live?.WentLiveAtUtc,live is null ? null : live.ExpiresAtUtc(p.PromotionLiveDurationDays),remaining,
                p.Description,p.Eligibility.Requirements,p.Location,a.ContentDueAtUtc??p.ContentDueAtUtc,
                a.Platform?.ToString(),socialProfile?.ProfileUrl,
                submission?.ReviewMediaAssetId is null?null:$"/api/review-media/promotions/{submission.Id}",
                publication is null?null:CreatorPublicationService.View(publication, Now),a.CreatorSocialProfileId));
        }
        return result;
    }
    public async Task<EarningsWorkspace> EarningsAsync(Actor actor,CancellationToken ct)
    {
        await DemandCreator(actor,ct);
        var configuration=await new FinancialConfigurationResolver(db).FindEffectiveAsync(Now,ct);
        var threshold=configuration?.CreatorPayoutThreshold;
        var available=await CreatorEarnedBalance.AvailableAsync(db, actor.CreatorId!.Value, ct);
        var entries=await db.CreatorEarningEntries.AsNoTracking().Where(x=>x.CreatorId==actor.CreatorId).OrderByDescending(x=>x.CreatedAtUtc).ToListAsync(ct);
        var corrections=await db.UgcPaymentCorrections.AsNoTracking().Where(x=>x.CreatorId==actor.CreatorId).ToListAsync(ct);
        var correctedIds=corrections.Select(x=>x.OriginalEarningEntryId).ToHashSet();
        var earnedUgcAssignments=await db.CreatorPublicationVerifications.AsNoTracking().Where(x=>x.CreatorId==actor.CreatorId
            && x.UgcAssignmentId!=null && x.WentLiveAtUtc!=null).Select(x=>x.UgcAssignmentId!.Value)
            .Concat(db.UgcAssignments.AsNoTracking().Where(a => a.CreatorId == actor.CreatorId && a.Status == UgcAssignmentStatus.Approved
                && a.CompletedAtUtc != null && db.UgcOpportunities.Any(o => o.Id == a.UgcOpportunityId && o.DeliverableType == UgcDeliverableType.DirectVideoDelivery)
                && db.CreatorEarningEntries.Any(e => e.UgcAssignmentId == a.Id && e.Source == EarningSource.Ugc
                    && !db.UgcPaymentCorrections.Any(c => c.OriginalEarningEntryId == e.Id))).Select(a => a.Id))
            .Distinct().ToListAsync(ct);
        var pending=(await db.UgcAssignments.AsNoTracking().Where(x=>x.CreatorId==actor.CreatorId && x.Status!=UgcAssignmentStatus.Rejected
            && !earnedUgcAssignments.Contains(x.Id)).Select(x=>x.CreatorPayment).ToListAsync(ct)).Sum(x=>x.Amount);
        var promotionIds=entries.Where(x=>x.PromotionId is not null).Select(x=>x.PromotionId!.Value).Distinct().ToArray();
        var promotions=await db.Promotions.AsNoTracking().Where(x=>promotionIds.Contains(x.Id))
            .ToDictionaryAsync(x=>x.Id,x=>new{x.Title,x.BusinessId,x.PromotionType},ct);
        var assignmentIds=entries.Where(x=>x.UgcAssignmentId is not null).Select(x=>x.UgcAssignmentId!.Value).Distinct().ToArray();
        var ugcAssignments=await db.UgcAssignments.AsNoTracking().Where(x=>assignmentIds.Contains(x.Id))
            .ToDictionaryAsync(x=>x.Id,x=>x.UgcOpportunityId,ct);
        var ugcIds=ugcAssignments.Values.Distinct().ToArray();
        var ugc=await db.UgcOpportunities.AsNoTracking().Where(x=>ugcIds.Contains(x.Id))
            .ToDictionaryAsync(x=>x.Id,x=>new{x.Title,x.BusinessId},ct);
        var businessIds=promotions.Values.Select(x=>x.BusinessId).Concat(ugc.Values.Select(x=>x.BusinessId)).Distinct().ToArray();
        var businessNames=await db.PublicWorkspaceProfiles.AsNoTracking().Where(x=>x.Role==ActorRole.Business&&businessIds.Contains(x.SubjectId))
            .ToDictionaryAsync(x=>x.SubjectId,x=>x.DisplayName,ct);
        var saleUgcIds = await db.UgcCustomerOffers.AsNoTracking().Where(x => ugcIds.Contains(x.UgcOpportunityId)).Select(x => x.UgcOpportunityId).ToListAsync(ct);
        var history=new List<EarningItem>();
        foreach(var e in entries)
        {
            var promotion=e.PromotionId is { } promotionId && promotions.TryGetValue(promotionId,out var p)?p:null;
            var ugcSource=e.UgcAssignmentId is { } assignmentId && ugcAssignments.TryGetValue(assignmentId,out var ugcId)
                && ugc.TryGetValue(ugcId,out var u)?u:null;
            var businessId=promotion?.BusinessId ?? ugcSource?.BusinessId;
            history.Add(new(e.Id,promotion?.Title??ugcSource?.Title??"Account adjustment",
                e.Source switch{EarningSource.ViewReward=>"View Earnings",EarningSource.SaleCommission=>"Sale Earnings",EarningSource.Ugc=>"UGC Earnings",_=>"Authorized adjustment"},
                e.Amount.Amount,e.CreatedAtUtc,businessId is { } id?businessNames.GetValueOrDefault(id):null,
                e.Source==EarningSource.Ugc?"UGC":promotion?.PromotionType==PromotionType.ViewOnly?"VIEW_ONLY":"VIEW_PLUS_SALE",
                correctedIds.Contains(e.Id)?"Reversed":e.Source==EarningSource.Ugc && !earnedUgcAssignments.Contains(e.UgcAssignmentId!.Value)?"Pending correction":"Earned",
                e.Source == EarningSource.Ugc ? e.UgcAssignmentId is { } work && ugcAssignments.TryGetValue(work,out var opportunityId) && saleUgcIds.Contains(opportunityId) ? "UGC + Sale" : "UGC Only" : promotion?.PromotionType == PromotionType.ViewOnly ? "View Only" : "View + Sale"));
        }
        foreach(var correction in corrections)
            history.Add(new(correction.Id,"UGC payment correction","Returned to campaign reserve",-correction.CreatorAmount.Amount,
                correction.CreatedAtUtc,SourceType:"UGC",Status:"Correction"));
        history=history.OrderByDescending(x=>x.AtUtc).ToList();
        var paid=(await db.PayoutRecords.AsNoTracking().Where(x=>x.CreatorId==actor.CreatorId).OrderByDescending(x=>x.EligibleAtUtc).ToListAsync(ct))
            .Select(x=>new PayoutItem(x.Id,"Creator","You",x.Amount.Amount,x.ThresholdUsed.Amount,x.Status.ToString(),x.EligibleAtUtc,x.PaidAtUtc,x.Reference)).ToArray();
        return new(available.Amount,threshold?.Amount,threshold is { } minimum ? Math.Max(0,minimum.Amount-available.Amount) : null,configuration is null ? 0 : await PayoutEligibleBalance.AmountAsync(db,configuration,PayoutBeneficiary.Creator,actor.CreatorId!.Value,available.Amount,Now,ct),history,paid,
            entries.Where(x=>x.Source==EarningSource.ViewReward).Sum(x=>x.Amount.Amount),entries.Where(x=>x.Source==EarningSource.SaleCommission).Sum(x=>x.Amount.Amount),
            entries.Where(x=>x.Source==EarningSource.Ugc && !correctedIds.Contains(x.Id) && earnedUgcAssignments.Contains(x.UgcAssignmentId!.Value)).Sum(x=>x.Amount.Amount),
            pending,paid.Where(x=>x.Status=="Paid").Sum(x=>x.Amount),configuration?.MonthlyPayoutEnabled==true,configuration?.UgcPayoutMinimumExempt==true);
    }
}
