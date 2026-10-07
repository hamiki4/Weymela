using Microsoft.AspNetCore.DataProtection;
using Weymela.Application.Operations;

namespace Weymela.Api.Security;

public sealed class PayoutDestinationProtector(IDataProtectionProvider provider) : IPayoutDestinationProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("WeymelaV3.PayoutDestination.v1");
    public string Protect(string value) => protector.Protect(value);
    public string Unprotect(string protectedValue) => protector.Unprotect(protectedValue);
}
