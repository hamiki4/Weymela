namespace Weymela.Domain;

public enum UgcCustomerOfferStatus { Draft, Active, Exhausted, Cancelled }

public sealed record UgcCustomerOfferPricingSnapshot(
    decimal PlatformSalePercent,
    DateTime EffectiveFromUtc,
    Guid ConfigurationVersionId)
{
    public bool IsValid => PlatformSalePercent is >= 0 and <= 100;
}

public readonly record struct UgcCustomerOfferQuote(
    Money PurchaseAmount,
    Money CustomerDiscount,
    Money CustomerPays,
    Money PlatformFee,
    Money FundConsumption);

public sealed class UgcCustomerOffer
{
    private UgcCustomerOffer() { CustomerFacingSlogan = null!; PricingSnapshot = null!; }

    public Guid Id { get; } = Guid.NewGuid();
    public Guid UgcOpportunityId { get; }
    public Guid BusinessId { get; }
    public string? CustomerFacingSlogan { get; private set; }
    public decimal CustomerDiscountPercent { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public Money FundedLimit { get; private set; }
    public Money ReservedFunding { get; private set; }
    public Money UsedFunding { get; private set; }
    public Money RemainingFunding => FundedLimit.Subtract(UsedFunding);
    public UgcCustomerOfferPricingSnapshot PricingSnapshot { get; }
    public UgcCustomerOfferStatus Status { get; private set; } = UgcCustomerOfferStatus.Draft;
    public DateTime CreatedAtUtc { get; }
    public DateTime? PublishedAtUtc { get; private set; }
    public long Version { get; private set; }

    public UgcCustomerOffer(Guid ugcOpportunityId, Guid businessId, string? customerFacingSlogan,
        decimal customerDiscountPercent, DateTime startsAtUtc, DateTime endsAtUtc, Money fundedLimit,
        UgcCustomerOfferPricingSnapshot pricing, DateTime now)
    {
        if (ugcOpportunityId == Guid.Empty || businessId == Guid.Empty)
            throw new ArgumentException("UGC Customer Offer ownership is required.");
        if (customerDiscountPercent is <= 0 or > 100 || decimal.Round(customerDiscountPercent, 4) != customerDiscountPercent)
            throw new ArgumentException("Customer discount must be between zero and 100 with no more than four decimal places.");
        if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc <= startsAtUtc)
            throw new ArgumentException("Customer Offer dates must be valid UTC dates.");
        if (fundedLimit.Amount <= 0 || !pricing.IsValid)
            throw new ArgumentException("Customer Offer funding and pricing are required.");
        UgcOpportunityId = ugcOpportunityId; BusinessId = businessId;
        CustomerFacingSlogan = Clean(customerFacingSlogan, 160);
        CustomerDiscountPercent = customerDiscountPercent;
        StartsAtUtc = startsAtUtc; EndsAtUtc = endsAtUtc; FundedLimit = fundedLimit;
        ReservedFunding = Money.Zero(fundedLimit.Currency); UsedFunding = Money.Zero(fundedLimit.Currency);
        PricingSnapshot = pricing; CreatedAtUtc = now;
    }

    public void Publish(BusinessWallet wallet, DateTime now, Guid correlation)
    {
        Ensure(UgcCustomerOfferStatus.Draft);
        wallet.Reserve(FundedLimit, now, correlation);
        ReservedFunding = FundedLimit; Status = UgcCustomerOfferStatus.Active;
        PublishedAtUtc = now; Version++;
    }

