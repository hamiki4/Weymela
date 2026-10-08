using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Weymela.Api.Auth;
using Weymela.Api.Security;
using Weymela.Application;
using Weymela.Infrastructure.Identity;

namespace Weymela.Api.Endpoints;

internal static class AccountEndpoints
{
    private static readonly TimeSpan ReauthenticationWindow = TimeSpan.FromMinutes(10);

    public static void MapAccountEndpoints(this WebApplication app, bool development)
    {
        app.MapGet("/api/account/closure", async (HttpContext context, AccountDeletionService service,
            CancellationToken ct) => Results.Ok(await service.MineAsync(EndpointSupport.Actor(context), ct)))
            .RequireAuthorization("Workspace");

        app.MapPost("/api/account/closure", async (OwnRoleClosureInput input, HttpContext context,
            AccountDeletionService service, TrustedIdentityService identities, TimeProvider clock,
            CancellationToken ct) =>
        {
            var actor = EndpointSupport.Actor(context);
            DemandRecentAuthentication(context, clock, development);
            var result = await service.CloseOwnRoleAsync(actor, input, EndpointSupport.Key(context), ct);
            if (result.Status == "PendingIdentityDeletion")
            {
                await context.SignOutAsync(WorkspaceAuthentication.Scheme);
                context.Response.Cookies.Delete(DeviceSessionCredentialCookie.Name(development),
                    DeviceSessionCredentialCookie.DeleteOptions(development));
                return Results.Ok(result);
            }
            if (result.Status != "Closed" || !SelectedRole(actor, result.Role, result.SubjectId))
                return Results.Ok(result);

            var profiles = await identities.ProfilesForUserAsync(actor.UserId, ct);
            var next = profiles.FirstOrDefault()
                ?? throw new ApplicationFailure(FailureKind.Forbidden, "No active account role remains.");
            var principal = WorkspaceAuthentication.Principal(next.Actor, next.DisplayName, next.PublicId);
            var claims = (ClaimsIdentity)principal.Identity!;
            if (TryIdentityContext(context, out var bindingId, out var bindingVersion, out var authenticatedAt))
            {
                claims.AddClaim(new("identity-binding", bindingId.ToString()));
                claims.AddClaim(new("identity-version", bindingVersion.ToString(CultureInfo.InvariantCulture)));
                claims.AddClaim(new("authenticated-at", authenticatedAt.ToString("O", CultureInfo.InvariantCulture)));
                claims.AddClaim(new("auth-strength", "firebase-verified"));
            }
            else if (!development)
                throw new ApplicationFailure(FailureKind.Forbidden, "Sign in again before closing this role.",
                    code: "ReauthenticationRequired");
            var now = clock.GetUtcNow();
            var expires = development ? now.AddHours(1) : new DateTimeOffset(authenticatedAt).AddHours(1);
            await context.SignInAsync(WorkspaceAuthentication.Scheme, principal,
                new AuthenticationProperties { IsPersistent = false, IssuedUtc = now, ExpiresUtc = expires, AllowRefresh = false });
            return Results.Ok(result with { NextRole = next.Actor.Role.ToString() });
        }).RequireAuthorization("Workspace").AddEndpointFilter<ValidatedInputFilter>();
    }

    internal static void DemandRecentAuthentication(HttpContext context, TimeProvider clock, bool development)
    {
        if (development && context.User.FindFirst("identity-binding") is null) return;
        if (context.User.FindFirst("auth-strength")?.Value != "firebase-verified"
            || !TryIdentityContext(context, out _, out _, out var authenticatedAt))
            throw new ApplicationFailure(FailureKind.Forbidden, "Sign in again before closing an account role.",
                code: "ReauthenticationRequired");
        var now = clock.GetUtcNow().UtcDateTime;
        if (authenticatedAt > now.AddMinutes(1) || now - authenticatedAt > ReauthenticationWindow)
            throw new ApplicationFailure(FailureKind.Forbidden, "Sign in again before closing an account role.",
                code: "ReauthenticationRequired");
    }

    private static bool TryIdentityContext(HttpContext context, out Guid bindingId, out long bindingVersion,
        out DateTime authenticatedAt)
    {
        bindingId = Guid.Empty;
        bindingVersion = 0;
        authenticatedAt = default;
        return Guid.TryParse(context.User.FindFirst("identity-binding")?.Value, out bindingId)
            && long.TryParse(context.User.FindFirst("identity-version")?.Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out bindingVersion)
            && DateTime.TryParseExact(context.User.FindFirst("authenticated-at")?.Value, "O",
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out authenticatedAt);
    }

    private static bool SelectedRole(Actor actor, string role, Guid subjectId)
    {
        var selected = actor.Role switch
        {
            ActorRole.Business => actor.BusinessId,
            ActorRole.Creator => actor.CreatorId,
            ActorRole.Customer => actor.CustomerId,
            _ => actor.UserId
        };
        return actor.Role.ToString() == role && selected == subjectId;
    }
}
