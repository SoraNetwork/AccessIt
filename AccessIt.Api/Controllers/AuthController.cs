using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AccessIt.Api.DingTalk;
using AccessIt.Api.Domain;
using AccessIt.Api.Security;
using AccessIt.Api.Services;

namespace AccessIt.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IDingTalkGateway dingTalk, IIdentityService identity, IJwtTokenService jwt) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("dingtalk/web")]
    public async Task<ActionResult<LoginResponse>> LoginWeb([FromBody] DingTalkCodeRequest request, CancellationToken cancellationToken)
        => await LoginAsync(await dingTalk.GetWebProfileAsync(request.Code, cancellationToken), cancellationToken);

    [AllowAnonymous]
    [HttpPost("dingtalk/in-app")]
    public async Task<ActionResult<LoginResponse>> LoginInApp([FromBody] DingTalkCodeRequest request, CancellationToken cancellationToken)
        => await LoginAsync(await dingTalk.GetInAppProfileAsync(request.Code, cancellationToken), cancellationToken);

    [Authorize]
    [HttpGet("me")]
    public ActionResult<object> Me() => Ok(new { id = User.CurrentUserId(), name = User.Identity?.Name, role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value });

    [Authorize(Roles = nameof(ApplicationRole.SuperAdmin))]
    [HttpPost("directory/sync")]
    public async Task<ActionResult<DirectorySyncResult>> SyncDirectory(CancellationToken cancellationToken)
    {
        var entries = await dingTalk.GetDirectoryAsync(cancellationToken);
        return Ok(await identity.SyncDirectoryAsync(entries, cancellationToken));
    }

    private async Task<ActionResult<LoginResponse>> LoginAsync(DingTalkProfile profile, CancellationToken cancellationToken)
    {
        var user = await identity.SignInAsync(profile, cancellationToken);
        return Ok(new LoginResponse(jwt.Create(user), new LoginUser(user.Id, user.Name, user.Role, user.IsActive)));
    }
}

public sealed record DingTalkCodeRequest(string Code);
public sealed record LoginResponse(string Token, LoginUser User);
public sealed record LoginUser(Guid Id, string Name, ApplicationRole Role, bool IsActive);
