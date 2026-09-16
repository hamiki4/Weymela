using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
public sealed class DevicePinRecoveryHttpTests(PostgresFixture fixture)
{
    private const string Project = "isolated-v3-test";
    private const string Subject = "pin-recovery-http-user";
    private const string OldPin = "01234";
    private const string NewPin = "56789";
    private static readonly string PinPepper = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly string CodeKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task Initiation_is_generic_delivers_only_to_registered_email_and_changes_no_device_state()
    {
        var delivery = new CaptureDelivery();
        await using var host = await Host(delivery);
        var seeded = await SeedAsync(host);
        var before = await CredentialState(host, seeded.UserId);
        using var client = host.Anonymous();

        var known = await client.PostAsJsonAsync("/api/auth/email/start", new
        {
            identifier = seeded.Email, phone = (string?)null, purpose = "PinRecovery"
        });
        var unknown = await client.PostAsJsonAsync("/api/auth/email/start", new
        {
            identifier = "unknown@example.test", phone = (string?)null, purpose = "PinRecovery"
        });

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        var knownBody = await known.Content.ReadFromJsonAsync<JsonObject>();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(knownBody!.Select(x => x.Key).Order().ToArray(),
            unknownBody!.Select(x => x.Key).Order().ToArray());
        Assert.True(knownBody!["accepted"]!.GetValue<bool>());
        Assert.True(unknownBody!["accepted"]!.GetValue<bool>());
        Assert.DoesNotContain(seeded.Email, knownBody.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(seeded.Email, unknownBody.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(seeded.Email, delivery.SingleDestination);
        Assert.Equal(EmailCodePurpose.PinRecovery, delivery.SinglePurpose);
        Assert.Equal(before, await CredentialState(host, seeded.UserId));

        var phoneRecovery = await client.PostAsJsonAsync("/api/auth/email/start", new
        {
            identifier = seeded.Phone, phone = (string?)null, purpose = "PinRecovery"
        });
        Assert.Equal(HttpStatusCode.BadRequest, phoneRecovery.StatusCode);

        var replacementDestination = await client.PostAsJsonAsync("/api/auth/email/start", new
        {
            identifier = seeded.Email, phone = "+251911111111", email = "attacker@example.test",
            purpose = "PinRecovery"
        });
        Assert.Equal(HttpStatusCode.BadRequest, replacementDestination.StatusCode);
        Assert.Equal(seeded.Email, delivery.SingleDestination);
    }

    [Fact]
    public async Task Phone_alias_update_requires_verified_unlocked_account_and_keeps_one_binding()
    {
        var delivery = new CaptureDelivery();
        await using var host = await Host(delivery);
        var seeded = await SeedAsync(host);
        using var anonymous = host.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/account/phone-alias", new { phone = "0911111111" })).StatusCode);
        var signedIn = await SignInAsync(host, seeded);
        using var current = Client(host, signedIn.AllCookies);
        var updated = await current.PostAsJsonAsync("/api/account/phone-alias", new { phone = "0911111111" });
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        await using var db = host.Database.Open();
        var alias = Assert.Single(await db.AuthIdentifiers.Where(x => x.UserId == seeded.UserId && x.Kind == "Phone").ToListAsync());
        Assert.Equal(HashIdentifier("+251911111111"), alias.IdentifierHash);
        Assert.Single(await db.IdentityBindings.Where(x => x.UserId == seeded.UserId).ToListAsync());
        Assert.Single(await db.AuditEvents.Where(x => x.ActorId == seeded.UserId && x.EventType == "PhoneAliasUpdated").ToListAsync());
    }

    [Fact]
    public async Task Locked_recovery_replaces_only_device_cookies_and_preserves_account_profile()
    {
        var delivery = new CaptureDelivery();
        await using var host = await Host(delivery);
        var seeded = await SeedAsync(host, includeCreator: true);
        var signedIn = await SignInAsync(host, seeded);
        await SetState(host, seeded.UserId, DeviceAccessStates.Locked);
        using var current = Client(host, signedIn.AllCookies);
        Assert.Equal((HttpStatusCode)423, (await current.GetAsync("/api/customer/offers")).StatusCode);

        var code = await StartAsync(current, delivery, seeded.Email, seeded.Email);
        var wrong = await current.Post("/api/device/pin-recovery/complete", new
        {
            identifier = seeded.Email, code = "999999", newPin = NewPin, confirmPin = NewPin
        }, "recover-wrong-code");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("InvalidCode", (await wrong.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        await AssertOldStateActive(host, seeded.UserId);

        var recovered = await current.Post("/api/device/pin-recovery/complete", new
        {
            identifier = seeded.Email, code, newPin = NewPin, confirmPin = NewPin
        }, "recover-success");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(DeviceAccessStates.Unlocked,
            (await recovered.Content.ReadFromJsonAsync<JsonObject>())!["state"]!.GetValue<string>());
        var setCookies = recovered.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Equal(2, setCookies.Length);
        var deviceCookie = Cookie(setCookies, DeviceCredentialCookie.Name);
        var sessionCookie = Cookie(setCookies, DeviceSessionCredentialCookie.DevelopmentName);
        Assert.DoesNotContain(setCookies, x => x.StartsWith("WeymelaV3.Session=", StringComparison.Ordinal));
        foreach (var header in setCookies)
        {
            var lower = header.ToLowerInvariant();
            Assert.Contains("httponly", lower);
            Assert.Contains("samesite=strict", lower);
            Assert.Contains("path=/", lower);
        }

        using var oldCredentials = Client(host, signedIn.AllCookies);
        Assert.Equal(DeviceAccessStates.FullAuthenticationRequired,
            (await oldCredentials.GetFromJsonAsync<JsonObject>("/api/device/access"))!["state"]!.GetValue<string>());
        using var replacement = Client(host, $"{signedIn.AuthCookie}; {deviceCookie}; {sessionCookie}");
        var session = await replacement.GetFromJsonAsync<JsonObject>("/api/session");
        Assert.Equal("Customer", session!["role"]!.GetValue<string>());
        Assert.Equal(seeded.CustomerId.ToString(), session["profiles"]!.AsArray()
            .Single(x => x!["role"]!.GetValue<string>() == "Customer")!["subjectId"]!.GetValue<string>());
        Assert.Contains(session["profiles"]!.AsArray(), x => x!["role"]!.GetValue<string>() == "Creator");

        await using var db = host.Database.Open();
        var activeDevice = Assert.Single(await db.AuthorizedDevices.Where(x => x.UserId == seeded.UserId
            && x.RevokedAtUtc == null).ToListAsync());
        var activeSession = Assert.Single(await db.DeviceSessions.Where(x => x.UserId == seeded.UserId
            && x.RevokedAtUtc == null).ToListAsync());
        Assert.True(await DevicePinVerifier.VerifyAsync(NewPin, activeDevice.PinVerifier!, PinPepper, default));
        Assert.False(await DevicePinVerifier.VerifyAsync(OldPin, activeDevice.PinVerifier!, PinPepper, default));
        Assert.Equal(activeDevice.EnrolledAtUtc.AddDays(30), activeDevice.ExpiresAtUtc);
        Assert.Equal(activeSession.CreatedAtUtc.AddHours(1), activeSession.ExpiresAtUtc);
        var customerNotice = Assert.Single(await db.InAppNotifications.Where(x => x.UserId == seeded.UserId
            && x.Role == ActorRole.Customer && x.EventType == "DevicePinRecovered").ToListAsync());
        Assert.Single(await db.InAppNotifications.Where(x => x.UserId == seeded.UserId
            && x.Role == ActorRole.Creator && x.EventType == "DevicePinRecovered").ToListAsync());
        Assert.DoesNotContain(code, customerNotice.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPin, customerNotice.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(deviceCookie, customerNotice.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sessionCookie, customerNotice.Message, StringComparison.Ordinal);

        var replay = await replacement.Post("/api/device/pin-recovery/complete", new
        {
            identifier = seeded.Email, code, newPin = NewPin, confirmPin = NewPin
        }, "recover-replay");
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("InvalidCode", (await replay.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(DeviceAccessStates.Locked)]
    [InlineData(DeviceAccessStates.Cooldown)]
    [InlineData(DeviceAccessStates.RecoveryRequired)]
    public async Task Reviewed_locked_states_can_complete_recovery(string state)
    {
        var delivery = new CaptureDelivery();
        await using var host = await Host(delivery);
        var seeded = await SeedAsync(host);
        var signedIn = await SignInAsync(host, seeded);
        await SetState(host, seeded.UserId, state);
        using var client = Client(host, signedIn.AllCookies);
        var code = await StartAsync(client, delivery, seeded.Email, seeded.Email);

        var response = await client.Post("/api/device/pin-recovery/complete", new
        {
            identifier = seeded.Email, code, newPin = NewPin, confirmPin = NewPin
        }, $"recover-{state}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Completion_requires_auth_recognized_device_allowed_state_and_idempotency_key()
    {
        var delivery = new CaptureDelivery();
        await using var host = await Host(delivery);
        var seeded = await SeedAsync(host);
        var signedIn = await SignInAsync(host, seeded);
        using var anonymous = host.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Post("/api/device/pin-recovery/complete",
            new { identifier = seeded.Email, code = "123456", newPin = NewPin, confirmPin = NewPin })).StatusCode);

        using var noDevice = Client(host, signedIn.AuthCookie);
        var enrollmentRequired = await noDevice.Post("/api/device/pin-recovery/complete",
            new { identifier = seeded.Email, code = "123456", newPin = NewPin, confirmPin = NewPin });
        Assert.Equal((HttpStatusCode)428, enrollmentRequired.StatusCode);

        using var noSession = Client(host, $"{signedIn.AuthCookie}; {signedIn.DeviceCookie}");
        var fullAuth = await noSession.Post("/api/device/pin-recovery/complete",
            new { identifier = seeded.Email, code = "123456", newPin = NewPin, confirmPin = NewPin });
        Assert.Equal(HttpStatusCode.Unauthorized, fullAuth.StatusCode);
        Assert.Contains("FullAuthenticationRequired", await fullAuth.Content.ReadAsStringAsync());

        await SetState(host, seeded.UserId, DeviceAccessStates.Locked);
        using var current = Client(host, signedIn.AllCookies);
        var code = await StartAsync(current, delivery, seeded.Email, seeded.Email);
        var missingReference = await current.PostAsJsonAsync("/api/device/pin-recovery/complete",
            new { identifier = seeded.Email, code, newPin = NewPin, confirmPin = NewPin });
        Assert.Equal(HttpStatusCode.BadRequest, missingReference.StatusCode);

        var mismatch = await current.Post("/api/device/pin-recovery/complete",
            new { identifier = seeded.Email, code, newPin = NewPin, confirmPin = "11111" });
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        await AssertOldStateActive(host, seeded.UserId);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-purpose")]
    public async Task Expired_and_wrong_purpose_codes_use_the_same_safe_invalid_code_response(string defect)
    {
        var delivery = new CaptureDelivery();
        await using var host = await Host(delivery);
        var seeded = await SeedAsync(host);
        var signedIn = await SignInAsync(host, seeded);
        await SetState(host, seeded.UserId, DeviceAccessStates.Locked);
        using var client = Client(host, signedIn.AllCookies);
        var code = await StartAsync(client, delivery, seeded.Email, seeded.Email);
        await using (var db = host.Database.Open())
        {
            var query = db.EmailAuthChallenges.Where(x => x.UserId == seeded.UserId
                && x.Purpose == EmailCodePurpose.PinRecovery.ToString());
            if (defect == "expired")
                await query.ExecuteUpdateAsync(update => update.SetProperty(x => x.ExpiresAtUtc,
                    DateTime.UtcNow.AddSeconds(-1)));
            else
                await query.ExecuteUpdateAsync(update => update.SetProperty(x => x.Purpose,
                    EmailCodePurpose.DeviceEnrollment.ToString()));
        }

        var response = await client.Post("/api/device/pin-recovery/complete", new
        {
            identifier = seeded.Email, code, newPin = NewPin, confirmPin = NewPin
        }, $"recover-{defect}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var safe = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("InvalidCode", safe!["code"]!.GetValue<string>());
        Assert.Equal("The code is invalid or expired.", safe["message"]!.GetValue<string>());
        await AssertOldStateActive(host, seeded.UserId);
    }

    [Fact]
    public async Task Concurrent_HTTP_completion_has_one_winner_and_one_active_replacement_pair()
    {
        var delivery = new CaptureDelivery();
        await using var host = await Host(delivery);
        var seeded = await SeedAsync(host);
        var signedIn = await SignInAsync(host, seeded);
        await SetState(host, seeded.UserId, DeviceAccessStates.Locked);
        using var first = Client(host, signedIn.AllCookies);
        using var second = Client(host, signedIn.AllCookies);
        var code = await StartAsync(first, delivery, seeded.Email, seeded.Email);

        var responses = await Task.WhenAll(
            first.Post("/api/device/pin-recovery/complete",
                new { identifier = seeded.Email, code, newPin = NewPin, confirmPin = NewPin }, "http-concurrent-a"),
            second.Post("/api/device/pin-recovery/complete",
                new { identifier = seeded.Email, code, newPin = NewPin, confirmPin = NewPin }, "http-concurrent-b"));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode != HttpStatusCode.OK);

        await using var db = host.Database.Open();
        Assert.Single(await db.AuthorizedDevices.Where(x => x.UserId == seeded.UserId
            && x.RevokedAtUtc == null).ToListAsync());
        Assert.Single(await db.DeviceSessions.Where(x => x.UserId == seeded.UserId
            && x.RevokedAtUtc == null).ToListAsync());
    }

    [Fact]
    public async Task Legacy_anonymous_reset_is_retired_and_cannot_change_credentials()
    {
        var delivery = new CaptureDelivery();
        await using var host = await Host(delivery);
        var seeded = await SeedAsync(host);
        _ = await SignInAsync(host, seeded);
        using var client = host.Anonymous();
        var response = await client.PostAsJsonAsync("/api/auth/pin/reset",
            new { email = seeded.Email, code = "123456", newPin = NewPin });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertOldStateActive(host, seeded.UserId);
    }

    [Fact]
    public async Task Password_credential_enrollment_and_phone_sign_in_reuse_the_existing_identity_without_email()
    {
        const string password = "a correct horse battery staple";
        var delivery = new CaptureDelivery();
        await using var host = await Host(delivery);
        var seeded = await SeedAsync(host);
        var signedIn = await SignInAsync(host, seeded);
        using var current = Client(host, signedIn.AllCookies);

        var localPhone = "0" + seeded.Phone[4..];
        var enrolled = await current.PostAsJsonAsync("/api/account/password-credential", new
        {
            phone = localPhone, password, confirmPassword = password
        });
        Assert.Equal(HttpStatusCode.NoContent, enrolled.StatusCode);
        var status = await current.GetFromJsonAsync<JsonObject>("/api/account/security");
        Assert.True(status!["passwordEnrolled"]!.GetValue<bool>());
        Assert.True(status["phoneEnrolled"]!.GetValue<bool>());

        using var anonymous = host.Anonymous();
        foreach (var phone in new[] { localPhone, seeded.Phone[4..], seeded.Phone })
        {
            var response = await anonymous.PostAsJsonAsync("/api/auth/password/sign-in", new { phone, password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            Assert.Equal("unused-in-recovery-start", body!["customToken"]!.GetValue<string>());
        }
        Assert.Equal(0, delivery.Count);

        await using var db = host.Database.Open();
        var credential = Assert.Single(await db.PasswordCredentials.Where(x => x.UserId == seeded.UserId).ToListAsync());
        Assert.DoesNotContain(password, credential.PasswordHash, StringComparison.Ordinal);
        Assert.Single(await db.IdentityBindings.Where(x => x.UserId == seeded.UserId).ToListAsync());
        Assert.Single(await db.AuthIdentifiers.Where(x => x.UserId == seeded.UserId && x.Kind == "Phone").ToListAsync());
    }

    [Fact]
    public async Task Wrong_and_unknown_password_sign_in_are_privacy_safe_and_password_reset_is_email_only()
    {
        const string oldPassword = "old correct horse battery staple";
        const string newPassword = "new correct horse battery staple";
        var delivery = new CaptureDelivery();
        await using var host = await Host(delivery);
        var seeded = await SeedAsync(host);
        var signedIn = await SignInAsync(host, seeded);
        using (var current = Client(host, signedIn.AllCookies))
        {
            var enrolled = await current.PostAsJsonAsync("/api/account/password-credential", new
            {
                phone = (string?)null, password = oldPassword, confirmPassword = oldPassword
            });
            Assert.Equal(HttpStatusCode.NoContent, enrolled.StatusCode);
        }

        using var anonymous = host.Anonymous();
        var wrong = await anonymous.PostAsJsonAsync("/api/auth/password/sign-in",
            new { phone = seeded.Phone, password = "wrong password value" });
        var unknown = await anonymous.PostAsJsonAsync("/api/auth/password/sign-in",
            new { phone = "+251922222222", password = "wrong password value" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());

        var phoneRecovery = await anonymous.PostAsJsonAsync("/api/auth/email/start", new
        {
            identifier = seeded.Phone, phone = (string?)null, purpose = "PasswordRecovery"
        });
        Assert.Equal(HttpStatusCode.BadRequest, phoneRecovery.StatusCode);

        var start = await anonymous.PostAsJsonAsync("/api/auth/email/start", new
        {
            identifier = seeded.Email, phone = (string?)null, purpose = "PasswordRecovery"
        });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal(seeded.Email, delivery.SingleDestination);
        Assert.Equal(EmailCodePurpose.PasswordRecovery, delivery.SinglePurpose);

        var verified = await anonymous.PostAsJsonAsync("/api/auth/password/recovery/verify", new
        {
            email = seeded.Email, code = delivery.SingleCode
        });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var verifiedBody = await verified.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(verifiedBody!["expiresAtUtc"]);
        Assert.Null(verifiedBody["recoveryGrant"]);
        var recoveryCookieHeader = Assert.Single(verified.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(PasswordRecoveryTransactionCookie.DevelopmentName + "=", recoveryCookieHeader);
        Assert.Contains("httponly", recoveryCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", recoveryCookieHeader, StringComparison.OrdinalIgnoreCase);
        anonymous.DefaultRequestHeaders.Add("Cookie", recoveryCookieHeader.Split(';')[0]);
        var invalidPassword = await anonymous.PostAsJsonAsync("/api/auth/password/reset", new
        {
            newPassword = "too short", confirmPassword = "too short"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPassword.StatusCode);
        Assert.False(invalidPassword.Headers.Contains("Set-Cookie"));
        var reset = await anonymous.PostAsJsonAsync("/api/auth/password/reset", new
        {
            newPassword, confirmPassword = newPassword
        });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Contains(reset.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith(PasswordRecoveryTransactionCookie.DevelopmentName + "=", StringComparison.Ordinal)
            && value.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/auth/password/sign-in",
                new { phone = seeded.Phone, password = oldPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await anonymous.PostAsJsonAsync("/api/auth/password/sign-in",
                new { phone = seeded.Phone, password = newPassword })).StatusCode);
        Assert.NotEqual(HttpStatusCode.NoContent,
            (await anonymous.PostAsJsonAsync("/api/auth/password/reset", new
            {
                newPassword, confirmPassword = newPassword
            })).StatusCode);

        await using var db = host.Database.Open();
        Assert.All(await db.DeviceSessions.Where(x => x.UserId == seeded.UserId).ToListAsync(),
            session => Assert.NotNull(session.RevokedAtUtc));
        Assert.Single(await db.IdentityBindings.Where(x => x.UserId == seeded.UserId).ToListAsync());
        Assert.Single(await db.CommercePermissions.Where(x => x.UserId == seeded.UserId).ToListAsync());
    }

    private async Task<ApiFixture> Host(CaptureDelivery delivery) => await ApiFixture.CreateAsync(fixture, builder =>
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["V3:Auth:FirebaseProjectId"] = Project,
            ["V3:Auth:PinPepper"] = PinPepper,
            ["V3:Auth:CodeHashKey"] = CodeKey
        });
        builder.Services.AddSingleton<IIdentityTokenVerifier>(new TestIdentity());
        builder.Services.AddSingleton<IEmailCodeDelivery>(delivery);
        builder.Services.AddSingleton<IFirebaseCustomTokenIssuer, EnabledTokenIssuer>();
        builder.Services.AddScoped<IWorkspaceDirectory, PersistentWorkspaceDirectory>();
    });

    private static async Task<Seeded> SeedAsync(ApiFixture host, bool includeCreator = false)
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var email = $"recover-{Guid.NewGuid():N}@example.test";
        var phone = "+2519" + Random.Shared.NextInt64(10_000_000, 99_999_999);
        var binding = new IdentityBinding
        {
            Provider = "Firebase", ProjectId = Project, ExternalSubject = Subject,
            UserId = userId, IsActive = true, ValidAfterUtc = now.AddMinutes(-1), Version = 1
        };
        var credential = OpaqueDeviceCredential.Create();
        var verifier = await DevicePinVerifier.HashAsync(OldPin, PinPepper, default);
        var device = DeviceAccessPolicy.NewDevice(userId, credential.Digest, verifier, now);
        await using var db = host.Database.Open();
        db.AddRange(binding, device,
            new AuthIdentifierRecord { UserId = userId, Kind = "Email", IdentifierHash = HashIdentifier(email), DeliveryAddress = email, IsVerified = true, CreatedAtUtc = now },
            new AuthIdentifierRecord { UserId = userId, Kind = "Phone", IdentifierHash = HashIdentifier(phone), IsVerified = false, CreatedAtUtc = now },
            new PublicWorkspaceProfile { SubjectId = customerId, Role = ActorRole.Customer, DisplayName = "Recovery Customer", PublicId = "CU-RECOVERY" },
            new CommercePermission(userId, ActorRole.Customer, customerId, null, true, false));
        if (includeCreator)
        {
            var creatorId = Guid.NewGuid();
            db.AddRange(new PublicWorkspaceProfile { SubjectId = creatorId, Role = ActorRole.Creator, DisplayName = "Recovery Creator", PublicId = "CR-RECOVERY" },
                new CommercePermission(userId, ActorRole.Creator, creatorId, null, true, false));
        }
        await db.SaveChangesAsync();
        return new(userId, customerId, email, phone, credential);
    }

    private static async Task<SignedIn> SignInAsync(ApiFixture host, Seeded seeded)
    {
        using var client = Client(host, $"{DeviceCredentialCookie.Name}={seeded.DeviceCredential.Value}");
        var response = await client.PostAsJsonAsync("/api/auth/firebase/session", new
        {
            idToken = "verified-recovery-user", profileRole = "Customer", profileSubjectId = seeded.CustomerId
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookies = response.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0]).ToArray();
        return new(Assert.Single(cookies, x => x.StartsWith("WeymelaV3.Session=", StringComparison.Ordinal)),
            $"{DeviceCredentialCookie.Name}={seeded.DeviceCredential.Value}",
            Assert.Single(cookies, x => x.StartsWith(DeviceSessionCredentialCookie.DevelopmentName + "=", StringComparison.Ordinal)));
    }

    private static async Task<string> StartAsync(HttpClient client, CaptureDelivery delivery,
        string identifier, string expectedDestination)
    {
        var response = await client.PostAsJsonAsync("/api/auth/email/start", new
        {
            identifier, phone = (string?)null, purpose = "PinRecovery"
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(expectedDestination, delivery.SingleDestination);
        return delivery.SingleCode;
    }

    private static async Task SetState(ApiFixture host, Guid userId, string state)
    {
        await using var db = host.Database.Open();
        var session = await db.DeviceSessions.SingleAsync(x => x.UserId == userId && x.RevokedAtUtc == null);
        var device = await db.AuthorizedDevices.SingleAsync(x => x.UserId == userId && x.RevokedAtUtc == null);
        session.LastActivityAtUtc = DateTime.UtcNow.AddMinutes(-21);
        session.LockedAtUtc = null;
        if (state == DeviceAccessStates.Cooldown)
        {
            device.FailedAttempts = 5;
            device.LockedUntilUtc = DateTime.UtcNow.AddMinutes(15);
        }
        if (state == DeviceAccessStates.RecoveryRequired)
        {
            device.FailedAttempts = 10;
            device.LockedUntilUtc = null;
            device.RequiresRecovery = true;
        }
        session.Version++;
        device.Version++;
        await db.SaveChangesAsync();
    }

    private static async Task AssertOldStateActive(ApiFixture host, Guid userId)
    {
        await using var db = host.Database.Open();
        Assert.Single(await db.AuthorizedDevices.Where(x => x.UserId == userId && x.RevokedAtUtc == null).ToListAsync());
        Assert.Single(await db.DeviceSessions.Where(x => x.UserId == userId && x.RevokedAtUtc == null).ToListAsync());
    }

    private static async Task<CredentialSnapshot> CredentialState(ApiFixture host, Guid userId)
    {
        await using var db = host.Database.Open();
        var device = await db.AuthorizedDevices.AsNoTracking().SingleAsync(x => x.UserId == userId);
        var sessions = await db.DeviceSessions.AsNoTracking().Where(x => x.UserId == userId).ToListAsync();
        return new(device.CredentialIdHash, device.PinVerifier, device.FailedAttempts,
            device.LockedUntilUtc, device.RequiresRecovery, device.RevokedAtUtc, device.Version,
            string.Join(";", sessions.Select(x => $"{x.SessionIdentifierHash}|{x.LastActivityAtUtc:O}|{x.LockedAtUtc:O}|{x.RevokedAtUtc:O}|{x.Version}").Order()));
    }

    private static string Cookie(string[] headers, string name) =>
        Assert.Single(headers, x => x.StartsWith(name + "=", StringComparison.Ordinal)).Split(';')[0];

    private static string HashIdentifier(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static HttpClient Client(ApiFixture host, string cookies)
    {
        var client = host.Anonymous();
        client.DefaultRequestHeaders.Add("Cookie", cookies);
        return client;
    }

    private sealed record Seeded(Guid UserId, Guid CustomerId, string Email, string Phone,
        OpaqueDeviceCredential DeviceCredential);
    private sealed record CredentialSnapshot(string DeviceDigest, string? PinVerifier,
        int FailedAttempts, DateTime? LockedUntilUtc, bool RequiresRecovery,
        DateTime? DeviceRevokedAtUtc, long DeviceVersion, string Sessions);
    private sealed record SignedIn(string AuthCookie, string DeviceCookie, string DeviceSessionCookie)
    {
        public string AllCookies => $"{AuthCookie}; {DeviceCookie}; {DeviceSessionCookie}";
    }

    private sealed class CaptureDelivery : IEmailCodeDelivery
    {
        private readonly List<(string Destination, string Code, EmailCodePurpose Purpose)> sent = [];
        public bool Enabled => true;
        public int Count => sent.Count;
        public string SingleDestination => Assert.Single(sent).Destination;
        public string SingleCode => Assert.Single(sent).Code;
        public EmailCodePurpose SinglePurpose => Assert.Single(sent).Purpose;
        public Task SendAsync(string destination, string code, EmailCodePurpose purpose, CancellationToken ct)
        {
            sent.Add((destination, code, purpose));
            return Task.CompletedTask;
        }
    }

    private sealed class EnabledTokenIssuer : IFirebaseCustomTokenIssuer
    {
        public bool Enabled => true;
        public Task<FirebaseCustomTokenResult> IssueAsync(Guid userId, string projectId, CancellationToken ct)
            => Task.FromResult(new FirebaseCustomTokenResult("unused-in-recovery-start", DateTime.UtcNow.AddMinutes(5)));
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
