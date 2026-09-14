using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Weymela.Api.Auth;
using Weymela.Api.Endpoints;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class DeviceAccessHttpTests(PostgresFixture fixture)
{
    private const string Project = "isolated-v3-test";
    private const string Subject = "locked-device-user";
    private const string Pin = "01234";
    private static readonly string Pepper = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task Locked_workspace_reads_and_mutations_fail_before_side_effects_while_status_and_signout_remain_available()
    {
        await using var host = await Host();
        var seeded = await SeedAsync(host, includeCreator: true);
        var session = await SignInAsync(host, seeded.DeviceCredential, seeded.CustomerId);
        using var client = Client(host, session.AllCookies);
        var before = await client.GetFromJsonAsync<JsonObject>("/api/session");
        client.DefaultRequestHeaders.Add("X-Weymela-Profile", before!["activeProfileKey"]!.GetValue<string>());
        await LockAsync(host, seeded.UserId);

        var read = await client.GetAsync("/api/customer/offers");
        Assert.Equal((HttpStatusCode)423, read.StatusCode);
        Assert.Equal("SessionLocked", (await read.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());

        var auditBefore = await AuditCount(host, seeded.UserId);
        var creator = before["profiles"]!.AsArray().Single(x => x!["role"]!.GetValue<string>() == "Creator")!;
        var switched = await client.Post("/api/session/switch-profile", new
        {
            role = "Creator",
            subjectId = creator["subjectId"]!.GetValue<string>(),
            businessId = (string?)null
        });
        Assert.Equal((HttpStatusCode)423, switched.StatusCode);
        Assert.Equal(auditBefore, await AuditCount(host, seeded.UserId));

        var access = await client.GetFromJsonAsync<JsonObject>("/api/device/access");
        Assert.Equal(DeviceAccessStates.Locked, access!["state"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NoContent, (await client.Post("/api/session/sign-out", new { })).StatusCode);
    }

    [Fact]
    public async Task Correct_pin_rotates_only_device_session_cookie_and_preserves_profile_context()
    {
        await using var host = await Host();
        var seeded = await SeedAsync(host, includeCreator: true);
        var signedIn = await SignInAsync(host, seeded.DeviceCredential, seeded.CustomerId);
        using var client = Client(host, signedIn.AllCookies);
        var before = await client.GetFromJsonAsync<JsonObject>("/api/session");
        await LockAsync(host, seeded.UserId);

        var response = await client.Post("/api/device/unlock", new { pin = Pin }, "correct-pin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(DeviceAccessStates.Unlocked, body!["state"]!.GetValue<string>());
        var setCookies = response.Headers.GetValues("Set-Cookie").ToArray();
        var rotated = Assert.Single(setCookies, x => x.StartsWith(
            DeviceSessionCredentialCookie.DevelopmentName + "=", StringComparison.Ordinal)).Split(';')[0];
        Assert.DoesNotContain(setCookies, x => x.StartsWith("WeymelaV3.Session=", StringComparison.Ordinal));
        Assert.DoesNotContain(setCookies, x => x.StartsWith(DeviceCredentialCookie.Name + "=", StringComparison.Ordinal));

        using var oldCredential = Client(host, signedIn.AllCookies);
        Assert.Equal(DeviceAccessStates.FullAuthenticationRequired,
            (await oldCredential.GetFromJsonAsync<JsonObject>("/api/device/access"))!["state"]!.GetValue<string>());

        using var current = Client(host, $"{signedIn.AuthCookie}; {signedIn.DeviceCookie}; {rotated}");
        var after = await current.GetFromJsonAsync<JsonObject>("/api/session");
        Assert.Equal(before!["role"]!.GetValue<string>(), after!["role"]!.GetValue<string>());
        Assert.Equal(before["activeProfileKey"]!.GetValue<string>(), after["activeProfileKey"]!.GetValue<string>());
        Assert.Equal(before["profiles"]!.ToJsonString(), after["profiles"]!.ToJsonString());

        await using var db = host.Database.Open();
        var device = await db.AuthorizedDevices.SingleAsync(x => x.UserId == seeded.UserId);
        var durable = await db.DeviceSessions.SingleAsync(x => x.UserId == seeded.UserId && x.RevokedAtUtc == null);
        Assert.Equal(0, device.FailedAttempts);
        Assert.Null(durable.LockedAtUtc);
        Assert.Equal(durable.CreatedAtUtc.AddHours(1), durable.ExpiresAtUtc);
    }

    [Fact]
    public async Task Wrong_pin_is_safe_and_missing_or_expired_session_requires_full_authentication()
    {
        await using var host = await Host();
        var seeded = await SeedAsync(host, includeCreator: false);
        var signedIn = await SignInAsync(host, seeded.DeviceCredential, seeded.CustomerId);
        await LockAsync(host, seeded.UserId);
        using var client = Client(host, signedIn.AllCookies);

        var wrong = await client.Post("/api/device/unlock", new { pin = "99999" }, "wrong-pin");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var safe = await wrong.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("InvalidPin", safe!["code"]!.GetValue<string>());
        Assert.DoesNotContain("99999", safe.ToJsonString(), StringComparison.Ordinal);

        using var missing = Client(host, $"{signedIn.AuthCookie}; {signedIn.DeviceCookie}");
        var denied = await missing.GetAsync("/api/customer/offers");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Contains("FullAuthenticationRequired", await denied.Content.ReadAsStringAsync());

        await using (var db = host.Database.Open())
        {
            var expiredCreated = DateTime.UtcNow.AddHours(-2);
            await db.DeviceSessions.Where(x => x.UserId == seeded.UserId && x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.CreatedAtUtc, expiredCreated)
                    .SetProperty(x => x.ExpiresAtUtc, expiredCreated.AddHours(1)));
        }
        var expired = await client.Post("/api/device/unlock", new { pin = Pin }, "expired-pin");
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Contains("FullAuthenticationRequired", await expired.Content.ReadAsStringAsync());
    }

    private async Task<ApiFixture> Host() => await ApiFixture.CreateAsync(fixture, builder =>
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["V3:Auth:FirebaseProjectId"] = Project,
            ["V3:Auth:PinPepper"] = Pepper
        });
        builder.Services.AddSingleton<IIdentityTokenVerifier>(new TestIdentity());
        builder.Services.AddScoped<IWorkspaceDirectory, PersistentWorkspaceDirectory>();
    });

    private static async Task<Seeded> SeedAsync(ApiFixture host, bool includeCreator)
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var binding = new IdentityBinding
        {
            Provider = "Firebase", ProjectId = Project, ExternalSubject = Subject, UserId = userId,
            IsActive = true, ValidAfterUtc = now.AddMinutes(-1), Version = 1
        };
        var credential = OpaqueDeviceCredential.Create();
        var verifier = await DevicePinVerifier.HashAsync(Pin, Pepper, default);
        var device = DeviceAccessPolicy.NewDevice(userId, credential.Digest, verifier, now);
        await using var db = host.Database.Open();
        db.AddRange(binding, device,
            new AuthIdentifierRecord { UserId = userId, Kind = "Email", IdentifierHash = Guid.NewGuid().ToString("N"), IsVerified = true, CreatedAtUtc = now },
            new PublicWorkspaceProfile { SubjectId = customerId, Role = ActorRole.Customer, DisplayName = "Lock Customer", PublicId = "CU-LOCK" },
            new CommercePermission(userId, ActorRole.Customer, customerId, null, true, false));
        if (includeCreator)
        {
            var creatorId = Guid.NewGuid();
            db.AddRange(
                new PublicWorkspaceProfile { SubjectId = creatorId, Role = ActorRole.Creator, DisplayName = "Lock Creator", PublicId = "CR-LOCK" },
                new CommercePermission(userId, ActorRole.Creator, creatorId, null, true, false));
        }
        await db.SaveChangesAsync();
        return new(userId, customerId, credential);
    }

    private static async Task<SignedIn> SignInAsync(ApiFixture host, OpaqueDeviceCredential deviceCredential, Guid customerId)
    {
        using var client = Client(host, $"{DeviceCredentialCookie.Name}={deviceCredential.Value}");
        var response = await client.PostAsJsonAsync("/api/auth/firebase/session", new
        {
            idToken = "verified-lock-user", profileRole = "Customer", profileSubjectId = customerId
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookies = response.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0]).ToArray();
        var auth = Assert.Single(cookies, x => x.StartsWith("WeymelaV3.Session=", StringComparison.Ordinal));
        var deviceSession = Assert.Single(cookies, x => x.StartsWith(DeviceSessionCredentialCookie.DevelopmentName + "=", StringComparison.Ordinal));
        var device = $"{DeviceCredentialCookie.Name}={deviceCredential.Value}";
        return new(auth, device, deviceSession);
    }

    private static async Task LockAsync(ApiFixture host, Guid userId)
    {
        await using var db = host.Database.Open();
        var row = await db.DeviceSessions.SingleAsync(x => x.UserId == userId && x.RevokedAtUtc == null);
        row.LastActivityAtUtc = DateTime.UtcNow.AddMinutes(-21);
        row.LockedAtUtc = null;
        row.Version++;
        await db.SaveChangesAsync();
    }

    private static async Task<int> AuditCount(ApiFixture host, Guid userId)
    {
        await using var db = host.Database.Open();
        return await db.AuditEvents.CountAsync(x => x.ActorId == userId && x.EventType == "ProfileSwitched");
    }

    private static HttpClient Client(ApiFixture host, string cookies)
    {
        var client = host.Anonymous();
        client.DefaultRequestHeaders.Add("Cookie", cookies);
        return client;
    }

    private sealed record Seeded(Guid UserId, Guid CustomerId, OpaqueDeviceCredential DeviceCredential);
    private sealed record SignedIn(string AuthCookie, string DeviceCookie, string DeviceSessionCookie)
    {
        public string AllCookies => $"{AuthCookie}; {DeviceCookie}; {DeviceSessionCookie}";
    }

    private sealed class TestIdentity : IIdentityTokenVerifier
    {
        public Task<VerifiedIdentity> VerifyAsync(string _, CancellationToken __)
        {
            var now = DateTime.UtcNow;
            return Task.FromResult(new VerifiedIdentity("Firebase", Project, Subject, now, now.AddHours(1)));
        }
    }
}
