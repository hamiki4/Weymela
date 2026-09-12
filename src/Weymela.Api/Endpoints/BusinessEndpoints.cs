using Weymela.Application.Web;
using Weymela.Infrastructure.Web;

namespace Weymela.Api.Endpoints;

internal static class BusinessEndpoints
{
    public static void MapBusinessEndpoints(this WebApplication app,bool development)
    {
        var g=app.MapGroup("/api/business").RequireAuthorization("Business").AddEndpointFilter<Weymela.Api.Security.ValidatedInputFilter>();
        g.MapGet("/home",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.BusinessHomeAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/wallet",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.WalletAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/pricing",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.BusinessPricingAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/campaigns",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.BusinessCampaignsAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/campaigns/{id:guid}",(Guid id,HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.BusinessCampaignAsync(EndpointSupport.Actor(c),id,ct));
        g.MapPost("/wallet/deposits",async(DepositInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
        {
            // No pretend bank confirmation in a non-development environment. A trusted deposit adapter is required later.
            if(!development)return Results.Problem(statusCode:503,title:"Deposit confirmation is not connected.");
            return EndpointSupport.Id(await commands.DepositAsync(EndpointSupport.Actor(c),input,EndpointSupport.Key(c),ct));
        });
        g.MapPost("/campaigns",async(CreateCampaignInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.CreateAsync(EndpointSupport.Actor(c),input,EndpointSupport.Key(c),ct)));
        g.MapPost("/campaigns/{id:guid}/fund",async(Guid id,FundingInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.FundAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/campaigns/{id:guid}/publish",async(Guid id,VersionInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.PublishAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),false,ct)));
        g.MapPost("/campaigns/{id:guid}/start",async(Guid id,VersionInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.PublishAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),true,ct)));
        g.MapPost("/applicants/{id:guid}/approve",async(Guid id,BudgetInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.ApproveAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
        g.MapPost("/applicants/{id:guid}/reject",async(Guid id,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.RejectAsync(EndpointSupport.Actor(c),id,EndpointSupport.Key(c),ct)));
        g.MapPost("/creator-budgets/{id:guid}/increase",async(Guid id,BudgetInput input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.TopUpAsync(EndpointSupport.Actor(c),id,input,EndpointSupport.Key(c),ct)));
    }
}
