using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
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
    public async Task Self_service_closes_only_owned_selected_role_and_keeps_current_session()
    {
        await using var fixture = await ApiFixture.CreateAsync(postgres);
        using var customer = await fixture.Login("customer");
        var userId = DevelopmentDirectory.Id(7); var creatorId = Guid.NewGuid();
        await using (var db = fixture.Database.Open())
        {
            db.CommercePermissions.Add(new(userId, Weymela.Application.ActorRole.Creator, creatorId, null, true, false));
            db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = creatorId,
                Role = Weymela.Application.ActorRole.Creator, PublicId = "CR-HTTP-CLOSE", DisplayName = "Disposable Creator" });
            await db.SaveChangesAsync();
        }

        var overview = await customer.GetJson("/api/account/closure");
        Assert.Equal(2, overview["roles"]!.AsArray().Count);
        var result = await customer.PostJson("/api/account/closure",
            new { role = "Creator", subjectId = creatorId, confirmation = "DELETE" }, "self-close-http");
        Assert.Equal("Closed", result["status"]!.GetValue<string>());
        Assert.Equal(1, result["remainingRoles"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync("/api/customer/offers")).StatusCode);

        await using var verify = fixture.Database.Open();
        Assert.False((await verify.CommercePermissions.SingleAsync(x => x.UserId == userId
            && x.Role == Weymela.Application.ActorRole.Creator)).IsActive);
        Assert.True((await verify.CommercePermissions.SingleAsync(x => x.UserId == userId
            && x.Role == Weymela.Application.ActorRole.Customer)).IsActive);
        Assert.Single(await verify.AccountRoleHistory.Where(x => x.TargetUserId == userId && x.Action == "Deleted").ToListAsync());
    }

    [Fact]
    public async Task Closing_the_current_role_reissues_the_session_for_a_remaining_role()
    {
        await using var fixture = await ApiFixture.CreateAsync(postgres);
        using var cashier = await fixture.Login("cashier");
        var userId = DevelopmentDirectory.Id(9); var businessId = DevelopmentDirectory.Id(100);
        await using (var db = fixture.Database.Open())
        {
            var now = DateTime.UtcNow;
            db.CommercePermissions.Add(new(userId, Weymela.Application.ActorRole.Business, businessId, businessId, true, true));
            db.CashierPreauthorizations.Add(new(businessId, "Abc Checkout", "+251911000009",
                "fixture-phone-hash", "fixture-code-hash", now.AddDays(1), now)
            {
                Status = CashierPreauthorizationStatus.Active,
                ActivatedAtUtc = now,
                UserId = userId
            });
            if (!await db.PublicWorkspaceProfiles.AnyAsync(x => x.SubjectId == businessId
                && x.Role == Weymela.Application.ActorRole.Business))
                db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = businessId,
                    Role = Weymela.Application.ActorRole.Business, PublicId = "BUS-100", DisplayName = "Abc Coffee" });
            await db.SaveChangesAsync();
        }

        var overview = await cashier.GetJson("/api/account/closure");
        var current = overview["roles"]!.AsArray().Single(x => x!["role"]!.GetValue<string>() == "Cashier")!;
        var response = await cashier.Post("/api/account/closure", new {
            role = "Cashier", subjectId = current["subjectId"]!.GetValue<Guid>(), confirmation = "DELETE"
        }, "close-current-role-http");
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Closed", result["status"]!.GetValue<string>());
        Assert.Equal("Business", result["nextRole"]!.GetValue<string>());
        cashier.DefaultRequestHeaders.Remove("Cookie");
        cashier.DefaultRequestHeaders.Add("Cookie", response.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/business/wallet")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/customer/offers")).StatusCode);
        var session = await cashier.GetJson("/api/session");
        Assert.Equal("Business", session["role"]!.GetValue<string>());
        await using var verify = fixture.Database.Open();
        Assert.Equal(CashierPreauthorizationStatus.Revoked,
            (await verify.CashierPreauthorizations.SingleAsync(x => x.UserId == userId)).Status);
    }

    [Fact]
    public async Task Self_service_rejects_foreign_subject_and_last_role_fails_closed_without_provider()
    {
        await using var fixture = await ApiFixture.CreateAsync(postgres);
        using var customer = await fixture.Login("customer");
        using var anonymous = fixture.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/account/closure")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.Post("/api/account/closure",
            new { role = "Customer", subjectId = Guid.NewGuid(), confirmation = "DELETE" })).StatusCode);

        var overview = await customer.GetJson("/api/account/closure");
        var own = overview["roles"]!.AsArray().Single()!;
        Assert.Equal("ActionRequired", own["status"]!.GetValue<string>());
        var pending = await customer.PostJson("/api/account/closure", new {
            role = own["role"]!.GetValue<string>(), subjectId = own["subjectId"]!.GetValue<Guid>(), confirmation = "DELETE"
        }, "last-role-provider-unavailable");
        Assert.Equal("Pending", pending["status"]!.GetValue<string>());

        await using var db = fixture.Database.Open();
        Assert.True((await db.CommercePermissions.SingleAsync(x => x.UserId == DevelopmentDirectory.Id(7))).IsActive);
        Assert.False(await db.OutboxMessages.AnyAsync(x => x.EventType == "AccountIdentityDeletion"));
    }

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
