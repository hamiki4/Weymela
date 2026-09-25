using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

internal static class PlatformAdminSafety
{
    // Call inside the same serializable transaction as the role/lifecycle change.
    // PostgreSQL detects concurrent write-skew when two administrators each try
    // to remove the other's last remaining active authority.
    public static async Task RequireReplacementAsync(WeymelaDbContext db, Guid removingUserId, CancellationToken ct)
    {
        var replacements = await db.CommercePermissions.AsNoTracking()
            .Where(permission => permission.Role == ActorRole.PlatformAdmin && permission.IsActive
                && permission.UserId != removingUserId
                && !db.AccountLifecycles.Any(lifecycle => lifecycle.UserId == permission.UserId
                    && lifecycle.Status != AccountLifecycleStatus.Active))
            .Select(permission => permission.UserId).Distinct().CountAsync(ct);
        if (replacements == 0)
            throw new ApplicationFailure(FailureKind.Validation,
                "Keep another active Platform Admin before changing this account.", code: "LastActivePlatformAdmin");
    }
}
