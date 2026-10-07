namespace Weymela.Application.Operations;

public interface IPayoutDestinationProtector
{
    string Protect(string value);
    string Unprotect(string protectedValue);
}

public sealed record PayoutDestinationInput(string Method, string? BankName, string? AccountNumber);

public sealed record PayoutDestinationView(string Method, string Provider, string Account, string LegalName,
    DateTime? UpdatedAtUtc, bool IsMasked, bool IsConfigured);
