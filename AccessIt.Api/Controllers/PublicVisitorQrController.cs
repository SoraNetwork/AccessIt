using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;

namespace AccessIt.Api.Controllers;

[ApiController]
[Route("public/visitor-qr")]
public sealed class PublicVisitorQrController(AccessItDbContext db) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<IActionResult> ViewQr(string token, CancellationToken cancellationToken)
    {
        var person = await db.AccessPeople.SingleOrDefaultAsync(x => x.QrShareToken == token, cancellationToken);
        if (person is null || person.QrRevokedAtUtc is not null || person.EnableEndTimeUtc <= DateTime.UtcNow || string.IsNullOrWhiteSpace(person.QrContent)) return NotFound("访客二维码已失效。");
        var encoded = WebUtility.HtmlEncode(person.QrContent);
        return Content($"<!doctype html><meta charset=utf-8><title>访客二维码</title><main><h2>访客二维码</h2><div id=q></div></main><script src=https://cdn.jsdelivr.net/npm/qrcode@1.5.4/build/qrcode.min.js></script><script>QRCode.toCanvas(document.getElementById('q'), {System.Text.Json.JsonSerializer.Serialize(person.QrContent)}, {{width:300}})</script><style>body{{font-family:system-ui;display:grid;place-items:center;min-height:90vh}}main{{text-align:center}}canvas{{margin:20px}}</style>", "text/html; charset=utf-8");
    }
}
