namespace Weymela.Api.Auth;

/// <summary>
/// Carries only the opaque, single-use password-recovery transaction secret.
/// The account and purpose remain bound to the server-side challenge record.
/// </summary>
public static class PasswordRecoveryTransactionCookie
{
    public const string DevelopmentName = "WeymelaV3.PasswordRecovery";
    public const string ProductionName = "__Host-WeymelaV3.PasswordRecovery";

    public static string Name(bool development) => development ? DevelopmentName : ProductionName;

    public static CookieOptions Options(bool development, DateTime expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = !development,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)),
        IsEssential = true
    };

    public static CookieOptions DeleteOptions(bool development) => new()
    {
        HttpOnly = true,
        Secure = !development,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true
    };
}
