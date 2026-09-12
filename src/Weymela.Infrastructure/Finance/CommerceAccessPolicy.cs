using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Infrastructure.Finance;

public sealed class CommerceAccessPolicy(WeymelaDbContext db) : ICommerceAccessPolicy
{
    public async Task EnsureCustomerAsync(Actor actor, CancellationToken ct)
    {
        if (actor.Role != ActorRole.Customer || actor.CustomerId is null ||
            !await db.CommercePermissions.AnyAsync(x => x.UserId == actor.UserId && x.Role == ActorRole.Customer && x.SubjectId == actor.CustomerId && x.IsActive, ct))
            throw Forbidden();
    }
    public async Task EnsureCustomerIdAsync(Guid customerId, CancellationToken ct)
    {
        if (!await db.CommercePermissions.AnyAsync(x => x.Role == ActorRole.Customer && x.SubjectId == customerId && x.IsActive, ct)) throw Forbidden();
    }
    public async Task EnsureBusinessAsync(Guid businessId, CancellationToken ct)
    {
        if (!await db.CommercePermissions.AnyAsync(x => x.Role == ActorRole.Business && x.SubjectId == businessId && x.IsActive, ct)) throw Forbidden();
    }
    public async Task EnsureCreatorAsync(Actor actor, Guid creatorId, CancellationToken ct)
    {
        if (actor.Role != ActorRole.Creator || actor.CreatorId != creatorId ||
            !await db.CommercePermissions.AnyAsync(x => x.UserId == actor.UserId && x.Role == ActorRole.Creator && x.SubjectId == creatorId && x.IsActive, ct))
            throw Forbidden();
    }
    public async Task EnsureScannerAsync(Actor actor, Guid businessId, CancellationToken ct)
    {
        if (actor.BusinessId != businessId || actor.Role is not (ActorRole.Cashier or ActorRole.Business) ||
            !await db.CommercePermissions.AnyAsync(x => x.UserId == actor.UserId && x.Role == actor.Role &&
                x.BusinessId == businessId && x.IsActive && x.CanCheckout, ct)) throw Forbidden();
        await EnsureBusinessAsync(businessId, ct);
    }
    private static ApplicationFailure Forbidden() => new(FailureKind.Forbidden, "The account is not authorized for this operation.");
}
