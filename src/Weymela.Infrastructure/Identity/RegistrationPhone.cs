using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

// Called inside the caller's identity-verified, serializable transaction. A profile
// never changes account credentials. Phone aliases are not evidence of SMS verification.
internal static class RegistrationPhone
{
    public static async Task EnsureAsync(WeymelaDbContext db, Guid userId, string phone,
        DateTime now, CancellationToken ct)
    {
        var canonical = PhoneNumberNormalizer.Normalize(phone);
        var hash = EmailAuthService.HashIdentifier(canonical);
        var claimed = await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Phone"
            && x.IdentifierHash == hash, ct);
        if (claimed is not null && claimed.UserId != userId)
            throw new ApplicationFailure(FailureKind.Validation,
                "This phone number is already in use. Sign in to the existing account or use a different number.",
                code: "PhoneUnavailable");
        var existing = await db.AuthIdentifiers.AsTracking().Where(x => x.UserId == userId && x.Kind == "Phone").ToListAsync(ct);
        if (existing.Count > 1)
            throw new ApplicationFailure(FailureKind.ConcurrencyConflict,
                "Account phone setup could not be completed. Contact Weymela support.", code: "PhoneStateConflict");
        if (existing.Count == 1 && existing[0].IdentifierHash != hash)
            throw new ApplicationFailure(FailureKind.Validation,
                "Use the phone number already registered to this account.", code: "RegisteredPhoneMismatch");
        if (existing.Count == 1)
        {
            existing[0].DeliveryAddress = canonical;
            return;
        }
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = userId, Kind = "Phone",
            IdentifierHash = hash, DeliveryAddress = canonical, IsVerified = false, CreatedAtUtc = now });
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "RegistrationPhoneClaimed", userId,
            null, null, null, Guid.NewGuid(), now,
            $"country={PhoneNumberNormalizer.Region(canonical)};verified=false"));
    }
}
