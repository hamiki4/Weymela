using System.Text.RegularExpressions;
using Weymela.Application;

namespace Weymela.Infrastructure.Operations;

public static partial class InputRules
{
    public static string Reference(string? value, string label = "reference", int max = 120)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || !ReferencePattern().IsMatch(value))
            throw new ApplicationFailure(FailureKind.Validation, $"Enter a valid {label} using letters, numbers, dots, underscores or hyphens.");
        return value;
    }
    public static void Text(string? value, int max, bool required = false)
    {
        if (required && string.IsNullOrWhiteSpace(value) || value?.Length > max || value?.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) == true)
            throw new ApplicationFailure(FailureKind.Validation, "Check the required text and length limits.");
    }
    public static void Id(Guid value) { if (value == Guid.Empty) throw new ApplicationFailure(FailureKind.Validation, "A valid reference is required."); }
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")] private static partial Regex ReferencePattern();
}
