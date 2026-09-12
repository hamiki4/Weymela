using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Finance;

public sealed class CheckoutService(WeymelaDbContext db, ICommerceAccessPolicy access, TimeProvider clock)
{
    public static string HashToken(SensitiveQrToken token)
    {
        if (token.Value.Length != 43 || token.Value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ApplicationFailure(FailureKind.Validation, "Invalid offer token.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Value)));
    }

    public Task<IssuedOfferQr> IssueAsync(IssueOfferQrCommand c, CancellationToken ct = default) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            await access.EnsureCustomerAsync(c.Actor, token);
            var op = new FinancialOperation(db);
            var fp = RequestFingerprint.Create(c.Actor.CustomerId.ToString()!, c.CreatorAllocationId.ToString());
            var replay = await op.Replay(c.Actor, "IssueOfferQr", c.IdempotencyKey, fp, token);
            if (replay is not null)
            {
                var old = await db.OfferQrSessions.SingleAsync(x => x.Id == Guid.Parse(replay), token);
                return new IssuedOfferQr(old.Id, old.ExpiresAtUtc, null, true);
            }
            var allocation = await db.CreatorAllocations.SingleOrDefaultAsync(x => x.Id == c.CreatorAllocationId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Offer not found.");
            var p = (await new PromotionRepository(db).GetAsync(allocation.PromotionId, token))!;
            await ValidateOffer(p, allocation, token);
            var raw = new SensitiveQrToken(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
            var session = new OfferQrSession(c.Actor.CustomerId!.Value, p.Id, allocation.CreatorId, allocation.Id, p.BusinessId, HashToken(raw),
                clock.GetUtcNow().UtcDateTime, c.IdempotencyKey);
            db.OfferQrSessions.Add(session);
            op.Remember(c.Actor, "IssueOfferQr", c.IdempotencyKey, fp, session.Id.ToString(), clock.GetUtcNow().UtcDateTime);
            op.Audit(c.Actor, "OfferQrIssued", Guid.NewGuid(), clock.GetUtcNow().UtcDateTime, p.Id, allocation.CreatorId);
            return new IssuedOfferQr(session.Id, session.ExpiresAtUtc, raw, false);
        }, ct);

    public async Task<SafeCheckoutOffer> ResolveAsync(Actor scanner, SensitiveQrToken rawToken, IPublicIdentityDirectory directory, CancellationToken ct = default)
    {
        if (scanner.BusinessId is null) throw new ApplicationFailure(FailureKind.Forbidden, "Checkout permission is required.");
        await access.EnsureScannerAsync(scanner, scanner.BusinessId.Value, ct);
        var hash = HashToken(rawToken);
        var session = await db.OfferQrSessions.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct)
            ?? throw new ApplicationFailure(FailureKind.NotFound, "Offer not found.");
        ValidateQr(session, scanner.BusinessId.Value);
        var p = await db.Promotions.AsNoTracking().SingleAsync(x => x.Id == session.PromotionId, ct);
        var a = await db.CreatorAllocations.AsNoTracking().SingleAsync(x => x.Id == session.CreatorAllocationId, ct);
        await ValidateOffer(p, a, ct);
        await access.EnsureCustomerIdAsync(session.CustomerId, ct);
        return new(session.Id, p.Title, await directory.BusinessAsync(session.BusinessId, ct),
            await directory.CreatorAsync(session.CreatorId, ct), session.ExpiresAtUtc);
    }

    public Task<SaleResult> RedeemAsync(RedeemOfferCommand c, CancellationToken ct = default) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            if (c.Actor.BusinessId is null) throw new ApplicationFailure(FailureKind.Forbidden, "Checkout permission is required.");
            await access.EnsureScannerAsync(c.Actor, c.Actor.BusinessId.Value, token);
            var hash = HashToken(c.Token);
            var fp = RequestFingerprint.Create(c.Actor.BusinessId.ToString()!, hash, RequestFingerprint.Amount(c.PurchaseAmount));
            var op = new FinancialOperation(db);
            var replay = await op.Replay(c.Actor, "RedeemOfferQr", c.IdempotencyKey, fp, token);
            if (replay is not null) return Result(await db.VerifiedSales.SingleAsync(x => x.Id == Guid.Parse(replay), token));
            var session = await db.OfferQrSessions.SingleOrDefaultAsync(x => x.TokenHash == hash, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Offer not found.");
            ValidateQr(session, c.Actor.BusinessId.Value);
            var binding = new CheckoutBinding(session.CustomerId, session.PromotionId, session.CreatorId, session.CreatorAllocationId, session.BusinessId, session.Id.ToString());
            var sale = await ExecuteSale(c.Actor, binding, c.PurchaseAmount, c.IdempotencyKey, token);
            session.Use(sale.Id, c.Actor.BusinessId.Value, clock.GetUtcNow().UtcDateTime);
            op.Remember(c.Actor, "RedeemOfferQr", c.IdempotencyKey, fp, sale.Id.ToString(), clock.GetUtcNow().UtcDateTime);
            return Result(sale);
        }, ct);

