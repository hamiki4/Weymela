using Weymela.Domain;

namespace Weymela.Infrastructure.Persistence.Records;

public sealed record FinancialConfiguration(Guid Id, string Name);

public sealed class FinancialConfigurationVersion
{
    private FinancialConfigurationVersion() { }
    public Guid Id { get; private set; }
    public Guid ConfigurationId { get; private set; }
    public int Version { get; private set; }
    public Guid ChangedBy { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public PricingSnapshot? ViewOnly { get; private set; }
    public PricingSnapshot? ViewPlusCommission { get; private set; }
    // Versions created before UGC was introduced remain valid immutable history.
    public UgcPricingSnapshot? Ugc { get; private set; }
    public Money? CreatorPayoutThreshold { get; private set; }
    public Money? CustomerPayoutThreshold { get; private set; }
    public int? PromotionLiveDurationDays { get; private set; }
    public bool? EnforceAudienceRequirements { get; private set; }
    // Existing immutable versions retain their original sale snapshot rates without a backfill.
    public decimal? ApprovedCreatorCommissionPercent { get; private set; }
    public decimal? ApprovedCustomerCashbackPercent { get; private set; }
    public decimal? ApprovedPlatformPercent { get; private set; }
    public decimal CreatorCommissionPercent => ApprovedCreatorCommissionPercent ?? ViewPlusCommission!.CreatorCommissionPercent;
    public decimal CustomerCashbackPercent => ApprovedCustomerCashbackPercent ?? ViewPlusCommission!.CustomerCashbackPercent;
    public decimal PlatformPercent => ApprovedPlatformPercent ?? ViewPlusCommission!.PlatformPercent;

    public static FinancialConfigurationVersion CreatePartial(Guid id, Guid configurationId, int version, Guid changedBy,
        DateTime effectiveFromUtc, PricingSnapshot? viewOnly, PricingSnapshot? hybrid, Money? creatorThreshold,
        Money? customerThreshold, UgcPricingSnapshot? ugc, int? duration, bool? audience,
        decimal creatorPercent, decimal customerPercent, decimal platformPercent)
    {
        if (version <= 0 || creatorThreshold is { Amount: <= 0 } || customerThreshold is { Amount: <= 0 })
            throw new ArgumentException("Configured payout thresholds must be positive.");
        if (duration is { } days) PromotionLiveDurationPolicy.Validate(days);
        if (viewOnly is not null) Validate(viewOnly, PromotionType.ViewOnly);
        if (hybrid is not null) Validate(hybrid, PromotionType.ViewPlusCommission);
        if (ugc is not null && !ugc.IsValid) throw new ArgumentException("Invalid UGC financial configuration.");
        var rates = new[] { creatorPercent, customerPercent, platformPercent };
        if (rates.Any(x => x is < 0 or > 100 || decimal.Round(x, 4) != x) || rates.Sum() > 100)
            throw new ArgumentException("Invalid verified-sale percentages.");
        return new() {
            Id=id, ConfigurationId=configurationId, Version=version, ChangedBy=changedBy, EffectiveFromUtc=effectiveFromUtc,
            ViewOnly=viewOnly is null ? null : viewOnly with { ConfigurationVersionId=id, EffectiveFromUtc=effectiveFromUtc },
            ViewPlusCommission=hybrid is null ? null : hybrid with { ConfigurationVersionId=id, EffectiveFromUtc=effectiveFromUtc },
            CreatorPayoutThreshold=creatorThreshold, CustomerPayoutThreshold=customerThreshold,
            Ugc=ugc is null ? null : ugc with { ConfigurationVersionId=id, EffectiveFromUtc=effectiveFromUtc },
            PromotionLiveDurationDays=duration, EnforceAudienceRequirements=audience,
            ApprovedCreatorCommissionPercent=creatorPercent, ApprovedCustomerCashbackPercent=customerPercent,
            ApprovedPlatformPercent=platformPercent
        };
    }

    public Money RequirePayoutThreshold(PayoutBeneficiary kind) =>
        (kind == PayoutBeneficiary.Creator ? CreatorPayoutThreshold : CustomerPayoutThreshold)
        ?? throw new Weymela.Application.ApplicationFailure(Weymela.Application.FailureKind.Validation,
            $"Configure the {kind} payout minimum before processing this payout.", code: "PayoutConfigurationRequired");
    public int RequirePromotionDuration() => PromotionLiveDurationDays
        ?? throw new Weymela.Application.ApplicationFailure(Weymela.Application.FailureKind.Validation,
            "Configure the promotion duration before creating this promotion.", code: "PromotionConfigurationRequired");
    public bool RequireAudienceRules() => EnforceAudienceRequirements
        ?? throw new Weymela.Application.ApplicationFailure(Weymela.Application.FailureKind.Validation,
            "Configure audience rules before accepting promotion applications.", code: "AudienceConfigurationRequired");

    public FinancialConfigurationVersion(Guid id, Guid configurationId, int version, Guid changedBy,
        DateTime effectiveFromUtc, PricingSnapshot viewOnly, PricingSnapshot hybrid,
        Money creatorThreshold, Money customerThreshold, UgcPricingSnapshot? ugc = null,
        int promotionLiveDurationDays = 30, bool enforceAudienceRequirements = false)
    {
        if (version <= 0 || creatorThreshold.Amount <= 0 || customerThreshold.Amount <= 0)
            throw new ArgumentException("Version and payout thresholds must be positive.");
        PromotionLiveDurationPolicy.Validate(promotionLiveDurationDays);
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
        PromotionLiveDurationDays = promotionLiveDurationDays;
        EnforceAudienceRequirements = enforceAudienceRequirements;
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
