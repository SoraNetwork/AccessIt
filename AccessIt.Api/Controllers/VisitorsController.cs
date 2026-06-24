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
    [HttpPost]
    public async Task<ActionResult<VisitorCreatedResponse>> Create([FromBody] CreateVisitorRequest request, CancellationToken cancellationToken)
    {
        var person = await people.CreateVisitorAsync(request.Name, request.EnableBeginTimeUtc, request.EnableEndTimeUtc, request.CardNo, request.Password, request.FaceAssetId, request.GenerateQr, cancellationToken);
        await audit.WriteAsync(User.CurrentUserId(), "visitor.created", "AccessPerson", person.Id, new { request.Name, request.GenerateQr }, cancellationToken);
        var link = person.QrShareToken is null ? null : hikiot.Value.PublicBaseUrl.TrimEnd('/') + $"/public/visitor-qr/{person.QrShareToken}";
        return Ok(new VisitorCreatedResponse(PersonDto.From(await db.AccessPeople.Include(x => x.Sources).SingleAsync(x => x.Id == person.Id, cancellationToken)), link));
    }
}

public sealed record CreateVisitorRequest(string Name, DateTime EnableBeginTimeUtc, DateTime EnableEndTimeUtc, string? CardNo, string? Password, Guid? FaceAssetId, bool GenerateQr);
public sealed record VisitorCreatedResponse(PersonDto Person, string? SharePath);
