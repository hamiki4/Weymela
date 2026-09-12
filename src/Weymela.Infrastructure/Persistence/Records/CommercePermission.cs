using Weymela.Application;
namespace Weymela.Infrastructure.Persistence.Records;

// Local authorization facts populated by a future trusted identity/account-administration adapter.
// Phase 4 provides no public write endpoint and no default grant.
public sealed record CommercePermission(Guid UserId, ActorRole Role, Guid SubjectId, Guid? BusinessId, bool IsActive, bool CanCheckout);
