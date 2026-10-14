using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weymela.Domain;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Application;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class ProductionAccountLegalContentTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData(ActorRole.Customer)][InlineData(ActorRole.Creator)][InlineData(ActorRole.Business)]
    public async Task Explicit_owner_policy_deferral_allows_registration_without_fabricating_policy_or_consent(ActorRole role)
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var options = new RuntimeOptions { EnvironmentName = "Production", LegalConsentMode = "Deferred",
            LegalConsentAuthorizationReference = "isolated-test-owner-policy-deferral" };
        var user = Guid.NewGuid();
        var legal = new AccountLegalOnboardingService(db, TimeProvider.System, options);
        var status = await legal.StatusAsync(user, default);
        Assert.False(status.Required); Assert.False(status.Available); Assert.False(status.Current); Assert.Empty(status.Documents);
        var request = new RoleEnrollmentRequest(role, "Isolated user", null, "Addis Ababa", "Retail", null,
            SocialProfiles: role == ActorRole.Creator ? [new("TikTok", "https://www.tiktok.com/@isolated_creator")] : null);
        await new RoleEnrollmentService(db, TimeProvider.System, options).SubmitAsync(
            new Actor(user, ActorRole.Customer, CustomerId: Guid.Empty), request, "isolated-deferral-" + role, default);
        Assert.Single(await db.RoleEnrollments.ToListAsync());
        Assert.Empty(await db.LegalDocumentVersions.ToListAsync());
        Assert.Empty(await db.LegalAcceptances.ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
        await new Weymela.Infrastructure.Persistence.Repositories.LegalAcceptanceGate(db, TimeProvider.System, options)
            .EnsureCurrentAcceptedAsync(user, role == ActorRole.Business ? LegalRole.Business : LegalRole.Creator,
                [LegalDocumentType.AntiCircumventionAgreement], default);
    }
    [Fact]
    public async Task Production_consent_requires_all_four_readable_approved_documents_even_with_existing_acceptance()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var directory = Directory.CreateTempSubdirectory("v3-consent-publication-");
        try
        {
            var now = DateTime.UtcNow.AddMinutes(-1);
            var user = Guid.NewGuid();
            var approved = new List<object>();
            foreach (var type in new[] { LegalDocumentType.TermsOfService, LegalDocumentType.PrivacyPolicy })
                foreach (var language in new[] { "en", "am" })
                {
                    var text = $"Isolated consent test: {type}-{language}.\n";
                    var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
                    var version = new LegalDocumentVersion(Guid.NewGuid(), type, "test-1-" + language, hash, now);
                    db.LegalDocumentVersions.Add(version);
                    approved.Add(new { type = type.ToString(), language, approvedFinalVersion = version.Version,
                        approvedFinalContentSha256 = hash });
                    await File.WriteAllTextAsync(Path.Combine(directory.FullName, $"{type}-{language}.txt"), text);
                }
            await db.SaveChangesAsync();
            var service = new AccountLegalOnboardingService(db, TimeProvider.System,
                new RuntimeOptions { EnvironmentName = "Production" }, new AccountLegalDocumentSource(directory.FullName));
            Assert.False((await service.StatusAsync(user, default)).Available);
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "publication.json"), JsonSerializer.Serialize(new {
                environment = "Production", productionApproved = true, attorneyReviewed = false,
                ownerApprovalReference = "isolated-test-owner", documents = approved
            }));
            var status = await service.StatusAsync(user, default);
            Assert.True(status.Available);
            Assert.All(status.Documents, document => Assert.EndsWith("-en", document.Version));
            var amharic = await service.StatusAsync(user, default, "am");
            Assert.All(amharic.Documents, document => Assert.EndsWith("-am", document.Version));
            Assert.All(amharic.Documents, document => Assert.EndsWith("?language=am", document.ViewPath));
            var terms = status.Documents.Single(x => x.Kind == "TermsOfService");
            var privacy = status.Documents.Single(x => x.Kind == "PrivacyPolicy");
            var confirmation = new AccountLegalConfirmation(new(terms.DocumentId, terms.ContentHash, true),
                new(privacy.DocumentId, privacy.ContentHash, true));
            var amharicPrivacy = amharic.Documents.Single(x => x.Kind == "PrivacyPolicy");
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.AcceptCurrentAsync(user,
                confirmation with { PrivacyPolicy = new(amharicPrivacy.DocumentId, amharicPrivacy.ContentHash, true) },
                null, null, default));
            Assert.Empty(await db.LegalAcceptances.ToListAsync());
            await service.AcceptCurrentAsync(user, confirmation, null, null, default);
            await db.SaveChangesAsync();
            Assert.True((await service.StatusAsync(user, default)).Current);
            await File.AppendAllTextAsync(Path.Combine(directory.FullName, "PrivacyPolicy-am.txt"), "unapproved change");
            Assert.False((await service.StatusAsync(user, default)).Available);
            var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.AcceptCurrentAsync(user, confirmation, null, null, default));
            Assert.Equal("ApprovedLegalUnavailable", failure.Code);
            Assert.Equal(2, await db.LegalAcceptances.CountAsync());
            Assert.Equal(2, await db.AuditEvents.CountAsync());
        }
        finally { directory.Delete(true); }
    }
    [Fact]
    public async Task Production_content_requires_owner_approval_exact_bytes_and_published_version_without_attorney_gate()
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
                environment = "Production", productionApproved = approved, attorneyReviewed = false,
                ownerApprovalReference = "isolated-test-owner",
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
            db.LegalDocumentVersions.Add(new(Guid.NewGuid(), LegalDocumentType.TermsOfService, "test-new-en", hash, now.AddSeconds(1)));
            await db.SaveChangesAsync();
            Assert.Null(await source.ReadAsync(db, LegalDocumentType.TermsOfService, "en", now.AddSeconds(2), default));
            await File.AppendAllTextAsync(policy, "changed");
            Assert.Null(await source.ReadAsync(db, LegalDocumentType.TermsOfService, "en", now, default));
            await File.WriteAllTextAsync(Path.Combine(directory, "publication.json"), "{}");
            Assert.Null(await source.ReadAsync(db, LegalDocumentType.TermsOfService, "en", now, default));
        }
        finally { Directory.Delete(directory, true); }
    }
}
