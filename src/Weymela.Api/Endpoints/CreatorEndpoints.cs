using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Web;

namespace Weymela.Api.Endpoints;

internal static class CreatorEndpoints
{
    public static void MapCreatorEndpoints(this WebApplication app)
    {
        var g=app.MapGroup("/api/creator").RequireAuthorization("Creator").AddEndpointFilter<Weymela.Api.Security.ValidatedInputFilter>();
        g.MapGet("/home",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.CreatorHomeAsync(EndpointSupport.Actor(c),ct));
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
        g.MapPost("/creator-budgets/{id:guid}/content",async(Guid id,ContentInput input,HttpContext c,CreatorPromotionContentService service,CancellationToken ct)=>
        {
            Weymela.Infrastructure.Operations.InputRules.Reference(input.ExternalContentId,"video reference",100);
            if(input.Provider is not ("TikTok" or "YouTube" or "Instagram")||string.IsNullOrWhiteSpace(input.ExternalContentId)||input.ExternalContentId.Length>100)
                return Results.BadRequest(new{message="Choose a supported platform and valid video reference."});
            return Results.Ok(await service.SubmitAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct));
        });
        g.MapPost("/creator-budgets/{id:guid}/go-live",async(Guid id,HttpContext c,VerifiedViewService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.GoLiveAsync(EndpointSupport.Actor(c),id,EndpointSupport.Key(c),ct:ct)));
        g.MapPost("/participations/{id:guid}/refresh",(Guid id,HttpContext c,VerifiedViewService service,CancellationToken ct)=>
            service.RefreshAsync(new(EndpointSupport.Actor(c),id,EndpointSupport.Key(c)),ct));
        g.MapPost("/payouts/request",async(HttpContext c,PayoutService service,CancellationToken ct)=>
        {var a=EndpointSupport.Actor(c);return EndpointSupport.Id(await service.PrepareAsync(a,PayoutBeneficiary.Creator,a.CreatorId!.Value,EndpointSupport.Key(c),ct));});
        g.MapGet("/ugc",(HttpContext c,UgcService service,CancellationToken ct)=>service.DiscoverAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/ugc/requests",(HttpContext c,UgcService service,CancellationToken ct)=>service.CreatorRequestsAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/ugc/assignments",(HttpContext c,UgcService service,CancellationToken ct)=>service.CreatorAssignmentsAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/ugc/{id:guid}",(Guid id,HttpContext c,UgcService service,CancellationToken ct)=>service.DetailAsync(EndpointSupport.Actor(c),id,ct));
        g.MapPost("/ugc/{id:guid}/request",async(Guid id,HttpContext c,UgcService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.RequestAsync(EndpointSupport.Actor(c),id,EndpointSupport.Key(c),ct)));
        g.MapPost("/ugc/assignments/{id:guid}/submit",async(Guid id,UgcSubmissionInput input,HttpContext c,UgcService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.SubmitAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/ugc/assignments/{id:guid}/accept-revision",async(Guid id,VersionInput input,HttpContext c,UgcService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.AcceptRevisionAsync(EndpointSupport.Actor(c),id,checked((int)input.Version),EndpointSupport.Key(c),ct)));
    }
}
