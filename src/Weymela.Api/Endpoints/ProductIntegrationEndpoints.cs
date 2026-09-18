using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Weymela.Api.Auth;
using Weymela.Application;
using Weymela.Infrastructure.Identity;

namespace Weymela.Api.Endpoints;

internal static class ProductIntegrationEndpoints
{
    private sealed record HandoffRequest(string Role, string Purpose, string CallbackId, string State);
    private sealed record RedeemRequest(string Code, string CallbackId);
    private sealed record LegalAcceptanceRequest(AccountLegalConfirmation Confirmation);

    public static void MapProductIntegrationEndpoints(this WebApplication app)
    {
        var browser = app.MapGroup("/api/integration/product")
            .RequireAuthorization("VerifiedAccount");
        browser.MapGet("/configuration", (ProductIntegrationService service) => Results.Ok(service.PublicConfiguration()));
        browser.MapPost("/handoff", async (HandoffRequest input, HttpContext context,
            ProductIntegrationService service, CancellationToken ct) =>
        {
            if (!DeviceSessionPrincipal.TryGet(context.User, out var identity)
                || !Enum.TryParse<ActorRole>(input.Role, true, out var role))
                throw new ApplicationFailure(FailureKind.Forbidden, "The product handoff is not authorized.");
            var actor = EndpointSupport.Actor(context);
            Guid? subject = input.Purpose.Equals(ProductHandoffPurposes.ExistingWorkspace,
                StringComparison.OrdinalIgnoreCase) ? Subject(actor) : null;
            Guid? business = input.Purpose.Equals(ProductHandoffPurposes.ExistingWorkspace,
                StringComparison.OrdinalIgnoreCase) ? actor.BusinessId : null;
            if (input.Purpose.Equals(ProductHandoffPurposes.ExistingWorkspace, StringComparison.OrdinalIgnoreCase)
                && actor.Role != role)
                throw new ApplicationFailure(FailureKind.Forbidden, "Choose this profile before opening its workspace.");
            var correlation = Guid.TryParse(context.TraceIdentifier, out var parsed) ? parsed : Guid.NewGuid();
            var result = await service.IssueAsync(new(identity.UserId, identity.IdentityBindingId,
                identity.IdentityVersion, AuthenticatedAt(context.User), role, input.Purpose,
                input.CallbackId, subject, business, correlation), input.State, ct);
            return Results.Ok(result);
        });
        browser.MapPost("/legal-acceptance", async (LegalAcceptanceRequest input, HttpContext context,
            AccountLegalOnboardingService legal, CancellationToken ct) =>
        {
            var userId = UserId(context.User);
            await legal.AcceptCurrentAsync(userId, input.Confirmation,
                context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString(), ct);
            return Results.NoContent();
        });

        var server = app.MapGroup("/api/integration/product/server").AllowAnonymous();
        server.MapPost("/redeem", async (RedeemRequest input, HttpContext context,
            ProductIntegrationService service, CancellationToken ct) =>
        {
            AuthorizeServer(context, service);
            return Results.Ok(await service.RedeemAsync(input.Code, input.CallbackId, ct));
        });
        server.MapPost("/authority", async (ProductAuthorityRequest input, HttpContext context,
            ProductIntegrationService service, CancellationToken ct) =>
        {
            AuthorizeServer(context, service);
            return Results.Ok(await service.AuthorityAsync(input, ct));
        });
        server.MapPost("/profiles/synchronize", async (ProductProfileSynchronizationRequest input,
            HttpContext context, ProductIntegrationService service, CancellationToken ct) =>
        {
            AuthorizeServer(context, service);
            return Results.Ok(await service.SynchronizeProfileAsync(input, ct));
        });
    }

    private static void AuthorizeServer(HttpContext context, ProductIntegrationService service)
    {
        if (!service.AuthenticateClient(context.Request.Headers["X-Weymela-Integration-Client"].ToString(),
            context.Request.Headers["X-Weymela-Integration-Secret"].ToString()))
            throw new ApplicationFailure(FailureKind.Forbidden, "The product integration client is not authorized.");
    }

    private static Guid UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var value) && value != Guid.Empty
            ? value : throw new ApplicationFailure(FailureKind.Forbidden, "Sign in to your account.");
    private static DateTime AuthenticatedAt(ClaimsPrincipal principal) =>
        DateTime.TryParseExact(principal.FindFirst("authenticated-at")?.Value, "O", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var value) ? value
            : throw new ApplicationFailure(FailureKind.Forbidden, "Complete account authentication to continue.");
    private static Guid Subject(Actor actor) => actor.Role switch
    {
        ActorRole.Business => actor.BusinessId ?? Guid.Empty,
        ActorRole.Creator => actor.CreatorId ?? Guid.Empty,
        ActorRole.Customer => actor.CustomerId ?? Guid.Empty,
        ActorRole.PlatformAdmin or ActorRole.OperationsAdmin => actor.UserId,
        _ => Guid.Empty
    };
}
