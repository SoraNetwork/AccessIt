using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;
using AccessIt.Api.Services;

namespace AccessIt.Api.Controllers;

[Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)},{nameof(ApplicationRole.Auditor)}")]
[ApiController]
[Route("api/devices")]
public sealed class DevicesController(AccessItDbContext db, IDeviceService devices, IDeviceSyncService sync) : ControllerBase
{
    [HttpGet]
    public Task<List<AccessDevice>> List(CancellationToken cancellationToken) => db.AccessDevices.OrderBy(x => x.GroupName).ThenBy(x => x.DeviceSerial).ToListAsync(cancellationToken);

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPost("discover")]
    public Task<IReadOnlyList<AccessDevice>> Discover(CancellationToken cancellationToken) => devices.DiscoverAsync(User.CurrentUserId(), cancellationToken);

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPost("{id:guid}/open")]
    public async Task<ActionResult> Open(Guid id, [FromBody] RemoteOpenRequest request, CancellationToken cancellationToken)
    {
        var result = await devices.OpenDoorAsync(id, request.Reason, User.CurrentUserId(), cancellationToken);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPost("{id:guid}/sync")]
    public Task<SyncRun> Sync(Guid id, CancellationToken cancellationToken) => sync.SyncAsync(id, User.CurrentUserId(), cancellationToken);

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPost("sync-conflicts/{id:guid}/resolve")]
    public async Task<ActionResult> Resolve(Guid id, [FromBody] ResolveConflictRequest request, CancellationToken cancellationToken)
    {
        await sync.ResolveAsync(id, request.Resolution, User.CurrentUserId(), cancellationToken);
        return NoContent();
    }
}

public sealed record RemoteOpenRequest(string Reason);
public sealed record ResolveConflictRequest(SyncConflictResolution Resolution);
