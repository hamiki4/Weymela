using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Weymela.Api;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Tests;
using Weymela.Application.Web;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class OnboardingHttpTests(PostgresFixture fixture)
{
    private static readonly string PinPepper = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task Development_firebase_session_projects_all_persisted_active_profiles()
    {
        var userId = Guid.NewGuid();
        var customerSubject = Guid.NewGuid();
        var creatorSubject = Guid.NewGuid();
        await using var host = await ApiFixture.CreateAsync(fixture, builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["V3:Auth:FirebaseProjectId"] = "isolated-v3-test"
            });
            builder.Services.AddSingleton<IIdentityTokenVerifier>(new MultiProfileIdentity());
            builder.Services.AddScoped<IWorkspaceDirectory, PersistentWorkspaceDirectory>();
        });
        await using (var db = host.Database.Open())
        {
            db.IdentityBindings.Add(new IdentityBinding
            {
                Provider = "Firebase", ProjectId = "isolated-v3-test", ExternalSubject = "multi-profile-id",
                UserId = userId, IsActive = true, ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1), Version = 1
            });
            db.PublicWorkspaceProfiles.AddRange(
                new PublicWorkspaceProfile { SubjectId = customerSubject, Role = ActorRole.Customer, DisplayName = "Customer", PublicId = "CU-TEST" },
                new PublicWorkspaceProfile { SubjectId = creatorSubject, Role = ActorRole.Creator, DisplayName = "Creator", PublicId = "CR-TEST", Region = "Addis", Category = "Food" });
            db.CommercePermissions.AddRange(
                new CommercePermission(userId, ActorRole.Customer, customerSubject, null, true, false),
                new CommercePermission(userId, ActorRole.Creator, creatorSubject, null, true, false));
            await db.SaveChangesAsync();
        }

        using var client = host.Anonymous();
        var signIn = await client.PostAsJsonAsync("/api/auth/firebase/session", new
        {
            idToken = "valid-multi-profile-identity", profileRole = "Customer", profileSubjectId = customerSubject
        });
        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", signIn.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        var session = await client.GetFromJsonAsync<JsonObject>("/api/session");
        Assert.Equal("Customer", session!["role"]!.GetValue<string>());
        Assert.Equal("Customer:" + customerSubject.ToString("D") + ":-", session["activeProfileKey"]!.GetValue<string>());
        Assert.Equal(new[] { "Creator", "Customer" }, session["profiles"]!.AsArray().Select(x => x!["role"]!.GetValue<string>()).OrderBy(x => x).ToArray());

        await using (var db = host.Database.Open())
        {
            await db.CommercePermissions.Where(x => x.UserId == userId && x.Role == ActorRole.Creator)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        }
        var afterSuspension = await client.GetFromJsonAsync<JsonObject>("/api/session");
        Assert.Single(afterSuspension!["profiles"]!.AsArray());
        Assert.Equal("Customer", afterSuspension["profiles"]![0]!["role"]!.GetValue<string>());
    }

    [Fact]
    public async Task Zero_profile_firebase_session_exposes_no_synthetic_customer_profile()
    {
        var userId = Guid.NewGuid();
        await using var host = await ApiFixture.CreateAsync(fixture, builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["V3:Auth:FirebaseProjectId"] = "isolated-v3-test"
            });
            builder.Services.AddSingleton<IIdentityTokenVerifier>(new FakeIdentity());
        });
        await using (var db = host.Database.Open())
        {
            db.IdentityBindings.Add(new IdentityBinding
            {
                Provider = "Firebase", ProjectId = "isolated-v3-test", ExternalSubject = "external-id",
                UserId = userId, IsActive = true, ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1), Version = 1
            });
            await db.SaveChangesAsync();
        }

        using var client = host.Anonymous();
        var signIn = await client.PostAsJsonAsync("/api/auth/firebase/session", new { idToken = "valid-test-identity" });
        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", signIn.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        var session = await client.GetFromJsonAsync<JsonObject>("/api/session");
        Assert.Equal("Onboarding", session!["role"]!.GetValue<string>());
        Assert.Empty(session["profiles"]!.AsArray());
        var legal = await client.GetAsync("/api/onboarding/legal");
        Assert.Equal(HttpStatusCode.OK, legal.StatusCode);
        var legalBody = await legal.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(new[] { "PrivacyPolicy", "TermsOfService" }, legalBody!["documents"]!.AsArray()
            .Select(x => x!["kind"]!.GetValue<string>()).OrderBy(x => x).ToArray());
        var workspace = await client.GetAsync("/api/customer/offers");
        Assert.NotEqual(HttpStatusCode.OK, workspace.StatusCode);
    }

    [Fact]
    public async Task Customer_onboarding_generates_the_identifier_and_persists_one_typed_profile()
    {
        var userId = Guid.NewGuid();
        await using var host = await ApiFixture.CreateAsync(fixture, builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["V3:Auth:FirebaseProjectId"] = "isolated-v3-test",
                ["V3:Auth:PinPepper"] = PinPepper
            });
            builder.Services.AddSingleton<IIdentityTokenVerifier>(new CustomerOnboardingIdentity());
            builder.Services.AddScoped<IWorkspaceDirectory, PersistentWorkspaceDirectory>();
        });
        await using (var db = host.Database.Open())
        {
            db.IdentityBindings.Add(new IdentityBinding
            {
                Provider = "Firebase", ProjectId = "isolated-v3-test", ExternalSubject = "customer-onboarding-id",
                UserId = userId, IsActive = true, ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1), Version = 1
            });
            db.AuthIdentifiers.Add(new AuthIdentifierRecord
            {
                UserId = userId,
                Kind = "Email",
                IdentifierHash = Guid.NewGuid().ToString("N"),
                IsVerified = true,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var client = host.Anonymous();
        var signIn = await client.PostAsJsonAsync("/api/auth/firebase/session", new { idToken = "customer-onboarding-token" });
        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        var authCookie = signIn.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        client.DefaultRequestHeaders.Add("Cookie", authCookie);
        var enrolled = await client.Post("/api/device/enrollment",
            new { pin = "01234", confirmPin = "01234" }, "customer-device-enrollment");
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        var deviceCookies = enrolled.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';')[0]).ToArray();
        Assert.Equal(2, deviceCookies.Length);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", new[] { authCookie }.Concat(deviceCookies)));
        var legal = await client.GetFromJsonAsync<JsonObject>("/api/onboarding/legal");
        Assert.True(legal!["available"]!.GetValue<bool>());
        var terms = legal["documents"]!.AsArray().Single(x => x!["kind"]!.GetValue<string>() == "TermsOfService")!;
        var privacy = legal["documents"]!.AsArray().Single(x => x!["kind"]!.GetValue<string>() == "PrivacyPolicy")!;

        var response = await client.Post("/api/onboarding/profile", new
        {
            role = "Customer",
            displayName = "Hana",
            publicId = "CLIENT-CONTROLLED",
            accountLegal = new
            {
                termsOfService = new { documentId = terms["documentId"]!.GetValue<Guid>(), contentHash = terms["contentHash"]!.GetValue<string>(), accepted = true },
                privacyPolicy = new { documentId = privacy["documentId"]!.GetValue<Guid>(), contentHash = privacy["contentHash"]!.GetValue<string>(), accepted = true }
            }
        }, "customer-http-idempotency");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var generated = body!["publicId"]!.GetValue<string>();
        Assert.StartsWith("CU-", generated, StringComparison.Ordinal);
        Assert.NotEqual("CLIENT-CONTROLLED", generated);

        await using var verify = host.Database.Open();
        var profile = await verify.CustomerProfiles.SingleAsync(x => x.UserId == userId);
        var projection = await verify.PublicWorkspaceProfiles.SingleAsync(x => x.SubjectId == profile.CustomerId);
        Assert.Equal("Hana", profile.PreferredName);
        Assert.Equal(generated, projection.PublicId);
        Assert.Single(await verify.CommercePermissions.Where(x => x.UserId == userId && x.Role == ActorRole.Customer).ToListAsync());
    }

    private sealed class FakeIdentity : IIdentityTokenVerifier
    {
        public Task<VerifiedIdentity> VerifyAsync(string _, CancellationToken __)
            => Task.FromResult(new VerifiedIdentity("Firebase", "isolated-v3-test", "external-id", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30)));
    }

    private sealed class MultiProfileIdentity : IIdentityTokenVerifier
    {
        public Task<VerifiedIdentity> VerifyAsync(string _, CancellationToken __)
            => Task.FromResult(new VerifiedIdentity("Firebase", "isolated-v3-test", "multi-profile-id", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30)));
    }

    private sealed class CustomerOnboardingIdentity : IIdentityTokenVerifier
    {
        public Task<VerifiedIdentity> VerifyAsync(string _, CancellationToken __)
            => Task.FromResult(new VerifiedIdentity("Firebase", "isolated-v3-test", "customer-onboarding-id", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30)));
    }
}
