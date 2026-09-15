using System.Text.RegularExpressions;
using Weymela.Application;
using Weymela.Application.Operations;

namespace Weymela.Infrastructure.Identity;

/// <summary>Canonicalizes account phone aliases before hashing or lookup.</summary>
public static class PhoneNumberNormalizer
{
    private static readonly Regex International = new("^\\+[1-9][0-9]{6,14}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Invalid();
        var compact = new string(value.Trim().Where(c => c is not (' ' or '\t' or '\r' or '\n' or '-' or '(' or ')')).ToArray());
        if (compact.Length == 10 && compact[0] == '0' && (compact[1] is '7' or '9') && AsciiDigits(compact))
            compact = "+251" + compact[1..];
        else if (compact.Length == 9 && (compact[0] is '7' or '9') && AsciiDigits(compact))
            compact = "+251" + compact;
        else if (compact.Length == 12 && compact.StartsWith("251", StringComparison.Ordinal)
                 && (compact[3] is '7' or '9') && AsciiDigits(compact))
            compact = "+" + compact;
        if (!International.IsMatch(compact)) throw Invalid();
        // Ethiopian mobile aliases must use the same national-number shape in every format.
        if (compact.StartsWith("+251", StringComparison.Ordinal) && compact.Length > 4
            && (compact[4] is '7' or '9') && compact.Length != 13) throw Invalid();
        return compact;
    }

    private static bool AsciiDigits(string value) => value.All(c => c is >= '0' and <= '9');
    private static ApplicationFailure Invalid() => new(FailureKind.Validation, "Enter a valid email or phone number.");
}
