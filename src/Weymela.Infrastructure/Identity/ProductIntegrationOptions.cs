using Microsoft.Extensions.Configuration;
using Weymela.Infrastructure.Operations;

namespace Weymela.Infrastructure.Identity;

public sealed class ProductIntegrationOptions
{
    public bool Enabled { get; init; }
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public string Environment { get; init; } = "";
    public string CallbackId { get; init; } = "";
    public string CallbackUrl { get; init; } = "";
    public string BeginUrl { get; init; } = "";
    public string ProductWebUrl { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public TimeSpan CodeLifetime { get; init; } = TimeSpan.FromSeconds(45);

    public static ProductIntegrationOptions Load(IConfiguration configuration, RuntimeOptions runtime)
    {
        var enabled = configuration.GetValue<bool>("V3:ProductIntegration:Enabled");
        if (!enabled) return new() { Environment = runtime.EnvironmentName };
        var result = new ProductIntegrationOptions
        {
            Enabled = true,
            Issuer = configuration["V3:ProductIntegration:Issuer"] ?? "",
            Audience = configuration["V3:ProductIntegration:Audience"] ?? "",
            Environment = configuration["V3:ProductIntegration:Environment"] ?? "",
            CallbackId = configuration["V3:ProductIntegration:CallbackId"] ?? "",
            CallbackUrl = configuration["V3:ProductIntegration:CallbackUrl"] ?? "",
            BeginUrl = configuration["V3:ProductIntegration:BeginUrl"] ?? "",
            ProductWebUrl = configuration["V3:ProductIntegration:ProductWebUrl"] ?? "",
            ClientId = configuration["V3:ProductIntegration:ClientId"] ?? "",
            ClientSecret = configuration["V3:ProductIntegration:ClientSecret"] ?? ""
        };
        Require(result.Environment == runtime.EnvironmentName, "Product integration environment must match the V3 runtime.");
        Require(result.Issuer.Length is >= 3 and <= 100 && result.Audience.Length is >= 3 and <= 100,
            "Product integration issuer and audience are required.");
        Require(result.CallbackId.Length is >= 3 and <= 80, "Product integration callback identifier is required.");
        Require(RuntimeOptions.IsOrigin(result.ProductWebUrl, runtime.Development), "Product integration Web origin is invalid.");
        Require(IsEndpoint(result.CallbackUrl, "/api/v1/integration/v3/callback", runtime.Development)
            && IsEndpoint(result.BeginUrl, "/api/v1/integration/v3/begin", runtime.Development)
            && SameOrigin(result.CallbackUrl, result.BeginUrl),
            "Product integration callback and begin endpoints are invalid.");
        Require(result.ClientId.Length is >= 8 and <= 100 && result.ClientSecret.Length >= 32,
            "Protected product integration client credentials are required.");
        return result;
    }

    private static bool IsEndpoint(string value, string expectedPath, bool development) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || development && uri.Scheme == "http" && uri.IsLoopback)
        && uri.AbsolutePath == expectedPath && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);
    private static bool SameOrigin(string first, string second) => Uri.TryCreate(first, UriKind.Absolute, out var left)
        && Uri.TryCreate(second, UriKind.Absolute, out var right)
        && string.Equals(left.GetLeftPart(UriPartial.Authority), right.GetLeftPart(UriPartial.Authority),
            StringComparison.OrdinalIgnoreCase);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
