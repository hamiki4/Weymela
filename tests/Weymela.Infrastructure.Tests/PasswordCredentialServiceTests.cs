using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class PasswordCredentialServiceTests(PostgresFixture fixture)
{
    private const string Password = "correct horse battery staple";
    private static RuntimeOptions Options() => new()
    {
        FirebaseProjectId = "isolated-v3-test",
        AuthCodeHashKey = "test-only-password-code-key"
    };

    [Fact]
    public async Task Enrollment_preserves_identity_and_admin_and_stores_only_a_versioned_hash()
    {
        var database = await fixture.CreateAsync();
        Guid user = Guid.NewGuid(); Guid bindingId;
        await using (var db = database.Open())
        {
            var identity = await SeedIdentity(db, user, "admin@example.test"); bindingId = identity.IdentityBindingId;
            db.CommercePermissions.Add(new(user, ActorRole.PlatformAdmin, user, null, true, false));
            await db.SaveChangesAsync();
            await Service(db).EnrollAsync(identity, "0911111111", Password, Password, default);
        }
        await using (var verify = database.Open())
        {
            var credential = await verify.PasswordCredentials.SingleAsync();
            Assert.DoesNotContain(Password, credential.PasswordHash, StringComparison.Ordinal);
            Assert.Equal("PBKDF2-SHA256", credential.Algorithm);
            Assert.Equal(PasswordCredentialHasher.Iterations, credential.WorkFactor);
            Assert.Equal(1, await verify.IdentityBindings.CountAsync(x => x.UserId == user && x.Id == bindingId));
            Assert.Equal(1, await verify.CommercePermissions.CountAsync(x => x.UserId == user
                && x.Role == ActorRole.PlatformAdmin && x.IsActive));
            Assert.Empty(await verify.PublicWorkspaceProfiles.ToListAsync());
        }
    }

    [Theory]
    [InlineData("0911111111")]
    [InlineData("911111111")]
    [InlineData("+251911111111")]
    public async Task Ethiopian_phone_forms_sign_in_to_the_same_existing_identity(string signInPhone)
    {
        var database = await fixture.CreateAsync(); var user = Guid.NewGuid();
        await using var db = database.Open();
        var identity = await SeedIdentity(db, user, "phone@example.test");
        var issuer = new TestIssuer(); var service = Service(db, issuer);
        await service.EnrollAsync(identity, "0911111111", Password, Password, default);
        var existingPhone = await db.AuthIdentifiers.SingleAsync(x => x.Kind == "Phone");
        existingPhone.DeliveryAddress = null;
        await db.SaveChangesAsync();
        var result = await service.SignInAsync(signInPhone, Password, default);
        Assert.True(result.Succeeded); Assert.Equal(user, issuer.LastUserId);
        var phone = Assert.Single(await db.AuthIdentifiers.Where(x => x.Kind == "Phone").ToListAsync());
        Assert.Equal("+251911111111", phone.DeliveryAddress);
        Assert.Single(await db.IdentityBindings.ToListAsync());
    }

    [Fact]
    public async Task International_phone_is_preserved_and_wrong_or_unknown_credentials_are_privacy_safe()
    {
        var database = await fixture.CreateAsync(); var user = Guid.NewGuid();
        await using var db = database.Open();
        var identity = await SeedIdentity(db, user, "international@example.test");
        var service = Service(db);
        await service.EnrollAsync(identity, "+14155552671", Password, Password, default);
        Assert.True((await service.SignInAsync("+1 (415) 555-2671", Password, default)).Succeeded);
        Assert.False((await service.SignInAsync("+1 (415) 555-2671", "wrong-password", default)).Succeeded);
        Assert.False((await service.SignInAsync("+442079460958", "wrong-password", default)).Succeeded);
    }

    [Fact]
    public async Task Duplicate_phone_never_merges_users_and_sequential_replay_is_idempotent()
    {
        var database = await fixture.CreateAsync(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await using var db = database.Open();
        var firstIdentity = await SeedIdentity(db, first, "first@example.test");
        var secondIdentity = await SeedIdentity(db, second, "second@example.test");
        var service = Service(db);
        await service.EnrollAsync(firstIdentity, "0911111111", Password, Password, default);
        await service.EnrollAsync(firstIdentity, "0911111111", Password, Password, default);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.EnrollAsync(
            secondIdentity, "+251911111111", "another secure passphrase", "another secure passphrase", default));
        Assert.Single(await db.PasswordCredentials.ToListAsync());
        Assert.Equal(first, (await db.AuthIdentifiers.SingleAsync(x => x.Kind == "Phone")).UserId);
        Assert.Equal(2, await db.IdentityBindings.CountAsync());
    }

    [Fact]
    public async Task Concurrent_duplicate_phone_enrollment_has_one_winner_and_never_merges_identities()
    {
        var database = await fixture.CreateAsync();
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        DeviceSessionIdentity firstIdentity; DeviceSessionIdentity secondIdentity;
        await using (var seed = database.Open())
        {
            firstIdentity = await SeedIdentity(seed, first, "concurrent-first@example.test");
            secondIdentity = await SeedIdentity(seed, second, "concurrent-second@example.test");
        }

        async Task<bool> Enroll(DeviceSessionIdentity identity, string password)
        {
            await using var context = database.Open();
            try
            {
                await Service(context).EnrollAsync(identity, "+251911111111", password, password, default);
                return true;
            }
            catch (ApplicationFailure) { return false; }
        }

        var outcomes = await Task.WhenAll(
            Enroll(firstIdentity, "first secure account passphrase"),
            Enroll(secondIdentity, "second secure account passphrase"));
        Assert.Single(outcomes, succeeded => succeeded);

        await using var verify = database.Open();
        var phone = Assert.Single(await verify.AuthIdentifiers.Where(x => x.Kind == "Phone").ToListAsync());
        Assert.Contains(phone.UserId, new[] { first, second });
        Assert.Single(await verify.PasswordCredentials.ToListAsync());
        Assert.Equal(2, await verify.IdentityBindings.CountAsync());
        Assert.Equal(2, await verify.AuthIdentifiers.CountAsync(x => x.Kind == "Email" && x.IsVerified));
    }

    [Fact]
    public async Task Repeated_wrong_password_temporarily_locks_account_without_changing_identity()
    {
        var database = await fixture.CreateAsync(); var user = Guid.NewGuid();
        await using var db = database.Open();
        var identity = await SeedIdentity(db, user, "lockout@example.test"); var service = Service(db);
        await service.EnrollAsync(identity, "0911111111", Password, Password, default);
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.False((await service.SignInAsync("0911111111", "wrong-password", default)).Succeeded);
        var credential = await db.PasswordCredentials.SingleAsync();
        Assert.Equal(5, credential.FailedAttempts); Assert.NotNull(credential.LockedUntilUtc);
        Assert.False((await service.SignInAsync("0911111111", Password, default)).Succeeded);
        Assert.Single(await db.IdentityBindings.Where(x => x.UserId == user && x.IsActive).ToListAsync());
    }

    [Fact]
    public async Task Email_verified_reset_rejects_replay_revokes_sessions_and_changes_only_password()
    {
        var database = await fixture.CreateAsync(); var user = Guid.NewGuid(); var now = DateTime.UtcNow;
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        await using var db = database.Open();
        var identity = await SeedIdentity(db, user, "reset@example.test");
        await Service(db, issuer).EnrollAsync(identity, "0911111111", Password, Password, default);
        var device = new AuthorizedDeviceRecord
        {
            UserId = user, CredentialKind = "Pin", CredentialIdHash = new string('a', 64),
            EnrolledAtUtc = now, ExpiresAtUtc = now.AddDays(30), PinVerifier = "v1$test$test", Version = 1
        };
        db.AuthorizedDevices.Add(device);
        db.DeviceSessions.Add(new DeviceSessionRecord
        {
            UserId = user, IdentityBindingId = identity.IdentityBindingId, IdentityVersion = identity.IdentityVersion,
            AuthorizedDeviceId = device.Id, SessionIdentifierHash = new string('b', 64), CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1), LastActivityAtUtc = now, Version = 1
        });
        await db.SaveChangesAsync();
        var email = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);
        await email.StartAsync("reset@example.test", null, EmailCodePurpose.PasswordRecovery, default);
        var grant = await email.VerifyPasswordRecoveryAsync("reset@example.test", delivery.Code!, default);
        Assert.Equal(PasswordRecoveryNextStep.PasswordReset, grant.NextStep);
        const string replacement = "new correct horse battery staple";
        var passwords = Service(db, issuer);
        await passwords.ResetAsync(grant.RecoveryGrant!, replacement, replacement, default);
        await Assert.ThrowsAsync<PasswordRecoveryTransactionInvalidException>(() => passwords.ResetAsync(
            grant.RecoveryGrant!, replacement, replacement, default));
        Assert.False((await passwords.SignInAsync("0911111111", Password, default)).Succeeded);
        Assert.True((await passwords.SignInAsync("0911111111", replacement, default)).Succeeded);
        Assert.NotNull((await db.DeviceSessions.SingleAsync()).RevokedAtUtc);
        Assert.Null((await db.AuthorizedDevices.SingleAsync()).RevokedAtUtc);
        Assert.Single(await db.IdentityBindings.Where(x => x.UserId == user).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_password_reset_grant_has_exactly_one_winner()
    {
        const string replacement = "concurrent replacement passphrase";
        var database = await fixture.CreateAsync(); var user = Guid.NewGuid();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        string grant;
        await using (var setup = database.Open())
        {
            var identity = await SeedIdentity(setup, user, "concurrent-reset@example.test");
            await Service(setup, issuer).EnrollAsync(identity, "0911111111", Password, Password, default);
            var email = new EmailAuthService(setup, delivery, issuer, Options(), TimeProvider.System);
            await email.StartAsync("concurrent-reset@example.test", null, EmailCodePurpose.PasswordRecovery, default);
            grant = (await email.VerifyPasswordRecoveryAsync(
                "concurrent-reset@example.test", delivery.Code!, default)).RecoveryGrant!;
        }

        async Task<bool> Reset()
        {
            await using var context = database.Open();
            try
            {
                await Service(context).ResetAsync(grant, replacement, replacement, default);
                return true;
            }
            catch (PasswordRecoveryTransactionInvalidException) { return false; }
        }

        var outcomes = await Task.WhenAll(Reset(), Reset());
        Assert.Single(outcomes, succeeded => succeeded);
        await using var verify = database.Open();
        Assert.Single(await verify.AuditEvents.Where(x => x.ActorId == user
            && x.EventType == "PasswordCredentialReset").ToListAsync());
        Assert.NotNull((await verify.EmailAuthChallenges.SingleAsync()).RecoveryGrantConsumedAtUtc);
        Assert.True((await Service(verify).SignInAsync("0911111111", replacement, default)).Succeeded);
        Assert.Single(await verify.IdentityBindings.Where(x => x.UserId == user).ToListAsync());
    }

    [Fact]
    public async Task Cancelling_password_recovery_consumes_only_that_purpose_bound_transaction()
    {
        var database = await fixture.CreateAsync(); var user = Guid.NewGuid();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        await using var db = database.Open();
        var identity = await SeedIdentity(db, user, "cancel-reset@example.test");
        var passwords = Service(db, issuer);
        await passwords.EnrollAsync(identity, "0911111111", Password, Password, default);
        var email = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);
        await email.StartAsync("cancel-reset@example.test", null, EmailCodePurpose.PasswordRecovery, default);
        var grant = await email.VerifyPasswordRecoveryAsync("cancel-reset@example.test", delivery.Code!, default);

        await passwords.CancelResetAsync(grant.RecoveryGrant!, default);
        await passwords.CancelResetAsync(grant.RecoveryGrant!, default);
        await Assert.ThrowsAsync<PasswordRecoveryTransactionInvalidException>(() => passwords.ResetAsync(
            grant.RecoveryGrant!, "new correct horse battery staple", "new correct horse battery staple", default));

        Assert.True((await passwords.SignInAsync("0911111111", Password, default)).Succeeded);
        Assert.NotNull((await db.EmailAuthChallenges.SingleAsync()).RecoveryGrantConsumedAtUtc);
    }

    [Fact]
    public async Task New_password_recovery_supersedes_earlier_grant_and_expiry_is_server_enforced()
    {
        var database = await fixture.CreateAsync(); var user = Guid.NewGuid();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var clock = new ManualClock(DateTime.UtcNow);
        await using var db = database.Open();
        var identity = await SeedIdentity(db, user, "supersede-reset@example.test");
        var passwords = new PasswordCredentialService(db, issuer, Options(), clock);
        await passwords.EnrollAsync(identity, "0911111111", Password, Password, default);
        var email = new EmailAuthService(db, delivery, issuer, Options(), clock);
        await email.StartAsync("supersede-reset@example.test", null, EmailCodePurpose.PasswordRecovery, default);
        var first = await email.VerifyPasswordRecoveryAsync("supersede-reset@example.test", delivery.Code!, default);
        clock.Advance(TimeSpan.FromMinutes(2));
        await email.StartAsync("supersede-reset@example.test", null, EmailCodePurpose.PasswordRecovery, default);
        var second = await email.VerifyPasswordRecoveryAsync("supersede-reset@example.test", delivery.Code!, default);

        await Assert.ThrowsAsync<PasswordRecoveryTransactionInvalidException>(() => passwords.ResetAsync(
            first.RecoveryGrant!, "new correct horse battery staple", "new correct horse battery staple", default));
        clock.Advance(TimeSpan.FromMinutes(11));
        await Assert.ThrowsAsync<PasswordRecoveryTransactionInvalidException>(() => passwords.ResetAsync(
            second.RecoveryGrant!, "new correct horse battery staple", "new correct horse battery staple", default));
        Assert.True((await passwords.SignInAsync("0911111111", Password, default)).Succeeded);
    }

    [Fact]
    public async Task Password_reset_rejects_a_wrong_purpose_transaction_without_changing_the_credential()
    {
        var database = await fixture.CreateAsync(); var user = Guid.NewGuid();
        await using var db = database.Open();
        var identity = await SeedIdentity(db, user, "purpose-reset@example.test");
        var passwords = Service(db);
        await passwords.EnrollAsync(identity, "0911111111", Password, Password, default);
        const string wrongPurposeGrant = "wrong-purpose-recovery-authorization";
        var now = DateTime.UtcNow;
        db.EmailAuthChallenges.Add(new EmailAuthChallengeRecord
        {
            UserId = user,
            IdentifierHash = EmailAuthService.HashIdentifier("purpose-reset@example.test"),
            EmailIdentifierHash = EmailAuthService.HashIdentifier("purpose-reset@example.test"),
            Purpose = EmailCodePurpose.PinRecovery.ToString(),
            CodeHash = "not-used",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(10),
            ConsumedAtUtc = now,
            RecoveryGrantHash = PasswordCredentialService.HashRecoveryGrant(wrongPurposeGrant),
            RecoveryGrantExpiresAtUtc = now.AddMinutes(10)
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PasswordRecoveryTransactionInvalidException>(() => passwords.ResetAsync(
            wrongPurposeGrant, "new correct horse battery staple", "new correct horse battery staple", default));
        Assert.True((await passwords.SignInAsync("0911111111", Password, default)).Succeeded);
    }

    [Fact]
    public async Task Current_password_policy_does_not_invent_a_reuse_restriction()
    {
        var database = await fixture.CreateAsync(); var user = Guid.NewGuid();
        var delivery = new TestDelivery();
        await using var db = database.Open();
        var identity = await SeedIdentity(db, user, "reuse-reset@example.test");
        var passwords = Service(db);
        await passwords.EnrollAsync(identity, "0911111111", Password, Password, default);
        var email = new EmailAuthService(db, delivery, new TestIssuer(), Options(), TimeProvider.System);
        await email.StartAsync("reuse-reset@example.test", null, EmailCodePurpose.PasswordRecovery, default);
        var recovery = await email.VerifyPasswordRecoveryAsync(
            "reuse-reset@example.test", delivery.Code!, default);

        await passwords.ResetAsync(recovery.RecoveryGrant!, Password, Password, default);
        Assert.True((await passwords.SignInAsync("0911111111", Password, default)).Succeeded);
    }

    private static PasswordCredentialService Service(WeymelaDbContext db, TestIssuer? issuer = null) =>
        new(db, issuer ?? new TestIssuer(), Options(), TimeProvider.System);

    private static async Task<DeviceSessionIdentity> SeedIdentity(WeymelaDbContext db, Guid userId, string email)
    {
        var binding = new IdentityBinding
        {
            UserId = userId, Provider = "Firebase", ProjectId = "isolated-v3-test",
            ExternalSubject = userId.ToString("N"), IsActive = true,
            ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1), Version = 1
        };
        db.IdentityBindings.Add(binding);
        db.AuthIdentifiers.Add(new AuthIdentifierRecord
        {
            UserId = userId, Kind = "Email", IdentifierHash = EmailAuthService.HashIdentifier(email),
            DeliveryAddress = email, IsVerified = true, CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return new(userId, binding.Id, binding.Version);
    }

    private sealed class TestIssuer : IFirebaseCustomTokenIssuer
    {
        public bool Enabled => true;
        public Guid? LastUserId { get; private set; }
        public Task<FirebaseCustomTokenResult> IssueAsync(Guid userId, string projectId, CancellationToken ct)
        {
            LastUserId = userId;
            return Task.FromResult(new FirebaseCustomTokenResult("test-custom-token", DateTime.UtcNow.AddMinutes(5)));
        }
    }

    private sealed class TestDelivery : IEmailCodeDelivery
    {
        public bool Enabled => true;
        public string? Code { get; private set; }
        public Task SendAsync(string destination, string code, EmailCodePurpose purpose, CancellationToken ct)
        { Code = code; return Task.CompletedTask; }
    }

    private sealed class ManualClock(DateTime initial) : TimeProvider
    {
        private DateTime value = initial;
        public override DateTimeOffset GetUtcNow() => new(value, TimeSpan.Zero);
        public void Advance(TimeSpan duration) => value = value.Add(duration);
    }
}
