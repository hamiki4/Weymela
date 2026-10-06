using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Web;
using Weymela.Application.Web;
using Weymela.Domain;

namespace Weymela.BrowserHost;

// These adapters exist only in the disposable BrowserHost process. They are
// never registered by Pilot/Production and never send real mail or call Firebase.
public sealed class BrowserEmailCodeDelivery : IEmailCodeDelivery
{
    private static readonly ConcurrentDictionary<string, string> Codes = new(StringComparer.OrdinalIgnoreCase);
    public bool Enabled => true;
    public Task SendAsync(string destination, string code, EmailCodePurpose purpose, CancellationToken ct)
    {
        Codes[destination] = code;
        return Task.CompletedTask;
    }

    public static string? Read(string destination) => Codes.TryGetValue(destination.Trim(), out var code) ? code : null;
}

public sealed class BrowserFirebaseCustomTokenIssuer(WeymelaDbContext db) : IFirebaseCustomTokenIssuer
{
    public bool Enabled => true;

    public async Task<FirebaseCustomTokenResult> IssueAsync(Guid userId, string projectId, CancellationToken ct)
    {
        var externalSubject = userId.ToString("N");
        var existing = await db.IdentityBindings.SingleOrDefaultAsync(x => x.Provider == "Firebase"
            && x.ProjectId == projectId && x.ExternalSubject == externalSubject, ct);
        if (existing is null)
        {
            db.IdentityBindings.Add(new IdentityBinding
            {
                Provider = "Firebase", ProjectId = projectId, ExternalSubject = externalSubject,
                UserId = userId, IsActive = true, ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1), Version = 1
            });
            await db.SaveChangesAsync(ct);
        }
        else if (existing.UserId != userId || !existing.IsActive)
        {
            throw new InvalidOperationException("The isolated test identity binding conflicts.");
        }
        return new FirebaseCustomTokenResult($"browser-test:{externalSubject}", DateTime.UtcNow.AddMinutes(5));
    }
}

public sealed class BrowserIdentityTokenVerifier(TimeProvider clock) : IIdentityTokenVerifier
{
    public Task<VerifiedIdentity> VerifyAsync(string sensitiveIdToken, CancellationToken ct)
    {
        const string prefix = "browser-test:";
        if (!sensitiveIdToken.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(sensitiveIdToken[prefix.Length..], "N", out var userId))
            throw new ApplicationFailure(FailureKind.Forbidden, "Test identity could not be verified.");
        var now = clock.GetUtcNow().UtcDateTime;
        return Task.FromResult(new VerifiedIdentity("Firebase", "isolated-v3-test", userId.ToString("N"), now, now.AddHours(1)));
    }
}

// BrowserHost must resolve profiles created during the test flow from the
// disposable database, while retaining static fixture personas for existing
// development-only scenarios. This adapter is never registered by Pilot or
// Production.
public sealed class BrowserWorkspaceDirectory(PersistentWorkspaceDirectory persisted, DevelopmentDirectory fixtures) : IWorkspaceDirectory
{
    public Task<BusinessCard> BusinessCardAsync(Guid id, CancellationToken ct)
        => Prefer(() => persisted.BusinessCardAsync(id, ct), () => fixtures.BusinessCardAsync(id, ct));

    public Task<CustomerOfferBusiness> CustomerOfferBusinessAsync(Guid id, CancellationToken ct)
        => Prefer(() => persisted.CustomerOfferBusinessAsync(id, ct), () => fixtures.CustomerOfferBusinessAsync(id, ct));

    public Task<CreatorCard> CreatorCardAsync(Guid id, CancellationToken ct)
        => Prefer(() => persisted.CreatorCardAsync(id, ct), () => fixtures.CreatorCardAsync(id, ct));

    public Task<CustomerCard> CustomerCardAsync(Guid id, CancellationToken ct)
        => Prefer(() => persisted.CustomerCardAsync(id, ct), () => fixtures.CustomerCardAsync(id, ct));

    public Task<PublicBusiness> BusinessAsync(Guid id, CancellationToken ct)
        => Prefer(() => persisted.BusinessAsync(id, ct), () => fixtures.BusinessAsync(id, ct));

    public Task<PublicCreator> CreatorAsync(Guid id, CancellationToken ct)
        => Prefer(() => persisted.CreatorAsync(id, ct), () => fixtures.CreatorAsync(id, ct));

    private static async Task<T> Prefer<T>(Func<Task<T>> persisted, Func<Task<T>> fixture)
    {
        try { return await persisted(); }
        catch (ApplicationFailure e) when (e.Kind == FailureKind.NotFound) { return await fixture(); }
    }
}

