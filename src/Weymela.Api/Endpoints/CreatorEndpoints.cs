using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Web;

namespace Weymela.Api.Endpoints;

internal static class CreatorEndpoints
{
    private sealed record SocialProfileLinkInput(string ProfileUrl, long Audience = 0);
    private static CreatorPlatform Platform(string value)
        => Enum.TryParse<CreatorPlatform>(value, true, out var platform) && Enum.IsDefined(platform)
            ? platform : throw new Weymela.Application.ApplicationFailure(Weymela.Application.FailureKind.Validation, "Choose a supported social platform.");
    public static void MapCreatorEndpoints(this WebApplication app)
    {
        var g=app.MapGroup("/api/creator").RequireAuthorization("Creator").AddEndpointFilter<Weymela.Api.Security.ValidatedInputFilter>();
        g.MapGet("/home",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.CreatorHomeAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/social-accounts",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.CreatorSocialAccountsAsync(EndpointSupport.Actor(c),ct));
        g.MapPost("/social-profiles/{platform}",async(string platform,SocialProfileLinkInput input,HttpContext c,CreatorSocialProfileLinks links,CancellationToken ct)=>
            Results.Ok(new { id = await links.SaveAsync(EndpointSupport.Actor(c),Platform(platform),input.ProfileUrl,input.Audience,ct) }));
        g.MapDelete("/social-profiles/{platform}",async(string platform,HttpContext c,CreatorSocialProfileLinks links,CancellationToken ct)=>
        { await links.RemoveAsync(EndpointSupport.Actor(c),Platform(platform),ct);return Results.NoContent(); });
        g.MapGet("/requests",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.CreatorRequestsAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/pricing",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.CreatorPricingAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/discover",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.DiscoverAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/promotions/discover",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.DiscoverAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/discover/{id:guid}",(Guid id,HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.OpportunityAsync(EndpointSupport.Actor(c),id,ct));
        g.MapGet("/campaigns",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.CreatorCampaignsAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/earnings",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.EarningsAsync(EndpointSupport.Actor(c),ct));
        g.MapPost("/campaigns/{id:guid}/join",async(Guid id,JoinInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.JoinAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/promotions/{id:guid}/request",async(Guid id,JoinInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.JoinAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/creator-budgets/{id:guid}/content/review",async(Guid id,HttpContext c,
            PrivateReviewMediaStore media,CreatorPromotionContentService service,Weymela.Infrastructure.Persistence.WeymelaDbContext db,CancellationToken ct)=>
        {
            if(!c.Request.HasFormContentType) throw new Weymela.Application.ApplicationFailure(Weymela.Application.FailureKind.Validation,"Choose an MP4 review video.");
            var form=await c.Request.ReadFormAsync(ct);
            if(form.Count!=0||form.Files.Count!=1||form.Files[0].Name!="media")
                throw new Weymela.Application.ApplicationFailure(Weymela.Application.FailureKind.Validation,"Choose one MP4 review video.");
            var saved=await media.SaveAsync(form.Files[0],true,ct);
            try
            {
                var result=await service.SubmitPrivateAsync(EndpointSupport.Actor(c),id,
                    new(saved.StorageKey,saved.ContentType,saved.Length,saved.Sha256,saved.OriginalFileName),EndpointSupport.Key(c),ct);
                await media.PruneOrphansAsync(db,ct);
                return Results.Ok(result);
            }
            catch { media.Delete(saved.StorageKey); throw; }
        });
        g.MapPost("/creator-budgets/{id:guid}/publication",async(Guid id,PublicationInput input,HttpContext c,CreatorPublicationService service,CancellationToken ct)=>
            Results.Ok(await service.RequestPromotionAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/creator-budgets/{id:guid}/go-live",async(Guid id,HttpContext c,CreatorPublicationService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.GoLivePromotionAsync(EndpointSupport.Actor(c),id,EndpointSupport.Key(c),ct)));
        g.MapPost("/participations/{id:guid}/refresh",(Guid id,HttpContext c,VerifiedViewService service,CancellationToken ct)=>
            service.RefreshAsync(new(EndpointSupport.Actor(c),id,EndpointSupport.Key(c)),ct));
        g.MapPost("/payouts/request",async(HttpContext c,PayoutService service,CancellationToken ct)=>
        {var a=EndpointSupport.Actor(c);return EndpointSupport.Id(await service.PrepareAsync(a,PayoutBeneficiary.Creator,a.CreatorId!.Value,EndpointSupport.Key(c),ct));});
        g.MapGet("/ugc",(HttpContext c,UgcService service,CancellationToken ct)=>service.DiscoverAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/ugc/requests",(HttpContext c,UgcService service,CancellationToken ct)=>service.CreatorRequestsAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/ugc/assignments",(HttpContext c,UgcService service,CancellationToken ct)=>service.CreatorAssignmentsAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/ugc/{id:guid}",(Guid id,HttpContext c,UgcService service,CancellationToken ct)=>service.DetailAsync(EndpointSupport.Actor(c),id,ct));
        g.MapPost("/ugc/{id:guid}/request",async(Guid id,string? selectedPlatform,Guid? verifiedSocialProfileId,HttpContext c,UgcService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.RequestAsync(EndpointSupport.Actor(c),id,EndpointSupport.Key(c),ct,selectedPlatform,verifiedSocialProfileId)));
        g.MapPost("/ugc/assignments/{id:guid}/submit-review",async(Guid id,HttpContext c,
            PrivateReviewMediaStore media,UgcService service,Weymela.Infrastructure.Persistence.WeymelaDbContext db,CancellationToken ct)=>
        {
            if(!c.Request.HasFormContentType) throw new Weymela.Application.ApplicationFailure(Weymela.Application.FailureKind.Validation,"Choose a private review file.");
            var form=await c.Request.ReadFormAsync(ct);
            if(form.Count!=0||form.Files.Count!=1||form.Files[0].Name!="media")
                throw new Weymela.Application.ApplicationFailure(Weymela.Application.FailureKind.Validation,"Choose one private review file.");
            var saved=await media.SaveAsync(form.Files[0],false,ct);
            try
            {
                var result=await service.SubmitPrivateAsync(EndpointSupport.Actor(c),id,
                    new(saved.StorageKey,saved.ContentType,saved.Length,saved.Sha256,saved.OriginalFileName),EndpointSupport.Key(c),ct);
                await media.PruneOrphansAsync(db,ct);
                return EndpointSupport.Id(result);
            }
            catch { media.Delete(saved.StorageKey); throw; }
        });
        g.MapPost("/ugc/assignments/{id:guid}/publication",async(Guid id,PublicationInput input,HttpContext c,CreatorPublicationService service,CancellationToken ct)=>
            Results.Ok(await service.RequestUgcAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/ugc/assignments/{id:guid}/go-live",async(Guid id,HttpContext c,CreatorPublicationService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.GoLiveUgcAsync(EndpointSupport.Actor(c),id,EndpointSupport.Key(c),ct)));
        g.MapPost("/ugc/assignments/{id:guid}/accept-revision",async(Guid id,VersionInput input,HttpContext c,UgcService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.AcceptRevisionAsync(EndpointSupport.Actor(c),id,checked((int)input.Version),EndpointSupport.Key(c),ct)));
    }
}
