using Weymela.Api.Auth;
using Weymela.Application;

namespace Weymela.Api.Endpoints;

internal static class EndpointSupport
{
    public static Actor Actor(HttpContext context)=>WorkspaceAuthentication.Actor(context.User);
    public static string Key(HttpContext context)
    {
        var value=context.Request.Headers["Idempotency-Key"].ToString();
        if(string.IsNullOrWhiteSpace(value)||value.Length>200)throw new ApplicationFailure(FailureKind.Validation,"A request reference is required. Please retry from the workspace.");
        return value;
    }
    public static IResult Id(Guid id)=>Results.Ok(new{id});
}
