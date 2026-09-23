using Weymela.Application;

namespace Weymela.Infrastructure.Persistence.Records;

// V3-authoritative authorization fact. Revocation is a state transition so the
// grant history and composite identity remain auditable; runtime never deletes it.
public sealed class CommercePermission
{
    private CommercePermission() { }
    public Guid UserId { get; init; }
    public ActorRole Role { get; init; }
    public Guid SubjectId { get; init; }
    public Guid? BusinessId { get; init; }
    public bool IsActive { get; set; }
    public bool CanCheckout { get; set; }
    public CommercePermission(Guid userId, ActorRole role, Guid subjectId, Guid? businessId, bool isActive, bool canCheckout)
    { UserId = userId; Role = role; SubjectId = subjectId; BusinessId = businessId; IsActive = isActive; CanCheckout = canCheckout; }
}
