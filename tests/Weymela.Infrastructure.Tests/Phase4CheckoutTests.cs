using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class Phase4CheckoutTests(PostgresFixture fixture)
{
    [Fact] public async Task Hybrid_issue_binds_all_parties_and_stores_only_hash_without_secret_in_controls()
    {
        var s = await Phase4Scenario.Create(fixture); var qr = await s.Issue(); await using var db = s.Database.Open();
        var stored = await db.OfferQrSessions.SingleAsync();
        Assert.Equal(CheckoutService.HashToken(qr.Token!), stored.TokenHash); Assert.NotEqual(qr.Token!.Value, stored.TokenHash);
        Assert.Equal(s.Customer.CustomerId, stored.CustomerId); Assert.Equal(s.Creator.CreatorId, stored.CreatorId);
        Assert.Equal(s.AllocationId, stored.CreatorAllocationId); Assert.Equal(s.Seed.PromotionId, stored.PromotionId);
        Assert.Equal(s.Seed.Business.BusinessId, stored.BusinessId);
        Assert.Equal(TimeSpan.FromMinutes(5), stored.ExpiresAtUtc - stored.IssuedAtUtc);
        var controls = JsonSerializer.Serialize(new { Stored = stored, Idempotency = await db.IdempotencyRecords.ToListAsync(),
            Audit = await db.AuditEvents.ToListAsync(), Outbox = await db.OutboxMessages.ToListAsync() });
        Assert.DoesNotContain(qr.Token.Value, controls);
        Assert.DoesNotContain(qr.Token.Value, new RedeemOfferCommand(s.Cashier, qr.Token, new Money(1000), "key").ToString());
    }
    [Fact] public async Task View_only_is_absent_from_customer_discovery_and_rejects_qr()
    {
        var s = await Phase4Scenario.Create(fixture, PromotionType.ViewOnly);
        await Assert.ThrowsAsync<ApplicationFailure>(() => s.Issue()); await using var db = s.Database.Open();
        Assert.Empty(await s.Queries(db).CustomerOffersAsync(s.Customer)); Assert.Empty(await db.OfferQrSessions.ToListAsync());
    }
    [Fact] public async Task Issue_retry_returns_same_session_reference_without_persisting_recoverable_raw_token()
    {
        var s = await Phase4Scenario.Create(fixture); var qr = await s.Issue(); var repeat = await s.Issue();
        Assert.Equal(qr.SessionId, repeat.SessionId); Assert.True(repeat.Replayed); Assert.Null(repeat.Token);
        await using var db = s.Database.Open(); Assert.Single(await db.OfferQrSessions.ToListAsync());
    }
    [Fact] public async Task Exact_expiry_boundary_rejects_without_balance_or_qr_mutation()
    {
        var s = await Phase4Scenario.Create(fixture); var qr = await s.Issue(); s.Clock.Now = qr.ExpiresAtUtc;
        await Assert.ThrowsAsync<ApplicationFailure>(() => s.Redeem(qr)); await using var db = s.Database.Open();
        Assert.Empty(await db.VerifiedSales.ToListAsync()); Assert.Equal(OfferQrStatus.Issued, (await db.OfferQrSessions.SingleAsync()).Status);
        Assert.Equal(2000, (await db.CreatorAllocations.SingleAsync()).RemainingAmount.Amount);
    }
    [Fact] public async Task Expired_qr_replacement_has_new_token_and_retains_old_history()
    {
        var s = await Phase4Scenario.Create(fixture); var old = await s.Issue(); s.Clock.Now = old.ExpiresAtUtc;
        var replacement = await s.Issue("replacement"); Assert.NotEqual(old.Token!.Value, replacement.Token!.Value);
        await s.Redeem(replacement); await using var db = s.Database.Open();
        Assert.Equal(2, await db.OfferQrSessions.CountAsync());
        Assert.Equal(OfferQrStatus.Issued, (await db.OfferQrSessions.SingleAsync(x => x.Id == old.SessionId)).Status);
    }
    [Fact] public async Task Wrong_business_fails_without_consuming_then_right_business_can_redeem()
    {
        var s = await Phase4Scenario.Create(fixture); var qr = await s.Issue(); var wrong = new Actor(Guid.NewGuid(), ActorRole.Business, Guid.NewGuid());
        await using (var db = s.Database.Open())
        {
            db.CommercePermissions.Add(new(wrong.UserId, ActorRole.Business, wrong.BusinessId!.Value, wrong.BusinessId, true, true)); await db.SaveChangesAsync();
        }
        var error = await Assert.ThrowsAsync<ApplicationFailure>(() => s.Redeem(qr, scanner: wrong)); Assert.Equal(FailureKind.Forbidden, error.Kind);
        await using (var db = s.Database.Open()) { Assert.Empty(await db.VerifiedSales.ToListAsync()); Assert.Null((await db.OfferQrSessions.SingleAsync()).UsedAtUtc); }
        await s.Redeem(qr); await using var after = s.Database.Open(); Assert.Single(await after.VerifiedSales.ToListAsync());
    }
    [Fact] public async Task Assigned_cashier_and_owner_use_the_same_checkout_path()
    {
        var s = await Phase4Scenario.Create(fixture); await s.Redeem(await s.Issue());
        await s.Redeem(await s.Issue("owner-offer"), scanner: s.Seed.Business, key: "owner-sale");
        await using var db = s.Database.Open(); Assert.Equal(2, await db.VerifiedSales.CountAsync());
        Assert.Equal(200, (await db.CreatorAllocations.SingleAsync()).UsedAmount.Amount);
        Assert.Equal(40, (await db.CustomerCashbackAccounts.SingleAsync()).AvailableCashback.Amount);
    }
    [Fact] public async Task Role_claim_without_assigned_checkout_permission_is_rejected()
    {
        var s = await Phase4Scenario.Create(fixture); var qr = await s.Issue();
        var error = await Assert.ThrowsAsync<ApplicationFailure>(() => s.Redeem(qr, scanner: s.Cashier with { UserId = Guid.NewGuid() }));
        Assert.Equal(FailureKind.Forbidden, error.Kind);
    }
    [Fact] public async Task Sale_1000_posts_45_creator_20_customer_35_platform_and_100_bound_budget()
    {
        var s = await Phase4Scenario.Create(fixture); var result = await s.Redeem(await s.Issue());
        Assert.Equal(100, result.TotalBusinessCharge.Amount); await using var db = s.Database.Open();
        Assert.Equal(45, (await db.CreatorEarningsAccounts.SingleAsync()).AvailableEarnings.Amount);
        Assert.Equal(20, (await db.CustomerCashbackAccounts.SingleAsync()).AvailableCashback.Amount);
        Assert.Equal(35, (await db.PlatformRevenueEntries.SingleAsync()).Amount.Amount);
        Assert.Equal(PlatformRevenueSource.SalePlatformShare, (await db.PlatformRevenueEntries.SingleAsync()).Source);
        Assert.Equal(1900, (await db.CreatorAllocations.SingleAsync()).RemainingAmount.Amount);
        Assert.Equal(4000, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
        Assert.Equal(5900, (await db.BusinessWallets.SingleAsync()).ReservedBalance.Amount);
        Assert.Equal(9900, (await db.BusinessWallets.SingleAsync()).TotalBalance.Amount);
        var journal = await db.FinancialJournals.Include(x => x.Lines).SingleAsync(x => x.SourceType == JournalSourceType.VerifiedSale);
        Assert.Equal(0, journal.Lines.Sum(x => x.Type == JournalLineType.Debit ? x.Amount.Amount : -x.Amount.Amount));
        Assert.Contains(journal.Lines, x => x.Account == "CreatorAllocatedReserve" && x.Amount.Amount == 100 && x.Type == JournalLineType.Debit);
        Assert.Contains(await db.OutboxMessages.ToListAsync(), x => x.EventType == "VerifiedSaleRecorded");
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "VerifiedSale");
    }
    [Fact] public async Task Exact_budget_boundary_can_be_spent_without_negative_balance()
    {
        var s = await Phase4Scenario.Create(fixture, allocation: 100); await s.Redeem(await s.Issue());
        await using var db = s.Database.Open(); Assert.Equal(0, (await db.CreatorAllocations.SingleAsync()).RemainingAmount.Amount);
        Assert.Equal(ParticipationStatus.FundingRequired, (await db.CreatorPromotionParticipations.SingleAsync()).Status);
        Assert.Contains(await db.OutboxMessages.ToListAsync(), x => x.EventType == "CreatorBudgetExhausted");
        await Assert.ThrowsAsync<ApplicationFailure>(() => s.Issue("empty"));
    }
    [Fact] public async Task Insufficient_creator_budget_does_not_borrow_from_wallet_or_other_creator()
    {
        var s = await Phase4Scenario.Create(fixture, allocation: 99.99m);
        var another = await s.Seed.ApprovedCreator(); var otherAllocation = await s.Seed.Assign(another, 3000, "other");
        var qr = await s.Issue(); var error = await Assert.ThrowsAsync<ApplicationFailure>(() => s.Redeem(qr));
        Assert.Equal(FailureKind.InsufficientFunds, error.Kind);
        await using var db = s.Database.Open(); Assert.Empty(await db.VerifiedSales.ToListAsync()); Assert.Empty(await db.CreatorEarningEntries.ToListAsync());
        Assert.Equal(3000, (await db.CreatorAllocations.SingleAsync(x => x.Id == otherAllocation)).RemainingAmount.Amount);
        Assert.Equal(99.99m, (await db.CreatorAllocations.SingleAsync(x => x.Id == s.AllocationId)).RemainingAmount.Amount);
        Assert.Null((await db.OfferQrSessions.SingleAsync()).UsedAtUtc);
    }
    [Fact] public async Task Valid_retry_returns_completed_sale_even_after_qr_expires()
    {
        var s = await Phase4Scenario.Create(fixture); var qr = await s.Issue(); var result = await s.Redeem(qr);
        s.Clock.Now = qr.ExpiresAtUtc.AddSeconds(1); Assert.Equal(result, await s.Redeem(qr));
        await using var db = s.Database.Open(); Assert.Single(await db.VerifiedSales.ToListAsync());
        Assert.Single(await db.CustomerCashbackEntries.ToListAsync());
    }
    [Fact] public async Task Used_qr_with_new_key_cannot_create_second_sale()
    {
        var s = await Phase4Scenario.Create(fixture); var qr = await s.Issue(); await s.Redeem(qr);
        await Assert.ThrowsAsync<ApplicationFailure>(() => s.Redeem(qr, key: "different"));
        await using var db = s.Database.Open(); Assert.Single(await db.VerifiedSales.ToListAsync());
    }
    [Fact] public async Task Same_key_conflicting_purchase_fingerprint_is_rejected()
    {
        var s = await Phase4Scenario.Create(fixture); var qr = await s.Issue(); await s.Redeem(qr);
        var error = await Assert.ThrowsAsync<ApplicationFailure>(() => s.Redeem(qr, 2000)); Assert.Equal(FailureKind.IdempotencyConflict, error.Kind);
    }
    [Fact] public async Task Two_simultaneous_cashiers_produce_exactly_one_sale_and_one_financial_posting()
    {
        var s = await Phase4Scenario.Create(fixture); var qr = await s.Issue(); var second = s.Cashier with { UserId = Guid.NewGuid() };
        await using (var db = s.Database.Open()) { db.CommercePermissions.Add(new(second.UserId, ActorRole.Cashier, second.UserId, second.BusinessId, true, true)); await db.SaveChangesAsync(); }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(SaleResult? Sale, Exception? Error)> Attempt(Actor actor)
        {
            await gate.Task; try { return (await s.Redeem(qr, scanner: actor), null); } catch(Exception ex) { return (null, ex); }
        }
        var one = Attempt(s.Cashier); var two = Attempt(second); gate.SetResult(); var results = await Task.WhenAll(one,two);
        Assert.Single(results, x => x.Sale is not null);
        var failure = Assert.Single(results, x => x.Error is not null).Error;
        Assert.True(failure is ApplicationFailure, failure!.ToString());
        var error = Assert.IsType<ApplicationFailure>(failure);
        Assert.Contains(error.Kind, new[] { FailureKind.ConcurrencyConflict, FailureKind.Validation });
        await using var after = s.Database.Open(); Assert.Single(await after.VerifiedSales.ToListAsync());
        Assert.Single(await after.FinancialJournals.Where(x => x.SourceType == JournalSourceType.VerifiedSale).ToListAsync());
        Assert.Equal(100, (await after.CreatorAllocations.SingleAsync()).UsedAmount.Amount);
    }
    [Fact] public async Task Outbox_failure_rolls_back_sale_earnings_wallet_and_qr_consumption()
    {
        var s = await Phase4Scenario.Create(fixture); var qr = await s.Issue(); await s.FailOutbox();
        await Assert.ThrowsAnyAsync<Exception>(() => s.Redeem(qr)); await using var db = s.Database.Open();
        Assert.Empty(await db.VerifiedSales.ToListAsync()); Assert.Empty(await db.CreatorEarningEntries.ToListAsync());
        Assert.Empty(await db.CustomerCashbackEntries.ToListAsync()); Assert.Empty(await db.PlatformRevenueEntries.ToListAsync());
        Assert.Equal(2000, (await db.CreatorAllocations.SingleAsync()).RemainingAmount.Amount); Assert.Equal(6000,(await db.BusinessWallets.SingleAsync()).ReservedBalance.Amount);
        Assert.Null((await db.OfferQrSessions.SingleAsync()).UsedAtUtc);
        Assert.DoesNotContain(await db.IdempotencyRecords.ToListAsync(),x => x.OperationType == "RedeemOfferQr");
    }
    [Fact] public async Task Later_admin_configuration_cannot_reprice_sale_snapshot()
    {
        var s = await Phase4Scenario.Create(fixture); await using (var db = s.Database.Open())
        {
            var id = Guid.NewGuid(); var hybrid = Scenario.Price(PromotionType.ViewPlusCommission,id) with { CreatorCommissionPercent=1, CustomerCashbackPercent=1, PlatformPercent=1 };
            db.FinancialConfigurationVersions.Add(new(id,s.Seed.ConfigurationId,2,Phase4Scenario.Admin.UserId,Scenario.Now,
                Scenario.Price(PromotionType.ViewOnly,id),hybrid,new Money(5000),new Money(5000))); await db.SaveChangesAsync();
        }
        await s.Redeem(await s.Issue()); await using var after = s.Database.Open();
        var sale = await after.VerifiedSales.SingleAsync(); Assert.Equal(45,sale.CreatorCommissionAmount.Amount); Assert.Equal(100,sale.TotalPromotionCharge.Amount);
    }
    [Fact] public async Task Paused_participation_rejects_redemption_without_consuming_qr()
    {
        var s = await Phase4Scenario.Create(fixture); var qr = await s.Issue(); await using var db = s.Database.Open();
        await s.Views(db).SetPausedAsync(s.Creator,s.ParticipationId,true); await Assert.ThrowsAsync<ApplicationFailure>(() => s.Redeem(qr));
        Assert.Null((await db.OfferQrSessions.SingleAsync()).UsedAtUtc);
    }
}
