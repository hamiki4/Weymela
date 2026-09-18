using Weymela.Domain;

namespace Weymela.Infrastructure.Persistence.Records;

public sealed record FinancialConfiguration(Guid Id, string Name);

public sealed class FinancialConfigurationVersion
{
    private FinancialConfigurationVersion() { ViewOnly = null!; ViewPlusCommission = null!; }
    public Guid Id { get; private set; }
    public Guid ConfigurationId { get; private set; }
    public int Version { get; private set; }
    public Guid ChangedBy { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public PricingSnapshot ViewOnly { get; private set; }
    public PricingSnapshot ViewPlusCommission { get; private set; }
    // Versions created before UGC was introduced remain valid immutable history.
    public UgcPricingSnapshot? Ugc { get; private set; }
    public Money CreatorPayoutThreshold { get; private set; }
    public Money CustomerPayoutThreshold { get; private set; }

    public FinancialConfigurationVersion(Guid id, Guid configurationId, int version, Guid changedBy,
        DateTime effectiveFromUtc, PricingSnapshot viewOnly, PricingSnapshot hybrid,
        Money creatorThreshold, Money customerThreshold, UgcPricingSnapshot? ugc = null)
    {
        if (version <= 0 || creatorThreshold.Amount <= 0 || customerThreshold.Amount <= 0)
            throw new ArgumentException("Version and payout thresholds must be positive.");
        Validate(viewOnly, PromotionType.ViewOnly);
        Validate(hybrid, PromotionType.ViewPlusCommission);
        Id = id; ConfigurationId = configurationId; Version = version; ChangedBy = changedBy;
        EffectiveFromUtc = effectiveFromUtc;
        ViewOnly = viewOnly with { ConfigurationVersionId = id, EffectiveFromUtc = effectiveFromUtc };
        ViewPlusCommission = hybrid with { ConfigurationVersionId = id, EffectiveFromUtc = effectiveFromUtc };
        Ugc = (ugc ?? new(new Money(200), 10m, null, effectiveFromUtc, id)) with
            { ConfigurationVersionId = id, EffectiveFromUtc = effectiveFromUtc };
        if (!Ugc.IsValid) throw new ArgumentException("Invalid UGC financial configuration.");
        CreatorPayoutThreshold = creatorThreshold; CustomerPayoutThreshold = customerThreshold;
    }

    private static void Validate(PricingSnapshot p, PromotionType type)
    {
        SnapshotPricing.Validate(p);
        if (p.PromotionType != type || !p.IsValid || p.BusinessCharge.Amount <= 0 ||
            p.CreatorEarning.Add(p.PlatformEarning) != p.BusinessCharge ||
            p.CreatorCommissionPercent + p.CustomerCashbackPercent + p.PlatformPercent > 100)
            throw new ArgumentException("Invalid Platform financial configuration split.");
    }
}
