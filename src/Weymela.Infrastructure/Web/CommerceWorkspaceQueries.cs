using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Web;

public sealed partial class WorkspaceQueries
{
    public async Task<IReadOnlyList<CheckoutSaleRow>> RecentSalesAsync(Actor actor,CancellationToken ct)
    {
        if(actor.BusinessId is null)throw new ApplicationFailure(FailureKind.Forbidden,"Checkout permission is required.");
        if(actor.Role==ActorRole.Business) await DemandBusiness(actor,ct);
        else await Access.EnsureScannerAsync(actor,actor.BusinessId.Value,ct);
        var limit = actor.Role == ActorRole.Cashier ? 50 : int.MaxValue;
        var rows=new List<CheckoutSaleRow>();
        var promotionSalesQuery = db.VerifiedSales.AsNoTracking().Where(x => x.BusinessId == actor.BusinessId);
        if (actor.Role == ActorRole.Cashier)
            promotionSalesQuery = promotionSalesQuery.Where(x => x.CashierId == actor.UserId);
        var promotionSales=await promotionSalesQuery
            .OrderByDescending(x=>x.CreatedAtUtc).Take(limit).ToListAsync(ct);
        var offerSalesQuery = db.UgcCustomerOfferSales.AsNoTracking().Where(x => x.BusinessId == actor.BusinessId);
        if (actor.Role == ActorRole.Cashier)
            offerSalesQuery = offerSalesQuery.Where(x => x.CashierId == actor.UserId);
        var offerSales=await offerSalesQuery
            .OrderByDescending(x=>x.CreatedAtUtc).Take(limit).ToListAsync(ct);
        var assignmentIds=offerSales.Where(x=>x.UgcAssignmentId is not null).Select(x=>x.UgcAssignmentId!.Value).Distinct().ToArray();
        var assignmentCreators=await db.UgcAssignments.AsNoTracking().Where(x=>assignmentIds.Contains(x.Id))
            .ToDictionaryAsync(x=>x.Id,x=>x.CreatorId,ct);
        var creatorIds=promotionSales.Select(x=>x.CreatorId).Concat(assignmentCreators.Values).Distinct().ToArray();
        var creatorNames=await db.PublicWorkspaceProfiles.AsNoTracking()
            .Where(x=>x.Role==ActorRole.Creator && creatorIds.Contains(x.SubjectId))
            .ToDictionaryAsync(x=>x.SubjectId,x=>x.DisplayName,ct);
        var creatorNumbers=await db.PublicWorkspaceProfiles.AsNoTracking()
            .Where(x=>x.Role==ActorRole.Creator && creatorIds.Contains(x.SubjectId) && x.CreatorNumber!=null)
            .ToDictionaryAsync(x=>x.SubjectId,x=>x.CreatorNumber,ct);
        var customerIds=promotionSales.Select(x=>x.CustomerId).Concat(offerSales.Select(x=>x.CustomerId)).Distinct().ToArray();
        var customerUsers=await db.CustomerProfiles.AsNoTracking().Where(x=>customerIds.Contains(x.CustomerId))
            .ToDictionaryAsync(x=>x.CustomerId,x=>x.UserId,ct);
        var userIds=customerUsers.Values.Distinct().ToArray();
        var phones=await db.AuthIdentifiers.AsNoTracking().Where(x=>userIds.Contains(x.UserId)
            && x.Kind=="Phone" && x.IsVerified && x.DeliveryAddress!=null).ToListAsync(ct);
        var maskedByUser=phones.GroupBy(x=>x.UserId).ToDictionary(x=>x.Key,x=>
        {
            var digits=new string(x.First().DeliveryAddress!.Where(char.IsAsciiDigit).ToArray());
            return digits.Length>=4 ? "••••"+digits[^4..] : "Customer";
        });
        string Masked(Guid customerId) => customerUsers.TryGetValue(customerId,out var userId)
            ? maskedByUser.GetValueOrDefault(userId,"Customer") : "Customer";
        var promotionIds=promotionSales.Select(x=>x.PromotionId).Distinct().ToArray();
        var titles=await db.Promotions.AsNoTracking().Where(x=>promotionIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.Title,ct);
        // Revoked preauthorizations are immutable audit history. A cashier can
        // later be invited again with the same verified phone/user, so select
        // one current label per user instead of assuming the history is unique.
        var cashierHistory = await db.CashierPreauthorizations.AsNoTracking()
            .Where(x => x.BusinessId == actor.BusinessId && x.UserId != null)
            .ToListAsync(ct);
        var promotionCashiers = cashierHistory
            .GroupBy(x => x.UserId!.Value)
            .ToDictionary(x => x.Key, x => x
                .OrderByDescending(y => y.Status == CashierPreauthorizationStatus.Active)
                .ThenByDescending(y => y.ActivatedAtUtc)
                .ThenByDescending(y => y.CreatedAtUtc)
                .First().DisplayName);
        rows.AddRange(promotionSales.Select(x=>new CheckoutSaleRow(x.Id,titles.GetValueOrDefault(x.PromotionId,"View & Sale"),
            "VIEW_AND_SALE_PROMOTION",x.PurchaseAmount.Amount,x.CustomerCashbackAmount.Amount,x.PurchaseAmount.Amount,
            x.TotalPromotionCharge.Amount,x.CreatedAtUtc,promotionCashiers.GetValueOrDefault(x.CashierId),
            creatorNames.GetValueOrDefault(x.CreatorId),Masked(x.CustomerId),x.Status.ToString(),
            actor.Role==ActorRole.Business?creatorNumbers.GetValueOrDefault(x.CreatorId):null,
            actor.Role==ActorRole.Business?x.CreatorCommissionAmount.Amount:null)));
        var offerIds=offerSales.Select(x=>x.UgcCustomerOfferId).Distinct().ToArray();
        var offers=await db.UgcCustomerOffers.AsNoTracking().Where(x=>offerIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
        var ugcIds=offers.Values.Select(x=>x.UgcOpportunityId).Distinct().ToArray();
        var ugcTitles=await db.UgcOpportunities.AsNoTracking().Where(x=>ugcIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.Title,ct);
        rows.AddRange(offerSales.Select(x=>new CheckoutSaleRow(x.Id,
            offers.TryGetValue(x.UgcCustomerOfferId,out var offer)?ugcTitles.GetValueOrDefault(offer.UgcOpportunityId,"UGC"):"UGC",
            "UGC_CUSTOMER_OFFER",x.PurchaseAmount.Amount,x.CustomerDiscountAmount.Amount,x.CustomerPaysAmount.Amount,
            x.TotalOfferCharge.Amount,x.CreatedAtUtc,promotionCashiers.GetValueOrDefault(x.CashierId),
            x.UgcAssignmentId is { } assignmentId && assignmentCreators.TryGetValue(assignmentId,out var creatorId)
                ? creatorNames.GetValueOrDefault(creatorId) : null,Masked(x.CustomerId),"Completed",
            actor.Role==ActorRole.Business && x.UgcAssignmentId is { } linkedId && assignmentCreators.TryGetValue(linkedId,out var linkedCreator)
                ? creatorNumbers.GetValueOrDefault(linkedCreator) : null,null,x.BenefitMode.ToString())));
        return rows.OrderByDescending(x=>x.CreatedAtUtc).Take(limit).ToArray();
    }

    public async Task<SaleResult> CheckoutResultAsync(Actor actor,Guid saleId,CancellationToken ct)
    {
        if(actor.BusinessId is null)throw new ApplicationFailure(FailureKind.Forbidden,"Checkout permission is required.");
        await Access.EnsureScannerAsync(actor,actor.BusinessId.Value,ct);
        // The first HTTP response and retry both use committed PostgreSQL precision, not an unrounded tracked object.
        var sale=await db.VerifiedSales.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==saleId&&x.BusinessId==actor.BusinessId,ct);
        if(sale is not null)return new(sale.Id,sale.PurchaseAmount,sale.TotalPromotionCharge,sale.CreatedAtUtc);
        var ugcSale=await db.UgcCustomerOfferSales.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==saleId&&x.BusinessId==actor.BusinessId,ct)
            ??throw new ApplicationFailure(FailureKind.NotFound,"Confirmed purchase not found.");
        return new(ugcSale.Id,ugcSale.PurchaseAmount,ugcSale.TotalOfferCharge,ugcSale.CreatedAtUtc,
            ugcSale.CustomerPaysAmount,ugcSale.CustomerDiscountAmount,"UGC_CUSTOMER_OFFER",ugcSale.BenefitMode.ToString());
    }
    public async Task<IReadOnlyList<CustomerOfferCard>> OffersAsync(Actor actor,CancellationToken ct)
    {
        var offers=await Financial.CustomerOffersAsync(actor,ct);var result=new List<CustomerOfferCard>();
        foreach(var offer in offers)
        {
            if(offer.Source=="VIEW_AND_SALE_PROMOTION")
            {
                var a=await db.CreatorAllocations.AsNoTracking().SingleAsync(x=>x.Id==offer.OfferId,ct);
                var live=await db.CreatorPromotionParticipations.AsNoTracking().SingleAsync(x=>x.CreatorAllocationId==a.Id,ct);
                var profile=await directory.CreatorCardAsync(a.CreatorId,ct);
                var business=await directory.CustomerOfferBusinessAsync(offer.Business.Id,ct);
                var content=PublicContentLink.Create(live.Provider,live.ExternalContentId);
                result.Add(new(a.Id,offer.Source,offer.Offer,new(business.DisplayName,business.DirectionsUrl,business.Latitude,business.Longitude,business.Category,
                        OfferAddress(business.Address, offer.Location)),
                    new(profile.DisplayName),offer.BenefitPercent,content,offer.Slogan,offer.Location,offer.RemainingDays));
            }
            else
            {
                var business=await directory.CustomerOfferBusinessAsync(offer.Business.Id,ct);
                if (offer.Creator is null || offer.UgcAssignmentId is null) continue;
                result.Add(new(offer.OfferId,offer.Source,offer.Offer,
                    new(business.DisplayName,business.DirectionsUrl,business.Latitude,business.Longitude,business.Category,
                        OfferAddress(business.Address, offer.Location)),
                    new(offer.Creator.DisplayName),offer.BenefitPercent,
                    offer.Provider is null || offer.ExternalContentId is null ? null
                        : PublicContentLink.Create(offer.Provider,offer.ExternalContentId),
                    offer.Slogan,offer.Location,UgcAssignmentId:offer.UgcAssignmentId,BenefitMode:offer.BenefitMode));
            }
        }
        return result;
    }
    private static string? OfferAddress(string? businessAddress, string? offerLocation) =>
        !string.IsNullOrWhiteSpace(businessAddress) ? businessAddress.Trim()
        : !string.IsNullOrWhiteSpace(offerLocation) ? offerLocation.Trim() : null;
    public async Task<string> QrStatusAsync(Actor actor,Guid id,CancellationToken ct)
    {
        await Access.EnsureCustomerAsync(actor,ct);var qr=await db.OfferQrSessions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.CustomerId==actor.CustomerId,ct)
            ??throw new ApplicationFailure(FailureKind.NotFound,"Offer QR not found.");
        return qr.Status==OfferQrStatus.Used?"Used":Now>=qr.ExpiresAtUtc?"Expired":"Issued";
    }
    internal static string? PublicContentUrl(string provider,string content) => PublicContentLink.Create(provider,content);
}
