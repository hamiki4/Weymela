using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Deposits;

public sealed class ReceivingDestinationService(WeymelaDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<ReceivingDestinationView>> ActiveAsync(Actor actor, CancellationToken ct = default)
    {
        if (actor.Role != ActorRole.Business || actor.BusinessId is null)
            throw new ApplicationFailure(FailureKind.Forbidden, "Business access is required.");
        return await db.PlatformReceivingDestinations.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder)
            .Select(x => new ReceivingDestinationView(x.Id, x.Method, x.Name, x.AccountReference, x.IsActive, x.SortOrder, x.Version)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReceivingDestinationView>> AdminAsync(Actor actor, CancellationToken ct = default)
    {
        PayoutService.DemandPlatformAdmin(actor);
        return await db.PlatformReceivingDestinations.AsNoTracking().OrderBy(x => x.SortOrder)
            .Select(x => new ReceivingDestinationView(x.Id, x.Method, x.Name, x.AccountReference, x.IsActive, x.SortOrder, x.Version)).ToListAsync(ct);
    }

    public async Task<ReceivingDestinationView> UpdateAsync(Actor actor, Guid id, ReceivingDestinationInput input, CancellationToken ct = default)
    {
        PayoutService.DemandPlatformAdmin(actor);
        var row = await db.PlatformReceivingDestinations.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ApplicationFailure(FailureKind.NotFound, "Receiving destination not found.");
        if (row.Version != input.ExpectedVersion) throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "Receiving destination changed. Refresh and try again.");
        var method = Clean(input.Method, 30); var name = Clean(input.Name, 100); var account = Clean(input.AccountReference, 100);
        if (method is not ("Telebirr" or "Bank") || input.SortOrder < 0)
            throw new ApplicationFailure(FailureKind.Validation, "Receiving destination is invalid.");
        row.Method = method; row.Name = name; row.AccountReference = account; row.IsActive = input.IsActive;
        row.SortOrder = input.SortOrder; row.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime; row.UpdatedBy = actor.UserId;
        db.AuditEvents.Add(new(Guid.NewGuid(), "ReceivingDestinationUpdated", actor.UserId, null, null, null, row.Id,
            row.UpdatedAtUtc, $"Name={name};Method={method};Active={input.IsActive}"));
        await db.SaveChangesAsync(ct);
        return new(row.Id, row.Method, row.Name, row.AccountReference, row.IsActive, row.SortOrder, row.Version);
    }

    private static string Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ApplicationFailure(FailureKind.Validation, "Receiving destination values are required.");
        var clean = value.Trim(); if (clean.Length > max) throw new ApplicationFailure(FailureKind.Validation, "Receiving destination value is too long."); return clean;
    }
}
