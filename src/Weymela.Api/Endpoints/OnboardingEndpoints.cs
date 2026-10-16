using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Weymela.Api.Auth;
using Weymela.Api.Security;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Operations;
using Weymela.Domain;

namespace Weymela.Api.Endpoints;

internal static class OnboardingEndpoints
{
    private sealed record ProfileRequest(string Role, string DisplayName, string? PublicId, string? Region,
        string? Category, string? Submission, Guid? ProposedBusinessId = null,
        AccountLegalConfirmation? AccountLegal = null,
        IReadOnlyList<CreatorApplicationSocialProfile>? SocialProfiles = null,
        string? LegalName = null, string? RegisteredPhone = null);
    private sealed record ReviewRequest(bool Approve, string? Reason, long ExpectedVersion);
    private sealed record AccountActivationInput(string ActivationSecret, AccountLegalConfirmation? AccountLegal = null);

    public static void MapOnboardingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/legal/account/status", async (string? language, AccountLegalOnboardingService service,
            CancellationToken ct) => Results.Ok(await service.PublicationAsync(ct, language ?? "en"))).AllowAnonymous();
        app.MapGet("/api/legal/account/{kind}", async (string kind, string? language,
            AccountLegalDocumentSource source, WeymelaDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var type = kind switch { "terms" => LegalDocumentType.TermsOfService,
                "privacy" => LegalDocumentType.PrivacyPolicy, _ => (LegalDocumentType?)null };
            if (type is null || language is not (null or "en" or "am")) return Results.NotFound();
            var content = await source.ReadAsync(db, type.Value, language ?? "en", clock.GetUtcNow().UtcDateTime, ct);
            return content is null
                ? Results.Json(new { code = "ApprovedLegalUnavailable", message = "The approved legal document is not available yet." }, statusCode: 503)
                : Results.Ok(content);
        }).AllowAnonymous();
        var account = app.MapGroup("/api/onboarding").RequireAuthorization("VerifiedAccount")
            .AddEndpointFilter<ValidatedInputFilter>();
        account.MapGet("/status", async (HttpContext c, RoleEnrollmentService service, CancellationToken ct) =>
        {
            var userId = UserId(c);
            return Results.Ok(new { profiles = await service.MineAsync(userId, ct) });
        });
        account.MapGet("/legal", async (string? language, HttpContext c, AccountLegalOnboardingService service, CancellationToken ct) =>
            Results.Ok(await service.StatusAsync(UserId(c), ct, language ?? "en")));
        account.MapPost("/account-preauthorizations/{id:guid}/activate", async (Guid id, AccountActivationInput input,
            HttpContext c, PlatformAdminAccountService service, CancellationToken ct) =>
            Results.Ok(await service.ActivateAsync(EndpointSupport.Actor(c), id, input.ActivationSecret, ct,
                input.AccountLegal, c.Connection.RemoteIpAddress?.ToString(), c.Request.Headers.UserAgent.ToString())));
        account.MapGet("/identity", async (HttpContext c, RoleEnrollmentService service, CancellationToken ct) =>
            Results.Ok(await service.IdentityAsync(UserId(c), ct)));
        MapProfile("/registration", ProfileEnrollmentIntent.FirstProfile);
        MapProfile("/profiles", ProfileEnrollmentIntent.AdditionalProfile);
        // VersionCode 11 and older web sessions use this existing contract.
        MapProfile("/profile", ProfileEnrollmentIntent.Compatible);

        void MapProfile(string path, ProfileEnrollmentIntent intent) => account.MapPost(path,
            async (ProfileRequest input, HttpContext c, RoleEnrollmentService service,
                WeymelaDbContext db, TrustedIdentityService identities, TimeProvider clock, CancellationToken ct) =>
        {
            var userId = UserId(c);
            if (!Enum.TryParse<ActorRole>(input.Role, true, out var role))
                throw new ApplicationFailure(FailureKind.Validation, "Choose an available profile.");
            var actor = new Actor(userId, ActorRole.Customer, CustomerId: Guid.Empty);
            var result = await service.SubmitAsync(actor,
                new RoleEnrollmentRequest(role, input.DisplayName, role == ActorRole.Customer ? input.PublicId : null, input.Region, input.Category,
                    input.Submission, input.ProposedBusinessId, input.AccountLegal,
                    c.Connection.RemoteIpAddress?.ToString(), c.Request.Headers.UserAgent.ToString(), input.SocialProfiles,
                    input.LegalName, intent == ProfileEnrollmentIntent.AdditionalProfile ? null : input.RegisteredPhone),
                EndpointSupport.Key(c), ct, intent);
            if (role is ActorRole.Customer or ActorRole.Business && result.Status == RoleEnrollmentStatus.Approved)
            {
                // Real verified identities carry binding claims and can be moved
                // directly into the newly activated workspace. Synthetic Development
                // personas intentionally have no trusted binding and only exercise the
                // persisted profile list.
                if (Guid.TryParse(c.User.FindFirst("identity-binding")?.Value, out var bindingId)
                    && long.TryParse(c.User.FindFirst("identity-version")?.Value, out var bindingVersion)
                    && DateTime.TryParseExact(c.User.FindFirst("authenticated-at")?.Value, "O",
                        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var authenticatedAt))
                {
                    var permission = await db.CommercePermissions.AsNoTracking().SingleAsync(x => x.UserId == userId
                        && x.Role == role && x.IsActive, ct);
                    var target = await identities.SelectAsync(userId, bindingId, bindingVersion,
                        new ProfileSelection(role, permission.SubjectId, permission.BusinessId), authenticatedAt,
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
            }
            return Results.Ok(result);
        });

        var admin = app.MapGroup("/api/admin/role-enrollments").RequireAuthorization("AdminOperations")
            .AddEndpointFilter<ValidatedInputFilter>();
        admin.MapGet("", (HttpContext c, RoleEnrollmentService service, CancellationToken ct) => service.PendingAsync(EndpointSupport.Actor(c), ct, EndpointSupport.ListFilter(c)));
        admin.MapPost("/{id:guid}/review", async (Guid id, ReviewRequest input, HttpContext c, RoleEnrollmentService service, CancellationToken ct) =>
            Results.Ok(await service.ReviewAsync(EndpointSupport.Actor(c), id, input.Approve, input.Reason, input.ExpectedVersion, EndpointSupport.Key(c), ct)));
    }

    private static Guid UserId(HttpContext c)
        => Guid.TryParse(c.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty
            ? id
            : throw new ApplicationFailure(FailureKind.Forbidden, "Sign in to your account.");
}
