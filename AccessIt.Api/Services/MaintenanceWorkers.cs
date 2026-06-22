using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AccessIt.Api.Configuration;
using AccessIt.Api.Data;
using AccessIt.Api.DingTalk;
using AccessIt.Api.Domain;

namespace AccessIt.Api.Services;

public sealed class DingTalkDirectorySyncWorker(IServiceScopeFactory scopeFactory, IOptionsMonitor<DingTalkOptions> options, ILogger<DingTalkDirectorySyncWorker> logger) : BackgroundService
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

public sealed class VisitorExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<VisitorExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AccessItDbContext>();
                var faces = scope.ServiceProvider.GetRequiredService<IFaceStorageService>();
                var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
                var expired = await db.AccessPeople.Include(x => x.FaceAssets)
                    .Where(x => x.Kind == PersonKind.Visitor && x.Status == PersonStatus.Active && !x.PermanentValid && x.EnableEndTime < DateTime.Now)
                    .ToListAsync(stoppingToken);
                foreach (var visitor in expired)
                {
                    visitor.Status = PersonStatus.Expired;
                    foreach (var face in visitor.FaceAssets.ToList()) await faces.DeleteAsync(face, stoppingToken);
                }
                if (expired.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    foreach (var visitor in expired)
                        await audit.WriteAsync(null, "visitor.expired", "AccessPerson", visitor.Id, new { visitor.EmployeeNo }, stoppingToken);
                    logger.LogInformation("Marked {Count} visitors as expired.", expired.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Visitor expiry sweep failed."); }
        }
    }
}
