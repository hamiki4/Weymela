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
public sealed class DeviceSessionEstablishmentHttpTests(PostgresFixture fixture)
{
    private const string Project = "isolated-v3-test";
    private const string Subject = "device-session-user";
    private static readonly string Pepper = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task Recognized_device_full_auth_issues_separate_session_without_changing_workspace_identity()
    {
        await using var host = await Host();
        var account = await SeedAccount(host, includeCreator: false);
        var device = await AddDevice(host, account.UserId, DeviceState.Active);
        using var client = host.Anonymous();
        client.DefaultRequestHeaders.Add("Cookie", $"{DeviceCredentialCookie.Name}={device.Credential.Value}");

        var response = await client.PostAsJsonAsync("/api/auth/firebase/session", new { idToken = "verified-token" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        var authCookie = Cookie(cookies, "WeymelaV3.Session");
        var sessionCookie = Cookie(cookies, DeviceSessionCredentialCookie.DevelopmentName);
        Assert.DoesNotContain(cookies, x => x.StartsWith(DeviceCredentialCookie.Name + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", Header(cookies, "WeymelaV3.Session").ToLowerInvariant());
        Assert.Contains("samesite=strict", Header(cookies, "WeymelaV3.Session").ToLowerInvariant());

        using var authenticated = Client(host, authCookie, sessionCookie,
            $"{DeviceCredentialCookie.Name}={device.Credential.Value}");
        var projected = await authenticated.GetFromJsonAsync<JsonObject>("/api/session");
        Assert.Equal("Customer", projected!["role"]!.GetValue<string>());
        Assert.Equal("Customer:" + account.CustomerId.ToString("D") + ":-",
            projected["activeProfileKey"]!.GetValue<string>());
        Assert.Single(projected["profiles"]!.AsArray());

        var rawSessionCredential = Value(sessionCookie);
        await using var db = host.Database.Open();
        var saved = await db.DeviceSessions.SingleAsync(x => x.UserId == account.UserId);
        Assert.Equal(account.BindingId, saved.IdentityBindingId);
        Assert.Equal(account.BindingVersion, saved.IdentityVersion);
        Assert.Equal(device.Device!.Id, saved.AuthorizedDeviceId);
        Assert.Equal(saved.CreatedAtUtc, saved.LastActivityAtUtc);
        Assert.Equal(saved.CreatedAtUtc.AddHours(1), saved.ExpiresAtUtc);
        Assert.True(OpaqueDeviceCredential.Matches(rawSessionCredential, saved.SessionIdentifierHash));
        Assert.DoesNotContain(rawSessionCredential, saved.SessionIdentifierHash, StringComparison.Ordinal);

        using var credentialOnly = Client(host, sessionCookie);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await credentialOnly.GetAsync("/api/session")).StatusCode);
    }

    [Theory]
    [InlineData(DeviceState.Unknown)]
    [InlineData(DeviceState.RecoveryRequired)]
    public async Task Unknown_or_recovery_required_device_preserves_existing_fail_closed_contract(
        DeviceState state)
    {
        await using var host = await Host();
        var account = await SeedAccount(host, includeCreator: false);
        var device = state == DeviceState.Unknown
            ? new DeviceFixture(OpaqueDeviceCredential.Create(), null)
            : await AddDevice(host, account.UserId, state);
        using var client = host.Anonymous();
        client.DefaultRequestHeaders.Add("Cookie", $"{DeviceCredentialCookie.Name}={device.Credential.Value}");

        var response = await client.PostAsJsonAsync("/api/auth/firebase/session", new { idToken = "verified-token" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cookies, x => x.StartsWith("WeymelaV3.Session=", StringComparison.Ordinal));
        Assert.DoesNotContain(cookies, x => x.StartsWith(
            DeviceSessionCredentialCookie.DevelopmentName + "=", StringComparison.Ordinal));
        Assert.DoesNotContain(cookies, x => x.StartsWith(
            DeviceCredentialCookie.Name + "=", StringComparison.Ordinal));
        if (state == DeviceState.RecoveryRequired)
        {
            using var recovery = Client(host, Cookie(cookies, "WeymelaV3.Session"),
                $"{DeviceCredentialCookie.Name}={device.Credential.Value}");
            var access = await recovery.GetFromJsonAsync<JsonObject>("/api/device/access");
            Assert.Equal(DeviceAccessStates.RecoveryRequired,
                access!["state"]!.GetValue<string>());
        }
        await using var db = host.Database.Open();
        Assert.Empty(db.DeviceSessions.Where(x => x.UserId == account.UserId));
    }

    [Theory]
    [InlineData(DeviceState.Expired)]
    [InlineData(DeviceState.Revoked)]
    public async Task Same_user_stale_device_is_cleared_only_after_full_auth_and_reaches_enrollment(
        DeviceState state)
    {
        await using var host = await Host();
        var account = await SeedAccount(host, includeCreator: true, includeBusiness: true);
        var device = await AddDevice(host, account.UserId, state);
        var oldSessionCredential = OpaqueDeviceCredential.Create();
        await using (var db = host.Database.Open())
        {
            db.DeviceSessions.Add(DeviceAccessPolicy.NewSession(account.UserId, account.BindingId,
                account.BindingVersion, device.Device!.Id, oldSessionCredential.Digest,
                DateTime.UtcNow.AddMinutes(-30)));
            await db.SaveChangesAsync();
        }

        using var client = Client(host,
            $"{DeviceCredentialCookie.Name}={device.Credential.Value}",
            $"{DeviceSessionCredentialCookie.DevelopmentName}={oldSessionCredential.Value}");
        var response = await client.PostAsJsonAsync("/api/auth/firebase/session", new
        {
            idToken = "verified-token", profileRole = "Customer", profileSubjectId = account.CustomerId
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        var authCookie = Cookie(cookies, "WeymelaV3.Session");
        AssertCleared(cookies, DeviceCredentialCookie.Name);
        AssertCleared(cookies, DeviceSessionCredentialCookie.DevelopmentName);

        using var authenticated = Client(host, authCookie);
        var access = await authenticated.GetFromJsonAsync<JsonObject>("/api/device/access");
        Assert.Equal(DeviceAccessStates.EnrollmentRequired,
            access!["state"]!.GetValue<string>());
        var enrollment = await authenticated.GetFromJsonAsync<JsonObject>("/api/device/enrollment");
        Assert.Equal(DeviceEnrollmentStates.EnrollmentRequired,
            enrollment!["state"]!.GetValue<string>());
        var session = await authenticated.GetFromJsonAsync<JsonObject>("/api/session");
        Assert.Equal("Customer", session!["role"]!.GetValue<string>());
        Assert.Equal("Customer:" + account.CustomerId.ToString("D") + ":-",
            session["activeProfileKey"]!.GetValue<string>());
        var roles = session["profiles"]!.AsArray()
            .Select(x => x!["role"]!.GetValue<string>()).ToArray();
        Assert.Equal(3, roles.Length);
        Assert.Contains("Customer", roles);
        Assert.Contains("Creator", roles);
        Assert.Contains("Business", roles);

        await using var verify = host.Database.Open();
        var savedDevice = await verify.AuthorizedDevices.AsNoTracking()
            .SingleAsync(x => x.Id == device.Device.Id);
        if (state == DeviceState.Revoked)
            Assert.NotNull(savedDevice.RevokedAtUtc);
        else
            Assert.True(savedDevice.ExpiresAtUtc <= DateTime.UtcNow);
        Assert.Equal(device.Device.Version, savedDevice.Version);
        Assert.True(OpaqueDeviceCredential.Matches(device.Credential.Value,
            savedDevice.CredentialIdHash));
        Assert.Empty(await verify.DeviceSessions.Where(x => x.UserId == account.UserId
            && x.RevokedAtUtc == null && x.SessionIdentifierHash != oldSessionCredential.Digest)
            .ToListAsync());

        using var oldCredentials = Client(host, authCookie,
            $"{DeviceCredentialCookie.Name}={device.Credential.Value}",
            $"{DeviceSessionCredentialCookie.DevelopmentName}={oldSessionCredential.Value}");
        var oldAccess = await oldCredentials.GetFromJsonAsync<JsonObject>("/api/device/access");
        Assert.Equal(DeviceAccessStates.FullAuthenticationRequired,
            oldAccess!["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task Failed_full_auth_cannot_clear_stale_device_credentials()
    {
        await using var host = await Host();
        var account = await SeedAccount(host, includeCreator: false);
        var device = await AddDevice(host, account.UserId, DeviceState.Revoked);
        using var client = Client(host, $"{DeviceCredentialCookie.Name}={device.Credential.Value}");

        var response = await client.PostAsJsonAsync("/api/auth/firebase/session",
            new { idToken = "rejected-token" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        await using var db = host.Database.Open();
        Assert.NotNull((await db.AuthorizedDevices.AsNoTracking()
            .SingleAsync(x => x.Id == device.Device!.Id)).RevokedAtUtc);

        using var unauthenticated = Client(host,
            $"{DeviceCredentialCookie.Name}={device.Credential.Value}");
        var access = await unauthenticated.GetAsync("/api/device/access");
        Assert.Equal(HttpStatusCode.Unauthorized, access.StatusCode);
        Assert.False(access.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Mismatched_user_stale_device_is_not_cleared_or_adopted()
    {
        await using var host = await Host();
        await SeedAccount(host, includeCreator: false);
        var other = await AddDevice(host, Guid.NewGuid(), DeviceState.Revoked);
        using var client = Client(host, $"{DeviceCredentialCookie.Name}={other.Credential.Value}");

        var response = await client.PostAsJsonAsync("/api/auth/firebase/session",
            new { idToken = "verified-token" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.DoesNotContain(cookies, x => x.StartsWith(
            DeviceCredentialCookie.Name + "=", StringComparison.Ordinal));
        Assert.DoesNotContain(cookies, x => x.StartsWith(
            DeviceSessionCredentialCookie.DevelopmentName + "=", StringComparison.Ordinal));
        await using var db = host.Database.Open();
        Assert.Empty(await db.DeviceSessions.Where(x => x.UserId == other.Device!.UserId).ToListAsync());
    }

    [Fact]
    public async Task Profile_switch_preserves_device_session_and_does_not_rotate_or_extend_it()
    {
        await using var host = await Host();
        var account = await SeedAccount(host, includeCreator: true);
        var device = await AddDevice(host, account.UserId, DeviceState.Active);
        using var signIn = host.Anonymous();
        signIn.DefaultRequestHeaders.Add("Cookie", $"{DeviceCredentialCookie.Name}={device.Credential.Value}");
        var authenticated = await signIn.PostAsJsonAsync("/api/auth/firebase/session", new
        {
            idToken = "verified-token", profileRole = "Customer", profileSubjectId = account.CustomerId
        });
        Assert.Equal(HttpStatusCode.NoContent, authenticated.StatusCode);
        var initialCookies = authenticated.Headers.GetValues("Set-Cookie").ToArray();
        var authCookie = Cookie(initialCookies, "WeymelaV3.Session");
        var sessionCookie = Cookie(initialCookies, DeviceSessionCredentialCookie.DevelopmentName);
        DeviceSessionRecord before;
        await using (var db = host.Database.Open())
            before = await db.DeviceSessions.AsNoTracking().SingleAsync(x => x.UserId == account.UserId);

        using var client = Client(host, authCookie, sessionCookie,
            $"{DeviceCredentialCookie.Name}={device.Credential.Value}");
        var switched = await client.Post("/api/session/switch-profile", new
        {
            role = "Creator", subjectId = account.CreatorId, businessId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var switchCookies = switched.Headers.GetValues("Set-Cookie").ToArray();
        var switchedAuthCookie = Cookie(switchCookies, "WeymelaV3.Session");
        Assert.DoesNotContain(switchCookies, x => x.StartsWith(
            DeviceSessionCredentialCookie.DevelopmentName + "=", StringComparison.Ordinal));

        await using (var db = host.Database.Open())
        {
            var after = await db.DeviceSessions.AsNoTracking().SingleAsync(x => x.UserId == account.UserId);
            Assert.Equal(before.SessionIdentifierHash, after.SessionIdentifierHash);
            Assert.Equal(before.ExpiresAtUtc, after.ExpiresAtUtc);
            Assert.Equal(before.Generation, after.Generation);
            Assert.Equal(before.Version, after.Version);
            Assert.Null(after.RevokedAtUtc);
        }
        using var creator = Client(host, switchedAuthCookie, sessionCookie,
            $"{DeviceCredentialCookie.Name}={device.Credential.Value}");
        var session = await creator.GetFromJsonAsync<JsonObject>("/api/session");
        Assert.Equal("Creator", session!["role"]!.GetValue<string>());
        Assert.Equal(2, session["profiles"]!.AsArray().Count);
    }

    [Fact]
    public async Task Sign_out_clears_auth_and_device_session_cookies_without_revoking_device_or_session()
    {
        await using var host = await Host();
        var account = await SeedAccount(host, includeCreator: false);
        var device = await AddDevice(host, account.UserId, DeviceState.Active);
        using var signIn = host.Anonymous();
        signIn.DefaultRequestHeaders.Add("Cookie", $"{DeviceCredentialCookie.Name}={device.Credential.Value}");
        var authenticated = await signIn.PostAsJsonAsync("/api/auth/firebase/session", new { idToken = "verified-token" });
        var issued = authenticated.Headers.GetValues("Set-Cookie").ToArray();
        using var client = Client(host, Cookie(issued, "WeymelaV3.Session"),
            Cookie(issued, DeviceSessionCredentialCookie.DevelopmentName),
            $"{DeviceCredentialCookie.Name}={device.Credential.Value}");

        var response = await client.PostAsJsonAsync("/api/session/sign-out", new { });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cleared = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cleared, x => x.StartsWith("WeymelaV3.Session=", StringComparison.Ordinal)
            && x.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cleared, x => x.StartsWith(DeviceSessionCredentialCookie.DevelopmentName + "=", StringComparison.Ordinal)
            && x.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cleared, x => x.StartsWith(DeviceCredentialCookie.Name + "=", StringComparison.Ordinal));

        await using var db = host.Database.Open();
        Assert.Null((await db.AuthorizedDevices.SingleAsync(x => x.Id == device.Device!.Id)).RevokedAtUtc);
        Assert.Null((await db.DeviceSessions.SingleAsync(x => x.UserId == account.UserId)).RevokedAtUtc);
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

    private static async Task<Account> SeedAccount(ApiFixture host, bool includeCreator,
        bool includeBusiness = false)
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var binding = new IdentityBinding
        {
            Provider = "Firebase", ProjectId = Project, ExternalSubject = Subject, UserId = userId,
            IsActive = true, ValidAfterUtc = now.AddMinutes(-1), Version = 3
        };
        await using var db = host.Database.Open();
        db.IdentityBindings.Add(binding);
        db.AuthIdentifiers.Add(new AuthIdentifierRecord
        {
            UserId = userId, Kind = "Email", IdentifierHash = Guid.NewGuid().ToString("N"),
            IsVerified = true, CreatedAtUtc = now
        });
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile
        {
            SubjectId = customerId, Role = ActorRole.Customer,
            DisplayName = "Session Customer", PublicId = "CU-SESSION"
        });
        db.CommercePermissions.Add(new CommercePermission(userId, ActorRole.Customer,
            customerId, null, true, false));
        if (includeCreator)
        {
            db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile
            {
                SubjectId = creatorId, Role = ActorRole.Creator, DisplayName = "Session Creator",
                PublicId = "CR-SESSION", Region = "Addis", Category = "Food"
            });
            db.CommercePermissions.Add(new CommercePermission(userId, ActorRole.Creator,
                creatorId, null, true, false));
        }
        if (includeBusiness)
        {
            db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile
            {
                SubjectId = businessId, Role = ActorRole.Business, DisplayName = "Session Business",
                PublicId = "BU-SESSION", Region = "Addis", Category = "Food"
            });
            db.CommercePermissions.Add(new CommercePermission(userId, ActorRole.Business,
                businessId, businessId, true, false));
        }
        await db.SaveChangesAsync();
        return new(userId, binding.Id, binding.Version, customerId, creatorId, businessId);
    }

    private static async Task<DeviceFixture> AddDevice(ApiFixture host, Guid userId, DeviceState state)
    {
        var now = DateTime.UtcNow;
        var credential = OpaqueDeviceCredential.Create();
        var enrolled = state == DeviceState.Expired ? now.AddDays(-31) : now;
        var device = DeviceAccessPolicy.NewDevice(userId, credential.Digest, "pin-v1$fixture", enrolled);
        if (state == DeviceState.Revoked) device.RevokedAtUtc = now;
        if (state == DeviceState.RecoveryRequired)
        {
            device.FailedAttempts = DeviceAccessPolicy.RecoveryAttemptThreshold;
            device.RequiresRecovery = true;
        }
        await using var db = host.Database.Open();
        db.AuthorizedDevices.Add(device);
        await db.SaveChangesAsync();
        return new(credential, device);
    }

    private static HttpClient Client(ApiFixture host, params string[] cookies)
    {
        var client = host.Anonymous();
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
        return client;
    }

    private static string Cookie(string[] headers, string name) => Header(headers, name).Split(';')[0];
    private static string Header(string[] headers, string name) =>
        Assert.Single(headers, value => value.StartsWith(name + "=", StringComparison.Ordinal));
    private static void AssertCleared(string[] headers, string name)
    {
        var header = Header(headers, name);
        Assert.StartsWith(name + "=;", header, StringComparison.Ordinal);
        Assert.Contains("expires=", header, StringComparison.OrdinalIgnoreCase);
    }
    private static string Value(string cookie) => cookie[(cookie.IndexOf('=') + 1)..];

    public enum DeviceState { Active, Unknown, Expired, Revoked, RecoveryRequired }
    private sealed record Account(Guid UserId, Guid BindingId, long BindingVersion,
        Guid CustomerId, Guid CreatorId, Guid BusinessId);
    private sealed record DeviceFixture(OpaqueDeviceCredential Credential, AuthorizedDeviceRecord? Device);

    private sealed class TestIdentity : IIdentityTokenVerifier
    {
        public Task<VerifiedIdentity> VerifyAsync(string token, CancellationToken __)
        {
            if (token == "rejected-token")
                throw new ApplicationFailure(FailureKind.Forbidden,
                    "The authentication token is invalid.");
            var now = DateTime.UtcNow;
            return Task.FromResult(new VerifiedIdentity("Firebase", Project, Subject, now, now.AddHours(1)));
        }
    }
}
