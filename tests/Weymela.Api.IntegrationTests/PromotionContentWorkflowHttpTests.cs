using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class PromotionContentWorkflowHttpTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Private_revision_and_verified_current_publication_are_required_before_customer_visibility()
    {
        await using var host = await ApiFixture.CreateAsync(postgres);
        using var business = await host.Login("business");
        using var creator = await host.Login("other-creator");
        using var unrelatedBusiness = await host.Login("other-business");
        using var unrelatedCreator = await host.Login("creator");
        using var customer = await host.Login("customer");
        using var admin = await host.Login("admin");
        using var anonymous = host.Anonymous();

        var promotion = (await business.GetJson("/api/business/campaigns")).AsArray()
            .Single(row => row!["type"]!.GetValue<string>() == "View & Sale")!;
        var promotionId = promotion["id"]!.GetValue<string>();
        var details = await business.GetJson($"/api/business/campaigns/{promotionId}");
        var request = details["applicants"]!.AsArray().Single(row => row!["status"]!.GetValue<string>() == "Pending")!;
        var allocation = await business.PostJson($"/api/business/applicants/{request["id"]!.GetValue<string>()}/approve",
            new { amount = 100, version = details["campaign"]!["version"]!.GetValue<long>() }, "content-workflow-approve");
        var allocationId = allocation["id"]!.GetValue<string>();
        Guid socialProfileId;
        await using (var db = host.Database.Open())
            socialProfileId = (await db.CreatorAllocations.SingleAsync(x => x.Id == Guid.Parse(allocationId))).CreatorSocialProfileId!.Value;

        var tooEarly = await creator.Post($"/api/creator/creator-budgets/{allocationId}/go-live", new { });
        Assert.Equal(HttpStatusCode.BadRequest, tooEarly.StatusCode);
        await AssertUnavailable(customer, allocationId);

        var firstUpload = await UploadReview(creator, allocationId, "content-revision-one");
        Assert.Equal(HttpStatusCode.OK, firstUpload.StatusCode);
        var underReview = JsonNode.Parse(await firstUpload.Content.ReadAsStringAsync())!;
        Assert.Equal("UnderReview", underReview["reviewStatus"]!.GetValue<string>());
        Assert.Equal(1, underReview["revisionNumber"]!.GetValue<int>());
        var first = await LoadLatestSubmission(host, Guid.Parse(allocationId));
        var mediaUrl = $"/api/review-media/promotions/{first}";
        Assert.Equal(HttpStatusCode.OK, (await creator.GetAsync(mediaUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await business.GetAsync(mediaUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(mediaUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unrelatedBusiness.GetAsync(mediaUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unrelatedCreator.GetAsync(mediaUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(mediaUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(mediaUrl)).StatusCode);
        await AssertUnavailable(customer, allocationId);

        var forbidden = await unrelatedBusiness.Post($"/api/business/promotion-content-submissions/{first}/review",
            new { action = "approve", feedback = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await business.PostJson($"/api/business/promotion-content-submissions/{first}/review",
            new { action = "requestchanges", feedback = "Please revise the opening." }, "review-revision-one");
        await AssertUnavailable(customer, allocationId);
        var secondUpload = await UploadReview(creator, allocationId, "content-revision-two");
        Assert.Equal(HttpStatusCode.OK, secondUpload.StatusCode);
        var revised = JsonNode.Parse(await secondUpload.Content.ReadAsStringAsync())!;
        Assert.Equal(2, revised["revisionNumber"]!.GetValue<int>());
        Assert.Equal("UnderReview", revised["reviewStatus"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await business.Post(
            $"/api/business/promotion-content-submissions/{first}/review",
            new { action = "approve", feedback = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await creator.Post($"/api/creator/creator-budgets/{allocationId}/go-live", new { })).StatusCode);
        await AssertUnavailable(customer, allocationId);

        var second = await LoadLatestSubmission(host, Guid.Parse(allocationId));
        await business.PostJson($"/api/business/promotion-content-submissions/{second}/review",
            new { action = "approve", feedback = (string?)null }, "review-revision-two");
        await AssertUnavailable(customer, allocationId);

        const string publicPost = "7622222222222222222";
        var publication = await creator.PostJson($"/api/creator/creator-budgets/{allocationId}/publication",
            new { provider = "TikTok", externalContentId = publicPost, creatorSocialProfileId = socialProfileId },
            "publication-revision-two");
        Assert.Equal("Verified", publication["status"]!.GetValue<string>());
        var publicationId = publication["id"]!.GetValue<Guid>();
        await using (var db = host.Database.Open())
        {
            Assert.False(await db.CreatorPromotionParticipations.AnyAsync(x => x.CreatorAllocationId == Guid.Parse(allocationId)));
            var bound = await db.CreatorPublicationVerifications.SingleAsync(x => x.Id == publicationId);
            Assert.Equal(second, bound.PromotionContentSubmissionId);
            Assert.Equal(socialProfileId, bound.CreatorSocialProfileId);
        }
        await AssertUnavailable(customer, allocationId);

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
            Assert.Equal(second, participation.ApprovedContentSubmissionId);
            Assert.Equal(socialProfileId, participation.CreatorSocialProfileId);
        }
        var offers = (await customer.GetJson("/api/customer/offers")).AsArray();
        var customerOffer = offers.Single(row => row!["id"]!.GetValue<string>() == allocationId)!;
        Assert.Equal(30, customerOffer["remainingDays"]!.GetValue<int>());
        Assert.Equal($"https://www.tiktok.com/@creator/video/{publicPost}", customerOffer["watchUrl"]!.GetValue<string>());
        var json = customerOffer.ToJsonString();
        Assert.DoesNotContain("creatorId", json);
        Assert.DoesNotContain("creatorAllocationId", json);

        var qr = await customer.PostJson($"/api/customer/offers/{allocationId}/qr", new { }, "current-publication-qr");
        Assert.NotNull(qr["token"]);
        var replay = await customer.PostJson($"/api/customer/offers/{allocationId}/qr", new { }, "current-publication-qr");
        Assert.True(replay["replayed"]!.GetValue<bool>());
        await using (var db = host.Database.Open())
            Assert.Single(await db.OfferQrSessions.Where(x => x.CreatorAllocationId == Guid.Parse(allocationId)).ToListAsync());

        await admin.PostJson($"/api/admin/publication-verifications/{publicationId}/review",
            new { action = "fail", evidenceReference = (string?)null, baselineViews = (long?)null },
            "publication-unavailable");
        await AssertUnavailable(customer, allocationId);

        await using (var db = host.Database.Open())
        {
            var permission = await db.CommercePermissions.SingleAsync(x => x.Role == ActorRole.Creator
                && x.SubjectId == DevelopmentDirectory.Id(400));
            permission.IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await creator.GetAsync(mediaUrl)).StatusCode);
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
        foreach (var forbidden in new[] { "creatorId", "businessId", "platformRevenue", "commission", "wallet" })
            Assert.DoesNotContain(forbidden, queue.ToJsonString());
    }

    private static readonly byte[] Mp4 = [0, 0, 0, 12, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
        (byte)'i', (byte)'s', (byte)'o', (byte)'m'];

    private static async Task<HttpResponseMessage> UploadReview(HttpClient client, string allocationId, string key)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Mp4);
        file.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(file, "media", "SAMPLE-WEYMELA-REVIEW-ONLY.mp4");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/creator/creator-budgets/{allocationId}/content/review") { Content = form };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task AssertUnavailable(HttpClient customer, string allocationId)
    {
        var offers = (await customer.GetJson("/api/customer/offers")).AsArray();
        Assert.DoesNotContain(offers, row => row!["id"]!.GetValue<string>() == allocationId);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await customer.Post($"/api/customer/offers/{allocationId}/qr", new { })).StatusCode);
    }

    private static async Task<Guid> LoadLatestSubmission(ApiFixture host, Guid allocationId)
    {
        await using var db = host.Database.Open();
        return await db.CreatorPromotionContentSubmissions.Where(x => x.CreatorAllocationId == allocationId)
            .OrderByDescending(x => x.RevisionNumber).Select(x => x.Id).FirstAsync();
    }
}
