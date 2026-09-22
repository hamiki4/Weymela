namespace Weymela.Domain;

public enum PromotionContentReviewStatus
{
    UnderReview,
    ChangesRequested,
    Approved,
    Rejected
}

/// <summary>One immutable Creator-submitted content revision for an approved Promotion allocation.</summary>
public sealed class CreatorPromotionContentSubmission
{
    private CreatorPromotionContentSubmission() { Provider = null!; ContentReference = null!; }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CreatorAllocationId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string Provider { get; private set; }
    public string ContentReference { get; private set; }
    public DateTime SubmittedAtUtc { get; private set; }
    public PromotionContentReviewStatus ReviewStatus { get; private set; } = PromotionContentReviewStatus.UnderReview;
    public string? Feedback { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public long Version { get; private set; }

    public CreatorPromotionContentSubmission(Guid creatorAllocationId, int revisionNumber,
        string provider, string contentReference, DateTime submittedAtUtc)
    {
        if (creatorAllocationId == Guid.Empty) throw new ArgumentException("An approved Creator allocation is required.");
        if (revisionNumber <= 0) throw new ArgumentOutOfRangeException(nameof(revisionNumber));
        if (provider is not ("TikTok" or "YouTube" or "Instagram")) throw new ArgumentException("Unsupported content provider.");
        if (string.IsNullOrWhiteSpace(contentReference) || contentReference.Length > 100)
            throw new ArgumentException("A valid content reference is required.");
        if (submittedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Submission time must be UTC.");
        CreatorAllocationId = creatorAllocationId;
        RevisionNumber = revisionNumber;
        Provider = provider;
        ContentReference = contentReference.Trim();
        SubmittedAtUtc = submittedAtUtc;
    }

    public void Approve(Guid reviewerUserId, DateTime reviewedAtUtc, string? feedback = null)
        => Review(PromotionContentReviewStatus.Approved, reviewerUserId, reviewedAtUtc, feedback);

    public void RequestChanges(Guid reviewerUserId, DateTime reviewedAtUtc, string feedback)
    {
        if (string.IsNullOrWhiteSpace(feedback)) throw new ArgumentException("Feedback is required when requesting changes.");
        Review(PromotionContentReviewStatus.ChangesRequested, reviewerUserId, reviewedAtUtc, feedback);
    }

    public void Reject(Guid reviewerUserId, DateTime reviewedAtUtc, string? feedback = null)
        => Review(PromotionContentReviewStatus.Rejected, reviewerUserId, reviewedAtUtc, feedback);

    private void Review(PromotionContentReviewStatus status, Guid reviewerUserId, DateTime reviewedAtUtc, string? feedback)
    {
        if (ReviewStatus != PromotionContentReviewStatus.UnderReview)
            throw new InvalidOperationException("Only content under review can be decided.");
        if (reviewerUserId == Guid.Empty) throw new ArgumentException("A Business reviewer is required.");
        if (reviewedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Review time must be UTC.");
        if (feedback?.Length > 2000) throw new ArgumentException("Review feedback is too long.");
        ReviewStatus = status;
        Feedback = string.IsNullOrWhiteSpace(feedback) ? null : feedback.Trim();
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = reviewedAtUtc;
        Version++;
    }
}
