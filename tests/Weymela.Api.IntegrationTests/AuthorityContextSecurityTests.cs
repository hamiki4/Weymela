using System.Security.Claims;
using Weymela.Api.Auth;
using Weymela.Application;
using Xunit;

namespace Weymela.Api.IntegrationTests;

public sealed class AuthorityContextSecurityTests
{
    [Fact]
    public void Client_supplied_reserved_authority_claims_are_rejected_before_authority_resolution()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, ActorRole.OperationsAdmin.ToString()),
            new Claim(AuthorityClaimTypes.ViewedRole, ActorRole.PlatformAdmin.ToString()),
            new Claim(AuthorityClaimTypes.ViewedUserId, Guid.NewGuid().ToString())
        ], WorkspaceAuthentication.Scheme));

        var failure = Assert.Throws<ApplicationFailure>(() => WorkspaceAuthentication.Authority(principal));

        Assert.Equal(FailureKind.Forbidden, failure.Kind);
        Assert.Equal("ForgedAuthorityContext", failure.Code);
    }

    [Fact]
    public void Server_principal_preserves_normal_real_actor_authentication()
    {
        var actor = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);

        var context = WorkspaceAuthentication.Authority(
            WorkspaceAuthentication.Principal(actor, "Platform Admin", "admin"));

        Assert.Equal(actor, context.RealActor.Identity);
        Assert.Equal(actor, context.CommandActor);
    }

    [Theory]
    [InlineData(ActorRole.PlatformAdmin)]
    [InlineData(ActorRole.OperationsAdmin)]
    public void Viewed_customer_claim_cannot_keep_real_admin_pii_authority(ActorRole realRole)
    {
        var principal = WorkspaceAuthentication.Principal(new Actor(Guid.NewGuid(), realRole), "Admin", "admin");
        principal.AddIdentity(new ClaimsIdentity([
            new Claim(AuthorityClaimTypes.ViewedRole, ActorRole.Customer.ToString()),
            new Claim(AuthorityClaimTypes.ViewedUserId, Guid.NewGuid().ToString())]));

        var failure = Assert.Throws<ApplicationFailure>(() => WorkspaceAuthentication.Authority(principal));
        Assert.Equal("ForgedAuthorityContext", failure.Code);
    }
}
