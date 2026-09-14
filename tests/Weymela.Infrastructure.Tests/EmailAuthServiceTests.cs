using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class EmailAuthServiceTests(PostgresFixture fixture)
{
    private static RuntimeOptions Options() => new() { FirebaseProjectId = "isolated-v3-test", AuthCodeHashKey = "test-only-key" };

    [Fact]
    public async Task Signup_requires_both_identifiers_and_email_verification_before_token_issue()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var service = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);

        var started = await service.StartAsync("Owner@Example.com", "+251 900 000000", EmailCodePurpose.Signup, default);
        Assert.True(started.Accepted); Assert.Single(delivery.Codes); Assert.False(issuer.Called);
        Assert.Empty(await db.AuthIdentifiers.ToListAsync());

        var token = await service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, delivery.Codes[0].Code, default);
        Assert.Equal("custom-token", token.CustomToken); Assert.True(issuer.Called);
        var identifiers = await db.AuthIdentifiers.OrderBy(x => x.Kind).ToListAsync();
        Assert.Equal(2, identifiers.Count);
        Assert.Equal("Email", identifiers[0].Kind); Assert.True(identifiers[0].IsVerified);
        Assert.Equal("Phone", identifiers[1].Kind); Assert.False(identifiers[1].IsVerified);
        Assert.All(identifiers, identifier => Assert.Equal(identifiers[0].UserId, identifier.UserId));
    }

    [Fact]
    public async Task Email_and_phone_login_resolve_to_the_same_user_and_firebase_identity()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var service = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);

        await service.StartAsync("owner@example.com", "+251900000000", EmailCodePurpose.Signup, default);
        await service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, delivery.Codes[^1].Code, default);
        var accountUserId = issuer.IssuedUserIds[^1];

        await service.StartAsync("owner@example.com", null, EmailCodePurpose.DeviceEnrollment, default);
        await service.VerifyAsync("owner@example.com", EmailCodePurpose.DeviceEnrollment, delivery.Codes[^1].Code, default);
        var emailLoginUserId = issuer.IssuedUserIds[^1];

        await service.StartAsync("+251 900 000000", null, EmailCodePurpose.DeviceEnrollment, default);
        Assert.Equal("owner@example.com", delivery.Codes[^1].Destination);
        await service.VerifyAsync("+251900000000", EmailCodePurpose.DeviceEnrollment, delivery.Codes[^1].Code, default);
        var phoneLoginUserId = issuer.IssuedUserIds[^1];

        Assert.Equal(accountUserId, emailLoginUserId);
        Assert.Equal(accountUserId, phoneLoginUserId);
        var aliases = await db.AuthIdentifiers.ToListAsync();
        Assert.Contains(aliases, x => x.Kind == "Phone" && !x.IsVerified && x.UserId == accountUserId);
    }

    [Fact]
    public async Task Signup_verification_cannot_substitute_different_email_or_phone()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var service = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);
        await service.StartAsync("owner@example.com", "+251900000000", EmailCodePurpose.Signup, default);
        var code = delivery.Codes[0].Code;
        await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync("other@example.com", EmailCodePurpose.Signup, code, default));
        Assert.Empty(await db.AuthIdentifiers.ToListAsync());
        Assert.False(issuer.Called);
    }

    [Fact]
    public async Task Replayed_signup_verification_cannot_issue_a_second_identity()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var service = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);
        await service.StartAsync("owner@example.com", "+251900000000", EmailCodePurpose.Signup, default);
        var code = delivery.Codes[0].Code;
        await service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, code, default);
        await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, code, default));
        Assert.Single(issuer.IssuedUserIds);
        Assert.Equal(2, await db.AuthIdentifiers.CountAsync());
    }

    [Fact]
    public async Task Unknown_phone_returns_generic_result_without_email_delivery_or_account_creation()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var service = new EmailAuthService(db, delivery, new TestIssuer(), Options(), TimeProvider.System);
        var result = await service.StartAsync("+251911111111", null, EmailCodePurpose.DeviceEnrollment, default);
        Assert.False(result.Accepted);
        Assert.Empty(delivery.Codes);
        Assert.Empty(await db.AuthIdentifiers.ToListAsync());
    }

    [Fact]
    public async Task Wrong_purpose_expired_and_replayed_codes_are_rejected()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var clock = new ManualClock(DateTime.UtcNow);
        var service = new EmailAuthService(db, delivery, new TestIssuer(), Options(), clock);
        await service.StartAsync("owner@example.com", "+251900000000", EmailCodePurpose.Signup, default);
        var code = delivery.Codes[0].Code;
        await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync("owner@example.com", EmailCodePurpose.DeviceEnrollment, code, default));
        clock.Advance(TimeSpan.FromMinutes(11));
        await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, code, default));
    }

    [Fact]
    public async Task Repeated_wrong_codes_hit_attempt_limit_without_revealing_or_consuming_account()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var service = new EmailAuthService(db, delivery, new TestIssuer(), Options(), TimeProvider.System);
        await service.StartAsync("owner@example.com", "+251900000000", EmailCodePurpose.Signup, default);
        for (var i = 0; i < 5; i++) await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, "000000", default));
        await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, delivery.Codes[0].Code, default));
        Assert.Empty(await db.AuthIdentifiers.ToListAsync());
    }

    [Fact]
    public async Task Conflicting_identifiers_never_create_a_new_mapping_or_send_a_code()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = Guid.NewGuid(), Kind = "Phone", IdentifierHash = HashIdentifier("+251900000000"), CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var delivery = new TestDelivery(); var service = new EmailAuthService(db, delivery, new TestIssuer(), Options(), TimeProvider.System);
        var result = await service.StartAsync("owner@example.com", "+251900000000", EmailCodePurpose.Signup, default);
        Assert.False(result.Accepted); Assert.Empty(delivery.Codes); Assert.Single(await db.AuthIdentifiers.ToListAsync());
    }

    [Fact]
    public async Task Recovery_initiation_requires_a_registered_verified_email()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = Guid.NewGuid(), Kind = "Email", IdentifierHash = HashIdentifier("owner@example.com"), IsVerified = false, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var delivery = new TestDelivery(); var service = new EmailAuthService(db, delivery, new TestIssuer(), Options(), TimeProvider.System);
        var result = await service.StartAsync("owner@example.com", null, EmailCodePurpose.PinRecovery, default);
        Assert.False(result.Accepted); Assert.Empty(delivery.Codes);
    }

    private sealed class TestDelivery : IEmailCodeDelivery
    {
        public bool Enabled => true;
        public List<(string Destination, string Code, EmailCodePurpose Purpose)> Codes { get; } = [];
        public Task SendAsync(string destination, string code, EmailCodePurpose purpose, CancellationToken ct)
        { Codes.Add((destination, code, purpose)); return Task.CompletedTask; }
    }

    private sealed class TestIssuer : IFirebaseCustomTokenIssuer
    {
        public bool Enabled => true; public bool Called { get; private set; }
        public List<Guid> IssuedUserIds { get; } = [];
        public Task<FirebaseCustomTokenResult> IssueAsync(Guid userId, string projectId, CancellationToken ct)
        { Called = true; IssuedUserIds.Add(userId); return Task.FromResult(new FirebaseCustomTokenResult("custom-token", DateTime.UtcNow.AddMinutes(5))); }
    }

    private sealed class ManualClock(DateTime initial) : TimeProvider
    {
        private DateTime utc = DateTime.SpecifyKind(initial, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(utc);
        public void Advance(TimeSpan amount) => utc = utc.Add(amount);
    }
    private static string HashIdentifier(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
