using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class AccountDeletionHttpTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Platform_role_deletion_denies_stale_business_and_cashier_sessions_without_removing_financial_history()
    {
        await using var fixture = await ApiFixture.CreateAsync(postgres);
        using var admin = await fixture.Login("admin");
        using var business = await fixture.Login("business");
        using var cashier = await fixture.Login("cashier");
        var user = DevelopmentDirectory.Id(2);
        await using var db = fixture.Database.Open();
        var journalBefore = await db.FinancialJournals.CountAsync();
        var walletBefore = await db.BusinessWallets.SingleAsync(x => x.BusinessId == DevelopmentDirectory.Id(100));
        var beforeAmount = walletBefore.AvailableBalance.Amount;
        Assert.Equal(HttpStatusCode.OK, (await business.GetAsync("/api/business/wallet")).StatusCode);
        await admin.PostJson($"/api/admin/accounts/{user}/roles/delete", new { role = "Business", subjectId = DevelopmentDirectory.Id(100), reason = "Approved policy deletion" }, "delete-business-role");
        Assert.Equal(HttpStatusCode.Forbidden, (await business.GetAsync("/api/business/wallet")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/checkout/recent")).StatusCode);
        Assert.Equal(journalBefore, await db.FinancialJournals.CountAsync());
        Assert.Equal(beforeAmount, (await db.BusinessWallets.SingleAsync(x => x.BusinessId == DevelopmentDirectory.Id(100))).AvailableBalance.Amount);
    }

    [Theory]
    [InlineData("operations-admin")][InlineData("customer")][InlineData("creator")][InlineData("business")][InlineData("cashier")]
    public async Task Other_roles_cannot_call_role_or_entire_account_deletion(string alias)
    {
        await using var fixture = await ApiFixture.CreateAsync(postgres);
        using var caller = await fixture.Login(alias);
        var user = DevelopmentDirectory.Id(7);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.Post($"/api/admin/accounts/{user}/roles/delete",
            new { role = "Customer", reason = "Unauthorized" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.Post($"/api/admin/accounts/{user}/delete-entire",
            new { reason = "Unauthorized", confirmation = "DELETE" })).StatusCode);
    }

    [Fact]
    public async Task Entire_deletion_fails_closed_when_external_identity_provider_is_not_configured()
    {
        await using var fixture = await ApiFixture.CreateAsync(postgres);
        using var admin = await fixture.Login("admin");
        var user = DevelopmentDirectory.Id(7);
        var response = await admin.Post($"/api/admin/accounts/{user}/delete-entire",
            new { reason = "Pilot cleanup", confirmation = "DELETE" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = fixture.Database.Open();
        Assert.True(await db.CommercePermissions.AnyAsync(x => x.UserId == user && x.IsActive));
        Assert.False(await db.OutboxMessages.AnyAsync(x => x.EventType == "AccountIdentityDeletion"));
    }

    [Fact]
    public async Task Platform_entire_deletion_reconciles_external_identity_and_releases_email_once()
    {
        var provider = new FakeIdentityDeletionProvider();
        await using var fixture = await ApiFixture.CreateAsync(postgres, builder =>
            builder.Services.AddSingleton<IAccountIdentityDeletionProvider>(provider));
        using var admin = await fixture.Login("admin");
        using var customer = await fixture.Login("customer");
        var user = DevelopmentDirectory.Id(7);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("reusable-pilot-test@example.test"))).ToLowerInvariant();
        await using var db = fixture.Database.Open();
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = user, Kind = "Email", IdentifierHash = hash,
            DeliveryAddress = "reusable-pilot-test@example.test", IsVerified = true, CreatedAtUtc = DateTime.UtcNow });
        db.IdentityBindings.Add(new IdentityBinding { UserId = user, ProjectId = "isolated-v3-test",
            ExternalSubject = "fixture-external-user", IsActive = true, Version = 1 });
        await db.SaveChangesAsync();
        var journals = await db.FinancialJournals.CountAsync();
        var response = await admin.PostJson($"/api/admin/accounts/{user}/delete-entire",
            new { reason = "Eligible test account cleanup", confirmation = "DELETE" }, "delete-entire-http");
        Assert.Equal("Pending", response["status"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync("/api/customer/offers")).StatusCode);
        await new AccountIdentityDeletionProcessor(db, provider, TimeProvider.System).ProcessAsync(default);
        db.ChangeTracker.Clear();
        var status = "Pending";
        for (var attempt = 0; attempt < 30 && status != "Completed"; attempt++)
        {
            status = (await admin.GetJson($"/api/admin/accounts/{user}/deletion-status"))["status"]!.GetValue<string>();
            if (status != "Completed") await Task.Delay(100);
        }
        Assert.Equal("Completed", status);
        Assert.Equal(1, provider.Calls);
        Assert.NotEqual(hash, (await db.AuthIdentifiers.SingleAsync(x => x.UserId == user && x.Kind == "Email")).IdentifierHash);
        Assert.Equal(journals, await db.FinancialJournals.CountAsync());
        Assert.Equal("Completed", (await admin.PostJson($"/api/admin/accounts/{user}/delete-entire",
            new { reason = "Eligible test account cleanup", confirmation = "DELETE" }, "delete-entire-http"))["status"]!.GetValue<string>());
        Assert.Single(await db.OutboxMessages.Where(x => x.EventType == "AccountIdentityDeletion").ToListAsync());
    }

    private sealed class FakeIdentityDeletionProvider : IAccountIdentityDeletionProvider
    {
        public bool Enabled => true;
        public int Calls { get; private set; }
        public Task DeleteAsync(string projectId, string externalSubject, CancellationToken ct)
        {
            Assert.Equal("isolated-v3-test", projectId);
            Assert.Equal("fixture-external-user", externalSubject);
            Calls++;
            return Task.CompletedTask;
        }
    }
}
