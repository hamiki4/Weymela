using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Notifications;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);
var options = RuntimeOptions.Load(builder.Configuration, builder.Environment.EnvironmentName, worker: true);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => { o.IncludeScopes = true; o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ"; o.UseUtcTimestamp = true; });
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Error);
builder.Services.AddSingleton(options); builder.Services.AddWeymelaPersistence(options.ConnectionString);
builder.Services.AddSingleton<INotificationPushProvider, DisabledPushProvider>(); builder.Services.AddScoped<WorkerPump>();
builder.Services.AddSingleton<WorkerHealthSignal>();
builder.Services.AddHostedService<OperationalWorker>();
using var host = builder.Build();
await host.RunAsync(); return 0;

internal sealed class OperationalWorker(IServiceScopeFactory scopes, RuntimeOptions options, WorkerHealthSignal healthSignal,
    ILogger<OperationalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        healthSignal.MarkFailedCycle();
        if (!options.WorkerEnabled) { logger.LogInformation("V3 operational worker disabled by configuration"); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var count = await scope.ServiceProvider.GetRequiredService<WorkerPump>().RunOnceAsync(timeout.Token);
                healthSignal.MarkSuccessfulCycle();
                logger.LogInformation("V3 worker cycle completed {ProcessedCount}", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch {
                healthSignal.MarkFailedCycle();
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
