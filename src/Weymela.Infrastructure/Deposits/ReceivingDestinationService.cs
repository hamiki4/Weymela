using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Operations;

namespace Weymela.Infrastructure.Deposits;

public sealed class ReceivingDestinationService(WeymelaDbContext db, TimeProvider clock, RuntimeOptions? options = null)
{
    private bool Production => options?.EnvironmentName == "Production";
    internal static bool ApprovedProductionDestination(PlatformReceivingDestination row) =>
        row.IsActive && row.UpdatedBy is not null && row.UpdatedBy != Guid.Empty && row.Version > 0
        && !Placeholder(row.AccountReference);
    private static bool Placeholder(string account) => account is "0911111111" or "1000000000" or "123456789";

    private IQueryable<PlatformReceivingDestination> AvailableRows()
    {
        var rows = db.PlatformReceivingDestinations.AsNoTracking().Where(x => x.IsActive);
        return Production ? rows.Where(x => x.UpdatedBy != null && x.UpdatedBy != Guid.Empty && x.Version > 0
            && x.AccountReference != "0911111111" && x.AccountReference != "1000000000" && x.AccountReference != "123456789") : rows;
    }

    public async Task<DepositAvailability> AvailabilityAsync(Actor actor, CancellationToken ct = default)
    {
        if (actor.Role != ActorRole.PlatformAdmin && (actor.Role != ActorRole.Business || actor.BusinessId is null))
            throw new ApplicationFailure(FailureKind.Forbidden, "Business or Platform Admin access is required.");
        var mode = options?.DevelopmentIdentity == true ? "Development" : options?.DepositMode ?? "Disabled";
        var count = await AvailableRows().CountAsync(ct);
        if (count == 0)
            return new(mode, false, "ReceivingDestinationVerificationRequired",
                "Deposits are paused. Platform Admin must verify a Weymela-controlled receiving account, replace any example account details, and save the destination as active before deposits can be enabled. No funds have been credited.", count);
        if (mode != "ManualApproval")
            return new(mode, false, "DepositActivationRequired",
                "Deposits are paused. Platform Admin must confirm ownership of the receiving accounts, then an operator must enable receipt submission and manual settlement review. Active destinations alone do not enable deposits. No funds have been credited.", count);
        var now = clock.GetUtcNow().UtcDateTime;
        if (options is null || !options.FinancialOperationActive(ProductionFinancialOperations.DepositRequests, now)
            || !options.FinancialOperationActive(ProductionFinancialOperations.DepositReview, now))
            return new(mode, false, "DepositReviewPaused",
                "Deposits are paused. Receipt submission and Admin settlement review must both be enabled before you send money or upload a receipt. No funds have been credited.", count);
        return new(mode, true, "Ready",
            "Receipts can be submitted for review. Wallets are credited only after an Admin verifies settlement and approves the deposit.", count);
    }

    public async Task<IReadOnlyList<ReceivingDestinationView>> ActiveAsync(Actor actor, CancellationToken ct = default)
    {
        if (actor.Role != ActorRole.Business || actor.BusinessId is null)
            throw new ApplicationFailure(FailureKind.Forbidden, "Business access is required.");
        var rows = AvailableRows();
        return await rows.OrderBy(x => x.SortOrder)
            .Select(x => new ReceivingDestinationView(x.Id, x.Method, x.Name, x.AccountReference, x.IsActive, x.SortOrder, x.Version)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReceivingDestinationView>> AdminAsync(Actor actor, CancellationToken ct = default)
    {
        PayoutService.DemandPlatformAdmin(actor);
        var rows = await db.PlatformReceivingDestinations.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync(ct);
        return rows.Select(x => new ReceivingDestinationView(x.Id, x.Method, x.Name, x.AccountReference,
            Production ? ApprovedProductionDestination(x) : x.IsActive, x.SortOrder, x.Version)).ToList();
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
        if (Production && input.IsActive && Placeholder(account))
            throw new ApplicationFailure(FailureKind.Validation, "Replace the example receiving account with verified Business deposit details before activating it.");
        row.Method = method; row.Name = name; row.AccountReference = account; row.IsActive = input.IsActive;
        row.SortOrder = input.SortOrder; row.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime; row.UpdatedBy = actor.UserId;
        db.AuditEvents.Add(new(Guid.NewGuid(), "ReceivingDestinationUpdated", actor.UserId, null, null, null, row.Id,
            row.UpdatedAtUtc, $"Name={name};Method={method};Active={input.IsActive};AdminVerified={Production && input.IsActive}"));
        await db.SaveChangesAsync(ct);
        return new(row.Id, row.Method, row.Name, row.AccountReference, row.IsActive, row.SortOrder, row.Version);
    }

    private static string Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ApplicationFailure(FailureKind.Validation, "Receiving destination values are required.");
        var clean = value.Trim(); if (clean.Length > max) throw new ApplicationFailure(FailureKind.Validation, "Receiving destination value is too long."); return clean;
    }
}
