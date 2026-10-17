using System.Data;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Repositories;

namespace Weymela.Infrastructure.Persistence.Transactions;

public sealed partial class FinancialCommands
{
    public Task<Guid> AddPromotionFundsAsync(Actor actor, Guid id, Money amount, long campaignVersion,
        long walletVersion, string key, DateTime now, CancellationToken ct) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            DemandBusiness(actor);
            var fp = RequestFingerprint.Create(actor.BusinessId!.Value.ToString(), id.ToString(), RequestFingerprint.Amount(amount));
            var replay = await Replay(actor, "AddPromotionFunds", key, fp, token);
            if (replay is not null) return replay.Value;
            var promotion = await new PromotionRepository(db).GetAsync(id,token)
                ?? throw new ApplicationFailure(FailureKind.NotFound,"Promotion not found.");
            if (promotion.BusinessId != actor.BusinessId) throw new ApplicationFailure(FailureKind.Forbidden,"Promotion belongs to another Business.");
            var wallet = await new BusinessWalletRepository(db).GetAsync(promotion.BusinessId,token)
                ?? throw new ApplicationFailure(FailureKind.NotFound,"Wallet not found.");
            ExpectedVersion.Check(db,promotion,campaignVersion);
            ExpectedVersion.Check(db,wallet,walletVersion);
            if (amount.Amount <= 0 || amount.Amount > wallet.AvailableBalance.Amount)
                throw new ApplicationFailure(FailureKind.InsufficientFunds,"Check available funds.");
            var correlation = Guid.NewGuid();
            var liveEnd = await db.CreatorPublicationVerifications.AsNoTracking().Where(x => x.CreatorAllocationId != null && db.CreatorAllocations.Any(a => a.Id == x.CreatorAllocationId && a.PromotionId == id) && x.WentLiveAtUtc != null && x.EndedAtUtc == null).MaxAsync(x => x.ExpiresAtUtc,token);
            try { promotion.AddRewardFunding(wallet,amount,now,correlation,liveEnd); }
            catch (InvalidOperationException ex) { throw new ApplicationFailure(FailureKind.Validation,ex.Message); }
            var journal = Journal(actor,"PromotionReservation",amount,"BusinessAvailable","CampaignUnallocatedReserve",now,key,id,correlation:correlation);
            db.WalletEntries.Add(new(Guid.NewGuid(),promotion.BusinessId,id,amount,"Reserve",journal.Id,now));
            db.PromotionBudgetEntries.Add(new(Guid.NewGuid(),id,null,amount,"Funded",journal.Id,now));
            await Idempotency.SaveAsync(new(key,"AddPromotionFunds",actor.UserId,fp,id,now),token);
            Audit(actor,"PromotionFundsAdded",now,correlation,id);
            return id;
        },ct);
}
