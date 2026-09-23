using Weymela.Application.Web;
using Weymela.Infrastructure.Web;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Identity;

namespace Weymela.Api.Endpoints;

internal static class BusinessEndpoints
{
    public static void MapBusinessEndpoints(this WebApplication app,bool development)
    {
        var g=app.MapGroup("/api/business").RequireAuthorization("Business").AddEndpointFilter<Weymela.Api.Security.ValidatedInputFilter>();
        g.MapGet("/home",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.BusinessHomeAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/wallet",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.WalletAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/pricing",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.BusinessPricingAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/ugc-pricing",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.UgcPricingAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/cashiers",(HttpContext c,CashierService service,CancellationToken ct)=>service.ListAsync(EndpointSupport.Actor(c),ct));
        g.MapPost("/cashiers",async(CreateCashierInput input,HttpContext c,CashierService service,CancellationToken ct)=>
            Results.Ok(await service.CreateAsync(EndpointSupport.Actor(c),input,EndpointSupport.Key(c),ct)));
        g.MapPost("/cashiers/{id:guid}/activation-code",async(Guid id,HttpContext c,CashierService service,CancellationToken ct)=>
            Results.Ok(await service.RegenerateActivationCodeAsync(EndpointSupport.Actor(c),id,EndpointSupport.Key(c),ct)));
        g.MapPost("/cashiers/{id:guid}/{state}",async(Guid id,string state,HttpContext c,CashierService service,CancellationToken ct)=>
            Results.Ok(await service.SetStateAsync(EndpointSupport.Actor(c),id,state,EndpointSupport.Key(c),ct)));
        g.MapGet("/campaigns",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.BusinessCampaignsAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/campaigns/{id:guid}",(Guid id,HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.BusinessCampaignAsync(EndpointSupport.Actor(c),id,ct));
        g.MapGet("/promotions",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.BusinessCampaignsAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/promotions/{id:guid}",(Guid id,HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.BusinessCampaignAsync(EndpointSupport.Actor(c),id,ct));
        g.MapGet("/promotion-content-submissions",(HttpContext c,CreatorPromotionContentService service,CancellationToken ct)=>
            service.BusinessSubmissionsAsync(EndpointSupport.Actor(c),ct));
        g.MapPost("/promotion-content-submissions/{id:guid}/review",async(Guid id,PromotionContentReviewInput input,HttpContext c,CreatorPromotionContentService service,CancellationToken ct)=>
            Results.Ok(await service.ReviewAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/wallet/deposits",async(DepositInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
        {
            // No pretend bank confirmation in a non-development environment. A trusted deposit adapter is required later.
            if(!development)return Results.Problem(statusCode:503,title:"Deposit confirmation is not connected.");
            return EndpointSupport.Id(await commands.DepositAsync(EndpointSupport.Actor(c),input,EndpointSupport.Key(c),ct));
        });
        g.MapPost("/campaigns",async(CreateCampaignInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.CreateAsync(EndpointSupport.Actor(c),input,EndpointSupport.Key(c),ct)));
        g.MapPost("/promotions",async(CreateCampaignInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.CreateAsync(EndpointSupport.Actor(c),input,EndpointSupport.Key(c),ct)));
        g.MapPost("/campaigns/{id:guid}/fund",async(Guid id,FundingInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.FundAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/campaigns/{id:guid}/publish",async(Guid id,VersionInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.PublishAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),false,ct)));
        g.MapPost("/promotions/{id:guid}/fund",async(Guid id,FundingInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.FundAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/promotions/{id:guid}/publish",async(Guid id,VersionInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.PublishAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),false,ct)));
        g.MapPost("/promotions/{id:guid}/update",async(Guid id,PromotionPresentationInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.UpdatePromotionAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/promotions/{id:guid}/cancel",async(Guid id,VersionInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.CancelPromotionAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/campaigns/{id:guid}/start",async(Guid id,VersionInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.PublishAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),true,ct)));
        g.MapPost("/applicants/{id:guid}/approve",async(Guid id,BudgetInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.ApproveAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/applicants/{id:guid}/reject",async(Guid id,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.RejectAsync(EndpointSupport.Actor(c),id,EndpointSupport.Key(c),ct)));
        g.MapPost("/promotion-requests/{id:guid}/approve",async(Guid id,BudgetInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.ApproveAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/promotion-requests/{id:guid}/reject",async(Guid id,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.RejectAsync(EndpointSupport.Actor(c),id,EndpointSupport.Key(c),ct)));
        g.MapPost("/creator-budgets/{id:guid}/increase",async(Guid id,BudgetInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.TopUpAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapGet("/ugc",(HttpContext c,UgcService service,CancellationToken ct)=>service.BusinessAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/ugc/{id:guid}",(Guid id,HttpContext c,UgcService service,CancellationToken ct)=>service.DetailAsync(EndpointSupport.Actor(c),id,ct));
        g.MapPost("/ugc",async(CreateUgcInput input,HttpContext c,UgcService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.CreateAsync(EndpointSupport.Actor(c),input,EndpointSupport.Key(c),ct)));
        g.MapPost("/ugc/{id:guid}/publish",async(Guid id,VersionInput input,HttpContext c,UgcService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.PublishAsync(EndpointSupport.Actor(c),id,input.Version,EndpointSupport.Key(c),ct)));
        g.MapPost("/ugc/{id:guid}/update",async(Guid id,UgcRevisionInput input,long expectedVersion,HttpContext c,UgcService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.UpdateAsync(EndpointSupport.Actor(c),id,input,expectedVersion,EndpointSupport.Key(c),ct)));
        g.MapPost("/ugc/{id:guid}/cancel",async(Guid id,VersionInput input,HttpContext c,UgcService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.CancelAsync(EndpointSupport.Actor(c),id,input.Version,EndpointSupport.Key(c),ct)));
        g.MapPost("/ugc/requests/{id:guid}/approve",async(Guid id,UgcReviewInput input,HttpContext c,UgcService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.ReviewRequestAsync(EndpointSupport.Actor(c),id,true,input.Reason,EndpointSupport.Key(c),ct)));
        g.MapPost("/ugc/requests/{id:guid}/reject",async(Guid id,UgcReviewInput input,HttpContext c,UgcService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.ReviewRequestAsync(EndpointSupport.Actor(c),id,false,input.Reason,EndpointSupport.Key(c),ct)));
        g.MapPost("/ugc/assignments/{id:guid}/review/{action}",async(Guid id,string action,UgcReviewInput input,HttpContext c,UgcService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.ReviewSubmissionAsync(EndpointSupport.Actor(c),id,action,input.Reason,EndpointSupport.Key(c),ct)));
    }
}
