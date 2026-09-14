using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Weymela.Api.Security;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class EndpointAuditTests(PostgresFixture fixture)
{
    [Fact] public async Task Every_api_route_has_explicit_authorization_or_an_explicit_auth_bootstrap_exception()
    {
        await using var host=await ApiFixture.CreateAsync(fixture);
        var routes=((IEndpointRouteBuilder)host.App).DataSources.SelectMany(x=>x.Endpoints).OfType<RouteEndpoint>().Where(x=>x.RoutePattern.RawText?.StartsWith("/api/")==true).ToArray();
        Assert.True(routes.Length>=50);
        foreach(var route in routes)
        {
            var path=route.RoutePattern.RawText!;
            if(path is "/api/auth/mode" or "/api/auth/firebase/session" or "/api/development/session" or "/api/auth/email/start" or "/api/auth/email/verify") Assert.NotNull(route.Metadata.GetMetadata<IAllowAnonymous>());
            else { Assert.Null(route.Metadata.GetMetadata<IAllowAnonymous>()); Assert.NotEmpty(route.Metadata.GetOrderedMetadata<IAuthorizeData>()); }
        }
    }
    [Fact] public async Task Creator_cannot_refresh_another_creators_participation()
    {
        await using var host=await ApiFixture.CreateAsync(fixture); using var creator=await host.Login("creator"); using var other=await host.Login("other-creator");
        var active=await creator.GetJson("/api/creator/campaigns"); var id=active[0]!["participationId"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.Forbidden,(await other.Post($"/api/creator/participations/{id}/refresh",new{})).StatusCode);
    }
    [Fact] public async Task Customer_cannot_read_another_customers_qr_status()
    {
        await using var host=await ApiFixture.CreateAsync(fixture); using var customer=await host.Login("customer"); using var other=await host.Login("other-customer");
        var offers=await customer.GetJson("/api/customer/offers"); var id=offers[0]!["id"]!.GetValue<string>(); var qr=await customer.PostJson($"/api/customer/offers/{id}/qr",new{});
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/customer/qr/{qr["id"]!.GetValue<string>()}")).StatusCode);
    }
    [Fact] public async Task Cashier_cookie_cannot_retain_access_after_business_reassignment_or_checkout_permission_removal()
    {
        await using var host=await ApiFixture.CreateAsync(fixture); using var cashier=await host.Login("cashier");
        await using var db=host.Database.Open(); var id=new DevelopmentDirectory().Personas.Single(x=>x.Alias=="cashier").Actor.UserId;
        await db.CommercePermissions.Where(x=>x.UserId==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CanCheckout,false));
        Assert.Equal(HttpStatusCode.Forbidden,(await cashier.Post("/api/checkout/manual-lookup",new{})).StatusCode);
        await db.CommercePermissions.Where(x=>x.UserId==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CanCheckout,true).SetProperty(x=>x.BusinessId,Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden,(await cashier.Post("/api/checkout/resolve",new{token=new string('A',43)})).StatusCode);
    }
    [Theory] [InlineData("/api/business/wallet/deposits")][InlineData("/api/business/deposit-requests")]
    [InlineData("/api/admin/deposit-requests/11111111-1111-1111-1111-111111111111/review")]
    [InlineData("/api/admin/financial-settings")][InlineData("/api/admin/platform/settlements")]
    [InlineData("/api/checkout/confirm")][InlineData("/api/customer/offers/11111111-1111-1111-1111-111111111111/qr")]
    [InlineData("/api/creator/participations/11111111-1111-1111-1111-111111111111/refresh")]
    public async Task Explicit_financial_freeze_blocks_new_and_existing_financial_write_paths(string path)
    {
        await using var host=await ApiFixture.CreateAsync(fixture,b=>b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["V3:FinancialWritesEnabled"]="false"})); using var client=await host.Login("admin");
        var response=await client.Post(path,new{}); Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
        Assert.Contains("FinancialWritesPaused",await response.Content.ReadAsStringAsync());
    }
    [Fact] public async Task Notification_read_route_cannot_mark_another_users_notification()
    {
        await using var host=await ApiFixture.CreateAsync(fixture); var personas=new DevelopmentDirectory().Personas;
        var creator=personas.Single(x=>x.Alias=="creator").Actor; Guid id;
        await using(var db=host.Database.Open()) { var row=new Weymela.Infrastructure.Persistence.Records.InAppNotification {UserId=creator.UserId,Role=creator.Role,SourceKey="test",EventType="Test",Title="Private update",Message="Safe content",Route="/creator/earnings",CreatedAtUtc=DateTime.UtcNow};db.InAppNotifications.Add(row);await db.SaveChangesAsync();id=row.Id; }
        using var other=await host.Login("other-creator"); Assert.Equal(HttpStatusCode.NotFound,(await other.Post($"/api/notifications/{id}/read",new{})).StatusCode);
        var list=await other.GetJson("/api/notifications"); Assert.DoesNotContain("Private update",list.ToJsonString());
    }
}
