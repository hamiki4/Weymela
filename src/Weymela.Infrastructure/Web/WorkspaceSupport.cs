using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Infrastructure.Web;

public sealed partial class WorkspaceQueries(WeymelaDbContext db, IWorkspaceDirectory directory, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private CommerceAccessPolicy Access => new(db);
    private FinancialQueries Financial => new(db, Access, directory, clock);
    private async Task DemandBusiness(Actor actor, CancellationToken ct)
    {
        if (actor.Role != ActorRole.Business || actor.BusinessId is null ||
            !await db.CommercePermissions.AnyAsync(x => x.UserId == actor.UserId && x.Role == actor.Role && x.SubjectId == actor.BusinessId && x.IsActive,ct))
            throw new ApplicationFailure(FailureKind.Forbidden,"This Business workspace is not available to you.");
    }
    private static void DemandAdmin(Actor actor)
    { if(!AdministrativeAuthority.For(new RealActor(actor)).IsAdmin) throw new ApplicationFailure(FailureKind.Forbidden,"Admin access is required."); }
    private static void DemandPlatformAdmin(Actor actor)
    { if(!AdministrativeAuthority.For(new RealActor(actor)).IsPlatformAdmin) throw new ApplicationFailure(FailureKind.Forbidden,"Platform Admin access is required."); }
    private Task DemandCreator(Actor actor, CancellationToken ct) => Access.EnsureCreatorAsync(actor, actor.CreatorId ?? Guid.Empty,ct);
    internal static string PromotionTypeLabel(PromotionType value)=>value==PromotionType.ViewOnly?"View Only":"View & Sale";
    internal static string PromotionStatusLabel(PromotionStatus value)=>value switch{PromotionStatus.Published=>"Open",PromotionStatus.BudgetExhausted=>"Ended",_=>value.ToString()};
    internal static BusinessPrice BusinessPrice(PricingSnapshot p) => new(PromotionTypeLabel(p.PromotionType),p.ViewsPerReward,p.BusinessCharge.Amount,
        p.PromotionType == PromotionType.ViewPlusCommission ? SnapshotPricing.TotalSalePercent(p) : 0,p.MinimumPromotionBudget?.Amount);
    internal static CreatorPrice CreatorPrice(PricingSnapshot p) => new(PromotionTypeLabel(p.PromotionType),p.ViewsPerReward,p.CreatorEarning.Amount,
        p.PromotionType == PromotionType.ViewPlusCommission ? p.CreatorCommissionPercent : 0);
    private async Task<CampaignRow> Row(Promotion p,CancellationToken ct) => new(p.Id,p.PublicPromotionId,p.BusinessId,
        (await directory.BusinessCardAsync(p.BusinessId,ct)).DisplayName,p.Title,PromotionTypeLabel(p.PromotionType),p.TotalBudget.Amount,p.AllocatedBudget.Amount,
        p.UnallocatedBudget.Amount,p.UsedBudget.Amount,p.RemainingBudget.Amount,p.Allocations.Count,p.StartDateUtc,p.EndDateUtc,PromotionStatusLabel(p.Status),p.Version,p.PromotionLiveDurationDays,
        p.Slogan,p.Location,p.Platforms.Select(x=>new PromotionPlatformView(x.Platform.ToString(),x.ApprovedCount,x.Capacity,x.Available)).ToArray());
    private async Task<IReadOnlyList<ActivityItem>> History(Guid? campaign,CancellationToken ct)
    {
        var query=db.AuditEvents.AsNoTracking();if(campaign is not null) query=query.Where(x=>x.PromotionId==campaign);
        return (await query.OrderByDescending(x=>x.OccurredAtUtc).Take(100).ToListAsync(ct))
            .Select(x=>new ActivityItem(x.Id,EventLabel(x.EventType),x.OccurredAtUtc,x.CorrelationId.ToString())).ToArray();
    }
    internal static string EventLabel(string value) => value switch
    {
        "PromotionCreated"=>"Promotion created", "PromotionFunded"=>"Promotion funded", "PromotionPublished"=>"Promotion published",
        "PromotionActivated"=>"Promotion started", "PromotionCompleted"=>"Promotion completed", "CreatorApplied"=>"New Promotion request",
        "CreatorApproved"=>"Creator approved", "CreatorRejected"=>"Creator request declined", "CreatorBudgetAssigned"=>"Creator Budget assigned",
        "CreatorBudgetIncreased"=>"Creator Budget increased", "CreatorParticipationCompleted"=>"Creator participation completed",
        "BusinessWalletCredited"=>"Funds added", "FinancialConfigurationChanged"=>"Financial settings updated", "ViewReward" or "ViewRewardEarned"=>"View Reward earned",
        "VerifiedSale" or "VerifiedSaleRecorded"=>"Sale confirmed", "PayoutPaid"=>"Payout confirmed", "PayoutPrepared"=>"Payout ready for review",
        "PlatformSettled"=>"Platform settlement recorded", "CreatorPayoutEligible"=>"Creator eligible for payout",
        "CustomerPayoutEligible"=>"Customer eligible for payout", "CreatorBudgetExhausted"=>"Creator Budget needs funding",
        "OfferQrIssued"=>"Offer QR created", "CreatorParticipationActivated"=>"Creator content is live", "VerifiedViewsRecorded"=>"Views verified",
        "VerifiedViewAnomaly"=>"View verification needs review", "ParticipationPaused"=>"Creator participation paused",
        "ParticipationResumed"=>"Creator participation resumed", "CreatorCommissionEarned"=>"Sale Earnings recorded",
        "CustomerCashbackEarned"=>"Customer cashback earned", "PlatformRevenueEarned"=>"Platform revenue earned",
        "UgcCreated"=>"UGC created", "UgcPublished"=>"UGC published", "UgcRequestReceived"=>"New UGC request",
        "UgcRequestApproved"=>"UGC request approved", "UgcContentSubmitted"=>"UGC content submitted",
        "UgcChangesRequested"=>"UGC changes requested", "UgcContentApproved"=>"UGC content approved", _=>"Account activity recorded"
    };
    private async Task<Promotion> Campaign(Guid id,CancellationToken ct) => await db.Promotions.AsNoTracking().Include(x=>x.Allocations).Include(x=>x.Platforms).SingleOrDefaultAsync(x=>x.Id==id,ct)
        ??throw new ApplicationFailure(FailureKind.NotFound,"Promotion not found.");
}
