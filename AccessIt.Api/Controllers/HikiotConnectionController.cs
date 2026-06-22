using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AccessIt.Api.Domain;
using AccessIt.Api.Hikiot;

namespace AccessIt.Api.Controllers;

[ApiController]
[Route("api/hikiot/connection")]
public sealed class HikiotConnectionController(IHikiotGateway hikiot) : ControllerBase
{
    [Authorize(Roles = nameof(ApplicationRole.SuperAdmin))]
    [HttpGet]
    public Task<HikiotConnectionStatus> Status(CancellationToken cancellationToken) => hikiot.GetConnectionStatusAsync(cancellationToken);

    [Authorize(Roles = nameof(ApplicationRole.SuperAdmin))]
    [HttpPost("authorize")]
    public async Task<ActionResult<object>> Begin(CancellationToken cancellationToken)
        => Ok(new { authorizationUrl = await hikiot.BeginAuthorizationAsync(User.CurrentUserId(), cancellationToken) });

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string state, [FromQuery] string authCode, CancellationToken cancellationToken)
    {
        await hikiot.CompleteAuthorizationAsync(state, authCode, cancellationToken);
        return Content("HIKIoT 授权成功，您可以关闭此页面。", "text/html; charset=utf-8");
    }
}
