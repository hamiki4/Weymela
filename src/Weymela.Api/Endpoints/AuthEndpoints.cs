using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Weymela.Api.Auth;
using Weymela.Application.Web;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Identity;
using Weymela.Api.Security;
using System.Security.Claims;

namespace Weymela.Api.Endpoints;

internal static class AuthEndpoints
{
    private sealed record DevelopmentSignIn(string Alias,string AccessKey);
    private sealed record FirebaseSignIn(string IdToken);
    public static void MapAuthEndpoints(this WebApplication app,bool development)
    {
        app.MapGet("/api/auth/mode",()=>Results.Ok(new{development,personas=development?new DevelopmentDirectory().Personas.Select(x=>new{x.Alias,x.Name,role=x.Actor.Role.ToString()}):null})).AllowAnonymous();
        app.MapGet("/api/session",async(HttpContext c,WeymelaDbContext db,CancellationToken ct)=>
        {
            var a=EndpointSupport.Actor(c);var checkout=await db.CommercePermissions.AnyAsync(x=>x.UserId==a.UserId&&x.Role==a.Role&&x.IsActive&&x.CanCheckout,ct);
            return Results.Ok(new SessionUser(a.Role.ToString(),c.User.Identity!.Name!,c.User.FindFirst("publicId")?.Value??"",development,checkout));
        }).RequireAuthorization("Workspace");
        app.MapPost("/api/session/sign-out",async(HttpContext c)=>{await c.SignOutAsync(WorkspaceAuthentication.Scheme);return Results.NoContent();}).RequireAuthorization("Workspace");
        app.MapPost("/api/auth/firebase/session",async(FirebaseSignIn input,HttpContext c,TrustedIdentityService identities,TimeProvider clock,CancellationToken ct)=>
        {
            var identity=await identities.SignInAsync(input.IdToken,ct);
            var principal=WorkspaceAuthentication.Principal(identity.Actor,identity.DisplayName,identity.PublicId);
            var claims=(ClaimsIdentity)principal.Identity!;
            claims.AddClaim(new("identity-binding",identity.BindingId.ToString()));claims.AddClaim(new("identity-version",identity.BindingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            claims.AddClaim(new("authenticated-at",identity.AuthenticatedAtUtc.ToString("O",System.Globalization.CultureInfo.InvariantCulture)));
            var now=clock.GetUtcNow();var expiry=new DateTimeOffset(identity.ExpiresAtUtc);
            await c.SignInAsync(WorkspaceAuthentication.Scheme,principal,new AuthenticationProperties{IsPersistent=false,IssuedUtc=now,ExpiresUtc=expiry<now.AddHours(1)?expiry:now.AddHours(1),AllowRefresh=false});
            return Results.NoContent();
        }).AllowAnonymous().AddEndpointFilter<ValidatedInputFilter>();
        if(!development)return;
        app.MapPost("/api/development/session",async(DevelopmentSignIn input,HttpContext c,DevelopmentDirectory directory,IConfiguration configuration)=>
        {
            if(!WorkspaceAuthentication.MatchesKey(configuration["V3:DevelopmentAccessKey"]??"",input.AccessKey))return Results.Unauthorized();
            var persona=directory.Personas.SingleOrDefault(x=>x.Alias==input.Alias);if(persona is null)return Results.Unauthorized();
            await c.SignInAsync(WorkspaceAuthentication.Scheme,WorkspaceAuthentication.Principal(persona.Actor,persona.Name,persona.PublicId));
            return Results.NoContent();
        }).AllowAnonymous().AddEndpointFilter<ValidatedInputFilter>();
    }
}
