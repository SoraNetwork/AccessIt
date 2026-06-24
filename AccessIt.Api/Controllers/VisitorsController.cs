using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;
using AccessIt.Api.Services;
using AccessIt.Api.Configuration;
using Microsoft.Extensions.Options;

namespace AccessIt.Api.Controllers;

[Authorize(Roles = nameof(ApplicationRole.SuperAdmin))]
[ApiController]
[Route("api/visitors")]
public sealed class VisitorsController(IPersonService people, AccessItDbContext db, IAuditService audit, IOptions<HikiotOptions> hikiot) : ControllerBase
{
    [HttpGet]
    public async Task<VisitorPageResponse> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? q = null, [FromQuery] string? status = null, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var now = DateTime.UtcNow;
        var query = db.AccessPeople.Include(x => x.Sources).Include(x => x.Cards).Where(x => x.Kind == AccessPersonKind.Visitor);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.Name.Contains(q) || (x.Mobile ?? "").Contains(q) || x.DeviceEmployeeNo.Contains(q));
        query = status switch
        {
            "active" => query.Where(x => x.EnableBeginTimeUtc <= now && x.EnableEndTimeUtc >= now),
            "expired" => query.Where(x => x.EnableEndTimeUtc < now),
            "upcoming" => query.Where(x => x.EnableBeginTimeUtc > now),
            _ => query
        };
        var total = await query.CountAsync(cancellationToken);
        var items = (await query.OrderByDescending(x => x.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken)).Select(PersonDto.From).ToList();
        return new VisitorPageResponse(items, total, page, pageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PersonDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var person = await db.AccessPeople.Include(x => x.Sources).Include(x => x.Cards).SingleOrDefaultAsync(x => x.Id == id && x.Kind == AccessPersonKind.Visitor, cancellationToken);
        return person is null ? NotFound() : Ok(PersonDto.From(person));
    }
    [HttpPost]
    public async Task<ActionResult<VisitorCreatedResponse>> Create([FromBody] CreateVisitorRequest request, CancellationToken cancellationToken)
    {
        var person = await people.CreateVisitorAsync(request.Name, request.EnableBeginTimeUtc, request.EnableEndTimeUtc, request.CardNo, request.Password, request.FaceAssetId, request.GenerateQr, cancellationToken);
        await audit.WriteAsync(User.CurrentUserId(), "visitor.created", "AccessPerson", person.Id, new { request.Name, request.GenerateQr }, cancellationToken);
        var link = person.QrShareToken is null ? null : hikiot.Value.PublicBaseUrl.TrimEnd('/') + $"/public/visitor-qr/{person.QrShareToken}";
        return Ok(new VisitorCreatedResponse(PersonDto.From(await db.AccessPeople.Include(x => x.Sources).Include(x => x.Cards).SingleAsync(x => x.Id == person.Id, cancellationToken)), link));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<IReadOnlyList<PersonIssueResult>>> Update(Guid id, [FromBody] UpdateVisitorRequest request, CancellationToken cancellationToken)
    {
        var result = await people.UpdateVisitorAsync(id, request.Name, request.EnableBeginTimeUtc, request.EnableEndTimeUtc, request.CardNo, request.Password, request.FaceAssetId, request.GenerateQr, cancellationToken);
        await audit.WriteAsync(User.CurrentUserId(), "visitor.updated", "AccessPerson", id, new { request.Name, request.GenerateQr }, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/reissue")]
    public async Task<ActionResult<IReadOnlyList<PersonIssueResult>>> Reissue(Guid id, [FromBody] ReissueVisitorRequest request, CancellationToken cancellationToken)
        => Ok(await people.ReissueVisitorAsync(id, request.Password, cancellationToken));

    [HttpPost("{id:guid}/revoke-qr")]
    public async Task<IActionResult> RevokeQr(Guid id, CancellationToken cancellationToken)
    {
        await people.RevokeVisitorQrAsync(id, cancellationToken);
        await audit.WriteAsync(User.CurrentUserId(), "visitor.qr.revoked", "AccessPerson", id, null, cancellationToken);
        return NoContent();
    }
}

public sealed record CreateVisitorRequest(string Name, DateTime EnableBeginTimeUtc, DateTime EnableEndTimeUtc, string? CardNo, string? Password, Guid? FaceAssetId, bool GenerateQr);
public sealed record VisitorCreatedResponse(PersonDto Person, string? SharePath);
public sealed record UpdateVisitorRequest(string Name, DateTime EnableBeginTimeUtc, DateTime EnableEndTimeUtc, string? CardNo, string? Password, Guid? FaceAssetId, bool GenerateQr);
public sealed record ReissueVisitorRequest(string? Password);
public sealed record VisitorPageResponse(IReadOnlyList<PersonDto> Items, int Total, int Page, int PageSize);
