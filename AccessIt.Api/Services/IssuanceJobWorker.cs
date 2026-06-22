namespace AccessIt.Api.Services;

public sealed class IssuanceJobWorker(IServiceScopeFactory scopeFactory, ILogger<IssuanceJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IIssuanceJobService>();
                var processed = await jobs.ProcessNextAsync(stoppingToken);
                await Task.Delay(processed ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Issuance worker iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
