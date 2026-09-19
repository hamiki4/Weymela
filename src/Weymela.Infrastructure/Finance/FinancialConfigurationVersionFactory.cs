using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Finance;

public static class FinancialConfigurationVersionFactory
{
    public static FinancialConfigurationVersion Create(Guid id, Guid configurationId, int version,
        Guid changedBy, DateTime effectiveFromUtc, FinancialSettingsInput input)
    {
        PricingSnapshot Price(PromotionType type, ViewPriceInput price) => new(type,
            price.ViewsPerReward, Amount(price.BusinessPays), NonNegative(price.CreatorEarns),
            NonNegative(price.PlatformKeeps), input.CreatorCommissionPercent,
            input.CustomerCashbackPercent, input.PlatformPercent, effectiveFromUtc, id,
            price.MinimumCampaignBudget is { } minimum ? Amount(minimum) : null);

        var ugcInput = input.Ugc ?? new UgcSettingsInput(200m, 10m, null);
        if (ugcInput.PlatformFeePercent is < 0 or > 100 || decimal.Round(ugcInput.PlatformFeePercent, 4) != ugcInput.PlatformFeePercent)
            throw new ApplicationFailure(FailureKind.Validation, "Enter a valid UGC Platform fee percentage.");
        if (ugcInput.CustomerOfferPlatformSalePercent is { } offerPercent
            && (offerPercent is < 0 or > 100 || decimal.Round(offerPercent, 4) != offerPercent))
            throw new ApplicationFailure(FailureKind.Validation, "Enter a valid UGC Customer Offer Platform Sale fee percentage.");
        if (ugcInput.MaximumCustomerDiscountPercent is { } maximumDiscount
            && (maximumDiscount is <= 0 or > 100 || decimal.Round(maximumDiscount, 4) != maximumDiscount))
            throw new ApplicationFailure(FailureKind.Validation, "Enter a valid maximum UGC Customer Discount percentage.");
        var ugc = new UgcPricingSnapshot(Amount(ugcInput.MinimumCreatorPayment), ugcInput.PlatformFeePercent,
            ugcInput.MinimumUgcBudget is { } minimumUgc ? Amount(minimumUgc) : null, effectiveFromUtc, id,
            ugcInput.CustomerOfferPlatformSalePercent, ugcInput.MaximumCustomerDiscountPercent);
        return new FinancialConfigurationVersion(id, configurationId, version, changedBy,
            effectiveFromUtc, Price(PromotionType.ViewOnly, input.ViewOnly),
            Price(PromotionType.ViewPlusCommission, input.ViewPlusCommission),
            Amount(input.CreatorThreshold), Amount(input.CustomerThreshold), ugc);
    }

    private static Money Amount(decimal value)
    {
        if (value <= 0 || value > 9999999999999999.99m || decimal.Round(value, 2) != value)
            throw new ApplicationFailure(FailureKind.Validation,
                "Enter a positive amount with no more than two decimal places.");
        return new(value);
    }

    private static Money NonNegative(decimal value) => value == 0 ? Money.Zero() : Amount(value);
}
