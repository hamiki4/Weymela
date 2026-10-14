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
    [InlineData("/api/business/deposit-requests", "DepositRequests")]
    [InlineData("/api/admin/deposit-requests/{id:guid}/approve", "DepositReview")]
    [InlineData("/api/admin/deposit-requests/{id:guid}/review", "DepositReview")]
    [InlineData("/api/admin/accounts/businesses/{businessId:guid}/promotional-funding", "WalletFunding")]
    [InlineData("/api/business/campaigns/{id:guid}/fund", "PromotionReservations")]
    [InlineData("/api/business/ugc/{id:guid}/publish", "PromotionReservations")]
    [InlineData("/api/business/checkout/confirm", "Checkout")]
    [InlineData("/api/customer/offers/{id:guid}/qr", "Checkout")]
    [InlineData("/api/admin/payouts/{id:guid}/paid", "Payouts")]
    public async Task Production_operation_controls_do_not_enable_other_money_routes(string route, string operation)
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        foreach (var enabled in new[] { "DepositRequests", "DepositReview", "WalletFunding", "PromotionReservations", "Checkout", "Payouts" })
        {
            var called = false;
            var middleware = new ApiSafetyMiddleware(_ => { called = true; return Task.CompletedTask; },
                new RuntimeOptions { EnvironmentName = "Production", FinancialWritesEnabled = true,
                    ProductionFinancialAuthorizationReference = "isolated-test-authorization",
                    EnabledProductionFinancialOperations = [enabled] },
                new ProductIntegrationOptions(), NullLogger<ApiSafetyMiddleware>.Instance);
            var context = Context(route);
            await middleware.InvokeAsync(context, db);
            Assert.Equal(enabled == operation, called);
            Assert.Equal(enabled == operation ? 200 : 503, context.Response.StatusCode);
        }
        Assert.False(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.FinancialJournals));
    }
    [Theory]
    [InlineData("/api/creator/participations/{id:guid}/refresh")]
    [InlineData("/api/future/approve")]
    public async Task Production_never_enables_unverified_view_rewards_or_unclassified_money_routes(string route)
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var called = false;
        var middleware = new ApiSafetyMiddleware(_ => { called = true; return Task.CompletedTask; },
            new RuntimeOptions { EnvironmentName = "Production", FinancialWritesEnabled = true,
                ProductionFinancialAuthorizationReference = "isolated-test-authorization",
                EnabledProductionFinancialOperations = ["DepositRequests", "DepositReview", "WalletFunding", "PromotionReservations", "Checkout", "Payouts"] },
            new ProductIntegrationOptions(), NullLogger<ApiSafetyMiddleware>.Instance);
        var context = Context(route);
        await middleware.InvokeAsync(context, db);
        Assert.False(called);
        Assert.Equal(503, context.Response.StatusCode);
    }
    private static DefaultHttpContext Context(string route)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST"; context.Request.Path = route; context.Request.Scheme = "https";
        context.Request.ContentLength = 0; context.Request.Headers["X-Weymela-Request"] = "1";
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(route),
            0, EndpointMetadataCollection.Empty, "isolated operation request"));
        context.Response.Body = new MemoryStream();
        return context;
    }
    [Theory]
    [InlineData("/api/admin/financial-settings")]
    [InlineData("/api/customer/payout-destination")]
    [InlineData("/api/creator/payout-destination")]
    public async Task Configuration_can_reach_its_own_authorization_checks_while_money_is_paused(string route)
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var called = false;
        var middleware = new ApiSafetyMiddleware(_ => { called = true; return Task.CompletedTask; },
            new RuntimeOptions { EnvironmentName = "Production", FinancialWritesEnabled = false },
            new ProductIntegrationOptions(), NullLogger<ApiSafetyMiddleware>.Instance);
        var context = Context(route);
        await middleware.InvokeAsync(context, db);
        Assert.True(called);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.False(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.FinancialJournals));
    }
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
