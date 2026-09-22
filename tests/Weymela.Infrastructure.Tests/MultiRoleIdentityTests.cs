using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class MultiRoleIdentityTests(PostgresFixture fixture)
{
    [Fact]
    public async Task One_verified_identity_can_have_multiple_profiles_but_sign_in_requires_selection()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var business = Guid.NewGuid(); var creator = Guid.NewGuid(); var customer = Guid.NewGuid();
        await SeedAsync(db, user, business, creator, customer);
        var service = new TrustedIdentityService(db, new Verifier(), new Directory());

        var required = await Assert.ThrowsAsync<ProfileSelectionRequiredException>(() => service.SignInAsync("token", default));
        Assert.Equal(3, required.Profiles.Count);
        Assert.Contains(required.Profiles, x => x.Actor.Role == ActorRole.Business && x.Actor.BusinessId == business);
        Assert.Contains(required.Profiles, x => x.Actor.Role == ActorRole.Creator && x.Actor.CreatorId == creator);
        Assert.Contains(required.Profiles, x => x.Actor.Role == ActorRole.Customer && x.Actor.CustomerId == customer);

        var selected = await service.SignInAsync("token", new ProfileSelection(ActorRole.Business, business, business), default);
        Assert.Equal(ActorRole.Business, selected.Actor.Role);
        Assert.Equal(business, selected.Actor.BusinessId);
        Assert.Equal(3, selected.Profiles.Count);
    }

    [Fact]
    public async Task Tampered_selection_is_rejected_and_inactive_membership_is_not_selectable()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var business = Guid.NewGuid(); var creator = Guid.NewGuid(); var customer = Guid.NewGuid();
        await SeedAsync(db, user, business, creator, customer);
        var inactiveBusiness = Guid.NewGuid();
        db.CommercePermissions.Add(new CommercePermission(user, ActorRole.Business, inactiveBusiness, inactiveBusiness, false, false));
        await db.SaveChangesAsync();
        var service = new TrustedIdentityService(db, new Verifier(), new Directory());

        var available = await service.ProfilesForUserAsync(user, default);
        Assert.Equal(3, available.Count);
        Assert.DoesNotContain(available, profile => profile.Actor.BusinessId == inactiveBusiness);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SignInAsync("token",
            new ProfileSelection(ActorRole.Business, inactiveBusiness, inactiveBusiness), default));

        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SignInAsync("token",
            new ProfileSelection(ActorRole.Business, Guid.NewGuid(), business), default));
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SignInAsync("token",
            new ProfileSelection(ActorRole.Business, business, Guid.NewGuid()), default));
    }

    [Fact]
    public async Task A_single_active_profile_preserves_direct_sign_in_behavior()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var creator = Guid.NewGuid();
        db.IdentityBindings.Add(new IdentityBinding { Provider = "Firebase", ProjectId = "weymela-pilot", ExternalSubject = "uid", UserId = user, IsActive = true, ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1), Version = 1 });
        db.CommercePermissions.Add(new CommercePermission(user, ActorRole.Creator, creator, null, true, false));
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = creator, Role = ActorRole.Creator, DisplayName = "Creator", PublicId = "CR-1", Region = "Region", Category = "Food" });
        await db.SaveChangesAsync();
        var result = await new TrustedIdentityService(db, new Verifier(), new Directory()).SignInAsync("token", default);
        Assert.Equal(ActorRole.Creator, result.Actor.Role);
        Assert.Single(result.Profiles);
    }

    [Fact]
    public async Task Verified_identity_without_profiles_gets_restricted_onboarding_context()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        db.IdentityBindings.Add(new IdentityBinding { Provider = "Firebase", ProjectId = "weymela-pilot", ExternalSubject = "uid", UserId = user, IsActive = true, ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1), Version = 1 });
        await db.SaveChangesAsync();
        var result = await new TrustedIdentityService(db, new Verifier(), new Directory()).SignInAsync("token", default);
        Assert.Equal(user, result.Actor.UserId);
        Assert.Equal(ActorRole.Customer, result.Actor.Role);
        Assert.Equal(Guid.Empty, result.Actor.CustomerId);
        Assert.Empty(result.Profiles);
    }

    [Fact]
    public async Task Same_second_firebase_auth_time_is_accepted_for_a_new_binding()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        var authTime = new DateTime(2026, 9, 15, 18, 57, 57, DateTimeKind.Utc);
        db.IdentityBindings.Add(new IdentityBinding { Provider = "Firebase", ProjectId = "weymela-pilot", ExternalSubject = "uid", UserId = user, IsActive = true, ValidAfterUtc = authTime, Version = 1 });
        await db.SaveChangesAsync();

        var result = await new TrustedIdentityService(db, new Verifier(authTime), new Directory()).SignInAsync("token", default);

        Assert.Equal(user, result.Actor.UserId);
        Assert.Equal(ActorRole.Customer, result.Actor.Role);
    }

    [Fact]
    public async Task Firebase_auth_time_older_than_binding_is_rejected()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        var validAfter = new DateTime(2026, 9, 15, 18, 57, 57, DateTimeKind.Utc);
        db.IdentityBindings.Add(new IdentityBinding { Provider = "Firebase", ProjectId = "weymela-pilot", ExternalSubject = "uid", UserId = user, IsActive = true, ValidAfterUtc = validAfter, Version = 1 });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationFailure>(() =>
            new TrustedIdentityService(db, new Verifier(validAfter.AddSeconds(-1)), new Directory()).SignInAsync("token", default));
    }

    [Fact]
    public async Task Revoked_firebase_binding_is_rejected_even_at_the_same_auth_time()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        var authTime = new DateTime(2026, 9, 15, 18, 57, 57, DateTimeKind.Utc);
        db.IdentityBindings.Add(new IdentityBinding { Provider = "Firebase", ProjectId = "weymela-pilot", ExternalSubject = "uid", UserId = user, IsActive = false, ValidAfterUtc = authTime, Version = 1 });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationFailure>(() =>
            new TrustedIdentityService(db, new Verifier(authTime), new Directory()).SignInAsync("token", default));
    }

    private static async Task SeedAsync(WeymelaDbContext db, Guid user, Guid business, Guid creator, Guid customer)
    {
        db.IdentityBindings.Add(new IdentityBinding { Provider = "Firebase", ProjectId = "weymela-pilot", ExternalSubject = "uid", UserId = user, IsActive = true, ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1), Version = 1 });
        db.CommercePermissions.AddRange(
            new CommercePermission(user, ActorRole.Business, business, business, true, true),
            new CommercePermission(user, ActorRole.Creator, creator, null, true, false),
            new CommercePermission(user, ActorRole.Customer, customer, null, true, false));
        db.PublicWorkspaceProfiles.AddRange(
            new PublicWorkspaceProfile { SubjectId = business, Role = ActorRole.Business, DisplayName = "Business", PublicId = "BUS-1" },
            new PublicWorkspaceProfile { SubjectId = creator, Role = ActorRole.Creator, DisplayName = "Creator", PublicId = "CR-1", Region = "Region", Category = "Food" },
            new PublicWorkspaceProfile { SubjectId = customer, Role = ActorRole.Customer, DisplayName = "Customer", PublicId = "CU-1" });
        await db.SaveChangesAsync();
    }

    private sealed class Verifier(DateTime? requestedAuthenticatedAtUtc = null) : IIdentityTokenVerifier
    {
        private readonly DateTime authenticatedAtUtc = requestedAuthenticatedAtUtc ?? DateTime.UtcNow;

        public Task<VerifiedIdentity> VerifyAsync(string _, CancellationToken __)
            => Task.FromResult(new VerifiedIdentity("Firebase", "weymela-pilot", "uid", authenticatedAtUtc, authenticatedAtUtc.AddHours(1)));
    }

    private sealed class Directory : IWorkspaceDirectory
    {
        public Task<BusinessCard> BusinessCardAsync(Guid id, CancellationToken _) => Task.FromResult(new BusinessCard(id, "Business", "Region", null));
        public Task<CustomerOfferBusiness> CustomerOfferBusinessAsync(Guid id, CancellationToken _) => Task.FromResult(new CustomerOfferBusiness("Business", null));
        public Task<CreatorCard> CreatorCardAsync(Guid id, CancellationToken _) => Task.FromResult(new CreatorCard(id, "Creator", "CR-1", "Region", "Food", 1, 1, true, null));
        public Task<CustomerCard> CustomerCardAsync(Guid id, CancellationToken _) => Task.FromResult(new CustomerCard(id, "Customer", "CU-1"));
        public Task<PublicBusiness> BusinessAsync(Guid id, CancellationToken _) => Task.FromResult(new PublicBusiness(id, "Business"));
        public Task<PublicCreator> CreatorAsync(Guid id, CancellationToken _) => Task.FromResult(new PublicCreator(id, "CR-1", "Creator"));
    }
}
