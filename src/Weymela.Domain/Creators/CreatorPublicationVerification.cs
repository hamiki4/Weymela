namespace Weymela.Domain;

public enum PublicationVerificationStatus { VerificationPending, Verified, Failed, Expired }
public enum PublicationVerificationMethod { Provider, Manual }

/// <summary>
/// Narrow per-Creator publication evidence. It does not replace Promotion,
/// content-review, allocation, participation, offer, or settlement authority.
/// </summary>
public sealed class CreatorPublicationVerification
{
    private CreatorPublicationVerification()
    {
        Provider = null!;
        ExternalContentId = null!;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CreatorId { get; private set; }
    public Guid CreatorSocialProfileId { get; private set; }
    public Guid? CreatorAllocationId { get; private set; }
    public Guid? UgcAssignmentId { get; private set; }
    public Guid? PromotionContentSubmissionId { get; private set; }
    public Guid? UgcSubmissionId { get; private set; }
    public string Provider { get; private set; }
    public string ExternalContentId { get; private set; }
    public PublicationVerificationStatus Status { get; private set; } = PublicationVerificationStatus.VerificationPending;
    public PublicationVerificationMethod? VerificationMethod { get; private set; }
    public string? EvidenceReference { get; private set; }
    public long? BaselineViews { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }
    public Guid? VerifiedByUserId { get; private set; }
    public DateTime? WentLiveAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }
    public long Version { get; private set; }

    public CreatorPublicationVerification(Guid creatorId, Guid socialProfileId,
        Guid? creatorAllocationId, Guid? ugcAssignmentId,
        Guid? promotionContentSubmissionId, Guid? ugcSubmissionId,
        string provider, string externalContentId, DateTime requestedAtUtc)
    {
        if (creatorId == Guid.Empty || socialProfileId == Guid.Empty)
            throw new ArgumentException("Publication requires an exact Creator social profile.");
        if ((creatorAllocationId is null) == (ugcAssignmentId is null)
            || (promotionContentSubmissionId is null) == (ugcSubmissionId is null)
            || (creatorAllocationId is null) != (promotionContentSubmissionId is null))
            throw new ArgumentException("Publication must bind exactly one work item and its approved revision.");
        if (provider is not ("TikTok" or "YouTube" or "Instagram"))
            throw new ArgumentException("Unsupported publication provider.");
        if (!ValidContentId(externalContentId))
            throw new ArgumentException("Publication content ID is invalid.");
        if (requestedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Publication request time must be UTC.");

        CreatorId = creatorId;
        CreatorSocialProfileId = socialProfileId;
        CreatorAllocationId = creatorAllocationId;
        UgcAssignmentId = ugcAssignmentId;
        PromotionContentSubmissionId = promotionContentSubmissionId;
        UgcSubmissionId = ugcSubmissionId;
        Provider = provider;
        ExternalContentId = externalContentId.Trim();
        RequestedAtUtc = requestedAtUtc;
    }

    public void Verify(PublicationVerificationMethod method, string evidenceReference,
        long? baselineViews, DateTime verifiedAtUtc, Guid? verifiedByUserId = null)
    {
        if (Status != PublicationVerificationStatus.VerificationPending)
            throw new InvalidOperationException("Only pending publication evidence can be verified.");
        if (string.IsNullOrWhiteSpace(evidenceReference) || evidenceReference.Length > 500)
            throw new ArgumentException("Publication verification evidence is required.");
        if (baselineViews < 0) throw new ArgumentOutOfRangeException(nameof(baselineViews));
        if (CreatorAllocationId is not null && baselineViews is null)
            throw new ArgumentException("A verified view baseline is required for this Promotion.");
        if (method == PublicationVerificationMethod.Manual && verifiedByUserId is null)
            throw new ArgumentException("Manual verification requires an Admin reviewer.");
        if (verifiedAtUtc.Kind != DateTimeKind.Utc || verifiedAtUtc < RequestedAtUtc)
            throw new ArgumentException("Publication verification time is invalid.");
        Status = PublicationVerificationStatus.Verified;
        VerificationMethod = method;
        EvidenceReference = evidenceReference.Trim();
        BaselineViews = baselineViews;
        VerifiedAtUtc = verifiedAtUtc;
        VerifiedByUserId = verifiedByUserId;
        Version++;
    }

    public void Fail(bool expired, DateTime atUtc)
    {
        if (atUtc.Kind != DateTimeKind.Utc || atUtc < RequestedAtUtc)
            throw new ArgumentException("Publication end time is invalid.");
        if (Status is PublicationVerificationStatus.Failed or PublicationVerificationStatus.Expired) return;
        if (Status == PublicationVerificationStatus.Verified && WentLiveAtUtc is not null)
            EndedAtUtc = atUtc;
        Status = expired ? PublicationVerificationStatus.Expired : PublicationVerificationStatus.Failed;
        Version++;
    }

    public void GoLive(DateTime atUtc)
    {
        if (Status != PublicationVerificationStatus.Verified || VerifiedAtUtc is null)
            throw new InvalidOperationException("Publication must be verified before Go Live.");
        if (WentLiveAtUtc is not null) return;
        if (atUtc.Kind != DateTimeKind.Utc || atUtc < VerifiedAtUtc)
            throw new ArgumentException("Go Live time is invalid.");
        WentLiveAtUtc = atUtc;
        Version++;
    }

    public bool IsLive => Status == PublicationVerificationStatus.Verified
        && WentLiveAtUtc is not null && EndedAtUtc is null;

    public static bool ValidContentId(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 100
        && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_');
}
