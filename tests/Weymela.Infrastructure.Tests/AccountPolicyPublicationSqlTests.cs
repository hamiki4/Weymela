using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class AccountPolicyPublicationSqlTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Prepared_publication_is_database_scoped_append_only_and_idempotent_and_never_accepts_for_users()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = Guid.NewGuid();
        db.CommercePermissions.Add(new(admin, ActorRole.PlatformAdmin, admin, null, true, false));
        await db.SaveChangesAsync();
        var directory = Directory.CreateTempSubdirectory("v3-isolated-publication-");
        try
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "tools/ops/prepare-account-policy-publication.py")))
                repository = repository.Parent;
            Assert.NotNull(repository);
            var documents = new List<object>();
            foreach (var type in new[] { "TermsOfService", "PrivacyPolicy" })
                foreach (var language in new[] { "en", "am" })
                {
                    var content = $"Isolated publication test: {type} {language}.\n";
                    await File.WriteAllTextAsync(Path.Combine(directory.FullName, $"{type}-{language}.txt"), content);
                    documents.Add(new { type, language, approvedFinalVersion = "isolated-1-" + language,
                        approvedFinalContentSha256 = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant(),
                        effectiveFromUtc = "2026-01-01T00:00:00Z" });
                }
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "publication.json"), JsonSerializer.Serialize(new {
                environment = "Production", productionApproved = true, attorneyReviewed = false,
                ownerApprovalReference = "Isolated test owner's reference", documents
            }));
            var output = Path.Combine(directory.FullName, "publication.sql");
            var start = new ProcessStartInfo("python3") { RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { Path.Combine(repository!.FullName, "tools/ops/prepare-account-policy-publication.py"),
                "--approved-bundle", directory.FullName, "--operator-user-id", admin.ToString(), "--output-sql", output })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, error);
            var sql = await File.ReadAllTextAsync(output);
            async Task Execute(string statement)
            {
                await using var connection = new NpgsqlConnection(database.ConnectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(statement, connection);
                await command.ExecuteNonQueryAsync();
            }
            await Assert.ThrowsAsync<PostgresException>(() => Execute(sql));
            Assert.Empty(await db.LegalDocumentVersions.ToListAsync());
            // Only this disposable fixture uses its explicitly scoped test database.
            sql = sql.Replace("'weymela_v3_prod_main'", "'" + db.Database.GetDbConnection().Database + "'", StringComparison.Ordinal);
            await Execute(sql);
            await Execute(sql);
            Assert.Equal(4, await db.LegalDocumentVersions.CountAsync());
            Assert.Single(await db.AuditEvents.Where(x => x.EventType == "AccountLegalDocumentsPublished").ToListAsync());
            Assert.Empty(await db.LegalAcceptances.ToListAsync());
            Assert.Empty(await db.FinancialJournals.ToListAsync());
            var published = await db.LegalDocumentVersions.AsNoTracking().FirstAsync();
            await Assert.ThrowsAsync<PostgresException>(() => Execute(
                sql.Replace(published.ContentHash, "sha256:" + new string('0', 64), StringComparison.Ordinal)));
            Assert.Equal(4, await db.LegalDocumentVersions.CountAsync());
        }
        finally { directory.Delete(true); }
    }
}
