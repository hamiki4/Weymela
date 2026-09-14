using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class OperationalHttpSecurityTests(PostgresFixture fixture)
{
    [Theory] [InlineData("business")][InlineData("creator")][InlineData("customer")][InlineData("cashier")]
    public async Task Operational_admin_endpoints_reject_every_non_admin_role(string alias)
    {
        await using var host = await ApiFixture.CreateAsync(fixture); using var client = await host.Login(alias);
        foreach (var path in new[] { "/api/admin/operations", "/api/admin/reconciliation", "/api/admin/deposit-requests" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }
    [Fact] public async Task Anonymous_notification_and_wallet_requests_fail_authorization()
    {
        await using var host = await ApiFixture.CreateAsync(fixture); using var client = host.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/business/deposit-requests")).StatusCode);
    }
    [Fact] public async Task Development_fixture_rate_limit_is_bounded_without_consuming_firebase_auth_capacity()
    {
        await using var host = await ApiFixture.CreateAsync(fixture); using var client = host.Anonymous();
        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/development/session", new { alias = "admin", accessKey = "incorrect-but-not-secret" })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/development/session", new { alias = "admin", accessKey = "incorrect-but-not-secret" });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode); Assert.True(response.Headers.Contains("Retry-After"));
        Assert.NotEqual(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/auth/firebase/session", new { idToken = "invalid-test-token" })).StatusCode);
    }
    [Fact] public async Task Firebase_auth_rate_limit_remains_bounded_and_returns_retry_after()
    {
        await using var host = await ApiFixture.CreateAsync(fixture); using var client = host.Anonymous();
        for (var i = 0; i < 20; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests,
                (await client.PostAsJsonAsync("/api/auth/firebase/session", new { idToken = "invalid-test-token" })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/auth/firebase/session", new { idToken = "invalid-test-token" });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode); Assert.True(response.Headers.Contains("Retry-After"));
    }
    [Fact] public async Task Notification_rate_limit_does_not_block_separate_wallet_reads()
    {
        await using var host = await ApiFixture.CreateAsync(fixture); using var client = await host.Login("business");
        HttpResponseMessage? response = null; for (var i = 0; i < 91; i++) response = await client.GetAsync("/api/notifications");
        Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/business/wallet")).StatusCode);
    }
    [Fact] public async Task Explicit_cors_origin_is_allowed_and_untrusted_origin_cannot_mutate()
    {
        await using var host = await ApiFixture.CreateAsync(fixture, b => b.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["V3:AllowedOrigins:0"] = "https://trusted.example.invalid" }));
        using var client = await host.Login("business"); client.DefaultRequestHeaders.Add("Origin", "https://trusted.example.invalid");
        var allowed = await client.GetAsync("/api/business/wallet"); Assert.Equal("https://trusted.example.invalid", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "https://evil.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.Post("/api/business/wallet/deposits", new { amount = 1, expectedVersion = 0 })).StatusCode);
    }
    [Fact] public async Task Api_responses_have_safe_camera_csp_and_no_store_headers()
    {
        await using var host = await ApiFixture.CreateAsync(fixture); using var client = await host.Login("business"); var r = await client.GetAsync("/api/business/wallet");
        Assert.Contains("camera=(self)", r.Headers.GetValues("Permissions-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single()); Assert.True(r.Headers.CacheControl!.NoStore);
        Assert.True(Guid.TryParse(r.Headers.GetValues("X-Correlation-ID").Single(), out _));
    }
    [Theory] [InlineData("text/plain", 415)][InlineData("application/xml", 415)]
    public async Task Unsupported_body_content_types_are_rejected(string type, int expected)
    {
        await using var host = await ApiFixture.CreateAsync(fixture); using var client = await host.Login("business");
        Assert.Equal(expected, (int)(await client.PostAsync("/api/business/wallet/deposits", new StringContent("amount=20", Encoding.UTF8, type))).StatusCode);
    }
    [Fact] public async Task Oversized_request_is_rejected_before_command_execution()
    {
        await using var host = await ApiFixture.CreateAsync(fixture); using var client = await host.Login("business");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsJsonAsync("/api/business/campaigns", new { title = new string('a', 40000) })).StatusCode);
    }
    [Theory] [InlineData(-1)][InlineData(0)][InlineData(1.001)]
    public async Task Deposit_money_validation_rejects_negative_zero_and_subcent_values(decimal value)
    {
        await using var host = await ApiFixture.CreateAsync(fixture); using var client = await host.Login("business");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.Post("/api/business/wallet/deposits", new { amount = value, expectedVersion = 0 })).StatusCode);
    }
    [Fact] public async Task Disabled_manual_lookup_cannot_bypass_checkout_and_is_privacy_safe()
    {
        await using var host = await ApiFixture.CreateAsync(fixture); using var cashier = await host.Login("cashier"); using var customer = await host.Login("customer");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await cashier.Post("/api/checkout/manual-lookup", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Post("/api/checkout/manual-lookup", new { })).StatusCode);
    }
    [Fact] public async Task Firebase_token_role_claim_cannot_grant_admin_and_revoked_binding_invalidates_session()
    {
        var userId = Guid.NewGuid(); var subjectId = Guid.NewGuid(); var now = DateTime.UtcNow;
        await using var host = await ApiFixture.CreateAsync(fixture, b => {
            b.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["V3:Auth:FirebaseProjectId"] = "isolated-v3-test" });
            b.Services.AddSingleton<IIdentityTokenVerifier>(new FakeIdentity(now));
        });
        await using (var db = host.Database.Open()) {
            db.IdentityBindings.Add(new() { Provider = "Firebase", ProjectId = "isolated-v3-test", ExternalSubject = "external-id", UserId = userId, IsActive = true, ValidAfterUtc = now.AddHours(-1) });
            db.CommercePermissions.Add(new(userId, ActorRole.Cashier, userId, subjectId, true, true)); await db.SaveChangesAsync();
        }
        using var client = host.Anonymous(); var signIn = await client.PostAsJsonAsync("/api/auth/firebase/session", new { idToken = "test-token-role-PlatformAdmin" });
        Assert.True(signIn.IsSuccessStatusCode, await signIn.Content.ReadAsStringAsync());
        var cookie = signIn.Headers.GetValues("Set-Cookie").Single(); Assert.Contains("httponly", cookie.ToLowerInvariant()); Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/operations")).StatusCode);
        await using (var db = host.Database.Open()) await db.IdentityBindings.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false).SetProperty(x => x.Version, x => x.Version + 1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/session")).StatusCode);
    }
    [Fact] public async Task Unmapped_verified_identity_cannot_self_register_or_choose_role()
    {
        await using var host = await ApiFixture.CreateAsync(fixture, b => b.Services.AddSingleton<IIdentityTokenVerifier>(new FakeIdentity(DateTime.UtcNow)));
        using var client = host.Anonymous(); var result = await client.PostAsJsonAsync("/api/auth/firebase/session", new { idToken = "valid-test-identity" });
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode); Assert.False(result.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/firebase/session", new { idToken="valid-test-identity", role="PlatformAdmin" })).StatusCode);
    }
    [Fact] public async Task Logs_never_contain_raw_qr_or_identity_tokens_or_query_values()
    {
        var logs = new CapturedLogs(); await using var host = await ApiFixture.CreateAsync(fixture, b => b.Logging.AddProvider(logs)); using var client = await host.Login("cashier");
        var raw = new string('Q', 43); await client.Post("/api/checkout/resolve?token=QUERY-SECRET", new { token = raw });
        await client.PostAsJsonAsync("/api/auth/firebase/session", new { idToken = "PRIVATE-ID-TOKEN" });
        var text = string.Join("\n", logs.Messages); Assert.DoesNotContain(raw, text); Assert.DoesNotContain("PRIVATE-ID-TOKEN", text); Assert.DoesNotContain("QUERY-SECRET", text);
    }
    private sealed class FakeIdentity(DateTime now) : IIdentityTokenVerifier
    {
        public Task<VerifiedIdentity> VerifyAsync(string sensitiveIdToken, CancellationToken ct) => Task.FromResult(new VerifiedIdentity("Firebase", "isolated-v3-test", "external-id", now, now.AddMinutes(30)));
    }
    private sealed class CapturedLogs : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Capture(this); public void Dispose() { }
        private sealed class Capture(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState,Exception?,string> formatter) => owner.Messages.Add(formatter(state, exception));
        }
    }
}
