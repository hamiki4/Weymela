using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Finance;

public sealed record InactiveFinancialBaselineRequest(Guid PlatformAdminUserId,
    decimal CustomerCashbackPercent, decimal CreatorCommissionPercent, decimal PlatformPercent,
    DateTime EffectiveFromUtc, string OperatorReference, Guid CorrelationId, string IdempotencyKey,
    string? ProductionAuthorizationReference = null);

// A versioned approval snapshot in the existing append-only audit store. It is deliberately
// not a PricingSnapshot: absent prices and thresholds cannot be resolved by a money workflow.
public sealed record InactiveFinancialBaseline(Guid Id, int Version, string Target,
    string FirebaseProjectId, InactiveFinancialBaselineRequest Approval);

public static class InactiveFinancialBaselines
{
    public const string EventType = "InactiveFinancialBaselineApproved";

    public static async Task<InactiveFinancialBaseline?> EffectiveAsync(WeymelaDbContext db,
        string projectId, DateTime now, CancellationToken ct)
    {
        var events = await db.AuditEvents.AsNoTracking().Where(x => x.EventType == EventType
            && x.OccurredAtUtc <= now).OrderByDescending(x => x.OccurredAtUtc).Take(2).ToListAsync(ct);
        if (events.Count != 1) return null;
        try
        {
            var baseline = JsonSerializer.Deserialize<InactiveFinancialBaseline>(events[0].Detail);
            if (baseline is null || baseline.Id != events[0].Id || baseline.Version != 1
                || baseline.FirebaseProjectId != projectId || baseline.Approval.PlatformAdminUserId != events[0].ActorId
                || baseline.Approval.CorrelationId != events[0].CorrelationId
                || baseline.Approval.EffectiveFromUtc.Kind != DateTimeKind.Utc
                || baseline.Approval.EffectiveFromUtc > now || !ValidRates(baseline.Approval)
                || (baseline.Target == "production" && !PlatformAdminBootstrapTarget
                    .IsValidProductionAuthorizationReference(baseline.Approval.ProductionAuthorizationReference))) return null;
            return baseline;
        }
        catch (JsonException) { return null; }
    }

    internal static bool ValidRates(InactiveFinancialBaselineRequest request) =>
        new[] { request.CustomerCashbackPercent, request.CreatorCommissionPercent, request.PlatformPercent }
            .All(x => x is >= 0 and <= 100 && decimal.Round(x, 4) == x)
        && request.CustomerCashbackPercent + request.CreatorCommissionPercent + request.PlatformPercent <= 100;
}

public sealed partial class FinancialConfigurationBootstrapper
{
    public async Task<InactiveFinancialBaseline> ProvisionInactiveAsync(
        InactiveFinancialBaselineRequest request, CancellationToken ct = default)
    {
        var now = Now;
        if (request.PlatformAdminUserId == Guid.Empty || request.CorrelationId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.OperatorReference) || request.OperatorReference.Length > 160
            || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 160
            || request.EffectiveFromUtc.Kind != DateTimeKind.Utc || request.EffectiveFromUtc > now
            || !InactiveFinancialBaselines.ValidRates(request))
            throw new ArgumentException("An inactive baseline requires approved rates, effective UTC time and bounded audit references.");
        if (_target.RequiresProductionAuthorization
            ? !PlatformAdminBootstrapTarget.IsValidProductionAuthorizationReference(request.ProductionAuthorizationReference)
            : request.ProductionAuthorizationReference is not null)
            throw new InvalidOperationException("The financial baseline authorization does not match the selected target.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await RequirePlatformAdminAsync(request.PlatformAdminUserId, now, ct);
        const string operation = "InactiveFinancialBaselineBootstrap";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        var prior = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.ActorId == request.PlatformAdminUserId
            && x.OperationType == operation && x.Key == request.IdempotencyKey, ct);
        if (prior is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(prior.RequestFingerprint),
                Encoding.UTF8.GetBytes(fingerprint)))
                throw new InvalidOperationException("The baseline idempotency key was used for a different approval.");
            var id = Guid.Parse(prior.ResultReference);
            var audit = await db.AuditEvents.SingleAsync(x => x.Id == id && x.EventType == InactiveFinancialBaselines.EventType, ct);
            var replay = JsonSerializer.Deserialize<InactiveFinancialBaseline>(audit.Detail)
                ?? throw new InvalidOperationException("The baseline audit record is invalid.");
            await transaction.CommitAsync(ct);
            return replay;
        }
        if (await db.FinancialConfigurations.AnyAsync(ct) || await db.FinancialConfigurationVersions.AnyAsync(ct)
            || await db.AuditEvents.AnyAsync(x => x.EventType == InactiveFinancialBaselines.EventType, ct)
            || await db.FinancialJournals.AnyAsync(ct) || await db.Promotions.AnyAsync(ct)
            || await db.UgcOpportunities.AnyAsync(ct) || await db.UgcCustomerOffers.AnyAsync(ct)
            || await db.DepositRequests.AnyAsync(ct) || await db.PayoutRecords.AnyAsync(ct))
            throw new InvalidOperationException("Inactive baseline bootstrap is closed once configuration or financial activity exists.");

        var baseline = new InactiveFinancialBaseline(Guid.NewGuid(), 1, _target.Name, _target.FirebaseProjectId, request);
        db.AuditEvents.Add(new AuditEvent(baseline.Id, InactiveFinancialBaselines.EventType,
            request.PlatformAdminUserId, null, null, null, request.CorrelationId, now, JsonSerializer.Serialize(baseline)));
        db.IdempotencyRecords.Add(new StoredIdempotencyRecord(request.PlatformAdminUserId, operation,
            request.IdempotencyKey, fingerprint, baseline.Id.ToString("D"), now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return baseline;
    }
}
