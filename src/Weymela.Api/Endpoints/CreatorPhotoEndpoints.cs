using Weymela.Application;
using Weymela.Api.Security;

namespace Weymela.Api.Endpoints;

internal static class CreatorPhotoEndpoints
{
    public static void MapCreatorPhotoEndpoints(this WebApplication app)
    {
        var creator = app.MapGroup("/api/creator/photo").RequireAuthorization("Creator").AddEndpointFilter<ValidatedInputFilter>();
        creator.MapGet("", async (HttpContext c, CreatorPhotoService service, CancellationToken ct) =>
            Photo(c, await service.ReadOwnAsync(EndpointSupport.Actor(c), ct)));
        creator.MapPost("", async (HttpContext c, CreatorPhotoService service, CancellationToken ct) =>
        {
            if (!c.Request.HasFormContentType) throw new ApplicationFailure(FailureKind.Validation, "Choose a JPEG or PNG photo.");
            var form = await c.Request.ReadFormAsync(ct);
            if (form.Count != 0 || form.Files.Count != 1 || form.Files[0].Name != "photo")
                throw new ApplicationFailure(FailureKind.Validation, "Choose one JPEG or PNG photo.");
            await service.ReplaceAsync(EndpointSupport.Actor(c), form.Files[0], ct);
            return Results.Ok(new { hasCreatorPhoto = true });
        });
        creator.MapDelete("", async (HttpContext c, CreatorPhotoService service, CancellationToken ct) =>
        { await service.RemoveAsync(EndpointSupport.Actor(c), ct); return Results.NoContent(); });

        app.MapGet("/api/business/creator-photos/{creatorId:guid}", async (Guid creatorId, HttpContext c, CreatorPhotoService service, CancellationToken ct) =>
            Photo(c, await service.ReadForBusinessAsync(EndpointSupport.Actor(c), creatorId, ct)))
            .RequireAuthorization("Business");
    }

    private static IResult Photo(HttpContext context, (byte[] Bytes, string ContentType) photo)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        return Results.File(photo.Bytes, photo.ContentType);
    }
}
