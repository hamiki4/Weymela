using System;
using Weymela.Domain;
using Xunit;

namespace Weymela.Domain.Tests;

public sealed class FinancialInvariantTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static Promotion Promotion(PromotionType type) => new(Guid.NewGuid(), "Campaign", "", type, new Money(1000), new(null, null, null, null), Now, Now.AddDays(1), new PricingSnapshot(type, 1000, new Money(300), new Money(200), new Money(100), 4.5m, 2m, 3.5m, Now, Guid.NewGuid()), Now, 30);

    [Fact] public void View_only_rejects_verified_sale() => Assert.Throws<InvalidOperationException>(() => new VerifiedSale(Promotion(PromotionType.ViewOnly), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new Money(100), new Money(5), new Money(2), new Money(3), "qr", "key", Now));
    [Fact] public void View_plus_commission_sale_uses_configured_split() { var sale = new VerifiedSale(Promotion(PromotionType.ViewPlusCommission), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new Money(100), new Money(4.5m), new Money(2), new Money(3.5m), "qr", "key", Now); Assert.Equal(10, sale.TotalPromotionCharge.Amount); }
    [Fact] public void Creator_earnings_accumulate_view_and_sale_sources() { var account = new CreatorEarningsAccount(Guid.NewGuid()); account.Earn(new Money(200), EarningSource.ViewReward, Guid.NewGuid(), Now, Guid.NewGuid()); account.Earn(new Money(100), EarningSource.SaleCommission, Guid.NewGuid(), Now, Guid.NewGuid()); Assert.Equal(300, account.AvailableEarnings.Amount); Assert.Contains(account.Entries, x => x.Source == EarningSource.ViewReward); Assert.Contains(account.Entries, x => x.Source == EarningSource.SaleCommission); }
    [Fact] public void Customer_cashback_accumulates_only_as_verified_sale_entries() { var account = new CustomerCashbackAccount(Guid.NewGuid()); account.Earn(new Money(2), Guid.NewGuid(), Now, Guid.NewGuid()); account.Earn(new Money(3), Guid.NewGuid(), Now, Guid.NewGuid()); Assert.Equal(5, account.AvailableCashback.Amount); Assert.All(account.Entries, x => Assert.Equal(CashbackSource.VerifiedSale, x.Source)); }
    [Fact] public void Platform_revenue_sources_are_classified() { var view = new PlatformRevenueEntry(Guid.NewGuid(), Guid.NewGuid(), PlatformRevenueSource.ViewRewardPlatformShare, new Money(100), RevenueStatus.Accrued, Now, Guid.NewGuid()); var sale = view with { Source = PlatformRevenueSource.SalePlatformShare, Status = RevenueStatus.Settled }; Assert.Equal(RevenueStatus.Accrued, view.Status); Assert.Equal(PlatformRevenueSource.SalePlatformShare, sale.Source); }
    [Fact] public void Balanced_journal_posts_and_retains_references() { var correlation = Guid.NewGuid(); var journal = new FinancialJournal("DEP-1", correlation, Guid.NewGuid(), JournalSourceType.Deposit, Now, "idem-1"); journal.AddLine(JournalLineType.Debit, new Money(100), "business-wallet"); journal.AddLine(JournalLineType.Credit, new Money(100), "cash"); journal.Post(); Assert.True(journal.IsPosted); Assert.Equal("DEP-1", journal.Reference); Assert.Equal(correlation, journal.CorrelationId); Assert.Equal("idem-1", journal.IdempotencyReference); }
    [Fact] public void Unbalanced_journal_is_rejected() { var journal = new FinancialJournal("X", Guid.NewGuid(), null, JournalSourceType.ViewReward, Now); journal.AddLine(JournalLineType.Debit, new Money(100), "wallet"); journal.AddLine(JournalLineType.Credit, new Money(99), "revenue"); Assert.Throws<InvalidOperationException>(() => journal.Post()); }
    [Fact] public void Posted_journal_is_immutable() { var journal = new FinancialJournal("X", Guid.NewGuid(), null, JournalSourceType.ViewReward, Now); journal.AddLine(JournalLineType.Debit, new Money(1), "a"); journal.AddLine(JournalLineType.Credit, new Money(1), "b"); journal.Post(); Assert.Throws<InvalidOperationException>(() => journal.AddLine(JournalLineType.Debit, new Money(1), "c")); Assert.Throws<InvalidOperationException>(() => journal.Post()); }
}
