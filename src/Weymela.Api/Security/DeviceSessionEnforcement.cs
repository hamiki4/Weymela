using Microsoft.AspNetCore.Authorization;
using Weymela.Api.Auth;
using Weymela.Api.Endpoints;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;

namespace Weymela.Api.Security;

internal static class DeviceSessionRequestPolicy
{
    public const string ActivityHeader = "X-Weymela-Activity";

    private static readonly HashSet<string> BootstrapRoutes = new(StringComparer.Ordinal)
    {
        "/api/session",
        "/api/device/enrollment"
    };

    private static readonly HashSet<string> LockedRoutes = new(StringComparer.Ordinal)
    {
        "/api/device/access",
        "/api/device/unlock",
        "/api/session/sign-out"
    };

    // Audited user-action routes. A client hint is necessary but is never
    // sufficient: the matched endpoint method and route must also be listed.
    private static readonly HashSet<string> MeaningfulPosts = new(StringComparer.Ordinal)
    {
        "/api/session/switch-profile",
        "/api/onboarding/profile",
        "/api/admin/role-enrollments/{id:guid}/review",
        "/api/notifications/{id:guid}/read",
        "/api/notifications/read-all",
        "/api/legal/{id:guid}/accept",
        "/api/business/wallet/deposits",
        "/api/business/campaigns",
        "/api/business/campaigns/{id:guid}/fund",
        "/api/business/campaigns/{id:guid}/publish",
        "/api/business/campaigns/{id:guid}/start",
        "/api/business/applicants/{id:guid}/approve",
        "/api/business/applicants/{id:guid}/reject",
        "/api/business/creator-budgets/{id:guid}/increase",
        "/api/business/deposit-requests",
        "/api/creator/campaigns/{id:guid}/join",
        "/api/creator/creator-budgets/{id:guid}/content",
        "/api/creator/participations/{id:guid}/refresh",
        "/api/creator/payouts/request",
        "/api/customer/offers/{id:guid}/qr",
        "/api/checkout/resolve",
        "/api/checkout/confirm",
        "/api/checkout/manual-lookup",
        "/api/admin/financial-settings",
        "/api/admin/payouts/{kind}/{subject:guid}/prepare",
        "/api/admin/payouts/{id:guid}/paid",
        "/api/admin/platform/settlements",
        "/api/admin/deposit-requests/{id:guid}/review"
    };

    public static string Route(HttpContext context) =>
        ((context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? string.Empty).TrimEnd('/');

    public static bool IsLockedRoute(HttpContext context) => LockedRoutes.Contains(Route(context));
    public static bool IsBootstrapRoute(HttpContext context) => BootstrapRoutes.Contains(Route(context));
    public static bool IsMeaningfulActivity(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && context.Request.Headers[ActivityHeader] == "1"
        && MeaningfulPosts.Contains(Route(context));
}

/// <summary>
/// Additional enforcement after normal authentication and authorization. It never
/// builds a principal and never substitutes for Workspace authorization.
/// </summary>
internal sealed class DeviceSessionEnforcementMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, DeviceAccessService access, RuntimeOptions options)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || context.User.Identity?.IsAuthenticated != true
            || !context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        // Explicit synthetic DevelopmentDirectory personas have no trusted binding.
        // Real BrowserHost Firebase identities have the full verified claims and are enforced.
        if (!DeviceSessionPrincipal.TryGet(context.User, out var identity))
        {
            if (options.DevelopmentIdentity && context.User.FindFirst("identity-binding") is null)
            {
                await next(context);
                return;
            }
            await WriteAsync(context, 401, "FullAuthenticationRequired",
                "Complete account authentication to continue.");
            return;
        }

        if (DeviceSessionRequestPolicy.IsLockedRoute(context))
        {
            await next(context);
            return;
        }

        DeviceAccessStatus status;
        try
        {
            status = await access.EnforceAsync(identity,
                context.Request.Cookies[DeviceCredentialCookie.Name],
                context.Request.Cookies[DeviceSessionCredentialCookie.Name(options.DevelopmentIdentity)],
                DeviceSessionRequestPolicy.IsMeaningfulActivity(context),
                context.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteAsync(context, 503, "DeviceAccessUnavailable",
                "Secure device access is temporarily unavailable.");
            return;
        }

        if (status.State == DeviceAccessStates.Unlocked
            || status.State == DeviceAccessStates.EnrollmentRequired
                && DeviceSessionRequestPolicy.IsBootstrapRoute(context))
        {
            await next(context);
            return;
        }

        await WriteStatusAsync(context, status);
    }

    public static Task WriteStatusAsync(HttpContext context, DeviceAccessStatus status) => status.State switch
    {
        DeviceAccessStates.Locked => WriteAsync(context, 423, "SessionLocked",
            "Weymela is locked. Enter your PIN to continue.", status),
        DeviceAccessStates.Cooldown => WriteAsync(context, 429, "PinCooldown",
            "Too many PIN attempts. Wait before trying again.", status),
        DeviceAccessStates.RecoveryRequired => WriteAsync(context, 423, "PinRecoveryRequired",
            "PIN recovery is required before this device can unlock Weymela.", status),
        DeviceAccessStates.EnrollmentRequired => WriteAsync(context, 428, "DeviceEnrollmentRequired",
            "Set up this device before opening a workspace.", status),
        _ => WriteAsync(context, 401, "FullAuthenticationRequired",
            "Complete account authentication to continue.", status)
    };

    private static async Task WriteAsync(HttpContext context, int statusCode, string code,
        string message, DeviceAccessStatus? status = null)
    {
        context.Response.StatusCode = statusCode;
        if (status?.RetryAfterSeconds is int retry)
            context.Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(new
        {
            code,
            message,
            state = status?.State,
            idleExpiresAtUtc = status?.IdleExpiresAtUtc,
            sessionExpiresAtUtc = status?.SessionExpiresAtUtc,
            retryAfterSeconds = status?.RetryAfterSeconds
        });
    }
}
