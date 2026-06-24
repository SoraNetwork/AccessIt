using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.DingTalk;
using AccessIt.Api.Domain;
using AccessIt.Api.Services;

namespace AccessIt.Api.Controllers;

[Authorize(Roles = nameof(ApplicationRole.SuperAdmin))]
[ApiController]
[Route("api/persons")]
public sealed class PersonsController(AccessItDbContext db, IPersonService people, IDingTalkGateway dingTalk, IFaceStorageService faces, IAuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<PersonDto>> List([FromQuery] string? q, CancellationToken cancellationToken)
    {
        var query = db.AccessPeople.Include(x => x.Sources).Include(x => x.FaceAsset).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.Name.Contains(q) || (x.Mobile ?? "").Contains(q));
        return (await query.OrderBy(x => x.Kind).ThenBy(x => x.Name).ToListAsync(cancellationToken)).Select(PersonDto.From).ToList();
    }

    [HttpPost("sync/hikiot")]
    public async Task<SyncResult> SyncHikiot(CancellationToken cancellationToken)
    {
        var result = await people.SyncHikiotAsync(cancellationToken);
        await audit.WriteAsync(User.CurrentUserId(), "person.sync.hikiot", "AccessPerson", "all", result, cancellationToken);
        return result;
    }

    [HttpPost("sync/dingtalk")]
    public async Task<SyncResult> SyncDingTalk(CancellationToken cancellationToken)
    {
        var result = await people.SyncDingTalkAsync(await dingTalk.GetDirectoryAsync(cancellationToken), cancellationToken);
        await audit.WriteAsync(User.CurrentUserId(), "person.sync.dingtalk", "AccessPerson", "all", result, cancellationToken);
        return result;
    }

    [HttpPost("{id:guid}/publish-hikiot")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
    {
        await people.PublishToHikiotAsync(id, cancellationToken);
        await audit.WriteAsync(User.CurrentUserId(), "person.publish.hikiot", "AccessPerson", id, null, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/face")]
    [RequestSizeLimit(3_000_000)]
    public async Task<ActionResult<FaceUploadResponse>> UploadFace(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0) return BadRequest("请选择图片。");
        if (!await db.AccessPeople.AnyAsync(x => x.Id == id, cancellationToken)) return NotFound();
        await using var stream = file.OpenReadStream();
        var face = await faces.StoreAsync(stream, cancellationToken);
        return Ok(new FaceUploadResponse(face.Id, face.PublicToken));
    }

    [HttpPut("{id:guid}/credentials")]
    public async Task<IReadOnlyList<PersonIssueResult>> UpdateCredentials(Guid id, [FromBody] CredentialRequest request, CancellationToken cancellationToken)
    {
        var result = await people.UpdateCredentialsAsync(id, request.CardNo, request.Password, request.FaceAssetId, cancellationToken);
        await audit.WriteAsync(User.CurrentUserId(), "person.credentials.updated", "AccessPerson", id, new { request.CardNo, HasPassword = !string.IsNullOrWhiteSpace(request.Password), request.FaceAssetId }, cancellationToken);
        return result;
    }
}

public sealed record CredentialRequest(string? CardNo, string? Password, Guid? FaceAssetId);
public sealed record FaceUploadResponse(Guid FaceAssetId, string PublicToken);
public sealed record PersonDto(Guid Id, string Name, string? Mobile, AccessPersonKind Kind, string? HikiotPersonNo, IReadOnlyList<string> Sources, string? CardNo, Guid? FaceAssetId, DateTime? EnableBeginTimeUtc, DateTime? EnableEndTimeUtc, string? QrShareToken, string? LastIssueResultJson)
{
    public static PersonDto From(AccessPerson x) => new(x.Id, x.Name, x.Mobile, x.Kind, x.HikiotPersonNo, x.Sources.Select(s => s.SourceType.ToString()).Distinct().ToList(), x.CardNo, x.FaceAssetId, x.EnableBeginTimeUtc, x.EnableEndTimeUtc, x.QrShareToken, x.LastIssueResultJson);
}
