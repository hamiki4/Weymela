namespace Weymela.Application.Operations;

public interface IPayoutDestinationProtector
{
    string Protect(string value);
    string Unprotect(string protectedValue);
}

public sealed record PayoutDestinationInput(string Method, string? BankName, string? AccountNumber,
    string? BankCountry = null, string? Iban = null, string? SwiftBic = null, string? RoutingNumber = null);

public sealed record PayoutDestinationView(string Method, string Provider, string Account, string LegalName,
    DateTime? UpdatedAtUtc, bool IsMasked, bool IsConfigured, string? RegisteredPhone = null,
    string? PhoneCountry = null, bool TelebirrEligible = false, bool MpesaEligible = false,
    string? BankCountry = null, string? Iban = null, string? SwiftBic = null, string? RoutingNumber = null);
