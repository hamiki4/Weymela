using System.Net;
using System.Buffers.Binary;
using Weymela.Api;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class ReceiptDepositHttpTests(PostgresFixture fixture)
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");

    // Valid PNG with a CRC-checked ancillary chunk; exact byte boundaries without large image dimensions.
    private static byte[] SizedPng(int size)
    {
        var payload = new byte[size - Png.Length - 12];
        Array.Fill(payload, (byte)'x');
        "Comment\0"u8.CopyTo(payload);
        var chunk = new byte[payload.Length + 12];
        BinaryPrimitives.WriteInt32BigEndian(chunk, payload.Length);
        "tEXt"u8.CopyTo(chunk.AsSpan(4));
        payload.CopyTo(chunk, 8);
        uint crc = 0xffffffff;
        foreach (var value in chunk.AsSpan(4, payload.Length + 4))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ~crc);
        return [.. Png[..^12], .. chunk, .. Png[^12..]];
    }

    [Theory]
    [InlineData(65752)]
    [InlineData(4 * 1024 * 1024)]
    public async Task Valid_receipt_transport_boundaries_are_accepted_by_api(int size)
    {
        var bytes = SizedPng(size);
        Assert.True(PrivateReceiptStore.ValidImage(bytes, "image/png"));
        var (host, directory) = await Host(fixture);
        try { await using (host) { using var business = await host.Login("business");
            Assert.Equal(HttpStatusCode.OK, (await Submit(business, bytes: bytes)).StatusCode); } }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<(ApiFixture Host, string Directory)> Host(PostgresFixture fixture)
    {
        var directory = Path.Combine(Path.GetTempPath(), "weymela-receipt-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var host = await ApiFixture.CreateAsync(fixture, b => b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["V3:Deposits:Mode"] = "ManualApproval", ["V3:Deposits:ReceiptDirectory"] = directory }));
        return (host, directory);
    }
    private static async Task<HttpResponseMessage> Submit(HttpClient client, decimal? amount = 10000m, byte[]? bytes = null,
        string media = "image/png", string? key = null, bool attach = true)
    {
        using var form = new MultipartFormDataContent();
        if (amount is not null) form.Add(new StringContent(amount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), "amount");
        if (attach)
        {
            var file = new ByteArrayContent(bytes ?? Png);
            file.Headers.ContentType = new MediaTypeHeaderValue(media);
            form.Add(file, "receipt", "receipt.png");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/business/deposit-requests") { Content = form };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }
    private static async Task<decimal> Available(ApiFixture host)
    {
        await using var db = host.Database.Open();
        return (await db.BusinessWallets.AsNoTracking().SingleAsync(x => x.BusinessId == Weymela.Infrastructure.Development.DevelopmentDirectory.Id(100))).AvailableBalance.Amount;
    }

    [Fact]
    public async Task Jpeg_receipt_is_accepted_and_mislabeled_png_is_rejected()
    {
        var (host, directory) = await Host(fixture);
        try
        {
            await using (host)
            {
                using var business = await host.Login("business");
                Assert.Equal(HttpStatusCode.OK, (await Submit(business, bytes: CreatorPhotoHttpTests.Jpeg, media: "image/jpeg")).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await Submit(business, media: "image/jpeg")).StatusCode);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact] public async Task Frozen_financial_writes_reject_receipt_before_storage_with_typed_response()
    {
        var directory = Path.Combine(Path.GetTempPath(), "weymela-frozen-receipt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var host = await ApiFixture.CreateAsync(fixture, b => b.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["V3:Deposits:Mode"] = "ManualApproval",
                    ["V3:Deposits:ReceiptDirectory"] = directory, ["V3:FinancialWritesEnabled"] = "false" }));
            using var business = await host.Login("business");
            var response = await Submit(business, bytes: SizedPng(65752));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var error = await response.Content.ReadFromJsonAsync<JsonNode>();
            Assert.Equal("FinancialWritesPaused", error?["code"]?.GetValue<string>());
            Assert.Empty(Directory.GetFiles(directory));
            await using var db = host.Database.Open();
            Assert.False(await db.DepositRequests.AnyAsync());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact] public async Task Receipt_submission_is_pending_private_idempotent_and_approval_credits_once()
    {
        var (host, directory) = await Host(fixture);
        try
        {
            await using (host)
            {
                using var business = await host.Login("business"); using var admin = await host.Login("admin");
                var before = await Available(host);
                const string key = "receipt-submit-once";
                var first = await Submit(business, key: key);
                Assert.Equal(HttpStatusCode.OK, first.StatusCode);
                var id = (await first.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
                Assert.Equal(id, (await (await Submit(business, key: key)).Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>());
                Assert.Equal(before, await Available(host));
                var own = await business.GetJson("/api/business/deposit-requests");
                Assert.Equal("Pending", own[0]!["status"]!.GetValue<string>());
                Assert.DoesNotContain("proofReference", own.ToJsonString(), StringComparison.OrdinalIgnoreCase);
                var pending = await admin.GetJson("/api/admin/deposit-requests");
                Assert.Contains(pending.AsArray(), x => x!["id"]!.GetValue<Guid>() == id);
                Assert.DoesNotContain("proofReference", pending.ToJsonString(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("r_", Directory.GetFiles(directory).Single());
                var ownReceipt = await business.GetAsync($"/api/business/deposit-requests/{id}/receipt");
                Assert.Equal(HttpStatusCode.OK, ownReceipt.StatusCode);
                Assert.Equal("image/png", ownReceipt.Content.Headers.ContentType!.MediaType);
                Assert.True(ownReceipt.Headers.CacheControl!.NoStore);
                Assert.Equal(Png, await ownReceipt.Content.ReadAsByteArrayAsync());
                Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/admin/deposit-requests/{id}/receipt")).StatusCode);

                var review = await admin.PostJson($"/api/admin/deposit-requests/{id}/review", new { approve = true, expectedVersion = 0, confirmationReference = "BANK-10000" }, "review-once");
                Assert.Equal(id, review["id"]!.GetValue<Guid>());
                Assert.Equal(id, (await admin.PostJson($"/api/admin/deposit-requests/{id}/review", new { approve = true, expectedVersion = 0, confirmationReference = "BANK-10000" }, "review-once"))["id"]!.GetValue<Guid>());
                Assert.Equal(before + 10000m, await Available(host));
                await using var db = host.Database.Open();
                var row = await db.DepositRequests.SingleAsync(x => x.Id == id);
                Assert.Equal(DepositReviewStatus.Approved, row.Status);
                Assert.NotNull(row.JournalId); Assert.NotNull(row.ReviewedBy); Assert.NotNull(row.ReviewedAtUtc);
                Assert.Single(await db.FinancialJournals.Where(x => x.IdempotencyReference == $"approved-deposit-{id:N}").ToListAsync());
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact] public async Task Validation_and_receipt_authorization_reject_invalid_requests_and_other_roles()
    {
        var (host, directory) = await Host(fixture);
        try
        {
            await using (host)
            {
                using var business = await host.Login("business");
                Assert.Equal(HttpStatusCode.BadRequest, (await Submit(business, amount: null)).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await Submit(business, attach: false)).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await Submit(business, amount: 0)).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await Submit(business, bytes: [1, 2, 3])).StatusCode);
                var corruptPng = Png.ToArray(); corruptPng[20] ^= 0x01;
                Assert.Equal(HttpStatusCode.BadRequest, (await Submit(business, bytes: corruptPng)).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await Submit(business, media: "image/heic")).StatusCode);
                var oversized = await Submit(business, bytes: SizedPng(4 * 1024 * 1024 + 1));
                Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
                Assert.Contains("Receipt must be 4 MB or smaller.", await oversized.Content.ReadAsStringAsync());
                var valid = await Submit(business);
                Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
                var id = (await valid.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
                using var other = await host.Login("other-business");
                Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/business/deposit-requests/{id}/receipt")).StatusCode);
                foreach (var role in new[] { "creator", "customer", "cashier" })
                {
                    using var client = await host.Login(role);
                    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/business/deposit-requests/{id}/receipt")).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/admin/deposit-requests/{id}/receipt")).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden, (await client.Post($"/api/admin/deposit-requests/{id}/review", new { approve = true, expectedVersion = 0, confirmationReference = "FORBIDDEN" })).StatusCode);
                }
                using var operations = await host.Login("operations-admin");
                Assert.Equal(HttpStatusCode.OK, (await operations.GetAsync($"/api/admin/deposit-requests/{id}/receipt")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await operations.GetAsync("/api/admin/wallets")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await business.GetAsync($"/api/admin/deposit-requests/{id}/receipt")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await business.Post($"/api/admin/deposit-requests/{id}/review", new { approve = true, expectedVersion = 0, confirmationReference = "FORBIDDEN" })).StatusCode);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory] [InlineData("operations-admin")] [InlineData("admin")]
    public async Task Authorized_rejection_preserves_review_audit_and_credits_zero(string reviewer)
    {
        var (host, directory) = await Host(fixture);
        try
        {
            await using (host)
            {
                using var business = await host.Login("business"); using var operations = await host.Login(reviewer);
                var before = await Available(host);
                var id = (await (await Submit(business)).Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
                await operations.PostJson($"/api/admin/deposit-requests/{id}/review", new { approve = false, expectedVersion = 0, confirmationReference = "REJECT-UNMATCHED" });
                Assert.Equal(before, await Available(host));
                await using var db = host.Database.Open();
                var row = await db.DepositRequests.SingleAsync(x => x.Id == id);
                Assert.Equal(DepositReviewStatus.Rejected, row.Status);
                Assert.NotNull(row.ReviewedAtUtc); Assert.NotNull(row.ReviewedBy);
                Assert.Equal("REJECT-UNMATCHED", row.ConfirmationReference);
                Assert.Null(row.JournalId);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact] public async Task Operations_approval_credits_exactly_once_without_configuration_authority()
    {
        var (host, directory) = await Host(fixture);
        try
        {
            await using (host)
            {
                using var business = await host.Login("business"); using var operations = await host.Login("operations-admin");
                var before = await Available(host);
                var id = (await (await Submit(business)).Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
                var reviewPath = $"/api/admin/deposit-requests/{id}/review";
                var body = new { approve = true, expectedVersion = 0, confirmationReference = "OPS-BANK-10000" };
                await operations.PostJson(reviewPath, body, "ops-review-once");
                await operations.PostJson(reviewPath, body, "ops-review-once");
                Assert.Equal(before + 10000m, await Available(host));
                Assert.Equal(HttpStatusCode.Forbidden, (await operations.GetAsync("/api/admin/financial-settings")).StatusCode);
                await using var db = host.Database.Open();
                var row = await db.DepositRequests.SingleAsync(x => x.Id == id);
                Assert.Equal(DepositReviewStatus.Approved, row.Status);
                Assert.Equal(Weymela.Infrastructure.Development.DevelopmentDirectory.Id(11), row.ReviewedBy);
                Assert.Single(await db.FinancialJournals.Where(x => x.IdempotencyReference == $"approved-deposit-{id:N}").ToListAsync());
                Assert.NotNull(row.JournalId);
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}
