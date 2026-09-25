using Weymela.Api.Auth;
using Weymela.Application;

namespace Weymela.Api.Endpoints;

internal static class EndpointSupport
{
    public static AuthorityContext Authority(HttpContext context)=>WorkspaceAuthentication.Authority(context);
    public static RealActor RealActor(HttpContext context)=>Authority(context).RealActor;
    public static Actor Actor(HttpContext context)=>Authority(context).CommandActor;
    public static Actor CommandActor(HttpContext context)=>Authority(context).CommandActor;
    public static Guid Correlation(HttpContext context)
        => Guid.TryParse(context.Request.Headers["X-Correlation-ID"], out var id) && id != Guid.Empty ? id : Guid.NewGuid();
    public static string Key(HttpContext context)
    {
        var value=context.Request.Headers["Idempotency-Key"].ToString();
        if(string.IsNullOrWhiteSpace(value)||value.Length>200)throw new ApplicationFailure(FailureKind.Validation,"A request reference is required. Please retry from the workspace.");
        return value;
    }
    public static IResult Id(Guid id)=>Results.Ok(new{id});
}
