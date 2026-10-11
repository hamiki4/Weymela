using Weymela.Infrastructure.Identity;
using Xunit;

namespace Weymela.Infrastructure.Tests;

public sealed class BootstrapTargetIsolationTests
{
    private static V3BootstrapTargetConfiguration Configuration => TestBootstrapTargets.TargetConfiguration;

    private static string Connection(string database, string role) =>
        $"Host=isolated-test-postgres;Database={database};Username={role};Passfile=/run/secrets/test-bootstrap.pgpass";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("legacy-production")]
    public void Target_selection_is_required_and_rejects_unsupported_environments(string? target)
    {
        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Resolve(
            target, Connection(Configuration.PilotDatabaseName, Configuration.PilotBootstrapRole), Configuration));
    }

    [Fact]
    public void Explicit_pilot_target_uses_its_separate_configured_database_role_and_firebase_project()
    {
        var target = PlatformAdminBootstrapTarget.Resolve("pilot", Connection(
            Configuration.PilotDatabaseName, Configuration.PilotBootstrapRole), Configuration);

        Assert.Equal("pilot", target.Name);
        Assert.Equal(Configuration.PilotDatabaseName, target.DatabaseName);
        Assert.Equal(Configuration.PilotBootstrapRole, target.BootstrapRole);
        Assert.Equal("weymela-pilot", target.FirebaseProjectId);
    }

    [Fact]
    public void Production_test_cannot_select_the_pilot_or_production_target_pair()
    {
        var target = PlatformAdminBootstrapTarget.Resolve("production-test", Connection(
            Configuration.ProductionTestDatabaseName, Configuration.ProductionTestBootstrapRole), Configuration);

        Assert.Equal("production-test", target.Name);
        Assert.Equal(Configuration.ProductionTestDatabaseName, target.DatabaseName);
        Assert.Equal(Configuration.ProductionTestBootstrapRole, target.BootstrapRole);
        Assert.Equal("weymela-production", target.FirebaseProjectId);

        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Resolve("production-test",
            Connection(Configuration.ProductionDatabaseName, Configuration.ProductionBootstrapRole), Configuration));
        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Resolve("production-test",
            Connection(Configuration.PilotDatabaseName, Configuration.PilotBootstrapRole), Configuration));
        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Resolve("pilot",
            Connection(Configuration.ProductionTestDatabaseName, Configuration.ProductionTestBootstrapRole), Configuration));
    }

    [Fact]
    public void Production_target_is_explicit_and_uses_its_private_configured_pair()
    {
        var target = PlatformAdminBootstrapTarget.Resolve("production", Connection(
            Configuration.ProductionDatabaseName, Configuration.ProductionBootstrapRole), Configuration);

        Assert.Equal("production", target.Name);
        Assert.Equal(Configuration.ProductionDatabaseName, target.DatabaseName);
        Assert.Equal(Configuration.ProductionBootstrapRole, target.BootstrapRole);
        Assert.Equal("weymela-production", target.FirebaseProjectId);
        Assert.True(target.RequiresProductionAuthorization);

        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Resolve("production",
            Connection(Configuration.PilotDatabaseName, Configuration.PilotBootstrapRole), Configuration));
        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Resolve("production",
            Connection(Configuration.ProductionTestDatabaseName, Configuration.ProductionTestBootstrapRole), Configuration));
        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Resolve("production-test",
            Connection(Configuration.ProductionDatabaseName, Configuration.ProductionBootstrapRole), Configuration));
    }

    [Fact]
    public void Target_configuration_rejects_reused_pairs_and_non_v3_or_unprivileged_names()
    {
        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Select("production",
            Configuration with { ProductionDatabaseName = Configuration.PilotDatabaseName }));
        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Select("production",
            Configuration with { ProductionTestBootstrapRole = Configuration.PilotBootstrapRole }));
        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Select("production",
            Configuration with { ProductionDatabaseName = "legacy_production" }));
        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Select("production",
            Configuration with { ProductionBootstrapRole = "weymela_v3_ci_admin" }));
    }

    [Fact]
    public void Protected_environment_mapping_is_required_and_selects_the_configured_production_pair()
    {
        var values = new Dictionary<string, string>
        {
            ["V3_BOOTSTRAP_PILOT_DATABASE"] = Configuration.PilotDatabaseName,
            ["V3_BOOTSTRAP_PILOT_ROLE"] = Configuration.PilotBootstrapRole,
            ["V3_BOOTSTRAP_PRODUCTION_TEST_DATABASE"] = Configuration.ProductionTestDatabaseName,
            ["V3_BOOTSTRAP_PRODUCTION_TEST_ROLE"] = Configuration.ProductionTestBootstrapRole,
            ["V3_BOOTSTRAP_PRODUCTION_DATABASE"] = Configuration.ProductionDatabaseName,
            ["V3_BOOTSTRAP_PRODUCTION_ROLE"] = Configuration.ProductionBootstrapRole
        };
        var configured = V3BootstrapTargetConfiguration.FromEnvironment(name => values.GetValueOrDefault(name));
        var target = PlatformAdminBootstrapTarget.Select("production", configured);

        Assert.Equal(Configuration, configured);
        Assert.Equal(Configuration.ProductionDatabaseName, target.DatabaseName);
        Assert.Equal(Configuration.ProductionBootstrapRole, target.BootstrapRole);
        Assert.Throws<InvalidOperationException>(() => V3BootstrapTargetConfiguration.FromEnvironment(_ => null));
    }

    [Theory]
    [InlineData("owner-approval/PR#134")]
    [InlineData("approval:record_1")]
    public void Production_authorization_reference_is_a_bounded_audit_identifier(string reference) =>
        Assert.True(PlatformAdminBootstrapTarget.IsValidProductionAuthorizationReference(reference));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("owner approval")]
    [InlineData("approval;operator=other")]
    [InlineData("approval\noperator=other")]
    public void Production_authorization_reference_rejects_blank_or_audit_injection(string? reference) =>
        Assert.False(PlatformAdminBootstrapTarget.IsValidProductionAuthorizationReference(reference));
}
