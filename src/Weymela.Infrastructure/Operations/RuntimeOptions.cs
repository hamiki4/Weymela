using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Weymela.Infrastructure.Operations;

public sealed class RuntimeOptions
{
    public const string CameraPolicy = "camera=(self), microphone=(), geolocation=(), payment=(), usb=()";
    public bool Development { get; init; }
    public string EnvironmentName { get; init; } = "Development";
    public bool DevelopmentIdentity { get; init; }
    public string ConnectionString { get; init; } = "";
    public string[] AllowedOrigins { get; init; } = [];
    public string PublicWebUrl { get; init; } = "";
    public string PublicApiUrl { get; init; } = "";
    public string FirebaseProjectId { get; init; } = "";
    public string EmailDeliveryMode { get; init; } = "Disabled";
    public string FirebaseCustomTokenMode { get; init; } = "Disabled";
    public string? AuthCodeHashKey { get; init; }
    public string? PinPepper { get; init; }
    public string CookieKeyDirectory { get; init; } = "";
    public string CookieCertificatePath { get; init; } = "";
    public string? CookieCertificatePassword { get; init; }
    public string DepositMode { get; init; } = "Disabled";
    public string SocialMode { get; init; } = "Disabled";
    public bool WorkerEnabled { get; init; }
    public bool FinancialWritesEnabled { get; init; }
    public int WorkerBatchSize { get; init; } = 20;
    public int RecipientBatchSize { get; init; } = 100;
    public int WorkerIntervalSeconds { get; init; } = 5;
    public int RetryLimit { get; init; } = 5;
    public int RateLimitMultiplier { get; init; } = 1;
    public string[] TrustedProxies { get; init; } = [];
    public const int RequestBytes = 32 * 1024;

