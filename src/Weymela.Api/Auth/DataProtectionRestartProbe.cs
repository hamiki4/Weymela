using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Weymela.Api.Auth;

[SupportedOSPlatform("linux")]
public static class DataProtectionRestartProbe
{
    private const string SentinelName = ".weymela-data-protection-restart-probe";
    private const string SentinelPlaintext = "WeymelaV3.DataProtection.RestartProbe.v1";

    public static void Verify(
        string certificatePath,
        string? certificatePassword,
        string keyDirectory,
        string applicationName,
        DateTime nowUtc)
    {
        if (!Path.IsPathFullyQualified(certificatePath) || !Path.IsPathFullyQualified(keyDirectory)
            || string.IsNullOrWhiteSpace(certificatePassword) || string.IsNullOrWhiteSpace(applicationName))
            throw new InvalidOperationException("Data Protection probe configuration is unavailable.");
        if (!File.Exists(certificatePath) || File.GetAttributes(certificatePath).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("Data Protection certificate is unavailable.");
        if (!Directory.Exists(keyDirectory) || File.GetAttributes(keyDirectory).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("Data Protection key directory is unavailable.");

        using var certificate = LoadCertificate(certificatePath, certificatePassword, nowUtc);
        VerifyWritable(keyDirectory);
        var sentinelPath = Path.Combine(keyDirectory, SentinelName);
        if (File.Exists(sentinelPath))
        {
            var attributes = File.GetAttributes(sentinelPath);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)
                || (File.GetUnixFileMode(sentinelPath) & ~(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite)) != 0)
                throw new InvalidOperationException("Data Protection restart sentinel is unavailable.");
        }
        string protectedPayload;
        using (var first = Provider(certificate, keyDirectory, applicationName))
        {
            var protector = first.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("WeymelaV3.PilotRestartProbe");
            if (File.Exists(sentinelPath))
            {
                protectedPayload = File.ReadAllText(sentinelPath);
                VerifySentinel(protector.Unprotect(protectedPayload));
            }
            else
            {
                protectedPayload = protector.Protect(SentinelPlaintext);
                WriteSentinel(sentinelPath, protectedPayload);
            }
        }
        using (var restarted = Provider(certificate, keyDirectory, applicationName))
        {
            var recovered = restarted.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("WeymelaV3.PilotRestartProbe").Unprotect(protectedPayload);
            VerifySentinel(recovered);
        }
    }

    private static X509Certificate2 LoadCertificate(string path, string password, DateTime nowUtc)
    {
        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password);
        }
        catch
        {
            throw new InvalidOperationException("Data Protection certificate or password is invalid.");
        }
        var now = nowUtc.ToUniversalTime();
        if (!certificate.HasPrivateKey || certificate.NotBefore.ToUniversalTime() > now
            || certificate.NotAfter.ToUniversalTime() <= now)
        {
            certificate.Dispose();
            throw new InvalidOperationException("A current private Data Protection certificate is required.");
        }
        return certificate;
    }

    private static ServiceProvider Provider(X509Certificate2 certificate, string keyDirectory, string applicationName)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName(applicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
            .ProtectKeysWithCertificate(certificate);
        return services.BuildServiceProvider();
    }

    private static void VerifyWritable(string keyDirectory)
    {
        var path = Path.Combine(keyDirectory, ".weymela-write-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1, FileOptions.WriteThrough);
            stream.WriteByte(0);
            stream.Flush(true);
        }
        catch
        {
            throw new InvalidOperationException("Data Protection key directory is not writable.");
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static void WriteSentinel(string path, string protectedPayload)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(protectedPayload);
        try
        {
            using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
            stream.Write(bytes);
            stream.Flush(true);
        }
        catch
        {
            throw new InvalidOperationException("Data Protection restart sentinel cannot be persisted.");
        }
    }

    private static void VerifySentinel(string recovered)
    {
        if (!string.Equals(recovered, SentinelPlaintext, StringComparison.Ordinal))
            throw new InvalidOperationException("Data Protection restart persistence failed.");
    }
}
