using System.Net;
using Microsoft.EntityFrameworkCore;
using Weymela.Domain;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class PromotionContentWorkflowHttpTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Latest_business_approved_revision_is_required_before_creator_goes_live()
    {
        await using var host = await ApiFixture.CreateAsync(postgres);
        using var business = await host.Login("business");
        using var creator = await host.Login("other-creator");
        using var unrelatedBusiness = await host.Login("other-business");

        var promotion = (await business.GetJson("/api/business/campaigns")).AsArray()
            .Single(row => row!["type"]!.GetValue<string>() == "View & Sale")!;
        var promotionId = promotion["id"]!.GetValue<string>();
        var details = await business.GetJson($"/api/business/campaigns/{promotionId}");
        var request = details["applicants"]!.AsArray().Single(row => row!["status"]!.GetValue<string>() == "Pending")!;
        var allocation = await business.PostJson($"/api/business/applicants/{request["id"]!.GetValue<string>()}/approve",
            new { amount = 100, version = details["campaign"]!["version"]!.GetValue<long>() }, "content-workflow-approve");
        var allocationId = allocation["id"]!.GetValue<string>();

        var tooEarly = await creator.Post($"/api/creator/creator-budgets/{allocationId}/go-live", new { });
        Assert.Equal(HttpStatusCode.BadRequest, tooEarly.StatusCode);
        var underReview = await creator.PostJson($"/api/creator/creator-budgets/{allocationId}/content",
            new { provider = "TikTok", externalContentId = "http-revision-one" }, "content-revision-one");
        Assert.Equal("UnderReview", underReview["reviewStatus"]!.GetValue<string>());
        Assert.Equal(1, underReview["revisionNumber"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await creator.Post($"/api/creator/creator-budgets/{allocationId}/go-live", new { })).StatusCode);

        await using (var db = host.Database.Open())
            Assert.False(await db.CreatorPromotionParticipations.AnyAsync(x => x.CreatorAllocationId == Guid.Parse(allocationId)));
        var first = await LoadLatestSubmission(host, Guid.Parse(allocationId));
        var forbidden = await unrelatedBusiness.Post($"/api/business/promotion-content-submissions/{first}/review",
            new { action = "approve", feedback = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await business.PostJson($"/api/business/promotion-content-submissions/{first}/review",
            new { action = "requestchanges", feedback = "Please revise the opening." }, "review-revision-one");
        var revised = await creator.PostJson($"/api/creator/creator-budgets/{allocationId}/content",
            new { provider = "TikTok", externalContentId = "http-revision-two" }, "content-revision-two");
        Assert.Equal(2, revised["revisionNumber"]!.GetValue<int>());
        Assert.Equal("UnderReview", revised["reviewStatus"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await creator.Post($"/api/creator/creator-budgets/{allocationId}/go-live", new { })).StatusCode);

        var second = await LoadLatestSubmission(host, Guid.Parse(allocationId));
        await business.PostJson($"/api/business/promotion-content-submissions/{second}/review",
            new { action = "approve", feedback = (string?)null }, "review-revision-two");
        await using (var db = host.Database.Open())
            Assert.False(await db.CreatorPromotionParticipations.AnyAsync(x => x.CreatorAllocationId == Guid.Parse(allocationId)));

        var before = DateTime.UtcNow;
        var live = await creator.PostJson($"/api/creator/creator-budgets/{allocationId}/go-live", new { }, "creator-go-live");
        Assert.NotNull(live["id"]);
        await using (var db = host.Database.Open())
        {
            var participation = await db.CreatorPromotionParticipations.SingleAsync(x => x.CreatorAllocationId == Guid.Parse(allocationId));
            var promotionRow = await db.Promotions.SingleAsync(x => x.Id == participation.PromotionId);
            Assert.InRange(participation.WentLiveAtUtc, before, DateTime.UtcNow.AddSeconds(1));
            Assert.Equal(participation.WentLiveAtUtc.AddDays(promotionRow.PromotionLiveDurationDays),
                participation.ExpiresAtUtc(promotionRow.PromotionLiveDurationDays));
        }
        var customer = await host.Login("customer");
        using (customer)
        {
            var offers = (await customer.GetJson("/api/customer/offers")).AsArray();
            var customerOffer = offers.Single(row => row!["id"]!.GetValue<string>() == allocationId)!;
            Assert.Equal(30, customerOffer["remainingDays"]!.GetValue<int>());
            var json = customerOffer.ToJsonString();
            Assert.DoesNotContain("creatorId", json);
            Assert.DoesNotContain("creatorAllocationId", json);
        }
    }

    [Fact]
    public async Task Business_review_queue_is_business_only_and_customer_cannot_read_it()
    {
        await using var host = await ApiFixture.CreateAsync(postgres);
        using var customer = await host.Login("customer");
        using var creator = await host.Login("creator");
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/business/promotion-content-submissions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await creator.GetAsync("/api/business/promotion-content-submissions")).StatusCode);
        using var business = await host.Login("business");
        var queue = await business.GetJson("/api/business/promotion-content-submissions");
        Assert.NotEmpty(queue.AsArray());
        foreach (var forbidden in new[] { "creatorId", "creatorAllocationId", "businessId", "platformRevenue", "commission", "wallet" })
            Assert.DoesNotContain(forbidden, queue.ToJsonString());
    }

    private static async Task<Guid> LoadLatestSubmission(ApiFixture host, Guid allocationId)
    {
        await using var db = host.Database.Open();
        return await db.CreatorPromotionContentSubmissions.Where(x => x.CreatorAllocationId == allocationId)
            .OrderByDescending(x => x.RevisionNumber).Select(x => x.Id).FirstAsync();
    }
}
