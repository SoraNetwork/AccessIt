using Microsoft.AspNetCore.Mvc;
using AccessIt.Api.Services;

namespace AccessIt.Api.Controllers;

[ApiController]
[Route("public/faces")]
public sealed class PublicFacesController(IFaceStorageService faces) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken cancellationToken)
    {
        var stream = await faces.OpenAsync(token, cancellationToken);
        return stream is null ? NotFound() : File(stream, "image/jpeg");
    }
}
