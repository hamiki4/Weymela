using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Weymela.Application;
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
    public async Task Email_only_signup_verifies_once_without_creating_a_phone_alias()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var service = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);

        var started = await service.StartAsync("Owner@Example.com", null, EmailCodePurpose.Signup, default);
        Assert.True(started.Accepted); Assert.Single(delivery.Codes); Assert.False(issuer.Called);
        Assert.Empty(await db.AuthIdentifiers.ToListAsync());

        var token = await service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, delivery.Codes[0].Code, default);
        Assert.Equal("custom-token", token.CustomToken); Assert.True(issuer.Called);
        var identifiers = await db.AuthIdentifiers.OrderBy(x => x.Kind).ToListAsync();
        Assert.Single(identifiers);
        Assert.Equal("Email", identifiers[0].Kind); Assert.True(identifiers[0].IsVerified);
        Assert.Single(issuer.IssuedUserIds);
    }

    [Fact]
    public async Task Existing_account_email_verification_returns_to_the_same_firebase_identity()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var service = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);

        await service.StartAsync("owner@example.com", null, EmailCodePurpose.Signup, default);
        await service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, delivery.Codes[^1].Code, default);
        var accountUserId = issuer.IssuedUserIds[^1];
        var binding = new IdentityBinding { UserId = accountUserId, Provider = "Firebase", ProjectId = "isolated-v3-test",
            ExternalSubject = "test-user", IsActive = true, Version = 1, ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1) };
        db.IdentityBindings.Add(binding); await db.SaveChangesAsync();
        await new PhoneAliasService(db, Options(), TimeProvider.System).RegisterAsync(
            new DeviceSessionIdentity(accountUserId, binding.Id, binding.Version), "+251900000000", default);

        await service.StartAsync("owner@example.com", null, EmailCodePurpose.DeviceEnrollment, default);
        await service.VerifyAsync("owner@example.com", EmailCodePurpose.DeviceEnrollment, delivery.Codes[^1].Code, default);
        var emailLoginUserId = issuer.IssuedUserIds[^1];

        Assert.Equal(accountUserId, emailLoginUserId);
        var aliases = await db.AuthIdentifiers.ToListAsync();
        Assert.Contains(aliases, x => x.Kind == "Phone" && !x.IsVerified && x.UserId == accountUserId);
    }

    [Fact]
    public async Task Signup_verification_cannot_substitute_a_different_email()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var service = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);
        await service.StartAsync("owner@example.com", null, EmailCodePurpose.Signup, default);
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
        await service.StartAsync("owner@example.com", null, EmailCodePurpose.Signup, default);
        var code = delivery.Codes[0].Code;
        await service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, code, default);
        await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, code, default));
        Assert.Single(issuer.IssuedUserIds);
        Assert.Equal(1, await db.AuthIdentifiers.CountAsync());
    }

    [Fact]
    public async Task Device_enrollment_rejects_phone_input_without_email_delivery_or_account_creation()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var service = new EmailAuthService(db, delivery, new TestIssuer(), Options(), TimeProvider.System);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.StartAsync(
            "+251911111111", null, EmailCodePurpose.DeviceEnrollment, default));
        Assert.Empty(delivery.Codes);
        Assert.Empty(await db.AuthIdentifiers.ToListAsync());
    }

    [Fact]
    public async Task Wrong_purpose_expired_and_replayed_codes_are_rejected()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var clock = new ManualClock(DateTime.UtcNow);
        var service = new EmailAuthService(db, delivery, new TestIssuer(), Options(), clock);
        await service.StartAsync("owner@example.com", null, EmailCodePurpose.Signup, default);
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
        await service.StartAsync("owner@example.com", null, EmailCodePurpose.Signup, default);
        for (var i = 0; i < 5; i++) await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, "000000", default));
        await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, delivery.Codes[0].Code, default));
        Assert.Empty(await db.AuthIdentifiers.ToListAsync());
    }

    [Fact]
    public async Task A_registered_phone_does_not_block_a_distinct_email_only_signup()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = Guid.NewGuid(), Kind = "Phone", IdentifierHash = HashIdentifier("+251900000000"), CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var delivery = new TestDelivery(); var service = new EmailAuthService(db, delivery, new TestIssuer(), Options(), TimeProvider.System);
        var result = await service.StartAsync("owner@example.com", null, EmailCodePurpose.Signup, default);
        Assert.True(result.Accepted); Assert.Single(delivery.Codes); Assert.Single(await db.AuthIdentifiers.ToListAsync());
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

    [Fact]
    public async Task Recovery_rejects_phone_input_without_sending_a_code()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery();
        var service = new EmailAuthService(db, delivery, new TestIssuer(), Options(), TimeProvider.System);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.StartAsync("0911111111", null, EmailCodePurpose.PinRecovery, default));
        Assert.Empty(delivery.Codes);
    }

    [Theory]
    [InlineData("0911111111", "+251911111111")]
    [InlineData("911111111", "+251911111111")]
    [InlineData("+251 911-111-111", "+251911111111")]
    [InlineData("+1 (202) 555-0123", "+12025550123")]
    public void Phones_normalize_before_hashing_and_lookup(string input, string canonical)
        => Assert.Equal(canonical, PhoneNumberNormalizer.Normalize(input));

    [Theory]
    [InlineData("091111111")]
    [InlineData("91111111")]
    [InlineData("+25191111111")]
    [InlineData("091111111A")]
    public void Malformed_phones_are_rejected(string input)
        => Assert.Throws<ApplicationFailure>(() => PhoneNumberNormalizer.Normalize(input));

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
