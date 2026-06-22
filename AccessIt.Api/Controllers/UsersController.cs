using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;
using AccessIt.Api.Services;

namespace AccessIt.Api.Controllers;

[Authorize(Roles = nameof(ApplicationRole.SuperAdmin))]
[ApiController]
[Route("api/users")]
public sealed class UsersController(AccessItDbContext db, IAuditService audit) : ControllerBase
{
    [HttpGet]
    public Task<List<ApplicationUser>> List(CancellationToken cancellationToken) => db.ApplicationUsers.OrderBy(x => x.Name).ToListAsync(cancellationToken);

    [HttpPut("{id:guid}/role")]
    public async Task<ActionResult> SetRole(Guid id, [FromBody] SetUserRoleRequest request, CancellationToken cancellationToken)
    {
        if (request.Role == ApplicationRole.None) throw new InvalidOperationException("请使用“停用”而不是授予无权限角色。");
        var user = await db.ApplicationUsers.FindAsync([id], cancellationToken);
        if (user is null) return NotFound();
        user.Role = request.Role;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(User.CurrentUserId(), "user.role.updated", "ApplicationUser", id, new { request.Role }, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/active")]
    public async Task<ActionResult> SetActive(Guid id, [FromBody] SetUserActiveRequest request, CancellationToken cancellationToken)
    {
        var user = await db.ApplicationUsers.FindAsync([id], cancellationToken);
        if (user is null) return NotFound();
        user.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(User.CurrentUserId(), "user.active.updated", "ApplicationUser", id, new { request.IsActive }, cancellationToken);
        return NoContent();
    }
}

public sealed record SetUserRoleRequest(ApplicationRole Role);
public sealed record SetUserActiveRequest(bool IsActive);
