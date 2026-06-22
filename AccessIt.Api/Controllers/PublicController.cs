using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AccessIt.Api.Services;

namespace AccessIt.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("public")]
public sealed class PublicController(IVisitorQrService qr, IFaceStorageService faces) : ControllerBase
{
    [HttpGet("visitor-qr/{token}")]
    public async Task<ActionResult<object>> VisitorQr(string token, CancellationToken cancellationToken)
    {
        var share = await qr.GetPublicAsync(token, cancellationToken);
        return share is null ? NotFound() : Ok(new { qrCode = share.QrCodeContent, expiresAtUtc = share.ExpiresAtUtc });
    }

    [HttpGet("faces/{token}")]
    public async Task<IActionResult> Face(string token, CancellationToken cancellationToken)
    {
        var stream = await faces.OpenAsync(token, cancellationToken);
        return stream is null ? NotFound() : File(stream, "image/jpeg");
    }
}
