using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weymela.Application;
using Weymela.Infrastructure.Providers;
using Weymela.Domain;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class CreatorGoLiveHttpTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Creator_requests_go_live_once_and_business_evidence_acceptance_starts_the_window()
    {
        await using var host = await ApiFixture.CreateAsync(postgres);
        using var business = await host.Login("business"); using var creator = await host.Login("creator");
        using var foreignBusiness = await host.Login("other-business");
        var assignment = await ApprovedAssignment(business, creator, direct: false);
        var profiles = (await creator.GetJson("/api/creator/social-accounts")).AsArray();
        var profile = profiles.First(x => x!["platform"]!.GetValue<string>() == "TikTok")!;
        var path = $"/api/creator/ugc/assignments/{assignment}/launch";
        // Model a pre-release pending review explicitly. The HTTP test provider is a
        // local simulator; unavailable-provider behavior is tested with the real router separately.
        await using (var setup = host.Database.Open())
        {
            var work = await setup.UgcAssignments.SingleAsync(x => x.Id == assignment);
            var sample = await setup.UgcSubmissions.SingleAsync(x => x.UgcAssignmentId == assignment);
            var publication = new CreatorPublicationVerification(work.CreatorId, profile["id"]!.GetValue<Guid>(),
                null, work.Id, null, sample.Id, "TikTok", "7412345678901234567", DateTime.UtcNow);
            publication.RecordPublicLink(profile["profileUrl"]!.GetValue<string>().TrimEnd('/') + "/video/7412345678901234567");
            publication.RecordProviderFailure("ProviderUnavailable");
            setup.CreatorPublicationVerifications.Add(publication); await setup.SaveChangesAsync();
        }
        var pending = await creator.PostJson(path, null!, "creator-single-action");
        Assert.Equal("VerificationPending", pending["status"]!.GetValue<string>());
        Assert.NotNull(pending["goLiveRequestedAtUtc"]); Assert.Null(pending["wentLiveAtUtc"]);
        var id = pending["id"]!.GetValue<Guid>();
        Assert.Equal(id, (await creator.PostJson(path, null!, "same-work-new-request"))["id"]!.GetValue<Guid>());
        var review = $"/api/business/publication-verifications/{id}/review";
        var evidenceReference = "isolated-http-evidence-review:" + new string('e', 300);
        var evidence = new { action = "verify", evidenceReference, evidenceChecked = true };
        Assert.Equal(HttpStatusCode.Forbidden, (await foreignBusiness.Post(review, evidence)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await creator.Post(review, evidence)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await business.Post(review, new { action = "verify", evidenceReference = "unchecked" })).StatusCode);
        await business.PostJson(review, evidence, "accept-publication-evidence");
        var live = await creator.PostJson(path, null!, "creator-retry-after-accepted");
        Assert.Equal("Manual", live["verificationMethod"]!.GetValue<string>());
        var start = live["wentLiveAtUtc"]!.GetValue<DateTime>();
        Assert.Equal(start.AddDays(30), live["expiresAtUtc"]!.GetValue<DateTime>());
        var retry = await creator.PostJson(path, null!, "creator-second-retry");
        Assert.Equal(start, retry["wentLiveAtUtc"]!.GetValue<DateTime>());
        await using var db = host.Database.Open();
        Assert.Single(db.CreatorEarningEntries.Where(x => x.UgcAssignmentId == assignment));
        Assert.Equal(evidenceReference, (await db.CreatorPublicationVerifications.SingleAsync(x => x.Id == id)).EvidenceReference);
    }

    [Fact]
    public async Task Long_public_video_urls_normalize_but_stay_unverified_without_a_provider()
    {
        await using var host = await ApiFixture.CreateAsync(postgres, builder =>
            builder.Host.ConfigureContainer<IServiceCollection>((_, services) => services.Replace(
                ServiceDescriptor.Singleton<IVerifiedViewProvider>(new SocialProviderRouter([], TimeProvider.System)))));
        using var business = await host.Login("business"); using var creator = await host.Login("creator");
        var assignment = await ApprovedAssignment(business, creator, direct: false);
        var profile = (await creator.GetJson("/api/creator/social-accounts")).AsArray()
            .First(x => x!["platform"]!.GetValue<string>() == "TikTok")!;
        var canonical = profile["profileUrl"]!.GetValue<string>().TrimEnd('/') + "/video/7412345678901234567";
        var shared = canonical + "?is_from_webapp=1&tracking=" + new string('x', 250);
        Assert.InRange(shared.Length, 101, 1000);
        var path = $"/api/creator/ugc/assignments/{assignment}/launch";
        var pending = await creator.PostJson(path, new {
            provider = "TikTok", externalContentId = shared, creatorSocialProfileId = profile["id"]!.GetValue<Guid>()
        });
        Assert.Equal("VerificationPending", pending["status"]!.GetValue<string>());
        Assert.Equal("7412345678901234567", pending["externalContentId"]!.GetValue<string>());
        Assert.Equal(canonical, pending["watchUrl"]!.GetValue<string>());
        Assert.NotNull(pending["goLiveRequestedAtUtc"]); Assert.Null(pending["wentLiveAtUtc"]);
        Assert.Null(pending["verificationMethod"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await creator.Post(path, new {
            provider = "TikTok", externalContentId = canonical + "?tracking=" + new string('x', 1000),
            creatorSocialProfileId = profile["id"]!.GetValue<Guid>()
        })).StatusCode);
        await using var db = host.Database.Open();
        Assert.False(await db.CreatorEarningEntries.AnyAsync(x => x.UgcAssignmentId == assignment));
        Assert.Single(await db.CreatorPublicationVerifications.Where(x => x.UgcAssignmentId == assignment).ToListAsync());
    }

    [Fact]
    public async Task Direct_delivery_requires_an_uploaded_video_and_download_is_authorized()
    {
        await using var host = await ApiFixture.CreateAsync(postgres);
        using var business = await host.Login("business"); using var creator = await host.Login("creator");
        using var stranger = await host.Login("other-business"); using var customer = await host.Login("customer");
        var assignment = await ApprovedAssignment(business, creator, direct: true);
        var rows = (await creator.GetJson("/api/creator/ugc/assignments")).AsArray();
        var row = rows.Single(x => x!["id"]!.GetValue<Guid>() == assignment)!;
        Assert.Equal("DirectVideoDelivery", row["deliverableType"]!.GetValue<string>());
        Assert.NotNull(row["completedAtUtc"]); Assert.Null(row["publication"]);
        var url = row["downloadUrl"]!.GetValue<string>();
        var file = await business.GetAsync(url); Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("attachment", file.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(Mp4, await file.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, (await creator.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(url)).StatusCode);
        using var anonymous = host.Anonymous(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        await using var db = host.Database.Open();
        Assert.Single(db.CreatorEarningEntries.Where(x => x.UgcAssignmentId == assignment));
    }

    // Isolated transport fixture, deliberately not evidence of a real social publication.
    private static readonly byte[] Mp4 = [0, 0, 0, 12, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m'];

    private static async Task<Guid> ApprovedAssignment(HttpClient business, HttpClient creator, bool direct)
    {
        var id = (await business.PostJson("/api/business/ugc", new {
            title = "Isolated Creator delivery HTTP", contentType = "Video", instructions = "Deliver fixture video",
            resources = Array.Empty<string>(), dueDateUtc = DateTime.UtcNow.AddDays(7), productProvided = true,
            creatorMustPurchase = false, creatorPayment = 500m, creatorsNeeded = 1,
            platformRequirements = Array.Empty<object>(), platformCapacities = Array.Empty<object>(),
            deliverableType = direct ? "DirectVideoDelivery" : "PublicSocialVideo"
        }))["id"]!.GetValue<Guid>();
        await business.PostJson($"/api/business/ugc/{id}/publish", new { version = 0 });
        var request = (await creator.PostJson($"/api/creator/ugc/{id}/request", new { }))["id"]!.GetValue<Guid>();
        var assignment = (await business.PostJson($"/api/business/ugc/requests/{request}/approve", new { }))["id"]!.GetValue<Guid>();
        using var multipart = new MultipartFormDataContent();
        var media = new ByteArrayContent(Mp4); media.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        multipart.Add(media, "media", "final.mp4");
        using var submission = new HttpRequestMessage(HttpMethod.Post, $"/api/creator/ugc/assignments/{assignment}/submit-review") { Content = multipart };
        submission.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await creator.SendAsync(submission);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        await business.PostJson($"/api/business/ugc/assignments/{assignment}/review/approve", new { });
        return assignment;
    }
}
