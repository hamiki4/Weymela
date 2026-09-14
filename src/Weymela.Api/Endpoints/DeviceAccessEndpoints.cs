using Weymela.Api.Auth;
using Weymela.Api.Security;
using Weymela.Application;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;

namespace Weymela.Api.Endpoints;

internal static class DeviceAccessEndpoints
{
    private sealed record PinUnlockInput(string Pin);

    public static void MapDeviceAccessEndpoints(this WebApplication app, bool development)
    {
        var group = app.MapGroup("/api/device")
            .RequireAuthorization("VerifiedAccount")
            .AddEndpointFilter<ValidatedInputFilter>();

        group.MapGet("/access", async (HttpContext context, DeviceAccessService service,
            RuntimeOptions options, CancellationToken ct) =>
        {
            if (!DeviceSessionPrincipal.TryGet(context.User, out var identity))
            {
                if (options.DevelopmentIdentity && context.User.FindFirst("identity-binding") is null)
                    return Results.Ok(new DeviceAccessStatus(DeviceAccessStates.Unlocked, null, null, null));
                return Results.Json(new { code = "FullAuthenticationRequired", message = "Complete account authentication to continue." }, statusCode: 401);
            }
            DeviceAccessStatus status;
            try
            {
                status = await service.InspectAsync(identity,
                    context.Request.Cookies[DeviceCredentialCookie.Name],
                    context.Request.Cookies[DeviceSessionCredentialCookie.Name(development)], ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Results.Json(new { code = "DeviceAccessUnavailable", message = "Secure device access is temporarily unavailable." }, statusCode: 503);
            }
            return Results.Ok(status);
        });

        group.MapPost("/unlock", async (PinUnlockInput input, HttpContext context,
            DeviceAccessService service, CancellationToken ct) =>
        {
            if (!DeviceSessionPrincipal.TryGet(context.User, out var identity))
                return Results.Json(new { code = "FullAuthenticationRequired", message = "Complete account authentication to continue." }, statusCode: 401);
            DeviceUnlockResult result;
            try
            {
                result = await service.UnlockAsync(identity,
                    context.Request.Cookies[DeviceCredentialCookie.Name],
                    context.Request.Cookies[DeviceSessionCredentialCookie.Name(development)],
                    input.Pin, EndpointSupport.Key(context), ct);
            }
            catch (DeviceAccessUnavailableException)
            {
                return Results.Json(new { code = "DeviceAccessUnavailable", message = "Secure device access is temporarily unavailable." }, statusCode: 503);
            }
            if (!result.Succeeded)
            {
                if (result.Status.State == DeviceAccessStates.Locked)
                    return Results.Json(new { code = "InvalidPin", message = "The PIN is incorrect.", state = result.Status.State }, statusCode: 401);
                return await StatusResultAsync(context, result.Status);
            }
            context.Response.Cookies.Append(DeviceSessionCredentialCookie.Name(development),
                result.Credential!.Value, DeviceSessionCredentialCookie.Options(development,
                    result.Session!));
            return Results.Ok(result.Status);
        });
    }

    private static async Task<IResult> StatusResultAsync(HttpContext context, DeviceAccessStatus status)
    {
        await DeviceSessionEnforcementMiddleware.WriteStatusAsync(context, status);
        return Results.Empty;
    }
}
