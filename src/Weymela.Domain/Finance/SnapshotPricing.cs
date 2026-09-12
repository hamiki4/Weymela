namespace Weymela.Domain;

public sealed record SaleAmounts(Money Creator, Money Customer, Money Platform)
{
    public Money Total => Creator.Add(Customer).Add(Platform);
}

public static class SnapshotPricing
{
    public static decimal TotalSalePercent(PricingSnapshot p) =>
        p.CreatorCommissionPercent + p.CustomerCashbackPercent + p.PlatformPercent;

    public static void Validate(PricingSnapshot p, decimal? declaredTotalSalePercent = null)
    {
        if (p.ViewsPerReward <= 0 || p.BusinessCharge.Amount <= 0 ||
            p.CreatorEarning.Add(p.PlatformEarning) != p.BusinessCharge ||
            p.CreatorCommissionPercent < 0 || p.CustomerCashbackPercent < 0 || p.PlatformPercent < 0 ||
            TotalSalePercent(p) > 100 || (declaredTotalSalePercent.HasValue && declaredTotalSalePercent != TotalSalePercent(p)))
            throw new ArgumentException("Invalid financial pricing split.");
    }

    public static SaleAmounts Sale(PricingSnapshot p, Money purchase)
    {
        Validate(p);
        if (p.PromotionType != PromotionType.ViewPlusCommission)
            throw new InvalidOperationException("VIEW_ONLY cannot create a purchase.");
        if (purchase.Amount <= 0 || purchase.Currency != p.BusinessCharge.Currency || decimal.Round(purchase.Amount, 2) != purchase.Amount)
            throw new ArgumentException("Purchase amount must be positive currency units with at most two decimal places.");
        Money Part(decimal percent) => new(decimal.Round(purchase.Amount * percent / 100, 2, MidpointRounding.AwayFromZero), purchase.Currency);
        var result = new SaleAmounts(Part(p.CreatorCommissionPercent), Part(p.CustomerCashbackPercent), Part(p.PlatformPercent));
        if (result.Total.Amount <= 0) throw new ArgumentException("Purchase produces no payable campaign charge at currency precision.");
        return result;
    }
}
