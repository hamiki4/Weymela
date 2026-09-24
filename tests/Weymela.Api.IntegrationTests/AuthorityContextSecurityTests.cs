using System.Security.Claims;
using Weymela.Api.Auth;
using Weymela.Application;
using Xunit;

namespace Weymela.Api.IntegrationTests;

public sealed class AuthorityContextSecurityTests
{
    [Fact]
    public void Client_supplied_view_as_claims_are_rejected_before_authority_resolution()
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
    public void Server_principal_has_no_viewed_subject_and_preserves_normal_authentication()
    {
        var actor = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);

        var context = WorkspaceAuthentication.Authority(
            WorkspaceAuthentication.Principal(actor, "Platform Admin", "admin"));

        Assert.Equal(actor, context.RealActor.Identity);
        Assert.Equal(actor, context.CommandActor);
        Assert.False(context.IsViewAsActive);
    }
}
