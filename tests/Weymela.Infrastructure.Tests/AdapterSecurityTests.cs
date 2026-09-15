using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Providers;
using Weymela.Infrastructure.Persistence;
using Xunit;

namespace Weymela.Infrastructure.Tests;

public sealed class AdapterSecurityTests
{
    private static string TestSecret(string purpose) => Convert.ToBase64String(
        SHA256.HashData(Encoding.UTF8.GetBytes("isolated-adapter-fixture-" + purpose)));
    private static Dictionary<string, string?> Config() => new()
    {
        ["ConnectionStrings:WeymelaV3"] = "Host=127.0.0.1;Port=1;Database=weymela_v3_pilot_test;Username=isolated;Password=test-only",
        ["V3:Auth:Provider"] = "Firebase", ["V3:Auth:FirebaseProjectId"] = "weymela-pilot",
        ["V3:Auth:EmailDeliveryMode"] = "Resend", ["V3:Auth:FirebaseCustomTokenMode"] = "FirebaseAdmin",
        ["V3:Auth:ResendApiKey"] = "re_" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes("isolated-resend-key"))),
        ["V3:Auth:ResendFromAddress"] = "no-reply@pilot-mail.weymela.com", ["V3:Auth:ResendFromName"] = "Weymela Pilot",
        ["GOOGLE_APPLICATION_CREDENTIALS"] = "/run/secrets/v3-firebase-admin.json",
        ["V3:Auth:CodeHashKey"] = TestSecret("code"), ["V3:Auth:PinPepper"] = TestSecret("pin"),
        ["V3:AllowedOrigins:0"] = "https://v3-pilot.weymela.com", ["V3:PublicWebUrl"] = "https://v3-pilot.weymela.com",
        ["V3:PublicApiUrl"] = "https://api-v3-pilot.weymela.com", ["V3:Security:CameraPolicy"] = RuntimeOptions.CameraPolicy,
        ["V3:Security:TlsEdgeConfirmed"] = "true", ["V3:Auth:CookieKeyDirectory"] = "/run/weymela-v3/keys",
        ["V3:Auth:CookieCertificatePath"] = "/run/secrets/v3-cookie-protection.pfx",
        ["V3:Auth:CookieCertificatePassword"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("isolated-cookie-password")))
    };
    [Fact] public void Fully_explicit_pilot_configuration_defaults_to_frozen_and_bounded_without_connecting()
    {
        var options = RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(Config()).Build(), "Pilot");
        Assert.False(options.DevelopmentIdentity); Assert.False(options.FinancialWritesEnabled); Assert.Equal("Disabled", options.DepositMode);
        Assert.Equal("Resend", options.EmailDeliveryMode); Assert.Equal("FirebaseAdmin", options.FirebaseCustomTokenMode);
        Assert.Equal(20, new Npgsql.NpgsqlConnectionStringBuilder(options.ConnectionString).MaxPoolSize);
        Assert.False(new Npgsql.NpgsqlConnectionStringBuilder(options.ConnectionString).IncludeErrorDetail);
    }
    [Fact] public void Pilot_modes_replace_only_the_disabled_adapter_registrations()
    {
        var options = RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(Config()).Build(), "Pilot");
        var services = new ServiceCollection();
        services.AddWeymelaPersistence(options.ConnectionString).AddPilotAuthenticationAdapters(options);
        Assert.Equal(ServiceLifetime.Singleton, services.Last(x => x.ServiceType == typeof(IEmailCodeDelivery)).Lifetime);
        Assert.NotNull(services.Last(x => x.ServiceType == typeof(IEmailCodeDelivery)).ImplementationFactory);
        Assert.Equal(typeof(FirebaseAdminCustomTokenIssuer), services.Last(x => x.ServiceType == typeof(IFirebaseCustomTokenIssuer)).ImplementationType);

        var disabled = new ServiceCollection();
        disabled.AddWeymelaPersistence(options.ConnectionString).AddPilotAuthenticationAdapters(new RuntimeOptions());
        Assert.Equal(typeof(DisabledEmailCodeDelivery), disabled.Last(x => x.ServiceType == typeof(IEmailCodeDelivery)).ImplementationType);
        Assert.Equal(typeof(DisabledFirebaseCustomTokenIssuer), disabled.Last(x => x.ServiceType == typeof(IFirebaseCustomTokenIssuer)).ImplementationType);
    }
    [Fact] public void Pilot_adapters_cannot_be_activated_as_Production_configuration()
    {
        var config = Config();
        config["ConnectionStrings:WeymelaV3"] = "Host=127.0.0.1;Port=1;Database=weymela_v3_prod_test;Username=isolated;Password=test-only";
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production"));

        config["V3:Auth:EmailDeliveryMode"] = "Disabled";
        config["V3:Auth:FirebaseCustomTokenMode"] = "Disabled";
        var production = RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production");
        var services = new ServiceCollection();
        services.AddWeymelaPersistence(production.ConnectionString).AddPilotAuthenticationAdapters(production);
        Assert.Equal(typeof(DisabledEmailCodeDelivery), services.Last(x => x.ServiceType == typeof(IEmailCodeDelivery)).ImplementationType);
        Assert.Equal(typeof(DisabledFirebaseCustomTokenIssuer), services.Last(x => x.ServiceType == typeof(IFirebaseCustomTokenIssuer)).ImplementationType);
    }
    [Theory]
    [InlineData("V3:Auth:Provider", "")][InlineData("V3:Auth:FirebaseProjectId", "")]
    [InlineData("V3:AllowedOrigins:0", "*")][InlineData("V3:PublicWebUrl", "http://unsafe.invalid")]
    [InlineData("V3:EnableDevelopmentIdentity", "true")][InlineData("V3:Deposits:Mode", "Development")]
    [InlineData("V3:Social:Mode", "Test")][InlineData("V3:Social:Mode", "TikTok")]
    [InlineData("V3:Push:Enabled", "true")][InlineData("V3:Security:CameraPolicy", "camera=*")]
    [InlineData("V3:Security:TlsEdgeConfirmed", "false")][InlineData("V3:Worker:BatchSize", "10000")]
    [InlineData("V3:RateLimitMultiplier", "20")][InlineData("V3:Auth:CookieCertificatePath", "relative.pfx")]
    [InlineData("V3:Auth:EmailDeliveryMode", "Disabled")][InlineData("V3:Auth:FirebaseCustomTokenMode", "Disabled")]
    [InlineData("V3:Auth:ResendApiKey", "")][InlineData("V3:Auth:ResendFromAddress", "other@example.com")]
    [InlineData("V3:Auth:ResendFromName", "Other")][InlineData("V3:Auth:CodeHashKey", "weak")]
    [InlineData("V3:Auth:PinPepper", "weak")][InlineData("GOOGLE_APPLICATION_CREDENTIALS", "/tmp/other.json")]
    [InlineData("V3:Auth:CodeHashKey", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("V3:Auth:PinPepper", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("V3:Auth:CookieCertificatePassword", "short")]
    [InlineData("V3:Auth:ResendApiKey", "re_replace-me-with-a-secret")]
    [InlineData("V3:Auth:CookieCertificatePassword", "replace-me-password")]
    [InlineData("V3:FinancialWritesEnabled", "true")]
    public void Unsafe_pilot_configuration_fails_closed(string key, string value)
    {
        var config = Config(); config[key] = value;
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot"));
    }
    [Fact] public void Pilot_rejects_reused_auth_code_and_pin_secret_material()
    {
        var config = Config(); config["V3:Auth:PinPepper"] = config["V3:Auth:CodeHashKey"];
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot"));
    }
    [Theory] [InlineData("https://evil.invalid/profile")][InlineData("javascript:alert(1)")]
    [InlineData("https://www.tiktok.com@evil.invalid/x")][InlineData("https://www.tiktok.com.evil.invalid/x")][InlineData("http://www.tiktok.com/x")]
    public void Public_links_reject_untrusted_or_unsafe_urls(string value) => Assert.Null(PersistentWorkspaceDirectory.SafeUrl(value));
    [Fact] public async Task Signed_firebase_identity_validates_project_but_does_not_trust_role_claims()
    {
        using var signer = new TokenFixture(); var result = await signer.Verifier.VerifyAsync(signer.Token(), default);
        Assert.Equal("trusted-subject", result.Subject); Assert.Equal("isolated-v3-test", result.ProjectId);
        Assert.DoesNotContain(result.GetType().GetProperties(), p => p.Name.Contains("Role"));
    }
    [Theory] [InlineData("issuer")][InlineData("audience")][InlineData("expired")][InlineData("old-auth")]
    [InlineData("future-auth")][InlineData("wrong-key")][InlineData("empty-subject")][InlineData("missing-auth")]
    public async Task Invalid_firebase_tokens_fail_without_exposing_token(string defect)
    {
        using var signer = new TokenFixture(); var token = signer.Token(defect);
        var error = await Assert.ThrowsAsync<ApplicationFailure>(() => signer.Verifier.VerifyAsync(token, default));
        Assert.Equal(FailureKind.Forbidden, error.Kind); Assert.DoesNotContain(token, error.Message); Assert.DoesNotContain("trusted-subject", error.Message);
    }
    [Theory] [InlineData("not-a-token")][InlineData("eyJhbGciOiJub25lIn0.e30.")]
    public async Task Malformed_and_unsigned_tokens_are_rejected(string token)
    { using var signer = new TokenFixture(); await Assert.ThrowsAsync<ApplicationFailure>(() => signer.Verifier.VerifyAsync(token, default)); }
    [Theory] [InlineData("ownership")][InlineData("creator")][InlineData("content")][InlineData("negative")][InlineData("future")][InlineData("stale")][InlineData("health")]
    public async Task Social_adapter_rejects_unverified_or_mismatched_evidence(string defect)
    {
        var clock = new TestClock(); var adapter = new FakeSocial(clock, defect);
        await Assert.ThrowsAsync<ApplicationFailure>(() => new SocialProviderRouter([adapter], clock).VerifyAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Fake", "content"), default));
    }
    [Fact] public async Task Capability_verified_social_adapter_preserves_provider_neutral_evidence()
    {
        var clock = new TestClock(); var result = await new SocialProviderRouter([new FakeSocial(clock, "")], clock).VerifyAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Fake", "content"), default);
        Assert.Equal(1234, result.Count); Assert.Equal("Fake", result.Provider);
    }
    private sealed class FakeSocial(TestClock clock, string defect) : ISocialVerificationAdapter
    {
        public ProviderCapabilities Capabilities => new("Fake", true, true, true);
        public Task<ProviderHealth> HealthAsync(CancellationToken ct) => Task.FromResult(new ProviderHealth("Fake", defect == "health" ? ProviderHealthState.Unavailable : ProviderHealthState.Healthy, "test", clock.Now));
        public Task<VerifiedContentEvidence> VerifyAsync(VerifiedViewRequest r, CancellationToken ct) => Task.FromResult(new VerifiedContentEvidence(
            defect == "creator" ? Guid.NewGuid() : r.CreatorId, r.Provider, defect == "content" ? "wrong" : r.ExternalContentId,
            defect != "ownership", defect == "negative" ? -1 : 1234, defect == "future" ? clock.Now.AddMinutes(1) : defect == "stale" ? clock.Now.AddHours(-1) : clock.Now, "evidence"));
    }
    private sealed class TokenFixture : IDisposable, IIdentitySigningKeys
    {
        private readonly RSA rsa = RSA.Create(2048); private readonly TestClock clock = new();
        public FirebaseTokenVerifier Verifier => new(new RuntimeOptions { FirebaseProjectId = "isolated-v3-test" }, this, clock);
        public Task<SecurityKey?> FindAsync(string keyId, CancellationToken ct) => Task.FromResult<SecurityKey?>(keyId == "test-key" ? new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = keyId } : null);
        public string Token(string defect = "")
        {
            var claims = new Dictionary<string, object> { ["sub"] = defect == "empty-subject" ? "" : "trusted-subject", ["role"] = "PlatformAdmin" };
            if (defect != "missing-auth") claims["auth_time"] = new DateTimeOffset(clock.Now.AddMinutes(defect == "old-auth" ? -10 : defect == "future-auth" ? 1 : -1)).ToUnixTimeSeconds();
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
                Issuer = defect == "issuer" ? "https://evil.invalid" : "https://securetoken.google.com/isolated-v3-test",
                Audience = defect == "audience" ? "other-project" : "isolated-v3-test", Claims = claims,
                IssuedAt = clock.Now.AddMinutes(defect == "expired" ? -10 : 0), NotBefore = clock.Now.AddMinutes(-10), Expires = clock.Now.AddMinutes(defect == "expired" ? -1 : 60),
                SigningCredentials = new(new RsaSecurityKey(rsa) { KeyId = defect == "wrong-key" ? "unknown" : "test-key" }, SecurityAlgorithms.RsaSha256)
            });
        }
        public void Dispose() => rsa.Dispose();
    }
}
