using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;

namespace Weymela.Infrastructure.Web;

public sealed partial class WorkspaceQueries
{
    public async Task<SaleResult> CheckoutResultAsync(Actor actor,Guid saleId,CancellationToken ct)
    {
        if(actor.BusinessId is null)throw new ApplicationFailure(FailureKind.Forbidden,"Checkout permission is required.");
        await Access.EnsureScannerAsync(actor,actor.BusinessId.Value,ct);
        // The first HTTP response and retry both use committed PostgreSQL precision, not an unrounded tracked object.
        var sale=await db.VerifiedSales.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==saleId&&x.BusinessId==actor.BusinessId,ct)
            ??throw new ApplicationFailure(FailureKind.NotFound,"Confirmed purchase not found.");
        return new(sale.Id,sale.PurchaseAmount,sale.TotalPromotionCharge,sale.CreatedAtUtc);
    }
    public async Task<IReadOnlyList<CustomerOfferCard>> OffersAsync(Actor actor,CancellationToken ct)
    {
        var offers=await Financial.CustomerOffersAsync(actor,ct);var result=new List<CustomerOfferCard>();
        foreach(var offer in offers)
        {
            var a=await db.CreatorAllocations.AsNoTracking().SingleAsync(x=>x.Id==offer.AllocationId,ct);
            var live=await db.CreatorPromotionParticipations.AsNoTracking().SingleAsync(x=>x.CreatorAllocationId==a.Id,ct);
            var profile=await directory.CreatorCardAsync(a.CreatorId,ct);
            var content=PublicContentUrl(live.Provider,live.ExternalContentId);
            result.Add(new(a.Id,a.PromotionId,offer.Campaign,await directory.BusinessCardAsync(offer.Business.Id,ct),profile,offer.CashbackPercent,content));
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
