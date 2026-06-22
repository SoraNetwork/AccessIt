using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;
using AccessIt.Api.Services;

namespace AccessIt.Api.Controllers;

[Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)},{nameof(ApplicationRole.Auditor)}")]
[ApiController]
[Route("api/people")]
public sealed class PeopleController(AccessItDbContext db, IPersonService people) : ControllerBase
{
    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpGet("/api/directory-users")]
    public Task<List<DirectoryUserOption>> DirectoryUsers(CancellationToken cancellationToken)
        => db.ApplicationUsers
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.DingTalkUserId)
            .Select(x => new DirectoryUserOption(x.DingTalkUserId, x.Name, x.Mobile))
            .ToListAsync(cancellationToken);

    [HttpGet]
    public Task<List<AccessPerson>> List([FromQuery] PersonKind? kind, [FromQuery] string? keyword, CancellationToken cancellationToken)
    {
        var query = db.AccessPeople.Include(x => x.DeviceGrants).Include(x => x.Cards).Include(x => x.FaceAssets).AsQueryable();
        if (kind is not null) query = query.Where(x => x.Kind == kind);
        if (!string.IsNullOrWhiteSpace(keyword)) query = query.Where(x => x.Name.Contains(keyword) || x.EmployeeNo.Contains(keyword));
        return query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccessPerson>> Get(Guid id, CancellationToken cancellationToken)
    {
        var person = await db.AccessPeople.Include(x => x.DeviceGrants).ThenInclude(x => x.AccessDevice).Include(x => x.Cards).Include(x => x.FaceAssets).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return person is null ? NotFound() : Ok(person);
    }

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPost("employees")]
    public Task<AccessPerson> CreateEmployee([FromBody] CreateEmployeeInput input, CancellationToken cancellationToken) => people.CreateEmployeeAsync(input, User.CurrentUserId(), cancellationToken);

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPost("visitors")]
    public Task<AccessPerson> CreateVisitor([FromBody] CreateVisitorInput input, CancellationToken cancellationToken) => people.CreateVisitorAsync(input, User.CurrentUserId(), cancellationToken);

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPut("{id:guid}/visitor")]
    public Task<AccessPerson> UpdateVisitor(Guid id, [FromBody] UpdateVisitorInput input, CancellationToken cancellationToken) => people.UpdateVisitorAsync(id, input, User.CurrentUserId(), cancellationToken);

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPost("{id:guid}/cards")]
    public Task<AccessCard> AddCard(Guid id, [FromBody] AddCardRequest input, CancellationToken cancellationToken) => people.AddCardAsync(id, input.CardNo, input.IsVirtual, User.CurrentUserId(), cancellationToken);

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPut("{id:guid}/password")]
    public async Task<ActionResult> SetPassword(Guid id, [FromBody] SetPasswordRequest input, CancellationToken cancellationToken)
    {
        await people.SetPasswordAsync(id, input.Password, User.CurrentUserId(), cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpPost("{id:guid}/face")]
    [RequestSizeLimit(3_000_000)]
    public async Task<ActionResult<FaceAsset>> AddFace(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0) return BadRequest("请上传人脸图片。");
        await using var stream = file.OpenReadStream();
        return Ok(await people.AddFaceAsync(id, stream, User.CurrentUserId(), cancellationToken));
    }

    [Authorize(Roles = $"{nameof(ApplicationRole.SuperAdmin)},{nameof(ApplicationRole.AccessAdmin)}")]
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await people.DeleteAsync(id, User.CurrentUserId(), cancellationToken);
        return Accepted();
    }
}

public sealed record AddCardRequest(string CardNo, bool IsVirtual);
public sealed record SetPasswordRequest(string Password);
public sealed record DirectoryUserOption(string DingTalkUserId, string Name, string? Mobile);
