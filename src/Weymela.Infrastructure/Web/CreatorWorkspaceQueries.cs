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
            result.Add(new(r.Id,p.Id,p.Title,business.DisplayName,p.PromotionType.ToString(),r.Status.ToString(),r.AppliedAtUtc));
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
        var campaigns=await db.Promotions.AsNoTracking().Include(x=>x.Allocations).Where(x=>(x.Status==PromotionStatus.Published||x.Status==PromotionStatus.Active)&&x.EndDateUtc>Now).ToListAsync(ct);
        var result=new List<CampaignOpportunity>();
        foreach(var p in campaigns)
        {
            if(p.ReservedBudget.Amount<=0||!new CreatorEligibility().IsEligible(actor.CreatorId.Value,p.Eligibility,profile))continue;
            if(!await db.CommercePermissions.AnyAsync(x=>x.Role==ActorRole.Business&&x.SubjectId==p.BusinessId&&x.IsActive,ct))continue;
            var request=await db.CreatorApplications.AsNoTracking().Where(x=>x.PromotionId==p.Id&&x.CreatorId==actor.CreatorId).OrderByDescending(x=>x.AppliedAtUtc).FirstOrDefaultAsync(ct);
            if(p.UnallocatedBudget.Amount<=0&&request is null)continue;
            result.Add(new(p.Id,p.PublicPromotionId,await directory.BusinessCardAsync(p.BusinessId,ct),p.Title,p.Description,p.PromotionType.ToString(),
                p.Eligibility.Requirements,p.Eligibility.Category,p.Eligibility.Market,p.Eligibility.MinimumVerifiedFollowers,p.StartDateUtc,p.EndDateUtc,
                CreatorPrice(p.PricingSnapshot),request?.Status.ToString(),"Your verified profile meets this Campaign's requirements."));
        }
        return result;
    }
    public async Task<CampaignOpportunity> OpportunityAsync(Actor actor,Guid id,CancellationToken ct) =>
        (await DiscoverAsync(actor,ct)).SingleOrDefault(x=>x.Id==id)??throw new ApplicationFailure(FailureKind.NotFound,"This Campaign is not available for you to join.");
    public async Task<IReadOnlyList<CreatorCampaignCard>> CreatorCampaignsAsync(Actor actor,CancellationToken ct)
    {
        await DemandCreator(actor,ct);var allocations=await db.CreatorAllocations.AsNoTracking().Where(x=>x.CreatorId==actor.CreatorId).ToListAsync(ct);
        var result=new List<CreatorCampaignCard>();
        foreach(var a in allocations)
        {
            var p=await Campaign(a.PromotionId,ct);var f=await Financial.CreatorAsync(actor,a.Id,ct);
            var live=await db.CreatorPromotionParticipations.AsNoTracking().SingleOrDefaultAsync(x=>x.CreatorAllocationId==a.Id,ct);
            var closed=a.Status is CreatorAllocationStatus.Completed or CreatorAllocationStatus.Cancelled;
            result.Add(new(p.Id,a.Id,live?.Id,p.Title,await directory.BusinessCardAsync(p.BusinessId,ct),p.PromotionType.ToString(),a.OriginalAllocation.Amount,
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
            history.Add(new(e.Id,e.PromotionId is null?"Account adjustment":await db.Promotions.Where(x=>x.Id==e.PromotionId).Select(x=>x.Title).SingleAsync(ct),
                e.Source switch{EarningSource.ViewReward=>"View Reward",EarningSource.SaleCommission=>"Sale Commission",_=>"Authorized adjustment"},e.Amount.Amount,e.CreatedAtUtc));
        var paid=(await db.PayoutRecords.AsNoTracking().Where(x=>x.CreatorId==actor.CreatorId).OrderByDescending(x=>x.EligibleAtUtc).ToListAsync(ct))
            .Select(x=>new PayoutItem(x.Id,"Creator","You",x.Amount.Amount,x.ThresholdUsed.Amount,x.Status.ToString(),x.EligibleAtUtc,x.PaidAtUtc,x.Reference)).ToArray();
        return new(eligibility.Available.Amount,eligibility.Threshold.Amount,Math.Max(0,eligibility.Threshold.Amount-eligibility.Available.Amount),eligibility.EligibleAmount.Amount,history,paid);
    }
}
