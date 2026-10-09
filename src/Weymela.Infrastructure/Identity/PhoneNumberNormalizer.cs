using PhoneNumbers;
using Weymela.Application;
using Weymela.Application.Operations;

namespace Weymela.Infrastructure.Identity;

/// <summary>Canonicalizes account phone aliases before hashing or lookup.</summary>
public static class PhoneNumberNormalizer
{
    private static readonly PhoneNumberUtil Phones = PhoneNumberUtil.GetInstance();

    public static string Normalize(string value, string defaultRegion = "ET")
    {
        if (string.IsNullOrWhiteSpace(value)) throw Invalid();
        try
        {
            var region = string.IsNullOrWhiteSpace(defaultRegion) ? "ZZ" : defaultRegion.Trim().ToUpperInvariant();
            var parsed = Phones.Parse(value.Trim(), region);
            if (!Phones.IsValidNumber(parsed)) throw Invalid();
            return Phones.Format(parsed, PhoneNumberFormat.E164);
        }
        catch (NumberParseException)
        {
            throw Invalid();
        }
    }

    public static string Region(string canonicalPhone)
    {
        try
        {
            var parsed = Phones.Parse(canonicalPhone, "ZZ");
            return Phones.GetRegionCodeForNumber(parsed) ?? "ZZ";
        }
        catch (NumberParseException) { return "ZZ"; }
    }

    private static ApplicationFailure Invalid() => new(FailureKind.Validation, "Enter a valid phone number.", code: "InvalidPhone");
}
