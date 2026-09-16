using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Weymela.Api.Auth;
using Weymela.Application.Web;
using Weymela.Application;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Identity;
using Weymela.Api.Security;
using System.Security.Claims;
using Weymela.Application.Operations;

namespace Weymela.Api.Endpoints;

internal static class AuthEndpoints
{
    private sealed record DevelopmentSignIn(string Alias,string AccessKey);
    private sealed record FirebaseSignIn(string IdToken, string? ProfileRole = null, Guid? ProfileSubjectId = null, Guid? ProfileBusinessId = null);
    private sealed record ProfileSwitchInput(string Role, Guid SubjectId, Guid? BusinessId);
    private sealed record EmailCodeStart(string Identifier, string? Phone, string Purpose);
    private sealed record EmailCodeVerify(string Identifier, string Purpose, string Code);
    private sealed record PhoneAliasInput(string Phone);
    private sealed record PasswordSignInInput(string Phone, string Password);
    private sealed record PasswordCredentialInput(string? Phone, string Password, string ConfirmPassword);
    private sealed record PasswordRecoveryVerifyInput(string Email, string Code);
    private sealed record PasswordResetInput(string Email, string RecoveryGrant, string NewPassword, string ConfirmPassword);
    public static void MapAuthEndpoints(this WebApplication app,bool development)
    {
        app.MapGet("/api/auth/mode",()=>Results.Ok(new{development,personas=development?new DevelopmentDirectory().Personas.Select(x=>new{x.Alias,x.Name,role=x.Actor.Role.ToString()}):null})).AllowAnonymous();
        app.MapGet("/api/session",async(HttpContext c,WeymelaDbContext db,CancellationToken ct)=>
        {
            if (!Guid.TryParse(c.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                throw new ApplicationFailure(FailureKind.Forbidden, "Sign in to your account.");
            var onboarding = c.User.FindFirst("onboarding") is not null;
            var a = onboarding ? new Actor(userId, ActorRole.Customer, CustomerId: Guid.Empty) : EndpointSupport.Actor(c);
            var checkout=onboarding ? false : await db.CommercePermissions.AnyAsync(x=>x.UserId==a.UserId&&x.Role==a.Role&&x.IsActive&&x.CanCheckout,ct);
            var profiles = onboarding
                ? new List<SessionProfile>()
                // Development fixture sessions have no Firebase verification claim and
                // retain their synthetic persona projection. BrowserHost Firebase
                // identities are verified and must resolve persisted memberships even
                // while the host runs in Development.
                : development && c.User.FindFirst("auth-strength")?.Value != "firebase-verified"
                    ? new List<SessionProfile> { new(a.Role.ToString(), SubjectId(a), a.BusinessId, c.User.Identity!.Name!, c.User.FindFirst("publicId")?.Value??"", checkout) }
                    : (await c.RequestServices.GetRequiredService<TrustedIdentityService>().ProfilesForUserAsync(a.UserId, ct)).Select(ToSessionProfile).ToList();
            var role = onboarding ? "Onboarding" : a.Role.ToString();
            return Results.Ok(new SessionUser(role,onboarding ? "Account setup" : c.User.Identity!.Name!,c.User.FindFirst("publicId")?.Value??"",development,checkout,
                profiles, c.User.FindFirst("profile-key")?.Value));
        }).RequireAuthorization("VerifiedAccount");
        app.MapPost("/api/session/switch-profile", async (ProfileSwitchInput input, HttpContext c, WeymelaDbContext db,
            TrustedIdentityService identities, TimeProvider clock, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ActorRole>(input.Role, true, out var role) || !Enum.IsDefined(role))
                throw new ApplicationFailure(FailureKind.Validation, "Choose an approved profile.");
            if (!Guid.TryParse(c.User.FindFirst("identity-binding")?.Value, out var bindingId)
                || !long.TryParse(c.User.FindFirst("identity-version")?.Value, out var bindingVersion)
                || !DateTime.TryParseExact(c.User.FindFirst("authenticated-at")?.Value, "O", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var authenticatedAt))
                throw new ApplicationFailure(FailureKind.Forbidden, "Your session must be refreshed before switching profiles.");
            var current = EndpointSupport.Actor(c);
            var target = await identities.SelectAsync(current.UserId, bindingId, bindingVersion,
                new ProfileSelection(role, input.SubjectId, input.BusinessId), authenticatedAt,
                clock.GetUtcNow().UtcDateTime.AddHours(1), ct);
            if (target.Actor.Role == ActorRole.PlatformAdmin && c.User.FindFirst("auth-strength")?.Value != "firebase-verified")
                throw new ApplicationFailure(FailureKind.Forbidden, "Additional verification is required for this profile.");
            var correlation = Guid.TryParse(c.Request.Headers["X-Correlation-ID"], out var supplied) ? supplied : Guid.NewGuid();
            db.AuditEvents.Add(new(Guid.NewGuid(), "ProfileSwitched", current.UserId, target.Actor.BusinessId, null,
                target.Actor.CreatorId, correlation, clock.GetUtcNow().UtcDateTime,
                $"from={WorkspaceAuthentication.ProfileKey(c.User)};to={TrustedIdentityService.WorkspaceProfileKey(target.Actor)}"));
            await db.SaveChangesAsync(ct);
            var principal=WorkspaceAuthentication.Principal(target.Actor,target.DisplayName,target.PublicId);
            var claims=(ClaimsIdentity)principal.Identity!;
            claims.AddClaim(new("identity-binding",target.BindingId.ToString()));
            claims.AddClaim(new("identity-version",target.BindingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            claims.AddClaim(new("authenticated-at",target.AuthenticatedAtUtc.ToString("O",System.Globalization.CultureInfo.InvariantCulture)));
            claims.AddClaim(new("auth-strength", "firebase-verified"));
            var now=clock.GetUtcNow();
            await c.SignInAsync(WorkspaceAuthentication.Scheme,principal,new AuthenticationProperties{IsPersistent=false,IssuedUtc=now,ExpiresUtc=now.AddHours(1),AllowRefresh=false});
            var selectedProfile = target.Profiles.Single(x => x.Key == TrustedIdentityService.WorkspaceProfileKey(target.Actor));
            return Results.Ok(new SessionUser(target.Actor.Role.ToString(), target.DisplayName, target.PublicId, development,
                selectedProfile.CanCheckout, target.Profiles.Select(ToSessionProfile).ToList(), selectedProfile.Key));
        }).RequireAuthorization("Workspace").AddEndpointFilter<ValidatedInputFilter>();
        app.MapPost("/api/session/sign-out",async(HttpContext c)=>
        {
            await c.SignOutAsync(WorkspaceAuthentication.Scheme);
            c.Response.Cookies.Delete(DeviceSessionCredentialCookie.Name(development),
                DeviceSessionCredentialCookie.DeleteOptions(development));
            return Results.NoContent();
        }).RequireAuthorization("VerifiedAccount");
        app.MapPost("/api/auth/firebase/session",async(FirebaseSignIn input,HttpContext c,TrustedIdentityService identities,
            DeviceSessionService deviceSessions,TimeProvider clock,CancellationToken ct)=>
        {
            ProfileSelection? selection = null;
            if (input.ProfileRole is not null || input.ProfileSubjectId is not null || input.ProfileBusinessId is not null)
            {
                if (!Enum.TryParse<ActorRole>(input.ProfileRole, true, out var role) || input.ProfileSubjectId is null)
                    throw new ApplicationFailure(FailureKind.Validation, "Choose an approved profile.");
                selection = new ProfileSelection(role, input.ProfileSubjectId.Value, input.ProfileBusinessId);
            }
            TrustedWorkspaceIdentity identity;
            try { identity=await identities.SignInAsync(input.IdToken,selection,ct); }
            catch (ProfileSelectionRequiredException e)
            {
                return Results.Conflict(new { code="ProfileSelectionRequired", message="Choose an approved profile to continue.",
                    profiles=e.Profiles.Select(ToSessionProfile).ToList() });
            }
            var principal=WorkspaceAuthentication.Principal(identity.Actor,identity.DisplayName,identity.PublicId);
            var claims=(ClaimsIdentity)principal.Identity!;
            claims.AddClaim(new("identity-binding",identity.BindingId.ToString()));claims.AddClaim(new("identity-version",identity.BindingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            claims.AddClaim(new("authenticated-at",identity.AuthenticatedAtUtc.ToString("O",System.Globalization.CultureInfo.InvariantCulture)));
            claims.AddClaim(new("auth-strength", "firebase-verified"));
            if (identity.Profiles.Count == 0) claims.AddClaim(new("onboarding", "true"));
            var now=clock.GetUtcNow();var expiry=new DateTimeOffset(identity.ExpiresAtUtc);
            var deviceSession = await deviceSessions.EstablishAfterFullAuthenticationAsync(
                new DeviceSessionIdentity(identity.Actor.UserId, identity.BindingId, identity.BindingVersion),
                c.Request.Cookies[DeviceCredentialCookie.Name], ct);
            await c.SignInAsync(WorkspaceAuthentication.Scheme,principal,new AuthenticationProperties{IsPersistent=false,IssuedUtc=now,ExpiresUtc=expiry<now.AddHours(1)?expiry:now.AddHours(1),AllowRefresh=false});
            if (deviceSession.ClearStaleCredentials)
            {
                c.Response.Cookies.Delete(DeviceCredentialCookie.Name,
                    DeviceCredentialCookie.DeleteOptions(development));
                c.Response.Cookies.Delete(DeviceSessionCredentialCookie.Name(development),
                    DeviceSessionCredentialCookie.DeleteOptions(development));
            }
            else if (deviceSession.Session is not null)
                c.Response.Cookies.Append(DeviceSessionCredentialCookie.Name(development),
                    deviceSession.Session.Credential.Value,
                    DeviceSessionCredentialCookie.Options(development, deviceSession.Session.Session));
            return Results.NoContent();
        }).AllowAnonymous().AddEndpointFilter<ValidatedInputFilter>();
        app.MapPost("/api/auth/email/start", async (EmailCodeStart input, EmailAuthService auth, CancellationToken ct) =>
        {
            if (!Enum.TryParse<EmailCodePurpose>(input.Purpose, true, out var purpose))
                throw new ApplicationFailure(FailureKind.Validation, "The verification request is invalid.");
            var result = await auth.StartAsync(input.Identifier, input.Phone, purpose, ct);
            // Always use the same response shape for conflicts and unknown accounts.
            return Results.Accepted(value: new { accepted = true, expiresAtUtc = result.ExpiresAtUtc, resendAfterSeconds = result.ResendAfterSeconds });
        }).AllowAnonymous().AddEndpointFilter<ValidatedInputFilter>();
        app.MapPost("/api/auth/email/verify", async (EmailCodeVerify input, EmailAuthService auth, CancellationToken ct) =>
        {
            if (!Enum.TryParse<EmailCodePurpose>(input.Purpose, true, out var purpose))
                throw new ApplicationFailure(FailureKind.Validation, "The verification request is invalid.");
            var result = await auth.VerifyAsync(input.Identifier, purpose, input.Code, ct);
            return Results.Ok(new { customToken = result.CustomToken, expiresAtUtc = result.ExpiresAtUtc });
        }).AllowAnonymous().AddEndpointFilter<ValidatedInputFilter>();
        app.MapPost("/api/auth/password/sign-in", async (PasswordSignInInput input,
            PasswordCredentialService passwords, CancellationToken ct) =>
        {
            var result = await passwords.SignInAsync(input.Phone, input.Password, ct);
            return result.Succeeded && result.Token is not null
                ? Results.Ok(new { customToken = result.Token.CustomToken, expiresAtUtc = result.Token.ExpiresAtUtc })
                : Results.Json(new { code = "InvalidCredentials", message = "Phone number or password is incorrect." }, statusCode: 401);
        }).AllowAnonymous().AddEndpointFilter<ValidatedInputFilter>();
        app.MapPost("/api/auth/password/recovery/verify", async (PasswordRecoveryVerifyInput input,
            EmailAuthService auth, CancellationToken ct) =>
        {
            var result = await auth.VerifyPasswordRecoveryAsync(input.Email, input.Code, ct);
            return Results.Ok(new { recoveryGrant = result.RecoveryGrant, expiresAtUtc = result.ExpiresAtUtc });
        }).AllowAnonymous().AddEndpointFilter<ValidatedInputFilter>();
        app.MapPost("/api/auth/password/reset", async (PasswordResetInput input,
            PasswordCredentialService passwords, CancellationToken ct) =>
        {
            await passwords.ResetAsync(input.Email, input.RecoveryGrant, input.NewPassword, input.ConfirmPassword, ct);
            return Results.NoContent();
        }).AllowAnonymous().AddEndpointFilter<ValidatedInputFilter>();
        app.MapGet("/api/account/security", async (HttpContext c, PasswordCredentialService passwords,
            CancellationToken ct) => Results.Ok(await passwords.StatusAsync(VerifiedIdentity(c).UserId, ct)))
            .RequireAuthorization("VerifiedAccount");
        app.MapPost("/api/account/password-credential", async (PasswordCredentialInput input, HttpContext c,
            PasswordCredentialService passwords, CancellationToken ct) =>
        {
            await passwords.EnrollAsync(VerifiedIdentity(c), input.Phone, input.Password, input.ConfirmPassword, ct);
            return Results.NoContent();
        }).RequireAuthorization("VerifiedAccount").AddEndpointFilter<ValidatedInputFilter>();
        app.MapPost("/api/account/phone-alias", async (PhoneAliasInput input, HttpContext c,
            PhoneAliasService service, CancellationToken ct) =>
        {
            if (c.User.FindFirst("auth-strength")?.Value != "firebase-verified"
                || !Guid.TryParse(c.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                || !Guid.TryParse(c.User.FindFirst("identity-binding")?.Value, out var bindingId)
                || !long.TryParse(c.User.FindFirst("identity-version")?.Value, out var bindingVersion))
                throw new ApplicationFailure(FailureKind.Forbidden, "Sign in to update your phone.");
            await service.RegisterAsync(new DeviceSessionIdentity(userId, bindingId, bindingVersion), input.Phone, ct);
            return Results.NoContent();
        }).RequireAuthorization("VerifiedAccount").AddEndpointFilter<ValidatedInputFilter>();
        if(!development)return;
        app.MapPost("/api/development/session",async(DevelopmentSignIn input,HttpContext c,DevelopmentDirectory directory,IConfiguration configuration)=>
        {
            if(!WorkspaceAuthentication.MatchesKey(configuration["V3:DevelopmentAccessKey"]??"",input.AccessKey))return Results.Unauthorized();
            var persona=directory.Personas.SingleOrDefault(x=>x.Alias==input.Alias);if(persona is null)return Results.Unauthorized();
            await c.SignInAsync(WorkspaceAuthentication.Scheme,WorkspaceAuthentication.Principal(persona.Actor,persona.Name,persona.PublicId));
            return Results.NoContent();
        }).AllowAnonymous().AddEndpointFilter<ValidatedInputFilter>();
    }

    private static SessionProfile ToSessionProfile(TrustedWorkspaceProfile profile)
        => new(profile.Actor.Role.ToString(), SubjectId(profile.Actor), profile.Actor.BusinessId, profile.DisplayName, profile.PublicId, profile.CanCheckout);
    private static Guid SubjectId(Actor actor) => actor.Role switch
    {
        ActorRole.Business => actor.BusinessId ?? Guid.Empty,
        ActorRole.Creator => actor.CreatorId ?? Guid.Empty,
        ActorRole.Customer => actor.CustomerId ?? Guid.Empty,
        _ => actor.UserId
    };

    private static DeviceSessionIdentity VerifiedIdentity(HttpContext context)
    {
        if (context.User.FindFirst("auth-strength")?.Value != "firebase-verified"
            || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(context.User.FindFirst("identity-binding")?.Value, out var bindingId)
            || !long.TryParse(context.User.FindFirst("identity-version")?.Value, out var bindingVersion))
            throw new ApplicationFailure(FailureKind.Forbidden, "Complete verified account sign-in first.");
        return new(userId, bindingId, bindingVersion);
    }
}
