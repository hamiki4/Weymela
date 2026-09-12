using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Infrastructure.Finance;

// One posting path for verified views and verified sales. It never draws from wallet Available
// or another Creator allocation. All outputs are journal-linked cached accounts/history.
internal sealed class AttributableFinance(WeymelaDbContext db)
{
    public async Task<FinancialJournal> PostAsync(Actor actor, Promotion promotion, CreatorAllocation allocation,
        SaleAmounts amounts, JournalSourceType source, string key, DateTime at, VerifiedSale? sale, CancellationToken ct)
    {
        if (source is not (JournalSourceType.ViewReward or JournalSourceType.VerifiedSale) ||
            allocation.PromotionId != promotion.Id || allocation.Status != CreatorAllocationStatus.Active ||
            amounts.Total.Amount <= 0 || allocation.RemainingAmount.Amount < amounts.Total.Amount ||
            promotion.ReservedBudget.Amount < amounts.Total.Amount)
            throw new ApplicationFailure(FailureKind.InsufficientFunds, "Creator Budget cannot cover the complete charge.");
        if (source == JournalSourceType.VerifiedSale && (sale is null || promotion.PromotionType != PromotionType.ViewPlusCommission))
            throw new ApplicationFailure(FailureKind.Validation, "Only eligible hybrid sales can generate commission and cashback.");
        if (source == JournalSourceType.ViewReward && amounts.Customer.Amount != 0)
            throw new ApplicationFailure(FailureKind.Validation, "View rewards cannot generate cashback.");

        var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == promotion.BusinessId, ct);
        var correlation = Guid.NewGuid();
        promotion.Consume(allocation.Id, amounts.Total, at, correlation);
        wallet.ConsumeReservedFunds(amounts.Total, at, correlation);
        var j = new FinancialJournal(Guid.NewGuid().ToString("N"), correlation, actor.UserId, source, at, key);
        j.AddLine(JournalLineType.Debit, amounts.Total, "CreatorAllocatedReserve");
        if (amounts.Creator.Amount > 0) j.AddLine(JournalLineType.Credit, amounts.Creator, "CreatorPayable");
        if (amounts.Customer.Amount > 0) j.AddLine(JournalLineType.Credit, amounts.Customer, "CustomerCashbackPayable");
        if (amounts.Platform.Amount > 0) j.AddLine(JournalLineType.Credit, amounts.Platform, "PlatformRevenue");
        j.Post(); db.FinancialJournals.Add(j);
        db.Entry(j).Property("BusinessId").CurrentValue = promotion.BusinessId;
        db.Entry(j).Property("PromotionId").CurrentValue = promotion.Id;
        db.Entry(j).Property("CreatorId").CurrentValue = allocation.CreatorId;
        db.Entry(j).Property("CustomerId").CurrentValue = sale?.CustomerId;
        db.WalletEntries.Add(new(Guid.NewGuid(), promotion.BusinessId, promotion.Id, amounts.Total, "Consumed", j.Id, at));
        db.PromotionBudgetEntries.Add(new(Guid.NewGuid(), promotion.Id, allocation.Id, amounts.Total, source.ToString(), j.Id, at));
        var operation = new FinancialOperation(db);
        if (amounts.Creator.Amount > 0)
        {
            var account = await db.CreatorEarningsAccounts.SingleOrDefaultAsync(x => x.CreatorId == allocation.CreatorId, ct);
            if (account is null) { account = new(allocation.CreatorId); db.CreatorEarningsAccounts.Add(account); }
            var before = account.AvailableEarnings;
            account.Earn(amounts.Creator, source == JournalSourceType.ViewReward ? EarningSource.ViewReward : EarningSource.SaleCommission, promotion.Id, at, correlation);
            var entry = account.Entries.Last(); db.CreatorEarningEntries.Add(entry); db.Entry(entry).Property("JournalId").CurrentValue = j.Id;
            await operation.EmitEligibility(PayoutBeneficiary.Creator, allocation.CreatorId, before, account.AvailableEarnings, at, ct);
            operation.Event(source == JournalSourceType.ViewReward ? nameof(ViewRewardEarned) : nameof(CreatorCommissionEarned),
                new { allocation.CreatorId, AllocationId = allocation.Id, Amount = amounts.Creator, JournalId = j.Id, CorrelationId = correlation }, at);
        }
        if (sale is not null)
        {
            if (allocation.RemainingAmount.Amount == 0)
            {
                var participation = await db.CreatorPromotionParticipations.SingleAsync(x => x.CreatorAllocationId == allocation.Id, ct);
                participation.SetFundingRequired(true);
                operation.Event("CreatorBudgetExhausted", new { AllocationId = allocation.Id, ParticipationId = participation.Id }, at);
            }
            db.VerifiedSales.Add(sale); db.Entry(sale).Property("JournalId").CurrentValue = j.Id;
            if (amounts.Customer.Amount > 0)
            {
                var account = await db.CustomerCashbackAccounts.SingleOrDefaultAsync(x => x.CustomerId == sale.CustomerId, ct);
                if (account is null) { account = new(sale.CustomerId); db.CustomerCashbackAccounts.Add(account); }
                var before = account.AvailableCashback;
                account.Earn(amounts.Customer, sale.Id, at, correlation);
                var entry = account.Entries.Last(); db.CustomerCashbackEntries.Add(entry); db.Entry(entry).Property("JournalId").CurrentValue = j.Id;
                await operation.EmitEligibility(PayoutBeneficiary.Customer, sale.CustomerId, before, account.AvailableCashback, at, ct);
                operation.Event(nameof(CustomerCashbackEarned), new { sale.CustomerId, Amount = amounts.Customer, JournalId = j.Id, CorrelationId = correlation }, at);
            }
            operation.Event(nameof(VerifiedSaleRecorded), new { SaleId = sale.Id, JournalId = j.Id, CorrelationId = correlation }, at);
        }
        if (amounts.Platform.Amount > 0)
        {
            var entry = new PlatformRevenueEntry(Guid.NewGuid(), promotion.Id,
                source == JournalSourceType.ViewReward ? PlatformRevenueSource.ViewRewardPlatformShare : PlatformRevenueSource.SalePlatformShare,
                amounts.Platform, RevenueStatus.Accrued, at, correlation);
            db.PlatformRevenueEntries.Add(entry); db.Entry(entry).Property("JournalId").CurrentValue = j.Id;
            operation.Event(nameof(PlatformRevenueEarned), new { Amount = amounts.Platform, Source = entry.Source.ToString(), JournalId = j.Id, CorrelationId = correlation }, at);
        }
        operation.Audit(actor with { BusinessId = promotion.BusinessId }, source.ToString(), correlation, at, promotion.Id, allocation.CreatorId);
        return j;
    }
}
