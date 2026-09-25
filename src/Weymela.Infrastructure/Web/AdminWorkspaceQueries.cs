using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Web;

public sealed partial class WorkspaceQueries
{
    public async Task<OperationsHome> OperationsHomeAsync(Actor actor, CancellationToken ct)
    {
        DemandCapability(actor, AdministrativeCapability.OperationsWorkspace);
        var creatorPayouts = await db.PayoutRecords.AsNoTracking().CountAsync(x => x.Beneficiary == PayoutBeneficiary.Creator && x.Status == PayoutStatus.Eligible, ct);
        var customerPayouts = await db.PayoutRecords.AsNoTracking().CountAsync(x => x.Beneficiary == PayoutBeneficiary.Customer && x.Status == PayoutStatus.Eligible, ct);
        return new(
            await db.RoleEnrollments.CountAsync(x => x.Status == RoleEnrollmentStatus.Pending, ct),
            await db.CommercePermissions.Where(x => x.Role == ActorRole.Business && x.IsActive).Select(x => x.SubjectId).Distinct().CountAsync(ct),
            await db.CommercePermissions.Where(x => x.Role == ActorRole.Creator && x.IsActive).Select(x => x.SubjectId).Distinct().CountAsync(ct),
            await db.CommercePermissions.Where(x => x.Role == ActorRole.Customer && x.IsActive).Select(x => x.SubjectId).Distinct().CountAsync(ct),
            await db.Promotions.CountAsync(x => x.Status == PromotionStatus.Active, ct), creatorPayouts, customerPayouts);
    }

    public async Task<IReadOnlyList<OperationsBusinessView>> OperationsBusinessesAsync(Actor actor, CancellationToken ct)
    {
        DemandCapability(actor, AdministrativeCapability.BusinessOperationalVisibility);
        var wallets = await db.BusinessWallets.AsNoTracking().OrderBy(x => x.BusinessId).ToListAsync(ct);
        var result = new List<OperationsBusinessView>();
        foreach (var wallet in wallets)
        {
            result.Add(new(await directory.BusinessCardAsync(wallet.BusinessId, ct),
                await db.CommercePermissions.AnyAsync(x => x.SubjectId == wallet.BusinessId && x.Role == ActorRole.Business && x.IsActive, ct) ? "Active" : "Inactive",
                await db.Promotions.CountAsync(x => x.BusinessId == wallet.BusinessId && x.Status == PromotionStatus.Active, ct),
                await db.WalletEntries.Where(x => x.BusinessId == wallet.BusinessId && x.Movement == "Deposit")
                    .OrderByDescending(x => x.CreatedAtUtc).Select(x => (DateTime?)x.CreatedAtUtc).FirstOrDefaultAsync(ct)));
        }
        return result;
    }

