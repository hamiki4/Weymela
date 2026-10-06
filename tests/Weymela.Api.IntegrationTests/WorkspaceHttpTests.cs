using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Tests;
using Weymela.Infrastructure.Web;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class WorkspaceHttpTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Full_email_is_limited_to_authorized_admin_account_and_review_projections()
    {
        await using var fixture = await ApiFixture.CreateAsync(postgres);
        var directory = fixture.App.Services.GetRequiredService<DevelopmentDirectory>();
        static string Email(string alias) => $"private-{alias}@example.test";
        await using (var db = fixture.Database.Open())
        {
            foreach (var alias in new[] { "customer", "creator", "business" })
                db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = directory.Get(alias).Actor.UserId,
                    Kind = "Email", IdentifierHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Email(alias)))).ToLowerInvariant(),
                    DeliveryAddress = Email(alias), IsVerified = true, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await new RoleEnrollmentService(db, TimeProvider.System).SubmitAsync(directory.Get("customer").Actor,
                new RoleEnrollmentRequest(ActorRole.Creator, "Review applicant", null, "Addis", "Food", "Profile request",
                    SocialProfiles: [new("TikTok", "https://www.tiktok.com/@reviewapplicant")]),
                "private-email-review", default);
        }

        using var platform = await fixture.Login("admin");
        var customerId = directory.Get("customer").Actor.UserId;
        var accounts = (await platform.GetJson("/api/admin/accounts?role=Customer")).AsArray();
        var account = accounts.Single(x => x!["userId"]!.GetValue<Guid>() == customerId)!;
        Assert.Equal(Email("customer"), account["fullEmail"]!.GetValue<string>());
        Assert.NotEqual(Email("customer"), account["safeIdentifier"]!.GetValue<string>());
        var search = (await platform.GetJson($"/api/admin/accounts?role=Customer&search={Uri.EscapeDataString(Email("customer"))}")).AsArray();
        Assert.Contains(search, x => x!["userId"]!.GetValue<Guid>() == customerId);
        var detail = await platform.GetJson($"/api/admin/accounts/{customerId}?role=Customer");
        Assert.Equal(Email("customer"), detail["account"]!["fullEmail"]!.GetValue<string>());
        Assert.DoesNotContain(Email("customer"), detail["transactions"]!.ToJsonString());

        using var operations = await fixture.Login("operations-admin");
        foreach (var (path, alias) in new[] {
            ("/api/admin/customers", "customer"), ("/api/admin/creators", "creator"),
            ("/api/admin/businesses", "business") })
            Assert.Contains((await operations.GetJson(path)).AsArray(), x => x!["fullEmail"]?.GetValue<string>() == Email(alias));
        Assert.Contains((await operations.GetJson("/api/admin/role-enrollments")).AsArray(),
            x => x!["fullEmail"]?.GetValue<string>() == Email("customer"));
        Assert.Equal(HttpStatusCode.Forbidden, (await operations.GetAsync("/api/admin/accounts?role=Customer")).StatusCode);

        foreach (var alias in new[] { "customer", "creator", "business", "cashier" })
        {
            using var nonAdmin = await fixture.Login(alias);
            foreach (var path in new[] { "/api/admin/accounts?role=Customer", "/api/admin/customers",
                "/api/admin/creators", "/api/admin/businesses", "/api/admin/role-enrollments" })
                Assert.Equal(HttpStatusCode.Forbidden, (await nonAdmin.GetAsync(path)).StatusCode);
        }
        using var anonymous = fixture.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/customers")).StatusCode);
        using var creator = await fixture.Login("creator");
        using var customer = await fixture.Login("customer");
        foreach (var response in new[] { await creator.GetJson("/api/creator/discover"),
            await customer.GetJson("/api/customer/offers"), await customer.GetJson("/api/customer/transactions") })
        {
            var payload = response.ToJsonString();
            Assert.DoesNotContain("\"fullEmail\"", payload);
            foreach (var alias in new[] { "customer", "creator", "business" })
                Assert.DoesNotContain(Email(alias), payload);
        }
    }

    [Fact]
    public async Task Unfunded_promotion_draft_is_not_discoverable_or_requestable_and_funding_rechecks_available_balance()
    {
        await using var fixture = await ApiFixture.CreateAsync(postgres);
        using var business = await fixture.Login("business");
        using var creator = await fixture.Login("other-creator");
        var wallet = await business.GetJson("/api/business/wallet");
        var available = wallet["available"]!.GetValue<decimal>();
        var budget = available + 1000m;
        var title = "Funding gate regression";
        var created = await business.PostJson("/api/business/campaigns", new
        {
            title, description = "An original local story", type = "ViewOnly", campaignBudget = budget,
            requirements = (string?)null, category = (string?)null, region = (string?)null,
            minimumVerifiedFollowers = (long?)null, startUtc = DateTime.UtcNow.AddMinutes(-1),
            endUtc = DateTime.UtcNow.AddDays(14), platforms = Array.Empty<object>()
        });
        var id = created["id"]!.GetValue<Guid>();
        Assert.DoesNotContain((await creator.GetJson("/api/creator/discover")).AsArray(), row => row?["id"]?.GetValue<Guid>() == id);
        var request = await creator.Post($"/api/creator/campaigns/{id}/join", new { message = "Interested", contentConcept = "Local story" });
        Assert.False(request.IsSuccessStatusCode);
        var insufficient = await business.Post($"/api/business/campaigns/{id}/fund", new
        {
            campaignVersion = 0, walletVersion = wallet["version"]!.GetValue<long>()
        });
        Assert.False(insufficient.IsSuccessStatusCode);
        await using (var db = fixture.Database.Open())
        {
            var draft = await db.Promotions.SingleAsync(x => x.Id == id);
            Assert.Equal(PromotionStatus.Draft, draft.Status);
            Assert.Equal(0m, draft.ReservedBudget.Amount);
            Assert.False(await db.CreatorApplications.AnyAsync(x => x.PromotionId == id));
            Assert.False(await db.CreatorAllocations.AnyAsync(x => x.PromotionId == id));
        }
        await business.PostJson("/api/business/wallet/deposits", new
        {
            amount = 1000m, expectedVersion = wallet["version"]!.GetValue<long>()
        });
        var toppedUp = await business.GetJson("/api/business/wallet");
        await business.PostJson($"/api/business/campaigns/{id}/fund", new
        {
            campaignVersion = 0, walletVersion = toppedUp["version"]!.GetValue<long>()
        });
        Assert.DoesNotContain((await creator.GetJson("/api/creator/discover")).AsArray(), row => row?["id"]?.GetValue<Guid>() == id);
        await business.PostJson($"/api/business/campaigns/{id}/publish", new { version = 1 });
        Assert.Contains((await creator.GetJson("/api/creator/discover")).AsArray(), row => row?["id"]?.GetValue<Guid>() == id);
    }

    [Fact]
    public async Task Current_Business_agreement_does_not_block_Promotion_creation_or_funding()
    {
        using var content = new TestBusinessLegalContent();
        await using var fixture = await ApiFixture.CreateAsync(postgres, builder =>
            builder.Services.AddSingleton(new BusinessLegalDocumentSource(content.Root)));
        using var business = await fixture.Login("business");
        var version = Guid.NewGuid();
        var hash = content.Publish(version, "Business version two");
        await using (var db = fixture.Database.Open())
        {
            db.LegalDocumentVersions.Add(new(version, LegalDocumentType.BusinessAgreement, "new-current", hash, DateTime.UtcNow.AddMinutes(-1)));
            await db.SaveChangesAsync();
        }
        var input = new
        {
            title = "Agreement gate regression", description = "An original local story", type = "ViewOnly", campaignBudget = 3000m,
            requirements = (string?)null, category = (string?)null, region = (string?)null,
            minimumVerifiedFollowers = (long?)null, startUtc = DateTime.UtcNow.AddMinutes(-1),
            endUtc = DateTime.UtcNow.AddDays(14), platforms = Array.Empty<object>()
        };
        var unblocked = await business.Post("/api/business/campaigns", input, "agreement-not-current");
        Assert.True(unblocked.IsSuccessStatusCode);
        var documents = (await business.GetJson("/api/legal/current")).AsArray();
        Assert.False(documents.Single(x => x?["id"]?.GetValue<Guid>() == version)!["accepted"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.BadRequest, (await business.Post($"/api/legal/{version}/accept", new { contentHash = hash, confirmed = false })).StatusCode);
        var reviewed = await business.GetJson($"/api/legal/{version}/content");
        Assert.Equal("new-current", reviewed["version"]!.GetValue<string>());
        Assert.Equal(hash, reviewed["contentHash"]!.GetValue<string>());
        await business.PostJson($"/api/legal/{version}/accept", new { contentHash = hash, confirmed = true });
        using (var otherBusiness = await fixture.Login("other-business"))
            Assert.False((await otherBusiness.GetJson("/api/legal/current")).AsArray()
                .Single(x => x?["id"]?.GetValue<Guid>() == version)!["accepted"]!.GetValue<bool>());
        var draft = await business.PostJson("/api/business/campaigns", input, "agreement-now-current");
        var draftId = draft["id"]!.GetValue<Guid>();
        var newer = Guid.NewGuid();
        var newerHash = content.Publish(newer, "Business version three");
        await using (var db = fixture.Database.Open())
        {
            db.LegalDocumentVersions.Add(new(newer, LegalDocumentType.BusinessAgreement, "newer-current", newerHash, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var wallet = await business.GetJson("/api/business/wallet");
        var funding = new { campaignVersion = 0, walletVersion = wallet["version"]!.GetValue<long>() };
        var fundedBeforeAcceptance = await business.Post($"/api/business/campaigns/{draftId}/fund", funding, "fund-outdated-agreement");
        Assert.True(fundedBeforeAcceptance.IsSuccessStatusCode);
        await business.PostJson($"/api/legal/{newer}/accept", new { contentHash = newerHash, confirmed = true });
        Assert.True((await business.Post($"/api/business/campaigns/{draftId}/fund", funding, "fund-outdated-agreement")).IsSuccessStatusCode);
        await using (var db = fixture.Database.Open())
        {
            db.LegalDocumentVersions.Add(new(Guid.NewGuid(), LegalDocumentType.BusinessAgreement, "latest-current", "fixture-latest-hash", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        Assert.True((await business.Post($"/api/business/campaigns/{draftId}/fund", funding, "fund-outdated-agreement")).IsSuccessStatusCode);
        await using (var db = fixture.Database.Open())
        {
            Assert.Equal(2, await db.LegalAcceptances.CountAsync(x => x.UserId == DevelopmentDirectory.Id(2)
                && (x.DocumentVersionId == version || x.DocumentVersionId == newer)));
            Assert.Equal(2, await db.AuditEvents.CountAsync(x => x.ActorId == DevelopmentDirectory.Id(2)
                && x.EventType == "LegalVersionAccepted" && (x.Detail == version.ToString() || x.Detail == newer.ToString())));
        }
    }

    [Fact]
    public async Task Missing_Business_document_content_blocks_review_and_acceptance_for_every_role()
    {
        await using var fixture = await ApiFixture.CreateAsync(postgres);
        var version = Guid.NewGuid();
        await using (var db = fixture.Database.Open())
        {
            db.LegalDocumentVersions.Add(new(version, LegalDocumentType.BusinessAgreement, "unpublished-content", "sha256:" + new string('0', 64), DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        using var business = await fixture.Login("business");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await business.GetAsync($"/api/legal/{version}/content")).StatusCode);
        Assert.False((await business.Post($"/api/legal/{version}/accept", new { contentHash = "sha256:" + new string('0', 64), confirmed = true })).IsSuccessStatusCode);
        foreach (var alias in new[] { "customer", "creator", "cashier", "operations-admin", "admin" })
        {
            using var actor = await fixture.Login(alias);
            Assert.False((await actor.GetAsync($"/api/legal/{version}/content")).IsSuccessStatusCode);
            Assert.False((await actor.Post($"/api/legal/{version}/accept", new { contentHash = "sha256:" + new string('0', 64), confirmed = true })).IsSuccessStatusCode);
        }
        await using (var db = fixture.Database.Open())
        {
            Assert.False(await db.LegalAcceptances.AnyAsync(x => x.DocumentVersionId == version));
            Assert.False(await db.AuditEvents.AnyAsync(x => x.EventType == "LegalVersionAccepted" && x.Detail == version.ToString()));
        }
    }

    [Fact]
    public async Task Shared_AntiCircumvention_version_is_accepted_only_by_the_authenticated_Business_or_Creator()
    {
        using var content = new TestBusinessLegalContent();
        await using var fixture = await ApiFixture.CreateAsync(postgres, builder =>
            builder.Services.AddSingleton(new BusinessLegalDocumentSource(content.Root)));
        var version = Guid.NewGuid();
        var hash = content.Publish(version, "Shared AntiCircumvention version");
        await using (var db = fixture.Database.Open())
        {
            db.LegalDocumentVersions.Add(new(version, LegalDocumentType.AntiCircumventionAgreement,
                "shared-current", hash, DateTime.UtcNow.AddMinutes(-1)));
            await db.SaveChangesAsync();
        }

        using var business = await fixture.Login("business");
        using var creator = await fixture.Login("creator");
        using var otherCreator = await fixture.Login("other-creator");
        var businessAgreement = (await business.GetJson("/api/legal/current")).AsArray()
            .Single(document => document?["type"]?.GetValue<string>() == "BusinessAgreement")!;
        var creatorCurrent = (await creator.GetJson("/api/legal/current")).AsArray();
        Assert.DoesNotContain(creatorCurrent, document => document?["type"]?.GetValue<string>() == "BusinessAgreement");
        Assert.False(creatorCurrent.Single(document => document?["id"]?.GetValue<Guid>() == version)!["accepted"]!.GetValue<bool>());
        Assert.False((await otherCreator.GetJson("/api/legal/current")).AsArray()
            .Single(document => document?["id"]?.GetValue<Guid>() == version)!["accepted"]!.GetValue<bool>());
        var creatorContent = await creator.GetJson($"/api/legal/{version}/content");
        Assert.Equal(version, creatorContent["id"]!.GetValue<Guid>());
        Assert.Equal(hash, creatorContent["contentHash"]!.GetValue<string>());
        Assert.Equal("INTEGRATION TEST ONLY: Shared AntiCircumvention version", creatorContent["content"]!.GetValue<string>());
        Assert.False((await creator.GetAsync($"/api/legal/{businessAgreement["id"]!.GetValue<Guid>()}/content")).IsSuccessStatusCode);

        Assert.False((await creator.Post($"/api/legal/{businessAgreement["id"]!.GetValue<Guid>()}/accept",
            new { contentHash = businessAgreement["contentHash"]!.GetValue<string>(), confirmed = true })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await creator.Post($"/api/legal/{version}/accept",
            new { contentHash = hash, confirmed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await creator.Post($"/api/legal/{version}/accept",
            new { contentHash = "incorrect-hash", confirmed = true })).StatusCode);
        foreach (var alias in new[] { "customer", "cashier", "operations-admin", "admin" })
        {
            using var other = await fixture.Login(alias);
            Assert.False((await other.GetAsync($"/api/legal/{version}/content")).IsSuccessStatusCode);
            Assert.False((await other.Post($"/api/legal/{version}/accept",
                new { contentHash = hash, confirmed = true })).IsSuccessStatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await creator.Post($"/api/legal/{version}/accept",
            new { contentHash = hash, confirmed = true, userId = DevelopmentDirectory.Id(5) })).StatusCode);
        await creator.PostJson($"/api/legal/{version}/accept", new { contentHash = hash, confirmed = true });
        await business.PostJson($"/api/legal/{version}/accept", new { contentHash = hash, confirmed = true });
        Assert.True((await creator.GetJson("/api/legal/current")).AsArray()
            .Single(document => document?["id"]?.GetValue<Guid>() == version)!["accepted"]!.GetValue<bool>());
        Assert.False((await otherCreator.GetJson("/api/legal/current")).AsArray()
            .Single(document => document?["id"]?.GetValue<Guid>() == version)!["accepted"]!.GetValue<bool>());

        await using var verify = fixture.Database.Open();
        Assert.Equal(2, await verify.LegalAcceptances.CountAsync(acceptance => acceptance.DocumentVersionId == version));
        Assert.True(await verify.LegalAcceptances.AnyAsync(acceptance => acceptance.DocumentVersionId == version
            && acceptance.UserId == DevelopmentDirectory.Id(4) && acceptance.Role == LegalRole.Creator));
        Assert.True(await verify.LegalAcceptances.AnyAsync(acceptance => acceptance.DocumentVersionId == version
            && acceptance.UserId == DevelopmentDirectory.Id(2) && acceptance.Role == LegalRole.Business));
        Assert.False(await verify.LegalAcceptances.AnyAsync(acceptance => acceptance.DocumentVersionId == version
            && acceptance.UserId == DevelopmentDirectory.Id(5)));
        Assert.Equal(1, await verify.AuditEvents.CountAsync(audit => audit.EventType == "LegalVersionAccepted"
            && audit.Detail == version.ToString() && audit.ActorId == DevelopmentDirectory.Id(4)
            && audit.CreatorId == DevelopmentDirectory.Id(300) && audit.BusinessId == null));
        Assert.Equal(1, await verify.AuditEvents.CountAsync(audit => audit.EventType == "LegalVersionAccepted"
            && audit.Detail == version.ToString() && audit.ActorId == DevelopmentDirectory.Id(2)
            && audit.BusinessId == DevelopmentDirectory.Id(100) && audit.CreatorId == null));
    }

    private sealed class TestBusinessLegalContent : IDisposable
    {
        private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("weymela-legal-test-");
        public string Root => root.FullName;
        public string Publish(Guid id, string marker)
        {
            var folder = Directory.CreateDirectory(Path.Combine(Root, "BusinessLegal"));
            // These bytes exist only in this disposable integration-test directory.
            var bytes = Encoding.UTF8.GetBytes("INTEGRATION TEST ONLY: " + marker);
            File.WriteAllBytes(Path.Combine(folder.FullName, $"{id:N}.txt"), bytes);
            return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        public void Dispose() => root.Delete(true);
    }

    [Theory]
    [InlineData("customer", "Customer")]
    [InlineData("creator", "Creator")]
    [InlineData("business", "Business")]
    public async Task Own_profile_returns_only_current_role_and_safe_fields(string persona, string role)
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        using var client = await f.Login(persona);
        var profile = await client.GetJson("/api/profile");
        Assert.Equal(role, profile["role"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(profile["displayName"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(profile["publicId"]!.GetValue<string>()));
        Assert.Equal("Active", profile["status"]!.GetValue<string>());
        foreach (var secret in new[] { "token", "secret", "credential", "password", "identifierHash" })
            Assert.DoesNotContain(secret, profile.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        if (role != "Business")
        {
            Assert.Null(profile["businessType"]?.GetValue<string>());
            Assert.Null(profile["region"]?.GetValue<string>());
        }
    }

    [Fact]
    public async Task Profile_endpoint_denies_internal_roles()
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        using var admin = await f.Login("admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/profile")).StatusCode);
    }

    [Fact]
    public async Task Creator_self_profile_returns_database_assigned_number_and_keeps_long_public_id_for_existing_contracts()
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        await using (var db = f.Database.Open())
        {
            db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile
            {
                SubjectId = DevelopmentDirectory.Id(300), Role = Weymela.Application.ActorRole.Creator,
                DisplayName = "Bella", PublicId = "CR-EXISTING-CHECKOUT"
            });
            await db.SaveChangesAsync();
        }
        using var creator = await f.Login("creator");
        var profile = await creator.GetJson("/api/profile");
        var creatorId = profile["creatorId"]!.GetValue<long>();
        Assert.True(creatorId >= 1000);
        Assert.Equal("CR-EXISTING-CHECKOUT", profile["publicId"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await creator.PutAsJsonAsync("/api/profile", new { creatorId = 9999 })).StatusCode);
        using var otherCreator = await f.Login("other-creator");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await otherCreator.PutAsJsonAsync("/api/profile", new { creatorId = 9999 })).StatusCode);
        Assert.Equal(creatorId, (await creator.GetJson("/api/profile"))["creatorId"]!.GetValue<long>());
        using var customer = await f.Login("customer");
        Assert.Null((await customer.GetJson("/api/profile"))["creatorId"]?.GetValue<long>());
    }

    [Fact]
    public async Task Creator_social_accounts_are_self_scoped_and_expose_no_credentials()
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        await using (var db = f.Database.Open())
        {
            var now = DateTime.UtcNow;
            var creatorTikTok = await db.CreatorSocialProfiles.SingleAsync(x =>
                x.CreatorId == DevelopmentDirectory.Id(300) && x.Platform == CreatorPlatform.TikTok);
            creatorTikTok.ProfileUrl = "https://www.tiktok.com/@bella";
            creatorTikTok.SelfReportedAudience = 42000;
            creatorTikTok.VerificationStatus = "Verified";
            creatorTikTok.VerifiedAudience = 40000;
            creatorTikTok.AudienceVerificationSource = SocialAudienceEligibility.AdminVerified;
            creatorTikTok.UpdatedAtUtc = now;
            db.CreatorSocialProfiles.AddRange(
                new CreatorSocialProfileRecord { CreatorId = DevelopmentDirectory.Id(300), Platform = CreatorPlatform.Instagram,
                    ProfileUrl = "https://www.instagram.com/bella", IsActive = false, CreatedAtUtc = now, UpdatedAtUtc = now },
                new CreatorSocialProfileRecord { CreatorId = DevelopmentDirectory.Id(400), Platform = CreatorPlatform.YouTube,
                    ProfileUrl = "https://www.youtube.com/@elias", CreatedAtUtc = now, UpdatedAtUtc = now });
            await db.SaveChangesAsync();
        }

        using var anonymous = f.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/creator/social-accounts")).StatusCode);
        using var customer = await f.Login("customer");
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/creator/social-accounts")).StatusCode);
        using var creator = await f.Login("creator");
        var response = await creator.GetAsync("/api/creator/social-accounts?creatorId=" + DevelopmentDirectory.Id(400));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var profile = Assert.Single(JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray())!;
        Assert.Equal("TikTok", profile["platform"]!.GetValue<string>());
        Assert.Equal("https://www.tiktok.com/@bella", profile["profileUrl"]!.GetValue<string>());
        Assert.Equal("Verified", profile["verificationStatus"]!.GetValue<string>());
        Assert.Equal(7, profile.AsObject().Count);
        foreach (var secret in new[] { "token", "secret", "credential", "password" })
            Assert.DoesNotContain(secret, profile.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

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
    [InlineData("admin","/api/admin/accounts")]
    [InlineData("admin","/api/admin/wallets")]
    [InlineData("admin","/api/admin/ugc/finance")]
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

    [Fact]
    public async Task Platform_admin_read_projections_keep_current_balances_separate_from_period_activity()
    {
        await using var f = await ApiFixture.CreateAsync(postgres); using var admin = await f.Login("admin");
        var wallets = await admin.GetJson("/api/admin/wallets");
        Assert.NotEmpty(wallets["businesses"]!.AsArray());
        Assert.Contains(wallets["businesses"]!.AsArray(), x => x?["totalBalance"]?.GetValue<decimal>() > 0);
        Assert.NotNull((await admin.GetJson("/api/admin/ugc/finance")).AsArray());
        var future = await admin.GetJson("/api/admin/reports?from=2050-01-01&to=2050-01-07");
        Assert.Equal(0m, future["activity"]!["businessDeposits"]!.GetValue<decimal>());
        Assert.Equal(0m, future["activity"]!["platformRevenue"]!.GetValue<decimal>());
        Assert.Equal(0, future["accounts"]!["newAccountsInPeriod"]!.GetValue<int>());
        Assert.True(future["currentBusinessWalletBalance"]!.GetValue<decimal>() > 0);
        Assert.Equal(2, future["promotions"]!.AsArray().Count);
        Assert.Equal(2, future["ugc"]!.AsArray().Count);
        Assert.Empty(future["purchases"]!.AsArray());
    }

    [Theory]
    [InlineData("operations-admin")][InlineData("customer")][InlineData("creator")][InlineData("business")][InlineData("cashier")]
    public async Task Platform_admin_read_projections_deny_other_roles(string persona)
    {
        await using var f = await ApiFixture.CreateAsync(postgres); using var client = await f.Login(persona);
        foreach (var path in new[] { "/api/admin/wallets", "/api/admin/ugc/finance", "/api/admin/reports?from=2026-01-01&to=2026-01-31" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Platform_admin_promotional_funding_is_distinct_and_business_visible()
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        using var admin = await f.Login("admin"); using var business = await f.Login("business");
        var businessId = DevelopmentDirectory.Id(100);
        var path = $"/api/admin/accounts/businesses/{businessId:D}/promotional-funding";
        var before = (await business.GetJson("/api/business/wallet"))["available"]!.GetValue<decimal>();
        var first = await admin.PostJson(path, new { amount = 5000m, reason = "Launch promotion support" }, "admin-funding-http");
        var replay = await admin.PostJson(path, new { amount = 5000m, reason = "Launch promotion support" }, "admin-funding-http");
        Assert.Equal(first["id"]!.GetValue<string>(), replay["id"]!.GetValue<string>());
        Assert.Equal(first["journalId"]!.GetValue<string>(), replay["journalId"]!.GetValue<string>());
        Assert.Equal("Weymela Admin", first["platformAdminDisplayName"]!.GetValue<string>());
        Assert.Equal(5000m, first["amount"]!.GetValue<decimal>());
        var wallet = await business.GetJson("/api/business/wallet");
        Assert.Equal(before + 5000m, wallet["available"]!.GetValue<decimal>());
        Assert.Contains(wallet["history"]!.AsArray(), x => x?["label"]?.GetValue<string>() == "Promotional funds from Weymela"
            && x["reason"]?.GetValue<string>() == "Launch promotion support");
        Assert.Single((await admin.GetJson(path)).AsArray());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post(path, new { amount = 5001m, reason = "Launch promotion support" }, "admin-funding-http")).StatusCode);
        Assert.Empty((await admin.GetJson("/api/admin/reconciliation")).AsArray());
    }

    [Theory]
    [InlineData("operations-admin")] [InlineData("business")] [InlineData("creator")]
    [InlineData("customer")] [InlineData("cashier")]
    public async Task Promotional_funding_endpoints_deny_other_roles(string persona)
    {
        await using var f = await ApiFixture.CreateAsync(postgres); using var client = await f.Login(persona);
        var path = $"/api/admin/accounts/businesses/{DevelopmentDirectory.Id(100):D}/promotional-funding";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.Post(path, new { amount = 12m, reason = "not allowed" })).StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/view-as/start")]
    [InlineData("/api/admin/view-as/current")]
    [InlineData("/api/admin/view-as/end")]
    [InlineData("/api/admin/audit")]
    public async Task Retired_admin_product_routes_are_not_mapped(string path)
    {
        await using var f = await ApiFixture.CreateAsync(postgres); using var c = await f.Login("admin");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(path)).StatusCode);
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
        foreach(var path in new[]{"/api/admin/operations/home","/api/admin/promotions","/api/admin/ugc","/api/admin/businesses","/api/admin/creators","/api/admin/customers","/api/admin/payouts","/api/admin/notifications","/api/admin/role-enrollments"})
            Assert.Equal(HttpStatusCode.OK,(await c.GetAsync(path)).StatusCode);
        var operationalDetails=(await c.GetJson("/api/admin/operations")).ToJsonString();
        foreach(var forbidden in new[]{"financialWritesEnabled","depositMode","socialMode"})
            Assert.DoesNotContain(forbidden,operationalDetails,StringComparison.OrdinalIgnoreCase);
        foreach(var path in new[]{"/api/admin/home","/api/admin/accounts","/api/admin/financial-settings","/api/admin/platform","/api/admin/reconciliation"})
            Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(path)).StatusCode);
    }

    [Fact] public async Task Operations_admin_receives_safe_operational_projections_without_platform_financial_totals()
    {
        await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("operations-admin");
        var businesses=(await c.GetJson("/api/admin/businesses")).ToJsonString();
        var creators=(await c.GetJson("/api/admin/creators")).ToJsonString();
        var payouts=(await c.GetJson("/api/admin/payouts")).ToJsonString();
        foreach(var forbidden in new[]{"totalBalance","availableBalance","reservedBalance","platformAccrued","platformSettled","platformUnsettled","PlatformSettled"})
            Assert.DoesNotContain(forbidden,businesses+creators+payouts,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Platform",payouts,StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public async Task Operations_admin_cannot_mutate_admin_grants_financial_settings_or_platform_money()
    {
        await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("operations-admin");
        foreach (var role in new[] { "Customer", "Creator", "Business", "OperationsAdmin", "PlatformAdmin" })
            Assert.Equal(HttpStatusCode.Forbidden,(await c.Post("/api/admin/accounts/preauthorize",new{email="verified@example.com",displayName="Target",role})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.Post($"/api/admin/accounts/{DevelopmentDirectory.Id(7)}/lifecycle",new{action="close",reason="forbidden",confirmClose=true})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.Post($"/api/admin/accounts/{DevelopmentDirectory.Id(1)}/revoke",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.Post("/api/admin/financial-settings",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.Post("/api/admin/platform/settlements",new{amount=1,reference="forbidden"})).StatusCode);
    }

    [Theory]
    [InlineData("customer")][InlineData("creator")][InlineData("business")][InlineData("cashier")]
    public async Task Normal_profiles_cannot_access_new_admin_product_endpoints(string persona)
    {
        await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login(persona);
        foreach(var path in new[]{"/api/admin/home","/api/admin/promotions","/api/admin/ugc","/api/admin/accounts","/api/admin/financial-settings"})
            Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.Post("/api/admin/accounts/preauthorize",new{email="verified@example.com",displayName="Target",role="OperationsAdmin"})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.Post("/api/admin/financial-settings",new{})).StatusCode);
    }

    [Fact] public async Task Sensitive_responses_are_no_store_and_camera_policy_is_scoped()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("customer");var response=await c.GetAsync("/api/customer/offers");Assert.Equal("no-store",response.Headers.CacheControl!.ToString());Assert.Equal("camera=(self), microphone=(), geolocation=(self), payment=(), usb=()",response.Headers.GetValues("Permissions-Policy").Single());}

    [Fact] public async Task Disabled_account_loses_access_even_with_existing_cookie()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("creator");await using var db=f.Database.Open();await db.Database.ExecuteSqlRawAsync("UPDATE v3.\"CommercePermissions\" SET \"IsActive\" = false WHERE \"Role\" = 'Creator'");Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/creator/home")).StatusCode);}

    [Fact]
    public async Task Account_lifecycle_blocks_old_and_new_sessions_and_keeps_history_after_close()
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        using var admin = await f.Login("admin"); using var customer = await f.Login("customer");
        var target = DevelopmentDirectory.Id(7);
        await using var db = f.Database.Open();
        var financialBefore = await db.FinancialJournals.CountAsync();
        var transactionBefore = await db.VerifiedSales.CountAsync();
        var profileBefore = await db.CustomerProfiles.CountAsync(x => x.UserId == target);
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync("/api/session")).StatusCode);

        await admin.PostJson($"/api/admin/accounts/{target}/lifecycle", new { action = "lock", reason = "security review" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync("/api/customer/offers")).StatusCode);
        using (var fresh = f.Anonymous())
            Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.PostAsJsonAsync("/api/development/session", new { alias = "customer", accessKey = f.AccessKey })).StatusCode);

        await admin.PostJson($"/api/admin/accounts/{target}/lifecycle", new { action = "unlock", reason = "review complete" });
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync("/api/session")).StatusCode);
        await admin.PostJson($"/api/admin/accounts/{target}/lifecycle", new { action = "deactivate", reason = "inactive account" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync("/api/session")).StatusCode);
        await admin.PostJson($"/api/admin/accounts/{target}/lifecycle", new { action = "reactivate", reason = "return approved" });
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync("/api/session")).StatusCode);

        var unconfirmed = await admin.Post($"/api/admin/accounts/{target}/lifecycle", new { action = "close", reason = "account owner request" });
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        await admin.PostJson($"/api/admin/accounts/{target}/lifecycle", new { action = "close", reason = "account owner request", confirmClose = true });
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync("/api/customer/transactions")).StatusCode);
        Assert.Equal(AccountLifecycleStatus.Closed, (await db.AccountLifecycles.SingleAsync(x => x.UserId == target)).Status);
        Assert.False((await db.CommercePermissions.SingleAsync(x => x.UserId == target && x.Role == Weymela.Application.ActorRole.Customer)).IsActive);
        var closedDetail = await admin.GetJson($"/api/admin/accounts/{target}?role=Customer");
        Assert.Equal("Closed", closedDetail["account"]!["status"]!.GetValue<string>());
        Assert.False(closedDetail["account"]!["canManage"]!.GetValue<bool>());
        Assert.Equal(profileBefore, await db.CustomerProfiles.CountAsync(x => x.UserId == target));
        Assert.Equal(financialBefore, await db.FinancialJournals.CountAsync());
        Assert.Equal(transactionBefore, await db.VerifiedSales.CountAsync());
        Assert.Contains(await db.AccountRoleHistory.Where(x => x.TargetUserId == target).ToListAsync(), x => x.Action == "Closed");
    }
}
