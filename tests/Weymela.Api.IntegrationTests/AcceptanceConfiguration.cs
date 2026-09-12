using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace Weymela.Api.IntegrationTests;

// Ephemeral self-signed test key only. No live certificate/configuration is read.
internal sealed class AcceptanceConfiguration : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "v3-acceptance-" + Guid.NewGuid().ToString("N"));
    public AcceptanceConfiguration()
    {
        Directory.CreateDirectory(directory); using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=isolated-v3-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        File.WriteAllBytes(Path.Combine(directory, "test.pfx"), cert.Export(X509ContentType.Pfx));
    }
    public void Apply(IConfigurationBuilder builder) => builder.AddInMemoryCollection(new Dictionary<string,string?> {
        ["ConnectionStrings:WeymelaV3"]="Host=127.0.0.1;Port=1;Database=v3_test_acceptance;Username=unused;Password=test-only",
        ["V3:Auth:Provider"]="Firebase",["V3:Auth:FirebaseProjectId"]="isolated-v3-test",["V3:AllowedOrigins:0"]="https://localhost",
        ["V3:PublicWebUrl"]="https://localhost",["V3:PublicApiUrl"]="https://localhost",
        ["V3:Security:CameraPolicy"]=Weymela.Infrastructure.Operations.RuntimeOptions.CameraPolicy,["V3:Security:TlsEdgeConfirmed"]="true",
        ["V3:Auth:CookieKeyDirectory"]=Path.Combine(directory,"keys"),["V3:Auth:CookieCertificatePath"]=Path.Combine(directory,"test.pfx")
    });
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
