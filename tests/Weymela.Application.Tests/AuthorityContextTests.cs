using System;
using Weymela.Application;
using Xunit;

namespace Weymela.Application.Tests;

public sealed class AuthorityContextTests
{
    [Fact]
    public void Normal_authentication_uses_the_real_actor_for_commands()
    {
        var actor = new Actor(Guid.NewGuid(), ActorRole.Business, BusinessId: Guid.NewGuid());

        var context = AuthorityContext.ForAuthenticatedActor(actor);

        Assert.Equal(actor, context.RealActor.Identity);
        Assert.Equal(actor, context.CommandActor);
    }

    [Fact]
    public void Administrative_authority_is_derived_from_the_real_actor()
    {
        var platform = AuthorityContext.ForAuthenticatedActor(new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin));
        var operations = AuthorityContext.ForAuthenticatedActor(new Actor(Guid.NewGuid(), ActorRole.OperationsAdmin));

        Assert.True(platform.Authority.IsPlatformAdmin);
        Assert.True(platform.Authority.CanManagePlatform);
        Assert.False(operations.Authority.IsPlatformAdmin);
        Assert.False(operations.Authority.CanManagePlatform);
    }

    [Fact]
    public void Operations_capability_matrix_allows_operations_and_denies_platform_control()
    {
        var operations = AdministrativeAuthority.For(new RealActor(new Actor(Guid.NewGuid(), ActorRole.OperationsAdmin)));
        var platform = AdministrativeAuthority.For(new RealActor(new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin)));

        Assert.All(new[]
        {
            AdministrativeCapability.OperationsWorkspace,
            AdministrativeCapability.AccountReview,
            AdministrativeCapability.DepositReview,
            AdministrativeCapability.BusinessOperationalVisibility,
            AdministrativeCapability.CreatorOperationalVisibility,
            AdministrativeCapability.CustomerOperationalVisibility,
            AdministrativeCapability.CampaignOperationalVisibility,
            AdministrativeCapability.UgcOperationalVisibility,
            AdministrativeCapability.CreatorPayoutProcessing,
            AdministrativeCapability.CustomerPayoutProcessing
        }, capability => Assert.True(operations.Allows(capability)));
        Assert.All(new[]
        {
            AdministrativeCapability.PlatformDashboard,
            AdministrativeCapability.PlatformFinancialReports,
            AdministrativeCapability.PlatformFinancialConfiguration,
            AdministrativeCapability.PlatformFinancialConfigurationHistory,
            AdministrativeCapability.PlatformSettlement,
            AdministrativeCapability.PlatformReconciliation,
            AdministrativeCapability.PlatformAccountManagement,
            AdministrativeCapability.PlatformRoleGrant,
            AdministrativeCapability.ProtectedPlatformVariables
        }, capability => Assert.False(operations.Allows(capability)));
        Assert.All(Enum.GetValues<AdministrativeCapability>(), capability => Assert.True(platform.Allows(capability)));
    }

}
