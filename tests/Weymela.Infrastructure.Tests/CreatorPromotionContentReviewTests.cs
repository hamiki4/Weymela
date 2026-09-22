using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class CreatorPromotionContentReviewTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Content_submission_and_business_approval_do_not_go_live_until_creator_clicks_go_live()
    {
        var s = await Phase4Scenario.Create(fixture, goLive: false);
        await using var db = s.Database.Open();
        var review = new CreatorPromotionContentService(db, new CommerceAccessPolicy(db), new TestDirectory(), s.Clock);
        var submitted = await review.SubmitAsync(s.Creator, s.AllocationId,
            new ContentInput("TikTok", "approved-content"), "submit-v1", default);
        Assert.Equal("UnderReview", submitted.ReviewStatus);
        Assert.Empty(await db.CreatorPromotionParticipations.ToListAsync());
        await Assert.ThrowsAsync<ApplicationFailure>(() => s.Views(db)
            .GoLiveAsync(s.Creator, s.AllocationId, "too-early", ct: default));

        var row = await db.CreatorPromotionContentSubmissions.SingleAsync();
        var decision = await review.ReviewAsync(s.Seed.Business, row.Id,
            new PromotionContentReviewInput("approve", null), "approve-v1", default);
        Assert.Equal("Approved", decision.ReviewStatus);
        Assert.Empty(await db.CreatorPromotionParticipations.ToListAsync());

        var participationId = await s.Views(db).GoLiveAsync(s.Creator, s.AllocationId, "creator-go-live", ct: default);
        var participation = await db.CreatorPromotionParticipations.SingleAsync(x => x.Id == participationId);
        var promotion = await db.Promotions.SingleAsync(x => x.Id == s.Seed.PromotionId);
        Assert.Equal(s.Clock.Now, participation.WentLiveAtUtc);
        Assert.Equal(s.Clock.Now.AddDays(promotion.PromotionLiveDurationDays),
            participation.ExpiresAtUtc(promotion.PromotionLiveDurationDays));
    }

    [Fact]
    public async Task Changes_requested_keeps_revision_and_new_revision_under_review_blocks_go_live()
    {
        var s = await Phase4Scenario.Create(fixture, goLive: false);
        await using var db = s.Database.Open();
        var review = new CreatorPromotionContentService(db, new CommerceAccessPolicy(db), new TestDirectory(), s.Clock);
        await review.SubmitAsync(s.Creator, s.AllocationId, new("TikTok", "revision-one"), "submit-1", default);
        var first = await db.CreatorPromotionContentSubmissions.SingleAsync();
        await review.ReviewAsync(s.Seed.Business, first.Id,
            new("requestchanges", "Please revise the caption."), "changes-1", default);
        await review.SubmitAsync(s.Creator, s.AllocationId, new("TikTok", "revision-two"), "submit-2", default);
        var revisions = await db.CreatorPromotionContentSubmissions.OrderBy(x => x.RevisionNumber).ToListAsync();
        Assert.Equal(new[] { 1, 2 }, revisions.Select(x => x.RevisionNumber));
        Assert.Equal(PromotionContentReviewStatus.ChangesRequested, revisions[0].ReviewStatus);
        Assert.Equal("revision-one", revisions[0].ContentReference);
        Assert.Equal(PromotionContentReviewStatus.UnderReview, revisions[1].ReviewStatus);
        await Assert.ThrowsAsync<ApplicationFailure>(() => s.Views(db)
            .GoLiveAsync(s.Creator, s.AllocationId, "not-approved", ct: default));
        Assert.Empty(await db.CreatorPromotionParticipations.ToListAsync());
    }

    [Fact]
    public async Task Older_approved_revision_cannot_authorize_go_live_after_a_newer_revision_exists()
    {
        var s = await Phase4Scenario.Create(fixture, goLive: false);
        await using var db = s.Database.Open();
        var approved = new CreatorPromotionContentSubmission(s.AllocationId, 1, "TikTok", "approved-revision", s.Clock.Now);
        db.CreatorPromotionContentSubmissions.Add(approved);
        await db.SaveChangesAsync();
        approved.Approve(s.Seed.Business.UserId, s.Clock.Now.AddMinutes(1));
        await db.SaveChangesAsync();
        db.CreatorPromotionContentSubmissions.Add(new CreatorPromotionContentSubmission(
            s.AllocationId, 2, "TikTok", "newer-under-review", s.Clock.Now.AddMinutes(2)));
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationFailure>(() => s.Views(db)
            .GoLiveAsync(s.Creator, s.AllocationId, "old-approval-must-not-go-live", ct: default));
        Assert.Equal(FailureKind.Validation, error.Kind);
        Assert.Empty(await db.CreatorPromotionParticipations.ToListAsync());
    }

    [Fact]
    public async Task Rejected_latest_revision_cannot_go_live()
    {
        var s = await Phase4Scenario.Create(fixture, goLive: false);
        await using var db = s.Database.Open();
        var review = new CreatorPromotionContentService(db, new CommerceAccessPolicy(db), new TestDirectory(), s.Clock);
        await review.SubmitAsync(s.Creator, s.AllocationId, new("TikTok", "rejected-content"), "submit-rejected", default);
        var submission = await db.CreatorPromotionContentSubmissions.SingleAsync();
        await review.ReviewAsync(s.Seed.Business, submission.Id,
            new("reject", "This content cannot be used."), "reject-content", default);

        var error = await Assert.ThrowsAsync<ApplicationFailure>(() => s.Views(db)
            .GoLiveAsync(s.Creator, s.AllocationId, "rejected-content-go-live", ct: default));
        Assert.Equal(FailureKind.Validation, error.Kind);
        Assert.Empty(await db.CreatorPromotionParticipations.ToListAsync());
    }

    [Fact]
    public async Task Unrelated_business_cannot_review_creator_content()
    {
        var s = await Phase4Scenario.Create(fixture, goLive: false);
        await using var db = s.Database.Open();
        var service = new CreatorPromotionContentService(db, new CommerceAccessPolicy(db), new TestDirectory(), s.Clock);
        await service.SubmitAsync(s.Creator, s.AllocationId, new("Instagram", "post-1"), "submit", default);
        var row = await db.CreatorPromotionContentSubmissions.SingleAsync();
        var otherBusiness = new Actor(Guid.NewGuid(), ActorRole.Business, BusinessId: Guid.NewGuid());
        var error = await Assert.ThrowsAsync<ApplicationFailure>(() => service.ReviewAsync(otherBusiness, row.Id,
            new("approve", null), "forbidden-review", default));
        Assert.Equal(FailureKind.Forbidden, error.Kind);
        Assert.Equal(PromotionContentReviewStatus.UnderReview, row.ReviewStatus);
    }

    [Fact]
    public async Task Customer_offer_and_existing_qr_cannot_be_used_after_creator_live_window_expires()
    {
        var s = await Phase4Scenario.Create(fixture);
        s.Clock.Now = Scenario.Now.AddDays(30).AddMinutes(-2);
        var resolveQr = await s.Issue("resolve-near-expiry");
        var redeemQr = await s.Issue("redeem-near-expiry");
        s.Clock.Now = Scenario.Now.AddDays(30);
        await using var db = s.Database.Open();
        Assert.Empty(await s.Queries(db).CustomerOffersAsync(s.Customer));
        await Assert.ThrowsAsync<ApplicationFailure>(() => s.Checkout(db).IssueAsync(new(s.Customer, s.AllocationId, "new-after-expiry")));
        await Assert.ThrowsAsync<ApplicationFailure>(() => s.Checkout(db).ResolveAsync(s.Cashier, resolveQr.Token!, new TestDirectory()));
        await Assert.ThrowsAsync<ApplicationFailure>(() => s.Checkout(db).RedeemAsync(new(s.Cashier, redeemQr.Token!, new Money(1000), "redeem-after-expiry")));
    }

    [Fact]
    public async Task Submitted_content_and_review_decisions_are_database_auditable_and_immutable()
    {
        var s = await Phase4Scenario.Create(fixture);
        await using var db = s.Database.Open();
        var row = await db.CreatorPromotionContentSubmissions.SingleAsync();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE v3.\"CreatorPromotionContentSubmissions\" SET \"ContentReference\"='rewritten' WHERE \"Id\"={0}", row.Id));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM v3.\"CreatorPromotionContentSubmissions\" WHERE \"Id\"={0}", row.Id));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE v3.\"CreatorPromotionContentSubmissions\" SET \"Feedback\"='rewritten' WHERE \"Id\"={0}", row.Id));
    }
}
