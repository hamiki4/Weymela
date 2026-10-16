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
    public static Weymela.Application.Web.AdminListFilter ListFilter(HttpContext context)
    {
        var values = context.Request.Query;
        int Number(string name, int fallback) => !values.ContainsKey(name) ? fallback
            : int.TryParse(values[name], out var number) ? number
            : throw new ApplicationFailure(FailureKind.Validation, "Choose a valid page.");
        DateTime? Date(string name) => !values.ContainsKey(name) || string.IsNullOrEmpty(values[name]) ? null
            : DateOnly.TryParseExact(values[name], "yyyy-MM-dd", out var date)
                ? DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
                : throw new ApplicationFailure(FailureKind.Validation, "Choose a valid date.");
        var filter = new Weymela.Application.Web.AdminListFilter(values["search"], values["status"], values["type"],
            Date("from"), Date("to"), Number("page", 1), Number("pageSize", 50));
        filter.Validate();
        return filter;
    }
    public static IResult Id(Guid id)=>Results.Ok(new{id});
}
