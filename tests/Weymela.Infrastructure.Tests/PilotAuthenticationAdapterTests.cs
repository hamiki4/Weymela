using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class PilotAuthenticationAdapterTests(PostgresFixture fixture)
{
    private const string ApiKey = "re_test_only_not_a_live_resend_key_123456";
    private static readonly string HashKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());

    private static RuntimeOptions Options() => new()
    {
        EmailDeliveryMode = "Resend",
        ResendApiKey = ApiKey,
        ResendFromAddress = "no-reply@pilot-mail.weymela.com",
        ResendFromName = "Weymela Pilot",
        AuthCodeHashKey = HashKey,
        FirebaseProjectId = "weymela-pilot"
    };

    [Theory]
    [InlineData(EmailCodePurpose.Signup, "Your Weymela verification code")]
    [InlineData(EmailCodePurpose.DeviceEnrollment, "Your Weymela sign-in code")]
    [InlineData(EmailCodePurpose.PinRecovery, "Your Weymela recovery code")]
    [InlineData(EmailCodePurpose.PasswordRecovery, "Your Weymela password reset code")]
    public async Task Resend_uses_fixed_https_endpoint_and_purpose_bound_messages(
        EmailCodePurpose purpose, string expectedSubject)
    {
        var handler = new RecordingHandler(_ => Success());
        using var delivery = new ResendEmailCodeDelivery(Options(), handler);

        await delivery.SendAsync("registered@example.com", "123456", purpose, default);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(ResendEmailCodeDelivery.Endpoint, request.Uri);
        Assert.Equal(Uri.UriSchemeHttps, request.Uri.Scheme);
        Assert.True(request.HasBearerAuthorization);
        Assert.StartsWith("weymela-", request.IdempotencyKey, StringComparison.Ordinal);
        using var payload = JsonDocument.Parse(request.Body);
        Assert.Equal("Weymela Pilot <no-reply@pilot-mail.weymela.com>", payload.RootElement.GetProperty("from").GetString());
        Assert.Equal("registered@example.com", payload.RootElement.GetProperty("to")[0].GetString());
        Assert.Equal(expectedSubject, payload.RootElement.GetProperty("subject").GetString());
        var body = payload.RootElement.GetProperty("text").GetString()!;
        Assert.Contains("123456", body, StringComparison.Ordinal);
        Assert.Contains("expires in 10 minutes", body, StringComparison.Ordinal);
        Assert.DoesNotContain("http", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ApiKey, request.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("timeout")]
    [InlineData("malformed")]
    public async Task Resend_failures_are_generic_and_do_not_expose_code_or_key(string defect)
    {
        var handler = new RecordingHandler(_ => defect switch
        {
            "provider" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "malformed" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") },
            _ => throw new TaskCanceledException("synthetic timeout")
        });
        using var delivery = new ResendEmailCodeDelivery(Options(), handler);

        var error = await Assert.ThrowsAsync<AuthChallengeUnavailableException>(
            () => delivery.SendAsync("registered@example.com", "654321", EmailCodePurpose.Signup, default));

        Assert.Equal("Email delivery is temporarily unavailable.", error.Message);
        Assert.DoesNotContain("654321", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resend_failure_rolls_back_new_challenge()
    {
        var database = await fixture.CreateAsync();
        await using (var db = database.Open())
        {
            using var delivery = new ResendEmailCodeDelivery(Options(),
                new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
            var service = new EmailAuthService(db, delivery, new FakeIssuer(), Options(), TimeProvider.System);
            await Assert.ThrowsAsync<AuthChallengeUnavailableException>(() => service.StartAsync(
                "owner@example.com", null, EmailCodePurpose.Signup, default));
        }
        await using var verification = database.Open();
        Assert.Empty(await verification.EmailAuthChallenges.AsNoTracking().ToListAsync());
        Assert.Empty(await verification.AuthIdentifiers.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Email_challenges_reject_phone_alias_input_and_deliver_only_to_verified_email()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var handler = new RecordingHandler(_ => Success());
        using var delivery = new ResendEmailCodeDelivery(Options(), handler);
        var service = new EmailAuthService(db, delivery, new FakeIssuer(), Options(), TimeProvider.System);

        await service.StartAsync("owner@example.com", null, EmailCodePurpose.Signup, default);
        await service.VerifyAsync("owner@example.com", EmailCodePurpose.Signup, Code(handler.Requests[^1].Body), default);
        var user = (await db.AuthIdentifiers.SingleAsync(x => x.Kind == "Email")).UserId;
        var binding = new IdentityBinding { UserId = user, Provider = "Firebase", ProjectId = "weymela-pilot",
            ExternalSubject = "pilot-test-user", IsActive = true, Version = 1, ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1) };
        db.IdentityBindings.Add(binding); await db.SaveChangesAsync();
        await new PhoneAliasService(db, Options(), TimeProvider.System).RegisterAsync(
            new DeviceSessionIdentity(user, binding.Id, binding.Version), "+251900000000", default);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.StartAsync(
            "+251900000000", null, EmailCodePurpose.DeviceEnrollment, default));
        Assert.Single(handler.Requests);

        await service.StartAsync("owner@example.com", null, EmailCodePurpose.DeviceEnrollment, default);

        using var payload = JsonDocument.Parse(handler.Requests[^1].Body);
        Assert.Equal("owner@example.com", payload.RootElement.GetProperty("to")[0].GetString());
    }

    [Fact]
    public async Task Unknown_identifier_remains_generic_and_never_calls_Resend()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var handler = new RecordingHandler(_ => Success());
        using var delivery = new ResendEmailCodeDelivery(Options(), handler);
        var service = new EmailAuthService(db, delivery, new FakeIssuer(), Options(), TimeProvider.System);

        var result = await service.StartAsync("unknown@example.test", null, EmailCodePurpose.PinRecovery, default);

        Assert.False(result.Accepted);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Firebase_issuer_reuses_the_authoritative_existing_UID()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var userId = Guid.NewGuid();
        db.IdentityBindings.Add(new IdentityBinding
        {
            UserId = userId, Provider = "Firebase", ProjectId = "weymela-pilot",
            ExternalSubject = "existing-firebase-uid", IsActive = true,
            ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1), Version = 7
        });
        await db.SaveChangesAsync();
        var signer = new FakeSigner();
        var issuer = new FirebaseAdminCustomTokenIssuer(db, signer, Options(), TimeProvider.System);

        var result = await issuer.IssueAsync(userId, "weymela-pilot", default);

        Assert.Equal("test-custom-token", result.CustomToken);
        Assert.Equal("existing-firebase-uid", Assert.Single(signer.Uids));
        Assert.Single(await db.IdentityBindings.Where(x => x.UserId == userId).ToListAsync());
    }

    [Fact]
    public async Task Firebase_issuer_creates_one_stable_binding_for_initial_signup()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var userId = Guid.NewGuid();
        var signer = new FakeSigner();
        var issuedAt = new DateTimeOffset(2026, 9, 15, 18, 57, 57, 789, TimeSpan.Zero);
        var issuer = new FirebaseAdminCustomTokenIssuer(db, signer, Options(), new FixedClock(issuedAt));

        await issuer.IssueAsync(userId, "weymela-pilot", default);
        await db.SaveChangesAsync();
        await issuer.IssueAsync(userId, "weymela-pilot", default);

        var binding = Assert.Single(await db.IdentityBindings.Where(x => x.UserId == userId).ToListAsync());
        Assert.Equal(userId.ToString("N"), binding.ExternalSubject);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(issuedAt.ToUnixTimeSeconds()).UtcDateTime, binding.ValidAfterUtc);
        Assert.Equal(0, binding.ValidAfterUtc.Ticks % TimeSpan.TicksPerSecond);
        Assert.Equal([binding.ExternalSubject, binding.ExternalSubject], signer.Uids);
    }

    [Fact]
    public async Task Email_only_signup_and_continuation_reuse_one_verified_user_and_one_Firebase_binding()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var handler = new RecordingHandler(_ => Success());
        using var delivery = new ResendEmailCodeDelivery(Options(), handler);
        var signer = new FakeSigner();
        var issuer = new FirebaseAdminCustomTokenIssuer(db, signer, Options(), TimeProvider.System);
        var service = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);
        await service.StartAsync("new-owner@example.test", null, EmailCodePurpose.Signup, default);
        var code = Code(handler.Requests[^1].Body);
        await service.VerifyAsync("new-owner@example.test", EmailCodePurpose.Signup, code, default);
        await Assert.ThrowsAsync<AuthChallengeInvalidException>(() => service.VerifyAsync(
            "new-owner@example.test", EmailCodePurpose.Signup, code, default));
        await service.StartAsync("new-owner@example.test", null, EmailCodePurpose.Signup, default);
        await service.VerifyAsync("new-owner@example.test", EmailCodePurpose.Signup,
            Code(handler.Requests[^1].Body), default);
        var email = Assert.Single(await db.AuthIdentifiers.ToListAsync());
        Assert.Equal("Email", email.Kind); Assert.True(email.IsVerified);
        var binding = Assert.Single(await db.IdentityBindings.ToListAsync());
        Assert.Equal(email.UserId, binding.UserId);
        Assert.Equal([binding.ExternalSubject, binding.ExternalSubject], signer.Uids);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("inactive")]
    [InlineData("provider")]
    [InlineData("binding-project")]
    public async Task Firebase_issuer_rejects_project_or_binding_conflicts(string defect)
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var userId = Guid.NewGuid();
        if (defect != "project")
        {
            db.IdentityBindings.Add(new IdentityBinding
            {
                UserId = userId, Provider = defect == "provider" ? "Other" : "Firebase",
                ProjectId = defect == "binding-project" ? "other-project" : "weymela-pilot", ExternalSubject = "bound-uid",
                IsActive = defect != "inactive", ValidAfterUtc = DateTime.UtcNow, Version = 1
            });
            await db.SaveChangesAsync();
        }
        var signer = new FakeSigner();
        var issuer = new FirebaseAdminCustomTokenIssuer(db, signer, Options(), TimeProvider.System);

        await Assert.ThrowsAsync<AuthChallengeUnavailableException>(() =>
            issuer.IssueAsync(userId, defect == "project" ? "other-project" : "weymela-pilot", default));
        Assert.Empty(signer.Uids);
    }

    [Fact]
    public async Task Firebase_issuer_rejects_an_external_UID_already_bound_to_another_user()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var userId = Guid.NewGuid();
        db.IdentityBindings.Add(new IdentityBinding
        {
            UserId = Guid.NewGuid(), Provider = "Firebase", ProjectId = "weymela-pilot",
            ExternalSubject = userId.ToString("N"), IsActive = true,
            ValidAfterUtc = DateTime.UtcNow, Version = 1
        });
        await db.SaveChangesAsync();
        var signer = new FakeSigner();
        var issuer = new FirebaseAdminCustomTokenIssuer(db, signer, Options(), TimeProvider.System);

        await Assert.ThrowsAsync<AuthChallengeUnavailableException>(() =>
            issuer.IssueAsync(userId, "weymela-pilot", default));
        Assert.Empty(signer.Uids);
        Assert.DoesNotContain(db.ChangeTracker.Entries<IdentityBinding>(),
            entry => entry.Entity.UserId == userId && entry.State == EntityState.Added);
    }

    [Fact]
    public async Task Signing_failure_does_not_persist_a_new_binding()
    {
        var database = await fixture.CreateAsync();
        var userId = Guid.NewGuid();
        await using (var db = database.Open())
        {
            var issuer = new FirebaseAdminCustomTokenIssuer(db, new FakeSigner(fail: true), Options(), TimeProvider.System);
            await Assert.ThrowsAsync<AuthChallengeUnavailableException>(() => issuer.IssueAsync(userId, "weymela-pilot", default));
        }
        await using var verification = database.Open();
        Assert.Empty(await verification.IdentityBindings.Where(x => x.UserId == userId).ToListAsync());
    }

    [Fact]
    public async Task Signing_failure_rolls_back_challenge_consumption_and_signup_identity()
    {
        var database = await fixture.CreateAsync();
        await using (var db = database.Open())
        {
            var handler = new RecordingHandler(_ => Success());
            using var delivery = new ResendEmailCodeDelivery(Options(), handler);
            var issuer = new FirebaseAdminCustomTokenIssuer(db, new FakeSigner(fail: true), Options(), TimeProvider.System);
            var service = new EmailAuthService(db, delivery, issuer, Options(), TimeProvider.System);
            await service.StartAsync("owner@example.com", null, EmailCodePurpose.Signup, default);
            await Assert.ThrowsAsync<AuthChallengeUnavailableException>(() => service.VerifyAsync(
                "owner@example.com", EmailCodePurpose.Signup, Code(handler.Requests[^1].Body), default));
        }

        await using var verification = database.Open();
        var challenge = Assert.Single(await verification.EmailAuthChallenges.AsNoTracking().ToListAsync());
        Assert.Null(challenge.ConsumedAtUtc);
        Assert.Equal(0, challenge.AttemptCount);
        Assert.Empty(await verification.AuthIdentifiers.AsNoTracking().ToListAsync());
        Assert.Empty(await verification.IdentityBindings.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Firebase_Admin_signer_uses_validated_local_service_account_without_network()
    {
        var path = Path.Combine(Path.GetTempPath(), $"weymela-firebase-{Guid.NewGuid():N}.json");
        using var rsa = RSA.Create(2048);
        var credential = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = "weymela-pilot",
            ["private_key_id"] = "test-key-id",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "test-signer@weymela-pilot.iam.gserviceaccount.com",
            ["client_id"] = "1234567890",
            ["auth_uri"] = "https://accounts.google.com/o/oauth2/auth",
            ["token_uri"] = "https://oauth2.googleapis.com/token",
            ["auth_provider_x509_cert_url"] = "https://www.googleapis.com/oauth2/v1/certs",
            ["client_x509_cert_url"] = "https://www.googleapis.com/robot/v1/metadata/x509/test"
        });
        await File.WriteAllTextAsync(path, credential);
        try
        {
            using var signer = new FirebaseAdminTokenSigner(new RuntimeOptions
            {
                FirebaseProjectId = "weymela-pilot", FirebaseAdminCredentialsPath = path
            });
            var token = await signer.CreateCustomTokenAsync("authoritative-uid", default);
            Assert.Equal(3, token.Split('.').Length);
            var error = Assert.Throws<InvalidOperationException>(() => new FirebaseAdminTokenSigner(new RuntimeOptions
            {
                FirebaseProjectId = "other-project", FirebaseAdminCredentialsPath = path
            }));
            Assert.DoesNotContain("private_key", error.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("test-signer", error.ToString(), StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    private static HttpResponseMessage Success() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"id\":\"test-message-id\"}", Encoding.UTF8, "application/json")
    };

    private static string Code(string body) => Regex.Match(body, "[0-9]{6}").Value;

    private sealed record RecordedRequest(Uri Uri, bool HasBearerAuthorization, string IdempotencyKey, string Body);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new(
                request.RequestUri!,
                request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == ApiKey,
                request.Headers.GetValues("Idempotency-Key").Single(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return response(request);
        }
    }

    private sealed class FakeIssuer : IFirebaseCustomTokenIssuer
    {
        public bool Enabled => true;
        public Task<FirebaseCustomTokenResult> IssueAsync(Guid userId, string projectId, CancellationToken ct) =>
            Task.FromResult(new FirebaseCustomTokenResult("test-custom-token", DateTime.UtcNow.AddHours(1)));
    }

    private sealed class FakeSigner(bool fail = false) : IFirebaseAdminTokenSigner
    {
        public List<string> Uids { get; } = [];
        public Task<string> CreateCustomTokenAsync(string uid, CancellationToken ct)
        {
            if (fail) throw new AuthChallengeUnavailableException("test signing unavailable");
            Uids.Add(uid);
            return Task.FromResult("test-custom-token");
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
