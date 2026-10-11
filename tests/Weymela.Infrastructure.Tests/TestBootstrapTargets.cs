using Weymela.Infrastructure.Identity;

namespace Weymela.Infrastructure.Tests;

internal static class TestBootstrapTargets
{
    private static readonly V3BootstrapTargetConfiguration Configuration = new(
        "weymela_v3_ci_pilot", "weymela_v3_ci_pilot_bootstrap",
        "weymela_v3_ci_auth_test", "weymela_v3_ci_auth_test_bootstrap",
        "weymela_v3_ci_production", "weymela_v3_ci_production_bootstrap");

    public static V3BootstrapTarget Select(string name) =>
        PlatformAdminBootstrapTarget.Select(name, Configuration);

    public static V3BootstrapTargetConfiguration TargetConfiguration => Configuration;
}
