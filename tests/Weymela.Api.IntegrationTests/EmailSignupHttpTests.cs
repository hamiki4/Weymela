using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class EmailSignupHttpTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Create_account_has_one_public_response_for_new_and_existing_mailboxes()
    {
        var delivery = new CaptureDelivery();
        await using var host = await Host(delivery);
        var existingEmail = $"existing-{Guid.NewGuid():N}@example.test";
        var newEmail = $"new-{Guid.NewGuid():N}@example.test";
        var existingUserId = Guid.NewGuid();
        await using (var db = host.Database.Open())
        {
            db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = existingUserId,
                Kind = "Email", IdentifierHash = Hash(existingEmail), DeliveryAddress = existingEmail,
                IsVerified = true, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        using var client = host.Anonymous();
        var existing = await Start(client, $" {existingEmail.ToUpperInvariant()} ");
        var fresh = await Start(client, newEmail);
        Assert.Equal(HttpStatusCode.Accepted, existing.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, fresh.StatusCode);
        var existingBody = (await existing.Content.ReadFromJsonAsync<JsonObject>())!;
        var freshBody = (await fresh.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(existingBody.Select(x => x.Key).Order(), freshBody.Select(x => x.Key).Order());
        Assert.True(existingBody["accepted"]!.GetValue<bool>());
        Assert.True(freshBody["accepted"]!.GetValue<bool>());
        Assert.Equal(60, existingBody["resendAfterSeconds"]!.GetValue<int>());
        Assert.Equal(60, freshBody["resendAfterSeconds"]!.GetValue<int>());
        Assert.DoesNotContain(existingEmail, existingBody.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DeviceEnrollment", existingBody.ToJsonString());
        Assert.Equal(EmailCodePurpose.DeviceEnrollment, delivery.Sent[0].Purpose);
        Assert.Equal(EmailCodePurpose.Signup, delivery.Sent[1].Purpose);

        var verified = await client.PostAsJsonAsync("/api/auth/email/verify", new
        {
            identifier = existingEmail, purpose = "Signup", code = delivery.Sent[0].Code
        });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        await using var check = host.Database.Open();
        Assert.Single(await check.AuthIdentifiers.Where(x => x.IdentifierHash == Hash(existingEmail)).ToListAsync());
        Assert.Empty(await check.CommercePermissions.Where(x => x.UserId == existingUserId).ToListAsync());
    }

    [Fact]
    public async Task Delivery_failure_does_not_expose_provider_error()
    {
        await using var host = await Host(new FailingDelivery());
        using var client = host.Anonymous();
        var response = await Start(client, $"provider-{Guid.NewGuid():N}@example.test");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private provider error", body);
        Assert.DoesNotContain("Firebase", body);
    }

    private async Task<ApiFixture> Host(IEmailCodeDelivery delivery) => await ApiFixture.CreateAsync(fixture, builder =>
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["V3:Auth:FirebaseProjectId"] = "isolated-v3-test",
            ["V3:Auth:CodeHashKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });
        builder.Services.AddSingleton(delivery);
        builder.Services.AddSingleton<IFirebaseCustomTokenIssuer>(new TestIssuer());
    });

    private static Task<HttpResponseMessage> Start(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/email/start", new { identifier = email, purpose = "Signup" });
    private static string Hash(string email) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()))).ToLowerInvariant();

    private sealed class CaptureDelivery : IEmailCodeDelivery
    {
        public bool Enabled => true;
        public List<(string Destination, string Code, EmailCodePurpose Purpose)> Sent { get; } = [];
        public Task SendAsync(string destination, string code, EmailCodePurpose purpose, CancellationToken ct)
        { Sent.Add((destination, code, purpose)); return Task.CompletedTask; }
    }
    private sealed class FailingDelivery : IEmailCodeDelivery
    {
        public bool Enabled => true;
        public Task SendAsync(string destination, string code, EmailCodePurpose purpose, CancellationToken ct)
            => throw new AuthChallengeUnavailableException("private provider error");
    }
    private sealed class TestIssuer : IFirebaseCustomTokenIssuer
    {
        public bool Enabled => true;
        public Task<FirebaseCustomTokenResult> IssueAsync(Guid userId, string projectId, CancellationToken ct)
            => Task.FromResult(new FirebaseCustomTokenResult("test-token", DateTime.UtcNow.AddMinutes(5)));
    }
}
