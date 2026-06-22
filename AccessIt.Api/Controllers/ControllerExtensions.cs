using System.Security.Claims;

namespace AccessIt.Api.Controllers;

public static class ControllerExtensions
{
    public static string CurrentUserId(this ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
}
