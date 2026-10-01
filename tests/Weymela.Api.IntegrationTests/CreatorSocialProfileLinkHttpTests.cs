using System.Net;
using Microsoft.EntityFrameworkCore;
using Weymela.Domain;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Web;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class CreatorSocialProfileLinkHttpTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("TikTok", "https://www.tiktok.com/@bella", "https://www.tiktok.com/@bella")]
    [InlineData("YouTube", "https://www.youtube.com/@bella", "https://www.youtube.com/@bella")]
    [InlineData("Instagram", "https://www.instagram.com/bella", "https://www.instagram.com/bella")]
    [InlineData("Facebook", "https://www.facebook.com/bella.page", "https://www.facebook.com/bella.page")]
    public async Task Creator_can_save_each_public_profile_without_claiming_verification(string platform, string url, string expected)
    {
        await using var host = await ApiFixture.CreateAsync(postgres);
        using var creator = await host.Login("creator");
        var result = await creator.PostJson($"/api/creator/social-profiles/{platform}", new { profileUrl = url });
        Assert.NotNull(result["id"]);
        var listed = await creator.GetJson("/api/creator/social-accounts");
        Assert.Contains(listed.AsArray(), row => row!["platform"]!.GetValue<string>() == platform
            && row["profileUrl"]!.GetValue<string>() == expected
            && row["verificationStatus"]!.GetValue<string>() == "SelfReported");
        await using var db = host.Database.Open();
        var record = await db.CreatorSocialProfiles.SingleAsync(x => x.CreatorId == DevelopmentDirectory.Id(300)
            && x.Platform.ToString() == platform && x.IsActive);
        Assert.Equal(0, record.SelfReportedAudience);
        Assert.Null(record.VerifiedAudience);
        Assert.Equal("SelfReported", record.VerificationStatus);
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "CreatorSocialProfileLinkSaved"
            && x.ActorId == DevelopmentDirectory.Id(4) && x.Detail == platform);
    }

    [Fact]
    public async Task Edit_remove_and_other_creator_are_scoped_to_the_authenticated_profile()
    {
        await using var host = await ApiFixture.CreateAsync(postgres);
        using var creator = await host.Login("creator");
        using var other = await host.Login("other-creator");
        var first = await creator.PostJson("/api/creator/social-profiles/TikTok", new { profileUrl = "https://www.tiktok.com/@bella" });
        var edited = await creator.PostJson("/api/creator/social-profiles/TikTok", new { profileUrl = "https://www.tiktok.com/@bella_two" });
        Assert.Equal(first["id"]!.GetValue<Guid>(), edited["id"]!.GetValue<Guid>());
        Assert.Equal(HttpStatusCode.BadRequest, (await other.Post("/api/creator/social-profiles/TikTok",
            new { profileUrl = "https://www.tiktok.com/@elias", creatorId = DevelopmentDirectory.Id(300) })).StatusCode);
        await other.PostJson("/api/creator/social-profiles/TikTok", new { profileUrl = "https://www.tiktok.com/@elias" });
        var own = await creator.GetJson("/api/creator/social-accounts");
        Assert.Contains(own.AsArray(), x => x!["profileUrl"]!.GetValue<string>().EndsWith("@bella_two"));
        Assert.DoesNotContain(own.AsArray(), x => x!["profileUrl"]!.GetValue<string>().EndsWith("@elias"));
        Assert.Equal(HttpStatusCode.NoContent, (await creator.DeleteAsync("/api/creator/social-profiles/TikTok")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await creator.DeleteAsync("/api/creator/social-profiles/TikTok")).StatusCode);
        Assert.DoesNotContain((await creator.GetJson("/api/creator/social-accounts")).AsArray(), x => x!["platform"]!.GetValue<string>() == "TikTok");
        Assert.Contains((await other.GetJson("/api/creator/social-accounts")).AsArray(), x => x!["platform"]!.GetValue<string>() == "TikTok");
        await using var db = host.Database.Open();
        Assert.False((await db.CreatorSocialProfiles.SingleAsync(x => x.CreatorId == DevelopmentDirectory.Id(300)
            && x.Platform.ToString() == "TikTok")).IsActive);
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "CreatorSocialProfileLinkRemoved"
            && x.ActorId == DevelopmentDirectory.Id(4));
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("business")]
    [InlineData("cashier")]
    [InlineData("admin")]
    [InlineData("operations-admin")]
    public async Task Other_roles_cannot_modify_creator_links(string alias)
    {
        await using var host = await ApiFixture.CreateAsync(postgres);
        using var actor = await host.Login(alias);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.Post("/api/creator/social-profiles/TikTok",
            new { profileUrl = "https://www.tiktok.com/@forbidden" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.DeleteAsync("/api/creator/social-profiles/TikTok")).StatusCode);
    }

    [Theory]
    [InlineData("TikTok", "javascript:alert(1)")]
    [InlineData("TikTok", "data:text/html,hi")]
    [InlineData("TikTok", "http://www.tiktok.com/@bella")]
    [InlineData("TikTok", "https://tiktok.com.evil.test/@bella")]
    [InlineData("TikTok", "https://www.tiktok.com/@bella/video/123")]
    [InlineData("TikTok", "https://user:password@www.tiktok.com/@bella")]
    [InlineData("TikTok", "https://www.tiktok.com/%2e%2e/@bella")]
    [InlineData("YouTube", "https://youtu.be/abcdefgh")]
    [InlineData("YouTube", "https://www.youtube.com/watch?v=abc")]
    [InlineData("Instagram", "https://www.instagram.com/reel/abc")]
    [InlineData("Facebook", "https://www.facebook.com/watch")]
    [InlineData("Facebook", "https://www.facebook.com/bella?redirect=https://evil.test")]
    public async Task Invalid_or_wrong_domain_urls_are_rejected_by_server(string platform, string url)
    {
        await using var host = await ApiFixture.CreateAsync(postgres);
        using var creator = await host.Login("creator");
        Assert.Equal(HttpStatusCode.BadRequest, (await creator.Post($"/api/creator/social-profiles/{platform}",
            new { profileUrl = url })).StatusCode);
    }

    [Fact]
    public void Validator_accepts_only_explicit_profile_shapes_and_normalizes_hosts()
    {
        Assert.Equal("https://www.facebook.com/profile.php?id=12345",
            CreatorSocialProfileLinks.Normalize(Weymela.Domain.CreatorPlatform.Facebook, " HTTPS://WWW.FACEBOOK.COM/profile.php?id=12345 "));
        Assert.Equal("https://www.youtube.com/channel/UC1234567890123456",
            CreatorSocialProfileLinks.Normalize(Weymela.Domain.CreatorPlatform.YouTube, "https://www.youtube.com/channel/UC1234567890123456/"));
    }

    [Theory]
    [InlineData(CreatorPlatform.TikTok, "https://www.tiktok.com/@bella?_r=1&_t=abc", "https://www.tiktok.com/@bella")]
    [InlineData(CreatorPlatform.YouTube, "https://youtube.com/@bella?feature=shared", "https://youtube.com/@bella")]
    public void Validator_accepts_profile_share_tracking_queries_without_changing_verification(CreatorPlatform platform, string input, string expected)
        => Assert.Equal(expected, CreatorSocialProfileLinks.Normalize(platform, input));

    [Fact]
    public async Task Self_reported_link_can_make_a_platform_promotion_eligible_when_enforcement_is_off()
    {
        await using var host = await ApiFixture.CreateAsync(postgres);
        using var business = await host.Login("business");
        using var creator = await host.Login("creator");
        await using (var db = host.Database.Open())
        {
            db.CreatorSocialProfiles.Add(new CreatorSocialProfileRecord
            {
                CreatorId = DevelopmentDirectory.Id(300), Platform = CreatorPlatform.TikTok,
                ProfileUrl = "https://www.tiktok.com/@bella", SelfReportedAudience = 42000,
                VerificationStatus = "Verified", VerifiedAudience = 40000,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var wallet = await business.GetJson("/api/business/wallet");
        var created = await business.PostJson("/api/business/campaigns", new
        {
            title = "Verified platform gate", description = "Public profile links do not verify a platform",
            type = "ViewOnly", campaignBudget = 3000m, requirements = (string?)null,
            category = (string?)null, region = (string?)null, minimumVerifiedFollowers = (long?)null,
            startUtc = DateTime.UtcNow.AddMinutes(-1), endUtc = DateTime.UtcNow.AddDays(14),
            platforms = new[] { new { platform = "TikTok", capacity = 1 } }
        });
        var campaignId = created["id"]!.GetValue<Guid>();
        await business.PostJson($"/api/business/campaigns/{campaignId}/fund", new
        { campaignVersion = 0, walletVersion = wallet["version"]!.GetValue<long>() });
        await business.PostJson($"/api/business/campaigns/{campaignId}/publish", new { version = 1 });
        Assert.Contains((await creator.GetJson("/api/creator/discover")).AsArray(), x => x!["id"]!.GetValue<Guid>() == campaignId);
        var manual = await creator.PostJson("/api/creator/social-profiles/TikTok", new
        { profileUrl = "https://www.tiktok.com/@bella_new" });
        Assert.Contains((await creator.GetJson("/api/creator/discover")).AsArray(), x => x!["id"]!.GetValue<Guid>() == campaignId);
        Assert.True((await creator.Post($"/api/creator/campaigns/{campaignId}/join", new
        { platform = "TikTok", creatorSocialProfileId = manual["id"]!.GetValue<Guid>(), message = "Interested", contentConcept = "A visit" })).IsSuccessStatusCode);
        await using var verify = host.Database.Open();
        Assert.True(await verify.CreatorApplications.AnyAsync(x => x.PromotionId == campaignId));
    }
}
