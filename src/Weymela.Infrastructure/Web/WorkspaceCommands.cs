using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Web;

public sealed class WorkspaceCommands(WeymelaDbContext db,IWorkspaceDirectory directory,TimeProvider clock)
{
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    private FinancialCommands Commands=>new(db,clock);
    private async Task Business(Actor actor,CancellationToken ct)
    {
        if(actor.Role!=ActorRole.Business||actor.BusinessId is null||!await db.CommercePermissions.AnyAsync(x=>x.UserId==actor.UserId&&x.Role==actor.Role&&x.SubjectId==actor.BusinessId&&x.IsActive,ct))
            throw new ApplicationFailure(FailureKind.Forbidden,"This Business workspace is not available to you.");
    }
    public async Task<Guid> CreateAsync(Actor actor,CreateCampaignInput input,string key,CancellationToken ct)
    {
        await Business(actor,ct);
        if(!Enum.TryParse<PromotionType>(input.Type,out var type)||!Enum.IsDefined(type))throw new ApplicationFailure(FailureKind.Validation,"Choose a Campaign Type.");
        if(string.IsNullOrWhiteSpace(input.Title)||input.Title.Length>120||input.Description.Length>3000||input.Requirements?.Length>2000||input.Category?.Length>80||input.Region?.Length>80)
            throw new ApplicationFailure(FailureKind.Validation,"Use a title up to 120 characters and a concise Campaign brief.");
        if(input.MinimumVerifiedFollowers<0||input.StartUtc.Kind!=DateTimeKind.Utc||input.EndUtc.Kind!=DateTimeKind.Utc||input.EndUtc<=Now)
            throw new ApplicationFailure(FailureKind.Validation,"Check the follower requirement and Campaign dates.");
        return await Commands.CreateCampaignOnceAsync(new(actor,input.Title,input.Description,type,Amount(input.CampaignBudget),
            new(Clean(input.Category),input.MinimumVerifiedFollowers,Clean(input.Region),Clean(input.Requirements)),input.StartUtc,input.EndUtc,Now),key,ct);
    }
    public async Task<Guid> DepositAsync(Actor actor,DepositInput input,string key,CancellationToken ct)
    { await Business(actor,ct);return await Commands.CreditDepositAsync(new(actor,Amount(input.Amount),key,Now),input.ExpectedVersion,ct); }
    public async Task<Guid> FundAsync(Actor actor,Guid id,FundingInput input,string key,CancellationToken ct)
    { await Business(actor,ct);return await Commands.FundPromotionAsync(new(actor,id,input.CampaignVersion,input.WalletVersion,key,Now),ct); }
    public async Task<Guid> PublishAsync(Actor actor,Guid id,VersionInput input,string key,bool startOnly,CancellationToken ct)
    { await Business(actor,ct);return await Commands.PublishCampaignOnceAsync(new(actor,id,input.Version,Now),key,startOnly,ct); }
    public async Task<Guid> ApproveAsync(Actor actor,Guid id,BudgetInput input,string key,CancellationToken ct)
    { await Business(actor,ct);return await Commands.ApproveAndSetBudgetAsync(actor,id,Amount(input.Amount),input.Version,key,Now,ct); }
    public async Task<Guid> RejectAsync(Actor actor,Guid id,string key,CancellationToken ct)
    { await Business(actor,ct);return await Commands.ReviewCreatorAsync(actor,id,false,Now,ct,key); }
    public async Task<Guid> TopUpAsync(Actor actor,Guid id,BudgetInput input,string key,CancellationToken ct)
    { await Business(actor,ct);return await Commands.IncreaseCreatorAllocationAsync(new(actor,id,Amount(input.Amount),input.Version,Now),key,ct); }
    public async Task<Guid> JoinAsync(Actor actor,Guid id,JoinInput input,string key,CancellationToken ct)
    {
        await new CommerceAccessPolicy(db).EnsureCreatorAsync(actor,actor.CreatorId??Guid.Empty,ct);
        if(input.Message?.Length>1000||input.ContentConcept?.Length>2000)throw new ApplicationFailure(FailureKind.Validation,"Keep your message and content concept concise.");
        var p=await directory.CreatorCardAsync(actor.CreatorId!.Value,ct);
        return await Commands.JoinCampaignOnceAsync(new(actor,id,input.Message,input.ContentConcept,new(p.Category,p.Region,p.VerifiedFollowers,p.SocialVerified),Now),key,ct);
    }
    public Task<Guid> SettingsAsync(Actor actor,FinancialSettingsInput input,int expectedVersion,string key,CancellationToken ct)=>
        new EfUnitOfWork(db,System.Data.IsolationLevel.Serializable).ExecuteAsync(async token=>
        {
            if(actor.Role!=ActorRole.PlatformAdmin)throw new ApplicationFailure(FailureKind.Forbidden,"Admin access is required.");
            var op=new FinancialOperation(db);var fingerprint=RequestFingerprint.Create(System.Text.Json.JsonSerializer.Serialize(input),expectedVersion.ToString());
            var replay=await op.Replay(actor,"FinancialSettings",key,fingerprint,token);if(replay is not null)return Guid.Parse(replay);
            var latest=await db.FinancialConfigurationVersions.OrderByDescending(x=>x.Version).FirstAsync(token);
            if(latest.Version!=expectedVersion)throw new ApplicationFailure(FailureKind.ConcurrencyConflict,"Financial settings changed. Reload the saved values before continuing.");
            var effective=input.EffectiveFromUtc??Now;
            if(effective.Kind!=DateTimeKind.Utc||input.EffectiveFromUtc is not null&&effective<Now)throw new ApplicationFailure(FailureKind.Validation,"Choose a future effective date or Effective Now.");
            var id=Guid.NewGuid();
            PricingSnapshot Price(PromotionType type,ViewPriceInput p)=>new(type,p.ViewsPerReward,Amount(p.BusinessPays),NonNegative(p.CreatorEarns),NonNegative(p.PlatformKeeps),
                input.CreatorCommissionPercent,input.CustomerCashbackPercent,input.PlatformPercent,effective,id,p.MinimumCampaignBudget is {} min?Amount(min):null);
            var version=new FinancialConfigurationVersion(id,latest.ConfigurationId,latest.Version+1,actor.UserId,effective,
                Price(PromotionType.ViewOnly,input.ViewOnly),Price(PromotionType.ViewPlusCommission,input.ViewPlusCommission),Amount(input.CreatorThreshold),Amount(input.CustomerThreshold));
            db.FinancialConfigurationVersions.Add(version);op.Remember(actor,"FinancialSettings",key,fingerprint,id.ToString(),Now);
            op.Audit(actor,"FinancialConfigurationChanged",Guid.NewGuid(),Now);op.Event("FinancialConfigurationChanged",new{VersionId=id,EffectiveFromUtc=effective},Now);return id;
        },ct);
    internal static Money Amount(decimal value)
    { if(value<=0||value>9999999999999999.99m||decimal.Round(value,2)!=value)throw new ApplicationFailure(FailureKind.Validation,"Enter a positive amount with no more than two decimal places.");return new(value); }
    private static Money NonNegative(decimal value)
    { if(value==0)return Money.Zero();return Amount(value); }
    private static string? Clean(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
}
