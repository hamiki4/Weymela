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
                if (old.CustomerId != c.Actor.CustomerId || old.CreatorAllocationId != c.CreatorAllocationId)
                    throw new ApplicationFailure(FailureKind.Forbidden, "Offer QR does not belong to this Customer offer.");
                var oldAllocation = await db.CreatorAllocations.SingleAsync(x => x.Id == c.CreatorAllocationId, token);
                var oldPromotion = (await new PromotionRepository(db).GetAsync(oldAllocation.PromotionId, token))!;
                await ValidateOffer(oldPromotion, oldAllocation, token);
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

    public async Task<IssuedOfferQr> IssueCustomerOfferAsync(Actor actor, Guid offerId, string key, CancellationToken ct = default)
    {
        if (await db.UgcCustomerOffers.AsNoTracking().AnyAsync(x => x.Id == offerId, ct))
            return await IssueUgcAsync(new(actor, offerId, key), ct);
        return await IssueAsync(new(actor, offerId, key), ct);
    }

    public Task<IssuedOfferQr> IssueUgcAsync(IssueUgcCustomerOfferQrCommand c, CancellationToken ct = default) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            await access.EnsureCustomerAsync(c.Actor, token);
            var op = new FinancialOperation(db);
            var fp = RequestFingerprint.Create(c.Actor.CustomerId.ToString()!, c.UgcCustomerOfferId.ToString());
            var replay = await op.Replay(c.Actor, "IssueUgcCustomerOfferQr", c.IdempotencyKey, fp, token);
            if (replay is not null)
            {
                var old = await db.OfferQrSessions.SingleAsync(x => x.Id == Guid.Parse(replay), token);
                return new IssuedOfferQr(old.Id, old.ExpiresAtUtc, null, true);
            }
            var offer = await db.UgcCustomerOffers.SingleOrDefaultAsync(x => x.Id == c.UgcCustomerOfferId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Offer not found.");
            await ValidateUgcOffer(offer, token);
            var raw = NewToken();
            var session = OfferQrSession.ForUgcCustomerOffer(c.Actor.CustomerId!.Value, offer.Id, offer.BusinessId,
                HashToken(raw), clock.GetUtcNow().UtcDateTime, c.IdempotencyKey);
            db.OfferQrSessions.Add(session);
            op.Remember(c.Actor, "IssueUgcCustomerOfferQr", c.IdempotencyKey, fp, session.Id.ToString(), clock.GetUtcNow().UtcDateTime);
            db.AuditEvents.Add(new(Guid.NewGuid(), "UgcCustomerOfferQrIssued", c.Actor.UserId, offer.BusinessId,
                null, null, Guid.NewGuid(), clock.GetUtcNow().UtcDateTime, "", offer.UgcOpportunityId, offer.Id));
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
        await access.EnsureCustomerIdAsync(session.CustomerId, ct);
        if (session.Source == OfferQrSource.UgcCustomerOffer)
        {
            var offer = await db.UgcCustomerOffers.AsNoTracking().SingleAsync(x => x.Id == session.UgcCustomerOfferId, ct);
            await ValidateUgcOffer(offer, ct);
            return new(session.Id, offer.CustomerFacingSlogan ?? $"{offer.CustomerDiscountPercent:0.####}% off",
                await directory.BusinessAsync(session.BusinessId, ct), null, session.ExpiresAtUtc,
                "UGC_CUSTOMER_OFFER", offer.CustomerDiscountPercent);
        }
        var p = await db.Promotions.AsNoTracking().SingleAsync(x => x.Id == session.PromotionId!.Value, ct);
        var a = await db.CreatorAllocations.AsNoTracking().SingleAsync(x => x.Id == session.CreatorAllocationId!.Value, ct);
        await ValidateOffer(p, a, ct);
        return new(session.Id, p.Title, await directory.BusinessAsync(session.BusinessId, ct),
            await directory.CreatorAsync(session.CreatorId!.Value, ct), session.ExpiresAtUtc);
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
            if (replay is not null)
            {
                var id = Guid.Parse(replay);
                var oldPromotionSale = await db.VerifiedSales.SingleOrDefaultAsync(x => x.Id == id, token);
                if (oldPromotionSale is not null) return Result(oldPromotionSale);
                return Result(await db.UgcCustomerOfferSales.SingleAsync(x => x.Id == id, token));
            }
            var session = await db.OfferQrSessions.SingleOrDefaultAsync(x => x.TokenHash == hash, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Offer not found.");
            ValidateQr(session, c.Actor.BusinessId.Value);
            if (session.Source == OfferQrSource.UgcCustomerOffer)
            {
                var sale = await ExecuteUgcCustomerOfferSale(c.Actor, session, c.PurchaseAmount, c.IdempotencyKey, token);
                session.UseForUgcCustomerOffer(sale.Id, c.Actor.BusinessId.Value, clock.GetUtcNow().UtcDateTime);
                op.Remember(c.Actor, "RedeemOfferQr", c.IdempotencyKey, fp, sale.Id.ToString(), clock.GetUtcNow().UtcDateTime);
                return Result(sale);
            }
            var binding = new CheckoutBinding(session.CustomerId, session.PromotionId!.Value, session.CreatorId!.Value,
                session.CreatorAllocationId!.Value, session.BusinessId, session.Id.ToString());
            var promotionSale = await ExecuteSale(c.Actor, binding, c.PurchaseAmount, c.IdempotencyKey, token);
            session.Use(promotionSale.Id, c.Actor.BusinessId.Value, clock.GetUtcNow().UtcDateTime);
            op.Remember(c.Actor, "RedeemOfferQr", c.IdempotencyKey, fp, promotionSale.Id.ToString(), clock.GetUtcNow().UtcDateTime);
            return Result(promotionSale);
        }, ct);

    private async Task<UgcCustomerOfferSale> ExecuteUgcCustomerOfferSale(Actor actor, OfferQrSession session,
        Money purchase, string key, CancellationToken ct)
    {
        var offer = await db.UgcCustomerOffers.SingleAsync(x => x.Id == session.UgcCustomerOfferId, ct);
        if (offer.BusinessId != session.BusinessId || offer.BusinessId != actor.BusinessId)
            throw new ApplicationFailure(FailureKind.Forbidden, "Offer belongs to another Business.");
        await ValidateUgcOffer(offer, ct);
        await access.EnsureCustomerIdAsync(session.CustomerId, ct);
        UgcCustomerOfferQuote quote;
        try { quote = offer.Quote(purchase, clock.GetUtcNow().UtcDateTime); }
        catch (InvalidOperationException ex) { throw new ApplicationFailure(FailureKind.InsufficientFunds, "Offer no longer available.", ex); }

        var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == offer.BusinessId, ct);
        var correlation = Guid.NewGuid();
        var sale = new UgcCustomerOfferSale(offer, session.CustomerId, actor.UserId, quote,
            session.Id.ToString(), RequestFingerprint.Create(actor.UserId.ToString(), key), clock.GetUtcNow().UtcDateTime);
        offer.Consume(quote, wallet, clock.GetUtcNow().UtcDateTime, correlation);
        var journal = new FinancialJournal(Guid.NewGuid().ToString("N"), correlation, actor.UserId,
            JournalSourceType.UgcCustomerOfferSale, clock.GetUtcNow().UtcDateTime, key);
        journal.AddLine(JournalLineType.Debit, quote.FundConsumption, "UgcCustomerOfferReserve");
        journal.AddLine(JournalLineType.Credit, quote.CustomerDiscount, "CustomerSaleDiscount");
        if (quote.PlatformFee.Amount > 0) journal.AddLine(JournalLineType.Credit, quote.PlatformFee, "PlatformRevenue");
        journal.Post(); db.FinancialJournals.Add(journal);
        db.Entry(journal).Property("BusinessId").CurrentValue = offer.BusinessId;
        db.Entry(journal).Property("CustomerId").CurrentValue = session.CustomerId;
        db.Entry(journal).Property("UgcOpportunityId").CurrentValue = offer.UgcOpportunityId;
        db.Entry(journal).Property("UgcCustomerOfferId").CurrentValue = offer.Id;
        db.Entry(journal).Property("UgcCustomerOfferSaleId").CurrentValue = sale.Id;
        db.UgcCustomerOfferSales.Add(sale); db.Entry(sale).Property("JournalId").CurrentValue = journal.Id;
        db.WalletEntries.Add(new(Guid.NewGuid(), offer.BusinessId, null, quote.FundConsumption,
            "UgcCustomerOfferConsumed", journal.Id, clock.GetUtcNow().UtcDateTime, offer.UgcOpportunityId, offer.Id));
        db.UgcCustomerOfferBudgetEntries.Add(new(Guid.NewGuid(), offer.Id, sale.Id, quote.FundConsumption,
            "Sale", journal.Id, clock.GetUtcNow().UtcDateTime));
        if (quote.PlatformFee.Amount > 0)
        {
            var revenue = new PlatformRevenueEntry(Guid.NewGuid(), null, PlatformRevenueSource.UgcCustomerOfferSaleFee,
                quote.PlatformFee, RevenueStatus.Accrued, clock.GetUtcNow().UtcDateTime, correlation, null, sale.Id);
            db.PlatformRevenueEntries.Add(revenue); db.Entry(revenue).Property("JournalId").CurrentValue = journal.Id;
        }
        db.AuditEvents.Add(new(Guid.NewGuid(), "UgcCustomerOfferSaleCompleted", actor.UserId, offer.BusinessId,
            null, null, correlation, clock.GetUtcNow().UtcDateTime, "", offer.UgcOpportunityId, offer.Id));
        db.OutboxMessages.Add(new() { EventType = "SaleCompleted", Payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            SaleId = sale.Id, offer.BusinessId, CustomerId = session.CustomerId, UgcCustomerOfferId = offer.Id,
            PurchaseAmount = purchase.Amount, CustomerDiscount = quote.CustomerDiscount.Amount,
            CustomerPays = quote.CustomerPays.Amount, PlatformFee = quote.PlatformFee.Amount
        }), OccurredAtUtc = clock.GetUtcNow().UtcDateTime });
        return sale;
    }

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
        var now = clock.GetUtcNow().UtcDateTime;
        VerifiedViewService.EnsureCampaignActive(p, now);
        await access.EnsureBusinessAsync(p.BusinessId, ct);
        if (a.Status != CreatorAllocationStatus.Active || a.ActivatedAtUtc is null || a.RemainingAmount.Amount <= 0)
            throw new ApplicationFailure(FailureKind.InsufficientFunds, "Creator participation is not active and funded.");
        var participation = await db.CreatorPromotionParticipations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CreatorAllocationId == a.Id && x.Status == ParticipationStatus.Active, ct);
        if (participation is null)
            throw new ApplicationFailure(FailureKind.InsufficientFunds, "Creator participation is not active and funded.");
        if (!participation.IsLive(now,p.PromotionLiveDurationDays))
            throw new ApplicationFailure(FailureKind.Validation, "Creator participation has ended.");
    }
    private async Task ValidateUgcOffer(UgcCustomerOffer offer, CancellationToken ct)
    {
        try { offer.EnsureAvailable(clock.GetUtcNow().UtcDateTime); }
        catch (InvalidOperationException ex) { throw new ApplicationFailure(FailureKind.Validation, "Offer no longer available.", ex); }
        await access.EnsureBusinessAsync(offer.BusinessId, ct);
    }
    private void ValidateQr(OfferQrSession session, Guid business)
    {
        if (session.BusinessId != business) throw new ApplicationFailure(FailureKind.Forbidden, "Offer belongs to another Business.");
        try { session.Validate(business, clock.GetUtcNow().UtcDateTime); }
        catch (InvalidOperationException) { throw new ApplicationFailure(FailureKind.Validation, "Offer QR is used or expired."); }
    }
    private static SaleResult Result(VerifiedSale sale) => new(sale.Id, sale.PurchaseAmount, sale.TotalPromotionCharge, sale.CreatedAtUtc);
    private static SaleResult Result(UgcCustomerOfferSale sale) => new(sale.Id, sale.PurchaseAmount,
        sale.TotalOfferCharge, sale.CreatedAtUtc, sale.CustomerPaysAmount, sale.CustomerDiscountAmount, "UGC_CUSTOMER_OFFER");
    private static SensitiveQrToken NewToken() => new(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_'));
}
