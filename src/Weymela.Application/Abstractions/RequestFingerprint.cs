using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Weymela.Domain;

namespace Weymela.Application;

public static class RequestFingerprint
{
    public static string Create(params string[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts))));
    public static string Amount(Money m) => m.Currency + ":" + m.Amount.ToString("0.00##########################", CultureInfo.InvariantCulture);
}
