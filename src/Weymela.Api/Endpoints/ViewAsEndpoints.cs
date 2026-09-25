using Weymela.Api.Auth;
using Weymela.Api.Security;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;

namespace Weymela.Api.Endpoints;

internal static class ViewAsEndpoints
{
    public static void MapViewAsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/view-as")
            .RequireAuthorization("VerifiedAccount")
            .AddEndpointFilter<ValidatedInputFilter>();

        group.MapPost("/start", async (ViewAsStartInput input, HttpContext context,
            ViewAsService service, RuntimeOptions options, CancellationToken ct) =>
        {
            var result = await service.StartAsync(EndpointSupport.Authority(context), input.ViewedUserId,
                input.Reason, EndpointSupport.Key(context), EndpointSupport.Correlation(context), ct);
            context.Response.Cookies.Append(SupportSessionCookie.Name(options.DevelopmentIdentity),
                result.CookieValue, SupportSessionCookie.Options(options.DevelopmentIdentity, result.Session.ExpiresAtUtc));
            return Results.Ok(result.Session);
        });

        group.MapGet("/current", async (HttpContext context, ViewAsService service, RuntimeOptions options,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.CurrentAsync(EndpointSupport.RealActor(context),
                    context.Request.Cookies[SupportSessionCookie.Name(options.DevelopmentIdentity)], ct));
            }
            catch (ApplicationFailure failure) when (failure.Code == "InvalidViewAsSession")
            {
                context.Response.Cookies.Delete(SupportSessionCookie.Name(options.DevelopmentIdentity),
                    SupportSessionCookie.DeleteOptions(options.DevelopmentIdentity));
                return Results.Json(new { code = failure.Code, message = failure.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
        });

        group.MapPost("/end", async (HttpContext context, ViewAsService service, RuntimeOptions options,
            CancellationToken ct) =>
        {
            await service.EndAsync(EndpointSupport.RealActor(context),
                context.Request.Cookies[SupportSessionCookie.Name(options.DevelopmentIdentity)],
                EndpointSupport.Correlation(context), ct);
            context.Response.Cookies.Delete(SupportSessionCookie.Name(options.DevelopmentIdentity),
                SupportSessionCookie.DeleteOptions(options.DevelopmentIdentity));
            return Results.NoContent();
        });
    }
}
