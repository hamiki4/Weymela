namespace Weymela.Application.Operations;

public enum AuthIdentifierKind { Email, Phone }
public enum EmailCodePurpose { Signup, DeviceEnrollment, PinRecovery }
public enum RoleEnrollmentStatus { Pending, Approved, Rejected }

public sealed record EmailCodeStartRequest(
    AuthIdentifierKind IdentifierKind,
    string Identifier,
    string? SecondaryIdentifier,
    EmailCodePurpose Purpose);

public sealed record EmailCodeVerificationRequest(
    string Identifier,
    EmailCodePurpose Purpose,
    string Code);

public sealed record EmailCodeChallengeResult(bool Accepted, DateTime ExpiresAtUtc, int ResendAfterSeconds);
public sealed record FirebaseCustomTokenResult(string CustomToken, DateTime ExpiresAtUtc);

/// <summary>Delivers a code without exposing it to application logs or API responses.</summary>
public interface IEmailCodeDelivery
{
    bool Enabled { get; }
    Task SendAsync(string destination, string code, EmailCodePurpose purpose, CancellationToken ct);
}

/// <summary>Issues a short-lived Firebase custom token only from a trusted server adapter.</summary>
public interface IFirebaseCustomTokenIssuer
{
    bool Enabled { get; }
    Task<FirebaseCustomTokenResult> IssueAsync(Guid userId, string projectId, CancellationToken ct);
}

/// <summary>Browser/PWA implementations use passkeys or an authenticated session; PIN alone is never an Internet credential.</summary>
public interface IAuthorizedDeviceCredential
{
    bool SupportsWeb { get; }
    Task<bool> VerifyAsync(Guid userId, string credential, CancellationToken ct);
}
