using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
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

    public async Task<IReadOnlyList<OperationsBusinessView>> OperationsBusinessesAsync(Actor actor, CancellationToken ct, AdminListFilter? filter = null)
    {
        DemandCapability(actor, AdministrativeCapability.BusinessOperationalVisibility);
        var query = filter ?? new();
        var subjects = new AdminSearchQueries(db, query).Permissions(ActorRole.Business).Select(x => x.SubjectId);
        var wallets = await db.BusinessWallets.AsNoTracking().Where(x => subjects.Contains(x.BusinessId))
            .OrderBy(x => x.BusinessId).Skip(query.Offset).Take(query.PageSize).ToListAsync(ct);
        var walletIds = wallets.Select(x => x.BusinessId).ToArray();
        var owners = await db.CommercePermissions.AsNoTracking().Where(x => x.Role == ActorRole.Business && walletIds.Contains(x.SubjectId)).ToListAsync(ct);
        var emails = await VerifiedAccountEmailsAsync(owners.Select(x => x.UserId), ct);
        var phones = await AccountPhonesAsync(owners.Select(x => x.UserId), ct);
        var result = new List<OperationsBusinessView>();
        foreach (var wallet in wallets)
        {
            var owner = owners.FirstOrDefault(x => x.SubjectId == wallet.BusinessId && x.IsActive)
                ?? owners.FirstOrDefault(x => x.SubjectId == wallet.BusinessId);
            result.Add(new(await directory.BusinessCardAsync(wallet.BusinessId, ct),
                await db.CommercePermissions.AnyAsync(x => x.SubjectId == wallet.BusinessId && x.Role == ActorRole.Business && x.IsActive, ct) ? "Active" : "Inactive",
                await db.Promotions.CountAsync(x => x.BusinessId == wallet.BusinessId && x.Status == PromotionStatus.Active, ct),
                await db.WalletEntries.Where(x => x.BusinessId == wallet.BusinessId && x.Movement == "Deposit")
                    .OrderByDescending(x => x.CreatedAtUtc).Select(x => (DateTime?)x.CreatedAtUtc).FirstOrDefaultAsync(ct),
                owner is null ? null : emails.GetValueOrDefault(owner.UserId), owner is null ? null : phones.GetValueOrDefault(owner.UserId)));
        }
        return result;
    }

    public async Task<IReadOnlyList<OperationsCreatorView>> OperationsCreatorsAsync(Actor actor, CancellationToken ct, AdminListFilter? filter = null)
    {
        DemandCapability(actor, AdministrativeCapability.CreatorOperationalVisibility);
        var threshold = (await new FinancialConfigurationResolver(db).FindEffectiveAsync(Now, ct))?.CreatorPayoutThreshold?.Amount;
        var query = filter ?? new();
        var permissions = await new AdminSearchQueries(db, query).Permissions(ActorRole.Creator)
            .OrderBy(x => x.SubjectId).ThenBy(x => x.UserId).Skip(query.Offset).Take(query.PageSize).ToListAsync(ct);
        var emails = await VerifiedAccountEmailsAsync(permissions.Select(x => x.UserId), ct);
        var phones = await AccountPhonesAsync(permissions.Select(x => x.UserId), ct);
        var result = new List<OperationsCreatorView>();
        foreach (var group in permissions.GroupBy(x => x.SubjectId))
        {
            var available = (await db.CreatorEarningsAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.CreatorId == group.Key, ct))?.AvailableEarnings.Amount ?? 0;
            result.Add(new(await directory.CreatorCardAsync(group.Key, ct), group.Any(x => x.IsActive) ? "Active" : "Inactive",
                await db.CreatorAllocations.CountAsync(x => x.CreatorId == group.Key && x.Status == CreatorAllocationStatus.Active, ct),
                threshold is { } minimum && available >= minimum, emails.GetValueOrDefault(group.First().UserId), phones.GetValueOrDefault(group.First().UserId)));
        }
        return result;
    }

    public async Task<IReadOnlyList<OperationsCustomerView>> OperationsCustomersAsync(Actor actor, CancellationToken ct, AdminListFilter? filter = null)
    {
        DemandCapability(actor, AdministrativeCapability.CustomerOperationalVisibility);
        var query = filter ?? new();
        var permissions = await new AdminSearchQueries(db, query).Permissions(ActorRole.Customer)
            .OrderBy(x => x.SubjectId).ThenBy(x => x.UserId).Skip(query.Offset).Take(query.PageSize).ToListAsync(ct);
        var emails = await VerifiedAccountEmailsAsync(permissions.Select(x => x.UserId), ct);
        var phones = await AccountPhonesAsync(permissions.Select(x => x.UserId), ct);
        var result = new List<OperationsCustomerView>();
        foreach (var group in permissions.GroupBy(x => x.SubjectId))
            result.Add(new(await directory.CustomerCardAsync(group.Key, ct), group.Any(x => x.IsActive) ? "Active" : "Inactive",
                emails.GetValueOrDefault(group.First().UserId), phones.GetValueOrDefault(group.First().UserId)));
        return result;
    }

    private async Task<Dictionary<Guid, string>> AccountPhonesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToArray();
        var rows = await db.AuthIdentifiers.AsNoTracking().Where(x => ids.Contains(x.UserId)
            && x.Kind == "Phone" && x.DeliveryAddress != null).ToListAsync(ct);
        return rows.GroupBy(x => x.UserId).ToDictionary(x => x.Key, x => x.First().DeliveryAddress!);
    }

    private async Task<Dictionary<Guid, string>> VerifiedAccountEmailsAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToArray();
        var rows = await db.AuthIdentifiers.AsNoTracking().Where(x => ids.Contains(x.UserId)
            && x.Kind == "Email" && x.IsVerified && x.DeliveryAddress != null).ToListAsync(ct);
        return rows.GroupBy(x => x.UserId).ToDictionary(x => x.Key,
            x => x.OrderByDescending(y => y.CreatedAtUtc).First().DeliveryAddress!);
    }

    public async Task<IReadOnlyList<OperationsCampaignView>> OperationsCampaignsAsync(Actor actor, CancellationToken ct, AdminListFilter? filter = null)
    {
        DemandCapability(actor, AdministrativeCapability.CampaignOperationalVisibility);
        var query = filter ?? new();
        var campaigns = await new AdminSearchQueries(db, query).Campaigns().Include(x => x.Allocations)
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(query.Offset).Take(query.PageSize).ToListAsync(ct);
        var result = new List<OperationsCampaignView>();
        foreach (var campaign in campaigns)
            result.Add(new(campaign.Id, campaign.PublicPromotionId, campaign.BusinessId,
                (await directory.BusinessCardAsync(campaign.BusinessId, ct)).DisplayName, campaign.Title,
                PromotionTypeLabel(campaign.PromotionType), campaign.Allocations.Count, campaign.StartDateUtc,
                campaign.EndDateUtc, PromotionStatusLabel(campaign.Status), campaign.Version,
                campaign.PromotionLiveDurationDays, campaign.Slogan, campaign.Location,(await Row(campaign,ct)).LowFunds));
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
            campaign.PromotionLiveDurationDays, campaign.Slogan, campaign.Location,(await Row(campaign,ct)).LowFunds);
    }

    public async Task<AdminHome> AdminHomeAsync(Actor actor,CancellationToken ct)
    {
        DemandPlatformAdmin(actor);var platform=await new PlatformRevenueRepository(db).SummaryAsync(ct);
        return new(await db.BusinessWallets.CountAsync(ct),await db.CommercePermissions.Where(x=>x.Role==ActorRole.Creator).Select(x=>x.SubjectId).Distinct().CountAsync(ct),
            await db.Promotions.CountAsync(x=>x.Status==PromotionStatus.Active,ct),(await db.Promotions.Select(x=>x.UsedBudget).ToListAsync(ct)).Sum(x=>x.Amount),
            (await db.CreatorEarningEntries.Select(x=>x.Amount).ToListAsync(ct)).Sum(x=>x.Amount) - (await db.UgcPaymentCorrections.Select(x=>x.CreatorAmount).ToListAsync(ct)).Sum(x=>x.Amount),(await db.CustomerCashbackEntries.Select(x=>x.Amount).ToListAsync(ct)).Sum(x=>x.Amount),
            platform.Accrued.Amount,await History(null,ct));
    }
    public async Task<IReadOnlyList<CampaignRow>> AdminCampaignsAsync(Actor actor,CancellationToken ct, AdminListFilter? filter = null)
    {
        DemandPlatformAdmin(actor);var query = filter ?? new();
        var campaigns=await new AdminSearchQueries(db, query).Campaigns().Include(x=>x.Allocations)
            .OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip(query.Offset).Take(query.PageSize).ToListAsync(ct);
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
        DemandPlatformAdmin(actor);var threshold=(await new FinancialConfigurationResolver(db).FindEffectiveAsync(Now,ct))?.CreatorPayoutThreshold?.Amount;
        var permissions=await db.CommercePermissions.AsNoTracking().Where(x=>x.Role==ActorRole.Creator).ToListAsync(ct);var result=new List<CreatorOversight>();
        foreach(var group in permissions.GroupBy(x=>x.SubjectId))
        {
            var available=(await db.CreatorEarningsAccounts.SingleOrDefaultAsync(x=>x.CreatorId==group.Key,ct))?.AvailableEarnings.Amount??0;
            result.Add(new(await directory.CreatorCardAsync(group.Key,ct),group.Any(x=>x.IsActive)?"Active":"Inactive",
                await db.CreatorAllocations.CountAsync(x=>x.CreatorId==group.Key&&x.Status==CreatorAllocationStatus.Active,ct),available,threshold is { } minimum && available>=minimum));
        }
        return result;
    }
    public async Task<FinancialSettingsWorkspace> SettingsAsync(Actor actor,CancellationToken ct)
    {
        DemandPlatformAdmin(actor);
        ViewPriceInput? Input(PricingSnapshot? p)=>p is null ? null : new(p.ViewsPerReward,p.BusinessCharge.Amount,p.CreatorEarning.Amount,p.PlatformEarning.Amount,p.MinimumPromotionBudget?.Amount);
        var saved=await db.FinancialConfigurationVersions.AsNoTracking().OrderByDescending(x=>x.Version).ToListAsync(ct);
        FinancialSettingsInput Settings(FinancialConfigurationVersion x)=>new(Input(x.ViewOnly),Input(x.ViewPlusCommission),x.CreatorCommissionPercent,x.CustomerCashbackPercent,x.PlatformPercent,x.CreatorPayoutThreshold?.Amount,x.CustomerPayoutThreshold?.Amount,x.EffectiveFromUtc,
            x.Ugc is { } ugc ? new(ugc.MinimumCreatorPayment.Amount,ugc.PlatformFeePercent,ugc.MinimumUgcBudget?.Amount,ugc.CustomerOfferPlatformSalePercent) : null,
            x.PromotionLiveDurationDays,x.EnforceAudienceRequirements,x.LowPromotionBalanceAlert,x.MonthlyPayoutDay,x.MonthlyPayoutEffectiveFromUtc,x.UgcPayoutMinimumExempt,x.MonthlyPayoutEnabled);
        var versions=saved.Select(x=>new FinancialVersionInfo(x.Id,x.Version,x.EffectiveFromUtc,x.ChangedBy,Settings(x))).ToArray();
        var v = saved.Where(x => x.EffectiveFromUtc <= Now).OrderByDescending(x => x.EffectiveFromUtc)
            .ThenByDescending(x => x.Version).ThenByDescending(x => x.Id).FirstOrDefault();
        if (v is null)
        {
            var baseline = options is null ? null : await InactiveFinancialBaselines.EffectiveAsync(
                db, options.FirebaseProjectId, Now, ct);
            var approval = baseline?.Approval;
            return new(null, 0, versions, approval is null ? null : new(baseline!.Version,
                approval.CustomerCashbackPercent, approval.CreatorCommissionPercent, approval.PlatformPercent,
                approval.EffectiveFromUtc));
        }
        return new(Settings(v),v.Version,versions);
    }
    public async Task<PayoutWorkspace> PayoutsAsync(Actor actor,CancellationToken ct, AdminListFilter? filter = null)
    {
        DemandPlatformAdmin(actor);
        var queues = await PayoutQueuesAsync(ct, filter, includePlatform: true);
        var totals = await db.Database.SqlQueryRaw<PlatformPayoutTotals>("""
            SELECT (SELECT coalesce(sum("Amount"),0) FROM v3."PlatformRevenueEntries")
                - (SELECT coalesce(sum("PlatformFee"),0) FROM v3."UgcPaymentCorrections") AS "Accrued",
                (SELECT coalesce(sum("Amount"),0) FROM v3."PlatformSettlements") AS "Settled"
            """).SingleAsync(ct);
        return new(queues.Creators,queues.Customers,totals.Accrued,totals.Settled,totals.Accrued-totals.Settled,queues.History);
    }

    public async Task<OperationsPayoutWorkspace> OperationsPayoutsAsync(Actor actor, CancellationToken ct, AdminListFilter? filter = null)
    {
        DemandCapability(actor, AdministrativeCapability.CreatorPayoutProcessing);
        var queues = await PayoutQueuesAsync(ct, filter);
        return new(queues.Creators, queues.Customers, queues.History);
    }

    private async Task<(IReadOnlyList<PayoutQueueRow> Creators, IReadOnlyList<PayoutQueueRow> Customers, IReadOnlyList<PayoutItem> History)> PayoutQueuesAsync(CancellationToken ct, AdminListFilter? filter = null, bool includePlatform = false)
    {
        var version=await new FinancialConfigurationResolver(db).FindEffectiveAsync(Now,ct);
        var query = filter ?? new(); query.Validate();
        var term = query.Term;
        var statusFilter = query.Status ?? "";
        var methodFilter = query.Type ?? "";
        // Numeric balances are filtered by PostgreSQL before paging. Creator earned
        // balance is still checked again below, and at the payout command boundary.
        var creatorCandidates = db.Database.SqlQuery<Guid>($"""
            SELECT a."CreatorId" AS "Value" FROM v3."CreatorEarningsAccounts" a
            WHERE a."AvailableEarnings">0
              AND ({term}='' OR EXISTS (SELECT 1 FROM v3."PublicWorkspaceProfiles" p WHERE p."Role"='Creator' AND p."SubjectId"=a."CreatorId" AND strpos(lower(p."DisplayName"),{term})>0))
              AND ({methodFilter}='' OR EXISTS (SELECT 1 FROM v3."PayoutDestinations" d WHERE d."SubjectId"=a."CreatorId" AND d."Beneficiary"='Creator' AND (d."Method"={methodFilter} OR d."Provider"={methodFilter})))
              AND ({statusFilter}=''
                OR {statusFilter}='NeedsDestination' AND NOT EXISTS (SELECT 1 FROM v3."PayoutDestinations" d WHERE d."Beneficiary"='Creator' AND d."SubjectId"=a."CreatorId")
                OR {statusFilter}='Ready' AND EXISTS (SELECT 1 FROM v3."PayoutRecords" p WHERE p."CreatorId"=a."CreatorId" AND p."Status"='Eligible')
                OR {statusFilter}='Eligible' AND EXISTS (SELECT 1 FROM v3."PayoutDestinations" d WHERE d."Beneficiary"='Creator' AND d."SubjectId"=a."CreatorId")
                  AND NOT EXISTS (SELECT 1 FROM v3."PayoutRecords" p WHERE p."CreatorId"=a."CreatorId" AND p."Status"='Eligible'))
            ORDER BY a."CreatorId"
            """);
        var customerCandidates = db.Database.SqlQuery<Guid>($"""
            SELECT a."CustomerId" AS "Value" FROM v3."CustomerCashbackAccounts" a
            WHERE a."AvailableCashback">0
              AND ({term}='' OR EXISTS (SELECT 1 FROM v3."PublicWorkspaceProfiles" p WHERE p."Role"='Customer' AND p."SubjectId"=a."CustomerId" AND strpos(lower(p."DisplayName"),{term})>0))
              AND ({methodFilter}='' OR EXISTS (SELECT 1 FROM v3."PayoutDestinations" d WHERE d."SubjectId"=a."CustomerId" AND d."Beneficiary"='Customer' AND (d."Method"={methodFilter} OR d."Provider"={methodFilter})))
              AND ({statusFilter}=''
                OR {statusFilter}='NeedsDestination' AND NOT EXISTS (SELECT 1 FROM v3."PayoutDestinations" d WHERE d."Beneficiary"='Customer' AND d."SubjectId"=a."CustomerId")
                OR {statusFilter}='Ready' AND EXISTS (SELECT 1 FROM v3."PayoutRecords" p WHERE p."CustomerId"=a."CustomerId" AND p."Status"='Eligible')
                OR {statusFilter}='Eligible' AND EXISTS (SELECT 1 FROM v3."PayoutDestinations" d WHERE d."Beneficiary"='Customer' AND d."SubjectId"=a."CustomerId")
                  AND NOT EXISTS (SELECT 1 FROM v3."PayoutRecords" p WHERE p."CustomerId"=a."CustomerId" AND p."Status"='Eligible'))
            ORDER BY a."CustomerId"
            """);
        async Task<List<Guid>> EligibleSubjects(IQueryable<Guid> source, PayoutBeneficiary kind)
        {
            var result = new List<Guid>();
            if (version is null) return result;
            var matched = 0;
            for (var offset = 0; ; offset += 100)
            {
                var batch = await source.Skip(offset).Take(100).ToListAsync(ct);
                foreach (var id in batch)
                {
                    var available = kind == PayoutBeneficiary.Creator
                        ? (await CreatorEarnedBalance.AvailableAsync(db,id,ct)).Amount
                        : (await db.CustomerCashbackAccounts.AsNoTracking().SingleAsync(x => x.CustomerId == id,ct)).AvailableCashback.Amount;
                    if (await PayoutEligibleBalance.AmountAsync(db,version,kind,id,available,Now,ct) <= 0) continue;
                    if (matched++ < query.Offset) continue;
                    result.Add(id);
                    if (result.Count == query.PageSize) return result;
                }
                if (batch.Count < 100) return result;
            }
        }
        var eligibleCreators = await EligibleSubjects(creatorCandidates, PayoutBeneficiary.Creator);
        var eligibleCustomers = await EligibleSubjects(customerCandidates, PayoutBeneficiary.Customer);
        var records=await db.PayoutRecords.AsNoTracking().Where(x => x.Status == PayoutStatus.Eligible
            && (x.CreatorId != null && eligibleCreators.Contains(x.CreatorId.Value)
                || x.CustomerId != null && eligibleCustomers.Contains(x.CustomerId.Value))).ToListAsync(ct);
        var creators=new List<PayoutQueueRow>();var customers=new List<PayoutQueueRow>();
        var destinations=await db.PayoutDestinations.AsNoTracking().Where(x => eligibleCreators.Contains(x.SubjectId) || eligibleCustomers.Contains(x.SubjectId)).ToDictionaryAsync(x=>(x.Beneficiary,x.SubjectId),ct);
        var historySource = db.PayoutRecords.AsNoTracking();
        if (term != "") historySource = historySource.Where(x => x.Reference != null && x.Reference.ToLower().Contains(term)
            || x.DestinationProvider != null && x.DestinationProvider.ToLower().Contains(term)
            || db.PublicWorkspaceProfiles.Any(p => (p.SubjectId == x.CreatorId || p.SubjectId == x.CustomerId) && p.DisplayName.ToLower().Contains(term)));
        if (query.From is { } from) historySource = historySource.Where(x => (x.PaidAtUtc ?? x.EligibleAtUtc) >= from);
        if (query.To is { } to) historySource = historySource.Where(x => (x.PaidAtUtc ?? x.EligibleAtUtc) < to.AddDays(1));
        if (Enum.TryParse<PayoutStatus>(statusFilter, out var historyStatus)) historySource = historySource.Where(x => x.Status == historyStatus);
        else if (statusFilter != "") historySource = historySource.Where(x => false);
        if (Enum.TryParse<PayoutDestinationMethod>(methodFilter, out var historyMethod)) historySource = historySource.Where(x => x.DestinationMethod == historyMethod);
        else if (methodFilter != "") historySource = historySource.Where(x => x.DestinationProvider == methodFilter);
        // Platform settlements share the history page, while Operations Admin never
        // receives them. Filter and merge keys before paging to avoid skipped rows.
        var platformSource = db.PlatformSettlements.AsNoTracking().Where(x => includePlatform);
        if (term != "") platformSource = platformSource.Where(x => "weymela".Contains(term) || x.Reference.ToLower().Contains(term));
        if (query.From is { } platformFrom) platformSource = platformSource.Where(x => x.SettledAtUtc >= platformFrom);
        if (query.To is { } platformTo) platformSource = platformSource.Where(x => x.SettledAtUtc < platformTo.AddDays(1));
        if (statusFilter != "" && statusFilter != "Paid" || methodFilter != "") platformSource = platformSource.Where(x => false);
        var historyKeys = await historySource.Select(x => new { x.Id, At = x.PaidAtUtc ?? x.EligibleAtUtc, IsPlatform = false })
            .Concat(platformSource.Select(x => new { x.Id, At = x.SettledAtUtc, IsPlatform = true }))
            .OrderByDescending(x => x.At).ThenBy(x => x.Id).Skip(query.Offset).Take(query.PageSize).ToListAsync(ct);
        var payoutIds = historyKeys.Where(x => !x.IsPlatform).Select(x => x.Id).ToArray();
        var platformIds = historyKeys.Where(x => x.IsPlatform).Select(x => x.Id).ToArray();
        var historyRows = await db.PayoutRecords.AsNoTracking().Where(x => payoutIds.Contains(x.Id)).ToListAsync(ct);
        var settlements = await db.PlatformSettlements.AsNoTracking().Where(x => platformIds.Contains(x.Id)).ToListAsync(ct);
        var history=new List<PayoutItem>();
        history.AddRange(settlements.Select(x => new PayoutItem(x.Id,"Platform","Weymela",x.Amount.Amount,0,"Paid",x.SettledAtUtc,x.SettledAtUtc,x.Reference)));
        foreach(var p in historyRows)history.Add(new(p.Id,p.Beneficiary.ToString(),p.CreatorId is {} creator?(await directory.CreatorCardAsync(creator,ct)).DisplayName:await CustomerLabel(p.CustomerId!.Value,ct),p.Amount.Amount,p.ThresholdUsed.Amount,p.Status.ToString(),p.EligibleAtUtc,p.PaidAtUtc,p.Reference));
        // Unknown thresholds cannot establish eligibility. Preserve actual history without
        // inventing thresholds or placing a beneficiary in an actionable payout queue.
        if (version is null) return (creators, customers, history.OrderByDescending(x => x.PaidAtUtc ?? x.EligibleAtUtc).ThenBy(x => x.Id).ToArray());
        foreach(var a in await db.CreatorEarningsAccounts.AsNoTracking().Where(x => eligibleCreators.Contains(x.CreatorId)).ToListAsync(ct))
        {
            if (version.CreatorPayoutThreshold is not { } creatorThreshold) break;
            var pending=records.SingleOrDefault(x=>x.CreatorId==a.CreatorId&&x.Status==PayoutStatus.Eligible);
            var available = (await CreatorEarnedBalance.AvailableAsync(db, a.CreatorId, ct)).Amount;
            var eligible = await PayoutEligibleBalance.AmountAsync(db,version,PayoutBeneficiary.Creator,a.CreatorId,available,Now,ct);
            if(eligible<=0)continue;
            var name=(await directory.CreatorCardAsync(a.CreatorId,ct)).DisplayName;
            var since=await EligibleSince(PayoutBeneficiary.Creator,a.CreatorId,creatorThreshold.Amount,version.EffectiveFromUtc,ct);
            destinations.TryGetValue((PayoutBeneficiary.Creator,a.CreatorId),out var destination);
            var method=pending?.DestinationMethod?.ToString()??destination?.Method.ToString();
            var provider=pending?.DestinationProvider??destination?.Provider;
            var protectedAccount=pending?.ProtectedDestinationAccount??destination?.ProtectedAccount;
            var account=protectedAccount is null||payoutProtector is null?null:PayoutDestinationService.DisplayUnprotected(payoutProtector.Unprotect(protectedAccount));
            creators.Add(new(a.CreatorId,pending?.Id,name,available,pending?.ThresholdUsed.Amount??creatorThreshold.Amount,
                pending?.Amount.Amount??eligible,pending?.EligibleAtUtc??since,
                method is null?"NeedsDestination":pending is null?"Eligible":"Ready",method,provider,account,eligible));
        }
        foreach(var a in await db.CustomerCashbackAccounts.AsNoTracking().Where(x => eligibleCustomers.Contains(x.CustomerId)).ToListAsync(ct))
        {
            if (version.CustomerPayoutThreshold is not { } customerThreshold) break;
            var pending=records.SingleOrDefault(x=>x.CustomerId==a.CustomerId&&x.Status==PayoutStatus.Eligible);
            var eligible = await PayoutEligibleBalance.AmountAsync(db,version,PayoutBeneficiary.Customer,a.CustomerId,a.AvailableCashback.Amount,Now,ct);
            if(eligible<=0 && pending is null)continue;
            destinations.TryGetValue((PayoutBeneficiary.Customer,a.CustomerId),out var destination);
            var method=pending?.DestinationMethod?.ToString()??destination?.Method.ToString();
            var provider=pending?.DestinationProvider??destination?.Provider;
            var protectedAccount=pending?.ProtectedDestinationAccount??destination?.ProtectedAccount;
            var account=protectedAccount is null||payoutProtector is null?null:PayoutDestinationService.DisplayUnprotected(payoutProtector.Unprotect(protectedAccount));
            customers.Add(new(a.CustomerId,pending?.Id,await CustomerLabel(a.CustomerId,ct),a.AvailableCashback.Amount,pending?.ThresholdUsed.Amount??customerThreshold.Amount,
                pending?.Amount.Amount??eligible,pending?.EligibleAtUtc??await EligibleSince(PayoutBeneficiary.Customer,a.CustomerId,customerThreshold.Amount,version.EffectiveFromUtc,ct),
                method is null?"NeedsDestination":pending is null?"Eligible":"Ready",method,provider,account,eligible));
        }
        return (creators.Where(x => statusFilter == "" || x.Status == statusFilter).ToArray(), customers.Where(x => statusFilter == "" || x.Status == statusFilter).ToArray(), history.OrderByDescending(x=>x.PaidAtUtc??x.EligibleAtUtc).ThenBy(x=>x.Id).ToArray());
    }
    public async Task<IReadOnlyList<PayoutQueueRow>> PayoutRecipientsAsync(Actor actor, PayoutBeneficiary kind, AdminListFilter filter, CancellationToken ct)
    {
        DemandCapability(actor, kind == PayoutBeneficiary.Creator ? AdministrativeCapability.CreatorPayoutProcessing : AdministrativeCapability.CustomerPayoutProcessing);
        filter.Validate();
        var role = kind == PayoutBeneficiary.Creator ? ActorRole.Creator : ActorRole.Customer;
        var term = filter.Term;
        var ids = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == role
            && (term == "" || x.DisplayName.ToLower().Contains(term) || x.PublicId.ToLower().Contains(term)))
            .OrderBy(x => x.DisplayName).ThenBy(x => x.SubjectId).Skip(filter.Offset).Take(filter.PageSize).Select(x => x.SubjectId).ToListAsync(ct);
        var result = new List<PayoutQueueRow>();
        var version = await new FinancialConfigurationResolver(db).FindEffectiveAsync(Now,ct);
        foreach (var id in ids)
        {
            var available = kind == PayoutBeneficiary.Creator ? (await CreatorEarnedBalance.AvailableAsync(db,id,ct)).Amount
                : (await db.CustomerCashbackAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.CustomerId == id,ct))?.AvailableCashback.Amount ?? 0;
            var eligible = version is null ? 0 : await PayoutEligibleBalance.AmountAsync(db,version,kind,id,available,Now,ct);
            var pending = await db.PayoutRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Beneficiary == kind && (x.CreatorId == id || x.CustomerId == id) && x.Status == PayoutStatus.Eligible,ct);
            var destination = await db.PayoutDestinations.AsNoTracking().SingleOrDefaultAsync(x => x.Beneficiary == kind && x.SubjectId == id,ct);
            var account = destination is null || payoutProtector is null ? null : PayoutDestinationService.DisplayUnprotected(payoutProtector.Unprotect(destination.ProtectedAccount));
            result.Add(new(id,pending?.Id,kind == PayoutBeneficiary.Creator ? (await directory.CreatorCardAsync(id,ct)).DisplayName : await CustomerLabel(id,ct),
                available,kind == PayoutBeneficiary.Creator ? version?.CreatorPayoutThreshold?.Amount ?? 0 : version?.CustomerPayoutThreshold?.Amount ?? 0,
                pending?.Amount.Amount ?? eligible,pending?.EligibleAtUtc,pending is not null ? "Ready" : eligible<=0 ? "BelowThreshold" : destination is null ? "NeedsDestination" : "Eligible",
                destination?.Method.ToString(),destination?.Provider,account,eligible));
        }
        return result;
    }
    private sealed record PlatformPayoutTotals(decimal Accrued, decimal Settled);

    private async Task<DateTime?> EligibleSince(PayoutBeneficiary kind,Guid id,decimal threshold,DateTime effective,CancellationToken ct)
    {
        var credits=kind==PayoutBeneficiary.Creator
            ?(await db.CreatorEarningEntries.Where(x=>x.CreatorId==id).ToListAsync(ct)).Select(x=>(At:x.CreatedAtUtc,Amount:x.Amount.Amount)).ToList()
            :(await db.CustomerCashbackEntries.Where(x=>x.CustomerId==id).ToListAsync(ct)).Select(x=>(At:x.CreatedAtUtc,Amount:x.Amount.Amount)).ToList();
        if(kind==PayoutBeneficiary.Creator) credits.AddRange((await db.UgcPaymentCorrections.Where(x=>x.CreatorId==id).ToListAsync(ct)).Select(x=>(At:x.CreatedAtUtc,Amount:-x.CreatorAmount.Amount)));
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
