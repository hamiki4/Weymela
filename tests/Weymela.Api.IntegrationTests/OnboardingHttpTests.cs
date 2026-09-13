using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Weymela.Api;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class OnboardingHttpTests(PostgresFixture fixture)
{
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
    }

    private sealed class FakeIdentity : IIdentityTokenVerifier
    {
        public Task<VerifiedIdentity> VerifyAsync(string _, CancellationToken __)
            => Task.FromResult(new VerifiedIdentity("Firebase", "isolated-v3-test", "external-id", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30)));
    }
}
