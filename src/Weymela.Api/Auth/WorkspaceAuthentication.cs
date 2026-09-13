using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Identity;

namespace Weymela.Api.Auth;

public static class WorkspaceAuthentication
{
    public const string Scheme="WeymelaV3";
    public static Actor Actor(ClaimsPrincipal user)
    {
        Guid? Read(string name)=>Guid.TryParse(user.FindFirstValue(name),out var id)?id:null;
        if(!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier),out var userId)||!Enum.TryParse<ActorRole>(user.FindFirstValue(ClaimTypes.Role),out var role))
            throw new ApplicationFailure(FailureKind.Forbidden,"Sign in to your workspace.");
        return new(userId,role,Read("business"),Read("creator"),Read("customer"));
    }
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

public sealed class ActiveWorkspaceRequirement(bool checkout = false) : IAuthorizationRequirement { public bool Checkout => checkout; }
public sealed class ActiveWorkspaceHandler(WeymelaDbContext db) : AuthorizationHandler<ActiveWorkspaceRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,ActiveWorkspaceRequirement requirement)
    {
        if(context.User.Identity?.IsAuthenticated!=true)return;
        var actor=WorkspaceAuthentication.Actor(context.User);
        var subject=actor.Role switch{ActorRole.Business=>actor.BusinessId,ActorRole.Creator=>actor.CreatorId,ActorRole.Customer=>actor.CustomerId,_=>actor.UserId};
        if(await db.CommercePermissions.AsNoTracking().AnyAsync(x=>x.UserId==actor.UserId&&x.Role==actor.Role&&x.SubjectId==subject&&x.IsActive
            &&(!requirement.Checkout||x.CanCheckout)
            &&(actor.Role!=ActorRole.Business&&actor.Role!=ActorRole.Cashier||x.BusinessId==actor.BusinessId)))context.Succeed(requirement);
    }
}
