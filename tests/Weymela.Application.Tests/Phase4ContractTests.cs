using System;
using System.Linq;
using System.Text.Json;
using Weymela.Application;
using Weymela.Domain;
using Xunit;

namespace Weymela.Application.Tests;

public sealed class Phase4ContractTests
{
    [Fact] public void Redemption_contract_accepts_only_scanner_token_purchase_and_idempotency_not_financial_splits()
    { Assert.Equal(new[]{"Actor","Token","PurchaseAmount","IdempotencyKey"},typeof(RedeemOfferCommand).GetProperties().Select(x=>x.Name)); }
    [Fact] public void Qr_token_is_redacted_from_command_diagnostics_and_generic_json()
    {
        var token=new SensitiveQrToken("a-private-opaque-token");var c=new RedeemOfferCommand(new(Guid.NewGuid(),ActorRole.Cashier),token,new Money(1000),"key");
        Assert.DoesNotContain(token.Value,c.ToString());Assert.DoesNotContain(token.Value,JsonSerializer.Serialize(c));Assert.DoesNotContain(token.Value,token.ToString());
    }
    [Fact] public void Customer_projection_cannot_expose_budgets_commission_or_platform_revenue()
    { Assert.Equal(new[]{"OfferId","Source","Offer","Business","Creator","BenefitPercent","Slogan","Location","WentLiveAtUtc","ExpiresAtUtc","RemainingDays","CashbackPercent"},typeof(CustomerOffer).GetProperties().Select(x=>x.Name)); }
    [Fact] public void Customer_history_projection_contains_only_customer_relevant_purchase_and_cashback()
    {
        Assert.Equal(new[]{"Source","Offer","Business","Creator","PurchaseAmount","CustomerPaidAmount","CashbackEarned","DiscountReceived","PurchasedAtUtc"},typeof(CustomerTransaction).GetProperties().Select(x=>x.Name));
        Assert.Equal(new[]{"AvailableCashback","MinimumCashOut","RemainingToCashOut","Eligible","Status","PayoutHistory"},typeof(CustomerCashbackSummary).GetProperties().Select(x=>x.Name));
    }
    [Fact] public void Creator_projection_contains_only_own_budget_views_and_earnings()
    { Assert.Equal(new[]{"PromotionId","Campaign","Type","YourBudget","BudgetRemaining","VerifiedViews","ViewEarnings","SaleCommissionEarnings","Status"},typeof(CreatorCampaignFinance).GetProperties().Select(x=>x.Name)); }
    [Fact] public void Business_projection_has_total_sale_cost_but_no_internal_earnings_split()
    {
        var names=typeof(BusinessCampaignFinance).GetProperties().Select(x=>x.Name).ToArray();Assert.Contains("TotalSaleCostPercent",names);
        Assert.DoesNotContain(names,x=>x.Contains("Commission")||x.Contains("Cashback")||x.Contains("Platform")||x.Contains("Earning"));
        Assert.Equal(new[]{"Creator","StartingBudget","Used","BudgetRemaining"},typeof(BusinessCreatorBudget).GetProperties().Select(x=>x.Name));
    }
    [Fact] public void Admin_creator_contract_exposes_full_view_sale_and_financial_oversight()
    {
        var names=typeof(AdminCreatorFinance).GetProperties().Select(x=>x.Name).ToArray();
        foreach(var required in new[]{"BaselineViews","LatestVerifiedViews","CampaignVerifiedViews","RewardedViews","ViewEarnings","VerifiedSales","SaleCommission","CustomerCashback","PlatformRevenue"})Assert.Contains(required,names);
    }
    [Fact] public void Public_identity_contracts_exclude_phone_email_and_private_address()
    {
        Assert.Equal(new[]{"Id","DisplayName"},typeof(PublicBusiness).GetProperties().Select(x=>x.Name));
        Assert.Equal(new[]{"Id","PublicId","DisplayName"},typeof(PublicCreator).GetProperties().Select(x=>x.Name));
    }
    [Fact] public void Manual_checkout_binding_cannot_override_rates_or_choose_separate_funding_source()
    { Assert.Equal(new[]{"CustomerId","PromotionId","CreatorId","CreatorAllocationId","BusinessId","SourceReference"},typeof(CheckoutBinding).GetProperties().Select(x=>x.Name)); }
    [Fact] public void View_provider_contract_carries_verification_evidence_without_raw_credentials()
    { Assert.Equal(new[]{"Count","Provider","ExternalContentId","VerifiedAtUtc","EvidenceReference"},typeof(VerifiedViewResult).GetProperties().Select(x=>x.Name)); }
}
