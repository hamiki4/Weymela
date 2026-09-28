using System.Security.Cryptography;
using System.Text;
using Weymela.Domain;

namespace Weymela.Infrastructure.Operations;

// Approved document bytes are packaged with the API and bound to the exact
// published version by its id and SHA-256 hash. Missing or mismatched content
// is never available for review or acceptance.
public sealed class BusinessLegalDocumentSource(string contentRoot)
{
    public async Task<string?> ReadAsync(LegalDocumentVersion version, CancellationToken ct)
    {
        if (version.Type is not (LegalDocumentType.BusinessAgreement or LegalDocumentType.AntiCircumventionAgreement)) return null;
        if (!version.ContentHash.StartsWith("sha256:", StringComparison.Ordinal) || version.ContentHash.Length != 71) return null;
        var path = Path.Combine(contentRoot, "BusinessLegal", $"{version.Id:N}.txt");
        if (!File.Exists(path)) return null;
        byte[] bytes;
        try {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > 262144) return null;
            bytes = await File.ReadAllBytesAsync(path, ct);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
        if (bytes.Length is <= 0 or > 262144) return null;
        if (!string.Equals("sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), version.ContentHash, StringComparison.Ordinal)) return null;
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return null; }
    }
}
