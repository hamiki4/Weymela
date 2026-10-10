using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Providers;
using Weymela.Infrastructure.Persistence;
using Xunit;

namespace Weymela.Infrastructure.Tests;

public sealed class AdapterSecurityTests
{
    private static readonly DirectoryInfo PhotoDirectory = CreatePhotoDirectory();
    private static readonly DirectoryInfo ReviewMediaDirectory = CreateReviewMediaDirectory();
    private static DirectoryInfo CreatePhotoDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("v3-creator-photo-config-");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(directory.FullName,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return directory;
    }
    private static DirectoryInfo CreateReviewMediaDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("v3-review-media-config-");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(directory.FullName,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return directory;
    }
    private static string TestSecret(string purpose) => Convert.ToBase64String(
        SHA256.HashData(Encoding.UTF8.GetBytes("isolated-adapter-fixture-" + purpose)));
    private static Dictionary<string, string?> Config() => new()
    {
        ["ConnectionStrings:WeymelaV3"] = "Host=127.0.0.1;Port=1;Database=weymela_v3_pilot_test;Username=isolated;Password=test-only",
        ["V3:Auth:Provider"] = "Firebase", ["V3:Auth:FirebaseProjectId"] = "weymela-pilot",
        ["V3:Auth:EmailDeliveryMode"] = "Resend", ["V3:Auth:FirebaseCustomTokenMode"] = "FirebaseAdmin",
        ["V3:Auth:ResendApiKey"] = "re_" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes("isolated-resend-key"))),
        ["V3:Auth:ResendFromAddress"] = "no-reply@pilot-mail.weymela.com", ["V3:Auth:ResendFromName"] = "Weymela Pilot",
        ["GOOGLE_APPLICATION_CREDENTIALS"] = "/run/secrets/v3-firebase-admin.json",
        ["V3:Auth:CodeHashKey"] = TestSecret("code"), ["V3:Auth:PinPepper"] = TestSecret("pin"),
        ["V3:AllowedOrigins:0"] = "https://v3-pilot.weymela.com", ["V3:PublicWebUrl"] = "https://v3-pilot.weymela.com",
        ["V3:PublicApiUrl"] = "https://api-v3-pilot.weymela.com", ["V3:Security:CameraPolicy"] = RuntimeOptions.CameraPolicy,
        ["V3:Security:TlsEdgeConfirmed"] = "true", ["V3:Auth:CookieKeyDirectory"] = "/run/weymela-v3/keys",
        ["V3:Auth:CookieCertificatePath"] = "/run/secrets/v3-cookie-protection.pfx",
        ["V3:Auth:CookieCertificatePassword"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("isolated-cookie-password"))),
        ["V3:CreatorPhotos:Directory"] = PhotoDirectory.FullName,
        ["V3:ReviewMedia:Directory"] = ReviewMediaDirectory.FullName
    };
    private static Dictionary<string, string?> ProductionConfig()
    {
        var config = Config();
        config["ConnectionStrings:WeymelaV3"] = "Host=127.0.0.1;Port=1;Database=weymela_v3_prod_auth_test;Username=isolated;Password=test-only";
        config["V3:Auth:FirebaseProjectId"] = "weymela-production";
        config["V3:Auth:ResendFromAddress"] = "no-reply@mail.weymela.com";
        config["V3:Auth:ResendFromName"] = "Weymela";
        config["GOOGLE_APPLICATION_CREDENTIALS"] = "/run/secrets/weymela-production-firebase-admin.json";
        config["V3:Auth:CookieKeyDirectory"] = "/run/weymela-v3/production-keys";
        config["V3:Auth:CookieCertificatePath"] = "/run/secrets/weymela-production-cookie-protection.pfx";
        config["V3:AllowedOrigins:0"] = "https://weymela.com";
        config["V3:PublicWebUrl"] = "https://weymela.com";
        config["V3:PublicApiUrl"] = "https://api.weymela.com";
        return config;
    }
    [Fact] public void Fully_explicit_pilot_configuration_defaults_to_frozen_and_bounded_without_connecting()
    {
        var options = RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(Config()).Build(), "Pilot");
        Assert.False(options.DevelopmentIdentity); Assert.False(options.FinancialWritesEnabled); Assert.Equal("Disabled", options.DepositMode);
        Assert.Equal("Resend", options.EmailDeliveryMode); Assert.Equal("FirebaseAdmin", options.FirebaseCustomTokenMode);
        var database = new Npgsql.NpgsqlConnectionStringBuilder(options.ConnectionString);
        Assert.Equal("test-only", database.Password);
        Assert.Equal(20, database.MaxPoolSize);
        Assert.False(database.IncludeErrorDetail);
    }
    [Fact] public void Passfile_only_database_authentication_requires_a_private_absolute_regular_file()
    {
        if (!OperatingSystem.IsLinux()) return;
        var passfile = Path.Combine(Path.GetTempPath(), "v3-runtime-passfile-" + Guid.NewGuid().ToString("N"));
        var link = passfile + ".link";
        try
        {
            File.WriteAllText(passfile, "127.0.0.1:1:weymela_v3_pilot_test:isolated:fixture\n");
            File.SetUnixFileMode(passfile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var config = Config();
            var connection = new Npgsql.NpgsqlConnectionStringBuilder(config["ConnectionStrings:WeymelaV3"]);
            connection.Password = "";
            connection.Passfile = passfile;
            config["ConnectionStrings:WeymelaV3"] = connection.ConnectionString;

            var options = RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot");
            var loaded = new Npgsql.NpgsqlConnectionStringBuilder(options.ConnectionString);
            Assert.True(string.IsNullOrEmpty(loaded.Password));
            Assert.Equal(passfile, loaded.Passfile);

            connection.Passfile = passfile + ".missing";
            config["ConnectionStrings:WeymelaV3"] = connection.ConnectionString;
            Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
                new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot"));

            connection.Passfile = passfile;
            config["ConnectionStrings:WeymelaV3"] = connection.ConnectionString;

            File.SetUnixFileMode(passfile, UnixFileMode.UserRead | UnixFileMode.GroupRead);
            Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
                new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot"));

            File.SetUnixFileMode(passfile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.CreateSymbolicLink(link, passfile);
            connection.Passfile = link;
            config["ConnectionStrings:WeymelaV3"] = connection.ConnectionString;
            Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
                new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot"));

            connection.Passfile = Path.GetFileName(passfile);
            config["ConnectionStrings:WeymelaV3"] = connection.ConnectionString;
            Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
                new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot"));
        }
        finally
        {
            if (File.Exists(link)) File.Delete(link);
            if (File.Exists(passfile)) File.Delete(passfile);
        }
    }
    [Fact] public void Pilot_accepts_the_approved_single_origin_without_weakening_other_runtime_guards()
    {
        var config = Config();
        config["V3:AllowedOrigins:0"] = "https://pilot.weymela.com";
        config["V3:PublicWebUrl"] = "https://pilot.weymela.com";
        config["V3:PublicApiUrl"] = "https://pilot.weymela.com";
        var options = RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot");
        Assert.Equal(["https://pilot.weymela.com"], options.AllowedOrigins);
        Assert.Equal("https://pilot.weymela.com", options.PublicWebUrl);
        Assert.Equal("https://pilot.weymela.com", options.PublicApiUrl);
        Assert.False(options.FinancialWritesEnabled);
    }
    [Fact] public void Pilot_rejects_mixed_legacy_and_single_origin_endpoints()
    {
        var config = Config();
        config["V3:AllowedOrigins:0"] = "https://pilot.weymela.com";
        config["V3:PublicWebUrl"] = "https://pilot.weymela.com";
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot"));
    }
    [Fact] public void Pilot_financial_test_window_is_bounded_and_expires_without_restart()
    {
        var directory = Directory.CreateTempSubdirectory("v3-pilot-receipts-");
        try
        {
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(directory.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var config = Config();
            config["V3:Deposits:Mode"] = "ManualApproval";
            config["V3:Deposits:ReceiptDirectory"] = directory.FullName;
            config["V3:FinancialWritesEnabled"] = "true";
            config["V3:PilotFinancialWritesMode"] = "Timed";
            config["V3:PilotFinancialWritesUntilUtc"] = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var options = RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot");
            Assert.True(options.FinancialWritesActive(DateTime.UtcNow));
            Assert.False(options.FinancialWritesActive(options.PilotFinancialWritesUntilUtc!.Value));
            config["V3:PilotFinancialWritesUntilUtc"] = DateTime.UtcNow.AddHours(5).ToString("yyyy-MM-ddTHH:mm:ssZ");
            Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
                new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot"));
            config["V3:PilotFinancialWritesUntilUtc"] = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
            config["V3:Deposits:Mode"] = "Disabled";
            Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
                new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot"));
        }
        finally { directory.Delete(); }
    }
    [Fact] public void Pilot_uat_financial_mode_is_durable_explicit_and_requires_manual_approval()
    {
        var directory = Directory.CreateTempSubdirectory("v3-pilot-uat-receipts-");
        try
        {
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(directory.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var config = Config();
            config["V3:Deposits:Mode"] = "ManualApproval";
            config["V3:Deposits:ReceiptDirectory"] = directory.FullName;
            config["V3:FinancialWritesEnabled"] = "true";
            config["V3:PilotFinancialWritesMode"] = "Uat";
            config["V3:PilotFinancialWritesUntilUtc"] = "disabled";
            var options = RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot");
            Assert.True(options.FinancialWritesActive(DateTime.MaxValue));
            Assert.Equal("Uat", options.PilotFinancialWritesMode);
            Assert.Null(options.PilotFinancialWritesUntilUtc);

            config["V3:PilotFinancialWritesUntilUtc"] = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
            Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
                new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot"));
            config["V3:PilotFinancialWritesUntilUtc"] = "disabled";
            config["V3:Deposits:Mode"] = "Disabled";
            Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
                new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot"));
        }
        finally { directory.Delete(); }
    }
    [Fact] public void Pilot_api_manual_deposits_require_an_existing_private_receipt_directory()
    {
        var directory = Directory.CreateTempSubdirectory("v3-api-receipts-");
        try
        {
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(directory.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var config = Config();
            config["V3:Deposits:Mode"] = "ManualApproval";
            config["V3:Deposits:ReceiptDirectory"] = directory.FullName;
            var valid = RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot");
            Assert.Equal(directory.FullName, valid.ReceiptDirectory);
            Assert.False(valid.FinancialWritesEnabled);

            config["V3:Deposits:ReceiptDirectory"] = Path.Combine(directory.FullName, "missing");
            Assert.Contains("private durable receipt directory", Assert.Throws<InvalidOperationException>(() =>
                RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot")).Message);
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute | UnixFileMode.GroupRead);
                config["V3:Deposits:ReceiptDirectory"] = directory.FullName;
                Assert.Contains("mode 0700", Assert.Throws<InvalidOperationException>(() =>
                    RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot")).Message);
            }
        }
        finally { directory.Delete(); }
    }
    [Fact] public void Pilot_worker_manual_deposits_start_without_receipt_storage_or_financial_writes()
    {
        var config = Config();
        config.Remove("V3:CreatorPhotos:Directory");
        config.Remove("V3:ReviewMedia:Directory");
        config["V3:Deposits:Mode"] = "ManualApproval";
        config["V3:FinancialWritesEnabled"] = "false";
        var options = RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot", worker: true);
        Assert.Equal("ManualApproval", options.DepositMode);
        Assert.Equal("", options.ReceiptDirectory);
        Assert.False(options.FinancialWritesEnabled);

        config["V3:Deposits:ReceiptDirectory"] = Path.GetTempPath();
        Assert.Contains("Worker cannot configure private receipt storage", Assert.Throws<InvalidOperationException>(() =>
            RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot", worker: true)).Message);
        config.Remove("V3:Deposits:ReceiptDirectory");
        config["V3:CreatorPhotos:Directory"] = PhotoDirectory.FullName;
        Assert.Contains("Worker cannot configure private Creator photo storage", Assert.Throws<InvalidOperationException>(() =>
            RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot", worker: true)).Message);
        config.Remove("V3:CreatorPhotos:Directory");
        config["V3:ReviewMedia:Directory"] = ReviewMediaDirectory.FullName;
        Assert.Contains("Worker cannot configure private review media storage", Assert.Throws<InvalidOperationException>(() =>
            RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot", worker: true)).Message);
    }
    [Fact] public void Pilot_api_requires_a_separate_private_creator_photo_directory()
    {
        var config = Config();
        config.Remove("V3:CreatorPhotos:Directory");
        Assert.Contains("private durable Creator photo directory", Assert.Throws<InvalidOperationException>(() =>
            RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot")).Message);
        config["V3:CreatorPhotos:Directory"] = PhotoDirectory.FullName;
        Assert.Equal(PhotoDirectory.FullName, RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot").CreatorPhotoDirectory);
    }
    [Fact] public void Pilot_api_requires_a_separate_private_review_media_directory()
    {
        var config = Config();
        config.Remove("V3:ReviewMedia:Directory");
        Assert.Contains("private durable review media directory", Assert.Throws<InvalidOperationException>(() =>
            RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot")).Message);
        config["V3:ReviewMedia:Directory"] = ReviewMediaDirectory.FullName;
        Assert.Equal(ReviewMediaDirectory.FullName,
            RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot").ReviewMediaDirectory);
    }
    [Fact] public void Pilot_modes_replace_only_the_disabled_adapter_registrations()
    {
        var options = RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(Config()).Build(), "Pilot");
        var services = new ServiceCollection();
        services.AddWeymelaPersistence(options.ConnectionString).AddConfiguredAuthenticationAdapters(options);
        Assert.Equal(ServiceLifetime.Singleton, services.Last(x => x.ServiceType == typeof(IEmailCodeDelivery)).Lifetime);
        Assert.NotNull(services.Last(x => x.ServiceType == typeof(IEmailCodeDelivery)).ImplementationFactory);
        Assert.Equal(typeof(FirebaseAdminCustomTokenIssuer), services.Last(x => x.ServiceType == typeof(IFirebaseCustomTokenIssuer)).ImplementationType);

        var disabled = new ServiceCollection();
        disabled.AddWeymelaPersistence(options.ConnectionString).AddConfiguredAuthenticationAdapters(new RuntimeOptions());
        Assert.Equal(typeof(DisabledEmailCodeDelivery), disabled.Last(x => x.ServiceType == typeof(IEmailCodeDelivery)).ImplementationType);
        Assert.Equal(typeof(DisabledFirebaseCustomTokenIssuer), disabled.Last(x => x.ServiceType == typeof(IFirebaseCustomTokenIssuer)).ImplementationType);
    }
    [Fact] public void Production_requires_project_scoped_firebase_and_resend_configuration()
    {
        var config = ProductionConfig();
        var production = RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production");
        Assert.Equal("weymela-production", production.FirebaseProjectId);
        Assert.Equal("Resend", production.EmailDeliveryMode);
        Assert.Equal("FirebaseAdmin", production.FirebaseCustomTokenMode);
        Assert.Equal("/run/secrets/weymela-production-firebase-admin.json", production.FirebaseAdminCredentialsPath);
        Assert.False(production.FinancialWritesEnabled);
        Assert.Equal("Disabled", production.DepositMode);

        config = ProductionConfig();
        config["ConnectionStrings:WeymelaV3"] = "Host=127.0.0.1;Port=1;Database=weymela_v3_pilot;Username=isolated;Password=test-only";
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production"));

        config = ProductionConfig();
        config["V3:Auth:FirebaseProjectId"] = "weymela-pilot";
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production"));

        config = ProductionConfig();
        config["V3:AllowedOrigins:1"] = "https://pilot.weymela.com";
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production"));

        config = ProductionConfig();
        config["V3:Auth:EmailDeliveryMode"] = "Disabled";
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production"));

        config = ProductionConfig();
        config["V3:Auth:ResendFromAddress"] = "no-reply@pilot-mail.weymela.com";
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production"));

        config = ProductionConfig();
        config["GOOGLE_APPLICATION_CREDENTIALS"] = "relative.json";
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production"));

        config = ProductionConfig();
        config["V3:Auth:CookieKeyDirectory"] = "/run/weymela-v3/keys";
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production"));

        config = ProductionConfig();
        config["V3:Auth:PinPepper"] = config["V3:Auth:CodeHashKey"];
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production"));

        config = ProductionConfig();
        config["V3:FinancialWritesEnabled"] = "true";
        config["V3:PilotFinancialWritesUntilUtc"] = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production"));
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production", worker: true));
        config["V3:PilotFinancialWritesMode"] = "Uat";
        config["V3:PilotFinancialWritesUntilUtc"] = "disabled";
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production"));
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production", worker: true));
    }
    [Fact] public void Production_api_registers_auth_adapters_but_worker_does_not()
    {
        var config = ProductionConfig();
        var production = RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production");
        var services = new ServiceCollection();
        services.AddWeymelaPersistence(production.ConnectionString).AddConfiguredAuthenticationAdapters(production);
        Assert.NotNull(services.Last(x => x.ServiceType == typeof(IEmailCodeDelivery)).ImplementationFactory);
        Assert.Equal(typeof(FirebaseAdminCustomTokenIssuer), services.Last(x => x.ServiceType == typeof(IFirebaseCustomTokenIssuer)).ImplementationType);

        config.Remove("V3:CreatorPhotos:Directory");
        config.Remove("V3:ReviewMedia:Directory");
        var worker = RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Production", worker: true);
        Assert.True(worker.IsWorkerProcess);
        var workerServices = new ServiceCollection();
        workerServices.AddWeymelaPersistence(worker.ConnectionString).AddConfiguredAuthenticationAdapters(worker);
        Assert.Equal(typeof(DisabledEmailCodeDelivery), workerServices.Last(x => x.ServiceType == typeof(IEmailCodeDelivery)).ImplementationType);
        Assert.Equal(typeof(DisabledFirebaseCustomTokenIssuer), workerServices.Last(x => x.ServiceType == typeof(IFirebaseCustomTokenIssuer)).ImplementationType);
    }
    [Fact] public void Financial_write_gate_fails_closed_for_manually_constructed_non_pilot_options()
    {
        Assert.False(new RuntimeOptions { EnvironmentName = "Production", FinancialWritesEnabled = true,
            PilotFinancialWritesMode = "Uat" }.FinancialWritesActive(DateTime.UtcNow));
        Assert.False(new RuntimeOptions { EnvironmentName = "Pilot", FinancialWritesEnabled = true,
            PilotFinancialWritesMode = "Timed" }.FinancialWritesActive(DateTime.UtcNow));
    }
    [Theory]
    [InlineData("V3:Auth:Provider", "")][InlineData("V3:Auth:FirebaseProjectId", "")]
    [InlineData("V3:AllowedOrigins:0", "*")][InlineData("V3:PublicWebUrl", "http://unsafe.invalid")]
    [InlineData("V3:EnableDevelopmentIdentity", "true")][InlineData("V3:Deposits:Mode", "Development")]
    [InlineData("V3:Social:Mode", "Test")][InlineData("V3:Social:Mode", "TikTok")]
    [InlineData("V3:Push:Enabled", "true")][InlineData("V3:Security:CameraPolicy", "camera=*")]
    [InlineData("V3:Security:TlsEdgeConfirmed", "false")][InlineData("V3:Worker:BatchSize", "10000")]
    [InlineData("V3:RateLimitMultiplier", "20")][InlineData("V3:Auth:CookieCertificatePath", "relative.pfx")]
    [InlineData("V3:Auth:EmailDeliveryMode", "Disabled")][InlineData("V3:Auth:FirebaseCustomTokenMode", "Disabled")]
    [InlineData("V3:Auth:ResendApiKey", "")][InlineData("V3:Auth:ResendFromAddress", "other@example.com")]
    [InlineData("V3:Auth:ResendFromName", "Other")][InlineData("V3:Auth:CodeHashKey", "weak")]
    [InlineData("V3:Auth:PinPepper", "weak")][InlineData("GOOGLE_APPLICATION_CREDENTIALS", "/tmp/other.json")]
    [InlineData("V3:Auth:CodeHashKey", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("V3:Auth:PinPepper", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("V3:Auth:CookieCertificatePassword", "short")]
    [InlineData("V3:Auth:ResendApiKey", "re_replace-me-with-a-secret")]
    [InlineData("V3:Auth:CookieCertificatePassword", "replace-me-password")]
    [InlineData("V3:FinancialWritesEnabled", "true")]
    public void Unsafe_pilot_configuration_fails_closed(string key, string value)
    {
        var config = Config(); config[key] = value;
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot"));
    }
    [Fact] public void Pilot_rejects_reused_auth_code_and_pin_secret_material()
    {
        var config = Config(); config["V3:Auth:PinPepper"] = config["V3:Auth:CodeHashKey"];
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), "Pilot"));
    }
    [Theory] [InlineData("https://evil.invalid/profile")][InlineData("javascript:alert(1)")]
    [InlineData("https://www.tiktok.com@evil.invalid/x")][InlineData("https://www.tiktok.com.evil.invalid/x")][InlineData("http://www.tiktok.com/x")]
    public void Public_links_reject_untrusted_or_unsafe_urls(string value) => Assert.Null(PersistentWorkspaceDirectory.SafeUrl(value));
    [Fact] public async Task Signed_firebase_identity_validates_project_but_does_not_trust_role_claims()
    {
        using var signer = new TokenFixture(); var result = await signer.Verifier.VerifyAsync(signer.Token(), default);
        Assert.Equal("trusted-subject", result.Subject); Assert.Equal("isolated-v3-test", result.ProjectId);
        Assert.DoesNotContain(result.GetType().GetProperties(), p => p.Name.Contains("Role"));
    }
    [Theory] [InlineData("issuer")][InlineData("audience")][InlineData("expired")][InlineData("old-auth")]
    [InlineData("future-auth")][InlineData("wrong-key")][InlineData("empty-subject")][InlineData("missing-auth")]
    public async Task Invalid_firebase_tokens_fail_without_exposing_token(string defect)
    {
        using var signer = new TokenFixture(); var token = signer.Token(defect);
        var error = await Assert.ThrowsAsync<ApplicationFailure>(() => signer.Verifier.VerifyAsync(token, default));
        Assert.Equal(FailureKind.Forbidden, error.Kind); Assert.DoesNotContain(token, error.Message); Assert.DoesNotContain("trusted-subject", error.Message);
    }
    [Theory] [InlineData("not-a-token")][InlineData("eyJhbGciOiJub25lIn0.e30.")]
    public async Task Malformed_and_unsigned_tokens_are_rejected(string token)
    { using var signer = new TokenFixture(); await Assert.ThrowsAsync<ApplicationFailure>(() => signer.Verifier.VerifyAsync(token, default)); }
    [Theory] [InlineData("ownership")][InlineData("creator")][InlineData("content")][InlineData("negative")][InlineData("future")][InlineData("stale")][InlineData("health")]
    public async Task Social_adapter_rejects_unverified_or_mismatched_evidence(string defect)
    {
        var clock = new TestClock(); var adapter = new FakeSocial(clock, defect);
        await Assert.ThrowsAsync<ApplicationFailure>(() => new SocialProviderRouter([adapter], clock).VerifyAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Fake", "content"), default));
    }
    [Fact] public async Task Capability_verified_social_adapter_preserves_provider_neutral_evidence()
    {
        var clock = new TestClock(); var result = await new SocialProviderRouter([new FakeSocial(clock, "")], clock).VerifyAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Fake", "content"), default);
        Assert.Equal(1234, result.Count); Assert.Equal("Fake", result.Provider);
    }
    private sealed class FakeSocial(TestClock clock, string defect) : ISocialVerificationAdapter
    {
        public ProviderCapabilities Capabilities => new("Fake", true, true, true);
        public Task<ProviderHealth> HealthAsync(CancellationToken ct) => Task.FromResult(new ProviderHealth("Fake", defect == "health" ? ProviderHealthState.Unavailable : ProviderHealthState.Healthy, "test", clock.Now));
        public Task<VerifiedContentEvidence> VerifyAsync(VerifiedViewRequest r, CancellationToken ct) => Task.FromResult(new VerifiedContentEvidence(
            defect == "creator" ? Guid.NewGuid() : r.CreatorId, r.Provider, defect == "content" ? "wrong" : r.ExternalContentId,
            defect != "ownership", defect == "negative" ? -1 : 1234, defect == "future" ? clock.Now.AddMinutes(1) : defect == "stale" ? clock.Now.AddHours(-1) : clock.Now, "evidence"));
    }
    private sealed class TokenFixture : IDisposable, IIdentitySigningKeys
    {
        private readonly RSA rsa = RSA.Create(2048); private readonly TestClock clock = new();
        public FirebaseTokenVerifier Verifier => new(new RuntimeOptions { FirebaseProjectId = "isolated-v3-test" }, this, clock);
        public Task<SecurityKey?> FindAsync(string keyId, CancellationToken ct) => Task.FromResult<SecurityKey?>(keyId == "test-key" ? new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = keyId } : null);
        public string Token(string defect = "")
        {
            var claims = new Dictionary<string, object> { ["sub"] = defect == "empty-subject" ? "" : "trusted-subject", ["role"] = "PlatformAdmin" };
            if (defect != "missing-auth") claims["auth_time"] = new DateTimeOffset(clock.Now.AddMinutes(defect == "old-auth" ? -10 : defect == "future-auth" ? 1 : -1)).ToUnixTimeSeconds();
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
                Issuer = defect == "issuer" ? "https://evil.invalid" : "https://securetoken.google.com/isolated-v3-test",
                Audience = defect == "audience" ? "other-project" : "isolated-v3-test", Claims = claims,
                IssuedAt = clock.Now.AddMinutes(defect == "expired" ? -10 : 0), NotBefore = clock.Now.AddMinutes(-10), Expires = clock.Now.AddMinutes(defect == "expired" ? -1 : 60),
                SigningCredentials = new(new RsaSecurityKey(rsa) { KeyId = defect == "wrong-key" ? "unknown" : "test-key" }, SecurityAlgorithms.RsaSha256)
            });
        }
        public void Dispose() => rsa.Dispose();
    }
}
