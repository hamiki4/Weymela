using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class BusinessLegalPublicationTests(PostgresFixture fixture)
{
    private static readonly DateTime Effective = new(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeProvider Clock = new FixedClock(Effective.AddDays(1));

    [Fact]
    public async Task Approved_bytes_match_both_version_hashes_and_reject_tampering()
    {
        var root = RepositoryFile("src", "Weymela.Api");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "BusinessLegal", "publication.json")));
        var documents = manifest.RootElement.GetProperty("documents").EnumerateArray().ToArray();
        Assert.False(manifest.RootElement.GetProperty("automaticPublication").GetBoolean());
        Assert.Equal(2, documents.Length);
        var expected = new Dictionary<string, string>
        {
            ["BusinessAgreement"] = string.Join('\n', [
                "Weymela Business Rules",
                "",
                "By accepting, you agree to follow Weymela's rules and use the platform",
                "honestly. Promotions, UGC opportunities, payments, discounts, Creator",
                "activities, and Customer benefits created through Weymela must be handled",
                "through Weymela according to the platform rules.",
                ""]),
            ["AntiCircumventionAgreement"] = string.Join('\n', [
                "Do not provide false information, manipulate transactions or engagement,",
                "misuse Customer or Creator information, or intentionally move Weymela",
                "Promotions or Creator relationships outside Weymela to avoid platform",
                "tracking, fees, commissions, cashback, discounts, or other obligations.",
                ""]),
        };
        var expectedVersions = new Dictionary<string, (Guid Id, string Sha256)>
        {
            ["BusinessAgreement"] = (Guid.Parse("a592e8f7-66a7-458a-8b6b-37b15c5926af"),
                "b2f974660a0b69174d0de01214a78009326d84fe43d2c9407222eae7506aa690"),
            ["AntiCircumventionAgreement"] = (Guid.Parse("59b49723-1c3a-4b8f-a51d-d1f2f959d1de"),
                "05a297468a6a513bc604a21c0e58a39d06c60ae8bdee8d3462a968d619f4078e"),
        };
        var source = new BusinessLegalDocumentSource(root);
        foreach (var document in documents)
        {
            var id = document.GetProperty("id").GetGuid();
            var type = document.GetProperty("type").GetString()!;
            var roles = document.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).ToArray();
            Assert.Equal(type == "BusinessAgreement" ? ["Business"] : new[] { "Business", "Creator" }, roles);
            var version = document.GetProperty("version").GetString()!;
            var hash = document.GetProperty("contentHash").GetString()!;
            var path = Path.Combine(root, "BusinessLegal", document.GetProperty("contentPath").GetString()!);
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal(expectedVersions[type].Id, id);
            Assert.Equal("sha256:" + expectedVersions[type].Sha256, hash);
            Assert.Equal($"{id:N}.txt", Path.GetFileName(path));
            Assert.Equal(expected[type], System.Text.Encoding.UTF8.GetString(bytes));
            Assert.Equal("initial-2026-09-28.1", version);
            Assert.Equal(Effective, document.GetProperty("effectiveFromUtc").GetDateTime());
            Assert.Equal("sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), hash);
            var row = new LegalDocumentVersion(id, Enum.Parse<LegalDocumentType>(type), version, hash, Effective);
            Assert.Equal(expected[type], await source.ReadAsync(row, default));
            Assert.Null(await source.ReadAsync(row with { ContentHash = "sha256:" + new string('0', 64) }, default));
        }
    }

    [Fact]
    public async Task Publication_is_target_guarded_and_requires_each_Business_to_accept_both_new_versions()
    {
        var apiRoot = RepositoryFile("src", "Weymela.Api");
        var script = await File.ReadAllTextAsync(Path.Combine(apiRoot, "BusinessLegal", "publish.sql"));
        script = string.Join('\n', script.Split('\n').Where(line => !line.StartsWith("\\set ", StringComparison.Ordinal)));
        var database = await fixture.CreateAsync();
        var user = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var creatorUser = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var otherCreatorUser = Guid.NewGuid();
        await using (var db = database.Open())
        {
            foreach (var type in new[] { LegalDocumentType.BusinessAgreement, LegalDocumentType.AntiCircumventionAgreement })
            {
                var id = Guid.NewGuid();
                db.LegalDocumentVersions.Add(new(id, type, "before-approved-rules", "old-hash", Effective.AddDays(-1)));
                db.LegalAcceptances.Add(new(user, LegalRole.Business, id, Effective.AddDays(-1), null, null));
                if (type == LegalDocumentType.AntiCircumventionAgreement)
                    db.LegalAcceptances.Add(new(creatorUser, LegalRole.Creator, id, Effective.AddDays(-1), null, null));
            }
            var creatorAgreementId = Guid.NewGuid();
            db.LegalDocumentVersions.Add(new(creatorAgreementId, LegalDocumentType.CreatorAgreement,
                "creator-fixture", "creator-hash", Effective.AddDays(-1)));
            db.LegalAcceptances.Add(new(creatorUser, LegalRole.Creator, creatorAgreementId, Effective.AddDays(-1), null, null));
            await db.SaveChangesAsync();
        }

        await using (var wrong = new NpgsqlConnection(database.ConnectionString))
        {
            await wrong.OpenAsync();
            var rejected = await Assert.ThrowsAsync<PostgresException>(
                () => new NpgsqlCommand(script, wrong).ExecuteNonQueryAsync());
            Assert.Contains("target rejected", rejected.MessageText, StringComparison.Ordinal);
        }
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            var name = new NpgsqlConnectionStringBuilder(database.ConnectionString).Database;
            await using var target = new NpgsqlCommand("SELECT set_config('weymela.business_legal_publication_target', @name, false)", connection);
            target.Parameters.AddWithValue("name", name!);
            await target.ExecuteNonQueryAsync();
            await new NpgsqlCommand(script, connection).ExecuteNonQueryAsync();
            await new NpgsqlCommand(script, connection).ExecuteNonQueryAsync();
        }

        await using var verify = database.Open();
        var actor = new Actor(user, ActorRole.Business, BusinessId: businessId);
        var service = new LegalWorkspaceService(verify, Clock, new BusinessLegalDocumentSource(apiRoot));
        var current = await service.CurrentAsync(actor, default);
        Assert.Equal(2, current.Count);
        Assert.All(current, document => Assert.False(document.Accepted));
        await Assert.ThrowsAsync<ApplicationFailure>(() => new LegalAcceptanceGate(verify, Clock)
            .EnsureCurrentAcceptedAsync(user, LegalRole.Business,
                [LegalDocumentType.BusinessAgreement, LegalDocumentType.AntiCircumventionAgreement], default));
        foreach (var document in current)
        {
            var content = await service.CurrentContentAsync(actor, document.Id, default);
            Assert.NotNull(content);
            Assert.Equal(document.ContentHash, content.ContentHash);
            await service.AcceptAsync(actor, document.Id, document.ContentHash, true, default);
        }
        Assert.All(await service.CurrentAsync(actor, default), document => Assert.True(document.Accepted));
        await new LegalAcceptanceGate(verify, Clock).EnsureCurrentAcceptedAsync(user, LegalRole.Business,
            [LegalDocumentType.BusinessAgreement, LegalDocumentType.AntiCircumventionAgreement], default);
        Assert.Equal(2, await verify.LegalAcceptances.CountAsync(x => x.UserId == user && current.Select(d => d.Id).Contains(x.DocumentVersionId)));
        Assert.Equal(2, await verify.AuditEvents.CountAsync(x => x.ActorId == user && x.EventType == "LegalVersionAccepted"
            && x.BusinessId == businessId && x.CreatorId == null && current.Select(d => d.Id.ToString()).Contains(x.Detail)));

        var businessAgreement = current.Single(d => d.Type == "BusinessAgreement");
        var antiCircumvention = current.Single(d => d.Type == "AntiCircumventionAgreement");
        var creator = new Actor(creatorUser, ActorRole.Creator, CreatorId: creatorId);
        var creatorCurrent = await service.CurrentAsync(creator, default);
        Assert.DoesNotContain(creatorCurrent, document => document.Type == "BusinessAgreement");
        Assert.True(creatorCurrent.Single(document => document.Type == "CreatorAgreement").Accepted);
        Assert.False(creatorCurrent.Single(document => document.Type == "AntiCircumventionAgreement").Accepted);
        var creatorContent = await service.CurrentContentAsync(creator, antiCircumvention.Id, default);
        Assert.NotNull(creatorContent);
        Assert.Equal(antiCircumvention.ContentHash, creatorContent.ContentHash);
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(apiRoot, "BusinessLegal", $"{antiCircumvention.Id:N}.txt")), creatorContent.Content);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.CurrentContentAsync(creator, businessAgreement.Id, default));
        await Assert.ThrowsAsync<ApplicationFailure>(() => new LegalAcceptanceGate(verify, Clock)
            .EnsureCurrentAcceptedAsync(creatorUser, LegalRole.Creator,
                [LegalDocumentType.CreatorAgreement, LegalDocumentType.AntiCircumventionAgreement], default));
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.AcceptAsync(creator,
            businessAgreement.Id, businessAgreement.ContentHash, true, default));
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.AcceptAsync(creator,
            antiCircumvention.Id, "incorrect-hash", true, default));
        await service.AcceptAsync(creator, antiCircumvention.Id, antiCircumvention.ContentHash, true, default);
        Assert.True((await service.CurrentAsync(creator, default))
            .Single(document => document.Type == "AntiCircumventionAgreement").Accepted);
        await new LegalAcceptanceGate(verify, Clock).EnsureCurrentAcceptedAsync(creatorUser, LegalRole.Creator,
            [LegalDocumentType.CreatorAgreement, LegalDocumentType.AntiCircumventionAgreement], default);
        Assert.Equal(1, await verify.LegalAcceptances.CountAsync(x => x.UserId == creatorUser
            && x.Role == LegalRole.Creator && x.DocumentVersionId == antiCircumvention.Id));
        Assert.Equal(1, await verify.AuditEvents.CountAsync(x => x.ActorId == creatorUser
            && x.CreatorId == creatorId && x.BusinessId == null && x.EventType == "LegalVersionAccepted"
            && x.Detail == antiCircumvention.Id.ToString()));
        var otherCreator = new Actor(otherCreatorUser, ActorRole.Creator, CreatorId: Guid.NewGuid());
        Assert.False((await service.CurrentAsync(otherCreator, default))
            .Single(document => document.Type == "AntiCircumventionAgreement").Accepted);
        Assert.False(await verify.LegalAcceptances.AnyAsync(x => x.UserId == otherCreatorUser
            && x.DocumentVersionId == antiCircumvention.Id));

        foreach (var role in new[] { ActorRole.Customer, ActorRole.Cashier, ActorRole.OperationsAdmin, ActorRole.PlatformAdmin })
        {
            var other = new Actor(Guid.NewGuid(), role);
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.CurrentAsync(other, default));
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.CurrentContentAsync(other, businessAgreement.Id, default));
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.CurrentContentAsync(other, antiCircumvention.Id, default));
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.AcceptAsync(other,
                businessAgreement.Id, businessAgreement.ContentHash, true, default));
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.AcceptAsync(other,
                antiCircumvention.Id, antiCircumvention.ContentHash, true, default));
        }
    }

    private static string RepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Weymela.slnx")))
                return Path.Combine([directory.FullName, .. segments]);
        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
