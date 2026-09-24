using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class WorkspaceHttpTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("business","/api/business/home")]
    [InlineData("business","/api/business/wallet")]
    [InlineData("business","/api/business/campaigns")]
    [InlineData("creator","/api/creator/home")]
    [InlineData("creator","/api/creator/campaigns")]
    [InlineData("creator","/api/creator/earnings")]
    [InlineData("admin","/api/admin/home")]
    [InlineData("admin","/api/admin/businesses")]
    [InlineData("admin","/api/admin/creators")]
    [InlineData("admin","/api/admin/financial-settings")]
    [InlineData("admin","/api/admin/payouts")]
    [InlineData("admin","/api/admin/platform")]
    [InlineData("admin","/api/admin/notifications")]
    [InlineData("admin","/api/admin/audit")]
    [InlineData("admin","/api/admin/accounts")]
    [InlineData("customer","/api/customer/offers")]
    [InlineData("customer","/api/customer/transactions")]
    [InlineData("customer","/api/customer/cashback")]
    [InlineData("customer","/api/customer/history")]
    public async Task Role_workspace_loads_from_real_persistence(string persona,string path)
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login(persona);Assert.NotNull(await c.GetJson(path));}

    [Fact]
    public async Task Platform_admin_account_directory_and_detail_are_safe()
    {
        await using var f = await ApiFixture.CreateAsync(postgres); using var c = await f.Login("admin");
        var rows = await c.GetJson("/api/admin/accounts");
        Assert.NotEmpty(rows.AsArray());
        var text = rows.ToJsonString();
        foreach (var secret in new[] { "passwordHash", "pinVerifier", "activationSecretHash", "firebaseToken", "sessionToken" })
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        var userId = rows[0]!["userId"]?.GetValue<string>() ?? rows[0]!["id"]!.GetValue<string>();
        var detail = await c.GetJson($"/api/admin/accounts/{userId}");
        foreach (var secret in new[] { "passwordHash", "pinVerifier", "activationSecretHash", "firebaseToken", "sessionToken" })
            Assert.DoesNotContain(secret, detail.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("creator","/api/business/wallet")]
    [InlineData("business","/api/admin/payouts")]
    [InlineData("customer","/api/admin/campaigns")]
    [InlineData("business","/api/customer/transactions")]
    [InlineData("creator","/api/customer/cashback")]
    [InlineData("cashier","/api/business/campaigns")]
    public async Task Other_role_endpoint_is_forbidden(string persona,string path)
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login(persona);Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(path)).StatusCode);}

    [Fact] public async Task Anonymous_requests_cannot_read_financial_workspace()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=f.Anonymous();Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/business/home")).StatusCode);}

    [Fact] public async Task Development_signin_requires_explicit_secret_and_does_not_return_it()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=f.Anonymous();var failed=await c.PostAsJsonAsync("/api/development/session",new{alias="admin",accessKey="wrong"});Assert.Equal(HttpStatusCode.Unauthorized,failed.StatusCode);var mode=(await c.GetJson("/api/auth/mode")).ToJsonString();Assert.DoesNotContain(f.AccessKey,mode);}

    [Fact] public async Task Cross_site_mutation_is_rejected_without_balance_change()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("business");var before=await c.GetJson("/api/business/wallet");c.DefaultRequestHeaders.Add("Sec-Fetch-Site","cross-site");var response=await c.Post("/api/business/wallet/deposits",new{amount=12.34m,expectedVersion=before["version"]!.GetValue<long>()});Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);c.DefaultRequestHeaders.Remove("Sec-Fetch-Site");Assert.Equal(before["totalBalance"]!.ToJsonString(),(await c.GetJson("/api/business/wallet"))["totalBalance"]!.ToJsonString());}

    [Fact] public async Task Arbitrary_deposit_is_persisted_and_retry_is_idempotent()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("business");var before=await c.GetJson("/api/business/wallet");var input=new{amount=12.34m,expectedVersion=before["version"]!.GetValue<long>()};var first=await c.PostJson("/api/business/wallet/deposits",input,"deposit-http");var second=await c.PostJson("/api/business/wallet/deposits",input,"deposit-http");Assert.Equal(first.ToJsonString(),second.ToJsonString());Assert.Equal(before["totalBalance"]!.GetValue<decimal>()+12.34m,(await c.GetJson("/api/business/wallet"))["totalBalance"]!.GetValue<decimal>());}

    [Fact] public async Task Business_pricing_contains_total_sale_cost_but_no_internal_split()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("business");var p=await c.GetJson("/api/business/pricing");Assert.Equal(2,p["rows"]!.AsArray().Count);Assert.Equal(10,p["rows"]![1]!["saleCostPercent"]!.GetValue<decimal>());var text=p.ToJsonString().ToLowerInvariant();foreach(var forbidden in new[]{"creatorearning","customer","platformpercent","platformkeeps"})Assert.DoesNotContain(forbidden,text);}

    [Fact] public async Task Creator_pricing_exposes_only_own_earning_terms()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("creator");var p=await c.GetJson("/api/creator/pricing");Assert.Equal(3000,p["minimumToCashOut"]!.GetValue<decimal>());Assert.Equal(3m,p["rows"]![1]!["saleCommissionPercent"]!.GetValue<decimal>());foreach(var forbidden in new[]{"businessPays","platform","cashback"})Assert.DoesNotContain(forbidden,p.ToJsonString());}

    [Fact] public async Task Creator_discovery_uses_trusted_eligibility_and_private_fields_are_absent()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("other-creator");var rows=await c.GetJson("/api/creator/discover");Assert.Equal(2,rows.AsArray().Count);foreach(var word in new[]{"wallet","campaignBudget","platformRevenue","cashback","email","phone","privateAddress"})Assert.DoesNotContain(word,rows.ToJsonString());using var ineligible=await f.Login("ineligible-creator");Assert.Empty((await ineligible.GetJson("/api/creator/discover")).AsArray());}

    [Fact] public async Task Other_business_cannot_open_or_approve_campaign_applicants()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var own=await f.Login("business");var id=(await own.GetJson("/api/business/campaigns"))[0]!["id"]!.GetValue<string>();var details=await own.GetJson($"/api/business/campaigns/{id}");using var other=await f.Login("other-business");Assert.Equal(HttpStatusCode.Forbidden,(await other.GetAsync($"/api/business/campaigns/{id}")).StatusCode);var applicant=details["applicants"]![0]!["id"]!.GetValue<string>();Assert.Equal(HttpStatusCode.Forbidden,(await other.Post($"/api/business/applicants/{applicant}/approve",new{amount=100,version=details["campaign"]!["version"]!.GetValue<long>()})).StatusCode);}

    [Fact] public async Task Customer_offers_are_eligible_customer_facing_only_and_have_no_internal_finances()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("customer");var offers=await c.GetJson("/api/customer/offers");Assert.Single(offers.AsArray());Assert.Equal("VIEW_AND_SALE_PROMOTION",offers[0]!["source"]!.GetValue<string>());Assert.Equal(4,offers[0]!["benefitPercent"]!.GetValue<decimal>());foreach(var forbidden in new[]{"budget","earning","platformRevenue","wallet","creatorPayment","creatorsNeeded","instructions","resources"})Assert.DoesNotContain(forbidden,offers.ToJsonString());}

    [Fact] public async Task Customer_offer_business_coordinates_are_nullable_and_omit_business_identity()
    {
        await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("customer");
        var offer=Assert.Single((await c.GetJson("/api/customer/offers")).AsArray());
        Assert.Null(offer!["business"]!["latitude"]);
        Assert.Null(offer["business"]!["longitude"]);
        Assert.Null(offer["business"]!["businessId"]);
    }

    [Fact] public async Task Customer_transactions_and_cashback_are_self_scoped_and_safe_no_store_reads()
    {
        await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("customer");
        var transactionsResponse=await c.GetAsync("/api/customer/transactions");
        Assert.Equal("no-store",transactionsResponse.Headers.CacheControl!.ToString());
        var transactions=JsonNode.Parse(await transactionsResponse.Content.ReadAsStringAsync())!;
        var transaction=Assert.Single(transactions.AsArray());
        Assert.Equal("VIEW_AND_SALE_PROMOTION",transaction!["source"]!.GetValue<string>());
        Assert.Equal("Abc Coffee",transaction["business"]!.GetValue<string>());
        Assert.Equal("Bella",transaction["creator"]!.GetValue<string>());
        Assert.NotNull(transaction["purchaseAmount"]);
        var cashbackResponse=await c.GetAsync("/api/customer/cashback");
        Assert.Equal("no-store",cashbackResponse.Headers.CacheControl!.ToString());
        var cashback=JsonNode.Parse(await cashbackResponse.Content.ReadAsStringAsync())!;
        Assert.NotNull(cashback["availableCashback"]);Assert.NotNull(cashback["minimumCashOut"]);
        Assert.NotNull(cashback["remainingToCashOut"]);Assert.NotNull(cashback["payoutHistory"]);
        var forbidden=new[]{"CustomerId","CreatorId","BusinessId","CreatorAllocationId","JournalId","CorrelationId","IdempotencyKey","PlatformRevenue","Commission","PlatformFee","Reference"};
        foreach(var field in forbidden)Assert.DoesNotContain(field,(transactions.ToJsonString()+cashback.ToJsonString()),StringComparison.OrdinalIgnoreCase);
        var withUntrustedQuery=await c.GetJson("/api/customer/cashback?customerId=00000000-0000-0000-0000-000000000001");
        Assert.Equal(cashback.ToJsonString(),withUntrustedQuery.ToJsonString());
        Assert.Equal("camera=(self), microphone=(), geolocation=(self), payment=(), usb=()",cashbackResponse.Headers.GetValues("Permissions-Policy").Single());
    }

    [Fact] public async Task Operations_admin_can_use_operational_areas_but_not_platform_configuration()
    {
        await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("operations-admin");
        foreach(var path in new[]{"/api/admin/home","/api/admin/promotions","/api/admin/ugc","/api/admin/businesses","/api/admin/creators","/api/admin/payouts","/api/admin/notifications"})
            Assert.Equal(HttpStatusCode.OK,(await c.GetAsync(path)).StatusCode);
        foreach(var path in new[]{"/api/admin/accounts","/api/admin/financial-settings","/api/admin/audit","/api/admin/reconciliation"})
            Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(path)).StatusCode);
    }

    [Fact] public async Task Operations_admin_cannot_mutate_admin_grants_financial_settings_or_platform_money()
    {
        await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("operations-admin");
        Assert.Equal(HttpStatusCode.Forbidden,(await c.Post("/api/admin/accounts",new{email="verified@example.com",role="OperationsAdmin"})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.Post("/api/admin/financial-settings",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.Post("/api/admin/platform/settlements",new{amount=1,reference="forbidden"})).StatusCode);
    }

    [Theory]
    [InlineData("customer")][InlineData("creator")][InlineData("business")][InlineData("cashier")]
    public async Task Normal_profiles_cannot_access_new_admin_product_endpoints(string persona)
    {
        await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login(persona);
        foreach(var path in new[]{"/api/admin/home","/api/admin/audit","/api/admin/promotions","/api/admin/ugc","/api/admin/accounts","/api/admin/financial-settings"})
            Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.Post("/api/admin/accounts",new{email="verified@example.com",role="OperationsAdmin"})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.Post("/api/admin/financial-settings",new{})).StatusCode);
    }

    [Fact] public async Task Sensitive_responses_are_no_store_and_camera_policy_is_scoped()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("customer");var response=await c.GetAsync("/api/customer/offers");Assert.Equal("no-store",response.Headers.CacheControl!.ToString());Assert.Equal("camera=(self), microphone=(), geolocation=(self), payment=(), usb=()",response.Headers.GetValues("Permissions-Policy").Single());}

    [Fact] public async Task Disabled_account_loses_access_even_with_existing_cookie()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("creator");await using var db=f.Database.Open();await db.Database.ExecuteSqlRawAsync("UPDATE v3.\"CommercePermissions\" SET \"IsActive\" = false WHERE \"Role\" = 'Creator'");Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/creator/home")).StatusCode);}
}
