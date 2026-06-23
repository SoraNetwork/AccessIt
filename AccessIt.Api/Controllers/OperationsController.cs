using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;
using AccessIt.Api.Services;

namespace AccessIt.Api.Controllers;

[Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)},{nameof(ApplicationRole.Auditor)}")]
[ApiController]
[Route("api")]
public sealed class OperationsController(AccessItDbContext db, IIssuanceJobService jobs, IVisitorQrService qr) : ControllerBase
{
    [HttpGet("jobs")]
    public Task<List<IssuanceJob>> Jobs([FromQuery] IssuanceJobStatus? status, CancellationToken cancellationToken)
    {
        var query = db.IssuanceJobs.AsQueryable();
        if (status is not null) query = query.Where(x => x.Status == status);
        return query.OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(cancellationToken);
    }

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPost("jobs/{id:guid}/retry")]
    public async Task<ActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        await jobs.RetryAsync(id, User.CurrentUserId(), cancellationToken);
        return NoContent();
    }

    [HttpGet("sync-runs")]
    public Task<List<SyncRun>> SyncRuns(CancellationToken cancellationToken) => db.SyncRuns.OrderByDescending(x => x.StartedAtUtc).Take(100).ToListAsync(cancellationToken);

    [HttpGet("sync-conflicts")]
    public Task<List<SyncConflict>> SyncConflicts([FromQuery] SyncConflictResolution? resolution, CancellationToken cancellationToken)
    {
        var query = db.SyncConflicts.AsQueryable();
        if (resolution is not null) query = query.Where(x => x.Resolution == resolution);
        return query.OrderByDescending(x => x.Id).Take(200).ToListAsync(cancellationToken);
    }

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPost("visitor-qr")]
    public async Task<ActionResult<VisitorQrIssueResult>> IssueQr([FromBody] IssueVisitorQrRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await qr.IssueAsync(request.VisitorId, request.DeviceId, request.ExpireMinutes, request.MaxOpenTimes, User.CurrentUserId(), cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPost("visitor-qr/{id:guid}/revoke")]
    public async Task<ActionResult> RevokeQr(Guid id, CancellationToken cancellationToken)
    {
        await qr.RevokeAsync(id, User.CurrentUserId(), cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPost("visitor-qr/{id:guid}/notify-host")]
    public async Task<ActionResult> NotifyHost(Guid id, CancellationToken cancellationToken)
    {
        await qr.NotifyHostAsync(id, User.CurrentUserId(), cancellationToken);
        return NoContent();
    }

    [HttpGet("audit-logs")]
    public Task<List<AuditEvent>> Audit([FromQuery] string? action, CancellationToken cancellationToken)
    {
        var query = db.AuditEvents.AsQueryable();
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action == action);
        return query.OrderByDescending(x => x.OccurredAtUtc).Take(500).ToListAsync(cancellationToken);
    }
}

public sealed record IssueVisitorQrRequest(Guid VisitorId, Guid DeviceId, int ExpireMinutes, int MaxOpenTimes);
