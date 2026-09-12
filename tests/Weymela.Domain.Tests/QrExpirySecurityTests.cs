using System;
using Weymela.Domain;
using Xunit;
namespace Weymela.Domain.Tests;
public sealed class QrExpirySecurityTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static OfferQrSession New() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('A', 64), Now, "request");
    [Fact] public void Expiry_is_exactly_five_minutes_and_cannot_be_observed_early()
    { var qr = New(); qr.ObserveExpiry(Now.AddMinutes(5).AddTicks(-1)); Assert.Equal(OfferQrStatus.Issued, qr.Status); Assert.Equal(Now.AddMinutes(5), qr.ExpiresAtUtc); }
    [Fact] public void Observing_expiry_is_idempotent_and_retains_binding_history()
    { var qr = New(); var original = qr.TokenHash; qr.ObserveExpiry(Now.AddMinutes(5)); qr.ObserveExpiry(Now.AddDays(1)); Assert.Equal(OfferQrStatus.Expired, qr.Status); Assert.Equal(1, qr.Version); Assert.Equal(original, qr.TokenHash); Assert.Null(qr.SaleId); }
    [Fact] public void Used_session_never_changes_to_expired()
    { var qr = New(); var sale = Guid.NewGuid(); qr.Use(sale, qr.BusinessId, Now.AddMinutes(1)); qr.ObserveExpiry(Now.AddMinutes(6)); Assert.Equal(OfferQrStatus.Used, qr.Status); Assert.Equal(sale, qr.SaleId); }
    [Fact] public void Qr_diagnostics_do_not_reveal_token_digest_or_private_bindings()
    { var qr = New(); Assert.DoesNotContain(qr.TokenHash, qr.ToString()); Assert.DoesNotContain(qr.CustomerId.ToString(), qr.ToString()); }
}
