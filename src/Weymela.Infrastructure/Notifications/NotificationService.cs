using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Infrastructure.Notifications;

public sealed class NotificationService(WeymelaDbContext db, TimeProvider clock)
{
    private async Task Demand(Actor actor, CancellationToken ct)
    {
        if (!await db.CommercePermissions.AnyAsync(x => x.UserId == actor.UserId && x.Role == actor.Role && x.IsActive, ct))
            throw new ApplicationFailure(FailureKind.Forbidden, "An active workspace is required.");
    }
    public async Task<NotificationPage> GetAsync(Actor actor, CancellationToken ct)
    {
        await Demand(actor, ct);
        var query = db.InAppNotifications.AsNoTracking().Where(x => x.UserId == actor.UserId && x.Role == actor.Role);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(100)
            .Select(x => new NotificationDto(x.Id, x.Title, x.Message, x.Route, x.CreatedAtUtc, x.ReadAtUtc)).ToListAsync(ct);
        return new(items, await query.CountAsync(x => x.ReadAtUtc == null, ct));
    }
    public async Task ReadAsync(Actor actor, Guid id, CancellationToken ct)
    {
        await Demand(actor, ct); Operations.InputRules.Id(id);
        var query = db.InAppNotifications.Where(x => x.Id == id && x.UserId == actor.UserId && x.Role == actor.Role);
        if (!await query.AnyAsync(ct)) throw new ApplicationFailure(FailureKind.NotFound, "Notification not found.");
        var now = clock.GetUtcNow().UtcDateTime;
        await query.Where(x => x.ReadAtUtc == null && x.CreatedAtUtc <= now).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAtUtc, now).SetProperty(x => x.Version, x => x.Version + 1), ct);
    }
    public async Task ReadAllAsync(Actor actor, CancellationToken ct)
    {
        await Demand(actor, ct); var through = clock.GetUtcNow().UtcDateTime;
        await db.InAppNotifications.Where(x => x.UserId == actor.UserId && x.Role == actor.Role && x.ReadAtUtc == null && x.CreatedAtUtc <= through)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAtUtc, through).SetProperty(x => x.Version, x => x.Version + 1), ct);
    }
}

public sealed class DisabledPushProvider : INotificationPushProvider
{
    public bool Enabled => false;
    public Task<PushDeliveryResult> SendAsync(PushNotification notification, CancellationToken ct) => Task.FromResult(new PushDeliveryResult(false, false, "PushDisabled"));
}
