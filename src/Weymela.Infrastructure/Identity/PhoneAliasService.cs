using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

/// <summary>Registers one private phone lookup alias for a verified V3 identity.</summary>
public sealed class PhoneAliasService(WeymelaDbContext db, RuntimeOptions options, TimeProvider clock)
{
    public async Task RegisterAsync(DeviceSessionIdentity identity, string phone, CancellationToken ct)
    {
        var canonical = PhoneNumberNormalizer.Normalize(phone);
        var hash = EmailAuthService.HashIdentifier(canonical);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            if (!await db.IdentityBindings.AsNoTracking().AnyAsync(x => x.Id == identity.IdentityBindingId
                && x.UserId == identity.UserId && x.Version == identity.IdentityVersion && x.IsActive
                && x.Provider == "Firebase" && x.ProjectId == options.FirebaseProjectId, ct))
                throw new ApplicationFailure(FailureKind.Forbidden, "Sign in again before updating your phone.");
            if (await db.AuthIdentifiers.AsNoTracking().CountAsync(x => x.UserId == identity.UserId
                && x.Kind == "Email" && x.IsVerified && x.DeliveryAddress != null, ct) != 1)
                throw new ApplicationFailure(FailureKind.Forbidden, "Verify your email before updating your phone.");
            var claimed = await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Phone"
                && x.IdentifierHash == hash, ct);
            if (claimed is not null && claimed.UserId != identity.UserId) throw Collision();
            var existing = await db.AuthIdentifiers.AsTracking().Where(x => x.UserId == identity.UserId
                && x.Kind == "Phone").ToListAsync(ct);
            if (claimed is not null && existing.Count == 1 && existing[0].Id == claimed.Id)
            {
                await tx.CommitAsync(ct);
                return;
            }
            db.AuthIdentifiers.RemoveRange(existing.Where(x => claimed is null || x.Id != claimed.Id));
            if (claimed is null)
                db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = identity.UserId, Kind = "Phone",
                    IdentifierHash = hash, IsVerified = false, CreatedAtUtc = clock.GetUtcNow().UtcDateTime });
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "PhoneAliasUpdated", identity.UserId,
                null, null, null, Guid.NewGuid(), clock.GetUtcNow().UtcDateTime, "authenticated-phone-lookup-alias"));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception e) when (DatabaseCollision(e))
        {
            throw Collision();
        }
    }

    private static ApplicationFailure Collision() => new(FailureKind.Validation,
        "This phone number cannot be added to your account.");

    private static bool DatabaseCollision(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is PostgresException postgres && postgres.SqlState is
                PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation)
                return true;
        return false;
    }
}
