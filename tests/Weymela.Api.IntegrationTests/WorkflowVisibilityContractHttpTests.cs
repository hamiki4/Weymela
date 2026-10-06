using System.Net;
using Microsoft.EntityFrameworkCore;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class WorkflowVisibilityContractHttpTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Customer_api_stays_absent_until_the_current_creator_revision_goes_live()
    {
        await using var host = await ApiFixture.CreateAsync(postgres);
        using var business = await host.Login("business");
        using var creator = await host.Login("other-creator");
        using var customer = await host.Login("customer");

        var title = $"Views and Sales contract {Guid.NewGuid():N}";
        var created = await business.PostJson("/api/business/campaigns", Draft(title), "contract-create");
        var promotionId = created["id"]!.GetValue<string>();
        await AssertCustomerDoesNotSeeAsync(customer, title);

        var draft = await business.GetJson($"/api/business/campaigns/{promotionId}");
        var postedBeforeFunding = await business.Post($"/api/business/campaigns/{promotionId}/publish",
            new { version = draft["campaign"]!["version"]!.GetValue<long>() }, "contract-post-before-funding");
        Assert.Equal(HttpStatusCode.BadRequest, postedBeforeFunding.StatusCode);
        await AssertCustomerDoesNotSeeAsync(customer, title);

        var wallet = await business.GetJson("/api/business/wallet");
        await business.PostJson($"/api/business/campaigns/{promotionId}/fund", new
        {
            campaignVersion = draft["campaign"]!["version"]!.GetValue<long>(),
            walletVersion = wallet["version"]!.GetValue<long>()
        }, "contract-fund");
        await AssertCustomerDoesNotSeeAsync(customer, title);

        var funded = await business.GetJson($"/api/business/campaigns/{promotionId}");
        await business.PostJson($"/api/business/campaigns/{promotionId}/publish",
            new { version = funded["campaign"]!["version"]!.GetValue<long>() }, "contract-post");
        Assert.Contains((await creator.GetJson("/api/creator/discover")).AsArray(),
            row => row?["id"]?.GetValue<string>() == promotionId);
        await AssertCustomerDoesNotSeeAsync(customer, title);

        var application = await creator.PostJson($"/api/creator/campaigns/{promotionId}/join",
            new { message = "I can create this story.", contentConcept = "A focused local story." }, "contract-apply");
        await AssertCustomerDoesNotSeeAsync(customer, title);

        var businessDetails = await business.GetJson($"/api/business/campaigns/{promotionId}");
        var allocation = await business.PostJson($"/api/business/applicants/{application["id"]!.GetValue<string>()}/approve",
            new
            {
                amount = 200,
                version = businessDetails["campaign"]!["version"]!.GetValue<long>()
            }, "contract-approve");
        var allocationId = allocation["id"]!.GetValue<string>();
        await AssertCustomerDoesNotSeeAsync(customer, title, allocationId);

        await creator.PostJson($"/api/creator/creator-budgets/{allocationId}/content", new
        {
            provider = "TikTok",
            externalContentId = $"contract-revision-one-{Guid.NewGuid():N}"
        }, "contract-submit-one");
        await AssertCustomerDoesNotSeeAsync(customer, title, allocationId);

        await using (var db = host.Database.Open())
        {
            var submission = await db.CreatorPromotionContentSubmissions
                .SingleAsync(row => row.CreatorAllocationId == Guid.Parse(allocationId));
            await business.PostJson($"/api/business/promotion-content-submissions/{submission.Id}/review",
                new { action = "requestchanges", feedback = "Please revise the opening." }, "contract-request-changes");
        }
        await AssertCustomerDoesNotSeeAsync(customer, title, allocationId);

        await creator.PostJson($"/api/creator/creator-budgets/{allocationId}/content", new
        {
            provider = "TikTok",
            externalContentId = $"contract-revision-two-{Guid.NewGuid():N}"
        }, "contract-submit-two");
        await AssertCustomerDoesNotSeeAsync(customer, title, allocationId);

        await using (var db = host.Database.Open())
        {
            var submission = await db.CreatorPromotionContentSubmissions
                .Where(row => row.CreatorAllocationId == Guid.Parse(allocationId))
                .OrderByDescending(row => row.RevisionNumber)
                .SingleAsync();
            await business.PostJson($"/api/business/promotion-content-submissions/{submission.Id}/review",
                new { action = "approve", feedback = (string?)null }, "contract-approve-latest");
        }
        await AssertCustomerDoesNotSeeAsync(customer, title, allocationId);

        var live = await creator.Post($"/api/creator/creator-budgets/{allocationId}/go-live", new { });
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        var offers = await customer.GetJson("/api/customer/offers");
        Assert.Contains(offers.AsArray(), row => row?["id"]?.GetValue<string>() == allocationId);
    }

    private static object Draft(string title) => new
    {
        title,
        description = "A contract-test Promotion brief.",
        type = "View & Sale",
        campaignBudget = 500m,
        requirements = "One original short video.",
        category = "Food",
        region = "Addis Ababa",
        minimumVerifiedFollowers = 0,
        startUtc = DateTime.UtcNow.AddMinutes(-1),
        endUtc = DateTime.UtcNow.AddDays(5),
        slogan = title
    };

    private static async Task AssertCustomerDoesNotSeeAsync(
        HttpClient customer, string title, string? allocationId = null)
    {
        var offers = await customer.GetJson("/api/customer/offers");
        Assert.DoesNotContain(offers.AsArray(), row =>
            row?["id"]?.GetValue<string>() == allocationId
            || row?["offer"]?.GetValue<string>() == title
            || row?["slogan"]?.GetValue<string>() == title);
    }
}
