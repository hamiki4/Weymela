namespace Weymela.Domain;

public enum OfferQrStatus { Issued, Used, Expired }
public enum OfferQrSource { ViewAndSalePromotion, UgcCustomerOffer }

public sealed class OfferQrSession
{
    private OfferQrSession() { TokenHash = null!; IdempotencyReference = null!; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CustomerId { get; private set; }
    public OfferQrSource Source { get; private set; }
    public Guid? PromotionId { get; private set; }
    public Guid? CreatorId { get; private set; }
    public Guid? CreatorAllocationId { get; private set; }
    public Guid? UgcCustomerOfferId { get; private set; }
    public Guid BusinessId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTime IssuedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }
    public Guid? SaleId { get; private set; }
    public Guid? UgcCustomerOfferSaleId { get; private set; }
    public OfferQrStatus Status { get; private set; } = OfferQrStatus.Issued;
    public string IdempotencyReference { get; private set; }
    public long Version { get; private set; }

    public OfferQrSession(Guid customer, Guid promotion, Guid creator, Guid allocation, Guid business, string tokenHash, DateTime issuedAt, string key)
    {
        if (tokenHash.Length != 64 || !tokenHash.All(Uri.IsHexDigit)) throw new ArgumentException("A SHA-256 token digest is required.");
        CustomerId = customer; Source = OfferQrSource.ViewAndSalePromotion; PromotionId = promotion; CreatorId = creator; CreatorAllocationId = allocation; BusinessId = business;
        TokenHash = tokenHash.ToUpperInvariant(); IssuedAtUtc = issuedAt; ExpiresAtUtc = issuedAt.AddMinutes(5); IdempotencyReference = key;
    }
    public static OfferQrSession ForUgcCustomerOffer(Guid customer, Guid offer, Guid business,
        string tokenHash, DateTime issuedAt, string key)
    {
        var session = new OfferQrSession();
        session.Initialize(customer, business, tokenHash, issuedAt, key);
        session.Source = OfferQrSource.UgcCustomerOffer; session.UgcCustomerOfferId = offer;
        return session;
    }
    private void Initialize(Guid customer, Guid business, string tokenHash, DateTime issuedAt, string key)
    {
        if (tokenHash.Length != 64 || !tokenHash.All(Uri.IsHexDigit)) throw new ArgumentException("A SHA-256 token digest is required.");
        CustomerId = customer; BusinessId = business; TokenHash = tokenHash.ToUpperInvariant();
        IssuedAtUtc = issuedAt; ExpiresAtUtc = issuedAt.AddMinutes(5); IdempotencyReference = key;
    }
    public void Validate(Guid business, DateTime now)
    {
        if (business != BusinessId) throw new InvalidOperationException("Offer belongs to another Business.");
        if (Status != OfferQrStatus.Issued || now >= ExpiresAtUtc || now < IssuedAtUtc)
            throw new InvalidOperationException("Offer QR is used or expired.");
    }
    public void Use(Guid saleId, Guid business, DateTime at)
    {
        Validate(business, at);
        if (Source != OfferQrSource.ViewAndSalePromotion) throw new InvalidOperationException("QR source does not match the Sale.");
        SaleId = saleId; UsedAtUtc = at; Status = OfferQrStatus.Used; Version++;
    }
    public void UseForUgcCustomerOffer(Guid saleId, Guid business, DateTime at)
    {
        Validate(business, at);
        if (Source != OfferQrSource.UgcCustomerOffer) throw new InvalidOperationException("QR source does not match the Sale.");
        UgcCustomerOfferSaleId = saleId; UsedAtUtc = at; Status = OfferQrStatus.Used; Version++;
    }
    public override string ToString() => $"OfferQrSession {Id} ({Status})";
    public void ObserveExpiry(DateTime now)
    {
        if (Status == OfferQrStatus.Issued && now >= ExpiresAtUtc) { Status = OfferQrStatus.Expired; Version++; }
    }
}
