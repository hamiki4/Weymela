using System.Security.Claims;
using Weymela.Api.Auth;
using Weymela.Api.Security;
using Weymela.Application;
using Weymela.Infrastructure.Identity;

namespace Weymela.Api.Endpoints;

internal static class DeviceEnrollmentEndpoints
{
    private sealed record InitialPinEnrollment(string Pin, string ConfirmPin);

    public static void MapDeviceEnrollmentEndpoints(this WebApplication app, bool development)
    {
        var group = app.MapGroup("/api/device/enrollment")
            .RequireAuthorization("VerifiedAccount")
            .AddEndpointFilter<ValidatedInputFilter>();

        group.MapGet("", async (HttpContext context, DeviceEnrollmentService service, CancellationToken ct) =>
        {
            // Explicit synthetic personas remain fixtures, not PIN-enrolled accounts.
            if (development && context.User.FindFirst("identity-binding") is null)
                return Results.Ok(new DeviceEnrollmentStatus(DeviceEnrollmentStates.NotRequired, null));
            var result = await service.StatusAsync(VerifiedUserId(context),
                context.Request.Cookies[DeviceCredentialCookie.Name], ct);
            return Results.Ok(result);
        });

        group.MapPost("", async (InitialPinEnrollment input, HttpContext context,
            DeviceEnrollmentService service, DeviceSessionService deviceSessions, CancellationToken ct) =>
        {
            var identity = VerifiedIdentity(context);
            var result = await service.EnrollAsync(identity.UserId, input.Pin, input.ConfirmPin,
                context.Request.Cookies[DeviceCredentialCookie.Name], EndpointSupport.Key(context), ct);
            if (result.Credential is not null)
            {
                var deviceSession = await deviceSessions.EstablishForRecognizedDeviceAsync(
                    identity, result.Credential.Value, ct)
                    ?? throw new ApplicationFailure(FailureKind.Forbidden,
                        "Secure device setup could not establish the device session.");
                context.Response.Cookies.Append(DeviceCredentialCookie.Name, result.Credential.Value,
                    DeviceCredentialCookie.Options(development, result.Status.ExpiresAtUtc!.Value));
                context.Response.Cookies.Append(DeviceSessionCredentialCookie.Name(development),
                    deviceSession.Credential.Value, DeviceSessionCredentialCookie.Options(development, deviceSession.Session));
            }
            return Results.Ok(result.Status);
        });
    }

    private static Guid VerifiedUserId(HttpContext context)
        => VerifiedIdentity(context).UserId;

    private static DeviceSessionIdentity VerifiedIdentity(HttpContext context)
    {
        if (context.User.FindFirst("auth-strength")?.Value != "firebase-verified"
            || !Guid.TryParse(context.User.FindFirst("identity-binding")?.Value, out var bindingId)
            || !long.TryParse(context.User.FindFirst("identity-version")?.Value,
                System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var bindingVersion)
            || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || userId == Guid.Empty)
            throw new ApplicationFailure(FailureKind.Forbidden,
                "Complete verified account sign-in before setting up this device.");
        return new DeviceSessionIdentity(userId, bindingId, bindingVersion);
    }
}

public static class DeviceCredentialCookie
{
    public const string Name = "WeymelaV3.Device";

    public static CookieOptions Options(bool development, DateTime expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = !development,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)),
        MaxAge = DeviceAccessPolicy.AuthorizedDeviceLifetime,
        IsEssential = true
    };
}
