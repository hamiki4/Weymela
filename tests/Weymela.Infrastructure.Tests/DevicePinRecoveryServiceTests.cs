using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class DevicePinRecoveryServiceTests(PostgresFixture fixture)
{
    private static readonly DateTime Now = new(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc);
    private static readonly string PinPepper = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly string CodeKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private const string Code = "246810";
    private const string OldPin = "01234";
    private const string NewPin = "56789";

    [Fact]
    public async Task Valid_recovery_replaces_account_device_state_and_preserves_identity_access_and_balance()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, usePhoneIdentifier: true, additionalDeviceAndSession: true,
            failedAttempts: DeviceAccessPolicy.RecoveryAttemptThreshold);

        DevicePinRecoveryResult result;
        await using (var db = database.Open())
            result = await Service(db).CompleteAsync(Request(seeded));

        Assert.Equal(seeded.UserId, result.Session.UserId);
        Assert.Equal(Now, result.Session.CreatedAtUtc);
        Assert.Equal(Now, result.Session.LastActivityAtUtc);
        Assert.Null(result.Session.LockedAtUtc);
        Assert.Equal(Now.AddHours(1), result.Session.ExpiresAtUtc);

        await using var verify = database.Open();
        var devices = await verify.AuthorizedDevices.Where(x => x.UserId == seeded.UserId)
            .OrderBy(x => x.EnrolledAtUtc).ToListAsync();
        var sessions = await verify.DeviceSessions.Where(x => x.UserId == seeded.UserId)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync();
        var replacement = Assert.Single(devices, x => x.RevokedAtUtc is null);
        var replacementSession = Assert.Single(sessions, x => x.RevokedAtUtc is null);

        Assert.Equal(result.AuthorizedDeviceId, replacement.Id);
        Assert.Equal(result.Session.Id, replacementSession.Id);
        Assert.All(devices.Where(x => x.Id != replacement.Id), x => Assert.Equal(Now, x.RevokedAtUtc));
        Assert.All(sessions.Where(x => x.Id != replacementSession.Id), x => Assert.Equal(Now, x.RevokedAtUtc));
        Assert.Equal(Now.AddDays(30), replacement.ExpiresAtUtc);
        Assert.Equal(0, replacement.FailedAttempts);
        Assert.Null(replacement.LockedUntilUtc);
        Assert.False(replacement.RequiresRecovery);
        Assert.True(await DevicePinVerifier.VerifyAsync(NewPin, replacement.PinVerifier!, PinPepper, default));
        Assert.False(await DevicePinVerifier.VerifyAsync(OldPin, replacement.PinVerifier!, PinPepper, default));
        Assert.True(OpaqueDeviceCredential.Matches(result.DeviceCredential.Value, replacement.CredentialIdHash));
        Assert.True(OpaqueDeviceCredential.Matches(result.DeviceSessionCredential.Value,
            replacementSession.SessionIdentifierHash));
        Assert.DoesNotContain(result.DeviceCredential.Value,
            await verify.AuthorizedDevices.Select(x => x.CredentialIdHash).ToListAsync());
        Assert.DoesNotContain(result.DeviceSessionCredential.Value,
            await verify.DeviceSessions.Select(x => x.SessionIdentifierHash).ToListAsync());
        Assert.DoesNotContain(NewPin, replacement.PinVerifier!, StringComparison.Ordinal);
        var challenge = await verify.EmailAuthChallenges.SingleAsync(x => x.Id == seeded.ChallengeId);
        Assert.Equal(Now, challenge.ConsumedAtUtc);
        Assert.DoesNotContain(Code, challenge.CodeHash, StringComparison.Ordinal);
        var idempotency = Assert.Single(await verify.IdempotencyRecords.Where(x => x.ActorId == seeded.UserId
            && x.OperationType == "DevicePinRecovery").ToListAsync());
        var audit = Assert.Single(await verify.AuditEvents.Where(x => x.ActorId == seeded.UserId
            && x.EventType == "DevicePinRecovered").ToListAsync());
        Assert.Contains("request=recover-request", audit.Detail, StringComparison.Ordinal);
        foreach (var secret in new[] { Code, NewPin, result.DeviceCredential.Value, result.DeviceSessionCredential.Value })
        {
            Assert.DoesNotContain(secret, audit.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, idempotency.RequestFingerprint, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, idempotency.ResultReference, StringComparison.Ordinal);
        }

        Assert.True(await verify.IdentityBindings.AnyAsync(x => x.Id == seeded.Identity.IdentityBindingId
            && x.UserId == seeded.UserId && x.ExternalSubject == seeded.FirebaseUid && x.IsActive));
        Assert.False((await verify.AuthIdentifiers.SingleAsync(x => x.UserId == seeded.UserId
            && x.Kind == "Phone")).IsVerified);
        Assert.Equal(2, await verify.CommercePermissions.CountAsync(x => x.UserId == seeded.UserId && x.IsActive));
        Assert.Equal(2, await verify.PublicWorkspaceProfiles.CountAsync(x =>
            x.SubjectId == seeded.CustomerId || x.SubjectId == seeded.CreatorId));
        Assert.Equal(seeded.Cashback, (await verify.CustomerCashbackAccounts
            .SingleAsync(x => x.CustomerId == seeded.CustomerId)).AvailableCashback);
    }

    [Theory]
    [InlineData("unknown-device")]
    [InlineData("wrong-user")]
    [InlineData("wrong-binding")]
    [InlineData("non-firebase")]
    [InlineData("wrong-project")]
    public async Task Firebase_account_and_current_recognized_device_are_both_required(string defect)
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database,
            provider: defect == "non-firebase" ? "Other" : "Firebase",
            projectId: defect == "wrong-project" ? "another-firebase-project" : "isolated-v3-test");
        var request = Request(seeded) with
        {
            DeviceCredential = defect == "unknown-device" ? new string('A', 64) : seeded.DeviceCredential.Value,
            Identity = defect switch
            {
                "wrong-user" => seeded.Identity with { UserId = Guid.NewGuid() },
                "wrong-binding" => seeded.Identity with { IdentityBindingId = Guid.NewGuid() },
                _ => seeded.Identity
            }
        };

        await using var db = database.Open();
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => Service(db).CompleteAsync(request));
        Assert.Equal(FailureKind.Forbidden, failure.Kind);
        await AssertOriginalStateAsync(database, seeded);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    public async Task Expired_or_revoked_device_cannot_use_email_proof_to_recover(string state)
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, expiredDevice: state == "expired");
        await using (var db = database.Open())
        {
            var device = await db.AuthorizedDevices.SingleAsync(x => x.Id == seeded.DeviceId);
            if (state == "revoked")
            {
                device.RevokedAtUtc = Now;
                device.Version++;
                await db.SaveChangesAsync();
            }
        }

        await using var recovery = database.Open();
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() =>
            Service(recovery).CompleteAsync(Request(seeded)));
        Assert.Equal(FailureKind.Forbidden, failure.Kind);
        await using var verify = database.Open();
        Assert.Null((await verify.EmailAuthChallenges.SingleAsync(x => x.Id == seeded.ChallengeId)).ConsumedAtUtc);
        Assert.Equal(seeded.OriginalDeviceCount,
            await verify.AuthorizedDevices.CountAsync(x => x.UserId == seeded.UserId));
    }

    [Fact]
    public async Task Identifier_and_challenge_must_belong_to_authenticated_account()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database);
        var otherEmail = $"other-{Guid.NewGuid():N}@example.test";
        await using (var db = database.Open())
        {
            db.AuthIdentifiers.Add(new AuthIdentifierRecord
            {
                UserId = Guid.NewGuid(), Kind = "Email",
                IdentifierHash = HashIdentifier(otherEmail),
                DeliveryAddress = otherEmail, IsVerified = true, CreatedAtUtc = Now
            });
            await db.SaveChangesAsync();
        }

        await using var recovery = database.Open();
        await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => Service(recovery)
            .CompleteAsync(Request(seeded) with { Identifier = otherEmail }));
        await AssertOriginalStateAsync(database, seeded);
    }

    [Fact]
    public async Task Wrong_code_increments_attempt_atomically_without_changing_device_or_session()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database);
        await using (var db = database.Open())
            await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => Service(db)
                .CompleteAsync(Request(seeded) with { Code = "999999" }));

        await using var verify = database.Open();
        Assert.Equal(1, (await verify.EmailAuthChallenges.SingleAsync(x => x.Id == seeded.ChallengeId)).AttemptCount);
        await AssertOriginalStateAsync(database, seeded, expectedAttempts: 1);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("consumed")]
    [InlineData("wrong-purpose")]
    [InlineData("unsent")]
    public async Task Invalid_challenge_state_fails_generically_without_reset(string state)
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, challengeState: state);
        await using var db = database.Open();
        var failure = await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => Service(db).CompleteAsync(Request(seeded)));
        Assert.Equal("The code is invalid or expired.", failure.Message);
        await AssertOriginalStateAsync(database, seeded,
            expectChallengeConsumed: state == "consumed");
    }

    [Fact]
    public async Task Challenge_attempt_cap_is_enforced_and_cannot_overflow()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, challengeAttempts: 4);
        await using (var db = database.Open())
            await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => Service(db)
                .CompleteAsync(Request(seeded) with { Code = "999999" }));
        await using (var db = database.Open())
            await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => Service(db)
                .CompleteAsync(Request(seeded)));

        await using var verify = database.Open();
        Assert.Equal(5, (await verify.EmailAuthChallenges.SingleAsync(x => x.Id == seeded.ChallengeId)).AttemptCount);
        await AssertOriginalStateAsync(database, seeded, expectedAttempts: 5);
    }

    [Theory]
    [InlineData("1234", "1234")]
    [InlineData("123456", "123456")]
    [InlineData("１２３４５", "１２３４５")]
    [InlineData("12a45", "12a45")]
    [InlineData("12345", "54321")]
    public async Task Replacement_pin_requires_matching_five_ascii_digits(string pin, string confirmation)
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database);
        await using var db = database.Open();
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => Service(db)
            .CompleteAsync(Request(seeded) with { NewPin = pin, ConfirmPin = confirmation }));
        Assert.Equal(FailureKind.Validation, failure.Kind);
        await AssertOriginalStateAsync(database, seeded);
    }

    [Theory]
    [InlineData("pin-pepper")]
    [InlineData("code-key")]
    [InlineData("firebase-project")]
    public async Task Missing_security_configuration_fails_closed(string missing)
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database);
        await using var db = database.Open();
        var options = new RuntimeOptions
        {
            PinPepper = missing == "pin-pepper" ? null : PinPepper,
            AuthCodeHashKey = missing == "code-key" ? null : CodeKey,
            FirebaseProjectId = missing == "firebase-project" ? "" : "isolated-v3-test"
        };
        await Assert.ThrowsAsync<DevicePinRecoveryUnavailableException>(() =>
            new DevicePinRecoveryService(db, options, new ManualClock(Now)).CompleteAsync(Request(seeded)));
        await AssertOriginalStateAsync(database, seeded);
    }

    [Fact]
    public async Task Concurrent_completion_has_exactly_one_winner_and_one_replacement_pair()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database);
        async Task<bool> Attempt(string reference)
        {
            await using var db = database.Open();
            try
            {
                await Service(db).CompleteAsync(Request(seeded) with { IdempotencyKey = reference });
                return true;
            }
            catch (AuthChallengeInvalidException) { return false; }
            catch (ApplicationFailure failure) when (failure.Kind is FailureKind.ConcurrencyConflict or FailureKind.Forbidden)
            { return false; }
        }

        var outcomes = await Task.WhenAll(Attempt("concurrent-a"), Attempt("concurrent-b"));
        Assert.Single(outcomes, x => x);
        await using var verify = database.Open();
        Assert.Single(await verify.AuthorizedDevices.Where(x => x.UserId == seeded.UserId && x.RevokedAtUtc == null).ToListAsync());
        Assert.Single(await verify.DeviceSessions.Where(x => x.UserId == seeded.UserId && x.RevokedAtUtc == null).ToListAsync());
        Assert.NotNull((await verify.EmailAuthChallenges.SingleAsync(x => x.Id == seeded.ChallengeId)).ConsumedAtUtc);
    }

    [Fact]
    public async Task Transaction_failure_rolls_back_challenge_and_all_credential_state()
    {
        var database = await fixture.CreateAsync();
        var seeded = await SeedAsync(database, additionalDeviceAndSession: true);
        await using (var db = database.Open())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION v3.reject_pin_recovery_audit() RETURNS trigger AS $$
                BEGIN
                    IF NEW."EventType" = 'DevicePinRecovered' THEN
                        RAISE EXCEPTION 'isolated recovery rollback test';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER reject_pin_recovery_audit
                BEFORE INSERT ON v3."AuditEvents"
                FOR EACH ROW EXECUTE FUNCTION v3.reject_pin_recovery_audit();
                """);
        }

        await using (var db = database.Open())
            await Assert.ThrowsAnyAsync<Exception>(() => Service(db).CompleteAsync(Request(seeded)));

        await AssertOriginalStateAsync(database, seeded);
        await using var verify = database.Open();
        Assert.Equal(seeded.OriginalDeviceCount,
            await verify.AuthorizedDevices.CountAsync(x => x.UserId == seeded.UserId));
        Assert.Equal(seeded.OriginalSessionCount,
            await verify.DeviceSessions.CountAsync(x => x.UserId == seeded.UserId));
        Assert.Empty(await verify.IdempotencyRecords.Where(x => x.ActorId == seeded.UserId
            && x.OperationType == "DevicePinRecovery").ToListAsync());
    }

    private static DevicePinRecoveryService Service(Weymela.Infrastructure.Persistence.WeymelaDbContext db) =>
        new(db, new RuntimeOptions
        {
            PinPepper = PinPepper,
            AuthCodeHashKey = CodeKey,
            FirebaseProjectId = "isolated-v3-test"
        }, new ManualClock(Now));

    private static DevicePinRecoveryRequest Request(Seeded seeded) => new(
        seeded.Identity, seeded.DeviceCredential.Value, seeded.Identifier, Code,
        NewPin, NewPin, "recover-request");

    private static async Task<Seeded> SeedAsync(
        TestDatabase database,
        bool usePhoneIdentifier = false,
        bool additionalDeviceAndSession = false,
        int failedAttempts = 0,
        int challengeAttempts = 0,
        string? challengeState = null,
        string provider = "Firebase",
        string projectId = "isolated-v3-test",
        bool expiredDevice = false)
    {
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        var phone = "+2519" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString();
        var emailHash = HashIdentifier(email);
        var phoneHash = HashIdentifier(phone);
        var identifier = usePhoneIdentifier ? phone : email;
        var identifierHash = usePhoneIdentifier ? phoneHash : emailHash;
        var firebaseUid = Guid.NewGuid().ToString("N");
        var binding = new IdentityBinding
        {
            UserId = userId, Provider = provider, ProjectId = projectId,
            ExternalSubject = firebaseUid, IsActive = true, ValidAfterUtc = Now, Version = 4
        };
        var oldVerifier = await DevicePinVerifier.HashAsync(OldPin, PinPepper, default);
        var deviceCredential = OpaqueDeviceCredential.Create();
        var device = DeviceAccessPolicy.NewDevice(userId, deviceCredential.Digest, oldVerifier,
            Now.AddDays(expiredDevice ? -30 : -1));
        device.LastUsedAtUtc = Now.AddMinutes(-30);
        device.FailedAttempts = failedAttempts;
        device.RequiresRecovery = failedAttempts == DeviceAccessPolicy.RecoveryAttemptThreshold;
        var sessionCredential = OpaqueDeviceCredential.Create();
        var session = DeviceAccessPolicy.NewSession(userId, binding.Id, binding.Version,
            device.Id, sessionCredential.Digest, Now.AddMinutes(-30));
        session.LockedAtUtc = Now.AddMinutes(-10);
        var challenge = new EmailAuthChallengeRecord
        {
            UserId = userId,
            IdentifierHash = identifierHash,
            EmailIdentifierHash = emailHash,
            PhoneIdentifierHash = usePhoneIdentifier ? phoneHash : null,
            Purpose = challengeState == "wrong-purpose"
                ? EmailCodePurpose.DeviceEnrollment.ToString()
                : EmailCodePurpose.PinRecovery.ToString(),
            CodeHash = AuthCodeHashing.Hash(Code, CodeKey),
            CreatedAtUtc = Now.AddMinutes(-2),
            ExpiresAtUtc = challengeState == "expired" ? Now : Now.AddMinutes(8),
            LastSentAtUtc = challengeState == "unsent" ? null : Now.AddMinutes(-2),
            ConsumedAtUtc = challengeState == "consumed" ? Now.AddMinutes(-1) : null,
            AttemptCount = challengeAttempts,
            MaxAttempts = 5
        };
        var customerProfile = new PublicWorkspaceProfile
        {
            SubjectId = customerId, Role = ActorRole.Customer,
            DisplayName = "Recovery Customer", PublicId = "recovery-customer"
        };
        var creatorProfile = new PublicWorkspaceProfile
        {
            SubjectId = creatorId, Role = ActorRole.Creator,
            DisplayName = "Recovery Creator", PublicId = "recovery-creator"
        };
        var cashback = Money.Zero("ETB");
        var cashbackAccount = new CustomerCashbackAccount(customerId);

        await using var db = database.Open();
        db.AddRange(binding,
            new AuthIdentifierRecord { UserId = userId, Kind = "Email", IdentifierHash = emailHash,
                DeliveryAddress = email, IsVerified = true, CreatedAtUtc = Now.AddDays(-2) },
            new AuthIdentifierRecord { UserId = userId, Kind = "Phone", IdentifierHash = phoneHash,
                IsVerified = false, CreatedAtUtc = Now.AddDays(-2) },
            device, session, challenge, customerProfile, creatorProfile,
            new CommercePermission(userId, ActorRole.Customer, customerId, null, true, false),
            new CommercePermission(userId, ActorRole.Creator, creatorId, null, true, false),
            cashbackAccount);

        var originalDevices = 1;
        var originalSessions = 1;
        if (additionalDeviceAndSession)
        {
            var otherCredential = OpaqueDeviceCredential.Create();
            var otherDevice = DeviceAccessPolicy.NewDevice(userId, otherCredential.Digest,
                oldVerifier, Now.AddDays(-2));
            var otherSession = DeviceAccessPolicy.NewSession(userId, binding.Id, binding.Version,
                otherDevice.Id, OpaqueDeviceCredential.Create().Digest, Now.AddMinutes(-20));
            db.AddRange(otherDevice, otherSession);
            originalDevices++;
            originalSessions++;
        }
        await db.SaveChangesAsync();
        return new(userId, customerId, creatorId, cashback, firebaseUid,
            new DeviceSessionIdentity(userId, binding.Id, binding.Version), device.Id, session.Id,
            challenge.Id, identifier, deviceCredential, originalDevices, originalSessions);
    }

    private static async Task AssertOriginalStateAsync(
        TestDatabase database, Seeded seeded, int expectedAttempts = 0,
        bool expectChallengeConsumed = false)
    {
        await using var verify = database.Open();
        var device = await verify.AuthorizedDevices.SingleAsync(x => x.Id == seeded.DeviceId);
        var session = await verify.DeviceSessions.SingleAsync(x => x.Id == seeded.SessionId);
        var challenge = await verify.EmailAuthChallenges.SingleAsync(x => x.Id == seeded.ChallengeId);
        Assert.Null(device.RevokedAtUtc);
        Assert.Null(session.RevokedAtUtc);
        if (expectChallengeConsumed) Assert.NotNull(challenge.ConsumedAtUtc);
        else Assert.Null(challenge.ConsumedAtUtc);
        Assert.Equal(expectedAttempts, challenge.AttemptCount);
        Assert.Equal(seeded.OriginalDeviceCount,
            await verify.AuthorizedDevices.CountAsync(x => x.UserId == seeded.UserId));
        Assert.Equal(seeded.OriginalSessionCount,
            await verify.DeviceSessions.CountAsync(x => x.UserId == seeded.UserId));
    }

    private static string HashIdentifier(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record Seeded(
        Guid UserId,
        Guid CustomerId,
        Guid CreatorId,
        Money Cashback,
        string FirebaseUid,
        DeviceSessionIdentity Identity,
        Guid DeviceId,
        Guid SessionId,
        Guid ChallengeId,
        string Identifier,
        OpaqueDeviceCredential DeviceCredential,
        int OriginalDeviceCount,
        int OriginalSessionCount);

    private sealed class ManualClock(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(value);
    }
}
