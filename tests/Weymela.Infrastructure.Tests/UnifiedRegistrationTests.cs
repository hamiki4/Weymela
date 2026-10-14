using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class UnifiedRegistrationTests(PostgresFixture fixture)
{
    private static RuntimeOptions Options => new() { EnvironmentName = "Production",
        FirebaseProjectId = "isolated-registration", LegalConsentMode = "Deferred",
        LegalConsentAuthorizationReference = "isolated-test-owner-deferral" };

    [Theory]
    [InlineData(ActorRole.Customer)] [InlineData(ActorRole.Creator)] [InlineData(ActorRole.Business)]
    public async Task First_profile_reuses_the_verified_identity_and_phone_and_retries_without_duplicate_wallets(ActorRole role)
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var identity = await Seed(db);
        var phones = new PhoneAliasService(db, Options, TimeProvider.System);
        await phones.EnsureRegistrationAsync(identity, "+1 (404) 555-0123", default);
        var service = new RoleEnrollmentService(db, TimeProvider.System, Options);
        var request = Request(role) with { RegisteredPhone = "+14045550123" };
        var first = await service.SubmitAsync(new(identity.UserId, ActorRole.Customer), request, "first", default,
            ProfileEnrollmentIntent.FirstProfile);
        var replay = await service.SubmitAsync(new(identity.UserId, ActorRole.Customer), request, "first", default,
            ProfileEnrollmentIntent.FirstProfile);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(role == ActorRole.Creator ? RoleEnrollmentStatus.Pending : RoleEnrollmentStatus.Approved, first.Status);
        Assert.Single(await db.IdentityBindings.ToListAsync());
        Assert.Single(await db.AuthIdentifiers.Where(x => x.Kind == "Email").ToListAsync());
        Assert.Single(await db.AuthIdentifiers.Where(x => x.Kind == "Phone").ToListAsync());
        Assert.Equal(role == ActorRole.Business ? 1 : 0, await db.BusinessWallets.CountAsync());
        Assert.Equal(role == ActorRole.Customer ? 1 : 0, await db.CustomerCashbackAccounts.CountAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
        Assert.Empty(await db.LegalAcceptances.ToListAsync());
    }

    [Theory]
    [InlineData(ActorRole.Business, ActorRole.Customer)]
    [InlineData(ActorRole.Customer, ActorRole.Creator)]
    [InlineData(ActorRole.Creator, ActorRole.Business)]
    public async Task Additional_profile_and_legacy_client_reuse_server_identity_and_ignore_stale_signup_credentials(
        ActorRole original, ActorRole additional)
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var identity = await Seed(db);
        await new PhoneAliasService(db, Options, TimeProvider.System).EnsureRegistrationAsync(identity, "+14045550123", default);
        var service = new RoleEnrollmentService(db, TimeProvider.System, Options);
        var first = await service.SubmitAsync(new(identity.UserId, ActorRole.Customer), Request(original), "first", default);
        if (original == ActorRole.Creator)
            await new RoleEnrollmentService(db, TimeProvider.System).ReviewAsync(new(Guid.NewGuid(), ActorRole.PlatformAdmin),
                first.Id, true, "Isolated fixture review", first.Version, "review", default);
        var request = Request(additional) with { RegisteredPhone = "+254712345678" };
        var result = await service.SubmitAsync(new(identity.UserId, original), request, "additional", default,
            ProfileEnrollmentIntent.AdditionalProfile);
        var repeated = await service.SubmitAsync(new(identity.UserId, original), request, "different-retry-key", default);
        Assert.Equal(result.Id, repeated.Id);
        Assert.Equal("+14045550123", (await service.IdentityAsync(identity.UserId, default)).Phone);
        Assert.Equal(2, await db.RoleEnrollments.CountAsync());
        Assert.Single(await db.IdentityBindings.ToListAsync());
        Assert.Single(await db.AuthIdentifiers.Where(x => x.Kind == "Phone").ToListAsync());
        Assert.True(await db.CommercePermissions.AnyAsync(x => x.UserId == identity.UserId && x.Role == original && x.IsActive));
        Assert.Equal(additional == ActorRole.Creator ? RoleEnrollmentStatus.Pending : RoleEnrollmentStatus.Approved, result.Status);
        Assert.Equal(original == ActorRole.Business || additional == ActorRole.Business ? 1 : 0, await db.BusinessWallets.CountAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
    }

    [Fact]
    public async Task Entry_points_cannot_restart_registration_or_bypass_first_profile_setup()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var identity = await Seed(db); var actor = new Actor(identity.UserId, ActorRole.Customer);
        var service = new RoleEnrollmentService(db, TimeProvider.System, Options);
        var missing = await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(actor, Request(ActorRole.Business),
            "wrong-add", default, ProfileEnrollmentIntent.AdditionalProfile));
        Assert.Equal("FirstProfileRequired", missing.Code);
        await service.SubmitAsync(actor, Request(ActorRole.Customer), "first", default, ProfileEnrollmentIntent.FirstProfile);
        var restart = await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(actor, Request(ActorRole.Business),
            "wrong-first", default, ProfileEnrollmentIntent.FirstProfile));
        Assert.Equal("UseAddProfile", restart.Code);
        var owned = await service.SubmitAsync(actor, Request(ActorRole.Customer), "new-key", default,
            ProfileEnrollmentIntent.AdditionalProfile);
        Assert.True(owned.AlreadyOwned);
        Assert.Single(await db.CustomerProfiles.ToListAsync());
        Assert.Single(await db.CustomerCashbackAccounts.ToListAsync());
    }

    [Fact]
    public async Task Different_identity_cannot_claim_phone_during_first_profile_creation()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var first = await Seed(db); var other = await Seed(db);
        await new PhoneAliasService(db, Options, TimeProvider.System).EnsureRegistrationAsync(first, "+14045550123", default);
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() =>
            new RoleEnrollmentService(db, TimeProvider.System, Options).SubmitAsync(new(other.UserId, ActorRole.Customer),
                Request(ActorRole.Business) with { RegisteredPhone = "+1 (404) 555-0123" }, "collision", default,
                ProfileEnrollmentIntent.FirstProfile));
        Assert.Equal("PhoneUnavailable", failure.Code);
        Assert.Empty(await db.BusinessWallets.AsNoTracking().ToListAsync());
        Assert.Empty(await db.RoleEnrollments.AsNoTracking().ToListAsync());
        Assert.Equal(2, await db.IdentityBindings.CountAsync());
    }

    private static RoleEnrollmentRequest Request(ActorRole role) => new(role, "Isolated registration", null, null, null, null,
        SocialProfiles: role == ActorRole.Creator ? [new("TikTok", "https://www.tiktok.com/@isolated_creator")] : []);

    private static async Task<DeviceSessionIdentity> Seed(WeymelaDbContext db)
    {
        var userId = Guid.NewGuid();
        var binding = new IdentityBinding { UserId = userId, Provider = "Firebase", ProjectId = "isolated-registration",
            ExternalSubject = userId.ToString("N"), IsActive = true, Version = 1, ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1) };
        db.IdentityBindings.Add(binding);
        var email = $"{userId:N}@example.test";
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = userId, Kind = "Email", IsVerified = true,
            DeliveryAddress = email, IdentifierHash = EmailAuthService.HashIdentifier(email), CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return new(userId, binding.Id, binding.Version);
    }
}
