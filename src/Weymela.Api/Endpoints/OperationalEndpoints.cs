using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Weymela.Api.Security;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
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
        workspace.MapGet("/profile", async (HttpContext c, WeymelaDbContext db, RuntimeOptions options, IWorkspaceDirectory directory, CancellationToken ct) =>
        {
            var actor = EndpointSupport.Actor(c);
            var subjectId = actor.Role switch
            {
                ActorRole.Customer => actor.CustomerId,
                ActorRole.Creator => actor.CreatorId,
                ActorRole.Business => actor.BusinessId,
                _ => null
            };
            if (subjectId is null) throw new ApplicationFailure(FailureKind.Forbidden, "A Customer, Creator or Business profile is required.");
            var profile = await db.PublicWorkspaceProfiles.AsNoTracking()
                .SingleOrDefaultAsync(x => x.SubjectId == subjectId && x.Role == actor.Role, ct);
            if ((profile is null && !options.DevelopmentIdentity) || !await db.CommercePermissions.AsNoTracking().AnyAsync(x =>
                    x.UserId == actor.UserId && x.Role == actor.Role && x.SubjectId == subjectId && x.IsActive, ct))
                throw new ApplicationFailure(FailureKind.Forbidden, "This profile is unavailable.");
            var fixtureRegion = profile is null && actor.Role == ActorRole.Business
                ? (await directory.BusinessCardAsync(subjectId.Value, ct)).Region : null;
            var identifiers = await db.AuthIdentifiers.AsNoTracking()
                .Where(x => x.UserId == actor.UserId && x.IsVerified && x.DeliveryAddress != null
                    && (x.Kind == "Email" || x.Kind == "Phone"))
                .Select(x => new { x.Kind, x.DeliveryAddress }).ToListAsync(ct);
            return Results.Ok(new
            {
                role = actor.Role.ToString(),
                displayName = profile?.DisplayName ?? c.User.Identity?.Name,
                publicId = profile?.PublicId ?? c.User.FindFirst("publicId")?.Value,
                creatorId = actor.Role == ActorRole.Creator ? profile?.CreatorNumber : null,
                hasCreatorPhoto = actor.Role == ActorRole.Creator && profile?.CreatorPhotoKey is not null,
                email = identifiers.FirstOrDefault(x => x.Kind == "Email")?.DeliveryAddress,
                phone = identifiers.FirstOrDefault(x => x.Kind == "Phone")?.DeliveryAddress,
                status = "Active",
                region = actor.Role == ActorRole.Business && !string.IsNullOrWhiteSpace(profile?.Region) ? profile.Region : fixtureRegion,
                businessType = actor.Role == ActorRole.Business && !string.IsNullOrWhiteSpace(profile?.Category) ? profile.Category : null
            });
        });
        workspace.MapGet("/notifications",(HttpContext c,NotificationService service,CancellationToken ct)=>service.GetAsync(EndpointSupport.Actor(c),ct));
        workspace.MapPost("/notifications/{id:guid}/read",async(Guid id,HttpContext c,NotificationService service,CancellationToken ct)=>
        {await service.ReadAsync(EndpointSupport.Actor(c),id,ct);return Results.NoContent();});
        workspace.MapPost("/notifications/read-all",async(HttpContext c,NotificationService service,CancellationToken ct)=>
        {await service.ReadAllAsync(EndpointSupport.Actor(c),ct);return Results.NoContent();});
        workspace.MapGet("/legal/current",(HttpContext c,LegalWorkspaceService service,CancellationToken ct)=>service.CurrentAsync(EndpointSupport.Actor(c),ct));
        workspace.MapGet("/legal/{id:guid}/content",async(Guid id,HttpContext c,LegalWorkspaceService service,CancellationToken ct)=>
        {
            var content=await service.CurrentContentAsync(EndpointSupport.Actor(c),id,ct);
            c.Response.Headers.CacheControl="no-store";
            return content is null ? Results.Json(new{message="Approved Business document content is unavailable."},statusCode:503) : Results.Ok(content);
        });
        workspace.MapPost("/legal/{id:guid}/accept",async(Guid id,LegalConsent consent,HttpContext c,LegalWorkspaceService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.AcceptAsync(EndpointSupport.Actor(c),id,consent.ContentHash,consent.Confirmed,ct)));
        var business=app.MapGroup("/api/business").RequireAuthorization("Business").AddEndpointFilter<ValidatedInputFilter>();
        business.MapGet("/deposit-method",(RuntimeOptions options)=>Results.Ok(new{mode=options.DevelopmentIdentity?"Development":options.DepositMode}));
        business.MapGet("/deposit-requests",(HttpContext c,DepositService service,CancellationToken ct)=>service.OwnAsync(EndpointSupport.Actor(c),ct));
        business.MapPost("/deposit-requests",async(HttpContext c,WeymelaDbContext db,PrivateReceiptStore receipts,DepositService service,CancellationToken ct)=>
        {
            var actor=EndpointSupport.Actor(c);
            if(actor.BusinessId is null || !await db.CommercePermissions.AnyAsync(x=>x.UserId==actor.UserId&&x.Role==ActorRole.Business&&x.SubjectId==actor.BusinessId&&x.IsActive,ct))
                throw new ApplicationFailure(FailureKind.Forbidden,"Business access is required.");
            if(!c.Request.HasFormContentType) throw new ApplicationFailure(FailureKind.Validation,"Choose a payment receipt.");
            var form=await c.Request.ReadFormAsync(ct);
            if(form.Count!=1||form["amount"].Count!=1||form.Files.Count!=1||form.Files[0].Name!="receipt"
                ||!decimal.TryParse(form["amount"],NumberStyles.Number,CultureInfo.InvariantCulture,out var amount))
                throw new ApplicationFailure(FailureKind.Validation,"Enter an amount and choose a payment receipt.");
            var key=EndpointSupport.Key(c);
            var proof=await receipts.SaveAsync(actor.BusinessId.Value,key,form.Files[0],ct);
            return await service.SubmitAsync(actor,new DepositSubmission(amount,"R"+proof,proof),key,ct);
        });
        business.MapGet("/deposit-requests/{id:guid}/receipt",async(Guid id,HttpContext c,WeymelaDbContext db,PrivateReceiptStore receipts,CancellationToken ct)=>
        {
            var actor=EndpointSupport.Actor(c);
            var request=await db.DepositRequests.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.BusinessId==actor.BusinessId,ct);
            if(request?.ProofReference is null) throw new ApplicationFailure(FailureKind.NotFound,"Receipt not found.");
            return await ReceiptResult(c,receipts,request.ProofReference,ct);
        });
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
        {
            var rows=await db.DepositRequests.AsNoTracking().OrderBy(x=>x.Status).ThenBy(x=>x.SubmittedAtUtc).Take(100).ToListAsync(ct);
            var ids=rows.Select(x=>x.BusinessId).Distinct().ToArray();
            var names=await db.PublicWorkspaceProfiles.AsNoTracking().Where(x=>x.Role==ActorRole.Business&&ids.Contains(x.SubjectId))
                .ToDictionaryAsync(x=>x.SubjectId,x=>x.DisplayName,ct);
            return rows.Select(x=>new{x.Id,x.BusinessId,business=names.GetValueOrDefault(x.BusinessId,"Business"),amount=x.Amount.Amount,
                status=x.Status.ToString(),hasReceipt=x.ProofReference!=null,x.SubmittedAtUtc,x.ReviewedAtUtc,x.Version});
        });
        admin.MapGet("/deposit-requests/{id:guid}/receipt",async(Guid id,HttpContext c,WeymelaDbContext db,PrivateReceiptStore receipts,CancellationToken ct)=>
        {
            var actor=EndpointSupport.Actor(c);
            if(!AdministrativeAuthority.For(new RealActor(actor)).Allows(AdministrativeCapability.DepositReview)
                ||!await db.CommercePermissions.AnyAsync(x=>x.UserId==actor.UserId&&x.Role==actor.Role&&x.IsActive,ct))
                throw new ApplicationFailure(FailureKind.Forbidden,"Deposit review access is required.");
            var request=await db.DepositRequests.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(request?.ProofReference is null) throw new ApplicationFailure(FailureKind.NotFound,"Receipt not found.");
            return await ReceiptResult(c,receipts,request.ProofReference,ct);
        });
        admin.MapPost("/deposit-requests/{id:guid}/review",async(Guid id,DepositReview input,HttpContext c,FinancialCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.ReviewDepositAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        app.MapPost("/api/checkout/manual-lookup",()=>Results.Json(new{code="Disabled",message="Manual identity lookup is not enabled. Use the Customer's Offer QR."},statusCode:503)).RequireAuthorization("Checkout");
    }
    private static async Task<IResult> ReceiptResult(HttpContext context,PrivateReceiptStore receipts,string proof,CancellationToken ct)
    {
        var (bytes,contentType)=await receipts.ReadAsync(proof,ct);
        context.Response.Headers.CacheControl="private,no-store";
        context.Response.Headers.ContentDisposition="inline; filename=receipt."+(contentType==PrivateReceiptStore.Jpeg?"jpg":"png");
        return Results.File(bytes,contentType);
    }
}
