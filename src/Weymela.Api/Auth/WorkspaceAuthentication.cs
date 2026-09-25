using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Api.Auth;

public static class WorkspaceAuthentication
{
    public const string Scheme="WeymelaV3";
    public static Actor Actor(ClaimsPrincipal user)
    {
        if (user.Claims.Any(x => AuthorityClaimTypes.IsReserved(x.Type)))
            throw new ApplicationFailure(FailureKind.Forbidden, "The authority context is not server-controlled.", code: "ForgedAuthorityContext");
        Guid? Read(string name)=>Guid.TryParse(user.FindFirstValue(name),out var id)?id:null;
        if(!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier),out var userId)||!Enum.TryParse<ActorRole>(user.FindFirstValue(ClaimTypes.Role),out var role))
            throw new ApplicationFailure(FailureKind.Forbidden,"Sign in to your workspace.");
        return new(userId,role,Read("business"),Read("creator"),Read("customer"));
    }
    public static AuthorityContext Authority(ClaimsPrincipal user) => AuthorityContext.ForAuthenticatedActor(Actor(user));
    public static AuthorityContext Authority(HttpContext context) => Authority(context.User);
    public static ClaimsPrincipal Principal(Actor actor,string name,string publicId)
    {
        List<Claim> claims=[new(ClaimTypes.NameIdentifier,actor.UserId.ToString()),new(ClaimTypes.Role,actor.Role.ToString()),new(ClaimTypes.Name,name),new("publicId",publicId)];
        claims.Add(new("profile-key", TrustedIdentityService.WorkspaceProfileKey(actor)));
        if(actor.BusinessId is {} b)claims.Add(new("business",b.ToString()));
        if(actor.CreatorId is {} c)claims.Add(new("creator",c.ToString()));
        if(actor.CustomerId is {} u)claims.Add(new("customer",u.ToString()));
        return new(new ClaimsIdentity(claims,Scheme));
    }
    public static string? ProfileKey(ClaimsPrincipal user) => user.FindFirstValue("profile-key");
    public static bool MatchesKey(string expected,string? supplied)
    {
        if(expected.Length<32||supplied is null)return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    }
}

public sealed class WorkspaceRoleRequirement(IReadOnlySet<ActorRole> roles) : IAuthorizationRequirement
{
    public IReadOnlySet<ActorRole> Roles { get; } = roles;
}

public sealed class WorkspaceRoleHandler(IHttpContextAccessor accessor)
    : AuthorizationHandler<WorkspaceRoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, WorkspaceRoleRequirement requirement)
    {
        var http = accessor.HttpContext;
        if (http is null) return Task.CompletedTask;
        try
        {
            var authority = WorkspaceAuthentication.Authority(http);
            if (requirement.Roles.Contains(authority.RealActor.Role))
                context.Succeed(requirement);
        }
        catch (ApplicationFailure) { }
        return Task.CompletedTask;
    }
}

public sealed class ActiveWorkspaceRequirement(bool checkout = false) : IAuthorizationRequirement { public bool Checkout => checkout; }
public sealed class ActiveWorkspaceHandler(IHttpContextAccessor accessor, WeymelaDbContext db) : AuthorizationHandler<ActiveWorkspaceRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,ActiveWorkspaceRequirement requirement)
    {
        if(context.User.Identity?.IsAuthenticated!=true)return;
        AuthorityContext authority;
        try
        {
            authority = accessor.HttpContext is { } http
                ? WorkspaceAuthentication.Authority(http)
                : WorkspaceAuthentication.Authority(context.User);
        }
        catch (ApplicationFailure) { return; }
        var actor=authority.CommandActor;
        if(await db.AccountLifecycles.AsNoTracking().AnyAsync(x=>x.UserId==actor.UserId
            && (x.Status == AccountLifecycleStatus.Suspended || x.Status == AccountLifecycleStatus.Disabled || x.Status == AccountLifecycleStatus.Revoked || x.Status == AccountLifecycleStatus.Closed), CancellationToken.None)) return;
        var subject=actor.Role switch{ActorRole.Business=>actor.BusinessId,ActorRole.Creator=>actor.CreatorId,ActorRole.Customer=>actor.CustomerId,_=>actor.UserId};
        if(await db.CommercePermissions.AsNoTracking().AnyAsync(x=>x.UserId==actor.UserId&&x.Role==actor.Role&&x.SubjectId==subject&&x.IsActive
            &&(!requirement.Checkout||x.CanCheckout)
            &&(actor.Role!=ActorRole.Business&&actor.Role!=ActorRole.Cashier||x.BusinessId==actor.BusinessId)))context.Succeed(requirement);
    }
}
