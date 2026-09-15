using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Weymela.Api.Auth;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[SupportedOSPlatform("linux")]
public sealed class DataProtectionRestartProbeTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly string directory = Path.Combine(Path.GetTempPath(),
        "v3-data-protection-probe-" + Guid.NewGuid().ToString("N"));
    private readonly string password = Convert.ToHexString(
        SHA256.HashData("isolated-data-protection-password"u8));

    public DataProtectionRestartProbeTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Protected_payload_survives_independent_provider_restart_with_persistent_keyring()
    {
        var certificate = CreateCertificate(Now.AddMinutes(-1), Now.AddHours(1), true);
        var keys = Path.Combine(directory, "keys");
        Directory.CreateDirectory(keys);

        DataProtectionRestartProbe.Verify(certificate, password, keys,
            "WeymelaV3-isolated-restart-test", Now);
        DataProtectionRestartProbe.Verify(certificate, password, keys,
            "WeymelaV3-isolated-restart-test", Now.AddMinutes(1));

        Assert.NotEmpty(Directory.GetFiles(keys));
        var sentinel = Directory.GetFiles(keys, ".weymela-data-protection-restart-probe").Single();
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(sentinel));
        Assert.DoesNotContain("WeymelaV3.DataProtection.RestartProbe.v1",
            File.ReadAllText(sentinel), StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_password_fails_without_exposing_password()
    {
        var certificate = CreateCertificate(Now.AddMinutes(-1), Now.AddHours(1), true);
        var keys = Path.Combine(directory, "wrong-password-keys");
        Directory.CreateDirectory(keys);
        var supplied = Convert.ToHexString(SHA256.HashData("wrong-password-fixture"u8));

        var error = Assert.Throws<InvalidOperationException>(() => DataProtectionRestartProbe.Verify(
            certificate, supplied, keys, "WeymelaV3-isolated-restart-test", Now));

        Assert.DoesNotContain(supplied, error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(keys));
    }

    [Fact]
    public void Different_certificate_cannot_read_the_existing_persistent_keyring()
    {
        var originalCertificate = CreateCertificate(Now.AddMinutes(-1), Now.AddHours(1), true);
        var replacementCertificate = CreateCertificate(Now.AddMinutes(-1), Now.AddHours(1), true);
        var keys = Path.Combine(directory, "wrong-certificate-keys");
        Directory.CreateDirectory(keys);
        DataProtectionRestartProbe.Verify(originalCertificate, password, keys,
            "WeymelaV3-isolated-restart-test", Now);

        var error = Assert.ThrowsAny<Exception>(() => DataProtectionRestartProbe.Verify(
            replacementCertificate, password, keys, "WeymelaV3-isolated-restart-test", Now));
        Assert.DoesNotContain(password, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Missing_private_key_or_expired_certificate_fails_closed(
        bool includePrivateKey, bool expired)
    {
        var certificate = CreateCertificate(
            expired ? Now.AddHours(-2) : Now.AddMinutes(-1),
            expired ? Now.AddHours(-1) : Now.AddHours(1),
            includePrivateKey);
        var keys = Path.Combine(directory, $"invalid-certificate-{includePrivateKey}-{expired}");
        Directory.CreateDirectory(keys);

        Assert.Throws<InvalidOperationException>(() => DataProtectionRestartProbe.Verify(
            certificate, password, keys, "WeymelaV3-isolated-restart-test", Now));
    }

    [Fact]
    public void Missing_or_non_directory_keyring_fails_closed()
    {
        var certificate = CreateCertificate(Now.AddMinutes(-1), Now.AddHours(1), true);
        var missing = Path.Combine(directory, "missing-keys");
        Assert.Throws<InvalidOperationException>(() => DataProtectionRestartProbe.Verify(
            certificate, password, missing, "WeymelaV3-isolated-restart-test", Now));

        var file = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(file, "synthetic fixture");
        Assert.Throws<InvalidOperationException>(() => DataProtectionRestartProbe.Verify(
            certificate, password, file, "WeymelaV3-isolated-restart-test", Now));
    }

    private string CreateCertificate(DateTime notBefore, DateTime notAfter, bool includePrivateKey)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=isolated-v3-data-protection", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pfx");
        byte[] material;
        if (includePrivateKey)
        {
            material = certificate.Export(X509ContentType.Pfx, password);
        }
        else
        {
            using var publicCertificate = X509CertificateLoader.LoadCertificate(
                certificate.Export(X509ContentType.Cert));
            material = publicCertificate.Export(X509ContentType.Pfx, password);
        }
        File.WriteAllBytes(path, material);
        CryptographicOperations.ZeroMemory(material);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
