namespace Weymela.Domain;

public enum ReviewMediaAssetState { Active, Retained }

/// <summary>
/// Immutable metadata for a private review file. StorageKey is an opaque API-only
/// reference; access is always authorized through the owning work item.
/// </summary>
public sealed class PrivateReviewMediaAsset
{
    private PrivateReviewMediaAsset()
    {
        StorageKey = null!;
        ContentType = null!;
        Sha256 = null!;
        OriginalFileName = null!;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CreatorId { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid? CreatorAllocationId { get; private set; }
    public Guid? UgcAssignmentId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string StorageKey { get; private set; }
    public string ContentType { get; private set; }
    public long Length { get; private set; }
    public string Sha256 { get; private set; }
    public string OriginalFileName { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public ReviewMediaAssetState State { get; private set; } = ReviewMediaAssetState.Active;

    public PrivateReviewMediaAsset(Guid creatorId, Guid businessId, Guid? creatorAllocationId,
        Guid? ugcAssignmentId, int revisionNumber, string storageKey, string contentType,
        long length, string sha256, string originalFileName, DateTime createdAtUtc)
    {
        if (creatorId == Guid.Empty || businessId == Guid.Empty)
            throw new ArgumentException("Review media requires an owning Creator and Business.");
        if ((creatorAllocationId is null) == (ugcAssignmentId is null))
            throw new ArgumentException("Review media must belong to exactly one Creator work item.");
        if (revisionNumber <= 0) throw new ArgumentOutOfRangeException(nameof(revisionNumber));
        if (!IsStorageKey(storageKey)) throw new ArgumentException("Review media storage key is invalid.");
        if (contentType is not ("video/mp4" or "image/jpeg" or "image/png"))
            throw new ArgumentException("Review media type is invalid.");
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (sha256.Length != 64 || sha256.Any(x => !char.IsAsciiHexDigit(x)))
            throw new ArgumentException("Review media digest is invalid.");
        if (string.IsNullOrWhiteSpace(originalFileName) || originalFileName.Length > 120)
            throw new ArgumentException("Review media filename is invalid.");
        if (createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Review media creation time must be UTC.");

        CreatorId = creatorId;
        BusinessId = businessId;
        CreatorAllocationId = creatorAllocationId;
        UgcAssignmentId = ugcAssignmentId;
        RevisionNumber = revisionNumber;
        StorageKey = storageKey;
        ContentType = contentType;
        Length = length;
        Sha256 = sha256.ToLowerInvariant();
        OriginalFileName = originalFileName;
        CreatedAtUtc = createdAtUtc;
    }

    public static bool IsStorageKey(string? value) => value is { Length: 66 }
        && value.StartsWith("m_", StringComparison.Ordinal)
        && value.AsSpan(2).IndexOfAnyExcept("0123456789abcdef") < 0;
}
