using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Infrastructure.Operations;

public sealed record PublishedAccountLegalContent(string Version, string ContentHash, string Content);

// Operator-managed approved files are mounted read-only. Review drafts and Pilot policy files
// are never a fallback for Production. The published DB version and exact approved bytes agree.
public sealed class AccountLegalDocumentSource(string directory)
{
    public async Task<PublishedAccountLegalContent?> ReadAsync(WeymelaDbContext db,
        LegalDocumentType type, string language, DateTime now, CancellationToken ct)
    {
        if (type is not (LegalDocumentType.TermsOfService or LegalDocumentType.PrivacyPolicy)
            || language is not ("en" or "am") || !Path.IsPathFullyQualified(directory)) return null;
        try
        {
            var manifestPath = Path.Combine(directory, "publication.json");
            if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 65536) return null;
            using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(manifestPath, ct));
            var root = manifest.RootElement;
            if (root.GetProperty("environment").GetString() != "Production"
                || !root.GetProperty("productionApproved").GetBoolean()
                || !root.GetProperty("attorneyReviewed").GetBoolean()
                || string.IsNullOrWhiteSpace(root.GetProperty("ownerApprovalReference").GetString())
                || string.IsNullOrWhiteSpace(root.GetProperty("legalApprovalReference").GetString())) return null;
            var documents = root.GetProperty("documents").EnumerateArray().Where(x =>
                x.GetProperty("type").GetString() == type.ToString()
                && x.GetProperty("language").GetString() == language).ToList();
            if (documents.Count != 1) return null;
            var approved = documents[0];
            var versionName = approved.GetProperty("approvedFinalVersion").GetString();
            var hash = approved.GetProperty("approvedFinalContentSha256").GetString();
            if (string.IsNullOrWhiteSpace(versionName) || !versionName.EndsWith("-" + language, StringComparison.Ordinal)
                || hash is null || hash.Length != 71
                || !hash.StartsWith("sha256:", StringComparison.Ordinal)) return null;
            var suffix = "-" + language;
            var version = await db.LegalDocumentVersions.AsNoTracking().Where(x =>
                x.Type == type && x.Version.EndsWith(suffix) && x.EffectiveFromUtc <= now)
                .OrderByDescending(x => x.EffectiveFromUtc).ThenByDescending(x => x.Version)
                .ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (version is null || version.Version != versionName || version.ContentHash != hash) return null;
            var path = Path.Combine(directory, $"{type}-{language}.txt");
            if (!File.Exists(path) || new FileInfo(path).Length is <= 0 or > 262144) return null;
            var bytes = await File.ReadAllBytesAsync(path, ct);
            if ("sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != hash) return null;
            return new(version.Version, hash, new UTF8Encoding(false, true).GetString(bytes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
            or KeyNotFoundException or InvalidOperationException or DecoderFallbackException) { return null; }
    }
}
