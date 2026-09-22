namespace Weymela.Domain;

public enum ParticipationStatus { Active, Paused, FundingRequired, Completed }

public sealed class CreatorPromotionParticipation
{
    private CreatorPromotionParticipation() { Provider = null!; ExternalContentId = null!; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PromotionId { get; private set; }
    public Guid CreatorId { get; private set; }
    public Guid CreatorAllocationId { get; private set; }
    public string Provider { get; private set; }
    public string ExternalContentId { get; private set; }
    public long BaselineViews { get; private set; }
    public long LatestVerifiedViews { get; private set; }
    public long RewardedViewCount { get; private set; }
    public long CampaignVerifiedViews => Math.Max(0, LatestVerifiedViews - BaselineViews);
    public ParticipationStatus Status { get; private set; } = ParticipationStatus.Active;
    public DateTime WentLiveAtUtc { get; private set; }
    public DateTime LatestVerifiedAtUtc { get; private set; }
    public long Version { get; private set; }

    public CreatorPromotionParticipation(Guid promotionId, Guid creatorId, Guid allocationId, string provider, string contentId,
        long baselineViews, DateTime verifiedAt)
        : this(promotionId, creatorId, allocationId, provider, contentId, baselineViews, verifiedAt, verifiedAt) { }

    public CreatorPromotionParticipation(Guid promotionId, Guid creatorId, Guid allocationId, string provider, string contentId,
        long baselineViews, DateTime wentLiveAtUtc, DateTime verifiedAtUtc)
    {
        if (baselineViews < 0 || string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(contentId))
            throw new ArgumentException("A verified content baseline is required.");
        PromotionId = promotionId; CreatorId = creatorId; CreatorAllocationId = allocationId;
        Provider = provider; ExternalContentId = contentId; BaselineViews = baselineViews; LatestVerifiedViews = baselineViews;
        WentLiveAtUtc = wentLiveAtUtc; LatestVerifiedAtUtc = verifiedAtUtc;
    }

    public DateTime ExpiresAtUtc(int promotionLiveDurationDays)
        => CreatorLiveWindow.ExpiresAtUtc(WentLiveAtUtc, promotionLiveDurationDays);
    public int? RemainingDays(DateTime nowUtc, int promotionLiveDurationDays)
        => CreatorLiveWindow.RemainingDays(WentLiveAtUtc, nowUtc, promotionLiveDurationDays);
    public bool IsLive(DateTime nowUtc, int promotionLiveDurationDays)
        => CreatorLiveWindow.IsLive(WentLiveAtUtc, nowUtc, promotionLiveDurationDays);

    public bool Observe(long reportedViews, DateTime verifiedAt)
    {
        if (reportedViews < 0) throw new ArgumentException("Verified counts cannot be negative.");
        var anomaly = reportedViews < LatestVerifiedViews || verifiedAt < LatestVerifiedAtUtc;
        if (!anomaly) { LatestVerifiedViews = reportedViews; LatestVerifiedAtUtc = verifiedAt; }
        Version++;
        return anomaly;
    }
    public long CompleteUnpaidBlocks(int viewsPerReward)
    {
        if (viewsPerReward <= 0) throw new ArgumentOutOfRangeException(nameof(viewsPerReward));
        return (CampaignVerifiedViews - RewardedViewCount) / viewsPerReward;
    }
    public void Reward(long blocks, int viewsPerReward)
    {
        if (Status is ParticipationStatus.Paused or ParticipationStatus.Completed || blocks <= 0 || blocks > CompleteUnpaidBlocks(viewsPerReward))
            throw new InvalidOperationException("Only complete, unpaid verified-view blocks can be rewarded.");
        RewardedViewCount = checked(RewardedViewCount + blocks * viewsPerReward); Version++;
    }
    public void SetFundingRequired(bool required)
    {
        if (Status is ParticipationStatus.Paused or ParticipationStatus.Completed) throw new InvalidOperationException("Participation is not active.");
        Status = required ? ParticipationStatus.FundingRequired : ParticipationStatus.Active; Version++;
    }
    public void Pause() { if (Status == ParticipationStatus.Completed) throw new InvalidOperationException(); Status = ParticipationStatus.Paused; Version++; }
    public void Resume() { if (Status != ParticipationStatus.Paused) throw new InvalidOperationException(); Status = ParticipationStatus.Active; Version++; }
    public void Complete() { if (Status == ParticipationStatus.Completed) return; Status = ParticipationStatus.Completed; Version++; }
}

public sealed record ViewRewardReceipt(Guid Id, Guid ParticipationId, Guid JournalId, long Blocks, long RewardedThrough,
    Money BusinessCharge, Money CreatorEarning, Money PlatformEarning, DateTime CreatedAtUtc);
