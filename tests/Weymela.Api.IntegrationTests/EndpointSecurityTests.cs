using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Weymela.Api.Security;
using Weymela.Infrastructure.Operations;
using Xunit;

namespace Weymela.Api.IntegrationTests;

public sealed class EndpointSecurityTests
{
    [Theory]
    [InlineData("/api/session")]
    [InlineData("/api/auth/mode")]
    public void Read_only_bootstrap_routes_use_general_read_limiter(string route)
    {
        Assert.Equal("reads", EndpointSecurity.Category(Context(route, HttpMethods.Get)));
    }

    [Theory]
    [InlineData("/api/auth/firebase/session", "POST")]
    [InlineData("/api/auth/password/sign-in", "POST")]
    [InlineData("/api/session/sign-out", "POST")]
    [InlineData("/api/device/unlock", "POST")]
    [InlineData("/api/account/password-credential", "POST")]
    public void Credential_and_security_operations_keep_strict_auth_limiter(string route, string method)
    {
        Assert.Equal("auth", EndpointSecurity.Category(Context(route, method)));
    }

    [Fact]
    public void Strict_auth_bucket_remains_tighter_than_read_bucket()
    {
        using var authServices = BuildLimiter(out var authLimiter);
        var authResults = Enumerable.Range(0, 21)
            .Select(_ => AcquireAndRelease(authLimiter, Context("/api/auth/password/sign-in", HttpMethods.Post)))
            .ToArray();
        Assert.All(authResults[..20], value => Assert.True(value));
        Assert.False(authResults[20]);

        using var readServices = BuildLimiter(out var readLimiter);
        var readResults = Enumerable.Range(0, 21)
            .Select(_ => AcquireAndRelease(readLimiter, Context("/api/session", HttpMethods.Get)))
            .ToArray();
        Assert.All(readResults, value => Assert.True(value));
    }

    [Fact]
    public void Global_concurrency_limit_remains_intact()
    {
        using var services = BuildLimiter(out var limiter);
        var leases = Enumerable.Range(0, 17)
            .Select(_ => Acquire(limiter, Context("/api/session", HttpMethods.Get)))
            .ToArray();
        Assert.All(leases[..16], lease => Assert.True(lease.IsAcquired));
        Assert.False(leases[16].IsAcquired);
        foreach (var lease in leases) lease.Dispose();
    }

    private static RateLimitLease Acquire(PartitionedRateLimiter<HttpContext> limiter, HttpContext context) =>
        limiter.AcquireAsync(context).AsTask().GetAwaiter().GetResult();

    private static bool AcquireAndRelease(PartitionedRateLimiter<HttpContext> limiter, HttpContext context)
    {
        using var lease = Acquire(limiter, context);
        return lease.IsAcquired;
    }

    private static ServiceProvider BuildLimiter(out PartitionedRateLimiter<HttpContext> limiter)
    {
        var services = new ServiceCollection();
        EndpointSecurity.AddLimits(services, new RuntimeOptions { RateLimitMultiplier = 1 });
        var provider = services.BuildServiceProvider();
        limiter = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter!;
        return provider;
    }

    private static DefaultHttpContext Context(string route, string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(route),
            0,
            EndpointMetadataCollection.Empty,
            route));
        return context;
    }
}
