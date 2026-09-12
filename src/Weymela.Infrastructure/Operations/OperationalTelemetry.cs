using System.Diagnostics.Metrics;
using Weymela.Application;

namespace Weymela.Infrastructure.Operations;

public static class OperationalTelemetry
{
    public static readonly Meter Meter = new("Weymela.V3", "0.6.0");
    public static readonly TrackedCounter FinancialFailures = new("weymela.financial.failures");
    public static readonly TrackedCounter QrFailures = new("weymela.qr.failures");
    public static readonly TrackedCounter ConcurrencyConflicts = new("weymela.concurrency.conflicts");
    public static readonly TrackedCounter ProviderErrors = new("weymela.provider.errors");
    public static readonly TrackedCounter OutboxFailures = new("weymela.outbox.failures");
    public static readonly TrackedCounter NotificationFailures = new("weymela.notification.failures");
    public static readonly TrackedCounter RateLimited = new("weymela.http.rate_limited");
    public static void Failure(FailureKind kind, bool financial, bool qr)
    {
        if (financial) FinancialFailures.Add(1);
        if (qr) QrFailures.Add(1);
        if (kind == FailureKind.ConcurrencyConflict) ConcurrencyConflicts.Add(1);
    }
    public static object Snapshot() => new { financialFailures = FinancialFailures.Value, qrFailures = QrFailures.Value,
        concurrencyConflicts = ConcurrencyConflicts.Value, providerErrors = ProviderErrors.Value, outboxFailures = OutboxFailures.Value,
        notificationFailures = NotificationFailures.Value, rateLimited = RateLimited.Value };
}
public sealed class TrackedCounter(string name)
{
    private readonly Counter<long> counter = OperationalTelemetry.Meter.CreateCounter<long>(name);
    private long value;
    public long Value => Interlocked.Read(ref value);
    public void Add(long count) { Interlocked.Add(ref value, count); counter.Add(count); }
}
