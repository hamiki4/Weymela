namespace Weymela.Domain;

public static class PromotionLiveDurationPolicy
{
    public const int MinimumDays = 1;
    public const int MaximumDays = 365;

    public static bool IsValid(int days) => days is >= MinimumDays and <= MaximumDays;

    public static void Validate(int days)
    {
        if (!IsValid(days))
            throw new ArgumentOutOfRangeException(nameof(days),
                $"Promotion live duration must be between {MinimumDays} and {MaximumDays} days.");
    }
}
