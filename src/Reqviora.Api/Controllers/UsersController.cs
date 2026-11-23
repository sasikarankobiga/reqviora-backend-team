using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Application.Features.Authentication.DTOs;
using Reqviora.Application.Features.Authentication.Interfaces;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController(IAuthService authService) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized();

        var result = await authService.GetUserAsync(userId, cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : NotFound();
    }
}
