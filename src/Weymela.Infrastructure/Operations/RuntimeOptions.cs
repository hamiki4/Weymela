using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

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
    public string? ResendApiKey { get; init; }
    public string ResendFromAddress { get; init; } = "";
    public string ResendFromName { get; init; } = "";
    public string FirebaseAdminCredentialsPath { get; init; } = "";
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
        var resendApiKey = config["V3:Auth:ResendApiKey"];
        var resendFromAddress = config["V3:Auth:ResendFromAddress"] ?? "";
        var resendFromName = config["V3:Auth:ResendFromName"] ?? "";
        var firebaseAdminCredentials = config["GOOGLE_APPLICATION_CREDENTIALS"] ?? "";
        if (!dev)
        {
            Require(config["V3:Auth:Provider"] == "Firebase", "Firebase authentication must be explicitly configured.");
            Require(System.Text.RegularExpressions.Regex.IsMatch(project, "^[a-z][a-z0-9-]{4,28}[a-z0-9]$"), "A valid Firebase project identity is required.");
            Require(origins.Length > 0 && IsOrigin(web, false) && IsOrigin(api, false) && origins.Contains(web, StringComparer.Ordinal), "Explicit HTTPS Web/API URLs and allowed Web origin are required.");
            Require(config["V3:Security:CameraPolicy"] == CameraPolicy, "The approved same-origin camera policy is required.");
            Require(config["V3:Security:TlsEdgeConfirmed"] == "true", "TLS edge and HSTS configuration must be confirmed.");
            Require(emailDelivery is "Disabled" or "Resend", "Unsupported email delivery mode.");
            Require(customToken is "Disabled" or "FirebaseAdmin", "Unsupported Firebase custom-token mode.");
            if (environment == "Production")
                Require(emailDelivery == "Disabled" && customToken == "Disabled", "Production authentication adapters are not activated by the Pilot implementation.");
            if (emailDelivery != "Disabled" || customToken != "Disabled")
                Require(!string.IsNullOrWhiteSpace(config["V3:Auth:CodeHashKey"]), "An external auth code hash key is required when email authentication is enabled.");
            if (!worker) Require(Path.IsPathFullyQualified(config["V3:Auth:CookieKeyDirectory"] ?? "") && Path.IsPathFullyQualified(config["V3:Auth:CookieCertificatePath"] ?? ""), "External protected cookie-key storage is required.");
            if (!worker && environment == "Pilot")
            {
                Require(project == "weymela-pilot", "Pilot must use the approved Firebase project.");
                Require(emailDelivery == "Resend", "Pilot requires the Resend email delivery adapter.");
                Require(customToken == "FirebaseAdmin", "Pilot requires Firebase Admin custom-token signing.");
                Require(IsResendKey(resendApiKey), "A protected Resend API key is required.");
                Require(resendFromAddress == "no-reply@pilot-mail.weymela.com" && IsEmail(resendFromAddress), "The approved Pilot Resend sender address is required.");
                Require(resendFromName == "Weymela Pilot", "The approved Pilot Resend sender name is required.");
                var codeHashKey = SecretMaterial(config["V3:Auth:CodeHashKey"], 32);
                var pinPepper = SecretMaterial(config["V3:Auth:PinPepper"], 32);
                Require(codeHashKey is not null, "The auth code hash key must be strict base64-encoded 32+ byte secret material.");
                Require(pinPepper is not null, "The PIN pepper must be strict base64-encoded 32+ byte secret material.");
                Require(!CryptographicOperations.FixedTimeEquals(codeHashKey!, pinPepper!), "Auth code and PIN secrets must be independent.");
                Require(firebaseAdminCredentials == "/run/secrets/v3-firebase-admin.json" && Path.IsPathFullyQualified(firebaseAdminCredentials), "The approved external Firebase Admin credential file is required.");
                Require(config["V3:Auth:CookieKeyDirectory"] == "/run/weymela-v3/keys"
                    && config["V3:Auth:CookieCertificatePath"] == "/run/secrets/v3-cookie-protection.pfx",
                    "Pilot cookie protection must use the approved mounted paths.");
                Require(IsProtectedPassword(config["V3:Auth:CookieCertificatePassword"]),
                    "A protected cookie certificate password is required.");
                var legacyOrigins = web == "https://v3-pilot.weymela.com"
                    && api == "https://api-v3-pilot.weymela.com";
                var singleOrigin = web == "https://pilot.weymela.com"
                    && api == "https://pilot.weymela.com";
                Require((legacyOrigins || singleOrigin)
                    && origins.SequenceEqual([web], StringComparer.Ordinal),
                    "Pilot Web/API origins must match an approved endpoint set.");
            }
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
        var financialWrites = config.GetValue("V3:FinancialWritesEnabled", dev);
        Require(dev || !financialWrites, "Financial writes must remain disabled outside Development.");
        var proxies = config.GetSection("V3:Security:TrustedProxies").Get<string[]>() ?? [];
        Require(proxies.All(p => System.Net.IPAddress.TryParse(p, out _)), "Trusted proxies must be explicit IP addresses.");
        return new()
        {
            EnvironmentName = environment, Development = dev, DevelopmentIdentity = devIdentity, ConnectionString = db.ConnectionString, AllowedOrigins = origins,
            PublicWebUrl = web, PublicApiUrl = api, FirebaseProjectId = project,
            EmailDeliveryMode = emailDelivery, FirebaseCustomTokenMode = customToken, AuthCodeHashKey = config["V3:Auth:CodeHashKey"],
            PinPepper = config["V3:Auth:PinPepper"],
            ResendApiKey = resendApiKey, ResendFromAddress = resendFromAddress, ResendFromName = resendFromName,
            FirebaseAdminCredentialsPath = firebaseAdminCredentials,
            CookieKeyDirectory = config["V3:Auth:CookieKeyDirectory"] ?? "", CookieCertificatePath = config["V3:Auth:CookieCertificatePath"] ?? "",
            CookieCertificatePassword = config["V3:Auth:CookieCertificatePassword"], DepositMode = deposits, SocialMode = social,
            WorkerEnabled = config.GetValue("V3:Worker:Enabled", !dev), WorkerBatchSize = batch, WorkerIntervalSeconds = interval,
            FinancialWritesEnabled = financialWrites,
            RecipientBatchSize = recipientBatch, RateLimitMultiplier = multiplier, TrustedProxies = proxies
        };
    }
    public static bool IsOrigin(string value, bool development) => Uri.TryCreate(value, UriKind.Absolute, out var u)
        && (u.Scheme == "https" || development && u.Scheme == "http" && u.IsLoopback)
        && u.AbsolutePath == "/" && string.IsNullOrEmpty(u.Query) && string.IsNullOrEmpty(u.Fragment)
        && string.IsNullOrEmpty(u.UserInfo) && !value.Contains('*') && value == u.GetLeftPart(UriPartial.Authority);
    private static bool IsEmail(string value) => value.Length is > 3 and <= 320
        && System.Text.RegularExpressions.Regex.IsMatch(value, "^[^@\\s<>]+@[^@\\s<>]+$")
        && !value.Any(char.IsControl);
    private static bool IsResendKey(string? value) => value is { Length: >= 20 and <= 256 }
        && value.StartsWith("re_", StringComparison.Ordinal)
        && value.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')
        && !ContainsPlaceholder(value);
    private static bool IsProtectedPassword(string? value) => value is { Length: >= 16 and <= 512 }
        && !value.Any(char.IsControl) && !ContainsPlaceholder(value);
    private static bool ContainsPlaceholder(string value) =>
        new[] { "placeholder", "replace-me", "replace_me", "change-me", "example", "external", "test-only", "not-a-live" }
            .Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    private static byte[]? SecretMaterial(string? value, int minimum)
    {
        try
        {
            if (value is null || value.Any(char.IsWhiteSpace)) return null;
            var bytes = Convert.FromBase64String(value);
            if (Convert.ToBase64String(bytes) != value || bytes.Length < minimum || bytes.Distinct().Count() < 16
                || bytes.SequenceEqual(Enumerable.Range(1, 32).Select(x => (byte)x))) return null;
            var printable = Encoding.UTF8.GetString(bytes).ToLowerInvariant();
            return ContainsPlaceholder(printable) ? null : bytes;
        }
        catch (FormatException) { return null; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
