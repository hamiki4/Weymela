using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class PhoneAliasServiceTests(PostgresFixture fixture)
{
    private static RuntimeOptions Options() => new() { FirebaseProjectId = "isolated-v3-test" };

    [Fact]
    public async Task All_local_forms_register_one_alias_and_repeated_registration_is_idempotent()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var identity = await SeedAsync(db);
        var service = new PhoneAliasService(db, Options(), TimeProvider.System);
        await service.RegisterAsync(identity, "0911111111", default);
        await service.RegisterAsync(identity, "911111111", default);
        await service.RegisterAsync(identity, "+251911111111", default);
        var alias = Assert.Single(await db.AuthIdentifiers.Where(x => x.Kind == "Phone").ToListAsync());
        Assert.Equal(identity.UserId, alias.UserId);
        Assert.Equal(EmailAuthService.HashIdentifier("+251911111111"), alias.IdentifierHash);
        Assert.Equal("+251911111111", alias.DeliveryAddress);
        Assert.False(alias.IsVerified);
        Assert.Single(await db.AuditEvents.Where(x => x.EventType == "PhoneAliasUpdated").ToListAsync());
    }

    [Fact]
    public async Task Another_identity_cannot_claim_the_same_number_or_merge_users()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var first = await SeedAsync(db); var second = await SeedAsync(db);
        var service = new PhoneAliasService(db, Options(), TimeProvider.System);
        await service.RegisterAsync(first, "0911111111", default);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.RegisterAsync(second, "+251911111111", default));
        Assert.Single(await db.AuthIdentifiers.Where(x => x.Kind == "Phone").ToListAsync());
        Assert.Equal(2, await db.IdentityBindings.CountAsync());
    }

    [Fact]
    public async Task Concurrent_claims_commit_at_most_one_alias()
    {
        var database = await fixture.CreateAsync();
        DeviceSessionIdentity first; DeviceSessionIdentity second;
        await using (var seed = database.Open())
        {
            first = await SeedAsync(seed); second = await SeedAsync(seed);
        }
        await using var one = database.Open(); await using var two = database.Open();
        var results = await Task.WhenAll(
            Attempt(new PhoneAliasService(one, Options(), TimeProvider.System), first, "0911111111"),
            Attempt(new PhoneAliasService(two, Options(), TimeProvider.System), second, "911111111"));
        Assert.Single(results, success => success);
        await using var verify = database.Open();
        Assert.Single(await verify.AuthIdentifiers.Where(x => x.Kind == "Phone").ToListAsync());
        Assert.Equal(2, await verify.IdentityBindings.CountAsync());
    }

    [Fact]
    public async Task Inactive_binding_cannot_register_a_phone_alias()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var identity = await SeedAsync(db, active: false);
        await Assert.ThrowsAsync<ApplicationFailure>(() => new PhoneAliasService(db, Options(), TimeProvider.System)
            .RegisterAsync(identity, "0911111111", default));
        Assert.Empty(await db.AuthIdentifiers.Where(x => x.Kind == "Phone").ToListAsync());
    }

    private static async Task<bool> Attempt(PhoneAliasService service, DeviceSessionIdentity identity, string phone)
    {
        try { await service.RegisterAsync(identity, phone, default); return true; }
        catch (ApplicationFailure) { return false; }
    }

    private static async Task<DeviceSessionIdentity> SeedAsync(Weymela.Infrastructure.Persistence.WeymelaDbContext db, bool active = true)
    {
        var user = Guid.NewGuid();
        var binding = new IdentityBinding { UserId = user, Provider = "Firebase", ProjectId = "isolated-v3-test",
            ExternalSubject = Guid.NewGuid().ToString("N"), IsActive = active, Version = 1,
            ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1) };
        db.IdentityBindings.Add(binding);
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = user, Kind = "Email",
            IdentifierHash = EmailAuthService.HashIdentifier($"{user:N}@example.test"),
            DeliveryAddress = $"{user:N}@example.test", IsVerified = true, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return new(user, binding.Id, binding.Version);
    }
}
