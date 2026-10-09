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
    /// <summary>
    /// Claims the registration phone after email verification and before device/PIN
    /// enrollment. Unlike an authenticated settings change, this never replaces a
    /// phone already owned by the identity.
    /// </summary>
    public async Task EnsureRegistrationAsync(DeviceSessionIdentity identity, string phone, CancellationToken ct)
    {
        var canonical = PhoneNumberNormalizer.Normalize(phone);
        var hash = EmailAuthService.HashIdentifier(canonical);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await EnsureVerifiedIdentity(identity, "registering", ct);
            var claimed = await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Phone"
                && x.IdentifierHash == hash, ct);
            if (claimed is not null && claimed.UserId != identity.UserId) throw Collision();
            var existing = await db.AuthIdentifiers.AsTracking().Where(x => x.UserId == identity.UserId
                && x.Kind == "Phone").ToListAsync(ct);
            if (existing.Count > 1)
                throw new ApplicationFailure(FailureKind.ConcurrencyConflict,
                    "Account phone setup could not be completed. Contact Weymela support.", code: "PhoneStateConflict");
            if (existing.Count == 1 && existing[0].IdentifierHash != hash)
                throw new ApplicationFailure(FailureKind.Validation,
                    "Use the phone number already registered to this account.", code: "RegisteredPhoneMismatch");
            if (existing.Count == 0)
            {
                db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = identity.UserId, Kind = "Phone",
                    IdentifierHash = hash, DeliveryAddress = canonical, IsVerified = false,
                    CreatedAtUtc = clock.GetUtcNow().UtcDateTime });
                db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "RegistrationPhoneClaimed", identity.UserId,
                    null, null, null, Guid.NewGuid(), clock.GetUtcNow().UtcDateTime,
                    $"country={PhoneNumberNormalizer.Region(canonical)};verified=false"));
            }
            else
            {
                existing[0].DeliveryAddress = canonical;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception e) when (HasPostgresCode(e, PostgresErrorCodes.UniqueViolation))
        {
            throw Collision();
        }
        catch (Exception e) when (HasPostgresCode(e, PostgresErrorCodes.SerializationFailure))
        {
            throw new ApplicationFailure(FailureKind.ConcurrencyConflict,
                "Phone setup changed at the same time. Try again.", code: "PhoneClaimConflict");
        }
    }

    public async Task RegisterAsync(DeviceSessionIdentity identity, string phone, CancellationToken ct)
    {
        var canonical = PhoneNumberNormalizer.Normalize(phone);
        var hash = EmailAuthService.HashIdentifier(canonical);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await EnsureVerifiedIdentity(identity, "updating", ct);
            var claimed = await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Phone"
                && x.IdentifierHash == hash, ct);
            if (claimed is not null && claimed.UserId != identity.UserId) throw Collision();
            var existing = await db.AuthIdentifiers.AsTracking().Where(x => x.UserId == identity.UserId
                && x.Kind == "Phone").ToListAsync(ct);
            if (claimed is not null && existing.Count == 1 && existing[0].Id == claimed.Id)
            {
                claimed.DeliveryAddress = canonical;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return;
            }
            db.AuthIdentifiers.RemoveRange(existing.Where(x => claimed is null || x.Id != claimed.Id));
            if (claimed is null)
                db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = identity.UserId, Kind = "Phone",
                    IdentifierHash = hash, DeliveryAddress = canonical, IsVerified = false,
                    CreatedAtUtc = clock.GetUtcNow().UtcDateTime });
            else
                claimed.DeliveryAddress = canonical;
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "PhoneAliasUpdated", identity.UserId,
                null, null, null, Guid.NewGuid(), clock.GetUtcNow().UtcDateTime, "authenticated-phone-lookup-alias"));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception e) when (HasPostgresCode(e, PostgresErrorCodes.UniqueViolation))
        {
            throw Collision();
        }
        catch (Exception e) when (HasPostgresCode(e, PostgresErrorCodes.SerializationFailure))
        {
            throw new ApplicationFailure(FailureKind.ConcurrencyConflict,
                "Phone setup changed at the same time. Try again.", code: "PhoneClaimConflict");
        }
    }

    private static ApplicationFailure Collision() => new(FailureKind.Validation,
        "This phone number is already in use. Sign in to the existing account or use a different number.",
        code: "PhoneUnavailable");

    private async Task EnsureVerifiedIdentity(DeviceSessionIdentity identity, string operation, CancellationToken ct)
    {
        if (!await db.IdentityBindings.AsNoTracking().AnyAsync(x => x.Id == identity.IdentityBindingId
            && x.UserId == identity.UserId && x.Version == identity.IdentityVersion && x.IsActive
            && x.Provider == "Firebase" && x.ProjectId == options.FirebaseProjectId, ct))
            throw new ApplicationFailure(FailureKind.Forbidden, $"Sign in again before {operation} your phone.");
        if (await db.AuthIdentifiers.AsNoTracking().CountAsync(x => x.UserId == identity.UserId
            && x.Kind == "Email" && x.IsVerified && x.DeliveryAddress != null, ct) != 1)
            throw new ApplicationFailure(FailureKind.Forbidden, $"Verify your email before {operation} your phone.");
    }

    private static bool HasPostgresCode(Exception error, string code)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is PostgresException postgres && postgres.SqlState == code)
                return true;
        return false;
    }
}
