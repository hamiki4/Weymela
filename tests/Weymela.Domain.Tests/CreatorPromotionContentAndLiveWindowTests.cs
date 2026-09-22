using System;
using Weymela.Domain;
using Xunit;

namespace Weymela.Domain.Tests;

public sealed class CreatorPromotionContentAndLiveWindowTests
{
    private static readonly DateTime Start = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Submission_starts_under_review_and_review_records_auditor_and_time()
    {
        var submission = new CreatorPromotionContentSubmission(Guid.NewGuid(), 1, "TikTok", "video-1", Start);
        Assert.Equal(PromotionContentReviewStatus.UnderReview, submission.ReviewStatus);
        Assert.Null(submission.ReviewedAtUtc);
        var reviewer = Guid.NewGuid();
        submission.Approve(reviewer, Start.AddMinutes(4));
        Assert.Equal(PromotionContentReviewStatus.Approved, submission.ReviewStatus);
        Assert.Equal(reviewer, submission.ReviewedByUserId);
        Assert.Equal(Start.AddMinutes(4), submission.ReviewedAtUtc);
        Assert.Throws<InvalidOperationException>(() => submission.Reject(Guid.NewGuid(), Start.AddMinutes(5)));
    }

    [Fact]
    public void Changes_requested_requires_feedback_and_revision_identity_is_positive()
    {
        var submission = new CreatorPromotionContentSubmission(Guid.NewGuid(), 1, "Instagram", "post-1", Start);
        Assert.Throws<ArgumentException>(() => submission.RequestChanges(Guid.NewGuid(), Start.AddMinutes(1), " "));
        submission.RequestChanges(Guid.NewGuid(), Start.AddMinutes(2), "Please revise the opening.");
        Assert.Equal(PromotionContentReviewStatus.ChangesRequested, submission.ReviewStatus);
        Assert.Equal("Please revise the opening.", submission.Feedback);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CreatorPromotionContentSubmission(Guid.NewGuid(), 0, "TikTok", "x", Start));
    }

    [Fact]
    public void Live_window_uses_the_snapshotted_duration_without_zero()
    {
        const int durationDays = 30;
        for (var elapsedDays = 0; elapsedDays < durationDays; elapsedDays++)
            Assert.Equal(durationDays - elapsedDays,
                CreatorLiveWindow.RemainingDays(Start, Start.AddDays(elapsedDays), durationDays));
        Assert.Equal(1, CreatorLiveWindow.RemainingDays(Start, Start.AddDays(29).AddHours(23), durationDays));
    }

    [Fact]
    public void Live_window_supports_a_non_default_duration_and_ends_at_the_boundary()
    {
        const int durationDays = 20;
        var expires = Start.AddDays(durationDays);
        Assert.Equal(expires, CreatorLiveWindow.ExpiresAtUtc(Start, durationDays));
        Assert.Null(CreatorLiveWindow.RemainingDays(Start, expires, durationDays));
        Assert.Null(CreatorLiveWindow.RemainingDays(Start, expires.AddTicks(1), durationDays));
        Assert.False(CreatorLiveWindow.IsLive(Start, expires, durationDays));
    }
}
