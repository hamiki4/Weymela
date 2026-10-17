namespace Weymela.Domain;

public sealed record PayoutBalanceMovement(DateTime AtUtc, decimal Amount, bool Ugc = false);

public static class PayoutEligibilityPolicy
{
    // The application's ETB payout calendar uses Ethiopia time (UTC+3).
    public static DateTime? MonthlyCutoff(DateTime nowUtc, int? day, DateTime? effectiveUtc)
    {
        if (day is null || effectiveUtc is null || nowUtc < effectiveUtc) return null;
        var local = nowUtc.AddHours(3);
        var due = new DateTime(local.Year, local.Month, day.Value, 0, 0, 0, DateTimeKind.Utc).AddHours(-3);
        if (due > nowUtc) due = due.AddMonths(-1);
        return due >= effectiveUtc ? due : null;
    }

    public static decimal Eligible(decimal available, decimal threshold, bool exemptUgc, DateTime? cutoff,
        IEnumerable<PayoutBalanceMovement> movements)
    {
        if (available <= 0) return 0;
        if (available >= threshold) return available;
        var lots = new List<(DateTime At, decimal Amount, bool Ugc)>();
        decimal debt = 0;
        foreach (var movement in movements.OrderBy(x => x.AtUtc).ThenByDescending(x => x.Amount))
        {
            if (movement.Amount > 0)
            {
                var amount = movement.Amount - Math.Min(debt, movement.Amount);
                debt = Math.Max(0, debt - movement.Amount);
                if (amount > 0) lots.Add((movement.AtUtc, amount, movement.Ugc));
                continue;
            }
            var deduction = -movement.Amount;
            // Consume exempt UGC first, then the oldest regular earnings. Paid
            // amounts never become eligible again on refresh or a later month.
            foreach (var index in Enumerable.Range(0, lots.Count).OrderByDescending(i => lots[i].Ugc).ThenBy(i => lots[i].At))
            {
                var lot = lots[index];
                var used = Math.Min(deduction, lot.Amount);
                lots[index] = (lot.At, lot.Amount - used, lot.Ugc);
                deduction -= used;
                if (deduction == 0) break;
            }
            debt += deduction;
        }
        return Math.Min(available, lots.Where(x => exemptUgc && x.Ugc || cutoff is { } due && x.At <= due).Sum(x => x.Amount));
    }
}
