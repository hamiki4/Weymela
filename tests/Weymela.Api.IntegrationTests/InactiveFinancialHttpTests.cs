using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using Weymela.Api;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class InactiveFinancialHttpTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData("/api/business/wallet/deposits")]
    [InlineData("/api/business/campaigns/{id:guid}/fund")]
    [InlineData("/api/admin/payouts/{id:guid}/paid")]
    [InlineData("/api/business/checkout")]
    [InlineData("/api/admin/accounts/businesses/{businessId:guid}/promotional-funding")]
    public async Task Explicit_financial_pause_blocks_money_regardless_of_product_settings(string route)
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var called = false;
        var middleware = new ApiSafetyMiddleware(_ => { called = true; return Task.CompletedTask; },
            new RuntimeOptions { Development = true, FinancialWritesEnabled = false },
            new ProductIntegrationOptions(), NullLogger<ApiSafetyMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = route;
        context.Request.Scheme = "https";
        context.Request.Headers["X-Weymela-Request"] = "1";
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(route),
            0, EndpointMetadataCollection.Empty, "isolated money request"));
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context, db);
        Assert.False(called);
        Assert.Equal(503, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("FinancialWritesPaused", body.RootElement.GetProperty("code").GetString());
    }
}