    public static RuntimeOptions Load(IConfiguration config, string environment, bool worker = false)
    {
        var dev = environment == "Development";
        var devIdentity = config.GetValue<bool>("V3:EnableDevelopmentIdentity");
        Require(dev || !devIdentity, "Development identity cannot run outside Development.");
        var connection = config.GetConnectionString("WeymelaV3");
        Require(dev || !string.IsNullOrWhiteSpace(connection), "A separate V3 database connection is required.");
        connection ??= "Host=127.0.0.1;Port=65432;Database=weymela_v3_dev_unconfigured;Username=unconfigured;Timeout=1";
        NpgsqlConnectionStringBuilder db;
        try { db = new(connection); } catch { throw new InvalidOperationException("Invalid V3 database configuration."); }
        Require((db.Database ?? "").StartsWith("weymela_v3_", StringComparison.Ordinal) || (db.Database ?? "").StartsWith("v3_test_", StringComparison.Ordinal), "Only an isolated V3 database may be configured.");
        if (devIdentity)
        {
            Require((db.Database ?? "").StartsWith("v3_test_", StringComparison.Ordinal) || (db.Database ?? "").StartsWith("weymela_v3_dev", StringComparison.Ordinal), "Development identities require a development/test database.");
            Require((config["V3:DevelopmentAccessKey"]?.Length ?? 0) >= 32, "Explicit development access key is required.");
        }
        if (!dev)
        {
            Require(environment is "Pilot" or "Production" or "Acceptance", "Unknown environment; use Development, Pilot or Production.");
            var prefix = environment == "Production" ? "weymela_v3_prod" : "weymela_v3_pilot";
            Require(environment == "Acceptance" || (db.Database ?? "").StartsWith(prefix, StringComparison.Ordinal), "Database must match the V3 environment.");
            Require(!string.IsNullOrWhiteSpace(db.Username) && !string.IsNullOrWhiteSpace(db.Password), "Database credentials must be provided externally.");
        }
        db.MaxPoolSize = Math.Min(db.MaxPoolSize, worker ? 5 : 20);
        db.MinPoolSize = 0; db.Timeout = Math.Min(db.Timeout, 10); db.CommandTimeout = 30;
        db.IncludeErrorDetail = false; db.LogParameters = false;
        var origins = config.GetSection("V3:AllowedOrigins").Get<string[]>() ?? [];
        var web = config["V3:PublicWebUrl"] ?? ""; var api = config["V3:PublicApiUrl"] ?? "";
        foreach (var origin in origins) Require(IsOrigin(origin, dev), "Allowed origins must be explicit origins, never wildcards.");
        var project = config["V3:Auth:FirebaseProjectId"] ?? "";
        var emailDelivery = config["V3:Auth:EmailDeliveryMode"] ?? "Disabled";
        var customToken = config["V3:Auth:FirebaseCustomTokenMode"] ?? "Disabled";
        if (!dev)
        {
            Require(config["V3:Auth:Provider"] == "Firebase", "Firebase authentication must be explicitly configured.");
            Require(System.Text.RegularExpressions.Regex.IsMatch(project, "^[a-z][a-z0-9-]{4,28}[a-z0-9]$"), "A valid Firebase project identity is required.");
            Require(origins.Length > 0 && IsOrigin(web, false) && IsOrigin(api, false) && origins.Contains(web, StringComparer.Ordinal), "Explicit HTTPS Web/API URLs and allowed Web origin are required.");
            Require(config["V3:Security:CameraPolicy"] == CameraPolicy, "The approved same-origin camera policy is required.");
            Require(config["V3:Security:TlsEdgeConfirmed"] == "true", "TLS edge and HSTS configuration must be confirmed.");
            Require(emailDelivery is "Disabled" or "Smtp" or "Provider", "Unsupported email delivery mode.");
            Require(customToken is "Disabled" or "FirebaseAdmin", "Unsupported Firebase custom-token mode.");
            if (emailDelivery != "Disabled" || customToken != "Disabled")
                Require(!string.IsNullOrWhiteSpace(config["V3:Auth:CodeHashKey"]), "An external auth code hash key is required when email authentication is enabled.");
            if (!worker) Require(Path.IsPathFullyQualified(config["V3:Auth:CookieKeyDirectory"] ?? "") && Path.IsPathFullyQualified(config["V3:Auth:CookieCertificatePath"] ?? ""), "External protected cookie-key storage is required.");
        }
        var deposits = config["V3:Deposits:Mode"] ?? "Disabled";
        Require(deposits is "Disabled" or "ManualApproval" || dev && deposits == "Development", "No configured deposit provider/approval mechanism.");
        var social = config["V3:Social:Mode"] ?? "Disabled";
        Require(social == "Disabled" || dev && social == "Test", "Live social providers require an approved adapter; test providers cannot run outside Development.");
        Require(!config.GetValue<bool>("V3:Push:Enabled"), "Push is not connected; in-app notifications do not require push.");
        var batch = config.GetValue("V3:Worker:BatchSize", 20); var interval = config.GetValue("V3:Worker:IntervalSeconds", 5);
        var recipientBatch = config.GetValue("V3:Worker:RecipientBatchSize", 100); var multiplier = config.GetValue("V3:RateLimitMultiplier", 1);
        Require(batch is >= 1 and <= 50 && interval is >= 2 and <= 60 && recipientBatch is >= 1 and <= 200, "Worker limits must be bounded.");
        Require(multiplier is >= 1 and <= 20 && (dev || multiplier == 1), "Rate-limit overrides are development-only.");
        var proxies = config.GetSection("V3:Security:TrustedProxies").Get<string[]>() ?? [];
        Require(proxies.All(p => System.Net.IPAddress.TryParse(p, out _)), "Trusted proxies must be explicit IP addresses.");
        return new()
        {
            EnvironmentName = environment, Development = dev, DevelopmentIdentity = devIdentity, ConnectionString = db.ConnectionString, AllowedOrigins = origins,
            PublicWebUrl = web, PublicApiUrl = api, FirebaseProjectId = project,
            EmailDeliveryMode = emailDelivery, FirebaseCustomTokenMode = customToken, AuthCodeHashKey = config["V3:Auth:CodeHashKey"],
            PinPepper = config["V3:Auth:PinPepper"],
            CookieKeyDirectory = config["V3:Auth:CookieKeyDirectory"] ?? "", CookieCertificatePath = config["V3:Auth:CookieCertificatePath"] ?? "",
            CookieCertificatePassword = config["V3:Auth:CookieCertificatePassword"], DepositMode = deposits, SocialMode = social,
            WorkerEnabled = config.GetValue("V3:Worker:Enabled", !dev), WorkerBatchSize = batch, WorkerIntervalSeconds = interval,
            FinancialWritesEnabled = config.GetValue("V3:FinancialWritesEnabled", dev),
            RecipientBatchSize = recipientBatch, RateLimitMultiplier = multiplier, TrustedProxies = proxies
        };
    }
    public static bool IsOrigin(string value, bool development) => Uri.TryCreate(value, UriKind.Absolute, out var u)
        && (u.Scheme == "https" || development && u.Scheme == "http" && u.IsLoopback)
        && u.AbsolutePath == "/" && string.IsNullOrEmpty(u.Query) && string.IsNullOrEmpty(u.Fragment)
        && string.IsNullOrEmpty(u.UserInfo) && !value.Contains('*') && value == u.GetLeftPart(UriPartial.Authority);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
