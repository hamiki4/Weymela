using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class RoleApplicationHttpTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Customer_creator_application_requires_social_link_and_operations_review_activates_role()
    {
        await using var host = await ApiFixture.CreateAsync(fixture);
        using var customer = await host.Login("customer");
        using var operations = await host.Login("operations-admin");
        var creatorActionsBefore = (await operations.GetJson("/api/admin/action-counts"))["creators"]!.GetValue<int>();
        var missing = await customer.Post("/api/onboarding/profile", new { role = "Creator", displayName = "Hana" }, "creator-missing-social");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var invalid = await customer.Post("/api/onboarding/profile", new { role = "Creator", displayName = "Hana",
            socialProfiles = new[] { new { platform = "TikTok", profileUrl = "https://example.com/@hana" } } }, "creator-invalid-social");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var application = new { role = "Creator", displayName = "Hana", category = "Food", region = "Addis",
            socialProfiles = new[] { new { platform = "TikTok", profileUrl = "https://www.tiktok.com/@hana" } } };
        var pending = await customer.PostJson("/api/onboarding/profile", application, "creator-application");
        var replay = await customer.PostJson("/api/onboarding/profile", application, "creator-application");
        Assert.Equal(pending["id"]!.GetValue<Guid>(), replay["id"]!.GetValue<Guid>());
        Assert.Equal((int)RoleEnrollmentStatus.Pending, pending["status"]!.GetValue<int>());
        Assert.Equal(creatorActionsBefore + 1, (await operations.GetJson("/api/admin/action-counts"))["creators"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.NoContent, (await operations.Post("/api/notifications/read-all", new { })).StatusCode);
        Assert.Equal(creatorActionsBefore + 1, (await operations.GetJson("/api/admin/action-counts"))["creators"]!.GetValue<int>());
        var before = await customer.GetJson("/api/session");
        Assert.DoesNotContain(before["profiles"]!.AsArray(), x => x!["role"]!.GetValue<string>() == "Creator");
        var queue = await operations.GetJson("/api/admin/role-enrollments");
        var review = Assert.Single(queue.AsArray(), x => x!["id"]!.GetValue<Guid>() == pending["id"]!.GetValue<Guid>())!;
        Assert.Equal("Food", review["category"]!.GetValue<string>());
        Assert.Equal("TikTok", review["socialProfiles"]![0]!["platform"]!.GetValue<string>());
        var approved = await operations.PostJson($"/api/admin/role-enrollments/{pending["id"]!.GetValue<Guid>()}/review",
            new { approve = true, expectedVersion = pending["version"]!.GetValue<long>() }, "creator-approval");
        Assert.Equal((int)RoleEnrollmentStatus.Approved, approved["status"]!.GetValue<int>());
        Assert.Equal(creatorActionsBefore, (await operations.GetJson("/api/admin/action-counts"))["creators"]!.GetValue<int>());
        await using var db = host.Database.Open();
        Assert.Single(await db.CommercePermissions.Where(x => x.UserId == Weymela.Infrastructure.Development.DevelopmentDirectory.Id(7)
            && x.Role == ActorRole.Creator).ToListAsync());
    }

    [Fact]
    public async Task Verified_business_registration_immediately_activates_zero_balance_workspace()
    {
        await using var host = await ApiFixture.CreateAsync(fixture);
        using var customer = await host.Login("other-customer");
        var approved = await customer.PostJson("/api/onboarding/profile", new { role = "Business", displayName = "Teddy Cafe",
            category = "Restaurant", region = "Addis" }, "business-application");
        Assert.Equal((int)RoleEnrollmentStatus.Approved, approved["status"]!.GetValue<int>());
        await using var db = host.Database.Open();
        var permission = await db.CommercePermissions.SingleAsync(x => x.UserId == Weymela.Infrastructure.Development.DevelopmentDirectory.Id(8)
            && x.Role == ActorRole.Business);
        var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == permission.BusinessId);
        Assert.Equal(0m, wallet.AvailableBalance.Amount);
        Assert.Equal(0m, wallet.ReservedBalance.Amount);
    }
}
