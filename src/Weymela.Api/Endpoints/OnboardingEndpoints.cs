using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Weymela.Api.Auth;
using Weymela.Api.Security;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Api.Endpoints;

internal static class OnboardingEndpoints
{
    private sealed record ProfileRequest(string Role, string DisplayName, string? PublicId, string? Region,
        string? Category, string? Submission, Guid? ProposedBusinessId = null,
        AccountLegalConfirmation? AccountLegal = null);
    private sealed record ReviewRequest(bool Approve, string? Reason, long ExpectedVersion);

    public static void MapOnboardingEndpoints(this WebApplication app)
    {
        var account = app.MapGroup("/api/onboarding").RequireAuthorization("VerifiedAccount")
            .AddEndpointFilter<ValidatedInputFilter>();
        account.MapGet("/status", async (HttpContext c, RoleEnrollmentService service, CancellationToken ct) =>
        {
            var userId = UserId(c);
            return Results.Ok(new { profiles = await service.MineAsync(userId, ct) });
        });
        account.MapGet("/legal", async (HttpContext c, AccountLegalOnboardingService service, CancellationToken ct) =>
            Results.Ok(await service.StatusAsync(UserId(c), ct)));
        account.MapPost("/profile", async (ProfileRequest input, HttpContext c, RoleEnrollmentService service,
            WeymelaDbContext db, TrustedIdentityService identities, TimeProvider clock, CancellationToken ct) =>
        {
            var userId = UserId(c);
            if (!Enum.TryParse<ActorRole>(input.Role, true, out var role))
                throw new ApplicationFailure(FailureKind.Validation, "Choose an available profile.");
            var actor = new Actor(userId, ActorRole.Customer, CustomerId: Guid.Empty);
            var result = await service.SubmitAsync(actor,
                new RoleEnrollmentRequest(role, input.DisplayName, input.PublicId, input.Region, input.Category,
                    input.Submission, input.ProposedBusinessId, input.AccountLegal,
                    c.Connection.RemoteIpAddress?.ToString(), c.Request.Headers.UserAgent.ToString()),
                EndpointSupport.Key(c), ct);
            if (role == ActorRole.Customer && result.Status == RoleEnrollmentStatus.Approved)
            {
                if (!Guid.TryParse(c.User.FindFirst("identity-binding")?.Value, out var bindingId)
                    || !long.TryParse(c.User.FindFirst("identity-version")?.Value, out var bindingVersion)
                    || !DateTime.TryParseExact(c.User.FindFirst("authenticated-at")?.Value, "O",
                        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var authenticatedAt))
                    throw new ApplicationFailure(FailureKind.Forbidden, "Your session must be refreshed before opening the Customer workspace.");
                var permission = await db.CommercePermissions.AsNoTracking().SingleAsync(x => x.UserId == userId
                    && x.Role == ActorRole.Customer && x.IsActive, ct);
                var target = await identities.SelectAsync(userId, bindingId, bindingVersion,
                    new ProfileSelection(ActorRole.Customer, permission.SubjectId, null), authenticatedAt,
                    clock.GetUtcNow().UtcDateTime.AddHours(1), ct);
                var principal = WorkspaceAuthentication.Principal(target.Actor, target.DisplayName, target.PublicId);
                var claims = (ClaimsIdentity)principal.Identity!;
                claims.AddClaim(new("identity-binding", target.BindingId.ToString()));
                claims.AddClaim(new("identity-version", target.BindingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                claims.AddClaim(new("authenticated-at", target.AuthenticatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
                claims.AddClaim(new("auth-strength", "firebase-verified"));
                var now = clock.GetUtcNow();
                await c.SignInAsync(WorkspaceAuthentication.Scheme, principal, new AuthenticationProperties
                {
                    IsPersistent = false, IssuedUtc = now, ExpiresUtc = now.AddHours(1), AllowRefresh = false
                });
            }
            return Results.Ok(result);
        });

        var admin = app.MapGroup("/api/admin/role-enrollments").RequireAuthorization("PlatformAdmin")
            .AddEndpointFilter<ValidatedInputFilter>();
        admin.MapGet("", (RoleEnrollmentService service, CancellationToken ct) => service.PendingAsync(ct));
        admin.MapPost("/{id:guid}/review", async (Guid id, ReviewRequest input, HttpContext c, RoleEnrollmentService service, CancellationToken ct) =>
            Results.Ok(await service.ReviewAsync(EndpointSupport.Actor(c), id, input.Approve, input.Reason, input.ExpectedVersion, EndpointSupport.Key(c), ct)));
    }

    private static Guid UserId(HttpContext c)
        => Guid.TryParse(c.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty
            ? id
            : throw new ApplicationFailure(FailureKind.Forbidden, "Sign in to your account.");
}
