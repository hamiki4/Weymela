using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Persistence.Repositories;

public sealed class FinancialConfigurationResolver(WeymelaDbContext db) : IFinancialConfigurationResolver
{
    public async Task<FinancialConfigurationVersion> EffectiveAsync(DateTime at, CancellationToken ct = default) =>
        await db.FinancialConfigurationVersions.AsNoTracking().Where(x => x.EffectiveFromUtc <= at)
            .OrderByDescending(x => x.EffectiveFromUtc).ThenByDescending(x => x.Version).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct) ?? throw new ApplicationFailure(FailureKind.Validation, "No effective Platform pricing configuration exists.");
    public async Task<PricingSnapshot> ResolveAsync(PromotionType type, DateTime at, CancellationToken ct)
    {
        var v = await EffectiveAsync(at, ct);
        return type == PromotionType.ViewOnly ? v.ViewOnly : v.ViewPlusCommission;
    }

    public async Task<PromotionConfigurationSnapshot> ResolvePromotionAsync(PromotionType type, DateTime at, CancellationToken ct)
    {
        var v = await EffectiveAsync(at, ct);
        return new(type == PromotionType.ViewOnly ? v.ViewOnly : v.ViewPlusCommission,
            v.PromotionLiveDurationDays);
    }
}

public sealed class LegalAcceptanceGate(WeymelaDbContext db, TimeProvider clock) : ILegalAcceptanceGate
{
    public async Task EnsureCurrentAcceptedAsync(Guid userId, LegalRole role, IReadOnlyCollection<LegalDocumentType> required, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var type in required)
        {
            var current = await db.LegalDocumentVersions.Where(x => x.Type == type && x.EffectiveFromUtc <= now)
                .OrderByDescending(x => x.EffectiveFromUtc).ThenByDescending(x => x.Version).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (current is null || !await db.LegalAcceptances.AnyAsync(x => x.UserId == userId && x.Role == role && x.DocumentVersionId == current.Id, ct))
                throw new ApplicationFailure(FailureKind.Forbidden, "Accept the required current legal document version before continuing.");
        }
    }
}