    public async Task<IReadOnlyList<OperationsCreatorView>> OperationsCreatorsAsync(Actor actor, CancellationToken ct)
    {
        DemandCapability(actor, AdministrativeCapability.CreatorOperationalVisibility);
        var threshold = (await new FinancialConfigurationResolver(db).EffectiveAsync(Now, ct)).CreatorPayoutThreshold.Amount;
        var permissions = await db.CommercePermissions.AsNoTracking().Where(x => x.Role == ActorRole.Creator).ToListAsync(ct);
        var result = new List<OperationsCreatorView>();
        foreach (var group in permissions.GroupBy(x => x.SubjectId))
        {
            var available = (await db.CreatorEarningsAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.CreatorId == group.Key, ct))?.AvailableEarnings.Amount ?? 0;
            result.Add(new(await directory.CreatorCardAsync(group.Key, ct), group.Any(x => x.IsActive) ? "Active" : "Inactive",
                await db.CreatorAllocations.CountAsync(x => x.CreatorId == group.Key && x.Status == CreatorAllocationStatus.Active, ct), available >= threshold));
        }
        return result;
    }

    public async Task<IReadOnlyList<OperationsCustomerView>> OperationsCustomersAsync(Actor actor, CancellationToken ct)
    {
        DemandCapability(actor, AdministrativeCapability.CustomerOperationalVisibility);
        var permissions = await db.CommercePermissions.AsNoTracking().Where(x => x.Role == ActorRole.Customer).ToListAsync(ct);
        var result = new List<OperationsCustomerView>();
        foreach (var group in permissions.GroupBy(x => x.SubjectId))
            result.Add(new(await directory.CustomerCardAsync(group.Key, ct), group.Any(x => x.IsActive) ? "Active" : "Inactive"));
        return result;
    }

    public async Task<IReadOnlyList<OperationsCampaignView>> OperationsCampaignsAsync(Actor actor, CancellationToken ct)
    {
        DemandCapability(actor, AdministrativeCapability.CampaignOperationalVisibility);
        var campaigns = await db.Promotions.AsNoTracking().Include(x => x.Allocations)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var result = new List<OperationsCampaignView>();
        foreach (var campaign in campaigns)
            result.Add(new(campaign.Id, campaign.PublicPromotionId, campaign.BusinessId,
                (await directory.BusinessCardAsync(campaign.BusinessId, ct)).DisplayName, campaign.Title,
                PromotionTypeLabel(campaign.PromotionType), campaign.Allocations.Count, campaign.StartDateUtc,
                campaign.EndDateUtc, PromotionStatusLabel(campaign.Status), campaign.Version,
                campaign.PromotionLiveDurationDays, campaign.Slogan, campaign.Location));
        return result;
    }

    public async Task<OperationsCampaignView> OperationsCampaignAsync(Actor actor, Guid id, CancellationToken ct)
    {
        DemandCapability(actor, AdministrativeCapability.CampaignOperationalVisibility);
        var campaign = await Campaign(id, ct);
        return new(campaign.Id, campaign.PublicPromotionId, campaign.BusinessId,
            (await directory.BusinessCardAsync(campaign.BusinessId, ct)).DisplayName, campaign.Title,
            PromotionTypeLabel(campaign.PromotionType), campaign.Allocations.Count, campaign.StartDateUtc,
            campaign.EndDateUtc, PromotionStatusLabel(campaign.Status), campaign.Version,
            campaign.PromotionLiveDurationDays, campaign.Slogan, campaign.Location);
    }

    public async Task<AdminHome> AdminHomeAsync(Actor actor,CancellationToken ct)
    {
        DemandPlatformAdmin(actor);var platform=await new PlatformRevenueRepository(db).SummaryAsync(ct);
        return new(await db.BusinessWallets.CountAsync(ct),await db.CommercePermissions.Where(x=>x.Role==ActorRole.Creator).Select(x=>x.SubjectId).Distinct().CountAsync(ct),
            await db.Promotions.CountAsync(x=>x.Status==PromotionStatus.Active,ct),(await db.Promotions.Select(x=>x.UsedBudget).ToListAsync(ct)).Sum(x=>x.Amount),
            (await db.CreatorEarningEntries.Select(x=>x.Amount).ToListAsync(ct)).Sum(x=>x.Amount),(await db.CustomerCashbackEntries.Select(x=>x.Amount).ToListAsync(ct)).Sum(x=>x.Amount),
            platform.Accrued.Amount,await History(null,ct));
    }
    public async Task<IReadOnlyList<CampaignRow>> AdminCampaignsAsync(Actor actor,CancellationToken ct)
    {
        DemandPlatformAdmin(actor);var campaigns=await db.Promotions.AsNoTracking().Include(x=>x.Allocations).OrderByDescending(x=>x.CreatedAtUtc).ToListAsync(ct);
        var result=new List<CampaignRow>();foreach(var p in campaigns)result.Add(await Row(p,ct));return result;
    }
    public async Task<AdminCampaign> AdminCampaignAsync(Actor actor,Guid id,CancellationToken ct)
    {
        DemandPlatformAdmin(actor);var p=await Campaign(id,ct);var details=await Financial.CampaignAsync(actor,id,ct);var creators=new List<AdminCreatorRow>();
        foreach(var a in details.Creators)creators.Add(new(await directory.CreatorCardAsync(a.CreatorId,ct),a.StartingBudget.Amount,a.Used.Amount,
            a.ParticipationStatus is "Completed" or "Cancelled"?0:a.Remaining.Amount,a.BaselineViews,a.LatestVerifiedViews,a.CampaignVerifiedViews,a.RewardedViews,
            a.ViewEarnings.Amount,a.VerifiedSales,a.SaleCommission.Amount,a.CustomerCashback.Amount,a.PlatformRevenue.Amount,a.ParticipationStatus));
        var version=await db.FinancialConfigurationVersions.AsNoTracking().Where(x=>x.Id==p.PricingSnapshot.ConfigurationVersionId).Select(x=>x.Version).SingleAsync(ct);
        return new(await Row(p,ct),creators,creators.Sum(x=>x.ViewEarnings+x.SaleCommission),creators.Sum(x=>x.CustomerCashback),creators.Sum(x=>x.PlatformRevenue),await History(id,ct),version);
    }
    public async Task<IReadOnlyList<BusinessOversight>> BusinessesAsync(Actor actor,CancellationToken ct)
    {
        DemandPlatformAdmin(actor);var wallets=await db.BusinessWallets.AsNoTracking().ToListAsync(ct);var result=new List<BusinessOversight>();
        foreach(var w in wallets)result.Add(new(await directory.BusinessCardAsync(w.BusinessId,ct),
            await db.CommercePermissions.AnyAsync(x=>x.SubjectId==w.BusinessId&&x.Role==ActorRole.Business&&x.IsActive,ct)?"Active":"Inactive",
            w.TotalBalance.Amount,w.AvailableBalance.Amount,w.ReservedBalance.Amount,await db.Promotions.CountAsync(x=>x.BusinessId==w.BusinessId&&x.Status==PromotionStatus.Active,ct),
            await db.WalletEntries.Where(x=>x.BusinessId==w.BusinessId&&x.Movement=="Deposit").OrderByDescending(x=>x.CreatedAtUtc).Select(x=>(DateTime?)x.CreatedAtUtc).FirstOrDefaultAsync(ct)));
        return result;
    }
    public async Task<IReadOnlyList<CreatorOversight>> CreatorsAsync(Actor actor,CancellationToken ct)
    {
        DemandPlatformAdmin(actor);var threshold=(await new FinancialConfigurationResolver(db).EffectiveAsync(Now,ct)).CreatorPayoutThreshold.Amount;
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
        DemandPlatformAdmin(actor);var v=await new FinancialConfigurationResolver(db).EffectiveAsync(Now,ct);
        ViewPriceInput Input(PricingSnapshot p)=>new(p.ViewsPerReward,p.BusinessCharge.Amount,p.CreatorEarning.Amount,p.PlatformEarning.Amount,p.MinimumPromotionBudget?.Amount);
        var saved=await db.FinancialConfigurationVersions.AsNoTracking().OrderByDescending(x=>x.Version).ToListAsync(ct);
        FinancialSettingsInput Settings(FinancialConfigurationVersion x)=>new(Input(x.ViewOnly),Input(x.ViewPlusCommission),x.ViewPlusCommission.CreatorCommissionPercent,x.ViewPlusCommission.CustomerCashbackPercent,x.ViewPlusCommission.PlatformPercent,x.CreatorPayoutThreshold.Amount,x.CustomerPayoutThreshold.Amount,x.EffectiveFromUtc,
            x.Ugc is { } ugc ? new(ugc.MinimumCreatorPayment.Amount,ugc.PlatformFeePercent,ugc.MinimumUgcBudget?.Amount,ugc.CustomerOfferPlatformSalePercent) : null,
            x.PromotionLiveDurationDays);
        var versions=saved.Select(x=>new FinancialVersionInfo(x.Id,x.Version,x.EffectiveFromUtc,x.ChangedBy,Settings(x))).ToArray();
        return new(new(Input(v.ViewOnly),Input(v.ViewPlusCommission),v.ViewPlusCommission.CreatorCommissionPercent,v.ViewPlusCommission.CustomerCashbackPercent,
            v.ViewPlusCommission.PlatformPercent,v.CreatorPayoutThreshold.Amount,v.CustomerPayoutThreshold.Amount,v.EffectiveFromUtc,
            v.Ugc is { } ugc ? new(ugc.MinimumCreatorPayment.Amount,ugc.PlatformFeePercent,ugc.MinimumUgcBudget?.Amount,ugc.CustomerOfferPlatformSalePercent) : null,
            v.PromotionLiveDurationDays),v.Version,versions);
    }
    public async Task<PayoutWorkspace> PayoutsAsync(Actor actor,CancellationToken ct)
    {
        DemandPlatformAdmin(actor);
        var queues = await PayoutQueuesAsync(ct);
        var platform=await Financial.PlatformAsync(actor,ct);
        var history = queues.History.ToList();
        history.AddRange(platform.History.Select(x=>new PayoutItem(x.Id,"Platform","Weymela",x.Amount.Amount,0,"Paid",x.SettledAtUtc,x.SettledAtUtc,x.Reference)));
        return new(queues.Creators,queues.Customers,platform.Accrued.Amount,platform.Settled.Amount,platform.Unsettled.Amount,history.OrderByDescending(x=>x.PaidAtUtc??x.EligibleAtUtc).ToArray());
    }

    public async Task<OperationsPayoutWorkspace> OperationsPayoutsAsync(Actor actor, CancellationToken ct)
    {
        DemandCapability(actor, AdministrativeCapability.CreatorPayoutProcessing);
        var queues = await PayoutQueuesAsync(ct);
        return new(queues.Creators, queues.Customers, queues.History);
    }

    private async Task<(IReadOnlyList<PayoutQueueRow> Creators, IReadOnlyList<PayoutQueueRow> Customers, IReadOnlyList<PayoutItem> History)> PayoutQueuesAsync(CancellationToken ct)
    {
        var version=await new FinancialConfigurationResolver(db).EffectiveAsync(Now,ct);
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
        return (creators, customers, history.OrderByDescending(x=>x.PaidAtUtc??x.EligibleAtUtc).ToArray());
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
    public async Task<IReadOnlyList<ActivityItem>> NotificationsAsync(Actor actor,CancellationToken ct)
    {
        DemandCapability(actor, AdministrativeCapability.OperationsWorkspace);
        return (await db.OutboxMessages.AsNoTracking().Where(x=>!x.EventType.EndsWith("Audit")).OrderByDescending(x=>x.OccurredAtUtc).Take(100).ToListAsync(ct))
            .Select(x=>new ActivityItem(x.Id,EventLabel(x.EventType),x.OccurredAtUtc,x.Id.ToString())).ToArray();
    }
    private async Task<string> CustomerLabel(Guid id,CancellationToken ct)
    {var customer=await directory.CustomerCardAsync(id,ct);return customer.DisplayName+" · "+customer.PublicId;}
}
