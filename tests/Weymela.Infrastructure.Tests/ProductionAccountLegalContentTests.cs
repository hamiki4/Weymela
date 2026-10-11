using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weymela.Domain;
using Weymela.Infrastructure.Operations;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class ProductionAccountLegalContentTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Production_content_requires_owner_legal_approval_exact_bytes_and_published_version()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var directory = Path.Combine(Path.GetTempPath(), "v3-isolated-legal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = new AccountLegalDocumentSource(directory);
            var now = DateTime.UtcNow;
            Assert.Null(await source.ReadAsync(db, LegalDocumentType.TermsOfService, "en", now, default));
            const string text = "Isolated approved policy test bytes.\n";
            var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
            var policy = Path.Combine(directory, "TermsOfService-en.txt");
            await File.WriteAllTextAsync(policy, text);
            async Task Manifest(bool approved) => await File.WriteAllTextAsync(Path.Combine(directory, "publication.json"), JsonSerializer.Serialize(new
            {
                environment = "Production", productionApproved = approved, attorneyReviewed = approved,
                ownerApprovalReference = "isolated-test-owner", legalApprovalReference = "isolated-test-legal",
                documents = new[] { new { type = "TermsOfService", language = "en", approvedFinalVersion = "test-en", approvedFinalContentSha256 = hash } }
            }));
            await Manifest(false);
            Assert.Null(await source.ReadAsync(db, LegalDocumentType.TermsOfService, "en", now, default));
            await Manifest(true);
            Assert.Null(await source.ReadAsync(db, LegalDocumentType.TermsOfService, "en", now, default));
            db.LegalDocumentVersions.Add(new(Guid.NewGuid(), LegalDocumentType.TermsOfService, "test-en", hash, now));
            await db.SaveChangesAsync();
            Assert.Equal(text, (await source.ReadAsync(db, LegalDocumentType.TermsOfService, "en", now, default))!.Content);
            Assert.Null(await source.ReadAsync(db, LegalDocumentType.TermsOfService, "am", now, default));
            await File.AppendAllTextAsync(policy, "changed");
            Assert.Null(await source.ReadAsync(db, LegalDocumentType.TermsOfService, "en", now, default));
            await File.WriteAllTextAsync(Path.Combine(directory, "publication.json"), "{}");
            Assert.Null(await source.ReadAsync(db, LegalDocumentType.TermsOfService, "en", now, default));
        }
        finally { Directory.Delete(directory, true); }
    }
}
