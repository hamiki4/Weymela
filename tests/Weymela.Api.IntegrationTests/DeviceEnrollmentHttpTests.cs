using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Weymela.Api;
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
public sealed class DeviceEnrollmentHttpTests(PostgresFixture fixture)
{
    private const string Project = "isolated-v3-test";
    private const string Subject = "device-enrollment-user";
    private static readonly string Pepper = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task Enrollment_requires_authenticated_email_verified_account_and_crypto_configuration()
    {
        await using var host = await Host();
        using var anonymous = host.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/device/enrollment")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/device/enrollment",
            new { pin = "12345", confirmPin = "12345" })).StatusCode);

        var userId = Guid.NewGuid();
        await SeedAccount(host, userId, verifiedEmail: false);
        var cookie = await SignInCookie(host);
        using var unverified = ClientWithCookies(host, cookie);
        Assert.Equal(HttpStatusCode.Forbidden, (await unverified.GetAsync("/api/device/enrollment")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unverified.Post("/api/device/enrollment",
            new { pin = "12345", confirmPin = "12345" })).StatusCode);

        await using var unavailableHost = await Host(pinPepper: "");
        var unavailableUser = Guid.NewGuid();
        await SeedAccount(unavailableHost, unavailableUser, verifiedEmail: true);
        using var unavailable = ClientWithCookies(unavailableHost, await SignInCookie(unavailableHost));
        var response = await unavailable.GetAsync("/api/device/enrollment");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("DeviceEnrollmentUnavailable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Initial_enrollment_persists_only_verifiers_and_digest_and_preserves_session_context()
    {
        await using var host = await Host();
        var userId = Guid.NewGuid();
        await SeedAccount(host, userId, verifiedEmail: true);
        var authCookie = await SignInCookie(host);
        using var client = ClientWithCookies(host, authCookie);
        var before = await client.GetFromJsonAsync<JsonObject>("/api/session");
        Assert.Equal("Customer", before!["role"]!.GetValue<string>());
        Assert.Equal(DeviceEnrollmentStates.EnrollmentRequired,
            (await client.GetFromJsonAsync<JsonObject>("/api/device/enrollment"))!["state"]!.GetValue<string>());

        var missingReference = await client.PostAsJsonAsync("/api/device/enrollment",
            new { pin = "12345", confirmPin = "12345" });
        Assert.Equal(HttpStatusCode.BadRequest, missingReference.StatusCode);

        foreach (var body in new object[]
        {
            new { pin = "1234", confirmPin = "1234" },
            new { pin = "123456", confirmPin = "123456" },
            new { pin = "１２３４５", confirmPin = "１２３４５" },
            new { pin = "12345", confirmPin = "54321" }
        })
        {
            var invalid = await client.Post("/api/device/enrollment", body);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.False(invalid.Headers.Contains("Set-Cookie"));
        }

        const string rawPin = "01234";
        var enrolled = await client.Post("/api/device/enrollment",
            new { pin = rawPin, confirmPin = rawPin }, "device-enrollment-once");
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        var safeBody = await enrolled.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(DeviceEnrollmentStates.Enrolled, safeBody!["state"]!.GetValue<string>());
        Assert.DoesNotContain(rawPin, safeBody.ToJsonString(), StringComparison.Ordinal);
        var setCookie = enrolled.Headers.GetValues("Set-Cookie").Single();
        var lower = setCookie.ToLowerInvariant();
        Assert.Contains("httponly", lower);
        Assert.Contains("samesite=strict", lower);
        Assert.Contains("path=/", lower);
        Assert.Contains("max-age=2592000", lower);
        Assert.DoesNotContain("secure", lower); // Development host only.
        var deviceCookie = setCookie.Split(';')[0];
        var rawCredential = deviceCookie[(deviceCookie.IndexOf('=') + 1)..];
        Assert.Equal(64, rawCredential.Length);

        await using (var db = host.Database.Open())
        {
            var device = await db.AuthorizedDevices.SingleAsync(x => x.UserId == userId);
            Assert.NotEqual(rawPin, device.PinVerifier);
            Assert.True(await DevicePinVerifier.VerifyAsync(rawPin, device.PinVerifier!, Pepper, default));
            Assert.True(OpaqueDeviceCredential.Matches(rawCredential, device.CredentialIdHash));
            Assert.DoesNotContain(rawCredential, device.CredentialIdHash, StringComparison.Ordinal);
            Assert.Equal(device.EnrolledAtUtc.AddDays(30), device.ExpiresAtUtc);
            Assert.Equal(0, device.FailedAttempts);
            Assert.Null(device.LockedUntilUtc);
            Assert.False(device.RequiresRecovery);
            Assert.Empty(db.DeviceSessions);
            var audit = Assert.Single(db.AuditEvents.Where(x => x.ActorId == userId && x.EventType == "AuthorizedDeviceEnrolled"));
            var idempotency = Assert.Single(db.IdempotencyRecords.Where(x => x.ActorId == userId && x.OperationType == "InitialDevicePinEnrollment"));
            Assert.DoesNotContain(rawPin, audit.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain(rawCredential, audit.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain(rawPin, idempotency.RequestFingerprint, StringComparison.Ordinal);
            Assert.DoesNotContain(rawCredential, idempotency.RequestFingerprint, StringComparison.Ordinal);
        }

        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"{authCookie}; {deviceCookie}");
        Assert.Equal(DeviceEnrollmentStates.Enrolled,
            (await client.GetFromJsonAsync<JsonObject>("/api/device/enrollment"))!["state"]!.GetValue<string>());
        var replay = await client.Post("/api/device/enrollment",
            new { pin = rawPin, confirmPin = rawPin }, "another-safe-browser-request");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.False(replay.Headers.Contains("Set-Cookie"));
        await using (var db = host.Database.Open()) Assert.Single(db.AuthorizedDevices.Where(x => x.UserId == userId));

        using var replayWithoutDeviceCredential = ClientWithCookies(host, authCookie);
        var ambiguousReplay = await replayWithoutDeviceCredential.Post("/api/device/enrollment",
            new { pin = rawPin, confirmPin = rawPin }, "device-enrollment-once");
        Assert.Equal(HttpStatusCode.Conflict, ambiguousReplay.StatusCode);
        Assert.False(ambiguousReplay.Headers.Contains("Set-Cookie"));
        await using (var db = host.Database.Open()) Assert.Single(db.AuthorizedDevices.Where(x => x.UserId == userId));

        var after = await client.GetFromJsonAsync<JsonObject>("/api/session");
        Assert.Equal(before["role"]!.GetValue<string>(), after!["role"]!.GetValue<string>());
        Assert.Equal(before["activeProfileKey"]!.GetValue<string>(), after["activeProfileKey"]!.GetValue<string>());
        Assert.Equal(before["profiles"]!.ToJsonString(), after["profiles"]!.ToJsonString());
    }

    [Fact]
    public async Task Expired_revoked_and_recovery_required_credentials_fail_closed_without_new_rows()
    {
        await using var host = await Host();
        var userId = Guid.NewGuid();
        await SeedAccount(host, userId, verifiedEmail: true);
        var authCookie = await SignInCookie(host);
        var now = DateTime.UtcNow;
        var fixtures = new[]
        {
            Device(DeviceEnrollmentStates.Expired, userId, now.AddDays(-31)),
            Device(DeviceEnrollmentStates.Revoked, userId, now),
            Device(DeviceEnrollmentStates.RecoveryRequired, userId, now)
        };
        await using (var db = host.Database.Open())
        {
            db.AuthorizedDevices.AddRange(fixtures.Select(x => x.Device));
            await db.SaveChangesAsync();
        }

        foreach (var fixtureDevice in fixtures)
        {
            using var client = ClientWithCookies(host, authCookie,
                $"{DeviceCredentialCookie.Name}={fixtureDevice.Credential.Value}");
            var status = await client.GetFromJsonAsync<JsonObject>("/api/device/enrollment");
            Assert.Equal(fixtureDevice.State, status!["state"]!.GetValue<string>());
            var reenroll = await client.Post("/api/device/enrollment",
                new { pin = "12345", confirmPin = "12345" });
            Assert.Equal(HttpStatusCode.Forbidden, reenroll.StatusCode);
            Assert.False(reenroll.Headers.Contains("Set-Cookie"));
        }
        await using var verify = host.Database.Open();
        Assert.Equal(3, await verify.AuthorizedDevices.CountAsync(x => x.UserId == userId));
    }

    [Fact]
    public void Device_cookie_policy_is_strict_and_secure_outside_development()
    {
        var expires = DateTime.UtcNow.AddDays(30);
        var development = DeviceCredentialCookie.Options(true, expires);
        var pilot = DeviceCredentialCookie.Options(false, expires);
        Assert.True(development.HttpOnly);
        Assert.False(development.Secure);
        Assert.True(pilot.HttpOnly);
        Assert.True(pilot.Secure);
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.Strict, pilot.SameSite);
        Assert.Equal("/", pilot.Path);
        Assert.Equal(TimeSpan.FromDays(30), pilot.MaxAge);
        Assert.Equal(new DateTimeOffset(expires), pilot.Expires);
    }

    private async Task<ApiFixture> Host(string? pinPepper = null) => await ApiFixture.CreateAsync(fixture, builder =>
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["V3:Auth:FirebaseProjectId"] = Project,
            ["V3:Auth:PinPepper"] = pinPepper ?? Pepper
        });
        builder.Services.AddSingleton<IIdentityTokenVerifier>(new DeviceIdentity());
        builder.Services.AddScoped<IWorkspaceDirectory, PersistentWorkspaceDirectory>();
    });

    private static async Task SeedAccount(ApiFixture host, Guid userId, bool verifiedEmail)
    {
        var customerId = Guid.NewGuid();
        await using var db = host.Database.Open();
        db.IdentityBindings.Add(new IdentityBinding
        {
            Provider = "Firebase", ProjectId = Project, ExternalSubject = Subject, UserId = userId,
            IsActive = true, ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1), Version = 1
        });
        db.AuthIdentifiers.Add(new AuthIdentifierRecord
        {
            UserId = userId, Kind = "Email", IdentifierHash = Guid.NewGuid().ToString("N"),
            IsVerified = verifiedEmail, CreatedAtUtc = DateTime.UtcNow
        });
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile
        {
            SubjectId = customerId, Role = ActorRole.Customer, DisplayName = "Device Customer", PublicId = "CU-DEVICE"
        });
        db.CommercePermissions.Add(new CommercePermission(userId, ActorRole.Customer, customerId, null, true, false));
        await db.SaveChangesAsync();
    }

    private static async Task<string> SignInCookie(ApiFixture host)
    {
        using var client = host.Anonymous();
        var response = await client.PostAsJsonAsync("/api/auth/firebase/session", new { idToken = "verified-device-user" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }

    private static HttpClient ClientWithCookies(ApiFixture host, params string[] cookies)
    {
        var client = host.Anonymous();
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
        return client;
    }

    private static (string State, OpaqueDeviceCredential Credential, AuthorizedDeviceRecord Device) Device(
        string state, Guid userId, DateTime enrolledAtUtc)
    {
        var credential = OpaqueDeviceCredential.Create();
        var device = DeviceAccessPolicy.NewDevice(userId, credential.Digest, "pin-v1$c2FsdHNhbHRzYWx0MTI=$dmVyaWZpZXJ2ZXJpZmllcnZlcmlmaWVyMTI=", enrolledAtUtc);
        if (state == DeviceEnrollmentStates.Revoked) device.RevokedAtUtc = enrolledAtUtc;
        if (state == DeviceEnrollmentStates.RecoveryRequired)
        {
            device.FailedAttempts = 10;
            device.RequiresRecovery = true;
        }
        return (state, credential, device);
    }

    private sealed class DeviceIdentity : IIdentityTokenVerifier
    {
        public Task<VerifiedIdentity> VerifyAsync(string _, CancellationToken __)
        {
            var now = DateTime.UtcNow;
            return Task.FromResult(new VerifiedIdentity("Firebase", Project, Subject, now, now.AddHours(1)));
        }
    }
}
