namespace BenefitCardsPortal.Services;

public sealed class AutomaticBackupService(IServiceScopeFactory scopeFactory, ILogger<AutomaticBackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IBackupService>().RunScheduledBackupIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Automatic database backup failed; it will retry on the next minute tick."); }
        }
    }
}
