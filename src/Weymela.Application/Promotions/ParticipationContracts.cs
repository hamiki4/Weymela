using Weymela.Domain;
namespace Weymela.Application;
public sealed record WithdrawApplicationCommand(Actor Actor, Guid ApplicationId, DateTime Now);
public sealed record ApproveCreatorApplicationCommand(Actor Actor, Guid ApplicationId, DateTime Now);
public sealed record RejectCreatorApplicationCommand(Actor Actor, Guid ApplicationId, DateTime Now);
public sealed record IncreaseCreatorAllocationCommand(Actor Actor, Guid AllocationId, Money Amount, long ExpectedAllocationVersion, DateTime Now);
public sealed record CompleteCreatorParticipationCommand(Actor Actor, Guid AllocationId, long ExpectedAllocationVersion, DateTime Now);
public sealed record CreateFinancialConfigurationVersionCommand(Actor Actor, PricingSnapshot Snapshot, DateTime Now);
public sealed record GetEffectiveFinancialConfigurationQuery(Actor Actor, PromotionType Type, DateTime AtUtc);
public sealed record AuthorizedFinancialAdjustmentCommand(Actor Actor, Guid BusinessId, Money Amount, string Reason, string IdempotencyKey, DateTime Now);
public interface IParticipationService { Task<CreatorApplication> ApplyAsync(ApplyToPromotionCommand command, CancellationToken ct); Task<CreatorAllocation> AssignAsync(AssignCreatorAllocationCommand command, CancellationToken ct); Task IncreaseAsync(IncreaseCreatorAllocationCommand command, CancellationToken ct); Task CompleteAsync(CompleteCreatorParticipationCommand command, CancellationToken ct); }
public interface IAdminPromotionQueries { Task<IReadOnlyList<AdminPromotionProjection>> ListAsync(Actor actor, CancellationToken ct); Task<object?> DetailsAsync(Actor actor, Guid promotionId, CancellationToken ct); }
public interface IAuthorizedAdjustmentService { Task ApplyAsync(AuthorizedFinancialAdjustmentCommand command, CancellationToken ct); }
