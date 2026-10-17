using System;
using Weymela.Domain;
using Xunit;

namespace Weymela.Domain.Tests;

public sealed class PayoutEligibilityPolicyTests
{
    private static readonly DateTime November = new(2026,10,31,21,0,0,DateTimeKind.Utc);
    [Fact]
    public void Monthly_eligibility_starts_on_owner_date_in_Ethiopia_and_persists_after_the_day()
    {
        Assert.Null(PayoutEligibilityPolicy.MonthlyCutoff(November.AddTicks(-1),1,November));
        Assert.Equal(November,PayoutEligibilityPolicy.MonthlyCutoff(November,1,November));
        Assert.Equal(November,PayoutEligibilityPolicy.MonthlyCutoff(November.AddDays(10),1,November));
        Assert.Equal(new DateTime(2026,11,30,21,0,0,DateTimeKind.Utc),PayoutEligibilityPolicy.MonthlyCutoff(November.AddMonths(1),1,November));
    }
    [Fact]
    public void Admin_day_changes_do_not_unlock_a_future_cutoff()
    {
        Assert.Null(PayoutEligibilityPolicy.MonthlyCutoff(November.AddDays(10),28,November));
        Assert.Equal(November.AddDays(27),PayoutEligibilityPolicy.MonthlyCutoff(November.AddDays(28),28,November));
    }
    [Fact]
    public void Minimum_unlocks_actual_available_balance()
    {
        Assert.Equal(4200,PayoutEligibilityPolicy.Eligible(4200,3000,false,null,[]));
        Assert.Equal(0,PayoutEligibilityPolicy.Eligible(2999,3000,false,null,[]));
    }
    [Fact]
    public void Fixed_ugc_is_available_immediately_without_unlocking_regular_earnings()
    {
        Assert.Equal(4900,PayoutEligibilityPolicy.Eligible(5100,6000,true,null,
            [new(November,4900,true),new(November,200)]));
        Assert.Equal(0,PayoutEligibilityPolicy.Eligible(4900,6000,false,null,[new(November,4900,true)]));
    }
    [Fact]
    public void Monthly_cutoff_excludes_new_earnings_and_prevents_duplicate_payment()
    {
        PayoutBalanceMovement[] history=[new(November.AddDays(-1),1200),new(November.AddDays(1),400)];
        Assert.Equal(1200,PayoutEligibilityPolicy.Eligible(1600,2000,false,November,history));
        Assert.Equal(0,PayoutEligibilityPolicy.Eligible(400,2000,false,November,[..history,new(November.AddDays(2),-1200)]));
    }
    [Fact]
    public void Earlier_paid_earnings_do_not_consume_later_fixed_ugc()
    {
        Assert.Equal(4900,PayoutEligibilityPolicy.Eligible(4900,6000,true,null,
            [new(November.AddDays(-4),6000),new(November.AddDays(-3),-6000),new(November,4900,true)]));
    }
    [Fact]
    public void Partial_ugc_payment_leaves_only_the_unpaid_fixed_amount_exempt()
    {
        Assert.Equal(900,PayoutEligibilityPolicy.Eligible(1400,6000,true,null,
            [new(November,4900,true),new(November,500),new(November.AddDays(1),-4000)]));
    }
}