// Browser-only data needed by the manual checkout journey. Development personas
// normally use synthetic session projections, so this deliberately lives in the
// disposable host rather than changing production provisioning or persistence.
public static class BrowserFixtureSeed
{
    public static async Task EnsureManualCheckoutCustomerAsync(WeymelaDbContext db)
    {
        var now = DateTime.UtcNow;
        var userId = DevelopmentDirectory.Id(7);
        var customerId = DevelopmentDirectory.Id(600);
        const string phone = "+251911111111";

        if (!await db.CustomerProfiles.AnyAsync(x => x.CustomerId == customerId))
        {
            db.CustomerProfiles.Add(new CustomerProfileRecord
            {
                CustomerId = customerId,
                UserId = userId,
                PreferredName = "Hana",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        EnsurePublicProfile(db, customerId, ActorRole.Customer, "Hana", "CU-100", "Addis Ababa", "");
        EnsurePublicProfile(db, DevelopmentDirectory.Id(100), ActorRole.Business,
            "Abc Coffee", "BUS-100", "Addis Ababa", "Food",
            directionsUrl: "https://www.google.com/maps/search/?api=1&query=Addis+Ababa");
        EnsurePublicProfile(db, DevelopmentDirectory.Id(200), ActorRole.Business,
            "Bole Studio", "BUS-200", "Addis Ababa", "Food",
            directionsUrl: "https://www.google.com/maps/search/?api=1&query=Bole+Addis+Ababa");
        EnsurePublicProfile(db, DevelopmentDirectory.Id(300), ActorRole.Creator,
            "Bella", "CR-100", "Addis Ababa", "Food", verifiedFollowers: 42000,
            verifiedViews: 186000, socialVerified: true);
        EnsurePublicProfile(db, DevelopmentDirectory.Id(400), ActorRole.Creator,
            "Elias", "CR-200", "Addis Ababa", "Food", verifiedFollowers: 29000,
            verifiedViews: 121000, socialVerified: true);

        // Browser promotions with platform slots require an authoritative
        // verified profile; the public fixture flag alone grants no eligibility.
        foreach (var (creatorId, handle, audience) in new[]
        {
            (DevelopmentDirectory.Id(300), "bella", 42000L),
            (DevelopmentDirectory.Id(400), "elias", 29000L)
        })
        {
            if (!await db.CreatorSocialProfiles.AnyAsync(x => x.CreatorId == creatorId && x.Platform == CreatorPlatform.TikTok))
                db.CreatorSocialProfiles.Add(new CreatorSocialProfileRecord
                {
                    CreatorId = creatorId, Platform = CreatorPlatform.TikTok,
                    ProfileUrl = $"https://www.tiktok.com/@{handle}", SelfReportedAudience = audience,
                    VerificationStatus = "Verified", VerifiedAudience = audience,
                    AudienceVerificationSource = SocialAudienceEligibility.AdminVerified,
                    CreatedAtUtc = now, UpdatedAtUtc = now
                });
        }
        foreach (var (creatorId, handle, audience) in new[]
        {
            (DevelopmentDirectory.Id(300), "bella", 42000L),
            (DevelopmentDirectory.Id(400), "elias", 29000L),
            (DevelopmentDirectory.Id(500), "mika", 800L)
        })
        {
            if (!await db.CreatorSocialProfiles.AnyAsync(x => x.CreatorId == creatorId && x.Platform == CreatorPlatform.Instagram))
                db.CreatorSocialProfiles.Add(new CreatorSocialProfileRecord
                {
                    CreatorId = creatorId, Platform = CreatorPlatform.Instagram,
                    ProfileUrl = $"https://www.instagram.com/{handle}", SelfReportedAudience = audience,
                    VerificationStatus = "Verified", VerifiedAudience = audience,
                    AudienceVerificationSource = SocialAudienceEligibility.AdminVerified,
                    CreatedAtUtc = now, UpdatedAtUtc = now
                });
        }

        if (!await db.CashierPreauthorizations.AnyAsync(x => x.UserId == DevelopmentDirectory.Id(9)))
        {
            var cashier = new CashierPreauthorization(
                DevelopmentDirectory.Id(100), "Abc Checkout", "+251911222222",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("+251911222222"))).ToLowerInvariant(), "browser-fixture-hash",
                now.AddDays(1), now)
            {
                Status = CashierPreauthorizationStatus.Active,
                ActivatedAtUtc = now.AddDays(-1),
                UserId = DevelopmentDirectory.Id(9),
            };
            db.CashierPreauthorizations.Add(cashier);
        }

        var phoneHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(phone))).ToLowerInvariant();
        if (!await db.AuthIdentifiers.AnyAsync(x => x.Kind == "Phone" && x.IdentifierHash == phoneHash))
        {
            db.AuthIdentifiers.Add(new AuthIdentifierRecord
            {
                UserId = userId,
                Kind = "Phone",
                IdentifierHash = phoneHash,
                DeliveryAddress = phone,
                IsVerified = true,
                CreatedAtUtc = now,
            });
        }

        await db.SaveChangesAsync();
    }

    private static void EnsurePublicProfile(
        WeymelaDbContext db,
        Guid subjectId,
        ActorRole role,
        string displayName,
        string publicId,
        string region,
        string category,
        long verifiedFollowers = 0,
        long verifiedViews = 0,
        bool socialVerified = false,
        string? directionsUrl = null)
    {
        if (db.PublicWorkspaceProfiles.Any(x => x.SubjectId == subjectId && x.Role == role)) return;
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile
        {
            SubjectId = subjectId,
            Role = role,
            DisplayName = displayName,
            PublicId = publicId,
            Region = region,
            Category = category,
            VerifiedFollowers = verifiedFollowers,
            VerifiedViews = verifiedViews,
            SocialVerified = socialVerified,
            DirectionsUrl = directionsUrl,
        });
    }
}
