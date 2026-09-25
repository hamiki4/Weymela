using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class Phase4ProjectionAndIntegrityTests(PostgresFixture fixture)
{
    [Fact] public async Task Admin_query_reads_real_journal_attributed_creator_financial_metrics()
    {
        var s=await Phase4Scenario.Create(fixture);await s.Refresh(7400);await s.Redeem(await s.Issue());
        await using var db=s.Database.Open();var admin=await s.Queries(db).CampaignAsync(Phase4Scenario.Admin,s.Seed.PromotionId);
        Assert.Equal(6000,admin.TotalBudget.Amount);Assert.Equal(2000,admin.Assigned.Amount);Assert.Equal(4000,admin.Unassigned.Amount);
        Assert.Equal(700,admin.Used.Amount);Assert.Equal(5300,admin.Remaining.Amount);
        var creator=Assert.Single(admin.Creators);Assert.Equal(1000,creator.BaselineViews);Assert.Equal(8400,creator.LatestVerifiedViews);
        Assert.Equal(7400,creator.CampaignVerifiedViews);Assert.Equal(6000,creator.RewardedViews);
        Assert.Equal(400,creator.ViewEarnings.Amount);Assert.Equal(1,creator.VerifiedSales);Assert.Equal(45,creator.SaleCommission.Amount);
        Assert.Equal(20,creator.CustomerCashback.Amount);Assert.Equal(235,creator.PlatformRevenue.Amount);
    }
    [Fact] public async Task Business_query_exposes_total_cost_and_creator_budgets_without_internal_split()
    {
        var s=await Phase4Scenario.Create(fixture);await s.Redeem(await s.Issue());await using var db=s.Database.Open();
        var result=await s.Queries(db).BusinessAsync(s.Seed.Business,s.Seed.PromotionId);var json=JsonSerializer.Serialize(result);
        Assert.Equal(10,result.TotalSaleCostPercent);Assert.Equal(1900,Assert.Single(result.Creators).BudgetRemaining.Amount);
        Assert.DoesNotContain("Cashback",json);Assert.DoesNotContain("Commission",json);Assert.DoesNotContain("PlatformRevenue",json);
        var error=await Assert.ThrowsAsync<ApplicationFailure>(()=>s.Queries(db).BusinessAsync(s.Seed.Business with { BusinessId=Guid.NewGuid() },s.Seed.PromotionId));
        Assert.Equal(FailureKind.Forbidden,error.Kind);
    }
    [Fact] public async Task Creator_query_exposes_only_own_budget_views_and_earnings()
    {
        var s=await Phase4Scenario.Create(fixture);await s.Refresh(3000);await s.Redeem(await s.Issue());await using var db=s.Database.Open();
        var result=await s.Queries(db).CreatorAsync(s.Creator,s.AllocationId);var json=JsonSerializer.Serialize(result);
        Assert.Equal(1600,result.BudgetRemaining.Amount);Assert.Equal(200,result.ViewEarnings.Amount);Assert.Equal(45,result.SaleCommissionEarnings.Amount);
        foreach(var forbidden in new[]{"Wallet","BusinessId","Cashback","Platform","Creators","Email","Phone","Address"})Assert.DoesNotContain(forbidden,json);
        await Assert.ThrowsAsync<ApplicationFailure>(()=>s.Queries(db).CreatorAsync(s.Creator with { UserId=Guid.NewGuid() },s.AllocationId));
    }
    [Fact] public async Task Customer_offer_history_and_cashier_resolution_are_safe_role_projections()
    {
        var s=await Phase4Scenario.Create(fixture);var qr=await s.Issue();await using var db=s.Database.Open();
        var resolved=await s.Checkout(db).ResolveAsync(s.Cashier,qr.Token!,new TestDirectory());Assert.Equal("Abc",resolved.Business.DisplayName);Assert.Equal("Bella",resolved.Creator.DisplayName);
        var offer=Assert.Single(await s.Queries(db).CustomerOffersAsync(s.Customer));Assert.Equal(2,offer.CashbackPercent);
        await s.Redeem(qr);var history=Assert.Single(await s.Queries(db).CustomerTransactionsAsync(s.Customer));
        Assert.Equal("VIEW_AND_SALE_PROMOTION",history.Source);Assert.Equal(20,history.CashbackEarned!.Value.Amount);
        Assert.Equal("Bella",history.Creator);Assert.Equal(1000,history.PurchaseAmount.Amount);
        var json=JsonSerializer.Serialize(new { resolved,offer,history });
        foreach(var forbidden in new[]{"Wallet","Budget","PlatformRevenue","Commission","Email","Phone","Address"})Assert.DoesNotContain(forbidden,json);
        foreach(var forbidden in new[]{"SaleId","BusinessId","CreatorId","CreatorAllocationId","JournalId","CorrelationId","IdempotencyKey"})Assert.DoesNotContain(forbidden,json);
    }
    [Fact] public async Task Customer_transactions_are_newest_first_from_recorded_sale_times()
    {
        var s=await Phase4Scenario.Create(fixture);var first=await s.Issue("first-issue");
        await s.Redeem(first,1000,"first-sale");
        s.Clock.Now=Scenario.Now.AddHours(2);
        var second=await s.Issue("second-issue");await s.Redeem(second,2000,"second-sale");
        await using var db=s.Database.Open();var transactions=await s.Queries(db).CustomerTransactionsAsync(s.Customer);
        Assert.Equal(2,transactions.Count);
        Assert.Equal(Scenario.Now.AddHours(2),transactions[0].PurchasedAtUtc);
        Assert.Equal(40,transactions[0].CashbackEarned!.Value.Amount);
        Assert.Equal(Scenario.Now,transactions[1].PurchasedAtUtc);
        Assert.Equal(20,transactions[1].CashbackEarned!.Value.Amount);
    }
    [Theory] [InlineData(ActorRole.Business)] [InlineData(ActorRole.Creator)] [InlineData(ActorRole.Customer)] [InlineData(ActorRole.Cashier)]
    public async Task Non_admin_roles_cannot_access_control_tower_financials(ActorRole role)
    {
        var s=await Phase4Scenario.Create(fixture);await using var db=s.Database.Open();
        var e=await Assert.ThrowsAsync<ApplicationFailure>(()=>s.Queries(db).CampaignAsync(new(Guid.NewGuid(),role),s.Seed.PromotionId));Assert.Equal(FailureKind.Forbidden,e.Kind);
    }
    [Fact] public async Task Completed_participation_returns_unused_reserve_to_campaign_without_reclaiming_earnings()
    {
        var s=await Phase4Scenario.Create(fixture);await s.Refresh(3000);await using var db=s.Database.Open();var a=await db.CreatorAllocations.SingleAsync();
        await new FinancialCommands(db).CompleteCreatorParticipationAsync(new(s.Seed.Business,a.Id,a.Version,Scenario.Now),"complete");
        var p=await db.Promotions.Include(x=>x.Allocations).SingleAsync();var w=await db.BusinessWallets.SingleAsync();
        Assert.Equal(5700,p.UnallocatedBudget.Amount);Assert.Equal(5700,w.ReservedBalance.Amount);Assert.Equal(4000,w.AvailableBalance.Amount);
        Assert.Equal(200,(await db.CreatorEarningsAccounts.SingleAsync()).AvailableEarnings.Amount);
        Assert.Equal(ParticipationStatus.Completed,(await db.CreatorPromotionParticipations.SingleAsync()).Status);
        await Assert.ThrowsAsync<ApplicationFailure>(()=>s.Refresh(6000,"after-complete"));
    }
    [Fact] public async Task Customer_history_is_scoped_to_self_even_when_another_valid_customer_exists()
    {
        var s=await Phase4Scenario.Create(fixture);await s.Redeem(await s.Issue());await using var db=s.Database.Open();
        var other=new Actor(Guid.NewGuid(),ActorRole.Customer,CustomerId:Guid.NewGuid());
        db.CommercePermissions.Add(new(other.UserId,other.Role,other.CustomerId!.Value,null,true,false));await db.SaveChangesAsync();
        Assert.Empty(await s.Queries(db).CustomerTransactionsAsync(other));
    }
    [Fact] public async Task Baseline_and_reward_receipts_cannot_be_rewritten_using_raw_sql()
    {
        var s=await Phase4Scenario.Create(fixture);await s.Refresh(3000);await using var db=s.Database.Open();
        await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE v3.\"CreatorPromotionParticipations\" SET \"BaselineViews\"=0"));
        await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE v3.\"ViewRewardReceipts\" SET \"Blocks\"=2"));
        Assert.Equal(1000,(await db.CreatorPromotionParticipations.SingleAsync()).BaselineViews);
    }
    [Fact] public async Task Qr_hash_and_business_binding_cannot_be_rewritten()
    {
        var s=await Phase4Scenario.Create(fixture);await s.Issue();await using var db=s.Database.Open();
        await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE v3.\"OfferQrSessions\" SET \"BusinessId\"=gen_random_uuid()"));
    }
    [Fact] public async Task Earned_account_cannot_be_edited_without_credit_or_confirmed_payout_history()
    {
        var s=await Phase4Scenario.Create(fixture);await s.Refresh(3000);await using var db=s.Database.Open();
        await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE v3.\"CreatorEarningsAccounts\" SET \"AvailableEarnings\"=201"));
    }
    [Fact] public async Task Paid_payout_cannot_be_changed_or_deleted_through_sql()
    {
        var s=await Phase4Scenario.Create(fixture,PromotionType.ViewOnly,9000,10000);await s.Refresh(81000);await using var db=s.Database.Open();
        var id=await s.Payouts(db).PrepareAsync(s.Creator,PayoutBeneficiary.Creator,s.Creator.CreatorId!.Value,"prepare");
        await s.Payouts(db).MarkPaidAsync(Phase4Scenario.Admin,id,"confirmed","paid");
        await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE v3.\"PayoutRecords\" SET \"Reference\"='different'"));
        await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("DELETE FROM v3.\"PayoutRecords\""));
    }
    [Fact] public async Task Go_live_requires_acceptance_of_current_creator_legal_versions()
    {
        var s=await Phase4Scenario.Create(fixture);await using var db=s.Database.Open();
        db.LegalDocumentVersions.Add(new(Guid.NewGuid(),LegalDocumentType.CreatorAgreement,"2","new-hash",Scenario.Now));await db.SaveChangesAsync();
        var e=await Assert.ThrowsAsync<ApplicationFailure>(()=>s.Views(db).GoLiveAsync(new(s.Creator,s.AllocationId,"TikTok","content-1","new-key")));
        Assert.Equal(FailureKind.Forbidden,e.Kind);
    }
    [Fact] public async Task Additive_migration_matches_model_and_retains_existing_schema_history()
    {
        var s=await Phase4Scenario.Create(fixture);await using var db=s.Database.Open();
        var migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(new[]{"20260911225904_InitialV3Schema","20260911233032_AddViewRewardsQrAndPayouts","20260912011149_AddOperationalSecurityAndNotifications","20260913045523_AddAuthenticationRecovery","20260913054814_AddRoleEnrollments","20260913062900_AddPhoneLoginAliases","20260914022116_AddDevicePinSessionFoundation","20260916042557_AddPasswordCredentials","20260916202055_AddCustomerProfiles","20260917020034_AddProductHandoffTransactions","20260917233008_AddBusinessLedPromotionAndUgc","20260918144832_AddUgcCustomerOffers","20260919120000_AddUgcCustomerDiscountLimit","20260922004528_AddBusinessProfileCoordinates","20260922161742_AddCreatorPromotionContentSubmissions","20260922184111_AddPromotionLiveDurationSnapshots","20260923025814_AddCashierPreauthorizationsAndBusinessOwnerCheckout","20260924034537_AddAdminAccountAuthorityFoundation","20260925010921_AddViewAsSupportSessions"},migrations);
        Assert.False(db.Database.HasPendingModelChanges());
        foreach(var entity in new[]{typeof(OfferQrSession),typeof(CreatorPromotionParticipation),typeof(CreatorPromotionContentSubmission),typeof(PayoutRecord),typeof(IdentityBinding),typeof(DepositRequest),typeof(InAppNotification),typeof(WorkerCheckpoint),typeof(UgcOpportunity),typeof(UgcAssignment),typeof(AdminGrantRecord),typeof(AccountPreauthorizationRecord),typeof(AccountLifecycleRecord),typeof(SupportSessionRecord)})
        { var model=db.Model.FindEntityType(entity)!;Assert.True(model.FindProperty("xmin")!.IsConcurrencyToken);Assert.True(model.FindProperty("Version")!.IsConcurrencyToken); }
        Assert.Equal(18,db.Model.FindEntityType(typeof(PayoutRecord))!.FindProperty(nameof(PayoutRecord.Amount))!.GetPrecision());
        Assert.Equal(2,db.Model.FindEntityType(typeof(ViewRewardReceipt))!.FindProperty(nameof(ViewRewardReceipt.BusinessCharge))!.GetScale());
    }
}
