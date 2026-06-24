using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AccessIt.Api.Configuration;
using AccessIt.Api.Data;
using AccessIt.Api.DingTalk;
using AccessIt.Api.Services;

namespace AccessIt.Api.Services;

/// <summary>
/// 定时同步钉钉通讯录到本地用户表。属于钉钉 API 集成部分，予以保留。
/// </summary>
public sealed class DingTalkDirectorySyncWorker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DingTalkOptions> options,
    ILogger<DingTalkDirectorySyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(Math.Max(1, options.CurrentValue.DirectorySyncHours)), stoppingToken);
                using var scope = scopeFactory.CreateScope();
                var gateway = scope.ServiceProvider.GetRequiredService<IDingTalkGateway>();
                var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
                var result = await identity.SyncDirectoryAsync(await gateway.GetDirectoryAsync(stoppingToken), stoppingToken);
                logger.LogInformation("DingTalk directory synced: {Created} created, {Updated} updated.", result.Created, result.Updated);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "DingTalk directory sync failed."); }
        }
    }
}
