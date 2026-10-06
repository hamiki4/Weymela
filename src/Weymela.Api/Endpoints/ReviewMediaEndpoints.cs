using Weymela.Api.Security;

namespace Weymela.Api.Endpoints;

internal static class ReviewMediaEndpoints
{
    public static void MapReviewMediaEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/review-media").RequireAuthorization("Workspace")
            .AddEndpointFilter<ValidatedInputFilter>();
        group.MapGet("/promotions/{submissionId:guid}", async (Guid submissionId, HttpContext c,
            ReviewMediaAccessService service, CancellationToken ct) =>
            Media(c, await service.OpenPromotionAsync(EndpointSupport.Actor(c), submissionId, ct)));
        group.MapGet("/ugc/{submissionId:guid}", async (Guid submissionId, HttpContext c,
            ReviewMediaAccessService service, CancellationToken ct) =>
            Media(c, await service.OpenUgcAsync(EndpointSupport.Actor(c), submissionId, ct)));
    }

    private static IResult Media(HttpContext context, (FileStream Stream, string ContentType) media)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.ContentDisposition = "inline";
        return Results.Stream(media.Stream, media.ContentType, enableRangeProcessing: true);
    }
}
