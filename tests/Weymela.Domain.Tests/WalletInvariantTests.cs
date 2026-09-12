using System;
using Weymela.Domain;
using Xunit;
namespace Weymela.Domain.Tests;
public sealed class WalletInvariantTests
{
 private static readonly DateTime Now=new(2026,1,1,0,0,0,DateTimeKind.Utc);
 private static BusinessWallet Wallet(decimal amount=0){var w=new BusinessWallet(Guid.NewGuid());if(amount>0)w.CreditDeposit(new Money(amount),Now,Guid.NewGuid());return w;}
 private static void AssertInvariant(BusinessWallet w)=>Assert.Equal(w.TotalBalance.Amount,w.AvailableBalance.Amount+w.ReservedBalance.Amount);
 [Fact] public void Arbitrary_positive_deposit_is_accepted(){var w=Wallet();w.CreditDeposit(new Money(37.25m),Now,Guid.NewGuid());Assert.Equal(37.25m,w.AvailableBalance.Amount);AssertInvariant(w);}
 [Fact] public void Zero_deposit_is_rejected(){Assert.Throws<ArgumentOutOfRangeException>(()=>Wallet().CreditDeposit(new Money(0),Now,Guid.NewGuid()));}
 [Fact] public void Negative_deposit_is_rejected(){Assert.Throws<ArgumentOutOfRangeException>(()=>Wallet().CreditDeposit(new Money(-1),Now,Guid.NewGuid()));}
 [Fact] public void Reservation_moves_available_to_reserved_without_changing_total(){var w=Wallet(1000);var total=w.TotalBalance;w.ReserveForPromotion(new Money(400),Now,Guid.NewGuid());Assert.Equal(600,w.AvailableBalance.Amount);Assert.Equal(400,w.ReservedBalance.Amount);Assert.Equal(total,w.TotalBalance);AssertInvariant(w);}
 [Fact] public void Reservation_greater_than_available_is_rejected(){Assert.Throws<InvalidOperationException>(()=>Wallet(100).ReserveForPromotion(new Money(101),Now,Guid.NewGuid()));}
 [Fact] public void Consumption_decreases_reserved_and_total(){var w=Wallet(1000);w.ReserveForPromotion(new Money(600),Now,Guid.NewGuid());w.ConsumeReservedFunds(new Money(250),Now,Guid.NewGuid());Assert.Equal(350,w.ReservedBalance.Amount);Assert.Equal(750,w.TotalBalance.Amount);AssertInvariant(w);}
 [Fact] public void Consumption_cannot_make_reserved_negative(){var w=Wallet(100);w.ReserveForPromotion(new Money(50),Now,Guid.NewGuid());Assert.Throws<InvalidOperationException>(()=>w.ConsumeReservedFunds(new Money(51),Now,Guid.NewGuid()));AssertInvariant(w);}
 [Fact] public void Release_returns_reserved_to_available_and_preserves_invariant(){var w=Wallet(1000);w.ReserveForPromotion(new Money(600),Now,Guid.NewGuid());w.ReleasePromotionReserve(new Money(200),Now,Guid.NewGuid());Assert.Equal(600,w.AvailableBalance.Amount);Assert.Equal(400,w.ReservedBalance.Amount);AssertInvariant(w);}
 [Fact] public void Wallet_has_no_business_type_minimum_wallet_concept(){var names=typeof(BusinessWallet).GetProperties();Assert.DoesNotContain(names,p=>p.Name.Contains("Minimum",StringComparison.OrdinalIgnoreCase)||p.Name.Contains("BusinessType",StringComparison.OrdinalIgnoreCase));}
}
