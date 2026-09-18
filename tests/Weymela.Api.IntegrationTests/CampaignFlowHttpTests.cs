using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class CampaignFlowHttpTests(PostgresFixture postgres)
{
    [Fact] public async Task Concurrent_approval_and_rejection_cannot_leave_a_rejected_applicant_with_a_budget()
    {
        await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("business");using var competing=await f.Login("business");
        var row=(await c.GetJson("/api/business/campaigns")).AsArray().Single(r=>r!["type"]!.GetValue<string>()=="View & Sale")!;
        var id=row["id"]!.GetValue<string>();var d=await c.GetJson($"/api/business/campaigns/{id}");var a=d["applicants"]!.AsArray().Single(r=>r!["status"]!.GetValue<string>()=="Pending")!;
        var applicationId=a["id"]!.GetValue<string>();var responses=await Task.WhenAll(c.Post($"/api/business/applicants/{applicationId}/approve",new{amount=100,version=row["version"]!.GetValue<long>()}),competing.Post($"/api/business/applicants/{applicationId}/reject",new{}));
        Assert.Single(responses,r=>r.IsSuccessStatusCode);Assert.All(responses.Where(r=>!r.IsSuccessStatusCode),r=>Assert.Contains(r.StatusCode,new[]{HttpStatusCode.BadRequest,HttpStatusCode.Conflict}));
        await using var db=f.Database.Open();var stored=await db.CreatorApplications.SingleAsync(x=>x.Id==Guid.Parse(applicationId));var hasBudget=await db.CreatorAllocations.AnyAsync(x=>x.PromotionId==stored.PromotionId&&x.CreatorId==stored.CreatorId);Assert.Equal(stored.Status==CreatorApplicationStatus.Approved,hasBudget);
    }
    private static object Draft(decimal budget=500)=>new{title="HTTP Promotion",description="A real persisted Promotion",type="View & Sale",campaignBudget=budget,requirements="Original content",category="Food",region="Addis Ababa",minimumVerifiedFollowers=1000,startUtc=DateTime.UtcNow.AddMinutes(-1),endUtc=DateTime.UtcNow.AddDays(5)};
    [Fact] public async Task Create_fund_publish_join_approve_and_topup_share_real_financial_services()
    {
        await using var f=await ApiFixture.CreateAsync(postgres);using var business=await f.Login("business");using var creator=await f.Login("other-creator");
        var created=await business.PostJson("/api/business/campaigns",Draft());var id=created["id"]!.GetValue<string>();
        var wallet=await business.GetJson("/api/business/wallet");var details=await business.GetJson($"/api/business/campaigns/{id}");
        var input=new{campaignVersion=details["campaign"]!["version"]!.GetValue<long>(),walletVersion=wallet["version"]!.GetValue<long>()};
        await business.PostJson($"/api/business/campaigns/{id}/fund",input,"fund-http");await business.PostJson($"/api/business/campaigns/{id}/fund",input,"fund-http");
        var funded=await business.GetJson($"/api/business/campaigns/{id}");Assert.Equal("Funded",funded["campaign"]!["status"]!.GetValue<string>());
        var after=await business.GetJson("/api/business/wallet");Assert.Equal(wallet["available"]!.GetValue<decimal>()-500,after["available"]!.GetValue<decimal>());
        await business.PostJson($"/api/business/campaigns/{id}/publish",new{version=funded["campaign"]!["version"]!.GetValue<long>()});
        var opportunities=(await creator.GetJson("/api/creator/discover")).AsArray();Assert.Contains(opportunities,r=>r!["id"]!.GetValue<string>()==id);
        var application=await creator.PostJson($"/api/creator/campaigns/{id}/join",new{message="I have an idea",contentConcept="Original story"},"join-http");
        var replay=await creator.PostJson($"/api/creator/campaigns/{id}/join",new{message="I have an idea",contentConcept="Original story"},"join-http");Assert.Equal(application.ToJsonString(),replay.ToJsonString());
        var current=await business.GetJson($"/api/business/campaigns/{id}");var approval=await business.PostJson($"/api/business/applicants/{application["id"]!.GetValue<string>()}/approve",new{amount=200,version=current["campaign"]!["version"]!.GetValue<long>()},"approve-http");
        current=await business.GetJson($"/api/business/campaigns/{id}");Assert.Equal(200,current["campaign"]!["assignedToCreators"]!.GetValue<decimal>());
        var budget=current["creators"]![0]!;await business.PostJson($"/api/business/creator-budgets/{approval["id"]!.GetValue<string>()}/increase",new{amount=100,version=budget["version"]!.GetValue<long>()});
        var own=(await creator.GetJson("/api/creator/campaigns")).AsArray().Single(r=>r!["id"]!.GetValue<string>()==id)!;Assert.Equal(300,own["yourBudget"]!.GetValue<decimal>());
        Assert.DoesNotContain("platform",own.ToJsonString());
    }
    [Fact] public async Task Approve_and_set_budget_rolls_back_approval_when_budget_is_too_large()
    {
        await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("business");var rows=(await c.GetJson("/api/business/campaigns")).AsArray();var hybrid=rows.Single(r=>r!["type"]!.GetValue<string>()=="View & Sale")!;var id=hybrid["id"]!.GetValue<string>();var d=await c.GetJson($"/api/business/campaigns/{id}");var app=d["applicants"]!.AsArray().Single(r=>r!["status"]!.GetValue<string>()=="Pending")!;
        var response=await c.Post($"/api/business/applicants/{app["id"]!.GetValue<string>()}/approve",new{amount=999999,version=d["campaign"]!["version"]!.GetValue<long>()});Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var after=await c.GetJson($"/api/business/campaigns/{id}");Assert.Equal("Pending",after["applicants"]!.AsArray().Single(r=>r!["id"]!.ToJsonString()==app["id"]!.ToJsonString())!["status"]!.GetValue<string>());Assert.Equal(d["campaign"]!["assignedToCreators"]!.ToJsonString(),after["campaign"]!["assignedToCreators"]!.ToJsonString());
    }
    [Fact] public async Task Business_cannot_submit_financial_rates_or_creator_identity()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var c=await f.Login("business");var input=System.Text.Json.JsonSerializer.SerializeToNode(Draft(),new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;input["creatorCommissionPercent"]=99;Assert.Equal(HttpStatusCode.BadRequest,(await c.Post("/api/business/campaigns",input)).StatusCode);}

    [Fact] public async Task Creator_cannot_forge_eligibility_or_apply_as_another_creator()
    {await using var f=await ApiFixture.CreateAsync(postgres);using var business=await f.Login("business");var id=(await business.GetJson("/api/business/campaigns"))[0]!["id"]!.GetValue<string>();using var c=await f.Login("ineligible-creator");var response=await c.Post($"/api/creator/campaigns/{id}/join",new{message="hello",contentConcept="story"});Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);var forged=await c.Post($"/api/creator/campaigns/{id}/join",new{message="hello",creatorId=Guid.NewGuid(),verifiedFollowers=999999});Assert.Equal(HttpStatusCode.BadRequest,forged.StatusCode);}

    [Fact] public async Task Future_settings_do_not_apply_early_and_saved_versions_are_authoritative()
    {
        await using var f=await ApiFixture.CreateAsync(postgres);using var admin=await f.Login("admin");using var business=await f.Login("business");var original=await admin.GetJson("/api/admin/financial-settings");var settings=original["current"]!.DeepClone();settings["effectiveFromUtc"]=DateTime.UtcNow.AddDays(1);settings["viewOnly"]!["creatorEarns"]=210;settings["viewOnly"]!["platformKeeps"]=90;
        await admin.PostJson("/api/admin/financial-settings",new{settings,expectedVersion=1});var saved=await admin.GetJson("/api/admin/financial-settings");Assert.Equal(1,saved["version"]!.GetValue<int>());Assert.Equal(200,saved["current"]!["viewOnly"]!["creatorEarns"]!.GetValue<decimal>());Assert.Equal(210,saved["versions"]![0]!["settings"]!["viewOnly"]!["creatorEarns"]!.GetValue<decimal>());
        settings["effectiveFromUtc"]=null;settings["viewOnly"]!["businessPays"]=350;settings["viewOnly"]!["platformKeeps"]=140;await admin.PostJson("/api/admin/financial-settings",new{settings,expectedVersion=2});Assert.Equal(350,(await business.GetJson("/api/business/pricing"))["rows"]![0]!["businessPays"]!.GetValue<decimal>());
        var campaign=(await business.GetJson("/api/business/campaigns")).AsArray().Single(r=>r!["type"]!.GetValue<string>()=="View Only")!;Assert.Equal(300,(await business.GetJson($"/api/business/campaigns/{campaign["id"]!.GetValue<string>()}"))["pricing"]!["businessPays"]!.GetValue<decimal>());
    }
    [Fact] public async Task HTTP_QR_wrong_business_fails_then_correct_cashier_redeems_once()
    {
        await using var f=await ApiFixture.CreateAsync(postgres);using var customer=await f.Login("customer");using var wrong=await f.Login("other-cashier");using var cashier=await f.Login("cashier");var offer=(await customer.GetJson("/api/customer/offers"))[0]!;var qr=await customer.PostJson($"/api/customer/offers/{offer["id"]!.GetValue<string>()}/qr",new{});var token=qr["token"]!.GetValue<string>();
        var wrongResponse=await wrong.Post("/api/checkout/resolve",new{token});Assert.Equal(HttpStatusCode.Forbidden,wrongResponse.StatusCode);
        var safe=await cashier.PostJson("/api/checkout/resolve",new{token});Assert.DoesNotContain(token,safe.ToJsonString());Assert.DoesNotContain("cashback",safe.ToJsonString());
        var sale=await cashier.PostJson("/api/checkout/confirm",new{token,purchaseAmount=1000},"checkout-retry");var replay=await cashier.PostJson("/api/checkout/confirm",new{token,purchaseAmount=1000},"checkout-retry");Assert.Equal(sale.ToJsonString(),replay.ToJsonString());Assert.Equal(100,sale["totalBusinessCharge"]!["amount"]!.GetValue<decimal>());
        await using var db=f.Database.Open();Assert.Equal(2,await db.VerifiedSales.CountAsync());Assert.DoesNotContain(token,System.Text.Json.JsonSerializer.Serialize(await db.OfferQrSessions.ToListAsync()));
    }
    [Fact] public async Task Admin_payout_and_settlement_endpoints_preserve_carry_forward()
    {
        await using var f=await ApiFixture.CreateAsync(postgres);
        await using(var db=f.Database.Open())
        {
            await new EfUnitOfWork(db).ExecuteAsync(async ct=>
            {
                var creatorId=Weymela.Infrastructure.Development.DevelopmentDirectory.Id(300);
                var account=await db.CreatorEarningsAccounts.SingleAsync(x=>x.CreatorId==creatorId,ct);
                var threshold=(await db.FinancialConfigurationVersions.AsNoTracking().OrderByDescending(x=>x.Version).FirstAsync(ct)).CreatorPayoutThreshold.Amount;
                var amount=threshold+125-account.AvailableEarnings.Amount;
                Assert.True(amount>0);
                var promotionId=await db.Promotions.Select(x=>x.Id).FirstAsync(ct);var correlation=Guid.NewGuid();var at=DateTime.UtcNow;
                account.Earn(new Money(amount),EarningSource.AuthorizedAdjustment,promotionId,at,correlation);
                var journal=new FinancialJournal(Guid.NewGuid().ToString("N"),correlation,null,JournalSourceType.Adjustment,at,"fixture-payout-eligibility");
                journal.AddLine(JournalLineType.Debit,new Money(amount),"AuthorizedAdjustmentClearing");journal.AddLine(JournalLineType.Credit,new Money(amount),"CreatorPayable");journal.Post();db.FinancialJournals.Add(journal);
                db.Entry(journal).Property("CreatorId").CurrentValue=creatorId;db.Entry(journal).Property("PromotionId").CurrentValue=promotionId;
                var entry=account.Entries.Last();db.CreatorEarningEntries.Add(entry);db.Entry(entry).Property("JournalId").CurrentValue=journal.Id;return true;
            });
        }
        using var c=await f.Login("admin");var queue=await c.GetJson("/api/admin/payouts");var creator=queue["creators"]![0]!;
        var prepared=await c.PostJson($"/api/admin/payouts/Creator/{creator["subjectId"]!.GetValue<string>()}/prepare",new{});
        await c.PostJson($"/api/admin/payouts/{prepared["id"]!.GetValue<string>()}/paid",new{reference="test-payment-1"},"pay");
        await c.PostJson($"/api/admin/payouts/{prepared["id"]!.GetValue<string>()}/paid",new{reference="test-payment-1"},"pay");
        using var own=await f.Login("creator");Assert.Equal(125,(await own.GetJson("/api/creator/earnings"))["availableEarnings"]!.GetValue<decimal>());
        var unsettled=(await c.GetJson("/api/admin/platform"))["unsettled"]!["amount"]!.GetValue<decimal>();
        await c.PostJson("/api/admin/platform/settlements",new{amount=100,reference="test-settlement"});
        Assert.Equal(unsettled-100,(await c.GetJson("/api/admin/platform"))["unsettled"]!["amount"]!.GetValue<decimal>());
    }
}
