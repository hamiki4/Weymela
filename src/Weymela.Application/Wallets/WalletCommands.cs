using Weymela.Domain;
namespace Weymela.Application;
public sealed class WalletApplicationService(IWalletRepository wallets, IIdempotencyStore idempotency, IEventPublisher events)
{
 public async Task CreditDepositAsync(CreditBusinessDepositCommand c, long expectedWalletVersion, CancellationToken ct)
 {
     if(c.Actor.Role != ActorRole.Business || c.Actor.BusinessId is null) throw new ApplicationFailure(FailureKind.Forbidden,"Only a Business may credit its wallet.");
     if(c.Amount.Amount <= 0) throw new ApplicationFailure(FailureKind.Validation,"Deposit must be positive.");
     if(string.IsNullOrWhiteSpace(c.IdempotencyKey)) throw new ApplicationFailure(FailureKind.Validation,"Idempotency key is required.");
     var fingerprint = RequestFingerprint.Create(c.Actor.BusinessId.Value.ToString(), RequestFingerprint.Amount(c.Amount));
     var prior = await idempotency.FindAsync(c.IdempotencyKey, "CreditBusinessDeposit", c.Actor.UserId, ct);
     if(prior is not null)
     {
         if(prior.ActorId != c.Actor.UserId || prior.OperationType != "CreditBusinessDeposit" || prior.RequestFingerprint != fingerprint)
             throw new ApplicationFailure(FailureKind.IdempotencyConflict,"Idempotency key conflicts with another request.");
         return;
     }
     var wallet = await wallets.GetAsync(c.Actor.BusinessId.Value,ct) ?? throw new ApplicationFailure(FailureKind.NotFound,"Business wallet was not found.");
     wallet.CreditDeposit(c.Amount,c.Now,Guid.NewGuid());
     await wallets.SaveAsync(wallet,expectedWalletVersion,ct);
     await idempotency.SaveAsync(new(c.IdempotencyKey,"CreditBusinessDeposit",c.Actor.UserId,fingerprint,wallet.BusinessId,c.Now),ct);
     await events.PublishAsync(wallet.DomainEvents,ct);
 }
}
