using Weymela.Infrastructure.Identity;

namespace Weymela.Api;

/// <summary>Only the API host has the Firebase credential. Notification workers skip this event.</summary>
public sealed class AccountDeletionBackgroundService(IServiceScopeFactory scopes, ILogger<AccountDeletionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (scope.ServiceProvider.GetRequiredService<IAccountIdentityDeletionProvider>().Enabled)
                    await scope.ServiceProvider.GetRequiredService<AccountIdentityDeletionProcessor>().ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { logger.LogWarning("Account identity deletion processing is temporarily unavailable."); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
