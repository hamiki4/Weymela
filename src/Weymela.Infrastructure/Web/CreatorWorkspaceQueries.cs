using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence.Repositories;

namespace Weymela.Infrastructure.Web;

public sealed partial class WorkspaceQueries
{
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
        return new(await directory.CreatorCardAsync(actor.CreatorId!.Value,ct),
            await db.CreatorApplications.CountAsync(x=>x.CreatorId==actor.CreatorId&&x.Status==CreatorApplicationStatus.Pending,ct),
            await db.CreatorAllocations.CountAsync(x=>x.CreatorId==actor.CreatorId&&x.Status==CreatorAllocationStatus.Active,ct),await EarningsAsync(actor,ct));
    }
    public async Task<CreatorPricing> CreatorPricingAsync(Actor actor,CancellationToken ct)
    {
        await DemandCreator(actor,ct);var v=await new FinancialConfigurationResolver(db).EffectiveAsync(Now,ct);
        return new([CreatorPrice(v.ViewOnly),CreatorPrice(v.ViewPlusCommission)],v.CreatorPayoutThreshold.Amount,v.EffectiveFromUtc);
    }
    public async Task<IReadOnlyList<CampaignOpportunity>> DiscoverAsync(Actor actor,CancellationToken ct)
    {
        await DemandCreator(actor,ct);var creator=await directory.CreatorCardAsync(actor.CreatorId!.Value,ct);
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
            if(p.UnallocatedBudget.Amount<=0&&request is null)continue;
            var eligibleSocials=socials.Where(s=>p.Platforms.Any(slot=>slot.Platform==s.Platform&&slot.Available>0)).Select(s=>new CreatorSocialProfileView(s.Id,s.Platform.ToString(),s.ProfileUrl,s.SelfReportedAudience,s.VerificationStatus,s.VerifiedAudience)).ToArray();
            if(p.Platforms.Count>0&&eligibleSocials.Length==0&&request is null)continue;
            result.Add(new(p.Id,p.PublicPromotionId,businessCards[p.BusinessId],p.Title,p.Description,PromotionTypeLabel(p.PromotionType),
                p.Eligibility.Requirements,p.Eligibility.Category,p.Eligibility.Market,p.Eligibility.MinimumVerifiedFollowers,p.StartDateUtc,p.EndDateUtc,
                CreatorPrice(p.PricingSnapshot),request?.Status.ToString(),"Your Creator profile meets this Promotion's requirements.",p.Slogan,p.Location,
                p.Platforms.Select(x=>new PromotionPlatformView(x.Platform.ToString(),x.ApprovedCount,x.Capacity,x.Available)).ToArray(),eligibleSocials,
                p.TotalBudget.Amount,p.Platforms.Sum(x=>x.ApprovedCount),p.Platforms.Sum(x=>x.Capacity)));
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
            var closed=a.Status is CreatorAllocationStatus.Completed or CreatorAllocationStatus.Cancelled;
            result.Add(new(p.Id,a.Id,live?.Id,p.Title,await directory.BusinessCardAsync(p.BusinessId,ct),PromotionTypeLabel(p.PromotionType),a.OriginalAllocation.Amount,
                closed?0:a.RemainingAmount.Amount,live?.CampaignVerifiedViews??0,live?.RewardedViewCount??0,f.ViewEarnings.Amount,f.SaleCommissionEarnings.Amount,
                closed?a.Status.ToString():live?.Status.ToString()??"AwaitingContent",live is null?"Ready for your content":"Content connected",live?.Provider,live?.ExternalContentId,p.StartDateUtc,p.EndDateUtc));
        }
        return result;
    }
    public async Task<EarningsWorkspace> EarningsAsync(Actor actor,CancellationToken ct)
    {
        await DemandCreator(actor,ct);var eligibility=await new PayoutService(db,clock).EligibilityAsync(actor,PayoutBeneficiary.Creator,actor.CreatorId!.Value,ct);
        var entries=await db.CreatorEarningEntries.AsNoTracking().Where(x=>x.CreatorId==actor.CreatorId).OrderByDescending(x=>x.CreatedAtUtc).ToListAsync(ct);
        var history=new List<EarningItem>();foreach(var e in entries)
            history.Add(new(e.Id,e.PromotionId is null?(e.Source==EarningSource.Ugc?"UGC":"Account adjustment"):await db.Promotions.Where(x=>x.Id==e.PromotionId).Select(x=>x.Title).SingleAsync(ct),
                e.Source switch{EarningSource.ViewReward=>"View Earnings",EarningSource.SaleCommission=>"Sale Earnings",EarningSource.Ugc=>"UGC Earnings",_=>"Authorized adjustment"},e.Amount.Amount,e.CreatedAtUtc));
        var paid=(await db.PayoutRecords.AsNoTracking().Where(x=>x.CreatorId==actor.CreatorId).OrderByDescending(x=>x.EligibleAtUtc).ToListAsync(ct))
            .Select(x=>new PayoutItem(x.Id,"Creator","You",x.Amount.Amount,x.ThresholdUsed.Amount,x.Status.ToString(),x.EligibleAtUtc,x.PaidAtUtc,x.Reference)).ToArray();
        return new(eligibility.Available.Amount,eligibility.Threshold.Amount,Math.Max(0,eligibility.Threshold.Amount-eligibility.Available.Amount),eligibility.EligibleAmount.Amount,history,paid,
            entries.Where(x=>x.Source==EarningSource.ViewReward).Sum(x=>x.Amount.Amount),entries.Where(x=>x.Source==EarningSource.SaleCommission).Sum(x=>x.Amount.Amount),entries.Where(x=>x.Source==EarningSource.Ugc).Sum(x=>x.Amount.Amount));
    }
}
