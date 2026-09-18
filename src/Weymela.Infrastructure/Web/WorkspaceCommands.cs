using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Transactions;
using System.Text.Json;

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
        var normalizedType=input.Type.Replace("&","").Replace(" ","");
        var type=normalizedType.Equals("ViewSale",StringComparison.OrdinalIgnoreCase)||normalizedType.Equals("ViewAndSale",StringComparison.OrdinalIgnoreCase)
            ?PromotionType.ViewPlusCommission:normalizedType.Equals("ViewOnly",StringComparison.OrdinalIgnoreCase)?PromotionType.ViewOnly:
            Enum.TryParse<PromotionType>(input.Type,true,out var parsed)&&Enum.IsDefined(parsed)?parsed:throw new ApplicationFailure(FailureKind.Validation,"Choose View Only or View & Sale.");
        if(string.IsNullOrWhiteSpace(input.Title)||input.Title.Length>120||input.Description.Length>3000||input.Requirements?.Length>2000||input.Category?.Length>80||input.Region?.Length>80)
            throw new ApplicationFailure(FailureKind.Validation,"Use a title up to 120 characters and a concise Promotion brief.");
        if(input.MinimumVerifiedFollowers<0||input.StartUtc.Kind!=DateTimeKind.Utc||input.EndUtc.Kind!=DateTimeKind.Utc||input.EndUtc<=Now)
            throw new ApplicationFailure(FailureKind.Validation,"Check the follower requirement and Promotion dates.");
        var platforms=new List<(CreatorPlatform Platform,int Capacity)>();
        foreach(var row in input.Platforms??[])
        {
            if(!Enum.TryParse<CreatorPlatform>(row.Platform,true,out var platform)||!Enum.IsDefined(platform)||row.Capacity is <1 or >100)
                throw new ApplicationFailure(FailureKind.Validation,"Choose supported Promotion platforms and valid Creator capacity.");
            platforms.Add((platform,row.Capacity));
        }
        if(platforms.GroupBy(x=>x.Platform).Any(x=>x.Count()>1))throw new ApplicationFailure(FailureKind.Validation,"Add each Promotion platform once.");
        var resources=(input.Resources??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(SafeExternalUrl).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return await Commands.CreateCampaignOnceAsync(new(actor,input.Title,input.Description,type,Amount(input.CampaignBudget),
            new(Clean(input.Category),input.MinimumVerifiedFollowers,Clean(input.Region),Clean(input.Requirements)),input.StartUtc,input.EndUtc,Now,
            Clean(input.Slogan),Clean(input.Location),JsonSerializer.Serialize(resources),platforms),key,ct);
    }
    public async Task<Guid> DepositAsync(Actor actor,DepositInput input,string key,CancellationToken ct)
    { await Business(actor,ct);return await Commands.CreditDepositAsync(new(actor,Amount(input.Amount),key,Now),input.ExpectedVersion,ct); }
    public async Task<Guid> FundAsync(Actor actor,Guid id,FundingInput input,string key,CancellationToken ct)
    { await Business(actor,ct);return await Commands.FundPromotionAsync(new(actor,id,input.CampaignVersion,input.WalletVersion,key,Now),ct); }
    public async Task<Guid> PublishAsync(Actor actor,Guid id,VersionInput input,string key,bool startOnly,CancellationToken ct)
    { await Business(actor,ct);return await Commands.PublishCampaignOnceAsync(new(actor,id,input.Version,Now),key,startOnly,ct); }
    public Task<Guid> UpdatePromotionAsync(Actor actor,Guid id,PromotionPresentationInput input,string key,CancellationToken ct)=>
        new EfUnitOfWork(db,System.Data.IsolationLevel.Serializable).ExecuteAsync(async token=>
        {
            await Business(actor,token);var resources=(input.Resources??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(SafeExternalUrl).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var fingerprint=RequestFingerprint.Create(id.ToString(),input.Description,input.Slogan??"",input.Location??"",JsonSerializer.Serialize(resources),input.Version.ToString());
            var op=new FinancialOperation(db);var replay=await op.Replay(actor,"UpdatePromotion",key,fingerprint,token);if(replay is not null)return Guid.Parse(replay);
            var promotion=await new PromotionRepository(db).GetAsync(id,token)??throw new ApplicationFailure(FailureKind.NotFound,"Promotion not found.");
            if(promotion.BusinessId!=actor.BusinessId)throw new ApplicationFailure(FailureKind.Forbidden,"This Promotion belongs to another Business.");
            if(promotion.Version!=input.Version)throw new ApplicationFailure(FailureKind.ConcurrencyConflict,"Promotion changed. Reload before trying again.");
            promotion.UpdatePresentation(input.Description,Clean(input.Slogan),Clean(input.Location),JsonSerializer.Serialize(resources));
            var correlation=Guid.NewGuid();op.Remember(actor,"UpdatePromotion",key,fingerprint,id.ToString(),Now);op.Audit(actor,"PromotionUpdated",correlation,Now,id);op.Event("PromotionUpdated",new{PromotionId=id,promotion.BusinessId},Now);return id;
        },ct);
    public Task<Guid> CancelPromotionAsync(Actor actor,Guid id,VersionInput input,string key,CancellationToken ct)=>
        new EfUnitOfWork(db,System.Data.IsolationLevel.Serializable).ExecuteAsync(async token=>
        {
            await Business(actor,token);var fingerprint=RequestFingerprint.Create(id.ToString(),input.Version.ToString());var op=new FinancialOperation(db);
            var replay=await op.Replay(actor,"CancelPromotion",key,fingerprint,token);if(replay is not null)return Guid.Parse(replay);
            var promotion=await new PromotionRepository(db).GetAsync(id,token)??throw new ApplicationFailure(FailureKind.NotFound,"Promotion not found.");
            if(promotion.BusinessId!=actor.BusinessId)throw new ApplicationFailure(FailureKind.Forbidden,"This Promotion belongs to another Business.");
            if(promotion.Version!=input.Version)throw new ApplicationFailure(FailureKind.ConcurrencyConflict,"Promotion changed. Reload before trying again.");
            var wallet=await db.BusinessWallets.SingleAsync(x=>x.BusinessId==promotion.BusinessId,token);var released=promotion.ReservedBudget;var correlation=Guid.NewGuid();
            try{promotion.CancelExceptional(wallet,Now,correlation);}catch(InvalidOperationException ex){throw new ApplicationFailure(FailureKind.Validation,ex.Message,ex);}
            if(released.Amount>0)
            {
                var journal=new FinancialJournal(Guid.NewGuid().ToString("N"),correlation,actor.UserId,JournalSourceType.PromotionReservation,Now,key);
                journal.AddLine(JournalLineType.Debit,released,"CampaignUnallocatedReserve");journal.AddLine(JournalLineType.Credit,released,"BusinessAvailable");journal.Post();db.FinancialJournals.Add(journal);
                db.Entry(journal).Property("BusinessId").CurrentValue=promotion.BusinessId;db.Entry(journal).Property("PromotionId").CurrentValue=id;
                db.WalletEntries.Add(new(Guid.NewGuid(),promotion.BusinessId,id,released,"Release",journal.Id,Now));db.PromotionBudgetEntries.Add(new(Guid.NewGuid(),id,null,released,"Released",journal.Id,Now));
            }
            op.Remember(actor,"CancelPromotion",key,fingerprint,id.ToString(),Now);op.Audit(actor,"PromotionCancelled",correlation,Now,id);op.Event("PromotionCancelled",new{PromotionId=id,promotion.BusinessId,Released=released.Amount},Now);return id;
        },ct);
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
        CreatorPlatform? platform=null;
        if(!string.IsNullOrWhiteSpace(input.Platform))
        {if(!Enum.TryParse<CreatorPlatform>(input.Platform,true,out var parsed)||!Enum.IsDefined(parsed))throw new ApplicationFailure(FailureKind.Validation,"Choose a supported Promotion platform.");platform=parsed;}
        if(platform is not null&&input.CreatorSocialProfileId is null)throw new ApplicationFailure(FailureKind.Validation,"Choose one of your Creator social profiles.");
        if(input.CreatorSocialProfileId is {} profileId&&!await db.CreatorSocialProfiles.AnyAsync(x=>x.Id==profileId&&x.CreatorId==actor.CreatorId&&x.IsActive&&x.Platform==platform,ct))
            throw new ApplicationFailure(FailureKind.Forbidden,"This social profile is not available to the active Creator.");
        return await Commands.JoinCampaignOnceAsync(new(actor,id,input.Message,input.ContentConcept,new(p.Category,p.Region,p.VerifiedFollowers,p.SocialVerified),Now,input.CreatorSocialProfileId,platform),key,ct);
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
            var version=FinancialConfigurationVersionFactory.Create(id,latest.ConfigurationId,
                latest.Version+1,actor.UserId,effective,input);
            db.FinancialConfigurationVersions.Add(version);op.Remember(actor,"FinancialSettings",key,fingerprint,id.ToString(),Now);
            op.Audit(actor,"FinancialConfigurationChanged",Guid.NewGuid(),Now);op.Event("FinancialConfigurationChanged",new{VersionId=id,EffectiveFromUtc=effective},Now);return id;
        },ct);
    internal static Money Amount(decimal value)
    { if(value<=0||value>9999999999999999.99m||decimal.Round(value,2)!=value)throw new ApplicationFailure(FailureKind.Validation,"Enter a positive amount with no more than two decimal places.");return new(value); }
    private static Money NonNegative(decimal value)
    { if(value==0)return Money.Zero();return Amount(value); }
    private static string? Clean(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static string SafeExternalUrl(string value)
    {if(value.Length>1000||!Uri.TryCreate(value.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps||!string.IsNullOrEmpty(uri.UserInfo))throw new ApplicationFailure(FailureKind.Validation,"Promotion resources must use secure HTTPS links.");return uri.AbsoluteUri;}
}
