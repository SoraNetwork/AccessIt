using System.Text.Json;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;

namespace AccessIt.Api.Services;

public interface IAuditService
{
    Task WriteAsync(string? actorUserId, string action, string entityType, object entityId, object? details = null, CancellationToken cancellationToken = default);
}

public sealed class AuditService(AccessItDbContext db) : IAuditService
{
    public async Task WriteAsync(string? actorUserId, string action, string entityType, object entityId, object? details = null, CancellationToken cancellationToken = default)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString() ?? string.Empty,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details)
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
