using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Web;

namespace Weymela.Api.Endpoints;

internal static class AdminEndpoints
{
    private sealed record SettingsRequest(FinancialSettingsInput Settings,int ExpectedVersion);
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var g=app.MapGroup("/api/admin").RequireAuthorization("PlatformAdmin").AddEndpointFilter<Weymela.Api.Security.ValidatedInputFilter>();
        g.MapGet("/home",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.AdminHomeAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/businesses",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.BusinessesAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/creators",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.CreatorsAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/campaigns",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.AdminCampaignsAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/campaigns/{id:guid}",(Guid id,HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.AdminCampaignAsync(EndpointSupport.Actor(c),id,ct));
        g.MapGet("/financial-settings",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.SettingsAsync(EndpointSupport.Actor(c),ct));
        g.MapPost("/financial-settings",async(SettingsRequest input,HttpContext c,WorkspaceCommands commands,CancellationToken ct)=>
            EndpointSupport.Id(await commands.SettingsAsync(EndpointSupport.Actor(c),input.Settings,input.ExpectedVersion,EndpointSupport.Key(c),ct)));
        g.MapGet("/payouts",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.PayoutsAsync(EndpointSupport.Actor(c),ct));
        g.MapPost("/payouts/{kind}/{subject:guid}/prepare",async(string kind,Guid subject,HttpContext c,PayoutService service,CancellationToken ct)=>
        {
            if(!Enum.TryParse<PayoutBeneficiary>(kind,true,out var beneficiary)||!Enum.IsDefined(beneficiary))return Results.BadRequest();
            return EndpointSupport.Id(await service.PrepareAsync(EndpointSupport.Actor(c),beneficiary,subject,EndpointSupport.Key(c),ct));
        });
        g.MapPost("/payouts/{id:guid}/paid",async(Guid id,ConfirmPaymentInput input,HttpContext c,PayoutService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.MarkPaidAsync(EndpointSupport.Actor(c),id,input.Reference,EndpointSupport.Key(c),ct)));
        g.MapPost("/platform/settlements",async(SettlementInput input,HttpContext c,PayoutService service,CancellationToken ct)=>
            EndpointSupport.Id(await service.SettlePlatformAsync(EndpointSupport.Actor(c),new Money(input.Amount),input.Reference,EndpointSupport.Key(c),ct)));
        g.MapGet("/platform",(HttpContext c,FinancialQueries q,CancellationToken ct)=>q.PlatformAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/notifications",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.NotificationsAsync(EndpointSupport.Actor(c),ct));
        g.MapGet("/audit",(HttpContext c,WorkspaceQueries q,CancellationToken ct)=>q.AuditAsync(EndpointSupport.Actor(c),ct));
    }
}
