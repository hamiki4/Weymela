using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Repositories;

namespace Weymela.Infrastructure.Web;

public sealed partial class WorkspaceQueries
{
    public async Task<WalletWorkspace> WalletAsync(Actor actor,CancellationToken ct)
    {
        await DemandBusiness(actor,ct);
        var wallet=await db.BusinessWallets.AsNoTracking().SingleAsync(x=>x.BusinessId==actor.BusinessId,ct);
        var history=(await db.WalletEntries.AsNoTracking().Where(x=>x.BusinessId==actor.BusinessId).OrderByDescending(x=>x.CreatedAtUtc).Take(100).ToListAsync(ct))
            .Select(x=>new WalletMovement(x.Id,x.Movement switch {"Deposit"=>"Funds added","Reserve"=>"Campaign funded","Consumed"=>"Campaign activity",_=>"Funds movement"},x.Amount.Amount,x.CreatedAtUtc,x.JournalId.ToString())).ToArray();
        return new(wallet.TotalBalance.Amount,wallet.AvailableBalance.Amount,wallet.ReservedBalance.Amount,wallet.Version,history);
    }
    public async Task<BusinessHome> BusinessHomeAsync(Actor actor,CancellationToken ct)
    {
        var wallet=await WalletAsync(actor,ct);
        var ids=await db.Promotions.Where(x=>x.BusinessId==actor.BusinessId).Select(x=>x.Id).ToListAsync(ct);
        return new(await directory.BusinessCardAsync(actor.BusinessId!.Value,ct),wallet,
            await db.Promotions.CountAsync(x=>x.BusinessId==actor.BusinessId && x.Status==PromotionStatus.Active,ct),
            await db.CreatorApplications.CountAsync(x=>ids.Contains(x.PromotionId)&&x.Status==CreatorApplicationStatus.Pending,ct),
            await db.VerifiedSales.CountAsync(x=>x.BusinessId==actor.BusinessId,ct));
    }
    public async Task<BusinessPricing> BusinessPricingAsync(Actor actor,CancellationToken ct)
    {
        await DemandBusiness(actor,ct);var v=await new FinancialConfigurationResolver(db).EffectiveAsync(Now,ct);
        return new([BusinessPrice(v.ViewOnly),BusinessPrice(v.ViewPlusCommission)],v.EffectiveFromUtc);
    }
    public async Task<IReadOnlyList<CampaignRow>> BusinessCampaignsAsync(Actor actor,CancellationToken ct)
    {
        await DemandBusiness(actor,ct);var list=await db.Promotions.AsNoTracking().Include(x=>x.Allocations).Where(x=>x.BusinessId==actor.BusinessId).OrderByDescending(x=>x.CreatedAtUtc).ToListAsync(ct);
        var result=new List<CampaignRow>();foreach(var p in list) result.Add(await Row(p,ct));return result;
    }
    public async Task<BusinessCampaign> BusinessCampaignAsync(Actor actor,Guid id,CancellationToken ct)
    {
        await DemandBusiness(actor,ct);var p=await Campaign(id,ct);
        if(p.BusinessId!=actor.BusinessId)throw new ApplicationFailure(FailureKind.Forbidden,"This Campaign belongs to another Business.");
        var applications=await db.CreatorApplications.AsNoTracking().Where(x=>x.PromotionId==id).OrderBy(x=>x.AppliedAtUtc).ToListAsync(ct);
        var applicants=new List<ApplicantCard>();foreach(var a in applications)applicants.Add(new(a.Id,await directory.CreatorCardAsync(a.CreatorId,ct),a.Message,a.ContentConcept,a.Status.ToString(),a.AppliedAtUtc));
        var creators=new List<CreatorBudgetCard>();
        foreach(var a in p.Allocations)
        {
            var live=await db.CreatorPromotionParticipations.AsNoTracking().SingleOrDefaultAsync(x=>x.CreatorAllocationId==a.Id,ct);
            var closed=a.Status is CreatorAllocationStatus.Completed or CreatorAllocationStatus.Cancelled;
            creators.Add(new(a.Id,await directory.CreatorCardAsync(a.CreatorId,ct),a.OriginalAllocation.Amount,a.UsedAmount.Amount,closed?0:a.RemainingAmount.Amount,
                live?.CampaignVerifiedViews??0,await db.VerifiedSales.CountAsync(x=>x.CreatorAllocationId==a.Id,ct),
                closed?a.Status.ToString():live?.Status.ToString()??"AwaitingContent",a.Version,
                !closed && p.Status is PromotionStatus.Funded or PromotionStatus.Published or PromotionStatus.Active && p.UnallocatedBudget.Amount>0));
        }
        return new(await Row(p,ct),p.Description,p.Eligibility.Requirements,p.Eligibility.Category,p.Eligibility.Market,p.Eligibility.MinimumVerifiedFollowers,
            BusinessPrice(p.PricingSnapshot),applicants,creators,await History(p.Id,ct));
    }
}
