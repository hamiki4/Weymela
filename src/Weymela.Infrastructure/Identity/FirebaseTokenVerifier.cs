using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Operations;

namespace Weymela.Infrastructure.Identity;

public interface IIdentitySigningKeys
{
    Task<SecurityKey?> FindAsync(string keyId, CancellationToken ct);
}

// Only Google's public signing certificate endpoint is contacted, on demand after live integration is authorized.
// No Firebase project mutation, service-account private key, token introspection or startup network request.
public sealed class FirebaseSigningKeys(HttpClient client, TimeProvider clock) : IIdentitySigningKeys, IDisposable
{
    public const string CertificateEndpoint = "https://www.googleapis.com/robot/v1/metadata/x509/securetoken@system.gserviceaccount.com";
    private readonly SemaphoreSlim gate = new(1, 1);
    private Dictionary<string, SecurityKey> keys = [];
    private DateTimeOffset expires;
    private DateTimeOffset retryAfter;
    public async Task<SecurityKey?> FindAsync(string keyId, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (now < expires && keys.TryGetValue(keyId, out var cached)) return cached;
            if (now < retryAfter) return null;
            retryAfter = now.AddSeconds(30); // Unknown-kid floods cannot repeatedly fetch certificates.
            using var response = await client.GetAsync(CertificateEndpoint, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 256 * 1024) throw new InvalidOperationException("Signing key response exceeds its bound.");
            await response.Content.LoadIntoBufferAsync(256 * 1024, ct);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(await response.Content.ReadAsStringAsync(ct)) ?? [];
            if (values.Count is < 1 or > 20) throw new InvalidOperationException("Invalid signing key set.");
            Dictionary<string, SecurityKey> fresh = [];
            foreach (var value in values)
            {
                using var certificate = X509Certificate2.CreateFromPem(value.Value);
                if (certificate.NotAfter.ToUniversalTime() <= now.UtcDateTime) continue;
                using var rsa = certificate.GetRSAPublicKey();
                if (rsa is null || rsa.KeySize < 2048) continue;
                fresh[value.Key] = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = value.Key };
            }
            keys = fresh;
            expires = now.AddSeconds(Math.Clamp(response.Headers.CacheControl?.MaxAge?.TotalSeconds ?? 300, 30, 3600));
            return keys.GetValueOrDefault(keyId);
        }
        finally { gate.Release(); }
    }
    public void Dispose() => gate.Dispose();
}

public sealed class FirebaseTokenVerifier(RuntimeOptions options, IIdentitySigningKeys keys, TimeProvider clock) : IIdentityTokenVerifier
{
    public async Task<VerifiedIdentity> VerifyAsync(string sensitiveIdToken, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(sensitiveIdToken) || sensitiveIdToken.Length > 8192 || string.IsNullOrEmpty(options.FirebaseProjectId)) throw Invalid();
            var handler = new JsonWebTokenHandler { MaximumTokenSizeInBytes = 8192 };
            var untrusted = handler.ReadJsonWebToken(sensitiveIdToken);
            if (untrusted.Alg != SecurityAlgorithms.RsaSha256 || string.IsNullOrEmpty(untrusted.Kid) || untrusted.Kid.Length > 128) throw Invalid();
            var key = await keys.FindAsync(untrusted.Kid, ct) ?? throw Invalid();
            var result = await handler.ValidateTokenAsync(sensitiveIdToken, new TokenValidationParameters
            {
                RequireSignedTokens = true, ValidateIssuerSigningKey = true, IssuerSigningKey = key, TryAllIssuerSigningKeys = false,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ValidateIssuer = true,
                ValidIssuer = $"https://securetoken.google.com/{options.FirebaseProjectId}", ValidateAudience = true,
                ValidAudience = options.FirebaseProjectId, RequireExpirationTime = true, ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30), IncludeTokenOnFailedValidation = false, LogTokenId = false,
                LifetimeValidator = (notBefore, expiry, _, _) => expiry is {} end && end > clock.GetUtcNow().UtcDateTime
                    && (notBefore is null || notBefore <= clock.GetUtcNow().UtcDateTime.AddSeconds(30))
            });
            if (!result.IsValid || result.SecurityToken is not JsonWebToken token) throw Invalid();
            var now = clock.GetUtcNow().UtcDateTime;
            if (string.IsNullOrWhiteSpace(token.Subject) || token.Subject.Length > 128 || token.Subject.Any(char.IsControl)
                || token.Audiences.Count() != 1 || token.IssuedAt > now.AddSeconds(30) || token.IssuedAt >= token.ValidTo
                || !token.TryGetPayloadValue<long>("auth_time", out var authTime)) throw Invalid();
            var authenticated = DateTimeOffset.FromUnixTimeSeconds(authTime).UtcDateTime;
            if (authenticated > now.AddSeconds(30) || authenticated > token.IssuedAt || authenticated < now.AddMinutes(-5)) throw Invalid();
            return new("Firebase", options.FirebaseProjectId, token.Subject, authenticated, token.ValidTo);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { throw Invalid(); } // Never propagate token/library/provider exception text.
    }
    private static ApplicationFailure Invalid() => new(FailureKind.Forbidden, "Sign-in could not be verified. Sign in again.");
}
