using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Notifications;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;

var checkHealth = args.Contains("--check-health", StringComparer.Ordinal);
var builder = Host.CreateApplicationBuilder(args.Where(x => x != "--check-health").ToArray());
var options = RuntimeOptions.Load(builder.Configuration, builder.Environment.EnvironmentName, worker: true);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => { o.IncludeScopes = true; o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ"; o.UseUtcTimestamp = true; });
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Error);
builder.Services.AddSingleton(options); builder.Services.AddWeymelaPersistence(options.ConnectionString);
builder.Services.AddSingleton<INotificationPushProvider, DisabledPushProvider>(); builder.Services.AddScoped<WorkerPump>();
builder.Services.AddHostedService<OperationalWorker>();
using var host = builder.Build();
if (checkHealth)
{
    try
    {
        using var scope = host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WeymelaDbContext>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var since = DateTime.UtcNow.AddSeconds(-Math.Max(60, options.WorkerIntervalSeconds * 4));
        var healthy = await db.WorkerCheckpoints.AsNoTracking().AnyAsync(x => x.Name == "operational-worker" && x.LastSuccessAtUtc >= since, timeout.Token);
        Console.WriteLine(healthy ? "healthy" : "unhealthy"); return healthy ? 0 : 1;
    }
    catch { Console.WriteLine("unhealthy"); return 1; }
}
await host.RunAsync(); return 0;

internal sealed class OperationalWorker(IServiceScopeFactory scopes, RuntimeOptions options, ILogger<OperationalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.WorkerEnabled) { logger.LogInformation("V3 operational worker disabled by configuration"); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var count = await scope.ServiceProvider.GetRequiredService<WorkerPump>().RunOnceAsync(timeout.Token);
                logger.LogInformation("V3 worker cycle completed {ProcessedCount}", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch {
                logger.LogWarning("V3 worker cycle failed {ErrorCode}", "WorkerUnavailable");
                try { using var scope=scopes.CreateScope();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await scope.ServiceProvider.GetRequiredService<WorkerPump>().RecordFailureAsync(timeout.Token); }
                catch { /* A database outage is reported in logs; never block shutdown or reveal provider details. */ }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(options.WorkerIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
