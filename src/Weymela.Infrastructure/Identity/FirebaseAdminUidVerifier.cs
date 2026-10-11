using System.Text;
using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;

namespace Weymela.Infrastructure.Identity;

/// <summary>Confirms an operator-supplied UID exists and is enabled in the explicitly selected Firebase project.</summary>
public sealed class FirebaseAdminUidVerifier : IProductionFirebaseUidVerifier, IDisposable
{
    private readonly FirebaseApp app;
    private readonly FirebaseAuth auth;
    private readonly string projectId;

    public FirebaseAdminUidVerifier(string credentialsPath, string expectedProjectId)
    {
        projectId = expectedProjectId;
        try
        {
            if (!Path.IsPathFullyQualified(credentialsPath)) throw new InvalidOperationException();
            var bytes = File.ReadAllBytes(credentialsPath);
            if (bytes.Length is < 100 or > 64 * 1024) throw new InvalidOperationException();
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.GetProperty("type").GetString() != "service_account"
                || root.GetProperty("project_id").GetString() != expectedProjectId
                || string.IsNullOrWhiteSpace(root.GetProperty("client_email").GetString())
                || string.IsNullOrWhiteSpace(root.GetProperty("private_key").GetString()))
                throw new InvalidOperationException();
            var credential = CredentialFactory
                .FromJson<ServiceAccountCredential>(Encoding.UTF8.GetString(bytes))
                .ToGoogleCredential();
            app = FirebaseApp.Create(new AppOptions
            {
                Credential = credential,
                ProjectId = expectedProjectId
            }, "WeymelaV3Bootstrap-" + Guid.NewGuid().ToString("N"));
            auth = FirebaseAuth.GetAuth(app);
        }
        catch
        {
            throw new InvalidOperationException("The protected Firebase Admin credential is unavailable or does not match the selected Production project.");
        }
    }

    public async Task VerifyAsync(string projectId, string uid, CancellationToken cancellationToken)
    {
        if (!string.Equals(projectId, this.projectId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(uid) || uid.Length > 128)
            throw new InvalidOperationException("Firebase UID verification does not match the selected Production project.");
        try
        {
            var user = await auth.GetUserAsync(uid).WaitAsync(cancellationToken);
            if (!string.Equals(user.Uid, uid, StringComparison.Ordinal) || user.Disabled)
                throw new InvalidOperationException("The selected Production Firebase UID is missing or disabled.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            throw new InvalidOperationException("The selected UID could not be verified as an enabled user in the Production Firebase project.");
        }
    }

    public void Dispose() => app.Delete();
}
