using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Web;

namespace Weymela.Api.Endpoints;

internal static class CommerceEndpoints
{
    public static void MapCommerceEndpoints(this WebApplication app)
    {
        var customer=app.MapGroup("/api/customer").RequireAuthorization("Customer").AddEndpointFilter<Weymela.Api.Security.ValidatedInputFilter>();
        customer.MapGet("/offers",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.OffersAsync(EndpointSupport.Actor(c),ct));
        customer.MapGet("/history",(HttpContext c,FinancialQueries q,CancellationToken ct)=>q.CustomerHistoryAsync(EndpointSupport.Actor(c),ct));
        customer.MapPost("/offers/{id:guid}/qr",async(Guid id,HttpContext c,CheckoutService service,CancellationToken ct)=>
        {
            var qr=await service.IssueCustomerOfferAsync(EndpointSupport.Actor(c),id,EndpointSupport.Key(c),ct);
            // This is the only transport projection that exposes the one-time token to its issuing Customer.
            return Results.Ok(new QrResponse(qr.SessionId,qr.Token?.Value,qr.ExpiresAtUtc,qr.Replayed));
        });
        customer.MapGet("/qr/{id:guid}",async(Guid id,HttpContext c,WorkspaceQueries q,CancellationToken ct)=>Results.Ok(new{status=await q.QrStatusAsync(EndpointSupport.Actor(c),id,ct)}));
        var checkout=app.MapGroup("/api/checkout").RequireAuthorization("Checkout").AddEndpointFilter<Weymela.Api.Security.ValidatedInputFilter>();
        checkout.MapGet("/recent",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.RecentSalesAsync(EndpointSupport.Actor(c),ct));
        checkout.MapPost("/resolve",async(TokenInput input,HttpContext c,CheckoutService service,IWorkspaceDirectory directory,WeymelaDbContext db,CancellationToken ct)=>
        {
            var offer=await service.ResolveAsync(EndpointSupport.Actor(c),new SensitiveQrToken(input.Token),directory,ct);
            var session=await db.OfferQrSessions.AsNoTracking().SingleAsync(x=>x.Id==offer.SessionId,ct);
            var publicCustomer=await directory.CustomerCardAsync(session.CustomerId,ct);
            return Results.Ok(new CheckoutOffer(offer.SessionId,offer.Offer,new(offer.Business.DisplayName,null),
                offer.Creator is null?null:new CustomerOfferCreator(offer.Creator.DisplayName),
                publicCustomer.DisplayName,offer.ExpiresAtUtc,
                offer.Source,offer.CustomerDiscountPercent));
        });
        checkout.MapPost("/confirm",async(CheckoutInput input,HttpContext c,CheckoutService service,WorkspaceQueries queries,CancellationToken ct)=>
        {
            var actor=EndpointSupport.Actor(c);
            var result=await service.RedeemAsync(new(actor,new SensitiveQrToken(input.Token),new Money(input.PurchaseAmount),EndpointSupport.Key(c)),ct);
            return await queries.CheckoutResultAsync(actor,result.SaleId,ct);
        });
    }
}
