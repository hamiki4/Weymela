namespace Weymela.Api.Auth;

/// <summary>HttpOnly transport for the server-controlled View As session id.</summary>
public static class SupportSessionCookie
{
    public const string DevelopmentName = "WeymelaV3.SupportSession";
    public const string ProductionName = "__Host-WeymelaV3.SupportSession";

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
