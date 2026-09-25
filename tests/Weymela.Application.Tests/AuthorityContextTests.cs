using System;
using Weymela.Application;
using Xunit;

namespace Weymela.Application.Tests;

public sealed class AuthorityContextTests
{
    [Fact]
    public void Normal_authentication_has_real_effective_subject_and_no_support_session()
    {
        var actor = new Actor(Guid.NewGuid(), ActorRole.Business, BusinessId: Guid.NewGuid());

        var context = AuthorityContext.ForAuthenticatedActor(actor);

        Assert.Equal(actor, context.RealActor.Identity);
        Assert.Equal(actor, context.EffectiveSubject.Identity);
        Assert.False(context.EffectiveSubject.IsViewed);
        Assert.False(context.IsViewAsActive);
        Assert.Equal(actor, context.CommandActor);
    }

    [Fact]
    public void Administrative_authority_is_derived_from_the_real_actor()
    {
        var platform = AuthorityContext.ForAuthenticatedActor(new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin));
        var operations = AuthorityContext.ForAuthenticatedActor(new Actor(Guid.NewGuid(), ActorRole.OperationsAdmin));

        Assert.True(platform.Authority.IsPlatformAdmin);
        Assert.True(platform.Authority.CanManagePlatform);
        Assert.True(platform.Authority.CanStartViewAs);
        Assert.False(operations.Authority.IsPlatformAdmin);
        Assert.False(operations.Authority.CanManagePlatform);
        Assert.False(operations.Authority.CanStartViewAs);
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
            AdministrativeCapability.ProtectedPlatformVariables,
            AdministrativeCapability.ViewAs
        }, capability => Assert.False(operations.Allows(capability)));
        Assert.All(Enum.GetValues<AdministrativeCapability>(), capability => Assert.True(platform.Allows(capability)));
    }

    [Fact]
    public void Validated_view_context_never_changes_the_command_actor()
    {
        var real = new RealActor(new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin));
        var viewed = EffectiveSubject.Viewed(new Actor(Guid.NewGuid(), ActorRole.Business, BusinessId: Guid.NewGuid()));
        var session = new SupportSessionContext(Guid.NewGuid(), real.UserId, viewed, DateTime.UtcNow.AddMinutes(10));

        var context = AuthorityContext.ForValidatedViewAs(real, viewed, session, DateTime.UtcNow);

        Assert.True(context.IsViewAsActive);
        Assert.Equal(viewed.Identity, context.EffectiveSubject.Identity);
        Assert.Equal(real.Identity, context.CommandActor);
        Assert.Equal(real.UserId, context.SupportSession!.RealActorUserId);
    }

    [Theory]
    [InlineData(ActorRole.PlatformAdmin)]
    [InlineData(ActorRole.Cashier)]
    public void Platform_admin_cannot_view_restricted_roles(ActorRole role)
    {
        var real = new RealActor(new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin));
        var viewed = EffectiveSubject.Viewed(new Actor(Guid.NewGuid(), role));
        var session = new SupportSessionContext(Guid.NewGuid(), real.UserId, viewed, DateTime.UtcNow.AddMinutes(10));

        Assert.Throws<ApplicationFailure>(() => AuthorityContext.ForValidatedViewAs(real, viewed, session, DateTime.UtcNow));
    }

    [Fact]
    public void Operations_admin_cannot_create_a_view_as_context()
    {
        var real = new RealActor(new Actor(Guid.NewGuid(), ActorRole.OperationsAdmin));
        var viewed = EffectiveSubject.Viewed(new Actor(Guid.NewGuid(), ActorRole.Customer));
        var session = new SupportSessionContext(Guid.NewGuid(), real.UserId, viewed, DateTime.UtcNow.AddMinutes(10));

        var failure = Assert.Throws<ApplicationFailure>(() =>
            AuthorityContext.ForValidatedViewAs(real, viewed, session, DateTime.UtcNow));

        Assert.Equal(FailureKind.Forbidden, failure.Kind);
    }

    [Fact]
    public void Mismatched_support_session_is_rejected()
    {
        var real = new RealActor(new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin));
        var viewed = EffectiveSubject.Viewed(new Actor(Guid.NewGuid(), ActorRole.Customer));
        var session = new SupportSessionContext(Guid.NewGuid(), Guid.NewGuid(), viewed, DateTime.UtcNow.AddMinutes(10));

        Assert.Throws<ApplicationFailure>(() => AuthorityContext.ForValidatedViewAs(real, viewed, session, DateTime.UtcNow));
    }
}
