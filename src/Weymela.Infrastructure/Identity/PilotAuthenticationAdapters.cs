using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

/// <summary>Direct, bounded Resend HTTPS adapter. It never logs provider responses or message content.</summary>
public sealed class ResendEmailCodeDelivery : IEmailCodeDelivery, IDisposable
{
    public static readonly Uri Endpoint = new("https://api.resend.com/emails");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    private readonly RuntimeOptions options;
    private readonly HttpClient client;

    public ResendEmailCodeDelivery(RuntimeOptions options, HttpMessageHandler handler)
    {
        if (options.EmailDeliveryMode != "Resend"
            || options.ResendApiKey is not { Length: >= 20 }
            || string.IsNullOrWhiteSpace(options.ResendFromAddress)
            || string.IsNullOrWhiteSpace(options.ResendFromName)
            || !TrySecret(options.AuthCodeHashKey, out _))
            throw Unavailable();
        this.options = options;
        client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout };
    }

    public bool Enabled => true;

    public async Task SendAsync(string destination, string code, EmailCodePurpose purpose, CancellationToken ct)
    {
        Validate(destination, code);
        var subject = purpose switch
        {
            EmailCodePurpose.Signup => "Your Weymela verification code",
            EmailCodePurpose.DeviceEnrollment => "Your Weymela sign-in code",
            EmailCodePurpose.PinRecovery => "Your Weymela recovery code",
            _ => throw Unavailable()
        };
        var purposeText = purpose switch
        {
            EmailCodePurpose.Signup => "verify your Weymela account",
            EmailCodePurpose.DeviceEnrollment => "sign in to Weymela",
            EmailCodePurpose.PinRecovery => "recover your Weymela PIN",
            _ => throw Unavailable()
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ResendApiKey);
        request.Headers.UserAgent.ParseAdd("WeymelaV3/1.0");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", IdempotencyKey(destination, code, purpose));
        request.Content = JsonContent.Create(new
        {
            from = $"{options.ResendFromName} <{options.ResendFromAddress}>",
            to = new[] { destination },
            subject,
            text = $"Use {code} to {purposeText}. This code expires in 10 minutes. If you did not request it, ignore this message."
        });
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 8192)
                throw Unavailable();
            await response.Content.LoadIntoBufferAsync(8192, ct);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!payload.RootElement.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.String
                || id.GetString() is not { Length: > 0 and <= 200 })
                throw Unavailable();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AuthChallengeUnavailableException) { throw; }
        catch { throw Unavailable(); }
    }

    private string IdempotencyKey(string destination, string code, EmailCodePurpose purpose)
    {
        if (!TrySecret(options.AuthCodeHashKey, out var key)) throw Unavailable();
        var value = Encoding.UTF8.GetBytes($"{purpose}|{destination}|{code}");
        try { return "weymela-" + Convert.ToHexString(HMACSHA256.HashData(key, value)).ToLowerInvariant(); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static bool TrySecret(string? value, out byte[] bytes)
    {
        try { bytes = Convert.FromBase64String(value ?? ""); return bytes.Length >= 32; }
        catch (FormatException) { bytes = []; return false; }
    }

    private static void Validate(string destination, string code)
    {
        if (destination.Length is < 4 or > 320 || destination.Any(char.IsControl)
            || !destination.Contains('@', StringComparison.Ordinal)
            || code.Length != 6 || code.Any(c => c is < '0' or > '9'))
            throw Unavailable();
    }

    private static AuthChallengeUnavailableException Unavailable() =>
        new("Email delivery is temporarily unavailable.");

    public void Dispose() => client.Dispose();
}

public interface IFirebaseAdminTokenSigner
{
    Task<string> CreateCustomTokenAsync(string uid, CancellationToken ct);
}

/// <summary>Owns the Firebase Admin application initialized from an external read-only credential file.</summary>
public sealed class FirebaseAdminTokenSigner : IFirebaseAdminTokenSigner, IDisposable
{
    private readonly FirebaseApp app;
    private readonly FirebaseAuth auth;

    public FirebaseAdminTokenSigner(RuntimeOptions options)
    {
        try
        {
            var bytes = File.ReadAllBytes(options.FirebaseAdminCredentialsPath);
            if (bytes.Length is < 100 or > 64 * 1024) throw new InvalidOperationException();
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.GetProperty("type").GetString() != "service_account"
                || root.GetProperty("project_id").GetString() != options.FirebaseProjectId
                || string.IsNullOrWhiteSpace(root.GetProperty("client_email").GetString())
                || string.IsNullOrWhiteSpace(root.GetProperty("private_key").GetString()))
                throw new InvalidOperationException();
            var credential = CredentialFactory
                .FromJson<ServiceAccountCredential>(Encoding.UTF8.GetString(bytes))
                .ToGoogleCredential();
            app = FirebaseApp.Create(new AppOptions
            {
                Credential = credential,
                ProjectId = options.FirebaseProjectId
            }, "WeymelaV3-" + Guid.NewGuid().ToString("N"));
            auth = FirebaseAuth.GetAuth(app);
        }
        catch { throw new InvalidOperationException("The Firebase Admin signing credential is unavailable or invalid."); }
    }

    public async Task<string> CreateCustomTokenAsync(string uid, CancellationToken ct)
    {
        try { return await auth.CreateCustomTokenAsync(uid).WaitAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { throw new AuthChallengeUnavailableException("Firebase custom-token signing is temporarily unavailable."); }
    }

    public void Dispose() => app.Delete();
}

/// <summary>Resolves or creates exactly one authoritative Firebase binding before signing its UID.</summary>
public sealed class FirebaseAdminCustomTokenIssuer(
    WeymelaDbContext db,
    IFirebaseAdminTokenSigner signer,
    RuntimeOptions options,
    TimeProvider clock) : IFirebaseCustomTokenIssuer
{
    public bool Enabled => true;

    public async Task<FirebaseCustomTokenResult> IssueAsync(Guid userId, string projectId, CancellationToken ct)
    {
        try
        {
            if (userId == Guid.Empty || projectId != options.FirebaseProjectId)
                throw Unavailable();
            var binding = await db.IdentityBindings.SingleOrDefaultAsync(x => x.UserId == userId, ct);
            if (binding is not null && (binding.Provider != "Firebase" || binding.ProjectId != projectId
                || !binding.IsActive || string.IsNullOrWhiteSpace(binding.ExternalSubject)))
                throw Unavailable();
            if (binding is null)
            {
                var uid = userId.ToString("N");
                if (await db.IdentityBindings.AnyAsync(x => x.Provider == "Firebase"
                    && x.ProjectId == projectId && x.ExternalSubject == uid && x.UserId != userId, ct))
                    throw Unavailable();
                binding = new IdentityBinding
                {
                    Provider = "Firebase", ProjectId = projectId, ExternalSubject = uid,
                    UserId = userId, IsActive = true, ValidAfterUtc = clock.GetUtcNow().UtcDateTime,
                    Version = 1
                };
                db.IdentityBindings.Add(binding);
            }
            var token = await signer.CreateCustomTokenAsync(binding.ExternalSubject, ct);
            if (string.IsNullOrWhiteSpace(token) || token.Length > 8192) throw Unavailable();
            return new(token, clock.GetUtcNow().UtcDateTime.AddHours(1));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AuthChallengeUnavailableException) { throw; }
        catch { throw Unavailable(); }
    }

    private static AuthChallengeUnavailableException Unavailable() =>
        new("Firebase custom-token signing is temporarily unavailable.");
}

public static class PilotAuthenticationAdapters
{
    public static IServiceCollection AddPilotAuthenticationAdapters(
        this IServiceCollection services, RuntimeOptions options)
    {
        if (options.EnvironmentName != "Pilot") return services;
        if (options.EmailDeliveryMode == "Resend")
        {
            services.Replace(ServiceDescriptor.Singleton<IEmailCodeDelivery>(sp =>
                new ResendEmailCodeDelivery(options, new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    MaxConnectionsPerServer = 2,
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                    AutomaticDecompression = System.Net.DecompressionMethods.None,
                    UseCookies = false
                })));
        }
        if (options.FirebaseCustomTokenMode == "FirebaseAdmin")
        {
            services.AddSingleton<IFirebaseAdminTokenSigner, FirebaseAdminTokenSigner>();
            services.Replace(ServiceDescriptor.Scoped<IFirebaseCustomTokenIssuer, FirebaseAdminCustomTokenIssuer>());
        }
        return services;
    }
}
