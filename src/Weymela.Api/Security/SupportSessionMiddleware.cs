using Microsoft.AspNetCore.Authorization;
using Weymela.Api.Auth;
using Weymela.Api.Endpoints;
using Weymela.Application;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;

namespace Weymela.Api.Security;

/// <summary>
/// Resolves the opaque support cookie after authentication and before policy
/// authorization. It never changes ClaimsPrincipal; it only attaches a
/// server-validated AuthorityContext to the current request.
/// </summary>
internal sealed class SupportSessionMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> ControlRoutes = new(StringComparer.Ordinal)
    {
        "/api/admin/view-as/start",
        "/api/admin/view-as/current",
        "/api/admin/view-as/end"
    };

    public async Task InvokeAsync(HttpContext context, ViewAsService service, RuntimeOptions options)
    {
        var cookie = context.Request.Cookies[SupportSessionCookie.Name(options.DevelopmentIdentity)];
        if (context.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(cookie)
            || !context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        // View As control routes must remain usable with an ended or expired
        // cookie so the server can reject/reconcile it and clear the browser
        // state. All actual workspace requests still resolve and enforce the
        // active support session below.
        if (ControlRoutes.Contains(EndpointSecurity.Operation(context)))
        {
            await next(context);
            return;
        }

        var real = WorkspaceAuthentication.Authority(context.User).RealActor;
        var authority = await service.ResolveActiveAsync(real, cookie, context.RequestAborted);
        context.Items[WorkspaceAuthentication.AuthorityContextItem] = authority;

        var route = EndpointSecurity.Operation(context);
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await next(context);
            return;
        }

        if (!HttpMethods.IsGet(context.Request.Method) || !AllowedRead(context, authority.EffectiveSubject.Role))
        {
            await service.RecordBlockedAsync(authority, route, EndpointSupport.Correlation(context), context.RequestAborted);
            throw new ApplicationFailure(FailureKind.Forbidden,
                "View As is read-only and this workspace action is not available.", code: "ViewAsReadOnly");
        }

        await next(context);
    }

    private static bool AllowedRead(HttpContext context, ActorRole role)
    {
        var path = context.Request.Path.Value?.TrimEnd('/') ?? "";
        if (path is "/api/session" or "/api/notifications" or "/api/legal/current") return true;
        return role switch
        {
            ActorRole.Business => path.StartsWith("/api/business/", StringComparison.Ordinal)
                || path == "/api/business",
            ActorRole.Creator => path.StartsWith("/api/creator/", StringComparison.Ordinal)
                || path == "/api/creator",
            ActorRole.Customer => path.StartsWith("/api/customer/", StringComparison.Ordinal)
                || path == "/api/customer",
            ActorRole.OperationsAdmin => OperationsRead(path),
            _ => false
        };
    }

    private static bool OperationsRead(string path)
    {
        if (path is "/api/admin/operations" or "/api/admin/operations/home"
            or "/api/admin/businesses" or "/api/admin/creators" or "/api/admin/customers"
            or "/api/admin/campaigns" or "/api/admin/payouts" or "/api/admin/notifications"
            or "/api/admin/ugc" or "/api/admin/role-enrollments" or "/api/admin/deposit-requests") return true;
        return path.StartsWith("/api/admin/campaigns/", StringComparison.Ordinal);
    }
}
