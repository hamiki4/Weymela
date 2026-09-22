namespace Weymela.Domain;

public static class CreatorLiveWindow
{
    public static DateTime ExpiresAtUtc(DateTime wentLiveAtUtc, int durationDays)
    {
        EnsureUtc(wentLiveAtUtc);
        EnsureDuration(durationDays);
        return wentLiveAtUtc.AddDays(durationDays);
    }

    /// <summary>Returns the configured duration through 1 while live, and null at or after expiry.</summary>
    public static int? RemainingDays(DateTime wentLiveAtUtc, DateTime nowUtc, int durationDays)
    {
        EnsureUtc(wentLiveAtUtc);
        EnsureUtc(nowUtc);
        EnsureDuration(durationDays);
        var remaining = ExpiresAtUtc(wentLiveAtUtc, durationDays) - nowUtc;
        if (remaining <= TimeSpan.Zero) return null;
        return Math.Clamp((int)Math.Ceiling(remaining.TotalDays), 1, durationDays);
    }

    public static bool IsLive(DateTime wentLiveAtUtc, DateTime nowUtc, int durationDays)
        => RemainingDays(wentLiveAtUtc, nowUtc, durationDays) is not null;

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Live-window times must be UTC.");
    }

    private static void EnsureDuration(int durationDays)
    {
        if (durationDays <= 0) throw new ArgumentOutOfRangeException(nameof(durationDays));
    }
}
