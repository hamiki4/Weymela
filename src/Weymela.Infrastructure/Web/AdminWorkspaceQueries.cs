using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Web;

public sealed partial class WorkspaceQueries
{
    public async Task<AdminHome> AdminHomeAsync(Actor actor,CancellationToken ct)
    {
        DemandAdmin(actor);var platform=await new PlatformRevenueRepository(db).SummaryAsync(ct);
        return new(await db.BusinessWallets.CountAsync(ct),await db.CommercePermissions.Where(x=>x.Role==ActorRole.Creator).Select(x=>x.SubjectId).Distinct().CountAsync(ct),
            await db.Promotions.CountAsync(x=>x.Status==PromotionStatus.Active,ct),(await db.Promotions.Select(x=>x.UsedBudget).ToListAsync(ct)).Sum(x=>x.Amount),
            (await db.CreatorEarningEntries.Select(x=>x.Amount).ToListAsync(ct)).Sum(x=>x.Amount),(await db.CustomerCashbackEntries.Select(x=>x.Amount).ToListAsync(ct)).Sum(x=>x.Amount),
            platform.Accrued.Amount,await History(null,ct));
    }
    public async Task<IReadOnlyList<CampaignRow>> AdminCampaignsAsync(Actor actor,CancellationToken ct)
    {
        DemandAdmin(actor);var campaigns=await db.Promotions.AsNoTracking().Include(x=>x.Allocations).OrderByDescending(x=>x.CreatedAtUtc).ToListAsync(ct);
        var result=new List<CampaignRow>();foreach(var p in campaigns)result.Add(await Row(p,ct));return result;
    }
    public async Task<AdminCampaign> AdminCampaignAsync(Actor actor,Guid id,CancellationToken ct)
    {
        DemandAdmin(actor);var p=await Campaign(id,ct);var details=await Financial.CampaignAsync(actor,id,ct);var creators=new List<AdminCreatorRow>();
        foreach(var a in details.Creators)creators.Add(new(await directory.CreatorCardAsync(a.CreatorId,ct),a.StartingBudget.Amount,a.Used.Amount,
            a.ParticipationStatus is "Completed" or "Cancelled"?0:a.Remaining.Amount,a.BaselineViews,a.LatestVerifiedViews,a.CampaignVerifiedViews,a.RewardedViews,
            a.ViewEarnings.Amount,a.VerifiedSales,a.SaleCommission.Amount,a.CustomerCashback.Amount,a.PlatformRevenue.Amount,a.ParticipationStatus));
        return new(await Row(p,ct),creators,creators.Sum(x=>x.ViewEarnings+x.SaleCommission),creators.Sum(x=>x.CustomerCashback),creators.Sum(x=>x.PlatformRevenue),await History(id,ct));
    }
    public async Task<IReadOnlyList<BusinessOversight>> BusinessesAsync(Actor actor,CancellationToken ct)
    {
        DemandAdmin(actor);var wallets=await db.BusinessWallets.AsNoTracking().ToListAsync(ct);var result=new List<BusinessOversight>();
        foreach(var w in wallets)result.Add(new(await directory.BusinessCardAsync(w.BusinessId,ct),
            await db.CommercePermissions.AnyAsync(x=>x.SubjectId==w.BusinessId&&x.Role==ActorRole.Business&&x.IsActive,ct)?"Active":"Inactive",
            w.TotalBalance.Amount,w.AvailableBalance.Amount,w.ReservedBalance.Amount,await db.Promotions.CountAsync(x=>x.BusinessId==w.BusinessId&&x.Status==PromotionStatus.Active,ct),
            await db.WalletEntries.Where(x=>x.BusinessId==w.BusinessId&&x.Movement=="Deposit").OrderByDescending(x=>x.CreatedAtUtc).Select(x=>(DateTime?)x.CreatedAtUtc).FirstOrDefaultAsync(ct)));
        return result;
    }
    public async Task<IReadOnlyList<CreatorOversight>> CreatorsAsync(Actor actor,CancellationToken ct)
    {
        DemandAdmin(actor);var threshold=(await new FinancialConfigurationResolver(db).EffectiveAsync(Now,ct)).CreatorPayoutThreshold.Amount;
        var permissions=await db.CommercePermissions.AsNoTracking().Where(x=>x.Role==ActorRole.Creator).ToListAsync(ct);var result=new List<CreatorOversight>();
        foreach(var group in permissions.GroupBy(x=>x.SubjectId))
        {
            var available=(await db.CreatorEarningsAccounts.SingleOrDefaultAsync(x=>x.CreatorId==group.Key,ct))?.AvailableEarnings.Amount??0;
            result.Add(new(await directory.CreatorCardAsync(group.Key,ct),group.Any(x=>x.IsActive)?"Active":"Inactive",
                await db.CreatorAllocations.CountAsync(x=>x.CreatorId==group.Key&&x.Status==CreatorAllocationStatus.Active,ct),available,available>=threshold));
        }
        return result;
    }
    public async Task<FinancialSettingsWorkspace> SettingsAsync(Actor actor,CancellationToken ct)
    {
        DemandAdmin(actor);var v=await new FinancialConfigurationResolver(db).EffectiveAsync(Now,ct);
        ViewPriceInput Input(PricingSnapshot p)=>new(p.ViewsPerReward,p.BusinessCharge.Amount,p.CreatorEarning.Amount,p.PlatformEarning.Amount,p.MinimumPromotionBudget?.Amount);
        var saved=await db.FinancialConfigurationVersions.AsNoTracking().OrderByDescending(x=>x.Version).ToListAsync(ct);
        FinancialSettingsInput Settings(FinancialConfigurationVersion x)=>new(Input(x.ViewOnly),Input(x.ViewPlusCommission),x.ViewPlusCommission.CreatorCommissionPercent,x.ViewPlusCommission.CustomerCashbackPercent,x.ViewPlusCommission.PlatformPercent,x.CreatorPayoutThreshold.Amount,x.CustomerPayoutThreshold.Amount,x.EffectiveFromUtc);
        var versions=saved.Select(x=>new FinancialVersionInfo(x.Id,x.Version,x.EffectiveFromUtc,x.ChangedBy,Settings(x))).ToArray();
        return new(new(Input(v.ViewOnly),Input(v.ViewPlusCommission),v.ViewPlusCommission.CreatorCommissionPercent,v.ViewPlusCommission.CustomerCashbackPercent,
            v.ViewPlusCommission.PlatformPercent,v.CreatorPayoutThreshold.Amount,v.CustomerPayoutThreshold.Amount,v.EffectiveFromUtc),v.Version,versions);
    }
    public async Task<PayoutWorkspace> PayoutsAsync(Actor actor,CancellationToken ct)
    {
        DemandAdmin(actor);var version=await new FinancialConfigurationResolver(db).EffectiveAsync(Now,ct);
        var records=await db.PayoutRecords.AsNoTracking().ToListAsync(ct);var creators=new List<PayoutQueueRow>();var customers=new List<PayoutQueueRow>();
        var history=new List<PayoutItem>();
        foreach(var a in await db.CreatorEarningsAccounts.AsNoTracking().ToListAsync(ct))
        {
            var pending=records.SingleOrDefault(x=>x.CreatorId==a.CreatorId&&x.Status==PayoutStatus.Eligible);
            if(a.AvailableEarnings.Amount<version.CreatorPayoutThreshold.Amount&&pending is null)continue;
            var name=(await directory.CreatorCardAsync(a.CreatorId,ct)).DisplayName;
            var since=await EligibleSince(PayoutBeneficiary.Creator,a.CreatorId,version.CreatorPayoutThreshold.Amount,version.EffectiveFromUtc,ct);
            creators.Add(new(a.CreatorId,pending?.Id,name,a.AvailableEarnings.Amount,pending?.ThresholdUsed.Amount??version.CreatorPayoutThreshold.Amount,
                pending?.Amount.Amount??version.CreatorPayoutThreshold.Amount,pending?.EligibleAtUtc??since,pending is null?"Eligible":"Ready"));
        }
        foreach(var a in await db.CustomerCashbackAccounts.AsNoTracking().ToListAsync(ct))
        {
            var pending=records.SingleOrDefault(x=>x.CustomerId==a.CustomerId&&x.Status==PayoutStatus.Eligible);
            if(a.AvailableCashback.Amount<version.CustomerPayoutThreshold.Amount&&pending is null)continue;
            customers.Add(new(a.CustomerId,pending?.Id,await CustomerLabel(a.CustomerId,ct),a.AvailableCashback.Amount,pending?.ThresholdUsed.Amount??version.CustomerPayoutThreshold.Amount,
                pending?.Amount.Amount??version.CustomerPayoutThreshold.Amount,pending?.EligibleAtUtc??await EligibleSince(PayoutBeneficiary.Customer,a.CustomerId,version.CustomerPayoutThreshold.Amount,version.EffectiveFromUtc,ct),pending is null?"Eligible":"Ready"));
        }
        foreach(var p in records.OrderByDescending(x=>x.PaidAtUtc??x.EligibleAtUtc))history.Add(new(p.Id,p.Beneficiary.ToString(),p.CreatorId is {} creator?(await directory.CreatorCardAsync(creator,ct)).DisplayName:await CustomerLabel(p.CustomerId!.Value,ct),p.Amount.Amount,p.ThresholdUsed.Amount,p.Status.ToString(),p.EligibleAtUtc,p.PaidAtUtc,p.Reference));
        var platform=await Financial.PlatformAsync(actor,ct);
        history.AddRange(platform.History.Select(x=>new PayoutItem(x.Id,"Platform","Weymela",x.Amount.Amount,0,"Paid",x.SettledAtUtc,x.SettledAtUtc,x.Reference)));
        return new(creators,customers,platform.Accrued.Amount,platform.Settled.Amount,platform.Unsettled.Amount,history.OrderByDescending(x=>x.PaidAtUtc??x.EligibleAtUtc).ToArray());
    }
    private async Task<DateTime?> EligibleSince(PayoutBeneficiary kind,Guid id,decimal threshold,DateTime effective,CancellationToken ct)
    {
        var credits=kind==PayoutBeneficiary.Creator
            ?(await db.CreatorEarningEntries.Where(x=>x.CreatorId==id).ToListAsync(ct)).Select(x=>(At:x.CreatedAtUtc,Amount:x.Amount.Amount)).ToList()
            :(await db.CustomerCashbackEntries.Where(x=>x.CustomerId==id).ToListAsync(ct)).Select(x=>(At:x.CreatedAtUtc,Amount:x.Amount.Amount)).ToList();
        credits.AddRange((await db.PayoutRecords.Where(x=>(x.CreatorId==id||x.CustomerId==id)&&x.Status==PayoutStatus.Paid).ToListAsync(ct)).Select(x=>(x.PaidAtUtc!.Value,-x.Amount.Amount)));
        decimal balance=0;DateTime? since=null;
        foreach(var m in credits.OrderBy(x=>x.At)) { var before=balance;balance+=m.Amount;if(balance<threshold)since=null;else if(before<threshold)since=m.At>effective?m.At:effective; }
        return since;
    }
    public Task<IReadOnlyList<ActivityItem>> AuditAsync(Actor actor,CancellationToken ct) { DemandAdmin(actor);return History(null,ct); }
    public async Task<IReadOnlyList<ActivityItem>> NotificationsAsync(Actor actor,CancellationToken ct)
    {
        DemandAdmin(actor);
        return (await db.OutboxMessages.AsNoTracking().Where(x=>!x.EventType.EndsWith("Audit")).OrderByDescending(x=>x.OccurredAtUtc).Take(100).ToListAsync(ct))
            .Select(x=>new ActivityItem(x.Id,EventLabel(x.EventType),x.OccurredAtUtc,x.Id.ToString())).ToArray();
    }
    private async Task<string> CustomerLabel(Guid id,CancellationToken ct)
    {var customer=await directory.CustomerCardAsync(id,ct);return customer.DisplayName+" · "+customer.PublicId;}
}