    public void UpdateDraft(string? customerFacingSlogan, decimal customerDiscountPercent,
        DateTime startsAtUtc, DateTime endsAtUtc, Money fundedLimit)
    {
        Ensure(UgcCustomerOfferStatus.Draft);
        if (customerDiscountPercent is <= 0 or > 100 || decimal.Round(customerDiscountPercent, 4) != customerDiscountPercent)
            throw new ArgumentException("Customer discount must be between zero and 100 with no more than four decimal places.");
        if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc <= startsAtUtc)
            throw new ArgumentException("Customer Offer dates must be valid UTC dates.");
        if (fundedLimit.Amount <= 0) throw new ArgumentException("Customer Offer funding is required.");
        CustomerFacingSlogan = Clean(customerFacingSlogan, 160); CustomerDiscountPercent = customerDiscountPercent;
        StartsAtUtc = startsAtUtc; EndsAtUtc = endsAtUtc; FundedLimit = fundedLimit;
        ReservedFunding = Money.Zero(fundedLimit.Currency); UsedFunding = Money.Zero(fundedLimit.Currency); Version++;
    }

    public UgcCustomerOfferQuote Quote(Money purchase, DateTime now)
    {
        EnsureAvailable(now);
        if (purchase.Amount <= 0) throw new InvalidOperationException("Enter a positive Sale amount.");
        var discount = Percentage(purchase, CustomerDiscountPercent);
        var platformFee = Percentage(purchase, PricingSnapshot.PlatformSalePercent);
        var consumption = discount.Add(platformFee);
        if (discount.Amount <= 0 || consumption.Amount <= 0 || consumption.Amount > RemainingFunding.Amount)
            throw new InvalidOperationException("Offer no longer available.");
        return new(purchase, discount, purchase.Subtract(discount), platformFee, consumption);
    }

    public void Consume(UgcCustomerOfferQuote quote, BusinessWallet wallet, DateTime now, Guid correlation)
    {
        EnsureAvailable(now);
        if (quote.FundConsumption.Amount > RemainingFunding.Amount)
            throw new InvalidOperationException("Offer no longer available.");
        wallet.ConsumeReservedFunds(quote.FundConsumption, now, correlation);
        ReservedFunding = ReservedFunding.Subtract(quote.FundConsumption);
        UsedFunding = UsedFunding.Add(quote.FundConsumption);
        if (RemainingFunding.Amount == 0) Status = UgcCustomerOfferStatus.Exhausted;
        Version++;
    }

    public void Cancel(BusinessWallet wallet, DateTime now, Guid correlation)
    {
        Ensure(UgcCustomerOfferStatus.Draft, UgcCustomerOfferStatus.Active);
        if (ReservedFunding.Amount > 0) wallet.ReleaseReserve(ReservedFunding, now, correlation);
        ReservedFunding = Money.Zero(FundedLimit.Currency); Status = UgcCustomerOfferStatus.Cancelled; Version++;
    }

    public void EnsureAvailable(DateTime now)
    {
        if (Status != UgcCustomerOfferStatus.Active || now < StartsAtUtc || now >= EndsAtUtc || RemainingFunding.Amount <= 0)
            throw new InvalidOperationException("Offer no longer available.");
    }

    private static Money Percentage(Money amount, decimal percent) => new(
        decimal.Round(amount.Amount * percent / 100m, 2, MidpointRounding.AwayFromZero), amount.Currency);
    private void Ensure(params UgcCustomerOfferStatus[] allowed)
    { if (!allowed.Contains(Status)) throw new InvalidOperationException("Customer Offer is not available in its current state."); }
    private static string? Clean(string? value, int maximum)
    { if (string.IsNullOrWhiteSpace(value)) return null; var result = value.Trim(); if (result.Length > maximum) throw new ArgumentException("Customer Offer information is too long."); return result; }
}

public sealed class UgcCustomerOfferSale
{
    private UgcCustomerOfferSale() { QrTokenReference = null!; IdempotencyKey = null!; }
    public Guid Id { get; } = Guid.NewGuid();
    public Guid UgcCustomerOfferId { get; }
    public Guid UgcOpportunityId { get; }
    public Guid BusinessId { get; }
    public Guid CustomerId { get; }
    public Guid CashierId { get; }
    public Money PurchaseAmount { get; }
    public Money CustomerDiscountAmount { get; }
    public Money CustomerPaysAmount { get; }
    public Money PlatformRevenueAmount { get; }
    public Money TotalOfferCharge { get; }
    public string QrTokenReference { get; }
    public string IdempotencyKey { get; }
    public DateTime CreatedAtUtc { get; }

    public UgcCustomerOfferSale(UgcCustomerOffer offer, Guid customerId, Guid cashierId,
        UgcCustomerOfferQuote quote, string qrReference, string idempotencyKey, DateTime now)
    {
        if (customerId == Guid.Empty || cashierId == Guid.Empty || string.IsNullOrWhiteSpace(qrReference)
            || string.IsNullOrWhiteSpace(idempotencyKey) || quote.PurchaseAmount.Amount <= 0
            || quote.CustomerDiscount.Amount <= 0 || quote.CustomerPays.Amount < 0
            || quote.CustomerPays.Add(quote.CustomerDiscount) != quote.PurchaseAmount
            || quote.CustomerDiscount.Add(quote.PlatformFee) != quote.FundConsumption)
            throw new ArgumentException("UGC Customer Offer Sale amounts are invalid.");
        UgcCustomerOfferId = offer.Id; UgcOpportunityId = offer.UgcOpportunityId;
        BusinessId = offer.BusinessId; CustomerId = customerId; CashierId = cashierId;
        PurchaseAmount = quote.PurchaseAmount; CustomerDiscountAmount = quote.CustomerDiscount;
        CustomerPaysAmount = quote.CustomerPays; PlatformRevenueAmount = quote.PlatformFee;
        TotalOfferCharge = quote.FundConsumption; QrTokenReference = qrReference;
        IdempotencyKey = idempotencyKey; CreatedAtUtc = now;
    }
}
