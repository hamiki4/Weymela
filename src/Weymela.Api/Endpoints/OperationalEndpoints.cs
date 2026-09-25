using Microsoft.EntityFrameworkCore;
using Weymela.Api.Security;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Deposits;
using Weymela.Infrastructure.Notifications;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Api.Endpoints;

internal static class OperationalEndpoints
{
    private sealed record LegalConsent(string ContentHash, bool Confirmed);
    public static void MapOperationalEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live",()=>Results.Ok(new{status="live"})).AllowAnonymous();
        app.MapGet("/health/ready",async(HttpContext context,OperationalHealth health,CancellationToken ct)=>
        {context.Response.Headers.CacheControl="no-store";var result=await health.ReadinessAsync(ct);return Results.Json(new{status=result.Status},statusCode:result.Ready?200:503);}).AllowAnonymous();
        var workspace=app.MapGroup("/api").RequireAuthorization("Workspace").AddEndpointFilter<ValidatedInputFilter>();
        workspace.MapGet("/notifications",(HttpContext c,NotificationService service,CancellationToken ct)=>service.GetAsync(EndpointSupport.Actor(c),ct));
        workspace.MapPost("/notifications/{id:guid}/read",async(Guid id,HttpContext c,NotificationService service,CancellationToken ct)=>
        {await service.ReadAsync(EndpointSupport.Actor(c),id,ct);return Results.NoContent();});
        workspace.MapPost("/notifications/read-all",async(HttpContext c,NotificationService service,CancellationToken ct)=>
        {await service.ReadAllAsync(EndpointSupport.Actor(c),ct);return Results.NoContent();});
        workspace.MapGet("/legal/current",(HttpContext c,LegalWorkspaceService service,CancellationToken ct)=>service.CurrentAsync(EndpointSupport.Actor(c),ct));
        workspace.MapPost("/legal/{id:guid}/accept",async(Guid id,LegalConsent consent,HttpContext c,LegalWorkspaceService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.AcceptAsync(EndpointSupport.Actor(c),id,consent.ContentHash,consent.Confirmed,ct)));
        var business=app.MapGroup("/api/business").RequireAuthorization("Business").AddEndpointFilter<ValidatedInputFilter>();
        business.MapGet("/deposit-method",(RuntimeOptions options)=>Results.Ok(new{mode=options.DevelopmentIdentity?"Development":options.DepositMode}));
        business.MapGet("/deposit-requests",(HttpContext c,DepositService service,CancellationToken ct)=>service.OwnAsync(EndpointSupport.Actor(c),ct));
        business.MapPost("/deposit-requests",(DepositSubmission input,HttpContext c,DepositService service,CancellationToken ct)=>service.SubmitAsync(EndpointSupport.Actor(c),input,EndpointSupport.Key(c),ct));
        var admin=app.MapGroup("/api/admin").RequireAuthorization("AdminOperations").AddEndpointFilter<ValidatedInputFilter>();
        admin.MapGet("/operations",async(HttpContext c,OperationalHealth health,CancellationToken ct)=>
        {
            var actor=EndpointSupport.Actor(c);
            return actor.Role==ActorRole.OperationsAdmin
                ? Results.Ok(await health.OperationsDetailsAsync(ct))
                : Results.Ok(await health.DetailsAsync(ct));
        });
        admin.MapGet("/reconciliation",(HttpContext c,ReconciliationService service,CancellationToken ct)=>service.CheckAsync(EndpointSupport.Actor(c),ct)).RequireAuthorization("PlatformAdmin");
        admin.MapGet("/deposit-requests",async(WeymelaDbContext db,CancellationToken ct)=>
            await db.DepositRequests.AsNoTracking().OrderBy(x=>x.Status).ThenBy(x=>x.SubmittedAtUtc).Take(100)
                .Select(x=>new{x.Id,x.BusinessId,amount=x.Amount.Amount,status=x.Status.ToString(),x.Provider,x.ExternalReference,x.ProofReference,x.SubmittedAtUtc,x.ReviewedAtUtc,x.Version}).ToListAsync(ct));
        admin.MapPost("/deposit-requests/{id:guid}/review",async(Guid id,DepositReview input,HttpContext c,FinancialCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.ReviewDepositAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        app.MapPost("/api/checkout/manual-lookup",()=>Results.Json(new{code="Disabled",message="Manual identity lookup is not enabled. Use the Customer's Offer QR."},statusCode:503)).RequireAuthorization("Checkout");
    }
}
