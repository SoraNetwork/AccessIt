namespace AccessIt.Api.Services;

/// <summary>Continues HIKIoT's asynchronous standard-issue verification without asking an operator to revisit a task centre.</summary>
public sealed class HikiotIssueReconcileWorker(IServiceScopeFactory scopeFactory, ILogger<HikiotIssueReconcileWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IStandardAuthorityIssuanceService>();
                var count = await service.ReconcilePendingAsync(stoppingToken);
                if (count > 0) logger.LogInformation("Reconciled {Count} pending HIKIoT authority issue batches.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning(exception, "Unable to reconcile pending HIKIoT authority issue batches."); }

            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }
}
