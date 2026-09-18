using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;

namespace Weymela.Infrastructure.Web;

public sealed partial class WorkspaceQueries
{
    public async Task<IReadOnlyList<CheckoutSaleRow>> RecentSalesAsync(Actor actor,CancellationToken ct)
    {
        if(actor.BusinessId is null)throw new ApplicationFailure(FailureKind.Forbidden,"Checkout permission is required.");
        await Access.EnsureScannerAsync(actor,actor.BusinessId.Value,ct);
        var rows=new List<CheckoutSaleRow>();
        var promotionSales=await db.VerifiedSales.AsNoTracking().Where(x=>x.BusinessId==actor.BusinessId)
            .OrderByDescending(x=>x.CreatedAtUtc).Take(50).ToListAsync(ct);
        var promotionIds=promotionSales.Select(x=>x.PromotionId).Distinct().ToArray();
        var titles=await db.Promotions.AsNoTracking().Where(x=>promotionIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.Title,ct);
        rows.AddRange(promotionSales.Select(x=>new CheckoutSaleRow(x.Id,titles.GetValueOrDefault(x.PromotionId,"View & Sale"),
            "VIEW_AND_SALE_PROMOTION",x.PurchaseAmount.Amount,x.CustomerCashbackAmount.Amount,x.PurchaseAmount.Amount,
            x.PlatformRevenueAmount.Amount,x.CreatedAtUtc)));
        var offerSales=await db.UgcCustomerOfferSales.AsNoTracking().Where(x=>x.BusinessId==actor.BusinessId)
            .OrderByDescending(x=>x.CreatedAtUtc).Take(50).ToListAsync(ct);
        var offerIds=offerSales.Select(x=>x.UgcCustomerOfferId).Distinct().ToArray();
        var offers=await db.UgcCustomerOffers.AsNoTracking().Where(x=>offerIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
        var ugcIds=offers.Values.Select(x=>x.UgcOpportunityId).Distinct().ToArray();
        var ugcTitles=await db.UgcOpportunities.AsNoTracking().Where(x=>ugcIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.Title,ct);
        rows.AddRange(offerSales.Select(x=>new CheckoutSaleRow(x.Id,
            offers.TryGetValue(x.UgcCustomerOfferId,out var offer)?offer.CustomerFacingSlogan??ugcTitles.GetValueOrDefault(offer.UgcOpportunityId,"Customer Offer"):"Customer Offer",
            "UGC_CUSTOMER_OFFER",x.PurchaseAmount.Amount,x.CustomerDiscountAmount.Amount,x.CustomerPaysAmount.Amount,
            x.PlatformRevenueAmount.Amount,x.CreatedAtUtc)));
        return rows.OrderByDescending(x=>x.CreatedAtUtc).Take(50).ToArray();
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
            ugcSale.CustomerPaysAmount,ugcSale.CustomerDiscountAmount,"UGC_CUSTOMER_OFFER");
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
                var business=await directory.BusinessCardAsync(offer.Business.Id,ct);
                var content=PublicContentUrl(live.Provider,live.ExternalContentId);
                result.Add(new(a.Id,offer.Source,offer.Offer,new(business.DisplayName,business.DirectionsUrl),
                    new(profile.DisplayName),offer.BenefitPercent,content,offer.Slogan,offer.Location));
            }
            else
            {
                var business=await directory.BusinessCardAsync(offer.Business.Id,ct);
                result.Add(new(offer.OfferId,offer.Source,offer.Offer,
                    new(business.DisplayName,business.DirectionsUrl),null,offer.BenefitPercent,null,offer.Slogan,offer.Location));
            }
        }
        return result;
    }
    public async Task<string> QrStatusAsync(Actor actor,Guid id,CancellationToken ct)
    {
        await Access.EnsureCustomerAsync(actor,ct);var qr=await db.OfferQrSessions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.CustomerId==actor.CustomerId,ct)
            ??throw new ApplicationFailure(FailureKind.NotFound,"Offer QR not found.");
        return qr.Status==OfferQrStatus.Used?"Used":Now>=qr.ExpiresAtUtc?"Expired":"Issued";
    }
    internal static string? PublicContentUrl(string provider,string content) => provider switch
    {
        "TikTok" when content.All(char.IsAsciiDigit)=>"https://www.tiktok.com/@creator/video/"+content,
        "YouTube" when content.All(x=>char.IsAsciiLetterOrDigit(x)||x is '-' or '_')=>"https://www.youtube.com/watch?v="+content,
        "Instagram" when content.All(x=>char.IsAsciiLetterOrDigit(x)||x is '-' or '_')=>"https://www.instagram.com/p/"+content+"/",
        _=>null
    };
}
