using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Weymela.Api.Endpoints;
using Weymela.Application;
using Xunit;

namespace Weymela.Api.IntegrationTests;

public sealed class AccountClosureReauthenticationTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Production_closure_requires_verified_recent_authentication()
    {
        var missing = new DefaultHttpContext();
        var failure = Assert.Throws<ApplicationFailure>(() =>
            AccountEndpoints.DemandRecentAuthentication(missing, new FixedClock(), development: false));
        Assert.Equal(FailureKind.Forbidden, failure.Kind);
        Assert.Equal("ReauthenticationRequired", failure.Code);

        var stale = Context(Now.AddMinutes(-11));
        Assert.Equal("ReauthenticationRequired", Assert.Throws<ApplicationFailure>(() =>
            AccountEndpoints.DemandRecentAuthentication(stale, new FixedClock(), development: false)).Code);

        AccountEndpoints.DemandRecentAuthentication(Context(Now.AddMinutes(-9)), new FixedClock(), development: false);
    }

    [Fact]
    public void Development_synthetic_session_is_the_only_reauthentication_exception()
    {
        AccountEndpoints.DemandRecentAuthentication(new DefaultHttpContext(), new FixedClock(), development: true);
        Assert.Throws<ApplicationFailure>(() => AccountEndpoints.DemandRecentAuthentication(
            Context(Now.AddMinutes(-11)), new FixedClock(), development: true));
    }

    private static DefaultHttpContext Context(DateTime authenticatedAt)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("identity-binding", Guid.NewGuid().ToString()),
            new Claim("identity-version", "1"),
            new Claim("authenticated-at", authenticatedAt.ToString("O", CultureInfo.InvariantCulture)),
            new Claim("auth-strength", "firebase-verified"),
        ], "test"));
        return context;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
