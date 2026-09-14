using Weymela.Infrastructure.Identity;

namespace Weymela.Api.Auth;

/// <summary>Prepared Phase 9A.3 cookie policy. No endpoint issues this cookie yet.</summary>
public static class DeviceSessionCredentialCookie
{
    public const string DevelopmentName = "WeymelaV3.DeviceSession";
    public const string ProductionName = "__Host-WeymelaV3.DeviceSession";

    public static string Name(bool development) => development ? DevelopmentName : ProductionName;

    public static CookieOptions Options(bool development, DeviceSessionSnapshot session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.CreatedAtUtc.Kind != DateTimeKind.Utc || session.ExpiresAtUtc.Kind != DateTimeKind.Utc
            || session.ExpiresAtUtc != DeviceAccessPolicy.SessionExpiresAt(session.CreatedAtUtc))
            throw new ArgumentException("Device-session cookie expiry must match the fixed UTC session boundary.", nameof(session));
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = !development,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = new DateTimeOffset(session.ExpiresAtUtc),
            IsEssential = true
        };
    }
}
