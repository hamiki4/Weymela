using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
public sealed class EmailAuthServiceTests(PostgresFixture fixture)
{
    private static RuntimeOptions Options() => new() { FirebaseProjectId = "isolated-v3-test", AuthCodeHashKey = "test-only-key" };
    private static RuntimeOptions ProductionOptions() => new()
        { EnvironmentName = "Production", FirebaseProjectId = "isolated-v3-test", AuthCodeHashKey = "test-only-key" };

    [Theory]
    [InlineData("missing")]
    [InlineData("unapproved")]
    [InlineData("incomplete")]
    [InlineData("tampered")]
    [InlineData("approved")]
    public async Task Production_email_verification_succeeds_before_legal_publication_but_role_activation_requires_consent(string publication)
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var directory = Directory.CreateTempSubdirectory("isolated-registration-legal-");
        try
        {
            const string text = "Isolated policy fixture, never a Production document.\n";
            var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
            var documents = new List<object>();
            foreach (var type in new[] { LegalDocumentType.TermsOfService, LegalDocumentType.PrivacyPolicy })
                foreach (var language in new[] { "en", "am" })
                {
                    var version = $"isolated-{type}-{language}";
                    db.LegalDocumentVersions.Add(new(Guid.NewGuid(), type, version, hash, DateTime.UtcNow.AddMinutes(-1)));
                    documents.Add(new { type = type.ToString(), language,
                        approvedFinalVersion = version, approvedFinalContentSha256 = hash });
                    if (publication != "incomplete" || language != "am")
                        await File.WriteAllTextAsync(Path.Combine(directory.FullName, $"{type}-{language}.txt"), text);
                }
            await db.SaveChangesAsync();
            if (publication != "missing")
                await File.WriteAllTextAsync(Path.Combine(directory.FullName, "publication.json"), JsonSerializer.Serialize(new
                {
                    environment = "Production", productionApproved = publication != "unapproved",
                    attorneyReviewed = publication != "unapproved", ownerApprovalReference = "isolated-test-owner",
                    legalApprovalReference = "isolated-test-legal", documents
                }));
            if (publication == "tampered")
                await File.AppendAllTextAsync(Path.Combine(directory.FullName, "PrivacyPolicy-am.txt"), "changed");
            var delivery = new TestDelivery(); var issuer = new TestIssuer();
            var service = new EmailAuthService(db, delivery, issuer, ProductionOptions(), TimeProvider.System);
            await service.StartAsync("new@example.test", null, EmailCodePurpose.Signup, default);
            var code = delivery.Codes.Single().Code;
            var wrong = code == "99999" ? "99998" : "99999";
            await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync(
                "new@example.test", EmailCodePurpose.Signup, wrong, default));
            await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync(
                "different@example.test", EmailCodePurpose.Signup, code, default));
            var result = await service.VerifyAsync("new@example.test", EmailCodePurpose.Signup, code, default);
            Assert.Equal("custom-token", result.CustomToken);
            Assert.True(issuer.Called);
            Assert.True((await db.AuthIdentifiers.SingleAsync()).IsVerified);
            Assert.NotNull((await db.EmailAuthChallenges.SingleAsync()).ConsumedAtUtc);
            await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync(
                "new@example.test", EmailCodePurpose.Signup, code, default));
            // Email verification grants no role or consent; role activation still requires acceptance.
            Assert.Empty(await db.CommercePermissions.ToListAsync());
            Assert.Empty(await db.LegalAcceptances.ToListAsync());
            Assert.Empty(await db.FinancialJournals.ToListAsync());
            var admin = Guid.NewGuid();
            db.CommercePermissions.Add(new(admin, ActorRole.PlatformAdmin, admin, null, true, false));
            db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = admin, Kind = "Email",
                IdentifierHash = HashIdentifier("bootstrap-admin@example.test"), DeliveryAddress = "bootstrap-admin@example.test",
                IsVerified = true, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            var enrollment = new RoleEnrollmentService(db, TimeProvider.System, ProductionOptions(),
                new AccountLegalDocumentSource(directory.FullName));
            var request = new RoleEnrollmentRequest(ActorRole.Customer, "Isolated owner", null, null, null, null,
                RegisteredPhone: "+251900000000");
            var actor = new Actor(admin, ActorRole.PlatformAdmin);
            if (publication != "approved")
            {
                var rejected = await Assert.ThrowsAsync<ApplicationFailure>(() => enrollment.SubmitAsync(actor, request, "unpublished", default));
                Assert.Equal("ApprovedLegalUnavailable", rejected.Code);
                Assert.Equal("Account activation requires published Terms of Service and Privacy Policy.", rejected.Message);
            }
            else
            {
                var rejected = await Assert.ThrowsAsync<ApplicationFailure>(() => enrollment.SubmitAsync(actor, request, "unaccepted", default));
                Assert.Equal("Accept the current Terms of Service and Privacy Policy.", rejected.Message);
                db.ChangeTracker.Clear();
                var status = await new AccountLegalOnboardingService(db, TimeProvider.System).StatusAsync(admin, default);
                var terms = status.Documents.Single(x => x.Kind == "TermsOfService");
                var privacy = status.Documents.Single(x => x.Kind == "PrivacyPolicy");
                await enrollment.SubmitAsync(actor, request with { AccountLegal = new(
                    new(terms.DocumentId, terms.ContentHash, true), new(privacy.DocumentId, privacy.ContentHash, true)) },
                    "accepted", default);
                Assert.Equal(2, await db.LegalAcceptances.CountAsync(x => x.UserId == admin));
            }
            Assert.Equal(publication == "approved" ? 2 : 1, await db.CommercePermissions.CountAsync());
            Assert.Empty(await db.FinancialJournals.ToListAsync());
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task Production_expired_signup_code_is_rejected_without_legal_documents()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var clock = new ManualClock(DateTime.UtcNow);
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var service = new EmailAuthService(db, delivery, issuer, ProductionOptions(), clock);
        await service.StartAsync("expiry@example.test", null, EmailCodePurpose.Signup, default);
        clock.Advance(TimeSpan.FromMinutes(11));
        await Assert.ThrowsAsync<AuthChallengeExpiredException>(() => service.VerifyAsync(
            "expiry@example.test", EmailCodePurpose.Signup, delivery.Codes.Single().Code, default));
        Assert.False(issuer.Called);
        Assert.Empty(await db.AuthIdentifiers.ToListAsync());
        Assert.Empty(await db.CommercePermissions.ToListAsync());
    }

    [Fact]
    public async Task Production_existing_verified_account_can_sign_in_without_published_policies()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var userId = Guid.NewGuid();
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = userId, Kind = "Email",
            IdentifierHash = HashIdentifier("owner@example.test"), DeliveryAddress = "owner@example.test",
            IsVerified = true, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var service = new EmailAuthService(db, delivery, issuer, ProductionOptions(), TimeProvider.System);
        await service.StartAsync("owner@example.test", null, EmailCodePurpose.Signup, default);
        Assert.Equal(EmailCodePurpose.DeviceEnrollment, delivery.Codes.Single().Purpose);
        await service.VerifyAsync("owner@example.test", EmailCodePurpose.Signup, delivery.Codes.Single().Code, default);
        Assert.Equal(userId, Assert.Single(issuer.IssuedUserIds));
        Assert.Single(await db.AuthIdentifiers.ToListAsync());
        Assert.Empty(await db.LegalAcceptances.ToListAsync());
    }

    [Theory]
    [InlineData("", "EmailRequired", "Email address is required.")]
    [InlineData("owner@example", "InvalidEmail", "Enter a valid email address.")]
    [InlineData("owner@.com", "InvalidEmail", "Enter a valid email address.")]
    public async Task Invalid_email_is_rejected_before_delivery_with_structured_failure(
        string email, string code, string message)
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var delivery = new TestDelivery();
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => new EmailAuthService(
            db, delivery, new TestIssuer(), Options(), TimeProvider.System)
            .StartAsync(email, null, EmailCodePurpose.Signup, default));
        Assert.Equal(code, failure.Code);
        Assert.Equal(message, failure.Message);
        Assert.Empty(delivery.Codes);
    }

    [Fact]
    public async Task Email_only_signup_verifies_once_without_creating_a_phone_alias()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var service = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);

        var started = await service.StartAsync("Owner@Example.com", null, EmailCodePurpose.Signup, default);
        Assert.True(started.Accepted); Assert.Single(delivery.Codes); Assert.False(issuer.Called);
        Assert.Equal(EmailCodePurpose.Signup, delivery.Codes[0].Purpose);
        Assert.Matches("^[0-9]{5}$", delivery.Codes[0].Code);
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
    public async Task Create_account_for_existing_email_uses_sign_in_delivery_and_preserves_identity()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var service = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);
        await service.StartAsync("Owner@Example.com", null, EmailCodePurpose.Signup, default);
        await service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, delivery.Codes[^1].Code, default);
        var originalUserId = Assert.Single(issuer.IssuedUserIds);
        var customerId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfileRecord { CustomerId = customerId,
            UserId = originalUserId, PreferredName = "Existing customer",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var started = await service.StartAsync(" OWNER@example.com ", null, EmailCodePurpose.Signup, default);
        Assert.True(started.Accepted);
        Assert.Equal(EmailCodePurpose.DeviceEnrollment, delivery.Codes[^1].Purpose);
        Assert.Equal("owner@example.com", delivery.Codes[^1].Destination);
        var challenge = await db.EmailAuthChallenges.OrderByDescending(x => x.CreatedAtUtc).FirstAsync();
        Assert.Equal(EmailCodePurpose.DeviceEnrollment.ToString(), challenge.Purpose);
        await service.VerifyAsync(" Owner@Example.com ", EmailCodePurpose.Signup, delivery.Codes[^1].Code, default);

        Assert.Equal([originalUserId, originalUserId], issuer.IssuedUserIds);
        Assert.Single(await db.AuthIdentifiers.Where(x => x.Kind == "Email").ToListAsync());
        Assert.Equal(customerId, Assert.Single(await db.CustomerProfiles.ToListAsync()).CustomerId);
        Assert.Empty(await db.RoleEnrollments.ToListAsync());
    }

    [Fact]
    public async Task Existing_account_resend_obeys_the_same_sixty_second_limit()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var clock = new ManualClock(DateTime.UtcNow);
        var service = new EmailAuthService(db, delivery, new TestIssuer(), Options(), clock);
        await service.StartAsync("owner@example.com", null, EmailCodePurpose.Signup, default);
        await service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, delivery.Codes[^1].Code, default);
        await service.StartAsync("owner@example.com", null, EmailCodePurpose.Signup, default);
        var firstSignInCode = delivery.Codes[^1].Code;
        clock.Advance(TimeSpan.FromSeconds(30));
        var limited = await service.StartAsync(" OWNER@EXAMPLE.COM ", null, EmailCodePurpose.Signup, default);
        Assert.InRange(limited.ResendAfterSeconds, 29, 31);
        Assert.Equal(2, delivery.Codes.Count);
        clock.Advance(TimeSpan.FromSeconds(31));
        await service.StartAsync("owner@example.com", null, EmailCodePurpose.Signup, default);
        Assert.Equal(3, delivery.Codes.Count);
        Assert.Equal(EmailCodePurpose.DeviceEnrollment, delivery.Codes[^1].Purpose);
        Assert.NotEqual(firstSignInCode, delivery.Codes[^1].Code);
        await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync(
            "owner@example.com", EmailCodePurpose.Signup, firstSignInCode, default));
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
    public async Task Concurrent_signup_verifications_cannot_create_duplicate_email_identities()
    {
        var database = await fixture.CreateAsync();
        var delivery = new TestDelivery();
        await using (var starterDb = database.Open())
            await new EmailAuthService(starterDb, delivery, new TestIssuer(), Options(), TimeProvider.System)
                .StartAsync("owner@example.com", null, EmailCodePurpose.Signup, default);
        var code = delivery.Codes.Single().Code;

        async Task<bool> Verify()
        {
            await using var db = database.Open();
            try
            {
                await new EmailAuthService(db, delivery, new TestIssuer(), Options(), TimeProvider.System)
                    .VerifyAsync(" OWNER@EXAMPLE.COM ", EmailCodePurpose.Signup, code, default);
                return true;
            }
            catch (Exception exception) when (exception is AuthChallengeInvalidException
                or DbUpdateException or Npgsql.NpgsqlException
                || exception is InvalidOperationException
                    && exception.InnerException?.InnerException is Npgsql.PostgresException { SqlState: "40001" })
            { return false; }
        }
        var results = await Task.WhenAll(Verify(), Verify());
        Assert.Single(results, x => x);
        await using var check = database.Open();
        Assert.Single(await check.AuthIdentifiers.Where(x => x.Kind == "Email").ToListAsync());
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
        await Assert.ThrowsAsync<AuthChallengeExpiredException>(() => service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, code, default));
    }

    [Fact]
    public async Task Repeated_wrong_codes_hit_attempt_limit_without_revealing_or_consuming_account()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var service = new EmailAuthService(db, delivery, new TestIssuer(), Options(), TimeProvider.System);
        await service.StartAsync("owner@example.com", null, EmailCodePurpose.Signup, default);
        for (var i = 0; i < 5; i++) await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, "00000", default));
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
    [InlineData("0911111111", "+251911111111", "ET")]
    [InlineData("911111111", "+251911111111", "ET")]
    [InlineData("+251 911-111-111", "+251911111111", "ET")]
    [InlineData("+1 (202) 555-0123", "+12025550123", "ET")]
    [InlineData("+1.404.555.0123", "+14045550123", "ET")]
    [InlineData("0712 345 678", "+254712345678", "KE")]
    [InlineData("020 7946 0958", "+442079460958", "GB")]
    public void Phones_normalize_before_hashing_and_lookup(string input, string canonical, string region)
        => Assert.Equal(canonical, PhoneNumberNormalizer.Normalize(input, region));

    [Theory]
    [InlineData("091111111")]
    [InlineData("91111111")]
    [InlineData("+25191111111")]
    [InlineData("091111111A")]
    public void Malformed_phones_are_rejected(string input)
        => Assert.Throws<ApplicationFailure>(() => PhoneNumberNormalizer.Normalize(input));

    [Fact]
    public async Task Signup_telemetry_distinguishes_new_identity_and_same_identity_continuation()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var service = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);
        var createdBefore = OperationalTelemetry.SignupChallengeCreated.Value;
        var acceptedBefore = OperationalTelemetry.SignupDeliveryAccepted.Value;
        var continuationBefore = OperationalTelemetry.SignupContinuation.Value;

        await service.StartAsync("telemetry@example.test", null, EmailCodePurpose.Signup, default);
        await service.VerifyAsync("telemetry@example.test", EmailCodePurpose.Signup, delivery.Codes[0].Code, default);
        var originalUser = issuer.IssuedUserIds.Single();
        var repeated = await service.StartAsync("telemetry@example.test", null, EmailCodePurpose.Signup, default);
        await service.VerifyAsync("telemetry@example.test", EmailCodePurpose.Signup, delivery.Codes[1].Code, default);

        Assert.True(repeated.Accepted);
        Assert.Equal(createdBefore + 2, OperationalTelemetry.SignupChallengeCreated.Value);
        Assert.Equal(acceptedBefore + 2, OperationalTelemetry.SignupDeliveryAccepted.Value);
        Assert.Equal(continuationBefore + 1, OperationalTelemetry.SignupContinuation.Value);
        Assert.Equal(2, delivery.Codes.Count);
        Assert.Equal(EmailCodePurpose.DeviceEnrollment, delivery.Codes[^1].Purpose);
        Assert.All(issuer.IssuedUserIds, issued => Assert.Equal(originalUser, issued));
        Assert.Single(await db.AuthIdentifiers.Where(x => x.Kind == "Email").ToListAsync());
    }

    [Fact]
    public async Task Password_recovery_resumes_verified_identity_when_security_setup_is_incomplete()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var delivery = new TestDelivery(); var issuer = new TestIssuer();
        var service = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);
        await service.StartAsync("resume@example.test", null, EmailCodePurpose.Signup, default);
        await service.VerifyAsync("resume@example.test", EmailCodePurpose.Signup, delivery.Codes[^1].Code, default);
        var originalUser = issuer.IssuedUserIds[^1];

        await service.StartAsync("resume@example.test", null, EmailCodePurpose.PasswordRecovery, default);
        var result = await service.VerifyPasswordRecoveryAsync(
            "resume@example.test", delivery.Codes[^1].Code, default);

        Assert.Equal(PasswordRecoveryNextStep.AccountSetup, result.NextStep);
        Assert.Null(result.RecoveryGrant);
        Assert.NotNull(result.AccountSetupToken);
        Assert.Equal(originalUser, issuer.IssuedUserIds[^1]);
        Assert.Single(await db.AuthIdentifiers.ToListAsync());
        Assert.Empty(await db.PasswordCredentials.ToListAsync());
        var challenge = await db.EmailAuthChallenges.OrderByDescending(x => x.CreatedAtUtc).FirstAsync();
        Assert.NotNull(challenge.ConsumedAtUtc);
        Assert.Null(challenge.RecoveryGrantHash);
    }

    [Fact]
    public async Task Signup_provider_failure_has_safe_telemetry_and_does_not_persist_challenge()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var failedBefore = OperationalTelemetry.SignupDeliveryFailed.Value;
        var providerBefore = OperationalTelemetry.ProviderErrors.Value;
        var service = new EmailAuthService(db, new FailingDelivery(), new TestIssuer(), Options(), TimeProvider.System);

        var failure = await Assert.ThrowsAsync<EmailDeliveryFailedException>(() => service.StartAsync(
            "provider-failure@example.test", null, EmailCodePurpose.Signup, default));
        Assert.Equal("Unable to send verification email. Please try again.", failure.Message);
        db.ChangeTracker.Clear();

        Assert.Equal(failedBefore + 1, OperationalTelemetry.SignupDeliveryFailed.Value);
        Assert.Equal(providerBefore + 1, OperationalTelemetry.ProviderErrors.Value);
        Assert.Empty(await db.EmailAuthChallenges.ToListAsync());
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

    private sealed class FailingDelivery : IEmailCodeDelivery
    {
        public bool Enabled => true;
        public Task SendAsync(string destination, string code, EmailCodePurpose purpose, CancellationToken ct)
            => throw new InvalidOperationException("test provider failure");
    }

    private sealed class ManualClock(DateTime initial) : TimeProvider
    {
        private DateTime utc = DateTime.SpecifyKind(initial, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(utc);
        public void Advance(TimeSpan amount) => utc = utc.Add(amount);
    }
    private static string HashIdentifier(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
