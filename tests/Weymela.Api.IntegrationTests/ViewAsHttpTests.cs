using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Weymela.Application;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class ViewAsHttpTests(PostgresFixture postgres)
{
    private const string SupportCookieName = "WeymelaV3.SupportSession";

    [Theory]
    [InlineData(2, "Business", "/api/business/home")]
    [InlineData(4, "Creator", "/api/creator/home")]
    [InlineData(7, "Customer", "/api/customer/offers")]
    [InlineData(11, "OperationsAdmin", "/api/admin/operations/home")]
    public async Task Active_platform_admin_can_start_each_allowed_view_and_read_the_viewed_workspace(
        int targetUserNumber, string viewedRole, string readPath)
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        using var client = await f.Login("admin");

        var response = await StartAsync(client, DevelopmentDirectory.Id(targetUserNumber), "start-" + viewedRole);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal(viewedRole, body["viewedRole"]!.GetValue<string>());
        Assert.Equal(DevelopmentDirectory.Id(targetUserNumber).ToString(), body["viewedUserId"]!.GetValue<string>());

        var current = await client.GetJson("/api/admin/view-as/current");
        Assert.Equal(body["supportSessionId"]!.ToJsonString(), current["supportSessionId"]!.ToJsonString());
        Assert.Equal((int)HttpStatusCode.OK, (int)(await client.GetAsync(readPath)).StatusCode);
    }

    [Theory]
    [InlineData("admin", 1)]
    [InlineData("admin", 9)]
    [InlineData("operations-admin", 7)]
    [InlineData("business", 7)]
    [InlineData("creator", 7)]
    [InlineData("customer", 7)]
    [InlineData("cashier", 7)]
    public async Task View_as_start_rejects_forbidden_target_roles_and_non_platform_real_actors(string persona, int targetUserNumber)
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        using var client = await f.Login(persona);

        var response = await StartAsync(client, DevelopmentDirectory.Id(targetUserNumber), "forbidden-" + persona);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Inactive_platform_admin_cannot_start_view_as()
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        await using (var db = f.Database.Open())
        {
            var permission = await db.CommercePermissions.SingleAsync(x => x.UserId == DevelopmentDirectory.Id(1)
                && x.Role == ActorRole.PlatformAdmin);
            permission.IsActive = false;
            await db.SaveChangesAsync();
        }

        using var client = await f.Login("admin");
        var response = await StartAsync(client, DevelopmentDirectory.Id(7), "inactive-admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task View_as_does_not_replace_real_actor_and_blocks_direct_high_risk_commands()
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        using var client = await f.Login("admin");
        var response = await StartAsync(client, DevelopmentDirectory.Id(7), "identity-check");
        var sessionId = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["supportSessionId"]!.GetValue<Guid>();

        var session = await client.GetJson("/api/session");
        Assert.Equal("Customer", session["role"]!.GetValue<string>());

        foreach (var attempt in new[]
        {
            ("/api/admin/accounts", (object)new { email = "verified@example.com", role = "OperationsAdmin" }),
            ($"/api/admin/accounts/{DevelopmentDirectory.Id(7):D}/lifecycle", (object)new { action = "suspend", reason = "blocked" }),
            ("/api/admin/financial-settings", (object)new { expectedVersion = 1 }),
            ($"/api/admin/payouts/Creator/{DevelopmentDirectory.Id(300):D}/prepare", (object)new { }),
            ($"/api/admin/payouts/{Guid.NewGuid():D}/paid", (object)new { reference = "blocked" }),
            ("/api/admin/platform/settlements", (object)new { amount = 1, reference = "blocked" }),
            ($"/api/admin/deposit-requests/{Guid.NewGuid():D}/review", (object)new { approve = true, confirmationReference = "blocked", expectedVersion = 1L }),
            ($"/api/admin/role-enrollments/{Guid.NewGuid():D}/review", (object)new { approve = true, reason = "blocked", expectedVersion = 1L }),
            ("/api/account/password-credential", (object)new { phone = "+251911111111", password = "NoPasswordChange123!", confirmPassword = "NoPasswordChange123!" }),
            ("/api/account/phone-alias", (object)new { phone = "+251922222222" })
        })
        {
            var blocked = await client.Post(attempt.Item1, attempt.Item2, "blocked-" + Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        }

        await using var db = f.Database.Open();
        var events = await db.AuditEvents.Where(x => x.SupportSessionId == sessionId).ToListAsync();
        Assert.Contains(events, x => x.EventType == "ViewAsStarted" && x.ActorId == DevelopmentDirectory.Id(1)
            && x.TargetUserId == DevelopmentDirectory.Id(7) && x.TargetRole == ActorRole.Customer);
        Assert.Contains(events, x => x.EventType == "ViewAsActionBlocked" && x.ActorId == DevelopmentDirectory.Id(1));
        Assert.DoesNotContain(events, x => x.Detail.Contains("password", StringComparison.OrdinalIgnoreCase)
            || x.Detail.Contains("token", StringComparison.OrdinalIgnoreCase)
            || x.Reason?.Contains("password", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Operations_admin_view_is_restricted_to_b3_operational_reads()
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        using var client = await f.Login("admin");
        await StartAsync(client, DevelopmentDirectory.Id(11), "operations-read");

        foreach (var path in new[]
        {
            "/api/admin/operations", "/api/admin/operations/home", "/api/admin/businesses",
            "/api/admin/creators", "/api/admin/customers", "/api/admin/campaigns", "/api/admin/ugc",
            "/api/admin/payouts", "/api/admin/notifications", "/api/admin/role-enrollments",
            "/api/admin/deposit-requests"
        })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);

        foreach (var path in new[]
        {
            "/api/admin/home", "/api/admin/accounts", "/api/admin/financial-settings",
            "/api/admin/platform", "/api/admin/audit", "/api/admin/reconciliation"
        })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task View_as_is_idempotent_for_same_start_key_and_rejects_a_second_active_target()
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        using var client = await f.Login("admin");

        var first = await StartAsync(client, DevelopmentDirectory.Id(7), "idempotent");
        var firstBody = JsonNode.Parse(await first.Content.ReadAsStringAsync())!;
        var replay = await StartAsync(client, DevelopmentDirectory.Id(7), "idempotent");
        var replayBody = JsonNode.Parse(await replay.Content.ReadAsStringAsync())!;
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(firstBody["supportSessionId"]!.ToJsonString(), replayBody["supportSessionId"]!.ToJsonString());

        var second = await StartAsync(client, DevelopmentDirectory.Id(4), "different-target");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task End_is_server_side_immediate_and_reuse_is_rejected()
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        using var client = await f.Login("admin");
        var started = await StartAsync(client, DevelopmentDirectory.Id(4), "end-test");
        var sessionId = JsonNode.Parse(await started.Content.ReadAsStringAsync())!["supportSessionId"]!.GetValue<Guid>();

        var ended = await client.Post("/api/admin/view-as/end", new { }, "end-test");
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/view-as/current")).StatusCode);

        RemoveSupportCookie(client);
        var normal = await client.GetAsync("/api/admin/home");
        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);

        await using var db = f.Database.Open();
        var events = await db.AuditEvents.Where(x => x.SupportSessionId == sessionId).ToListAsync();
        Assert.Contains(events, x => x.EventType == "ViewAsEnded" && x.ActorId == DevelopmentDirectory.Id(1)
            && x.TargetUserId == DevelopmentDirectory.Id(4) && x.TargetRole == ActorRole.Creator);
    }

    [Fact]
    public async Task Expired_unknown_and_malformed_sessions_are_rejected_server_side()
    {
        var clock = new MutableClock();
        await using var f = await ApiFixture.CreateAsync(postgres, builder => builder.Services.AddSingleton<TimeProvider>(clock));
        using var client = await f.Login("admin");
        var started = await StartAsync(client, DevelopmentDirectory.Id(7), "expiry-test");
        var sessionId = JsonNode.Parse(await started.Content.ReadAsStringAsync())!["supportSessionId"]!.GetValue<Guid>();
        clock.Now = clock.Now.AddMinutes(16);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customer/offers")).StatusCode);

        using var unknown = await f.Login("admin");
        SetSupportCookie(unknown, SupportCookie(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, (await unknown.GetAsync("/api/admin/view-as/current")).StatusCode);
        using var malformed = await f.Login("admin");
        SetSupportCookie(malformed, SupportCookieName + "=not-a-guid");
        Assert.Equal(HttpStatusCode.Forbidden, (await malformed.GetAsync("/api/admin/view-as/current")).StatusCode);
    }

    [Fact]
    public async Task Client_cannot_submit_trusted_viewed_role_or_scope_fields()
    {
        await using var f = await ApiFixture.CreateAsync(postgres);
        using var client = await f.Login("admin");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/view-as/start")
        {
            Content = JsonContent.Create(new
            {
                viewedUserId = DevelopmentDirectory.Id(7), viewedRole = "PlatformAdmin",
                viewedCustomerId = DevelopmentDirectory.Id(7), reason = "forged"
            })
        };
        request.Headers.Add("Idempotency-Key", "forged-fields");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> StartAsync(HttpClient client, Guid target, string key)
    {
        var response = await client.Post("/api/admin/view-as/start", new { viewedUserId = target, reason = "B4 test" }, key);
        if (response.IsSuccessStatusCode)
        {
            var cookie = response.Headers.GetValues("Set-Cookie")
                .Single(x => x.StartsWith(SupportCookieName + "=", StringComparison.Ordinal))
                .Split(';', 2)[0];
            SetSupportCookie(client, cookie);
        }
        return response;
    }

    private static string SupportCookie(Guid id) => SupportCookieName + "=" + id.ToString("D");

    private static void SetSupportCookie(HttpClient client, string cookie)
    {
        RemoveSupportCookie(client);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
    }

    private static void RemoveSupportCookie(HttpClient client)
    {
        if (!client.DefaultRequestHeaders.TryGetValues("Cookie", out var values)) return;
        var retained = string.Join("; ", values.SelectMany(x => x.Split(';'))
            .Select(x => x.Trim())
            .Where(x => !x.StartsWith(SupportCookieName + "=", StringComparison.Ordinal)));
        client.DefaultRequestHeaders.Remove("Cookie");
        if (!string.IsNullOrWhiteSpace(retained)) client.DefaultRequestHeaders.Add("Cookie", retained);
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTime Now { get; set; } = DateTime.UtcNow;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
