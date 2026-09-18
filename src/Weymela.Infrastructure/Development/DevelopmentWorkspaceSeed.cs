using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Development;

public static class DevelopmentWorkspaceSeed
{
    // Called ONLY by isolated test/browser hosts, never by the API startup path.
    public static async Task SeedAsync(WeymelaDbContext db,DevelopmentDirectory directory,DevelopmentViewProvider provider,TimeProvider clock)
    {
        var database=db.Database.GetDbConnection().Database;
        if(!database.StartsWith("v3_test_",StringComparison.Ordinal))throw new InvalidOperationException("Fixture seed requires a disposable V3 test database.");
        if(await db.FinancialConfigurations.AnyAsync())return;
        var now=clock.GetUtcNow().UtcDateTime;var admin=directory.Get("admin").Actor;var business=directory.Get("business").Actor;var creator=directory.Get("creator").Actor;
        foreach(var persona in directory.Personas)
        {
            var a=persona.Actor;var subject=a.Role switch{ActorRole.Business=>a.BusinessId!.Value,ActorRole.Creator=>a.CreatorId!.Value,ActorRole.Customer=>a.CustomerId!.Value,_=>a.UserId};
            db.CommercePermissions.Add(new(a.UserId,a.Role,subject,a.BusinessId,true,a.Role is ActorRole.Business or ActorRole.Cashier));
            if(a.Role==ActorRole.Business)db.BusinessWallets.Add(new(a.BusinessId!.Value));
        }
        var config=Guid.NewGuid();var version=Guid.NewGuid();db.FinancialConfigurations.Add(new(config,"PlatformPricing"));
        db.FinancialConfigurationVersions.Add(new(version,config,1,admin.UserId,now.AddDays(-1),
            new(PromotionType.ViewOnly,3000,new Money(300),new Money(200),new Money(100),3m,4m,3m,now.AddDays(-1),version),
            new(PromotionType.ViewPlusCommission,3000,new Money(150),new Money(100),new Money(50),3m,4m,3m,now.AddDays(-1),version),new Money(3000),new Money(4000),
            new(new Money(200),10m,null,now.AddDays(-1),version)));
        foreach(var type in new[]{LegalDocumentType.TermsOfService,LegalDocumentType.PrivacyPolicy,LegalDocumentType.BusinessAgreement,LegalDocumentType.CreatorAgreement,LegalDocumentType.AntiCircumventionAgreement})
        {
            var id=Guid.NewGuid();db.LegalDocumentVersions.Add(new(id,type,"fixture-1","development-fixture-not-legal-wording",now.AddDays(-1)));
            foreach(var persona in directory.Personas.Where(x=>type is not (LegalDocumentType.TermsOfService or LegalDocumentType.PrivacyPolicy) && x.Actor.Role is ActorRole.Business or ActorRole.Creator))
                db.LegalAcceptances.Add(new(persona.Actor.UserId,persona.Actor.Role==ActorRole.Business?LegalRole.Business:LegalRole.Creator,id,now,null,null));
        }
        await db.SaveChangesAsync();db.ChangeTracker.Clear();var commands=new FinancialCommands(db,clock);
        await commands.CreditDepositAsync(new(business,new Money(18000),"fixture-deposit",now),0);
        await commands.CreditDepositAsync(new(directory.Get("other-business").Actor,new Money(3000),"fixture-deposit",now),0);
        var campaigns=new List<Guid>();
        foreach(var type in new[]{PromotionType.ViewPlusCommission,PromotionType.ViewOnly})
        {
            var hybrid=type==PromotionType.ViewPlusCommission;var id=await commands.CreateCampaignOnceAsync(new(business,
                hybrid?"A little coffee. A great story.":"Made for your morning.",
                hybrid?"Bring your audience into our everyday coffee ritual. Share an honest visit, a favourite cup, and a moment worth coming back for.":"Tell the story behind a better morning. Create thoughtful short-form content celebrating local coffee.",
                type,new Money(hybrid?6000:4000),new("Food",10000,"Addis Ababa","One original short video. Authentic storytelling. Clearly disclose the partnership."),now.AddHours(-1),now.AddDays(21),now),"fixture-create-"+type);
            var w=await db.BusinessWallets.AsNoTracking().SingleAsync(x=>x.BusinessId==business.BusinessId);var p=await db.Promotions.AsNoTracking().SingleAsync(x=>x.Id==id);
            await commands.FundPromotionAsync(new(business,id,p.Version,w.Version,"fixture-fund-"+type,now));
            p=await db.Promotions.AsNoTracking().SingleAsync(x=>x.Id==id);
            await commands.PublishCampaignOnceAsync(new(business,id,p.Version,now),"fixture-publish-"+type);
            var app=await commands.JoinCampaignOnceAsync(new(creator,id,"I'd love to tell this story.","A warm, honest look at a local favourite.",new("Food","Addis Ababa",42000,true),now),"fixture-join-"+type);
            p=await db.Promotions.AsNoTracking().SingleAsync(x=>x.Id==id);
            var allocation=await commands.ApproveAndSetBudgetAsync(business,app,new Money(hybrid?2000:1500),p.Version,"fixture-approve-"+type,now);
            var content=hybrid?"7611111111111111111":"7611111111111111112";provider.SetCount(content,1000);
            var views=new VerifiedViewService(db,provider,new CommerceAccessPolicy(db),clock);
            var participation=await views.GoLiveAsync(new(creator,allocation,"TikTok",content,"fixture-live-"+type));
            provider.SetCount(content,hybrid?4000:7000);await views.RefreshAsync(new(creator,participation,"fixture-views-"+type));
            if(hybrid)
            {
                var checkout=new CheckoutService(db,new CommerceAccessPolicy(db),clock);
                var qr=await checkout.IssueAsync(new(directory.Get("customer").Actor,allocation,"fixture-offer"));
                await checkout.RedeemAsync(new(directory.Get("cashier").Actor,qr.Token!,new Money(6000),"fixture-sale"));
                var other=directory.Get("other-creator").Actor;
                await commands.JoinCampaignOnceAsync(new(other,id,"A Campaign that fits my audience.","A behind-the-scenes coffee tasting.",new("Food","Addis Ababa",29000,true),now),"fixture-pending");
            }
            campaigns.Add(id);
        }
    }
}
