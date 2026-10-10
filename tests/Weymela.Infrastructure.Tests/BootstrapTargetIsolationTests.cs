using Weymela.Infrastructure.Identity;
using Xunit;

namespace Weymela.Infrastructure.Tests;

public sealed class BootstrapTargetIsolationTests
{
    private static string Connection(string database, string role) =>
        $"Host=isolated-test-postgres;Database={database};Username={role};Passfile=/run/secrets/test-bootstrap.pgpass";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("production")]
    [InlineData("legacy-production")]
    public void Target_selection_is_required_and_rejects_unsupported_environments(string? target)
    {
        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Resolve(
            target, Connection(PlatformAdminBootstrapTarget.DatabaseName, PlatformAdminBootstrapTarget.BootstrapRole)));
    }

    [Fact]
    public void Explicit_pilot_target_preserves_the_existing_database_role_and_firebase_project()
    {
        var target = PlatformAdminBootstrapTarget.Resolve("pilot", Connection(
            PlatformAdminBootstrapTarget.DatabaseName, PlatformAdminBootstrapTarget.BootstrapRole));

        Assert.Equal("pilot", target.Name);
        Assert.Equal("weymela_v3_pilot", target.DatabaseName);
        Assert.Equal("weymela_v3_bootstrap", target.BootstrapRole);
        Assert.Equal("weymela-pilot", target.FirebaseProjectId);
    }

    [Fact]
    public void Production_test_target_is_exact_and_refuses_pilot_or_live_production_database_names()
    {
        var target = PlatformAdminBootstrapTarget.Resolve("production-test", Connection(
            PlatformAdminBootstrapTarget.ProductionTestDatabaseName,
            PlatformAdminBootstrapTarget.ProductionTestBootstrapRole));

        Assert.Equal("production-test", target.Name);
        Assert.Equal("weymela_v3_prod_auth_test", target.DatabaseName);
        Assert.Equal("weymela_v3_prod_auth_test_bootstrap", target.BootstrapRole);
        Assert.Equal("weymela-production", target.FirebaseProjectId);

        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Resolve("production-test",
            Connection("weymela_v3_prod", PlatformAdminBootstrapTarget.ProductionTestBootstrapRole)));
        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Resolve("production-test",
            Connection(PlatformAdminBootstrapTarget.DatabaseName, PlatformAdminBootstrapTarget.BootstrapRole)));
        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Resolve("production-test",
            Connection(PlatformAdminBootstrapTarget.ProductionTestDatabaseName, PlatformAdminBootstrapTarget.BootstrapRole)));
        Assert.Throws<InvalidOperationException>(() => PlatformAdminBootstrapTarget.Resolve("pilot",
            Connection(PlatformAdminBootstrapTarget.ProductionTestDatabaseName,
                PlatformAdminBootstrapTarget.ProductionTestBootstrapRole)));
    }
}