    // Manual lookup is intentionally not implemented. A future resolver must call this same
    // transaction engine through a trusted adapter, never supply split amounts or wallet sources.
    internal async Task<VerifiedSale> ExecuteSale(Actor actor, CheckoutBinding binding, Money purchase, string key, CancellationToken ct)
    {
        await access.EnsureScannerAsync(actor, binding.BusinessId, ct);
        await access.EnsureCustomerIdAsync(binding.CustomerId, ct);
        var p = await new PromotionRepository(db).GetAsync(binding.PromotionId, ct) ?? throw new ApplicationFailure(FailureKind.NotFound, "Offer not found.");
        var a = p.Allocations.SingleOrDefault(x => x.Id == binding.CreatorAllocationId && x.CreatorId == binding.CreatorId)
            ?? throw new ApplicationFailure(FailureKind.Validation, "Offer binding does not match the Creator Budget.");
        if (p.BusinessId != binding.BusinessId) throw new ApplicationFailure(FailureKind.Forbidden, "Offer belongs to another Business.");
        await ValidateOffer(p, a, ct);
        var amounts = SnapshotPricing.Sale(p.PricingSnapshot, purchase);
        if (a.RemainingAmount.Amount < amounts.Total.Amount)
            throw new ApplicationFailure(FailureKind.InsufficientFunds, "Creator Budget cannot cover the complete sale charge.");
        var sale = new VerifiedSale(p, a.CreatorId, a.Id, binding.CustomerId, actor.UserId, purchase, amounts.Creator, amounts.Customer,
            amounts.Platform, binding.SourceReference, RequestFingerprint.Create(actor.UserId.ToString(), key), clock.GetUtcNow().UtcDateTime);
        await new AttributableFinance(db).PostAsync(actor, p, a, amounts, JournalSourceType.VerifiedSale, key, clock.GetUtcNow().UtcDateTime, sale, ct);
        return sale;
    }

    private async Task ValidateOffer(Promotion p, CreatorAllocation a, CancellationToken ct)
    {
        if (p.PromotionType != PromotionType.ViewPlusCommission) throw new ApplicationFailure(FailureKind.Validation, "VIEW_ONLY is not a purchase offer.");
        VerifiedViewService.EnsureCampaignActive(p, clock.GetUtcNow().UtcDateTime);
        await access.EnsureBusinessAsync(p.BusinessId, ct);
        if (a.Status != CreatorAllocationStatus.Active || a.ActivatedAtUtc is null || a.RemainingAmount.Amount <= 0 ||
            !await db.CreatorPromotionParticipations.AnyAsync(x => x.CreatorAllocationId == a.Id && x.Status == ParticipationStatus.Active, ct))
            throw new ApplicationFailure(FailureKind.InsufficientFunds, "Creator participation is not active and funded.");
    }
    private void ValidateQr(OfferQrSession session, Guid business)
    {
        if (session.BusinessId != business) throw new ApplicationFailure(FailureKind.Forbidden, "Offer belongs to another Business.");
        try { session.Validate(business, clock.GetUtcNow().UtcDateTime); }
        catch (InvalidOperationException) { throw new ApplicationFailure(FailureKind.Validation, "Offer QR is used or expired."); }
    }
    private static SaleResult Result(VerifiedSale sale) => new(sale.Id, sale.PurchaseAmount, sale.TotalPromotionCharge, sale.CreatedAtUtc);
}
